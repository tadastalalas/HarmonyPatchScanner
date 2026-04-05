using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using HarmonyLib;

namespace HarmonyPatchScanner
{
    public static class PatchProcessor
    {
        private static readonly System.Collections.Generic.HashSet<string> _officialAssemblyPrefixes = new()
        {
            "TaleWorlds.",
            "SandBox.",
            "StoryMode.",
            "CustomBattle.",
            "Multiplayer."
        };

        public static void ProcessPatches(
            IEnumerable<Patch> patches,
            string patchType,
            MethodBase originalMethod,
            Dictionary<string, List<PatchInfo>> patchesByMod,
            bool excludeCommonLifecycle = false,
            bool excludeCommunityLibraries = false)
        {
            if (patches == null) return;

            foreach (var patch in patches)
            {
                try
                {
                    if (excludeCommonLifecycle && patch.PatchMethod != null &&
                        FilterHelper.ShouldExcludePatchMethod(patch.PatchMethod.Name, true))
                        continue;

                    var assemblyName = patch.PatchMethod?.DeclaringType?.Assembly?.GetName()?.Name;
                    if (FilterHelper.ShouldExcludeCommunityLibrary(assemblyName, excludeCommunityLibraries))
                        continue;

                    var modName = GetModName(patch);

                    if (!patchesByMod.ContainsKey(modName))
                        patchesByMod[modName] = new List<PatchInfo>();

                    patchesByMod[modName].Add(BuildPatchInfo(patch, patchType, originalMethod));
                }
                catch
                {
                    // Skip problematic patches
                }
            }
        }

        public static void CollectPatchesForMethod(
            IEnumerable<Patch> patches,
            string patchType,
            MethodBase originalMethod,
            List<PatchInfo> collectedPatches,
            bool excludeCommonLifecycle = false,
            bool excludeCommunityLibraries = false)
        {
            if (patches == null) return;

            foreach (var patch in patches)
            {
                try
                {
                    if (excludeCommonLifecycle && patch.PatchMethod != null &&
                        FilterHelper.ShouldExcludePatchMethod(patch.PatchMethod.Name, true))
                        continue;

                    var assemblyName = patch.PatchMethod?.DeclaringType?.Assembly?.GetName()?.Name;
                    if (FilterHelper.ShouldExcludeCommunityLibrary(assemblyName, excludeCommunityLibraries))
                        continue;

                    collectedPatches.Add(BuildPatchInfo(patch, patchType, originalMethod));
                }
                catch
                {
                    // Skip problematic patches
                }
            }
        }

        private static PatchInfo BuildPatchInfo(Patch patch, string patchType, MethodBase originalMethod)
        {
            // A Prefix can short-circuit execution only if its patch method returns bool.
            // (Harmony docs: "a prefix can return a boolean that, if false, skips the original")
            var canShortCircuit = patchType == "Prefix"
                && patch.PatchMethod?.ReturnType == typeof(bool);

            // Resolve the launcher load order using the assembly name of the patch method.
            var rawAssemblyName  = patch.PatchMethod?.DeclaringType?.Assembly?.GetName()?.Name;
            var launcherPosition = ModuleLoadOrderHelper.GetLauncherPosition(rawAssemblyName);

            // Determine if this patch targets official TaleWorlds code.
            var targetTypeName  = originalMethod.DeclaringType?.FullName ?? string.Empty;
            var targetsOfficial = IsOfficialAssembly(targetTypeName);

            return new PatchInfo
            {
                TargetMethod        = $"{originalMethod.DeclaringType?.FullName ?? "Unknown"}.{originalMethod.Name}",
                PatchType           = patchType,
                Priority            = patch.priority,
                Owner               = GetModName(patch),
                HarmonyOwner        = patch.owner ?? string.Empty,
                PatchMethod         = $"{patch.PatchMethod?.DeclaringType?.FullName ?? "Unknown"}.{patch.PatchMethod?.Name ?? "Unknown"}",
                Index               = patch.index,
                Before              = patch.before ?? System.Array.Empty<string>(),
                After               = patch.after  ?? System.Array.Empty<string>(),
                CanShortCircuit     = canShortCircuit,
                LauncherLoadOrder   = launcherPosition,
                TargetsOfficialCode = targetsOfficial
            };
        }

        private static bool IsOfficialAssembly(string typeFullName)
        {
            foreach (var prefix in _officialAssemblyPrefixes)
                if (typeFullName.StartsWith(prefix))
                    return true;
            return false;
        }

        private static string GetModName(Patch patch)
        {
            try
            {
                if (patch.PatchMethod?.DeclaringType?.Assembly != null)
                {
                    var assembly     = patch.PatchMethod.DeclaringType.Assembly;
                    var assemblyName = assembly.GetName().Name ?? patch.owner ?? "Unknown";

                    if (!string.IsNullOrEmpty(assembly.Location))
                    {
                        var location = Path.GetFileName(
                            Path.GetDirectoryName(
                                Path.GetDirectoryName(
                                    Path.GetDirectoryName(assembly.Location))));

                        if (!string.IsNullOrEmpty(location))
                            return $"{location} ({assemblyName})";
                    }

                    return assemblyName;
                }

                return patch.owner ?? "Unknown";
            }
            catch
            {
                return patch.owner ?? "Unknown";
            }
        }
    }
}
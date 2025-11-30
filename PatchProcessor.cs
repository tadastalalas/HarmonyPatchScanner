using System.Collections.Generic;
using System.IO;
using System.Reflection;
using HarmonyLib;

namespace HarmonyPatchScanner
{
    public static class PatchProcessor
    {
        public static void ProcessPatches(IEnumerable<Patch> patches, string patchType, MethodBase originalMethod, Dictionary<string, List<PatchInfo>> patchesByMod, bool excludeCommonLifecycle = false)
        {
            if (patches == null) return;

            foreach (var patch in patches)
            {
                try
                {
                    // Skip patches where the patch method itself is a common lifecycle method
                    if (excludeCommonLifecycle && patch.PatchMethod != null && 
                        FilterHelper.ShouldExcludePatchMethod(patch.PatchMethod.Name, true))
                    {
                        continue;
                    }

                    var modName = GetModName(patch);
                    
                    if (!patchesByMod.ContainsKey(modName))
                    {
                        patchesByMod[modName] = new List<PatchInfo>();
                    }

                    patchesByMod[modName].Add(new PatchInfo
                    {
                        TargetMethod = $"{originalMethod.DeclaringType?.FullName ?? "Unknown"}.{originalMethod.Name}",
                        PatchType = patchType,
                        Priority = patch.priority,
                        Owner = patch.owner,
                        PatchMethod = $"{patch.PatchMethod?.DeclaringType?.FullName ?? "Unknown"}.{patch.PatchMethod?.Name ?? "Unknown"}",
                        Index = patch.index
                    });
                }
                catch
                {
                    // Skip problematic patches
                }
            }
        }

        public static void CollectPatchesForMethod(IEnumerable<Patch> patches, string patchType, MethodBase originalMethod, List<PatchInfo> collectedPatches, bool excludeCommonLifecycle = false)
        {
            if (patches == null) return;

            foreach (var patch in patches)
            {
                try
                {
                    // Skip patches where the patch method itself is a common lifecycle method
                    if (excludeCommonLifecycle && patch.PatchMethod != null && 
                        FilterHelper.ShouldExcludePatchMethod(patch.PatchMethod.Name, true))
                    {
                        continue;
                    }

                    var modName = GetModName(patch);

                    collectedPatches.Add(new PatchInfo
                    {
                        TargetMethod = $"{originalMethod.DeclaringType?.FullName ?? "Unknown"}.{originalMethod.Name}",
                        PatchType = patchType,
                        Priority = patch.priority,
                        Owner = modName,
                        PatchMethod = $"{patch.PatchMethod?.DeclaringType?.FullName ?? "Unknown"}.{patch.PatchMethod?.Name ?? "Unknown"}",
                        Index = patch.index
                    });
                }
                catch
                {
                    // Skip problematic patches
                }
            }
        }

        private static string GetModName(Patch patch)
        {
            try
            {
                // Try to get mod name from patch method's assembly
                if (patch.PatchMethod?.DeclaringType?.Assembly != null)
                {
                    var assembly = patch.PatchMethod.DeclaringType.Assembly;
                    var assemblyName = assembly.GetName().Name;
                    
                    // Try to get mod folder name from assembly location
                    if (!string.IsNullOrEmpty(assembly.Location))
                    {
                        var location = Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(assembly.Location))));
                        if (!string.IsNullOrEmpty(location))
                        {
                            return $"{location} ({assemblyName})";
                        }
                    }
                    
                    return assemblyName;
                }

                // Fallback to owner ID
                return patch.owner ?? "Unknown";
            }
            catch
            {
                return patch.owner ?? "Unknown";
            }
        }
    }
}
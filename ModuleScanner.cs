using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using HarmonyLib;
using TaleWorlds.Library;

namespace HarmonyPatchScanner
{
    public static class ModuleScanner
    {
        public static void ScanSelectedModule()
        {
            try
            {
                var settings = ScannerSettings.Instance;
                var moduleId = settings?.GetSelectedModuleId();

                if (string.IsNullOrEmpty(moduleId))
                {
                    InformationManager.DisplayMessage(new InformationMessage(
                        "Please select a module from the dropdown first.", Colors.Yellow));
                    return;
                }

                var assemblies = ModuleLoadOrderHelper.GetAssembliesForModule(moduleId);
                if (assemblies == null || assemblies.Count == 0)
                {
                    InformationManager.DisplayMessage(new InformationMessage(
                        $"No assemblies found for module '{moduleId}'.", Colors.Yellow));
                    return;
                }

                var moduleName       = ModuleLoadOrderHelper.GetModuleName(moduleId);
                var launcherPos      = ModuleLoadOrderHelper.GetLauncherPosition(assemblies.First());
                var results          = new StringBuilder();
                var excludeLifecycle = settings!.ExcludeCommonLifecycleMethods;
                var excludeCommunity = settings.ExcludeCommunityLibraries;

                // ── Header ──────────────────────────────────────────────────────────
                results.AppendLine("════════════════════════════════════════════════════");
                results.AppendLine("    Harmony Patch Scanner — Module Report           ");
                results.AppendLine("════════════════════════════════════════════════════");
                results.AppendLine($"Scan Time   : {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                results.AppendLine($"Module      : {moduleName}");
                results.AppendLine($"Module Id   : {moduleId}");
                results.AppendLine($"Launcher Pos: {PatchDisplayHelper.FormatLauncherOrder(launcherPos)}");
                results.AppendLine($"Assemblies  : {string.Join(", ", assemblies)}");
                results.AppendLine();

                if (excludeLifecycle)
                {
                    results.AppendLine("Note: Common lifecycle method patches are excluded from this scan.");
                    results.AppendLine();
                }

                if (excludeCommunity)
                {
                    results.AppendLine("Note: Community library patches (Harmony, ButterLib, UIExtenderEx, MCM, BetterExceptionWindow) are excluded from this scan.");
                    results.AppendLine();
                }

                // ── Collect patches ─────────────────────────────────────────────────
                var allPatchedMethods     = Harmony.GetAllPatchedMethods().ToList();
                var modulePatchesByTarget = new Dictionary<string, List<PatchInfo>>();
                var conflictsByTarget     = new Dictionary<string, List<PatchInfo>>();

                foreach (var originalMethod in allPatchedMethods)
                {
                    try
                    {
                        if (FilterHelper.ShouldExcludeMethod(originalMethod.Name, excludeLifecycle))
                            continue;

                        var patchInfo = Harmony.GetPatchInfo(originalMethod);
                        if (patchInfo == null) continue;

                        var methodKey = $"{originalMethod.DeclaringType?.FullName ?? "Unknown"}.{originalMethod.Name}";

                        var allPatchesOnMethod    = new List<PatchInfo>();
                        var modulePatchesOnMethod = new List<PatchInfo>();

                        CollectAndClassify(patchInfo.Prefixes,    "Prefix",     originalMethod, assemblies, allPatchesOnMethod, modulePatchesOnMethod, excludeLifecycle, excludeCommunity);
                        CollectAndClassify(patchInfo.Postfixes,   "Postfix",    originalMethod, assemblies, allPatchesOnMethod, modulePatchesOnMethod, excludeLifecycle, excludeCommunity);
                        CollectAndClassify(patchInfo.Transpilers, "Transpiler", originalMethod, assemblies, allPatchesOnMethod, modulePatchesOnMethod, excludeLifecycle, excludeCommunity);
                        CollectAndClassify(patchInfo.Finalizers,  "Finalizer",  originalMethod, assemblies, allPatchesOnMethod, modulePatchesOnMethod, excludeLifecycle, excludeCommunity);

                        if (modulePatchesOnMethod.Count > 0)
                        {
                            modulePatchesByTarget[methodKey] = modulePatchesOnMethod;

                            // If other mods also patch this method, record all patches for the conflict section
                            var otherModPatches = allPatchesOnMethod.Where(p => !IsFromModule(p, assemblies)).ToList();
                            if (otherModPatches.Count > 0)
                                conflictsByTarget[methodKey] = allPatchesOnMethod;
                        }
                    }
                    catch (Exception ex)
                    {
                        results.AppendLine($"[ERROR] Could not process {originalMethod.DeclaringType?.FullName}.{originalMethod.Name}: {ex.Message}");
                    }
                }

                // ── Summary stats ───────────────────────────────────────────────────
                var totalPatches       = modulePatchesByTarget.Sum(x => x.Value.Count);
                var totalTranspilers   = modulePatchesByTarget.Sum(x => x.Value.Count(p => p.PatchType == "Transpiler"));
                var totalPrefixes      = modulePatchesByTarget.Sum(x => x.Value.Count(p => p.PatchType == "Prefix"));
                var totalPostfixes     = modulePatchesByTarget.Sum(x => x.Value.Count(p => p.PatchType == "Postfix"));
                var totalFinalizers    = modulePatchesByTarget.Sum(x => x.Value.Count(p => p.PatchType == "Finalizer"));
                var totalShortCircuits = modulePatchesByTarget.Sum(x => x.Value.Count(p => p.CanShortCircuit));
                var totalOfficial      = modulePatchesByTarget.Sum(x => x.Value.Count(p => p.TargetsOfficialCode));

                results.AppendLine($"Patched Methods             : {modulePatchesByTarget.Count}");
                results.AppendLine($"Total Patches               : {totalPatches}");
                results.AppendLine($"  — Prefixes                : {totalPrefixes}");
                results.AppendLine($"  — Postfixes               : {totalPostfixes}");
                results.AppendLine($"  — Transpilers             : {totalTranspilers}  (highest risk patch type)");
                results.AppendLine($"  — Finalizers              : {totalFinalizers}");
                results.AppendLine($"  — Prefixes (short-circuit): {totalShortCircuits}  (can skip original method)");
                results.AppendLine($"  — Target official code    : {totalOfficial}");
                results.AppendLine($"Conflicts with other mods   : {conflictsByTarget.Count}");
                results.AppendLine();

                if (totalPatches == 0)
                {
                    results.AppendLine("No Harmony patches found for this module.");
                    results.AppendLine();
                }
                else
                {
                    // ── All patches by target method ────────────────────────────────
                    results.AppendLine("════════════════════════════════════════════════════");
                    results.AppendLine($"  All Patches by {moduleName}");
                    results.AppendLine("════════════════════════════════════════════════════");
                    results.AppendLine();

                    foreach (var targetGroup in modulePatchesByTarget.OrderBy(x => x.Key))
                    {
                        var patches     = targetGroup.Value;
                        var hasConflict = conflictsByTarget.ContainsKey(targetGroup.Key);
                        var officialTag = patches.First().TargetsOfficialCode ? "  [official code]" : string.Empty;
                        var conflictTag = hasConflict ? "  ⚠ CONFLICT" : string.Empty;

                        results.AppendLine($"  Target : {PatchDisplayHelper.FormatMethodName(targetGroup.Key, verbose: false)}{officialTag}{conflictTag}");

                        var hasShortCircuit = patches.Any(p => p.CanShortCircuit);
                        if (hasShortCircuit && patches.Count(p => p.PatchType == "Prefix") > 1)
                        {
                            results.AppendLine("    ⚠ Short-circuit chain: a prefix here can return false, which");
                            results.AppendLine("      skips the original AND all lower-priority prefixes.");
                        }

                        foreach (var patch in patches)
                        {
                            var shortCircuitNote = patch.CanShortCircuit ? " [can skip original]" : string.Empty;
                            var beforeStr        = patch.Before.Length > 0 ? string.Join(", ", patch.Before) : "none";
                            var afterStr         = patch.After.Length  > 0 ? string.Join(", ", patch.After)  : "none";

                            results.AppendLine($"    Type          : {patch.PatchType}{shortCircuitNote}");
                            results.AppendLine($"    Priority      : {PatchDisplayHelper.FormatPriority(patch.Priority)}");
                            results.AppendLine($"    Harmony Index : {PatchDisplayHelper.FormatIndex(patch.Index)}");
                            results.AppendLine($"    Launcher Pos  : {PatchDisplayHelper.FormatLauncherOrder(patch.LauncherLoadOrder)}");
                            results.AppendLine($"    Patch Method  : {PatchDisplayHelper.FormatMethodName(patch.PatchMethod, verbose: false)}");
                            results.AppendLine($"    Harmony ID    : {patch.HarmonyOwner}");
                            results.AppendLine($"    Before        : {beforeStr}");
                            results.AppendLine($"    After         : {afterStr}");
                            results.AppendLine();
                        }
                    }

                    // ── Patches grouped by type ─────────────────────────────────────
                    results.AppendLine("════════════════════════════════════════════════════");
                    results.AppendLine("  Patches by Type");
                    results.AppendLine("════════════════════════════════════════════════════");
                    results.AppendLine();

                    var allModulePatches = modulePatchesByTarget.SelectMany(x => x.Value).ToList();

                    foreach (var typeGroup in allModulePatches.GroupBy(p => p.PatchType).OrderBy(g => GetTypeOrder(g.Key)))
                    {
                        var patches = typeGroup.ToList();
                        results.AppendLine($"  ── {typeGroup.Key} ({patches.Count}) ──────────────────────────────");
                        results.AppendLine();

                        foreach (var patch in patches.OrderBy(p => p.TargetMethod))
                        {
                            var shortCircuitNote = patch.CanShortCircuit ? " [can skip original]" : string.Empty;
                            var officialTag      = patch.TargetsOfficialCode ? "  [official code]" : string.Empty;

                            results.AppendLine($"    Target        : {PatchDisplayHelper.FormatMethodName(patch.TargetMethod, verbose: false)}{officialTag}");
                            results.AppendLine($"    Patch Method  : {PatchDisplayHelper.FormatMethodName(patch.PatchMethod, verbose: false)}{shortCircuitNote}");
                            results.AppendLine($"    Priority      : {PatchDisplayHelper.FormatPriority(patch.Priority)}");
                            results.AppendLine($"    Harmony Index : {PatchDisplayHelper.FormatIndex(patch.Index)}");
                            results.AppendLine($"    Harmony ID    : {patch.HarmonyOwner}");
                            results.AppendLine();
                        }
                    }

                    // ── Conflict section ────────────────────────────────────────────
                    if (conflictsByTarget.Count > 0)
                    {
                        results.AppendLine("════════════════════════════════════════════════════");
                        results.AppendLine("  Conflicts — Methods Also Patched by Other Mods");
                        results.AppendLine("════════════════════════════════════════════════════");
                        results.AppendLine();
                        results.AppendLine($"  {conflictsByTarget.Count} method(s) are patched by both {moduleName}");
                        results.AppendLine("  and at least one other mod. Review these for potential issues.");
                        results.AppendLine();

                        var sortedConflicts = conflictsByTarget
                            .Select(kvp => new
                            {
                                MethodKey     = kvp.Key,
                                AllPatches    = kvp.Value,
                                ModulePatches = kvp.Value.Where(p => IsFromModule(p, assemblies)).ToList(),
                                OtherPatches  = kvp.Value.Where(p => !IsFromModule(p, assemblies)).ToList(),
                                Risk          = GetConflictRisk(kvp.Value, assemblies)
                            })
                            .OrderByDescending(c => c.Risk)
                            .ThenBy(c => c.MethodKey)
                            .ToList();

                        // ── Conflict table of contents ──────────────────────────────
                        results.AppendLine("  Table of Contents:");
                        results.AppendLine();
                        foreach (var c in sortedConflicts)
                        {
                            var riskLabel = c.Risk == 2 ? "[HIGH]" : c.Risk == 1 ? "[MEDIUM]" : "[LOW]";
                            var otherMods = string.Join(", ", c.OtherPatches.Select(p => p.Owner).Distinct());
                            results.AppendLine($"    {riskLabel,-8} {PatchDisplayHelper.FormatMethodName(c.MethodKey, verbose: false)}  — also patched by: {otherMods}");
                        }
                        results.AppendLine();

                        // ── Conflict details ────────────────────────────────────────
                        foreach (var conflict in sortedConflicts)
                        {
                            var otherMods       = conflict.OtherPatches.Select(p => p.Owner).Distinct().ToList();
                            var hasTranspilers  = conflict.AllPatches.Count(p => p.PatchType == "Transpiler") > 1;
                            var hasShortCircuit = conflict.AllPatches.Any(p => p.CanShortCircuit && p.PatchType == "Prefix")
                                              && conflict.AllPatches.Count(p => p.PatchType == "Prefix") > 1;
                            var targetsOfficial = conflict.ModulePatches.Any(p => p.TargetsOfficialCode);
                            var riskLabel       = conflict.Risk == 2 ? "HIGH" : conflict.Risk == 1 ? "MEDIUM" : "LOW";

                            results.AppendLine("────────────────────────────────────────────────────");
                            results.AppendLine($"  Target     : {PatchDisplayHelper.FormatMethodName(conflict.MethodKey, verbose: false)}{(targetsOfficial ? "  [official TaleWorlds code]" : string.Empty)}");
                            results.AppendLine($"  Risk       : {riskLabel}");
                            results.AppendLine($"  This mod   : {conflict.ModulePatches.Count} patch(es)");
                            results.AppendLine($"  Other mods : {conflict.OtherPatches.Count} patch(es) from {otherMods.Count} mod(s): {string.Join(", ", otherMods)}");
                            results.AppendLine();

                            if (hasTranspilers)
                            {
                                results.AppendLine("  ⚠ HIGH RISK: Multiple transpilers on this method.");
                                results.AppendLine("    Each transpiler sees IL already modified by the previous one.");
                                results.AppendLine();
                            }

                            if (hasShortCircuit)
                            {
                                results.AppendLine("  ⚠ SHORT-CIRCUIT RISK: A prefix returns bool — if it returns false,");
                                results.AppendLine("    the original method AND all lower-priority prefixes are skipped.");
                                results.AppendLine();
                            }

                            foreach (var typeGroup in conflict.AllPatches.GroupBy(p => p.PatchType).OrderBy(g => GetTypeOrder(g.Key)))
                            {
                                var ordered = GetPatchesInExecutionOrder(typeGroup.Key, typeGroup.ToList());
                                results.AppendLine($"  {typeGroup.Key} Patches — Execution Order:");

                                int step = 1;
                                foreach (var patch in ordered)
                                {
                                    var isThisMod = IsFromModule(patch, assemblies);
                                    var marker    = isThisMod ? " ◄ THIS MOD" : string.Empty;
                                    var scNote    = patch.CanShortCircuit ? "  ← can return false to skip original + later prefixes" : string.Empty;
                                    var beforeStr = patch.Before.Length > 0 ? string.Join(", ", patch.Before) : "none";
                                    var afterStr  = patch.After.Length  > 0 ? string.Join(", ", patch.After)  : "none";

                                    results.AppendLine($"    [{step}] Mod          : {patch.Owner}{marker}");
                                    results.AppendLine($"        Method       : {PatchDisplayHelper.FormatMethodName(patch.PatchMethod, verbose: false)}");
                                    results.AppendLine($"        Harmony ID   : {patch.HarmonyOwner}");
                                    results.AppendLine($"        Priority     : {PatchDisplayHelper.FormatPriority(patch.Priority)}");
                                    results.AppendLine($"        Harmony Idx  : {PatchDisplayHelper.FormatIndex(patch.Index)}");
                                    results.AppendLine($"        Launcher Pos : {PatchDisplayHelper.FormatLauncherOrder(patch.LauncherLoadOrder)}{scNote}");
                                    results.AppendLine($"        Before       : {beforeStr}");
                                    results.AppendLine($"        After        : {afterStr}");
                                    step++;
                                }
                                results.AppendLine();
                            }
                        }
                    }
                    else
                    {
                        results.AppendLine("════════════════════════════════════════════════════");
                        results.AppendLine("  No Conflicts Detected");
                        results.AppendLine("════════════════════════════════════════════════════");
                        results.AppendLine();
                        results.AppendLine($"  No other mod patches the same methods as {moduleName}.");
                        results.AppendLine();
                    }

                    // ── Summary ──────────────────────────────────────────────────────
                    results.AppendLine("════════════════════════════════════════════════════");
                    results.AppendLine($"  Module Summary — {moduleName}");
                    results.AppendLine("════════════════════════════════════════════════════");
                    results.AppendLine();
                    results.AppendLine($"  Module Id              : {moduleId}");
                    results.AppendLine($"  Launcher Position      : {PatchDisplayHelper.FormatLauncherOrder(launcherPos)}");
                    results.AppendLine($"  Assemblies             : {string.Join(", ", assemblies)}");
                    results.AppendLine($"  Total Patched Methods  : {modulePatchesByTarget.Count}");
                    results.AppendLine($"  Total Patches          : {totalPatches}");
                    results.AppendLine($"    Prefixes             : {totalPrefixes}");
                    results.AppendLine($"    Postfixes            : {totalPostfixes}");
                    results.AppendLine($"    Transpilers          : {totalTranspilers}");
                    results.AppendLine($"    Finalizers           : {totalFinalizers}");
                    results.AppendLine($"  Short-circuit Prefixes : {totalShortCircuits}");
                    results.AppendLine($"  Targets Official Code  : {totalOfficial}");
                    results.AppendLine($"  Conflicts              : {conflictsByTarget.Count} method(s) shared with other mods");
                    results.AppendLine();
                }

                // ── Save ────────────────────────────────────────────────────────────
                var safeFileName = SanitizeFileName(moduleName);
                var outputPath   = FileHelper.GetOutputPath($"ModuleScan_{safeFileName}.txt");
                File.WriteAllText(outputPath, results.ToString());

                var messageColor = conflictsByTarget.Count > 0 ? Colors.Yellow : Colors.Green;
                InformationManager.DisplayMessage(new InformationMessage(
                    $"Module scan complete! {moduleName}: {totalPatches} patches, {conflictsByTarget.Count} conflicts. Saved to {outputPath}",
                    messageColor));
            }
            catch (Exception ex)
            {
                InformationManager.DisplayMessage(new InformationMessage($"Module scan failed: {ex.Message}", Colors.Red));
            }
        }

        // ── Helpers ─────────────────────────────────────────────────────────────────

        private static void CollectAndClassify(
            IEnumerable<Patch> patches,
            string patchType,
            System.Reflection.MethodBase originalMethod,
            HashSet<string> moduleAssemblies,
            List<PatchInfo> allPatchesOnMethod,
            List<PatchInfo> modulePatchesOnMethod,
            bool excludeLifecycle,
            bool excludeCommunity)
        {
            if (patches == null) return;

            foreach (var patch in patches)
            {
                try
                {
                    if (excludeLifecycle && patch.PatchMethod != null &&
                        FilterHelper.ShouldExcludePatchMethod(patch.PatchMethod.Name, true))
                        continue;

                    var assemblyName = patch.PatchMethod?.DeclaringType?.Assembly?.GetName()?.Name;
                    var isFromModule = !string.IsNullOrEmpty(assemblyName) && moduleAssemblies.Contains(assemblyName);

                    // For non-module patches, apply community library exclusion
                    if (!isFromModule && FilterHelper.ShouldExcludeCommunityLibrary(assemblyName, excludeCommunity))
                        continue;

                    var info = BuildPatchInfo(patch, patchType, originalMethod);
                    allPatchesOnMethod.Add(info);

                    if (isFromModule)
                        modulePatchesOnMethod.Add(info);
                }
                catch
                {
                    // Skip problematic patches
                }
            }
        }

        private static PatchInfo BuildPatchInfo(Patch patch, string patchType, System.Reflection.MethodBase originalMethod)
        {
            var canShortCircuit = patchType == "Prefix"
                && patch.PatchMethod?.ReturnType == typeof(bool);

            var rawAssemblyName  = patch.PatchMethod?.DeclaringType?.Assembly?.GetName()?.Name;
            var launcherPosition = ModuleLoadOrderHelper.GetLauncherPosition(rawAssemblyName);

            var targetTypeName  = originalMethod.DeclaringType?.FullName ?? string.Empty;
            var targetsOfficial = targetTypeName.StartsWith("TaleWorlds.")
                               || targetTypeName.StartsWith("SandBox.")
                               || targetTypeName.StartsWith("StoryMode.")
                               || targetTypeName.StartsWith("CustomBattle.")
                               || targetTypeName.StartsWith("Multiplayer.");

            return new PatchInfo
            {
                TargetMethod        = $"{originalMethod.DeclaringType?.FullName ?? "Unknown"}.{originalMethod.Name}",
                PatchType           = patchType,
                Priority            = patch.priority,
                Owner               = GetModName(patch),
                HarmonyOwner        = patch.owner ?? string.Empty,
                PatchMethod         = $"{patch.PatchMethod?.DeclaringType?.FullName ?? "Unknown"}.{patch.PatchMethod?.Name ?? "Unknown"}",
                Index               = patch.index,
                Before              = patch.before ?? Array.Empty<string>(),
                After               = patch.after  ?? Array.Empty<string>(),
                CanShortCircuit     = canShortCircuit,
                LauncherLoadOrder   = launcherPosition,
                TargetsOfficialCode = targetsOfficial
            };
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

        private static bool IsFromModule(PatchInfo patch, HashSet<string> moduleAssemblies)
        {
            foreach (var asm in moduleAssemblies)
            {
                if (patch.Owner.IndexOf(asm, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
                if (patch.PatchMethod.IndexOf(asm, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
            return false;
        }

        private static int GetConflictRisk(List<PatchInfo> allPatches, HashSet<string> moduleAssemblies)
        {
            if (allPatches.Count(p => p.PatchType == "Transpiler") > 1)
                return 2;

            var prefixes       = allPatches.Where(p => p.PatchType == "Prefix").ToList();
            var modulePrefixes = prefixes.Where(p => IsFromModule(p, moduleAssemblies)).ToList();
            var otherPrefixes  = prefixes.Where(p => !IsFromModule(p, moduleAssemblies)).ToList();

            if (modulePrefixes.Count > 0 && otherPrefixes.Count > 0)
            {
                if (modulePrefixes.Any(mp => otherPrefixes.Any(op => op.Priority == mp.Priority)))
                    return 1;
            }

            return 0;
        }

        private static List<PatchInfo> GetPatchesInExecutionOrder(string patchType, List<PatchInfo> patches)
        {
            switch (patchType)
            {
                case "Prefix":
                case "Transpiler":
                    return patches.OrderByDescending(p => p.Priority).ThenBy(p => p.Index).ToList();
                case "Postfix":
                case "Finalizer":
                    return patches.OrderBy(p => p.Priority).ThenByDescending(p => p.Index).ToList();
                default:
                    return patches.OrderByDescending(p => p.Priority).ThenBy(p => p.Index).ToList();
            }
        }

        private static int GetTypeOrder(string patchType)
        {
            switch (patchType)
            {
                case "Transpiler": return 0;
                case "Prefix":     return 1;
                case "Finalizer":  return 2;
                case "Postfix":    return 3;
                default:           return 4;
            }
        }

        private static string SanitizeFileName(string name)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var sb      = new StringBuilder(name.Length);
            foreach (var c in name)
                sb.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);
            return sb.ToString();
        }
    }
}
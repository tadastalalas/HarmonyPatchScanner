using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using HarmonyLib;
using TaleWorlds.Library;

namespace HarmonyPatchScanner
{
    public static class ConflictScanner
    {
        public static void FindDuplicatePatches()
        {
            try
            {
                var settings = ScannerSettings.Instance;
                var results  = new StringBuilder();

                // ── Header ────────────────────────────────────────────────────────────
                results.AppendLine("════════════════════════════════════════════════════");
                results.AppendLine("    Harmony Patch Scanner — Conflict Report         ");
                results.AppendLine("════════════════════════════════════════════════════");
                results.AppendLine($"Scan Time : {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                results.AppendLine();

                // ── Launcher load order ───────────────────────────────────────────────
                ModuleLoadOrderHelper.AppendLoadOrderHeader(results);

                results.AppendLine("This report lists methods patched by more than one mod.");
                results.AppendLine("Patches may conflict depending on their type and priority.");
                results.AppendLine();

                if (settings.ExcludeCommonLifecycleMethods)
                {
                    results.AppendLine("Note: Common lifecycle method patches are excluded from this scan.");
                    results.AppendLine();
                }

                if (settings.ExcludeCommunityLibraries)
                {
                    results.AppendLine("Note: Community library patches (Harmony, ButterLib, UIExtenderEx, MCM, BetterExceptionWindow) are excluded from this scan.");
                    results.AppendLine();
                }

                // ── Collect ────────────────────────────────────────────────────────────
                var allPatchedMethods     = Harmony.GetAllPatchedMethods().ToList();
                var patchesByTargetMethod = new Dictionary<string, List<PatchInfo>>();

                foreach (var originalMethod in allPatchedMethods)
                {
                    try
                    {
                        if (FilterHelper.ShouldExcludeMethod(originalMethod.Name, settings.ExcludeCommonLifecycleMethods))
                            continue;

                        var patchInfo = Harmony.GetPatchInfo(originalMethod);
                        if (patchInfo == null) continue;

                        var methodKey = $"{originalMethod.DeclaringType?.FullName ?? "Unknown"}.{originalMethod.Name}";
                        patchesByTargetMethod[methodKey] = new List<PatchInfo>();

                        PatchProcessor.CollectPatchesForMethod(patchInfo.Prefixes,    "Prefix",     originalMethod, patchesByTargetMethod[methodKey], settings.ExcludeCommonLifecycleMethods, settings.ExcludeCommunityLibraries);
                        PatchProcessor.CollectPatchesForMethod(patchInfo.Postfixes,   "Postfix",    originalMethod, patchesByTargetMethod[methodKey], settings.ExcludeCommonLifecycleMethods, settings.ExcludeCommunityLibraries);
                        PatchProcessor.CollectPatchesForMethod(patchInfo.Transpilers, "Transpiler", originalMethod, patchesByTargetMethod[methodKey], settings.ExcludeCommonLifecycleMethods, settings.ExcludeCommunityLibraries);
                        PatchProcessor.CollectPatchesForMethod(patchInfo.Finalizers,  "Finalizer",  originalMethod, patchesByTargetMethod[methodKey], settings.ExcludeCommonLifecycleMethods, settings.ExcludeCommunityLibraries);
                    }
                    catch (Exception ex)
                    {
                        results.AppendLine($"[ERROR] Could not process {originalMethod.DeclaringType?.FullName}.{originalMethod.Name}: {ex.Message}");
                    }
                }

                // ── Build conflict list ────────────────────────────────────────────────
                var conflictingMethods = patchesByTargetMethod
                    .Where(kvp => kvp.Value.Count > 1 && kvp.Value.Select(p => p.Owner).Distinct().Count() > 1)
                    .Select(kvp => new ConflictInfo(kvp.Key, kvp.Value))
                    .OrderByDescending(c => c.RiskLevel)
                    .ThenByDescending(c => c.Patches.Count)
                    .ToList();

                var sameModMultiPatches = patchesByTargetMethod
                    .Where(kvp =>
                    {
                        var owners = kvp.Value.Select(p => p.Owner).ToList();
                        return owners.Distinct().Count() == 1 && owners.Count > 1;
                    })
                    .Select(kvp => new ConflictInfo(kvp.Key, kvp.Value))
                    .OrderByDescending(c => c.Patches.Count)
                    .ToList();

                var highRisk              = conflictingMethods.Count(c => c.RiskLevel == RiskLevel.High);
                var mediumRisk            = conflictingMethods.Count(c => c.RiskLevel == RiskLevel.Medium);
                var lowRisk               = conflictingMethods.Count(c => c.RiskLevel == RiskLevel.Low);
                var officialConflicts     = conflictingMethods.Count(c => c.Patches.Any(p => p.TargetsOfficialCode));
                var shortCircuitConflicts = conflictingMethods.Count(c =>
                    c.Patches.Count(p => p.PatchType == "Prefix") > 1 &&
                    c.Patches.Any(p => p.CanShortCircuit));

                results.AppendLine($"Total Methods Patched          : {patchesByTargetMethod.Count}");
                results.AppendLine($"Cross-Mod Conflicts            : {conflictingMethods.Count}  ({highRisk} High / {mediumRisk} Medium / {lowRisk} Low)");
                results.AppendLine($"  — Targeting official code    : {officialConflicts}");
                results.AppendLine($"  — Short-circuit prefix risk  : {shortCircuitConflicts}  (one prefix can silence another mod's prefix)");
                results.AppendLine($"Same-Mod Multi-Patch (suspect) : {sameModMultiPatches.Count}");
                results.AppendLine();

                if (conflictingMethods.Count == 0 && sameModMultiPatches.Count == 0)
                {
                    results.AppendLine("No conflicts detected. All methods are patched by a single mod.");
                }
                else
                {
                    // ── Table of contents ────────────────────────────────────────────────
                    results.AppendLine("════════════════════════════════════════════════════");
                    results.AppendLine("  Table of Contents                                 ");
                    results.AppendLine("════════════════════════════════════════════════════");
                    results.AppendLine();

                    if (conflictingMethods.Count > 0)
                    {
                        AppendTocSection(results, conflictingMethods, RiskLevel.High,   "HIGH RISK");
                        AppendTocSection(results, conflictingMethods, RiskLevel.Medium, "MEDIUM RISK");
                        AppendTocSection(results, conflictingMethods, RiskLevel.Low,    "LOW RISK");
                    }

                    if (sameModMultiPatches.Count > 0)
                    {
                        results.AppendLine("  [SAME-MOD MULTI-PATCH]");
                        foreach (var c in sameModMultiPatches)
                            results.AppendLine($"    · {PatchDisplayHelper.FormatMethodName(c.MethodKey, verbose: false)}  ({c.Patches.Count} patches)");
                        results.AppendLine();
                    }

                    if (conflictingMethods.Count > 0)
                    {
                        OutputConflictsByRiskLevel(results, conflictingMethods, RiskLevel.High,   "HIGH RISK CONFLICTS");
                        OutputConflictsByRiskLevel(results, conflictingMethods, RiskLevel.Medium, "MEDIUM RISK CONFLICTS");
                        OutputConflictsByRiskLevel(results, conflictingMethods, RiskLevel.Low,    "LOW RISK CONFLICTS");
                    }

                    if (sameModMultiPatches.Count > 0)
                        OutputSameModSection(results, sameModMultiPatches);

                    // ── Summary ───────────────────────────────────────────────────────────
                    results.AppendLine("════════════════════════════════════════════════════");
                    results.AppendLine("  Conflict Summary                                  ");
                    results.AppendLine("════════════════════════════════════════════════════");
                    results.AppendLine();
                    results.AppendLine($"  Cross-Mod Conflicts  : {conflictingMethods.Count}");
                    results.AppendLine($"    High   : {highRisk}");
                    results.AppendLine($"    Medium : {mediumRisk}");
                    results.AppendLine($"    Low    : {lowRisk}");
                    results.AppendLine($"  Same-Mod Multi-Patch : {sameModMultiPatches.Count}");
                    results.AppendLine();
                    results.AppendLine("  Execution Order Tiebreak Chain (Harmony + Bannerlord)");
                    results.AppendLine("  ────────────────────────────────────────────────────");
                    results.AppendLine("  1. Priority        — higher wins (Prefix/Transpiler), lower wins (Postfix/Finalizer)");
                    results.AppendLine("  2. before/after    — [HarmonyBefore] / [HarmonyAfter] explicit ordering hints");
                    results.AppendLine("  3. Harmony Index   — assigned when each mod calls harmony.Patch() / PatchAll()");
                    results.AppendLine("  4. Launcher Pos    — the order mods were sorted in the Bannerlord launcher");
                    results.AppendLine("                       (determines which mod's DLL is loaded first by TaleWorlds,");
                    results.AppendLine("                        and therefore which mod registers its patches first → lower index)");
                    results.AppendLine("  5. INDETERMINATE   — if all of the above are identical, order is undefined");
                    results.AppendLine();
                    results.AppendLine("  Patch Type Risk (highest to lowest)");
                    results.AppendLine("  ───────────────────────────────────");
                    results.AppendLine("  Transpiler  — rewrites IL; each one sees the already-modified code");
                    results.AppendLine("  Prefix      — runs before original; bool return can skip original + later prefixes");
                    results.AppendLine("  Finalizer   — always runs, even on exception; wraps the original");
                    results.AppendLine("  Postfix     — runs after original; generally the safest");
                    results.AppendLine();
                }

                // ── Save ──────────────────────────────────────────────────────────────
                var outputPath = FileHelper.GetOutputPath("DuplicateHarmonyPatches.txt");
                File.WriteAllText(outputPath, results.ToString());

                var messageColor = highRisk > 0 ? Colors.Red
                    : conflictingMethods.Count > 0 ? Colors.Yellow
                    : Colors.Green;

                InformationManager.DisplayMessage(new InformationMessage(
                    $"Conflict scan complete! {conflictingMethods.Count} conflicts ({highRisk} high risk), {shortCircuitConflicts} short-circuit risks. Saved to {outputPath}",
                    messageColor));
            }
            catch (Exception ex)
            {
                InformationManager.DisplayMessage(new InformationMessage($"Conflict scan failed: {ex.Message}", Colors.Red));
            }
        }

        // ── ToC helper ────────────────────────────────────────────────────────────────
        private static void AppendTocSection(StringBuilder results, List<ConflictInfo> conflicts, RiskLevel level, string label)
        {
            var subset = conflicts.Where(c => c.RiskLevel == level).ToList();
            if (subset.Count == 0) return;

            results.AppendLine($"  [{label}]  ({subset.Count})");
            foreach (var c in subset)
            {
                var officialTag = c.Patches.Any(p => p.TargetsOfficialCode) ? " [official]"       : string.Empty;
                var scTag       = c.Patches.Any(p => p.CanShortCircuit)     ? " [short-circuit]"  : string.Empty;
                results.AppendLine($"    · {PatchDisplayHelper.FormatMethodName(c.MethodKey, verbose: false)}  ({c.Patches.Count} patches from {c.Patches.Select(p => p.Owner).Distinct().Count()} mods){officialTag}{scTag}");
            }
            results.AppendLine();
        }

        // ── Cross-mod conflict output ─────────────────────────────────────────────────
        private static void OutputConflictsByRiskLevel(StringBuilder results, List<ConflictInfo> conflicts, RiskLevel riskLevel, string header)
        {
            var subset = conflicts.Where(c => c.RiskLevel == riskLevel).ToList();
            if (subset.Count == 0) return;

            results.AppendLine();
            results.AppendLine("════════════════════════════════════════════════════");
            results.AppendLine($"  {header}  ({subset.Count})");
            results.AppendLine("════════════════════════════════════════════════════");
            results.AppendLine();

            foreach (var conflict in subset)
            {
                var uniqueMods      = conflict.Patches.Select(p => p.Owner).Distinct().Count();
                var targetsOfficial = conflict.Patches.Any(p => p.TargetsOfficialCode);
                var hasShortCircuit = conflict.Patches.Any(p => p.CanShortCircuit && p.PatchType == "Prefix");

                results.AppendLine($"  Target  : {PatchDisplayHelper.FormatMethodName(conflict.MethodKey, verbose: false)}{(targetsOfficial ? "  [official TaleWorlds code]" : string.Empty)}");
                results.AppendLine($"  Patches : {conflict.Patches.Count} from {uniqueMods} mod(s)");

                if (conflict.HasMultipleTranspilers)
                {
                    results.AppendLine("  ⚠ HIGH RISK: Multiple transpilers detected.");
                    results.AppendLine("    Each transpiler sees IL already modified by the previous one.");
                    results.AppendLine("    Execution order shown below — first in list sees the original IL.");
                }
                else if (conflict.HasMultiplePrefixesWithSamePriority)
                {
                    results.AppendLine("  ⚠ MEDIUM RISK: Multiple prefixes share the same priority.");
                    results.AppendLine("    Tie-broken by before/after hints → Harmony index → Launcher position.");
                    if (conflict.HasIndeterminateOrder)
                        results.AppendLine("  ⚠ INDETERMINATE: Patches share priority, index, launcher position, and no before/after hints.");
                }

                if (hasShortCircuit)
                {
                    results.AppendLine("  ⚠ SHORT-CIRCUIT RISK: At least one prefix returns bool.");
                    results.AppendLine("    If it returns false, the original method AND all lower-priority prefixes");
                    results.AppendLine("    from OTHER mods are silently skipped — those mods may malfunction.");
                }

                results.AppendLine();

                foreach (var typeGroup in conflict.Patches.GroupBy(p => p.PatchType).OrderBy(g => g.Key))
                {
                    var ordered = GetPatchesInExecutionOrder(typeGroup.Key, typeGroup.ToList());
                    results.AppendLine($"  {typeGroup.Key} Patches — Execution Order:");

                    int step = 1;
                    foreach (var patch in ordered)
                    {
                        var scNote    = patch.CanShortCircuit ? "  ← can return false to skip original + later prefixes" : string.Empty;
                        var beforeStr = patch.Before.Length > 0 ? string.Join(", ", patch.Before) : "none";
                        var afterStr  = patch.After.Length  > 0 ? string.Join(", ", patch.After)  : "none";

                        results.AppendLine($"    [{step}] Mod          : {patch.Owner}");
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

                results.AppendLine("────────────────────────────────────────────────────");
                results.AppendLine();
            }
        }

        // ── Same-mod multi-patch output ───────────────────────────────────────────────
        private static void OutputSameModSection(StringBuilder results, List<ConflictInfo> sameModConflicts)
        {
            results.AppendLine();
            results.AppendLine("════════════════════════════════════════════════════");
            results.AppendLine($"  SAME-MOD MULTI-PATCH SUSPECTS  ({sameModConflicts.Count})");
            results.AppendLine("════════════════════════════════════════════════════");
            results.AppendLine("  These methods are patched more than once by the same mod.");
            results.AppendLine("  This may be intentional but could also indicate a bug.");
            results.AppendLine();

            foreach (var conflict in sameModConflicts)
            {
                results.AppendLine($"  Target : {PatchDisplayHelper.FormatMethodName(conflict.MethodKey, verbose: false)}");
                results.AppendLine($"  Mod    : {conflict.Patches[0].Owner}");
                results.AppendLine($"  Count  : {conflict.Patches.Count} patches");
                results.AppendLine();

                foreach (var typeGroup in conflict.Patches.GroupBy(p => p.PatchType).OrderBy(g => g.Key))
                {
                    var ordered = GetPatchesInExecutionOrder(typeGroup.Key, typeGroup.ToList());
                    results.AppendLine($"  {typeGroup.Key} Patches — Execution Order:");

                    int step = 1;
                    foreach (var patch in ordered)
                    {
                        var scNote    = patch.CanShortCircuit ? "  ← can return false to skip original + later prefixes" : string.Empty;
                        var beforeStr = patch.Before.Length > 0 ? string.Join(", ", patch.Before) : "none";
                        var afterStr  = patch.After.Length  > 0 ? string.Join(", ", patch.After)  : "none";

                        results.AppendLine($"    [{step}] Method       : {PatchDisplayHelper.FormatMethodName(patch.PatchMethod, verbose: false)}");
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

                results.AppendLine("────────────────────────────────────────────────────");
                results.AppendLine();
            }
        }

        // ── Execution order ───────────────────────────────────────────────────────────
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

        // ── ConflictInfo ──────────────────────────────────────────────────────────────
        private class ConflictInfo
        {
            public string MethodKey { get; }
            public List<PatchInfo> Patches { get; }
            public bool HasMultipleTranspilers              { get; }
            public bool HasMultiplePrefixes                 { get; }
            public bool HasMultiplePrefixesWithSamePriority { get; }
            public bool HasIndeterminateOrder               { get; }
            public RiskLevel RiskLevel                      { get; }

            public ConflictInfo(string methodKey, List<PatchInfo> patches)
            {
                MethodKey = methodKey;
                Patches   = patches;

                HasMultipleTranspilers = patches.Count(p => p.PatchType == "Transpiler") > 1;
                HasMultiplePrefixes    = patches.Count(p => p.PatchType == "Prefix") > 1;
                HasMultiplePrefixesWithSamePriority = HasMultiplePrefixes
                    && patches.Where(p => p.PatchType == "Prefix")
                              .GroupBy(p => p.Priority)
                              .Any(g => g.Count() > 1);

                HasIndeterminateOrder = patches
                    .GroupBy(p => new { p.PatchType, p.Priority, p.Index, p.LauncherLoadOrder })
                    .Any(g => g.Count() > 1
                        && g.All(p => p.Before.Length == 0 && p.After.Length == 0));

                if (HasMultipleTranspilers)
                    RiskLevel = RiskLevel.High;
                else if (HasMultiplePrefixesWithSamePriority)
                    RiskLevel = RiskLevel.Medium;
                else
                    RiskLevel = RiskLevel.Low;
            }
        }

        private enum RiskLevel { Low = 0, Medium = 1, High = 2 }
    }
}
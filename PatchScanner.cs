using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using HarmonyLib;
using TaleWorlds.Library;

namespace HarmonyPatchScanner
{
    public static class PatchScanner
    {
        public static void ScanAndLog()
        {
            try
            {
                var settings = ScannerSettings.Instance;
                var results  = new StringBuilder();

                // ── Header ──────────────────────────────────────────────────────────
                results.AppendLine("════════════════════════════════════════════════════");
                results.AppendLine("       Harmony Patch Scanner — Full Patch List      ");
                results.AppendLine("════════════════════════════════════════════════════");
                results.AppendLine($"Scan Time : {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                results.AppendLine();

                // ── Launcher load order ──────────────────────────────────────────────
                ModuleLoadOrderHelper.AppendLoadOrderHeader(results);

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

                // ── Collect ──────────────────────────────────────────────────────────
                var allPatchedMethods = Harmony.GetAllPatchedMethods().ToList();
                var patchesByMod      = new Dictionary<string, List<PatchInfo>>();

                foreach (var originalMethod in allPatchedMethods)
                {
                    try
                    {
                        if (FilterHelper.ShouldExcludeMethod(originalMethod.Name, settings.ExcludeCommonLifecycleMethods))
                            continue;

                        var patchInfo = Harmony.GetPatchInfo(originalMethod);
                        if (patchInfo == null) continue;

                        PatchProcessor.ProcessPatches(patchInfo.Prefixes,    "Prefix",     originalMethod, patchesByMod, settings.ExcludeCommonLifecycleMethods, settings.ExcludeCommunityLibraries);
                        PatchProcessor.ProcessPatches(patchInfo.Postfixes,   "Postfix",    originalMethod, patchesByMod, settings.ExcludeCommonLifecycleMethods, settings.ExcludeCommunityLibraries);
                        PatchProcessor.ProcessPatches(patchInfo.Transpilers, "Transpiler", originalMethod, patchesByMod, settings.ExcludeCommonLifecycleMethods, settings.ExcludeCommunityLibraries);
                        PatchProcessor.ProcessPatches(patchInfo.Finalizers,  "Finalizer",  originalMethod, patchesByMod, settings.ExcludeCommonLifecycleMethods, settings.ExcludeCommunityLibraries);
                    }
                    catch (Exception ex)
                    {
                        results.AppendLine($"[ERROR] Could not process {originalMethod.DeclaringType?.FullName}.{originalMethod.Name}: {ex.Message}");
                    }
                }

                var totalPatches         = patchesByMod.Sum(x => x.Value.Count);
                var totalTranspilers     = patchesByMod.Sum(x => x.Value.Count(p => p.PatchType == "Transpiler"));
                var totalShortCircuits   = patchesByMod.Sum(x => x.Value.Count(p => p.CanShortCircuit));
                var totalOfficialTargets = patchesByMod.Sum(x => x.Value.Count(p => p.TargetsOfficialCode));
                var danglingHints        = GetDanglingBeforeAfterHints(patchesByMod);

                results.AppendLine($"Total Patched Methods       : {allPatchedMethods.Count}");
                results.AppendLine($"Total Mods with Patches     : {patchesByMod.Count}");
                results.AppendLine($"Total Patches               : {totalPatches}");
                results.AppendLine($"  — Transpilers             : {totalTranspilers}  (highest risk patch type)");
                results.AppendLine($"  — Prefixes (short-circuit): {totalShortCircuits}  (can skip original method)");
                results.AppendLine($"  — Target official code    : {totalOfficialTargets}");
                if (danglingHints.Count > 0)
                    results.AppendLine($"  — Dangling before/after   : {danglingHints.Count}  (reference a mod that is not loaded — see below)");
                results.AppendLine();

                // ── Dangling before/after warnings ───────────────────────────────────
                if (danglingHints.Count > 0)
                {
                    results.AppendLine("════════════════════════════════════════════════════");
                    results.AppendLine("  ⚠ DANGLING before/after HINTS");
                    results.AppendLine("════════════════════════════════════════════════════");
                    results.AppendLine("  These patches declare [HarmonyBefore] or [HarmonyAfter] targeting");
                    results.AppendLine("  a Harmony owner ID that is not present in the loaded patch list.");
                    results.AppendLine("  The hint is silently ignored by Harmony — ordering may be wrong.");
                    results.AppendLine();
                    foreach (var (patchMethod, hintType, referencedId) in danglingHints)
                    {
                        results.AppendLine($"  Patch  : {PatchDisplayHelper.FormatMethodName(patchMethod, verbose: false)}");
                        results.AppendLine($"  [{hintType}] references : {referencedId}  (not loaded)");
                        results.AppendLine();
                    }
                }

                // ── Per-mod detail ────────────────────────────────────────────────────
                foreach (var modGroup in patchesByMod.OrderBy(x => x.Key))
                {
                    var modTranspilers   = modGroup.Value.Count(p => p.PatchType == "Transpiler");
                    var modShortCircuits = modGroup.Value.Count(p => p.CanShortCircuit);
                    var modOfficial      = modGroup.Value.Count(p => p.TargetsOfficialCode);

                    results.AppendLine("────────────────────────────────────────────────────");
                    results.AppendLine($"  Mod          : {modGroup.Key}");
                    results.AppendLine($"  Patches      : {modGroup.Value.Count}  (transpilers: {modTranspilers}  |  short-circuit prefixes: {modShortCircuits}  |  targets official code: {modOfficial})");
                    results.AppendLine("────────────────────────────────────────────────────");
                    results.AppendLine();

                    // Group by target method so related patches are shown together
                    foreach (var targetGroup in modGroup.Value.GroupBy(p => p.TargetMethod).OrderBy(g => g.Key))
                    {
                        var allPatches       = targetGroup.ToList();
                        var hasShortCircuit  = allPatches.Any(p => p.CanShortCircuit);
                        var officialTag      = allPatches.First().TargetsOfficialCode ? "  [official code]" : string.Empty;

                        results.AppendLine($"  Target : {PatchDisplayHelper.FormatMethodName(targetGroup.Key, verbose: false)}{officialTag}");

                        // Short-circuit chain warning — if any prefix can skip the original,
                        // later prefixes (on this method, from any mod) may never execute.
                        if (hasShortCircuit && allPatches.Count(p => p.PatchType == "Prefix") > 1)
                        {
                            results.AppendLine("    ⚠ Short-circuit chain: a prefix here can return false, which");
                            results.AppendLine("      skips the original AND all lower-priority prefixes.");
                        }

                        foreach (var patch in allPatches)
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
                }

                // ── Mod ranking ───────────────────────────────────────────────────────
                results.AppendLine("════════════════════════════════════════════════════");
                results.AppendLine("  Mod Ranking by Patch Count (most patches first)  ");
                results.AppendLine("════════════════════════════════════════════════════");
                results.AppendLine();
                int rank = 1;
                foreach (var modGroup in patchesByMod.OrderByDescending(x => x.Value.Count))
                {
                    var launcherPos = modGroup.Value.FirstOrDefault()?.LauncherLoadOrder;
                    var posStr      = launcherPos.HasValue ? $"  launcher #{launcherPos.Value}" : string.Empty;
                    results.AppendLine($"  #{rank,-3} {modGroup.Key}  ({modGroup.Value.Count} patches){posStr}");
                    rank++;
                }
                results.AppendLine();

                // ── Save ──────────────────────────────────────────────────────────────
                var outputPath = FileHelper.GetOutputPath("AllHarmonyPatches.txt");
                File.WriteAllText(outputPath, results.ToString());

                InformationManager.DisplayMessage(new InformationMessage(
                    $"Scan complete! {patchesByMod.Count} mods / {totalPatches} patches. Transpilers: {totalTranspilers}. Results saved to {outputPath}"));
            }
            catch (Exception ex)
            {
                InformationManager.DisplayMessage(new InformationMessage($"Scan failed: {ex.Message}", Colors.Red));
            }
        }

        // ── Dangling hint detection ───────────────────────────────────────────────────
        private static List<(string PatchMethod, string HintType, string ReferencedId)> GetDanglingBeforeAfterHints(
            Dictionary<string, List<PatchInfo>> patchesByMod)
        {
            var allHarmonyOwners = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var group in patchesByMod.Values)
                foreach (var p in group)
                    if (!string.IsNullOrEmpty(p.HarmonyOwner))
                        allHarmonyOwners.Add(p.HarmonyOwner);

            var dangling = new List<(string, string, string)>();
            foreach (var group in patchesByMod.Values)
            {
                foreach (var p in group)
                {
                    foreach (var id in p.Before)
                        if (!allHarmonyOwners.Contains(id))
                            dangling.Add((p.PatchMethod, "HarmonyBefore", id));

                    foreach (var id in p.After)
                        if (!allHarmonyOwners.Contains(id))
                            dangling.Add((p.PatchMethod, "HarmonyAfter", id));
                }
            }
            return dangling;
        }
    }
}
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
                var results = new StringBuilder();
                results.AppendLine("=== Duplicate/Conflicting Harmony Patches ===");
                results.AppendLine($"Scan Time: {DateTime.Now}");
                results.AppendLine();
                results.AppendLine("This report shows methods that have multiple patches from different mods.");
                results.AppendLine("These patches may conflict depending on their type and priority.");
                results.AppendLine();

                if (settings.ExcludeCommonLifecycleMethods)
                {
                    results.AppendLine("Note: Common lifecycle method patches are excluded from this scan.");
                    results.AppendLine();
                }

                // Get all patches from Harmony's internal registry
                var allPatchedMethods = Harmony.GetAllPatchedMethods().ToList();

                // Group all patches by target method
                var patchesByTargetMethod = new Dictionary<string, List<PatchInfo>>();

                foreach (var originalMethod in allPatchedMethods)
                {
                    try
                    {
                        // Skip common lifecycle target methods if the filter is enabled
                        if (FilterHelper.ShouldExcludeMethod(originalMethod.Name, settings.ExcludeCommonLifecycleMethods))
                        {
                            continue;
                        }

                        var patchInfo = Harmony.GetPatchInfo(originalMethod);
                        if (patchInfo == null) continue;

                        var methodKey = $"{originalMethod.DeclaringType?.FullName ?? "Unknown"}.{originalMethod.Name}";
                        patchesByTargetMethod[methodKey] = new List<PatchInfo>();

                        // Collect all patches for this method (now filtering patch methods too)
                        PatchProcessor.CollectPatchesForMethod(patchInfo.Prefixes, "Prefix", originalMethod, patchesByTargetMethod[methodKey], settings.ExcludeCommonLifecycleMethods);
                        PatchProcessor.CollectPatchesForMethod(patchInfo.Postfixes, "Postfix", originalMethod, patchesByTargetMethod[methodKey], settings.ExcludeCommonLifecycleMethods);
                        PatchProcessor.CollectPatchesForMethod(patchInfo.Transpilers, "Transpiler", originalMethod, patchesByTargetMethod[methodKey], settings.ExcludeCommonLifecycleMethods);
                        PatchProcessor.CollectPatchesForMethod(patchInfo.Finalizers, "Finalizer", originalMethod, patchesByTargetMethod[methodKey], settings.ExcludeCommonLifecycleMethods);
                    }
                    catch (Exception ex)
                    {
                        results.AppendLine($"Error processing method: {ex.Message}");
                    }
                }

                // Find methods with multiple patches from different mods (AFTER filtering)
                var conflictingMethods = patchesByTargetMethod
                    .Where(kvp => kvp.Value.Count > 1 && kvp.Value.Select(p => p.Owner).Distinct().Count() > 1)
                    .Select(kvp => new ConflictInfo
                    {
                        MethodKey = kvp.Key,
                        Patches = kvp.Value,
                        RiskLevel = DetermineRiskLevel(kvp.Value)
                    })
                    .OrderByDescending(c => c.RiskLevel)
                    .ThenByDescending(c => c.Patches.Count)
                    .ToList();

                var highRiskConflicts = conflictingMethods.Count(c => c.RiskLevel == RiskLevel.High);
                var mediumRiskConflicts = conflictingMethods.Count(c => c.RiskLevel == RiskLevel.Medium);
                var lowRiskConflicts = conflictingMethods.Count(c => c.RiskLevel == RiskLevel.Low);

                results.AppendLine($"Total Methods Patched: {patchesByTargetMethod.Count}");
                results.AppendLine($"Methods with Multiple Patches: {conflictingMethods.Count}");
                results.AppendLine();

                if (conflictingMethods.Count == 0)
                {
                    results.AppendLine("No conflicting patches found! All methods have patches from a single mod.");
                }
                else
                {
                    // Output conflicts grouped by risk level
                    OutputConflictsByRiskLevel(results, conflictingMethods, RiskLevel.High, "HIGH RISK CONFLICTS");
                    OutputConflictsByRiskLevel(results, conflictingMethods, RiskLevel.Medium, "MEDIUM RISK CONFLICTS");
                    OutputConflictsByRiskLevel(results, conflictingMethods, RiskLevel.Low, "LOW RISK CONFLICTS");

                    // Summary
                    results.AppendLine();
                    results.AppendLine("========================================");
                    results.AppendLine("=== CONFLICT SUMMARY ===");
                    results.AppendLine("========================================");
                    results.AppendLine($"Total Conflicts: {conflictingMethods.Count}");
                    results.AppendLine($"  High Risk:   {highRiskConflicts}");
                    results.AppendLine($"  Medium Risk: {mediumRiskConflicts}");
                    results.AppendLine($"  Low Risk:    {lowRiskConflicts}");
                    results.AppendLine();
                    results.AppendLine("Risk Level Definitions:");
                    results.AppendLine("  HIGH   - Multiple transpilers on same method (IL code conflicts)");
                    results.AppendLine("  MEDIUM - Multiple prefixes with same priority (execution order issues)");
                    results.AppendLine("  LOW    - Multiple patches with proper priority ordering");
                    results.AppendLine();
                    results.AppendLine("Execution Order Notes:");
                    results.AppendLine("  Prefixes:     Higher priority executes first, then by index");
                    results.AppendLine("  Postfixes:    Lower priority executes first, then by reverse index");
                    results.AppendLine("  Transpilers:  Higher priority executes first, then by index");
                    results.AppendLine("  Finalizers:   Lower priority executes first, then by reverse index");
                }

                // Save results
                var outputPath = FileHelper.GetOutputPath("DuplicateHarmonyPatches.txt");
                File.WriteAllText(outputPath, results.ToString());

                var messageColor = highRiskConflicts > 0 ? Colors.Red : (conflictingMethods.Count > 0 ? Colors.Yellow : Colors.Green);
                InformationManager.DisplayMessage(new InformationMessage(
                    $"Conflict scan complete! Found {conflictingMethods.Count} conflicts ({highRiskConflicts} high risk). Results saved to {outputPath}", 
                    messageColor));
            }
            catch (Exception ex)
            {
                InformationManager.DisplayMessage(new InformationMessage($"Conflict scan failed: {ex.Message}", Colors.Red));
            }
        }

        private static RiskLevel DetermineRiskLevel(List<PatchInfo> patches)
        {
            var hasMultipleTranspilers = patches.Count(p => p.PatchType == "Transpiler") > 1;
            var hasMultiplePrefixes = patches.Count(p => p.PatchType == "Prefix") > 1;
            var hasSamePriority = patches.GroupBy(p => new { p.PatchType, p.Priority }).Any(g => g.Count() > 1);

            if (hasMultipleTranspilers)
                return RiskLevel.High;
            
            if (hasMultiplePrefixes && hasSamePriority)
                return RiskLevel.Medium;
            
            return RiskLevel.Low;
        }

        private static void OutputConflictsByRiskLevel(StringBuilder results, List<ConflictInfo> conflicts, RiskLevel riskLevel, string header)
        {
            var conflictsAtLevel = conflicts.Where(c => c.RiskLevel == riskLevel).ToList();
            
            if (conflictsAtLevel.Count == 0)
                return;

            results.AppendLine();
            results.AppendLine("========================================");
            results.AppendLine($"=== {header} ({conflictsAtLevel.Count}) ===");
            results.AppendLine("========================================");
            results.AppendLine();

            foreach (var conflict in conflictsAtLevel)
            {
                var patches = conflict.Patches;
                var uniqueMods = patches.Select(p => p.Owner).Distinct().Count();

                results.AppendLine($"Target Method: {conflict.MethodKey}");
                results.AppendLine($"  Patches: {patches.Count} from {uniqueMods} different mod(s)");
                results.AppendLine();

                // Group by patch type and show in EXECUTION ORDER
                var patchesByType = patches.GroupBy(p => p.PatchType);
                foreach (var typeGroup in patchesByType.OrderBy(g => g.Key))
                {
                    var orderedPatches = GetPatchesInExecutionOrder(typeGroup.Key, typeGroup.ToList());
                    
                    results.AppendLine($"  {typeGroup.Key} Patches ({typeGroup.Count()}) - Execution Order:");
                    
                    int executionOrder = 1;
                    foreach (var patch in orderedPatches)
                    {
                        results.AppendLine($"    [{executionOrder}] Mod: {patch.Owner}");
                        results.AppendLine($"        Method: {patch.PatchMethod}");
                        results.AppendLine($"        Priority: {patch.Priority} | Index: {patch.Index}");
                        executionOrder++;
                    }
                    results.AppendLine();
                }

                // Add conflict analysis
                var hasMultipleTranspilers = patches.Count(p => p.PatchType == "Transpiler") > 1;
                var hasMultiplePrefixes = patches.Count(p => p.PatchType == "Prefix") > 1;
                var hasSamePriority = patches.GroupBy(p => new { p.PatchType, p.Priority }).Any(g => g.Count() > 1);

                if (hasMultipleTranspilers)
                {
                    results.AppendLine("  ⚠️ WARNING: Multiple transpilers detected! This is HIGH RISK for conflicts.");
                    results.AppendLine("     Transpilers modify IL code and multiple transpilers may interfere with each other.");
                    results.AppendLine("     Execution order shown above - first transpiler sees original IL, subsequent ones see modified IL.");
                }
                else if (hasMultiplePrefixes)
                {
                    results.AppendLine("  ⚠️ CAUTION: Multiple prefix patches. Execution order shown above.");
                    if (hasSamePriority)
                    {
                        results.AppendLine("     Some patches have the same priority - execution order determined by index.");
                    }
                    results.AppendLine("     If any prefix returns false, execution stops and remaining prefixes won't run.");
                }

                results.AppendLine();
                results.AppendLine("---");
                results.AppendLine();
            }
        }

        private static List<PatchInfo> GetPatchesInExecutionOrder(string patchType, List<PatchInfo> patches)
        {
            // Harmony execution order rules:
            // Prefixes: Higher priority first, then by index (ascending)
            // Postfixes: Lower priority first, then by reverse index (descending)
            // Transpilers: Higher priority first, then by index (ascending)
            // Finalizers: Lower priority first, then by reverse index (descending)

            switch (patchType)
            {
                case "Prefix":
                case "Transpiler":
                    // Higher priority executes first, then by index
                    return patches.OrderByDescending(p => p.Priority)
                                  .ThenBy(p => p.Index)
                                  .ToList();

                case "Postfix":
                case "Finalizer":
                    // Lower priority executes first, then by reverse index
                    return patches.OrderBy(p => p.Priority)
                                  .ThenByDescending(p => p.Index)
                                  .ToList();

                default:
                    return patches.OrderByDescending(p => p.Priority)
                                  .ThenBy(p => p.Index)
                                  .ToList();
            }
        }

        private class ConflictInfo
        {
            public string MethodKey { get; set; } = string.Empty;
            public List<PatchInfo> Patches { get; set; } = new List<PatchInfo>();
            public RiskLevel RiskLevel { get; set; }
        }

        private enum RiskLevel
        {
            Low = 0,
            Medium = 1,
            High = 2
        }
    }
}
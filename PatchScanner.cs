using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
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
                var results = new StringBuilder();
                results.AppendLine("=== Harmony Patch Scanner Results ===");
                results.AppendLine($"Scan Time: {DateTime.Now}");
                results.AppendLine();

                if (settings.ExcludeCommonLifecycleMethods)
                {
                    results.AppendLine("Note: Common lifecycle method patches are excluded from this scan.");
                    results.AppendLine();
                }

                // Get all patches from Harmony's internal registry
                var allPatchedMethods = Harmony.GetAllPatchedMethods().ToList();
                results.AppendLine($"Total Patched Methods: {allPatchedMethods.Count}");
                results.AppendLine();

                // Group patches by mod/assembly
                var patchesByMod = new Dictionary<string, List<PatchInfo>>();

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

                        // Process all patch types (now filtering patch methods too)
                        PatchProcessor.ProcessPatches(patchInfo.Prefixes, "Prefix", originalMethod, patchesByMod, settings.ExcludeCommonLifecycleMethods);
                        PatchProcessor.ProcessPatches(patchInfo.Postfixes, "Postfix", originalMethod, patchesByMod, settings.ExcludeCommonLifecycleMethods);
                        PatchProcessor.ProcessPatches(patchInfo.Transpilers, "Transpiler", originalMethod, patchesByMod, settings.ExcludeCommonLifecycleMethods);
                        PatchProcessor.ProcessPatches(patchInfo.Finalizers, "Finalizer", originalMethod, patchesByMod, settings.ExcludeCommonLifecycleMethods);
                    }
                    catch (Exception ex)
                    {
                        results.AppendLine($"Error processing method {originalMethod.DeclaringType?.FullName}.{originalMethod.Name}: {ex.Message}");
                    }
                }

                // Sort and output results
                foreach (var modGroup in patchesByMod.OrderBy(x => x.Key))
                {
                    results.AppendLine($"=== {modGroup.Key} ===");
                    results.AppendLine($"Total Patches: {modGroup.Value.Count}");
                    results.AppendLine();

                    foreach (var patch in modGroup.Value.OrderBy(p => p.TargetMethod))
                    {
                        results.AppendLine($"  Target: {patch.TargetMethod}");
                        results.AppendLine($"    Type: {patch.PatchType} | Priority: {patch.Priority} | Owner: {patch.Owner}");
                        results.AppendLine($"    Patch: {patch.PatchMethod}");
                        results.AppendLine();
                    }
                    results.AppendLine();
                }

                // Save results
                var outputPath = FileHelper.GetOutputPath("AllHarmonyPatches.txt");
                File.WriteAllText(outputPath, results.ToString());
                
                InformationManager.DisplayMessage(new InformationMessage($"Scan complete! Found {patchesByMod.Count} mods with {patchesByMod.Sum(x => x.Value.Count)} patches. Results saved to {outputPath}"));
            }
            catch (Exception ex)
            {
                InformationManager.DisplayMessage(new InformationMessage($"Scan failed: {ex.Message}", Colors.Red));
            }
        }
    }
}
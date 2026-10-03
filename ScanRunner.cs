using System;
using System.IO;
using HarmonyPatchScanner.Analysis;
using HarmonyPatchScanner.Model;
using HarmonyPatchScanner.Reporting;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace HarmonyPatchScanner
{
    /// <summary>Shared plumbing for the three MCM buttons: options, scan, save, in-game message.</summary>
    internal static class ScanRunner
    {
        internal static ScanOptions Options()
        {
            ScannerSettings? s = ScannerSettings.Instance;
            return new ScanOptions
            {
                ExcludeLifecycle          = s?.ExcludeCommonLifecycleMethods ?? true,
                ExcludeCommunityLibraries = s?.ExcludeCommunityLibraries ?? true,
                AnalyzePrefixReturns      = s?.AnalyzePrefixReturns ?? true,
                DeepTranspilerAnalysis    = s?.DeepTranspilerAnalysis ?? false
            };
        }

        internal static ScanModel Scan()
        {
            ScanModel model = PatchCollector.Scan(Options());
            (model.Context, model.ContextFolder) = ScanContext();
            return model;
        }

        // Patches applied on campaign start or per mission only exist from that state on, so each context
        // gets its own folder under logs/ and the user can keep all three scans side by side.
        private static (string Label, string Folder) ScanContext()
        {
            bool campaign = Campaign.Current != null;
            bool mission  = Mission.Current != null;
            if (mission)  return (campaign ? "mission (campaign running)" : "mission (no campaign)", "mission");
            if (campaign) return ("campaign", "campaign");
            return ("main menu", "mainmenu");
        }

        internal static string Save(ScanModel model, string fileName, string text)
        {
            string path = FileHelper.GetOutputPath(model.ContextFolder, fileName);
            File.WriteAllText(path, text);
            return path;
        }

        internal static void Message(string text, Color color) =>
            InformationManager.DisplayMessage(new InformationMessage(text, color));

        internal static void Run(string what, Action action)
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                Message($"{what} failed: {ex.GetType().Name}: {ex.Message}", Colors.Red);
            }
        }
    }
}

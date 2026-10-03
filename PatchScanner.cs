using HarmonyPatchScanner.Model;
using HarmonyPatchScanner.Reporting;
using TaleWorlds.Library;

namespace HarmonyPatchScanner
{
    /// <summary>MCM button "Scan Now" → AllHarmonyPatches.txt.</summary>
    internal static class PatchScanner
    {
        internal static void ScanAndLog() => ScanRunner.Run("Scan", () =>
        {
            ScanModel model = ScanRunner.Scan();
            string text     = FullReport.Build(model, out FullSummary s);
            string path     = ScanRunner.Save(model, "AllHarmonyPatches.txt", text);

            ScanRunner.Message(
                $"Scan complete: {s.Mods} mods, {s.Patches} patches, {s.Shared} shared methods ({s.High} high risk). Saved to {path}",
                s.High > 0 ? Colors.Yellow : Colors.Green);
        });
    }
}

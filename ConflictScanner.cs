using HarmonyPatchScanner.Model;
using HarmonyPatchScanner.Reporting;
using TaleWorlds.Library;

namespace HarmonyPatchScanner
{
    /// <summary>MCM button "Find Conflicts" → DuplicateHarmonyPatches.txt.</summary>
    internal static class ConflictScanner
    {
        internal static void FindDuplicatePatches() => ScanRunner.Run("Conflict scan", () =>
        {
            ScanModel model = ScanRunner.Scan();
            string text     = ConflictReport.Build(model, out ConflictSummary s);
            string path     = ScanRunner.Save(model, "DuplicateHarmonyPatches.txt", text);

            Color color = s.High > 0 ? Colors.Red : s.Medium > 0 ? Colors.Yellow : Colors.Green;
            ScanRunner.Message(
                $"Conflict scan complete: {s.Total} shared methods — {s.High} high, {s.Medium} medium, {s.Low} low. Saved to {path}",
                color);
        });
    }
}

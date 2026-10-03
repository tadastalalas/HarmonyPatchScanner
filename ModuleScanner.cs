using System;
using System.IO;
using System.Text;
using HarmonyPatchScanner.Model;
using HarmonyPatchScanner.Reporting;
using TaleWorlds.Library;

namespace HarmonyPatchScanner
{
    /// <summary>MCM button "Scan Module" → ModuleScan_{module}.txt for the module chosen in the dropdown.</summary>
    internal static class ModuleScanner
    {
        internal static void ScanSelectedModule() => ScanRunner.Run("Module scan", () =>
        {
            string? moduleId = ScannerSettings.Instance?.GetSelectedModuleId();
            if (string.IsNullOrEmpty(moduleId))
            {
                ScanRunner.Message("Please select a module from the dropdown first.", Colors.Yellow);
                return;
            }

            ScanModel model   = ScanRunner.Scan();
            string moduleName = ModuleLoadOrderHelper.GetModuleName(moduleId);
            string text       = ModuleReport.Build(model, moduleId!, out ModuleSummary s);
            string path       = ScanRunner.Save(model, $"ModuleScan_{SanitizeFileName(moduleName)}.txt", text);

            Color color = s.High > 0 ? Colors.Red : s.Conflicts > 0 || s.Lints > 0 ? Colors.Yellow : Colors.Green;
            ScanRunner.Message(
                $"{moduleName}: {s.Patches} patches, {s.Conflicts} shared methods ({s.High} high risk), {s.Lints} own-patch findings. Saved to {path}",
                color);
        });

        private static string SanitizeFileName(string name)
        {
            char[] invalid = Path.GetInvalidFileNameChars();
            StringBuilder sb = new(name.Length);
            foreach (char c in name)
                sb.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);
            return sb.ToString();
        }
    }
}

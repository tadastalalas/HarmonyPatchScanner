using System.IO;
using TaleWorlds.Library;

namespace HarmonyPatchScanner
{
    internal static class FileHelper
    {
        /// <summary>Modules/HarmonyPatchScanner/logs/{subFolder}/{fileName}; creates the folder if needed.</summary>
        internal static string GetOutputPath(string subFolder, string fileName)
        {
            string folder = Path.Combine(BasePath.Name, "Modules", "HarmonyPatchScanner", "logs", subFolder);
            Directory.CreateDirectory(folder);
            return Path.Combine(folder, fileName);
        }
    }
}
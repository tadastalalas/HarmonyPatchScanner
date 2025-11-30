using System.IO;
using TaleWorlds.Library;

namespace HarmonyPatchScanner
{
    public static class FileHelper
    {
        public static string GetOutputPath(string fileName)
        {
            var modulesPath = Path.Combine(BasePath.Name, "Modules", "HarmonyPatchScanner", "logs");
            Directory.CreateDirectory(modulesPath);
            return Path.Combine(modulesPath, fileName);
        }
    }
}
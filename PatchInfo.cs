using System.Reflection;

namespace HarmonyPatchScanner
{
    public class PatchInfo
    {
        public string TargetMethod { get; set; } = string.Empty;
        public string PatchType { get; set; } = string.Empty;
        public int Priority { get; set; }
        public string Owner { get; set; } = string.Empty;
        public string PatchMethod { get; set; } = string.Empty;
        public int Index { get; set; }
    }
}
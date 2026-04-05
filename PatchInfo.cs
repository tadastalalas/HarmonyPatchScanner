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

        /// <summary>
        /// Harmony IDs this patch explicitly requests to run before.
        /// Populated from [HarmonyBefore] annotation.
        /// </summary>
        public string[] Before { get; set; } = System.Array.Empty<string>();

        /// <summary>
        /// Harmony IDs this patch explicitly requests to run after.
        /// Populated from [HarmonyAfter] annotation.
        /// </summary>
        public string[] After { get; set; } = System.Array.Empty<string>();

        /// <summary>
        /// True if this is a Prefix whose patch method returns bool,
        /// meaning it can return false to skip the original and remaining prefixes.
        /// </summary>
        public bool CanShortCircuit { get; set; }

        /// <summary>
        /// 1-based position of the owning mod in the Bannerlord launcher load order.
        /// Null if the position could not be determined.
        /// This directly maps to which mod's DLL was loaded first by TaleWorlds.
        /// </summary>
        public int? LauncherLoadOrder { get; set; }

        /// <summary>
        /// The raw Harmony owner ID (e.g. "com.mymod.harmony") declared when the
        /// Harmony instance was created. Used to validate before/after references.
        /// </summary>
        public string HarmonyOwner { get; set; } = string.Empty;

        /// <summary>
        /// True when this patch targets a method in an official TaleWorlds assembly.
        /// </summary>
        public bool TargetsOfficialCode { get; set; }
    }
}
using HarmonyLib;

namespace HarmonyPatchScanner
{
    public static class PatchDisplayHelper
    {
        /// <summary>
        /// Returns the Harmony priority value with its human-readable name.
        /// Uses HarmonyLib.Priority constants directly.
        /// </summary>
        public static string FormatPriority(int priority)
        {
            string name;
            if      (priority == Priority.First)            name = "First";
            else if (priority == Priority.VeryHigh)         name = "VeryHigh";
            else if (priority == Priority.High)             name = "High";
            else if (priority == Priority.HigherThanNormal) name = "HigherThanNormal";
            else if (priority == Priority.Normal)           name = "Normal";
            else if (priority == Priority.LowerThanNormal)  name = "LowerThanNormal";
            else if (priority == Priority.Low)              name = "Low";
            else if (priority == Priority.VeryLow)          name = "VeryLow";
            else if (priority == Priority.Last)             name = "Last";
            else                                            name = "Custom";

            return $"{priority} ({name})";
        }

        /// <summary>
        /// Formats the Harmony patch index as a human-readable load order annotation.
        /// When two patches share the same priority, Harmony executes them in ascending
        /// index order for Prefixes/Transpilers (descending for Postfixes/Finalizers).
        /// </summary>
        public static string FormatIndex(int index)
        {
            return $"#{index + 1} (index {index})";
        }

        /// <summary>
        /// Formats the Bannerlord launcher position.
        /// This is the true DLL load order — set by the user in the launcher.
        /// When Harmony patches share priority and index, this is the real tiebreaker.
        /// </summary>
        public static string FormatLauncherOrder(int? launcherPosition)
        {
            return launcherPosition.HasValue
                ? $"Launcher position #{launcherPosition.Value}"
                : "Launcher position unknown";
        }

        /// <summary>
        /// When verbose is false, trims the full namespace and returns only
        /// "DeclaringTypeName.MethodName" for easier reading.
        /// </summary>
        public static string FormatMethodName(string fullName, bool verbose)
        {
            if (verbose || string.IsNullOrEmpty(fullName))
                return fullName;

            var parts = fullName.Split('.');
            return parts.Length >= 2
                ? $"{parts[parts.Length - 2]}.{parts[parts.Length - 1]}"
                : fullName;
        }
    }
}
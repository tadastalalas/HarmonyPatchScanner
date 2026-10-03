using System;
using System.Collections.Generic;

namespace HarmonyPatchScanner.Model
{
    /// <summary>
    /// One mod as seen by the scanner. Resolved from the assembly that declares a patch method.
    /// One instance per key, so reference equality is safe for grouping.
    /// </summary>
    internal sealed class ModInfo
    {
        /// <summary>Module id when known, otherwise a synthetic key derived from folder or assembly name.</summary>
        internal string Key { get; }

        /// <summary>Human-readable name shown in reports (launcher module name when known).</summary>
        internal string Label { get; }

        internal string? ModuleId { get; }
        internal int? LauncherPosition { get; }
        internal bool IsOfficial { get; }
        internal bool IsCommunityLibrary { get; }

        internal HashSet<string> Assemblies { get; } = new(StringComparer.OrdinalIgnoreCase);
        internal HashSet<string> HarmonyIds { get; } = new(StringComparer.Ordinal);

        internal ModInfo(string key, string label, string? moduleId, int? launcherPosition, bool isOfficial, bool isCommunityLibrary)
        {
            Key                = key;
            Label              = label;
            ModuleId           = moduleId;
            LauncherPosition   = launcherPosition;
            IsOfficial         = isOfficial;
            IsCommunityLibrary = isCommunityLibrary;
        }

        public override string ToString() => Label;
    }
}

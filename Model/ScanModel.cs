using System;
using System.Collections.Generic;
using System.Linq;

namespace HarmonyPatchScanner.Model
{
    internal sealed class ScanOptions
    {
        internal bool ExcludeLifecycle { get; set; }
        internal bool ExcludeCommunityLibraries { get; set; }
        internal bool AnalyzePrefixReturns { get; set; }
        internal bool DeepTranspilerAnalysis { get; set; }
    }

    /// <summary>Result of one scan. All three reports are views over this.</summary>
    internal sealed class ScanModel
    {
        internal DateTime ScanTime { get; } = DateTime.Now;
        internal Version HarmonyVersion { get; set; } = new(0, 0);
        internal bool OrderIsExact { get; set; }
        /// <summary>Game state at scan time: "main menu", "campaign", "mission (…)". Set by the entry point.</summary>
        internal string Context { get; set; } = "unknown";
        /// <summary>Sub-folder under logs/ for this context: "mainmenu", "campaign" or "mission". Set by the entry point.</summary>
        internal string ContextFolder { get; set; } = "unknown";
        internal ScanOptions Options { get; }

        internal List<TargetReport> Targets { get; } = [];
        internal List<ModInfo> Mods { get; } = [];

        /// <summary>Every Harmony owner id that has at least one patch anywhere (before filtering).</summary>
        internal HashSet<string> AllOwnerIds { get; } = new(StringComparer.Ordinal);

        /// <summary>Non-fatal problems met while scanning; always printed at the end of a report.</summary>
        internal List<string> Errors { get; } = [];

        internal ScanModel(ScanOptions options) => Options = options;

        /// <summary>Targets that should appear in reports given the current filters.</summary>
        internal IEnumerable<TargetReport> VisibleTargets =>
            Targets.Where(t => !(Options.ExcludeLifecycle && t.IsLifecycle) && t.All.Any(p => !p.IsFiltered));

        internal IEnumerable<ScannedPatch> PatchesOf(ModInfo mod, bool visibleOnly) =>
            VisibleTargets.SelectMany(t => t.All).Where(p => p.Mod == mod && (!visibleOnly || !p.IsFiltered));
    }
}

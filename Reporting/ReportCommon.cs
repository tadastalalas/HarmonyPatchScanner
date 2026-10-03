using System.Collections.Generic;
using System.Linq;
using HarmonyPatchScanner.Model;

namespace HarmonyPatchScanner.Reporting
{
    /// <summary>Header, mod directory, load order and error sections shared by all three reports.</summary>
    internal static class ReportCommon
    {
        internal static void Header(ReportWriter w, ScanModel m, string title)
        {
            w.Title("HARMONY PATCH SCANNER — " + title);
            w.Line($"  Generated {m.ScanTime:yyyy-MM-dd HH:mm:ss} · Harmony {m.HarmonyVersion} · {ModuleLoadOrderHelper.OrderedModules.Count} modules loaded" +
                   (m.OrderIsExact ? " · order: exact (Harmony's PatchSorter)" : " · order: approximate (priority/index only)"));

            List<string> filters = [];
            filters.Add(m.Options.ExcludeLifecycle ? "SubModule lifecycle hooks hidden" : "SubModule lifecycle hooks shown");
            filters.Add(m.Options.ExcludeCommunityLibraries ? "library patches (Harmony, ButterLib, UIExtenderEx, MCM, BEW) hidden unless they share a method with your mods" : "library patches shown");
            filters.Add("prefix return analysis " + (m.Options.AnalyzePrefixReturns ? "on" : "off"));
            filters.Add("deep transpiler analysis " + (m.Options.DeepTranspilerAnalysis ? "on" : "off"));
            w.Wrap("  Settings: ", "            ", string.Join(" · ", filters));
            w.Wrap("  Scan context: ", "                ",
                $"{m.Context} — saved under logs/{m.ContextFolder}/. Patches that mods apply later (on campaign start, per mission) only show up " +
                "when scanning from that state; scan from the main menu, a campaign and a mission to compare the three folders.");
            w.Line("  Full launcher load order and a glossary are at the end of this file.");
        }

        /// <summary>One row per mod that appears in the report: label → module id, assemblies, Harmony ids, launcher position.</summary>
        internal static void ModDirectory(ReportWriter w, IEnumerable<ModInfo> mods, string heading)
        {
            List<ModInfo> list = mods.Distinct().OrderBy(x => x.LauncherPosition ?? int.MaxValue).ThenBy(x => x.Label).ToList();
            if (list.Count == 0) return;

            w.Section(heading);
            List<string[]> rows = [];
            foreach (ModInfo mod in list)
            {
                string flags = mod.IsOfficial ? " [official]" : mod.IsCommunityLibrary ? " [library]" : string.Empty;
                rows.Add([
                    Names.Launcher(mod.LauncherPosition),
                    mod.Label + flags,
                    mod.ModuleId ?? "—",
                    string.Join(", ", mod.Assemblies.OrderBy(a => a)),
                    string.Join(", ", mod.HarmonyIds.OrderBy(a => a))
                ]);
            }
            w.Table("  ", ["launcher", "mod (as shown in this report)", "module id", "assemblies", "harmony ids"], rows);
        }

        internal static void LoadOrder(ReportWriter w)
        {
            w.Section("Launcher load order");
            IReadOnlyList<(int Position, string ModuleId, string ModuleName)> modules = ModuleLoadOrderHelper.OrderedModules;
            if (modules.Count == 0)
            {
                w.Line("  (could not determine load order)");
                return;
            }

            List<string[]> rows = [];
            foreach ((int pos, string id, string name) in modules)
            {
                string flags = ModuleLoadOrderHelper.IsOfficialModule(id) ? "official"
                    : FilterHelper.IsCommunityLibrary(id) ? "library" : string.Empty;
                rows.Add(["#" + pos, name, id, flags]);
            }
            w.Table("  ", ["pos", "module", "id", ""], rows);
        }

        internal static void Errors(ReportWriter w, ScanModel m)
        {
            if (m.Errors.Count == 0) return;
            w.Section($"Problems while scanning ({m.Errors.Count})");
            w.Line("  These patches or methods could not be fully analyzed; the rest of the report is unaffected.");
            foreach (string e in m.Errors)
                w.Wrap("  · ", "    ", e);
        }

        internal static void Footer(ReportWriter w, ScanModel m, IEnumerable<ModInfo> modsInReport)
        {
            ModDirectory(w, modsInReport, "Mods in this report");
            LoadOrder(w);
            Errors(w, m);
            Glossary.Write(w);
        }

        internal static string Count(int n, string one, string many) => n == 1 ? $"{n} {one}" : $"{n} {many}";
    }
}

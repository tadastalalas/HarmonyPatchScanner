using System.Collections.Generic;
using System.Linq;
using HarmonyPatchScanner.Model;

namespace HarmonyPatchScanner.Reporting
{
    internal sealed class ModuleSummary
    {
        internal int Patches   { get; set; }
        internal int Conflicts { get; set; }
        internal int High      { get; set; }
        internal int Lints     { get; set; }
    }

    /// <summary>ModuleScan_{name}.txt — one mod's patches, its conflicts with others, and problems in its own patches.</summary>
    internal static class ModuleReport
    {
        internal static string Build(ScanModel m, string moduleId, out ModuleSummary summary)
        {
            ReportWriter w = new();
            string moduleName = ModuleLoadOrderHelper.GetModuleName(moduleId);
            ReportCommon.Header(w, m, "MODULE REPORT — " + moduleName);

            HashSet<string> assemblies = ModuleLoadOrderHelper.GetAssembliesForModule(moduleId) ?? [];
            ModInfo? you = m.Mods.FirstOrDefault(x => string.Equals(x.ModuleId, moduleId, System.StringComparison.OrdinalIgnoreCase))
                        ?? m.Mods.FirstOrDefault(x => x.Assemblies.Overlaps(assemblies));
            summary = new ModuleSummary();

            w.Line($"  Module id: {moduleId} · launcher {Names.Launcher(ModuleLoadOrderHelper.GetLauncherPosition(moduleId))} · assemblies: {string.Join(", ", assemblies)}" +
                   (you != null && you.HarmonyIds.Count > 0 ? $" · harmony ids: {string.Join(", ", you.HarmonyIds)}" : string.Empty));

            if (you == null)
            {
                w.Section("Result");
                w.Line($"  No Harmony patches found for {moduleName}.");
                w.Line("  If this mod patches later (e.g. on campaign start), run the scan again from that point.");
                ReportCommon.LoadOrder(w);
                ReportCommon.Errors(w, m);
                return w.ToString();
            }

            List<TargetReport> mine = m.VisibleTargets.Where(t => t.HasPatchFrom(you)).OrderBy(t => t.ShortName).ToList();
            List<ScannedPatch> own  = mine.SelectMany(t => t.All).Where(p => p.Mod == you).ToList();

            List<TargetReport> conflicts = mine.Where(t => t.Mods(true).Any(x => x != you)).OrderByDescending(t => t.Severity).ThenBy(t => t.ShortName).ToList();
            List<TargetReport> libsOnly  = mine.Where(t => !conflicts.Contains(t) && t.Mods(false).Any(x => x != you)).ToList();
            List<(ScannedPatch Patch, Finding Lint)> lints = own.SelectMany(p => p.Lints.Select(l => (p, l))).OrderByDescending(x => x.l.Severity).ToList();

            summary.Patches   = own.Count;
            summary.Conflicts = conflicts.Count;
            summary.High      = conflicts.Count(t => t.Severity == Severity.High);
            summary.Lints     = lints.Count(x => x.Lint.Severity >= Severity.Medium);

            // ── Summary ───────────────────────────────────────────────────────────────────────────────
            w.Section("Summary");
            w.Line($"  {own.Count} patches on {mine.Count} methods");
            w.Line($"  {FullReport.Kinds(own)}");
            w.Line($"  Methods shared with other mods: {conflicts.Count}" + (conflicts.Count == 0 ? string.Empty :
                   $"  ({conflicts.Count(t => t.Severity == Severity.High)} HIGH / {conflicts.Count(t => t.Severity == Severity.Medium)} MEDIUM / {conflicts.Count(t => t.Severity <= Severity.Low)} LOW)"));
            if (libsOnly.Count > 0)
                w.Line($"  Methods shared only with hidden libraries: {libsOnly.Count}");
            w.Line($"  Findings about your own patches: {lints.Count} ({lints.Count(x => x.Lint.Severity >= Severity.Medium)} worth fixing)");

            if (conflicts.Count > 0)
            {
                w.Blank();
                w.Line("  Worst first:");
                foreach (TargetReport t in conflicts.Where(t => t.Severity >= Severity.Medium).Take(10))
                {
                    Finding worst = t.Findings.Where(f => f.Mods.Contains(you)).OrderByDescending(f => f.Severity).FirstOrDefault()
                                    ?? t.Findings.OrderByDescending(f => f.Severity).First();
                    w.Line($"    {Names.Severity(t.Severity),-7} {t.ShortName}");
                    w.Wrap("            ", worst.Text);
                }
            }

            // ── Your patches ──────────────────────────────────────────────────────────────────────────
            w.Section($"Your patches ({own.Count})");
            foreach (TargetReport t in mine)
            {
                List<ModInfo> others = t.Mods(false).Where(x => x != you).ToList();
                string flag = others.Count == 0 ? string.Empty
                    : $"   ⚠ shared with {string.Join(", ", others.Select(x => x.Label + (x.IsCommunityLibrary ? " (library)" : string.Empty)))} [{Names.Severity(t.Severity)}]";
                w.Wrap($"  {t.ShortName}", "        ", $"  — {Names.Origin(t)}{flag}");

                foreach (ScannedPatch p in t.All.Where(p => p.Mod == you))
                {
                    w.Line($"      {p.RowLabel,-4}{Names.Kind(p.Kind),-14}{Names.PatchMethod(p.Method)}");
                    List<string> tags = TargetBlockFormatter.Tags(p);
                    tags.Add($"priority {Names.Priority(p.Priority)}");
                    w.Wrap("                        ", string.Join(" · ", tags));
                    foreach (Finding lint in p.Lints.Where(l => l.Severity >= Severity.Medium))
                        w.Wrap("                        ! ", "                          ", lint.Text);
                }
                w.Blank();
            }

            // ── Conflicts ─────────────────────────────────────────────────────────────────────────────
            if (conflicts.Count > 0)
            {
                w.Section($"Methods shared with other mods ({conflicts.Count}) — your patches are marked ◄ YOU");
                foreach (TargetReport t in conflicts)
                    TargetBlockFormatter.Write(w, t, you);
            }
            else
            {
                w.Section("Methods shared with other mods");
                w.Line($"  None. No other mod patches the methods that {moduleName} patches.");
            }

            if (libsOnly.Count > 0)
            {
                w.Section($"Shared only with hidden libraries ({libsOnly.Count})");
                w.Line("  These are hidden by the 'Exclude Community Libraries' setting. Turn it off to see full details.");
                List<string[]> rows = [];
                foreach (TargetReport t in libsOnly)
                    rows.Add([t.ShortName, string.Join(", ", t.All.Where(p => p.Mod != you).Select(p => $"{p.Mod.Label} ({Names.KindSingular(p.Kind)})").Distinct()), Names.Severity(t.Severity)]);
                w.Table("  ", ["method", "library patches", "severity"], rows);
            }

            // ── Your code patched by others ───────────────────────────────────────────────────────────
            List<TargetReport> yourCode = m.VisibleTargets.Where(t => t.OriginMod == you && t.All.Any(p => p.Mod != you && !p.IsFiltered)).OrderBy(t => t.ShortName).ToList();
            if (yourCode.Count > 0)
            {
                w.Section($"Your own code patched by other mods ({yourCode.Count})");
                List<string[]> rows = [];
                foreach (TargetReport t in yourCode)
                    rows.Add([t.ShortName, string.Join(", ", t.All.Where(p => p.Mod != you && !p.IsFiltered).Select(p => $"{p.Mod.Label} ({Names.KindSingular(p.Kind)})").Distinct())]);
                w.Table("  ", ["your method", "patched by"], rows);
            }

            // ── Lints ─────────────────────────────────────────────────────────────────────────────────
            w.Section($"Findings about your own patches ({lints.Count})");
            if (lints.Count == 0)
            {
                w.Line("  Nothing to report.");
            }
            else
            {
                foreach (IGrouping<ScannedPatch, (ScannedPatch Patch, Finding Lint)> g in lints.GroupBy(x => x.Patch))
                {
                    TargetReport t = mine.First(x => x.Method == g.Key.Target);
                    w.Line($"  {t.ShortName} · {g.Key.RowLabel} {Names.Kind(g.Key.Kind)} {Names.PatchMethod(g.Key.Method)}");
                    foreach ((ScannedPatch _, Finding lint) in g)
                    {
                        string first = $"    {TargetBlockFormatter.Glyph(lint.Severity)} {Names.Severity(lint.Severity),-7} ";
                        w.Wrap(first, new string(' ', first.Length), lint.Text);
                    }
                    w.Blank();
                }
            }

            IEnumerable<ModInfo> modsInReport = mine.SelectMany(t => t.Mods(false));
            ReportCommon.Footer(w, m, modsInReport);
            return w.ToString();
        }
    }
}

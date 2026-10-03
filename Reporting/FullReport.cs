using System.Collections.Generic;
using System.Linq;
using HarmonyPatchScanner.Model;

namespace HarmonyPatchScanner.Reporting
{
    internal sealed class FullSummary
    {
        internal int Mods    { get; set; }
        internal int Patches { get; set; }
        internal int Shared  { get; set; }
        internal int High    { get; set; }
    }

    /// <summary>AllHarmonyPatches.txt — every visible patch, grouped by mod in launcher order.</summary>
    internal static class FullReport
    {
        internal static string Build(ScanModel m, out FullSummary summary)
        {
            ReportWriter w = new();
            ReportCommon.Header(w, m, "ALL PATCHES");

            List<TargetReport> targets = m.VisibleTargets.ToList();
            List<ScannedPatch> patches = targets.SelectMany(t => t.All).Where(p => !p.IsFiltered).ToList();
            List<ModInfo> mods = patches.Select(p => p.Mod).Distinct().OrderBy(x => x.LauncherPosition ?? int.MaxValue).ThenBy(x => x.Label).ToList();
            List<TargetReport> shared = targets.Where(t => t.Mods(true).Count > 1).ToList();

            summary = new FullSummary
            {
                Mods    = mods.Count,
                Patches = patches.Count,
                Shared  = shared.Count,
                High    = shared.Count(t => t.Severity == Severity.High)
            };

            // ── Totals ────────────────────────────────────────────────────────────────────────────────
            w.Section("Totals");
            w.Line($"  {targets.Count} patched methods · {patches.Count} patches · {mods.Count} mods");
            w.Line($"  {Kinds(patches)}");
            if (shared.Count > 0)
                w.Line($"  {shared.Count} methods are patched by more than one mod: " +
                       $"{shared.Count(t => t.Severity == Severity.High)} HIGH / {shared.Count(t => t.Severity == Severity.Medium)} MEDIUM / " +
                       $"{shared.Count(t => t.Severity <= Severity.Low)} LOW — details in DuplicateHarmonyPatches.txt");

            // ── Hotspots ──────────────────────────────────────────────────────────────────────────────
            List<TargetReport> hot = shared.Where(t => t.Severity >= Severity.Medium).OrderByDescending(t => t.Severity).ThenBy(t => t.ShortName).ToList();
            if (hot.Count > 0)
            {
                w.Section("Hotspots (methods with HIGH or MEDIUM findings)");
                List<string[]> rows = [];
                foreach (TargetReport t in hot)
                    rows.Add([Names.Severity(t.Severity), t.ShortName, string.Join(", ", t.Mods(false).Select(x => x.Label))]);
                w.Table("  ", ["", "method", "mods"], rows);
            }

            // ── Per mod ───────────────────────────────────────────────────────────────────────────────
            w.Section("Patches by mod (launcher order)");
            foreach (ModInfo mod in mods)
            {
                List<ScannedPatch> own = patches.Where(p => p.Mod == mod).ToList();
                List<TargetReport> ownTargets = targets.Where(t => own.Any(p => p.Target == t.Method)).OrderBy(t => t.ShortName).ToList();

                w.Rule('═');
                w.Line($"  {Names.Launcher(mod.LauncherPosition),-5}{mod.Label}");
                w.Line($"       module id: {mod.ModuleId ?? "—"} · assemblies: {string.Join(", ", mod.Assemblies)} · harmony ids: {string.Join(", ", mod.HarmonyIds)}");
                w.Line($"       {own.Count} patches on {ownTargets.Count} methods · {Kinds(own)}");
                int sharedCount = ownTargets.Count(t => t.Mods(true).Count > 1);
                if (sharedCount > 0)
                    w.Line($"       {sharedCount} of these methods are also patched by other mods");
                w.Blank();

                foreach (TargetReport t in ownTargets)
                {
                    List<ModInfo> others = t.Mods(false).Where(x => x != mod).ToList();
                    string flag = others.Count == 0 ? string.Empty
                        : $"   ⚠ also patched by {string.Join(", ", others.Select(x => x.Label))} [{Names.Severity(t.Severity)}]";
                    w.Wrap($"  {t.ShortName}", "        ", $"  — {Names.Origin(t)}{flag}");

                    foreach (ScannedPatch p in t.All.Where(p => p.Mod == mod))
                    {
                        w.Line($"      {p.RowLabel,-4}{Names.Kind(p.Kind),-14}{Names.PatchMethod(p.Method)}");
                        List<string> tags = TargetBlockFormatter.Tags(p);
                        tags.Add($"priority {Names.Priority(p.Priority)}");
                        w.Wrap("                        ", string.Join(" · ", tags));
                    }
                    w.Blank();
                }
            }

            // ── Ranking ───────────────────────────────────────────────────────────────────────────────
            w.Section("Mods ranked by patch count");
            List<string[]> ranking = [];
            int rank = 1;
            foreach (ModInfo mod in mods.OrderByDescending(x => patches.Count(p => p.Mod == x)))
            {
                List<ScannedPatch> own = patches.Where(p => p.Mod == mod).ToList();
                ranking.Add([$"#{rank++}", mod.Label, own.Count.ToString(), own.Count(p => p.Kind == PatchKind.Transpiler).ToString(),
                    own.Count(p => p.Kind == PatchKind.Prefix && p.Caps.ReturnsBool && p.ReturnClass != PrefixReturn.AlwaysTrue).ToString(), Names.Launcher(mod.LauncherPosition)]);
            }
            w.Table("  ", ["rank", "mod", "patches", "transpilers", "skipping prefixes", "launcher"], ranking);

            ReportCommon.Footer(w, m, mods);
            return w.ToString();
        }

        internal static string Kinds(List<ScannedPatch> ps)
        {
            int prefixes = ps.Count(p => p.Kind == PatchKind.Prefix);
            int skipping = ps.Count(p => p.Kind == PatchKind.Prefix && p.Caps.ReturnsBool && p.ReturnClass != PrefixReturn.AlwaysTrue);
            int postfix  = ps.Count(p => p.Kind == PatchKind.Postfix);
            int writers  = ps.Count(p => p.Kind == PatchKind.Postfix && p.Caps.CanChangeResult);
            int transp   = ps.Count(p => p.Kind == PatchKind.Transpiler);
            int fin      = ps.Count(p => p.Kind == PatchKind.Finalizer);
            int infix    = ps.Count(p => p.Kind is PatchKind.InnerPrefix or PatchKind.InnerPostfix);

            List<string> parts =
            [
                $"prefixes {prefixes} ({skipping} can skip the original)",
                $"postfixes {postfix} ({writers} can change the result)",
                $"transpilers {transp}",
                $"finalizers {fin}"
            ];
            if (infix > 0) parts.Add($"infixes {infix}");
            return string.Join(" · ", parts);
        }
    }
}

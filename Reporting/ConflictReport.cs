using System.Collections.Generic;
using System.Linq;
using HarmonyPatchScanner.Model;

namespace HarmonyPatchScanner.Reporting
{
    internal sealed class ConflictSummary
    {
        internal int Total  { get; set; }
        internal int High   { get; set; }
        internal int Medium { get; set; }
        internal int Low    { get; set; }
        internal int SameMod { get; set; }
    }

    /// <summary>DuplicateHarmonyPatches.txt — methods patched by more than one mod, worst first.</summary>
    internal static class ConflictReport
    {
        internal static string Build(ScanModel m, out ConflictSummary summary)
        {
            ReportWriter w = new();
            ReportCommon.Header(w, m, "CONFLICT REPORT");

            List<TargetReport> shared  = m.VisibleTargets.Where(t => t.Mods(true).Count > 1).ToList();
            List<TargetReport> sameMod = m.VisibleTargets.Where(t => t.Mods(true).Count == 1 && t.All.Count(p => !p.IsFiltered) > 1).ToList();

            List<TargetReport> high   = shared.Where(t => t.Severity == Severity.High).ToList();
            List<TargetReport> medium = shared.Where(t => t.Severity == Severity.Medium).ToList();
            List<TargetReport> low    = shared.Where(t => t.Severity <= Severity.Low).ToList();

            summary = new ConflictSummary { Total = shared.Count, High = high.Count, Medium = medium.Count, Low = low.Count, SameMod = sameMod.Count };

            // ── Read me first ─────────────────────────────────────────────────────────────────────────
            w.Section("Read me first");
            if (shared.Count == 0)
            {
                w.Line("  No method is patched by more than one mod. Nothing can conflict.");
            }
            else
            {
                w.Line($"  {ReportCommon.Count(shared.Count, "method is", "methods are")} patched by more than one mod.");
                w.Line($"    {high.Count,4} HIGH    — something is skipped, overwritten or broken for sure");
                w.Line($"    {medium.Count,4} MEDIUM  — works only if the current order is the intended one");
                w.Line($"    {low.Count,4} LOW     — several mods touch the method; nothing found that interferes");
                if (sameMod.Count > 0)
                    w.Line($"    {sameMod.Count,4} methods are patched more than once by the same mod (listed at the end)");
                w.Blank();

                List<TargetReport> top = high.Concat(medium).Take(15).ToList();
                if (top.Count > 0)
                {
                    w.Line("  Worst first:");
                    foreach (TargetReport t in top)
                    {
                        Finding worst = t.Findings.OrderByDescending(f => f.Severity).First();
                        string first = $"    {Names.Severity(t.Severity),-7} {t.ShortName}";
                        w.Line(first);
                        w.Wrap("            ", worst.Text);
                    }
                }
            }

            // ── Details by severity ───────────────────────────────────────────────────────────────────
            Bucket(w, high,   "HIGH — broken or overridden");
            Bucket(w, medium, "MEDIUM — order-dependent");
            Bucket(w, low,    "LOW — coexisting");

            // ── Same mod, several patches ─────────────────────────────────────────────────────────────
            if (sameMod.Count > 0)
            {
                w.Section("Same mod, several patches on one method");
                w.Line("  Usually intentional (e.g. prefix + postfix pair). Listed for completeness; details are in the module report.");
                w.Blank();
                List<string[]> rows = [];
                foreach (TargetReport t in sameMod.OrderBy(t => t.ShortName))
                {
                    ModInfo mod = t.Mods(true)[0];
                    rows.Add([t.ShortName, mod.Label, Composition(t)]);
                }
                w.Table("  ", ["method", "mod", "patches"], rows);
            }

            IEnumerable<ModInfo> modsInReport = shared.Concat(sameMod).SelectMany(t => t.Mods(false));
            ReportCommon.Footer(w, m, modsInReport);
            return w.ToString();
        }

        private static void Bucket(ReportWriter w, List<TargetReport> targets, string heading)
        {
            if (targets.Count == 0) return;
            w.Section($"{heading}  ({targets.Count})");
            foreach (TargetReport t in targets.OrderByDescending(t => t.Findings.Count(f => f.Severity == t.Severity)).ThenBy(t => t.ShortName))
                TargetBlockFormatter.Write(w, t, null);
        }

        internal static string Composition(TargetReport t)
        {
            List<string> parts = [];
            Add(parts, t.Prefixes.Count,      "prefix", "prefixes");
            Add(parts, t.Postfixes.Count,     "postfix", "postfixes");
            Add(parts, t.Transpilers.Count,   "transpiler", "transpilers");
            Add(parts, t.Finalizers.Count,    "finalizer", "finalizers");
            Add(parts, t.InnerPrefixes.Count + t.InnerPostfixes.Count, "infix", "infixes");
            return string.Join(", ", parts);
        }

        private static void Add(List<string> parts, int n, string one, string many)
        {
            if (n > 0) parts.Add(ReportCommon.Count(n, one, many));
        }
    }
}

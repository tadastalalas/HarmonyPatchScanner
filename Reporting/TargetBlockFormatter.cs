using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyPatchScanner.Analysis;
using HarmonyPatchScanner.Model;

namespace HarmonyPatchScanner.Reporting
{
    /// <summary>Renders one target method: header, execution flow, problems, order details.</summary>
    internal static class TargetBlockFormatter
    {
        private const string RowIndent    = "    ";
        private const string DetailIndent = "                      ";   // 22 = row indent + label + kind columns

        internal static void Write(ReportWriter w, TargetReport t, ModInfo? you)
        {
            const string pad = "          ";   // width of the "[SEVERITY]" column
            w.Rule();
            w.Line($"{"[" + Names.Severity(t.Severity) + "]",-10}{t.ShortName}");
            w.Line(pad + (t.Namespace.Length > 0 ? t.Namespace + " · " : string.Empty) + Names.Origin(t));

            List<ModInfo> mods = t.Mods(false);
            w.Wrap(pad + "patched by: ", pad + "            ",
                $"{string.Join(", ", mods.Select(m => m.Label + (m.IsCommunityLibrary ? " (library)" : string.Empty)))}  ({t.PatchCount} patches)");
            if (!t.OrderIsExact)
                w.Line(pad + "note: Harmony's sorter was not reachable; order below is priority/index only.");
            w.Blank();

            WriteFlow(w, t, you);
            WriteProblems(w, t);
            WriteOrderDetails(w, t);
        }

        // ── execution flow ───────────────────────────────────────────────────────────────────────────

        private static void WriteFlow(ReportWriter w, TargetReport t, ModInfo? you)
        {
            w.Line("  Execution flow");

            List<ScannedPatch> rewriters = t.Transpilers.Concat(t.InnerPrefixes).Concat(t.InnerPostfixes).ToList();
            if (rewriters.Count > 0)
            {
                w.Line(RowIndent + "— applied once, at patch time, in this order (each sees the previous one's output) —");
                foreach (ScannedPatch p in rewriters)
                    WriteRow(w, p, t, you);
                w.Line(RowIndent + "— per call —");
            }

            ScannedPatch? firstSkipper = t.Prefixes.FirstOrDefault(p => p.Caps.ReturnsBool && p.ReturnClass != PrefixReturn.AlwaysTrue);

            foreach (ScannedPatch p in t.Prefixes)
                WriteRow(w, p, t, you);

            string original = firstSkipper == null
                ? "always runs"
                : firstSkipper.ReturnClass == PrefixReturn.AlwaysFalse
                    ? $"never runs — {firstSkipper.RowLabel} always returns false"
                    : $"runs unless {firstSkipper.RowLabel} returns false";
            if (t.Transpilers.Count > 0)
                original += $" (body rewritten by {string.Join(", ", t.Transpilers.Select(x => x.RowLabel))})";
            w.Line($"{RowIndent}{string.Empty,-4}{"ORIGINAL",-14}{original}");

            bool hasVoid = t.Postfixes.Any(p => !p.Caps.PassThrough);
            bool hasPass = t.Postfixes.Any(p => p.Caps.PassThrough);
            foreach (ScannedPatch p in t.Postfixes)
            {
                if (p.Caps.PassThrough && hasVoid && hasPass && ReferenceEquals(p, t.Postfixes.First(x => x.Caps.PassThrough)))
                    w.Line(RowIndent + "— pass-through postfixes run after all void postfixes —");
                WriteRow(w, p, t, you);
            }

            foreach (ScannedPatch p in t.Finalizers)
                WriteRow(w, p, t, you);

            w.Blank();
        }

        private static void WriteRow(ReportWriter w, ScannedPatch p, TargetReport t, ModInfo? you)
        {
            string label = p.RowLabel.PadRight(4);
            string kind  = Names.Kind(p.Kind).PadRight(14);
            string mod   = (p.Mod.Label + (p.IsFiltered ? " (library)" : string.Empty)).PadRight(24);
            string marker = you != null && p.Mod == you ? "   ◄ YOU" : string.Empty;

            w.Line($"{RowIndent}{label}{kind}{mod}{Names.PatchMethod(p.Method)}{marker}");

            List<string> tags = Tags(p);
            if (tags.Count > 0)
                w.Wrap(DetailIndent, string.Join(" · ", tags));

            foreach (string status in StatusLines(p, t))
                w.Wrap(DetailIndent, status);

            foreach (HintRef h in p.Hints)
                w.Wrap(DetailIndent, HintLine(h, p, t));

            if (p.Caps.Notes.Count > 0)
                w.Wrap(DetailIndent, "note: " + string.Join("; ", p.Caps.Notes));
        }

        /// <summary>Short capability tags; shared with the full report.</summary>
        internal static List<string> Tags(ScannedPatch p)
        {
            PatchCapabilities c = p.Caps;
            List<string> tags = [];

            switch (p.Kind)
            {
                case PatchKind.Prefix:
                    tags.Add(c.ReturnsBool ? "bool → " + ReturnText(p.ReturnClass) : "void");
                    break;
                case PatchKind.Postfix:
                    if (c.PassThrough) tags.Add("pass-through: its return value becomes __result");
                    break;
                case PatchKind.Finalizer:
                    tags.Add(c.ReturnsException ? "returns Exception → can swallow or replace the exception" : "void");
                    if (c.ObservesException) tags.Add("sees __exception");
                    break;
                case PatchKind.Transpiler:
                    if (p.Delta != null) tags.Add(DeltaText(p.Delta));
                    if (c.TakesGenerator) tags.Add("uses ILGenerator");
                    break;
                case PatchKind.InnerPrefix:
                case PatchKind.InnerPostfix:
                    tags.Add(p.InnerMethod != null
                        ? $"wraps call to {Names.Method(p.InnerMethod)}{Positions(p.InnerPositions)}"
                        : "wraps an inner call (target unknown)");
                    break;
            }

            if (p.Kind != PatchKind.Transpiler)
            {
                if (c.CanChangeResult && !c.PassThrough)      tags.Add("can change __result");
                else if (c.ReadsResult && p.Kind != PatchKind.Prefix && !c.PassThrough) tags.Add("reads __result");

                if (c.UsesArgsArray)                          tags.Add("can change all arguments (__args)");
                else if (c.CanChangeArgs.Count > 0)           tags.Add("can change " + Quoted(c.CanChangeArgs));

                if (c.WritableFields.Count > 0)               tags.Add("can change field " + Quoted(c.WritableFields));
                else if (c.Fields.Count > 0)                  tags.Add("reads field " + Quoted(c.Fields));

                if (c.UsesState)                              tags.Add(c.StateIsWritable && p.Kind == PatchKind.Prefix ? "sets __state" : "reads __state");
                if (c.UsesRunOriginal)                        tags.Add("checks __runOriginal");
                if (p.Kind == PatchKind.Prefix && !c.AffectsOriginal) tags.Add("always runs (no side effects)");
            }

            if (c.IsFactory) tags.Add("factory: real patch method is created at patch time");
            if (p.Debug)     tags.Add("[HarmonyDebug]");
            return tags;
        }

        private static IEnumerable<string> StatusLines(ScannedPatch p, TargetReport t)
        {
            if (p.Kind == PatchKind.Prefix)
            {
                ScannedPatch? skipper = t.Prefixes.FirstOrDefault(q => q.Slot < p.Slot && q.Caps.ReturnsBool && q.ReturnClass != PrefixReturn.AlwaysTrue);
                if (skipper != null && p.Caps.AffectsOriginal)
                    yield return skipper.ReturnClass == PrefixReturn.AlwaysFalse
                        ? $"✗ never runs: {skipper.RowLabel} always returns false and this prefix counts as side-effecting ({p.Caps.AffectsReason})"
                        : $"✗ skipped when {skipper.RowLabel} returns false (counts as side-effecting: {p.Caps.AffectsReason})";

                if (p.Caps.ReturnsBool && p.ReturnClass != PrefixReturn.AlwaysTrue)
                {
                    List<string> skipped = ["the original"];
                    skipped.AddRange(t.Prefixes.Where(q => q.Slot > p.Slot && q.Caps.AffectsOriginal).Select(q => q.RowLabel));
                    skipped.AddRange(t.Transpilers.Select(q => q.RowLabel));
                    skipped.AddRange(t.InnerPrefixes.Concat(t.InnerPostfixes).Select(q => q.RowLabel));
                    yield return (p.ReturnClass == PrefixReturn.AlwaysFalse ? "⇒ always skips " : "⇒ when false, skips ") + string.Join(", ", skipped);
                }
            }
            else if (p.Kind == PatchKind.Transpiler && p.Delta != null)
            {
                if (p.Delta.Threw)         yield return "✗ THREW when re-applied: " + p.Delta.Error;
                else if (p.Delta.Skipped)  yield return "not evaluated: an earlier transpiler threw";
                else if (p.Delta.NoEffect) yield return "✗ NO EFFECT: changed nothing when re-applied";
            }

            foreach (Finding lint in p.Lints.Where(l => l.Severity >= Severity.Medium && p.Kind is not (PatchKind.Transpiler or PatchKind.InnerPrefix or PatchKind.InnerPostfix)))
                yield return "! " + lint.Text;
        }

        private static string HintLine(HintRef h, ScannedPatch p, TargetReport t)
        {
            string attr = h.Kind == HintKind.Before ? "HarmonyBefore" : "HarmonyAfter";
            string tag  = $"[{attr} \"{h.OwnerId}\"]";
            switch (h.Status)
            {
                case HintStatus.Bound:
                {
                    // PatchSorter binds within one kind only, so only same-kind rows are affected.
                    List<string> rows = t.ListFor(p.Kind).Where(q => q.HarmonyId == h.OwnerId && q != p).Select(q => q.RowLabel).ToList();
                    return $"{tag} → enforced: runs {(h.Kind == HintKind.Before ? "before" : "after")} {string.Join(", ", rows)}";
                }
                case HintStatus.Cyclic:
                    return $"{tag} → part of a circular constraint; Harmony dropped one of the constraints";
                case HintStatus.OwnerHasOtherKindHere:
                    return $"{tag} → no effect: that mod has only other kinds of patches on this method";
                case HintStatus.OwnerLoadedNotHere:
                    return $"{tag} → nothing to order against (that mod has no such patch here)";
                case HintStatus.CaseMismatch:
                    return $"{tag} → ignored: ids are case-sensitive, '{h.Suggestion}' exists";
                default:
                    return $"{tag} → nothing to order against (id not loaded)";
            }
        }

        // ── problems ─────────────────────────────────────────────────────────────────────────────────

        private static void WriteProblems(ReportWriter w, TargetReport t)
        {
            w.Line("  Problems");
            if (t.Findings.Count == 0)
            {
                w.Line("    · none found");
            }
            else
            {
                foreach (Finding f in t.Findings.OrderByDescending(f => f.Severity))
                {
                    string first = $"    {Glyph(f.Severity)} {Names.Severity(f.Severity),-7} ";
                    w.Wrap(first, new string(' ', first.Length), f.Text);
                }
            }
            w.Blank();
        }

        internal static string Glyph(Severity s) => s switch
        {
            Severity.High   => "✗",
            Severity.Medium => "!",
            _               => "·"
        };

        // ── order details ────────────────────────────────────────────────────────────────────────────

        private static void WriteOrderDetails(ReportWriter w, TargetReport t)
        {
            bool anyContest = Enum.GetValues(typeof(PatchKind)).Cast<PatchKind>().Any(k => t.ListFor(k).Count > 1);
            if (!anyContest) return;

            w.Line("  Order details  (per kind: higher priority first, then lower index first; before/after override)");
            List<string[]> rows = [];
            foreach (ScannedPatch p in t.Transpilers.Concat(t.InnerPrefixes).Concat(t.InnerPostfixes).Concat(t.Prefixes).Concat(t.Postfixes).Concat(t.Finalizers))
            {
                string hints = string.Join(", ",
                    p.Hints.Select(h => (h.Kind == HintKind.Before ? "before " : "after ") + h.OwnerId + (h.Status == HintStatus.Bound ? string.Empty : " (no effect)")));
                rows.Add([p.RowLabel, Names.KindSingular(p.Kind), p.Mod.Label, p.HarmonyId, Names.Priority(p.Priority), p.Index.ToString(), hints.Length == 0 ? "—" : hints]);
            }
            w.Table("    ", ["row", "kind", "mod", "harmony id", "priority", "index", "before / after"], rows);
            w.Blank();
        }

        // ── helpers ──────────────────────────────────────────────────────────────────────────────────

        internal static string ReturnText(PrefixReturn r) => r switch
        {
            PrefixReturn.AlwaysTrue  => "always TRUE",
            PrefixReturn.AlwaysFalse => "always FALSE",
            PrefixReturn.Conditional => "TRUE or FALSE (conditional)",
            PrefixReturn.Unknown     => "value not analyzed",
            _                        => "not bool"
        };

        internal static string DeltaText(TranspilerDelta d)
        {
            if (d.Threw)   return "Δ threw";
            if (d.Skipped) return "Δ not evaluated";
            if (d.NoEffect) return "Δ none";
            string members = d.TouchedMembers.Count == 0
                ? string.Empty
                : " · around " + string.Join(", ", d.TouchedMembers.Take(3)) + (d.TouchedMembers.Count > 3 ? $" (+{d.TouchedMembers.Count - 3})" : string.Empty);
            return $"Δ +{d.Added} −{d.Removed} instructions{members}";
        }

        private static string Positions(int[] positions) =>
            positions.Length == 0 ? " (every call)" : " (call " + string.Join(", ", positions.Select(p => p < 0 ? $"{-p}. from the end" : "#" + p)) + ")";

        private static string Quoted(List<string> names) => string.Join(", ", names.Select(n => "'" + n + "'"));
    }
}

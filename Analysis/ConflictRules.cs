using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyPatchScanner.Model;
using HarmonyPatchScanner.Reporting;

namespace HarmonyPatchScanner.Analysis
{
    /// <summary>
    /// Turns the ordered, analyzed patches of one target into plain-language findings.
    /// Every rule is grounded in Harmony 2.4.2's MethodCreator/PatchSorter behaviour; see Glossary.
    /// </summary>
    internal static class ConflictRules
    {
        internal static void Apply(TargetReport target, ScanOptions options)
        {
            PrefixSkipChains(target);
            ResultWriters(target);
            ArgumentWriters(target);
            Finalizers(target);
            Transpilers(target);
            Infixes(target, options);
            Hints(target);
            IndexCollisions(target);
            StateUsage(target);
            Coexistence(target);
        }

        // ── Prefix skip chains ────────────────────────────────────────────────────────────────────────

        private static void PrefixSkipChains(TargetReport t)
        {
            List<ScannedPatch> prefixes = t.Prefixes;
            for (int i = 0; i < prefixes.Count; i++)
            {
                ScannedPatch p = prefixes[i];
                if (!p.Caps.ReturnsBool) continue;

                if (p.ReturnClass == PrefixReturn.AlwaysTrue)
                {
                    p.Lints.Add(new Finding(Severity.Info,
                        "returns bool but every return statement returns true — a void prefix would do the same and could never be skipped by other prefixes.", p.Mod));
                    continue;
                }

                List<ScannedPatch> victims   = prefixes.Skip(i + 1).Where(v => v.Caps.AffectsOriginal).ToList();
                List<ScannedPatch> others    = victims.Where(v => v.Mod != p.Mod).ToList();
                List<ScannedPatch> rewriters = t.Transpilers.Concat(t.InnerPrefixes).Concat(t.InnerPostfixes).ToList();
                List<ScannedPatch> otherRw   = rewriters.Where(r => r.Mod != p.Mod).ToList();
                List<ScannedPatch> readers   = t.Postfixes.Concat(t.Finalizers)
                    .Where(q => q.Mod != p.Mod && q.Caps.ReadsResult && !q.Caps.UsesRunOriginal).ToList();

                string when = p.ReturnClass switch
                {
                    PrefixReturn.AlwaysFalse => "always returns false",
                    PrefixReturn.Conditional => "can return false",
                    _                        => "returns bool (value not analyzed)"
                };

                List<string> skipped = ["the original method"];
                if (victims.Count > 0)
                    skipped.Add($"{Plural(victims.Count, "prefix", "prefixes")} {Refs(victims)}");
                if (t.Transpilers.Count > 0)
                    skipped.Add($"the code of {Plural(t.Transpilers.Count, "transpiler", "transpilers")} {Refs(t.Transpilers)}");
                if (t.InnerPrefixes.Count + t.InnerPostfixes.Count > 0)
                    skipped.Add($"{Plural(t.InnerPrefixes.Count + t.InnerPostfixes.Count, "infix", "infixes")} {Refs(t.InnerPrefixes.Concat(t.InnerPostfixes))}");

                string text = p.ReturnClass == PrefixReturn.AlwaysFalse
                    ? $"{Ref(p)} {when}: {Join(skipped)} never {(skipped.Count == 1 ? "runs" : "run")}."
                    : $"{Ref(p)} {when}; when it does, {Join(skipped)} {(skipped.Count == 1 ? "is" : "are")} skipped.";

                if (readers.Count > 0)
                    text += readers.Count == 1
                        ? $" Postfix {Refs(readers)} still runs and reads whatever __result {Ref(p)} left behind."
                        : $" Postfixes {Refs(readers)} still run and read whatever __result {Ref(p)} left behind.";

                bool hitsOthers = others.Count > 0 || otherRw.Count > 0;
                Severity severity = p.ReturnClass == PrefixReturn.AlwaysFalse
                    ? (hitsOthers ? Severity.High : (readers.Count > 0 ? Severity.Medium : Severity.Low))
                    : (hitsOthers ? Severity.Medium : Severity.Low);

                // A method replaced by a single mod with nobody else around is not a conflict.
                if (t.Mods(false).Count == 1) severity = Severity.Info;

                t.Findings.Add(new Finding(severity, text, Mods(p, victims, rewriters, readers)));

                foreach (ScannedPatch v in victims.Where(v => !v.Caps.ReturnsBool && !HasRefOrOut(v)))
                    v.Lints.Add(new Finding(Severity.Low,
                        $"counts as side-effecting only because of a {v.Caps.AffectsReason}; it is skipped when {Ref(p)} returns false. Taking only value-type parameters would make it always run.", v.Mod));
            }
        }

        private static bool HasRefOrOut(ScannedPatch p) =>
            p.Caps.CanChangeArgs.Count > 0 || p.Caps.CanChangeResult || p.Caps.WritableFields.Count > 0 || p.Caps.UsesArgsArray;

        // ── __result ──────────────────────────────────────────────────────────────────────────────────

        private static void ResultWriters(TargetReport t)
        {
            foreach (ScannedPatch p in t.Prefixes)
            {
                if (!p.Caps.CanChangeResult) continue;
                bool skipsOriginal = p.Caps.ReturnsBool && p.ReturnClass != PrefixReturn.AlwaysTrue;
                if (!skipsOriginal)
                    p.Lints.Add(new Finding(Severity.Medium,
                        "can write __result but never skips the original method, which then overwrites __result. Return false to keep the value, or move the write to a postfix.", p.Mod));
            }

            List<ScannedPatch> writers = t.Prefixes.Where(p => p.Caps.CanChangeResult)
                .Concat(t.Postfixes.Where(p => p.Caps.CanChangeResult))
                .Concat(t.Finalizers.Where(p => p.Caps.CanChangeResult))
                .ToList();

            List<ModInfo> mods = DistinctMods(writers);
            if (mods.Count < 2) return;

            ScannedPatch last = writers[writers.Count - 1];
            t.Findings.Add(new Finding(Severity.Medium,
                $"__result can be changed by {Refs(writers)}; they run in that order, so {Ref(last)} has the final say.", mods));
        }

        // ── arguments ────────────────────────────────────────────────────────────────────────────────

        private static void ArgumentWriters(TargetReport t)
        {
            Dictionary<string, List<ScannedPatch>> byArg = new(StringComparer.Ordinal);
            string[] allArgs = t.Method.GetParameters().Select(p => p.Name ?? "?").ToArray();

            foreach (ScannedPatch p in t.Prefixes.Concat(t.Postfixes).Concat(t.Finalizers))
            {
                IEnumerable<string> args = p.Caps.UsesArgsArray ? allArgs : p.Caps.CanChangeArgs;
                foreach (string a in args)
                {
                    if (!byArg.TryGetValue(a, out List<ScannedPatch>? list))
                        byArg[a] = list = [];
                    if (!list.Contains(p)) list.Add(p);
                }
            }

            foreach (KeyValuePair<string, List<ScannedPatch>> kv in byArg)
            {
                List<ModInfo> mods = DistinctMods(kv.Value);
                if (mods.Count < 2) continue;
                t.Findings.Add(new Finding(Severity.Medium,
                    $"argument '{kv.Key}' can be changed by {Refs(kv.Value)}; each one sees the value left by the previous.", mods));
            }
        }

        // ── finalizers ───────────────────────────────────────────────────────────────────────────────

        private static void Finalizers(TargetReport t)
        {
            List<ScannedPatch> exceptionWriters = t.Finalizers.Where(f => f.Caps.ReturnsException).ToList();
            if (exceptionWriters.Count == 0) return;

            List<ModInfo> mods = DistinctMods(exceptionWriters);
            if (mods.Count >= 2)
            {
                ScannedPatch last = exceptionWriters[exceptionWriters.Count - 1];
                t.Findings.Add(new Finding(Severity.High,
                    $"finalizers {Refs(exceptionWriters)} {(exceptionWriters.Count == 2 ? "both" : "all")} return an Exception; each overwrites the previous decision, so {Ref(last)} decides what is thrown or whether the exception is swallowed.", mods));
                return;
            }

            if (t.Mods(false).Count > 1)
            {
                ScannedPatch f = exceptionWriters[0];
                t.Findings.Add(new Finding(Severity.Low,
                    $"{Ref(f)} returns an Exception: it can swallow or replace any exception thrown by the original method or by the other mods' patches here.", f.Mod));
            }
        }

        // ── transpilers ──────────────────────────────────────────────────────────────────────────────

        private static void Transpilers(TargetReport t)
        {
            List<ScannedPatch> ts = t.Transpilers;
            if (ts.Count == 0) return;

            bool analyzed = ts.All(x => x.Delta != null);

            if (!analyzed)
            {
                if (DistinctMods(ts).Count >= 2)
                    t.Findings.Add(new Finding(Severity.Medium,
                        $"{ts.Count} transpilers rewrite this method in sequence ({Refs(ts)}); each one works on the output of the previous. Whether they interfere cannot be told from signatures — enable 'Deep transpiler analysis'.", DistinctMods(ts)));
                return;
            }

            for (int i = 0; i < ts.Count; i++)
            {
                ScannedPatch x = ts[i];
                TranspilerDelta d = x.Delta!;

                if (d.Threw)
                {
                    t.Findings.Add(new Finding(Severity.High,
                        $"{Ref(x)} threw when the transpiler chain was re-applied: {d.Error}. Its changes are not in the method, and any mod that patched this method later received this exception from Harmony.", x.Mod));
                    x.Lints.Add(new Finding(Severity.High, $"throws when applied: {d.Error}", x.Mod));
                    continue;
                }
                if (d.Skipped)
                    continue;

                if (d.NoEffect)
                {
                    string why = i == 0
                        ? "the instructions it looks for do not exist in this game version (or the transpiler refuses to run a second time)"
                        : $"the instructions it looks for were already removed or changed by {Refs(ts.Take(i))}";
                    t.Findings.Add(new Finding(Severity.High, $"{Ref(x)} changes nothing: {why}. The mod's feature is silently missing.", x.Mod));
                    x.Lints.Add(new Finding(Severity.High, "changes nothing when applied — its anchor instructions are not present.", x.Mod));
                }
            }

            // Overlap between pairs from different mods.
            bool anyOverlap = false;
            for (int i = 0; i < ts.Count; i++)
            {
                for (int j = i + 1; j < ts.Count; j++)
                {
                    ScannedPatch a = ts[i], b = ts[j];
                    if (a.Mod == b.Mod || a.Delta!.Threw || b.Delta!.Threw || a.Delta.Skipped || b.Delta.Skipped) continue;

                    List<string> shared = a.Delta.TouchedMembers.Intersect(b.Delta.TouchedMembers, StringComparer.Ordinal).ToList();
                    if (shared.Count == 0) continue;

                    anyOverlap = true;
                    t.Findings.Add(new Finding(Severity.Medium,
                        $"{Ref(a)} and {Ref(b)} both edit code around {Join(shared.Take(3).ToList())}{(shared.Count > 3 ? " and more" : string.Empty)}; {Ref(b)} works on {Ref(a)}'s output.", a.Mod, b.Mod));
                }
            }

            if (!anyOverlap && DistinctMods(ts).Count >= 2 && ts.All(x => !x.Delta!.Threw && !x.Delta.NoEffect && !x.Delta.Skipped))
                t.Findings.Add(new Finding(Severity.Low,
                    $"transpilers {Refs(ts)} change different parts of the method (no shared members); later ones see earlier output.", DistinctMods(ts)));
        }

        // ── infixes ──────────────────────────────────────────────────────────────────────────────────

        private static void Infixes(TargetReport t, ScanOptions options)
        {
            List<ScannedPatch> infixes = t.InnerPrefixes.Concat(t.InnerPostfixes).ToList();
            if (infixes.Count == 0) return;

            foreach (ScannedPatch i in infixes)
                foreach (Finding lint in i.Lints.Where(l => l.Severity >= Severity.Medium))
                    t.Findings.Add(new Finding(lint.Severity, $"{Ref(i)}: {lint.Text}", i.Mod));

            if (options.DeepTranspilerAnalysis) return;

            foreach (ScannedPatch i in infixes)
            {
                List<ScannedPatch> foreignTranspilers = t.Transpilers.Where(x => x.Mod != i.Mod).ToList();
                if (foreignTranspilers.Count == 0) continue;
                t.Findings.Add(new Finding(Severity.Low,
                    $"{Ref(i)} wraps a call to {Names.Method(i.InnerMethod!)} inside this method while {Refs(foreignTranspilers)} rewrite the method; if that call is removed the infix silently stops working. Enable 'Deep transpiler analysis' to verify.",
                    foreignTranspilers.Select(x => x.Mod).Append(i.Mod)));
            }
        }

        // ── before / after ───────────────────────────────────────────────────────────────────────────

        private static void Hints(TargetReport t)
        {
            foreach (ScannedPatch p in t.All)
            {
                foreach (HintRef h in p.Hints)
                {
                    string attr = h.Kind == HintKind.Before ? "HarmonyBefore" : "HarmonyAfter";
                    string tag  = $"[{attr}(\"{h.OwnerId}\")]";
                    switch (h.Status)
                    {
                        case HintStatus.Cyclic:
                            t.Findings.Add(new Finding(Severity.Medium,
                                $"{Ref(p)}'s {tag} is part of a circular ordering constraint; Harmony drops one of the constraints silently, so the order is not what the mods asked for.", p.Mod));
                            break;
                        case HintStatus.OwnerHasOtherKindHere:
                        {
                            string kinds = Join(t.All.Where(q => q.HarmonyId == h.OwnerId).Select(q => Names.KindPlural(q.Kind)).Distinct().ToList());
                            t.Findings.Add(new Finding(Severity.Low,
                                $"{Ref(p)}'s {tag} has no effect: '{h.OwnerId}' only has {kinds} on this method, and ordering hints bind only among patches of the same kind.", p.Mod));
                            break;
                        }
                        case HintStatus.CaseMismatch:
                            p.Lints.Add(new Finding(Severity.Low,
                                $"{tag} matches no Harmony id; '{h.Suggestion}' exists. Harmony compares ids case-sensitively, so this hint is ignored.", p.Mod));
                            break;
                        case HintStatus.OwnerLoadedNotHere:
                            p.Lints.Add(new Finding(Severity.Info,
                                $"{tag}: '{h.OwnerId}' has no {Names.KindSingular(p.Kind)} on this method, so there is nothing to order against (harmless).", p.Mod));
                            break;
                        case HintStatus.OwnerNotLoaded:
                            p.Lints.Add(new Finding(Severity.Info,
                                $"{tag}: no loaded mod uses the Harmony id '{h.OwnerId}' (harmless if that mod is optional).", p.Mod));
                            break;
                    }
                }
            }
        }

        // ── index collisions ─────────────────────────────────────────────────────────────────────────

        private static void IndexCollisions(TargetReport t)
        {
            foreach (PatchKind kind in Enum.GetValues(typeof(PatchKind)))
            {
                List<ScannedPatch> list = t.ListFor(kind);
                foreach (IGrouping<(int, int), ScannedPatch> g in list.GroupBy(p => (p.Priority, p.Index)))
                {
                    if (g.Count() < 2) continue;
                    List<ScannedPatch> same = g.ToList();
                    t.Findings.Add(new Finding(Severity.Low,
                        $"{Refs(same)} share priority and Harmony index (a patch was removed and re-added on this method earlier); Harmony's sort gives no guarantee about their relative order.", DistinctMods(same)));
                }
            }
        }

        // ── __state ──────────────────────────────────────────────────────────────────────────────────

        private static void StateUsage(TargetReport t)
        {
            IEnumerable<IGrouping<Type?, ScannedPatch>> byClass = t.Prefixes.Concat(t.Postfixes).Concat(t.Finalizers)
                .Where(p => p.Caps.UsesState)
                .GroupBy(p => p.Method.DeclaringType);

            foreach (IGrouping<Type?, ScannedPatch> g in byClass)
            {
                bool hasProducer = g.Any(p => p.Kind == PatchKind.Prefix && p.Caps.StateIsWritable);
                bool hasConsumer = g.Any(p => p.Kind != PatchKind.Prefix);

                foreach (ScannedPatch p in g)
                {
                    if (p.Kind != PatchKind.Prefix && !hasProducer)
                        p.Lints.Add(new Finding(Severity.Medium,
                            $"reads __state but no prefix in {Names.Type(p.Method.DeclaringType)} writes it (Harmony keys __state by the patch class), so it is always default/null.", p.Mod));
                    else if (p.Kind == PatchKind.Prefix && p.Caps.StateIsWritable && !hasConsumer)
                        p.Lints.Add(new Finding(Severity.Info,
                            $"sets __state but no postfix or finalizer in {Names.Type(p.Method.DeclaringType)} reads it.", p.Mod));
                    else if (p.Kind == PatchKind.Prefix && !p.Caps.StateIsWritable)
                        p.Lints.Add(new Finding(Severity.Low,
                            "takes __state without ref/out in a prefix; the value is always default/null here.", p.Mod));
                }
            }
        }

        // ── summary for targets without real problems ───────────────────────────────────────────────

        private static void Coexistence(TargetReport t)
        {
            if (t.Findings.Any(f => f.Severity >= Severity.Low)) return;
            if (t.Mods(false).Count < 2) return;

            List<ScannedPatch> changers = t.All.Where(CanChangeSomething).ToList();
            List<ModInfo> changerMods = DistinctMods(changers);

            string text = changers.Count == 0
                ? "no patch here can change the result, the arguments or the control flow; all of them only observe, so their order does not matter."
                : changerMods.Count == 1
                    ? $"only {changerMods[0].Label} changes anything here ({Refs(changers)}); the other mods only observe."
                    : "the patches that change something touch different things; no ordering problem found.";

            t.Findings.Add(new Finding(Severity.Info, text, t.Mods(false)));
        }

        private static bool CanChangeSomething(ScannedPatch p) =>
            p.Kind is PatchKind.Transpiler or PatchKind.InnerPrefix or PatchKind.InnerPostfix
            || p.Caps.ReturnsBool || p.Caps.CanChangeResult || p.Caps.CanChangeArgs.Count > 0
            || p.Caps.UsesArgsArray || p.Caps.WritableFields.Count > 0 || p.Caps.ReturnsException;

        // ── helpers ──────────────────────────────────────────────────────────────────────────────────

        internal static string Ref(ScannedPatch p) => $"{p.RowLabel} ({p.Mod.Label})";

        internal static string Refs(IEnumerable<ScannedPatch> ps) => Join(ps.Select(Ref).ToList());

        internal static string Join(List<string> items) => items.Count switch
        {
            0 => string.Empty,
            1 => items[0],
            2 => items[0] + " and " + items[1],
            _ => string.Join(", ", items.Take(items.Count - 1)) + " and " + items[items.Count - 1]
        };

        private static string Plural(int n, string one, string many) => n == 1 ? one : many;

        private static List<ModInfo> DistinctMods(IEnumerable<ScannedPatch> ps)
        {
            List<ModInfo> result = [];
            foreach (ScannedPatch p in ps)
                if (!result.Contains(p.Mod))
                    result.Add(p.Mod);
            return result;
        }

        private static IEnumerable<ModInfo> Mods(ScannedPatch first, params IEnumerable<ScannedPatch>[] groups)
        {
            yield return first.Mod;
            foreach (IEnumerable<ScannedPatch> g in groups)
                foreach (ScannedPatch p in g)
                    yield return p.Mod;
        }
    }
}

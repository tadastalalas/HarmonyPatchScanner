using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using HarmonyPatchScanner.Model;
using HarmonyPatchScanner.Reporting;

namespace HarmonyPatchScanner.Analysis
{
    /// <summary>Builds the ScanModel: collects every patch Harmony knows about, orders and analyzes it.</summary>
    internal static class PatchCollector
    {
        internal static ScanModel Scan(ScanOptions options)
        {
            ScanModel model = new(options)
            {
                HarmonyVersion = typeof(Harmony).Assembly.GetName().Version ?? new Version(0, 0),
                OrderIsExact   = OrderResolver.IsExact
            };

            ModResolver mods = new();

            foreach (MethodBase original in Harmony.GetAllPatchedMethods().ToList())
            {
                try
                {
                    Patches? patches = Harmony.GetPatchInfo(original);
                    if (patches == null) continue;

                    (TargetOrigin origin, ModInfo? originMod) = mods.ResolveTarget(original.DeclaringType?.Assembly);
                    TargetReport target = new(original, Names.Key(original), Names.Method(original), Names.Namespace(original), origin, originMod)
                    {
                        IsLifecycle = FilterHelper.IsLifecycleMethod(original)
                    };

                    Add(target, mods, patches.Prefixes,       PatchKind.Prefix,       options, model.Errors);
                    Add(target, mods, patches.Postfixes,      PatchKind.Postfix,      options, model.Errors);
                    Add(target, mods, patches.Transpilers,    PatchKind.Transpiler,   options, model.Errors);
                    Add(target, mods, patches.Finalizers,     PatchKind.Finalizer,    options, model.Errors);
                    Add(target, mods, patches.InnerPrefixes,  PatchKind.InnerPrefix,  options, model.Errors);
                    Add(target, mods, patches.InnerPostfixes, PatchKind.InnerPostfix, options, model.Errors);

                    if (target.PatchCount > 0)
                        model.Targets.Add(target);
                }
                catch (Exception ex)
                {
                    model.Errors.Add($"{Describe(original)}: {ex.GetType().Name} — {ex.Message}");
                }
            }

            foreach (ScannedPatch p in model.Targets.SelectMany(t => t.All))
            {
                if (string.IsNullOrEmpty(p.HarmonyId)) continue;
                model.AllOwnerIds.Add(p.HarmonyId);
                p.Mod.HarmonyIds.Add(p.HarmonyId);
            }

            foreach (TargetReport target in model.Targets)
            {
                try
                {
                    Order(target);
                    Label(target);
                    HintAnalyzer.Analyze(target, model.AllOwnerIds);

                    foreach (ScannedPatch p in target.Prefixes.Where(p => p.Caps.ReturnsBool))
                        p.ReturnClass = options.AnalyzePrefixReturns ? PrefixReturnAnalyzer.Classify(p.Method) : PrefixReturn.Unknown;

                    if (options.DeepTranspilerAnalysis)
                        TranspilerDeltaAnalyzer.Analyze(target, model.Errors);

                    ConflictRules.Apply(target, options);
                }
                catch (Exception ex)
                {
                    model.Errors.Add($"{target.ShortName}: analysis failed — {ex.GetType().Name}: {ex.Message}");
                }
            }

            model.Targets.Sort((a, b) => string.CompareOrdinal(a.ShortName, b.ShortName));
            model.Mods.AddRange(mods.All.OrderBy(m => m.LauncherPosition ?? int.MaxValue).ThenBy(m => m.Label, StringComparer.OrdinalIgnoreCase));
            return model;
        }

        private static void Add(TargetReport target, ModResolver mods, IEnumerable<Patch> patches, PatchKind kind, ScanOptions options, List<string> errors)
        {
            foreach (Patch raw in patches)
            {
                if (raw == null) continue;

                MethodInfo? method = null;
                try
                {
                    method = raw.PatchMethod;
                }
                catch (Exception ex)
                {
                    errors.Add($"{target.ShortName}: {Names.KindSingular(kind)} by '{raw.owner}' — patch method could not be resolved ({ex.Message})");
                }
                if (method == null) continue;

                Assembly assembly = method.DeclaringType?.Assembly ?? method.Module.Assembly;
                ModInfo  mod      = mods.Resolve(assembly);
                ScannedPatch p    = new(raw, method, kind, target.Method, mod);

                if (kind is PatchKind.InnerPrefix or PatchKind.InnerPostfix)
                    ReadInnerMethod(p, raw);

                try
                {
                    p.Caps = CapabilityAnalyzer.Analyze(method, kind, p.InnerMethod ?? target.Method);
                }
                catch (Exception ex)
                {
                    errors.Add($"{target.ShortName}: could not analyze {Names.PatchMethod(method)} — {ex.Message}");
                }

                p.IsFiltered = options.ExcludeCommunityLibraries && mod.IsCommunityLibrary;
                target.ListFor(kind).Add(p);
            }
        }

        // Patch.innerMethod → InnerMethod { Method, positions }; member visibility is not part of the public contract, so use reflection.
        private static void ReadInnerMethod(ScannedPatch p, Patch raw)
        {
            try
            {
                object? inner = raw.innerMethod;
                if (inner == null) return;

                p.InnerMethod    = Member(inner, "Method") as MethodBase;
                p.InnerPositions = Member(inner, "positions") as int[] ?? [];
            }
            catch
            {
                // leave empty; the report shows the infix without its inner target
            }
        }

        private static object? Member(object obj, string name)
        {
            Type t = obj.GetType();
            FieldInfo? field = AccessTools.Field(t, name);
            if (field != null) return field.GetValue(obj);
            PropertyInfo? prop = AccessTools.Property(t, name);
            return prop?.GetValue(obj);
        }

        // Harmony sorts each kind separately. Postfixes additionally run in two passes: void ones first, then pass-through ones.
        private static void Order(TargetReport target)
        {
            bool exact = true;
            foreach (PatchKind kind in Enum.GetValues(typeof(PatchKind)))
            {
                List<ScannedPatch> list = target.ListFor(kind);
                if (list.Count <= 1) continue;

                Patch[] sorted = OrderResolver.Sort(list.Select(p => p.Raw).ToList(), out bool kindExact);
                exact &= kindExact;

                List<ScannedPatch> ordered = [];
                foreach (Patch raw in sorted)
                {
                    ScannedPatch? match = list.FirstOrDefault(p => ReferenceEquals(p.Raw, raw) && !ordered.Contains(p));
                    if (match != null) ordered.Add(match);
                }
                foreach (ScannedPatch p in list)
                    if (!ordered.Contains(p)) ordered.Add(p);

                if (kind == PatchKind.Postfix)
                    ordered = ordered.Where(p => !p.Caps.PassThrough).Concat(ordered.Where(p => p.Caps.PassThrough)).ToList();

                list.Clear();
                list.AddRange(ordered);
            }
            target.OrderIsExact = exact;
        }

        private static void Label(TargetReport target)
        {
            int n = 0;
            foreach (ScannedPatch p in target.Prefixes.Concat(target.Postfixes).Concat(target.Finalizers))
            {
                p.Slot     = ++n;
                p.RowLabel = "#" + n;
            }
            for (int i = 0; i < target.Transpilers.Count; i++)
            {
                target.Transpilers[i].Slot     = i + 1;
                target.Transpilers[i].RowLabel = "T" + (i + 1);
            }
            int k = 0;
            foreach (ScannedPatch p in target.InnerPrefixes.Concat(target.InnerPostfixes))
            {
                p.Slot     = ++k;
                p.RowLabel = "I" + k;
            }
        }

        private static string Describe(MethodBase m)
        {
            try { return Names.Method(m); }
            catch { return m.Name; }
        }
    }
}

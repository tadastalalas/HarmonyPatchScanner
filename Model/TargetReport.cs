using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace HarmonyPatchScanner.Model
{
    /// <summary>Everything the scanner knows about one patched method.</summary>
    internal sealed class TargetReport
    {
        internal MethodBase Method { get; }
        /// <summary>Unique per overload (Harmony FullDescription).</summary>
        internal string Key { get; }
        /// <summary>"Hero.get_Age()" — declaring type without namespace, parameters as short type names.</summary>
        internal string ShortName { get; }
        internal string Namespace { get; }
        internal TargetOrigin Origin { get; }
        /// <summary>When Origin is Mod: the mod whose code is being patched.</summary>
        internal ModInfo? OriginMod { get; }

        // All lists are in exact Harmony execution order (see OrderResolver).
        internal List<ScannedPatch> Prefixes      { get; } = [];
        internal List<ScannedPatch> Postfixes     { get; } = [];
        internal List<ScannedPatch> Transpilers   { get; } = [];
        internal List<ScannedPatch> Finalizers    { get; } = [];
        internal List<ScannedPatch> InnerPrefixes { get; } = [];
        internal List<ScannedPatch> InnerPostfixes{ get; } = [];

        internal List<Finding> Findings { get; } = [];

        /// <summary>False when PatchSorter could not be reached by reflection and priority/index sorting was used instead.</summary>
        internal bool OrderIsExact { get; set; } = true;

        /// <summary>Set when the lifecycle filter hides this target. Kept in the model so per-mod counts stay honest.</summary>
        internal bool IsLifecycle { get; set; }

        internal TargetReport(MethodBase method, string key, string shortName, string ns, TargetOrigin origin, ModInfo? originMod)
        {
            Method    = method;
            Key       = key;
            ShortName = shortName;
            Namespace = ns;
            Origin    = origin;
            OriginMod = originMod;
        }

        internal IEnumerable<ScannedPatch> All =>
            Transpilers.Concat(InnerPrefixes).Concat(InnerPostfixes).Concat(Prefixes).Concat(Postfixes).Concat(Finalizers);

        internal int PatchCount => Prefixes.Count + Postfixes.Count + Transpilers.Count + Finalizers.Count + InnerPrefixes.Count + InnerPostfixes.Count;

        internal List<ScannedPatch> ListFor(PatchKind kind) => kind switch
        {
            PatchKind.Prefix       => Prefixes,
            PatchKind.Postfix      => Postfixes,
            PatchKind.Transpiler   => Transpilers,
            PatchKind.Finalizer    => Finalizers,
            PatchKind.InnerPrefix  => InnerPrefixes,
            _                      => InnerPostfixes
        };

        internal Severity Severity => Findings.Count == 0 ? Severity.Info : Findings.Max(f => f.Severity);

        /// <summary>Distinct mods patching this method, filtered patches excluded when <paramref name="visibleOnly"/>.</summary>
        internal List<ModInfo> Mods(bool visibleOnly)
        {
            List<ModInfo> result = [];
            foreach (ScannedPatch p in All)
                if ((!visibleOnly || !p.IsFiltered) && !result.Contains(p.Mod))
                    result.Add(p.Mod);
            return result;
        }

        internal bool HasPatchFrom(ModInfo mod) => All.Any(p => p.Mod == mod);
    }
}

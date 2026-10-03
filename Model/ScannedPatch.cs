using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace HarmonyPatchScanner.Model
{
    internal sealed class HintRef
    {
        internal HintKind Kind { get; }
        internal string OwnerId { get; }
        internal HintStatus Status { get; set; } = HintStatus.OwnerNotLoaded;
        /// <summary>For CaseMismatch: the owner id that exists with different casing.</summary>
        internal string? Suggestion { get; set; }

        internal HintRef(HintKind kind, string ownerId)
        {
            Kind    = kind;
            OwnerId = ownerId;
        }
    }

    /// <summary>One Harmony patch on one target method, with everything the analyzers derived about it.</summary>
    internal sealed class ScannedPatch
    {
        internal Patch Raw { get; }
        internal MethodInfo Method { get; }
        internal PatchKind Kind { get; }
        internal MethodBase Target { get; }
        internal ModInfo Mod { get; }

        internal string HarmonyId => Raw.owner ?? string.Empty;
        internal int Priority     => Raw.priority;
        internal int Index        => Raw.index;
        internal bool Debug       => Raw.debug;

        internal PatchCapabilities Caps { get; set; } = new();
        internal PrefixReturn ReturnClass { get; set; } = PrefixReturn.NotBool;
        internal List<HintRef> Hints { get; } = [];

        /// <summary>
        /// Position in the execution order of its kind on this target (1-based). Postfixes are numbered
        /// across both groups (void first, then pass-through). Assigned by the collector.
        /// </summary>
        internal int Slot { get; set; }

        /// <summary>Row label used in reports: "1", "2" for runtime patches, "T1" for transpilers, "I1" for infixes.</summary>
        internal string RowLabel { get; set; } = string.Empty;

        /// <summary>Infix only: the inner call this patch wraps, and the call positions it applies to (empty = all).</summary>
        internal MethodBase? InnerMethod { get; set; }
        internal int[] InnerPositions { get; set; } = [];

        /// <summary>Transpiler only, when deep analysis is enabled.</summary>
        internal TranspilerDelta? Delta { get; set; }

        /// <summary>Issues with this patch on its own (not conflicts). Shown in the module report.</summary>
        internal List<Finding> Lints { get; } = [];

        /// <summary>Hidden by a filter (community library). Still participates in order and conflict analysis.</summary>
        internal bool IsFiltered { get; set; }

        internal ScannedPatch(Patch raw, MethodInfo method, PatchKind kind, MethodBase target, ModInfo mod)
        {
            Raw    = raw;
            Method = method;
            Kind   = kind;
            Target = target;
            Mod    = mod;
        }

        internal bool IsRuntimePatch => Kind is PatchKind.Prefix or PatchKind.Postfix or PatchKind.Finalizer;
    }
}

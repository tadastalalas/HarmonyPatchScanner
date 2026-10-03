using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyPatchScanner.Model;

namespace HarmonyPatchScanner.Analysis
{
    /// <summary>Resolves [HarmonyBefore]/[HarmonyAfter] ids the way PatchSorter binds them and detects cycles.</summary>
    internal static class HintAnalyzer
    {
        internal static void Analyze(TargetReport target, HashSet<string> allOwnerIds)
        {
            foreach (PatchKind kind in Enum.GetValues(typeof(PatchKind)))
            {
                List<ScannedPatch> list = target.ListFor(kind);
                if (list.Count == 0) continue;

                foreach (ScannedPatch p in list)
                {
                    p.Hints.Clear();
                    foreach (string id in p.Raw.before ?? [])
                        p.Hints.Add(Resolve(new HintRef(HintKind.Before, id), target, list, allOwnerIds));
                    foreach (string id in p.Raw.after ?? [])
                        p.Hints.Add(Resolve(new HintRef(HintKind.After, id), target, list, allOwnerIds));
                }

                MarkCycles(list);
            }
        }

        private static HintRef Resolve(HintRef hint, TargetReport target, List<ScannedPatch> sameKind, HashSet<string> allOwnerIds)
        {
            // PatchSorter: node.before.Contains(x.owner) — ordinal, same list only.
            if (sameKind.Any(q => string.Equals(q.HarmonyId, hint.OwnerId, StringComparison.Ordinal)))
                hint.Status = HintStatus.Bound;
            else if (target.All.Any(q => string.Equals(q.HarmonyId, hint.OwnerId, StringComparison.Ordinal)))
                hint.Status = HintStatus.OwnerHasOtherKindHere;
            else if (allOwnerIds.Contains(hint.OwnerId))
                hint.Status = HintStatus.OwnerLoadedNotHere;
            else
            {
                string? similar = allOwnerIds.FirstOrDefault(o => string.Equals(o, hint.OwnerId, StringComparison.OrdinalIgnoreCase));
                if (similar != null)
                {
                    hint.Status     = HintStatus.CaseMismatch;
                    hint.Suggestion = similar;
                }
                else
                    hint.Status = HintStatus.OwnerNotLoaded;
            }
            return hint;
        }

        // Edge a→b means "a must run before b". A hint is cyclic when its edge lies on a directed cycle
        // (including a patch referencing its own owner id). Harmony culls such edges silently.
        private static void MarkCycles(List<ScannedPatch> list)
        {
            List<(ScannedPatch From, ScannedPatch To, HintRef Hint)> edges = [];
            foreach (ScannedPatch p in list)
            {
                foreach (HintRef h in p.Hints)
                {
                    if (h.Status != HintStatus.Bound) continue;
                    foreach (ScannedPatch q in list)
                    {
                        if (!string.Equals(q.HarmonyId, h.OwnerId, StringComparison.Ordinal)) continue;
                        if (h.Kind == HintKind.Before) edges.Add((p, q, h));
                        else                           edges.Add((q, p, h));
                    }
                }
            }

            if (edges.Count == 0) return;

            Dictionary<ScannedPatch, List<ScannedPatch>> adjacency = [];
            foreach ((ScannedPatch from, ScannedPatch to, HintRef _) in edges)
            {
                if (!adjacency.TryGetValue(from, out List<ScannedPatch>? next))
                    adjacency[from] = next = [];
                next.Add(to);
            }

            foreach ((ScannedPatch from, ScannedPatch to, HintRef hint) in edges)
                if (from == to || Reaches(adjacency, to, from))
                    hint.Status = HintStatus.Cyclic;
        }

        private static bool Reaches(Dictionary<ScannedPatch, List<ScannedPatch>> adjacency, ScannedPatch start, ScannedPatch goal)
        {
            HashSet<ScannedPatch> seen = [];
            Stack<ScannedPatch> stack = new();
            stack.Push(start);
            while (stack.Count > 0)
            {
                ScannedPatch node = stack.Pop();
                if (node == goal) return true;
                if (!seen.Add(node) || !adjacency.TryGetValue(node, out List<ScannedPatch>? next)) continue;
                for (int i = 0; i < next.Count; i++)
                    stack.Push(next[i]);
            }
            return false;
        }
    }
}

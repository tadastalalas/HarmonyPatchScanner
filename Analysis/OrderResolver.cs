using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;

namespace HarmonyPatchScanner.Analysis
{
    /// <summary>
    /// Produces the exact order Harmony runs patches of one kind on one method by invoking Harmony's own
    /// internal PatchSorter (priority desc → index asc, then [HarmonyBefore]/[HarmonyAfter] as hard constraints,
    /// cycles broken silently). Falls back to priority/index when the internal API is not reachable.
    /// </summary>
    internal static class OrderResolver
    {
        private static readonly ConstructorInfo? Ctor;
        private static readonly MethodInfo?      SortMethod;

        static OrderResolver()
        {
            try
            {
                Type? sorter = typeof(Harmony).Assembly.GetType("HarmonyLib.PatchSorter");
                if (sorter == null) return;

                const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                Ctor       = sorter.GetConstructor(flags, null, [typeof(Patch[]), typeof(bool)], null);
                SortMethod = sorter.GetMethod("Sort", flags, null, Type.EmptyTypes, null);

                if (SortMethod != null && SortMethod.ReturnType != typeof(Patch[]))
                    SortMethod = null;
            }
            catch
            {
                Ctor       = null;
                SortMethod = null;
            }
        }

        internal static bool IsExact => Ctor != null && SortMethod != null;

        /// <summary>Returns the patches in execution order. <paramref name="exact"/> tells whether Harmony's sorter was used.</summary>
        internal static Patch[] Sort(IList<Patch> patches, out bool exact)
        {
            Patch[] input = patches.ToArray();
            if (input.Length <= 1)
            {
                exact = true;
                return input;
            }

            if (IsExact)
            {
                try
                {
                    object sorter = Ctor!.Invoke([input, false]);
                    if (SortMethod!.Invoke(sorter, null) is Patch[] sorted && sorted.Length == input.Length)
                    {
                        exact = true;
                        return sorted;
                    }
                }
                catch
                {
                    // fall through to the approximation
                }
            }

            exact = false;
            return input.OrderByDescending(p => p.priority).ThenBy(p => p.index).ToArray();
        }
    }
}

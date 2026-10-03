namespace HarmonyPatchScanner.Reporting
{
    /// <summary>Reference text appended to every report. Statements follow Harmony 2.4.2's MethodCreator and PatchSorter.</summary>
    internal static class Glossary
    {
        internal static void Write(ReportWriter w)
        {
            w.Section("How to read this report (Harmony 2.4.2)");

            Para(w, "Order within one kind.",
                "Harmony sorts prefixes, postfixes, transpilers and finalizers separately, each the same way: higher priority first, " +
                "equal priority → lower Harmony index first. The index is the registration order on that method (the mod whose PatchAll ran " +
                "earlier gets the lower index), so the launcher position only matters indirectly, through the index.");

            Para(w, "[HarmonyBefore] / [HarmonyAfter].",
                "Hard constraints, not tie-breakers: a patch waits until every patch it must run after has been placed, even if its own " +
                "priority is higher. They bind only among patches of the same kind on the same method, by the exact (case-sensitive) Harmony id. " +
                "Circular constraints are broken silently. Ids that match nothing are ignored.");

            Para(w, "Prefixes and skipping.",
                "A bool prefix that returns false skips the original method and every later prefix that \"affects the original\". A prefix " +
                "affects the original if it returns bool, or has any ref/out parameter, or has any parameter of a reference type (classes, " +
                "strings, arrays, __args) other than __instance, __originalMethod and __state. Prefixes with only value-type parameters and a " +
                "void return always run. Once skipping starts it cannot be undone by a later prefix.");

            Para(w, "Postfixes.",
                "Always run, even when the original was skipped. Void postfixes run first (in sorted order), then pass-through postfixes " +
                "(non-void, first parameter of the return type) whose return value replaces __result. A postfix can tell whether the original " +
                "ran by taking bool __runOriginal.");

            Para(w, "Finalizers.",
                "Wrap everything in try/catch and always run. A finalizer returning Exception decides what is thrown: returning null swallows " +
                "the exception, returning another exception replaces it. With several such finalizers the last one to run wins. If all " +
                "finalizers are void, the original exception is rethrown.");

            Para(w, "Transpilers.",
                "Run once when the method is patched (and again whenever anyone patches the method later), each receiving the previous " +
                "transpiler's output. A transpiler whose anchor instructions were removed by an earlier transpiler or by a game update " +
                "typically returns the input unchanged — the mod's feature silently disappears. A bool prefix returning false bypasses the " +
                "whole rewritten body.");

            Para(w, "Infixes (inner prefix / inner postfix).",
                "Wrap a specific call inside the method body. If no such call exists after transpilers ran, the infix attaches to nothing.");

            Para(w, "__state.",
                "Shared between a prefix and a postfix/finalizer only if both are declared in the same class.");

            Para(w, "Deep transpiler analysis.",
                "Re-applies the chain with Harmony's PatchProcessor.GetCurrentInstructions(method, k) for k = 0..n and attributes the " +
                "difference to transpiler k. Label-only changes are invisible to this comparison. A transpiler that keeps state and refuses to " +
                "run twice shows as \"no effect\".");

            Para(w, "Prefix return analysis.",
                "Reads the prefix's IL and classifies each return statement: constant true/false, or a computed value (call, comparison, " +
                "field, argument). \"TRUE or FALSE\" means the value depends on runtime state.");

            Para(w, "Severity.",
                "HIGH: something is skipped, overwritten or broken for sure. MEDIUM: correct only if the current order is the intended one, " +
                "or a mod's own patch is unlikely to do what its author meant. LOW: worth knowing, no interference found.");

            Para(w, "What this scan cannot see.",
                "Patches applied later (e.g. on campaign start or in missions) if the scan ran before that point; reverse patches; " +
                "whether a patch actually writes to a ref parameter it declares.");
        }

        private static void Para(ReportWriter w, string term, string text)
        {
            w.Wrap("  " + term + " ", "  ", text);
            w.Blank();
        }
    }
}

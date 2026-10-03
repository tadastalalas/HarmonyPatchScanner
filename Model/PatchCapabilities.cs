using System.Collections.Generic;

namespace HarmonyPatchScanner.Model
{
    /// <summary>
    /// What a patch method *can* do, derived from its signature the same way Harmony's
    /// MethodCreator interprets it. "Can change" means the parameter is ref/out — whether the
    /// patch actually writes to it is not known without reading its code.
    /// </summary>
    internal sealed class PatchCapabilities
    {
        internal bool IsVoid        { get; set; }
        internal bool ReturnsBool   { get; set; }

        /// <summary>Prefix only. Mirrors MethodCreatorTools.AffectsOriginal: such prefixes are skipped once an earlier prefix returned false.</summary>
        internal bool AffectsOriginal { get; set; }
        /// <summary>Why AffectsOriginal is true, e.g. "returns bool", "ref parameter 'hero'", "reference-type parameter 'hero'".</summary>
        internal string AffectsReason { get; set; } = string.Empty;

        internal bool ReadsResult      { get; set; }
        internal bool CanChangeResult  { get; set; }
        /// <summary>Postfix with a non-void return whose first parameter has the same type: its return value replaces __result.</summary>
        internal bool PassThrough      { get; set; }

        internal bool UsesInstance     { get; set; }
        internal bool UsesArgsArray    { get; set; }
        internal bool UsesRunOriginal  { get; set; }
        internal bool UsesState        { get; set; }
        internal bool StateIsWritable  { get; set; }

        /// <summary>Finalizer only: non-void return value is stored as the exception to (re)throw; null suppresses it.</summary>
        internal bool ReturnsException   { get; set; }
        internal bool ObservesException  { get; set; }

        /// <summary>Transpiler only.</summary>
        internal bool TakesGenerator { get; set; }
        internal bool TakesOriginal  { get; set; }

        internal List<string> ReadArgs      { get; } = [];
        internal List<string> CanChangeArgs { get; } = [];
        internal List<string> Fields        { get; } = [];
        internal List<string> WritableFields { get; } = [];

        /// <summary>Patch method returns DynamicMethod/MethodInfo from a MethodBase: Harmony calls it to obtain the real patch at patch time.</summary>
        internal bool IsFactory { get; set; }

        /// <summary>Signature problems that Harmony would have rejected or that make no sense; informational.</summary>
        internal List<string> Notes { get; } = [];
    }
}

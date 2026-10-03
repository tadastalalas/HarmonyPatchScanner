using System.Collections.Generic;

namespace HarmonyPatchScanner.Model
{
    /// <summary>
    /// What one transpiler changed when Harmony's transpiler chain was re-applied up to and including it
    /// (PatchProcessor.GetCurrentInstructions with maxTranspilers = position). Multiset comparison of
    /// normalized instructions: label moves are invisible, everything else is counted.
    /// </summary>
    internal sealed class TranspilerDelta
    {
        internal int Added   { get; set; }
        internal int Removed { get; set; }
        internal bool Threw  { get; set; }
        /// <summary>An earlier transpiler in the chain threw, so this one could not be re-applied.</summary>
        internal bool Skipped { get; set; }
        internal string? Error { get; set; }

        /// <summary>Members (methods/fields) that appear in added or removed instructions — what the transpiler works around.</summary>
        internal HashSet<string> TouchedMembers { get; } = [];

        internal bool NoEffect => !Threw && !Skipped && Added == 0 && Removed == 0;
    }
}

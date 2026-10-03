using System.Collections.Generic;

namespace HarmonyPatchScanner.Model
{
    /// <summary>A problem or noteworthy fact about a target method or a single patch.</summary>
    internal sealed class Finding
    {
        internal Severity Severity { get; }
        /// <summary>One sentence, plain language. Row labels (#1, T2) refer to the execution flow of the same target.</summary>
        internal string Text { get; }
        /// <summary>Mods this finding is about; used by the module report to decide relevance.</summary>
        internal HashSet<ModInfo> Mods { get; } = [];

        internal Finding(Severity severity, string text, params ModInfo[] mods)
        {
            Severity = severity;
            Text     = text;
            foreach (ModInfo mod in mods)
                Mods.Add(mod);
        }

        internal Finding(Severity severity, string text, IEnumerable<ModInfo> mods)
        {
            Severity = severity;
            Text     = text;
            foreach (ModInfo mod in mods)
                Mods.Add(mod);
        }
    }
}

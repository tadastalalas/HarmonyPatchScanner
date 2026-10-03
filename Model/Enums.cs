namespace HarmonyPatchScanner.Model
{
    internal enum PatchKind
    {
        Prefix,
        Postfix,
        Transpiler,
        Finalizer,
        InnerPrefix,
        InnerPostfix
    }

    internal enum Severity
    {
        Info   = 0,
        Low    = 1,
        Medium = 2,
        High   = 3
    }

    /// <summary>What a prefix's bool return value looks like after reading its IL.</summary>
    internal enum PrefixReturn
    {
        NotBool,
        AlwaysTrue,
        AlwaysFalse,
        Conditional,
        Unknown
    }

    internal enum HintKind
    {
        Before,
        After
    }

    /// <summary>
    /// How Harmony's PatchSorter treats one [HarmonyBefore]/[HarmonyAfter] id.
    /// Binding happens only among patches of the same kind on the same method, by exact owner string.
    /// </summary>
    internal enum HintStatus
    {
        Bound,
        Cyclic,
        OwnerHasOtherKindHere,
        OwnerLoadedNotHere,
        OwnerNotLoaded,
        CaseMismatch
    }

    internal enum TargetOrigin
    {
        Official,
        Framework,
        Mod,
        Unknown
    }
}

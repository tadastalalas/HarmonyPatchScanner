namespace HarmonyPatchScanner
{
    /// <summary>
    /// Wrapper for MCM Dropdown items. MCM calls ToString() on each item
    /// to render the dropdown label — same pattern used by Dramalord's HeroWrapper.
    /// </summary>
    internal class ModuleWrapper
    {
        /// <summary>Module Id (e.g. "MyMod.Module"). Null for the placeholder entry.</summary>
        internal string? ModuleId { get; }

        /// <summary>Human-readable module name shown in the launcher.</summary>
        internal string ModuleName { get; }

        internal ModuleWrapper(string? moduleId, string moduleName)
        {
            ModuleId   = moduleId;
            ModuleName = moduleName;
        }

        public override string ToString()
        {
            if (ModuleId == null)
                return ModuleName; // placeholder "-- Select a module --"

            return $"{ModuleName} ({ModuleId})";
        }

        public override int GetHashCode()
        {
            return (ModuleId ?? ModuleName).GetHashCode();
        }
    }
}
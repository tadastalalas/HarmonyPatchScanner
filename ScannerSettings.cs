using System;
using MCM.Abstractions.Attributes;
using MCM.Abstractions.Attributes.v2;
using MCM.Abstractions.Base.Global;

namespace HarmonyPatchScanner
{
    internal class ScannerSettings : AttributeGlobalSettings<ScannerSettings>
    {
        public override string Id => "HarmonyPatchScannerSettings";
        public override string DisplayName => "Harmony Patch Scanner";
        public override string FolderName => "HarmonyPatchScanner";
        public override string FormatType => "json2";

        [SettingPropertyBool("Exclude Common Lifecycle Methods", Order = 0, RequireRestart = false,
            HintText = "Exclude common mod lifecycle method patches (OnSubModuleLoad, OnGameStart, etc.) that are typically used for initialization.")]
        [SettingPropertyGroup("Filters")]
        public bool ExcludeCommonLifecycleMethods { get; set; } = true;

        [SettingPropertyBool("Exclude Community Libraries", Order = 1, RequireRestart = false,
            HintText = "Exclude patches from common community libraries that are present in almost every mod list: Harmony, BetterExceptionWindow, ButterLib, UIExtenderEx, and Mod Configuration Menu v5. Their internal patches are rarely relevant when debugging your own mod.")]
        [SettingPropertyGroup("Filters")]
        public bool ExcludeCommunityLibraries { get; set; } = true;

        [SettingPropertyButton("Scan Harmony Patches", Content = "Scan Now", Order = 1, RequireRestart = false,
            HintText = "Scan all mods for Harmony patches.")]
        [SettingPropertyGroup("Actions")]
        public Action ScanPatches { get; set; } = PatchScanner.ScanAndLog;

        [SettingPropertyButton("Find Duplicate Patches", Content = "Find Conflicts", Order = 2, RequireRestart = false,
            HintText = "Find methods with multiple patches that might conflict.")]
        [SettingPropertyGroup("Actions")]
        public Action FindDuplicates { get; set; } = ConflictScanner.FindDuplicatePatches;
    }
}
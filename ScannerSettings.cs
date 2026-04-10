using System;
using System.Collections.Generic;
using MCM.Abstractions.Attributes;
using MCM.Abstractions.Attributes.v2;
using MCM.Abstractions.Base.Global;
using MCM.Common;

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

        private static Dropdown<ModuleWrapper> _moduleDropdown = new Dropdown<ModuleWrapper>(
            new[] { new ModuleWrapper(null, "-- Select a module --") }, 0);

        [SettingPropertyDropdown("Select Module", Order = 0, RequireRestart = false,
            HintText = "Select a loaded mod module to scan its Harmony patches in isolation.")]
        [SettingPropertyGroup("Module Scanner")]
        public Dropdown<ModuleWrapper> SelectedModule
        {
            get => _moduleDropdown;
            set => _moduleDropdown = value;
        }

        [SettingPropertyButton("Scan Selected Module", Content = "Scan Module", Order = 1, RequireRestart = false,
            HintText = "Scan all Harmony patches made by the selected module and find its conflicts with other mods. Results are saved to a separate log file.")]
        [SettingPropertyGroup("Module Scanner")]
        public Action ScanSelectedModule { get; set; } = ModuleScanner.ScanSelectedModule;

        /// <summary>
        /// Rebuilds the dropdown with the current list of custom (non-official,
        /// non-community-library) modules. Called after ModuleLoadOrderHelper.Build().
        /// </summary>
        internal void RefreshModuleDropdown()
        {
            var customModules = ModuleLoadOrderHelper.GetCustomModules();

            var items = new List<ModuleWrapper> { new ModuleWrapper(null, "-- Select a module --") };
            foreach (var (moduleId, moduleName) in customModules)
                items.Add(new ModuleWrapper(moduleId, moduleName));

            _moduleDropdown = new Dropdown<ModuleWrapper>(items, 0);
        }

        /// <summary>
        /// Returns the module Id from the currently selected dropdown entry,
        /// or null if the placeholder is selected.
        /// </summary>
        internal string? GetSelectedModuleId()
        {
            return _moduleDropdown?.SelectedValue?.ModuleId;
        }
    }
}
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
            HintText = "Hide patches on SubModule lifecycle hooks (OnSubModuleLoad, OnGameStart, OnApplicationTick, ...). Libraries patch these on every mod's SubModule class, which only adds noise.")]
        [SettingPropertyGroup("Filters")]
        public bool ExcludeCommonLifecycleMethods { get; set; } = true;

        [SettingPropertyBool("Exclude Community Libraries", Order = 1, RequireRestart = false,
            HintText = "Hide patches from Harmony, BetterExceptionWindow, ButterLib, UIExtenderEx and Mod Configuration Menu v5. They still appear inside a method's execution flow when that method is also patched by your mods, so the order shown stays truthful.")]
        [SettingPropertyGroup("Filters")]
        public bool ExcludeCommunityLibraries { get; set; } = true;

        [SettingPropertyBool("Analyze Prefix Return Values", Order = 0, RequireRestart = false,
            HintText = "Read the IL of every bool prefix to tell whether it always returns true, always returns false, or decides at runtime. Read-only; nothing is executed.")]
        [SettingPropertyGroup("Analysis")]
        public bool AnalyzePrefixReturns { get; set; } = true;

        [SettingPropertyBool("Deep Transpiler Analysis", Order = 1, RequireRestart = false,
            HintText = "Re-apply each method's transpiler chain step by step to see what every transpiler really changes and to catch transpilers that change nothing (missing anchor) or throw. This executes mod transpiler code again; well-written transpilers are side-effect free, badly written ones may log or misbehave.")]
        [SettingPropertyGroup("Analysis")]
        public bool DeepTranspilerAnalysis { get; set; } = false;

        [SettingPropertyButton("Scan Harmony Patches", Content = "Scan Now", Order = 1, RequireRestart = false,
            HintText = "List every Harmony patch of every mod, grouped by mod, with what each patch can do. Saved to Modules/HarmonyPatchScanner/logs/<mainmenu|campaign|mission>/AllHarmonyPatches.txt depending on where you run it, so you can keep one scan per game state.")]
        [SettingPropertyGroup("Actions")]
        public Action ScanPatches { get; set; } = PatchScanner.ScanAndLog;

        [SettingPropertyButton("Find Duplicate Patches", Content = "Find Conflicts", Order = 2, RequireRestart = false,
            HintText = "Find methods patched by more than one mod, show the exact execution order and explain what interferes. Saved to logs/<mainmenu|campaign|mission>/DuplicateHarmonyPatches.txt.")]
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
            HintText = "Everything about one mod: its patches, the methods it shares with other mods (with your patches marked), and problems in its own patches. Saved to logs/<mainmenu|campaign|mission>/ModuleScan_<name>.txt.")]
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
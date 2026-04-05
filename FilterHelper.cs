using System.Collections.Generic;

namespace HarmonyPatchScanner
{
    public static class FilterHelper
    {
        private static readonly HashSet<string> CommonLifecycleMethods = new HashSet<string>
        {
            // SubModule lifecycle methods
            "OnSubModuleLoadPostfix",
            "OnSubModuleLoad",
            "OnSubModuleUnloadedPostfix",
            "OnSubModuleUnloaded",
            "RegisterSubModuleObjectsPostfix",
            "RegisterSubModuleObjects",
            "AfterRegisterSubModuleObjectsPostfix",
            "AfterRegisterSubModuleObjects",

            // Game lifecycle methods
            "OnGameStartPostfix",
            "OnGameStart",
            "OnGameLoadedPostfix",
            "OnGameLoaded",
            "OnGameEndPostfix",
            "OnGameEnd",
            "OnGameInitializationFinishedPostfix",
            "OnGameInitializationFinished",
            "OnAfterGameInitializationFinishedPostfix",
            "OnAfterGameInitializationFinished",
            "InitializeGameStarterPostfix",
            "InitializeGameStarter",
            "DoLoadingPostfix",
            "DoLoading",

            // Campaign lifecycle methods
            "OnCampaignStartPostfix",
            "OnCampaignStart",
            "BeginGameStartPostfix",
            "BeginGameStart",
            "OnNewGameCreatedPostfix",
            "OnNewGameCreated",

            // Mission lifecycle methods
            "OnBeforeMissionBehaviourInitializePostfix",
            "OnBeforeMissionBehaviourInitialize",
            "OnMissionBehaviourInitializePostfix",
            "OnMissionBehaviourInitialize",

            // Application/Screen lifecycle methods
            "OnApplicationTickPostfix",
            "OnApplicationTick",
            "OnBeforeInitialModuleScreenSetAsRootPostfix",
            "OnBeforeInitialModuleScreenSetAsRoot",
            "AfterAsyncTickTickPostfix",
            "AfterAsyncTickTick",

            // Multiplayer lifecycle methods
            "OnMultiplayerGameStartPostfix",
            "OnMultiplayerGameStart",

            // Configuration lifecycle methods
            "OnConfigChangedPostfix",
            "OnConfigChanged",

            // Initial state methods
            "OnInitialStatePostfix",
            "OnInitialState"
        };

        /// <summary>
        /// Community library module IDs that are present in almost every mod list
        /// but whose internal patches are rarely relevant to a developer debugging
        /// their own mod's conflicts.
        /// </summary>
        private static readonly HashSet<string> CommunityLibraryModuleIds = new HashSet<string>(
            System.StringComparer.OrdinalIgnoreCase)
        {
            "Bannerlord.Harmony",           // Harmony
            "BetterExceptionWindow",        // Better Exception Window
            "Bannerlord.ButterLib",         // ButterLib
            "Bannerlord.UIExtenderEx",      // UIExtenderEx
            "Bannerlord.MBOptionScreen",    // Mod Configuration Menu v5
        };

        public static bool ShouldExcludeMethod(string methodName, bool excludeCommonLifecycle)
        {
            if (!excludeCommonLifecycle)
                return false;

            return CommonLifecycleMethods.Contains(methodName);
        }

        public static bool ShouldExcludePatchMethod(string patchMethodName, bool excludeCommonLifecycle)
        {
            if (!excludeCommonLifecycle)
                return false;

            return CommonLifecycleMethods.Contains(patchMethodName);
        }

        /// <summary>
        /// Returns true if the given module Id is a well-known community library
        /// whose patches should be hidden when the user enables that filter.
        /// </summary>
        public static bool IsCommunityLibrary(string? moduleId)
        {
            if (string.IsNullOrEmpty(moduleId)) return false;
            return CommunityLibraryModuleIds.Contains(moduleId);
        }

        /// <summary>
        /// Returns true if the patch should be excluded because its owning assembly
        /// belongs to a community library module and the filter is enabled.
        /// </summary>
        public static bool ShouldExcludeCommunityLibrary(string? patchAssemblyName, bool excludeCommunityLibraries)
        {
            if (!excludeCommunityLibraries || string.IsNullOrEmpty(patchAssemblyName))
                return false;

            var moduleId = ModuleLoadOrderHelper.GetModuleId(patchAssemblyName);
            return IsCommunityLibrary(moduleId);
        }
    }
}
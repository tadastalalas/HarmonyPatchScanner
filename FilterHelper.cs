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

            // Check if the patch method name matches any lifecycle method
            return CommonLifecycleMethods.Contains(patchMethodName);
        }
    }
}
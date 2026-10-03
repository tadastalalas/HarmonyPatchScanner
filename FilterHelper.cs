using System;
using System.Collections.Generic;
using System.Reflection;
using TaleWorlds.MountAndBlade;

namespace HarmonyPatchScanner
{
    internal static class FilterHelper
    {
        // Lifecycle virtuals on MBSubModuleBase/Module that libraries patch on every mod's SubModule class.
        // Both spellings of the mission hooks are kept because the game renamed them.
        private static readonly HashSet<string> LifecycleMethodNames = new(StringComparer.Ordinal)
        {
            "OnSubModuleLoad",
            "OnSubModuleUnloaded",
            "RegisterSubModuleObjects",
            "AfterRegisterSubModuleObjects",
            "OnGameStart",
            "OnGameLoaded",
            "OnGameEnd",
            "OnGameInitializationFinished",
            "OnAfterGameInitializationFinished",
            "InitializeGameStarter",
            "DoLoading",
            "OnCampaignStart",
            "BeginGameStart",
            "OnNewGameCreated",
            "OnBeforeMissionBehaviourInitialize",
            "OnBeforeMissionBehaviorInitialize",
            "OnMissionBehaviourInitialize",
            "OnMissionBehaviorInitialize",
            "OnApplicationTick",
            "OnBeforeInitialModuleScreenSetAsRoot",
            "AfterAsyncTickTick",
            "OnMultiplayerGameStart",
            "OnConfigChanged",
            "OnInitialState"
        };

        /// <summary>
        /// Community library module IDs that are present in almost every mod list
        /// but whose internal patches are rarely relevant to a developer debugging
        /// their own mod's conflicts.
        /// </summary>
        private static readonly HashSet<string> CommunityLibraryModuleIds = new(StringComparer.OrdinalIgnoreCase)
        {
            "Bannerlord.Harmony",           // Harmony
            "BetterExceptionWindow",        // Better Exception Window
            "Bannerlord.ButterLib",         // ButterLib
            "Bannerlord.UIExtenderEx",      // UIExtenderEx
            "Bannerlord.MBOptionScreen",    // Mod Configuration Menu v5
        };

        /// <summary>
        /// True for lifecycle hooks declared on a SubModule class or on TaleWorlds' Module.
        /// Matching by name alone would also hide unrelated methods such as Game.OnGameStart.
        /// </summary>
        internal static bool IsLifecycleMethod(MethodBase method)
        {
            if (!LifecycleMethodNames.Contains(method.Name)) return false;

            Type? declaring = method.DeclaringType;
            if (declaring == null) return false;

            return typeof(MBSubModuleBase).IsAssignableFrom(declaring) || declaring == typeof(TaleWorlds.MountAndBlade.Module);
        }

        /// <summary>
        /// Returns true if the given module Id is a well-known community library
        /// whose patches should be hidden when the user enables that filter.
        /// </summary>
        internal static bool IsCommunityLibrary(string? moduleId) =>
            !string.IsNullOrEmpty(moduleId) && CommunityLibraryModuleIds.Contains(moduleId!);
    }
}

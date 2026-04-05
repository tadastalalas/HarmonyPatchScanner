using HarmonyLib;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;

namespace HarmonyPatchScanner
{
    public class SubModule : MBSubModuleBase
    {
        protected override void OnSubModuleLoad()
        {
            base.OnSubModuleLoad();
            var harmony = new Harmony("HarmonyPatchScannerPatch");
            harmony.PatchAll();
        }

        protected override void OnBeforeInitialModuleScreenSetAsRoot()
        {
            base.OnBeforeInitialModuleScreenSetAsRoot();

            // All SubModules have had OnSubModuleLoad() called by this point,
            // meaning all harmony.PatchAll() calls are complete.
            // Utilities.GetModulesNames() returns module IDs in launcher sort order —
            // the same array the game passed to ModuleHelper.InitializeModules().
            ModuleLoadOrderHelper.Build();
        }

        protected override void OnGameStart(Game game, IGameStarter gameStarter)
        {
            base.OnGameStart(game, gameStarter);
        }
    }
}
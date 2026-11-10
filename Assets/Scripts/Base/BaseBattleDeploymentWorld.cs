using System;
using BaseConquest.Battle;
using UnityEngine;
using UnityEngine.InputSystem;

namespace BaseConquest.Base
{
    public sealed partial class BaseSceneController
    {
        private BaseBattlePreparation deploymentDraft;
        private BattleSceneController deploymentController;
        private bool deploymentChangedMap;
        private float deploymentReturnZoom;
        internal bool DeploymentWorldActive => deploymentController != null;

        internal void BeginDeploymentWorld(BaseBattlePreparation draft, Action back, Action exit)
        {
            if (deploymentDraft == draft && deploymentController != null)
            {
                ResumeDeploymentWorld();
                return;
            }
            EndDeploymentWorld();
            deploymentReturnZoom = WorldCamera.orthographicSize;
            try
            {
                if (draft.Route == BasePrepRoute.Ruins && BaseSceneRules.Entrance(State))
                {
                    // Build the actual encounter map without publishing the pending portal transition.
                    var exploration = State;
                    var encounterMap = exploration.Copy();
                    BaseSceneRules.EnterDungeon(encounterMap);
                    deploymentChangedMap = true;
                    try { State = encounterMap; RebuildRegion(); }
                    finally { State = exploration; }
                }

                deploymentDraft = draft;
                player.gameObject.SetActive(false);
                foreach (var pair in companionViews) pair.Value.gameObject.SetActive(false);

                var host = new GameObject("BattleOnCurrentMap");
                host.SetActive(false);
                host.transform.SetParent(transform, false);
                deploymentController = host.AddComponent<BattleSceneController>();
                deploymentController.UseCurrentMap(regionView, MapSession);
                deploymentController.ConfigureDeploymentDraft(draft, () => State, back, exit, () => BeginPreparedBattle(draft));
                host.SetActive(true);
                if (Application.isPlaying && (deploymentController.PreviewSimulation == null || deploymentController.PreviewHud == null))
                    throw new InvalidOperationException("战斗部署界面未能启动");
            }
            catch
            {
                EndDeploymentWorld();
                throw;
            }
        }

        internal void ShowDeploymentBriefing()
        {
            if (!DeploymentWorldActive) return;
            deploymentController.ExternalDialogue = true;
            Hud.SetVisible(true);
        }

        internal void ResumeDeploymentWorld(string problem = null)
        {
            if (!DeploymentWorldActive) return;
            deploymentController.ExternalDialogue = false;
            deploymentController.RefreshDeploymentDraft(problem);
            Hud.HideForBattleDeployment();
        }

        private BattleSceneController TakeDeploymentController()
        {
            var controller = deploymentController;
            deploymentController = null;
            deploymentDraft = null;
            deploymentChangedMap = false;
            return controller;
        }

        internal void EndDeploymentWorld(bool restore = true)
        {
            if (deploymentController == null && !deploymentChangedMap && deploymentDraft == null) return;
            bool rebuildMap = deploymentChangedMap;
            var controller = TakeDeploymentController();
            if (controller != null)
            {
                controller.gameObject.SetActive(false);
                BattleBoardView.DestroyObject(controller.gameObject);
            }
            if (!restore || regionView == null) return;

            if (rebuildMap) RebuildRegion();
            else regionView.SetCombatMode(false, true);
            regionView.UpdateCamera(0, 0);
            regionView.SetViewSize(deploymentReturnZoom);
            Companions.Synchronize(State, true);
            PlaceCompanions();
            PlacePlayer();
            ClearSelection();
            RefreshSceneSight(true);
        }

        private void UpdateDeploymentWorld()
        {
            if (!deploymentController.ExternalDialogue) return;
            Hud.TickDialogue(Time.unscaledDeltaTime);
            var keyboard = Keyboard.current;
            if (keyboard == null || developerPanel?.BlocksGameInput == true) return;
            if (keyboard.eKey.wasPressedThisFrame || keyboard.spaceKey.wasPressedThisFrame) Hud.ContinueDialogue();
            else if (keyboard.escapeKey.wasPressedThisFrame) Hud.ClosePanel();
        }
    }
}

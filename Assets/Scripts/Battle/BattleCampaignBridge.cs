using System;
using System.Linq;
using BaseConquest.Base;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BaseConquest.Battle
{
    public sealed partial class BattleSceneController
    {
        [SerializeField, HideInInspector] private string campaignBattleId;
        [NonSerialized] private BaseCampaignSession campaignSession;
        [NonSerialized] private bool campaignSaved;
        [NonSerialized] private float campaignRetryAt;
        [NonSerialized] private float campaignCheckpointAt;
        [NonSerialized] private string checkpointJson;
        [NonSerialized] private BattleBoardView sharedMapView;
        [NonSerialized] private BaseMapSession sharedMapSession;
        public void UseCurrentMap(BattleBoardView view, BaseMapSession map)
        { sharedMapView=view; sharedMapSession=map; view.SetCombatAppearance(map.AppearanceBoard); }
        public bool CampaignGameOver { get; private set; }
        public string CampaignCaptives { get; private set; } = "";
        public bool IsCampaignBattle => !string.IsNullOrEmpty(campaignBattleId);
        public bool CanReturnCampaign => IsCampaignBattle && !CampaignGameOver && !animating && !skipping && (campaignSession == null || battle?.Phase == BattlePhase.Deployment || battle?.Phase == BattlePhase.Finished);
        private bool InitializeCampaignBattle()
        {
            if(InitializeSceneEncounter())return true;
            if (!Application.isPlaying) return false;
            if (!string.IsNullOrEmpty(BaseBattleGateway.RequestedId)) { campaignBattleId = BaseBattleGateway.RequestedId; BaseBattleGateway.RequestedId = null; }
            if (!IsCampaignBattle) return false;
            try
            {
                var state = BaseBattleGateway.Store().Load();
                if (!state.Campaign.HasActiveBattle)
                { SceneManager.LoadScene(BaseBattleGateway.ExplorationScene); return false; }
                if (state.Campaign.ActiveBattle.Id != campaignBattleId) throw new InvalidOperationException("存档已有另一场战斗");
                if(!state.Campaign.ActiveBattle.RealtimeRules)throw new InvalidOperationException("隔离实验不自动迁移旧回合战斗存档，请使用战前开局");
                campaignSession = new BaseCampaignSession(state.Campaign.ActiveBattle,sharedMapSession); campaignSaved = false;
                BaseConquest.Battle.BattleScenePlacement.Start(state,campaignSession);
                suppressInitialDeploy = true; ReplaceSimulation(campaignSession.Battle.Scenario, campaignSession.Battle);
                if(IsRealtimeCampaign&&battle.Phase!=BattlePhase.Deployment)AttachRealtime();
                feedback = state.Campaign.ActiveBattle.HasCheckpoint ? (realtime!=null?"已恢复"+RealtimeStatus+"；按继续运行":"已恢复真实布兵准备") : "真实队伍与供给已锁定；手动部署或一键布兵，当前生命入场。"; return true;
            }
            catch (Exception e) { feedback = "战役读取失败：" + e.Message; if(sharedMapView!=null) { Debug.LogError(feedback); BaseBattleGateway.RequestedId=null;SceneManager.LoadScene(BaseBattleGateway.ExplorationScene); } return false; }
        }
        private void PersistCampaignResult()
        {
            if(IsSceneEncounter){if(battle?.Phase==BattlePhase.Finished&&!campaignSaved){campaignSaved=true;feedback=SceneEncounterResult;}return;}
            if(!Application.isPlaying)return;
            if (campaignSession == null || campaignSaved || battle.Phase != BattlePhase.Finished || animating || skipping) return;
            if (Time.unscaledTime < campaignRetryAt) return; campaignRetryAt = Time.unscaledTime + 1;
            try
            {
                var store = BaseBattleGateway.Store(); var state = store.Load();
                realtime?.SaveCheckpoint(state);
                if (state.Campaign.SettledIds.Contains(campaignBattleId)) campaignSaved = true;
                else if (BaseCampaign.Settle(state, campaignSession)) { store.Save(state); campaignSaved = true; }
                else throw new InvalidOperationException("本场活动记录已变化");
                CampaignGameOver=state.Aftermath.GameOver;
                CampaignCaptives=string.Join("、",state.Aftermath.Prisoners.Where(p=>p.BattleId==campaignBattleId&&p.Status=="held").Select(p=>p.Name));
                feedback = CampaignGameOver ? "游戏结束："+state.Aftermath.Reason+"。只能读档或回主菜单。" : "战果已保存：伤势、即死与俘虏一次回写，配额释放。俘虏："+(CampaignCaptives=="" ? "无" : CampaignCaptives);
                hud?.Refresh();
            }
            catch (Exception e) { feedback = "战果尚未保存，返回时会重试：" + e.Message; hud?.Refresh(); }
        }
        public void ReturnCampaign()
        {
            if(IsSceneEncounter){if(CanReturnCampaign)FinishSceneEncounter();return;}
            if (!CanReturnCampaign) return;
            if(IsRealtimeCampaign&&battle.Phase==BattlePhase.Deployment){AbandonRealtimeBattle();return;}
            if (campaignSession == null) { campaignBattleId = ""; SceneManager.LoadScene(BaseBattleGateway.ExplorationScene); return; }
            try
            {
                if (battle.Phase == BattlePhase.Deployment)
                {
                    var store = BaseBattleGateway.Store(); var state = store.Load();
                    if (!BaseCampaign.Abort(state, campaignSession)) throw new InvalidOperationException("准备记录已变化"); store.Save(state);
                }
                else { campaignRetryAt = 0; PersistCampaignResult(); if (!campaignSaved || CampaignGameOver) {hud?.Refresh();return;} }
                BaseBattleGateway.ShowPrisoners=true;
                sharedMapSession?.LeaveBattle();sharedMapView?.SetCombatMode(false);
                campaignBattleId = ""; BaseBattleGateway.RequestedId = null; SceneManager.LoadScene(BaseBattleGateway.ExplorationScene);
            }
            catch (Exception e) { feedback = "尚未返回，原进度保留：" + e.Message; Debug.LogError(feedback);hud?.Refresh(); }
        }
        public void ReturnCampaignPrisoners(){if(!CanReturnCampaign)return;BaseBattleGateway.ShowPrisoners=true;ReturnCampaign();}
        private bool PersistCampaignCheckpoint(bool force=false,bool clearBaseEffects=false)
        {
            if(IsRealtimeCampaign||!Application.isPlaying)return true;
            if(campaignSession==null||campaignSaved||!force&&(battle.Phase==BattlePhase.Finished||animating||skipping||Time.unscaledTime<campaignCheckpointAt))return true;
            campaignCheckpointAt=Time.unscaledTime+1;
            try { campaignSession.Bind();var data=battle.Capture();string json=JsonUtility.ToJson(data)+(realtime!=null?JsonUtility.ToJson(realtime.Model.Capture()):"");if(!force&&json==checkpointJson)return true;var store=BaseBattleGateway.Store();var state=store.Load();if(clearBaseEffects||!RuntimeDeveloperOptions.Enabled)BaseDeveloperTools.ClearEffects(state);if(realtime!=null)realtime.SaveCheckpoint(state);else campaignSession.SaveCheckpoint(state);store.Save(state);checkpointJson=json;return true; }
            catch(Exception e){feedback="战斗存档尚未保存："+e.Message;return false;}
        }
        public void AbandonRealtimeBattle(bool mainMenu=false) => LeaveRealtimeBattle(mainMenu);

        private bool LeaveRealtimeBattle(bool mainMenu, bool loadScene = true)
        {
            if(IsSceneEncounter)return FinishSceneEncounter(true,mainMenu);
            try
            {
                if(!campaignSaved)BaseBattleGateway.Session().AbandonBattle();
                BaseBattleGateway.MainMenu=mainMenu;
                BaseBattleGateway.RequestedId=null;
                campaignBattleId="";
                if(loadScene)SceneManager.LoadScene(BaseBattleGateway.ExplorationScene);
                return true;
            }
            catch(Exception error){feedback="未放弃战斗："+error.Message;hud?.Refresh();return false;}
        }
        public void CampaignMainMenu() {if(IsRealtimeCampaign){AbandonRealtimeBattle(true);return;}PersistCampaignCheckpoint(true);LoadCampaignMainMenu();}
        private void LoadCampaignMainMenu()
        {
            BaseBattleGateway.MainMenu=true;
            BaseBattleGateway.RequestedId=null;
            campaignBattleId="";
            SceneManager.LoadScene(BaseBattleGateway.ExplorationScene);
        }
        private void OnApplicationPause(bool paused){if(paused){CancelGesture();ManualPause();PersistCampaignCheckpoint(true);}}
        private void OnApplicationQuit()=>PersistCampaignCheckpoint(true);
    }
}

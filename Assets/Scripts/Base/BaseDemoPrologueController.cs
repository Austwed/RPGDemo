using System;
using System.Linq;
using BaseConquest.Battle;
using BaseConquest.RealtimePrototype;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BaseConquest.Base
{
    public sealed partial class BaseSceneController
    {
        public void BeginDemoEscape()
        {
            if(Change(next=>{if(!BaseDemoPrologue.BeginEscape(next))throw new InvalidOperationException("请先到村民与队长旁听取消息");},"队长前往村头阻敌；带村民向东撤离"))
            {RebuildRegion();Companions.Synchronize(State,true);PlaceCompanions();PlacePlayer();Hud.ClosePanel();}
        }
        private void CheckPrologueTrigger()
        {
            if(!BaseDemoPrologue.AtTrigger(State)||combatController!=null||Hud.PanelOpen)return;
            Hud.OpenPursuitArrival(()=>{Hud.ClosePanel();BeginProloguePursuit();});
        }
        private void BeginProloguePursuit()
        {
            if(!Change(next=>{if(!BaseDemoPrologue.BeginPursuit(next))throw new InvalidOperationException("追兵已触发或撤离位置变化");},"两名半血精英追兵从村头方向追来"))return;
            try
            {
                BeginSceneEncounter(BaseDemoPrologue.Encounter(State),run=>BaseDemoPrologue.Configure(run,()=>BaseDemoPrologue.Safe(State)),AdvancePrologueFollowers,FinishPrologueEncounter,"掩护撤离 · 追兵交战");combatController.WaitForMovementRelease=true;
            }
            catch(Exception error)
            {
                MapSession.LeaveBattle();var next=State.Copy();BaseDemoPrologue.Resolve(next,null,true);SaveStore.Save(next);State=next;
                if(combatController!=null){combatController.gameObject.SetActive(false);BattleBoardView.DestroyObject(combatController.gameObject);combatController=null;}
                explorationActors.gameObject.SetActive(true);Hud.SetVisible(true);ReportMessage("追兵战未启动，保留重试："+error.Message);
            }
        }
        private void AdvancePrologueFollowers(double seconds)
        {
            while(seconds>0){double step=Math.Min(.05,seconds);Companions.Update(State,step);seconds-=step;}
            BaseDemoPrologue.CaptureDepartures(State,Companions);PlaceCompanions();
        }
        private bool FinishPrologueEncounter(RealtimeCampaignBattle run,bool abandon,bool mainMenu)
        {
            var next=State.Copy();if(!BaseDemoPrologue.Resolve(next,run,abandon))return false;
            if(!abandon&&run?.Battle.Phase==BattlePhase.Finished)BaseSceneRules.RememberStrategy(next,run.Session);
            BaseEconomy.Validate(next);SaveStore.Save(next);State=next;
            BaseBattleGateway.MainMenu=mainMenu;BaseBattleGateway.RequestedId=null;
            if(BaseSceneRules.Active(State)){FinishLocalScene(mainMenu,()=>Hud.OpenDemoStory());return true;}
            if(Application.isPlaying)SceneManager.LoadScene(BaseBattleGateway.ExplorationScene);
            else {var previous=combatController;combatController=null;if(previous!=null){previous.gameObject.SetActive(false);BattleBoardView.DestroyObject(previous.gameObject);}RebuildRegion();RebuildHud();Companions.Synchronize(State,true);PlaceCompanions();PlacePlayer();Hud.OpenDemoStory();}
            return true;
        }
        public void RecoverDemoPrologue()
        {
            if(Change(next=>{if(!BaseDemoPrologue.Recover(next))throw new InvalidOperationException("序章尚未完成");},"获救后在另一领地醒来，队长下落不明"))
            {RebuildRegion();RebuildHud();Companions.Synchronize(State,true);PlaceCompanions();PlacePlayer();Hud.OpenDemoStory();}
        }
    }
}

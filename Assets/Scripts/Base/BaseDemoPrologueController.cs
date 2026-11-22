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
            if(Change(next=>{if(!BaseDemoPrologue.BeginEscape(next))throw new InvalidOperationException("请先到村民与队长旁听取消息");},""))
            {RebuildRegion();Companions.Synchronize(State,true);PlaceCompanions();PlacePlayer();Hud.ClosePanel();}
        }
        private bool pursuitArrivalOpen;
        private void CheckPrologueTrigger()
        {
            if(!BaseDemoPrologue.AtTrigger(State)||combatController!=null||Hud.PanelOpen)return;
            pursuitArrivalOpen = true;
            OpeningAudio?.SetExploration(this, VillageOpeningAudio.Place.Battle);
            Hud.OpenPursuitArrival(()=>{pursuitArrivalOpen = false; Hud.ClosePanel();BeginProloguePursuit();});
        }
        private void BeginProloguePursuit()
        {
            if(!Change(next=>{if(!BaseDemoPrologue.BeginPursuit(next))throw new InvalidOperationException("追兵已触发或撤离位置变化");},""))return;
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
            BaseEconomy.Validate(next);SaveStore.Save(next);State=next;
            BaseBattleGateway.MainMenu=mainMenu;BaseBattleGateway.RequestedId=null;
            void AfterPursuit()
            {
                if(State.Demo.Stage==BaseDemoStage.PrologueWounded)Hud.OpenDemoStory();
                else Hud.OpenPursuitRetry();
            }
            if(BaseSceneRules.Active(State)){FinishLocalScene(mainMenu,AfterPursuit);return true;}
            if(Application.isPlaying)SceneLoadingCover.LoadExploration();
            else {var previous=combatController;combatController=null;if(previous!=null){previous.gameObject.SetActive(false);BattleBoardView.DestroyObject(previous.gameObject);}RebuildRegion();RebuildHud();Companions.Synchronize(State,true);PlaceCompanions();PlacePlayer();if(!mainMenu)AfterPursuit();}
            return true;
        }
        public void RecoverDemoPrologue()
        {
            if(Change(next=>{if(!BaseDemoPrologue.Recover(next))throw new InvalidOperationException("序章尚未完成");},""))
            {RebuildRegion();RebuildHud();Companions.Synchronize(State,true);PlaceCompanions();PlacePlayer();Hud.OpenDemoStory();}
        }
    }
}

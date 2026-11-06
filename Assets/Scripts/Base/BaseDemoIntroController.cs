using System;
using System.Linq;
using BaseConquest.Battle;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BaseConquest.Base
{
    public sealed partial class BaseSceneController
    {
        private string heroChoiceFocus="";
        private Vector3 heroChoiceCamera;
        internal void BeginHeroChoice()
        {
            heroChoiceFocus="";heroChoiceCamera=WorldPoint((State.PlayerX+State.Demo.Intro.CaptainX)*.5,(State.PlayerY+State.Demo.Intro.CaptainY)*.5);
            regionView.Follow(heroChoiceCamera);
        }
        internal void FocusHeroChoice(string hero){heroChoiceFocus=hero;}
        private void TickHeroChoice(float seconds)
        {
            var i=State.Demo.Intro;var target=heroChoiceFocus=="male"?WorldPoint(State.PlayerX,State.PlayerY):heroChoiceFocus=="female"?WorldPoint(i.CaptainX,i.CaptainY):WorldPoint((State.PlayerX+i.CaptainX)*.5,(State.PlayerY+i.CaptainY)*.5);
            float t=1-Mathf.Exp(-6*Mathf.Max(0,seconds));heroChoiceCamera=Vector3.Lerp(heroChoiceCamera,target,t);regionView.Follow(heroChoiceCamera);
            regionView.SetViewSize(Mathf.Lerp(WorldCamera.orthographicSize,heroChoiceFocus==""?5f:4f,t));Hud.UpdateHeroChoice();
        }
        public void SelectDemoHero(string hero)
        {
            if(!Change(next=>{if(!BaseDemoIntro.Choose(next,hero))throw new InvalidOperationException("请在新旅程选择主角");},BaseScriptNames.Village+"的清晨 · 走近搭档交谈"))return;
            RebuildRegion();RebuildHud();Companions.Synchronize(State,true);PlaceCompanions();PlacePlayer();Hud.ClosePanel();Hud.OpenDemoStory();
        }
        public void AdvanceDemoIntro(Func<BaseState,bool> action,string message)
        {
            if(!Change(next=>{if(!action(next))throw new InvalidOperationException(BaseDemoIntro.Objective(next));},message))return;
            Hud.ClosePanel();RebuildRegion();Companions.Synchronize(State,true);PlaceCompanions();PlacePlayer();
        }
        private void TickDemoIntro(double seconds)
        {
            if(!BaseDemoIntro.Active(State)||!BaseDemoPrologue.Village(State)||Hud.PanelOpen)return;
            var partner=State.Leaders.FirstOrDefault(l=>l.Id=="demo_captain");if(partner!=null){State.Demo.Intro.CaptainX=partner.MapX;State.Demo.Intro.CaptainY=partner.MapY;}
            if(State.Demo.Intro.ReporterLeaving&&!State.Demo.Intro.ReporterGone)
            {
                bool gone=BaseDemoIntro.AdvanceReporterExit(State,seconds);var messenger=npcViews.FirstOrDefault(v=>v!=null&&v.name.StartsWith("prologue_report ·"));
                messenger?.MoveTo(WorldPoint(State.Demo.Intro.ReporterX,State.Demo.Intro.ReporterY));
                if(gone){SaveStore.Save(State);RefreshDemoRegion();}
            }
            if(State.Demo.Intro.Step==6)
            {
                bool reportArrived=BaseDemoIntro.AdvanceReporter(State,seconds);var reporter=npcViews.FirstOrDefault(v=>v!=null&&v.name.StartsWith("prologue_report ·"));
                reporter?.MoveTo(WorldPoint(State.Demo.Intro.ReporterX,State.Demo.Intro.ReporterY));
                if(reportArrived){SaveStore.Save(State);RefreshDemoRegion();if(BaseInteraction.At(State,"base","prologue_report"))Hud.OpenDemoStory();}return;
            }
            if(State.Demo.Intro.Step!=7)return;
            bool arrived=BaseDemoIntro.AdvanceDeparture(State,seconds);
            var actor=npcViews.FirstOrDefault(v=>v!=null&&v.name.StartsWith("demo_captain ·"));
            if(actor!=null)actor.MoveTo(WorldPoint(State.Demo.Intro.CaptainX,State.Demo.Intro.CaptainY));
            if(arrived){SaveStore.Save(State);RefreshDemoRegion();}
        }
        public void BeginDemoPractice()
        {
            BaseCampaignSession encounter;
            try{encounter=BaseDemoIntro.Practice(State);}catch(Exception e){ReportMessage(e.Message);return;}
            // The saved node remains before the exercise. Reloading safely restarts the isolated drill.
            practiceShown=-1;SaveStore.Save(State);State.SceneBattleId=BaseDemoIntro.BattleId;
            try{BeginSceneEncounter(encounter,run=>BaseDemoIntro.Configure(run,State.Demo.Intro),null,FinishDemoPractice,"民兵演练 · 选主角／暂停／移动／攻击／驻守／手动鼓舞");}
            catch(Exception e){State.SceneBattleId="";MapSession.LeaveBattle();if(combatController!=null){combatController.gameObject.SetActive(false);BattleBoardView.DestroyObject(combatController.gameObject);combatController=null;}explorationActors.gameObject.SetActive(true);Hud.SetVisible(true);ReportMessage("演练准备失败："+e.Message);}
        }
        private bool FinishDemoPractice(RealtimeCampaignBattle run,bool abandon,bool mainMenu)
        {
            bool completed=!abandon&&run?.Battle.Report.Outcome==BattleOutcome.SideAWin;
            var next=State.Copy();if(!BaseDemoIntro.FinishPractice(next,completed))return false;
            if(!abandon&&run?.Battle.Phase==BattlePhase.Finished)BaseSceneRules.RememberStrategy(next,run.Session);BaseEconomy.Validate(next);SaveStore.Save(next);State=next;BaseBattleGateway.MainMenu=mainMenu;
            if(BaseSceneRules.Active(State)){FinishLocalScene(mainMenu);return true;}
            if(Application.isPlaying)SceneManager.LoadScene(BaseBattleGateway.ExplorationScene);
            else{var old=combatController;combatController=null;if(old!=null)BattleBoardView.DestroyObject(old.gameObject);RebuildRegion();RebuildHud();Companions.Synchronize(State,true);PlaceCompanions();PlacePlayer();}
            return true;
        }
    }
}

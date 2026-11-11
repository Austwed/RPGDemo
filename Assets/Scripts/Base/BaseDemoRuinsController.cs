using System;
using System.Linq;
using BaseConquest.Battle;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BaseConquest.Base
{
    public sealed partial class BaseSceneController
    {
        private BaseState beforeDemoRuins;
        public void MeetDemoPartner(string id)
        {if(Change(next=>{if(!BaseDemoRuins.Meet(next,id))throw new InvalidOperationException("请完成醒来并在伙伴旁交谈");},"已了解伙伴背景；是否同行由你决定"))Hud.OpenDemoPartner(id);}
        public void JoinDemoPartner(string id)
        {
            bool toBase=State.Adventure.Members.Count>=BaseAdventure.Capacity;
            if(!Change(next=>{if(!BaseDemoRuins.Join(next,id))throw new InvalidOperationException(!BaseRpg.HasRoom(next)?"营帐队长名额已满，请先扩建营帐或腾出名额":"请先交谈，并确认伙伴可加入、没有未返营的队伍");},toBase?"小队已满，伙伴已加入基地":"伙伴已加入小队"))return;
            RefreshDemoRegion();Hud.ClosePanel();if(id=="partner_craft")Hud.OpenSmithFather();
        }
        public void TravelDemo(string target)
        {
            if(Change(next=>{if(!BaseDemoRuins.Travel(next,target))throw new InvalidOperationException(BaseDemoRuins.TravelProblem(next,target));},"已通过场景出口")){RefreshDemoRegion();Hud.ClosePanel();}
        }
        internal void RefreshDemoRegion()
        {RebuildRegion();RebuildHud();Companions.Synchronize(State,true);PlaceCompanions();PlacePlayer();ClearSelection();Hud.RefreshSummary();}
        public void SweepDemoRuins(string mode)
        {
            BaseSweepPreview result=null;
            if(!Change(next=>result=BaseRuinsSweep.Resolve(next,mode),"遗迹扫荡完成，真实战损已保存"))return;
            RefreshDemoRegion();Hud.OpenSweepResult(result);
        }
        internal void SweepPreparedRuins(BaseBattlePreparation prepared)
        {
            BaseSweepPreview result = null;
            if (!Change(next => result = prepared.Sweep(next), "扫荡完成，战损已保存")) { Hud.RefreshPreparedFailure(); return; }
            RefreshDemoRegion();
            Hud.OpenSweepResult(result, prepared.Route);
        }

        internal void BeginPreparedBattle(BaseBattlePreparation prepared)
        {
            if (prepared == null || Busy) return;
            string problem = prepared.Problem(State, true);
            if (problem != "") { ReportMessage(problem); Hud.RefreshPreparedFailure(); return; }
            switch (prepared.Route)
            {
                case BasePrepRoute.Ruins: BeginDemoRuins("none", prepared: prepared); break;
                case BasePrepRoute.Defense:
                case BasePrepRoute.Second: BeginDemoDefense("none", prepared: prepared); break;
                case BasePrepRoute.BlackCrow: BeginDemoAttack("none", prepared: prepared); break;
                case BasePrepRoute.Hideout: BeginBanditHideout(prepared); break;
            }
        }

        public void BeginDemoRuins(string mode,bool confirmed=false,bool confrontationConfirmed=false,BaseBattlePreparation prepared=null)
        {
            if(!confirmed && BaseRuinsRewards.RequiresWarning(State, prepared))
            {if(prepared!=null)ShowDeploymentBriefing();Hud.OpenRuinsWarning(()=>BeginDemoRuins(mode,true,prepared:prepared));return;}
            if(State.Demo.RuinsDay==0&&!confrontationConfirmed)
            {if(prepared!=null)ShowDeploymentBriefing();Hud.OpenRuinsConfrontation(()=>BeginDemoRuins(mode,true,true,prepared));return;}
            Companions.Capture(State);var before=State.Copy();BaseCampaignSession encounter=null;
            if(!Change(next=>encounter=prepared==null?BaseDemoRuins.Begin(next,mode):prepared.Begin(next),"军需与真实人员已锁定；进入遗迹救助战"))
            {if(prepared!=null)Hud.RefreshPreparedFailure();return;}
            beforeDemoRuins=before;if(BaseSceneRules.Active(State)&&!DeploymentWorldActive)RefreshDemoRegion();
            try{BeginSceneEncounter(encounter,BaseDemoRuins.Configure,null,FinishDemoRuinsEncounter,"遗迹救助 · 击败控制来源",prepared);}
            catch(Exception error)
            {
                MapSession.LeaveBattle();SaveStore.Save(before);State=before;beforeDemoRuins=null;
                EndDeploymentWorld(false);
                if(combatController!=null){combatController.gameObject.SetActive(false);BattleBoardView.DestroyObject(combatController.gameObject);combatController=null;}
                RefreshDemoRegion();ReportMessage("未启动战斗，已恢复战前人员与军需："+error.Message);
            }
        }
        private bool FinishDemoRuinsEncounter(RealtimeCampaignBattle run,bool abandon,bool mainMenu)
        {
            if(beforeDemoRuins==null)return false;
            bool victory=run?.Battle.Report?.Outcome==BattleOutcome.SideAWin;
            bool soloRetreat=abandon&&run!=null&&ruinsRetreatGuideId==run.Session.Opening.Id;
            var next=run==null?beforeDemoRuins.Copy():State.Copy();
            if(victory?!BaseDemoRuins.Complete(next,run):run!=null&&!BaseDemoCombat.Fail(next,run))return false;
            if(!victory&&run!=null)BaseDemoCombat.RetreatPosition(next,false);
            if(BaseSceneRules.Active(next)){if(victory&&BaseSceneRules.Dungeon(next))BattleScenePlacement.Capture(next,run);BaseSceneRules.LeaveDungeon(next);}
            BaseEconomy.Validate(next);SaveStore.Save(next);State=next;beforeDemoRuins=null;ruinsRetreatGuideId=null;
            if(soloRetreat)Message="";
            void AfterRetreat()
            {
                if(victory)Hud.OpenRuinsVictory();
                else if(soloRetreat)Hud.OpenRuinsRetreatMonologue();
            }
            if(BaseSceneRules.Active(State)){FinishLocalScene(mainMenu,AfterRetreat);return true;}
            if(Application.isPlaying){BaseBattleGateway.MainMenu=mainMenu;BaseBattleGateway.RequestedId=null;SceneManager.LoadScene(BaseBattleGateway.ExplorationScene);}
            else
            {
                var previous=combatController;combatController=null;if(previous!=null){previous.gameObject.SetActive(false);BattleBoardView.DestroyObject(previous.gameObject);}
                RefreshDemoRegion();AfterRetreat();
            }
            return true;
        }
        public void DeliverDemoRuins()
        {if(Change(next=>{if(!BaseDemoRuins.Deliver(next))throw new InvalidOperationException("请带真实救助证据到领主同层一格以内，且库存未达上限");},"遗迹证据已交付，奖赏与建设许可只发放一次"))Hud.OpenObject("town_quest");}
        public void CancelRuinsEntry()
        {if(Change(s=>BaseAchievements.Unlock(s,BaseAchievements.Caution),"已暂缓进入遗迹"))Hud.ClosePanel();}
        public void ClaimRuinsReward(BasePrepRoute route)
        {
            string battle = BaseRuinsRewards.PendingBattle(State, route);
            if (Change(next =>
            {
                if (!BaseRuinsRewards.Claim(next, route)) throw new InvalidOperationException(BaseRuinsRewards.ClaimProblem(next, route));
            }, "已领取本次副本奖励"))
            {
                Hud.ClosePanel();
                Hud.OpenLoot(battle);
            }
        }
        public void SkipRuinsReward(BasePrepRoute route)
        {
            string battle = BaseRuinsRewards.PendingBattle(State, route);
            if (!Change(next =>
            {
                if (!BaseRuinsRewards.Skip(next, route)) throw new InvalidOperationException(BaseRuinsRewards.ClaimProblem(next, route));
            }, "已放弃本次副本奖励")) return;
            Hud.ClosePanel();
            if (route == BasePrepRoute.Hideout && State.Aftermath.Loot.Any(item => item.BattleId == battle && !item.Closed))
                Hud.OpenLoot(battle);
        }
    }
}

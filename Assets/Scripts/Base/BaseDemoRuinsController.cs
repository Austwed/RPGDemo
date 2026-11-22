using System;
using System.Linq;
using BaseConquest.Battle;
using BaseConquest.Presentation;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BaseConquest.Base
{
    public sealed partial class BaseSceneController
    {
        private BaseState beforeDemoRuins;
        // The verdict is saved in the prisoner record. The body belongs only to this entrance visit.
        private BaseMapObject fallenGalan;
        private HeroFacing fallenGalanFacing;
        private double galanFallStarted;

        internal void ExecuteWaitingGalan()
        {
            var marker = MapObjects.FirstOrDefault(o => o.Id == BaseDemoStories.GalanMarker);
            if (marker == null || !npcViews.TryGetValue(marker.Id, out var actor)) { Hud.ClosePanel(); return; }
            if (!Change(s => {
                if (!BaseDemoStories.ExecuteController(s, s.Demo.Flow.FirstControllerId))
                    throw new InvalidOperationException("迦兰已经离开。");
            }, "")) return;
            fallenGalan = marker;
            fallenGalanFacing = actor.GetComponent<CompanionSpriteView>().Facing;
            galanFallStarted = Time.unscaledTimeAsDouble;
            npcViews.Remove(marker.Id);
            PlayGalanFall(actor);
            Hud.ClosePanel();
            Hud.RefreshMapLabels();
        }

        private void RestoreFallenGalanView()
        {
            if (fallenGalan == null) return;
            if (State == null || !BaseSceneRules.Entrance(State)) { fallenGalan = null; return; }
            var body = AuthoredMapView.Create(explorationActors, fallenGalan, WorldPoint(fallenGalan.X, fallenGalan.Y),
                mapMaterial, regionView.ObliqueExploration);
            PlayGalanFall(body);
        }

        private void PlayGalanFall(AuthoredMapView body)
        {
            body.name = "GalanFallen";
            var sprite = body.GetComponent<CompanionSpriteView>();
            StartCoroutine(Animate());
            System.Collections.IEnumerator Animate()
            {
                while (sprite != null)
                {
                    double elapsed = Time.unscaledTimeAsDouble - galanFallStarted;
                    sprite.Preview(HeroPose.Down, elapsed, fallenGalanFacing);
                    MapSpriteOrder.Apply(body.transform, body.transform.position.z, true);
                    if (elapsed >= 1) yield break;
                    yield return null;
                }
            }
        }
        public void MeetDemoPartner(string id)
        {if(Change(next=>{if(!BaseDemoRuins.Meet(next,id))throw new InvalidOperationException("请完成醒来并在伙伴旁交谈");},"已了解伙伴背景；是否同行由你决定"))Hud.OpenDemoPartner(id);}
        public void JoinDemoPartner(string id, bool requestFollow = true)
        {
            if (!Change(next =>
            {
                if (!BaseDemoRuins.Join(next, id, requestFollow))
                    throw new InvalidOperationException("请完成醒来并在伙伴旁交谈");
            }, "")) return;
            RefreshDemoRegion();
            Hud.ClosePanel();
            bool following = BaseAdventure.Following(State, id);
            if (State.Leaders.Single(l => l.Id == id).WaitingAtRecruitment)
            {
                Hud.OpenRecruitDestination(id,id == "partner_craft" ? Hud.OpenSmithFather : Hud.ClosePanel);
                return;
            }
            if (id == "partner_farm")
            {
                Hud.OpenAheResponse(following ? "成，你走前头。我带着弓，替你看住背后。" :
                    "成，我就在田边。要搭手，喊我一声就行。");
            }
            else if (id == "partner_craft") Hud.OpenSmithFather();
        }
        public void TravelDemo(string target)
        {
            if(State==null||Busy)return;
            var problem=BaseDemoRuins.TravelProblem(State,target);
            if(problem!=""){ReportMessage(problem, showFeedback:true);return;}
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
                case BasePrepRoute.Second: BeginDemoDefense(prepared); break;
                case BasePrepRoute.BlackCrow: BeginDemoAttack("none", prepared: prepared); break;
                case BasePrepRoute.Hideout: BeginBanditHideout(prepared); break;
            }
        }

        public void BeginDemoRuins(string mode,bool confirmed=false,bool confrontationConfirmed=false,BaseBattlePreparation prepared=null)
        {
            if(!confirmed && prepared == null && BaseRuinsRewards.RequiresWarning(State, null))
            {Hud.OpenRuinsWarning(()=>BeginDemoRuins(mode,true));return;}
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
            bool soloRetreat=run?.Battle.Report?.Outcome==BattleOutcome.Retreat&&ruinsRetreatGuideId==run.Session.Opening.Id;
            var next=run==null?beforeDemoRuins.Copy():State.Copy();
            if(victory?!BaseDemoRuins.Complete(next,run):run!=null&&!BaseDemoCombat.Fail(next,run))return false;
            if(!victory&&run!=null)BaseDemoCombat.RetreatPosition(next,false);
            if(BaseSceneRules.Active(next)){if(victory&&BaseSceneRules.Dungeon(next))BattleScenePlacement.Capture(next,run);BaseSceneRules.LeaveDungeon(next);}
            BaseEconomy.Validate(next);SaveStore.Save(next);State=next;beforeDemoRuins=null;ruinsRetreatGuideId=null;
            if(soloRetreat)Message="";
            void AfterRetreat()
            {
                if(victory && run.Session.Opening.RuinsFirstClear)Hud.OpenRescuedCivilian("demo_rescued_1",true);
                else if(victory)Hud.OpenRuinsReward();
                else if(soloRetreat)Hud.OpenRuinsRetreatMonologue();
            }
            if(BaseSceneRules.Active(State)){FinishLocalScene(mainMenu,AfterRetreat);return true;}
            if(Application.isPlaying){BaseBattleGateway.MainMenu=mainMenu;BaseBattleGateway.RequestedId=null;SceneLoadingCover.LoadExploration();}
            else
            {
                var previous=combatController;combatController=null;if(previous!=null){previous.gameObject.SetActive(false);BattleBoardView.DestroyObject(previous.gameObject);}
                RefreshDemoRegion();AfterRetreat();
            }
            return true;
        }
        public void DeliverDemoRuins()
        {if(Change(next=>{if(!BaseDemoRuins.Deliver(next))throw new InvalidOperationException("请带真实救助证据到领主同层一格以内，且库存未达上限");},""))Hud.ClosePanel();}
        public void CancelRuinsEntry()
        {if(Change(s=>BaseAchievements.Unlock(s,BaseAchievements.Caution),"已暂缓进入遗迹"))Hud.CloseRuinsBriefing();}
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
            if (BaseHud.HasDungeonAftermath(State, battle))
                Hud.OpenLoot(battle);
        }
    }
}

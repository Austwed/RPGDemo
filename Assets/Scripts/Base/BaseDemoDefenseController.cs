using System;
using System.Linq;
using BaseConquest.Battle;
using BaseConquest.Story;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BaseConquest.Base
{
    public sealed partial class BaseSceneController
    {
        private BaseState beforeDemoDefense;
        private BaseMusterPlan pendingDemoDefense;
        public BaseMusterPlan DemoDefensePlan=>pendingDemoDefense;
        public void RepairDemoFacility(string id)
        {
            if(Change(next=>{if(!BaseDemoDefense.Repair(next,id))throw new InvalidOperationException(BaseDemoDefense.RepairProblem(next,id));},"设施已修复，只恢复一级基础功能；未使用营造图册"))
            {RefreshDemoRegion();Hud.OpenDemoStory();}
        }
        public void PrepareDemoDefense(BaseMusterPlan plan)
        {
            if(!BaseDemoDefense.AnyDue(State)||plan==null||plan.Purpose!="defense"||plan.Stamp!=BaseMuster.Snapshot(State)){ReportMessage("选兵预览已变化，请重新选择队伍");return;}
            pendingDemoDefense=plan;Hud.OpenDemoDefenseSupply();
        }
        public void BeginDemoDefense(string mode,bool briefingConfirmed=false,BaseBattlePreparation prepared=null)
        {
            bool second = BaseDemoDefense.SecondDue(State);
            bool seen = second ? State.Demo.Defense.SecondBriefingSeen : State.Demo.Defense.FirstBriefingSeen;
            if (!seen && !briefingConfirmed)
            {
                if (prepared != null) ShowDeploymentBriefing();
                Action proceed = () => BeginDemoDefense(mode, true, prepared);
                Action back = prepared == null ? Hud.OpenDemoDefenseSupply : Hud.ReturnToBattleDeployment;
                if (second) Hud.OpenSecondRaidBriefing(proceed, back);
                else Hud.OpenFirstRaidBriefing(proceed, back);
                return;
            }
            Companions.Capture(State);var before=State.Copy();BaseCampaignSession encounter=null;
            if (!Change(next =>
            {
                if (second) next.Demo.Defense.SecondBriefingSeen = true;
                else next.Demo.Defense.FirstBriefingSeen = true;
                encounter = prepared == null ? BaseDemoDefense.Begin(next, mode, pendingDemoDefense) : prepared.Begin(next);
            }, "真实队伍与军需已锁定")) { if (prepared != null) Hud.RefreshPreparedFailure(); return; }
            beforeDemoDefense=before;pendingDemoDefense=null;
            try{BeginSceneEncounter(encounter,BaseDemoDefense.Configure,null,FinishDemoDefenseEncounter,encounter.Opening.EventId==BaseDemoDefense.SecondEventId?"第二波防守 · 击败全部来敌":"首次防守 · 守住营地外围",prepared);}
            catch(Exception error)
            {
                MapSession.LeaveBattle();SaveStore.Save(before);State=before;beforeDemoDefense=null;
                EndDeploymentWorld(false);
                if(combatController!=null){combatController.gameObject.SetActive(false);BattleBoardView.DestroyObject(combatController.gameObject);combatController=null;}
                RefreshDemoRegion();ReportMessage("未启动防守，已恢复战前人员与军需："+error.Message);
            }
        }
        private bool FinishDemoDefenseEncounter(RealtimeCampaignBattle run,bool abandon,bool mainMenu)
        {
            bool victory=run?.Battle.Report?.Outcome==BattleOutcome.SideAWin;
            if(beforeDemoDefense==null){if(State.Campaign.HasActiveBattle)return false;FinishLocalScene(mainMenu,AfterDefense);return true;}
            if(mainMenu&&run?.Session.Opening.EventId==BaseDemoDefense.SecondEventId&&run.Battle.Phase==BattlePhase.Fighting)
            {SaveStore.Save(beforeDemoDefense);State=beforeDemoDefense;beforeDemoDefense=null;FinishLocalScene(true);return true;}
            var next=run==null?beforeDemoDefense.Copy():State.Copy();
            if(victory?!BaseDemoDefense.Complete(next,run):run!=null&&!BaseDemoCombat.Fail(next,run))return false;
            if(!victory&&run!=null)BaseDemoCombat.RetreatPosition(next,true);
            if(victory)BattleScenePlacement.Capture(next,run);
            next.Campaign.Location="base";var camp=BaseSpatial.Layout(next,"base");next.Campaign.CurrentMapId=camp.SourceMapId??"";
            var landing=BaseSpatial.Nearest(next,new BasePoint(1.6,6.5));next.PlayerX=landing.X;next.PlayerY=landing.Y;BaseAdventure.Arrive(next);
            BaseEconomy.Validate(next);SaveStore.Save(next);State=next;beforeDemoDefense=null;
            if(BaseSceneRules.Active(State)){FinishLocalScene(mainMenu,AfterDefense);return true;}
            if(Application.isPlaying){BaseBattleGateway.MainMenu=mainMenu;BaseBattleGateway.RequestedId=null;BaseBattleGateway.ShowPrisoners=BaseBattleLoot.Pending(next);SceneLoadingCover.LoadExploration();}
            else
            {
                var previous=combatController;combatController=null;if(previous!=null){previous.gameObject.SetActive(false);BattleBoardView.DestroyObject(previous.gameObject);}
                RefreshDemoRegion();AfterDefense();
            }
            return true;

            void AfterDefense()
            {
                if(State.Aftermath.GameOver)Hud.OpenGameOver();
                else if(victory&&run.Session.Opening.EventId==BaseDemoDefense.EventId)Hud.OpenDemoDefenseAftermath();
                else if(BaseDemoFinale.SecondRaidReportPending(State))Hud.OpenSecondRaidVictoryReport();
                else if(BaseBattleLoot.Pending(State))Hud.OpenLoot();
                else Hud.OpenDemoStory();
            }
        }
    }
    public sealed partial class BaseHud
    {
        private bool OpenDemoDefenseInteraction(string id)
        {
            if(!BaseDemoDefense.Enabled(owner.State))return false;
            if(id=="demo_defense_exit"||id=="demo_defense_return"){owner.TravelDemo(id=="demo_defense_exit"?"perimeter":"base");return true;}
            if(id=="demo_defense_start")
            {
                if(BaseDemoDefense.AnyDue(owner.State))OpenBattlePreparation(BaseDemoDefense.SecondDue(owner.State) ? BasePrepRoute.Second : BasePrepRoute.Defense);else OpenDemoStory();return true;
            }
            if(id!="demo_fix_workshop"&&id!="demo_fix_barracks")return false;
            var state = owner.State;
            string facility = id == "demo_fix_workshop" ? "workshop" : "barracks";
            string name = BaseEconomy.FacilityName(facility);
            string problem = BaseDemoDefense.RepairProblem(state, facility);
            activeObject = "demo-repair";
            interactionObject = "";
            BeginPanel(name + "修复");
            Line(facility == "workshop" ? "修复后可安排工人生产一级军需。" : "修复后开放军事整备。", 54, 21);
            if (problem != "") Line(problem, 65, 20);
            RowButton("修复" + name + " · 原料 " + BaseDemoDefense.RepairMaterialCost(state) + "／钱币 " +
                BaseDemoDefense.RepairCoinCost(state), 0, 800, () => owner.RepairDemoFacility(facility), problem == "");
            row += 60;
            EndPanel();
            return true;
        }
        // DEF_01_START_01/02：首次防守开战前巴图叫阵，主角拔剑回应。
        public void OpenFirstRaidBriefing(Action proceed, Action back = null)
        {
            NpcDialogue(new[]{
                new NpcLine(BaseScriptNames.Batu,BaseScriptNames.Portrait("batu"),"新打的军械都留下！这座营地，黑鸦寨也要了！")
            },new[]{new NpcReply("拔剑迎战",proceed),new NpcReply("先看布防",back ?? OpenDemoDefenseSupply)});
        }
        // DEF_01_MID_INTEL：敌军只剩一人时崩溃泄密，供出敌营位置。同场只播一次（BattleIntelSeen 由调用方记录）。
        public void OpenDefenseIntel(Action proceed)
        {
            root.GetComponent<UnityEngine.Canvas>().sortingOrder=300;
            NpcDialogue(new[]{
                new NpcLine(BaseScriptNames.Bandit+"匪兵","saber","别杀我！是巴图带我们来的，我知道寨子在哪儿。"),
                new NpcLine("你","","你们的寨子在哪儿？",true),
                new NpcLine(BaseScriptNames.Bandit+"匪兵","saber","南边官道拐口的山坳里。山道两边都是枯树和大石。")
            },new[]{new NpcReply("继续战斗",proceed)});
        }
        public void OpenSecondRaidBriefing(Action proceed, Action back = null)
        {
            var lines=new System.Collections.Generic.List<NpcLine>{new NpcLine(BaseScriptNames.Chief,BaseScriptNames.Portrait("chief"),"上回没拿下你们的营地。今天我亲自来。")};
            if(owner.State.Demo.Defense.BatuFate=="escaped")lines.Add(new NpcLine(BaseScriptNames.Batu,BaseScriptNames.Portrait("batu"),"寨主，就是这伙人。上回是我没拿下，今儿让我打头阵。"));
            else lines.Add(new NpcLine(BaseScriptNames.Chief,BaseScriptNames.Portrait("chief"),"巴图的账，今天一起算！"));
            lines.Add(new NpcLine(BaseScriptNames.Huoya,BaseScriptNames.Portrait("huoya"),"弟兄们，跟紧我！别让他们冲散了！"));
            NpcDialogue(lines.ToArray(),new[]{new NpcReply("准备迎战",proceed),new NpcReply("暂缓迎战",back ?? OpenDemoDefenseSupply)});
        }
        public void OpenDemoDefenseAftermath()
        {
            var state=owner.State;if(!BaseDemoDefense.Won(state)||state.Demo.Defense.BatuStoryDay>0){if(BaseBattleLoot.Pending(state))OpenLoot();else OpenDemoStory();return;}
            bool dead=state.Demo.Defense.BatuFate=="dead";
            NpcDialogue(new[]{new NpcLine(BaseScriptNames.Batu,BaseScriptNames.Portrait("batu"),dead?"这回算你们守住了……大当家会亲自来，替我报仇。":"这回算你们守住了。大当家不会就这么算了。")},
                new[]{new NpcReply("处理战果",()=>{if(owner.Change(next=>{BaseDemoDefense.AcknowledgeBatu(next);},"已记录敌军威胁")){ClosePanel();owner.RefreshDemoRegion();if(BaseBattleLoot.Pending(owner.State))owner.Hud.OpenLoot();else owner.Hud.OpenDemoStory();}})});
        }
        public void OpenSecondRaidVictoryReport()
        {
            if(!BaseDemoFinale.SecondRaidReportPending(owner.State))return;
            activeObject="demo-second-victory-report";
            NpcDialogue(new[]{
                    new NpcLine(BaseScriptNames.Scout,BaseScriptNames.Portrait("chenan"),
                        StoryText.Get("story.scene.defense.second_victory_report.1","大人，守寨的匪兵都跑散了，往白桦村的路通了。")),
                    new NpcLine(BaseScriptNames.Scout,BaseScriptNames.Portrait("chenan"),
                        StoryText.Get("story.scene.defense.second_victory_report.2","咱们也能先去寨里看看，还有没有物资留下。"))},
                new[]{new NpcReply(StoryText.Get("story.ui.defense.second_report_continue","知道了"),FinishReport)},
                lockExitUntilFinished:true);

            void FinishReport()
            {
                if(!owner.Change(next=>BaseDemoFinale.AcknowledgeSecondRaidReport(next),""))
                {
                    OpenSecondRaidVictoryReport();
                    return;
                }
                ClosePanel();
            }
        }
        public void OpenDemoDefenseSupply()
        {
            var state=owner.State;var plan=owner.DemoDefensePlan;if(plan==null){OpenDeparture("perimeter");return;}
            bool second=BaseDemoDefense.SecondDue(state);activeObject="demo-defense-supply";interactionObject="";BeginPanel(second?"第二波防守 · 军需确认":"首次防守 · 军需确认");
            Line("队长："+string.Join("、",plan.Leaders.Select(id=>state.Leaders.Single(person=>person.Id==id).Name))+"；实际选兵 "+plan.Total+"人。",90,22);
            Line(second?"军需按当前库存配给。这一波没有时限，敌军不死不退——击败全部来敌才算守住；我方全员失能即营地失守，行动中止。":"军需按当前库存配给。坚守到时后敌军撤退，可追击或结束追击；失败或撤退扣除损失，残军次日再袭。",130,21);
            foreach(string choice in new[]{"full","half","none"})
            {string mode=choice;RowButton("确认迎战 · "+(mode=="full"?"尽量配足军需":mode=="half"?"最多半数供给":"不使用军需"),0,800,()=>owner.BeginDemoDefense(mode));row+=60;}
            RowButton("返回重新选兵",0,800,()=>OpenDeparture("perimeter"));row+=60;EndPanel();
        }
    }
}


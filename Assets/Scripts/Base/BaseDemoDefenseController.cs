using System;
using System.Linq;
using BaseConquest.Battle;
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
        public void BeginDemoDefense(string mode,bool briefingConfirmed=false)
        {
            // DEF_01_START：首次防守开战前由巴图叫阵，只播一次。
            if(!BaseDemoDefense.SecondDue(State)&&!State.Demo.Defense.FirstBriefingSeen&&!briefingConfirmed)
            {Hud.OpenFirstRaidBriefing(()=>BeginDemoDefense(mode,true));return;}
            if(!BaseDemoDefense.SecondDue(State)&&briefingConfirmed&&!Change(next=>next.Demo.Defense.FirstBriefingSeen=true,"首防战前对白已记录"))return;
            if(BaseDemoDefense.SecondDue(State)&&!State.Demo.Defense.SecondBriefingSeen&&!briefingConfirmed)
            {Hud.OpenSecondRaidBriefing(()=>BeginDemoDefense(mode,true));return;}
            if(BaseDemoDefense.SecondDue(State)&&briefingConfirmed&&!Change(next=>next.Demo.Defense.SecondBriefingSeen=true,"第二波战前对白已记录"))return;
            Companions.Capture(State);var before=State.Copy();BaseCampaignSession encounter=null;
            if(!Change(next=>encounter=BaseDemoDefense.Begin(next,mode,pendingDemoDefense),"真实队伍／军需已锁定，就近落格后开始迎战"))return;
            beforeDemoDefense=before;pendingDemoDefense=null;
            try{BeginSceneEncounter(encounter,BaseDemoDefense.Configure,null,FinishDemoDefenseEncounter,encounter.Opening.EventId==BaseDemoDefense.SecondEventId?"第二波防守 · 守住营地外围":"首次防守 · 守住营地外围");}
            catch(Exception error)
            {
                MapSession.LeaveBattle();SaveStore.Save(before);State=before;beforeDemoDefense=null;
                if(combatController!=null){combatController.gameObject.SetActive(false);BattleBoardView.DestroyObject(combatController.gameObject);combatController=null;}
                RefreshDemoRegion();ReportMessage("未启动防守，已恢复战前人员与军需："+error.Message);
            }
        }
        private bool FinishDemoDefenseEncounter(RealtimeCampaignBattle run,bool abandon,bool mainMenu)
        {
            if(beforeDemoDefense==null){if(State.Campaign.HasActiveBattle)return false;FinishLocalScene(mainMenu,()=>Hud.OpenLoot());return true;}
            if(mainMenu&&run?.Session.Opening.EventId==BaseDemoDefense.SecondEventId&&run.Battle.Phase==BattlePhase.Fighting)
            {SaveStore.Save(beforeDemoDefense);State=beforeDemoDefense;beforeDemoDefense=null;FinishLocalScene(true);return true;}
            bool victory=run?.Battle.Report?.Outcome==BattleOutcome.SideAWin;var next=run==null?beforeDemoDefense.Copy():State.Copy();
            if(victory?!BaseDemoDefense.Complete(next,run):run!=null&&!BaseDemoCombat.Fail(next,run))return false;
            if(!victory&&run!=null)BaseDemoCombat.RetreatPosition(next,true);
            if(victory)BattleScenePlacement.Capture(next,run);
            next.Campaign.Location="base";var camp=BaseSpatial.Layout(next,"base");next.Campaign.CurrentMapId=camp.SourceMapId??"";
            var landing=BaseSpatial.Nearest(next,new BasePoint(1.6,6.5));next.PlayerX=landing.X;next.PlayerY=landing.Y;BaseAdventure.Arrive(next);
            BaseEconomy.Validate(next);SaveStore.Save(next);State=next;beforeDemoDefense=null;
            if(BaseSceneRules.Active(State)){FinishLocalScene(mainMenu,()=>{if(State.Aftermath.GameOver)Hud.OpenGameOver();else if(victory&&run.Session.Opening.EventId==BaseDemoDefense.EventId)Hud.OpenDemoDefenseAftermath();else if(BaseBattleLoot.Pending(State))Hud.OpenLoot();else Hud.OpenDemoStory();});return true;}
            if(Application.isPlaying){BaseBattleGateway.MainMenu=mainMenu;BaseBattleGateway.RequestedId=null;BaseBattleGateway.ShowPrisoners=BaseBattleLoot.Pending(next);SceneManager.LoadScene(BaseBattleGateway.ExplorationScene);}
            else
            {
                var previous=combatController;combatController=null;if(previous!=null){previous.gameObject.SetActive(false);BattleBoardView.DestroyObject(previous.gameObject);}
                RefreshDemoRegion();if(victory&&run.Session.Opening.EventId==BaseDemoDefense.EventId)Hud.OpenDemoDefenseAftermath();else if(BaseBattleLoot.Pending(State))Hud.OpenLoot();else Hud.OpenDemoStory();
            }
            return true;
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
                if(BaseDemoDefense.AnyDue(owner.State))OpenDeparture("perimeter");else OpenDemoStory();return true;
            }
            if(id!="demo_fix_workshop"&&id!="demo_fix_barracks")return false;
            string facility=id=="demo_fix_workshop"?"workshop":"barracks",problem=BaseDemoDefense.RepairProblem(owner.State,facility);
            activeObject="demo-repair";interactionObject="";BeginPanel(facility=="workshop"?"工坊 · 固定修复":"兵营 · 固定修复");
            Line("建设许可已取得。每座只付一次：原料"+BaseDemoDefense.RepairMaterialCost(owner.State)+"、钱币"+BaseDemoDefense.RepairCoinCost(owner.State)+"；不消耗图册，不推进日期，不提升设施等级。",115,22);
            Line(facility=="workshop"?"修复后可安排岗位，日结制作已有一级军需；岗位／耗料使用35号开局保存的规则。":"修复后开放现有招募与军事整备入口；首次防守仍需真实选兵、军需整备。",115,21);
            if(problem!="")Line(problem,65,20);RowButton("确认修复",-210,400,()=>owner.RepairDemoFacility(facility),problem=="");RowButton("暂不修复",210,400,ClosePanel);row+=60;EndPanel();return true;
        }
        // DEF_01_START_01/02：首次防守开战前巴图叫阵，主角拔剑回应。
        public void OpenFirstRaidBriefing(Action proceed)
        {
            NpcDialogue(new[]{
                new NpcLine(BaseScriptNames.Batu,BaseScriptNames.Portrait("batu"),"哈哈！新出炉的军械，还有"+BaseScriptNames.Village+"漏网的余党，今天全归"+BaseScriptNames.Bandit+"！"),
                new NpcLine("你","","冲着"+BaseScriptNames.Village+"幸存者来的？正好，拿你的脑袋问问幕后雇主是谁！",true)
            },new[]{new NpcReply("拔剑迎战",proceed),new NpcReply("先看布防",OpenDemoDefenseSupply)});
        }
        // DEF_01_MID_INTEL：敌军只剩一人时崩溃泄密，供出敌营位置。同场只播一次（BattleIntelSeen 由调用方记录）。
        public void OpenDefenseIntel(Action proceed)
        {
            root.GetComponent<UnityEngine.Canvas>().sortingOrder=300;
            NpcDialogue(new[]{
                new NpcLine(BaseScriptNames.Bandit+"匪兵","saber","完了完了！寨子里就留了老弱，大当家带人下山，谁守得住营盘啊……"),
                new NpcLine("你","","大当家带了多少人？营盘在哪个方向？快说！",true),
                new NpcLine(BaseScriptNames.Bandit+"匪兵","saber","在……在营地南边官道拐口！山道两边全是枯树和大石，你们攻不上去的！")
            },new[]{new NpcReply("继续战斗",proceed)});
        }
        public void OpenSecondRaidBriefing(Action proceed)
        {
            var lines=new System.Collections.Generic.List<NpcLine>{new NpcLine(BaseScriptNames.Chief,BaseScriptNames.Portrait("chief"),"上次没能拿下这营地，这回我亲自来。")};
            if(owner.State.Demo.Defense.BatuFate=="escaped")lines.Add(new NpcLine(BaseScriptNames.Batu,BaseScriptNames.Portrait("batu"),"我说过我们还会回来。"));
            else lines.Add(new NpcLine(BaseScriptNames.Chief,BaseScriptNames.Portrait("chief"),"巴图的账，今天一起算！"));
            lines.Add(new NpcLine(BaseScriptNames.Huoya,BaseScriptNames.Portrait("huoya"),"弟兄们，跟紧我，别乱了阵脚！"));
            NpcDialogue(lines.ToArray(),new[]{new NpcReply("准备迎战",proceed),new NpcReply("暂缓迎战",OpenDemoDefenseSupply)});
        }
        public void OpenDemoDefenseAftermath()
        {
            var state=owner.State;if(!BaseDemoDefense.Won(state)||state.Demo.Defense.BatuStoryDay>0){if(BaseBattleLoot.Pending(state))OpenLoot();else OpenDemoStory();return;}
            bool dead=state.Demo.Defense.BatuFate=="dead";
            NpcDialogue(new[]{new NpcLine(BaseScriptNames.Batu,BaseScriptNames.Portrait("batu"),dead?"咳……你们守得住这一回，也挡不住大当家亲自来。":"是我们低估了你们。告诉大当家，这笔账还没完。"),
                new NpcLine("你","","他们还会回来。先清点伤损，再查清敌营的位置。",true)},
                new[]{new NpcReply("处理战果",()=>{if(owner.Change(next=>{BaseDemoDefense.AcknowledgeBatu(next);},"已记录敌军威胁")){ClosePanel();owner.RefreshDemoRegion();if(BaseBattleLoot.Pending(owner.State))owner.Hud.OpenLoot();else owner.Hud.OpenDemoStory();}})});
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


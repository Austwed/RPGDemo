using System;
using System.IO;
using System.Linq;
using System.Text;
using BaseConquest.Base;
using BaseConquest.Battle;
using BaseConquest.RealtimePrototype;
using UnityEditor;
using UnityEngine;

namespace BaseConquest.Editor
{
    public static class VerifyDemoCheckpoint
    {
        private static void Need(bool yes,string reason){if(!yes)throw new InvalidOperationException(reason);}
        public static void VerifySecondRules(){VerifySecondOutcome();VerifySecondFailure();}
        public static void VerifySecondFailure()
        {
            var asset=Resources.Load<TextAsset>("DemoStages/defense-male");Need(asset!=null,"缺少首防阶段档");
            var state=JsonUtility.FromJson<BaseState>(asset.text);var first=BaseDemoDefense.Begin(state,"none");BattleScenePlacement.Start(state,first);
            var firstRun=new RealtimeCampaignBattle(first);BattleScenePlacement.Snap(state,firstRun);BaseDemoDefense.Configure(firstRun);
            foreach(var enemy in firstRun.Model.Units.Where(u=>u.Side==PrototypeSide.Enemy))enemy.Escaped=true;
            firstRun.Model.SetPaused(false);firstRun.Tick(.02);Need(firstRun.Battle.Report?.Outcome==BattleOutcome.SideAWin,"首防夹具未获胜");
            Need(BaseDemoDefense.Complete(state,firstRun),"首防结算夹具失败");BaseDemoDefense.AcknowledgeBatu(state);
            var advance=typeof(BaseEconomy).GetMethod("CompleteProductionDay",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic);
            Need(advance!=null,"缺少日结入口");while(state.Day<BaseDemoDefense.SecondArrival(state))advance.Invoke(null,new object[]{state,false});
            state.Campaign.Location="perimeter";var marker=BaseSpatial.Objects(state,"perimeter").Single(o=>o.Id=="demo_defense_start");state.PlayerX=marker.X;state.PlayerY=marker.Y;BaseAdventure.Arrive(state);
            var second=BaseDemoDefense.Begin(state,"none");BattleScenePlacement.Start(state,second);var run=new RealtimeCampaignBattle(second);BattleScenePlacement.Snap(state,run);BaseDemoDefense.Configure(run);
            Need(!run.RetreatAll()&&run.Battle.Phase==BattlePhase.Fighting,"第二波撤退入口未被逻辑层阻止");
            foreach(var ally in run.Model.Units.Where(u=>u.Side==PrototypeSide.Ally)){ally.Health=1;ally.KnockedDown=true;var soldier=run.Battle.Soldier(ally.Id-1);soldier.Health=1;soldier.KnockedDown=true;soldier.DeathResolved=true;}
            run.Model.SetPaused(false);run.Tick(.02);Need(run.Battle.Report?.Outcome==BattleOutcome.SideBWin,"第二波我方全倒未判失败");
            Need(BaseDemoCombat.Fail(state,run)&&state.Aftermath.GameOver&&state.Demo.Defense.SecondOutcome=="failed","第二波失败未进入游戏结束状态");
            BaseEconomy.Validate(state);
            var legacy=state.Copy();legacy.Demo.Defense.SecondOutcome="damaged";bool rejected=false;try{BaseEconomy.Validate(legacy);}catch(ArgumentException){rejected=true;}
            Need(rejected,"旧的第二波受创状态应判为不兼容");
            string folder=Path.GetFullPath("Logs/checkpoint-verification/second-legacy");Directory.CreateDirectory(folder);
            string obsolete=Path.Combine(folder,"slot1.json"),valid=Path.Combine(folder,"slot2.json");
            File.WriteAllText(obsolete,JsonUtility.ToJson(new BaseSaveDocument{DemoSaveVersion=BaseDemo.SaveVersion,State=legacy}));
            File.WriteAllText(valid,JsonUtility.ToJson(new BaseSaveDocument{DemoSaveVersion=BaseDemo.SaveVersion,State=state}));
            new BaseSaveCatalog(folder,d=>JsonUtility.ToJson(d),json=>JsonUtility.FromJson<BaseSaveDocument>(json),true);
            Need(!File.Exists(obsolete)&&File.Exists(valid),"旧档清理或有效档保留不符合预期");
            Directory.CreateDirectory("Logs/checkpoint-verification");File.WriteAllText("Logs/checkpoint-verification/second-failure.txt","PASS: actual second-wave setup, no retreat, defeat game over, isolated legacy cleanup and valid save preserved.\n");
        }
        public static void VerifySecondOutcome()
        {
            var asset=Resources.Load<TextAsset>("DemoStages/defense-male");Need(asset!=null,"缺少首防阶段档");
            var state=JsonUtility.FromJson<BaseState>(asset.text);var session=BaseDemoDefense.Begin(state,"none");BattleScenePlacement.Start(state,session);
            var run=new RealtimeCampaignBattle(session);BattleScenePlacement.Snap(state,run);
            session.Opening.EventId=BaseDemoDefense.SecondEventId;run.Battle.Scenario.Objective.HoldRounds=0;BaseDemoDefense.Configure(run);
            var allies=run.Model.Units.Where(u=>u.Side==PrototypeSide.Ally&&u.Alive).ToArray();var enemies=run.Model.Units.Where(u=>u.Side==PrototypeSide.Enemy&&u.Alive).ToArray();
            Need(allies.Length>0&&enemies.Length>0,"第二波判定夹具缺少双方单位");
            Need(run.OutcomeResolver(run)==BattleOutcome.None,"第二波不应因坚守时间结束获胜");
            foreach(var enemy in enemies)enemy.KnockedDown=true;
            Need(run.OutcomeResolver(run)==BattleOutcome.SideAWin,"第二波敌军全部失能后应获胜");
            foreach(var enemy in enemies)enemy.KnockedDown=false;foreach(var ally in allies)ally.KnockedDown=true;
            Need(run.OutcomeResolver(run)==BattleOutcome.SideBWin,"第二波我方全部失能后应失败");
            Directory.CreateDirectory("Logs/checkpoint-verification");File.WriteAllText("Logs/checkpoint-verification/second-outcome.txt","PASS: no timed victory, enemy elimination, ally defeat.\n");
        }
        [MenuItem("SLG/其他工具/验证/Demo阶段与首防检查点")]
        public static void Run()
        {
            var report=new StringBuilder();Directory.CreateDirectory("Logs/checkpoint-verification");
            foreach(string gender in new[]{"male","female"})
            {
                foreach(string stage in new[]{"camp","defense"})
                {
                    var asset=Resources.Load<TextAsset>("DemoStages/"+stage+"-"+gender);Need(asset!=null,"缺少阶段档");
                    var state=JsonUtility.FromJson<BaseState>(asset.text);BaseEconomy.Validate(state);
                    var store=new BaseSaveStore(Path.GetFullPath("Logs/checkpoint-verification/"+stage+"-"+gender+".json"),d=>JsonUtility.ToJson(d),json=>JsonUtility.FromJson<BaseSaveDocument>(json),true);
                    store.Save(state);var loaded=store.Load();Need(loaded.Demo.Intro.Hero==gender,"阶段档性别错误");
                    if(stage=="camp"){report.AppendLine(stage+"-"+gender+": PASS stage disk roundtrip");continue;}
                    var session=BaseDemoDefense.Begin(loaded,"none");BattleScenePlacement.Start(loaded,session);
                    Need(session.Battle.Soldiers.Count(u=>u.Side==BattleSide.B&&u.Kind==SoldierKind.Hero)==1&&session.Battle.Commander(BattleSide.B,0).ProfessionKind==SoldierKind.Ranged,"首防巴图须为唯一弓兵队长");
                    var run=new RealtimeCampaignBattle(session);BattleScenePlacement.Snap(loaded,run);BaseDemoDefense.Configure(run);
                    run.Model.SetPaused(false);
                    for(int step=0;step<600&&!run.Model.Units.Any(u=>u.Moving&&u.Progress>0);step++)run.Tick(.02);
                    Need(run.Model.Units.Any(u=>u.Moving&&u.Progress>0),"未捕获移动中状态");
                    run.SaveCheckpoint(loaded);store.Save(loaded);
                    var restored=store.Load();var resumed=new RealtimeCampaignBattle(new BaseCampaignSession(restored.Campaign.ActiveBattle));BaseDemoDefense.Configure(resumed);
                    Need(resumed.Model.Paused,"恢复后须暂停");Need(resumed.Model.Units.Count==run.Model.Units.Count,"恢复人数变化");
                    foreach(var unit in run.Model.Units)
                    {
                        var copy=resumed.Model.Units.Single(u=>u.Id==unit.Id);
                        Need((copy.Position-unit.Position).Length<1e-6&&copy.Cell==unit.Cell&&copy.NextCell==unit.NextCell&&Math.Abs(copy.Progress-unit.Progress)<1e-6,"恢复位置或预约变化");
                    }
                    Need(restored.Day==loaded.Day&&restored.Inventory.Food==loaded.Inventory.Food,"恢复重复日结或扣粮");
                    resumed.Model.SetPaused(false);for(int step=0;step<60;step++)resumed.Tick(.02);
                    resumed.SaveCheckpoint(restored);store.Save(restored);store.Load();
                    report.AppendLine(stage+"-"+gender+": PASS stage disk roundtrip, first-defense moving checkpoint disk roundtrip, pause, positions, reservations, food/day, resumed simulation and second save");
                    // Exercise roster construction with both documented fate inputs.
                    // This verifies configuration branches, not the narrative UI.
                    int huoyaTroops=-1;
                    foreach(string fate in new[]{"escaped","dead"})
                    {
                        var branch=state.Copy();branch.Demo.Stage=BaseDemoStage.DefenseWon;branch.Demo.Defense.BatuFate=fate;
                        BaseDemoDefense.AcknowledgeBatu(branch);
                        var force=branch.Demo.Defense.MainForce;var captains=force.Where(u=>u.Kind==SoldierKind.Hero).ToArray();
                        Need(captains.Length==(fate=="dead"?2:3),"巴图缺席不得补队长");
                        Need(captains.Single(u=>u.PresetSlot==0).Stats.ProfessionKind==SoldierKind.Cavalry,"大当家须为骑兵");
                        Need(captains.Single(u=>u.PresetSlot==2).Stats.ProfessionKind==SoldierKind.Melee,"豁牙须为近战");
                        if(fate=="escaped")Need(captains.Single(u=>u.PresetSlot==1).Stats.ProfessionKind==SoldierKind.Ranged,"主力巴图须为弓兵");
                        int troops=force.Count(u=>u.Kind!=SoldierKind.Hero&&u.CommanderSlot==2);
                        if(huoyaTroops<0)huoyaTroops=troops;else Need(troops==huoyaTroops,"巴图缺席不得增加豁牙带兵量");
                        Need(branch.Campaign.Layouts.Single(m=>m.Region=="base").Objects.Single(o=>o.Id=="demo_merchant").AssetId=="merchant","行商须使用商人形象");
                        report.AppendLine(stage+"-"+gender+" "+fate+": PASS first-raid captain, main-force professions, captain absence, Huoya troop count, merchant appearance (configuration branch)");
                    }
                }
            }
            File.WriteAllText("Logs/checkpoint-verification/report.txt",report.ToString());Debug.Log("[VerifyDemoCheckpoint] PASS");
        }
    }
}

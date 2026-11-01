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
        public static void VerifyQuestSword()
        {
            const string sword="royal_mithril_sword";
            var asset=Resources.Load<TextAsset>("DemoStages/camp-male");Need(asset!=null,"缺少营地阶段档");
            foreach(bool full in new[]{false,true})
            {
                var state=JsonUtility.FromJson<BaseState>(asset.text);
                if(full)for(int cell=0;cell<BasePartyInventory.Capacity;cell++)state.Backpack.Items.Add(new BaseBagItem {Id="bag:"+state.Backpack.NextId++,ItemId=BaseBackpack.Blueprint,Count=1,Holder=state.PlayerLeaderId,Cell=cell});
                Need(BaseDemo.AcknowledgeWaking(state),"苏醒交付失败");
                var item=state.Backpack.Items.Single(i=>i.ItemId==sword);
                Need(item.Holder==(full?"":state.PlayerLeaderId)&&item.Count==1,"任务道具交付位置错误");
                Need(!BaseDemo.AcknowledgeWaking(state)&&BaseBackpack.Count(state,sword)==1,"重复确认造成重复交付");
                Need(!BasePartyInventory.Destroy(state,item.Id,1),"任务道具能够销毁");
                BaseBackpack.Validate(state);BasePartyInventory.Validate(state);
                var store=new BaseSaveStore(Path.GetFullPath("Logs/checkpoint-verification/quest-sword-"+full+".json"),d=>JsonUtility.ToJson(d),json=>JsonUtility.FromJson<BaseSaveDocument>(json),true);
                store.Save(state);var restored=store.Load();Need(BaseBackpack.Count(restored,sword)==1&&restored.Backpack.Items.Single(i=>i.ItemId==sword).Holder==item.Holder,"任务道具存读后变化");
                if(!full)
                {
                    Need(BasePartyInventory.Move(state,item.Id,"",-1),"任务道具不能存入仓库");
                }
                var companion=state.Leaders.Single(l=>l.Id==state.PlayerLeaderId).Copy();companion.Id="test_companion";companion.PersonalEquipment.Items.Clear();companion.MaxHealth=BasePersonalEquipment.Stats(companion).Health;companion.Health=Math.Min(companion.Health,companion.MaxHealth);companion.PersonalEquipment.ExtraAccessory=false;state.Leaders.Add(companion);
                Need(BasePartyInventory.AtWarehouse(state)&&BasePartyInventory.People(state,true).Any(l=>l.Id==companion.Id)&&BasePartyInventory.AccessibleLeader(state,companion),"测试队友或仓库不可访问");
                Need(!BasePartyInventory.Move(state,item.Id,companion.Id,0),"任务道具可转交队友");
                if(!full)
                {
                    Need(!BasePartyInventory.QuickTransfer(state,item.Id,companion.Id,true),"任务道具可快捷转交队友");
                    state.Leaders.Remove(companion);
                    Need(BasePartyInventory.QuickTransfer(state,item.Id,state.PlayerLeaderId,true),"任务道具不能快捷取回主角背包");
                    Need(!BasePartyInventory.Move(state,item.Id,state.PlayerLeaderId,-1,"weapon"),"任务道具可装备");
                    state.Leaders.Add(companion);
                }
                item.Holder=companion.Id;item.Cell=0;bool rejected=false;try{BasePartyInventory.Validate(state);}catch(ArgumentException){rejected=true;}Need(rejected,"错误的队友持有未被存档校验拒绝");
            }
            Debug.Log("VerifyQuestSword PASS");
        }
        public static void VerifyGlobalDifficulty()
        {
            const string key="BaseConquest.GlobalDifficulty";bool had=PlayerPrefs.HasKey(key);string previous=PlayerPrefs.GetString(key,"");
            try
            {
                var asset=Resources.Load<TextAsset>("DemoStages/defense-male");Need(asset!=null,"缺少首防阶段档");
                var state=JsonUtility.FromJson<BaseState>(asset.text);var session=BaseDemoDefense.Begin(state,"none");BattleScenePlacement.Start(state,session);
                var run=new RealtimeCampaignBattle(session);BattleScenePlacement.Snap(state,run);
                var attack=run.Battle.Soldiers.First(s=>s.Side==BattleSide.A);var target=run.Battle.Soldiers.First(s=>s.Side==BattleSide.B);
                var damage=typeof(RealtimeCampaignBattle).GetMethod("RpgDamage",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);
                var incoming=typeof(RealtimeCampaignBattle).GetMethod("DifficultyIncoming",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);
                Need(damage!=null&&incoming!=null,"缺少伤害或侵蚀难度读取入口");
                double Hit(BattleSoldier a,BattleSoldier b)=>(double)damage.Invoke(run,new object[]{100d,1,1d,a,b});
                double Exposure()=>(double)incoming.Invoke(run,new object[]{BattleSide.A});
                var settings=new BaseDifficulty();settings.Set("normal");double normalHit=Hit(attack,target),normalTaken=Hit(target,attack);
                var copied=session.Opening.Difficulty.Copy();settings.Set("easy");
                Need(copied.Preset=="easy"&&Math.Abs(Hit(attack,target)/normalHit-2)<.0001&&Math.Abs(Hit(target,attack)/normalTaken-.5)<.0001&&Math.Abs(Exposure()-.5)<.0001,"简单难度未在当前战斗即时生效");
                settings.Set("hard");Need(Math.Abs(Hit(attack,target)/normalHit-1)<.0001&&Math.Abs(Hit(target,attack)/normalTaken-1.3)<.0001&&Math.Abs(Exposure()-1.3)<.0001,"困难难度未在当前战斗即时生效");
                Need(!JsonUtility.ToJson(state.Aftermath.Difficulty).Contains("\"Preset\"")&&!JsonUtility.ToJson(session.Opening.Difficulty).Contains("\"Preset\""),"难度档位仍写入存档或战斗快照");
                var legacy=JsonUtility.FromJson<BaseDifficulty>("{\"Version\":1,\"Preset\":\"easy\"}");Need(legacy.Preset=="hard","旧档难度覆盖了全局设置");
                Directory.CreateDirectory("Logs/checkpoint-verification");File.WriteAllText("Logs/checkpoint-verification/global-difficulty.txt","PASS: live damage and exposure factors, global setting, legacy save ignored, no serialized preset.\n");
            }
            finally{if(had)PlayerPrefs.SetString(key,previous);else PlayerPrefs.DeleteKey(key);PlayerPrefs.Save();}
        }
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

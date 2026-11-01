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

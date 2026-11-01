using System;
using System.Linq;
using System.IO;
using BaseConquest.Base;
using BaseConquest.Battle;
using BaseConquest.RealtimePrototype;
using UnityEditor;
using UnityEngine;

namespace BaseConquest.Editor
{
    internal static class VerifyStandaloneBattleFixture
    {
        [MenuItem("SLG/其他工具/验证/独立即时战斗夹具")]
        private static void Verify()
        {
            var stage=Resources.Load<TextAsset>("DemoStages/defense-male");
            if(stage==null)throw new InvalidOperationException("缺少防守阶段档");
            var state=JsonUtility.FromJson<BaseState>(stage.text);
            string original=JsonUtility.ToJson(state);
            var report=new System.Text.StringBuilder();
            foreach(BaseStandaloneBattlePreset preset in Enum.GetValues(typeof(BaseStandaloneBattlePreset)))
            {
                var session=BaseStandaloneBattle.Create(state,preset);
                var run=new RealtimeCampaignBattle(session);
                if(session.Battle.Phase!=BattlePhase.Fighting||run.Model.Units.Count!=18)throw new InvalidOperationException("未进入 18 人即时测试场");
                if(preset==BaseStandaloneBattlePreset.CavalryVsArchers&&
                    (session.Battle.Soldiers.Count(unit=>unit.Side==BattleSide.A&&unit.Kind==SoldierKind.Cavalry)!=8||session.Battle.Soldiers.Count(unit=>unit.Side==BattleSide.B&&unit.Kind==SoldierKind.Ranged)!=8))throw new InvalidOperationException("默认兵种编制错误");
                if(preset==BaseStandaloneBattlePreset.Swapped&&
                    (session.Battle.Soldiers.Count(unit=>unit.Side==BattleSide.A&&unit.Kind==SoldierKind.Ranged)!=8||session.Battle.Soldiers.Count(unit=>unit.Side==BattleSide.B&&unit.Kind==SoldierKind.Cavalry)!=8))throw new InvalidOperationException("互换兵种编制错误");
                if(preset==BaseStandaloneBattlePreset.Mixed&&session.Battle.Soldiers.Count(unit=>unit.Kind!=SoldierKind.Hero)!=16)throw new InvalidOperationException("混编总数错误");
                run.Model.SetPaused(false);
                for(int step=0;step<600&&!run.Model.Units.Any(u=>u.Moving&&u.Progress>0);step++)run.Model.Tick(.02);
                if(!run.Model.Units.Any(u=>u.Moving&&u.Progress>0))throw new InvalidOperationException("未捕获移动中检查点");
                var checkpoint=JsonUtility.FromJson<RealtimeBattleState>(JsonUtility.ToJson(run.CaptureState()));
                run.Model.Restore(checkpoint,RealtimeBattlePrototype.RpgAttackDamageLimit);
                if(run.Model.Units.Count!=checkpoint.Units.Count)throw new InvalidOperationException("即时检查点恢复人数错误");
                if(run.Model.Units.Any(u=>(u.Position-checkpoint.Units.Single(saved=>saved.Id==u.Id).Position).Length>1e-8))throw new InvalidOperationException("移动中位置恢复不一致");
                var invalid=checkpoint.Copy();var unit=invalid.Units.First(u=>u.Alive);
                unit.Position=new PrototypePoint(unit.Position.X+.1,unit.Position.Z);
                try {run.Model.Restore(invalid,RealtimeBattlePrototype.RpgAttackDamageLimit);throw new InvalidOperationException("非法插值未被拒绝");}
                catch(ArgumentException error){if(!error.Message.Contains("即时插值与预约不一致"))throw;}
                invalid=checkpoint.Copy();var foot=invalid.Units.Where(u=>u.Alive&&u.Role==PrototypeRole.Archer).Take(2).ToArray();
                if(foot.Length!=2)throw new InvalidOperationException("缺少步兵检查点夹具");
                foot[1].Cell=foot[0].Cell;foot[1].Position=run.Model.Arena.Center(foot[0].Cell);foot[1].Facing=foot[0].Facing;foot[1].NextCell=-1;foot[1].Progress=0;
                try {run.Model.Restore(invalid,RealtimeBattlePrototype.RpgAttackDamageLimit);throw new InvalidOperationException("重叠预约未被拒绝");}
                catch(ArgumentException error){if(!error.Message.Contains("即时预约重叠"))throw;}
                invalid=checkpoint.Copy();foreach(var actor in invalid.Units)actor.Escaped=true;
                foot=invalid.Units.Where(u=>u.Role==PrototypeRole.Archer).Take(2).ToArray();
                var arena=run.Model.Arena;int width=arena.FineColumns;
                int square=Enumerable.Range(0,width*(arena.FineRows-1)).First(cell=>cell%width<width-1&&arena.CanStep(cell,cell+width+1)&&arena.CanStep(cell+1,cell+width));
                for(int n=0;n<2;n++)
                {
                    var actor=foot[n];actor.Escaped=false;actor.Cell=square+n;actor.NextCell=square+width+1-n;actor.Progress=0;
                    actor.Position=arena.Center(actor.Cell);actor.Goal=actor.Anchor=arena.Center(actor.NextCell);
                }
                try {run.Model.Restore(invalid,RealtimeBattlePrototype.RpgAttackDamageLimit);throw new InvalidOperationException("交叉预约未被拒绝");}
                catch(ArgumentException error){if(!error.Message.Contains("即时检查点含穿体或交叉预约"))throw;}
                report.AppendLine(preset+": PASS JSON moving-state roundtrip, position, interpolation rejection, reservation rejection, crossing rejection");
                var full=new RealtimeCampaignBattle(BaseStandaloneBattle.Create(state,preset));full.Model.SetPaused(false);
                for(int step=0;step<1000&&full.Battle.Phase==BattlePhase.Fighting;step++)full.Tick(.1);
                if(full.Battle.Phase!=BattlePhase.Finished)throw new InvalidOperationException("独立战斗未在时限内结束："+preset);
                if(JsonUtility.ToJson(state)!=original)throw new InvalidOperationException("独立测试修改了输入剧情状态："+preset);
                report.AppendLine(preset+": PASS full Demo-rule battle; outcome="+full.Battle.Report.Outcome+"; seconds="+full.Model.Elapsed+"; story state unchanged");
            }
            Directory.CreateDirectory("Logs");File.WriteAllText("Logs/standalone-battle-verification.txt",report.ToString());
            Debug.Log("[VerifyStandaloneBattleFixture] PASS: default, swapped, mixed; all use RealtimeCampaignBattle and no save store.");
        }
    }
}

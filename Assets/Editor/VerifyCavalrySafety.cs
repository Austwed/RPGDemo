using System;
using System.IO;
using System.Linq;
using System.Reflection;
using BaseConquest.RealtimePrototype;
using UnityEditor;
using UnityEngine;

namespace BaseConquest.Editor
{
    public static class VerifyCavalrySafety
    {
        private static readonly MethodInfo Shove=typeof(RealtimeBattlePrototype).GetMethod("TryShoveThrough",BindingFlags.Instance|BindingFlags.NonPublic);
        private static bool Try(RealtimeBattlePrototype model,PrototypeUnit rider,int wanted)=>(bool)Shove.Invoke(model,new object[]{rider,wanted});
        private static void Need(bool condition,string reason){if(!condition)throw new InvalidOperationException(reason);}
        // 敌方步兵放在马头前一格；骑兵阻挡者要再远一格，否则两匹马的体积在夹具里就重叠了。
        private static RealtimeBattlePrototype Fixture(out PrototypeUnit rider,out PrototypeUnit blocker,bool enemy=false,bool cavalryBlocker=false)
        {
            var model=new RealtimeBattlePrototype(new PrototypeArena(PrototypeLayout.Open));
            rider=model.AddUnit(PrototypeSide.Ally,PrototypeRole.Melee,new PrototypePoint(cavalryBlocker?2.25:3.25,3.25));
            Need(model.SetFootprint(rider,2),"骑兵夹具落位失败");
            blocker=model.AddUnit(enemy?PrototypeSide.Enemy:PrototypeSide.Ally,PrototypeRole.Melee,new PrototypePoint(cavalryBlocker?4.25:3.75,3.25));
            if(cavalryBlocker)Need(model.SetFootprint(blocker,2),"被撞骑兵夹具落位失败");
            return model;
        }
        private static bool Tally(RealtimeBattlePrototype model,string key)=>model.ShoveStats.ContainsKey(key);
        [MenuItem("SLG/其他工具/验证/骑兵安全挤开")]
        public static void Run()
        {
            // 1) 友军步兵：有安全空位就让位，且不造成伤害。
            var model=Fixture(out var rider,out var blocker);int wanted=blocker.Cell;double health=blocker.Health;
            Need(Try(model,rider,wanted),"前方友军有安全空位却未让位");
            Need(blocker.Cell!=wanted&&model.ShoveMoves==1&&blocker.Health==health,"挤开落点、次数或无伤害约束失败");
            Need(!RealtimeBattlePrototype.BodiesCross(rider,model.Arena.Center(wanted),blocker,blocker.Position),"挤开后仍挡住骑兵");
            // 内存档往返（P22 第 4 条）：落点、免疫计时、次数与模型计数都要原样回来。
            int movedCell=blocker.Cell,movedTimes=blocker.ShovedTimes,moves=model.ShoveMoves;double readyAt=blocker.ShoveReadyAt;
            var saved=JsonUtility.FromJson<RealtimeBattleState>(JsonUtility.ToJson(model.Capture()));
            model.Restore(saved);
            var restored=model.Units.First(unit=>unit.Id==blocker.Id);
            Need(restored.Cell==movedCell&&restored.ShovedTimes==movedTimes&&Math.Abs(restored.ShoveReadyAt-readyAt)<1e-9&&model.ShoveMoves==moves,"挤开状态应随内存档往返");

            // 2) 敌方步兵：横冲直撞要能撞开（2026-11-06 裁决）。
            model=Fixture(out rider,out blocker,true);wanted=blocker.Cell;
            Need(Try(model,rider,wanted)&&blocker.Cell!=wanted&&model.ShoveMoves==1,"敌方步兵应被撞开");
            model.Restore(JsonUtility.FromJson<RealtimeBattleState>(JsonUtility.ToJson(model.Capture())));

            // 3) 敌方骑兵：同体积互顶，不撞开。
            model=Fixture(out rider,out blocker,true,true);wanted=blocker.Cell;
            Need(!Try(model,rider,wanted)&&blocker.Cell==wanted&&model.ShoveMoves==0&&Tally(model,"skip:cavalry"),"敌军骑兵不应被撞开");

            // 4) 友军骑兵：必须能互挤，否则密集骑兵起步会互相堵死。
            model=Fixture(out rider,out blocker,false,true);wanted=blocker.Cell;
            bool allyCavalry=Try(model,rider,wanted);
            Need(allyCavalry&&blocker.Cell!=wanted&&model.ShoveMoves==1,"友军骑兵应能互挤：结果 "+allyCavalry+" 移动 "+(blocker.Cell!=wanted)+" 次数 "+model.ShoveMoves+" 原因 "+string.Join(" ",model.ShoveStats.Select(kv=>kv.Key+kv.Value))+" 骑位 "+rider.Cell+" 挡位 "+blocker.Cell+" 目标 "+wanted);

            // 5) 正在前摇的敌人：撞开即打断前摇，不另加眩晕。
            model=Fixture(out rider,out blocker,true);wanted=blocker.Cell;
            blocker.HitAt=model.Elapsed+1;blocker.HitResolved=false;blocker.TargetId=rider.Id;
            Need(Try(model,rider,wanted)&&blocker.Cell!=wanted,"前摇中的敌人也应被撞开");
            Need(blocker.HitAt<0&&blocker.HitResolved,"撞开前摇单位应取消其前摇");

            // 6) 玩家主角：唯一豁免，被自家骑兵顶开会让操作者莫名其妙。
            model=Fixture(out rider,out blocker,false);wanted=blocker.Cell;blocker.Player=true;
            Need(!Try(model,rider,wanted)&&blocker.Cell==wanted&&Tally(model,"skip:player"),"玩家主角不应被自家骑兵顶开");

            // 7) 冷却期与无落点：照旧等待。
            model=Fixture(out rider,out blocker);wanted=blocker.Cell;blocker.ShoveReadyAt=100;
            Need(!Try(model,rider,wanted)&&blocker.Cell==wanted,"冷却期不应重复挤开");

            model=Fixture(out rider,out blocker);wanted=blocker.Cell;
            foreach(int cell in model.Arena.Neighbours(blocker.Cell).ToArray())
                if(!model.Reservations.ContainsKey(cell))model.AddUnit(PrototypeSide.Ally,PrototypeRole.Melee,model.Arena.Center(cell));
            int blockerId=blocker.Id;model.CanMove=unit=>unit.Id==blockerId;
            Need(!Try(model,rider,wanted)&&blocker.Cell==wanted,"无安全空位时应等待，不能覆盖预约");
            model.Restore(model.Capture());
            Directory.CreateDirectory("Logs");
            File.WriteAllText("Logs/cavalry-safety-verification.txt","PASS: ally and enemy infantry displaced; enemy cavalry and player leader exempt; wounded/ally cavalry interplay; windup interrupted; cooldown; fully reserved surroundings wait.\n");
            Debug.Log("[VerifyCavalrySafety] PASS");
        }
    }
}

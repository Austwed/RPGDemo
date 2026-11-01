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
        private static RealtimeBattlePrototype Fixture(out PrototypeUnit rider,out PrototypeUnit blocker,bool enemy=false)
        {
            var model=new RealtimeBattlePrototype(new PrototypeArena(PrototypeLayout.Open));
            rider=model.AddUnit(PrototypeSide.Ally,PrototypeRole.Melee,new PrototypePoint(3.25,3.25));
            Need(model.SetFootprint(rider,2),"骑兵夹具落位失败");
            blocker=model.AddUnit(enemy?PrototypeSide.Enemy:PrototypeSide.Ally,PrototypeRole.Melee,new PrototypePoint(3.75,3.25));
            return model;
        }
        [MenuItem("SLG/其他工具/验证/骑兵安全挤开")]
        public static void Run()
        {
            var model=Fixture(out var rider,out var blocker);int wanted=blocker.Cell;double health=blocker.Health;
            Need(Try(model,rider,wanted),"前方友军有安全空位却未让位");
            Need(blocker.Cell!=wanted&&model.ShoveMoves==1&&blocker.Health==health,"挤开落点、次数或无伤害约束失败");
            Need(!RealtimeBattlePrototype.BodiesCross(rider,model.Arena.Center(wanted),blocker,blocker.Position),"挤开后仍挡住骑兵");
            model.Restore(JsonUtility.FromJson<RealtimeBattleState>(JsonUtility.ToJson(model.Capture())));

            model=Fixture(out rider,out blocker,true);wanted=blocker.Cell;
            Need(!Try(model,rider,wanted)&&blocker.Cell==wanted&&model.ShoveMoves==0,"不能挤开敌军");

            model=Fixture(out rider,out blocker);wanted=blocker.Cell;blocker.ShoveReadyAt=100;
            Need(!Try(model,rider,wanted)&&blocker.Cell==wanted,"冷却期不应重复挤开");

            model=Fixture(out rider,out blocker);wanted=blocker.Cell;
            foreach(int cell in model.Arena.Neighbours(blocker.Cell).ToArray())
                if(!model.Reservations.ContainsKey(cell))model.AddUnit(PrototypeSide.Ally,PrototypeRole.Melee,model.Arena.Center(cell));
            int blockerId=blocker.Id;model.CanMove=unit=>unit.Id==blockerId;
            Need(!Try(model,rider,wanted)&&blocker.Cell==wanted,"无安全空位时应等待，不能覆盖预约");
            model.Restore(model.Capture());
            Directory.CreateDirectory("Logs");
            File.WriteAllText("Logs/cavalry-safety-verification.txt","PASS: safe ally displacement; no damage; post-shove JSON restore; enemy not displaced; cooldown; fully reserved surroundings wait.\n");
            Debug.Log("[VerifyCavalrySafety] PASS");
        }
    }
}

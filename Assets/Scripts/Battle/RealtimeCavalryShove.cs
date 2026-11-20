using System;
using System.Collections.Generic;
using System.Linq;
namespace BaseConquest.RealtimePrototype
{
    public sealed partial class PrototypeUnit
    {
        // 被骑兵挤开后的静置期：防止同一单位被反复推搡造成抖动。
        public bool Mounted;
        public double ShoveReadyAt;
        public int ShovedTimes;
    }
    public sealed partial class RealtimeBattlePrototype
    {
        public bool CavalryPathPriority=true;
        public bool CavalryShove=true;
        public bool ShoveEnemies=true;
        public double ShoveBlockedDelay=.15;    // 堵住这么久之后才允许挤人，避免一有擦碰就乱推
        public double ShoveCooldown=.5;         // 同一单位被顶之后的免疫时间
        public int ShoveMoves;                  // 挤开次数（诊断用）
        // 挤开失败原因计数，只在编辑器标定台里读，不影响玩法
        public readonly Dictionary<string,int> ShoveStats=new Dictionary<string,int>();
        private void Tally(string key){ShoveStats.TryGetValue(key,out int n);ShoveStats[key]=n+1;}

        public static bool IsCavalry(PrototypeUnit unit)=>unit!=null&&unit.Mounted;

        // wanted 是骑兵下一步想踏进去的格。挡路有三种：占着格子、挡住扫掠体、挡住转向后的身体。
        private bool TryShoveThrough(PrototypeUnit rider,int wanted)
        {
            if(!CavalryShove||!IsCavalry(rider))return false;
            Tally("enter");
            if(wanted<0||!Arena.FineWalkable(wanted)){Tally("badcell");return false;}
            var landing=Arena.Center(wanted);
            var heading=(landing-rider.Position).Normalized;
            if(!Fits(rider,wanted,heading,false)||!Fits(rider,rider.Cell,heading,false)){Tally("terrain");return false;}
            var claimed=new HashSet<int>(Footprint(rider,wanted,heading).Where(cell=>cell>=0));
            var targets=new List<PrototypeUnit>();
            void Consider(PrototypeUnit unit){if(unit!=null&&unit.Id!=rider.Id&&!targets.Contains(unit))targets.Add(unit);}
            foreach(int cell in claimed)if(reservations.TryGetValue(cell,out int owner)&&owner!=rider.Id)Consider(Units.FirstOrDefault(unit=>unit.Id==owner));
            foreach(var other in Units.Where(unit=>unit.Alive&&unit.Id!=rider.Id))
            {
                var otherPoint=other.Moving?Arena.Center(other.NextCell):other.Position;
                // 格子是空的，但邻居的身体挡住了马扫过去的那一瞬
                if(BodiesCross(rider,landing,other,otherPoint)){Consider(other);Tally("sweep");}
            }
            // 转向本身也会撞：马身摆过去与侧后方的人相交，起步在 Face 这一步就被否掉。
            // 用副本算转向后的体积，不动真单位。
            var probe=rider.Copy();probe.Facing=(landing-rider.Position).Normalized;probe.SetBodyAxis(PrototypeUnit.MajorAxis(probe.Facing,rider.BodyAxis));
            foreach(var other in Units.Where(unit=>unit.Alive&&unit.Id!=rider.Id))
                if(BodiesCross(probe,probe.Position,other,other.Moving?Arena.Center(other.NextCell):other.Position)){Consider(other);Tally("turn");}
            // 斜向起步时还要占住转角两侧的格子（与 TryBeginStep 的侧格校验一致）。这两格被占时
            // 原来的收集漏掉了它们，表现为「前面明明有人却挤不开」，马原地不动。
            int dx=wanted%Arena.FineColumns-rider.Cell%Arena.FineColumns,dz=wanted/Arena.FineColumns-rider.Cell/Arena.FineColumns;
            if(dx!=0&&dz!=0)
                foreach(int side in new[]{rider.Cell+dx,rider.Cell+dz*Arena.FineColumns})
                    if(reservations.TryGetValue(side,out int sideOwner)&&sideOwner!=rider.Id)Consider(Units.FirstOrDefault(unit=>unit.Id==sideOwner));
            if(targets.Count==0){Tally("nocollision");return false;}
            foreach(var blocker in targets)if(TryShove(rider,blocker,claimed,wanted))return true;
            Tally("failed");return false;
        }
        private bool TryShove(PrototypeUnit rider,PrototypeUnit blocker,HashSet<int> claimed,int wanted)
        {
            if(blocker==null||!blocker.Alive||blocker.Moving||blocker.Id==rider.Id||blocker==rider){Tally("skip:busy");return false;}
            if(blocker.Side!=rider.Side&&!ShoveEnemies){Tally("skip:enemy");return false;}
            if(IsCavalry(blocker)){Tally("skip:cavalry");return false;}
            // 唯一豁免是玩家自己的主角：被自家骑兵顶开会让操作者莫名其妙。其余队长（含敌方）都能撞。
            if(blocker.Player){Tally("skip:player");return false;}
            if(Elapsed<blocker.ShoveReadyAt){Tally("skip:cooldown");return false;}
            if(!(CanMove?.Invoke(blocker)??true)){Tally("skip:immobile");return false;}
            var from=Arena.Center(blocker.Cell);
            var away=(from-rider.BodyCenter).Normalized;
            if(away.Length<.000001)away=(from-rider.Position).Normalized;
            if(away.Length<.000001)return false;
            var options=Arena.Neighbours(blocker.Cell)
                .Where(cell=>!claimed.Contains(cell)&&Shovable(blocker,cell))
                .Select(cell=>new {Cell=cell,Align=PrototypePoint.Dot((Arena.Center(cell)-from).Normalized,away)})
                // 只能往"离开推者"的方向顶，绝不能迎着马头顶；侧向允许，但优先级最低
                .Where(option=>option.Align>.05)
                .OrderByDescending(option=>option.Align).ThenBy(option=>option.Cell)
                .Select(option=>option.Cell).ToArray();
            if(options.Length==0)Tally("skip:nolanding");
            foreach(int cell in options)
            {
                var landing=Arena.Center(cell);
                // 顶过去之后不能与任何单位穿体，也不能继续挡着这一趟路
                if(Units.Any(other=>other.Alive&&other.Id!=blocker.Id&&BodiesCross(blocker,landing,other,other.Moving?Arena.Center(other.NextCell):other.Position))){Tally("skip:cross");continue;}
                // Letting go is instantaneous: test the rider's subsequent sweep
                // against the displaced body, not the body's already-vacated path.
                var displaced=blocker.Copy();displaced.Position=landing;displaced.Cell=cell;displaced.NextCell=-1;displaced.Progress=0;
                if(BodiesCross(rider,Arena.Center(wanted),displaced,landing)){Tally("skip:stillblocking");continue;}
                Displace(blocker,cell);Tally("shoved");return true;
            }
            return false;
        }
        private bool Shovable(PrototypeUnit blocker,int cell)
        {
            return Arena.FineWalkable(cell)&&(UnitCellAllowed?.Invoke(blocker,cell)??true)&&Fits(blocker,cell,blocker.Facing);
        }
        // 强制让位：被顶者一帧内跨过一整格，比它自己的移动速度更快（"被顶超速"），
        // 但落点与预约都按正常规则校验，检查点 Restore 的插值／穿体断言依然成立。
        private bool Displace(PrototypeUnit blocker,int cell)
        {
            if(blocker.HitAt>=0&&!blocker.HitResolved)CancelWindup(blocker);
            blocker.PreviousCell=blocker.Cell;blocker.Cell=cell;blocker.NextCell=-1;blocker.Progress=0;
            blocker.Position=Arena.Center(cell);blocker.BlockedFor=0;blocker.CarrySeconds=0;
            blocker.ShoveReadyAt=Elapsed+ShoveCooldown;blocker.ShovedTimes++;blocker.AttackSlot=-1;
            ShoveMoves++;RebuildReservations();return true;
        }
    }
}

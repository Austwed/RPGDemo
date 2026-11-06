using System;
using System.Collections.Generic;
using System.Linq;
namespace BaseConquest.RealtimePrototype
{
    public sealed partial class PrototypeUnit
    {
        // 被骑兵挤开后的静置期：防止同一单位被反复推搡造成抖动。
        public double ShoveReadyAt;
        public int ShovedTimes;
    }
    public sealed partial class RealtimeBattlePrototype
    {
        // 骑兵（FootprintLength==2）身体长约0.98格，前进要两格同时空出，起步时还要保证
        // 扫掠体不与任何单位相交。密集队形里它既排不开也绕不动：实测8骑冲8弓时，
        // 5/8的骑兵「被堵帧」占存活时间的75~80%，全程只走0.5~2格。
        // 这里给骑兵两项特权，都可用开关关闭：
        //   CavalryPathPriority —— 起步顺序提前，骑兵先挑格子；
        //   CavalryShove        —— 把挡路的单位顶开一格（被顶者瞬移，等同"被顶超速"）。
        public bool CavalryPathPriority=true;   // 起步顺序：骑兵先手，其余单位再从剩下的格子里挑
        // 默认开启（韦德 2026-11-01 拍板方向：接受「其他单位被顶超速」的代价）。
        // 2026-11-06 裁决：骑兵要能横冲直撞、直取高嘲讽目标，所以默认连敌方也撞。
        // 关掉只需把这一项置 false；证据与档位对比见 策划/战斗系统_骑兵挤开与寻路优先级_v1.md。
        public bool CavalryShove=true;          // 骑兵可以挤开挡路者
        public bool ShoveEnemies=true;          // 可撞敌方步兵与敌方队长（含正在前摇的敌人，撞开即打断）
        public bool ShoveEnemyCavalry=false;    // 敌军骑兵不互撞：两匹马同体积互顶，谁也顶不动谁
        public bool ShoveAllyCavalry=true;      // 友军骑兵必须能互挤，否则 8 骑起步会互相堵死
        public double ShoveBlockedDelay=.15;    // 堵住这么久之后才允许挤人，避免一有擦碰就乱推
        public double ShoveCooldown=.5;         // 同一单位被顶之后的免疫时间
        public int ShoveMoves;                  // 挤开次数（诊断用）
        // 挤开失败原因计数，只在编辑器标定台里读，不影响玩法
        public readonly Dictionary<string,int> ShoveStats=new Dictionary<string,int>();
        private void Tally(string key){ShoveStats.TryGetValue(key,out int n);ShoveStats[key]=n+1;}

        public static bool IsCavalry(PrototypeUnit unit)=>unit!=null&&unit.FootprintLength==2;

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
            // 敌我分开判：敌方步兵与队长可撞，敌方骑兵不撞；友军骑兵必须能互挤。
            if(blocker.Side!=rider.Side&&!ShoveEnemies){Tally("skip:enemy");return false;}
            if(IsCavalry(blocker)&&(blocker.Side==rider.Side?!ShoveAllyCavalry:!ShoveEnemyCavalry)){Tally("skip:cavalry");return false;}
            // 唯一豁免是玩家自己的主角：被自家骑兵顶开会让操作者莫名其妙。其余队长（含敌方）都能撞。
            if(blocker.Player){Tally("skip:player");return false;}
            if(Elapsed<blocker.ShoveReadyAt){Tally("skip:cooldown");return false;}
            if(!(CanMove?.Invoke(blocker)??true)){Tally("skip:immobile");return false;}
            var from=Arena.Center(blocker.Cell);
            var away=(from-rider.BodyCenter).Normalized;
            if(away.Length<.000001)away=(from-rider.Position).Normalized;
            if(away.Length<.000001)return false;
            var options=CandidateCells(blocker)
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
        // 步兵可以往八个方向让；马只能沿身体轴向平移，否则瞬移就等于凭空转向。
        // 平移量必须是一个身长（2 个细格 = 1 个粗格）：只挪半个身长的话落点仍与推者重叠，
        // 表现为「友军骑兵永远挤不开」，两匹马卡死在原地。
        private IEnumerable<int> CandidateCells(PrototypeUnit blocker)
        {
            if(!IsCavalry(blocker))return Arena.Neighbours(blocker.Cell);
            var axis=blocker.BodyAxis;var result=new List<int>();
            foreach(int sign in new[]{1,-1})
            {
                int x=blocker.Cell%Arena.FineColumns+(int)axis.X*sign*PrototypeArena.Subdivision;
                int z=blocker.Cell/Arena.FineColumns+(int)axis.Z*sign*PrototypeArena.Subdivision;
                if(x>=0&&x<Arena.FineColumns&&z>=0&&z<Arena.FineRows)result.Add(z*Arena.FineColumns+x);
            }
            return result;
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

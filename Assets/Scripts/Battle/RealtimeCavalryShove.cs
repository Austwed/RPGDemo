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
        // 关掉只需把这一项置 false；证据与档位对比见 策划/战斗系统_骑兵挤开与寻路优先级_v1.md。
        public bool CavalryShove=true;          // 骑兵可以挤开挡路者
        public bool ShoveAlliesOnly=true;       // true=只挤同阵营；false=连敌方也能撞开（会打断敌方前摇，强度很高）
        public bool ShoveCavalry=true;          // 是否允许把另一匹马也顶开（8骑互挤时必须允许）
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
            if(!Fits(rider,wanted,rider.Facing,false)){Tally("terrain");return false;}   // 地形本身不让去，挤人也没用
            var claimed=new HashSet<int>(Footprint(rider,wanted,rider.Facing).Where(cell=>cell>=0));
            var landing=Arena.Center(wanted);
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
            var probe=rider.Copy();probe.Facing=(landing-rider.Position).Normalized;
            foreach(var other in Units.Where(unit=>unit.Alive&&unit.Id!=rider.Id))
                if(BodiesCross(probe,probe.Position,other,other.Moving?Arena.Center(other.NextCell):other.Position)){Consider(other);Tally("turn");}
            if(targets.Count==0){Tally("nocollision");return false;}
            foreach(var blocker in targets)if(TryShove(rider,blocker,claimed,wanted))return true;
            Tally("failed");return false;
        }
        private bool TryShove(PrototypeUnit rider,PrototypeUnit blocker,HashSet<int> claimed,int wanted)
        {
            if(blocker==null||!blocker.Alive||blocker.Moving||blocker.Id==rider.Id||blocker==rider){Tally("skip:busy");return false;}
            if(ShoveAlliesOnly&&blocker.Side!=rider.Side){Tally("skip:enemy");return false;}
            if(IsCavalry(blocker)&&!ShoveCavalry){Tally("skip:cavalry");return false;}
            if(blocker.Role==PrototypeRole.Leader&&blocker.Strategy==PrototypeStrategy.Obey&&!blocker.ManualOrder){Tally("skip:leader");return false;}
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
                if(BodiesCross(rider,Arena.Center(wanted),blocker,landing)){Tally("skip:stillblocking");continue;}
                Displace(blocker,cell);Tally("shoved");return true;
            }
            return false;
        }
        // 步兵可以往八个方向让；马只能沿身体轴向整格平移，否则瞬移就等于凭空转向。
        private IEnumerable<int> CandidateCells(PrototypeUnit blocker)
        {
            if(!IsCavalry(blocker))return Arena.Neighbours(blocker.Cell);
            var axis=PrototypeUnit.Axis(blocker.Facing);var result=new List<int>();
            foreach(int sign in new[]{1,-1})
            {
                int x=blocker.Cell%Arena.FineColumns+(int)axis.X*sign, z=blocker.Cell/Arena.FineColumns+(int)axis.Z*sign;
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

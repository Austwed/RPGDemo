using System;
using System.Collections.Generic;
using System.Linq;

namespace BaseConquest.RealtimePrototype
{
    public enum PrototypeStrategy { Attack, Protect, Flee, Obey }
    public sealed partial class PrototypeUnit
    {
        public int DoctrineVersion, SquadTargetId=-1, PatrolGoal=-1;
        public PrototypeStrategy Strategy;
        public bool ManualOrder, ObjectOrder, DirectCaptain;
        [NonSerialized] public bool Steering;
        public bool SteeringStopped;
        public string ObjectTargetId="";
        public double EngagedUntil;
        public double GuardRadius=3, ThreatRadius=6, CommandRadius=9, NextPatrolAt;
    }
    public sealed partial class RealtimeBattlePrototype
    {
        public bool CommanderDoctrine;
        public bool BothSidesHuman;
        internal bool PatrolEnabled = true;
        internal bool OnlyExitMovement;
        public bool LeaderlessEnemyAssault;
        internal bool LeaderlessAllyAssault;
        internal bool IndependentAssault(PrototypeSide side) => side == PrototypeSide.Enemy ? LeaderlessEnemyAssault : LeaderlessAllyAssault;
        public Func<PrototypeUnit,PrototypeUnit,PrototypePoint,double> HeightRangeBonus;
        public Func<PrototypePoint,PrototypePoint,bool> HeightShotClear;
        public double ReachFor(PrototypeUnit actor,PrototypeUnit target,PrototypePoint? origin=null)=>actor.Reach+(HeightRangeBonus?.Invoke(actor,target,origin??actor.Position)??0);
        public bool ClearAttack(PrototypePoint origin,PrototypePoint target)=>HeightShotClear?.Invoke(origin,target)??double.IsPositiveInfinity(Arena.WallFraction(origin,target));
        public void NotifyAttack(PrototypeUnit source,PrototypeUnit target)
        {var first=Owner(source);var second=Owner(target);if(first!=null)first.EngagedUntil=Elapsed+3;if(second!=null)second.EngagedUntil=Elapsed+3;}
        public Func<PrototypeUnit,int,bool> OtherSquadVision;
        private PrototypeUnit Owner(PrototypeUnit u)=>u.Role==PrototypeRole.Leader?u:Units.FirstOrDefault(l=>l.Role==PrototypeRole.Leader&&l.Side==u.Side&&l.Squad==u.Squad);
        public void SetStrategy(PrototypeUnit leader,PrototypeStrategy strategy)
        {
            leader.Strategy=strategy;
            foreach(var u in Units.Where(u=>u.Side==leader.Side&&u.Squad==leader.Squad)){u.NextThinkAt=0;u.PatrolGoal=-1;u.NextPatrolAt=Elapsed;}
        }
        public bool SteerLeader(int id,PrototypePoint direction)
        {
            var u=Units.FirstOrDefault(actor=>actor.Id==id&&actor.Alive&&actor.Side==PrototypeSide.Ally&&actor.Role==PrototypeRole.Leader);
            if(Paused||Outcome!=PrototypeOutcome.Fighting||u==null||direction.Length<.15||!(CanMove?.Invoke(u)??true))return false;
            SelectDirectCaptain(u);
            u.SteeringStopped=false;
            if(u.Moving)
            {u.Steering=true;u.ManualOrder=true;u.Order=PrototypeOrder.Move;u.Goal=u.Anchor=Arena.Center(u.NextCell);u.MoveGoal=u.NextCell;u.TargetId=-1;u.SquadTargetId=-1;u.ObjectOrder=false;u.ObjectTargetId="";return true;}
            double angle=Math.Round(Math.Atan2(direction.Z,direction.X)/(Math.PI/4))*(Math.PI/4);var axis=new PrototypePoint(Math.Round(Math.Cos(angle)),Math.Round(Math.Sin(angle)));int x=u.Cell%Arena.FineColumns+(int)axis.X,z=u.Cell/Arena.FineColumns+(int)axis.Z;
            if(x<0||z<0||x>=Arena.FineColumns||z>=Arena.FineRows)return false;
            int next=z*Arena.FineColumns+x;
            if(!BodyStep(u,next,axis)||!Face(u,axis))return false;
            CancelWindup(u);u.Steering=true;u.ManualOrder=true;u.ObjectOrder=false;u.ObjectTargetId="";u.SquadTargetId=-1;
            u.Order=PrototypeOrder.Move;u.Anchor=u.Goal=Arena.Center(next);u.MoveGoal=next;
            u.TargetId=-1;u.AttackSlot=-1;u.NextThinkAt=0;u.BlockedFor=0;u.PatrolGoal=-1;
            return true;
        }
        public void ReleaseSteering(int id)
        {var u=Units.FirstOrDefault(actor=>actor.Id==id);if(u==null||!u.Steering)return;u.Steering=false;u.SteeringStopped=u.Moving;u.MoveGoal=-1;u.Goal=u.Anchor=u.Position;u.NextThinkAt=0;}
        public void MarkDirectOrder(IEnumerable<int> squads,PrototypeOrder order,int target,bool focusSquad,PrototypeSide side=PrototypeSide.Ally)
        {
            var selected=Units.Where(u=>u.Alive&&u.Side==side&&u.Role==PrototypeRole.Leader&&squads.Contains(u.Squad)).ToArray();
            if(selected.Length==1&&order!=PrototypeOrder.Auto)SelectDirectCaptain(selected[0]);
            foreach(var l in selected)
            {l.Steering=false;l.SteeringStopped=false;l.ManualOrder=order!=PrototypeOrder.Auto;l.ObjectOrder=false;l.ObjectTargetId="";l.SquadTargetId=focusSquad?target:-1;l.NextThinkAt=0;l.PatrolGoal=-1;}
        }
        private void SelectDirectCaptain(PrototypeUnit captain)
        {
            foreach(var unit in Units.Where(u=>u.Side==captain.Side&&u.Role==PrototypeRole.Leader))
                unit.DirectCaptain=unit==captain;
        }
        private static bool Guards(PrototypeUnit leader)=>leader.Strategy!=PrototypeStrategy.Attack||leader.ManualOrder&&leader.Order==PrototypeOrder.Guard;
        private bool DoctrineSlotAllowed(PrototypeUnit u,PrototypePoint point)
        {
            var l=Owner(u);if(l==null)return true;
            if(u.Role==PrototypeRole.Leader&&u.ManualOrder&&u.Order!=PrototypeOrder.Guard)return true;
            var center=u.Role==PrototypeRole.Leader?(Guards(u)?u.Anchor:u.Position):l.Position;
            return (point-center).Length<=(Guards(l)?l.ThreatRadius:l.CommandRadius)+.001;
        }
        private int NearestFree(PrototypeUnit u,PrototypePoint goal,double radius)
        {
            var d=Arena.Distances(u.Cell,true);int best=-1;double score=double.PositiveInfinity;
            for(int c=0;c<d.Length;c++)
            {
                if(double.IsPositiveInfinity(d[c])||reservations.TryGetValue(c,out int owner)&&owner!=u.Id||!Fits(u,c,u.Facing))continue;
                double gap=(Arena.Center(c)-goal).Length;if(gap>radius)continue;
                double value=gap*4+d[c]*.1;if(value<score){score=value;best=c;}
            }
            return best;
        }
        private bool ReachableNow(PrototypeUnit u,int goal)
        {
            if(goal<0)return false;if(goal==u.Cell)return true;
            if(u.FootprintLength==2)return !double.IsPositiveInfinity(Arena.Distances(goal)[u.Cell]);
            var seen=new HashSet<int>{u.Cell};var pending=new Queue<int>();pending.Enqueue(u.Cell);
            while(pending.Count>0)foreach(int n in Arena.Neighbours(pending.Dequeue()))
            {
                if(!seen.Add(n)||reservations.TryGetValue(n,out int owner)&&owner!=u.Id||!Fits(u,n,u.Facing))continue;
                if(n==goal)return true;pending.Enqueue(n);
            }
            return false;
        }
        private void DoctrinePursue(PrototypeUnit u,PrototypeUnit target)
        {
            if(u.Role==PrototypeRole.Leader)u.EngagedUntil=Elapsed+3;
            u.TargetId=target.Id;var delta=target.BodyCenter-u.BodyCenter;
            if(InAttackRadius(u,target)&&ClearAttack(u.Position,target.BodyCenter))
            {u.AttackSlot=u.Cell;if(Face(u,delta.Normalized)&&Elapsed+1e-9>=u.ReadyAt)BeginAttack(u);return;}
            u.AttackSlot=AttackPosition(u,target);u.MoveGoal=u.AttackSlot;
        }
        private bool ShouldResumeSquadFocus(PrototypeUnit unit)
        {
            if (!CommanderDoctrine || !unit.Ranged || unit.Role == PrototypeRole.Leader || IndependentAssault(unit.Side)) return false;
            var captain = Owner(unit);
            if (captain == null || captain.SquadTargetId < 0 || captain.SquadTargetId == unit.TargetId ||
                (unit.Position - captain.Position).Length > captain.CommandRadius) return false;
            var focus = Units.FirstOrDefault(e => e.Id == captain.SquadTargetId);
            double boundary = Guards(captain) ? captain.ThreatRadius : captain.CommandRadius;
            return focus?.Alive == true && focus.Side != unit.Side && (CanTarget?.Invoke(unit, focus) ?? true) &&
                (focus.Position - captain.Position).Length <= boundary && ReadyTarget(unit, focus);
        }
        private void ThinkDoctrine(PrototypeUnit u)
        {
            if(u.ObjectOrder)
            {
                if (u.Order == PrototypeOrder.Move)
                {
                    int goal = Arena.CellAt(u.Goal);
                    if (goal != u.Cell) u.MoveGoal = goal;
                }
                return;
            }
            if(u.Order==PrototypeOrder.Retreat)
            {
                int exitX=(int)u.Goal.X,exitZ=(int)u.Goal.Z;
                bool AtExit(int cell)=>cell>=0&&cell%Arena.FineColumns/2==exitX&&cell/Arena.FineColumns/2==exitZ;
                if(Footprint(u,u.Cell,u.Facing).All(AtExit)){u.Goal=Arena.Center(u.Cell);u.Escaped=true;return;}
                var d=Arena.Distances(u.Cell,true);
                int goal=Enumerable.Range(0,4).Select(n=>(exitZ*2+n/2)*Arena.FineColumns+exitX*2+n%2)
                    .Where(c=>!double.IsPositiveInfinity(d[c])&&Fits(u,c,(Arena.Center(c)-u.Position).Normalized)&&Footprint(u,c,(Arena.Center(c)-u.Position).Normalized).All(AtExit))
                    .OrderBy(c=>d[c]).DefaultIfEmpty(Arena.CellAt(u.Goal)).First();
                u.Goal=Arena.Center(goal);u.MoveGoal=goal;return;
            }
            if (OnlyExitMovement && !u.Steering && !(u.ManualOrder && u.Order == PrototypeOrder.Move)) return;
            if(IndependentAssault(u.Side) && !OnlyExitMovement)
            {
                var assaultTarget=Target(u);
                if(assaultTarget!=null)DoctrinePursue(u,assaultTarget);
                else
                {
                    u.AttackSlot=-1;
                    u.MoveGoal=DefaultDestination?.Invoke(u)??-1;
                    if(u.MoveGoal==u.Cell)u.MoveGoal=-1;
                }
                return;
            }
            var l=Owner(u)??u;bool captain=u.Role==PrototypeRole.Leader;
            if(captain&&u.Steering){if(u.Cell!=Arena.CellAt(u.Goal))u.MoveGoal=Arena.CellAt(u.Goal);return;}
            if(captain&&u.ManualOrder)
            {
                if(u.Order==PrototypeOrder.Move)
                {
                    if(u.Cell!=Arena.CellAt(u.Goal)){u.MoveGoal=Arena.CellAt(u.Goal);return;}
                    u.ManualOrder=false;u.Order=PrototypeOrder.Auto;
                }
                else if(u.Order==PrototypeOrder.Focus)
                {
                    var ordered=Units.FirstOrDefault(e=>e.Id==u.TargetId&&e.Alive&&e.Side!=u.Side&&(CanTarget?.Invoke(u,e)??true));
                    if(ordered!=null){DoctrinePursue(u,ordered);return;}
                    u.ManualOrder=false;u.Order=PrototypeOrder.Auto;u.SquadTargetId=-1;
                }
            }
            if(l.Strategy==PrototypeStrategy.Obey)
            {
                if(captain)
                {
                    if(u.ManualOrder&&u.Order==PrototypeOrder.Guard&&u.Cell!=Arena.CellAt(u.Anchor)){u.MoveGoal=Arena.CellAt(u.Anchor);return;}
                    var ready=PreferredTarget(u,Units.Where(e=>e.Alive&&e.Side!=u.Side&&(CanTarget?.Invoke(u,e)??true)&&InAttackRadius(u,e)&&ClearAttack(u.Position,e.BodyCenter)));
                    u.TargetId=ready?.Id??-1;u.AttackSlot=ready==null?-1:u.Cell;
                    if(ready!=null){u.EngagedUntil=Elapsed+3;if(Face(u,(ready.BodyCenter-u.BodyCenter).Normalized)&&Elapsed+1e-9>=u.ReadyAt)BeginAttack(u);}return;
                }
                bool engaged=Elapsed<l.EngagedUntil&&Units.Any(e=>e.Alive&&e.Side!=u.Side&&(CanTarget?.Invoke(u,e)??true)&&(e.Position-l.Position).Length<=l.CommandRadius);
                if(!engaged){u.TargetId=-1;u.AttackSlot=-1;Patrol(u,l.Position,l.GuardRadius);return;}
            }
            var center=captain?u.Anchor:l.Position;
            bool guarding=Guards(l);
            double boundary=guarding?l.ThreatRadius:l.CommandRadius;
            // A soldier must return to its captain before acquiring another target.
            if(!captain&&(u.Position-l.Position).Length>l.CommandRadius)
            {u.TargetId=-1;u.MoveGoal=NearestFree(u,l.Position,l.GuardRadius);return;}
            if(captain&&u.ManualOrder&&u.Order==PrototypeOrder.Guard&&(u.Position-u.Anchor).Length>u.ThreatRadius)
            {u.TargetId=-1;u.MoveGoal=NearestFree(u,u.Anchor,u.GuardRadius);return;}
            if(captain&&!u.ManualOrder&&u.Strategy==PrototypeStrategy.Flee)
            {Flee(u);return;}
            bool Eligible(PrototypeUnit e)=>e.Alive&&e.Side!=u.Side&&(CanTarget?.Invoke(u,e)??true)&&
                (e.Position-(captain&&!guarding?u.Position:center)).Length<=boundary;
            PrototypeUnit target=null;
            // Squad focus is a preference: an occupied ring is not a reason to jam the squad.
            if(!captain&&l.SquadTargetId>=0)
            {
                var focus=Units.FirstOrDefault(e=>e.Id==l.SquadTargetId&&Eligible(e));
                bool rethink=Elapsed+1e-9>=u.NextThinkAt;
                if (focus != null)
                {
                    bool ready = u.Ranged ? ReadyTarget(u, focus) : InAttackRadius(u, focus);
                    if (u.Ranged && !ready)
                        target = PreferredTarget(u, Units.Where(e => e != focus && Eligible(e) && ReadyTarget(u, e)));
                    if (target == null && (ready ||
                        !rethink && u.TargetId == focus.Id && u.AttackSlot >= 0 ||
                        rethink && ReachableNow(u, AttackPosition(u, focus))))
                        target = focus;
                }
                if(target!=null&&rethink)u.NextThinkAt=Elapsed+.3;
            }
            if(target==null)
            {
                if(Elapsed+1e-9<u.NextThinkAt)target=Units.FirstOrDefault(e=>e.Id==u.TargetId&&Eligible(e)&&e.Id!=l.SquadTargetId);
                else
                {target=PreferredTarget(u,Units.Where(Eligible).Where(e=>captain||e.Id!=l.SquadTargetId));u.NextThinkAt=Elapsed+.3;}
            }
            if(target!=null){DoctrinePursue(u,target);return;}
            u.TargetId=-1;u.AttackSlot=-1;
            if(captain&&!guarding)
            {
                if(u.Side==PrototypeSide.Enemy&&!BothSidesHuman)
                {
                    u.MoveGoal=DefaultDestination?.Invoke(u)??-1;
                    if(u.MoveGoal==u.Cell)u.MoveGoal=-1;
                    return;
                }
                var direct=Units.FirstOrDefault(c=>c.Alive&&c.Side==u.Side&&c.DirectCaptain)
                    ??Units.FirstOrDefault(c=>c.Alive&&c.Side==u.Side&&c.Player);
                if(direct!=null&&direct!=u&&(u.Position-direct.Position).Length>direct.CommandRadius)
                {
                    u.MoveGoal=NearestFree(u,direct.Position,Math.Max(.5,direct.CommandRadius-.5));
                    u.PatrolGoal=-1;
                    return;
                }
                if(direct!=null&&direct!=u&&(u.Anchor-direct.Position).Length>direct.CommandRadius-.8)
                    u.Anchor=u.Position;
                Patrol(u,u.Anchor,Math.Min(l.GuardRadius,.8));
                return;
            }
            Patrol(u,center,l.GuardRadius);
        }
        private void Patrol(PrototypeUnit u,PrototypePoint center,double radius)
        {
            if (!PatrolEnabled) return;
            if((u.Position-center).Length>radius){u.MoveGoal=NearestFree(u,center,radius);return;}
            if(u.PatrolGoal<0)
            {
                if(u.NextPatrolAt<=0)u.NextPatrolAt=Elapsed+2+u.Id%4;
                if(Elapsed<u.NextPatrolAt){u.MoveGoal=-1;return;}
            }
            if(Elapsed>=u.NextPatrolAt||u.PatrolGoal<0||(Arena.Center(u.PatrolGoal)-center).Length>radius)
            {
                double angle=(u.Id*2.399963+Math.Floor(Elapsed/6)*1.7);var point=center+new PrototypePoint(Math.Cos(angle),Math.Sin(angle))*Math.Min(radius,.8);
                u.PatrolGoal=NearestFree(u,point,Math.Min(radius,.8));u.NextPatrolAt=Elapsed+6+u.Id%3;
            }
            u.MoveGoal=u.PatrolGoal==u.Cell?-1:u.PatrolGoal;
        }
        private void Flee(PrototypeUnit u)
        {
            var enemies=Units.Where(e=>e.Alive&&e.Side!=u.Side&&(CanTarget?.Invoke(u,e)??true)).ToArray();u.TargetId=-1;
            if(enemies.Length==0){Patrol(u,u.Anchor,u.GuardRadius);return;}
            if(Elapsed<u.NextPatrolAt&&u.PatrolGoal>=0&&u.Cell!=u.PatrolGoal){u.MoveGoal=u.PatrolGoal;return;}
            var d=Arena.Distances(u.Cell,true);int best=u.Cell;double score=double.NegativeInfinity;
            for(int c=0;c<d.Length;c++)
            {
                if(d[c]>6||double.IsPositiveInfinity(d[c])||reservations.TryGetValue(c,out int owner)&&owner!=u.Id||!Fits(u,c,u.Facing))continue;
                double gap=enemies.Min(e=>(Arena.Center(c)-e.Position).Length);
                double value=gap-d[c]*.15+((OtherSquadVision?.Invoke(u,c)??false)?12:0);
                if(value>score){score=value;best=c;}
            }
            u.PatrolGoal=best;u.NextPatrolAt=Elapsed+.6;u.MoveGoal=best==u.Cell?-1:best;
        }
    }
}

using System;
using System.Linq;
using System.Collections.Generic;
namespace BaseConquest.RealtimePrototype
{
    public sealed partial class PrototypeUnit
    {
        public int WindupTarget=-1,WindupCell=-1;
        public double WindupStarted;
        public double JumpDuration;
        public bool RpgWindup;
        public double ReleasedAt=-10,ObjectWindupAt=-1;
        public double AttackPoseTime(double seconds,double impact)
        {
            if(seconds-ReleasedAt<.16)return impact+(1-impact)*Math.Max(0,(seconds-ReleasedAt)/.16);
            return HitAt>WindupStarted?Math.Max(0,Math.Min(impact,(seconds-WindupStarted)/(HitAt-WindupStarted)*impact)):0;
        }
    }
    public sealed partial class RealtimeBattlePrototype
    {
        public bool InAttackRadius(PrototypeUnit actor, PrototypeUnit target, PrototypePoint? origin = null, PrototypePoint? axis = null)
            => AttackGap(actor, target, origin, axis) <= ReachFor(actor, target, origin) + 1e-8;

        internal double AttackGap(PrototypeUnit actor, PrototypeUnit target, PrototypePoint? origin = null, PrototypePoint? axis = null)
        {
            double gap = BodyGapAt(actor, target, origin ?? actor.Position, axis ?? actor.BodyAxis);
            // The melee radius touches the target's hit body, including its edge while it is between cells.
            if (AttackTriggered != null && !actor.Ranged && actor.FootprintLength == 1 && target.FootprintLength == 1) gap -= target.Radius;
            return gap;
        }
        private bool ReadyTarget(PrototypeUnit actor,PrototypeUnit target)=>target?.Alive==true&&InAttackRadius(actor,target)&&ClearAttack(actor.Position,target.BodyCenter);
        private PrototypeUnit PreferredTarget(PrototypeUnit actor,IEnumerable<PrototypeUnit> candidates)
        {
            var choices=candidates.ToArray();
            if(TargetWeight!=null&&actor.Ranged){var ready=choices.Where(t=>ReadyTarget(actor,t)).ToArray();if(ready.Length>0)choices=ready;}
            return choices.OrderBy(t=>(t.Position-actor.Position).Length/(TargetWeight?.Invoke(actor,t)??1)).ThenBy(t=>t.Id).FirstOrDefault();
        }
        public Action<PrototypeUnit> AttackTriggered;
        public Func<PrototypeUnit,double> WindupDuration;
        internal Func<PrototypeUnit, bool> AttackAllowed;
        internal bool AttacksAllowed(PrototypeUnit actor) => AttackAllowed?.Invoke(actor) != false;
        public bool TryJump(PrototypeUnit actor,int destination,double duration,Func<int,int,bool> heightAllowed)
        {
            if(actor?.Alive!=true||actor.Moving||!PrototypeArena.Finite(duration)||duration<=0||!Fits(actor,destination,actor.Facing))return false;
            int dx=Math.Abs(actor.Cell%Arena.FineColumns-destination%Arena.FineColumns),dz=Math.Abs(actor.Cell/Arena.FineColumns-destination/Arena.FineColumns);
            if(dx+dz!=1)return false;
            var from=Footprint(actor,actor.Cell,actor.Facing).ToArray();var to=Footprint(actor,destination,actor.Facing).ToArray();
            for(int i=0;i<from.Length;i++)if(!heightAllowed(from[i],to[i]))return false;
            if(Units.Any(other=>other!=actor&&other.Alive&&BodiesCross(actor,Arena.Center(destination),other,other.Moving?Arena.Center(other.NextCell):other.Position)))return false;
            CancelWindup(actor);actor.NextCell=destination;actor.Progress=0;actor.CarrySeconds=0;actor.JumpDuration=duration;
            foreach(int cell in to)reservations[cell]=actor.Id;return true;
        }
        public void CancelWindup(PrototypeUnit actor)
        {actor.HitAt=-1;actor.HitResolved=true;actor.WindupTarget=-1;actor.SwingUntil=Elapsed;actor.ReadyAt=Elapsed;actor.ObjectWindupAt=-1;}
        private void StartWindup(PrototypeUnit actor)
        {
            if (!AttacksAllowed(actor) || !actor.HitResolved && actor.HitAt >= 0) return;
            actor.RpgWindup=true;actor.WindupTarget=actor.TargetId;actor.WindupCell=actor.Cell;actor.WindupStarted=Elapsed;
            actor.AttackFacing=actor.Facing;actor.HitAt=Elapsed+WindupDuration(actor);actor.SwingUntil=actor.HitAt;actor.HitResolved=false;actor.ReadyAt=actor.HitAt;
            FollowMeleeWindup(actor);
        }
        private bool MayFollowMeleeWindup(PrototypeUnit actor)
        {
            return !actor.Ranged && actor.Order is not (PrototypeOrder.Guard or PrototypeOrder.Retreat or PrototypeOrder.Move)
                && !actor.ObjectOrder && !actor.Steering && actor.JumpDuration <= 0
                && (actor.Role != PrototypeRole.Leader || actor.Strategy != PrototypeStrategy.Obey || actor.ManualOrder && actor.Order == PrototypeOrder.Focus);
        }
        private void FollowMeleeWindup(PrototypeUnit actor)
        {
            if (AttackTriggered == null || actor.Moving || !MayFollowMeleeWindup(actor)) return;
            var target = Units.FirstOrDefault(u => u.Id == actor.WindupTarget);
            if (target?.Alive != true || !target.Moving || target.SteeringStopped) return;

            // Keep closing on the original target during the windup while using the normal occupancy and terrain rules.
            var goal = Arena.Center(target.NextCell);
            double gap = (goal - actor.Position).Length;
            actor.MoveGoal = Arena.Neighbours(actor.Cell)
                .Where(cell => (Arena.Center(cell) - goal).Length + 1e-8 < gap &&
                    (IndependentAssault(actor.Side) || !CommanderDoctrine || DoctrineSlotAllowed(actor, Arena.Center(cell))))
                .OrderBy(cell => (Arena.Center(cell) - goal).Length)
                .DefaultIfEmpty(-1).First();
        }
        private bool WindupLegal(PrototypeUnit actor)
        {
            var target=Units.FirstOrDefault(u=>u.Id==actor.WindupTarget);
            bool anchored = !actor.Moving && actor.Cell == actor.WindupCell;
            return actor.Alive && AttacksAllowed(actor) && (anchored || MayFollowMeleeWindup(actor)) && actor.TargetId==actor.WindupTarget&&target?.Alive==true&&
                (CanTarget?.Invoke(actor,target)??true)&&InAttackRadius(actor,target)&&ClearAttack(actor.Position,target.BodyCenter);
        }
        private void StartMovingMeleeWindups()
        {
            if (AttackTriggered == null) return;
            foreach (var actor in Units.Where(unit => unit.Alive && unit.Moving && !unit.SteeringStopped &&
                (unit.HitAt < 0 || unit.HitResolved) && unit.TargetId >= 0 && Elapsed + 1e-9 >= unit.ReadyAt))
            {
                if (!MayFollowMeleeWindup(actor)) continue;
                var target = Units.FirstOrDefault(unit => unit.Id == actor.TargetId);
                if (target?.Alive == true && (CanTarget?.Invoke(actor, target) ?? true) &&
                    InAttackRadius(actor, target) && ClearAttack(actor.Position, target.BodyCenter)) StartWindup(actor);
            }
        }
        private void ValidateWindups()
        {if(AttackTriggered!=null)foreach(var actor in Units.Where(u=>u.HitAt>=0&&!u.HitResolved).ToArray())if(!WindupLegal(actor))CancelWindup(actor);}
        public void ApplyResolvedHit(PrototypeUnit source,PrototypeUnit target,double damage)
        {double actual=Math.Min(target.Health,Math.Max(0,damage));target.Health-=actual;target.FlashUntil=Elapsed+.12;DamageApplied?.Invoke(source,target,actual);Hits++;}
        public void AddVisualArrow(PrototypeUnit source,PrototypePoint destination,double speed)
        {
            var delta=destination-source.Position;if(delta.Length<=1e-8)return;
            Arrows.Add(new PrototypeArrow {Id=nextArrowId++,SourceId=source.Id,Side=source.Side,Position=source.Position,Direction=delta.Normalized,Remaining=delta.Length,VisualOnly=true,Speed=speed});Shots++;
        }
    }
}

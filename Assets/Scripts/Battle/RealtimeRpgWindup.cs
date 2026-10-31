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
        private bool ReadyTarget(PrototypeUnit actor,PrototypeUnit target)=>target?.Alive==true&&BodyGap(actor,target)<=ReachFor(actor,target)&&ClearAttack(actor.Position,target.BodyCenter);
        private PrototypeUnit PreferredTarget(PrototypeUnit actor,IEnumerable<PrototypeUnit> candidates)
        {
            var choices=candidates.ToArray();
            if(TargetWeight!=null&&actor.Ranged){var ready=choices.Where(t=>ReadyTarget(actor,t)).ToArray();if(ready.Length>0)choices=ready;}
            return choices.OrderBy(t=>(t.Position-actor.Position).Length/(TargetWeight?.Invoke(actor,t)??1)).ThenBy(t=>t.Id).FirstOrDefault();
        }
        public Action<PrototypeUnit> AttackTriggered;
        public Func<PrototypeUnit,double> WindupDuration;
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
            if(!actor.HitResolved&&actor.HitAt>=0)return;
            actor.RpgWindup=true;actor.WindupTarget=actor.TargetId;actor.WindupCell=actor.Cell;actor.WindupStarted=Elapsed;
            actor.AttackFacing=actor.Facing;actor.HitAt=Elapsed+WindupDuration(actor);actor.SwingUntil=actor.HitAt;actor.HitResolved=false;actor.ReadyAt=actor.HitAt;
        }
        private bool WindupLegal(PrototypeUnit actor)
        {
            var target=Units.FirstOrDefault(u=>u.Id==actor.WindupTarget);
            return actor.Alive&&!actor.Moving&&actor.Cell==actor.WindupCell&&actor.TargetId==actor.WindupTarget&&target?.Alive==true&&
                (CanTarget?.Invoke(actor,target)??true)&&BodyGap(actor,target)<=ReachFor(actor,target)+1e-8&&ClearAttack(actor.Position,target.BodyCenter);
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

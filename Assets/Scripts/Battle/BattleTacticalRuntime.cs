using System;
using System.Collections.Generic;
using System.Linq;
using BaseConquest.RealtimePrototype;
namespace BaseConquest.Battle
{
    // Object orders use the same attack clock and the same live damage/casualty ledger as units.
    public sealed class BattleTacticalRuntime
    {
        private readonly RealtimeCampaignBattle run;
        private readonly Dictionary<int,string> orders=new Dictionary<int,string>();
        private readonly HashSet<string> occupiedLegacyRocks=new HashSet<string>();
        public IReadOnlyList<BattleTacticalObject> Objects=>run.Session.Opening.TacticalObjects;
        public int Revision {get;private set;}
        public Action<PrototypeUnit,BattleTacticalObject,double> ObjectHit;
        public BattleTacticalRuntime(RealtimeCampaignBattle battle)
        {
            run=battle;var rules=run.Session.Opening.TacticalRules??new BattleTacticalRules();rules.Validate();run.Session.Opening.TacticalObjects??=new List<BattleTacticalObject>();
            BattleTacticalObject.Validate(run.Session.Opening.TacticalObjects,run.Battle.Board.Width,run.Battle.Board.Height,rules);
            if(Objects.Count>0&&(double.IsNaN(run.Session.Opening.TacticalBaseDamage)||double.IsInfinity(run.Session.Opening.TacticalBaseDamage)||run.Session.Opening.TacticalBaseDamage<=0))throw new ArgumentException("战术伤害快照非法");
            // Preserve the saved location and fallen trunk of retired map obstacles.
            foreach(var o in Objects)o.Steps=0;
            foreach(var u in run.Model.Units.Where(u=>u.ObjectOrder))
            {
                if(u.Alive&&Attackable(u.ObjectTargetId)!=null)orders[u.Id]=u.ObjectTargetId;
                else
                {
                    if(u.ObjectWindupAt>=0)run.Model.CancelWindup(u);
                    FinishOrder(u);
                }
            }
            if(Objects.Count>0)Refresh();
        }
        private static bool Attackable(BattleTacticalObject o)=>o.Kind=="dummy"&&!o.Fallen;
        private BattleTacticalObject Attackable(string id)=>Objects.FirstOrDefault(o=>o.Id==id&&Attackable(o));
        public BattleTacticalObject At(PrototypePoint p)=>Objects.FirstOrDefault(o=>Attackable(o)&&Math.Abs(p.X-o.X-.5)<=.55&&Math.Abs(p.Z-o.Y-.5)<=.55);
        public void Cancel(IEnumerable<int> squads)
        {var chosen=squads.ToArray();foreach(var u in run.Model.Units.Where(u=>u.Side==PrototypeSide.Ally&&chosen.Contains(u.Squad))){orders.Remove(u.Id);u.ObjectWindupAt=-1;u.ObjectOrder=false;u.ObjectTargetId="";}}
        private void FinishOrder(PrototypeUnit actor){orders.Remove(actor.Id);actor.ObjectWindupAt=-1;actor.ObjectOrder=actor.ManualOrder=false;actor.ObjectTargetId="";actor.Order=PrototypeOrder.Auto;actor.NextThinkAt=0;}
        public bool Issue(IEnumerable<int> commanders,string id,bool leadersOnly=false)
        {
            if(run.Battle.Phase!=BattlePhase.Fighting||Attackable(id)==null)return false;
            var slots=commanders.Select(run.Battle.Soldier).Where(u=>u?.IsActive==true&&u.Side==BattleSide.A&&u.Kind==SoldierKind.Hero).Select(u=>u.OriginCommanderSlot).ToArray();
            var actors=run.Model.Units.Where(u=>u.Alive&&u.Side==PrototypeSide.Ally&&slots.Contains(u.Squad)&&(!leadersOnly||u.Role==PrototypeRole.Leader)).ToArray();
            foreach(var u in actors){if(run.Session.Opening.Rpg!=null)run.Model.CancelWindup(u);orders[u.Id]=id;u.ObjectTargetId=id;u.ManualOrder=true;u.ObjectOrder=true;u.SquadTargetId=-1;u.TargetId=-1;u.Order=PrototypeOrder.Guard;u.Goal=u.Position;u.NextThinkAt=run.Model.Elapsed+1;}
            return actors.Length>0;
        }
        internal void Refresh()
        {
            var b=run.Battle.Board;var movement=b.Cells.Select(c=>!c.Walkable).ToArray();var shots=b.Cells.Select(c=>c.BlocksShots).ToArray();
            foreach(var o in Objects)foreach(var p in o.Fallen?o.Trunk:new List<GridPosition>{new GridPosition(o.X,o.Y)}){int n=p.Z*b.Width+p.X;shots[n]=true;bool occupied=o.Kind=="rock"&&run.Model.Units.Any(u=>u.Alive&&RealtimeBattlePrototype.OccupiesCoarse(u,p.X,p.Z,run.Model.Arena));movement[n]=!occupied;if(occupied)occupiedLegacyRocks.Add(o.Id);else occupiedLegacyRocks.Remove(o.Id);}
            run.Model.Arena.RefreshObstacles(movement,shots);
            foreach(var u in run.Model.Units)
            {
                if(!run.Model.Arena.CanStand(u.Goal)){u.Goal=u.Position;u.Order=PrototypeOrder.Auto;u.ManualOrder=false;}
                if(!run.Model.Arena.CanStand(u.Anchor))u.Anchor=u.Position;
                if(u.MoveGoal>=0&&!run.Model.Arena.FineWalkable(u.MoveGoal))u.MoveGoal=-1;
                if(u.AttackSlot>=0&&!run.Model.Arena.FineWalkable(u.AttackSlot))u.AttackSlot=-1;
                if(u.PatrolGoal>=0&&!run.Model.Arena.FineWalkable(u.PatrolGoal))u.PatrolGoal=-1;
            }
            Revision++;
        }
        public bool Hit(int actorId,string id)
        {
            var actor=run.Model.Units.FirstOrDefault(u=>u.Id==actorId&&u.Alive);var o=Attackable(id);
            if(actor==null||o==null||run.Model.Paused||run.Battle.Phase!=BattlePhase.Fighting||actor.Moving||actor.ReadyAt>run.Model.Elapsed+1e-9)return false;
            var point=new PrototypePoint(o.X+.5,o.Y+.5);var delta=point-actor.Position;
            if(delta.Length<.001||delta.Length>actor.Reach+.55||run.Model.Arena.WallFraction(actor.Position,point-delta.Normalized*(.55/Math.Max(Math.Abs(delta.Normalized.X),Math.Abs(delta.Normalized.Z))))<=1){actor.ObjectWindupAt=-1;return false;}
            if(!run.Model.Face(actor,delta.Normalized))return false;
            double power=run.Model.DamageFor(actor);
            if(run.Session.Opening.Rpg!=null)
            {
                if(actor.ObjectWindupAt<0){actor.ObjectWindupAt=run.Model.Elapsed+run.Model.WindupDuration(actor);actor.HitAt=actor.ObjectWindupAt;actor.HitResolved=true;actor.RpgWindup=true;actor.WindupStarted=run.Model.Elapsed;actor.WindupCell=actor.Cell;actor.AttackFacing=actor.Facing;actor.SwingUntil=actor.ObjectWindupAt;return false;}
                if(actor.Cell!=actor.WindupCell){actor.ObjectWindupAt=-1;return false;}
                if(actor.ObjectWindupAt>run.Model.Elapsed+1e-9)return false;
                actor.ObjectWindupAt=-1;power=run.RecordRpgObjectAttack(actor,o.Id,point);actor.ReleasedAt=run.Model.Elapsed;
            }
            actor.ReadyAt=run.Model.Elapsed+(run.Session.Opening.Rpg==null?Math.Max(.1,actor.AttackInterval):0);actor.HitAt=run.Model.Elapsed;actor.HitResolved=true;actor.AttackFacing=actor.Facing;actor.SwingUntil=run.Model.Elapsed+.25;
            o.Health=Math.Max(0,o.Health-power);
            ObjectHit?.Invoke(actor,o,power);
            if(o.Health<=0){o.Fallen=true;FinishOrder(actor);Refresh();}
            return true;
        }
        public void Tick(double seconds)
        {
            if(Objects.Count==0||run.Battle.Phase!=BattlePhase.Fighting)return;
            if(Objects.Any(Attackable))foreach(var actor in run.Model.Units.Where(u=>u.Alive&&!u.ObjectOrder&&!u.ManualOrder&&u.TargetId<0&&u.Strategy==PrototypeStrategy.Attack))
            {
                var dummy=Objects.Where(o=>o.Kind=="dummy"&&!o.Fallen).OrderBy(o=>(new PrototypePoint(o.X+.5,o.Y+.5)-actor.Position).Length).FirstOrDefault();
                if(dummy==null)continue;
                orders[actor.Id]=dummy.Id;actor.ObjectOrder=true;actor.ObjectTargetId=dummy.Id;actor.ObjectWindupAt=-1;actor.TargetId=-1;actor.Order=PrototypeOrder.Guard;
            }
            if(occupiedLegacyRocks.Any(id=>{var rock=Objects.First(o=>o.Id==id);return !run.Model.Units.Any(u=>u.Alive&&RealtimeBattlePrototype.OccupiesCoarse(u,rock.X,rock.Y,run.Model.Arena));}))Refresh();
            foreach(var entry in orders.ToArray())
            {
                var actor=run.Model.Units.FirstOrDefault(u=>u.Id==entry.Key&&u.Alive);var o=Attackable(entry.Value);
                if(actor==null||o==null||!actor.ObjectOrder){orders.Remove(entry.Key);if(actor!=null)FinishOrder(actor);continue;}
                var point=new PrototypePoint(o.X+.5,o.Y+.5);
                if((point-actor.Position).Length<=actor.Reach+.55){actor.Order=PrototypeOrder.Guard;actor.Goal=actor.Position;actor.NextThinkAt=run.Model.Elapsed+1;Hit(actor.Id,o.Id);}
                else if(!actor.Moving)
                {
                    actor.ObjectWindupAt=-1;
                    var distances=run.Model.Arena.Distances(actor.Cell,true);
                    var cells=Enumerable.Range(0,distances.Length).Where(c=>!double.IsPositiveInfinity(distances[c])&&run.Model.Arena.CanStand(run.Model.Arena.Center(c))&&(run.Model.Arena.Center(c)-point).Length<=Math.Max(1.25,actor.Reach)&&!run.Model.Units.Any(u=>u!=actor&&u.Alive&&u.Cell==c)).OrderBy(c=>distances[c]).ToArray();
                    if(cells.Length>0){actor.Order=PrototypeOrder.Move;actor.Goal=run.Model.Arena.Center(cells[0]);actor.TargetId=-1;actor.NextThinkAt=0;}
                }
            }
        }
    }
}

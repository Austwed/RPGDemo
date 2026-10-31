using System;
using System.Linq;
using System.Collections.Generic;
using BaseConquest.RealtimePrototype;
namespace BaseConquest.Battle
{
    public sealed partial class RealtimeCampaignBattle
    {
        private sealed class StakeOrder
        {public GridPosition Cell;public PrototypePoint Goal;public PrototypeOrder Order;public int Target;public bool Manual;}
        private readonly Dictionary<int,StakeOrder> stakeOrders=new Dictionary<int,StakeOrder>();private double nextStakeThink;
        public BattleTacticalObject StakeAt(PrototypePoint point)
        {
            var p=new GridPosition((int)point.X,(int)point.Z);var c=Battle.Board.Cell(p);
            return c?.IsStake==true&&c.ObstacleHealth>0?new BattleTacticalObject{Id="stake:"+p.X+":"+p.Z,Kind="stake",X=p.X,Y=p.Z,Health=c.ObstacleHealth}:null;
        }
        private void SetStakeOrder(PrototypeUnit u,GridPosition p)
        {
            stakeOrders[u.Id]=new StakeOrder{Cell=p,Goal=u.Goal,Order=u.Order,Target=u.TargetId,Manual=u.ManualOrder};
            Model.CancelWindup(u);u.ObjectOrder=true;u.ObjectTargetId="stake:"+p.X+":"+p.Z;u.TargetId=-1;u.ObjectWindupAt=-1;u.Order=PrototypeOrder.Guard;u.Goal=u.Position;u.MoveGoal=-1;
        }
        public void RestoreStakeOrder(PrototypeUnit u)
        {var parts=u.ObjectTargetId.Split(':');if(parts.Length==3&&int.TryParse(parts[1],out int x)&&int.TryParse(parts[2],out int y)&&Battle.Board.Cell(new GridPosition(x,y))?.IsStake==true)SetStakeOrder(u,new GridPosition(x,y));else {u.ObjectOrder=false;u.ObjectTargetId="";}}
        public bool IssueStake(IEnumerable<int> commanders,string id,bool leadersOnly)
        {
            var parts=id.Split(':');if(parts.Length!=3||!int.TryParse(parts[1],out int x)||!int.TryParse(parts[2],out int y)||Battle.Phase!=BattlePhase.Fighting||StakeAt(new PrototypePoint(x+.5,y+.5))==null)return false;
            var slots=commanders.Select(Battle.Soldier).Where(u=>u?.IsActive==true&&u.Side==BattleSide.A&&u.Kind==SoldierKind.Hero).Select(u=>u.OriginCommanderSlot).ToArray();
            var actors=Model.Units.Where(u=>u.Alive&&u.Side==PrototypeSide.Ally&&slots.Contains(u.Squad)&&(!leadersOnly||u.Role==PrototypeRole.Leader)).ToArray();
            foreach(var u in actors){SetStakeOrder(u,new GridPosition(x,y));stakeOrders[u.Id].Goal=u.Position;stakeOrders[u.Id].Order=PrototypeOrder.Auto;stakeOrders[u.Id].Target=-1;stakeOrders[u.Id].Manual=false;u.ManualOrder=true;}return actors.Length>0;
        }
        public void CancelStakes(int[] squads)
        {foreach(var u in Model.Units.Where(u=>u.Side==PrototypeSide.Ally&&squads.Contains(u.Squad)&&stakeOrders.ContainsKey(u.Id))){stakeOrders.Remove(u.Id);u.ObjectOrder=false;u.ObjectTargetId="";u.ObjectWindupAt=-1;}}
        private void TickStakes()
        {
            if(Model.Paused||Battle.Phase!=BattlePhase.Fighting)return;
            if(Model.Elapsed>=nextStakeThink)
            {
                nextStakeThink=Model.Elapsed+.4;
                foreach(var u in Model.Units.Where(u=>u.Alive&&!u.Moving&&u.FootprintLength==2&&!u.ObjectOrder&&u.MoveGoal>=0&&(u.Role!=PrototypeRole.Leader||u.ManualOrder||u.Strategy!=PrototypeStrategy.Obey)))
                {
                    if(Model.BodyPath(u,u.MoveGoal).Count>0)continue;
                    var path=Model.BodyPath(u,u.MoveGoal,true);var previous=u.Cell;
                    foreach(int c in path)
                    {
                        var facing=(Model.Arena.Center(c)-Model.Arena.Center(previous)).Normalized;
                        var blocked=Model.Footprint(u,c,facing).Where(f=>f>=0).Select(f=>Model.Arena.Center(f)).Select(StakeAt).FirstOrDefault(o=>o!=null);
                        if(blocked!=null){SetStakeOrder(u,new GridPosition(blocked.X,blocked.Y));break;}previous=c;
                    }
                }
            }
            foreach(var pair in stakeOrders.ToArray())
            {
                var u=Model.Units.FirstOrDefault(a=>a.Id==pair.Key);var order=pair.Value;var cell=Battle.Board.Cell(order.Cell);
                if(u==null||!u.Alive||!u.ObjectOrder||cell==null||cell.ObstacleHealth<=0)
                {
                    stakeOrders.Remove(pair.Key);if(u!=null){u.ObjectOrder=false;u.ObjectTargetId="";u.ObjectWindupAt=-1;u.Goal=order.Goal;u.Order=order.Order;u.TargetId=order.Target;u.ManualOrder=order.Manual;u.NextThinkAt=0;}continue;
                }
                if(u.Moving)continue;var point=new PrototypePoint(order.Cell.X+.5,order.Cell.Z+.5);var delta=point-u.Position;
                if(delta.Length>u.Reach+.55||!Battle.Board.HasLineOfSight(new GridPosition((int)u.Position.X,(int)u.Position.Z),order.Cell))
                {
                    u.ObjectWindupAt=-1;var distances=Model.Arena.Distances(u.Cell,true);
                    int destination=Enumerable.Range(0,distances.Length).Where(c=>!double.IsPositiveInfinity(distances[c])&&(Model.Arena.Center(c)-point).Length<=u.Reach+.5&&Model.Fits(u,c,(point-Model.Arena.Center(c)).Normalized)&&Model.BodyPath(u,c).Count>0).OrderBy(c=>distances[c]).DefaultIfEmpty(-1).First();
                    if(destination>=0){u.Order=PrototypeOrder.Move;u.Goal=Model.Arena.Center(destination);u.NextThinkAt=0;}continue;
                }
                u.Order=PrototypeOrder.Guard;u.Goal=u.Position;u.MoveGoal=-1;u.NextThinkAt=Model.Elapsed+1;
                if(!Model.Face(u,delta.Normalized)||u.ReadyAt>Model.Elapsed)continue;
                if(u.ObjectWindupAt<0){u.ObjectWindupAt=Model.Elapsed+(Session.Opening.Rpg!=null?Model.WindupDuration(u):u.AttackInterval);u.WindupCell=u.Cell;u.AttackFacing=u.Facing;u.SwingUntil=u.ObjectWindupAt;continue;}
                if(u.WindupCell!=u.Cell){u.ObjectWindupAt=-1;continue;}if(Model.Elapsed<u.ObjectWindupAt)continue;
                double damage=Session.Opening.Rpg!=null?RecordRpgObjectAttack(u,u.ObjectTargetId,point):Model.DamageFor(u);u.ObjectWindupAt=-1;u.ReleasedAt=Model.Elapsed;u.SwingUntil=Model.Elapsed+.2;
                Battle.DamageStake(order.Cell,(float)damage);Tactics.Refresh();
            }
        }
    }
    public sealed partial class BattleSimulation
    {
        public void DamageStake(GridPosition p,float damage)
        {var c=Board.Cell(p);if(Phase!=BattlePhase.Fighting||c?.IsStake!=true||float.IsNaN(damage)||float.IsInfinity(damage)||damage<=0)return;c.ObstacleHealth=Math.Max(0,c.ObstacleHealth-damage);TerrainRevision++;}
    }
}
namespace BaseConquest.RealtimePrototype
{
    public sealed partial class RealtimeBattlePrototype
    {
        public List<int> BodyPath(PrototypeUnit u,int goal,bool ignoreUnitTerrain=false)
        {
            var rule=UnitCellAllowed;
            // Probe a route through stakes only to locate the first blocker; never move during this probe.
            try{if(ignoreUnitTerrain)UnitCellAllowed=null;return PlanFootprint(u,u.Cell,goal);}finally{UnitCellAllowed=rule;}
        }
    }
}

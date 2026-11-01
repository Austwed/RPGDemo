using System;
using System.Collections.Generic;
using System.Linq;
namespace BaseConquest.Battle
{
    public static class FortificationInfo
    {
        public static string Description(BoardCell c) => c.FixedCover ? "固定掩体：可通行，格上单位受到远程伤害×0.75；不可拆除" : "";
    }
    public sealed partial class BattleSimulation
    {
        public int TerrainRevision {get;private set;}
        public bool DamageObstacle(BattleSoldier actor,GridPosition p,float damage)
        {
            var c=Board.Cell(p);
            if(Phase!=BattlePhase.Fighting||actor==null||c==null||c.ObstacleHealth<=0||c.ObstacleOwner==actor.Side||float.IsNaN(damage)||float.IsInfinity(damage)||damage<=0||GridPosition.Distance(actor.PreviousCell,p)>actor.Stats.Range||!Board.HasLineOfSight(actor.PreviousCell,p)||!AttackAreaContains(actor,p))return false;
            float actual=Math.Min(c.ObstacleHealth,damage);c.ObstacleHealth-=actual;TerrainRevision++;
            actor.LastDecision="破坏路障 "+p+" −"+actual.ToString("0.#")+(c.ObstacleHealth<=0 ? "，道路已打通" : "，剩余"+c.ObstacleHealth.ToString("0.#"));
            return true;
        }
        private Intent PlanObstacleRoute(BattleSoldier s,Intent normal,Dictionary<GridPosition,List<BattleSoldier>> occupancy)
        {
            bool fleeing=RoutInProgress&&s.Side==BattleSide.B||s.Command==SquadCommand.Retreat;
            if(!ModernRules||s.IsTransit||s.RescueTargetId>=0||s.CarryingId>=0||s.DeveloperImmobile||normal.Action!=RoundAction.Wait&&normal.Action!=RoundAction.Move||!AttackAreaContains(s,s.Cell)||!Board.Cells.Any(c=>c.ObstacleHealth>0&&c.ObstacleOwner!=s.Side))return normal;
            if(!fleeing&&(s.Command==SquadCommand.Hold||Squads[s.SquadId].GuardAssigned||RoutInProgress||s.Command==SquadCommand.Auto&&CommanderFor(s)?.IsActive==true&&GuardConfiguration(Squads[s.SquadId]).Strategy!=SquadStrategy.Attack))return normal;
            var enemies=Soldiers.Where(u=>u.Side!=s.Side&&u.IsActive&&Vision.CanSee(s.Side,u)&&AttackAreaContains(s,u.Cell)).ToArray();
            if(s.Command==SquadCommand.Focus)enemies=enemies.Where(u=>u.Id==s.FocusTargetId).ToArray();
            bool Goal(GridPosition p) => fleeing ? IsExit(s.Side,p) : s.Command==SquadCommand.Move ? p==s.MoveGoal : enemies.Length>0 ? enemies.Any(e=>CanAttack(s,p,e)) : Scenario.FogEnabled&&s.Command==SquadCommand.Auto&&p!=s.Cell&&!Vision.Explored(s.Side,p);
            if(s.Command==SquadCommand.Focus&&enemies.Length==0)return normal;
            float power=AttackPower(s);if(power<=0)return normal;
            var known=KnownOccupancy(s.Side,occupancy);var distance=new Dictionary<GridPosition,double>{{s.Cell,0}};var previous=new Dictionary<GridPosition,GridPosition>();var open=new List<GridPosition>{s.Cell};var done=new HashSet<GridPosition>();GridPosition? end=null;
            while(open.Count>0)
            {
                var current=open.OrderBy(p=>distance[p]).ThenBy(p=>p.Z*Board.Width+p.X).First();open.Remove(current);if(!done.Add(current))continue;
                if(Goal(current)){end=current;break;}
                foreach(var direction in Directions)
                {
                    var next=current+direction;var cell=Board.Cell(next);if(cell==null||!Board.HeightConnection(current,next)||!AttackRouteAllowed(s,next)||!AttackAreaContains(s,next)||done.Contains(next))continue;
                    bool obstacle=cell.ObstacleHealth>0&&cell.ObstacleOwner!=s.Side;
                    if(!cell.Walkable&&!obstacle||known.TryGetValue(next,out var group)&&group.Any(u=>u.Side!=s.Side))continue;
                    double cost=distance[current]+cell.FootworkCost/(double)MovementSpeed(s)+(obstacle ? Math.Ceiling(cell.ObstacleHealth/power) : 0);
                    if(distance.TryGetValue(next,out var old)&&old<=cost)continue;distance[next]=cost;previous[next]=current;open.Add(next);
                }
            }
            if(!end.HasValue)return normal;
            var path=new List<GridPosition>{end.Value};while(path[path.Count-1]!=s.Cell)path.Add(previous[path[path.Count-1]]);path.Reverse();
            int barrier=path.FindIndex(p=>Board.Cell(p).ObstacleHealth>0);if(barrier<1)return normal;
            double detour=normal.Action==RoundAction.Move&&normal.Path.Count>1 ? Math.Ceiling((normal.Path.Count-1)/(double)MovementSpeed(s)) : double.PositiveInfinity;
            if(distance[end.Value]>=detour)return normal;
            var target=path[barrier];var intent=new Intent {Actor=s,Action=RoundAction.Wait,Destination=s.Cell,GoalCell=normal.GoalCell,Decision="清除挡路路障（清障路线优于绕行）"};
            if(GridPosition.Distance(s.Cell,target)<=s.Stats.Range&&Board.HasLineOfSight(s.Cell,target)){intent.Action=RoundAction.Attack;intent.ObstacleTarget=target;intent.Destination=target;}
            else SetMove(intent,path.Take(barrier).ToList());
            return intent;
        }
    }
}

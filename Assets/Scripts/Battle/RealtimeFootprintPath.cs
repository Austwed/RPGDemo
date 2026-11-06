using System;
using System.Collections.Generic;
using System.Linq;
namespace BaseConquest.RealtimePrototype
{
    public sealed partial class RealtimeBattlePrototype
    {
        private sealed class BodyRoute
        {public int Goal,Revision,Cell,Index;public List<int> Steps;}
        private readonly Dictionary<int,BodyRoute> bodyRoutes=new Dictionary<int,BodyRoute>();
        private static int Direction(PrototypePoint facing)
        {var a=PrototypeUnit.Axis(facing);return a.X>0?0:a.Z>0?1:a.X<0?2:3;}
        private static PrototypePoint Heading(int direction)=>direction==0?new PrototypePoint(1,0):direction==1?new PrototypePoint(0,1):direction==2?new PrototypePoint(-1,0):new PrototypePoint(0,-1);
        private bool CanTurnStep(PrototypeUnit u,int from,int to,PrototypePoint oldFacing,bool occupied=false,bool headOnly=false)
        {
            var facing=(Arena.Center(to)-Arena.Center(from)).Normalized;
            var old=PrototypeUnit.Axis(oldFacing);var next=PrototypeUnit.MajorAxis(facing,old);
            if(!Arena.CanStep(from,to))return false;
            if(headOnly)
            {
                // 马头格是唯一硬判据（2026-11-06 裁决）：尾格地形与占用交给落步时的挤开或绕行处理。
                // 否则窄口、贴墙拐角这些「头能过、尾巴擦到」的地方会在规划阶段就被判死，马原地不动。
                if(!Arena.FineWalkable(to)||UnitCellAllowed!=null&&!UnitCellAllowed(u,to)||occupied&&reservations.TryGetValue(to,out int headOwner)&&headOwner!=u.Id)return false;
            }
            else if(!Fits(u,from,next,occupied)||!Fits(u,to,next,occupied))return false;
            if(old.X!=next.X&&old.Z!=next.Z)
            {int corner=Arena.CellAt(Arena.Center(from)-(old+next)*.5);if(!Arena.FineWalkable(corner)||UnitCellAllowed!=null&&!UnitCellAllowed(u,corner)||occupied&&reservations.TryGetValue(corner,out int owner)&&owner!=u.Id)return false;}
            if(headOnly)return true;
            var first=Footprint(u,from,next).ToArray();var second=Footprint(u,to,next).ToArray();
            return first.Select((c,n)=>Arena.CanStep(c,second[n])).All(ok=>ok);
        }
        // 尾格要不要惩罚：头能过、身体摆不下，就多付绕行代价，让优先挑真正摆得开的格子。
        private double TailPenalty(PrototypeUnit u,int to,PrototypePoint heading)
        {
            if(u.FootprintLength!=2)return 0;
            var axis=PrototypeUnit.MajorAxis(heading,u.BodyAxis);
            int tail=Footprint(u,to,axis).Skip(1).FirstOrDefault();
            return tail>=0&&Arena.FineWalkable(tail)?0:6;
        }
        // A* includes heading in each state. Other units reserve during execution, not in the static route.
        // This permits waiting/yielding while avoiding routes that require a horse to cut through a corner.
        private List<int> PlanFootprint(PrototypeUnit u,int start,int goal,bool occupied=false)
        {
            var result=new List<int>();if(!Arena.FineWalkable(start)||!Arena.FineWalkable(goal)||start==goal)return result;
            int count=Arena.FineColumns*Arena.FineRows*4,source=start*4+Direction(u.BodyAxis),serial=0,last=-1;
            var cost=Enumerable.Repeat(double.PositiveInfinity,count).ToArray();var parent=Enumerable.Repeat(-1,count).ToArray();
            var open=new SortedSet<Tuple<double,int,int>>();cost[source]=0;open.Add(Tuple.Create((Arena.Center(start)-Arena.Center(goal)).Length,serial++,source));
            while(open.Count>0)
            {
                var entry=open.Min;open.Remove(entry);int state=entry.Item3,cell=state/4;
                double heuristic=(Arena.Center(cell)-Arena.Center(goal)).Length;
                if(entry.Item1>cost[state]+heuristic+1e-8)continue;
                if(cell==goal){last=state;break;}
                bool headFirst=u.FootprintLength==2;
                foreach(int next in Arena.Neighbours(cell))
                {
                    if(!CanTurnStep(u,cell,next,Heading(state%4),occupied,headFirst))continue;
                    int destination=next*4+Direction(PrototypeUnit.MajorAxis(Arena.Center(next)-Arena.Center(cell),Heading(state%4)));
                    double value=cost[state]+Arena.StepCost(cell,next)+(headFirst?TailPenalty(u,next,Arena.Center(next)-Arena.Center(cell)):0);
                    if(value>=cost[destination]-1e-8)continue;cost[destination]=value;parent[destination]=state;
                    open.Add(Tuple.Create(value+(Arena.Center(next)-Arena.Center(goal)).Length,serial++,destination));
                }
            }
            if(last<0)return result;
            for(int state=last;state!=source;state=parent[state]){if(state<0)return new List<int>();result.Add(state/4);}result.Reverse();return result;
        }
        private int FootprintNext(PrototypeUnit u,int goal)
        {
            if(bodyRoutes.TryGetValue(u.Id,out var route)&&route.Goal==goal&&route.Revision==Arena.NavigationRevision)
            {
                if(route.Index<route.Steps.Count&&route.Steps[route.Index]==u.Cell){route.Cell=u.Cell;route.Index++;}
                bool headFirst=u.FootprintLength==2;
                if(route.Cell==u.Cell&&route.Index<route.Steps.Count&&CanTurnStep(u,u.Cell,route.Steps[route.Index],u.BodyAxis,false,headFirst))return route.Steps[route.Index];
            }
            var steps=PlanFootprint(u,u.Cell,goal);bodyRoutes[u.Id]=new BodyRoute {Goal=goal,Revision=Arena.NavigationRevision,Cell=u.Cell,Steps=steps};return steps.Count==0?-1:steps[0];
        }
    }
}

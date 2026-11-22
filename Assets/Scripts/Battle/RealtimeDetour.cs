using System;
using System.Collections.Generic;
using System.Linq;
namespace BaseConquest.RealtimePrototype
{
    public sealed partial class RealtimeBattlePrototype
    {
        private const double BlockedRouteDelay = 2.5;
        private sealed class BlockedRoute
        {
            public double RetryAt, RejectUntil, LastProgressAt, BestDistance;
            public int Goal = -1, Revision = -1, RejectedCell = -1, RejectedTarget = -1;
            public readonly Queue<int> Route = new Queue<int>();
        }

        private readonly Dictionary<int, BlockedRoute> blockedRoutes = new Dictionary<int, BlockedRoute>();

        private bool RecentlyBlockedAttackPosition(PrototypeUnit actor, PrototypeUnit target, int cell) =>
            blockedRoutes.TryGetValue(actor.Id, out var detour) && detour.RejectedCell == cell
            && detour.RejectedTarget == target.Id && Elapsed < detour.RejectUntil;

        private bool PreviewDetour(PrototypeUnit unit, int cell, int goal, List<PrototypePoint> points)
        {
            if (!blockedRoutes.TryGetValue(unit.Id, out var detour) || detour.Goal != goal ||
                detour.Revision != Arena.NavigationRevision || detour.Route.Count == 0) return false;
            int count = points.Count;
            foreach (int step in detour.Route)
            {
                if (step == cell) continue;
                if (!Arena.CanStep(cell, step)) break;
                points.Add(Arena.Center(step));
                cell = step;
            }
            if (cell == goal) return true;
            points.RemoveRange(count, points.Count - count);
            return false;
        }

        private int RecoverBlockedRoute(PrototypeUnit unit, BodyRoute groupRoute, double remainingDistance)
        {
            if (!(CanMove?.Invoke(unit) ?? true) || unit.MoveGoal == unit.Cell) return -1;
            if (!blockedRoutes.TryGetValue(unit.Id, out var detour))
                blockedRoutes[unit.Id] = detour = new BlockedRoute();
            if (detour.Goal != unit.MoveGoal || detour.Revision != Arena.NavigationRevision)
            {
                detour.Route.Clear();
                detour.Goal = unit.MoveGoal;
                detour.Revision = Arena.NavigationRevision;
                detour.LastProgressAt = Elapsed;
                detour.BestDistance = remainingDistance;
            }
            else if (remainingDistance < detour.BestDistance - .001)
            {
                detour.BestDistance = remainingDistance;
                detour.LastProgressAt = Elapsed;
            }
            while (detour.Route.Count > 0 && detour.Route.Peek() == unit.Cell) detour.Route.Dequeue();
            bool FirstStepClear(int next) => CanTurnStep(unit, unit.Cell, next, unit.BodyAxis,
                occupied: true, avoidReservedCorner: true)
                && (unit.FootprintLength == 2 || !Units.Any(other => other != unit && other.Alive &&
                    BodiesCross(unit, Arena.Center(next), other, other.Moving ? Arena.Center(other.NextCell) : other.Position)));

            if (detour.Route.Count > 0)
            {
                if (FirstStepClear(detour.Route.Peek())) return detour.Route.Peek();
                detour.Route.Clear();
            }
            // A brief blockage or a normal turn uses the regular movement. Backtracking can
            // reset BlockedFor, so measure improvement against the same destination over time.
            if (Elapsed < detour.LastProgressAt + BlockedRouteDelay || Elapsed < detour.RetryAt) return -1;
            detour.RetryAt = Elapsed + .4 + unit.Id % 7 * .03;
            Func<PrototypePoint, bool> arrival = groupRoute == null || unit.FootprintLength != 2 ? null
                : axis => axis.Equals(groupRoute.ArrivalAxis);
            var steps = PlanFootprint(unit, unit.Cell, unit.MoveGoal, occupied: true, acceptArrival: arrival,
                firstStepAllowed: FirstStepClear, maximumExpanded: 1536);
            foreach (int step in steps) detour.Route.Enqueue(step);
            if (detour.Route.Count > 0) return detour.Route.Peek();
            if (unit.TargetId >= 0 && unit.MoveGoal == unit.AttackSlot)
            {
                detour.RejectedCell = unit.AttackSlot;
                detour.RejectedTarget = unit.TargetId;
                detour.RejectUntil = Elapsed + .9;
            }
            return -1;
        }

        private sealed class Detour {public double RetryAt;public int Goal=-1,Revision=-1;public Queue<int> Route=new Queue<int>();}
        private readonly Dictionary<int,Detour> detours=new Dictionary<int,Detour>();
        private int DetourNext(PrototypeUnit u)
        {
            if(!detours.TryGetValue(u.Id,out var d))detours[u.Id]=d=new Detour();
            if(d.Goal!=u.MoveGoal||d.Revision!=Arena.NavigationRevision){d.Route.Clear();d.Goal=u.MoveGoal;d.Revision=Arena.NavigationRevision;}
            while(d.Route.Count>0&&d.Route.Peek()==u.Cell)d.Route.Dequeue();
            int blocked=-1;
            if(d.Route.Count>0){int next=d.Route.Peek();if(Arena.CanStep(u.Cell,next)&&(u.FootprintLength==2||CanTurnStep(u,u.Cell,next,u.Facing,true))&&(u.FootprintLength!=2||u.BlockedFor<.6))return next;blocked=next;d.Route.Clear();}
            if(u.BlockedFor<(u.FootprintLength==2?.2:.6)||Elapsed<d.RetryAt)return -1;d.RetryAt=Elapsed+.6+u.Id%7*.03;
            var previous=new Dictionary<int,int>{{u.Cell,-1}};var queue=new Queue<int>();queue.Enqueue(u.Cell);int found=-1,visited=0;
            while(queue.Count>0&&visited++<512)
            {
                int cell=queue.Dequeue();if(cell==u.MoveGoal){found=cell;break;}
                foreach(int next in Arena.Neighbours(cell))
                {
                    if(previous.ContainsKey(next)||cell==u.Cell&&next==blocked||reservations.TryGetValue(next,out int owner)&&owner!=u.Id||u.FootprintLength!=2&&!CanTurnStep(u,cell,next,u.Facing,true))continue;
                    previous[next]=cell;queue.Enqueue(next);
                }
            }
            if(found>=0){var reverse=new Stack<int>();for(int cell=found;cell!=u.Cell;cell=previous[cell])reverse.Push(cell);foreach(int cell in reverse)d.Route.Enqueue(cell);}
            return d.Route.Count>0?d.Route.Peek():-1;
        }
    }
}

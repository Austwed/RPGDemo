using System;
using System.Collections.Generic;
namespace BaseConquest.RealtimePrototype
{
    public sealed partial class RealtimeBattlePrototype
    {
        private sealed class Detour {public double RetryAt;public int Goal=-1,Revision=-1;public Queue<int> Route=new Queue<int>();}
        private readonly Dictionary<int,Detour> detours=new Dictionary<int,Detour>();
        private int DetourNext(PrototypeUnit u)
        {
            if(!detours.TryGetValue(u.Id,out var d))detours[u.Id]=d=new Detour();
            if(d.Goal!=u.MoveGoal||d.Revision!=Arena.NavigationRevision){d.Route.Clear();d.Goal=u.MoveGoal;d.Revision=Arena.NavigationRevision;}
            while(d.Route.Count>0&&d.Route.Peek()==u.Cell)d.Route.Dequeue();
            int blocked=-1;
            if(d.Route.Count>0){int next=d.Route.Peek();if(Arena.CanStep(u.Cell,next)&&(u.FootprintLength==2||CanTurnStep(u,u.Cell,next,u.Facing,true))&&(u.FootprintLength!=2||u.BlockedFor<.6))return next;blocked=next;d.Route.Clear();}
            // Longer prototype footprints need an earlier detour when space is tight.
            if(u.BlockedFor<(u.FootprintLength==2?.2:.6)||Elapsed<d.RetryAt)return -1;d.RetryAt=Elapsed+.6+u.Id%7*.03;
            // Dynamic occupancy is consulted only after a stable wait; ordinary routing stays shared.
            {
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
            }
            return d.Route.Count>0?d.Route.Peek():-1;
        }
    }
}

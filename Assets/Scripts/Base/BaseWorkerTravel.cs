using System;
using System.Collections.Generic;
using System.Linq;
namespace BaseConquest.Base
{
    public static class BaseStageWalk
    {
        public static BasePoint Door(BaseState s,string id)
        {
            var o=BaseSpatial.Objects(s).FirstOrDefault(v=>v.Id==id);
            if(o==null)throw new ArgumentException("找不到设施入口："+id);
            return BaseSpatial.Nearest(s,new BasePoint(o.X,o.Y-o.Height/2-.6));
        }
        public static List<BasePoint> Route(BaseState s,BasePoint from,BasePoint to)
        {
            var objects=BaseSpatial.Objects(s);var board=BaseBattlefield.Board(s);
            Func<double,double,bool> pass=(x,y)=>BaseSpatial.Geometry(objects,x,y,board.Width,board.Height)&&BaseSpatial.TerrainWalkable(board,x,y,objects);
            var link=BaseMap.EightLink(from,to,null,pass,false,board.Width,board.Height);if(link!=null)return link;
            var route=BaseMap.FindRoute(from,to,null,pass,false,board.Width,board.Height);
            for(int n=route.Count-1;n>0;n--)if(BaseMap.EightAligned(from,route[n])&&BaseMap.ClearSegment(from,route[n],null,pass,false,board.Width,board.Height)){route.RemoveRange(0,n);break;}
            return route;
        }
        public static bool Advance(BaseState s,ref double x,ref double y,BasePoint target,double seconds)
        {
            var at=new BasePoint(x,y);if(at.Distance(target)<.08)return true;
            var route=Route(s,at,target);if(route.Count==0)return false;
            double remaining=Math.Max(0,seconds)*2.5;
            foreach(var next in route){double d=new BasePoint(x,y).Distance(next);if(d<=remaining){x=next.X;y=next.Y;remaining-=d;}else{if(d>0){x+=(next.X-x)*remaining/d;y+=(next.Y-y)*remaining/d;}break;}}
            return new BasePoint(x,y).Distance(target)<.08;
        }
    }
    public sealed class BaseWorkerJourney
    {
        public string AppearanceId;
        public BasePoint Position;
        public List<BasePoint> Route;
        public int Waypoint;
    }
    // Presentation only: the successful economy transaction already owns the assignment.
    public sealed class BaseWorkerTravel
    {
        private const string CivilianAppearance = "villager";
        private sealed class Batch {public string AppearanceId;public BasePoint From,To;public BasePoint? Via;public int Count;}
        private readonly Queue<Batch> pending=new Queue<Batch>();private double delay;
        public readonly List<BaseWorkerJourney> Active=new List<BaseWorkerJourney>();
        public int Pending=>pending.Sum(v=>v.Count);
        public bool CivilianInTransit => pending.Any(b => b.AppearanceId == CivilianAppearance) ||
            Active.Any(w => w.AppearanceId == CivilianAppearance);
        public void Clear(){pending.Clear();Active.Clear();delay=0;}
        public void Enqueue(BaseState s,BaseTroopKind kind,string from,string to,int count)
        {
            string appearance = kind == BaseTroopKind.Melee ? "saber" : kind == BaseTroopKind.Ranged ? "archer" : "rider";
            Enqueue(s, appearance, from == "" ? "barracks" : from, to == "" ? "barracks" : to, count);
        }
        public void EnqueueCivilians(BaseState s,string from,string to,int count)
            => Enqueue(s, CivilianAppearance, from, to, count);
        internal void EnqueueCivilian(BasePoint from, BasePoint to, BasePoint? via)
            => pending.Enqueue(new Batch { AppearanceId = CivilianAppearance, From = from, To = to, Via = via, Count = 1 });
        private void Enqueue(BaseState s,string appearance,string from,string to,int count)
        {
            if(count<=0)return;
            try{pending.Enqueue(new Batch{AppearanceId=appearance,Count=count,From=BaseStageWalk.Door(s,from),To=BaseStageWalk.Door(s,to)});}catch(ArgumentException){}
        }
        public void Tick(BaseState s,double seconds,bool paused=false)
        {
            if(paused)return;seconds=Math.Max(0,seconds);delay-=seconds;
            if(delay<=0&&Active.Count<8&&pending.Count>0)
            {
                var b=pending.Peek();var route=BaseStageWalk.Route(s,b.From,b.Via ?? b.To);
                if (b.Via.HasValue && route.Count > 0)
                {
                    var onward = BaseStageWalk.Route(s, b.Via.Value, b.To);
                    if (onward.Count > 0) route.AddRange(onward);
                }
                if(route.Count>0)Active.Add(new BaseWorkerJourney{AppearanceId=b.AppearanceId,Position=b.From,Route=route});
                if(--b.Count==0)pending.Dequeue();delay=.4;
            }
            foreach(var a in Active.ToArray())
            {
                double left=seconds*2.5;
                while(a.Waypoint<a.Route.Count&&left>0){var to=a.Route[a.Waypoint];double d=a.Position.Distance(to);if(d<=left){a.Position=to;a.Waypoint++;left-=d;}else{a.Position=new BasePoint(a.Position.X+(to.X-a.Position.X)*left/d,a.Position.Y+(to.Y-a.Position.Y)*left/d);left=0;}}
                if(a.Waypoint==a.Route.Count)Active.Remove(a);
            }
        }
    }
}

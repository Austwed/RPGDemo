using System;
using System.Collections.Generic;
using System.Linq;

namespace BaseConquest.Base
{
    public sealed class BaseMapObject
    {
        public readonly string Id, Name;
        public readonly double X, Y, Width, Height;
        public readonly bool Blocks;
        public readonly string SourceObjectId, AssetId, Kind;
        public readonly bool VisualOnly;
        public readonly string Shape;
        public readonly int Rotation;
        public readonly BaseObjectRect[] Parts;
        public double BoundsWidth=>Rotation%180==0?Width:Height;
        public double BoundsHeight=>Rotation%180==0?Height:Width;
        public BaseMapObject(string id, string name, double x, double y, double width, double height, bool blocks = true, string sourceObjectId = null, string assetId = null, string kind = null, bool visualOnly = false,string shape=null,int rotation=0)
        { Id = id; Name = name; X = x; Y = y; Width = width; Height = height; Blocks = blocks; SourceObjectId=sourceObjectId; AssetId=assetId; Kind=kind; VisualOnly=visualOnly;Shape=shape;Rotation=rotation;Parts=BaseObjectFootprint.Parts(this); }
        public double Distance(double x, double y)=>Parts.Min(part=>part.Distance(x,y));
        public bool Contains(double x,double y,double padding=0)=>Parts.Any(part=>part.Contains(x,y,padding));
    }
    public static class BaseMap
    {
        public const double Width = 24, Height = 14, Radius = 0.28, InteractionDistance = 1.35;
        public static readonly BaseMapObject[] Objects = {
            new BaseMapObject("home", "家", 4, 10.5, 4, 3),
            new BaseMapObject("government", "据点管理所", 19, 10.5, 4.5, 3),
            new BaseMapObject("farm", "粮田", 4, 3.2, 5, 3),
            new BaseMapObject("workshop", "工坊", 19, 3.2, 4, 3),
            new BaseMapObject("barracks", "兵营", 12, 10.5, 4, 2.6),
            new BaseMapObject("watchtower", "瞭望塔", 14.5, 3.2, 1.8, 1.8),
            new BaseMapObject("partner_farm", "伙伴 · 阿禾", 2.7, 7.8, 0.45, 0.45),
            new BaseMapObject("partner_craft", "伙伴 · 阿铸", 5.4, 7.8, 0.45, 0.45),
            new BaseMapObject("exit_west", "← 迎敌战场", 0.7, 6.5, 1.2, 2, false),
            new BaseMapObject("exit_east", "城镇 →", 23.3, 6.5, 1.2, 2, false),
            new BaseMapObject("exit_south", "↓ 世界地图", 12, 0.7, 2, 1.2, false),
            new BaseMapObject("exit_north", "上方预留", 12, 13.3, 2, 1.2, false)
        };
        public static bool Walkable(double x, double y, IEnumerable<BaseMapObject> occupants = null, bool useBaseBuildings = true,double width=Width,double height=Height)
        {
            if (double.IsNaN(x) || double.IsNaN(y) || double.IsInfinity(x) || double.IsInfinity(y) || x < Radius || y < Radius || x > width - Radius || y > height - Radius) return false;
            foreach (var obj in useBaseBuildings ? Objects : new BaseMapObject[0])
                if (!obj.Id.StartsWith("partner") && Blocks(obj, x, y)) return false;
            if (occupants != null) foreach (var obj in occupants) if (Blocks(obj, x, y)) return false;
            return true;
        }
        public static bool RelocateLegacyBarracks(ref double x, ref double y) => RelocateLegacyBuilding("barracks",ref x,ref y);
        public static bool RelocateLegacyWatchtower(ref double x,ref double y) => RelocateLegacyBuilding("watchtower",ref x,ref y);
        private static bool RelocateLegacyBuilding(string id,ref double x,ref double y)
        {
            var barracks = Objects.First(o => o.Id == id);
            var original = new BasePoint(x, y);
            if (!Blocks(barracks, x, y) || x < Radius || y < Radius || x > Width - Radius || y > Height - Radius || Objects.Any(o => o.Id != id && !o.Id.StartsWith("partner") && Blocks(o, original.X, original.Y))) return false;
            var candidates = new[] { new BasePoint(x, barracks.Y - barracks.Height / 2 - Radius - 0.05), new BasePoint(x, barracks.Y + barracks.Height / 2 + Radius + 0.05), new BasePoint(barracks.X - barracks.Width / 2 - Radius - 0.05, y), new BasePoint(barracks.X + barracks.Width / 2 + Radius + 0.05, y) };
            var nearest = candidates.Where(p => Walkable(p.X, p.Y)).OrderBy(p => p.Distance(original)).First(); x = nearest.X; y = nearest.Y; return true;
        }
        private static bool Blocks(BaseMapObject obj, double x, double y) => obj.Blocks && obj.Contains(x,y,Radius);
        public static void Move(ref double x, ref double y, double dx, double dy, IEnumerable<BaseMapObject> occupants = null, bool useBaseBuildings = true, Func<double, double, bool> terrain = null,double width=Width,double height=Height)
        {
            int steps = Math.Max(1, (int)Math.Ceiling(Math.Max(Math.Abs(dx), Math.Abs(dy)) / 0.1));
            for (int i = 0; i < steps; i++) { if (Walkable(x + dx / steps, y, occupants, useBaseBuildings,width,height) && (terrain == null || terrain(x + dx / steps, y))) x += dx / steps; if (Walkable(x, y + dy / steps, occupants, useBaseBuildings,width,height) && (terrain == null || terrain(x, y + dy / steps))) y += dy / steps; }
        }
        public static BaseMapObject Nearby(double x, double y, IEnumerable<BaseMapObject> companions = null)
        {
            BaseMapObject best = null; double distance = InteractionDistance;
            foreach (var obj in Objects.Where(o => !o.Id.StartsWith("partner")).Concat(companions ?? Objects.Where(o => o.Id.StartsWith("partner"))))
            { double d = obj.Distance(x, y); if (d < distance) { best = obj; distance = d; } }
            return best;
        }
        public static bool TryCell(double x, double y, out int cellX, out int cellY,double width=Width,double height=Height)
        {
            cellX = cellY = -1;
            if (double.IsNaN(x) || double.IsNaN(y) || double.IsInfinity(x) || double.IsInfinity(y) || x < 0 || y < 0 || x >= width || y >= height) return false;
            cellX = (int)Math.Floor(x); cellY = (int)Math.Floor(y); return true;
        }
        public static BaseMapObject FacilityAtCell(int x, int y)
        {
            if (x < 0 || y < 0 || x >= Width || y >= Height) return null;
            // 格子只要与设施占地有正面积交集，就属于该设施；相邻边界不误算。
            return Objects.FirstOrDefault(o => !o.Id.StartsWith("partner") && x < o.X + o.Width / 2 && x + 1 > o.X - o.Width / 2 && y < o.Y + o.Height / 2 && y + 1 > o.Y - o.Height / 2);
        }
        public static string GroundAtCell(int x, int y) => Math.Abs(x + 0.5 - 12) < 0.65 || Math.Abs(y + 0.5 - 6.5) < 0.55 ? "道路" : "草地";
        public static BasePoint WorkPost(string facility)
        {
            var obj = Objects.First(o => o.Id == facility);
            return new BasePoint(obj.X, obj.Y - obj.Height / 2 - 0.75);
        }
        public static List<BasePoint> FindRoute(BasePoint start, BasePoint goal, IEnumerable<BaseMapObject> occupants = null, Func<double,double,bool> terrain = null, bool useBaseBuildings = true,double width=Width,double height=Height)
        {
            // Quarter-cell samples retain narrow landing routes after expanding the body radius.
            var blockers = occupants?.ToArray(); int columns=(int)(width*4),rows=(int)(height*4);
            // One search sees a stable scene; adjacent edges repeatedly probe the same points.
            if(terrain!=null)
            {
                var source=terrain;var cache=new Dictionary<(double X,double Y),bool>();
                terrain=(x,y)=>{var key=(x,y);if(!cache.TryGetValue(key,out bool legal)){legal=source(x,y);cache.Add(key,legal);}return legal;};
            }
            BasePoint Point(int n) => new BasePoint((n % columns + 0.5) * 0.25, (n / columns + 0.5) * 0.25);
            int Nearest(BasePoint point)
            {
                bool Connected(int n){var p=Point(n);return Walkable(p.X,p.Y,blockers,useBaseBuildings,width,height)&&(terrain==null||terrain(p.X,p.Y))&&EightLink(point,p,blockers,terrain,useBaseBuildings,width,height)!=null;}
                int closest=Math.Max(0,Math.Min(rows-1,(int)Math.Ceiling(point.Y*4-1)))*columns+Math.Max(0,Math.Min(columns-1,(int)Math.Ceiling(point.X*4-1)));
                if(Connected(closest))return closest;
                // Closest first avoids testing long segments across the entire map before reaching a nearby sample.
                foreach(int n in Enumerable.Range(0,columns*rows).OrderBy(n=>Point(n).Distance(point)))
                {
                    if(n!=closest&&Connected(n))return n;
                }
                return -1;
            }
            int first = Nearest(start), last = Nearest(goal);
            if (first < 0 || last < 0 || (!Walkable(goal.X, goal.Y, blockers,useBaseBuildings,width,height) || terrain!=null&&!terrain(goal.X,goal.Y))) return new List<BasePoint>();
            var previous=Enumerable.Repeat(-2,columns*rows).ToArray();var costs=Enumerable.Repeat(int.MaxValue,columns*rows).ToArray();var closed=new bool[columns*rows];
            int Remaining(int n){int dx=Math.Abs(n%columns-last%columns),dy=Math.Abs(n/columns-last/columns);return 10*Math.Max(dx,dy)+4*Math.Min(dx,dy);}
            // Octile A*: eight headings, normalized diagonal cost, no corner cutting.
            var queue=new SortedSet<(int Total,int Remaining,int Node)>();previous[first]=-1;costs[first]=0;queue.Add((Remaining(first),Remaining(first),first));
            while (queue.Count > 0)
            {
                var entry=queue.Min;queue.Remove(entry);int n=entry.Node;if(n==last)break;closed[n]=true;int x=n%columns,y=n/columns;
                foreach (int next in new[] { x + 1 < columns ? n + 1 : -1, y > 0 ? n - columns : -1, x > 0 ? n - 1 : -1, y + 1 < rows ? n + columns : -1, x+1<columns&&y+1<rows?n+columns+1:-1,x>0&&y+1<rows?n+columns-1:-1,x>0&&y>0?n-columns-1:-1,x+1<columns&&y>0?n-columns+1:-1 })
                {
                    if(next<0||closed[next])continue;bool diagonal=next%columns!=x&&next/columns!=y;int step=diagonal?14:10;if(costs[n]+step>=costs[next])continue;var p=Point(next);
                    if(!Walkable(p.X,p.Y,blockers,useBaseBuildings,width,height)||terrain!=null&&!terrain(p.X,p.Y)||!ClearSegment(Point(n),p,blockers,terrain,useBaseBuildings,width,height))continue;
                    if(diagonal&&(!ClearSegment(Point(n),Point(y*columns+next%columns),blockers,terrain,useBaseBuildings,width,height)||!ClearSegment(Point(n),Point(next/columns*columns+x),blockers,terrain,useBaseBuildings,width,height)))continue;
                    int remaining=Remaining(next);if(costs[next]!=int.MaxValue)queue.Remove((costs[next]+remaining,remaining,next));
                    costs[next]=costs[n]+step;previous[next]=n;queue.Add((costs[next]+remaining,remaining,next));
                }
            }
            if (previous[last] == -2) return new List<BasePoint>();
            var route = new List<BasePoint>(); for (int n = last; n >= 0; n = previous[n]) route.Add(Point(n));
            route.Reverse();var linked=EightLink(start,route[0],blockers,terrain,useBaseBuildings,width,height);if(linked==null)return new List<BasePoint>();linked.AddRange(route.Skip(1));var tail=EightLink(route.Last(),goal,blockers,terrain,useBaseBuildings,width,height);if(tail==null)return new List<BasePoint>();linked.AddRange(tail);return linked;
        }
        public static bool EightAligned(BasePoint a,BasePoint b)
        {double x=Math.Abs(a.X-b.X),y=Math.Abs(a.Y-b.Y);return x<1e-7||y<1e-7||Math.Abs(x-y)<1e-7;}
        public static List<BasePoint> EightLink(BasePoint from,BasePoint to,IEnumerable<BaseMapObject> occupants=null,Func<double,double,bool> terrain=null,bool buildings=false,double width=Width,double height=Height)
        {
            if(EightAligned(from,to))return ClearSegment(from,to,occupants,terrain,buildings,width,height)?new List<BasePoint>{to}:null;
            double dx=to.X-from.X,dy=to.Y-from.Y,d=Math.Min(Math.Abs(dx),Math.Abs(dy));
            var diagonal=new BasePoint(Math.Sign(dx)*d,Math.Sign(dy)*d);
            foreach(var corner in new[]{new BasePoint(from.X+diagonal.X,from.Y+diagonal.Y),new BasePoint(to.X-diagonal.X,to.Y-diagonal.Y)})
                if(ClearSegment(from,corner,occupants,terrain,buildings,width,height)&&ClearSegment(corner,to,occupants,terrain,buildings,width,height))return new List<BasePoint>{corner,to};
            return null;
        }
        public static bool ClearSegment(BasePoint from, BasePoint to, IEnumerable<BaseMapObject> occupants = null, Func<double,double,bool> terrain = null, bool useBaseBuildings = true,double width=Width,double height=Height)
        {
            int steps = Math.Max(1, (int)Math.Ceiling(from.Distance(to) / 0.08));
            for (int i = 1; i <= steps; i++) { double x=from.X+(to.X-from.X)*i/steps,y=from.Y+(to.Y-from.Y)*i/steps; if(!Walkable(x,y,occupants,useBaseBuildings,width,height) || terrain!=null&&!terrain(x,y))return false; }
            return true;
        }
    }
    public readonly struct BasePoint
    {
        public readonly double X, Y;
        public BasePoint(double x, double y) { X = x; Y = y; }
        public double Distance(BasePoint other) => Math.Sqrt((X - other.X) * (X - other.X) + (Y - other.Y) * (Y - other.Y));
    }
}

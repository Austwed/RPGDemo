using System;
using System.Collections.Generic;
using System.Linq;
using BaseConquest.Battle;

namespace BaseConquest.Base
{
    [Serializable] public sealed class BaseMapPlacement
    {
        public string Id, Name;
        public string SourceObjectId, DefinitionId, AssetId, Kind;
        public bool VisualOnly;
        public double X, Y, Width=1, Height=1;
        public bool Blocks;
        public string Shape;
        public int Rotation;
        public BaseMapPlacement Copy() => (BaseMapPlacement)MemberwiseClone();
        public BaseMapObject Object => new BaseMapObject(Id,Name,X,Y,Width,Height,Blocks,SourceObjectId,AssetId,Kind,VisualOnly,Shape,Rotation);
    }
    // Scene authoring is copied into the campaign once. A saved game owns subsequent changes.
    [Serializable] public sealed class BaseMapLayout
    {
        public string Region, Title;
        public int Columns=24,Rows=14;
        public bool ForbidSoldiers;
        public string SurfaceStyle="";
        public string Safety="";
        public List<BattleTacticalObject> TacticalObjects=new List<BattleTacticalObject>();
        public string SourceMapId,ParentMapId;
        public BaseEnemyPreset Enemy;
        public double SpawnX=12, SpawnY=6.5;
        public List<BaseMapPlacement> Objects=new List<BaseMapPlacement>();
        public BaseMapLayout Copy() => new BaseMapLayout {Region=Region,Title=Title,ForbidSoldiers=ForbidSoldiers,SurfaceStyle=SurfaceStyle,Safety=Safety,Columns=Columns,Rows=Rows,TacticalObjects=(TacticalObjects??new List<BattleTacticalObject>()).Select(o=>o.Copy()).ToList(),SourceMapId=SourceMapId,ParentMapId=ParentMapId,Enemy=Enemy?.Copy(),SpawnX=SpawnX,SpawnY=SpawnY,Objects=Objects.Select(o=>o.Copy()).ToList()};
    }
    public sealed class BaseMapDesign
    {
        public BaseMapLayout Layout;
        public BattleBoard Board;
        public void Validate()
        {
            BaseSpatial.ValidateLayout(Layout);
            if(Board==null||Board.Width!=Layout.Columns||Board.Height!=Layout.Rows||Board.Cells?.Length!=Board.Width*Board.Height||Board.Cells.Any(c=>c==null||!c.MetadataValid||!Enum.IsDefined(typeof(TerrainKind),c.Terrain)||!Enum.IsDefined(typeof(DeploymentZone),c.Zone)))throw new ArgumentException("实验地图当前使用24×14格，请检查地形数据");
            BaseObjectFootprint.ValidateBoard(Layout,Board);
            if(Layout.Region=="world"&&Board.Cells.Any(c=>c.Terrain!=TerrainKind.Ground||c.Zone!=DeploymentZone.None||c.Elevation!=0||c.Ramp!=RampAxis.None))throw new ArgumentException("世界地图只允许导航空地，不放置战术地形或部署区");
            if(!BaseSpatial.Geometry(Layout.Objects.Select(o=>o.Object),Layout.SpawnX,Layout.SpawnY,Board.Width,Board.Height)||!BaseSpatial.TerrainWalkable(Board,Layout.SpawnX,Layout.SpawnY,Layout.Objects.Select(o=>o.Object).ToArray()))throw new ArgumentException(Layout.Title+"：玩家入口位于不可通行处，请移动入口或障碍物");
            foreach(var obj in Layout.Objects.Where(o=>!o.Id.StartsWith("partner")&&!o.Blocks&&!o.VisualOnly))
                if(!Board.Positions().Any(p=>BaseInteraction.WithinReach(Board,obj.Object,p.X+.5,p.Z+.5)&&Board.Walkable(p)))throw new ArgumentException("交互点无法在同层一格内接近："+obj.Name);
            if(Board.Cells.Any(c=>c.Elevation>0||c.Ramp!=RampAxis.None))
            {
                var start=new GridPosition((int)Math.Floor(Layout.SpawnX),(int)Math.Floor(Layout.SpawnY));var reached=new HashSet<GridPosition>{start};var open=new Queue<GridPosition>();open.Enqueue(start);
                while(open.Count>0){var from=open.Dequeue();foreach(var d in new[]{new GridPosition(1,0),new GridPosition(-1,0),new GridPosition(0,1),new GridPosition(0,-1)}){var to=from+d;if(Board.CanStep(from,to)&&reached.Add(to))open.Enqueue(to);}}
                foreach(var obj in Layout.Objects.Where(o=>!o.Blocks&&!o.Id.StartsWith("partner")&&!o.VisualOnly))
                    if(!reached.Any(p=>BaseInteraction.WithinReach(Board,obj.Object,p.X+.5,p.Z+.5)))throw new ArgumentException("出生点无法经坡道在同层一格内接近交互点："+obj.Name);
            }
            if(Layout.Region=="perimeter"&&(!Board.Walkable(new GridPosition(Board.Width-1,6))||!Board.Walkable(new GridPosition(0,6))||!Board.Cells.Any(c=>c.Zone==DeploymentZone.A&&c.Walkable)||!Board.Cells.Any(c=>c.Zone==DeploymentZone.B&&c.Walkable)))throw new ArgumentException("迎敌地图须保留东西撤离格(23,6)/(0,6)，并保留双方可部署格");
        }
        public void Apply(BaseState s)
        {
            if(BaseSceneInstances.Active(s))throw new InvalidOperationException("场景实例组合请在浏览器编辑后重新加载，旧单图工坊不能按区域覆盖实例");
            Validate();s.Campaign.Layouts??=new List<BaseMapLayout>();s.Campaign.Layouts.RemoveAll(m=>m.Region==Layout.Region);s.Campaign.Layouts.Add(Layout.Copy());
            if(Layout.Region=="perimeter")s.Campaign.PerimeterBoard=Board.Clone();
            else {s.Campaign.RegionMaps.RemoveAll(m=>m.Id==Layout.Region);s.Campaign.RegionMaps.Add(new BaseRegionMap {Id=Layout.Region,Board=Board.Clone()});}
            if(Layout.Region=="base")
            {
                s.Campaign.BaseX=Layout.SpawnX;s.Campaign.BaseY=Layout.SpawnY;
                foreach(var marker in Layout.Objects.Where(o=>o.Id.StartsWith("partner")))
                {var l=s.Leaders.FirstOrDefault(p=>p.Id==marker.Id);if(l==null)continue;l.MapRegion="base";l.HasHomePosition=l.HasMapPosition=true;l.HomeX=l.MapX=marker.X;l.HomeY=l.MapY=marker.Y;}
            }
            if(s.Campaign.Location==Layout.Region){s.PlayerX=Layout.SpawnX;s.PlayerY=Layout.SpawnY;}
        }
    }
    public sealed class BaseMotionAssist
    {
        internal int Direction, Slide;
        internal double Anchor, Hold;
    }
    public static class BaseSpatial
    {
        public static BaseMapLayout Layout(BaseState s,string region=null) => BaseSceneInstances.Active(s)?BaseSceneInstances.Layout(s,region):s.Campaign.Layouts?.SingleOrDefault(m=>m.Region==(region??s.Campaign.Location));
        public static BaseMapObject[] Objects(BaseState s,string region=null) => (Layout(s,region)?.Objects.Select(o=>o.Object).ToArray()??BaseCampaign.MapObjects(region??s.Campaign.Location)).Where(obj=>obj.Id!="town_inn"&&(obj.Id!="prologue_report"||!BaseDemoIntro.Daily(s)&&!(BaseDemoIntro.Active(s)&&s.Demo.Intro.ReporterGone))).Select(obj=>BaseDemoNpcs.Present(s,obj)).Concat(region==null||region==s.Campaign.Location?BaseDemoNpcs.Additional(s):Array.Empty<BaseMapObject>()).Concat(BaseDemoNpcs.CampResidentsCompat(s,region)).Concat(BaseDemoNpcs.TownChatCompat(s,region)).Concat(TacticalMarkers(s,region)).Concat(BaseItemRecovery.Markers(s,region??s.Campaign.Location)).Select(o=>o.Id=="demo_rescued_1"||o.Id=="demo_rescued_2"||o.Id=="town_guard"||o.Id=="town_girl"||o.Id=="prologue_report"?BaseDemoNpcs.Present(s,o):o).ToArray();
        private static IEnumerable<BaseMapObject> TacticalMarkers(BaseState s,string region)
        {
            var layout=Layout(s,region);if(layout?.TacticalObjects==null)yield break;
            foreach(var o in layout.TacticalObjects)foreach(var p in o.Fallen?o.Trunk:new List<GridPosition>{new GridPosition(o.X,o.Y)})
                yield return new BaseMapObject("tactic:"+o.Id+":"+p.X+":"+p.Z,o.Kind=="rock"?"巨石":o.Fallen?"倒下的树干":"枯树",p.X+.5,p.Z+.5,1,1,true,"tactic:"+o.Id,null,o.Fallen?"tactical-trunk":o.Kind=="tree"?"tactical-tree":"tactical-rock",true);
        }
        public static bool Geometry(IEnumerable<BaseMapObject> objects,double x,double y,double width=24,double height=14)
        {
            if(!BaseMap.Walkable(x,y,null,false,width,height))return false;
            foreach(var o in objects)if(!o.Id.StartsWith("partner")&&BaseMap.Blocks(o,x,y))return false;
            return true;
        }
        public static bool Geometry(BaseState s,double x,double y,string region=null) => Geometry(Objects(s,region),x,y,BaseBattlefield.Board(s,region).Width,BaseBattlefield.Board(s,region).Height);
        public static bool TerrainWalkable(BattleBoard b,double x,double y,BaseMapObject[] objects=null)
        {
            int left=(int)Math.Floor(x-BaseMap.Radius),right=(int)Math.Floor(x+BaseMap.Radius),bottom=(int)Math.Floor(y-BaseMap.Radius),top=(int)Math.Floor(y+BaseMap.Radius);
            var a=new GridPosition(left,bottom);var c=new GridPosition(right,bottom);var d=new GridPosition(left,top);var e=new GridPosition(right,top);
            // The footprint spans at most two cells per axis; check its four edges without per-probe arrays/closures.
            return Passable(b,a,objects)&&Passable(b,c,objects)&&Passable(b,d,objects)&&Passable(b,e,objects)
                &&(left==right||TerrainConnection(b,a,c)&&TerrainConnection(b,d,e))
                &&(bottom==top||TerrainConnection(b,a,d)&&TerrainConnection(b,c,e));
        }
        private static bool Passable(BattleBoard board,GridPosition cell,BaseMapObject[] objects)
        {
            if(board.Walkable(cell))return true;
            if(board.Cell(cell)?.Terrain!=TerrainKind.Building||objects==null)return false;
            foreach(var obj in objects)if(!string.IsNullOrEmpty(obj.SourceObjectId)&&obj.Id==board.Cell(cell).BuildingId)return true;
            return false;
        }
        private static bool TerrainConnection(BattleBoard b,GridPosition from,GridPosition to)
        {
            if(!b.HeightConnection(from,to))return false;
            if (b.IsDoubleLowRamp(from) || b.IsDoubleLowRamp(to)) return true;
            var a=b.Cell(from);var c=b.Cell(to);
            var axis=from.X==to.X ? RampAxis.NorthSouth : RampAxis.EastWest;
            if((a.Ramp==RampAxis.None||a.Ramp==axis)&&(c.Ramp==RampAxis.None||c.Ramp==axis))return true;
            // Adjacent lanes may share a side only when their incline profiles match.
            if(a.Ramp!=c.Ramp||a.Elevation!=c.Elevation)return false;
            var step=a.Ramp==RampAxis.NorthSouth ? new GridPosition(0,1) : new GridPosition(1,0);
            var back=new GridPosition(-step.X,-step.Z);
            return (b.Cell(from+step)?.Elevation<a.Elevation)==(b.Cell(to+step)?.Elevation<c.Elevation)
                &&(b.Cell(from+back)?.Elevation<a.Elevation)==(b.Cell(to+back)?.Elevation<c.Elevation);
        }
        public static double MovementFactor(BattleBoard board,double x,double y,double dx,double dy)
        {
            var p=new GridPosition((int)Math.Floor(x),(int)Math.Floor(y));var cell=board.Cell(p);
            if(cell?.Terrain==TerrainKind.Swamp)return .5;
            if(cell==null||cell.Ramp==RampAxis.None)return 1;
            var step=cell.Ramp==RampAxis.EastWest ? new GridPosition(1,0) : new GridPosition(0,1);
            double along=cell.Ramp==RampAxis.EastWest ? dx : dy;
            if(along==0)return 1;
            if(board.Cell(p+new GridPosition(-step.X,-step.Z))?.Elevation==cell.Elevation-1)return .7;
            if(board.Cell(p+step)?.Elevation==cell.Elevation-1)return cell.Ramp==RampAxis.NorthSouth ? 1 : .7;
            return 1;
        }
        public static bool Walkable(BaseState s,double x,double y,string region=null)
        {var objects=Objects(s,region);var board=BaseBattlefield.Board(s,region);return Geometry(objects,x,y,board.Width,board.Height)&&TerrainWalkable(board,x,y,objects);}
        public static void Move(BaseState s,ref double x,ref double y,double dx,double dy,IEnumerable<BaseMapObject> occupants=null,BaseMotionAssist assist=null,BaseMapObject[] geometry=null)
        {
            var board=BaseBattlefield.Board(s);var blockers=occupants?.ToArray();var objects=geometry??Objects(s);
            bool Terrain(double a,double b)=>Geometry(objects,a,b,board.Width,board.Height)&&TerrainWalkable(board,a,b,objects);
            double factorX=MovementFactor(board,x,y,dx,0),factorY=MovementFactor(board,x,y,0,dy);
            dx*=factorX;dy*=factorY;
            double oldX=x,oldY=y;
            BaseMap.Move(ref x,ref y,dx,dy,blockers,false,Terrain,board.Width,board.Height);
            bool horizontal=Math.Abs(dx)>=Math.Abs(dy);double along=horizontal ? dx : dy;
            int direction=(horizontal ? 1 : 2)*Math.Sign(along);
            if(assist!=null)assist.Hold=Math.Max(0,assist.Hold-Math.Sqrt(dx*dx+dy*dy));
            if(Math.Abs(along)<.00001||Math.Abs(horizontal ? x-oldX : y-oldY)>=Math.Abs(along)*.8)
            {if(assist!=null){assist.Direction=0;assist.Slide=0;}return;}
            double side=horizontal ? y : x;
            if(assist==null)assist=new BaseMotionAssist();
            if(assist.Direction!=direction){assist.Direction=direction;assist.Slide=0;assist.Anchor=side;assist.Hold=0;}
            var origin=new BasePoint(x,y);double chosen=0;
            foreach(int sign in assist.Slide!=0&&assist.Hold>0 ? new[]{assist.Slide} : new[]{1,-1})
                for(double amount=.02;amount<=.341;amount+=.02)
                {
                    double target=side+sign*amount;
                    if(Math.Abs(target-assist.Anchor)>.341)break;
                    var aligned=horizontal ? new BasePoint(x,target) : new BasePoint(target,y);
                    var forward=horizontal ? new BasePoint(aligned.X+along,aligned.Y) : new BasePoint(aligned.X,aligned.Y+along);
                    if(BaseMap.ClearSegment(origin,aligned,blockers,Terrain,false,board.Width,board.Height)&&BaseMap.ClearSegment(aligned,forward,blockers,Terrain,false,board.Width,board.Height))
                    {if(chosen==0||amount<Math.Abs(chosen))chosen=sign*amount;break;}
                }
            if(chosen==0)return;
            if(assist.Slide!=Math.Sign(chosen)){assist.Slide=Math.Sign(chosen);assist.Hold=1;}
            double remaining=Math.Max(0,Math.Sqrt(dx*dx+dy*dy)-new BasePoint(x,y).Distance(new BasePoint(oldX,oldY)));
            double correction=Math.Sign(chosen)*Math.Min(Math.Abs(chosen),remaining);
            if(horizontal)BaseMap.Move(ref x,ref y,0,correction,blockers,false,Terrain,board.Width,board.Height);
            else BaseMap.Move(ref x,ref y,correction,0,blockers,false,Terrain,board.Width,board.Height);
        }

        public static BasePoint Nearest(BaseState s,BasePoint origin,string region=null,IEnumerable<BaseMapObject> occupants=null)
        {
            var blockers=occupants?.ToArray();
            var board = BaseBattlefield.Board(s, region);
            var objects = Objects(s, region);
            bool Available(BasePoint p) => Geometry(objects, p.X, p.Y, board.Width, board.Height) &&
                TerrainWalkable(board, p.X, p.Y, objects) &&
                (blockers == null || BaseMap.Walkable(p.X, p.Y, blockers, false, board.Width, board.Height));
            if (Available(origin)) return origin;

            BasePoint nearest = default;
            double bestDistance = double.PositiveInfinity;
            foreach (var cell in board.Positions())
            {
                var candidate = new BasePoint(cell.X + .5, cell.Z + .5);
                double distance = candidate.Distance(origin);
                if (distance.CompareTo(bestDistance) >= 0 || !Available(candidate)) continue;
                nearest = candidate;
                bestDistance = distance;
            }
            if (double.IsPositiveInfinity(bestDistance)) throw new ArgumentException("地图没有可站立的位置");
            return nearest;
        }
        public static BasePoint WorkPost(BaseState s,string id)
        {
            var o=Objects(s,"base").Single(x=>x.Id==id);return Nearest(s,new BasePoint(o.X,o.Y-o.Height/2-.75),"base");
        }
        public static BaseMapObject AtCell(BaseState s,int x,int y) => Objects(s).FirstOrDefault(o=>!o.Id.StartsWith("partner")&&(string.IsNullOrEmpty(o.Shape)?x<o.X+o.Width/2&&x+1>o.X-o.Width/2&&y<o.Y+o.Height/2&&y+1>o.Y-o.Height/2:o.Parts.Any(part=>part.Touches(x,y))));
        public static void ValidateLayout(BaseMapLayout m,bool instances=false,bool demo=false,bool scenery=false,bool story=false)
        {
            if(m?.Enemy!=null){m.Enemy.Validate();if(m.Enemy.Version!=0&&(m.Region!="outpost"||!instances))throw new ArgumentException("自定义敌营编制只支持敌营实例");}
            bool Valid(double n)=>!double.IsNaN(n)&&!double.IsInfinity(n);
            if(m==null||!BaseCampaign.Locations.Contains(m.Region)&&!(instances&&m.Region=="custom")||string.IsNullOrWhiteSpace(m.Title)||m.Title.Length>100||!Valid(m.SpawnX)||!Valid(m.SpawnY)||m.Objects==null||m.Objects.Count>(story?300:200)||m.Objects.Any(o=>o==null||string.IsNullOrWhiteSpace(o.Id)||o.Id.Length>100||string.IsNullOrWhiteSpace(o.Name)||o.Name.Length>100||!Valid(o.X)||!Valid(o.Y)||!Valid(o.Width)||!Valid(o.Height)||o.Width<=0||o.Height<=0||o.X-o.Object.BoundsWidth/2<0||o.X+o.Object.BoundsWidth/2>m.Columns||o.Y-o.Object.BoundsHeight/2<0||o.Y+o.Object.BoundsHeight/2>m.Rows)||m.Objects.Select(o=>o.Id).Distinct().Count()!=m.Objects.Count)throw new ArgumentException("编辑地图的区域、标识、坐标或占地非法");
            if(!string.IsNullOrEmpty(m.Safety)&&m.Safety!="safe"&&m.Safety!="dangerous")throw new ArgumentException("地图安全属性非法");
            if(m.SurfaceStyle!=""&&m.SurfaceStyle!="dungeon")throw new ArgumentException("未知地图材质主题");
            if(m.Columns<6||m.Columns>(story?64:32)||m.Rows<6||m.Rows>(story?64:24))throw new ArgumentException("地图尺寸超出当前格式支持范围");
            foreach(var placement in m.Objects)BaseObjectFootprint.Validate(placement);
            foreach(var o in m.Objects.Where(o=>BaseEnvironment.Decorative(o.Object))){var d=BaseEnvironment.Find(o.AssetId);if(d==null||!o.VisualOnly||o.Kind!=(d.Animal==""?"decoration":"wildlife"))throw new ArgumentException("环境物件定义、交互或阻挡声明非法："+o.Id);}
            foreach(var placement in m.Objects.Where(obj=>obj.Blocks&&!string.IsNullOrEmpty(obj.Shape)))
                if(m.Objects.Any(other=>other!=placement&&other.Blocks&&placement.Object.Parts.Any(first=>other.Object.Parts.Any(second=>Math.Abs(first.X-second.X)<(first.Width+second.Width)/2-.00001&&Math.Abs(first.Y-second.Y)<(first.Height+second.Height)/2-.00001))))throw new ArgumentException("格形阻挡重叠："+placement.Name);
            if(m.Region=="base"&&!scenery&&(demo?new[]{"home","barracks"}:new[]{"home","government","farm","workshop","barracks"}).Any(id=>!m.Objects.Any(o=>o.Id==id&&o.Blocks)))throw new ArgumentException(demo?"Demo须保留营帐与兵营的固定位置":"据点实验模板须保留五种设施的唯一功能标识");
        }
        public static void ValidateLayouts(BaseState s)
        {
            if(BaseSceneInstances.Active(s)){BaseSceneInstances.ValidateLayouts(s);return;}
            s.Campaign.Layouts??=new List<BaseMapLayout>();if(s.Campaign.Layouts.Count>(BaseSceneRules.Active(s)?7:5)||s.Campaign.Layouts.Select(m=>m?.Region).Distinct().Count()!=s.Campaign.Layouts.Count)throw new ArgumentException("编辑地图重复或数量非法");foreach(var m in s.Campaign.Layouts){ValidateLayout(m,demo:BaseDemo.Active(s),scenery:BaseDemoPrologue.Village(s),story:s.Demo?.StoryScene?.Valid==true);if(m.Objects.Any(obj=>!string.IsNullOrEmpty(obj.Shape)))BaseObjectFootprint.ValidateBoard(m,BaseBattlefield.Board(s,m.Region));}
        }
        public static void RepairGridPositions(BaseState s)
        {
            // Legacy coordinates can pass continuous building tests but overlap a blocked tile.
            // Preserve every already-valid position; never reset NPCs to their default homes.
            var point=Nearest(s,new BasePoint(s.PlayerX,s.PlayerY));s.PlayerX=point.X;s.PlayerY=point.Y;
            point=Nearest(s,new BasePoint(s.Campaign.BaseX,s.Campaign.BaseY),"base");s.Campaign.BaseX=point.X;s.Campaign.BaseY=point.Y;
            foreach(var l in s.Leaders)
            {
                if(l.HasHomePosition){point=Nearest(s,new BasePoint(l.HomeX,l.HomeY),"base");l.HomeX=point.X;l.HomeY=point.Y;}
                if(l.HasMapPosition){point=Nearest(s,new BasePoint(l.MapX,l.MapY),BaseAdventure.MapKey(s,l));l.MapX=point.X;l.MapY=point.Y;}
            }
        }
    }
}

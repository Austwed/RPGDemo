using System;
using System.Collections.Generic;
using System.Linq;
using BaseConquest.Battle;
namespace BaseConquest.Base
{
    [Serializable] public sealed class EnvironmentDefinition
    {
        public string Id,Name,Animal;public double Width,Depth,ArtWidth,ArtHeight,Sight;public bool Blocks;
        public EnvironmentDefinition(string id,string name,double width,double depth,double artWidth,double artHeight,bool blocks=false,double sight=0,string animal="")
        {Id=id;Name=name;Width=width;Depth=depth;ArtWidth=artWidth;ArtHeight=artHeight;Blocks=blocks;Sight=sight;Animal=animal;}
    }
    public static class BaseEnvironment
    {
        public static readonly EnvironmentDefinition[] Catalog={
            new EnvironmentDefinition("tree-oak-a","阔叶树",.8,.8,3.4,4.2,true),new EnvironmentDefinition("tree-oak-b","细干阔叶树",.6,.6,2.6,4,true),new EnvironmentDefinition("tree-pine","松树",.7,.7,2.3,4,true),new EnvironmentDefinition("tree-dead","枯树装饰",.7,.7,2.5,3,true),
            new EnvironmentDefinition("rock-large","大石",1.3,1.1,1.8,1.4,true),new EnvironmentDefinition("rock-small","小石",.6,.5,.8,.6),new EnvironmentDefinition("rock-scatter","碎石",1,1,1.2,.6),new EnvironmentDefinition("bush-round","圆灌木",1,1,1.3,1),new EnvironmentDefinition("bush-wide","矮灌木",1.5,.7,1.8,.8),new EnvironmentDefinition("grass-tuft","草丛",.7,.7,.8,.7),new EnvironmentDefinition("flowers-yellow","黄花丛",.7,.7,.8,.7),new EnvironmentDefinition("flowers-white","白花丛",.7,.7,.8,.7),
            new EnvironmentDefinition("house-small","木屋",3,2,3.7,3.5,true,3),new EnvironmentDefinition("house-stone","石屋",3,2,3.7,3.3,true,3),new EnvironmentDefinition("house-long","长屋",4,2,4.6,3.1,true,3),new EnvironmentDefinition("house-two-story","双层住宅",2.5,2,3.1,4.5,true,3),new EnvironmentDefinition("house-row","联排住宅",4.5,2,5.1,3.5,true,3),
            new EnvironmentDefinition("shop-bakery","面包铺外观",3,1.6,3.5,3.2,true,3),new EnvironmentDefinition("shop-cloth","布店外观",3,1.6,3.5,3.2,true,3),new EnvironmentDefinition("shop-general","杂货铺外观",3,1.6,3.5,3.2,true,3),new EnvironmentDefinition("lord-manor","领主宅邸",3.4,1.8,3.9,4.5,true,3),
            new EnvironmentDefinition("bench","长椅",1.2,.5,1.4,.9,true),new EnvironmentDefinition("lamp","路灯",.3,.3,.7,2,true),new EnvironmentDefinition("fountain","喷泉",1.3,1.1,1.7,1.3,true),new EnvironmentDefinition("planter","花坛",1,.5,1.3,.7,true),new EnvironmentDefinition("fence","木栅栏",1.5,.3,1.8,1,true),new EnvironmentDefinition("handcart","手推车",1,.8,1.4,1.2,true),new EnvironmentDefinition("crates-barrels","箱桶",.8,.8,1.2,1,true),
            new EnvironmentDefinition("ruin-arch","封堵石拱",2,1,3,3,true,2),new EnvironmentDefinition("ruin-wall","残墙",2,.7,2.5,1.8,true,2),new EnvironmentDefinition("ruin-foundation","废弃地基",2,1.5,3,1),new EnvironmentDefinition("rabbit-hole","兔子洞",.5,.5,.6,.4),
            new EnvironmentDefinition("bird-flock","鸟群",1,1,.4,.4,false,0,"bird"),new EnvironmentDefinition("rabbit-burrow","兔子与洞口",1.8,.7,.55,.55,false,0,"rabbit")};
        public static EnvironmentDefinition Find(string id)=>Catalog.FirstOrDefault(d=>d.Id==id);
        public static bool Decorative(BaseMapObject o)=>o?.Kind=="decoration"||o?.Kind=="wildlife";
        public static bool Active(BaseState s)=>s?.Demo?.EnvironmentVersion==1;
        public static BasePoint FineCenter(BasePoint p)=>new BasePoint(Math.Floor(p.X*2)*.5+.25,Math.Floor(p.Y*2)*.5+.25);
        public static int[] RoadEdgeSlots(bool[] sides,bool[] corners)
        {
            var slots=new List<int>();int count=sides.Count(v=>v);
            // Corner order NW, NE, SE, SW; a concave tile leaves the opposite corner open.
            int[][] adjacent={new[]{0,3},new[]{0,1},new[]{1,2},new[]{2,3}};
            int elbow=-1;if(count==2)for(int c=0;c<4;c++)if(sides[adjacent[c][0]]&&sides[adjacent[c][1]])elbow=c;
            if(elbow>=0)slots.Add(8+(elbow+2)%4);else for(int n=0;n<4;n++)if(sides[n])slots.Add(n);
            for(int c=0;c<4;c++)if(corners[c]&&!sides[adjacent[c][0]]&&!sides[adjacent[c][1]])slots.Add(4+c);
            return slots.ToArray();
        }
        public static void Install(BaseState s,bool preserveRelease=false)
        {
            if(!BaseSceneRules.Active(s))return;s.Demo.EnvironmentVersion=1;
            if(s.Demo.StoryScene!=null){foreach(var map in s.Campaign.Layouts.Where(map=>map.Region=="dungeon"))Populate(map,BaseBattlefield.Board(s,map.Region));return;}
            foreach(var m in s.Campaign.Layouts.Where(m=>m.Region!="world"&&(!preserveRelease||m.Region!="outpost"&&m.Region!="perimeter")))Populate(m,BaseBattlefield.Board(s,m.Region));
            if(s.Demo.Finale?.Village!=null)Populate(s.Demo.Finale.Village,BattleBoard.CreateDefault(24,14));
            foreach(var m in s.Demo.ReleaseMaps)
            {
                if(preserveRelease)continue;
                if(m.Role=="bandit"){Populate(m.Layout,m.Board);s.Demo.Finale.CampBoard=m.Board.Clone();}
                else {var original=s.Campaign.Layouts.Single(l=>l.Region==(m.Role=="ruins"?"outpost":"perimeter"));m.Layout=original.Copy();m.Board=BaseBattlefield.Board(s,original.Region).Clone();}
            }
        }
        public static void Populate(BaseMapLayout m,BattleBoard b)
        {
            m.Objects.RemoveAll(o=>o.Id.StartsWith("env_",StringComparison.Ordinal)||o.Id=="ruins_wall");
            bool dungeon=m.SurfaceStyle=="dungeon",town=m.Region=="town",village=m.Objects.Any(o=>o.Id=="village_house");
            foreach(var p in b.Positions())
            {var c=b.Cell(p);if(p.X==15&&p.Z>=4&&p.Z<=19&&c.Terrain==TerrainKind.Wall)c.Terrain=TerrainKind.Ground;if(!dungeon&&c.Terrain==TerrainKind.Ground)c.Surface=c.Ramp!=RampAxis.None||town&&c.Elevation>0?GroundSurface.Road:GroundSurface.Grass;}
            void Road(int x,int y,GroundSurface surface){var c=b.Cell(new GridPosition(x,y));if(c!=null&&c.Walkable&&c.Ramp==RampAxis.None&&(!town||c.Elevation==0))c.Surface=surface;}
            var road=town?GroundSurface.Cobble:GroundSurface.DirtPath;
            if(!dungeon)
            {
                int lane=b.Width==32?8:6;
                for(int x=1;x<b.Width-1;x++)Road(x,lane,road);
                for(int y=2;y<b.Height-2;y++)Road(town?11:village?8:12,y,road);
                if(b.Width==32)for(int x=3;x<b.Width-3;x++)Road(x,17,road);
                if(town)for(int x=11;x<=15;x++)Road(x,4,road);
            }
            int serial=0;
            bool Place(string id,double x,double y)
            {
                var d=Find(id);if(d.Blocks&&d.Width<1)x=Math.Floor(x)+.5;if(d.Blocks&&d.Depth<1)y=Math.Floor(y)+.5;var o=new BaseMapPlacement{Id="env_"+serial++,SourceObjectId="env_"+(m.SourceMapId??m.Region)+"_"+serial,DefinitionId="env_"+id,AssetId=id,Name=d.Name,Kind=d.Animal==""?"decoration":"wildlife",VisualOnly=true,Blocks=d.Blocks,X=x,Y=y,Width=d.Width,Height=d.Depth};
                if(x-d.Width/2<.4||y-d.Depth/2<.4||x+d.Width/2>b.Width-.4||y+d.Depth/2>b.Height-.4)return false;
                if(new BasePoint(x,y).Distance(new BasePoint(m.SpawnX,m.SpawnY))<2)return false;
                // Leave the teaching facilities, their approach and villagers clear of canopy.
                if(village&&id.StartsWith("tree-")&&m.Objects.Any(v=>(v.Id=="intro_farm"||v.Id=="intro_smith")&&Math.Abs(v.X-x)<v.Width/2+d.ArtWidth/2+.5&&Math.Abs(v.Y-y)<v.Height/2+2.5))return false;
                if(village&&d.Blocks&&new[]{new BasePoint(3,8),new BasePoint(18,10)}.Any(p=>p.Distance(new BasePoint(x,y))<2))return false;
                var cells=b.Positions().Where(p=>o.Object.Contains(p.X+.5,p.Z+.5)).DefaultIfEmpty(new GridPosition((int)x,(int)y)).ToArray();
                if(cells.Length==0||cells.Any(p=>!b.Walkable(p)||b.Cell(p).Ramp!=RampAxis.None||d.Blocks&&b.Cell(p).Zone!=DeploymentZone.None||d.Blocks&&(b.Cell(p).Surface==GroundSurface.Cobble||b.Cell(p).Surface==GroundSurface.DirtPath))||cells.Select(p=>b.Cell(p).Elevation).Distinct().Count()>1)return false;
                if(m.Objects.Any(v=>(v.Blocks||!v.VisualOnly)&&v.Object.Parts.Any(r=>Math.Abs(r.X-x)<(r.Width+d.Width)/2+.4&&Math.Abs(r.Y-y)<(r.Height+d.Depth)/2+.4)))return false;
                if(m.TacticalObjects.Any(v=>Math.Abs(v.X+.5-x)<d.Width/2+1&&Math.Abs(v.Y+.5-y)<d.Depth/2+1))return false;
                if(town&&Math.Abs(x-7)<d.Width/2+.8&&Math.Abs(y-11)<d.Depth/2+.8)return false;
                m.Objects.Add(o);return true;
            }
            if(town)
            {
                Place("house-small",3,11);Place("house-two-story",12.5,11.5);Place("shop-bakery",3,2.1);Place("shop-cloth",7,2.1);Place("shop-general",20.5,3);Place("lord-manor",21,11.5);
                Place("fountain",5,9);Place("bench",3,8.1);Place("bench",7,8.1);Place("planter",5,10.5);Place("lamp",10,7.5);Place("lamp",14,7.5);Place("handcart",14,9.5);Place("crates-barrels",20.5,5);Place("bird-flock",5,7.5);
            }
            else if(village)
            {Place("house-stone",14,11.5);Place("house-long",18.5,3);Place("fence",10,3);Place("handcart",12,11);Place("rabbit-burrow",19,10);Place("bird-flock",14,8.5);}
            else if(dungeon)
            {Place("ruin-wall",11,3);Place("ruin-arch",22,20);Place("ruin-foundation",25,3);Place("rock-scatter",8,12);Place("rock-scatter",20,12);}
            else if(m.Region=="base")
            {Place("house-small",2.5,2);Place("crates-barrels",17,10);Place("fence",8,12);Place("handcart",17,2);Place("bird-flock",7,6);Place("rabbit-burrow",22,11);}
            else
            {
                Place("tree-oak-a",8.5,12.5);Place("tree-oak-b",9.5,13.5);Place("tree-pine",20.5,5.5);Place("rock-large",11.5,4.5);Place("ruin-arch",20,12);Place("ruin-wall",13,13);Place("ruin-foundation",24,19);Place("rabbit-burrow",7,10);Place("bird-flock",9,5);Place("rock-large",23,10);Place("tree-dead",27,13);
            }
            if(!dungeon)
            {
                string[] trees={"tree-oak-a","tree-pine","tree-oak-b"};
                for(int x=2;x<b.Width-1;x+=4){Place(trees[(x/4)%3],x,b.Height-1.4);Place(trees[(x/4+1)%3],x,1.5);}
                string[] detail={"grass-tuft","flowers-yellow","flowers-white","rock-small","bush-round","bush-wide","rock-scatter"};
                for(int x=2;x<b.Width-1;x+=3)for(int y=3;y<b.Height-2;y+=5)Place(detail[(x+y)%detail.Length],x+.3,y+.4);
            }
        }
    }
    public sealed class EnvironmentAnimal
    {
        public readonly string Kind;public readonly BasePoint Home;public BasePoint Position;public string Pose="idle";public double Clock,StateTime;public int State,Direction=1;private double away,danger;private readonly double delay;
        public EnvironmentAnimal(string kind,BasePoint home,double delay=0){Kind=kind;Home=home;Position=kind=="rabbit"?new BasePoint(home.X+1,home.Y):home;this.delay=delay;}
        public void Tick(double seconds,BasePoint player,bool paused=false)
        {
            if(paused||seconds<=0)return;Clock+=seconds;StateTime+=seconds;double distance=player.Distance(Home);
            if(Kind=="bird")
            {
                if(State==0){danger=distance<=2?danger+seconds:0;if(danger>delay){State=1;StateTime=0;Direction=player.X>Home.X?-1:1;Pose="fly";}}
                if(State==1){Position=new BasePoint(Home.X+Direction*StateTime*3,Home.Y+StateTime*2);if(StateTime>=2){State=2;Pose="hidden";}}
                return;
            }
            if(State==0&&player.Distance(Position)<=2){State=1;StateTime=0;Pose="walk";Direction=-1;}
            if(State==1){double t=Math.Min(1,StateTime/.5);Position=new BasePoint(Home.X+1-t,Home.Y);if(t>=1){State=2;StateTime=0;Pose="burrow";}}
            else if(State==2&&StateTime>=.65){State=3;StateTime=0;Pose="hidden";}
            else if(State==3){away=distance>3?away+seconds:0;if(away>=2){State=4;StateTime=0;Direction=1;Pose="emerge";}}
            else if(State==4&&StateTime>=.7){State=5;StateTime=0;Pose="walk";}
            else if(State==5){double t=Math.Min(1,StateTime/.5);Position=new BasePoint(Home.X+t,Home.Y);if(t>=1){State=0;StateTime=0;Pose="idle";away=0;}}
            else if(State==0)Position=new BasePoint(Home.X+1,Home.Y);
        }
    }
}

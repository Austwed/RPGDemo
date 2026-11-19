using System;
using System.Collections.Generic;
using System.Linq;
using BaseConquest.Battle;

namespace BaseConquest.Base
{
    [Serializable] public sealed class BaseRescueJourney
    {
        public int Stage;
        public double FirstX=11,FirstY=8.5,SecondX=12,SecondY=8.5;
        public int EntranceLayoutVersion;
        public double GalanX,GalanY;
        public BaseRescueJourney Copy()=>(BaseRescueJourney)MemberwiseClone();
    }
    [Serializable] public sealed class BaseTerrainMemory
    {
        public string MapId;public int Width,Height;public bool[] Known;
        public BaseTerrainMemory Copy()=>new BaseTerrainMemory{MapId=MapId,Width=Width,Height=Height,Known=Known?.ToArray()};
    }
    public static class BaseSceneRules
    {
        public const string DungeonId="demo_dungeon";
        private const string EntranceId = "demo_ruins_investigate";
        internal const int EntrancePeopleLayout = 2;
        private const double EntranceHalfWidth = 1.15, DoorHalfWidth = .40;
        private const double PostFront = .25, DoorSill = .52, EntranceBack = 1.05;
        public static bool Active(BaseState s)=>s?.Demo?.Rpg?.SceneRulesVersion==1;
        public static bool Dungeon(BaseState s)=>Active(s)&&(BaseSceneInstances.Active(s)?s.Campaign.CurrentMapId==DungeonId:s.Campaign.Location=="dungeon");
        public static bool Entrance(BaseState s)=>Active(s)&&(BaseSceneInstances.Active(s)?s.Campaign.CurrentMapId=="demo_ruins":s.Campaign.Location=="outpost");

        internal static bool BlocksEntrance(BaseMapObject portal, double x, double y)
        {
            if (portal.Id != EntranceId || portal.AssetId != "dungeon-entrance") return false;
            // 两侧石柱比正中的门槛靠前，按角色原有半径检查三段实体占地。
            double lateral = Math.Abs(x - portal.X), north = y - portal.Y;
            double front = lateral + BaseMap.Radius > DoorHalfWidth ? PostFront : DoorSill;
            return lateral < EntranceHalfWidth + BaseMap.Radius &&
                north > front - BaseMap.Radius && north < EntranceBack + BaseMap.Radius;
        }

        internal static IEnumerable<BaseObjectRect> EntranceCollisionParts(BaseMapObject portal)
        {
            if (portal.Id != EntranceId || portal.AssetId != "dungeon-entrance") yield break;
            double sideWidth = EntranceHalfWidth - DoorHalfWidth;
            double postY = portal.Y + (PostFront + EntranceBack) * .5;
            yield return new BaseObjectRect(portal.X - DoorHalfWidth - sideWidth * .5, postY, sideWidth, EntranceBack - PostFront);
            yield return new BaseObjectRect(portal.X + DoorHalfWidth + sideWidth * .5, postY, sideWidth, EntranceBack - PostFront);
            yield return new BaseObjectRect(portal.X, portal.Y + (DoorSill + EntranceBack) * .5, DoorHalfWidth * 2, EntranceBack - DoorSill);
        }

        private static BasePoint EntranceApproach(BaseState s, BaseMapObject portal, string map = null) =>
            BaseSpatial.Nearest(s, new BasePoint(portal.X, portal.Y + DoorSill - BaseMap.Radius - .05), map);

        internal static void RepairEntrancePositions(BaseState s)
        {
            if (!Active(s)) return;
            bool instanced = BaseSceneInstances.Active(s);
            string map = instanced ? "demo_ruins" : "outpost";
            var layout = s.Campaign.Layouts?.FirstOrDefault(m => m != null && (instanced ? m.SourceMapId : m.Region) == map);
            var portal = layout?.Objects?.FirstOrDefault(p => p?.Id == EntranceId && p.AssetId == "dungeon-entrance")?.Object;
            if (portal == null) return;
            bool current = Entrance(s);
            // 旧档可以站在原本无碰撞的门洞内。只修复新碰撞影响到的坐标。
            if (current && BlocksEntrance(portal, s.PlayerX, s.PlayerY))
            {
                var point = EntranceApproach(s, portal, map);
                s.PlayerX = point.X;
                s.PlayerY = point.Y;
            }
            if(current && s.Demo.Rescue is {Stage:>0} rescue && rescue.EntranceLayoutVersion<EntrancePeopleLayout)
                PlaceEntrancePeople(s,portal,rescue);
            foreach (var leader in s.Leaders)
            {
                if (leader?.HasMapPosition != true || !BlocksEntrance(portal, leader.MapX, leader.MapY)) continue;
                if (BaseAdventure.MapKey(s, leader) != map &&
                    !(current && string.IsNullOrEmpty(leader.MapId) && BaseAdventure.Region(leader) == layout.Region)) continue;
                var point = BaseSpatial.Nearest(s, new BasePoint(leader.MapX, leader.MapY), map);
                leader.MapX = point.X;
                leader.MapY = point.Y;
            }
        }
        private static void PlaceEntrancePeople(BaseState state,BaseMapObject portal,BaseRescueJourney rescue)
        {
            var landing=EntranceApproach(state,portal);
            var board=BaseBattlefield.Board(state);
            var objects=BaseSpatial.Objects(state);
            double level=board.SurfaceLevel(landing.X,landing.Y);
            bool Ground(double x,double y)=>Math.Abs(board.SurfaceLevel(x,y)-level)<.001 &&
                BaseSpatial.Geometry(objects,x,y,board.Width,board.Height)&&BaseSpatial.TerrainWalkable(board,x,y,objects);
            var available=new List<BasePoint>();
            for(int dx=-4;dx<=6;dx++)
                for(int dy=-6;dy<=2;dy++)
                {
                    var p=new BasePoint(landing.X+dx*.5,landing.Y+dy*.5);
                    if(p.Distance(landing)>=.7&&BaseMap.ClearSegment(landing,p,null,Ground,false,board.Width,board.Height))available.Add(p);
                }
            BasePoint Place(double dx,double dy)
            {
                if(available.Count==0)return landing;
                var desired=new BasePoint(landing.X+dx,landing.Y+dy);
                var post=available.OrderBy(p=>p.Distance(desired)).First();
                available.RemoveAll(p=>p.Distance(post)<.75);return post;
            }
            var first=Place(-1.5,-.5);var second=Place(-.5,-1.5);var galan=Place(2.5,-2.5);
            if(rescue.Stage==1)
            {
                rescue.FirstX=first.X;rescue.FirstY=first.Y;rescue.SecondX=second.X;rescue.SecondY=second.Y;
            }
            rescue.GalanX=galan.X;rescue.GalanY=galan.Y;rescue.EntranceLayoutVersion=EntrancePeopleLayout;
        }
        public static bool Neutral(BaseState s)=>Neutral(s, s.Campaign.Location);
        // 按指定区域判中立：副手在营地主持防守时，不能拿玩家所在城镇的“禁止士兵”清空战场编制。
        public static bool Neutral(BaseState s,string region)=>BaseSpatial.Layout(s,region)?.ForbidSoldiers==true;
        public static BaseTerrainMemory Memory(BaseState s)
        {
            string key=BaseDemoPrologue.Village(s)?"demo_prologue":null;
            return Memory(s,key??s.Campaign.Location,null,key!=null);
        }
        // 按指定战场写记忆：副手战在别处打，已知地形不能记到玩家当前站着的地图上。
        public static BaseTerrainMemory Memory(BaseState s,string mapKey,BattleBoard board=null,bool literalKey=false)
        {
            string mapId=literalKey?mapKey:BaseSpatial.Layout(s,mapKey)?.SourceMapId;
            string key=string.IsNullOrEmpty(mapId)?mapKey:mapId;
            // 序章的记忆键与营地分开，尺寸取当前村庄，不创建同名区域。
            var b=board??BaseBattlefield.Board(s,literalKey?null:mapKey);
            var memories=s.Demo.TerrainMemories??(s.Demo.TerrainMemories=new List<BaseTerrainMemory>());
            var m=memories.FirstOrDefault(v=>v.MapId==key);
            // 同一张图尺寸变了（换版本或旧记录）就重建，避免 Known 长度与宽高对不上。
            if(m==null||m.Width!=b.Width||m.Height!=b.Height){if(m!=null)memories.Remove(m);m=new BaseTerrainMemory{MapId=key,Width=b.Width,Height=b.Height,Known=new bool[b.Width*b.Height]};memories.Add(m);}return m;
        }
        public static bool FogEnabled(BaseState s)
        {
            if(BaseWorldJourney.OnWorld(s)||s.Campaign.Location=="perimeter")return false;
            if(BaseDemoPrologue.Evacuating(s)||BaseDemoPrologue.Pursuit(s))return true;
            var layout=BaseSpatial.Layout(s);
            if(layout?.Safety=="safe")return false;if(layout?.Safety=="dangerous")return true;
            return !Neutral(s)&&layout?.Region!="base"&&layout?.Region!="town"&&layout?.SourceMapId!=BaseDemoFinale.Home;
        }
        public static void Install(BaseState s)
        {
            if(!Active(s))return;
            foreach(var m in s.Campaign.Layouts.Where(m=>m.Region=="base"||m.Region=="town"||m.SourceMapId==BaseDemoFinale.Home))m.ForbidSoldiers=true;
            s.Demo.Finale.Village.ForbidSoldiers=true;
            s.Demo.Rescue??=new BaseRescueJourney();
            if(s.Campaign.Layouts.Any(m=>m.SourceMapId==DungeonId))return;
            var layout=new BaseMapLayout {Region="dungeon",SourceMapId=DungeonId,Title="遗迹地下回廊",Columns=32,Rows=24,SpawnX=4.5,SpawnY=7.5,SurfaceStyle="dungeon",Objects=new List<BaseMapPlacement> {
                new BaseMapPlacement {Id="demo_ruins_investigate",Name="遗迹深处",X=4.5,Y=7.5,Width=.5,Height=.5},
                new BaseMapPlacement {Id="demo_dungeon_exit",Name="返回遗迹入口",X=1.5,Y=7.5,Width=1,Height=2}
            },TacticalObjects=new List<BattleTacticalObject>{new BattleTacticalObject{Id="dungeon_rock",Kind="rock",X=18,Y=16}}};
            var b=BaseDemoRelease.Board();
            foreach(var p in b.Positions())
            {
                var c=b.Cell(p);if(p.X==0||p.Z==0||p.X==31||p.Z==23){c.Terrain=(p.X==0&&p.Z==23||p.X==31&&p.Z==0)?TerrainKind.Ground:TerrainKind.Wall;c.Zone=DeploymentZone.None;}
            }
            for(int z=9;z<=15;z++)for(int x=24;x<=30;x++)b.Cell(new GridPosition(x,z)).Elevation=1;
            for(int z=10;z<=14;z++)for(int x=25;x<=29;x++)b.Cell(new GridPosition(x,z)).Elevation=2;
            b.Cell(new GridPosition(24,12)).Ramp=RampAxis.EastWest;b.Cell(new GridPosition(25,12)).Ramp=RampAxis.EastWest;
            int pillar=0;foreach(var p in new[]{new GridPosition(10,4),new GridPosition(10,20),new GridPosition(22,4),new GridPosition(22,20)})layout.Objects.Add(new BaseMapPlacement{Id="dungeon_pillar_"+pillar++,Name="石柱",X=p.X+.5,Y=p.Z+.5,AssetId="stone-pillar",Kind="pixel-prop",SourceObjectId="dungeon:pillar:"+pillar,VisualOnly=true});
            s.Campaign.Layouts.Add(layout);s.Campaign.RegionMaps.RemoveAll(m=>m.Id=="dungeon");s.Campaign.RegionMaps.Add(new BaseRegionMap{Id="dungeon",Board=b});
            var entrance=s.Campaign.Layouts.Single(m=>m.Region=="outpost");entrance.Title="野外 · 遗迹入口";
            var portal=entrance.Objects.Single(m=>m.Id=="demo_ruins_investigate");portal.Name="遗迹入口 · 进入地下回廊";portal.AssetId="dungeon-entrance";portal.SourceObjectId="demo:portal:dungeon";portal.Kind="demo-prop";
        }
        public static void EnterDungeon(BaseState s)
        {
            if(!Entrance(s)||!BaseInteraction.CanReach(s,BaseSpatial.Objects(s).Single(o=>o.Id=="demo_ruins_investigate")))throw new InvalidOperationException("请靠近遗迹入口");
            s.Campaign.Location=BaseSceneInstances.Active(s)?"custom":"dungeon";if(BaseSceneInstances.Active(s))s.Campaign.CurrentMapId=DungeonId;
            var map=BaseSpatial.Layout(s);s.PlayerX=map.SpawnX;s.PlayerY=map.SpawnY;BaseAdventure.Arrive(s);
        }
        public static void LeaveDungeon(BaseState s)
        {
            if(!Dungeon(s))return;
            s.Campaign.Location=BaseSceneInstances.Active(s)?"custom":"outpost";if(BaseSceneInstances.Active(s))s.Campaign.CurrentMapId="demo_ruins";
            var portal=BaseSpatial.Objects(s).FirstOrDefault(o=>o.Id==EntranceId);
            var landing=portal==null ? BaseSpatial.Nearest(s,new BasePoint(11,7)) : EntranceApproach(s,portal);
            s.PlayerX=landing.X;s.PlayerY=landing.Y;BaseAdventure.Arrive(s);
        }
        public static bool TalkRescued(BaseState s,bool afterExtraction=false)
        {if(!Entrance(s)||s.Demo.Rescue?.Stage!=1||!afterExtraction&&!new[]{"demo_rescued_1","demo_rescued_2"}.Any(id=>BaseInteraction.At(s,s.Campaign.Location,id)))return false;s.Demo.Rescue.Stage=2;return true;}
        public static bool TickRescued(BaseState s,double seconds)
        {
            if(!Active(s)||s.Demo.Rescue?.Stage!=2)return false;var r=s.Demo.Rescue;
            void Move(ref double x,ref double y,double dy)
            {double dx=.7-x,dz=7.5+dy-y,d=Math.Sqrt(dx*dx+dz*dz),step=Math.Min(d,seconds*2.2);if(d>1e-8){x+=dx/d*step;y+=dz/d*step;}}
            Move(ref r.FirstX,ref r.FirstY,0);Move(ref r.SecondX,ref r.SecondY,.5);
            if(r.FirstX>.72||r.SecondX>.72)return false;r.Stage=3;return true;
        }
        public static void Validate(BaseState s)
        {
            if(s.Leaders.Any(l=>l!=null&&!Enum.IsDefined(typeof(SquadStrategy),l.PreferredStrategy)))throw new ArgumentException("队长策略非法");
            if(!Active(s))return;
            var memories=s.Demo.TerrainMemories??new List<BaseTerrainMemory>();
            if(memories.Count>32||memories.Any(m=>m==null||string.IsNullOrEmpty(m.MapId)||m.Width<6||m.Width>64||m.Height<6||m.Height>64||m.Known?.Length!=m.Width*m.Height)||memories.Select(m=>m.MapId).Distinct().Count()!=memories.Count)throw new ArgumentException("已知地形记录非法");
            var r=s.Demo.Rescue;
            if(r==null||r.Stage<0||r.Stage>3||new[]{r.FirstX,r.SecondX}.Any(v=>double.IsNaN(v)||double.IsInfinity(v)||v<0||v>=32)||new[]{r.FirstY,r.SecondY}.Any(v=>double.IsNaN(v)||double.IsInfinity(v)||v<0||v>=24))throw new ArgumentException("获救村民返城状态非法");
        }
        // P36：逃跑已退出三档 UI。旧档里读到逃跑就按「驻守」处理（最接近的"不追击"行为）；
        // 枚举值保留不删，避免改动序列化位、破坏旧存档。
        public static SquadStrategy Tier(SquadStrategy value) => value == SquadStrategy.Flee ? SquadStrategy.Protect : value;
        public static void Configure(BaseState s,BattleScenario scenario,IReadOnlyList<BaseLeader> leaders,string region=null)
        {
            if(Neutral(s,region??s.Campaign.Location)){scenario.A.MeleeCount=scenario.A.RangedCount=scenario.A.CavalryCount=0;foreach(var c in scenario.A.Commanders)c.Melee=c.Ranged=c.Cavalry=0;}
            if(!Active(s))return;
            bool atHomeField=(region??s.Campaign.Location)=="perimeter";
            scenario.HeightVision=true;
            scenario.FogEnabled=FogEnabled(s);
            scenario.DefenderVisionEnabled=false;
            if(atHomeField)BaseDemoDefense.ApplyHomeSight(scenario);
            scenario.VisionRadius=BattleHeightSight.DefaultRadius;
            while(scenario.A.Commanders.Count<scenario.A.HeroCount)scenario.A.Commanders.Add(new CommanderLoadout());
            BaseMuster.Validate(s);
            for(int n=0;n<scenario.A.Commanders.Count&&n<leaders.Count;n++)
            {
                var c=scenario.A.Commanders[n];c.Strategy=Tier((SquadStrategy)s.Muster.Strategies[n]);
                c.GuardRange=s.Muster.Ranges[n*3];c.AlertRange=s.Muster.Ranges[n*3+1];c.AttackRange=s.Muster.Ranges[n*3+2];
            }
        }
        public static void RememberStrategy(BaseState s,BaseCampaignSession session)
        {
            if(!Active(s))return;
            BaseMuster.Validate(s);
            foreach(var actor in session.Battle.Soldiers.Where(u=>u.Side==BattleSide.A&&u.Kind==SoldierKind.Hero))
                if(actor.OriginCommanderSlot>=0&&actor.OriginCommanderSlot<3)
                    s.Muster.Strategies[actor.OriginCommanderSlot]=(int)Tier(session.Battle.Scenario.A.Commanders[actor.OriginCommanderSlot].Strategy);
        }
    }
}

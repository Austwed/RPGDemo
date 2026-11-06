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
        public static bool Active(BaseState s)=>s?.Demo?.Rpg?.SceneRulesVersion==1;
        public static bool Dungeon(BaseState s)=>Active(s)&&(BaseSceneInstances.Active(s)?s.Campaign.CurrentMapId==DungeonId:s.Campaign.Location=="dungeon");
        public static bool Entrance(BaseState s)=>Active(s)&&(BaseSceneInstances.Active(s)?s.Campaign.CurrentMapId=="demo_ruins":s.Campaign.Location=="outpost");
        public static bool Neutral(BaseState s)=>BaseSpatial.Layout(s)?.ForbidSoldiers==true;
        public static BaseTerrainMemory Memory(BaseState s)
        {
            string mapId=BaseSpatial.Layout(s)?.SourceMapId;
            string key=BaseDemoPrologue.Village(s)?"demo_prologue":string.IsNullOrEmpty(mapId)?s.Campaign.Location:mapId;var b=BaseBattlefield.Board(s);
            var memories=s.Demo.TerrainMemories??(s.Demo.TerrainMemories=new List<BaseTerrainMemory>());var m=memories.SingleOrDefault(v=>v.MapId==key);
            if(m==null){m=new BaseTerrainMemory{MapId=key,Width=b.Width,Height=b.Height,Known=new bool[b.Width*b.Height]};memories.Add(m);}return m;
        }
        public static bool FogEnabled(BaseState s)
        {
            if(BaseWorldJourney.OnWorld(s))return false;
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
            s.PlayerX=11;s.PlayerY=7;BaseAdventure.Arrive(s);
        }
        public static bool TalkRescued(BaseState s)
        {if(!Entrance(s)||s.Demo.Rescue?.Stage!=1||!new[]{"demo_rescued_1","demo_rescued_2"}.Any(id=>BaseInteraction.At(s,s.Campaign.Location,id)))return false;s.Demo.Rescue.Stage=2;return true;}
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
        public static void Configure(BaseState s,BattleScenario scenario,IReadOnlyList<BaseLeader> leaders)
        {
            if(Neutral(s)){scenario.A.MeleeCount=scenario.A.RangedCount=scenario.A.CavalryCount=0;foreach(var c in scenario.A.Commanders)c.Melee=c.Ranged=c.Cavalry=0;}
            if(!Active(s))return;scenario.HeightVision=true;scenario.FogEnabled=FogEnabled(s);scenario.DefenderVisionEnabled=false;scenario.VisionRadius=10;
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

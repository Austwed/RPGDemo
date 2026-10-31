using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using BaseConquest.Battle;

namespace BaseConquest.Base
{
    [Serializable] public sealed class BaseDesignLink
    {
        public string SourceMapId,SourceObjectId,TargetMapId,Mode;
        public string TargetObjectId;
        public double X,Y;
        public BaseDesignLink Copy()=>(BaseDesignLink)MemberwiseClone();
    }
    [Serializable] public sealed class BaseDesignPackage
    {
        public string Format,Id;
        public int Version,BaseVersion;
        public BaseScenarioDocument Scenario;
        public string[] Dependencies,Warnings;
        public BaseState CreateState()
        {
            if(Format=="slg-play-package"&&(Version==7||Version==8||Version==9||Version==10||Version==11&&BaseDemo.Active(Scenario?.Opening)))
            {
                if((Version>=9)!=BaseDemoRelease.Enabled(Scenario?.Opening)||(Version>=10)!=BaseRpg.Active(Scenario?.Opening)||!Regex.IsMatch(Id??"","^[a-f0-9]{64}$")||Scenario?.Opening?.Demo?.Encounters?.Version!=(Version==7?1:2)||!BaseDemo.Active(Scenario.Opening)||!string.IsNullOrEmpty(Scenario.Opening.Campaign.DesignId)||Scenario.Opening.Campaign.HasActiveBattle||Scenario.Opening.Demo.Stage!=BaseDemoStage.PrologueBriefing||Scenario.Opening.Campaign.Reports.Count!=0||Scenario.Opening.Demo.AcceptedDay!=0||Dependencies==null||!Dependencies.OrderBy(value=>value).SequenceEqual(new[]{BaseDemo.BalanceTemplate,"23_据点迎战.json","24_据点进攻.json"}.OrderBy(value=>value)))throw new ArgumentException("Demo编排包须为带序章的完整新开局，不接受旧进度");
                if(Version==11&&!BaseEnvironment.Active(Scenario.Opening))throw new ArgumentException("格式11缺环境快照");
                BaseRpg.ValidatePreset(Scenario.Opening.Demo.Encounters.Ruins,Scenario.Opening.Demo.Rpg,true);
                BaseRpg.ValidatePreset(Scenario.Opening.Demo.Encounters.Defense,Scenario.Opening.Demo.Rpg);
                BaseRpg.ValidatePreset(Scenario.Opening.Demo.Encounters.Camp,Scenario.Opening.Demo.Rpg);
                var demo=Scenario.CreateState();
                demo.Demo.Encounters.Ruins.ValidateBoard(BaseSceneCombat.Project(demo,"outpost"));
                demo.Demo.Encounters.Defense.ValidateBoard(BaseBattlefield.Board(demo,"perimeter"));
                if(Version>=8)demo.Demo.Encounters.Camp.ValidateBoard(BaseDemoFinale.CreateCampBoard(demo));
                return demo;
            }
            int formatVersion=Version==11?BaseVersion:Version;
            bool shaped=Scenario?.Opening?.Campaign?.Layouts?.Any(map=>map?.Objects?.Any(obj=>!string.IsNullOrEmpty(obj?.Shape))==true)==true;
            bool world=Scenario?.Opening?.Campaign?.WorldJourney?.Version==1;
            bool configured=Scenario?.Opening?.Campaign?.Layouts?.Any(map=>(map?.Enemy?.Version??0)!=0)==true;
            bool positioned=Scenario?.Opening?.Campaign?.Layouts?.Any(map=>map?.Enemy?.Version==2)==true;
            if(Format!="slg-play-package"||formatVersion<1||formatVersion>6||!Regex.IsMatch(Id??"","^[a-f0-9]{64}$")||Scenario?.Opening?.Campaign?.DesignId!=Id||(formatVersion>=2)!=(Scenario.Opening.Campaign.InstanceVersion==1)||(formatVersion==3&&!world)||(formatVersion<3&&world)||(formatVersion==4&&!shaped)||(formatVersion<4&&shaped)||(formatVersion>=5)!=configured||(formatVersion==6)!=positioned)throw new ArgumentException("不支持的运行组合格式、版本或身份");
            if(Scenario.Opening.Campaign.Location!="base"||Scenario.Opening.Campaign.HasActiveBattle)throw new ArgumentException("运行组合必须从基地新开局，不接受游玩存档");
            if(Dependencies==null||!Dependencies.OrderBy(value=>value).SequenceEqual(new[]{"26_小队背包与仓库.json","23_据点迎战.json","24_据点进攻.json"}.OrderBy(value=>value)))throw new ArgumentException("运行组合依赖声明不完整或不受支持");
            foreach(var map in Scenario.Opening.Campaign.Layouts??new List<BaseMapLayout>())
            {
                foreach(var marker in map.Objects??new List<BaseMapPlacement>())
                {
                    string allowed=map.Region=="base" ? "home government farm workshop barracks watchtower partner_farm partner_craft exit_west exit_east exit_south exit_north" : map.Region=="town" ? "town_market town_recruit town_quest travel_base" : map.Region=="perimeter" ? "fixed_fieldworks travel_base campaign_defense" : map.Region=="world" ? "travel_base campaign_attack" : "travel_world campaign_attack";
                    bool generic=marker.DefinitionId=="map_exit"&&marker.Id==marker.SourceObjectId;
                    if(!Regex.IsMatch(marker.SourceObjectId??"","^[a-zA-Z0-9_-]{1,80}$")||!new[]{"facility","npc","interaction","obstacle","decoration","wildlife"}.Contains(marker.Kind)||!marker.VisualOnly&&!generic&&!allowed.Split(' ').Contains(marker.Id)||marker.Id?.StartsWith("planned.")==true&&!marker.VisualOnly)throw new ArgumentException(map.Title+"：未支持的对象声明 "+marker.Name);
                    if(marker.VisualOnly&&marker.Id!=marker.SourceObjectId&&marker.Id?.StartsWith("planned.")!=true&&marker.Id!="exit_north")throw new ArgumentException(map.Title+"：外观不得借用现有功能标识 "+marker.Id);
                }
            }
            var state=Scenario.CreateState();BaseDesignRoutes.Validate(state,true);
            foreach(var map in state.Campaign.Layouts)map.Enemy?.ValidateBoard(BaseBattlefield.Board(state,BaseSceneInstances.Active(state)?map.SourceMapId:map.Region));
            return state;
        }
    }
    public static class BaseDesignRoutes
    {
        public static bool Active(BaseState state)=>!string.IsNullOrEmpty(state.Campaign.DesignId);
        public static bool CanBegin(BaseState state,string kind)=>!Active(state)||state.Campaign.Location==(kind=="defense"?"perimeter":"outpost");
        public static BaseMapLayout Target(BaseState state,BaseDesignLink link)=>state.Campaign.Layouts.Single(map=>map.SourceMapId==link.TargetMapId);
        public static BaseDesignLink ForObject(BaseState state,string objectId)
        {
            if(!Active(state))return null;
            var source=BaseSpatial.Layout(state);var marker=source?.Objects.FirstOrDefault(obj=>obj.Id==objectId);
            return marker==null ? null : state.Campaign.DesignLinks.FirstOrDefault(link=>link.SourceMapId==source.SourceMapId&&link.SourceObjectId==marker.SourceObjectId);
        }
        public static BaseDesignLink Reachable(BaseState state,string region,string objectId=null)
        {
            var source=BaseSpatial.Layout(state);
            if(source==null)return null;
            foreach(var marker in source.Objects.Where(marker=>objectId==null||marker.Id==objectId))
            {
                var link=ForObject(state,marker.Id);
                if(link!=null&&Target(state,link).Region==region&&BaseInteraction.CanReach(state,marker.Object)&&BaseSpatial.Walkable(state,link.X,link.Y,BaseSceneInstances.Active(state)?link.TargetMapId:region))return link;
            }
            return null;
        }
        public static void Validate(BaseState state,bool paths=false)
        {
            if(!Active(state))return;
            var campaign=state.Campaign;var maps=campaign.Layouts;var links=campaign.DesignLinks;
            bool instances=BaseSceneInstances.Active(state);int expected=instances?maps.Count:5;
            BaseWorldJourney.Validate(state);
            if(!Regex.IsMatch(campaign.DesignId,"^[a-f0-9]{64}$")||maps.Count!=expected||maps.Any(map=>string.IsNullOrWhiteSpace(map.SourceMapId))||maps.Select(map=>map.SourceMapId).Distinct().Count()!=expected||links==null||links.Count>1500)throw new ArgumentException("设计组合身份、区域或连接集合非法");
            if(campaign.HasActiveBattle&&campaign.ActiveBattle!=null&&!new[]{"perimeter","outpost"}.Contains(campaign.ActiveBattle.MapRegion)&&!(BaseDemoRuins.Enabled(state)&&campaign.ActiveBattle.EventId==BaseDemoRuins.EventId&&campaign.ActiveBattle.MapRegion=="custom"&&(campaign.ActiveBattle.MapId=="demo_ruins"||BaseSceneRules.Active(state)&&campaign.ActiveBattle.MapId==BaseSceneRules.DungeonId)))throw new ArgumentException("设计战斗缺少实际战场归属");
            foreach(var map in maps)if(BaseBattlefield.Board(state,instances?map.SourceMapId:map.Region).Width!=(BaseDemoRelease.Enabled(state)?map.Columns:24)||BaseBattlefield.Board(state,instances?map.SourceMapId:map.Region).Height!=(BaseDemoRelease.Enabled(state)?map.Rows:14))throw new ArgumentException("设计组合只支持24×14地图");
            foreach(var map in maps)BaseObjectFootprint.ValidateBoard(map,BaseBattlefield.Board(state,instances?map.SourceMapId:map.Region));
            if(links.Select(link=>link==null ? "" : link.SourceMapId+":"+link.SourceObjectId).Distinct().Count()!=links.Count)throw new ArgumentException("同一出入口重复绑定");
            foreach(var link in links)
            {
                var source=maps.SingleOrDefault(map=>map.SourceMapId==link?.SourceMapId);var target=maps.SingleOrDefault(map=>map.SourceMapId==link?.TargetMapId);
                var marker=source?.Objects.SingleOrDefault(obj=>obj.SourceObjectId==link.SourceObjectId);
                if(link==null||source==null||target==null||link.Mode!="direct"||source==target||marker==null||marker.VisualOnly||marker.Blocks||!IsExit(source.Region,marker)||double.IsNaN(link.X)||double.IsInfinity(link.X)||double.IsNaN(link.Y)||double.IsInfinity(link.Y)||!BaseSpatial.Walkable(state,link.X,link.Y,instances?target.SourceMapId:target.Region))throw new ArgumentException("出入口引用、通行方式或落点非法："+link?.SourceObjectId);
                if(instances?!BaseSceneInstances.CanLink(source,target)&&!BaseWorldJourney.LocalLink(state,source,target):!new[]{"base:town","town:base","base:perimeter","perimeter:base","base:world","world:base","world:outpost","outpost:world"}.Contains(source.Region+":"+target.Region))throw new ArgumentException("当前编制不支持该区域连接："+source.Region+"→"+target.Region);
                if(!string.IsNullOrEmpty(link.TargetObjectId))
                {
                    var entrance=target.Objects.SingleOrDefault(obj=>obj.SourceObjectId==link.TargetObjectId);
                    if(entrance==null||!IsExit(target.Region,entrance)||!BaseInteraction.CanReach(BaseBattlefield.Board(state,instances?target.SourceMapId:target.Region),entrance.Object,link.X,link.Y))throw new ArgumentException("目标出口身份或同层安全落点非法："+link.TargetObjectId);
                }
            }
            foreach(var map in maps)
            {
                if(!BaseSpatial.Walkable(state,map.SpawnX,map.SpawnY,instances?map.SourceMapId:map.Region))throw new ArgumentException(map.Title+"：出生点不可站立");
                foreach(var marker in map.Objects.Where(obj=>IsExit(map.Region,obj)&&!obj.VisualOnly))if(!links.Any(link=>link.SourceMapId==map.SourceMapId&&link.SourceObjectId==marker.SourceObjectId)&&!(BaseWorldJourney.Active(state)&&campaign.WorldJourney.Entries.Any(entry=>entry.MapId==map.SourceMapId&&entry.ObjectId==marker.SourceObjectId)))throw new ArgumentException(map.Title+"：出口未绑定 "+marker.Name);
            }
            if(!paths)return;
            var baseMap=maps.Single(map=>map.Region=="base");
            foreach(bool reverse in new[]{false,true})
            {
                var reached=new HashSet<string>{baseMap.SourceMapId};int previous=-1;
                while(previous!=reached.Count){previous=reached.Count;foreach(var link in links)if(reached.Contains(reverse?link.TargetMapId:link.SourceMapId))reached.Add(reverse?link.SourceMapId:link.TargetMapId);if(BaseWorldJourney.Active(state)){string world=maps.Single(map=>map.Region=="world").SourceMapId;foreach(var entry in campaign.WorldJourney.Entries){if(reached.Contains(world))reached.Add(entry.MapId);if(reached.Contains(entry.MapId))reached.Add(world);}}}
                if(reached.Count!=maps.Count)throw new ArgumentException(reverse?"有地图不能返回基地":"有地图无法从基地前往");
            }
            foreach(var map in maps)
            {
                string key=instances?map.SourceMapId:map.Region;var board=BaseBattlefield.Board(state,key);var points=Flood(state,key,map.SpawnX,map.SpawnY);
                foreach(var marker in map.Objects.Where(obj=>IsExit(map.Region,obj)&&!obj.VisualOnly))if(!points.Any(point=>BaseInteraction.CanReach(board,marker.Object,point.X,point.Y)))throw new ArgumentException(map.Title+"：出生区到出口不通 "+marker.Name);
                foreach(var link in links.Where(link=>link.TargetMapId==map.SourceMapId))if(!Flood(state,key,link.X,link.Y).Any(point=>Math.Abs(point.X-map.SpawnX)+Math.Abs(point.Y-map.SpawnY)<=.4))throw new ArgumentException(map.Title+"：连接落点与出生区不通");
                if(map.Region=="outpost"||map.Region=="perimeter")foreach(var side in new[]{DeploymentZone.A,DeploymentZone.B})if(!board.Positions().Any(point=>board.Cell(point).Zone==side&&board.Walkable(point)&&BaseSpatial.Walkable(state,point.X+.5,point.Z+.5,key)))throw new ArgumentException(map.Title+"：双方部署区不完整");
            }
        }
        private static bool IsExit(string region,BaseMapPlacement marker)=>marker.DefinitionId=="map_exit"||new[]{"exit_west","exit_east","exit_south","travel_base","travel_world"}.Contains(marker.Id)||region=="world"&&marker.Id=="campaign_attack";
        private static List<BasePoint> Flood(BaseState state,string region,double x,double y)
        {
            var objects=BaseSpatial.Objects(state,region);var board=BaseBattlefield.Board(state,region);
            BasePoint Point(GridPosition point)=>new BasePoint((point.X+.5)/4.0,(point.Z+.5)/4.0);
            bool Walkable(double pointX,double pointY)=>BaseSpatial.Geometry(objects,pointX,pointY,board.Width,board.Height)&&BaseSpatial.TerrainWalkable(board,pointX,pointY,objects);
            bool Connected(GridPosition point){var target=Point(point);return Walkable(target.X,target.Y)&&BaseMap.ClearSegment(new BasePoint(x,y),target,null,Walkable,false,board.Width,board.Height);}
            var start=new GridPosition((int)Math.Ceiling(x*4-1),(int)Math.Ceiling(y*4-1));var open=new List<GridPosition>();var seen=new HashSet<GridPosition>();
            if(!Connected(start))
            {
                var candidates=Enumerable.Range(0,board.Width*4*board.Height*4).Select(index=>new GridPosition(index%(board.Width*4),index/(board.Width*4))).OrderBy(point=>Point(point).Distance(new BasePoint(x,y))).Where(Connected).Take(1).ToArray();
                if(candidates.Length==0)return new List<BasePoint>();start=candidates[0];
            }
            open.Add(start);seen.Add(start);
            for(int index=0;index<open.Count;index++)foreach(var offset in new[]{new GridPosition(1,0),new GridPosition(-1,0),new GridPosition(0,1),new GridPosition(0,-1)})
            {
                var next=open[index]+offset;
                var target=Point(next);
                if(!seen.Contains(next)&&Walkable(target.X,target.Y)&&BaseMap.ClearSegment(Point(open[index]),target,null,Walkable,false,board.Width,board.Height)){seen.Add(next);open.Add(next);}
            }
            return open.Select(Point).ToList();
        }
    }
}

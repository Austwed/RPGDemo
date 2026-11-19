using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using BaseConquest.Battle;

namespace BaseConquest.Base
{
    // 剧情场景包只描述地图，不携带剧情、数值或敌军编排。
    [Serializable] public sealed class BaseStoryScenePackage
    {
        private sealed class Rule
        {
            public readonly string Role,Id,Kind;public readonly bool Required;
            public Rule(string role,string id,string kind,bool required){Role=role;Id=id;Kind=kind;Required=required;}
            public string Definition=>"story_"+Role+"_"+Id.Replace(':','_').Replace('-','_');
        }
        private static readonly Rule[] Rules={
            new Rule("village","village_house","facility",true),new Rule("village","intro_farm","facility",true),new Rule("village","intro_smith","facility",false),
            new Rule("village","demo_captain","npc",true),new Rule("village","intro_villager","npc",true),new Rule("village","prologue_report","interaction",true),new Rule("village","prologue_trigger","interaction",true),new Rule("village","demo_home_world","interaction",true),new Rule("village","demo_broken_sword","interaction",true),new Rule("village","demo_survivor","npc",false),
            new Rule("village",BaseDemoIntro.DummyId,"interaction",false),
            new Rule("camp","home","facility",true),new Rule("camp","farm","facility",true),new Rule("camp","workshop","facility",true),new Rule("camp","barracks","facility",true),new Rule("camp","demo_rescuer","npc",false),new Rule("camp","demo_chunshen","npc",false),new Rule("camp","demo_ahe","npc",false),new Rule("camp","exit_east","interaction",true),new Rule("camp","demo_defense_exit","interaction",true),new Rule("camp","demo_march_exit","interaction",true),
            new Rule("town","town_quest","npc",true),new Rule("town","town_recruit","facility",true),new Rule("town","town_smith","facility",false),new Rule("town","town_market","facility",false),new Rule("town","town_blacksmith","npc",false),new Rule("town","town_guard","npc",false),new Rule("town","town_peddler","npc",false),new Rule("town","town_girl","npc",false),new Rule("town","demo_azhu","npc",false),new Rule("town","travel_base","interaction",true),new Rule("town","demo_ruins_exit","interaction",true),
            new Rule("ruins","demo_ruins_return","interaction",true),new Rule("ruins","demo_ruins_investigate","interaction",true),new Rule("ruins","demo_bandit_hideout","interaction",false),
            new Rule("dungeon","demo_dungeon_exit","interaction",true),new Rule("dungeon","demo_ruins_investigate","interaction",true),
            new Rule("field","demo_defense_return","interaction",true),new Rule("field","demo_defense_start","interaction",true),new Rule("bandit","demo_camp_world","interaction",true),new Rule("bandit","demo_camp_attack","interaction",true)
        };
        public string Format,Id;
        public int Version;
        public List<BaseDemoDesignMap> Maps;
        private static readonly HashSet<string> RequiredModern=new HashSet<string> {
            "intro_smith",BaseDemoIntro.DummyId,"demo_survivor","demo_rescuer","demo_chunshen","demo_ahe",
            "town_smith","town_market","town_blacksmith","town_peddler","demo_azhu","demo_bandit_hideout"
        };
        // JsonUtility 把 null 的嵌套对象写成 {}，读回来是空壳。判「有没有设计」一律用 Valid，
        // 不能只看 != null，否则旧档往返一次就会把剧情场景当缺失／非法。
        public bool Valid=>Format=="rpg-story-scene-package"&&(Version==1||Version==2)&&Maps!=null&&Maps.Count>0;
        public BaseStoryScenePackage Copy()=>new BaseStoryScenePackage {Format=Format,Id=Id,Version=Version,Maps=Maps?.Select(map=>map?.Copy()).ToList()};

        public void InstallVillage(BaseState state)
        {
            Validate();
            if(state?.Demo?.StoryScene?.Valid!=true||state.Campaign==null||!BaseDemoPrologue.Village(state))throw new InvalidOperationException("剧情村庄只能安装到携带设计的序章状态");
            var authored=Maps.Single(map=>map.Role=="village");var layout=authored.Layout.Copy();var board=authored.Board.Clone();
            var captain=layout.Objects.Single(obj=>obj.Id=="demo_captain");
            // 两名剧情人物由序章状态管理；返乡对象只在返乡阶段出现。
            layout.Objects.RemoveAll(obj=>new[]{"demo_captain","intro_villager","demo_home_world","demo_broken_sword","demo_survivor"}.Contains(obj.Id));
            if(Version==2)
            {
                layout.ForbidSoldiers=true;
                layout.Objects.Single(obj=>obj.Id=="village_house").VisualOnly=true;
                layout.Objects.Single(obj=>obj.Id=="prologue_trigger").VisualOnly=true;
                layout.Objects.Single(obj=>obj.Id==BaseDemoIntro.DummyId).Kind="pixel-prop";
            }
            state.Campaign.Layouts.RemoveAll(map=>map.Region=="base");state.Campaign.Layouts.Add(layout);
            state.Campaign.RegionMaps.RemoveAll(map=>map.Id=="base");state.Campaign.RegionMaps.Add(new BaseRegionMap {Id="base",Board=board});
            state.PlayerX=state.Campaign.BaseX=layout.SpawnX;state.PlayerY=state.Campaign.BaseY=layout.SpawnY;
            if(state.Demo.Intro!=null){state.Demo.Intro.CaptainX=captain.X;state.Demo.Intro.CaptainY=captain.Y;}
        }

        // 醒来后的灰岩营地及其余四张剧情图：按设计整体替换内置布局。
        // 只在 InstallCampMap 之后调用，避免被内置布局或发布模式的固定快照覆盖回去。
        public void InstallCamp(BaseState state)
        {
            Validate();
            if(state?.Demo==null||state.Campaign==null)throw new InvalidOperationException("剧情场景包只能装进 Demo 状态");
            var byRole=Maps.ToDictionary(map=>map.Role);
            Swap(state,"base",byRole["camp"]);
            Swap(state,"town",byRole["town"]);
            if(state.Campaign.Layouts.Any(map=>map.Region=="outpost"))Swap(state,"outpost",byRole["ruins"]);
            if(state.Campaign.Layouts.Any(map=>map.Region=="perimeter"))Swap(state,"perimeter",byRole["field"]);
            if(state.Demo.Finale!=null)state.Demo.Finale.CampBoard=byRole["bandit"].Board.Clone();
            var camp=byRole["camp"].Layout;
            // 工坊／兵营的「待修复」外观按当前修复进度接管，否则会和 BaseDemo.Validate 的外观一致性检查打架。
            var campLayout=state.Campaign.Layouts.Single(map=>map.Region=="base");
            if(Version==2)
            {
                campLayout.Objects.Single(obj=>obj.Id=="demo_defense_exit").VisualOnly=!BaseDemoDefense.Due(state)&&!BaseDemoDefense.Won(state);
                campLayout.Objects.Single(obj=>obj.Id=="demo_march_exit").VisualOnly=!BaseDemoDefense.Won(state);
            }
            foreach(string id in new[]{"workshop","barracks"})
            {
                var placement=campLayout.Objects.FirstOrDefault(obj=>obj.Id==id);
                if(placement==null)continue;
                placement.VisualOnly=!BaseDemoDefense.Repaired(state,id);
                // 修复点由游戏自己补：设计只表达设施位置，不要求作者摆修复交互。
                string fixId="demo_fix_"+id;
                if(campLayout.Objects.Any(obj=>obj.Id==fixId))continue;
                campLayout.Objects.Add(new BaseMapPlacement {Id=fixId,Name=(id=="workshop"?"工坊":"兵营")+"修复 · 需建设许可",X=placement.X,Y=placement.Y-placement.Height/2-.4,Width=1.2,Height=.6,VisualOnly=true});
            }
            state.PlayerX=state.Campaign.BaseX=camp.SpawnX;state.PlayerY=state.Campaign.BaseY=camp.SpawnY;
        }

        public BaseDemoDesignMap Map(string role)=>Maps.Single(map=>map.Role==role);

        public void InstallDungeon(BaseState state)
        {
            if(Version!=2||!Valid)return;
            Swap(state,"dungeon",Map("dungeon"));
        }

        // 返乡村庄：设计里的白桦村按劫后版本复用（受损名与两个返乡交互点由剧情状态补）。
        public BaseMapLayout ReturnVillage()
        {
            Validate();
            var layout=Map("village").Layout.Copy();
            layout.Region="custom";layout.ParentMapId=null;layout.Title=BaseScriptNames.Village+" · 劫后余痕";
            if(Version==2)
            {
                layout.Objects.RemoveAll(obj=>!obj.Blocks&&!BaseEnvironment.Decorative(obj.Object)&&!IsReturnMarker(obj.Id));
                var exit=layout.Objects.Single(obj=>obj.Id=="demo_home_world");
                var board=Map("village").Board;var objects=layout.Objects.Select(obj=>obj.Object).ToArray();
                var landing=board.Positions().Select(p=>new BasePoint(p.X+.5,p.Z+.5))
                    .Where(p=>Walkable(board,objects,p)&&BaseInteraction.WithinReach(board,exit.Object,p.X,p.Y))
                    .OrderBy(p=>p.Distance(new BasePoint(exit.X,exit.Y))).First();
                layout.SpawnX=landing.X;layout.SpawnY=landing.Y;
            }
            else
            {
                layout.SpawnX=21;layout.SpawnY=7;
                layout.Objects.RemoveAll(obj=>!obj.Blocks&&!BaseEnvironment.Decorative(obj.Object));
            }
            foreach(var obj in layout.Objects.Where(obj=>!IsReturnMarker(obj.Id)))
            {
                obj.Name="受损的"+obj.Name;
                if(Version==2){obj.VisualOnly=true;if(obj.Id==BaseDemoIntro.DummyId)obj.Kind="pixel-prop";}
            }
            return layout;
        }
        private static bool IsReturnMarker(string id)=>id=="demo_home_world"||id=="demo_broken_sword"||id=="demo_survivor";

        private static void Swap(BaseState state,string region,BaseDemoDesignMap design)
        {
            var layout=design.Layout.Copy();layout.Region=region;
            if(region=="dungeon")layout.SourceMapId=BaseSceneRules.DungeonId;
            if(region=="outpost")
                foreach(var portal in layout.Objects.Where(obj=>obj.AssetId=="dungeon-entrance"))portal.Kind="demo-prop";
            state.Campaign.Layouts.RemoveAll(map=>map.Region==region);
            state.Campaign.Layouts.Add(layout);
            if(region=="perimeter")state.Campaign.PerimeterBoard=design.Board.Clone();
            else
            {
                state.Campaign.RegionMaps.RemoveAll(map=>map.Id==region);
                state.Campaign.RegionMaps.Add(new BaseRegionMap {Id=region,Board=design.Board.Clone()});
            }
        }

        public void Validate()
        {
            var roles=new Dictionary<string,string> { {"village","base"}, {"camp","base"}, {"town","town"}, {"ruins","outpost"}, {"field","perimeter"}, {"bandit","outpost"} };
            bool modern=Version==2;if(modern)roles.Add("dungeon","dungeon");
            if(!Valid||!Regex.IsMatch(Id??"","^[a-f0-9]{64}$")||Maps.Count!=roles.Count||Maps.Any(map=>map==null||!roles.ContainsKey(map.Role??""))||Maps.Select(map=>map.Role).Distinct().Count()!=roles.Count)
                throw new ArgumentException("剧情场景包格式、身份或场景清单非法");
            var mapIds=new HashSet<string>();var objectIds=new HashSet<string>();
            foreach(var entry in Maps)
            {
                var map=entry.Layout;var board=entry.Board;
                if(map==null||map.Region!=roles[entry.Role]||!Regex.IsMatch(map.SourceMapId??"","^[a-zA-Z0-9_-]{1,80}$")||!mapIds.Add(map.SourceMapId)||string.IsNullOrWhiteSpace(map.Title)||map.Title.Length>100||map.Columns<10||map.Columns>64||map.Rows<10||map.Rows>64||!Finite(map.SpawnX)||!Finite(map.SpawnY)||map.SpawnX<0||map.SpawnX>=map.Columns||map.SpawnY<0||map.SpawnY>=map.Rows)
                    throw new ArgumentException("剧情场景身份、尺寸或出生点非法："+entry.Role);
                if(board==null||board.Width!=map.Columns||board.Height!=map.Rows||board.Cells?.Length!=board.Width*board.Height||board.Cells.Any(cell=>cell==null||!cell.MetadataValid||!Enum.IsDefined(typeof(TerrainKind),cell.Terrain)||!Enum.IsDefined(typeof(DeploymentZone),cell.Zone)||!cell.Walkable&&cell.Zone!=DeploymentZone.None))
                    throw new ArgumentException("剧情场景地格非法："+entry.Role);
                if(map.Objects==null||map.Objects.Count>300||map.Objects.Any(obj=>obj==null||string.IsNullOrWhiteSpace(obj.Id)||obj.Id.Length>100||string.IsNullOrWhiteSpace(obj.Name)||obj.Name.Length>100||!Regex.IsMatch(obj.SourceObjectId??"",modern?"^[a-zA-Z0-9_:-]{1,100}$":"^[a-zA-Z0-9_-]{1,80}$")||!objectIds.Add(obj.SourceObjectId)||!KindAllowed(obj.Kind,modern)||!Finite(obj.X)||!Finite(obj.Y)||!Finite(obj.Width)||!Finite(obj.Height)||obj.Width<=0||obj.Height<=0||obj.X<0||obj.Y<0||obj.X>=map.Columns||obj.Y>=map.Rows||(!modern||obj.Blocks)&&(obj.X-obj.Object.BoundsWidth/2<0||obj.X+obj.Object.BoundsWidth/2>map.Columns||obj.Y-obj.Object.BoundsHeight/2<0||obj.Y+obj.Object.BoundsHeight/2>map.Rows))||map.Objects.Select(obj=>obj.Id).Distinct().Count()!=map.Objects.Count)
                    throw new ArgumentException("剧情场景对象身份、坐标或占地非法："+entry.Role);
                foreach(var rule in Rules.Where(rule=>rule.Role==entry.Role&&(rule.Required||modern&&RequiredModern.Contains(rule.Id))))
                    if(!map.Objects.Any(obj=>obj.Id==rule.Id&&obj.DefinitionId==rule.Definition&&!obj.VisualOnly))throw new ArgumentException("缺少剧情必需对象："+rule.Id);
                foreach(var obj in map.Objects)
                {
                    var rule=Rules.FirstOrDefault(item=>item.Role==entry.Role&&item.Definition==obj.DefinitionId);
                    if(rule!=null)
                    {
                        bool primary=obj.Id==rule.Id&&!obj.VisualOnly;
                        bool duplicate=rule.Kind=="npc"&&obj.Id=="story-shell:"+obj.SourceObjectId&&obj.VisualOnly&&map.Objects.Any(item=>item.Id==rule.Id&&item.DefinitionId==rule.Definition&&!item.VisualOnly);
                        if(obj.Kind!=rule.Kind||obj.Blocks!=(rule.Kind=="facility"||modern&&obj.Id==BaseDemoIntro.DummyId)||!primary&&!duplicate)throw new ArgumentException("剧情对象身份非法："+obj.Name);
                    }
                    else if(!(modern&&obj.VisualOnly&&obj.DefinitionId=="story_visual")&&(!obj.VisualOnly||obj.Id!=obj.SourceObjectId||!(obj.DefinitionId=="decor_obstacle"&&obj.Kind=="obstacle"||obj.DefinitionId=="decor_marker"&&obj.Kind=="obstacle"||obj.DefinitionId?.StartsWith("env_")==true&&(obj.Kind=="decoration"||obj.Kind=="wildlife")&&BaseEnvironment.Find(obj.AssetId)!=null)))throw new ArgumentException("对象不属于当前剧情场景："+obj.Name);
                }
                foreach(var obj in map.Objects)BaseObjectFootprint.Validate(obj);
                BaseObjectFootprint.ValidateBoard(map,board);
                if(modern)
                {
                    if(map.ForbidSoldiers!=(entry.Role=="village"||entry.Role=="camp"||entry.Role=="town"))
                        throw new ArgumentException(map.Title+"：场景参战限制由正式规则提供");
                    BaseSpatial.ValidateLayout(map,demo:true,scenery:entry.Role=="village",story:true);
                    BattleTacticalObject.Validate(map.TacticalObjects,map.Columns,map.Rows);
                }
            }
        }
        private static bool KindAllowed(string kind,bool modern)=>new[]{"facility","npc","interaction","obstacle","decoration","wildlife"}.Contains(kind)||modern&&new[]{"demo-prop","pixel-prop"}.Contains(kind);
        internal static bool NpcRole(string id) => Rules.Any(rule => rule.Id == id);
        private static bool Walkable(BattleBoard board,BaseMapObject[] objects,BasePoint p)=>BaseSpatial.Geometry(objects,p.X,p.Y,board.Width,board.Height)&&BaseSpatial.TerrainWalkable(board,p.X,p.Y,objects);

        // 编辑器测试入口校验一次；存档只检查数据结构，不重复搜索整张地图。
        public void ValidateForPlay()
        {
            Validate();
            if(Version!=2)throw new ArgumentException("请以最新正式地图为底稿重新导出。");
            foreach(var entry in Maps)
            {
                var map=entry.Layout;var board=entry.Board;
                var objects=map.Objects.Select(obj=>obj.Object).ToArray();
                var spawn=new BasePoint(map.SpawnX,map.SpawnY);
                if(!Walkable(board,objects,spawn))throw new ArgumentException(map.Title+"：出生点不可通行");
                var reached=Reachable(board,objects,spawn);
                foreach(var marker in map.Objects.Where(o=>!o.VisualOnly))
                    if(!reached.Any(p=>BaseInteraction.WithinReach(board,marker.Object,p.X,p.Y)))
                        throw new ArgumentException($"{map.Title}：从出生点到「{marker.Name}」({marker.X:g},{marker.Y:g})没有可走的路");
                if(new[]{"dungeon","field","bandit"}.Contains(entry.Role))
                    foreach(var side in new[]{DeploymentZone.A,DeploymentZone.B})
                        if(!board.Positions().Any(p=>board.Cell(p).Zone==side&&board.Walkable(p)&&Walkable(board,objects,new BasePoint(p.X+.5,p.Z+.5))))
                            throw new ArgumentException(map.Title+"：缺少"+(side==DeploymentZone.A?"我方":"敌方")+"可用部署格");
            }
        }
        private static List<BasePoint> Reachable(BattleBoard board,BaseMapObject[] objects,BasePoint spawn)
        {
            var result=new List<BasePoint>{spawn};var seen=new HashSet<(int,int)>();var queue=new Queue<(int,int)>();
            bool Segment(BasePoint from,BasePoint to)=>BaseMap.ClearSegment(from,to,null,(x,y)=>Walkable(board,objects,new BasePoint(x,y)),false,board.Width,board.Height);
            BasePoint Point(int x,int y)=>new BasePoint(x*.25+.125,y*.25+.125);
            int sx=(int)(spawn.X*4),sy=(int)(spawn.Y*4);
            for(int y=sy-1;y<=sy+1;y++)for(int x=sx-1;x<=sx+1;x++)
                if(Segment(spawn,Point(x,y))&&seen.Add((x,y)))queue.Enqueue((x,y));
            var directions=new[]{(1,0),(-1,0),(0,1),(0,-1)};
            while(queue.Count>0)
            {
                var (x,y)=queue.Dequeue();var p=Point(x,y);result.Add(p);
                foreach(var (dx,dy) in directions)
                    if(!seen.Contains((x+dx,y+dy))&&Segment(p,Point(x+dx,y+dy))&&seen.Add((x+dx,y+dy)))queue.Enqueue((x+dx,y+dy));
            }
            return result;
        }
        private static bool Finite(double value)=>!double.IsNaN(value)&&!double.IsInfinity(value);
    }
}

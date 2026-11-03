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
            public string Definition=>"story_"+Role+"_"+Id;
        }
        private static readonly Rule[] Rules={
            new Rule("village","village_house","facility",true),new Rule("village","intro_farm","facility",true),new Rule("village","intro_smith","facility",false),
            new Rule("village","demo_captain","npc",true),new Rule("village","intro_villager","npc",true),new Rule("village","prologue_report","interaction",true),new Rule("village","prologue_trigger","interaction",true),new Rule("village","demo_home_world","interaction",true),new Rule("village","demo_broken_sword","interaction",true),new Rule("village","demo_survivor","npc",false),
            new Rule("camp","home","facility",true),new Rule("camp","farm","facility",true),new Rule("camp","workshop","facility",true),new Rule("camp","barracks","facility",true),new Rule("camp","demo_rescuer","npc",false),new Rule("camp","demo_chunshen","npc",false),new Rule("camp","demo_ahe","npc",false),new Rule("camp","exit_east","interaction",true),new Rule("camp","demo_defense_exit","interaction",true),new Rule("camp","demo_march_exit","interaction",true),
            new Rule("town","town_quest","npc",true),new Rule("town","town_recruit","facility",true),new Rule("town","town_smith","facility",false),new Rule("town","town_market","facility",false),new Rule("town","town_blacksmith","npc",false),new Rule("town","town_guard","npc",false),new Rule("town","town_peddler","npc",false),new Rule("town","town_girl","npc",false),new Rule("town","demo_azhu","npc",false),new Rule("town","travel_base","interaction",true),new Rule("town","demo_ruins_exit","interaction",true),
            new Rule("ruins","demo_ruins_return","interaction",true),new Rule("ruins","demo_ruins_investigate","interaction",true),new Rule("field","demo_defense_return","interaction",true),new Rule("field","demo_defense_start","interaction",true),new Rule("bandit","demo_camp_world","interaction",true),new Rule("bandit","demo_camp_attack","interaction",true)
        };
        public string Format,Id;
        public int Version;
        public List<BaseDemoDesignMap> Maps;
        public BaseStoryScenePackage Copy()=>new BaseStoryScenePackage {Format=Format,Id=Id,Version=Version,Maps=Maps?.Select(map=>map?.Copy()).ToList()};

        public void InstallVillage(BaseState state)
        {
            Validate();
            if(state?.Demo?.StoryScene==null||state.Campaign==null||!BaseDemoPrologue.Village(state))throw new InvalidOperationException("剧情村庄只能安装到携带设计的序章状态");
            var authored=Maps.Single(map=>map.Role=="village");var layout=authored.Layout.Copy();var board=authored.Board.Clone();
            var captain=layout.Objects.Single(obj=>obj.Id=="demo_captain");
            // 两名剧情人物由序章状态管理；返乡对象只在返乡阶段出现。
            layout.Objects.RemoveAll(obj=>new[]{"demo_captain","intro_villager","demo_home_world","demo_broken_sword","demo_survivor"}.Contains(obj.Id));
            state.Campaign.Layouts.RemoveAll(map=>map.Region=="base");state.Campaign.Layouts.Add(layout);
            state.Campaign.RegionMaps.RemoveAll(map=>map.Id=="base");state.Campaign.RegionMaps.Add(new BaseRegionMap {Id="base",Board=board});
            state.PlayerX=state.Campaign.BaseX=layout.SpawnX;state.PlayerY=state.Campaign.BaseY=layout.SpawnY;
            if(state.Demo.Intro!=null){state.Demo.Intro.CaptainX=captain.X;state.Demo.Intro.CaptainY=captain.Y;}
        }

        public void Validate()
        {
            var roles=new Dictionary<string,string> { {"village","base"}, {"camp","base"}, {"town","town"}, {"ruins","outpost"}, {"field","perimeter"}, {"bandit","outpost"} };
            if(Format!="rpg-story-scene-package"||Version!=1||!Regex.IsMatch(Id??"","^[a-f0-9]{64}$")||Maps==null||Maps.Count!=roles.Count||Maps.Any(map=>map==null||!roles.ContainsKey(map.Role??""))||Maps.Select(map=>map.Role).Distinct().Count()!=roles.Count)
                throw new ArgumentException("剧情场景包格式、身份或六场景清单非法");
            var mapIds=new HashSet<string>();var objectIds=new HashSet<string>();
            foreach(var entry in Maps)
            {
                var map=entry.Layout;var board=entry.Board;
                if(map==null||map.Region!=roles[entry.Role]||!Regex.IsMatch(map.SourceMapId??"","^[a-zA-Z0-9_-]{1,80}$")||!mapIds.Add(map.SourceMapId)||string.IsNullOrWhiteSpace(map.Title)||map.Title.Length>100||map.Columns<10||map.Columns>64||map.Rows<10||map.Rows>64||!Finite(map.SpawnX)||!Finite(map.SpawnY)||map.SpawnX<0||map.SpawnX>=map.Columns||map.SpawnY<0||map.SpawnY>=map.Rows)
                    throw new ArgumentException("剧情场景身份、尺寸或出生点非法："+entry.Role);
                if(board==null||board.Width!=map.Columns||board.Height!=map.Rows||board.Cells?.Length!=board.Width*board.Height||board.Cells.Any(cell=>cell==null||!cell.MetadataValid||!Enum.IsDefined(typeof(TerrainKind),cell.Terrain)||!Enum.IsDefined(typeof(DeploymentZone),cell.Zone)||!cell.Walkable&&cell.Zone!=DeploymentZone.None))
                    throw new ArgumentException("剧情场景地格非法："+entry.Role);
                if(map.Objects==null||map.Objects.Count>300||map.Objects.Any(obj=>obj==null||string.IsNullOrWhiteSpace(obj.Id)||obj.Id.Length>100||string.IsNullOrWhiteSpace(obj.Name)||obj.Name.Length>100||!Regex.IsMatch(obj.SourceObjectId??"","^[a-zA-Z0-9_-]{1,80}$")||!objectIds.Add(obj.SourceObjectId)||!new[]{"facility","npc","interaction","obstacle","decoration","wildlife"}.Contains(obj.Kind)||!Finite(obj.X)||!Finite(obj.Y)||!Finite(obj.Width)||!Finite(obj.Height)||obj.Width<=0||obj.Height<=0||obj.X-obj.Object.BoundsWidth/2<0||obj.X+obj.Object.BoundsWidth/2>map.Columns||obj.Y-obj.Object.BoundsHeight/2<0||obj.Y+obj.Object.BoundsHeight/2>map.Rows)||map.Objects.Select(obj=>obj.Id).Distinct().Count()!=map.Objects.Count)
                    throw new ArgumentException("剧情场景对象身份、坐标或占地非法："+entry.Role);
                foreach(var rule in Rules.Where(rule=>rule.Role==entry.Role&&rule.Required))
                    if(!map.Objects.Any(obj=>obj.Id==rule.Id&&obj.DefinitionId==rule.Definition&&!obj.VisualOnly))throw new ArgumentException("缺少剧情必需对象："+rule.Id);
                foreach(var obj in map.Objects)
                {
                    var rule=Rules.FirstOrDefault(item=>item.Role==entry.Role&&item.Definition==obj.DefinitionId);
                    if(rule!=null)
                    {
                        bool primary=obj.Id==rule.Id&&!obj.VisualOnly;
                        bool duplicate=rule.Kind=="npc"&&obj.Id=="story-shell:"+obj.SourceObjectId&&obj.VisualOnly&&map.Objects.Any(item=>item.Id==rule.Id&&item.DefinitionId==rule.Definition&&!item.VisualOnly);
                        if(obj.Kind!=rule.Kind||obj.Blocks!=(rule.Kind=="facility")||!primary&&!duplicate)throw new ArgumentException("剧情对象身份非法："+obj.Name);
                    }
                    else if(!obj.VisualOnly||obj.Id!=obj.SourceObjectId||!(obj.DefinitionId=="decor_obstacle"&&obj.Kind=="obstacle"||obj.DefinitionId=="decor_marker"&&obj.Kind=="obstacle"||obj.DefinitionId?.StartsWith("env_")==true&&(obj.Kind=="decoration"||obj.Kind=="wildlife")&&BaseEnvironment.Find(obj.AssetId)!=null))throw new ArgumentException("对象不属于当前剧情场景："+obj.Name);
                }
                foreach(var obj in map.Objects)BaseObjectFootprint.Validate(obj);
                BaseObjectFootprint.ValidateBoard(map,board);
            }
        }
        private static bool Finite(double value)=>!double.IsNaN(value)&&!double.IsInfinity(value);
    }
}

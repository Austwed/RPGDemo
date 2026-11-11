using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using BaseConquest.Battle;

namespace BaseConquest.Base
{
    public static class BaseSceneInstances
    {
        public static bool Active(BaseState state)=>state.Campaign.InstanceVersion==1;
        public static bool LocationValid(BaseState state,string region)=>BaseCampaign.Locations.Contains(region)||Active(state)&&region=="custom";
        public static BaseMapLayout Layout(BaseState state,string key=null)
        {
            var maps=state.Campaign.Layouts;
            if(key==null)return maps.Single(map=>map.SourceMapId==state.Campaign.CurrentMapId);
            var exact=maps.SingleOrDefault(map=>map.SourceMapId==key);if(exact!=null)return exact;
            var current=maps.SingleOrDefault(map=>map.SourceMapId==state.Campaign.CurrentMapId&&map.Region==key);if(current!=null)return current;
            var matches=maps.Where(map=>map.Region==key).ToArray();
            if(matches.Length!=1)throw new InvalidOperationException("场景角色不唯一，请使用地图ID："+key);
            return matches[0];
        }
        public static string BoardKey(BaseState state,string key=null)=>Active(state)?Layout(state,key).SourceMapId:key??state.Campaign.Location;
        public static void ValidateLayouts(BaseState state)
        {
            var campaign=state.Campaign;var maps=campaign.Layouts;
            if(string.IsNullOrEmpty(campaign.DesignId)||maps==null||maps.Count<5||maps.Count>32||maps.Any(map=>map==null||!Regex.IsMatch(map.SourceMapId??"","^[a-zA-Z0-9_-]{1,80}$"))||maps.Select(map=>map.SourceMapId).Distinct().Count()!=maps.Count)throw new ArgumentException("场景实例身份或数量非法");
            foreach(string role in new[]{"base","perimeter","world","town"})if(maps.Count(map=>map.Region==role)!=1)throw new ArgumentException("本批须保留一个"+role+"实例；多个经营基地或独立城镇经济待接入");
            if(maps.Count(map=>map.Region=="outpost")<1||maps.Count(map=>map.Region=="outpost")>16)throw new ArgumentException("敌营实例数量须为1～16");
            foreach(var map in maps)
            {
                if(BaseCampaign.Locations.Contains(map.SourceMapId)||map.SourceMapId=="custom")throw new ArgumentException("地图实例ID不能借用场景角色名称");
                BaseSpatial.ValidateLayout(map,true,demo:BaseDemo.Active(state),story:state.Demo?.StoryScene?.Valid==true);
                if(map.Region=="custom"&&string.IsNullOrEmpty(map.ParentMapId)&&!BaseWorldJourney.Active(state)||map.Region!="custom"&&!string.IsNullOrEmpty(map.ParentMapId))throw new ArgumentException("附属场景须用custom并声明父地图");
                var seen=new HashSet<string>{map.SourceMapId};var parent=map;
                while(!string.IsNullOrEmpty(parent.ParentMapId)){parent=maps.SingleOrDefault(value=>value.SourceMapId==parent.ParentMapId);if(parent==null||!seen.Add(parent.SourceMapId))throw new ArgumentException("附属父场景缺失或形成循环");}
                if(map.Region=="custom"&&map.Objects.Any(obj=>!obj.VisualOnly&&obj.DefinitionId!="map_exit"&&!(BaseDemoFinale.Opened(state)&&(map.SourceMapId==BaseDemoFinale.Home&&obj.Id=="demo_broken_sword"||map.SourceMapId=="demo_ruins"&&(obj.Id=="demo_ruins_investigate"||obj.Id==BaseBanditHideout.EntryId)||BaseSceneRules.Active(state)&&map.SourceMapId==BaseSceneRules.DungeonId&&(obj.Id=="demo_ruins_investigate"||obj.Id=="demo_dungeon_exit")))))throw new ArgumentException("附属场景仅支持出入口、外观及Demo固定调查点");
            }
            var identities=maps.SelectMany(map=>map.Objects).Select(obj=>obj.SourceObjectId).ToArray();
            if(identities.Any(string.IsNullOrWhiteSpace)||identities.Distinct().Count()!=identities.Length)throw new ArgumentException("跨场景对象实例ID重复或缺失");
            var active=maps.SingleOrDefault(map=>map.SourceMapId==campaign.CurrentMapId);
            if(active==null||active.Region!=campaign.Location)throw new ArgumentException("当前位置与场景实例不一致");
            if(campaign.RegionMaps==null||campaign.RegionMaps.Count!=maps.Count-1)throw new ArgumentException("场景实例缺少独立地图快照");
            if(campaign.HasActiveBattle)
            {
                var battle=campaign.ActiveBattle;var battlefield=maps.SingleOrDefault(map=>map.SourceMapId==battle?.MapId);
                var battleEvent=campaign.Events.SingleOrDefault(item=>item.Id==battle?.EventId);
                if(battlefield==null||battlefield.Region!=battle.MapRegion||battleEvent==null||battleEvent.Kind=="attack"&&battleEvent.SourceSiteId!=battle.MapId&&!BaseBanditHideout.OwnsBattlefield(battle)&&!(BaseDemoRuins.Enabled(state)&&battleEvent.Id==BaseDemoRuins.EventId&&(battle.MapId=="demo_ruins"||BaseSceneRules.Active(state)&&battle.MapId==BaseSceneRules.DungeonId)))throw new ArgumentException("活动战斗与场景实例／敌营来源不一致");
            }
        }
        public static bool CanLink(BaseMapLayout source,BaseMapLayout target)
        {
            if(source==target)return false;
            if(source.Region=="custom"||target.Region=="custom")return source.ParentMapId==target.SourceMapId||target.ParentMapId==source.SourceMapId;
            return new[]{"base:town","town:base","base:perimeter","perimeter:base","base:world","world:base","world:outpost","outpost:world","outpost:outpost"}.Contains(source.Region+":"+target.Region);
        }
        public static bool Travel(BaseState state,string region,string objectId)
        {
            BaseEconomy.Validate(state);
            if(!Active(state)||BaseBattleLoot.Pending(state)||state.Aftermath.GameOver||state.Campaign.HasActiveBattle||state.Muster.Parties.Any(party=>party.InCombat))return false;
            var link=BaseDesignRoutes.Reachable(state,region,objectId);if(link==null)return false;
            var source=Layout(state);var target=BaseDesignRoutes.Target(state,link);if(!CanLink(source,target)&&!BaseWorldJourney.LocalLink(state,source,target))return false;
            var context=target;while(context.Region=="custom"&&!string.IsNullOrEmpty(context.ParentMapId))context=Layout(state,context.ParentMapId);
            if((context.Region=="world"||context.Region=="outpost")&&!state.Muster.Parties.Any(party=>party.Purpose=="expedition"&&party.Leaders.Any(leader=>leader.LeaderId==state.PlayerLeaderId)))return false;
            if(source.Region=="base"){state.Campaign.BaseX=state.PlayerX;state.Campaign.BaseY=state.PlayerY;}
            state.Campaign.CurrentMapId=target.SourceMapId;state.Campaign.Location=target.Region;state.PlayerX=link.X;state.PlayerY=link.Y;
            foreach(var leader in BaseAdventure.Members(state)){leader.UnavailableDay=state.Day;leader.FacilityId="";}
            BaseAdventure.Arrive(state);return true;
        }
        public static void SaveBoard(BaseState state,BattleBoard board,string region,string mapId=null)
        {
            if(region=="perimeter")state.Campaign.PerimeterBoard=board;
            else state.Campaign.RegionMaps.Single(map=>map.Id==BoardKey(state,string.IsNullOrEmpty(mapId)?region:mapId)).Board=board;
        }
    }
}

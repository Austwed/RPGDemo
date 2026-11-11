using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace BaseConquest.Base
{
    [Serializable] public sealed class BaseWorldNode
    {
        public string Id,Title,AreaId;
        public double X,Y;
        public List<string> MapIds=new List<string>();
        public BaseWorldNode Copy()=>new BaseWorldNode{Id=Id,Title=Title,AreaId=AreaId,X=X,Y=Y,MapIds=MapIds.ToList()};
    }
    [Serializable] public sealed class BaseWorldRoad
    {
        public string Id,From,To,FromMapId,ToMapId,EnemyMapId,CrossingId;
        public int Days;
        public bool Bidirectional=true;
        public BaseWorldRoad Copy()=>(BaseWorldRoad)MemberwiseClone();
    }
    [Serializable] public sealed class BaseWorldEntry
    {
        public string Id,NodeId,MapId,ObjectId;
        public double X,Y;
        public BaseWorldEntry Copy()=>(BaseWorldEntry)MemberwiseClone();
    }
    [Serializable] public sealed class BaseWorldCrossing
    {
        public string Id,EntryId,ExitId;
        public BaseWorldCrossing Copy()=>(BaseWorldCrossing)MemberwiseClone();
    }
    [Serializable] public sealed class BaseWorldJourneyState
    {
        public int Version;
        public string NodeId,LastEntryId,BackgroundResource;
        public List<BaseWorldNode> Nodes=new List<BaseWorldNode>();
        public List<BaseWorldRoad> Roads=new List<BaseWorldRoad>();
        public List<BaseWorldEntry> Entries=new List<BaseWorldEntry>();
        public List<BaseWorldCrossing> Crossings=new List<BaseWorldCrossing>();
        public List<string> Discovered=new List<string>(),Unlocked=new List<string>(),Route=new List<string>();
        public int RoadIndex,RoadDay;
        public BaseWorldJourneyState Copy()=>new BaseWorldJourneyState{Version=Version,NodeId=NodeId,LastEntryId=LastEntryId,BackgroundResource=BackgroundResource,Nodes=Nodes.Select(node=>node.Copy()).ToList(),Roads=Roads.Select(road=>road.Copy()).ToList(),Entries=Entries.Select(entry=>entry.Copy()).ToList(),Crossings=Crossings.Select(crossing=>crossing.Copy()).ToList(),Discovered=Discovered.ToList(),Unlocked=Unlocked.ToList(),Route=Route.ToList(),RoadIndex=RoadIndex,RoadDay=RoadDay};
    }
    public sealed class BaseWorldPlan
    {
        public List<string> Path,Preview;
        public int Days;
        public bool Changed,Reachable;
    }
    public static class BaseWorldJourney
    {
        public static bool Active(BaseState state)=>state?.Campaign?.WorldJourney?.Version==1;
        public static bool OnWorld(BaseState state)=>Active(state)&&state.Campaign.Location=="world";
        public static bool Marching(BaseState state)=>Active(state)&&state.Campaign.WorldJourney.Route.Count>0;
        public static bool LocalLink(BaseState state,BaseMapLayout source,BaseMapLayout target)=>Active(state)&&AreaForMap(state,source.SourceMapId)!=""&&AreaForMap(state,source.SourceMapId)==AreaForMap(state,target.SourceMapId);
        public static string AreaForMap(BaseState state,string mapId){var node=state.Campaign.WorldJourney.Nodes.FirstOrDefault(n=>n.MapIds.Contains(mapId));return node==null?"":string.IsNullOrEmpty(node.AreaId)?node.Id:node.AreaId;}
        public static string AreaForNode(BaseState state,string id){var node=state.Campaign.WorldJourney.Nodes.First(n=>n.Id==id);return string.IsNullOrEmpty(node.AreaId)?node.Id:node.AreaId;}
        private static bool Identity(string value)=>Regex.IsMatch(value??"","^[a-zA-Z0-9_-]{1,80}$");
        public static BaseWorldRoad Road(BaseWorldJourneyState world,string from,string to)=>world.Roads.FirstOrDefault(road=>road.From==from&&road.To==to||road.Bidirectional&&road.To==from&&road.From==to);
        public static bool Passable(BaseState state,BaseWorldRoad road)=>road!=null&&(string.IsNullOrEmpty(road.EnemyMapId)||BaseThreats.Site(state,road.EnemyMapId)?.Owner=="player")&&(string.IsNullOrEmpty(road.CrossingId)||state.Campaign.WorldJourney.Unlocked.Contains(road.CrossingId))&&(!BaseDemoFinale.Opened(state)||road.EnemyMapId!=BaseDemoFinale.Camp||!BaseBattleLoot.Pending(state));
        public static BaseWorldEntry EntryForObject(BaseState state,string id)=>Active(state)?state.Campaign.WorldJourney.Entries.FirstOrDefault(entry=>entry.MapId==BaseSpatial.Layout(state).SourceMapId&&BaseSpatial.Layout(state).Objects.Any(obj=>obj.SourceObjectId==entry.ObjectId&&obj.Id==id)):null;
        public static void Validate(BaseState state)
        {
            var world=state.Campaign.WorldJourney;if(world==null||world.Version==0)return;
            if(world.Version!=1||!BaseSceneInstances.Active(state)||world.Nodes==null||world.Nodes.Count<2||world.Nodes.Count>64||world.Roads==null||world.Roads.Count>1500||world.Entries==null||world.Entries.Count>200||world.Crossings==null||world.Crossings.Count>64)throw new ArgumentException("世界行军版本或配置数量非法");
            if(world.Nodes.Any(node=>node==null||!Identity(node.Id)||string.IsNullOrWhiteSpace(node.Title)||node.Title.Length>100||double.IsNaN(node.X)||double.IsNaN(node.Y)||node.X<0||node.X>1||node.Y<0||node.Y>1||node.MapIds==null)||world.Nodes.Select(node=>node.Id).Distinct().Count()!=world.Nodes.Count)throw new ArgumentException("世界地点身份或位置非法");
            var ids=world.Nodes.Select(node=>node.Id).ToHashSet();var maps=state.Campaign.Layouts;
            var assigned=world.Nodes.SelectMany(node=>node.MapIds).ToArray();
            if(assigned.Length!=maps.Count-1||assigned.Distinct().Count()!=assigned.Length||assigned.Any(id=>!maps.Any(map=>map.SourceMapId==id&&map.Region!="world")))throw new ArgumentException("地区场景归属重复或缺失");
            if(world.Entries.Any(entry=>entry==null||!Identity(entry.Id)||!ids.Contains(entry.NodeId)||!world.Nodes.Single(node=>node.Id==entry.NodeId).MapIds.Contains(entry.MapId)||!maps.Any(map=>map.SourceMapId==entry.MapId&&map.Objects.Any(obj=>obj.SourceObjectId==entry.ObjectId&&!obj.Blocks&&!obj.VisualOnly&&(obj.DefinitionId=="map_exit"||new[]{"exit_west","exit_east","exit_south","travel_world","travel_base"}.Contains(obj.Id))))||double.IsNaN(entry.X)||double.IsNaN(entry.Y)||!BaseSpatial.Walkable(state,entry.X,entry.Y,entry.MapId)))throw new ArgumentException("世界出入口引用或落点非法");
            if(world.Entries.Select(entry=>entry.Id).Distinct().Count()!=world.Entries.Count||world.Entries.Select(entry=>entry.MapId+":"+entry.ObjectId).Distinct().Count()!=world.Entries.Count||world.Nodes.Any(node=>node.MapIds.Count>0&&!world.Entries.Any(entry=>entry.NodeId==node.Id)))throw new ArgumentException("世界出入口重复或地区没有入口");
            foreach(var entry in world.Entries){var marker=maps.Single(map=>map.SourceMapId==entry.MapId).Objects.Single(obj=>obj.SourceObjectId==entry.ObjectId);if(!BaseInteraction.WithinReach(BaseBattlefield.Board(state,entry.MapId),marker.Object,entry.X,entry.Y))throw new ArgumentException("世界入口落点必须在出口同层一格以内");}
            if(world.Roads.Any(road=>road==null||!Identity(road.Id)||!ids.Contains(road.From)||!ids.Contains(road.To)||road.From==road.To||road.Days<0||road.Days>365||!string.IsNullOrEmpty(road.EnemyMapId)&&!maps.Any(map=>map.SourceMapId==road.EnemyMapId&&map.Region=="outpost"))||world.Roads.Select(road=>road.Id).Distinct().Count()!=world.Roads.Count)throw new ArgumentException("世界道路端点、整数天数或阻路敌营非法");
            var directed=world.Roads.SelectMany(road=>road.Bidirectional?new[]{road.From+":"+road.To,road.To+":"+road.From}:new[]{road.From+":"+road.To}).ToArray();
            if(directed.Distinct().Count()!=directed.Length)throw new ArgumentException("世界道路方向重复");
            if(world.Roads.Any(road=>!string.IsNullOrEmpty(road.FromMapId)&&!world.Nodes.Single(node=>node.Id==road.From).MapIds.Contains(road.FromMapId)||!string.IsNullOrEmpty(road.ToMapId)&&!world.Nodes.Single(node=>node.Id==road.To).MapIds.Contains(road.ToMapId)))throw new ArgumentException("道路场景锚点不属于对应地区");
            string home=world.Nodes.Single(node=>node.MapIds.Contains(maps.Single(map=>map.Region=="base").SourceMapId)).Id;
            foreach(bool reverse in new[]{false,true}){var reached=new HashSet<string>{home};int previous=-1;while(previous!=reached.Count){previous=reached.Count;foreach(var road in world.Roads){string from=reverse?road.To:road.From,to=reverse?road.From:road.To;if(reached.Contains(from))reached.Add(to);if(road.Bidirectional&&reached.Contains(to))reached.Add(from);}}if(reached.Count!=world.Nodes.Count)throw new ArgumentException("世界道路缺少出发或返程的潜在路径");}
            if(world.Crossings.Any(crossing=>crossing==null||!Identity(crossing.Id)||crossing.EntryId==crossing.ExitId||!world.Entries.Any(entry=>entry.Id==crossing.EntryId)||!world.Entries.Any(entry=>entry.Id==crossing.ExitId))||world.Crossings.Select(crossing=>crossing.Id).Distinct().Count()!=world.Crossings.Count||world.Roads.Any(road=>!string.IsNullOrEmpty(road.CrossingId)&&!world.Crossings.Any(crossing=>crossing.Id==road.CrossingId)))throw new ArgumentException("首次穿越引用非法");
            if(!ids.Contains(world.NodeId)||world.Discovered==null||world.Unlocked==null||world.Route==null||world.Discovered.Distinct().Count()!=world.Discovered.Count||world.Unlocked.Distinct().Count()!=world.Unlocked.Count||world.Discovered.Any(id=>!world.Entries.Any(entry=>entry.Id==id))||world.Unlocked.Any(id=>!world.Crossings.Any(crossing=>crossing.Id==id))||!string.IsNullOrEmpty(world.LastEntryId)&&!world.Entries.Any(entry=>entry.Id==world.LastEntryId))throw new ArgumentException("世界位置或发现状态非法");
            if(world.Route.Count==0){if(world.RoadIndex!=0||world.RoadDay!=0)throw new ArgumentException("空行程不能保留路段进度");}
            else if(world.Route.Count<2||world.Route.Count>64||world.Route.Distinct().Count()!=world.Route.Count||world.Route.Any(id=>!ids.Contains(id))||world.RoadIndex<0||world.RoadIndex>=world.Route.Count-1||world.NodeId!=world.Route[world.RoadIndex]||world.Route.Skip(1).Where((id,index)=>Road(world,world.Route[index],id)==null).Any()||world.RoadDay<0||world.RoadDay>=Math.Max(1,Road(world,world.Route[world.RoadIndex],world.Route[world.RoadIndex+1]).Days)||state.Campaign.Location!="world")throw new ArgumentException("活动行程或路段进度非法");
        }
        public static BaseWorldPlan Plan(BaseState state,IEnumerable<string> previous,string target)
        {
            var world=state.Campaign.WorldJourney;var path=previous?.ToList()??new List<string>{world.NodeId};
            if(path.Count==0)path.Add(world.NodeId);
            if(!world.Nodes.Any(node=>node.Id==target)||path[0]!=world.NodeId||path.Distinct().Count()!=path.Count||path.Skip(1).Where((id,index)=>Road(world,path[index],id)==null).Any())throw new ArgumentException("计划身份、道路或真实起点非法");
            BaseWorldPlan Result(List<string> route,List<string> preview,bool reachable=true)=>new BaseWorldPlan{Path=route,Preview=preview,Days=route.Skip(1).Select((id,index)=>Road(world,route[index],id).Days).Sum(),Changed=!route.SequenceEqual(path),Reachable=reachable};
            int old=path.IndexOf(target);if(old>=0)return Result(path.Take(old+1).ToList(),old==path.Count-1?new List<string>():path.Skip(old).Reverse().ToList());
            var direct=Road(world,path.Last(),target);if(Passable(state,direct))return Result(path.Concat(new[]{target}).ToList(),new List<string>{path.Last(),target});
            for(int keep=path.Count;keep>=1;keep--)
            {
                var prefix=path.Take(keep).ToList();var visited=new HashSet<string>(prefix.Take(keep-1));var costs=new Dictionary<string,int>{{prefix.Last(),0}};var routes=new Dictionary<string,List<string>>{{prefix.Last(),new List<string>{prefix.Last()}}};
                while(costs.Keys.Any(id=>!visited.Contains(id)))
                {
                    string next=costs.Keys.Where(id=>!visited.Contains(id)).OrderBy(id=>costs[id]).First();
                    if(next==target)return Result(prefix.Concat(routes[next].Skip(1)).ToList(),path.Skip(keep-1).Reverse().Concat(routes[next].Skip(1)).ToList());
                    visited.Add(next);
                    foreach(var road in world.Roads.Where(road=>Passable(state,road)))
                    {
                        string neighbor=road.From==next?road.To:road.Bidirectional&&road.To==next?road.From:null;
                        if(neighbor==null||visited.Contains(neighbor))continue;int cost=costs[next]+road.Days;
                        if(!costs.ContainsKey(neighbor)||cost<costs[neighbor]){costs[neighbor]=cost;routes[neighbor]=routes[next].Concat(new[]{neighbor}).ToList();}
                    }
                }
            }
            return Result(path,new List<string>(),false);
        }
        public static void EnterWorld(BaseState state,string entryId)
        {
            var world=state.Campaign.WorldJourney;var entry=world.Entries.Single(item=>item.Id==entryId);var source=BaseSpatial.Layout(state);var marker=source.Objects.SingleOrDefault(obj=>obj.SourceObjectId==entry.ObjectId);
            if(source.SourceMapId!=entry.MapId||marker==null||!BaseInteraction.CanReach(state,marker.Object)||state.Campaign.HasActiveBattle||BaseBattleLoot.Pending(state)||!state.Muster.Parties.Any(party=>party.Purpose=="expedition"&&party.Leaders.Any(leader=>leader.LeaderId==state.PlayerLeaderId)))throw new InvalidOperationException("请由主角出征队在同层一格内与世界出入口交互");
            if(!world.Discovered.Contains(entryId))world.Discovered.Add(entryId);
            foreach(var crossing in world.Crossings.Where(crossing=>crossing.EntryId==world.LastEntryId&&crossing.ExitId==entryId||crossing.ExitId==world.LastEntryId&&crossing.EntryId==entryId))if(!world.Unlocked.Contains(crossing.Id))world.Unlocked.Add(crossing.Id);
            if(source.Region=="base"){state.Campaign.BaseX=state.PlayerX;state.Campaign.BaseY=state.PlayerY;}
            world.NodeId=entry.NodeId;world.LastEntryId=null;var map=BaseSceneInstances.Layout(state,"world");state.Campaign.CurrentMapId=map.SourceMapId;state.Campaign.Location="world";state.PlayerX=map.SpawnX;state.PlayerY=map.SpawnY;BaseAdventure.Arrive(state);
        }
        public static void EnterScene(BaseState state,string entryId)
        {
            var world=state.Campaign.WorldJourney;var entry=world.Entries.Single(item=>item.Id==entryId);
            if(!OnWorld(state)||Marching(state)||entry.NodeId!=world.NodeId||!world.Discovered.Contains(entryId)||BaseBattleLoot.Pending(state))throw new InvalidOperationException("只能从已抵达地点进入已发现入口");
            var map=BaseSceneInstances.Layout(state,entry.MapId);
            if(map.Region=="base")foreach(var party in state.Muster.Parties.Where(party=>party.Purpose=="expedition").ToArray()){foreach(var quota in state.Supply.Quotas.Where(quota=>quota.PartyId==party.Id).ToArray())if(!BaseSupply.Cancel(state,quota.BattleId))throw new InvalidOperationException("战斗军需未解锁");if(!BaseMuster.Return(state,party.Id,worker=>{worker.Resting=worker.Health<worker.MaxHealth*BaseDevelopment.DutyThreshold;}))throw new InvalidOperationException("出征队无法返营");}
            world.LastEntryId=entryId;state.Campaign.CurrentMapId=map.SourceMapId;state.Campaign.Location=map.Region;state.PlayerX=entry.X;state.PlayerY=entry.Y;BaseAdventure.Arrive(state);
        }
        public static void Start(BaseState state,IEnumerable<string> path)
        {
            if(!OnWorld(state)||Marching(state)||BaseBattleLoot.Pending(state))throw new InvalidOperationException("目前不能开始新行程");
            var route=path.ToList();var world=state.Campaign.WorldJourney;
            if(route.Count<2||route[0]!=world.NodeId||route.Distinct().Count()!=route.Count||route.Skip(1).Where((id,index)=>!Passable(state,Road(world,route[index],id))).Any())throw new InvalidOperationException("路线已失效，请重新规划");
            world.Route=route;world.RoadDay=world.RoadIndex=0;
            // P42：首次出发讨伐黑鸦寨时解锁副手设立；此前没有副手，剧情防守也只能玩家亲自。
            if(route[route.Count-1]=="bandit")BaseDeputy.Unlock(state);
        }
        public static bool Step(BaseState state,Func<string,BaseConquest.Battle.BattleScenarioDocument> resolve)
        {
            if(!OnWorld(state)||!Marching(state)||BaseBattleLoot.Pending(state)||state.Campaign.HasActiveBattle||state.Aftermath.GameOver)return false;
            var world=state.Campaign.WorldJourney;var road=Road(world,world.NodeId,world.Route[world.RoadIndex+1]);
            if(!Passable(state,road))return false;
            int raidDay=BaseDemoDefense.SecondArrival(state);
            if(raidDay>0&&world.NodeId=="camp"&&world.Route[world.RoadIndex+1]=="bandit"&&(state.Day==raidDay||state.Day+road.Days-world.RoadDay==raidDay))BaseDemoDefense.CancelSecond(state,"intercepted");
            if(road.Days>0){int day=state.Day;if(!BaseCampaign.Rest(state,day,resolve,true))return false;if(state.Aftermath.GameOver)return true;if(state.Day!=day+1)return false;world.RoadDay++;}
            if(world.RoadDay>=road.Days){world.NodeId=world.Route[++world.RoadIndex];world.RoadDay=0;if(world.RoadIndex==world.Route.Count-1){world.Route.Clear();world.RoadIndex=0;}}
            return true;
        }
    }
}

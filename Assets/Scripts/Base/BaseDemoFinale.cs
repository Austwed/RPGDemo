using System;
using System.Collections.Generic;
using System.Linq;
using BaseConquest.Battle;
using BaseConquest.RealtimePrototype;

namespace BaseConquest.Base
{
    [Serializable] public sealed class BaseDemoFinaleProgress
    {
        public int Version, AttackDay, EndDay,ReconstructionVersion,RebuildDay,RebuildMask;
        public bool WorldOpened,AttackBriefingSeen,VillageRebuilt;
        public BattleBoard CampBoard;
        public BaseMapLayout Village;
        public BaseDemoFinaleProgress Copy()=>new BaseDemoFinaleProgress {Version=Version,AttackDay=AttackDay,EndDay=EndDay,ReconstructionVersion=ReconstructionVersion,RebuildDay=RebuildDay,RebuildMask=RebuildMask,VillageRebuilt=VillageRebuilt,WorldOpened=WorldOpened,AttackBriefingSeen=AttackBriefingSeen,CampBoard=CampBoard?.Clone(),Village=Village?.Copy()};
    }
    public static class BaseDemoFinale
    {
        public const string Camp="demo_bandit", Home="demo_home", AttackProof="demo:bandit:captured", Sword="demo:clue:broken-sword";
        public static bool Enabled(BaseState state)=>BaseDemo.Active(state)&&state.Demo.Finale?.Version==1;
        public static bool Opened(BaseState state)=>Enabled(state)&&state.Demo.Finale.WorldOpened;
        public static bool Later(BaseState state)=>state.Demo.Stage==BaseDemoStage.AttackWon||state.Demo.Stage==BaseDemoStage.Finished;
        public static bool Rebuilding(BaseState state)=>Enabled(state)&&state.Demo.Stage==BaseDemoStage.AttackWon&&state.Demo.Finale.ReconstructionVersion==1;
        public static bool BattleActive(BaseState state)=>Opened(state)&&state.Campaign.CurrentMapId==Camp&&state.Campaign.HasActiveBattle&&state.Campaign.ActiveBattle.EventId==BaseThreats.Site(state,Camp).AttackEventId;
        public static bool Encounter(BaseCampaignSession session)=>session?.Opening.MapId==Camp;
        public static void PrepareOpening(BaseState state)
        {state.Demo.Finale=new BaseDemoFinaleProgress {Version=1,CampBoard=BaseBattlefield.Board(state,"outpost").Clone(),Village=BaseDemoPrologue.CreateVillage()};}
        public static void InstallMarker(BaseState state)
        {if(Enabled(state))state.Campaign.Layouts.Single(map=>map.Region=="base").Objects.Add(new BaseMapPlacement {Id="demo_march_exit",Name="世界道路 · 首次防守后开放",X=12,Y=.7,Width=2,Height=1,VisualOnly=true});}
        public static string Objective(BaseState state)=>!Enabled(state)?"此旧Demo没有攻营与结尾快照，请新开Demo":state.Demo.Stage==BaseDemoStage.Finished?"Demo结束：已记录队长断剑，失踪真相仍待追查":Rebuilding(state)?"返乡重建：放置小粮田、营帐和仓库，再确认完成":Later(state)?BaseBattleLoot.Pending(state)?"处理攻营战果后，沿世界道路返乡":"返乡通路已开放：行军至"+BaseScriptNames.Village+"，调查断剑":"首防已胜：补损整备，从营地南侧世界出口攻营";
        public static BaseMapLayout CreateCampLayout()=>new BaseMapLayout {Region="outpost",SourceMapId=Camp,Title=BaseScriptNames.Bandit,SpawnX=2,SpawnY=6.5,Objects=new List<BaseMapPlacement> {
            new BaseMapPlacement {Id="demo_camp_world",Name="世界道路",X=.7,Y=6.5,Width=1,Height=2},
            new BaseMapPlacement {Id="demo_camp_attack",Name="土匪驻军 · 进攻",X=3,Y=6.5,Width=1,Height=1}
        }};
        public static BattleBoard CreateCampBoard(BaseState state)
        {
            var board=state.Demo.Finale.CampBoard.Clone();
            if(BaseDemoRelease.Enabled(state)||state.Demo.StoryScene?.Valid==true)return board;
            foreach(var position in board.Positions()){var cell=board.Cell(position);cell.Zone=!cell.Walkable?DeploymentZone.None:position.X>=4&&position.X<=6?DeploymentZone.A:position.X>=17&&position.X<=19?DeploymentZone.B:DeploymentZone.None;}
            return board;
        }
        public static void OpenWorld(BaseState state)
        {
            if(!Enabled(state)||Opened(state)||state.Demo.Stage!=BaseDemoStage.DefenseWon||state.Demo.Defense.BatuStoryDay==0||BaseBattleLoot.Pending(state)||!BaseInteraction.At(state,"base","demo_march_exit")||state.Muster.Parties.Count>0)throw new InvalidOperationException("先处理首防战果并返营整备，到南侧世界出口确认");
            var campaign=state.Campaign;var keys=new Dictionary<string,string>{{"base","demo_camp"},{"town","demo_town"},{"outpost","demo_ruins"},{"perimeter","demo_field"},{"dungeon",BaseSceneRules.DungeonId},{BaseBanditHideout.Region,BaseBanditHideout.MapId}};
            var boards=campaign.Layouts.Where(map=>map.Region!="perimeter").Select(map=>new BaseRegionMap {Id=keys[map.Region],Board=BaseBattlefield.Board(state,map.Region).Clone()}).ToList();
            foreach(var map in campaign.Layouts)
            {
                map.SourceMapId=keys[map.Region];foreach(var obj in map.Objects)obj.SourceObjectId=map.SourceMapId+"_"+obj.Id;
                if(map.Region=="dungeon"){map.Region="custom";map.ParentMapId="demo_town";}
                else if(map.Region==BaseBanditHideout.Region){map.Region="custom";map.ParentMapId="demo_ruins";}
                else if(map.Region=="outpost"){map.Region="custom";map.ParentMapId="demo_town";foreach(var obj in map.Objects.Where(obj=>obj.Id!="demo_ruins_return"&&obj.Id!="demo_ruins_investigate"&&obj.Id!=BaseBanditHideout.EntryId))obj.VisualOnly=true;}
            }
            var worldMap=new BaseMapLayout {Region="world",SourceMapId="demo_world",Title="返乡行军",SpawnX=12,SpawnY=6.5};campaign.Layouts.Add(worldMap);
            var campLayout=state.Demo.StoryScene?.Valid==true?state.Demo.StoryScene.Map("bandit").Layout.Copy():BaseDemoRelease.Enabled(state)?state.Demo.ReleaseMaps.Single(m=>m.Role=="bandit").Layout.Copy():CreateCampLayout();campaign.Layouts.Add(campLayout);
            var village=state.Demo.StoryScene?.Valid==true?state.Demo.StoryScene.ReturnVillage():state.Demo.Finale.Village.Copy();village.SourceMapId=Home;village.Region="custom";village.ParentMapId=null;village.Title=BaseScriptNames.Village+" · 劫后余痕";village.SpawnX=21;village.SpawnY=7;
            village.Objects.RemoveAll(obj=>!obj.Blocks&&!BaseEnvironment.Decorative(obj.Object));foreach(var obj in village.Objects)obj.Name="受损的"+obj.Name;
            village.Objects.Add(new BaseMapPlacement {Id="demo_home_world",Name="返回世界道路",X=23.3,Y=7,Width=1,Height=2});
            village.Objects.Add(new BaseMapPlacement {Id="demo_broken_sword",Name="断剑 · 调查",X=10,Y=7,Width=1,Height=1});campaign.Layouts.Add(village);
            if(BaseDemoRelease.Enabled(state))
            {
                village.Objects.Add(new BaseMapPlacement {Id="demo_sword_prop",Name="断裂的王室式长剑",X=10.5,Y=7.5,Width=1.4,Height=1.8,Kind="demo-prop",AssetId="demo-broken-sword",VisualOnly=true});
                village.Objects.Add(new BaseMapPlacement {Id="demo_survivor",Name="幸存村民",X=8.8,Y=7.5,Width=.5,Height=.5,Kind="npc",AssetId="ahe"});
                int debris=0;foreach(var building in village.Objects.Where(o=>o.Blocks&&!BaseEnvironment.Decorative(o.Object)).ToArray())village.Objects.Add(new BaseMapPlacement {Id="demo_damage_"+debris++,Name="烧毁的残片",X=building.X,Y=building.Y-building.Height/2+.5,Width=1.6,Height=1,Kind="demo-prop",AssetId="demo-debris",VisualOnly=true});
            }
            foreach(var map in campaign.Layouts)foreach(var obj in map.Objects)if(string.IsNullOrEmpty(obj.SourceObjectId))obj.SourceObjectId=map.SourceMapId+"_"+obj.Id;
            var campBoard=CreateCampBoard(state);
            boards.Add(new BaseRegionMap {Id=Camp,Board=campBoard});var homeBoard=BaseWorldMap.CreateWorld();if(BaseEnvironment.Active(state))BaseEnvironment.Populate(village,homeBoard);boards.Add(new BaseRegionMap {Id=Home,Board=homeBoard});boards.Add(new BaseRegionMap {Id="demo_world",Board=BaseWorldMap.CreateWorld()});campaign.RegionMaps=boards;
            var site=campaign.EnemySites.Single();string oldSite=site.Id;site.Id=Camp;site.Name=BaseScriptNames.Bandit;
            if(state.Demo.Encounters?.Version==2)
            {
                var preset=state.Demo.Encounters.Camp;var layout=campaign.Layouts.Single(map=>map.SourceMapId==Camp);layout.Enemy=preset.Copy();
                preset.ValidateBoard(campBoard);BaseThreats.InstallPreset(site,preset);
                if(state.Demo.Defense.MainForce?.Count>0){site.Garrison=state.Demo.Defense.MainForce.Select(unit=>unit.Copy()).ToList();site.NextUnitNumber=Math.Max(site.NextUnitNumber,site.Garrison.Count+1);}
            }
            foreach(var mission in campaign.Events.Where(mission=>mission.SourceSiteId==oldSite))mission.SourceSiteId=Camp;
            foreach(var loss in state.Aftermath.LostItems.Where(loss=>loss.SourceSiteId==oldSite))loss.SourceSiteId=Camp;
            campaign.Events.Single(mission=>mission.Id==site.AttackEventId).Title="攻占"+BaseScriptNames.Bandit;
            foreach(var leader in state.Leaders.Where(leader=>leader.HasMapPosition)){leader.MapId=keys[BaseAdventure.Region(leader)];if(leader.MapRegion=="outpost"||leader.MapRegion=="dungeon"||leader.MapRegion==BaseBanditHideout.Region)leader.MapRegion="custom";}
            foreach(var paused in state.Adventure.Pauses){paused.MapId=keys[paused.Region];if(paused.Region=="outpost"||paused.Region=="dungeon"||paused.Region==BaseBanditHideout.Region)paused.Region="custom";paused.AreaId="camp";}
            foreach(var guest in state.Inn.Guests)if(guest.AreaId=="demo-local")guest.AreaId="camp";
            if(state.Demo.TerrainMemories!=null)foreach(var memory in state.Demo.TerrainMemories)if(keys.ContainsKey(memory.MapId))memory.MapId=keys[memory.MapId];
            campaign.InstanceVersion=1;campaign.DesignId=state.Demo.BalanceHash;campaign.CurrentMapId="demo_camp";
            campaign.DesignLinks.Clear();
            Bind(state,"demo_camp","exit_east","demo_town","travel_base",2,6.5);Bind(state,"demo_town","travel_base","demo_camp","exit_east",22.4,6.5);
            Bind(state,"demo_town","demo_ruins_exit","demo_ruins","demo_ruins_return",2,7.5);Bind(state,"demo_ruins","demo_ruins_return","demo_town","demo_ruins_exit",22.4,6.5);
            Bind(state,"demo_camp","demo_defense_exit","demo_field","demo_defense_return",22,6.5);Bind(state,"demo_field","demo_defense_return","demo_camp","demo_defense_exit",1.6,6.5);
            BaseSceneInstances.Layout(state,"perimeter").Objects.Single(obj=>obj.Id=="demo_defense_start").VisualOnly=true;
            var world=new BaseWorldJourneyState {Version=1,NodeId="camp"};campaign.WorldJourney=world;
            world.Nodes.Add(new BaseWorldNode {Id="camp",Title="新领地营地",X=.18,Y=.38,MapIds=new List<string>{"demo_camp","demo_town","demo_field","demo_ruins"}});
            if(BaseSceneRules.Active(state))world.Nodes.Single(n=>n.Id=="camp").MapIds.Add(BaseSceneRules.DungeonId);
            if(BaseBanditHideout.Installed(state))world.Nodes.Single(n=>n.Id=="camp").MapIds.Add(BaseBanditHideout.MapId);
            world.Nodes.Add(new BaseWorldNode {Id="bandit",AreaId="home",Title=BaseScriptNames.Village+"领地 · 敌营",X=.5,Y=.58,MapIds=new List<string>{Camp}});
            world.Nodes.Add(new BaseWorldNode {Id="home",AreaId="home",Title=BaseScriptNames.Village+"领地 · 村庄",X=.82,Y=.4,MapIds=new List<string>{Home}});
            world.Roads.Add(new BaseWorldRoad {Id="camp_bandit",From="camp",To="bandit",Days=1});world.Roads.Add(new BaseWorldRoad {Id="bandit_home",From="bandit",To="home",Days=1,EnemyMapId=Camp});
            Entry(state,world,"camp_entry","camp","demo_camp","demo_march_exit",12,1.5);Entry(state,world,"bandit_entry","bandit",Camp,"demo_camp_world",1.6,6.5);Entry(state,world,"home_entry","home",Home,"demo_home_world",22.4,7);
            state.Demo.Finale.WorldOpened=true;BaseAdventure.Arrive(state);BaseEconomy.Validate(state);
        }
        private static void Bind(BaseState state,string from,string marker,string to,string target,double x,double y)
        {
            var source=BaseSceneInstances.Layout(state,from).Objects.Single(obj=>obj.Id==marker);source.DefinitionId="map_exit";
            var landing=BaseSceneInstances.Layout(state,to);
            var entrance=BaseSceneInstances.Layout(state,to).Objects.Single(obj=>obj.Id==target);entrance.DefinitionId="map_exit";
            if(BaseDemoRelease.Enabled(state)){var safe=BaseDemoRelease.Landing(state,to,entrance);x=safe.X;y=safe.Y;}
            state.Campaign.DesignLinks.Add(new BaseDesignLink {SourceMapId=from,SourceObjectId=source.SourceObjectId,TargetMapId=to,TargetObjectId=entrance.SourceObjectId,Mode="direct",X=x,Y=y});
        }
        private static void Entry(BaseState state,BaseWorldJourneyState world,string id,string node,string map,string objectId,double x,double y)
        {
            var landing=BaseSceneInstances.Layout(state,map);
            var marker=BaseSceneInstances.Layout(state,map).Objects.Single(obj=>obj.Id==objectId);marker.DefinitionId="map_exit";marker.VisualOnly=false;marker.Name="世界道路";
            if(BaseDemoRelease.Enabled(state)){var safe=BaseDemoRelease.Landing(state,map,marker);x=safe.X;y=safe.Y;}
            world.Entries.Add(new BaseWorldEntry {Id=id,NodeId=node,MapId=map,ObjectId=marker.SourceObjectId,X=x,Y=y});world.Discovered.Add(id);
        }
        public static BaseCampaignSession Begin(BaseState state,string mode)
            => Begin(state, mode, null);

        internal static BaseCampaignSession Begin(BaseState state, string mode, BasePreparedRoster roster)
        {
            if(!Opened(state)||state.Demo.Stage!=BaseDemoStage.DefenseWon||state.Campaign.CurrentMapId!=Camp||!BaseInteraction.At(state,"outpost","demo_camp_attack"))throw new InvalidOperationException("请率真实出征队到土匪营地进攻点，不能遥控或重复进攻");
            var army=BaseThreats.Site(state,Camp).Capacity.Clone();army.CaptainSlot=0;army.Commanders=Enumerable.Range(0,army.HeroCount).Select(slot=>new CommanderLoadout()).ToList();
            var document=new BattleScenarioDocument {Scenario=new BattleScenario {Board=BaseBattlefield.Board(state).Clone(),A=army.Clone(),B=army,MaxRounds=90}};
            return new BaseCampaignSession(BaseCampaign.Begin(state,BaseThreats.Site(state,Camp).AttackEventId,document,mode,null,true,roster));
        }
        public static void Configure(RealtimeCampaignBattle run)
        {run.OutcomeResolver=current=>BaseDemoCombat.Defeated(current)?BattleOutcome.SideBWin:current.Battle.RealtimeVictorySatisfied||!current.Model.Units.Any(unit=>unit.Side==PrototypeSide.Enemy&&unit.Alive)?BattleOutcome.SideAWin:current.Model.Elapsed>=90?BattleOutcome.RoundLimit:BattleOutcome.None;}
        public static bool Complete(BaseState state,RealtimeCampaignBattle run)
        {
            if(!BattleActive(state)||!Encounter(run.Session)||state.Campaign.ActiveBattle.Id!=run.Session.Opening.Id||run.Battle.Phase!=BattlePhase.Finished||run.Battle.Report.Outcome!=BattleOutcome.SideAWin)return false;
            run.SaveCheckpoint(state);if(!BaseCampaign.Settle(state,run.Session))return false;
            state.Demo.Stage=BaseDemoStage.AttackWon;state.Demo.Finale.AttackDay=state.Day;state.Demo.Evidence.Add(AttackProof);BaseDemoDefense.CancelSecond(state,"camp_captured");ConnectHome(state);
            foreach(var manifest in state.Aftermath.Loot.Where(item=>item.BattleId==run.Session.Opening.Id))if(BaseBattleLoot.CanFinish(state,manifest))BaseBattleLoot.Finish(state,manifest.BattleId);
            BaseEconomy.Validate(state);return true;
        }
        private static void ConnectHome(BaseState s)
        {
            if(!BaseSceneRules.Active(s))return;
            var camp=BaseSceneInstances.Layout(s,Camp);var home=BaseSceneInstances.Layout(s,Home);
            if(camp.Objects.Any(o=>o.Id=="demo_direct_home"))return;
            var campPoint=BaseSpatial.Nearest(s,new BasePoint(camp.Columns-2,12),Camp);var homePoint=BaseSpatial.Nearest(s,new BasePoint(1.5,7.5),Home);
            camp.Objects.Add(new BaseMapPlacement{Id="demo_direct_home",Name="前往"+BaseScriptNames.Village+" · 领地内直达",SourceObjectId="demo_direct_home",DefinitionId="map_exit",X=campPoint.X,Y=campPoint.Y,Width=.6,Height=1});
            home.Objects.Add(new BaseMapPlacement{Id="demo_direct_camp",Name="返回旧敌营 · 领地内直达",SourceObjectId="demo_direct_camp",DefinitionId="map_exit",X=homePoint.X,Y=homePoint.Y,Width=.6,Height=1});
            Bind(s,Camp,"demo_direct_home",Home,"demo_direct_camp",homePoint.X,homePoint.Y);Bind(s,Home,"demo_direct_camp",Camp,"demo_direct_home",campPoint.X,campPoint.Y);
        }
        public static bool Investigate(BaseState state)
        {
            if(!Opened(state)||state.Demo.Stage!=BaseDemoStage.AttackWon||state.Demo.Finale.ReconstructionVersion!=0||state.Campaign.CurrentMapId!=Home||BaseBattleLoot.Pending(state)||!BaseInteraction.At(state,"custom","demo_broken_sword"))return false;
            state.Demo.Evidence.Add(Sword);state.Demo.Finale.ReconstructionVersion=1;state.Demo.Finale.RebuildDay=state.Day;return true;
        }
        private static readonly string[] RebuildIds={"demo_rebuild_farm","demo_rebuild_tent","demo_rebuild_warehouse"};
        private static readonly string[] RebuildNames={"小粮田","营帐","仓库"};
        private static readonly string[] RebuildAssets={"farm-small","camp-tent","warehouse"};
        internal static string RebuildName(int item)=>RebuildNames[item];
        private static bool RebuildRecordsValid(BaseState state)
        {
            var objects=BaseSceneInstances.Layout(state,Home).Objects;var placed=objects.Where(o=>RebuildIds.Contains(o.Id)).ToArray();
            for(int i=0;i<3;i++)if(placed.Count(o=>o.Id==RebuildIds[i])!=((state.Demo.Finale.RebuildMask&(1<<i))!=0?1:0))return false;
            if(placed.Any(o=>o.Blocks||!o.VisualOnly||o.Kind!="facility"||o.Width!=1||o.Height!=1||o.X!=Math.Floor(o.X)+.5||o.Y!=Math.Floor(o.Y)+.5||!BaseSpatial.Geometry(state,o.X,o.Y,Home)||!BaseBattlefield.Board(state,Home).Walkable(new GridPosition((int)o.X,(int)o.Y))))return false;
            return placed.Select(o=>o.X+":"+o.Y).Distinct().Count()==placed.Length;
        }
        public static string RebuildProblem(BaseState state,int item,int x,int y)
        {
            if(!Rebuilding(state)||state.Campaign.CurrentMapId!=Home)return "请在返乡村庄重建";
            if(item<0||item>=RebuildIds.Length)return "未知重建项目";
            if((state.Demo.Finale.RebuildMask&(1<<item))!=0)return "该设施已放置";
            if(BaseSceneInstances.Layout(state,Home).Objects.Count>=(state.Demo.StoryScene?.Valid==true?300:200))return "场景物件数量已满";
            var board=BaseBattlefield.Board(state,Home);var cell=board.Cell(new GridPosition(x,y));double px=x+.5,py=y+.5;
            var objects=BaseSpatial.Objects(state,Home);
            if(cell==null||!cell.Walkable||!BaseSpatial.Geometry(objects,px,py,board.Width,board.Height))return "请选择可通行空地";
            if(objects.Any(o=>(o.Blocks||!o.VisualOnly||RebuildIds.Contains(o.Id))&&o.Distance(px,py)<.5)||Math.Abs(state.PlayerX-px)<.8&&Math.Abs(state.PlayerY-py)<.8)return "不能覆盖设施或人物";
            return "";
        }
        public static bool PlaceRebuild(BaseState state,int item,int x,int y)
        {
            if(RebuildProblem(state,item,x,y)!="")return false;
            var layout=BaseSceneInstances.Layout(state,Home);string id=RebuildIds[item];
            layout.Objects.Add(new BaseMapPlacement{Id=id,SourceObjectId=id,Name=RebuildName(item),AssetId=RebuildAssets[item],Kind="facility",X=x+.5,Y=y+.5,Width=1,Height=1,VisualOnly=true});
            state.Demo.Finale.RebuildMask|=1<<item;return true;
        }
        public static bool RemoveRebuild(BaseState state,int item)
        {
            if(!Rebuilding(state)||state.Campaign.CurrentMapId!=Home||item<0||item>=RebuildIds.Length||(state.Demo.Finale.RebuildMask&(1<<item))==0)return false;
            var objects=BaseSceneInstances.Layout(state,Home).Objects;var selected=objects.SingleOrDefault(o=>o.Id==RebuildIds[item]);if(selected==null)return false;
            objects.Remove(selected);state.Demo.Finale.RebuildMask&=~(1<<item);return true;
        }
        public static bool FinishRebuild(BaseState state)
        {
            if(!Rebuilding(state)||state.Campaign.CurrentMapId!=Home||state.Demo.Finale.RebuildMask!=7||state.Day!=state.Demo.Finale.RebuildDay||!RebuildRecordsValid(state))return false;
            state.Demo.Finale.VillageRebuilt=true;state.Demo.Finale.EndDay=state.Day;state.Demo.Stage=BaseDemoStage.Finished;return true;
        }
        public static void Validate(BaseState state)
        {
            var progress=state.Demo.Finale;
            if(progress==null){if(Later(state))throw new ArgumentException("结尾阶段缺少快照");return;}
            bool storyCamp=state.Demo?.StoryScene?.Valid==true;
            if(progress.Version!=1||progress.CampBoard==null||(storyCamp?(progress.CampBoard.Width<10||progress.CampBoard.Width>64||progress.CampBoard.Height<10||progress.CampBoard.Height>64):(progress.CampBoard.Width!=(BaseDemoRelease.Enabled(state)?32:24)||progress.CampBoard.Height!=(BaseDemoRelease.Enabled(state)?24:14)))||progress.CampBoard.Cells?.Length!=progress.CampBoard.Width*progress.CampBoard.Height||progress.CampBoard.Cells.Any(cell=>cell==null||!cell.MetadataValid||!Enum.IsDefined(typeof(TerrainKind),cell.Terrain)||!Enum.IsDefined(typeof(DeploymentZone),cell.Zone))||progress.Village==null||progress.Village.Objects==null||!progress.Village.Objects.Any(obj=>obj.Id=="village_house"&&obj.Blocks)||!progress.Village.Objects.Any(obj=>(obj.Id=="intro_farm"||obj.Id=="village_field")&&obj.Blocks))throw new ArgumentException("Demo攻营／"+BaseScriptNames.Village+"快照非法");
            if(!progress.WorldOpened){if(progress.AttackDay!=0||progress.EndDay!=0||progress.ReconstructionVersion!=0||progress.RebuildDay!=0||progress.RebuildMask!=0||progress.VillageRebuilt||Later(state))throw new ArgumentException("未开启行军不能提交结尾");return;}
            if(!BaseDemoDefense.Won(state)||state.Campaign.InstanceVersion!=1||state.Campaign.DesignId!=state.Demo.BalanceHash||!BaseWorldJourney.Active(state)||state.Campaign.Layouts.Count!=(BaseSceneRules.Active(state)?8:7)+(BaseBanditHideout.Installed(state)?1:0)||state.Campaign.WorldJourney.Nodes.Count!=3||state.Campaign.WorldJourney.Roads.Count!=2||state.Campaign.WorldJourney.Roads.Any(road=>road.Days!=1))throw new ArgumentException("Demo世界身份、路线或阶段非法");
            var site=BaseThreats.Site(state,Camp);var home=state.Campaign.Layouts.Single(map=>map.SourceMapId==Home);
            if(site==null||home.Region!="custom")throw new ArgumentException("Demo敌营／"+BaseScriptNames.Village+"缺失");
            var survivor=home.Objects.SingleOrDefault(o=>o.Id=="demo_survivor");
            if(survivor!=null)survivor.VisualOnly=false;
            var sword=home.Objects.SingleOrDefault(o=>o.Id=="demo_broken_sword");
            if(sword?.Name=="幸存者与断剑 · 调查")sword.Name="断剑 · 调查";
            var tent=home.Objects.SingleOrDefault(o=>o.Id==RebuildIds[1]);
            if(tent?.AssetId=="home")tent.AssetId=RebuildAssets[1];
            if(Later(state)?progress.AttackDay<state.Demo.Defense.VictoryDay||progress.AttackDay>state.Day||site.Owner!="player"||!state.Campaign.Reports.Any(report=>report.EventId==site.AttackEventId&&report.Outcome=="SideAWin"&&!report.Passive):progress.AttackDay!=0)throw new ArgumentException("攻营阶段缺少真实主动胜利");
            if(state.Demo.Stage==BaseDemoStage.Finished?progress.EndDay<progress.AttackDay||progress.EndDay>state.Day:progress.EndDay!=0)throw new ArgumentException("断剑记录日期非法");
            if(progress.ReconstructionVersion<0||progress.ReconstructionVersion>1||progress.RebuildMask<0||progress.RebuildMask>7||progress.ReconstructionVersion==0&&(progress.RebuildDay!=0||progress.RebuildMask!=0||progress.VillageRebuilt)||progress.ReconstructionVersion==1&&(progress.RebuildDay<progress.AttackDay||progress.RebuildDay>state.Day||!state.Demo.Evidence.Contains(Sword)||progress.VillageRebuilt!=(state.Demo.Stage==BaseDemoStage.Finished)||progress.VillageRebuilt&&progress.RebuildMask!=7||!progress.VillageRebuilt&&state.Day!=progress.RebuildDay))throw new ArgumentException("返乡重建状态非法");
            if(progress.ReconstructionVersion==1&&!RebuildRecordsValid(state))throw new ArgumentException("返乡设施与重建记录不一致");
        }
    }
}

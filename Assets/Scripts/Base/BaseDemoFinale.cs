using System;
using System.Collections.Generic;
using System.Linq;
using BaseConquest.Battle;
using BaseConquest.RealtimePrototype;
using BaseConquest.Story;

namespace BaseConquest.Base
{
    [Serializable] public sealed class BaseDemoFinaleProgress
    {
        public int Version, AttackDay, EndDay,RebuildMask;
        public bool WorldOpened,AttackBriefingSeen,VillageRebuilt,VolunteersArrived,EndingSeen;
        public int HouseDialogueStep;
        public bool CampTakenByDefense, CampSuppliesTaken;
        public BattleBoard CampBoard, VillageBoard;
        public BaseMapLayout Village;
        public BaseDemoFinaleProgress Copy()=>new BaseDemoFinaleProgress {HouseDialogueStep=HouseDialogueStep,EndingSeen=EndingSeen,VolunteersArrived=VolunteersArrived,Version=Version,AttackDay=AttackDay,EndDay=EndDay,RebuildMask=RebuildMask,VillageRebuilt=VillageRebuilt,WorldOpened=WorldOpened,AttackBriefingSeen=AttackBriefingSeen,CampTakenByDefense=CampTakenByDefense,CampSuppliesTaken=CampSuppliesTaken,CampBoard=CampBoard?.Clone(),VillageBoard=VillageBoard?.Clone(),Village=Village?.Copy()};
    }
    public static class BaseDemoFinale
    {
        public const string Camp="demo_bandit", Home="demo_home", HomeField="demo_home_field", AttackProof="demo:bandit:captured", Sword="demo:clue:broken-sword";
        public const string CampSuppliesMarker="demo_camp_supplies";
        internal static bool IsReturnField(string id)=>id=="intro_farm"||id=="village_field";
        public static bool Enabled(BaseState state)=>BaseDemo.Active(state)&&state.Demo.Finale?.Version==1;
        public static bool Opened(BaseState state)=>Enabled(state)&&state.Demo.Finale.WorldOpened;
        public static bool Later(BaseState state)=>state.Demo.Stage==BaseDemoStage.AttackWon||state.Demo.Stage==BaseDemoStage.Finished;
        public static bool OccupiedBySecondDefense(BaseState state)=>Enabled(state)&&state.Demo.Finale.CampTakenByDefense;
        public static bool SecondRaidReportPending(BaseState state)=>OccupiedBySecondDefense(state)&&!state.Demo.Defense.SecondVictoryReportSeen;
        public static bool AcknowledgeSecondRaidReport(BaseState state)
        {
            if(!SecondRaidReportPending(state))return false;
            state.Demo.Defense.SecondVictoryReportSeen=true;
            return true;
        }
        public static bool Rebuilding(BaseState state)=>Enabled(state)&&state.Demo.Stage==BaseDemoStage.AttackWon&&state.Campaign.CurrentMapId==Home;
        public const string BrokenSwordItem = "partner_broken_sword";
        public const string Builder = "demo_village_builder", Helper = "demo_village_helper";
        public static bool HomeVolunteers(BaseState s) => s?.Demo?.Finale?.VolunteersArrived == true;
        private static int RepairBit(string id) => id=="home" ? 1 : id=="farm" ? 2 : id=="workshop" ? 4 : 0;
        public static bool HomeRepaired(BaseState s,string id)
        {
            if(s?.Demo?.Finale == null || !Opened(s)) return false;
            if(id=="barracks") return s.Campaign.Layouts.FirstOrDefault(m=>m.SourceMapId==Home)?.Objects.Any(o=>o.Id=="barracks")==true;
            return (s.Demo.Finale.RebuildMask & RepairBit(id))!=0;
        }
        internal static bool HomeObject(string map,string id) =>
            map==Home && (RepairBit(id)!=0 || id=="barracks" || id==Builder || id==Helper) ||
            map==HomeField && id=="demo_broken_sword";
        public static bool BattleActive(BaseState state)=>Opened(state)&&state.Campaign.CurrentMapId==Camp&&state.Campaign.HasActiveBattle&&state.Campaign.ActiveBattle.EventId==BaseThreats.Site(state,Camp).AttackEventId;
        public static bool Encounter(BaseCampaignSession session)=>session?.Opening.MapId==Camp;
        public static void PrepareOpening(BaseState state)
        {state.Demo.Finale=new BaseDemoFinaleProgress {Version=1,CampBoard=BaseBattlefield.Board(state,"outpost").Clone(),Village=BaseDemoPrologue.CreateVillage(),VillageBoard=BaseWorldMap.CreateWorld()};}
        public static void InstallMarker(BaseState state)
        {if(Enabled(state))state.Campaign.Layouts.Single(map=>map.Region=="base").Objects.Add(new BaseMapPlacement {Id="demo_march_exit",Name="世界道路 · 首次防守后开放",X=12,Y=.7,Width=2,Height=1,VisualOnly=true});}
        public static string Objective(BaseState s)
        {
            if(!Enabled(s)) return "";
            if(s.Demo.Stage==BaseDemoStage.Finished) return "白桦村已重新安顿";
            if(Rebuilding(s) && s.Demo.Finale.VillageRebuilt) return "在村中的长椅坐一会儿";
            if(Rebuilding(s)) return s.Demo.Finale.RebuildMask==7 ? "与麦田边的村民交谈" : "修好家、粮田和工坊";
            return Later(s) ? "沿世界道路返回白桦村" : "从灰岩营地南口率队前往黑鸦寨";
        }
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
            if(!Enabled(state)||Opened(state)||state.Demo.Stage!=BaseDemoStage.DefenseWon&&!OccupiedBySecondDefense(state)||state.Demo.Defense.BatuStoryDay==0||BaseBattleLoot.Pending(state)||!BaseInteraction.At(state,"base","demo_march_exit")||state.Muster.Parties.Count>0)throw new InvalidOperationException("先处理战果并返营整备，到南侧世界出口确认");
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
            var story=state.Demo.StoryScene;
            var campLayout=story?.Valid==true?story.Map("bandit").Layout.Copy():BaseDemoRelease.Enabled(state)?state.Demo.ReleaseMaps.Single(m=>m.Role=="bandit").Layout.Copy():CreateCampLayout();
            campLayout.SourceMapId=Camp;
            var campBoard=CreateCampBoard(state);
            var westRoad=campLayout.Objects.Single(o=>o.Id=="demo_camp_world");
            var eastRoad=FreeNear(campLayout,campBoard,campBoard.Width-1,westRoad.Y);
            campLayout.Objects.Add(Marker("demo_camp_east","东侧世界道路",eastRoad));
            campaign.Layouts.Add(campLayout);
            var village = story?.Valid==true ? story.ReturnVillage() : state.Demo.Finale.Village.Copy();
            var savedVillage=state.Demo.Finale.VillageBoard;
            var homeBoard = story?.Valid==true ? story.Map("village").Board.Clone() :
                savedVillage?.Cells?.Length>0 ? savedVillage.Clone() : BaseWorldMap.CreateWorld();
            if(story?.Valid!=true) PrepareVillage(village,homeBoard);
            campaign.Layouts.Add(village);
            var fieldTemplate = story?.Valid==true ? story.Map("field").Layout : campaign.Layouts.Single(m=>m.Region=="perimeter");
            var westBoard = story?.Valid==true ? story.Map("field").Board.Clone() : campaign.PerimeterBoard.Clone();
            var westGate = CreateWestGate(fieldTemplate,westBoard);
            campaign.Layouts.Add(westGate);
            foreach(var map in campaign.Layouts)foreach(var obj in map.Objects)if(string.IsNullOrEmpty(obj.SourceObjectId))obj.SourceObjectId=map.SourceMapId+"_"+obj.Id;
            boards.Add(new BaseRegionMap {Id=Camp,Board=campBoard});
            boards.Add(new BaseRegionMap {Id=Home,Board=homeBoard});
            boards.Add(new BaseRegionMap {Id=HomeField,Board=westBoard});
            boards.Add(new BaseRegionMap {Id="demo_world",Board=BaseWorldMap.CreateWorld()});campaign.RegionMaps=boards;
            var site=campaign.EnemySites.Single();string oldSite=site.Id;site.Id=Camp;site.Name=BaseScriptNames.Bandit;
            if(state.Demo.Encounters?.Version==2)
            {
                var preset=state.Demo.Encounters.Camp;var layout=campaign.Layouts.Single(map=>map.SourceMapId==Camp);layout.Enemy=preset.Copy();
                preset.ValidateBoard(campBoard);
                if(site.Owner=="enemy")
                {
                    BaseThreats.InstallPreset(site,preset);
                    if(state.Demo.Defense.MainForce?.Count>0){site.Garrison=state.Demo.Defense.MainForce.Select(unit=>unit.Copy()).ToList();site.NextUnitNumber=Math.Max(site.NextUnitNumber,site.Garrison.Count+1);}
                    BaseEnemySupply.Assign(state.Demo.Rpg,"bandit",site.Garrison);
                }
            }
            foreach(var mission in campaign.Events.Where(mission=>mission.SourceSiteId==oldSite))mission.SourceSiteId=Camp;
            foreach(var loss in state.Aftermath.LostItems.Where(loss=>loss.SourceSiteId==oldSite))loss.SourceSiteId=Camp;
            campaign.Events.Single(mission=>mission.Id==site.AttackEventId).Title="攻占"+BaseScriptNames.Bandit;
            foreach(var leader in state.Leaders.Where(leader=>leader.HasMapPosition)){leader.MapId=keys[BaseAdventure.Region(leader)];if(leader.MapRegion=="outpost"||leader.MapRegion=="dungeon"||leader.MapRegion==BaseBanditHideout.Region)leader.MapRegion="custom";}
            foreach(var paused in state.Adventure.Pauses){paused.MapId=keys[paused.Region];if(paused.Region=="outpost"||paused.Region=="dungeon"||paused.Region==BaseBanditHideout.Region)paused.Region="custom";paused.AreaId="camp";}
            if(state.Demo.TerrainMemories!=null)foreach(var memory in state.Demo.TerrainMemories)if(keys.ContainsKey(memory.MapId))memory.MapId=keys[memory.MapId];
            campaign.InstanceVersion=1;campaign.DesignId=state.Demo.BalanceHash;campaign.CurrentMapId="demo_camp";
            campaign.DesignLinks.Clear();
            Bind(state,"demo_camp","exit_east","demo_town","travel_base",2,6.5);Bind(state,"demo_town","travel_base","demo_camp","exit_east",22.4,6.5);
            Bind(state,"demo_town","demo_ruins_exit","demo_ruins","demo_ruins_return",2,7.5);Bind(state,"demo_ruins","demo_ruins_return","demo_town","demo_ruins_exit",22.4,6.5);
            Bind(state,"demo_camp","demo_defense_exit","demo_field","demo_defense_return",22,6.5);Bind(state,"demo_field","demo_defense_return","demo_camp","demo_defense_exit",1.6,6.5);
            var world=new BaseWorldJourneyState {Version=1,NodeId="camp"};campaign.WorldJourney=world;
            world.Nodes.Add(new BaseWorldNode {Id="camp",Title=BaseScriptNames.Camp,X=.18,Y=.38,MapIds=new List<string>{"demo_camp","demo_town","demo_field","demo_ruins"}});
            if(BaseSceneRules.Active(state))world.Nodes.Single(n=>n.Id=="camp").MapIds.Add(BaseSceneRules.DungeonId);
            if(BaseBanditHideout.Installed(state))world.Nodes.Single(n=>n.Id=="camp").MapIds.Add(BaseBanditHideout.MapId);
            world.Nodes.Add(new BaseWorldNode {Id="bandit",AreaId="bandit",Title=BaseScriptNames.Bandit,X=.5,Y=.58,MapIds=new List<string>{Camp}});
            world.Nodes.Add(new BaseWorldNode {Id="home",AreaId="home",Title=BaseScriptNames.Village,X=.82,Y=.4,MapIds=new List<string>{HomeField,Home}});
            world.Roads.Add(new BaseWorldRoad {Id="camp_bandit",From="camp",To="bandit",Days=1});world.Roads.Add(new BaseWorldRoad {Id="bandit_home",From="bandit",To="home",Days=1,EnemyMapId=Camp});
            Entry(state,world,"camp_entry","camp","demo_camp","demo_march_exit",12,1.5);
            Entry(state,world,"bandit_entry","bandit",Camp,"demo_camp_world",1.6,6.5);
            var eastVillage=village.Objects.Single(o=>o.Id=="demo_home_world");
            Entry(state,world,"bandit_east_entry","bandit",Camp,"demo_camp_east",eastRoad.X-1,eastRoad.Y);
            Entry(state,world,"home_entry","home",Home,"demo_home_world",eastVillage.X-1,eastVillage.Y);
            Bind(state,HomeField,"village_gate_in",Home,"village_gate_out",village.SpawnX,village.SpawnY);
            Bind(state,Home,"village_gate_out",HomeField,"village_gate_in",westGate.Objects.Single(o=>o.Id=="village_gate_in").X-1,westGate.Objects.Single(o=>o.Id=="village_gate_in").Y);
            state.Demo.Finale.WorldOpened=true;
            if(OccupiedBySecondDefense(state))
            {
                ShowOccupiedCamp(state);
            }
            BaseAdventure.Arrive(state);BaseEconomy.Validate(state);
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
            var marker=BaseSceneInstances.Layout(state,map).Objects.Single(obj=>obj.Id==objectId);marker.DefinitionId="map_exit";marker.VisualOnly=false;marker.Name=map==Camp ? (objectId=="demo_camp_east"?"东口 · 世界道路 →":"← 世界道路 · 西口") : "世界道路";
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
            RecordCampCapture(state,state.Day);BaseDemoDefense.CancelSecond(state,"camp_captured");
            foreach(var manifest in state.Aftermath.Loot.Where(item=>item.BattleId==run.Session.Opening.Id))if(BaseBattleLoot.CanFinish(state,manifest))BaseBattleLoot.Finish(state,manifest.BattleId);
            BaseEconomy.Validate(state);return true;
        }
        private static BaseEnemySite CampSite(BaseState state)=>BaseThreats.Site(state,Opened(state)?Camp:"outpost");

        private static BaseCampaignReport SecondDefenseVictory(BaseState state)=>state.Campaign.Reports.LastOrDefault(report=>report.EventId==BaseDemoDefense.SecondEventId&&report.Outcome=="SideAWin"&&!report.Passive);

        internal static void Normalize(BaseState state)
        {
            if(!Enabled(state))return;
            if(Opened(state))
            {
                var start=BaseSceneInstances.Layout(state,"perimeter").Objects.Single(obj=>obj.Id=="demo_defense_start");
                start.VisualOnly=!BaseDemoDefense.SecondDue(state);
            }
            if(Later(state))
            {
                if(Opened(state))ShowOccupiedCamp(state);
                return;
            }
            if(state.Demo.Stage!=BaseDemoStage.DefenseWon||state.Demo.Defense?.SecondOutcome!="defended"||state.Campaign.HasActiveBattle||state.Aftermath.GameOver)return;
            var victory=SecondDefenseVictory(state);
            var site=CampSite(state);
            if(victory!=null&&site?.Owner=="enemy"&&!state.Campaign.Reports.Any(report=>report.EventId==site.AttackEventId&&report.Day>=victory.Day))
                OccupyAfterSecondDefense(state,victory.Day);
        }

        internal static void OccupyAfterSecondDefense(BaseState state,int day)
        {
            var site=CampSite(state);
            var attack=state.Campaign.Events.Single(mission=>mission.Id==site.AttackEventId);
            site.Owner="player";
            site.CapturedDay=day;
            site.Garrison.Clear();
            attack.Status="resolved";
            attack.EnemyArmy.Clear();
            state.Demo.Finale.CampTakenByDefense=true;
            var baseMap=state.Campaign.Layouts.Single(map=>map.Region=="base");
            baseMap.Objects.Single(obj=>obj.Id=="demo_defense_exit").Name="← 迎敌战场 · 已守住";
            if(!Opened(state))baseMap.Objects.Single(obj=>obj.Id=="demo_march_exit").Name="世界道路 · 返乡";
            RecordCampCapture(state,day);
        }

        private static void RecordCampCapture(BaseState state,int day)
        {
            state.Demo.Stage=BaseDemoStage.AttackWon;
            state.Demo.Finale.AttackDay=day;
            state.Demo.Evidence.Add(AttackProof);
            if(Opened(state))
            {
                ShowOccupiedCamp(state);
            }
        }

        private static void ShowOccupiedCamp(BaseState state)
        {
            var world=state.Campaign.WorldJourney;
            world.Nodes.Single(node=>node.Id=="bandit").Title=BaseScriptNames.Bandit+" · 已占领";
            var camp=BaseSceneInstances.Layout(state,Camp);
            camp.Title=BaseScriptNames.Bandit;
            var marker=camp.Objects.SingleOrDefault(obj=>obj.Id=="demo_camp_attack");
            if(marker==null)return;
            if(!OccupiedBySecondDefense(state))
            {
                camp.Objects.Remove(marker);
                return;
            }
            marker.Id=CampSuppliesMarker;
            marker.SourceObjectId=Camp+"_"+CampSuppliesMarker;
            marker.Name=StoryText.Get("story.ui.finale.camp_supplies_marker","搜查剩余物资");
        }

        public static bool ClaimCampSupplies(BaseState state)
        {
            if(!OccupiedBySecondDefense(state)||!Opened(state)||state.Demo.Finale.CampSuppliesTaken||state.Campaign.CurrentMapId!=Camp||BaseBattleLoot.Pending(state)||state.Aftermath.GameOver||!BaseInteraction.At(state,"outpost",CampSuppliesMarker))return false;
            var attack=state.Campaign.Events.Single(mission=>mission.Id==CampSite(state).AttackEventId);
            var recovered=new BaseCampaignReport();
            BaseCampaign.ApplyEventInventory(state,attack,true,recovered);
            BaseItemRecovery.ReturnForEvent(state,attack.Id,recovered);
            state.Demo.Finale.CampSuppliesTaken=true;
            BaseSceneInstances.Layout(state,Camp).Objects.RemoveAll(obj=>obj.Id==CampSuppliesMarker);
            return true;
        }
        private static BasePoint FreeNear(BaseMapLayout map, BattleBoard board, double x, double y)
        {
            var objects = map.Objects.Select(o=>o.Object).ToArray();
            return board.Positions().Where(p=>board.Walkable(p))
                .Select(p=>new BasePoint(p.X+.5,p.Z+.5))
                .Where(p=>BaseSpatial.Geometry(objects,p.X,p.Y,board.Width,board.Height))
                .OrderBy(p=>p.Distance(new BasePoint(x,y))).First();
        }
        private static BaseMapPlacement Marker(string id,string name,BasePoint p,string kind=null,string art=null)
            => new BaseMapPlacement { Id=id,Name=name,X=p.X,Y=p.Y,Width=.6,Height=.6,Kind=kind,AssetId=art };
        internal static void PrepareVillage(BaseMapLayout map,BattleBoard board)
        {
            map.SourceMapId=Home;map.Region="custom";map.ParentMapId=null;map.Title="白桦村";map.ForbidSoldiers=true;
            var escape=map.Objects.FirstOrDefault(o=>o.Id=="prologue_trigger");
            map.Objects.RemoveAll(o=>BaseStoryScenePackage.IsRetiredVillageNpc(o)||o.Id=="demo_broken_sword"||o.Id=="demo_sword_prop"||o.Id=="demo_home_world"||o.Id=="demo_captain"||
                o.Id=="intro_villager"||o.Id=="prologue_report"||o.Id=="prologue_trigger"||o.Kind=="npc");
            foreach(var o in map.Objects)
            {
                if(o.Id=="village_house") o.Id="home";
                else if(IsReturnField(o.Id)) o.Id="farm";
                else if(o.Id=="intro_smith") o.Id="workshop";
                if(RepairBit(o.Id)!=0)
                {
                    o.Kind="facility";o.VisualOnly=false;
                    o.Name=o.Id=="home"?"家 · 修理":o.Id=="farm"?"粮田 · 修理":"工坊 · 修理";
                }
                else o.VisualOnly=true;
                o.SourceObjectId=Home+"_"+o.Id;
            }
            if(!map.Objects.Any(o=>o.Id=="workshop"))
                map.Objects.Add(new BaseMapPlacement{Id="workshop",Name="工坊 · 修理",X=16,Y=10,Width=2,Height=1,Blocks=true,Kind="facility",AssetId="workshop-small"});
            var exit=FreeNear(map,board,1,board.Height*.5);
            var east=FreeNear(map,board,escape?.X??board.Width-1,escape?.Y??board.Height*.5);
            map.Objects.Add(Marker("demo_home_world","世界道路 →",east));
            if(!map.Objects.Any(o=>o.AssetId=="bench"&&o.Rotation==0))
            {
                var seat=FreeNear(map,board,board.Width*.38,board.Height*.65);
                map.Objects.Add(new BaseMapPlacement {Id="village_bench",Name="长椅",AssetId="bench",Kind="decoration",X=seat.X,Y=seat.Y,Width=1.1,Height=.4,Blocks=true,VisualOnly=true});
            }
            map.Objects.Add(Marker("village_gate_out","← 西桥头",exit));
            var spawn=FreeNear(map,board,exit.X+1,exit.Y);
            map.SpawnX=spawn.X;map.SpawnY=spawn.Y;
        }
        private static BaseMapLayout CreateWestGate(BaseMapLayout template,BattleBoard board)
        {
            var map=template.Copy();
            map.SourceMapId=HomeField;map.ParentMapId=Home;map.Region="custom";map.Title="白桦村外 · 西桥头";
            map.TacticalObjects.Clear();map.Enemy=null;map.Safety="safe";map.ForbidSoldiers=false;
            map.Objects.RemoveAll(o=>!BaseEnvironment.Decorative(o.Object));
            foreach(var o in map.Objects) o.SourceObjectId=HomeField+"_"+o.Id;
            var gate=FreeNear(map,board,board.Width-1,board.Height*.5);
            map.Objects.Add(Marker("village_gate_in","进入白桦村 →",gate));
            map.Objects.Add(Marker("demo_broken_sword","断剑 · 调查",FreeNear(map,board,gate.X-2,gate.Y+1),"demo-prop","demo-broken-sword"));
            var landing=FreeNear(map,board,gate.X-1,gate.Y);
            map.SpawnX=landing.X;map.SpawnY=landing.Y;
            return map;
        }
        public static bool Investigate(BaseState state)
        {
            if(!Opened(state)||!Later(state)||state.Campaign.CurrentMapId!=HomeField||state.Demo.Evidence.Contains(Sword)||
                !BaseInteraction.At(state,"custom","demo_broken_sword")) return false;
            BaseBackpack.Receive(state,BrokenSwordItem,1,state.PlayerLeaderId);
            state.Demo.Evidence.Add(Sword);
            BaseSceneInstances.Layout(state,HomeField).Objects.RemoveAll(o=>o.Id=="demo_broken_sword");
            return true;
        }
        internal static bool CanPayRepair(BaseState s)=>s.Inventory.Materials>=BaseDemoDefense.RepairMaterialCost(s)&&s.Inventory.Coins>=BaseDemoDefense.RepairCoinCost(s);
        public static bool BeginHouseTalk(BaseState s)
        {
            if(!Rebuilding(s)||HomeRepaired(s,"home")||!CanPayRepair(s)||!BaseInteraction.At(s,"custom","home"))return false;
            if(s.Demo.Finale.HouseDialogueStep==0)s.Demo.Finale.HouseDialogueStep=1;
            return true;
        }
        public static bool HearHouseConfession(BaseState s)
        {
            if(s.Demo.Finale.HouseDialogueStep!=1||!BaseInteraction.At(s,"custom","home"))return false;
            s.Demo.Finale.HouseDialogueStep=2;
            return true;
        }
        public static bool RepairHome(BaseState s,string id)
        {
            int bit=RepairBit(id);
            if(!Rebuilding(s)||bit==0||HomeRepaired(s,id)||(id=="home"&&s.Demo.Finale.HouseDialogueStep!=2)||!CanPayRepair(s)||!BaseInteraction.At(s,"custom",id))return false;
            var map=BaseSceneInstances.Layout(s,Home);
            var obj=map.Objects.Single(o=>o.Id==id);
            s.Inventory.Materials-=BaseDemoDefense.RepairMaterialCost(s);s.Inventory.Coins-=BaseDemoDefense.RepairCoinCost(s);
            obj.Name=id=="home"?"家":id=="farm"?"粮田":"工坊";
            var finale=s.Demo.Finale;finale.RebuildMask|=bit;
            if(id=="home")finale.HouseDialogueStep=0;
            if(finale.RebuildMask==7)
            {
                var gate=map.Objects.Single(o=>o.Id=="village_gate_out");
                var board=BaseBattlefield.Board(s,Home);
                for(int i=0;i<2;i++)
                {
                    var entry=FreeNear(map,board,gate.X+1,gate.Y+i);
                    var helper=Marker(i==0?Builder:Helper,"村民",entry,"npc","villager");
                    helper.SourceObjectId=Home+"_"+helper.Id;helper.VisualOnly=true;map.Objects.Add(helper);
                }
            }
            return true;
        }
        public static BasePoint VolunteerPost(BaseState s,int index)
        {
            var layout=BaseSceneInstances.Layout(s,Home);
            var field=layout.Objects.Single(o=>o.Id=="farm");
            return FreeNear(layout,BaseBattlefield.Board(s,Home),field.X+(index==0?-1:1),field.Y-field.Height*.5-1);
        }
        public static bool WelcomeVolunteers(BaseState s)
        {
            if(!Rebuilding(s)||s.Demo.Finale.RebuildMask!=7||HomeVolunteers(s))return false;
            var objects=BaseSceneInstances.Layout(s,Home).Objects;
            for(int i=0;i<2;i++)
            {
                var person=objects.Single(o=>o.Id==(i==0?Builder:Helper));
                if(new BasePoint(person.X,person.Y).Distance(VolunteerPost(s,i))>=.08)return false;
            }
            s.Demo.Finale.VolunteersArrived=true;
            objects.Single(o=>o.Id==Builder).VisualOnly=false;
            return true;
        }
        public static bool FinishRebuild(BaseState state)
        {
            if(!Rebuilding(state)||state.Demo.Finale.VillageRebuilt||!HomeVolunteers(state)||!BaseInteraction.At(state,"custom",Builder))return false;
            state.Demo.Finale.VillageRebuilt=true;return true;
        }
        public static string EndingBench(BaseState state)
        {
            if(!Rebuilding(state)||!state.Demo.Finale.VillageRebuilt)return null;
            return BaseSpatial.Layout(state).Objects.FirstOrDefault(o=>o.AssetId=="bench"&&o.Rotation==0)?.Id;
        }
        public static bool FinishAtBench(BaseState state,string seatId)
        {
            string benchId=EndingBench(state);
            if(benchId==null || seatId!=benchId+":seat:left" && seatId!=benchId+":seat:right")return false;
            var bench=BaseSpatial.Layout(state).Objects.Single(o=>o.Id==benchId);
            double x=bench.X+(seatId.EndsWith(":left",StringComparison.Ordinal)?-1:1)*bench.Width*.29;
            double y=bench.Y-bench.Height*.5-BaseMap.Radius-.06;
            if(new BasePoint(x,y).Distance(new BasePoint(state.PlayerX,state.PlayerY))>.65)return false;
            state.Demo.Finale.EndDay=state.Day;
            state.Demo.Finale.EndingSeen=true;
            state.Demo.Stage=BaseDemoStage.Finished;
            return true;
        }
        public static void Validate(BaseState state)
        {
            var progress=state.Demo.Finale;
            if(progress==null){if(Later(state))throw new ArgumentException("结尾阶段缺少快照");return;}
            bool storyCamp=state.Demo?.StoryScene?.Valid==true;
            if(progress.Version!=1||progress.CampBoard==null||(storyCamp?(progress.CampBoard.Width<10||progress.CampBoard.Width>64||progress.CampBoard.Height<10||progress.CampBoard.Height>64):(progress.CampBoard.Width!=(BaseDemoRelease.Enabled(state)?32:24)||progress.CampBoard.Height!=(BaseDemoRelease.Enabled(state)?24:14)))||progress.CampBoard.Cells?.Length!=progress.CampBoard.Width*progress.CampBoard.Height||progress.CampBoard.Cells.Any(cell=>cell==null||!cell.MetadataValid||!Enum.IsDefined(typeof(TerrainKind),cell.Terrain)||!Enum.IsDefined(typeof(DeploymentZone),cell.Zone))||progress.Village==null||progress.Village.Objects==null||!progress.Village.Objects.Any(obj=>obj.Id=="village_house"&&obj.Blocks)||!progress.Village.Objects.Any(obj=>(obj.Id=="intro_farm"||obj.Id=="village_field")&&obj.Blocks))throw new ArgumentException("Demo攻营／"+BaseScriptNames.Village+"快照非法");
            if(progress.CampTakenByDefense)
            {
                var occupied=CampSite(state);
                if(!Later(state)||state.Demo.Defense?.SecondOutcome!="defended"||SecondDefenseVictory(state)?.Day!=progress.AttackDay||occupied?.Owner!="player"||occupied.CapturedDay!=progress.AttackDay)
                    throw new ArgumentException("黑鸦寨自动占领缺少第二波防守胜利");
            }
            else if(progress.CampSuppliesTaken||state.Demo.Defense?.SecondVictoryReportSeen==true)
                throw new ArgumentException("黑鸦寨未因第二波防守占领，不能提交对应报告或物资记录");
            if(!progress.WorldOpened)
            {
                if(!progress.CampTakenByDefense&&(progress.AttackDay!=0||Later(state))||progress.CampSuppliesTaken||state.Demo.Stage==BaseDemoStage.Finished||progress.EndDay!=0||progress.RebuildMask!=0||progress.VillageRebuilt)
                    throw new ArgumentException("未开启行军不能提交结尾");
                return;
            }
            if(!BaseDemoDefense.Won(state)||state.Campaign.InstanceVersion!=1||state.Campaign.DesignId!=state.Demo.BalanceHash||!BaseWorldJourney.Active(state)||state.Campaign.Layouts.Count!=(BaseSceneRules.Active(state)?9:8)+(BaseBanditHideout.Installed(state)?1:0)||state.Campaign.WorldJourney.Nodes.Count!=3||state.Campaign.WorldJourney.Roads.Count!=2||state.Campaign.WorldJourney.Roads.Any(road=>road.Days!=1))throw new ArgumentException("Demo世界身份、路线或阶段非法");
            var site=BaseThreats.Site(state,Camp);var home=state.Campaign.Layouts.Single(map=>map.SourceMapId==Home);
            if(site==null||home.Region!="custom")throw new ArgumentException("Demo敌营／"+BaseScriptNames.Village+"缺失");
            if(progress.CampTakenByDefense)
            {
                var objects=BaseSceneInstances.Layout(state,Camp).Objects;
                if(objects.Count(obj=>obj.Id==CampSuppliesMarker)!=(progress.CampSuppliesTaken?0:1)||objects.Any(obj=>obj.Id=="demo_camp_attack"))
                    throw new ArgumentException("黑鸦寨物资记录与地图交互不一致");
            }
            else if(Later(state)?progress.AttackDay<state.Demo.Defense.VictoryDay||progress.AttackDay>state.Day||site.Owner!="player"||!state.Campaign.Reports.Any(report=>report.EventId==site.AttackEventId&&report.Outcome=="SideAWin"&&!report.Passive):progress.AttackDay!=0)
                throw new ArgumentException("攻营阶段缺少真实主动胜利");
            if(state.Demo.Stage==BaseDemoStage.Finished?progress.EndDay<progress.AttackDay||progress.EndDay>state.Day:progress.EndDay!=0)throw new ArgumentException("返乡结局日期非法");
            if(progress.RebuildMask<0||progress.RebuildMask>7||progress.HouseDialogueStep<0||progress.HouseDialogueStep>2||
                progress.VolunteersArrived&&progress.RebuildMask!=7||state.Demo.Stage==BaseDemoStage.Finished&&!progress.VillageRebuilt||
                progress.VillageRebuilt&&!progress.VolunteersArrived||progress.EndingSeen&&state.Demo.Stage!=BaseDemoStage.Finished)
                throw new ArgumentException("白桦村修理或村民到场记录非法");
            bool restored=progress.RebuildMask==7;
            foreach(var id in new[]{Builder,Helper})
            {
                var resident=home.Objects.SingleOrDefault(o=>o.Id==id);
                if((resident!=null)!=restored||resident!=null&&resident.VisualOnly!=(id==Helper||!progress.VolunteersArrived))
                    throw new ArgumentException("修复进度和麦田村民状态不一致");
            }
            if(HomeRepaired(state,"home")&&progress.HouseDialogueStep!=0)
                throw new ArgumentException("已修好的家仍在进行门口问话");
            var bridge=BaseSceneInstances.Layout(state,HomeField);
            if(bridge.Objects.Any(o=>o.Id=="demo_broken_sword")==state.Demo.Evidence.Contains(Sword))
                throw new ArgumentException("断剑拾取记录与地面物件不一致");
        }
    }
}

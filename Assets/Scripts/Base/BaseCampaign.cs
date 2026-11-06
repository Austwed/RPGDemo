using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BaseConquest.Battle;
using BaseConquest.RealtimePrototype;

namespace BaseConquest.Base
{
    [Serializable] public sealed class BaseCampaignEvent
    {
        public string Id, Title, Kind, ScenarioFile, Status = "pending";
        public int AvailableDay = 1, DeadlineDay = 1;
        public int EnemyRecoveryDay;
        public int HoldRounds = 30;
        public int FoodReward = 12, MaterialReward = 8, CoinReward = 15;
        // 掠夺单价：每名来袭敌军能带走多少。FoodRaid 是「口粮天数」不是粮草件数（按各兵种日耗折算），
        // 口径见 BaseRaidLoot；0 表示这一战不掠。
        public int FoodRaid = 3, MaterialRaid = 1, CoinRaid = 6;
        public string SourceSiteId="";
        public int DepartedDay;
        public List<BaseEnemyMember> EnemyArmy = new List<BaseEnemyMember>();
        public List<BaseLootReward> ItemRewards = new List<BaseLootReward> {new BaseLootReward {Template="novice_light_helmet",RandomQuality=true}};
        public BaseCampaignEvent Copy() {var copy=(BaseCampaignEvent)MemberwiseClone();copy.EnemyArmy=(EnemyArmy??new List<BaseEnemyMember>()).Select(u=>u.Copy()).ToList();copy.ItemRewards=ItemRewards.Select(r=>r.Copy()).ToList();return copy;}
    }
    [Serializable] public sealed class BaseCampaignBattle
    {
        public string Id, EventId, PartyId;
        public string MapRegion;
        public string MapId;
        public int StartedDay;
        public BattleScenario Scenario;
        public List<BaseLeader> Leaders = new List<BaseLeader>();
        public List<BaseWorkerBucket> Troops = new List<BaseWorkerBucket>();
        public BaseDifficulty Difficulty = new BaseDifficulty();
        public RpgRules Rpg;
        public BaseEquipmentQuota SupplySnapshot;
        public bool HasCheckpoint;
        public bool RealtimeRules,HasRealtimeState,DemoCombat;
        public int[] PlayerTroopCounts;
        public int RealtimeRulesVersion;
        public double TacticalBaseDamage;
        public double RuinsExposureFraction;
        public bool RuinsFirstClear;
        public BattleTacticalRules TacticalRules;
        public List<BattleTacticalObject> TacticalObjects=new List<BattleTacticalObject>();
        public RealtimeBattleState RealtimeState;
        public BattleCheckpoint Checkpoint;
        public List<BaseCampaignBinding> Bindings = new List<BaseCampaignBinding>();
        public List<BaseEnemyMember> Enemies = new List<BaseEnemyMember>();
        public List<BaseEnemyBinding> EnemyBindings = new List<BaseEnemyBinding>();
        public BaseCampaignBattle Copy() => new BaseCampaignBattle { Id = Id, EventId = EventId, PartyId = PartyId, MapRegion=MapRegion, MapId=MapId, StartedDay = StartedDay,
            Rpg=Rpg?.Copy(),TacticalBaseDamage=TacticalBaseDamage,RuinsExposureFraction=RuinsExposureFraction,RuinsFirstClear=RuinsFirstClear,TacticalRules=TacticalRules?.Copy(),TacticalObjects=(TacticalObjects??new List<BattleTacticalObject>()).Select(o=>o.Copy()).ToList(),Scenario = Scenario.Clone(), Leaders = Leaders.Select(l => l.Copy()).ToList(), Troops = Troops.Select(w => w.Copy()).ToList(), Difficulty = (Difficulty??new BaseDifficulty()).Copy(), SupplySnapshot = SupplySnapshot?.Copy(), HasCheckpoint=HasCheckpoint,Checkpoint=HasCheckpoint?Checkpoint.Copy():null,RealtimeRules=RealtimeRules,DemoCombat=DemoCombat,PlayerTroopCounts=PlayerTroopCounts?.ToArray(),RealtimeRulesVersion=RealtimeRulesVersion,HasRealtimeState=HasRealtimeState,RealtimeState=HasRealtimeState?RealtimeState?.Copy():null,Bindings=(Bindings??new List<BaseCampaignBinding>()).Select(x=>x.Copy()).ToList(), Enemies=(Enemies??new List<BaseEnemyMember>()).Select(u=>u.Copy()).ToList(),EnemyBindings=(EnemyBindings??new List<BaseEnemyBinding>()).Select(x=>x.Copy()).ToList() };
    }
    [Serializable] public sealed class BaseCampaignBinding {public int SoldierId,TroopIndex;public BaseCampaignBinding Copy()=>(BaseCampaignBinding)MemberwiseClone();}
    [Serializable] public sealed class BaseCampaignReport
    {
        public string BattleId, EventId, Title, Outcome;
        public int Day, Rounds, Deployed, Killed, Missing, Returned, Wounded, LeadersDown, GearLost;
        public int FoodChange, MaterialChange, CoinChange;
        public int DropMaterials;
        public string RecoveryNote="";
        // 防守战的掠夺说明（自动迎战、惨胜、战败、无人迎战都会写；非防守战留空）。
        public string RaidNote="";
        public string SourceSiteId="";
        public int EnemyDeployed, EnemyLost, EnemyCaptured, EnemyReturned, EnemyDispersed;
        public bool Passive;
        public bool Realtime;
        public double Seconds;
        public BaseCampaignReport Copy() => (BaseCampaignReport)MemberwiseClone();
    }
    [Serializable] public sealed class BaseCampaignState
    {
        public int Version;
        public int MapVersion;
        public string Location = "base";
        public string DesignId;
        public int InstanceVersion;
        public string CurrentMapId;
        public BaseWorldJourneyState WorldJourney;
        public List<BaseDesignLink> DesignLinks=new List<BaseDesignLink>();
        public double BaseX = 12, BaseY = 6.5;
        public double WorldX = 12, WorldY = 6.5;
        public List<BaseCampaignEvent> Events = new List<BaseCampaignEvent>();
        public List<string> SettledIds = new List<string>();
        public List<BaseCampaignReport> Reports = new List<BaseCampaignReport>();
        public bool HasActiveBattle;
        public bool ContentInitialized;
        public int ThreatVersion, ThreatDay;
        public List<BaseEnemySite> EnemySites = new List<BaseEnemySite>();
        public BaseCampaignBattle ActiveBattle = new BaseCampaignBattle();
        public BattleBoard PerimeterBoard;
        public List<BaseRegionMap> RegionMaps = new List<BaseRegionMap>();
        public List<BaseMapLayout> Layouts = new List<BaseMapLayout>();
        public BaseCampaignState Copy() => new BaseCampaignState { Version = Version, MapVersion=MapVersion, Location = Location, DesignId=DesignId,InstanceVersion=InstanceVersion,CurrentMapId=CurrentMapId,WorldJourney=WorldJourney?.Copy(),DesignLinks=(DesignLinks??new List<BaseDesignLink>()).Select(link=>link.Copy()).ToList(), BaseX = BaseX, BaseY = BaseY,WorldX=WorldX,WorldY=WorldY,
            Events = Events.Select(e => e.Copy()).ToList(), SettledIds = SettledIds.ToList(), Reports = Reports.Select(r => r.Copy()).ToList(),
            HasActiveBattle = HasActiveBattle, ContentInitialized = ContentInitialized, ThreatVersion=ThreatVersion,ThreatDay=ThreatDay,EnemySites=(EnemySites??new List<BaseEnemySite>()).Select(x=>x.Copy()).ToList(), ActiveBattle = HasActiveBattle ? ActiveBattle.Copy() : new BaseCampaignBattle(), PerimeterBoard = PerimeterBoard?.Clone(), RegionMaps = (RegionMaps??new List<BaseRegionMap>()).Select(m=>m.Copy()).ToList(), Layouts=(Layouts??new List<BaseMapLayout>()).Select(m=>m.Copy()).ToList() };
    }
    public static class BaseCampaign
    {
        public static readonly string[] Locations = { "base", "perimeter", "town", "world", "outpost", "dungeon" };
        public static string LocationName(string id) => id == "base" ? "基地" : id == "perimeter" ? "迎敌战场" : id == "town" ? "城镇" : id=="outpost" ? "荒野据点区域" : "世界地图";
        public static string LocationName(BaseState state)=>BaseSceneInstances.Active(state)||BaseDemo.Active(state)?BaseSpatial.Layout(state).Title:LocationName(state.Campaign.Location);
        public static string OutcomeName(string id) => id == "SideAWin" ? "胜利" : id == "SideBWin" ? "战败" : id == "Draw" ? "双方失能" : id == "Retreat" ? "撤离" : id == "Stalemate" ? "僵持退兵" : id == "RoundLimit" ? "到达上限退兵" : id=="Occupied" ? "接管据点" : "无守军失守";
        public static void SeedContent(BaseState s)
        {
            Normalize(s); if (s.Campaign.ContentInitialized) { BaseThreats.Enable(s); return; }
            if (s.Campaign.Events.Count == 0) s.Campaign.Events.AddRange(new[] {
                new BaseCampaignEvent { Id = "raid_demo_001", Title = "西侧来袭部队", Kind = "defense", ScenarioFile = "23_据点迎战.json", AvailableDay = s.Day, DeadlineDay = s.Day },
                new BaseCampaignEvent { Id = "outpost_demo_001", Title = "荒野敌军据点", Kind = "attack", ScenarioFile = "24_据点进攻.json", AvailableDay = s.Day, DeadlineDay = s.Day, FoodRaid = 0, MaterialRaid = 0, CoinRaid = 0 }
            });
            s.Campaign.ContentInitialized = true;
            BaseThreats.Enable(s);
        }
        public static BaseMapObject[] MapObjects(string location) => location == "base" ? BaseMap.Objects : location == "perimeter" ? new[] {
            new BaseMapObject("travel_base", "基地 →", 23.3, 6.5, 1.2, 2, false), new BaseMapObject("campaign_defense", "敌情与迎战", 12, 7, 3, 2, false)
        } : location == "world" ? new[] {
            new BaseMapObject("travel_base", "↑ 基地", 12, 13.3, 2, 1.2, false), new BaseMapObject("campaign_attack", "进入敌军据点区域", 16, 7, 3, 2, false)
        } : location=="outpost" ? new[] {
            new BaseMapObject("travel_world","返回世界地图",.7,13.3,1.2,1.2,false),new BaseMapObject("campaign_attack","据点敌情与进攻",12,7,3,2,false)
        } : new[] { new BaseMapObject("travel_base", "← 基地", 0.7, 6.5, 1.2, 2, false), new BaseMapObject("town_market", "交易摊位", 9, 8, 3, 2, false), new BaseMapObject("town_recruit", "募兵处", 16, 8, 3, 2, false), new BaseMapObject("town_quest", "镇务官 · 委托", 16, 4.5, 2, 1.5, false) };
        public static void Normalize(BaseState s)
        {
            if (s.Campaign != null && s.Campaign.Version != 0) return;
            if (s.Campaign != null && (s.Campaign.HasActiveBattle || s.Campaign.Events?.Count > 0 || s.Campaign.SettledIds?.Count > 0 || s.Campaign.Reports?.Count > 0)) throw new ArgumentException("战役数据缺版本，拒绝清空");
            s.Campaign = new BaseCampaignState { Version = 1, BaseX = s.PlayerX, BaseY = s.PlayerY };
        }
        private static bool Finite(double n) => !double.IsNaN(n) && !double.IsInfinity(n);
        public static void Validate(BaseState s)
        {
            Normalize(s); var c = s.Campaign; BaseSpatial.ValidateLayouts(s);
            BaseBattlefield.Ensure(s);
            BaseBattlefield.MigrateLegacyMap(s);
            var field=c.PerimeterBoard;bool storyField=s.Demo?.StoryScene?.Valid==true;
            if((storyField?(field.Width<10||field.Width>64||field.Height<10||field.Height>64):(field.Width!=(BaseDemoRelease.Enabled(s)?32:24)||field.Height!=(BaseDemoRelease.Enabled(s)?24:14)))||field.Cells?.Length!=field.Width*field.Height||field.Cells.Any(x=>x==null||!x.MetadataValid||!Enum.IsDefined(typeof(TerrainKind),x.Terrain)||!Enum.IsDefined(typeof(DeploymentZone),x.Zone))||!field.Walkable(new GridPosition(field.Width-1,6))||!field.Walkable(new GridPosition(0,6)))throw new ArgumentException("迎战地图数据非法");
            if (c.Version != 1 || c.InstanceVersion<0||c.InstanceVersion>1||!BaseSceneInstances.LocationValid(s,c.Location) || !Finite(c.BaseX) || !Finite(c.BaseY) || !Finite(c.WorldX) || !Finite(c.WorldY) || !BaseMap.Walkable(c.WorldX,c.WorldY,null,false) || c.Events == null || c.SettledIds == null || c.Reports == null || c.Events.Count > 1000 || c.Reports.Count > 1000 || c.SettledIds.Count > 100000) throw new ArgumentException("战役版本／位置／集合非法");
            if (c.Events.Any(e => e == null || string.IsNullOrWhiteSpace(e.Id) || string.IsNullOrWhiteSpace(e.Title) || e.Kind != "defense" && e.Kind != "attack" || e.Status != "pending" && e.Status != "resolved" || e.AvailableDay < 1 || e.DeadlineDay < e.AvailableDay || string.IsNullOrWhiteSpace(e.ScenarioFile) || Path.GetFileName(e.ScenarioFile) != e.ScenarioFile || !e.ScenarioFile.EndsWith(".json", StringComparison.OrdinalIgnoreCase) || new[] { e.FoodReward, e.MaterialReward, e.CoinReward, e.FoodRaid, e.MaterialRaid, e.CoinRaid }.Any(n => n < 0 || n > 1000000)) || c.Events.Select(e => e.Id).Distinct().Count() != c.Events.Count) throw new ArgumentException("战役事件非法");
            if (c.SettledIds.Any(string.IsNullOrWhiteSpace) || c.SettledIds.Distinct().Count() != c.SettledIds.Count || c.Reports.Any(r => r == null || string.IsNullOrWhiteSpace(r.BattleId) || !c.SettledIds.Contains(r.BattleId) || r.Day < 1 || r.Day > s.Day || new[] { r.Rounds, r.Deployed, r.Killed, r.Missing, r.Returned, r.Wounded, r.LeadersDown, r.GearLost }.Any(n => n < 0)) || c.Reports.Select(r => r.BattleId).Distinct().Count() != c.Reports.Count) throw new ArgumentException("战役结算记录非法");
            BaseThreats.Validate(s);
            c.RegionMaps??=new List<BaseRegionMap>();
            bool story=s.Demo?.StoryScene?.Valid==true;
            if(c.RegionMaps.Count>(BaseSceneInstances.Active(s)?31:BaseSceneRules.Active(s)?5:4) || c.RegionMaps.Any(m=>m==null||m.Id=="perimeter"||!(BaseSceneInstances.Active(s)?c.Layouts.Any(layout=>layout.SourceMapId==m.Id&&layout.Region!="perimeter"):Locations.Contains(m.Id))||m.Board==null||m.Board.Width<6||m.Board.Width>(story?64:32)||m.Board.Height<6||m.Board.Height>(story?64:24)||!story&&m.Id=="base"&&(m.Board.Width!=24||m.Board.Height!=14)||m.Board.Cells?.Length!=m.Board.Width*m.Board.Height||m.Board.Cells.Any(cell=>cell==null||!cell.MetadataValid||!Enum.IsDefined(typeof(TerrainKind),cell.Terrain)||!Enum.IsDefined(typeof(DeploymentZone),cell.Zone)))||c.RegionMaps.Select(m=>m.Id).Distinct().Count()!=c.RegionMaps.Count) throw new ArgumentException("区域地图数据非法");
            foreach(var location in new[] {"base","world","town"}) BaseBattlefield.Board(s,location);
            BaseDesignRoutes.Validate(s);
            if(BaseBattlefield.Board(s,"world").Cells.Any(cell=>cell.Terrain!=TerrainKind.Ground||cell.Zone!=DeploymentZone.None||cell.Elevation!=0||cell.Ramp!=RampAxis.None))throw new ArgumentException("世界地图仅作区域导航，不包含战术地形或部署区");
            if (!c.HasActiveBattle) return;
            var a = c.ActiveBattle;
            if(a!=null){if(a.Difficulty==null||a.Difficulty.Version==0)a.Difficulty=new BaseDifficulty();a.Bindings??=new List<BaseCampaignBinding>();a.SupplySnapshot??=s.Supply.Quotas.FirstOrDefault(q=>q.BattleId==a.Id)?.Copy();foreach(var l in a.Leaders??new List<BaseLeader>())if(s.Leaders.Any(person=>person.Id==l.Id&&person.Recruited))l.Recruited=true;}
            if (a == null || a.Scenario == null || string.IsNullOrWhiteSpace(a.Id) || c.SettledIds.Contains(a.Id) || a.StartedDay != s.Day || a.Leaders == null || a.Troops == null || a.Leaders.Count < 1 || a.Leaders.Count > 3 || a.Troops.Count > 90 || a.Leaders.Any(l => l == null || !s.Leaders.Any(p => p.Id == l.Id && p.PartyId == a.PartyId && p.Away)) || a.Leaders.Select(l => l.Id).Distinct().Count() != a.Leaders.Count || a.Troops.Any(w => w == null || w.Count != 1 || !w.Away || w.PartyId != a.PartyId || w.Health <= 0 || w.Health > w.MaxHealth) || !c.Events.Any(e => e.Id == a.EventId && e.Status == "pending") || !s.Supply.Quotas.Any(q => q.BattleId == a.Id && q.PartyId == a.PartyId && q.Phase == "fighting")) throw new ArgumentException("活动战斗与真实编制／配额不一致");
            if (a.Scenario.SupplyBattleId != a.Id + ":0" || a.Scenario.A.HeroCount != a.Leaders.Count || Enum.GetValues(typeof(BaseTroopKind)).Cast<BaseTroopKind>().Any(k => a.Scenario.A.Count(ToBattle(k)) != a.Troops.Count(w => w.Kind == k))) throw new ArgumentException("活动战场供给分母不一致");
            string HealthKey(BaseWorkerBucket w) => w.SourceId + ":" + (int)w.Kind + ":" + w.Health.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + ":" + w.MaxHealth.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
            var actual = s.Workers.Where(w => w.Away && w.PartyId == a.PartyId).ToArray();
            if (actual.Sum(w => w.Count) != a.Troops.Count || a.Troops.GroupBy(HealthKey).Any(g => actual.Where(w => HealthKey(w) == g.Key).Sum(w => w.Count) != g.Count()) || a.Leaders.Any(l => l.Health <= 0 || !Finite(l.Health) || !Finite(l.MaxHealth) || l.Health > l.MaxHealth || s.Leaders.Single(p => p.Id == l.Id).Health != l.Health)) throw new ArgumentException("活动战役的真实人员已被其他操作改变，拒绝重新生成人员");
            BattleScenarioDocument.Validate(new BattleScenarioDocument { Scenario = a.Scenario, DeploymentMode = "explicit" });
            if(a.HasCheckpoint) {var b=BattleSimulation.Restore(a.Checkpoint,a.Rpg!=null);if(b.Scenario.SupplyBattleId!=a.Scenario.SupplyBattleId||a.Bindings==null||a.Bindings.Any(x=>x.TroopIndex<0||x.TroopIndex>=a.Troops.Count||b.Soldier(x.SoldierId)==null||b.Soldier(x.SoldierId).DeveloperSpawned||b.Soldier(x.SoldierId).Side!=BattleSide.A||b.Soldier(x.SoldierId).Kind!=ToBattle(a.Troops[x.TroopIndex].Kind))||a.Bindings.Select(x=>x.SoldierId).Distinct().Count()!=a.Bindings.Count||a.Bindings.Select(x=>x.TroopIndex).Distinct().Count()!=a.Bindings.Count||a.Bindings.Count!=b.Soldiers.Count(u=>u.Side==BattleSide.A&&u.Kind!=SoldierKind.Hero&&!u.DeveloperSpawned))throw new ArgumentException("战斗检查点来源非法");}
            ValidateRealtime(a);
        }
        private static void ValidateRealtime(BaseCampaignBattle opening)
        {
            if(opening.DemoCombat&&(!opening.RealtimeRules||opening.PlayerTroopCounts==null||opening.PlayerTroopCounts.Length!=3||opening.PlayerTroopCounts.Any(count=>count<0||count>10000)))throw new ArgumentException("Demo兵力快照非法");
            if(opening.RealtimeRulesVersion<0||opening.RealtimeRulesVersion>1||!opening.RealtimeRules&&opening.RealtimeRulesVersion!=0)throw new ArgumentException("未知即时战斗数值版本");
            if(!opening.HasRealtimeState&&opening.RealtimeState!=null&&(opening.RealtimeState.Units?.Count>0||opening.RealtimeState.Arrows?.Count>0||opening.RealtimeState.Seconds!=0))throw new ArgumentException("即时内容缺少检查点存在标志，拒绝清空");
            if(!opening.RealtimeRules){if(opening.HasRealtimeState)throw new ArgumentException("旧回合战不能读取即时检查点");return;}
            RealtimeCampaignBattle.ValidateBoard(opening.Scenario.Board);
            if(!opening.HasCheckpoint){if(opening.HasRealtimeState)throw new ArgumentException("即时检查点缺少真实编制绑定");return;}
            if(opening.Checkpoint.Phase==BattlePhase.Deployment){if(opening.HasRealtimeState)throw new ArgumentException("布兵阶段不应包含即时推进记录");return;}
            if(!opening.HasRealtimeState||opening.RealtimeState==null)throw new ArgumentException("即时战斗检查点缺失，拒绝按旧回合恢复");
            new RealtimeCampaignBattle(new BaseCampaignSession(opening));
        }
        public static SoldierKind ToBattle(BaseTroopKind k) => k == BaseTroopKind.Melee ? SoldierKind.Melee : k == BaseTroopKind.Ranged ? SoldierKind.Ranged : SoldierKind.Cavalry;
        public static BaseTroopKind ToBase(SoldierKind k) => k == SoldierKind.Melee ? BaseTroopKind.Melee : k == SoldierKind.Ranged ? BaseTroopKind.Ranged : BaseTroopKind.Cavalry;
        public static IEnumerable<BaseCampaignEvent> Due(BaseState s) => s.Campaign.Events.Where(e => e.Kind == "defense" && e.Status == "pending" && e.AvailableDay <= s.Day && e.DeadlineDay <= s.Day);
        public static bool Travel(BaseState s, string location,string connectionId=null)
        {
            if(BaseDemoFinale.Opened(s))return BaseSceneInstances.Travel(s,location,connectionId);
            if(BaseDemoRuins.Enabled(s))return BaseDemoRuins.Travel(s,location);
            if(BaseDemo.Active(s)&&BaseDemo.TravelProblem(s,location)!="")return false;
            if(BaseSceneInstances.Active(s))return BaseSceneInstances.Travel(s,location,connectionId);
            BaseEconomy.Validate(s);
            if (BaseBattleLoot.Pending(s) || s.Aftermath.GameOver || !Locations.Contains(location) || s.Campaign.HasActiveBattle || s.Muster.Parties.Any(p => p.InCombat)) return false;
            if (location == s.Campaign.Location) return false;
            var link=BaseDesignRoutes.Active(s) ? BaseDesignRoutes.Reachable(s,location,connectionId) : null;
            if(BaseDesignRoutes.Active(s)&&link==null)return false;
            if(location=="outpost"&&(!s.Campaign.RegionMaps.Any(m=>m.Id=="outpost")||!BaseDesignRoutes.Active(s)&&s.Campaign.Location!="world"))return false;
            if(BaseDesignRoutes.Active(s)&&(location=="outpost"||location=="world")&&!s.Muster.Parties.Any(p=>p.Purpose=="expedition"&&p.Leaders.Any(l=>l.LeaderId==s.PlayerLeaderId)))return false;
            if (s.Campaign.Location == "base") { s.Campaign.BaseX = s.PlayerX; s.Campaign.BaseY = s.PlayerY; }
            bool fromOutpost=s.Campaign.Location=="outpost";
            if(s.Campaign.Location=="world"){s.Campaign.WorldX=s.PlayerX;s.Campaign.WorldY=s.PlayerY;}
            s.Campaign.Location = location;
            var player = s.Leaders.Single(l => l.Id == s.PlayerLeaderId); player.UnavailableDay = s.Day; player.FacilityId = "";
            s.PlayerX = location == "base" ? s.Campaign.BaseX : location == "perimeter" ? 22.5 : 12; s.PlayerY = location == "base" ? s.Campaign.BaseY : 6.5;
            if(location=="outpost"){var entrance=BaseSpatial.Layout(s);s.PlayerX=entrance?.SpawnX??.5;s.PlayerY=entrance?.SpawnY??BaseBattlefield.Board(s).Height-.5;}
            else if(location=="world"&&fromOutpost){s.PlayerX=s.Campaign.WorldX;s.PlayerY=s.Campaign.WorldY;}
            else if(location!="base"&&BaseSpatial.Layout(s) is BaseMapLayout layout){s.PlayerX=layout.SpawnX;s.PlayerY=layout.SpawnY;}
            if(link!=null){s.PlayerX=link.X;s.PlayerY=link.Y;}
            BaseSpatial.RepairGridPositions(s);
            foreach(var leader in BaseAdventure.Members(s)) {leader.UnavailableDay=s.Day;leader.FacilityId="";}
            BaseAdventure.Arrive(s);
            return true;
        }
        public static BaseCampaignBattle Begin(BaseState s, string eventId, BattleScenarioDocument document, string mode = "full", BaseMusterPlan plan = null,bool realtime=false)
        {
            if(BaseDemoFinale.Rebuilding(s))throw new InvalidOperationException("返乡重建期间不能开始战斗");
            BaseEconomy.Validate(s); BattleScenarioDocument.Validate(document);
            if (document.StartInCombat || document.InitialHealth?.Count > 0 || document.InitialEffects?.Count > 0) throw new ArgumentException("战役模板需从准备阶段开始，不能使用独立倒地／临效预置");
            var e = s.Campaign.Events.Single(x => x.Id == eventId);
            if(BaseSceneInstances.Active(s)&&e.Kind=="attack"&&e.Id!=BaseDemoRuins.EventId&&(s.Campaign.Location!="outpost"||e.SourceSiteId!=s.Campaign.CurrentMapId))throw new InvalidOperationException("请到对应敌营实例开战，不能遥控其他据点");
            if (BaseSceneCombat.Active(s) || s.Muster.Parties.Any(p => p.InCombat) || e.Status != "pending" || e.AvailableDay > s.Day) throw new InvalidOperationException("事件未到达、已处理或已有战斗");
            if(e.Kind=="defense"&&!string.IsNullOrEmpty(e.SourceSiteId)&&e.EnemyArmy.Count==0)
            {
                e.EnemyArmy=BaseThreats.EnemySnapshot(s,e,document.Scenario.B);
                BaseItemRecovery.Normalize(s);
            }
            // Demo 里的防守战一律走即时规则：首防、第二波和主线后的随机入侵共用同一条即时战斗链。
            bool demoCombat=realtime && BaseDemo.Active(s) && (e.Kind=="defense" || new[]{BaseDemoRuins.EventId,BaseDemoDefense.EventId,BaseDemoDefense.SecondEventId,BaseThreats.Site(s,BaseDemoFinale.Camp)?.AttackEventId}.Contains(e.Id));
            if(demoCombat && string.IsNullOrEmpty(e.SourceSiteId) && e.EnemyArmy.Count==0)e.EnemyArmy=BaseDemoCombat.CreateEnemies(e.Id,document.Scenario.B,BaseDemoEncounterConfig.Preset(s,e.Id));
            if(demoCombat){BaseRpg.PrepareEnemies(s,e.EnemyArmy,e.Id);BaseItemRecovery.Normalize(s);}
            var enemies=BaseThreats.EnemySnapshot(s,e,document.Scenario.B);
            string purpose = e.Kind == "defense" ? "defense" : "expedition";
            var party = s.Muster.Parties.FirstOrDefault(p => p.Purpose == purpose);
            if (party == null)
            {
                plan = plan ?? BaseMuster.PreviewFormation(s,purpose,"combat");
                if (plan.Purpose != purpose || !BaseMuster.Confirm(s, plan)) throw new InvalidOperationException("可用队长不足或编制变化，请重新预览");
                party = s.Muster.Parties.Single(p => p.Id == plan.Id);
            }
            if(s.Workers.Where(w=>w.Away&&w.PartyId==party.Id).Sum(w=>w.Count)>BaseMuster.BattleSoldierLimit)throw new InvalidOperationException("出战士兵总数超过24人，请返营调整");
            if(e.Kind=="attack"&&!party.Leaders.Any(l=>l.LeaderId==s.PlayerLeaderId))throw new InvalidOperationException("进攻出征需主角带队，先返营重新选择");
            if(BaseWorldJourney.Active(s)&&e.Kind=="attack"&&(party.Leaders.Any(item=>!BaseAdventure.Alive(s.Leaders.Single(leader=>leader.Id==item.LeaderId)))||!demoCombat&&s.Workers.Any(worker=>worker.PartyId==party.Id&&worker.Health<=1)))throw new InvalidOperationException("出征队有阵亡队长或重伤士兵，请沿世界道路返营整备");
            var quota = s.Supply.Quotas.FirstOrDefault(q => q.PartyId == party.Id);
            if (quota == null)
            {
                var supply = BaseSupply.Preview(s, party.Id, mode);
                if (!BaseSupply.Reserve(s, supply)) throw new InvalidOperationException("军需预览已过期"); quota = s.Supply.Quotas.Single(q => q.BattleId == supply.Quota.BattleId);
            }
            if (!BaseSupply.StartBattle(s, quota.BattleId)) throw new InvalidOperationException("军需准备不可开战");
            if(e.Kind=="attack"&&!(BaseDemoRuins.Enabled(s)&&e.Id==BaseDemoRuins.EventId))
            {
                BaseWorldMap.PrepareAttackRegion(s,document);
                if(s.Campaign.Location=="world"){s.Campaign.WorldX=s.PlayerX;s.Campaign.WorldY=s.PlayerY;}
                if(s.Campaign.Location!="outpost"){s.Campaign.Location="outpost";var entrance=BaseSpatial.Layout(s);s.PlayerX=entrance?.SpawnX??.5;s.PlayerY=entrance?.SpawnY??BaseBattlefield.Board(s).Height-.5;BaseAdventure.Arrive(s);}
            }
            var raw = document.Scenario.Clone(); raw.Placements.Clear(); raw.SupplyBattleId = ""; raw.TestAutoDeployment = false;
            raw.MaxRounds = Math.Min(raw.MaxRounds, 300); raw.ShowEnemiesThroughFog = false;
            raw.DefenderSide = e.Kind == "defense" ? 0 : 1; raw.DefenderVisionEnabled = raw.CampaignRules = true; raw.FogEnabled = true;
            raw.InstantDeathEnabled = true; raw.LeaderInstantDeath = s.Aftermath.Difficulty.LeaderDeath; raw.TroopInstantDeath = s.Aftermath.Difficulty.TroopDeath;
            string location = s.Campaign.Location == "base" ? (e.Kind == "defense" ? "perimeter" : "world") : s.Campaign.Location;
            if(BaseDesignRoutes.Active(s)&&e.Kind=="defense")location="perimeter";
            raw.Board = realtime&&BaseSceneRules.Active(s)?BaseSceneCombat.Project(s,location):BaseBattlefield.Board(s, location).Clone(); raw.CustomExits = true;
            if(realtime)RealtimeCampaignBattle.ValidateBoard(raw.Board);
            raw.ExitA = e.Kind == "defense" ? new GridPosition(raw.Board.Width-1,6) : new GridPosition(0,raw.Board.Height-1); raw.ExitB = e.Kind=="defense" ? new GridPosition(0,6) : new GridPosition(raw.Board.Width-1,0);
            if (e.Kind == "defense") { raw.Objective.Kind = ObjectiveKinds.Hold; raw.Objective.HoldRounds = Math.Min(raw.MaxRounds, e.HoldRounds > 0 ? e.HoldRounds : 30); }
            raw.Objective.Title = e.Kind == "defense" ? "坚守迎敌战场" : "攻克敌点";
            BaseRpg.PrepareEnemies(s,enemies,e.Id);
            BaseThreats.ConfigureEnemy(raw,enemies);
            if(BaseDemoEncounterConfig.Preset(s,e.Id)!=null||e.Kind=="attack"&&BaseSpatial.Layout(s)?.Enemy?.Version==2)BaseThreats.ConfigureDeployment(raw,enemies);
            raw.A.MeleeCount = quota.Melee; raw.A.RangedCount = quota.Ranged; raw.A.CavalryCount = quota.Cavalry;
            raw.A.HeroCount = quota.Leaders.Count; raw.A.CaptainSlot = 0; raw.A.Capacity = 90;
            raw.PlayerLeaderSlot = quota.Leaders.IndexOf(s.PlayerLeaderId);
            var sceneLeaders=party.Leaders.Select(l=>s.Leaders.Single(p=>p.Id==l.LeaderId)).ToList();
            raw.A.Commanders = Enumerable.Range(0, raw.A.HeroCount).Select(slot => new CommanderLoadout()).ToList();
            var formation=BaseMuster.CurrentFormation(s,party);
            foreach (var kind in BattleSimulation.TroopKinds)
                if(formation!=null)for(int slot=0;slot<formation.Count;slot++)raw.A.Commanders[slot].Set(kind,formation[slot].Actual.Count(ToBase(kind)));
                else for (int n = 0; n < raw.A.Count(kind); n++) raw.A.Commanders[n % raw.A.HeroCount].Set(kind, raw.A.Commanders[n % raw.A.HeroCount].Count(kind) + 1);
            BaseSceneRules.Configure(s,raw,sceneLeaders);
            var a = new BaseCampaignBattle { Rpg=s.Demo?.Rpg?.Copy(),Id = quota.BattleId, EventId = e.Id, PartyId = party.Id, MapRegion=realtime||BaseDesignRoutes.Active(s)?location:null, StartedDay = s.Day,
                Scenario = BaseSupply.ApplyToScenario(s, quota.BattleId, raw), Leaders = party.Leaders.Select(l => s.Leaders.Single(p => p.Id == l.LeaderId).Copy()).ToList(), Difficulty = new BaseDifficulty(), SupplySnapshot = quota.Copy(), Enemies=enemies,RealtimeRules=realtime,DemoCombat=demoCombat,PlayerTroopCounts=demoCombat?BattleSimulation.TroopKinds.Select(kind=>s.Workers.Where(w=>ToBattle(w.Kind)==kind).Sum(w=>w.Count)).ToArray():null,RealtimeRulesVersion=realtime?1:0 };
            if(BaseDemoRelease.Enabled(s)&&realtime)
            {a.TacticalRules=s.Demo.TacticalRules.Copy();a.TacticalObjects=BaseSpatial.Layout(s,location).TacticalObjects.Select(o=>o.Copy()).ToList();a.TacticalBaseDamage=BaseRpg.Active(s)?s.Demo.Rpg.Templates.Single(t=>t.Id=="soldier_warrior").Attack:s.Demo.RuinsArmy.Melee.Damage;}
            if(BaseSceneInstances.Active(s))a.MapId=BaseSceneInstances.Layout(s,location).SourceMapId;
            if(e.Kind=="defense"&&enemies.Count>0)e.EnemyArmy=enemies.Select(u=>u.Copy()).ToList();
            a.Scenario.A.CommanderStats=a.Leaders.Select(l=>{var p=BaseCommanderIdentity.Stats(l,raw.A.Hero);p.Damage*=(float)quota.AttackFactor;return p;}).ToList();
            foreach (var w in s.Workers.Where(w => !BaseSceneRules.Neutral(s)&&w.Away && w.PartyId == party.Id))
                for (int n = 0; n < w.Count; n++) { var unit = w.Copy(); unit.Count = 1; a.Troops.Add(unit); }
            s.Campaign.ActiveBattle = a; s.Campaign.HasActiveBattle = true;
            BaseEconomy.Validate(s); return a;
        }
        public static bool Abort(BaseState s, BaseCampaignSession session)
        {
            BaseEconomy.Validate(s); if (session.Battle.Phase != BattlePhase.Deployment || !s.Campaign.HasActiveBattle || s.Campaign.ActiveBattle.Id != session.Opening.Id) return false;
            var a = s.Campaign.ActiveBattle; s.Campaign.HasActiveBattle = false; s.Campaign.ActiveBattle = new BaseCampaignBattle();
            var q = s.Supply.Quotas.Single(x => x.BattleId == a.Id); q.Phase = "prepared"; s.Muster.Parties.Single(p => p.Id == a.PartyId).InCombat = false;
            if (!BaseSupply.Cancel(s, a.Id) || !(BaseWorldJourney.Active(s)&&s.Muster.Parties.Single(party=>party.Id==a.PartyId).Purpose=="expedition")&&!BaseMuster.Return(s, a.PartyId)) throw new InvalidOperationException("未能取消战场准备");
            ToBaseLocation(s); return true;
        }
        private static void ToBaseLocation(BaseState s)
        {
            if(BaseWorldJourney.Active(s)||BaseDemoRuins.Enabled(s)&&(s.Campaign.Location=="outpost"||BaseSceneRules.Dungeon(s))){BaseSpatial.RepairGridPositions(s);BaseAdventure.Arrive(s);return;}
            if(BaseSceneInstances.Active(s))s.Campaign.CurrentMapId=BaseSceneInstances.Layout(s,"base").SourceMapId;
            s.Campaign.Location = "base"; s.PlayerX = s.Campaign.BaseX; s.PlayerY = s.Campaign.BaseY;
            BaseSpatial.RepairGridPositions(s);
            BaseAdventure.Arrive(s);
        }
        public static bool Settle(BaseState s, BaseCampaignSession session, bool passive = false)
        {
            BaseEconomy.Validate(s);
            var battle = session.Battle; var a = session.Opening;
            if (!s.Campaign.HasActiveBattle || s.Campaign.ActiveBattle.Id != a.Id || s.Campaign.SettledIds.Contains(a.Id)) return false;
            if (battle.Phase != BattlePhase.Finished || battle.Report == null || battle.Scenario.SupplyBattleId != a.Id + ":0") throw new InvalidOperationException("战斗未最终结束，不能回写");
            session.Bind(); session.SaveCheckpoint(s); var e = s.Campaign.Events.Single(x => x.Id == a.EventId);
            bool win = battle.Report.Outcome == BattleOutcome.SideAWin;
            bool lostField = !win;
            battle.ResolveCasualties();
            var main = battle.Soldiers.SingleOrDefault(u => u.Side == BattleSide.A && u.Kind == SoldierKind.Hero && u.OriginCommanderSlot == a.Scenario.PlayerLeaderSlot);
            bool wiped = a.Scenario.PlayerLeaderSlot >= 0 && battle.Soldiers.Any(u => u.Side == BattleSide.A) && battle.Soldiers.Where(u => u.Side == BattleSide.A && !u.DeveloperSpawned).All(u => !u.IsActive && !u.Escaped && !u.Rescued);
            if (!a.DemoCombat && (wiped || main != null && !main.IsAlive && !main.Rescued))
            { s.Aftermath.GameOver = true; s.Aftermath.Reason = wiped ? "主角队伍团灭，无人成功撤离" : "倒地主角未被救出"; }
            var r = new BaseCampaignReport { BattleId = a.Id, EventId = e.Id, Title = e.Title, Day = s.Day, Rounds = battle.RoundNumber, Outcome = battle.Report.Outcome.ToString(), Passive = passive,Realtime=a.RealtimeRules,Seconds=a.RealtimeState?.Seconds??0 };
            var outcome = new BaseSupplyOutcome(); var survivors = new List<BaseWorkerBucket>(); var forcedRest = new HashSet<BaseWorkerBucket>();
            for (int i = 0; i < a.Troops.Count; i++)
            {
                var snapshot = a.Troops[i]; var unit = session.UnitForTroop(i);
                bool lost = unit != null && (unit.InstantDead || !a.DemoCombat && lostField && !unit.Escaped && !unit.Rescued);
                if (unit != null) r.Deployed++;
                if (lost) { if (unit.InstantDead) r.Killed++; else r.Missing++; }
                else { var survivor = snapshot.Copy(); survivor.Health = unit == null ? snapshot.Health : Math.Max(1, Math.Min(snapshot.MaxHealth, unit.Health)); if (unit != null && !a.DemoCombat && survivor.Health < survivor.MaxHealth * BaseDevelopment.DutyThreshold) forcedRest.Add(survivor); survivors.Add(survivor); r.Returned++; if (survivor.Health < survivor.MaxHealth) r.Wounded++; }
                if (unit != null)
                {
                    if (snapshot.Kind == BaseTroopKind.Melee) { if (lost) outcome.LostMelee++; else outcome.SafeMelee++; }
                    else if (snapshot.Kind == BaseTroopKind.Ranged) { if (lost) outcome.LostRanged++; else outcome.SafeRanged++; }
                    else { if (lost) outcome.LostCavalry++; else outcome.SafeCavalry++; }
                }
            }
            var down = new List<string>();
            for (int i = 0; i < a.Leaders.Count; i++)
            {
                var leader = s.Leaders.Single(l => l.Id == a.Leaders[i].Id); var unit = battle.Soldiers.SingleOrDefault(w => w.Side == BattleSide.A && w.Kind == SoldierKind.Hero && w.OriginCommanderSlot == i);
                bool lost = false;
                leader.Health = unit == null ? Math.Max(1,a.Leaders[i].Health) : lost ? 0 : unit.IsAlive ? Math.Max(1,Math.Min(leader.MaxHealth, unit.Health)) : 1;
                leader.Fate=lost ? unit.InstantDead ? "dead" : "missing" : "active";
                if (unit != null && !unit.IsAlive) { r.LeadersDown++; if (leader.Health > 0) down.Add(leader.Id); }
            }
            s.Workers.RemoveAll(w => w.Away && w.PartyId == a.PartyId); s.Workers.AddRange(survivors);
            var returning=s.Muster.Parties.Single(p=>p.Id==a.PartyId);if(returning.CommanderTroops!=null)returning.CommanderTroops=BaseMuster.CurrentFormation(s,returning);
            int gear = s.Inventory.Equipment.Sum(x => x.Count);
            s.Campaign.HasActiveBattle = false; s.Campaign.ActiveBattle = new BaseCampaignBattle();
            BaseWine.Finish(s,session);
            // 先清除外出状态再置重伤闲置标记，避免“外出且休养”的中间状态被军需验证拒绝，导致战报无法返营。
            bool keepExpedition=BaseWorldJourney.Active(s)&&returning.Purpose=="expedition"&&a.EventId!=BaseDemoRuins.EventId;
            if (!BaseSupply.Finish(s, a.Id, outcome) || !keepExpedition&&!BaseMuster.Return(s, a.PartyId, w=> {if(forcedRest.Contains(w))w.Resting=true;})) throw new InvalidOperationException("未能回写真实军需／返营");
            r.GearLost = gear - s.Inventory.Equipment.Sum(x => x.Count);
            foreach (var id in down) { var l = s.Leaders.Single(x => x.Id == id); l.Resting = false; l.UnavailableDay = s.Day; }
            s.Adventure.Members.RemoveAll(id=>id!=s.PlayerLeaderId&&!BaseAdventure.Alive(s.Leaders.Single(l=>l.Id==id)));
            int personalBeforeCapture=s.PersonalEquipment.Warehouse.Count;
            BaseAftermath.Capture(s, session, win);
            if(s.PersonalEquipment.Warehouse.Count>personalBeforeCapture)r.RecoveryNote="俘虏穿戴装备已缴入仓库："+(s.PersonalEquipment.Warehouse.Count-personalBeforeCapture)+"件";
            BaseItemRecovery.Record(s,session,e,r,win);
            if(win&&!s.Aftermath.GameOver){r.DropMaterials=Math.Min(battle.Soldiers.Count(u=>u.Side==BattleSide.B&&!u.DeveloperSpawned&&!u.Escaped&&!u.IsAlive&&BattleSimulation.StableRoll(battle.Scenario.RandomSeed,u.Id,29)<a.Difficulty.DropRate),100000000-s.Inventory.Materials);s.Inventory.Materials+=r.DropMaterials;}
            ApplyEventInventory(s, e, win && !s.Aftermath.GameOver, r,a.Difficulty.RewardFactor); e.Status = win && !s.Aftermath.GameOver || e.Kind == "defense" ? "resolved" : "pending";
            BaseThreats.Settle(s,e,session,r,win&&!s.Aftermath.GameOver);
            // 掠夺等战果回写之后再结算：此时 EnemyDeployed 才是本场实际参战人头。
            BaseRaidLoot.Apply(s, e, r, win, passive);
            s.Campaign.SettledIds.Add(a.Id); s.Campaign.Reports.Add(r); BaseQuests.RecordVictory(s,r); if(!passive)ToBaseLocation(s);
            BaseBattleLoot.Create(s,r,win&&!s.Aftermath.GameOver?e.ItemRewards:Enumerable.Empty<BaseLootReward>());
            BaseSceneRules.RememberStrategy(s,session); BaseDevelopment.Allocate(s); BaseEconomy.Validate(s); return true;
        }
        private static void ApplyEventInventory(BaseState s, BaseCampaignEvent e, bool win, BaseCampaignReport r, double? rewardFactor=null)
        {
            if (win) { double f = rewardFactor??s.Aftermath.Difficulty.RewardFactor; r.FoodChange = Math.Min((int)(e.FoodReward * f), 100000000 - s.Inventory.Food); r.MaterialChange = Math.Min((int)(e.MaterialReward * f), 100000000 - s.Inventory.Materials); r.CoinChange = Math.Min((int)(e.CoinReward * f), 100000000 - s.Inventory.Coins); }
            // 防守失败的掠夺不再在这里扣：按人头算的损失由 BaseRaidLoot 统一结算。
            s.Inventory.Food = checked(s.Inventory.Food + r.FoodChange); s.Inventory.Materials = checked(s.Inventory.Materials + r.MaterialChange); s.Inventory.Coins = checked(s.Inventory.Coins + r.CoinChange);
        }
        public static bool ClaimUnopposed(BaseState s,string siteId)
        {
            BaseEconomy.Validate(s);var site=BaseThreats.Site(s,siteId);
            if(BaseSceneInstances.Active(s)&&siteId!=s.Campaign.CurrentMapId)return false;
            if(site==null||site.Owner!="enemy"||s.Aftermath.GameOver||s.Campaign.HasActiveBattle||s.Campaign.Location!="outpost"||site.Garrison.Any(u=>u.Kind==SoldierKind.Hero)||!s.Muster.Parties.Any(p=>p.Purpose=="expedition"&&p.Leaders.Any(l=>l.LeaderId==s.PlayerLeaderId)))return false;
            var e=s.Campaign.Events.Single(x=>x.Id==site.AttackEventId);string id="occupation:"+site.Id;
            if(e.Status!="pending"||e.AvailableDay>s.Day||s.Campaign.SettledIds.Contains(id))return false;
            var report=new BaseCampaignReport {BattleId=id,EventId=e.Id,Title=e.Title,Day=s.Day,Outcome="Occupied",SourceSiteId=site.Id,EnemyDispersed=site.Garrison.Count};
            ApplyEventInventory(s,e,true,report);site.Owner="player";site.CapturedDay=s.Day;site.Garrison.Clear();e.Status="resolved";
            BaseItemRecovery.ReturnForEvent(s,e.Id,report);
            s.Campaign.SettledIds.Add(id);s.Campaign.Reports.Add(report);BaseEconomy.Validate(s);return true;
        }
        public static bool Rest(BaseState s, int day, Func<string, BattleScenarioDocument> resolve,bool realtime=false)
        {
            if(BaseDemoFinale.Rebuilding(s))throw new InvalidOperationException("请先完成返乡重建");
            if(BaseDemoDefense.AnyDue(s))throw new InvalidOperationException("敌军已抵营门，此刻无法休息");
            BaseEconomy.Validate(s); if (s.Aftermath.GameOver || s.Day != day || s.LastCompletedDay >= day) return false;
            if (s.Campaign.HasActiveBattle || s.Muster.Parties.Any(p => p.InCombat)) throw new InvalidOperationException("先结束当前战斗");
            foreach (var e in Due(s).ToArray())
            {
                if(!string.IsNullOrEmpty(e.SourceSiteId)&&e.EnemyArmy.Count==0)e.EnemyArmy=BaseThreats.EnemySnapshot(s,e,resolve(e.ScenarioFile).Scenario.B);
                var available = s.Muster.Parties.FirstOrDefault(p => p.Purpose == "defense"); var plan = available == null ? BaseMuster.PreviewFormation(s,"defense","combat") : null;
                if (available == null && plan.Leaders.Count == 0)
                {
                    var id = "unguarded:" + e.Id; var r = new BaseCampaignReport { BattleId = id, EventId = e.Id, Title = e.Title, Day = s.Day, Outcome = "Unopposed", Passive = true };
                    ApplyEventInventory(s, e, false, r); e.Status = "resolved";
                    // 没人迎战等于全营被翻一遍：按敌方人头走重掠档，然后才把残军退回来源据点。
                    BaseRaidLoot.Apply(s, e, r, false, true, e.EnemyArmy.Count(u => !u.Dead));
                    var source=BaseThreats.Site(s,e.SourceSiteId);if(source!=null){r.SourceSiteId=source.Id;if(source.Owner=="enemy"){source.Garrison.AddRange(e.EnemyArmy.Select(u=>u.Copy()));r.EnemyReturned=e.EnemyArmy.Count;}else r.EnemyDispersed=e.EnemyArmy.Count;e.EnemyArmy.Clear();}
                    s.Campaign.SettledIds.Add(id); s.Campaign.Reports.Add(r); continue;
                }
                var a = Begin(s, e.Id, resolve(e.ScenarioFile), "full", plan,realtime); var session = new BaseCampaignSession(a); session.Battle.AutoDeploy(BattleSide.A); session.Bind();
                if (!session.Battle.Start(out var reason)) throw new InvalidOperationException("被动部署不能开始：" + reason);
                if(realtime)
                {
                    // 白名单内（基地自动迎战）且推算能取胜，就用战力结算跳过真实作战；
                    // 不能跳过或推算不胜／僵持的，照旧由玩家亲自指挥的真实战斗处理。
                    if(BaseAutoResolve.TryResolve(s,session,e.Id))
                    {
                        Settle(s,session,true);
                        var passive=s.Campaign.Reports.FirstOrDefault(r=>r.BattleId==session.Opening.Id);
                        if(passive!=null){passive.Passive=true;passive.Title=e.Title+" · 自动迎战";BaseEconomy.Validate(s);}
                        if(s.Aftermath.GameOver)return true;continue;
                    }
                    var simulation=new RealtimeCampaignBattle(session);simulation.Model.SetPaused(false);
                    for(int frame=0;frame<(a.Scenario.MaxRounds+1)*4&&session.Battle.Phase!=BattlePhase.Finished;frame++)simulation.Tick(.25);
                    if(session.Battle.Phase!=BattlePhase.Finished)throw new InvalidOperationException("即时自动战斗未在时限内结束，整次过夜取消");
                    simulation.SaveCheckpoint(s);Settle(s,session,true);if(s.Aftermath.GameOver)return true;continue;
                }
                int bound = a.Scenario.MaxRounds + 2;
                for (int n = 0; n < bound && session.Battle.Phase != BattlePhase.Finished; n++)
                { if (session.Battle.VictoryDeclared) { session.Battle.SettleVictory(); break; } session.Battle.AdvanceRound(true); }
                if (session.Battle.VictoryDeclared) session.Battle.SettleVictory();
                if (session.Battle.Phase != BattlePhase.Finished) throw new InvalidOperationException("被动模拟未在回合上限内结束，整次过夜取消");
                Settle(s, session, true);
                if (s.Aftermath.GameOver) return true;
            }
            return BaseEconomy.Rest(s, day);
        }
    }
    // 绑定真实快照到本场实际部署ID；未部署预备保持原生命、不发生磨损。
    public sealed class BaseCampaignSession
    {
        public readonly BaseCampaignBattle Opening;
        public readonly BattleSimulation Battle;
        private readonly Dictionary<int, int> troops = new Dictionary<int, int>();
        private readonly HashSet<int> leaders = new HashSet<int>();
        private readonly Dictionary<int,int> enemies = new Dictionary<int,int>();
        public BaseCampaignSession(BaseCampaignBattle opening, BaseMapSession map = null)
        {
            Opening = opening.Copy();
            if(Opening.Rpg!=null){Opening.Rpg.Validate();foreach(var person in Opening.Leaders)if(person.Rpg!=null)person.Rpg.Rules=Opening.Rpg;}
            Battle = Opening.HasCheckpoint ? BattleSimulation.Restore(Opening.Checkpoint,Opening.Rpg!=null) : new BattleSimulation(Opening.Scenario);
            if(Opening.HasCheckpoint) {foreach(var x in Opening.Bindings)troops.Add(x.SoldierId,x.TroopIndex);leaders.UnionWith(Battle.Soldiers.Where(u=>u.Side==BattleSide.A&&u.Kind==SoldierKind.Hero).Select(u=>u.Id));}
            else
            {
                if(Opening.Scenario.Placements.Any(item=>item.Side==BattleSide.B)&&Opening.Enemies.Any(unit=>unit.HasDeployment)&&Battle.Soldiers.Count(unit=>unit.Side==BattleSide.B)!=Opening.Enemies.Count)throw new ArgumentException("逐兵编排未完整部署，不能自动替换站位");
                Battle.AutoDeploy(BattleSide.B);
            }
            if(Opening.HasCheckpoint)foreach(var x in Opening.EnemyBindings)enemies.Add(x.SoldierId,x.MemberIndex);
            BindEnemies();
            for (int i = 0; i < Opening.Leaders.Count; i++) Battle.CommanderNamesA.Add(Opening.Leaders[i].Name);
            Battle.Scenario.A.CommanderRecruited=Opening.Leaders.Select(l=>l.Recruited).ToList();
            Battle.Scenario.A.CommanderAppearances=Opening.Leaders.Select(BaseCommanderIdentity.Appearance).ToList();
            map?.EnterBattle(Battle);
        }
        public void Bind()
        {
            BindEnemies();
            foreach (var id in troops.Keys.Where(id => Battle.Soldier(id) == null).ToArray()) troops.Remove(id);
            leaders.RemoveWhere(id => Battle.Soldier(id) == null);
            foreach (var unit in Battle.Soldiers.Where(w => w.Side == BattleSide.A && !w.DeveloperSpawned).OrderBy(w => w.Id))
            {
                unit.HasProvisionEffects=true;unit.ProvisionAttackFactor=(float)(Opening.SupplySnapshot?.AttackFactor??1);
                if(unit.OriginCommanderSlot>=0&&unit.OriginCommanderSlot<Opening.Leaders.Count)BaseWine.Apply(unit,Opening.Leaders[unit.OriginCommanderSlot],Opening.StartedDay);
                unit.ProvisionWeaponBonus=unit.Kind==SoldierKind.Hero ? 0 : (float)(unit.Kind==SoldierKind.Ranged ? Opening.SupplySnapshot?.BowBonus??0 : Opening.SupplySnapshot?.SwordBonus??0);unit.ProvisionArmorBonus=unit.Kind==SoldierKind.Hero ? 0 : (float)(Opening.SupplySnapshot?.ArmorBonus??0);
                if (unit.Kind == SoldierKind.Hero)
                {
                    unit.Recruited=Opening.Leaders[unit.OriginCommanderSlot].Recruited;
                    var identity=Opening.Leaders[unit.OriginCommanderSlot];
                    var appearance=BaseCommanderIdentity.Appearance(identity);
                    if(unit.AppearanceId!=appearance&&!identity.HasCombatProfile)
                    {
                        unit.Stats=BaseCommanderIdentity.Stats(identity,unit.Stats);
                        if(identity.PersonalEquipment?.Version==1)unit.Stats.Damage*=(float)(Opening.SupplySnapshot?.AttackFactor??1);
                    }
                    unit.AppearanceId=appearance;
                    if (!leaders.Add(unit.Id)) continue;
                    var l = identity;
                    if(l.PersonalEquipment?.Version==1||l.HasCombatProfile){unit.Stats=BaseCommanderIdentity.Stats(l,unit.Stats);unit.Stats.Damage*=(float)(Opening.SupplySnapshot?.AttackFactor??1);}
                    else unit.Stats=BaseCommanderIdentity.Stats(l,unit.Stats);
                    unit.Stats.Health = (float)l.MaxHealth; unit.Health = (float)l.Health; unit.ProtectFromInstantDeath = true;
                }
                else if (!troops.ContainsKey(unit.Id))
                {
                    int i = Enumerable.Range(0, Opening.Troops.Count).First(n => Opening.Troops[n].Kind == BaseCampaign.ToBase(unit.Kind) && !troops.ContainsValue(n));
                    troops.Add(unit.Id, i); var w = Opening.Troops[i];
                    unit.Recruited=w.Recruited;
                    if (w.HasCombatProfile) { unit.Stats = w.CombatStats.Clone(); var q = Opening.SupplySnapshot; if (q != null) { unit.Stats.Damage = (unit.Stats.Damage + (float)(w.Kind == BaseTroopKind.Ranged ? q.BowBonus : q.SwordBonus)) * (float)q.AttackFactor; unit.Stats.Defense += (float)q.ArmorBonus; } }
                    unit.Stats.Level=1;unit.Stats.Health = (float)w.MaxHealth; unit.Health = (float)w.Health;
                }
                if(unit.Kind!=SoldierKind.Hero&&troops.TryGetValue(unit.Id,out int sourceIndex))unit.Recruited=Opening.Troops[sourceIndex].Recruited;
                unit.BattlePower = unit.Stats.BaseBattlePower;
            }
            BaseRpg.BindBattle(this);
        }
        public BattleSoldier UnitForTroop(int index) => troops.Where(x => x.Value == index).Select(x => Battle.Soldier(x.Key)).SingleOrDefault();
        private void BindEnemies()
        {
            foreach(var hero in Battle.Soldiers.Where(u=>u.Side==BattleSide.B&&u.Kind==SoldierKind.Hero&&!u.DeveloperSpawned))
            {
                // 决斗、序章、遗迹及匪寨使用对应角色形象；随机敌营已作废（2026-11-02）。
                bool chiefLead=Opening.EventId==BaseDemoDefense.EventId||Opening.EventId==BaseDemoDefense.SecondEventId||Opening.MapId==BaseDemoFinale.Camp;
                hero.AppearanceId=Opening.EventId==BaseLocalDuel.Id?"azhu":Opening.EventId==BaseDemoIntro.BattleId?(Opening.Leaders.First().AppearanceId=="female"?"player":"female"):Opening.EventId==BaseDemoRuins.EventId?"galan":chiefLead?"chief":hero.AppearanceId;
            }
            if(Opening.Enemies.Count==0)return;
            foreach(var unit in Battle.Soldiers.Where(u=>u.Side==BattleSide.B&&!u.DeveloperSpawned).OrderBy(u=>u.Id))
            {
                if(enemies.ContainsKey(unit.Id))continue;
                int index=Enumerable.Range(0,Opening.Enemies.Count).First(n=>Opening.Enemies[n].Kind==unit.Kind&&Opening.Enemies[n].CommanderSlot==unit.OriginCommanderSlot&&!enemies.ContainsValue(n));
                enemies.Add(unit.Id,index);var source=Opening.Enemies[index];unit.Stats=source.Stats.Clone();unit.Health=source.Health;unit.KnockedDown=source.KnockedDown;unit.InstantDead=source.Dead;unit.DeathResolved=source.KnockedDown||source.Dead;unit.BattlePower=unit.Stats.BaseBattlePower;
            }
        }
        public BattleSoldier UnitForEnemy(int index) => enemies.Where(x=>x.Value==index).Select(x=>Battle.Soldier(x.Key)).SingleOrDefault();
        public void SaveCheckpoint(BaseState state) {Bind();if(!state.Campaign.HasActiveBattle||state.Campaign.ActiveBattle.Id!=Opening.Id)throw new InvalidOperationException("本场活动记录不一致");BaseWine.Bind(state,this);if(BaseSceneRules.Active(state))BaseSceneRules.Memory(state).Known=Battle.Vision.KnownTerrain(BattleSide.A);var a=state.Campaign.ActiveBattle;string region=string.IsNullOrEmpty(a.MapRegion)?state.Campaign.Location:a.MapRegion;BaseSceneInstances.SaveBoard(state,BaseSceneCombat.PersistedBoard(state,Battle.Board.Clone(),string.IsNullOrEmpty(a.MapId)?region:a.MapId),region,a.MapId);a.Checkpoint=Battle.Capture();a.HasRealtimeState=Opening.HasRealtimeState;a.RealtimeState=Opening.HasRealtimeState?Opening.RealtimeState?.Copy():null;a.Bindings=troops.Select(x=>new BaseCampaignBinding {SoldierId=x.Key,TroopIndex=x.Value}).ToList();a.EnemyBindings=enemies.Select(x=>new BaseEnemyBinding {SoldierId=x.Key,MemberIndex=x.Value}).ToList();a.HasCheckpoint=true;}
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace BaseConquest.Base
{
    [Serializable] public sealed class BaseDevelopmentState
    {
        public int Version;
        public bool Automatic = true;
        public string Policy = "food";
        public string VillagePolicy = "manual";
        // 2026-11-03：由“每日固定生命”改为“最大生命的固定比例”，比例随营帐等级叠加。
        public double RecoveryFraction = .3;
        // 2026-11-04 裁决：30%～100% 生命预警线，默认 50%；拖到 30%（＝可出战下限）即等同关闭。
        public double WarningThreshold = .5;
        public BaseDevelopmentState Copy() => (BaseDevelopmentState)MemberwiseClone();
    }
    internal static class BaseSettlements
    {
        public static bool AtVillage(BaseState s) => s.Campaign?.CurrentMapId == BaseDemoFinale.Home;
        public static bool AtHomeDoor(BaseState s) => BaseInteraction.At(s,AtVillage(s) ? "custom" : "base","home");
        public const string CampStation = "camp", VillageStation = "village", BlackCrowStation = "bandit";
        public static string Station(BaseWorkerBucket worker) => string.IsNullOrEmpty(worker.StationId)
            ? worker.VillageResident ? VillageStation : CampStation : worker.StationId;
        public static string Station(bool village) => village ? VillageStation : CampStation;
        public static string LocalStation(BaseState state) => state.Campaign?.CurrentMapId == BaseDemoFinale.Camp ? BlackCrowStation :
            Station(AtVillage(state) || state.Campaign?.CurrentMapId == BaseDemoFinale.HomeField);
        public static bool Local(BaseState state, BaseWorkerBucket worker) => Station(worker) == LocalStation(state);
        public static string StationName(BaseState state, BaseWorkerBucket worker)
        {
            if (Station(worker) != BlackCrowStation) return Name(worker.VillageResident);
            return BaseDemoFinale.Later(state) || BaseDemoFinale.OccupiedBySecondDefense(state)
                ? BaseScriptNames.Bandit : BaseScriptNames.Bandit + "·寨外";
        }

        public static void SetStation(BaseWorkerBucket worker, string destination)
        {
            worker.StationId = destination;
            worker.VillageResident = destination == VillageStation;
        }
        public static string Name(bool village) => village ? "白桦村" : "灰岩营地";
        public static bool Available(BaseState s, string facility, bool village)
            => facility == "" || (village ? BaseDemoFinale.HomeRepaired(s, facility) : BaseDemo.FacilityAvailable(s, facility));
        public static string Policy(BaseState s, bool village)
            => (village ? s.Development?.VillagePolicy : s.Development?.Policy) ?? "manual";
        public static void SetPolicy(BaseState s, string policy)
        {
            if (policy != "manual" && policy != "food" && policy != "equipment")
                throw new ArgumentException("未知据点策略");
            RecordPolicy(s,policy);
            Allocate(s);
        }
        internal static void RecordPolicy(BaseState s,string policy)
        {
            bool village = AtVillage(s);
            if (village) s.Development.VillagePolicy = policy;
            else
            {
                s.Development.Policy = policy;
                s.Development.Automatic = policy != "manual";
            }
            foreach (var worker in s.Workers.Where(w => Station(w) == Station(village) && !w.Away))
            {
                worker.DesiredFacility = worker.FacilityId;
                worker.PostLocked = policy == "manual";
            }
        }
        public static void Allocate(BaseState s)
        {
            s.EnsureSoldierNumbers();
            foreach (bool village in new[] { false, true })
            {
                string policy = Policy(s, village);
                var workers = s.Workers.Where(w => Station(w) == Station(village) && !w.Away && !w.TravelingMerchant && w.Count > 0).ToArray();
                if (policy == "manual")
                {
                    var room = s.Muster.Facilities.ToDictionary(f => f.FacilityId,f => f.Capacity);
                    if(room.ContainsKey("caravan"))room["caravan"]-=s.Workers.Where(w=>w.TravelingMerchant&&Station(w)==Station(village)).Sum(w=>w.Count);
                    foreach (var worker in workers.OrderByDescending(w => w.FacilityId != "" && w.FacilityId == w.DesiredFacility))
                    {
                        string post = worker.DesiredFacility;
                        worker.FacilityId = "";
                        if (string.IsNullOrEmpty(post) || !room.TryGetValue(post,out int capacity) || capacity <= 0 ||
                            !BaseDevelopment.Effective(s,worker) || !Available(s,post,village)) continue;
                        int count = Math.Min(worker.Count,capacity);
                        var assigned = worker;
                        if (count < worker.Count) { assigned = worker.Take(0,count); s.Workers.Add(assigned); }
                        assigned.FacilityId = post;
                        room[post] -= count;
                    }
                    continue;
                }
                var available = workers.Where(w => BaseDevelopment.Effective(s,w)).ToList();
                var oldPosts = workers.ToDictionary(w => w, w => w.FacilityId);
                foreach (var w in workers) w.FacilityId = w.DesiredFacility = "";
                foreach (string post in policy == "equipment" ? new[] {"workshop","farm"} : new[] {"farm","workshop"})
                {
                    if (!Available(s, post, village)) continue;
                    int room = s.Muster.Facilities.FirstOrDefault(f => f.FacilityId == post)?.Capacity ?? 0;
                    if (post == "workshop" && s.Inventory.Materials < BaseConstruction.MaterialCost(s,s.WorkshopProduct)) continue;
                    foreach (var w in available.Where(w => w.FacilityId == "" && w.Count > 0)
                        .OrderByDescending(w => BaseDevelopment.Efficiency(w,post))
                        .ThenBy(w => BaseDevelopment.Efficiency(w,post == "farm" ? "workshop" : "farm"))
                        .ThenByDescending(w => oldPosts[w] == post).ToArray())
                    {
                        if (room == 0) break;
                        int take = Math.Min(w.Count,room);
                        var assigned = w;
                        if (take < w.Count) { assigned = w.Take(0,take); s.Workers.Add(assigned); }
                        assigned.FacilityId = assigned.DesiredFacility = post;
                        room -= take;
                    }
                }
            }
            ReassignExcessWorkshopStaff(s);
            BaseDevelopment.Merge(s);
        }
        private static void ReassignExcessWorkshopStaff(BaseState s)
        {
            int capacity = s.Muster.Facilities.FirstOrDefault(f => f.FacilityId == "farm")?.Capacity ?? 0;
            var candidates = s.Workers.Where(w => w.FacilityId == "workshop" && BaseDevelopment.Effective(s,w) &&
                Policy(s,w.VillageResident) == "equipment" && Available(s,"farm",w.VillageResident) &&
                BaseEconomy.WorkersAt(s,"farm",w.VillageResident) < capacity)
                .OrderBy(w => w.WorkshopEfficiency / w.FarmEfficiency).ToArray();
            if (candidates.Length == 0) return;
            var preview = BaseEconomy.ProjectDay(s);
            if (preview.ProductsMade >= preview.WorkCapacity) return;

            foreach (var bucket in candidates)
            {
                int room = capacity - BaseEconomy.WorkersAt(s,"farm",bucket.VillageResident);
                while (room-- > 0 && bucket.Count > 0)
                {
                    var worker = bucket.Take(0,1);
                    s.Workers.Add(worker);
                    worker.FacilityId = worker.DesiredFacility = "farm";
                    if (BaseEconomy.ProjectDay(s).ProductsMade >= preview.ProductsMade) continue;
                    worker.FacilityId = worker.DesiredFacility = "workshop";
                    break;
                }
            }
        }
        public static string TransferProblem(BaseState s, BaseWorkerBucket w)
        {
            if (Station(w) == BlackCrowStation) return "黑鸦寨驻军请通过世界地图调动";
            if (!Available(s,"barracks",true)) return "白桦村缺少兵营";
            if (!Available(s,"barracks",false)) return "灰岩营地缺少兵营";
            if (w.Away || w.TravelingMerchant || w.Health <= 0 || s.Campaign.HasActiveBattle) return "当前无法调动";
            if (!w.Relocating && s.Inventory.Food<BaseEconomy.Ration(s,w)) return "路粮不足";
            return "";
        }
        public static void Order(BaseState s, int number)
        {
            var bucket = s.Workers.First(w => w.SoldierNumbers.Contains(number));
            if (!bucket.Relocating)
            {
                string problem = TransferProblem(s,bucket);
                if (problem.Length > 0) throw new InvalidOperationException(problem);
            }
            var w = bucket.Take(bucket.SoldierNumbers.IndexOf(number),1);
            s.Workers.Add(w);
            if (w.Relocating)
            {
                w.Relocating = false;
                s.Inventory.Food=Math.Min(100000000,s.Inventory.Food+w.PrepaidRoadFood);
                w.PrepaidRoadFood=0;
                var origin = s.Muster.Facilities.FirstOrDefault(f => f.FacilityId == w.DepartureFacility);
                if (origin != null && BaseEconomy.WorkersAt(s,origin.FacilityId,w.VillageResident) < origin.Capacity)
                    w.FacilityId = w.DesiredFacility = w.DepartureFacility;
                w.DepartureFacility = "";
            }
            else
            {
                w.DepartureFacility = w.FacilityId;
                w.FacilityId = w.DesiredFacility = "";
                w.PrepaidRoadFood=BaseEconomy.Ration(s,w);
                s.Inventory.Food-=w.PrepaidRoadFood;
                w.Relocating = true;
            }
            Allocate(s);
        }
        public static void Arrive(BaseState s)
        {
            foreach (var w in s.Workers.Where(w => w.Relocating))
            {
                SetStation(w, Station(!w.VillageResident));
                w.PrepaidRoadFood=0;
                w.Relocating = false;
                w.DepartureFacility = "";
            }
            foreach (var l in s.Leaders.Where(l => l.Relocating))
            {
                l.VillageResident = !l.VillageResident;
                l.Relocating = false;
                l.DesiredFacility = l.FacilityId = l.ArrivalFacility;
                l.ArrivalFacility = "";
                PositionManager(s,l);
            }
            Allocate(s);
        }
        public static BasePoint VillagePost(BaseState s, string id)
        {
            var map = BaseDemoFinale.Home;
            var site = BaseSpatial.Objects(s,map).First(o => o.Id == id);
            return BaseSpatial.Nearest(s,new BasePoint(site.X,site.Y-site.Height/2-.6),map);
        }
        public static void PositionManager(BaseState s, BaseLeader leader)
        {
            if (!BaseDemo.Active(s) || leader.FacilityId == "" || !BaseDemoFinale.Opened(s)) return;
            string map = leader.VillageResident ? BaseDemoFinale.Home : "base";
            bool watching = leader.VillageResident ? AtVillage(s) : s.Campaign.Location == "base";
            string start = watching ? "home" : leader.FacilityId;
            var site = BaseSpatial.Objects(s,map).FirstOrDefault(o => o.Id == start);
            if (site == null) return;
            var point = BaseSpatial.Nearest(s,new BasePoint(site.X,site.Y-site.Height/2-.6),map);
            leader.MapX = point.X; leader.MapY = point.Y;
            leader.MapRegion = leader.VillageResident ? "custom" : "base";
            leader.MapId = leader.VillageResident ? BaseDemoFinale.Home : BaseSpatial.Layout(s,"base").SourceMapId;
            leader.HasMapPosition = true;
        }
    }

    public static class BaseDevelopment
    {
        // P40（2026-11-06）：办公设施建成前只能逐人手动；建成后四档策略。旧 balanced／custom 迁移为自动／手动。
        public static readonly string[] Policies = { "food", "equipment", "auto", "manual" };
        public static bool OfficeBuilt(BaseState s) => BaseFacilityLayout.OfficeBuilt(s);
        public static string EffectivePolicy(BaseState s) => BaseDemo.Active(s) ? BaseSettlements.Policy(s, BaseSettlements.AtVillage(s)) : OfficeBuilt(s) ? (s?.Development?.Policy ?? "manual") : "manual";
        public static bool AutoAssign(BaseState s) => EffectivePolicy(s) != "manual";
        // 策略顺序：粮草优先／军需优先按固定顺序填满；自动分配另行试算配额；手动不自动补位。
        public static string[] PolicyOrder(BaseState s) => BaseDemo.Active(s)
            ? EffectivePolicy(s) == "equipment" ? new[] { "workshop","farm" } : new[] { "farm","workshop" }
            : EffectivePolicy(s) == "equipment"
            ? new[] { "workshop", "farm", "caravan" }
            : new[] { "farm", "workshop", "caravan" };
        // 营帐每升一级额外 +10 个百分点：1／2／3 级 ＝ 30%／40%／50%（比例本身由配表 recovery_per_day 提供）。
        public const double CampRecoveryStep = .1;
        public static double RecoveryRatio(BaseState s)
        {
            int level = s?.Demo?.Rpg == null ? 1 : Math.Max(1, Math.Min(3, s.Demo.Rpg.CampLevel));
            double baseValue = s?.Development?.RecoveryFraction ?? .3;
            return Math.Max(.01, Math.Min(1, baseValue + CampRecoveryStep * (level - 1)));
        }
        // 2026-11-04 最新裁决（取代伤员三档的 50%／20%）：士兵生命 <30% 不可工作、不可主动出战；
        // 工作、进攻和防守均要求至少 30% 生命。存活士兵仍可随军转移。
        // 队长不受血量阈值限制；恢复仍是营内全员每日自动，Resting 是「重伤闲置」的派生标记。
        public const double DutyThreshold = .3;
        public const double WarningDefault = .5, WarningFloor = .3, WarningCeiling = 1;
        public static bool HeavilyWounded(BaseWorkerBucket w) => w.Health > 0 && w.Health < w.MaxHealth * DutyThreshold;
        public static void MarkFoughtToday(BaseState s, IEnumerable<int> soldierNumbers)
        {
            var numbers = soldierNumbers.ToHashSet();
            if (numbers.Count == 0) return;
            s.EnsureSoldierNumbers();
            foreach (var worker in s.Workers.ToArray())
            {
                if (worker.LastBattleDay == s.Day) continue;
                var deployed = worker.SoldierNumbers.Where(numbers.Contains).ToList();
                if (deployed.Count == 0) continue;
                var participant = worker;
                if (deployed.Count < worker.Count)
                {
                    participant = worker.TakeNumbers(deployed);
                    s.Workers.Add(participant);
                }
                participant.LastBattleDay = s.Day;
            }
            Allocate(s);
        }
        public static bool Effective(BaseState s, BaseWorkerBucket w) => BaseSettlements.Station(w) != BaseSettlements.BlackCrowStation && !w.TravelingMerchant && w.LastBattleDay != s.Day && !w.Relocating && !w.Away && w.Health > 0 && w.Health >= w.MaxHealth * DutyThreshold;
        public static bool CanManage(BaseState s, BaseLeader l) => !l.Relocating && !l.Away && !l.WaitingAtRecruitment && BaseAdventure.Alive(l) && !BaseAdventure.Contains(s,l.Id) && l.Health > 0;
        public static bool Effective(BaseState s, BaseLeader l) => CanManage(s,l);
        public static bool CanMarch(BaseWorkerBucket w) => !w.TravelingMerchant && !w.Relocating && !w.Away && w.Health > 0;
        public static bool CanFight(BaseWorkerBucket w) => CanMarch(w) && w.Health >= w.MaxHealth * DutyThreshold;
        public static bool CanDefend(BaseWorkerBucket w) => CanFight(w);
        public static bool Eligible(BaseState s, string purpose, BaseWorkerBucket w) => BaseSettlements.Local(s,w) && Eligible(purpose,w);
        public static bool Eligible(string purpose, BaseWorkerBucket w) => purpose == "defense" ? CanDefend(w) : CanFight(w);
        public static bool LocalLeader(BaseState s,BaseLeader l) => !BaseDemo.Active(s) || BaseAdventure.Contains(s,l.Id) || l.VillageResident == (BaseSettlements.AtVillage(s) || s.Campaign?.CurrentMapId == BaseDemoFinale.HomeField);
        public static bool CanFight(BaseLeader l) => !l.Relocating && !l.Away && BaseAdventure.Alive(l) && l.Health > 0;
        // 预警线：0 表示旧档未写该字段，回落到默认 50%；拖到 30% 即关闭（低无可低）。
        public static double WarningThreshold(BaseState s)
        {
            double value = s?.Development?.WarningThreshold ?? 0;
            if (double.IsNaN(value) || double.IsInfinity(value) || value <= 0) return WarningDefault;
            return Math.Max(WarningFloor, Math.Min(WarningCeiling, value));
        }
        public static bool WarningOff(BaseState s) => WarningThreshold(s) <= DutyThreshold + 1e-9;
        public static bool BelowWarning(BaseState s, BaseWorkerBucket w) => !WarningOff(s) && w.MaxHealth > 0 && w.Health > 0 && w.Health < w.MaxHealth * WarningThreshold(s);
        public static void SetWarningThreshold(BaseState s, double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) throw new ArgumentException("生命预警线非法");
            s.Development.WarningThreshold = Math.Max(WarningFloor, Math.Min(WarningCeiling, value));
        }
        // 兵力详情四分类：总数／可工作／可主动出战／重伤。出场人物与训练不影响士兵统计。
        public static int[] Tally(BaseState s, BaseTroopKind? kind = null)
        {
            var buckets = s.Workers.Where(w => w != null && (kind == null || w.Kind == kind)).ToArray();
            return new[] { buckets.Sum(w => w.Count), buckets.Where(w => Effective(s, w)).Sum(w => w.Count), buckets.Where(CanFight).Sum(w => w.Count), buckets.Where(w => !w.Away && HeavilyWounded(w)).Sum(w => w.Count) };
        }
        public static double Efficiency(BaseWorkerBucket w, string facility) => facility == "farm" ? w.FarmEfficiency : w.WorkshopEfficiency;
        public static double Bonus(BaseLeader l, string facility) => BaseRpg.ProductionBonus(l,facility);
        public static string PolicyName(string id) => id == "food" ? "粮食优先" : id == "equipment" ? "军需优先" : id == "auto" ? "自动分配" : "手动分配";
        public static string ProfileKey(bool has, BaseConquest.Battle.SoldierStats stats) => !has ? "default" : stats == null ? "invalid" : string.Join(":",new[] {stats.Health,stats.Damage,stats.Defense,stats.CritChance,stats.CritMultiplier,stats.Penetration,stats.Resistance}.Select(x=>x.ToString("R",CultureInfo.InvariantCulture)))+":"+stats.Range+":"+stats.MoveDistance+":"+stats.HasCombatKind+":"+stats.CombatKind+":"+stats.HasProfessionKind+":"+stats.ProfessionKind+":"+stats.Level + (stats.SightRadius == 0 ? "" : ":sight=" + stats.SightRadius);
        public static void Normalize(BaseState s)
        {
            var d = s.Development;
            if (d != null && string.IsNullOrEmpty(d.VillagePolicy)) d.VillagePolicy = "manual";
            // 旧档迁移：Resting 由「玩家手动休养」改为「重伤闲置」派生标记，统一按 <30% 重算（幂等）。
            // 只重算营内工人：外出幸存者在返营流程里先清 Away 再置标记，这里抢先置会构成「外出且休养」非法态。
            foreach (var w in s.Workers.Where(x => x != null)) if (w.MaxHealth > 0 && w.Health > 0 && !w.Away) w.Resting = w.Health < w.MaxHealth * DutyThreshold;
            // 预警线是 P38 新增字段：旧档没写过（0）就补默认 50%，不把「未设置」当成「拖到最低」。
            if (d != null && (double.IsNaN(d.WarningThreshold) || double.IsInfinity(d.WarningThreshold) || d.WarningThreshold <= 0)) d.WarningThreshold = WarningDefault;
            // 版本 1 的旧档存的是“每日固定生命”（旧量纲），升级为按最大生命比例，比例取默认 30%。
            if (d != null && d.Version == 1) { d.Version = 2; if (!(d.RecoveryFraction > 0 && d.RecoveryFraction <= 1)) d.RecoveryFraction = .3; }
            // 版本 2 的四档旧策略在 P40 换名：均衡→自动分配、自定优先→手动分配；其余保持，未知值回落手动。
            if (d != null && d.Version == 2)
            {
                d.Version = 3;
                if (d.Policy == "balanced") d.Policy = "auto";
                else if (d.Policy == "custom" || string.IsNullOrEmpty(d.Policy) || !Policies.Contains(d.Policy)) d.Policy = "manual";
            }
            if (d != null && d.Version != 0) return;
            // 旧进度保留手动意向，不在读档时偷偷把伙伴和工作兵全部重排。
            s.Development = new BaseDevelopmentState { Version = 3, Automatic = false, Policy = "manual" };
            foreach (var l in s.Leaders.Where(x => x != null))
            {
                if (l.MaxHealth == 0) { l.MaxHealth = 200; l.Health = 200; }
                l.DesiredFacility = l.FacilityId ?? ""; l.PostLocked = l.DesiredFacility != "";
            }
            foreach (var w in s.Workers.Where(x => x != null))
            {
                if (w.MaxHealth == 0) { w.MaxHealth = w.Kind == BaseTroopKind.Ranged ? 65 : w.Kind == BaseTroopKind.Cavalry ? 120 : 100; w.Health = w.MaxHealth; }
                if (w.FarmEfficiency == 0) w.FarmEfficiency = 1; if (w.WorkshopEfficiency == 0) w.WorkshopEfficiency = 1;
                w.DesiredFacility = w.FacilityId ?? ""; w.PostLocked = w.DesiredFacility != "";
            }
            BaseMuster.Normalize(s);
            foreach (var f in s.Muster.Facilities)
                if (f.Capacity == 10000) f.Capacity = Math.Max(f.MinimumWorkers, Math.Max(6, s.Workers.Where(w => !w.Away && w.FacilityId == f.FacilityId).Sum(w => w.Count)));
        }
        private static bool Number(double n, double max) => !double.IsNaN(n) && !double.IsInfinity(n) && n >= 0 && n <= max;
        public static void Validate(BaseState s)
        {
            var d = s.Development;
            if (BaseDemo.Active(s) && (d.VillagePolicy != "manual" && d.VillagePolicy != "food" && d.VillagePolicy != "equipment" ||
                s.Workers.Any(w => !string.IsNullOrEmpty(w.StationId) &&
                    (w.StationId != BaseSettlements.CampStation && w.StationId != BaseSettlements.VillageStation && w.StationId != BaseSettlements.BlackCrowStation ||
                    w.VillageResident != (w.StationId == BaseSettlements.VillageStation) ||
                    w.StationId == BaseSettlements.BlackCrowStation && (w.FacilityId != "" || w.DesiredFacility != "" || w.PostLocked || w.Relocating || w.TravelingMerchant))) ||
                s.Workers.Any(w => w.PrepaidRoadFood<0 || w.PrepaidRoadFood>100 || !w.Relocating&&w.PrepaidRoadFood!=0) ||
                s.Workers.Any(w => w.Relocating && (w.Away || w.FacilityId != "" || w.DesiredFacility != "" || !BaseEconomy.Facilities.Contains(w.DepartureFacility))) ||
                s.Leaders.Any(l => l.Relocating && (l.Away || l.FacilityId != "" || !BaseEconomy.Facilities.Contains(l.ArrivalFacility)))))
                throw new ArgumentException("据点策略或人员调令非法");
            if (d.Version != 3 || !Policies.Contains(d.Policy) || !Number(d.RecoveryFraction, 1) || d.RecoveryFraction <= 0 || !Number(d.WarningThreshold, 1)) throw new ArgumentException("发展策略或回血规则非法");
            foreach (var l in s.Leaders)
            { if(l.HasCombatProfile&&(!BaseAftermath.ValidStats(l.CombatStats)||l.PersonalEquipment?.Version!=1&&l.CombatStats.Health!=l.MaxHealth)) throw new ArgumentException("人物战斗属性非法");
                if (!Number(l.MaxHealth, 100000) || l.MaxHealth < 1 || !Number(l.Health, l.MaxHealth) || !BaseEconomy.Facilities.Contains(l.DesiredFacility) || !Number(l.FarmBonus, 1) || !Number(l.WorkshopBonus, 1) || l.Resting && (l.Away || l.Health <= 0)) throw new ArgumentException("队长健康／岗位意向非法");
            }
            foreach (var w in s.Workers)
            { if(!string.IsNullOrEmpty(w.TemplateId)&&BaseRpg.Active(s)&&!s.Demo.Rpg.Templates.Any(t=>t.Id==w.TemplateId&&s.Demo.Rpg.MatchesTemplate(t.Id,BaseRpg.Profession(BaseCampaign.ToBattle(w.Kind)))&&s.Demo.Rpg.TemplateIdentity(t.Id)=="soldier"))throw new ArgumentException("士兵模板与兵种不符");
              if(w.HasCombatProfile&&(!BaseAftermath.ValidStats(w.CombatStats)||w.CombatStats.Health!=w.MaxHealth)) throw new ArgumentException("工兵战斗属性非法");
                if (!Number(w.MaxHealth, 100000) || w.MaxHealth <= 0 || !Number(w.Health, w.MaxHealth) || !BaseEconomy.Facilities.Contains(w.DesiredFacility) || !Number(w.FarmEfficiency, 10) || !Number(w.WorkshopEfficiency, 10) || w.FarmEfficiency <= 0 || w.WorkshopEfficiency <= 0 || w.Resting && (w.Away || w.Health <= 0)) throw new ArgumentException("工人健康／岗位意向非法");
            }
        }
        public static void SetPolicy(BaseState s, string policy)
        {
            if (BaseDemo.Active(s)) { BaseSettlements.SetPolicy(s, policy); return; }
            if (!Policies.Contains(policy)) throw new ArgumentException("未知发展策略");
            if (policy != "manual" && !OfficeBuilt(s)) throw new InvalidOperationException("办公设施未建成，只能逐人手动安排");
            s.Development.Policy = policy;
            if (s.Layout != null) s.Layout.PausedPolicy = "";
            if (policy == "food" || policy == "equipment")
                foreach (var f in s.Muster.Facilities)
                    f.Priority = (policy == "food") == (f.FacilityId == "farm") ? 2 : f.FacilityId == "caravan" ? 1 : 0;
            s.Development.Automatic = AutoAssign(s);
            Allocate(s);
        }
        public static void SetAutomatic(BaseState s, bool enabled) => SetPolicy(s, enabled ? "auto" : "manual");
        internal static void PausePolicy(BaseState s)
        {
            if (s.Development.Policy == "manual") return;
            if (s.Layout != null) s.Layout.PausedPolicy = s.Development.Policy;
            s.Development.Policy = "manual";
            s.Development.Automatic = false;
            foreach (var worker in s.Workers.Where(w => !w.Away && !w.PostLocked)) worker.DesiredFacility = worker.FacilityId;
            foreach (var leader in s.Leaders.Where(l => !l.Relocating && !l.Away && !l.PostLocked)) leader.DesiredFacility = leader.FacilityId;
        }
        public static void Allocate(BaseState s)
        {
            s.EnsureSoldierNumbers();
            Normalize(s);
            if (BaseDemo.Active(s)) { BaseSettlements.Allocate(s); return; }
            bool auto = AutoAssign(s);
            var order = PolicyOrder(s);
            int Rank(string id) { int index = Array.IndexOf(order, id); return index < 0 ? 9 : index; }
            var facilities = s.Muster.Facilities.Where(facility=>BaseDemo.FacilityAvailable(s,facility.FacilityId))
                .OrderBy(f => Rank(f.FacilityId)).ThenBy(f => f.FacilityId, StringComparer.Ordinal).ToArray();
            int Capacity(BaseFacilityProtection f)=>f.FacilityId=="workshop"&&s.Muster.Parties.Any(p=>p.PriorityMode=="production"&&p.ConserveMaterials) ? Math.Min(f.Capacity,Math.Max(1,f.MinimumWorkers)) : f.Capacity;
            bool Productive(string id) => id != "workshop" || s.Inventory.Materials >= BaseConstruction.MaterialCost(s,s.WorkshopProduct);
            var oldJobs = s.Workers.ToDictionary(w => w, w => w.FacilityId);
            var oldLeaders = s.Leaders.ToDictionary(l => l, l => l.FacilityId);
            foreach (var w in s.Workers) w.FacilityId = "";
            foreach (var l in s.Leaders) l.FacilityId = "";
            var assigned = new List<BaseWorkerBucket>(); var remaining = s.Workers.ToDictionary(w => w, w => w.Count);
            void Put(BaseWorkerBucket source, string id, int n)
            {
                if (n <= 0) return;
                var w = source.Slice(source.Count - remaining[source], n); w.FacilityId = id; assigned.Add(w); remaining[source] -= n;
            }
            int Assigned(string id) => assigned.Where(w => w.FacilityId == id).Sum(w => w.Count);
            double ChiefBonus(string id) => 1 + (s.Leaders.FirstOrDefault(l => l.FacilityId == id) is BaseLeader chief ? Bonus(chief, id) : 0);
            double FarmPerWorker() => s.Rules.FarmPerWorker * ChiefBonus("farm") * BaseEconomy.ProductionFactor(s);
            // 自动分配（§11.1A）：先保证粮食不赤字，再按三天原料试算工坊，最后才给商队排人。
            int Quota(BaseFacilityProtection f)
            {
                int capacity = Capacity(f) - Assigned(f.FacilityId);
                if (!auto) return capacity;
                if (f.FacilityId == "farm")
                {
                    double per = FarmPerWorker();
                    if (per <= 0) return 0;
                    int need = BaseEconomy.FoodRequired(s) - (int)Math.Floor(s.FarmFraction);
                    return Math.Max(0, Math.Min(capacity, (int)Math.Ceiling(Math.Max(0, need) / per)));
                }
                if (f.FacilityId == "workshop")
                {
                    double per = s.Rules.WorkshopPerWorker * ChiefBonus("workshop") * BaseEconomy.ProductionFactor(s) / BaseConstruction.ProductLevel(s);
                    int cost = BaseConstruction.MaterialCost(s, s.WorkshopProduct);
                    if (per <= 0 || cost <= 0) return 0;
                    return Math.Max(0, Math.Min(capacity, (int)Math.Floor(s.Inventory.Materials / (per * cost * 3d))));
                }
                if (f.FacilityId == "caravan")
                {
                    // 商队不产粮且吃双份：只在不造成粮食赤字时安排；全员种粮仍赤字就一个也不给。
                    double food = s.FarmFraction + Assigned("farm") * FarmPerWorker();
                    return Math.Max(0, Math.Min(capacity, (int)Math.Floor((food - BaseEconomy.FoodRequired(s)) / 2d)));
                }
                return capacity;
            }
            foreach (var f in facilities)
            {
                int capacity = Capacity(f);
                var locked = s.Workers.Where(w => Effective(s, w) && w.PostLocked && w.DesiredFacility == f.FacilityId)
                    .OrderByDescending(w => oldJobs[w] == f.FacilityId);
                foreach (var w in locked) { int n = Math.Min(capacity, remaining[w]); Put(w, f.FacilityId, n); capacity -= n; }
                var chief = s.Leaders.Where(l => CanManage(s, l) && l.PostLocked && l.DesiredFacility == f.FacilityId)
                    .OrderByDescending(l => oldLeaders[l] == f.FacilityId).ThenBy(l => l.Id, StringComparer.Ordinal).FirstOrDefault();
                if (chief != null) chief.FacilityId = f.FacilityId;
            }
            if(auto&&s.Muster.Parties.Any(p=>p.PriorityMode=="production"))
                foreach(var f in facilities)
                {
                    int missing=Math.Min(Capacity(f),Math.Max(1,f.MinimumWorkers))-assigned.Where(w=>w.FacilityId==f.FacilityId).Sum(w=>w.Count);
                    foreach(var w in s.Workers.Where(w=>Effective(s,w)&&!w.PostLocked).OrderByDescending(w=>Efficiency(w,f.FacilityId)).ThenByDescending(w=>oldJobs[w]==f.FacilityId).ThenBy(w=>(int)w.Kind))
                    {int n=Math.Min(missing,remaining[w]);Put(w,f.FacilityId,n);missing-=n;if(missing<=0)break;}
                }
            foreach (var f in facilities)
            {
                int capacity = auto ? Quota(f) : Capacity(f) - Assigned(f.FacilityId);
                if (!auto)
                {
                    foreach (var w in s.Workers.Where(w => Effective(s, w) && !w.PostLocked && w.DesiredFacility == f.FacilityId))
                    { int n = Math.Min(capacity, remaining[w]); Put(w, f.FacilityId, n); capacity -= n; }
                    foreach (var l in s.Leaders.Where(l => CanManage(s, l) && !l.PostLocked && l.DesiredFacility == f.FacilityId && l.FacilityId == ""))
                        if (!s.Leaders.Any(x => x.FacilityId == f.FacilityId)) l.FacilityId = f.FacilityId;
                    continue;
                }
                if (!Productive(f.FacilityId)) continue;
                foreach (var w in s.Workers.Where(w => Effective(s, w) && !w.PostLocked)
                    .OrderByDescending(w => Efficiency(w, f.FacilityId)).ThenByDescending(w => oldJobs[w] == f.FacilityId).ThenBy(w => (int)w.Kind))
                { int n = Math.Min(capacity, remaining[w]); Put(w, f.FacilityId, n); capacity -= n; if (capacity == 0) break; }
                if (!s.Leaders.Any(l => l.FacilityId == f.FacilityId))
                {
                    var chief = s.Leaders.Where(l => l.Id != s.PlayerLeaderId && CanManage(s, l) && !l.PostLocked && l.FacilityId == "")
                        .OrderByDescending(l => Bonus(l, f.FacilityId)).ThenByDescending(l => oldLeaders[l] == f.FacilityId).ThenBy(l => l.Id, StringComparer.Ordinal).FirstOrDefault();
                    if (chief != null) chief.FacilityId = f.FacilityId;
                }
            }
            foreach (var w in s.Workers) Put(w, "", remaining[w]);
            s.Workers = assigned;
            Merge(s);
        }
        public static void Merge(BaseState s)
        {
            var groups = new Dictionary<string, BaseWorkerBucket>(); var merged = new List<BaseWorkerBucket>();
            foreach (var w in s.Workers.Where(w => w.Count > 0))
            {
                if (w.SoldierNumbers == null || w.SoldierNumbers.Count == 0) s.EnsureSoldierNumbers();
                string key = w.Kind + "|" + w.FacilityId + "|" + w.DesiredFacility + "|" + w.PostLocked + "|" + w.Resting + "|" + w.Away + "|" + w.PartyId + "|" + w.SourceId + "|" + w.UnavailableDay + "|" + w.Health.ToString("R", CultureInfo.InvariantCulture) + "|" + w.MaxHealth.ToString("R", CultureInfo.InvariantCulture) + "|" + w.FarmEfficiency.ToString("R", CultureInfo.InvariantCulture) + "|" + w.WorkshopEfficiency.ToString("R", CultureInfo.InvariantCulture);
                key += "|" + w.LastBattleDay + "|" + BaseSettlements.Station(w) + "|" + w.Relocating + "|" + w.PrepaidRoadFood + "|" + w.TravelingMerchant + "|" + w.DepartureFacility;
                key += "|" + w.Recruited + "|" + ProfileKey(w.HasCombatProfile,w.CombatStats) + "|" + w.HeldBy + "|" + w.HeldSourceId + "|" + w.PartyOrder;
                if (groups.TryGetValue(key, out var found)) { found.SoldierNumbers.AddRange(w.SoldierNumbers); found.Count += w.Count; }
                else { groups.Add(key, w); merged.Add(w); }
            }
            s.Workers = merged;
        }
        public static bool RestLeader(BaseState s, string id, bool resting)
        {
            // 保留旧入口供调用者拒绝旧操作；队长恢复已不再是一项占用岗位的职责。
            return false;
        }
        public static bool RestWorkers(BaseState s, int index, int count, bool resting)
        {
            // 2026-11-04 选A：休养不再是玩家操作——恢复全员自动、Resting 由血量派生。旧入口保留但一律拒绝。
            return false;
        }
        public static int Recover(BaseState s)
        {
            int count = 0;
            double ratio = RecoveryRatio(s);
            foreach (var l in s.Leaders.Where(l => BaseAdventure.Alive(l) && l.Health > 0 && l.Health < l.MaxHealth))
            { l.Health = Math.Min(l.MaxHealth, l.Health + l.MaxHealth * ratio); l.Resting = false; count++; }
            // 2026-11-04 选A：恢复不再是休养专属——营内所有伤兵每天自动恢复；Resting 只作重伤闲置标记。
            foreach (var w in s.Workers.Where(w => !w.TravelingMerchant && !w.Relocating && !w.Away && w.Health > 0 && w.Health < w.MaxHealth))
            { w.Health = Math.Min(w.MaxHealth, w.Health + w.MaxHealth * ratio); count += w.Count; w.Resting = w.Health < w.MaxHealth * DutyThreshold; }
            return count;
        }
    }
}

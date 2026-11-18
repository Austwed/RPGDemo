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
        // 2026-11-03：由“每日固定生命”改为“最大生命的固定比例”，比例随营帐等级叠加。
        public double RecoveryFraction = .3;
        // 2026-11-04 裁决：30%～100% 生命预警线，默认 50%；拖到 30%（＝可出战下限）即等同关闭。
        public double WarningThreshold = .5;
        public BaseDevelopmentState Copy() => (BaseDevelopmentState)MemberwiseClone();
    }
    public static class BaseDevelopment
    {
        // P40（2026-11-06）：办公设施建成前只能逐人手动；建成后四档策略。旧 balanced／custom 迁移为自动／手动。
        public static readonly string[] Policies = { "food", "equipment", "auto", "manual" };
        public static bool OfficeBuilt(BaseState s) => BaseFacilityLayout.OfficeBuilt(s);
        public static string EffectivePolicy(BaseState s) => OfficeBuilt(s) && !BaseDemo.ExperimentalManagementHidden(s) ? (s?.Development?.Policy ?? "manual") : "manual";
        public static bool AutoAssign(BaseState s) => EffectivePolicy(s) != "manual";
        // 策略顺序：粮草优先／军需优先按固定顺序填满；自动分配另行试算配额；手动不自动补位。
        public static string[] PolicyOrder(BaseState s) => EffectivePolicy(s) == "equipment"
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
        // ≥30% 可工作也可主动出战。遇袭防守例外：生命仍 >0 就能参加迎敌战场。
        // 队长不受血量阈值限制；恢复仍是营内全员每日自动，Resting 是「重伤闲置」的派生标记。
        public const double DutyThreshold = .3;
        public const double WarningDefault = .5, WarningFloor = .3, WarningCeiling = 1;
        public static bool HeavilyWounded(BaseWorkerBucket w) => w.Health > 0 && w.Health < w.MaxHealth * DutyThreshold;
        public static bool Effective(BaseState s, BaseWorkerBucket w) => !w.Away && w.Health > 0 && w.Health >= w.MaxHealth * DutyThreshold;
        public static bool CanManage(BaseState s, BaseLeader l) => !l.Away && BaseAdventure.Alive(l) && !BaseAdventure.Contains(s,l.Id) && !BaseTraining.Busy(l.Training) && l.Health > 0;
        public static bool Effective(BaseState s, BaseLeader l) => CanManage(s,l);
        public static bool CanFight(BaseWorkerBucket w) => !w.Away && w.Health > 0 && w.Health >= w.MaxHealth * DutyThreshold;
        // 遇袭防守例外：守家时只要还能行动就上，不受 30% 门槛限制。
        public static bool CanDefend(BaseWorkerBucket w) => !w.Away && w.Health > 0;
        // 编队资格：防守用途放宽到 CanDefend，其余用途一律 CanFight。
        public static bool Eligible(string purpose, BaseWorkerBucket w) => purpose == "defense" ? CanDefend(w) : CanFight(w);
        public static bool CanFight(BaseLeader l) => !l.Away && BaseAdventure.Alive(l) && !BaseTraining.Busy(l.Training) && l.Health > 0;
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
        public static string ProfileKey(bool has, BaseConquest.Battle.SoldierStats stats) => !has ? "default" : stats == null ? "invalid" : string.Join(":",new[] {stats.Health,stats.Damage,stats.Defense,stats.CritChance,stats.CritMultiplier,stats.Penetration,stats.Resistance}.Select(x=>x.ToString("R",CultureInfo.InvariantCulture)))+":"+stats.Range+":"+stats.MoveDistance+":"+stats.HasCombatKind+":"+stats.CombatKind+":"+stats.HasProfessionKind+":"+stats.ProfessionKind+":"+stats.Level;
        public static void Normalize(BaseState s)
        {
            var d = s.Development;
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
            if (d != null && d.Version != 0)
            {
                if (d.Version == 3 && d.Policy != "manual" && Policies.Contains(d.Policy) && BaseDemo.ExperimentalManagementHidden(s))
                    PausePolicy(s);
                return;
            }
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
            if (d.Version != 3 || !Policies.Contains(d.Policy) || !Number(d.RecoveryFraction, 1) || d.RecoveryFraction <= 0 || !Number(d.WarningThreshold, 1)) throw new ArgumentException("发展策略或回血规则非法");
            foreach (var l in s.Leaders)
            { if(l.HasCombatProfile&&(!BaseAftermath.ValidStats(l.CombatStats)||l.PersonalEquipment?.Version!=1&&l.CombatStats.Health!=l.MaxHealth)) throw new ArgumentException("人物战斗属性非法");
                if (!Number(l.MaxHealth, 100000) || l.MaxHealth < 1 || !Number(l.Health, l.MaxHealth) || !BaseEconomy.Facilities.Contains(l.DesiredFacility) || !Number(l.FarmBonus, 1) || !Number(l.WorkshopBonus, 1) || l.Resting && (l.Away || l.Health <= 0)) throw new ArgumentException("队长健康／岗位意向非法");
            }
            foreach (var w in s.Workers)
            { if(w.HasCombatProfile&&(!BaseAftermath.ValidStats(w.CombatStats)||w.CombatStats.Health!=w.MaxHealth)) throw new ArgumentException("工兵战斗属性非法");
                if (!Number(w.MaxHealth, 100000) || w.MaxHealth <= 0 || !Number(w.Health, w.MaxHealth) || !BaseEconomy.Facilities.Contains(w.DesiredFacility) || !Number(w.FarmEfficiency, 10) || !Number(w.WorkshopEfficiency, 10) || w.FarmEfficiency <= 0 || w.WorkshopEfficiency <= 0 || w.Resting && (w.Away || w.Health <= 0)) throw new ArgumentException("工人健康／岗位意向非法");
            }
        }
        public static void SetPolicy(BaseState s, string policy)
        {
            if (!Policies.Contains(policy)) throw new ArgumentException("未知发展策略");
            if (policy != "manual" && BaseDemo.ExperimentalManagementHidden(s)) throw new InvalidOperationException("当前只能手动安排岗位");
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
            foreach (var leader in s.Leaders.Where(l => !l.Away && !l.PostLocked)) leader.DesiredFacility = leader.FacilityId;
        }
        public static void Allocate(BaseState s)
        {
            Normalize(s);
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
                var w = source.Copy(); w.Count = n; w.FacilityId = id; assigned.Add(w); remaining[source] -= n;
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
                string key = w.Kind + "|" + w.FacilityId + "|" + w.DesiredFacility + "|" + w.PostLocked + "|" + w.Resting + "|" + w.Away + "|" + w.PartyId + "|" + w.SourceId + "|" + w.UnavailableDay + "|" + w.Health.ToString("R", CultureInfo.InvariantCulture) + "|" + w.MaxHealth.ToString("R", CultureInfo.InvariantCulture) + "|" + w.FarmEfficiency.ToString("R", CultureInfo.InvariantCulture) + "|" + w.WorkshopEfficiency.ToString("R", CultureInfo.InvariantCulture);
                key += "|" + w.Recruited + "|" + ProfileKey(w.HasCombatProfile,w.CombatStats);
                if (groups.TryGetValue(key, out var found)) found.Count += w.Count;
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
            foreach (var w in s.Workers.Where(w => !w.Away && w.Health > 0 && w.Health < w.MaxHealth))
            { w.Health = Math.Min(w.MaxHealth, w.Health + w.MaxHealth * ratio); count += w.Count; w.Resting = w.Health < w.MaxHealth * DutyThreshold; }
            return count;
        }
    }
}

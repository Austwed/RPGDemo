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
        public double RecoveryPerDay = 20;
        public BaseDevelopmentState Copy() => (BaseDevelopmentState)MemberwiseClone();
    }
    public static class BaseDevelopment
    {
        public static readonly string[] Policies = { "food", "equipment", "balanced", "custom" };
        public static bool Effective(BaseState s, BaseWorkerBucket w) => !w.Away && !w.Resting && w.Health > 0 && w.UnavailableDay != s.Day;
        public static bool CanManage(BaseState s, BaseLeader l) => !l.Away && BaseAdventure.Alive(l) && !BaseAdventure.Contains(s,l.Id) && !BaseTraining.Busy(l.Training) && l.Health > 0;
        public static bool Effective(BaseState s, BaseLeader l) => CanManage(s,l) && l.UnavailableDay != s.Day;
        public static bool CanFight(BaseWorkerBucket w) => !w.Away && !w.Resting && w.Health > 0;
        public static bool CanFight(BaseLeader l) => !l.Away && BaseAdventure.Alive(l) && !BaseTraining.Busy(l.Training) && l.Health > 0;
        public static double Efficiency(BaseWorkerBucket w, string facility) => facility == "farm" ? w.FarmEfficiency : w.WorkshopEfficiency;
        public static double Bonus(BaseLeader l, string facility) => BaseRpg.ProductionBonus(l,facility);
        public static string PolicyName(string id) => id == "food" ? "粮草优先" : id == "equipment" ? "军需优先" : id == "balanced" ? "均衡" : "自定优先";
        public static string ProfileKey(bool has, BaseConquest.Battle.SoldierStats stats) => !has ? "default" : stats == null ? "invalid" : string.Join(":",new[] {stats.Health,stats.Damage,stats.Defense,stats.CritChance,stats.CritMultiplier,stats.Penetration,stats.Resistance}.Select(x=>x.ToString("R",CultureInfo.InvariantCulture)))+":"+stats.Range+":"+stats.MoveDistance+":"+stats.HasCombatKind+":"+stats.CombatKind+":"+stats.HasProfessionKind+":"+stats.ProfessionKind+":"+stats.Level;
        public static void Normalize(BaseState s)
        {
            var d = s.Development;
            if (d != null && d.Version != 0) return;
            // 旧进度保留手动意向，不在读档时偷偷把伙伴和工作兵全部重排。
            s.Development = new BaseDevelopmentState { Version = 1, Automatic = false };
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
            if (d.Version != 1 || !Policies.Contains(d.Policy) || !Number(d.RecoveryPerDay, 10000) || d.RecoveryPerDay <= 0) throw new ArgumentException("发展策略或回血规则非法");
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
            s.Development.Policy = policy;
            if (policy != "custom") foreach (var f in s.Muster.Facilities) f.Priority = policy == "balanced" ? 1 : (policy == "food") == (f.FacilityId == "farm") ? 2 : 0;
            Allocate(s);
        }
        public static void SetAutomatic(BaseState s, bool enabled)
        {
            if (!enabled && s.Development.Automatic)
            {
                foreach (var w in s.Workers.Where(w => w.FacilityId != "" && !w.PostLocked)) w.DesiredFacility = w.FacilityId;
                foreach (var l in s.Leaders.Where(l => l.FacilityId != "" && !l.PostLocked)) l.DesiredFacility = l.FacilityId;
            }
            s.Development.Automatic = enabled; Allocate(s);
        }
        public static void Allocate(BaseState s)
        {
            Normalize(s);
            var facilities = s.Muster.Facilities.Where(facility=>BaseDemo.FacilityAvailable(s,facility.FacilityId)).OrderByDescending(f => f.Priority).ThenBy(f => f.FacilityId, StringComparer.Ordinal).ToArray();
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
            if(s.Development.Automatic&&s.Muster.Parties.Any(p=>p.PriorityMode=="production"))
                foreach(var f in facilities)
                {
                    int missing=Math.Min(Capacity(f),Math.Max(1,f.MinimumWorkers))-assigned.Where(w=>w.FacilityId==f.FacilityId).Sum(w=>w.Count);
                    foreach(var w in s.Workers.Where(w=>Effective(s,w)&&!w.PostLocked).OrderByDescending(w=>Efficiency(w,f.FacilityId)).ThenByDescending(w=>oldJobs[w]==f.FacilityId).ThenBy(w=>(int)w.Kind))
                    {int n=Math.Min(missing,remaining[w]);Put(w,f.FacilityId,n);missing-=n;if(missing<=0)break;}
                }
            foreach (var f in facilities)
            {
                int capacity = Capacity(f) - assigned.Where(w => w.FacilityId == f.FacilityId).Sum(w => w.Count);
                if (!s.Development.Automatic)
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
            if (index < 0 || index >= s.Workers.Count || count <= 0) return false;
            var source = s.Workers[index];
            if (source.Away || source.Health <= 0 || count > source.Count || resting && source.Health >= source.MaxHealth) return false;
            var w = source.Copy(); source.Count -= count; w.Count = count; w.Resting = resting;
            if (source.Resting || resting) w.UnavailableDay = s.Day;
            if (resting) w.FacilityId = "";
            s.Workers.Add(w); Allocate(s); return true;
        }
        public static int Recover(BaseState s)
        {
            int count = 0;
            foreach (var l in s.Leaders.Where(l => BaseAdventure.Alive(l) && l.Health > 0 && l.Health < l.MaxHealth))
            { l.Health = Math.Min(l.MaxHealth, l.Health + s.Development.RecoveryPerDay); l.Resting = false; count++; }
            foreach (var w in s.Workers.Where(w => w.Resting && !w.Away && w.Health > 0))
            { w.Health = Math.Min(w.MaxHealth, w.Health + s.Development.RecoveryPerDay); count += w.Count; if (w.Health == w.MaxHealth) w.Resting = false; }
            return count;
        }
    }
}

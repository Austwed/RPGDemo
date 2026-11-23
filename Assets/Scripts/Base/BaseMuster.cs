using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace BaseConquest.Base
{
    [Serializable] public sealed class BaseArmyPreset
    {
        public string Purpose, SwordPriority = "high", BowPriority = "high", ArmorPriority = "high";
        public int LeaderCount = 2, Melee = 4, Ranged = 2, Cavalry = 1, Mage;
        public List<string> Candidates = new List<string>();
        public void SelectLeaders(IEnumerable<string> ids)
        {
            var selected=ids.ToList();
            if(selected.Count>3 || selected.Any(string.IsNullOrWhiteSpace) || selected.Distinct().Count()!=selected.Count) throw new ArgumentException("请选择最多三名不同队长");
            Candidates=selected; LeaderCount=Math.Max(1,selected.Count);
        }
        public int Count(BaseTroopKind kind) => kind == BaseTroopKind.Melee ? Melee : kind == BaseTroopKind.Ranged ? Ranged : kind == BaseTroopKind.Mage ? Mage : Cavalry;
        public void SetCount(BaseTroopKind kind, int value) { if (kind == BaseTroopKind.Melee) Melee = value; else if (kind == BaseTroopKind.Ranged) Ranged = value; else if (kind == BaseTroopKind.Mage) Mage = value; else Cavalry = value; }
        public BaseArmyPreset Copy() { var copy = (BaseArmyPreset)MemberwiseClone(); copy.Candidates = Candidates.ToList(); return copy; }
    }
    [Serializable] public sealed class BaseFacilityProtection
    {
        public string FacilityId;
        public int Priority, MinimumWorkers = 2, Capacity = 10000;
        public int Level = 1, MaxProductLevel = 1;
        public bool DeveloperProductionPaused;
        public BaseFacilityProtection Copy() => (BaseFacilityProtection)MemberwiseClone();
    }
    [Serializable] public sealed class BaseMusterSource
    {
        public string Id, FacilityId;
        public BaseTroopKind Kind;
        public int Taken;
        public BaseMusterSource Copy() => (BaseMusterSource)MemberwiseClone();
    }
    [Serializable] public sealed class BaseMusterLeader
    {
        public string LeaderId, OriginalFacility;
        public BaseMusterLeader Copy() => (BaseMusterLeader)MemberwiseClone();
    }
    [Serializable] public sealed class BaseOutsideParty
    {
        public string Id, Purpose;
        public int StartedDay;
        public bool InCombat;
        public string PriorityMode="";
        public bool ConserveMaterials;
        public List<BaseCommanderAllocation> CommanderTroops;
        public BaseArmyPreset Preset;
        public List<BaseMusterLeader> Leaders = new List<BaseMusterLeader>();
        public List<BaseMusterSource> Sources = new List<BaseMusterSource>();
        public BaseOutsideParty Copy() => new BaseOutsideParty { Id = Id, Purpose = Purpose, StartedDay = StartedDay, InCombat = InCombat, PriorityMode=PriorityMode,ConserveMaterials=ConserveMaterials,CommanderTroops=CommanderTroops?.Select(x=>x.Copy()).ToList(), Preset = Preset.Copy(), Leaders = Leaders.Select(x => x.Copy()).ToList(), Sources = Sources.Select(x => x.Copy()).ToList() };
    }
    [Serializable] public sealed class BaseMusterState
    {
        public int Version = 1, KeepMelee = 1, KeepRanged = 1, KeepCavalry, KeepMage;
        public List<int> Strategies, Ranges;
        public List<BaseCommanderTroops> SlotTroops;
        public List<BaseArmyPreset> Presets = new List<BaseArmyPreset>();
        public List<BaseFacilityProtection> Facilities = new List<BaseFacilityProtection>();
        public List<BaseOutsideParty> Parties = new List<BaseOutsideParty>();
        public List<string> ReturnedIds = new List<string>();
        public List<string> AppliedAppendIds = new List<string>();
        public int Keep(BaseTroopKind kind) => kind == BaseTroopKind.Melee ? KeepMelee : kind == BaseTroopKind.Ranged ? KeepRanged : kind == BaseTroopKind.Mage ? KeepMage : KeepCavalry;
        public void SetKeep(BaseTroopKind kind, int value) { if (kind == BaseTroopKind.Melee) KeepMelee = value; else if (kind == BaseTroopKind.Ranged) KeepRanged = value; else if (kind == BaseTroopKind.Mage) KeepMage = value; else KeepCavalry = value; }
        public BaseMusterState Copy() => new BaseMusterState { Version = Version, KeepMelee = KeepMelee, KeepRanged = KeepRanged, KeepCavalry = KeepCavalry, KeepMage = KeepMage, Strategies = Strategies?.ToList(), Ranges = Ranges?.ToList(), SlotTroops = SlotTroops?.Select(troops => troops.Copy()).ToList(), Presets = Presets.Select(x => x.Copy()).ToList(), Facilities = Facilities.Select(x => x.Copy()).ToList(), Parties = Parties.Select(x => x.Copy()).ToList(), ReturnedIds = ReturnedIds.ToList(), AppliedAppendIds = (AppliedAppendIds ?? new List<string>()).ToList() };
    }
    public sealed class BaseMusterTake
    {
        public int BucketIndex, Count;
    }
    public sealed class BaseMusterPlan
    {
        public int SoldierLimit=24;
        public string Id, Purpose, Stamp;
        public string PartyId;
        public int RequestedMelee, RequestedRanged, RequestedCavalry, RequestedMage;
        public BaseArmyPreset Selection;
        public List<BaseSoldierPick> ManualPicks;
        public bool IgnoreProtection;
        public string PriorityMode="";
        public bool ConserveMaterials;
        public List<BaseCommanderAllocation> CommanderTroops;
        internal List<BaseCommanderAllocation> ManualTeams;
        internal bool ForMarch;
        public BaseProductionForecast Production;
        public int MissingLeaders, MissingMelee, MissingRanged, MissingCavalry, MissingMage, FoodLoss, ProductLoss, RemainingBase;
        public List<string> Leaders = new List<string>();
        public List<BaseMusterTake> Takes = new List<BaseMusterTake>();
        public int Total => Takes.Sum(x => x.Count);
    }
    public static partial class BaseMuster
    {
        public const int BattleSoldierLimit=24;
        public static readonly string[] Purposes = { "defense", "expedition" };
        public static string PurposeName(string id) => id == "defense" ? "迎战" : "出征";
        public static void Normalize(BaseState s)
        {
            if (s == null) return;
            EnsureCaravan(s);
            var old = s.Muster;
            if (old != null && old.Version == 1 && old.AppliedAppendIds == null) old.AppliedAppendIds = new List<string>();
            bool empty = old != null && (old.Presets == null || old.Presets.Count == 0) && (old.Facilities == null || old.Facilities.Count == 0) && (old.Parties == null || old.Parties.Count == 0) && (old.ReturnedIds == null || old.ReturnedIds.Count == 0) && (old.AppliedAppendIds == null || old.AppliedAppendIds.Count == 0);
            // Unity缺失按值类字段可能给出带初始化器的空对象；Web序列化器则给null。
            if (old != null && old.Version != 0 && !(old.Version == 1 && empty)) return;
            if (old != null && !empty || s.Leaders.Any(l => !string.IsNullOrEmpty(l.PartyId)) || s.Workers.Any(w => !string.IsNullOrEmpty(w.PartyId) || !string.IsNullOrEmpty(w.SourceId))) throw new ArgumentException("缺失编队格式含已有人员或设置，拒绝清空");
            var ids = s.Leaders.Select(x => x.Id).ToList();
            s.Muster = new BaseMusterState {
                Presets = new List<BaseArmyPreset> {
                    new BaseArmyPreset { Purpose = "defense", Candidates = ids.Where(x => x != s.PlayerLeaderId).Concat(new[] { s.PlayerLeaderId }).ToList() },
                    new BaseArmyPreset { Purpose = "expedition", Candidates = new[] { s.PlayerLeaderId }.Concat(ids.Where(x => x != s.PlayerLeaderId)).ToList() }
                },
                Facilities = new List<BaseFacilityProtection> { new BaseFacilityProtection { FacilityId = "farm", Priority = 2 }, new BaseFacilityProtection { FacilityId = "workshop", Priority = 0 } }
            };
        }
        // 商队设施建成后才有岗位记录；收存时连人带记录一起移除，避免留下 0 容量的空设施。
        public static void EnsureCaravan(BaseState s)
        {
            if (s?.Muster?.Facilities == null || s.Campaign?.Layouts == null) return;
            // 剧情场景没有营地布局时不改动岗位记录，避免出门一趟把商队编制清空。
            if (BaseFacilityLayout.BaseLayout(s) == null) return;
            bool built = BaseFacilityLayout.Built(s, "caravan");
            var entry = s.Muster.Facilities.FirstOrDefault(f => f != null && f.FacilityId == "caravan");
            if (built && entry == null)
                s.Muster.Facilities.Add(new BaseFacilityProtection { FacilityId = "caravan", Priority = 0, MinimumWorkers = 1, Capacity = BaseFacilityLayout.Find("caravan").Capacity });
            else if (!built && entry != null) RemoveCaravan(s);
        }
        public static void RemoveCaravan(BaseState s)
        {
            if (s?.Muster?.Facilities == null) return;
            s.Muster.Facilities.RemoveAll(f => f != null && f.FacilityId == "caravan");
            foreach (var worker in s.Workers.Where(w => w.FacilityId == "caravan")) { worker.FacilityId = ""; worker.DesiredFacility = ""; worker.PostLocked = false; }
        }
        private static void ValidatePreset(BaseState s, BaseArmyPreset p)
        {
            if (p == null || !Purposes.Contains(p.Purpose) || p.LeaderCount < 1 || p.LeaderCount > 3 || new[] { p.Melee, p.Ranged, p.Cavalry, p.Mage }.Any(n => n < 0 || n > 30) || new[] { p.SwordPriority, p.BowPriority, p.ArmorPriority }.Any(x => x != "high" && x != "low") || p.Candidates == null || p.Candidates.Count > s.Leaders.Count || p.Candidates.Distinct().Count() != p.Candidates.Count || p.Candidates.Any(id => !s.Leaders.Any(l => l.Id == id))) throw new ArgumentException("迎战／出征预设非法");
        }
        public static void Validate(BaseState s)
        {
            Normalize(s); var m = s.Muster;
            if (m.Strategies == null || m.Strategies.Count == 0)
            {
                var candidates = m.Presets?.FirstOrDefault(p => p?.Purpose == "expedition")?.Candidates ?? new List<string>();
                m.Strategies = candidates.Take(3).Select(id => (int)BaseSceneRules.Tier(s.Leaders.FirstOrDefault(l => l.Id == id)?.PreferredStrategy ?? BaseConquest.Battle.SquadStrategy.Attack)).ToList();
                while (m.Strategies.Count < 3) m.Strategies.Add((int)BaseConquest.Battle.SquadStrategy.Attack);
            }
            if (m.Ranges == null || m.Ranges.Count == 0) m.Ranges = new List<int> { 3, 6, 9 };
            if (m.Ranges.Count == 3) m.Ranges = Enumerable.Range(0,3).SelectMany(_ => m.Ranges).ToList();
            if (m.Strategies.Count != 3 || m.Strategies.Any(v => !Enum.IsDefined(typeof(BaseConquest.Battle.SquadStrategy), v)) || m.Ranges.Count != 9 || Enumerable.Range(0,3).Any(slot => !ValidRanges(m.Ranges[slot*3],m.Ranges[slot*3+1],m.Ranges[slot*3+2]))) throw new ArgumentException("军营策略或范围非法");
            if (s.Development == null || s.Development.Version == 0)
                foreach (var f in m.Facilities ?? new List<BaseFacilityProtection>()) if (f != null) { if (f.Level == 0) f.Level = 1; if (f.MaxProductLevel == 0) f.MaxProductLevel = 1; }
            if (m.Version != 1 || m.Presets == null || m.Facilities == null || m.Parties == null || m.ReturnedIds == null || m.Presets.Count != 2 || m.Presets.Select(p => p?.Purpose).Distinct().Count() != 2 || new[] { m.KeepMelee, m.KeepRanged, m.KeepCavalry, m.KeepMage }.Any(n => n < 0 || n > 10000)) throw new ArgumentException("编队数据版本／集合非法");
            foreach (var p in m.Presets) ValidatePreset(s, p);
            if (m.AppliedAppendIds == null || m.AppliedAppendIds.Count > 100000 || m.AppliedAppendIds.Any(string.IsNullOrWhiteSpace) || m.AppliedAppendIds.Distinct().Count() != m.AppliedAppendIds.Count) throw new ArgumentException("追加操作身份非法");
            if (m.Facilities.Count != 2 && m.Facilities.Count != 3 || m.Facilities.Count == 3 && !m.Facilities.Any(f => f != null && f.FacilityId == "caravan") || m.Facilities.Any(f => f == null || f.FacilityId != "farm" && f.FacilityId != "workshop" && f.FacilityId != "caravan" || f.Priority < 0 || f.Priority > 2 || f.MinimumWorkers < 0 || f.Capacity < f.MinimumWorkers || f.Capacity > 10000 || f.Level < 1 || f.Level > 100 || f.MaxProductLevel < 1 || f.MaxProductLevel > 2) || m.Facilities.Select(f => f.FacilityId).Distinct().Count() != m.Facilities.Count || !m.Facilities.Any(f => f.FacilityId == "farm") || !m.Facilities.Any(f => f.FacilityId == "workshop") || m.Facilities.Any(f => BaseEconomy.WorkersAt(s, f.FacilityId, false) > f.Capacity || BaseEconomy.WorkersAt(s, f.FacilityId, true) > f.Capacity)) throw new ArgumentException("设施留守与生产保留非法");
            if (m.Parties.Count > 2 || m.Parties.Any(p => p == null || string.IsNullOrWhiteSpace(p.Id) || !Purposes.Contains(p.Purpose) || p.StartedDay < 1 || p.StartedDay > s.Day || p.Leaders == null || p.Sources == null || p.Leaders.Count < 1 || p.Leaders.Count > 3) || m.Parties.Select(p => p.Id).Distinct().Count() != m.Parties.Count || m.Parties.Select(p => p.Purpose).Distinct().Count() != m.Parties.Count || m.ReturnedIds.Count > 100000 || m.ReturnedIds.Any(string.IsNullOrWhiteSpace) || m.ReturnedIds.Distinct().Count() != m.ReturnedIds.Count || m.Parties.Any(p => m.ReturnedIds.Contains(p.Id))) throw new ArgumentException("外出队伍身份非法");
            foreach (var party in m.Parties)
            {
                if(string.IsNullOrEmpty(party.PriorityMode)&&party.CommanderTroops?.Count==0)party.CommanderTroops=null;
                if(party.PriorityMode!=null&&party.PriorityMode!=""&&party.PriorityMode!="production"&&party.PriorityMode!="combat")throw new ArgumentException("外出队伍抽调模式非法");
                if(!string.IsNullOrEmpty(party.PriorityMode)&&party.CommanderTroops==null)throw new ArgumentException("外出队伍缺少已冻结的队长编制");
                if(party.CommanderTroops!=null&&(party.CommanderTroops.Count!=party.Leaders.Count||!party.CommanderTroops.Select(r=>r?.LeaderId).SequenceEqual(party.Leaders.Select(l=>l.LeaderId))||party.CommanderTroops.Any(r=>r==null||!CompositionValid(r.Requested)||!CompositionValid(r.Actual))||Enum.GetValues(typeof(BaseTroopKind)).Cast<BaseTroopKind>().Any(k=>party.CommanderTroops.Sum(r=>r.Actual.Count(k))!=s.Workers.Where(w=>w.Away&&w.PartyId==party.Id&&w.Kind==k).Sum(w=>w.Count))))throw new ArgumentException("外出队伍分队兵力与实际来源不一致");
                ValidatePreset(s, party.Preset);
                if (party.Preset.Purpose != party.Purpose || party.Leaders.Select(x => x?.LeaderId).Distinct().Count() != party.Leaders.Count || party.Leaders.Any(x => x == null || !BaseEconomy.Facilities.Contains(x.OriginalFacility) || !s.Leaders.Any(l => l.Id == x.LeaderId && l.Away && l.PartyId == party.Id && l.FacilityId == "")) || party.Sources.Any(x => x == null || string.IsNullOrWhiteSpace(x.Id) || !BaseEconomy.Facilities.Contains(x.FacilityId) || !Enum.IsDefined(typeof(BaseTroopKind), x.Kind) || x.Taken < 1 || x.Taken > 10000) || party.Sources.Select(x => x.Id).Distinct().Count() != party.Sources.Count) throw new ArgumentException("外出人员／来源非法");
                foreach (var source in party.Sources)
                    if (s.Workers.Where(w => w.Away && w.PartyId == party.Id && w.SourceId == source.Id).Sum(w => w.Count) > source.Taken) throw new ArgumentException("外出实际数量超过抽调来源");
            }
            foreach (var leader in s.Leaders.Where(l => !string.IsNullOrEmpty(l.PartyId)))
                if (!leader.Away || !m.Parties.Any(p => p.Id == leader.PartyId && p.Leaders.Any(x => x.LeaderId == leader.Id))) throw new ArgumentException("人物编队归属不存在");
            foreach (var worker in s.Workers.Where(w => !string.IsNullOrEmpty(w.PartyId) || !string.IsNullOrEmpty(w.SourceId)))
                if (!worker.Away || worker.FacilityId != "" || !m.Parties.Any(p => p.Id == worker.PartyId && p.Sources.Any(x => x.Id == worker.SourceId && x.Kind == worker.Kind))) throw new ArgumentException("普通兵编队来源不存在");
        }
        public static bool ValidRanges(int guard,int alert,int attack) => guard>=1&&guard<=alert&&alert<=attack&&attack<=32;
        public static void SetRanges(BaseState s,int slot,int guard,int alert,int attack)
        {
            BaseEconomy.Validate(s);
            if(slot<0||slot>=3||!ValidRanges(guard,alert,attack))throw new ArgumentException("位次或范围非法");
            if(s.Campaign.HasActiveBattle)throw new InvalidOperationException("战斗中不能修改军营默认范围");
            s.Muster.Ranges[slot*3]=guard;s.Muster.Ranges[slot*3+1]=alert;s.Muster.Ranges[slot*3+2]=attack;
        }
        internal static void SetRanges(BaseState s, int[] values)
        {
            if (s.Campaign.HasActiveBattle) throw new InvalidOperationException("战斗中不能修改军营默认范围");
            if (values.Length != s.Muster.Ranges.Count) throw new ArgumentException("位次或范围非法");
            for (int index = 0; index < values.Length; index += 3)
                if (!ValidRanges(values[index], values[index + 1], values[index + 2]))
                    throw new ArgumentException("位次或范围非法");
            for (int index = 0; index < values.Length; index++)
                s.Muster.Ranges[index] = values[index];
        }
        public static string Snapshot(BaseState s)
        {
            s.EnsureSoldierNumbers();
            string N(double n) => n.ToString("R", CultureInfo.InvariantCulture);
            var parts = new List<string> { s.Day.ToString(), s.Inventory.Food.ToString(), s.Inventory.Materials.ToString(), s.Inventory.Coins.ToString(), s.WorkshopProduct, N(s.FarmFraction), s.TodayFoodMissing.ToString(), s.TodayFoodRequired.ToString(), N(s.Rules.FarmPerWorker), N(s.Rules.WorkshopPerWorker), N(s.Rules.HungerProductionPenalty), s.Rules.SwordMaterial.ToString(), s.Rules.BowMaterial.ToString(), s.Rules.ArmorMaterial.ToString() };
            parts.Add(string.Join(",",s.Adventure.Members)+"/"+string.Join(",",s.Adventure.Pauses.Select(p=>p.LeaderId+":"+p.MapId+":"+p.Region)));
            parts.AddRange(s.Leaders.Select(l => l.Id + ":" + l.FacilityId + ":" + l.Away + ":" + l.PartyId + ":" + l.UnavailableDay + ":" + N(l.ProductionBonus)+":"+l.Recruited));
            parts.AddRange(s.Workers.Select(w => w.Kind + ":" + w.FacilityId + ":" + string.Join(",",w.SoldierNumbers) + ":" + w.Away + ":" + w.PartyId + ":" + w.SourceId + ":" + w.UnavailableDay+":"+w.Recruited+(w.LastBattleDay>0 ? ":battle="+w.LastBattleDay : "")));
            parts.Add(BaseSettlements.AtVillage(s) + ":" + s.Development.VillagePolicy);
            parts.AddRange(s.Workers.Select(w => BaseSettlements.Station(w) + ":" + w.Relocating + ":" + w.DepartureFacility));
            parts.AddRange(s.Leaders.Select(l => l.VillageResident + ":" + l.Relocating + ":" + l.ArrivalFacility));
            parts.AddRange(s.Leaders.Select(l=>l.Id+":level:"+(l.Training?.Level??1)));
            parts.AddRange(s.Inventory.Equipment.Select(e => e.Product + ":" + e.Level + ":" + e.Count));
            parts.AddRange(s.WorkshopProgress.Select(p => p.Product + ":" + N(p.Fraction)));
            parts.AddRange(s.Muster.Presets.Select(p => p.Purpose + ":" + p.LeaderCount + ":" + p.Melee + ":" + p.Ranged + ":" + p.Cavalry + ":" + p.Mage + ":" + string.Join(",", p.Candidates) + ":" + p.SwordPriority + p.BowPriority + p.ArmorPriority));
            parts.AddRange(s.Muster.Facilities.Select(f => f.FacilityId + ":" + f.Priority + ":" + f.MinimumWorkers + ":" + f.Capacity + ":" + f.Level + ":" + f.MaxProductLevel));
            parts.Add(s.Muster.KeepMelee + ":" + s.Muster.KeepRanged + ":" + s.Muster.KeepCavalry + ":" + s.Muster.KeepMage); parts.AddRange(s.Muster.Parties.Select(p => p.Id)); parts.AddRange(s.Muster.ReturnedIds);
            parts.Add(string.Join(",", s.Muster.Strategies) + "/" + string.Join(",", s.Muster.Ranges));
            parts.Add(s.Rules.SoldierFood + ":" + s.Rules.CavalryFood + ":" + s.Rules.LeaderFood + ":" + (BaseRpg.Active(s)?s.Demo.Rpg.ArcherFood:0) + ":" + s.FoodPaidDay + ":" + s.TodayFoodConsumed);
            parts.Add(s.Development.Version + ":" + s.Development.Automatic + ":" + s.Development.Policy + ":" + N(s.Development.RecoveryFraction) + ":" + N(BaseDevelopment.WarningThreshold(s)));
            parts.AddRange(s.Leaders.Select(l => l.Id + ":" + N(l.Health) + ":" + N(l.MaxHealth) + ":" + l.Resting + ":" + l.DesiredFacility + ":" + l.PostLocked + ":" + l.HasAffinity + ":" + N(l.FarmBonus) + ":" + N(l.WorkshopBonus)));
            parts.AddRange(s.Workers.Select(w => N(w.Health) + ":" + N(w.MaxHealth) + ":" + w.Resting + ":" + w.DesiredFacility + ":" + w.PostLocked + ":" + N(w.FarmEfficiency) + ":" + N(w.WorkshopEfficiency)));
            parts.AddRange(s.Supply.Quotas.Select(q => q.BattleId + ":" + q.PartyId + ":" + q.Phase)); parts.AddRange(s.Supply.FinishedIds);
            parts.AddRange(s.Supply.Rules.Select(r => r.Product + ":" + N(r.LowBonus) + ":" + N(r.HighBonus)));
            parts.Add(N(s.Supply.ReturnedWear) + ":" + N(s.Supply.LostWear));
            parts.AddRange(s.Muster.Parties.Select(p => p.Id + ":" + p.InCombat + ":" + p.Preset.SwordPriority + p.Preset.BowPriority + p.Preset.ArmorPriority));
            parts.AddRange(s.Muster.AppliedAppendIds);
            parts.Add(BaseConstruction.Snapshot(s));
            parts.AddRange(s.Leaders.Where(l=>l.PersonalEquipment?.Version==1).Select(l=>l.Id+":gear:"+l.PersonalEquipment.Weapon+":"+BaseDevelopment.ProfileKey(true,l.PersonalEquipment.BaseStats)+":"+string.Join(";",l.PersonalEquipment.Items.Select(i=>i.Id+":"+i.Template+":"+i.Slot+":"+i.Quality))));
            parts.AddRange(s.Muster.Parties.SelectMany(p => p.Sources.Select(x => p.Id + ":" + x.Id + ":" + x.Kind + ":" + x.FacilityId + ":" + x.Taken)));
            parts.AddRange(s.Workers.Select(w => BaseDevelopment.ProfileKey(w.HasCombatProfile,w.CombatStats)));
            parts.AddRange(s.Leaders.Select(l => BaseDevelopment.ProfileKey(l.HasCombatProfile,l.CombatStats)));
            parts.Add("adventure:" + s.Adventure?.Version + ":" + string.Join(",", s.Adventure?.Members ?? new List<string>()));
            parts.AddRange(s.Muster.SlotTroops?.Select(t => $"slot:{t.Melee}:{t.Ranged}:{t.Cavalry}:{t.Mage}") ?? Enumerable.Empty<string>());
            parts.AddRange(s.Leaders.Select(l=>l.Id+":composition:"+(l.DefaultTroops==null ? "legacy" : l.DefaultTroops.Version+":"+l.DefaultTroops.Melee+":"+l.DefaultTroops.Ranged+":"+l.DefaultTroops.Cavalry+":"+l.DefaultTroops.Mage)));
            parts.AddRange(s.Muster.Parties.Select(p=>p.Id+":"+p.PriorityMode+":"+p.ConserveMaterials+":"+string.Join(";",p.CommanderTroops?.Select(r=>r.LeaderId+":"+r.Requested.Melee+":"+r.Requested.Ranged+":"+r.Requested.Cavalry+":"+r.Requested.Mage+":"+r.Actual.Melee+":"+r.Actual.Ranged+":"+r.Actual.Cavalry+":"+r.Actual.Mage)??Enumerable.Empty<string>())));
            parts.AddRange(s.Muster.Facilities.Select(f=>f.DeveloperProductionPaused.ToString()));
            parts.AddRange(s.Muster.Parties.SelectMany(p => (p.CommanderTroops ?? new List<BaseCommanderAllocation>())
                .Select(team => team.LeaderId + ":" + string.Join(",", team.SoldierNumbers ?? new List<int>()))));
            return string.Join("|", parts);
        }
        public static BaseMusterPlan Preview(BaseState s, string purpose, bool ignoreProtection = false, BaseArmyPreset selection = null, IEnumerable<BaseSoldierPick> manualPicks = null,string priorityMode="")
            => Preview(s, purpose, ignoreProtection, selection, manualPicks, priorityMode, null);

        internal static BaseMusterPlan Preview(BaseState s, string purpose, bool ignoreProtection, BaseArmyPreset selection,
            IEnumerable<BaseSoldierPick> manualPicks, string priorityMode, List<BaseCommanderAllocation> manualTeams, bool forMarch = false)
        {
            BaseEconomy.Validate(s);
            if (s.Aftermath.GameOver || !Purposes.Contains(purpose) || s.Muster.Parties.Any(p => p.Purpose == purpose)) throw new InvalidOperationException("该用途已有外出队伍，先返营再重新编队");
            var preset = selection ?? s.Muster.Presets.Single(p => p.Purpose == purpose); ValidatePreset(s,preset); if(preset.Purpose!=purpose) throw new ArgumentException("选择的用途不一致");
            var plan = new BaseMusterPlan { Id = Guid.NewGuid().ToString("N"), Purpose = purpose, IgnoreProtection = ignoreProtection, Stamp = Snapshot(s) };
            if(priorityMode!=""&&priorityMode!="production"&&priorityMode!="combat")throw new ArgumentException("未知出征模式");
            plan.PriorityMode=priorityMode;
            if(priorityMode!=""&&s.Campaign.HasActiveBattle)throw new InvalidOperationException("战斗中不能重新安排出征兵力");
            if(priorityMode!="")plan.IgnoreProtection=priorityMode=="combat";
            plan.Selection = selection?.Copy();
            plan.ManualPicks = manualPicks?.Select(p=>p?.Copy()).ToList();
            plan.ManualTeams = manualTeams?.Select(team => team.Copy()).ToList();
            plan.ForMarch = forMarch;
            plan.Leaders = preset.Candidates.Where(id => s.Leaders.Any(l => l.Id == id && BaseDevelopment.LocalLeader(s,l) && BaseDevelopment.CanFight(l))).Take(preset.LeaderCount).ToList(); plan.MissingLeaders = preset.LeaderCount - plan.Leaders.Count;
            if(plan.Leaders.Count>0)PrepareProduction(s,plan);
            if(plan.ManualPicks==null&&priorityMode!=""&&plan.Leaders.Count>0)SelectOrderedWorkers(s,plan,preset);
            else if(plan.ManualPicks==null) SelectWorkers(s, plan, preset, Apply, plan.Leaders.Count > 0);
            else SelectExactWorkers(s,plan,preset);
            if(plan.Total>BattleSoldierLimit)throw new InvalidOperationException("出战士兵总数不能超过24人");
            if(plan.Leaders.Count>0)FinishFormation(s,plan);
            var before = BaseEconomy.Preview(s); var copy = s.Copy();
            if (plan.Leaders.Count == 0) { plan.RemainingBase = s.Workers.Where(w => !w.Away).Sum(w => w.Count); return plan; }
            Apply(copy, plan); var after = BaseEconomy.Preview(copy);
            plan.FoodLoss = before.FoodProduced - after.FoodProduced; plan.ProductLoss = before.ProductsMade - after.ProductsMade;
            plan.RemainingBase = copy.Workers.Where(w => !w.Away).Sum(w => w.Count); return plan;
        }
        private static void SelectWorkers(BaseState s, BaseMusterPlan plan, BaseArmyPreset requested, Action<BaseState, BaseMusterPlan> apply, bool canApply)
        {
            foreach (BaseTroopKind kind in Enum.GetValues(typeof(BaseTroopKind)))
            {
                int need = requested.Count(kind), total = s.Workers.Where(w => BaseDevelopment.Eligible(s,plan.Purpose, w) && w.Kind == kind).Sum(w => w.Count);
                int already=plan.Takes.Where(t=>s.Workers[t.BucketIndex].Kind==kind).Sum(t=>t.Count);
                int canTake = Math.Min(Math.Max(0,plan.SoldierLimit-plan.Total),plan.IgnoreProtection ? total-already : Math.Max(0, total - s.Muster.Keep(kind)-already));
                // 2026-11-04 裁决：先按生命比例从高到低取，同档再按「先抽闲置、后抽低产设施」的老次序。
                var buckets = s.Workers.Select((w, i) => new { Worker = w, Index = i }).Where(x => BaseDevelopment.Eligible(s,plan.Purpose, x.Worker) && x.Worker.Kind == kind)
                    .OrderByDescending(x => x.Worker.MaxHealth > 0 ? x.Worker.Health / x.Worker.MaxHealth : 0)
                    .ThenBy(x => x.Worker.FacilityId == "" ? -1 : s.Muster.Facilities.Single(f => f.FacilityId == x.Worker.FacilityId).Priority)
                    .ThenBy(x => !BaseDevelopment.Effective(s,x.Worker) ? 0 : x.Worker.FacilityId == "farm" ? s.Rules.FarmPerWorker * (1 + (BaseEconomy.Manager(s, "farm")?.ProductionBonus ?? 0)) : x.Worker.FacilityId == "workshop" ? s.Rules.WorkshopPerWorker * (1 + (BaseEconomy.Manager(s, "workshop")?.ProductionBonus ?? 0)) : 0)
                    .ThenBy(x => x.Worker.FacilityId, StringComparer.Ordinal).ThenBy(x => x.Index);
                foreach (var entry in buckets)
                {
                    var worker = entry.Worker; int n = Math.Min(need, Math.Min(canTake, worker.Count-plan.Takes.Where(t=>t.BucketIndex==entry.Index).Sum(t=>t.Count)));
                    if (!plan.IgnoreProtection && canApply)
                        while (n > 0)
                        {
                            plan.Takes.Add(new BaseMusterTake { BucketIndex = entry.Index, Count = n });
                            var trial = s.Copy(); apply(trial, plan); plan.Takes.RemoveAt(plan.Takes.Count - 1);
                            if (s.Muster.Facilities.All(f => BaseEconomy.EligibleWorkers(trial, f.FacilityId) >= (plan.PriorityMode=="production" ? Math.Max(1,f.MinimumWorkers) : Math.Min(f.MinimumWorkers, BaseEconomy.EligibleWorkers(s, f.FacilityId))))&&(plan.PriorityMode!="production"||ForecastProduction(trial).Allowed)) break;
                            n--;
                        }
                    if (n == 0) continue;
                    plan.Takes.Add(new BaseMusterTake { BucketIndex = entry.Index, Count = n }); need -= n; canTake -= n;
                    if (need == 0) break;
                }
                if (kind == BaseTroopKind.Melee) plan.MissingMelee = need; else if (kind == BaseTroopKind.Ranged) plan.MissingRanged = need; else if (kind == BaseTroopKind.Mage) plan.MissingMage = need; else plan.MissingCavalry = need;
            }
        }
        private static string Shape(BaseMusterPlan p) => string.Join(",", p.Leaders) + "/" + string.Join(",", p.Takes.Select(t => t.BucketIndex + ":" + t.Count));
        public static bool Confirm(BaseState s, BaseMusterPlan plan)
        {
            BaseEconomy.Validate(s);
            if (plan == null || !string.IsNullOrEmpty(plan.PartyId) || plan.Leaders == null || plan.Leaders.Count == 0 || s.Muster.Parties.Any(p => p.Id == plan.Id) || s.Muster.ReturnedIds.Contains(plan.Id) || plan.Stamp != Snapshot(s)) return false;
            BaseMusterPlan actual;
            try { actual = Preview(s, plan.Purpose, plan.IgnoreProtection, plan.Selection,plan.ManualPicks,plan.PriorityMode,plan.ManualTeams,plan.ForMarch); }
            catch(ArgumentException) {return false;} catch(InvalidOperationException) {return false;}
            if (Shape(actual) != Shape(plan)) return false;
            if(actual.PriorityMode=="production"&&actual.Production?.Allowed!=true)return false;
            actual.Id = plan.Id; Apply(s, actual); BaseEconomy.Validate(s); return true;
        }
        private static void Apply(BaseState s, BaseMusterPlan plan)
        {
            var party = new BaseOutsideParty { Id = plan.Id, Purpose = plan.Purpose, StartedDay = s.Day, Preset = (plan.Selection ?? s.Muster.Presets.Single(p => p.Purpose == plan.Purpose)).Copy() };
            party.PriorityMode=plan.PriorityMode;party.ConserveMaterials=plan.ConserveMaterials;
            var requested=plan.CommanderTroops;
            if(requested==null&&plan.PriorityMode!="")requested=plan.Leaders.Select(id=>new BaseCommanderAllocation {LeaderId=id,Requested=s.Leaders.Single(l=>l.Id==id).DefaultTroops.Copy()}).ToList();
            party.CommanderTroops=requested==null ? null : Distribute(requested,k=>plan.Takes.Where(t=>s.Workers[t.BucketIndex].Kind==k).Sum(t=>t.Count));
            foreach (string id in plan.Leaders)
            {
                var leader = s.Leaders.Single(l => l.Id == id); party.Leaders.Add(new BaseMusterLeader { LeaderId = id, OriginalFacility = leader.DesiredFacility != "" ? leader.DesiredFacility : leader.FacilityId });
                leader.Away = true; leader.PartyId = party.Id; leader.FacilityId = ""; leader.UnavailableDay = s.Day;
            }
            TransferWorkers(s, party, plan, party.Id + ".");
            s.Muster.Parties.Add(party); BaseDevelopment.Allocate(s);
        }
        private static void TransferWorkers(BaseState s, BaseOutsideParty party, BaseMusterPlan plan, string prefix)
        {
            int index = 0;
            foreach (var take in plan.Takes.Where(t=>t.Count>0))
            {
                var source = s.Workers[take.BucketIndex]; string sourceId = prefix + index++;
                party.Sources.Add(new BaseMusterSource { Id = sourceId, Kind = source.Kind, FacilityId = source.DesiredFacility != "" ? source.DesiredFacility : source.FacilityId, Taken = take.Count });
                var away = plan.ManualPicks == null ? source.Take(0, take.Count) :
                    source.TakeNumbers(plan.ManualPicks.Where(p => p.BucketIndex == take.BucketIndex).Select(p => source.SoldierNumbers[p.Ordinal]).ToList());
                away.Away = true; away.FacilityId = ""; away.PartyId = party.Id; away.SourceId = sourceId; away.UnavailableDay = s.Day; away.Resting = false; s.Workers.Add(away);
            }
            s.Workers.RemoveAll(w => w.Count == 0);
        }
        public static bool Return(BaseState s, string id, Action<BaseWorkerBucket> afterReturn = null)
            => Return(s, id, afterReturn, null);

        internal static bool ReturnAtSettlement(BaseState s, string id, string station)
            => Return(s, id, worker => worker.Resting = worker.Health < worker.MaxHealth * BaseDevelopment.DutyThreshold, station);

        private static bool Return(BaseState s, string id, Action<BaseWorkerBucket> afterReturn, string destinationStation)
        {
            BaseEconomy.Validate(s);
            var party = s.Muster.Parties.FirstOrDefault(p => p.Id == id); if (party == null || party.InCombat || s.Supply.Quotas.Any(q => q.PartyId == id)) return false;
            foreach (var record in party.Leaders)
            {
                var leader = s.Leaders.Single(l => l.Id == record.LeaderId); leader.Away = false; leader.PartyId = "";
                bool? destinationVillage = destinationStation == null || destinationStation == BaseSettlements.BlackCrowStation
                    ? null : destinationStation == BaseSettlements.VillageStation;
                if (destinationVillage.HasValue && leader.VillageResident != destinationVillage.Value)
                {
                    leader.VillageResident = destinationVillage.Value;
                    leader.DesiredFacility = "";
                    leader.PostLocked = false;
                }
                else leader.DesiredFacility = record.OriginalFacility;
                leader.FacilityId = "";
                leader.UnavailableDay = s.Day;
            }
            foreach (var source in party.Sources)
            {
                foreach (var worker in s.Workers.Where(w => w.Away && w.PartyId == id && w.SourceId == source.Id).ToArray())
                {
                    worker.Away = false; worker.PartyId = worker.SourceId = ""; worker.PartyOrder = 0; worker.DesiredFacility = source.FacilityId; worker.FacilityId = ""; worker.UnavailableDay = s.Day;
                    if (destinationStation != null &&
                        (BaseSettlements.Station(worker) != destinationStation || destinationStation == BaseSettlements.BlackCrowStation))
                    {
                        BaseSettlements.SetStation(worker, destinationStation);
                        worker.DesiredFacility = "";
                        worker.PostLocked = false;
                    }
                    afterReturn?.Invoke(worker);
                }
            }
            s.Muster.Parties.Remove(party); s.Muster.ReturnedIds.Add(id); BaseDevelopment.Allocate(s); BaseEconomy.Validate(s); return true;
        }
    }
}

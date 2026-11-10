using System;
using System.Collections.Generic;
using System.Linq;
using System.Globalization;
using BaseConquest.Battle;

namespace BaseConquest.Base
{
    [Serializable] public sealed class BaseSupplyRule
    {
        public string Product;
        public double LowBonus, HighBonus, LowDefense, HighDefense;
        public BaseSupplyRule Copy() => (BaseSupplyRule)MemberwiseClone();
    }
    [Serializable] public sealed class BaseEquipmentWear
    {
        public string Product;
        public int Level;
        public double Fraction;
        public BaseEquipmentWear Copy() => (BaseEquipmentWear)MemberwiseClone();
    }
    [Serializable] public sealed class BaseTeamSupply
    {
        public string LeaderId;
        public int Soldiers, Low, High;
        public BaseTeamSupply Copy() => (BaseTeamSupply)MemberwiseClone();
        public double Average(double low, double high) => Soldiers == 0 ? 0 : (Low * low + High * high) / Soldiers;
    }
    [Serializable] public sealed class BaseEquipmentQuota
    {
        public string BattleId, PartyId, Phase = "prepared", Mode;
        public int Melee, Ranged, Cavalry;
        public List<string> Leaders = new List<string>();
        public List<BaseEquipmentStock> Equipment = new List<BaseEquipmentStock>();
        public List<BaseSupplyRule> Rules = new List<BaseSupplyRule>();
        public double SwordBonus, BowBonus, ArmorBonus, AttackFactor = 1;
        public double ReturnedWear, LostWear;
        public List<BaseTeamSupply> Teams;
        public bool PartySubset;
        public int FullConsumptionGrade;
        public bool ByTeam => Teams?.Count > 0;
        public BaseEquipmentQuota Copy() => new BaseEquipmentQuota { BattleId = BattleId, PartyId = PartyId, Phase = Phase, Mode = Mode, Melee = Melee, Ranged = Ranged, Cavalry = Cavalry, Leaders = Leaders.ToList(), Equipment = Equipment.Select(e => e.Copy()).ToList(), Rules = Rules.Select(r => r.Copy()).ToList(), SwordBonus = SwordBonus, BowBonus = BowBonus, ArmorBonus = ArmorBonus, AttackFactor = AttackFactor, ReturnedWear = ReturnedWear, LostWear = LostWear, Teams = Teams?.Select(t => t.Copy()).ToList(), PartySubset = PartySubset, FullConsumptionGrade = FullConsumptionGrade };
        public int Demand(string product) => product == "sword" ? Melee + Cavalry : product == "bow" ? Ranged : Melee + Ranged + Cavalry;
        public double WeaponBonus(int slot, bool ranged)
        {
            if (!ByTeam) return ranged ? BowBonus : SwordBonus;
            var rule = Rules.Single(r => r.Product == "supply");
            return Teams[slot].Average(rule.LowBonus, rule.HighBonus);
        }
        public double DefenseBonus(int slot)
        {
            if (!ByTeam) return ArmorBonus;
            var rule = Rules.Single(r => r.Product == "supply");
            return Teams[slot].Average(rule.LowDefense, rule.HighDefense);
        }
    }
    [Serializable] public sealed class BaseSupplyState
    {
        public int Version;
        public double ReturnedWear = 0.1, LostWear = 0.7;
        public List<BaseSupplyRule> Rules = new List<BaseSupplyRule>();
        public List<BaseEquipmentQuota> Quotas = new List<BaseEquipmentQuota>();
        public List<BaseEquipmentWear> Wear = new List<BaseEquipmentWear>();
        public List<string> FinishedIds = new List<string>();
        public BaseSupplyState Copy() => new BaseSupplyState { Version = Version, ReturnedWear = ReturnedWear, LostWear = LostWear, Rules = Rules.Select(r => r.Copy()).ToList(), Quotas = Quotas.Select(q => q.Copy()).ToList(), Wear = Wear.Select(w => w.Copy()).ToList(), FinishedIds = FinishedIds.ToList() };
    }
    public sealed class BaseSupplyPlan
    {
        public string Stamp;
        public BaseEquipmentQuota Quota;
    }
    public sealed class BaseSupplyOutcome
    {
        public int SafeMelee, LostMelee, SafeRanged, LostRanged, SafeCavalry, LostCavalry;
        internal readonly Dictionary<int, (int safe, int lost)> Teams = new Dictionary<int, (int safe, int lost)>();
        internal void RecordTeam(int slot, bool lost)
        {
            Teams.TryGetValue(slot, out var count);
            Teams[slot] = lost ? (count.safe, count.lost + 1) : (count.safe + 1, count.lost);
        }
        public int Safe(string product) => product == "sword" ? SafeMelee + SafeCavalry : product == "bow" ? SafeRanged : SafeMelee + SafeRanged + SafeCavalry;
        public int Lost(string product) => product == "sword" ? LostMelee + LostCavalry : product == "bow" ? LostRanged : LostMelee + LostRanged + LostCavalry;
    }
    public static class BaseSupply
    {
        public static readonly string[] Modes = { "full", "half", "none" };
        public static string ModeName(string mode) => mode == "full" ? "尽量配足" : mode == "half" ? "最多半数供给" : "不使用军需";
        public static void Normalize(BaseState s)
        {
            var old = s.Supply;
            if (old != null && old.Version != 0) return;
            if (old != null && (old.Quotas != null && old.Quotas.Count > 0 || old.FinishedIds != null && old.FinishedIds.Count > 0 || old.Rules != null && old.Rules.Count > 0 || old.Wear != null && old.Wear.Count > 0)) throw new ArgumentException("缺失军需版本含已有设置，拒绝清空");
            s.Supply = new BaseSupplyState { Version = 1,
                Rules = new List<BaseSupplyRule> { new BaseSupplyRule { Product = "sword", LowBonus = 10, HighBonus = 16 }, new BaseSupplyRule { Product = "bow", LowBonus = 10, HighBonus = 16 }, new BaseSupplyRule { Product = "armor", LowBonus = 2, HighBonus = 4 } },
                Wear = BaseEconomy.ProductsFor(s).SelectMany(p => new[] { new BaseEquipmentWear { Product = p, Level = 1 }, new BaseEquipmentWear { Product = p, Level = 2 } }).ToList()
            };
        }
        public static int Locked(BaseState s, string product, int level) => s.Supply.Quotas.Sum(q => q.Equipment.Where(e => e.Product == product && e.Level == level).Sum(e => e.Count));
        public static int Available(BaseState s, string product, int level) => s.Inventory.Equipment.Single(e => e.Product == product && e.Level == level).Count - Locked(s, product, level);
        private static bool Number(double n, double maximum) => !double.IsNaN(n) && !double.IsInfinity(n) && n >= 0 && n <= maximum;
        public static void Validate(BaseState s)
        {
            Normalize(s); var supply = s.Supply;
            if (supply.Version != 1 || supply.Rules == null || supply.Quotas == null || supply.Wear == null || supply.FinishedIds == null || !Number(supply.ReturnedWear, 1) || !Number(supply.LostWear, 1) || supply.Rules.Count != BaseEconomy.ProductsFor(s).Length || supply.Rules.Any(r => r == null || !BaseEconomy.ProductsFor(s).Contains(r.Product) || !Number(r.LowBonus, 1000) || !Number(r.HighBonus, 1000)||!Number(r.LowDefense,1000)||!Number(r.HighDefense,1000)) || supply.Rules.Select(r => r.Product).Distinct().Count() != BaseEconomy.ProductsFor(s).Length) throw new ArgumentException("军需规则／版本非法");
            if (supply.Wear.Count != BaseEconomy.ProductsFor(s).Length*2 || supply.Wear.Any(w => w == null || !BaseEconomy.ProductsFor(s).Contains(w.Product) || w.Level < 1 || w.Level > 2 || !Number(w.Fraction, 1) || w.Fraction >= 1) || supply.Wear.Select(w => w.Product + w.Level).Distinct().Count() != BaseEconomy.ProductsFor(s).Length*2) throw new ArgumentException("军需磨损余数非法");
            if (supply.Quotas.Count > 2 || supply.Quotas.Select(q => q?.BattleId).Distinct().Count() != supply.Quotas.Count || supply.Quotas.Select(q => q?.PartyId).Distinct().Count() != supply.Quotas.Count || supply.FinishedIds.Count > 100000 || supply.FinishedIds.Any(string.IsNullOrWhiteSpace) || supply.FinishedIds.Distinct().Count() != supply.FinishedIds.Count) throw new ArgumentException("军需事件身份非法");
            foreach (var q in supply.Quotas)
            {
                if (q == null || string.IsNullOrWhiteSpace(q.BattleId) || supply.FinishedIds.Contains(q.BattleId) || !Modes.Contains(q.Mode) || q.Phase != "prepared" && q.Phase != "fighting" || q.Leaders == null || q.Equipment == null || q.Leaders.Count < 1 || q.Leaders.Count > 3 || q.Leaders.Distinct().Count() != q.Leaders.Count || new[] { q.Melee, q.Ranged, q.Cavalry }.Any(n => n < 0 || n > 30) || q.Equipment.Count != BaseEconomy.ProductsFor(s).Length*2 || q.Equipment.Any(e => e == null || !BaseEconomy.ProductsFor(s).Contains(e.Product) || e.Level < 1 || e.Level > 2 || e.Count < 0) || q.Equipment.Select(e => e.Product + e.Level).Distinct().Count() != BaseEconomy.ProductsFor(s).Length*2 || new[] { q.SwordBonus, q.BowBonus, q.ArmorBonus }.Any(n => !Number(n, 1000)) || !Number(q.AttackFactor, 1)) throw new ArgumentException("军需配额非法");
                var party = s.Muster.Parties.SingleOrDefault(p => p.Id == q.PartyId);
                if (q.Rules == null || q.Rules.Count != BaseEconomy.ProductsFor(s).Length || q.Rules.Any(r => r == null || !BaseEconomy.ProductsFor(s).Contains(r.Product) || !Number(r.LowBonus, 1000) || !Number(r.HighBonus, 1000)||!Number(r.LowDefense,1000)||!Number(r.HighDefense,1000)) || q.Rules.Select(r => r.Product).Distinct().Count() != BaseEconomy.ProductsFor(s).Length || !Number(q.ReturnedWear, 1) || !Number(q.LostWear, 1)) throw new ArgumentException("锁定供给规则非法");
                foreach (string product in BaseEconomy.ProductsFor(s))
                {
                    var rule = q.Rules.Single(r => r.Product == product); int demand = q.Demand(product);
                    double actual = demand == 0 ? 0 : q.Equipment.Where(e => e.Product == product).Sum(e => e.Count * (e.Level == 1 ? rule.LowBonus : rule.HighBonus)) / demand;
                    double stored = product == "supply"?q.SwordBonus:product == "sword" ? q.SwordBonus : product == "bow" ? q.BowBonus : q.ArmorBonus;
                    if(product=="supply"&&(Math.Abs(q.BowBonus-actual)>1e-8||Math.Abs(q.ArmorBonus-(demand==0?0:q.Equipment.Sum(e=>e.Count*(e.Level==1?rule.LowDefense:rule.HighDefense))/demand))>1e-8))throw new ArgumentException("统一军需攻防与配额不一致");
                    if (Math.Abs(stored - actual) > 1e-8) throw new ArgumentException("供给属性与锁定数量不一致");
                }
                if (q.ByTeam && (!BaseEconomy.ProductsFor(s).SequenceEqual(new[] { "supply" }) ||
                    !q.Teams.Select(t => t?.LeaderId).SequenceEqual(q.Leaders) ||
                    q.Teams.Any(t => t.Soldiers < 0 || t.Low < 0 || t.High < 0 || (long)t.Low + t.High > t.Soldiers) ||
                    q.Teams.Sum(t => t.Soldiers) != q.Demand("supply") ||
                    q.Equipment.Single(e => e.Level == 1).Count != q.Teams.Sum(t => t.Low) ||
                    q.Equipment.Single(e => e.Level == 2).Count != q.Teams.Sum(t => t.High)))
                    throw new ArgumentException("逐队军需配额非法");
                if (q.FullConsumptionGrade is < 0 or > 2 || q.FullConsumptionGrade != 0 &&
                    (!q.ByTeam || q.Demand("supply") == 0 || q.Equipment.Any(e => e.Count != (e.Level == q.FullConsumptionGrade ? q.Demand("supply") : 0))))
                    throw new ArgumentException("出战人数与选定军需等级不一致");
                if (party == null || party.InCombat != (q.Phase == "fighting") ||
                    (q.PartySubset ? !q.ByTeam || party.Purpose != "expedition" || q.Leaders.Except(party.Leaders.Select(l => l.LeaderId)).Any()
                        : !q.Leaders.SequenceEqual(party.Leaders.Select(l => l.LeaderId))) ||
                    BaseEconomy.ProductsFor(s).Any(p => q.Equipment.Where(e => e.Product == p).Sum(e => e.Count) > q.Demand(p)))
                    throw new ArgumentException("配额与实际队伍／供给分母不一致");
                if (!q.PartySubset)
                {
                    if (q.Phase == "prepared" && (Count(s, party.Id, BaseTroopKind.Melee) != q.Melee || Count(s, party.Id, BaseTroopKind.Ranged) != q.Ranged || Count(s, party.Id, BaseTroopKind.Cavalry) != q.Cavalry)) throw new ArgumentException("备战队伍变化，先取消旧配额");
                    if (Count(s, party.Id, BaseTroopKind.Melee) > q.Melee || Count(s, party.Id, BaseTroopKind.Ranged) > q.Ranged || Count(s, party.Id, BaseTroopKind.Cavalry) > q.Cavalry) throw new ArgumentException("本场锁定后不能追加基地兵");
                }
            }
            if (s.Inventory.Equipment.Any(e => Available(s, e.Product, e.Level) < 0)) throw new ArgumentException("装备配额超过唯一库存");
        }
        public static int Count(BaseState s, string id, BaseTroopKind kind) => s.Workers.Where(w => w.Away && w.PartyId == id && w.Kind == kind).Sum(w => w.Count);
        public static BaseSupplyPlan Preview(BaseState s, string partyId, string mode = "full")
        {
            BaseEconomy.Validate(s);
            if (!Modes.Contains(mode) || s.Supply.Quotas.Any(q => q.PartyId == partyId)) throw new InvalidOperationException("队伍已有军需准备，先取消再重新预览");
            var party = s.Muster.Parties.Single(p => p.Id == partyId);
            if (party.InCombat) throw new InvalidOperationException("战斗中供给比例锁定");
            var quota = new BaseEquipmentQuota { BattleId = Guid.NewGuid().ToString("N"), PartyId = partyId, Mode = mode,
                Melee = Count(s, partyId, BaseTroopKind.Melee), Ranged = Count(s, partyId, BaseTroopKind.Ranged), Cavalry = Count(s, partyId, BaseTroopKind.Cavalry), Leaders = party.Leaders.Select(l => l.LeaderId).ToList(), AttackFactor = BaseEconomy.AttackFactor(s), Rules = s.Supply.Rules.Select(r => r.Copy()).ToList(), ReturnedWear = s.Supply.ReturnedWear, LostWear = s.Supply.LostWear };
            foreach (string product in BaseEconomy.ProductsFor(s))
            {
                int demand = quota.Demand(product), need = mode == "none" ? 0 : mode == "half" ? demand / 2 : demand;
                string priority = product == "sword" ? party.Preset.SwordPriority : product == "bow" ? party.Preset.BowPriority : party.Preset.ArmorPriority;
                foreach (int level in priority == "high" ? new[] { 2, 1 } : new[] { 1, 2 })
                {
                    int n = Math.Min(need, Available(s, product, level)); need -= n;
                    quota.Equipment.Add(new BaseEquipmentStock { Product = product, Level = level, Count = n });
                }
                var rule = s.Supply.Rules.Single(r => r.Product == product);
                double bonus = demand == 0 ? 0 : quota.Equipment.Where(e => e.Product == product).Sum(e => e.Count * (e.Level == 1 ? rule.LowBonus : rule.HighBonus)) / demand;
                if(product=="supply"){quota.SwordBonus=quota.BowBonus=bonus;quota.ArmorBonus=demand==0?0:quota.Equipment.Sum(e=>e.Count*(e.Level==1?rule.LowDefense:rule.HighDefense))/demand;}
                else if (product == "sword") quota.SwordBonus = bonus; else if (product == "bow") quota.BowBonus = bonus; else quota.ArmorBonus = bonus;
            }
            return new BaseSupplyPlan { Stamp = BaseMuster.Snapshot(s), Quota = quota };
        }
        private static string Shape(BaseEquipmentQuota q) => q.Melee + ":" + q.Ranged + ":" + q.Cavalry + ":" + string.Join(",", q.Leaders) + "|" + string.Join("|", q.Equipment.Select(e => e.Product + ":" + e.Level + ":" + e.Count)) + "|" + string.Join(":", new[] { q.SwordBonus, q.BowBonus, q.ArmorBonus, q.AttackFactor, q.ReturnedWear, q.LostWear }.Select(n => n.ToString("R", CultureInfo.InvariantCulture)));
        public static bool Reserve(BaseState s, BaseSupplyPlan plan)
        {
            BaseEconomy.Validate(s);
            if (plan?.Quota == null || string.IsNullOrWhiteSpace(plan.Quota.BattleId) || s.Supply.FinishedIds.Contains(plan.Quota.BattleId) || s.Supply.Quotas.Any(q => q.BattleId == plan.Quota.BattleId || q.PartyId == plan.Quota.PartyId) || plan.Stamp != BaseMuster.Snapshot(s)) return false;
            var actual = Preview(s, plan.Quota.PartyId, plan.Quota.Mode).Quota;
            if (Shape(actual) != Shape(plan.Quota)) return false;
            actual.BattleId = plan.Quota.BattleId; s.Supply.Quotas.Add(actual); BaseEconomy.Validate(s); return true;
        }
        public static bool Cancel(BaseState s, string battleId)
        {
            BaseEconomy.Validate(s); var q = s.Supply.Quotas.FirstOrDefault(x => x.BattleId == battleId);
            if (q == null || q.Phase != "prepared") return false;
            s.Supply.Quotas.Remove(q); s.Supply.FinishedIds.Add(battleId); return true;
        }
        internal static BaseEquipmentQuota ReservePrepared(BaseState state, BaseOutsideParty party, BasePreparedRoster roster)
        {
            int grade = roster.SupplyGrade;
            if (roster.Troops.Count == 0 ? grade != 0 : grade is not (1 or 2))
                throw new InvalidOperationException("出战人数与选定军需等级不一致");
            var teams = roster.Units.Where(unit => unit.Kind != SoldierKind.Hero).ToLookup(unit => unit.TeamId);
            var quota = new BaseEquipmentQuota
            {
                BattleId = Guid.NewGuid().ToString("N"), PartyId = party.Id, Mode = "none",
                Leaders = roster.Leaders.ToList(), FullConsumptionGrade = grade,
                Teams = roster.Leaders.Select(id =>
                {
                    int soldiers = teams[id].Count();
                    return new BaseTeamSupply { LeaderId = id, Soldiers = soldiers, Low = grade == 1 ? soldiers : 0, High = grade == 2 ? soldiers : 0 };
                }).ToList(),
                PartySubset = roster.ArrivedPartyId != null, Rules = state.Supply.Rules.Select(r => r.Copy()).ToList(),
                Melee = roster.Troops.Values.Count(w => w.Kind == BaseTroopKind.Melee),
                Ranged = roster.Troops.Values.Count(w => w.Kind == BaseTroopKind.Ranged),
                Cavalry = roster.Troops.Values.Count(w => w.Kind == BaseTroopKind.Cavalry),
                AttackFactor = BaseEconomy.AttackFactor(state), ReturnedWear = state.Supply.ReturnedWear, LostWear = state.Supply.LostWear
            };
            int low = quota.Teams.Sum(t => t.Low), high = quota.Teams.Sum(t => t.High);
            if (low > Available(state, "supply", 1) || high > Available(state, "supply", 2))
                throw new InvalidOperationException("军需库存已变化，请重新备战");
            quota.Equipment.Add(new BaseEquipmentStock { Product = "supply", Level = 1, Count = low });
            quota.Equipment.Add(new BaseEquipmentStock { Product = "supply", Level = 2, Count = high });
            var rule = quota.Rules.Single(r => r.Product == "supply");
            var totals = new BaseTeamSupply { Soldiers = quota.Demand("supply"), Low = low, High = high };
            quota.SwordBonus = quota.BowBonus = totals.Average(rule.LowBonus, rule.HighBonus);
            quota.ArmorBonus = totals.Average(rule.LowDefense, rule.HighDefense);
            state.Supply.Quotas.Add(quota);
            return quota;
        }
        public static bool StartBattle(BaseState s, string battleId)
        {
            BaseEconomy.Validate(s); var q = s.Supply.Quotas.FirstOrDefault(x => x.BattleId == battleId);
            if (q == null || q.Phase != "prepared") return false;
            q.AttackFactor = BaseEconomy.AttackFactor(s); q.Phase = "fighting"; s.Muster.Parties.Single(p => p.Id == q.PartyId).InCombat = true; return true;
        }
        public static bool Finish(BaseState s, string battleId, BaseSupplyOutcome result)
        {
            BaseEconomy.Validate(s); var q = s.Supply.Quotas.FirstOrDefault(x => x.BattleId == battleId);
            if (q == null || q.Phase != "fighting" || result == null) return false;
            if (new[] { result.SafeMelee, result.LostMelee, result.SafeRanged, result.LostRanged, result.SafeCavalry, result.LostCavalry }.Any(n => n < 0) || result.SafeMelee + (long)result.LostMelee > q.Melee || result.SafeRanged + (long)result.LostRanged > q.Ranged || result.SafeCavalry + (long)result.LostCavalry > q.Cavalry) throw new ArgumentException("军需损耗战报超过锁定编制");
            if (q.FullConsumptionGrade != 0 && result.Safe("supply") + result.Lost("supply") != q.Demand("supply"))
                throw new ArgumentException("军需实耗与实际上场人数不一致");
            foreach (var e in q.Equipment)
            {
                int demand = q.Demand(e.Product); if (demand == 0 || e.Count == 0) continue;
                var stock = s.Inventory.Equipment.Single(x => x.Product == e.Product && x.Level == e.Level);
                if (q.FullConsumptionGrade != 0)
                {
                    stock.Count -= e.Count;
                    continue;
                }
                var wear = s.Supply.Wear.Single(w => w.Product == e.Product && w.Level == e.Level);
                double amount = wear.Fraction;
                if (q.ByTeam)
                {
                    for (int slot = 0; slot < q.Teams.Count; slot++)
                    {
                        var team = q.Teams[slot];
                        if (!result.Teams.TryGetValue(slot, out var count) || team.Soldiers == 0) continue;
                        if (count.safe + count.lost > team.Soldiers) throw new ArgumentException("军需损耗超过出战队伍人数");
                        amount += (e.Level == 1 ? team.Low : team.High) *
                            (q.ReturnedWear * count.safe + q.LostWear * count.lost) / team.Soldiers;
                    }
                }
                else amount += e.Count * (q.ReturnedWear * result.Safe(e.Product) + q.LostWear * result.Lost(e.Product)) / demand;
                int lost = Math.Min(e.Count, (int)Math.Floor(amount + 1e-9)); wear.Fraction = Math.Max(0, amount - lost);
                stock.Count -= lost;
            }
            s.Muster.Parties.Single(p => p.Id == q.PartyId).InCombat = false;
            s.Supply.Quotas.Remove(q); s.Supply.FinishedIds.Add(battleId); BaseEconomy.Validate(s); return true;
        }
        // 用真实锁定快照加在基础属性上，再由既有战技／暴击／扣防逻辑结算。
        public static BattleScenario ApplyToScenario(BaseState s, string battleId, BattleScenario original, BattleSide side = BattleSide.A)
        {
            BaseEconomy.Validate(s); var q = s.Supply.Quotas.Single(x => x.BattleId == battleId); var copy = original.Clone();
            var army = side == BattleSide.A ? copy.A : copy.B;
            if (army.MeleeCount != q.Melee || army.RangedCount != q.Ranged || army.CavalryCount != q.Cavalry || army.HeroCount != q.Leaders.Count) throw new ArgumentException("棋盘编制与实际军需分母不一致");
            string snapshotId = battleId + ":" + (int)side;
            if (!string.IsNullOrEmpty(copy.SupplyBattleId))
            { if (copy.SupplyBattleId != snapshotId) throw new ArgumentException("不能叠加不同战斗／阵营的装备快照"); return copy; }
            copy.SupplyBattleId = snapshotId;
            double sword = q.ByTeam ? 0 : q.SwordBonus, bow = q.ByTeam ? 0 : q.BowBonus, armor = q.ByTeam ? 0 : q.ArmorBonus;
            army.Melee.Damage = (float)((army.Melee.Damage + sword) * q.AttackFactor); army.Cavalry.Damage = (float)((army.Cavalry.Damage + sword) * q.AttackFactor);
            army.Ranged.Damage = (float)((army.Ranged.Damage + bow) * q.AttackFactor); army.Hero.Damage *= (float)q.AttackFactor;
            army.Melee.Defense += (float)armor; army.Ranged.Defense += (float)armor; army.Cavalry.Defense += (float)armor;
            return copy;
        }
    }
}

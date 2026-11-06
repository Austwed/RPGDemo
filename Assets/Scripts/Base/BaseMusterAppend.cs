using System;
using System.Linq;

namespace BaseConquest.Base
{
    public static partial class BaseMuster
    {
        private static BaseOutsideParty AppendParty(BaseState s, string id)
        {
            var party = s.Muster.Parties.FirstOrDefault(p => p.Id == id && p.Purpose == "expedition");
            if (s.Aftermath.GameOver || party == null) throw new InvalidOperationException("只能追加到实际外出的出征队伍");
            if (s.Campaign.HasActiveBattle || s.Muster.Parties.Any(p => p.InCombat)) throw new InvalidOperationException("战斗中不能追加基地兵力");
            if (s.Supply.Quotas.Any(q => q.PartyId == id)) throw new InvalidOperationException("先取消本队军需准备，追加后重新计算配额");
            return party;
        }
        public static BaseMusterPlan PreviewAppend(BaseState s, string partyId, int melee, int ranged, int cavalry, bool ignoreProtection = false)
        {
            BaseEconomy.Validate(s); AppendParty(s, partyId);
            if (new[] { melee, ranged, cavalry }.Any(n => n < 0 || n > 30)) throw new ArgumentException("追加请求每兵种须在0～30之间");
            var plan = new BaseMusterPlan { Id = Guid.NewGuid().ToString("N"), PartyId = partyId, Purpose = "expedition", Stamp = Snapshot(s), IgnoreProtection = ignoreProtection, RequestedMelee = melee, RequestedRanged = ranged, RequestedCavalry = cavalry };
            int existingTotal=s.Workers.Where(w=>w.Away&&w.PartyId==partyId).Sum(w=>w.Count);
            if(existingTotal>BattleSoldierLimit)throw new InvalidOperationException("本队超过24人，请返营调整");
            plan.SoldierLimit=BattleSoldierLimit-existingTotal;
            var requested = new BaseArmyPreset { Melee = melee, Ranged = ranged, Cavalry = cavalry };
            var capped = requested.Copy();
            foreach (BaseTroopKind kind in Enum.GetValues(typeof(BaseTroopKind)))
            {
                int existing = s.Workers.Where(w => w.Away && w.PartyId == partyId && w.Kind == kind).Sum(w => w.Count);
                if (existing > 30) throw new InvalidOperationException("本队已有编制超出当前兵种上限");
                capped.SetCount(kind, Math.Min(requested.Count(kind), 30 - existing));
            }
            SelectWorkers(s, plan, capped, ApplyAppend, true);
            plan.MissingMelee += melee - capped.Melee; plan.MissingRanged += ranged - capped.Ranged; plan.MissingCavalry += cavalry - capped.Cavalry;
            var before = BaseEconomy.Preview(s); var trial = s.Copy(); ApplyAppend(trial, plan); var after = BaseEconomy.Preview(trial);
            plan.FoodLoss = before.FoodProduced - after.FoodProduced; plan.ProductLoss = before.ProductsMade - after.ProductsMade;
            plan.RemainingBase = trial.Workers.Where(w => !w.Away).Sum(w => w.Count); return plan;
        }
        public static bool ConfirmAppend(BaseState s, BaseMusterPlan plan)
        {
            BaseEconomy.Validate(s);
            if (plan == null || !Guid.TryParseExact(plan.Id, "N", out _) || string.IsNullOrEmpty(plan.PartyId) || plan.Purpose != "expedition" || plan.Leaders == null || plan.Leaders.Count != 0 || plan.Takes == null || plan.Total == 0 || s.Muster.AppliedAppendIds.Contains(plan.Id) || s.Muster.AppliedAppendIds.Count >= 100000 || plan.Stamp != Snapshot(s)) return false;
            BaseMusterPlan actual;
            try { actual = PreviewAppend(s, plan.PartyId, plan.RequestedMelee, plan.RequestedRanged, plan.RequestedCavalry, plan.IgnoreProtection); }
            catch (ArgumentException) { return false; } catch (InvalidOperationException) { return false; }
            if (Shape(actual) != Shape(plan)) return false;
            string prefix = plan.PartyId + ".append." + plan.Id + ".";
            if (s.Muster.Parties.Single(p => p.Id == plan.PartyId).Sources.Any(x => x.Id.StartsWith(prefix, StringComparison.Ordinal))) return false;
            actual.Id = plan.Id; ApplyAppend(s, actual); s.Muster.AppliedAppendIds.Add(actual.Id); BaseEconomy.Validate(s); return true;
        }
        private static void ApplyAppend(BaseState s, BaseMusterPlan plan)
        {
            var party = AppendParty(s, plan.PartyId);
            TransferWorkers(s, party, plan, party.Id + ".append." + plan.Id + ".");
            if(party.CommanderTroops!=null)party.CommanderTroops=CurrentFormation(s,party);
            BaseDevelopment.Allocate(s);
        }
    }
}

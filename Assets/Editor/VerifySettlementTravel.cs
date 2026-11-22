using System;
using System.IO;
using System.Linq;
using BaseConquest.Base;
using BaseConquest.Battle;
using UnityEngine;

namespace BaseConquest.Editor
{
    public static class VerifySettlementTravel
    {
        public static void Run()
        {
            var args = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(args, "-settlementFixture");
            if (index < 0 || index + 1 >= args.Length) throw new ArgumentException("Provide -settlementFixture with the old village arrival save copy.");
            var raw = File.ReadAllText(args[index + 1]);
            var legacy = JsonUtility.FromJson<BaseSaveDocument>(raw).State;
            if (legacy?.Muster?.Parties?.Count is not > 0) legacy = JsonUtility.FromJson<BaseState>(raw);
            BaseEconomy.Validate(legacy);
            Need(legacy.Campaign.CurrentMapId == BaseDemoFinale.Home, "需要已返乡但仍保留外出队伍的存档");

            var expedition = legacy.Muster.Parties.Single(p => p.Purpose == "expedition");
            var travelers = legacy.Workers.Where(w => w.Away && w.PartyId == expedition.Id).ToArray();
            var moved = travelers.SelectMany(w => w.SoldierNumbers).ToHashSet();
            var remaining = legacy.Workers.Where(w => !w.Away).SelectMany(w => w.SoldierNumbers).ToHashSet();
            Need(moved.Count > 0 && legacy.Muster.ReturnedIds.All(id => id != expedition.Id), "旧存档无待归队士兵");

            var state = legacy.Copy();
            OnWorld(state, "bandit");
            BaseWorldJourney.EnterScene(state, "bandit_east_entry");
            Need(state.Muster.Parties.All(p => p.Id != expedition.Id), "抵达友方黑鸦寨未结束该次调动");
            var atBlackCrow = BasePersonnel.Soldiers(state).ToDictionary(u => u.SoldierNumber);
            Need(moved.All(number => !atBlackCrow[number].Away && !atBlackCrow[number].Remote &&
                atBlackCrow[number].State == BaseScriptNames.Bandit), "已抵达人员未驻扎黑鸦寨");
            Need(remaining.All(number => !atBlackCrow[number].Away && atBlackCrow[number].Remote), "基地留守兵被判为在黑鸦寨");

            var gate = BaseSpatial.Objects(state).Single(o => o.Id == "demo_camp_east");
            state.PlayerX = gate.X;
            state.PlayerY = gate.Y;
            var route = new[] { "bandit", "home" };
            var walk = state.Copy();
            var peaceful = new BaseBattlePreparation(walk, BasePrepRoute.Travel, draftTravelTroops:false);
            Need(peaceful.Units.All(u => u.Kind == SoldierKind.Hero || !u.Selected), "友方路线自动调动了驻军");
            int walkFood = walk.Inventory.Food;
            BaseWorldJourney.Depart(walk, "bandit_east_entry", route, peaceful);
            Need(walk.Inventory.Food == walkFood && BaseWorldJourney.Step(walk, _ => null), "仅角色出门错误预扣路粮");
            BaseWorldJourney.EnterScene(walk, "home_entry");
            Need(walk.Workers.Where(w => w.SoldierNumbers.Any(moved.Contains)).All(w => !w.Away &&
                w.StationId == "bandit"), "仅角色出门带走了黑鸦寨驻军");

            var military = new BaseBattlePreparation(state, BasePrepRoute.Travel, draftTravelTroops:false);
            var transfer = military.Units.First(u => moved.Contains(u.SoldierNumber));
            Need(military.ToggleSoldier(military.PlayerId,transfer), "无法从黑鸦寨明确调动驻军");
            var outgoing = state.Inventory.Food;
            BaseWorldJourney.Depart(state, "bandit_east_entry", route, military);
            Need(state.Inventory.Food == outgoing - state.Rules.SoldierFood, "单兵调动预扣路粮错误");
            var traveling = state.Muster.Parties.Single(p => p.Purpose == "expedition");
            Need(BaseWorldJourney.Step(state, _ => null) && state.Day == legacy.Day + 1, "黑鸦寨至白桦村应行军一天");
            var beforeArrival = state.Copy();
            BaseWorldJourney.EnterScene(state, "home_entry");
            VerifyArrival(state, traveling, beforeArrival);
            Need(state.Workers.Where(w => w.SoldierNumbers.Any(n => n != transfer.SoldierNumber && moved.Contains(n))).All(w =>
                w.StationId == "bandit" && !w.Away), "黑鸦寨留守人员被搬到了白桦村");

            var restored = ReloadLegacy(legacy);
            VerifyArrival(restored, expedition, legacy);
            Need(restored.Day == legacy.Day, "旧档修正额外推进了一天");

            var draft = new BaseBattlePreparation(state, BasePrepRoute.Travel);
            var soldier = draft.Units.Single(u => u.SoldierNumber == transfer.SoldierNumber);
            Need(!draft.TravelingParty && draft.Units.Where(u => u.Kind != SoldierKind.Hero).All(u => u.SoldierNumber == transfer.SoldierNumber
                || legacy.Workers.Any(w => w.VillageResident && w.SoldierNumbers.Contains(u.SoldierNumber))), "白桦村备战混入异地兵");
            foreach (var u in draft.Units) u.Selected = u.Id == draft.PlayerId;
            soldier.TeamId = draft.PlayerId;
            soldier.Selected = true;
            var back = BaseWorldJourney.Plan(state, null, "camp", "home_entry");
            Need(back.Reachable && back.Days == 2, "白桦村至灰岩营地应经黑鸦寨行军两天");
            BaseWorldJourney.Depart(state, "home_entry", back.Path, draft);
            Need(BaseWorldJourney.Step(state, _ => null) && BaseWorldJourney.Step(state, _ => null), "返灰岩营地未结算完整行军");
            BaseWorldJourney.EnterScene(state, "camp_entry");
            Need(state.Muster.Parties.All(p => p.Purpose != "expedition") &&
                state.Workers.Single(w => w.SoldierNumbers.Contains(soldier.SoldierNumber)) is { Away: false, VillageResident: false } &&
                state.Workers.Where(w => w.SoldierNumbers.Any(n => n != soldier.SoldierNumber && moved.Contains(n))).All(w => !w.Away && w.StationId == "bandit"),
                "白桦村至灰岩营地必须只搬迁此次随队人员");
            Debug.Log("[VerifySettlementTravel] PASS: blackcrow garrison, ordinary travel, explicit village transfer, legacy load and selected return.");
        }

        private static void OnWorld(BaseState state, string node)
        {
            var world = BaseSpatial.Layout(state, "world");
            state.Campaign.Location = world.Region;
            state.Campaign.CurrentMapId = world.SourceMapId;
            state.Campaign.WorldJourney.NodeId = node;
            state.PlayerX = world.SpawnX;
            state.PlayerY = world.SpawnY;
            BaseAdventure.Arrive(state);
        }

        private static BaseState ReloadLegacy(BaseState legacy)
        {
            string path = Path.Combine(Path.GetTempPath(), "demo-village-arrival-" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                File.WriteAllText(path, JsonUtility.ToJson(new BaseSaveDocument { DemoSaveVersion = BaseDemo.SaveVersion, State = legacy }));
                return new BaseSaveStore(path, d => JsonUtility.ToJson(d), JsonUtility.FromJson<BaseSaveDocument>, demo: true).Load();
            }
            finally { if (File.Exists(path)) File.Delete(path); }
        }

        internal static void VerifyArrival(BaseState arrived, BaseOutsideParty expedition, BaseState before)
        {
            Need(arrived.Campaign.CurrentMapId == BaseDemoFinale.Home && arrived.Day == before.Day &&
                arrived.Muster.Parties.All(p => p.Id != expedition.Id) && arrived.Muster.ReturnedIds.Count(id => id == expedition.Id) == 1,
                "到白桦村应当天解散出征队一次");
            foreach (var captain in expedition.Leaders)
            {
                var leader = arrived.Leaders.Single(l => l.Id == captain.LeaderId);
                Need(!leader.Away && leader.VillageResident && leader.PartyId == "", "随队队长没有入驻白桦村");
            }
            var units = BasePersonnel.Soldiers(arrived).ToDictionary(u => u.SoldierNumber);
            Need(units.Count == before.Workers.Sum(w => w.Count), "返乡前后士兵编号总数不一致");
            foreach (var bucket in before.Workers)
                foreach (int number in bucket.SoldierNumbers)
                {
                    var result = arrived.Workers.Single(w => w.SoldierNumbers.Contains(number));
                    var unit = units[number];
                    bool wasTraveling = bucket.Away && bucket.PartyId == expedition.Id;
                    Need(result.Health == bucket.Health && result.LastBattleDay == bucket.LastBattleDay, "返乡改变了伤情或参战日");
                    Need(wasTraveling
                        ? !result.Away && result.VillageResident && result.PartyId == "" && !unit.Remote
                        : result.Away == bucket.Away && result.VillageResident == bucket.VillageResident && result.Relocating == bucket.Relocating,
                        "返乡改变了留守兵归属，或随队人员仍处在异地");
                }
        }

        private static void Need(bool ok, string message)
        {
            if (!ok) throw new InvalidOperationException(message);
        }
    }
}

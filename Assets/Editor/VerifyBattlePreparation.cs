using System;
using System.Linq;
using System.Reflection;
using BaseConquest.Base;
using BaseConquest.Battle;
using BaseConquest.RealtimePrototype;
using UnityEngine;

namespace BaseConquest.Editor
{
    public static class VerifyBattlePreparation
    {
        public static void Run()
        {
            VerifyNamedWorker();
            VerifySavedPreferences();
            VerifyRuins();
            VerifyDefenseAndAttack();
            Debug.Log("[VerifyBattlePreparation] PASS");
        }

        private static void VerifyNamedWorker()
        {
            var state = Load("defense");
            foreach (var batch in state.Workers) batch.SoldierNumbers = null;
            state.LastSoldierNumber = 0;
            BaseEconomy.Validate(state);
            var original = state.Workers.SelectMany(w => w.SoldierNumbers).ToArray();
            Need(original.SequenceEqual(Enumerable.Range(1, original.Length)), "旧档首次编号没有沿用原名册顺序");
            if (!state.Workers.Any(w => !w.Away && w.FacilityId=="farm" && w.Count>=2))
                Need(BaseEconomy.MoveWorkers(state,BaseTroopKind.Melee,"","farm",2),"无法准备两名粮田工人夹具");
            var worker = state.Workers.First(w => !w.Away && w.FacilityId == "farm" && w.Count >= 2);
            int number = worker.SoldierNumbers[1], neighbor = worker.SoldierNumbers[0];
            Need(BaseEconomy.MoveWorker(state, number, "farm", ""), "点名调离失败");
            Need(state.Workers.Single(w => w.SoldierNumbers.Contains(number)).FacilityId == "" &&
                state.Workers.Single(w => w.SoldierNumbers.Contains(neighbor)).FacilityId == "farm", "同批的另一名士兵被调离");
            Need(BaseEconomy.MoveWorker(state, number, "", "farm"), "原岗合批失败");
            BaseDevelopment.Allocate(state);
            var pick = BaseMuster.SoldierRows(state, worker.Kind).Single(r => r.Pick.SoldierNumber == number).Pick;
            var preset = state.Muster.Presets.Single(p => p.Purpose == "expedition").Copy();
            preset.SelectLeaders(new[] { state.PlayerLeaderId });
            foreach (BaseTroopKind kind in Enum.GetValues(typeof(BaseTroopKind))) preset.SetCount(kind, kind == worker.Kind ? 1 : 0);
            var plan = BaseMuster.Preview(state, "expedition", true, preset, new[] { pick });
            Need(BaseMuster.Confirm(state, plan) && state.Workers.Where(w => w.Away).SelectMany(w => w.SoldierNumbers).SequenceEqual(new[] { number }), "出征没有带走点中的士兵");
            state = JsonUtility.FromJson<BaseState>(JsonUtility.ToJson(state));
            BaseEconomy.Validate(state);
            Need(BaseMuster.Return(state, plan.Id) && state.Workers.All(w => !w.Away), "读档归队失败");
            var roster = BasePersonnel.Soldiers(state).ToList();
            Need(roster.Select(u => u.SoldierNumber).SequenceEqual(original) &&
                roster.Single(u => u.SoldierNumber == number).Key == "soldier:" + number, "调岗、出征或读档后人员编号变化");
            Debug.Log("[VerifyBattlePreparation] 固定编号、逐人调岗、出征与读档归队 PASS");
        }

        private static void VerifySavedPreferences()
        {
            var s=Load("defense");
            var leader=s.Leaders.Single(l=>l.Id==s.PlayerLeaderId);
            var soldiers=BasePersonnel.Soldiers(s,SoldierKind.Melee).ToArray();
            var id=soldiers.Last().SoldierNumber;
            BaseMuster.SetPreferenceCount(s,leader,BaseTroopKind.Melee,0);
            Need(BaseMuster.TogglePreference(s,leader,id),"不能按实名保存偏好");
            var before=s.Workers.Select(w=>(w.FacilityId,w.LastBattleDay)).ToArray();
            s=JsonUtility.FromJson<BaseState>(JsonUtility.ToJson(s));
            BaseEconomy.Validate(s);
            var draft=new BaseBattlePreparation(s,BasePrepRoute.Defense);
            Need(draft.Units.Any(u=>u.SoldierNumber==id && u.Selected && u.TeamId==draft.PlayerId) &&
                before.SequenceEqual(s.Workers.Select(w=>(w.FacilityId,w.LastBattleDay))),"恢复编队未优先点名或提前占用人员");
            var named=s.Workers.Single(w=>w.SoldierNumbers.Contains(id));
            foreach(var w in s.Workers.Where(w=>w.Kind==BaseTroopKind.Melee)) w.VillageResident=true;
            s.Workers.Add(new BaseWorkerBucket{Count=1,Kind=BaseTroopKind.Melee,Health=100,MaxHealth=100});
            BaseDevelopment.Allocate(s);
            var replacement=new BaseBattlePreparation(s,BasePrepRoute.Defense);
            Need(replacement.Units.Any(u=>u.Kind==SoldierKind.Melee && u.TeamId==replacement.PlayerId && u.Selected && u.SoldierNumber!=id),
                "异地指定兵应由可用同兵种代替");
            Debug.Log("[SavedPreferences] PASS");
        }

        private static BaseState Load(string id)
        {
            var asset = Resources.Load<TextAsset>("DemoStages/" + id + "-male");
            var state = JsonUtility.FromJson<BaseState>(asset.text);
            BaseEconomy.Validate(state);
            return state;
        }

        private static void At(BaseState state, string id)
        {
            var item = BaseSpatial.Objects(state).Single(o => o.Id == id);
            var point = item.Blocks ? BaseStageWalk.Door(state, id) : new BasePoint(item.X, item.Y);
            state.PlayerX = point.X;
            state.PlayerY = point.Y;
            BaseAdventure.Arrive(state);
        }

        private static void Need(bool ok, string message)
        {
            if (!ok) throw new InvalidOperationException(message);
        }

        private static BaseState ReadLegacyRoster(BaseState state)
        {
            var legacy = state.Copy();
            legacy.LastSoldierNumber = 0;
            foreach (var worker in legacy.Workers) worker.SoldierNumbers = null;
            foreach (var party in legacy.Muster.Parties)
                if (party.CommanderTroops != null)
                    foreach (var team in party.CommanderTroops) team.SoldierNumbers = null;
            if (legacy.Campaign.HasActiveBattle)
                foreach (var troop in legacy.Campaign.ActiveBattle.Troops) troop.SoldierNumbers = null;
            legacy = JsonUtility.FromJson<BaseState>(JsonUtility.ToJson(legacy));
            BaseEconomy.Validate(legacy);
            return legacy;
        }

        private static void ClearLoot(BaseState state)
        {
            foreach (var prisoner in state.Aftermath.Prisoners.Where(p => p.Status == "held").ToArray())
                Need(BaseAftermath.Decide(state, prisoner.Id, "release"), "战果俘虏");
            foreach (var manifest in state.Aftermath.Loot.Where(l => !l.Closed).ToArray())
            {
                foreach (var item in manifest.Items.Where(i => i.Status == "pending").ToArray())
                    Need(BaseBattleLoot.Resolve(state, manifest.BattleId, item.Id, state.PlayerLeaderId, true), "领取战果");
                Need(BaseBattleLoot.Finish(state, manifest.BattleId), "关闭战果");
            }
        }

        private static RealtimeCampaignBattle Start(BaseState state, BaseBattlePreparation draft)
        {
            var cell = draft.Positions[draft.PlayerId];
            Need(draft.Problem(state, true) == "", draft.Problem(state, true));
            var session = draft.Begin(state);
            var battle = new RealtimeCampaignBattle(session);
            var expected = draft.Units.Where(u => u.SoldierNumber > 0 && draft.Positions.ContainsKey(u.Id))
                .ToDictionary(u => u.SoldierNumber, u => u.TeamId);
            var opening = session.Opening;
            Need(opening.Troops.Count == expected.Count && opening.Troops.Select((troop, i) =>
                troop.SoldierNumbers.Count == 1 && expected.TryGetValue(troop.SoldierNumbers[0], out var team) &&
                opening.Leaders[opening.TroopSlots[i]].Id == team).All(matches => matches), "实际出战士兵编号或分队与落位不一致");
            for (int i = 0; i < opening.Troops.Count; i++)
            {
                int number = opening.Troops[i].SoldierNumbers[0];
                var actor = session.UnitForTroop(i);
                Need(actor?.SoldierNumber == number && actor.OriginCommanderSlot == opening.TroopSlots[i] &&
                    actor.Cell.Equals(draft.Positions["soldier:" + number]), "战场实体与所点士兵的编号、队长或部署格不一致");
            }
            Need(battle.Battle.Phase == BattlePhase.Fighting && battle.Model.Player != null, "草案未开战");
            Need(battle.Model.Units.Count(u => u.Side == PrototypeSide.Ally) == draft.Positions.Count, "未落位人员进了战场");
            Need((int)battle.Model.Player.Position.X == cell.X && (int)battle.Model.Player.Position.Z == cell.Z, "未使用主角落点");
            foreach (var rider in battle.Model.Units.Where(u => u.Side == PrototypeSide.Ally && u.Mounted))
                Need(rider.FootprintLength == 1 && battle.Model.Footprint(rider, rider.Cell, rider.Facing).All(n =>
                    battle.Battle.Board.Cell(new GridPosition(n % battle.Model.Arena.FineColumns / 2,
                        n / battle.Model.Arena.FineColumns / 2)).Zone == DeploymentZone.A), "骑兵占格跨出部署区");
            Need(draft.Problem(state, true) != "" && state.Supply.Quotas.Count == 1, "重复提交未拦截");
            return battle;
        }

        private static void VerifyRuins()
        {
            var state = Load("camp");
            At(state, "home");
            Need(BaseDemo.AcknowledgeWaking(state), "醒来");
            At(state, "exit_east");
            Need(BaseCampaign.Travel(state, "town"), "进入灰岩镇");
            At(state, "town_quest");
            Need(BaseDemo.AcceptLordQuest(state), "接取委托");
            At(state, "demo_ruins_exit");
            Need(BaseCampaign.Travel(state, "outpost"), "前往沉眠古窟");
            At(state, "demo_ruins_investigate");
            var draft = new BaseBattlePreparation(state, BasePrepRoute.Ruins);
            Need(draft.KnownEnemyPower(state) == null, "首战泄露敌情");
            Need(draft.AutoPlace() > 0, "古窟没有可用部署区");
            var battle = Start(state, draft);
            BaseDemoRuins.Configure(battle);
            var chief = battle.Model.Units.Single(u => u.Side == PrototypeSide.Enemy && u.Role == PrototypeRole.Leader);
            chief.Health = 1;
            chief.KnockedDown = true;
            var pawn = battle.Battle.Soldier(chief.Id - 1);
            pawn.Health = 1;
            pawn.KnockedDown = pawn.DeathResolved = true;
            battle.Model.SetPaused(false);
            battle.Tick(.02);
            VerifyChenanAndRuins.WalkOutOfRuins(battle);
            Need(BaseDemoRuins.Complete(state, battle), "古窟首通");
            ClearLoot(state);
            At(state, "demo_ruins_investigate");
            var repeat = new BaseBattlePreparation(state, BasePrepRoute.Ruins);
            var sweep = repeat.PreviewSweep(state);
            Need(sweep.EnemyPower > 0 && (sweep.Problem == "") == (sweep.AllyPower > sweep.EnemyPower * 1.5), "扫荡门槛与正式战力不一致");
            VerifyPreparedSweepSupply(state.Copy());
            repeat.AutoPlace();
            Need(!Start(state, repeat).Session.Opening.RuinsFirstClear, "古窟重战误作首通");
            Debug.Log("[VerifyBattlePreparation] 古窟首通、重打、扫荡门槛 PASS");
        }

        private static BaseEquipmentStock Stock(BaseState state, int grade) => state.Inventory.Equipment.Single(e => e.Product == "supply" && e.Level == grade);

        private static void VerifyPreparedSweepSupply(BaseState state)
        {
            var stats = new SoldierStats { Health = 1000, Damage = 1000, Defense = 100 };
            state.Workers.Add(new BaseWorkerBucket { Kind = BaseTroopKind.Melee, Count = 2, Health = stats.Health,
                MaxHealth = stats.Health, HasCombatProfile = true, CombatStats = stats });
            Stock(state, 2).Count = 2;
            var draft = new BaseBattlePreparation(state, BasePrepRoute.Ruins) { SupplyGrade = 2 };
            foreach (var soldier in draft.Units.Where(u => u.Kind != SoldierKind.Hero))
            {
                soldier.Selected = true;
                soldier.TeamId = draft.PlayerId;
            }
            string before = JsonUtility.ToJson(state);
            var estimate = draft.PreviewSweep(state);
            Need(estimate.Problem == "", "扫荡军需夹具无法扫荡：" + estimate.Problem);
            Need(before == JsonUtility.ToJson(state), "扫荡预览修改军需库存");
            draft.Sweep(state);
            Need(Stock(state, 2).Count == 0 && state.Campaign.Reports.Last().GearLost == 2 && state.Supply.Quotas.Count == 0,
                "扫荡未按实际编队人数全额扣军需");
        }

        private static void VerifyDefenseAndAttack()
        {
            var state = Load("defense");
            At(state, "demo_defense_start");
            Stock(state, 1).Count = Stock(state, 2).Count = 24;
            var before = JsonUtility.ToJson(state);
            var draft = new BaseBattlePreparation(state, BasePrepRoute.Defense);
            var soldier = draft.Units.First(u => u.Selected && u.Kind != SoldierKind.Hero && u.TeamId == draft.PlayerId);
            var cell = draft.Board.Positions().First(p => draft.CanPlace(draft.PlayerId, p));
            Need(!draft.Place(soldier.Id, cell), "队长未落位时布置士兵");
            Need(draft.AutoPlace() > 0, "首防不能自动部署");
            Need(!draft.Place(soldier.Id, draft.Positions[draft.PlayerId]), "接受重叠部署");
            Need(draft.SupplyGrade == 1 && draft.SupplyProblem(state, false) == "" && draft.SupplyProblem(state, true) == "", "默认一级军需未生效");
            draft.SupplyGrade = 1;
            var limited = state.Copy();
            Stock(limited, 1).Count = draft.RequiredSupply(true) - 1;
            Need(draft.SupplyProblem(limited, false) != "" && draft.SupplyProblem(limited, true) != "", "军需不足时未拦截");
            draft.Recall(soldier.Id);
            int deployed = draft.RequiredSupply(true);
            Need(deployed < draft.RequiredSupply(false) && draft.SupplyProblem(limited, true) == "", "军需未按实际落位校验");
            Need(before == JsonUtility.ToJson(state), "预览、落位或撤回修改了真实状态");
            var retreatState = state.Copy();
            var retreatDraft = new BaseBattlePreparation(retreatState, BasePrepRoute.Defense) { SupplyGrade = 2 };
            retreatDraft.AutoPlace();
            var retreat = Start(retreatState, retreatDraft);
            Need(BaseDemoCombat.Fail(retreatState, retreat) && Stock(retreatState, 2).Count == 24 - retreatDraft.RequiredSupply(true) &&
                Stock(retreatState, 1).Count == 24 && retreatState.Supply.Quotas.Count == 0, "正式撤退军需未全扣或错扣等级");

            var wear = state.Supply.Wear.Select(entry => entry.Fraction).ToArray();
            var battle = Start(state, draft);
            var tier = state.Supply.Rules.Single(r => r.Product == "supply");
            var member = battle.Battle.Soldiers.First(u => u.Side == BattleSide.A && u.Kind != SoldierKind.Hero && u.OriginCommanderSlot == 0);
            Need(Math.Abs(member.ProvisionWeaponBonus - tier.LowBonus) < .0001 && Math.Abs(member.ProvisionArmorBonus - tier.LowDefense) < .0001,
                "所选军需等级未作用于实际上场士兵");
            Need(Stock(state, 1).Count == 24 && BaseSupply.Available(state, "supply", 1) == 24 - deployed, "确认开战应只锁定实际用量");
            var restored = JsonUtility.FromJson<BaseState>(JsonUtility.ToJson(state));
            BaseEconomy.Validate(restored);
            var resumed = new RealtimeCampaignBattle(new BaseCampaignSession(restored.Campaign.ActiveBattle));
            var legacyBattle = ReadLegacyRoster(state);
            var legacyOpening = new BaseCampaignSession(legacyBattle.Campaign.ActiveBattle).Opening;
            var legacyAway = legacyBattle.Workers.Where(w => w.PartyId == legacyOpening.PartyId).ToArray();
            var legacyNumbers = legacyOpening.Troops.Select(troop => troop.SoldierNumbers.Single()).ToArray();
            Need(legacyNumbers.Distinct().Count() == deployed && legacyOpening.Troops.All(troop =>
                legacyAway.Any(worker => worker.SourceId == troop.SourceId && worker.SoldierNumbers.Contains(troop.SoldierNumbers[0]))),
                "旧战中存档的参战人员未对回真实出征来源");
            Need(resumed.Model.Player.Position.Equals(battle.Model.Player.Position) &&
                resumed.Session.Opening.SupplySnapshot.FullConsumptionGrade == 1 &&
                resumed.Session.Opening.SupplySnapshot.Equipment.Single(entry => entry.Level == 1).Count == deployed, "开战检查点丢失军需或落点");
            BaseDemoDefense.Configure(battle);
            Win(state, battle, () => BaseDemoDefense.Complete(state, battle));
            Need(Stock(state, 1).Count == 24 - deployed && Stock(state, 2).Count == 24 && state.Supply.Quotas.Count == 0 &&
                state.Campaign.Reports.Last().GearLost == deployed && state.Supply.Wear.Select(entry => entry.Fraction).SequenceEqual(wear),
                "正式胜利未按实际上场全扣或改写旧磨损余数");
            BaseDemoDefense.AcknowledgeBatu(state);

            var second = state.Copy();
            var advance = typeof(BaseEconomy).GetMethod("CompleteProductionDay", BindingFlags.Static | BindingFlags.NonPublic);
            while (second.Day < BaseDemoDefense.SecondArrival(second)) advance.Invoke(null, new object[] { second, false });
            second.Campaign.Location = "perimeter";
            At(second, "demo_defense_start");
            var secondDraft = new BaseBattlePreparation(second, BasePrepRoute.Second) { SupplyGrade = 2 };
            secondDraft.AutoPlace();
            int secondStock = Stock(second, 2).Count;
            var wave = Start(second, secondDraft);
            BaseDemoDefense.Configure(wave);
            Need(!wave.RetreatAll(), "第二波可撤退");
            wave.OutcomeResolver = _ => BattleOutcome.SideBWin;
            wave.Model.SetPaused(false);
            wave.Tick(.02);
            Need(BaseDemoCombat.Fail(second, wave) && Stock(second, 2).Count == secondStock - secondDraft.RequiredSupply(true), "正式败北军需未全扣");

            state.Campaign.Location = "base";
            At(state, "demo_march_exit");
            BaseDemoFinale.OpenWorld(state);
            var travel = new BaseBattlePreparation(state, BasePrepRoute.Travel);
            var teamIds = travel.Units.Where(unit => unit.Kind == SoldierKind.Hero && unit.Selected).Select(unit => unit.TeamId).ToArray();
            var pair = travel.Units.Where(unit => unit.SoldierNumber > 0 && unit.Selected)
                .GroupBy(unit => unit.Kind).First(group => group.Count() >= 2).Take(2).ToArray();
            pair[0].TeamId = teamIds[1];
            pair[1].TeamId = teamIds[0];
            var departingTeams = travel.Units.Where(unit => unit.SoldierNumber > 0 && unit.Selected)
                .ToDictionary(unit => unit.SoldierNumber, unit => unit.TeamId);
            Need(BaseMuster.Confirm(state, travel.TravelPlan(state)), "黑鸦寨行军编队失败");
            At(state, "demo_march_exit");
            BaseWorldJourney.EnterWorld(state, "camp_entry");
            BaseWorldJourney.Start(state, new[] { "camp", "bandit" });
            Need(BaseWorldJourney.Step(state, _ => null), "行军失败");
            BaseWorldJourney.EnterScene(state, "bandit_entry");
            At(state, "demo_camp_attack");
            var legacyTravel = ReadLegacyRoster(state);
            var legacyDraft = new BaseBattlePreparation(legacyTravel, BasePrepRoute.BlackCrow);
            var legacyParty = legacyTravel.Muster.Parties.Single(p => p.Purpose == "expedition");
            var expectedCounts = legacyParty.CommanderTroops;
            Need(expectedCounts.All(team => BattleSimulation.TroopKinds.All(kind =>
                legacyDraft.Units.Count(unit => unit.Selected && unit.Kind == kind && unit.TeamId == team.LeaderId) ==
                team.Actual.Count(BaseCampaign.ToBase(kind)))), "旧行军存档没有按原有兵种人数恢复出征分队");
            state = JsonUtility.FromJson<BaseState>(JsonUtility.ToJson(state));
            BaseEconomy.Validate(state);
            var attack = new BaseBattlePreparation(state, BasePrepRoute.BlackCrow);
            var arrived = attack.Units.Where(unit => unit.Selected && unit.SoldierNumber > 0).ToArray();
            Need(arrived.Length == departingTeams.Count && arrived.All(unit =>
                departingTeams.TryGetValue(unit.SoldierNumber, out var leader) && leader == unit.TeamId), "行军抵达读档后士兵编号或队长归属变化");
            var party = state.Muster.Parties.Single(p => p.Purpose == "expedition");
            var reserve = state.Workers.Where(w => w.PartyId == party.Id).Sum(w => w.Count);
            Need(reserve > 0, "攻营夹具没有随行士兵");
            VerifySubsetWound(state.Copy(), party.Id);
            Need(attack.Place(attack.PlayerId, attack.Board.Positions().First(p => attack.CanPlace(attack.PlayerId, p))), "无法仅部署主角");
            Need(attack.RequiredSupply(true) == 0 && attack.SupplyProblem(state, true) == "", "只有队长上场时被要求配军需");
            var encounter = Start(state, attack);
            Need(encounter.Session.Opening.Troops.Count == 0 && state.Workers.Where(w => w.PartyId == party.Id).Sum(w => w.Count) == reserve,
                "未参战随行士兵被锁进战场");
            BaseDemoFinale.Configure(encounter);
            Win(state, encounter, () => BaseDemoFinale.Complete(state, encounter));
            Need(state.Workers.Where(w => w.PartyId == party.Id).Sum(w => w.Count) == reserve && state.Campaign.Reports.Last().GearLost == 0,
                "仅队长参战时误扣军需或改动了场外随行士兵");
            Debug.Log("[VerifyBattlePreparation] 首防、第二波、行军、黑鸦寨、军需与存读 PASS");
        }

        private static void VerifySubsetWound(BaseState state, string partyId)
        {
            var source = state.Workers.Where(w => w.PartyId == partyId && w.Health >= w.MaxHealth * BaseDevelopment.DutyThreshold)
                .OrderByDescending(w => w.Count).First();
            int chosen = source.SoldierNumbers.Last();
            var originalHealth = state.Workers.SelectMany(w => w.SoldierNumbers.Select(n => (Number: n, w.Health))).ToDictionary(p => p.Number, p => p.Health);
            var draft = new BaseBattlePreparation(state, BasePrepRoute.BlackCrow) { SupplyGrade = 1 };
            foreach (var soldier in draft.Units.Where(u => u.Kind != SoldierKind.Hero))
            {
                soldier.Selected = soldier.SoldierNumber == chosen;
                soldier.TeamId = draft.PlayerId;
            }
            draft.AutoPlace();
            var battle = Start(state, draft);
            Need(battle.Session.Opening.Troops.Single().SoldierNumbers.Single() == chosen, "攻营快照未带走点中编号");
            var restored = JsonUtility.FromJson<BaseState>(JsonUtility.ToJson(state));
            BaseEconomy.Validate(restored);
            Need(restored.Campaign.ActiveBattle.Troops.Single().SoldierNumbers.Single() == chosen, "战斗读档丢失实际参战编号");
            var resumed = new RealtimeCampaignBattle(new BaseCampaignSession(restored.Campaign.ActiveBattle));
            var named = resumed.Session.UnitForTroop(0);
            Need(named.SoldierNumber == chosen && resumed.Battle.UnitName(named).Contains("#" + chosen), "战场显示的士兵编号与名册不同");
            BaseDemoFinale.Configure(resumed);
            var actor = resumed.Model.Units.Single(u => u.Side == PrototypeSide.Ally && u.Role != PrototypeRole.Leader);
            actor.Health = 7;
            Win(restored, resumed, () => BaseDemoFinale.Complete(restored, resumed));
            var after = BasePersonnel.Soldiers(restored).ToDictionary(u => u.SoldierNumber);
            Need(after.Count == originalHealth.Count && originalHealth.All(p => after[p.Key].Health == (p.Key == chosen ? 7 : p.Value)),
                "攻营战损写到未上场士兵或改变了编号");
        }

        private static void Win(BaseState state, RealtimeCampaignBattle battle, Func<bool> complete)
        {
            foreach (var enemy in battle.Model.Units.Where(u => u.Side == PrototypeSide.Enemy)) enemy.Escaped = true;
            battle.Model.SetPaused(false);
            battle.Tick(.02);
            Need(complete(), "战果结算");
            ClearLoot(state);
        }
    }
}

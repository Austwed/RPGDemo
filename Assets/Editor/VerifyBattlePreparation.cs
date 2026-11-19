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
            VerifyRuins();
            VerifyDefenseAndAttack();
            Debug.Log("[VerifyBattlePreparation] PASS");
        }

        private static void VerifyNamedWorker()
        {
            var state = Load("defense");
            var worker = state.Workers.First(w => !w.Away && w.FacilityId == "farm");
            var chosen = worker.Copy();
            chosen.Count = 1;
            chosen.Health = chosen.MaxHealth * .75;
            state.Workers.Add(chosen);
            int unassigned = state.Workers.Where(w => w.Kind == worker.Kind && w.Health == chosen.Health && w.FacilityId == "").Sum(w => w.Count);
            var move = typeof(BaseEconomy).GetMethod("MoveWorkers", BindingFlags.NonPublic | BindingFlags.Static, null,
                new[] { typeof(BaseState), typeof(BaseTroopKind), typeof(string), typeof(string), typeof(int), typeof(int?) }, null);
            Need((bool)move.Invoke(null, new object[] { state, worker.Kind, "farm", "", 1, (int?)(state.Workers.Count - 1) }), "点名调离失败");
            Need(state.Workers.Where(w => w.Kind == worker.Kind && w.Health == chosen.Health && w.FacilityId == "").Sum(w => w.Count) == unassigned + 1,
                "点名没有移动所选人员");
            Debug.Log("[VerifyBattlePreparation] 逐人点名调配 PASS");
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
            Need(battle.Battle.Phase == BattlePhase.Fighting && battle.Model.Player != null, "草案未开战");
            Need(battle.Model.Units.Count(u => u.Side == PrototypeSide.Ally) == draft.Positions.Count, "未落位人员进了战场");
            Need((int)battle.Model.Player.Position.X == cell.X && (int)battle.Model.Player.Position.Z == cell.Z, "未使用主角落点");
            foreach (var rider in battle.Model.Units.Where(u => u.Side == PrototypeSide.Ally && u.FootprintLength == 2))
                Need(battle.Model.Footprint(rider, rider.Cell, rider.Facing).All(n =>
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
            Need(draft.SupplyGrade == 0 && draft.SupplyProblem(state, false) != "" && draft.SupplyProblem(state, true) != "", "未选军需等级可出战士兵");
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
            Need(BaseMuster.Confirm(state, travel.TravelPlan(state)), "黑鸦寨行军编队失败");
            At(state, "demo_march_exit");
            BaseWorldJourney.EnterWorld(state, "camp_entry");
            BaseWorldJourney.Start(state, new[] { "camp", "bandit" });
            Need(BaseWorldJourney.Step(state, _ => null), "行军失败");
            BaseWorldJourney.EnterScene(state, "bandit_entry");
            At(state, "demo_camp_attack");
            var attack = new BaseBattlePreparation(state, BasePrepRoute.BlackCrow);
            var party = state.Muster.Parties.Single(p => p.Purpose == "expedition");
            var reserve = state.Workers.Where(w => w.PartyId == party.Id).Sum(w => w.Count);
            Need(reserve > 0, "攻营夹具没有随行士兵");
            Need(attack.Place(attack.PlayerId, attack.Board.Positions().First(p => attack.CanPlace(attack.PlayerId, p))), "无法仅部署主角");
            Need(attack.SupplyGrade == 0 && attack.RequiredSupply(true) == 0 && attack.SupplyProblem(state, true) == "", "只有队长上场时被要求配军需");
            var encounter = Start(state, attack);
            Need(encounter.Session.Opening.Troops.Count == 0 && state.Workers.Where(w => w.PartyId == party.Id).Sum(w => w.Count) == reserve,
                "未参战随行士兵被锁进战场");
            BaseDemoFinale.Configure(encounter);
            Win(state, encounter, () => BaseDemoFinale.Complete(state, encounter));
            Need(state.Workers.Where(w => w.PartyId == party.Id).Sum(w => w.Count) == reserve && state.Campaign.Reports.Last().GearLost == 0,
                "仅队长参战时误扣军需或改动了场外随行士兵");
            Debug.Log("[VerifyBattlePreparation] 首防、第二波、行军、黑鸦寨、军需与存读 PASS");
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

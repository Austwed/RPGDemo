using System;
using System.Linq;
using BaseConquest.Base;
using BaseConquest.Battle;
using BaseConquest.RealtimePrototype;
using UnityEngine;

namespace BaseConquest.Editor
{
    public static class VerifyMageOperations
    {
        public static void Run()
        {
            var start = Stage("camp");
            Need(start.Day == 1 && BaseTown.TroopKinds(start).Count == 4 &&
                start.Town.Recruits.All(r => r.Remaining == (r.Kind == BaseTroopKind.Mage ? 0 : 2)), "首日教学兵源");

            var state = Stage("defense");
            AtScene(state, "town", "town_recruit");
            Need(state.Town.Recruits.Count == 3 && BaseTown.MaxHire(state, BaseTroopKind.Mage) == 0, "雷恩只出售剑弓骑");
            int food = BaseEconomy.FoodRequired(state);
            int before = BaseTown.TroopCount(state, BaseTroopKind.Mage);
            VerifySmithReward(state.Copy());
            var prisoner = HeatCapture(state.Copy());
            state.Aftermath.Prisoners.Add(prisoner);
            int lastNumber = state.LastSoldierNumber;
            Need(BaseAftermath.Decide(state, prisoner.Id, "recruit") && !BaseAftermath.Decide(state, prisoner.Id, "recruit"), "遗迹俘虏单次招募");
            var worker = state.Workers.Single(w => w.Kind == BaseTroopKind.Mage && w.SoldierNumbers.Any(n => n > lastNumber));
            int number = worker.SoldierNumbers.Single(n => n > lastNumber);
            Need(BaseTown.TroopCount(state, BaseTroopKind.Mage) == before + 1 &&
                worker.CombatStats.Health == prisoner.Stats.Health && BaseEconomy.FoodRequired(state) - food == state.Demo.Rpg.MageFood, "归编与口粮");
            // Advance from convalescence to the healthy fixture used by the work and deployment cases.
            worker.Health = worker.MaxHealth;
            worker.Resting = false;
            AtScene(state, "base", "home");
            var facility = worker.FacilityId == "workshop" ? "farm" : "workshop";
            var facilityRule = state.Muster.Facilities.Single(f => f.FacilityId == facility);
            if (BaseEconomy.WorkersAt(state, facility) >= facilityRule.Capacity)
            {
                var previous = state.Workers.First(w => !w.Away && !w.VillageResident && w.FacilityId == facility);
                Need(BaseEconomy.MoveWorker(state, previous.SoldierNumbers[0], facility, ""), "腾出测试岗位");
            }
            Need(BaseEconomy.MoveWorker(state, number, worker.FacilityId, facility) &&
                BasePersonnel.Soldiers(state, SoldierKind.Mage).Any(u => u.SoldierNumber == number && u.Effective &&
                    u.Facility == BaseEconomy.FacilityName(facility)), "实名岗位调配与名册");
            AtScene(state, "perimeter", "demo_defense_start");
            VerifyCasualties(state.Copy(), number, false);
            VerifyCasualties(state.Copy(), number, true);
            var draft = MageDraft(state, number);
            Need(draft.Units.Single(u => u.SoldierNumber == number).Kind == SoldierKind.Mage, "备战名册丢失法师");
            var supplies = state.Inventory.Equipment.Single(g => g.Product == "supply" && g.Level == 1);
            int supplyBefore = supplies.Count;
            Need(draft.AutoPlace() > 0 && draft.RequiredSupply(true) == 1 && draft.Problem(state, true) == "", "法师部署或军需校验");
            var session = draft.Begin(state);
            var battle = new RealtimeCampaignBattle(session);
            var actor = session.Battle.Soldiers.Single(u => u.SoldierNumber == number && u.Side == BattleSide.A);
            var quota = session.Opening.SupplySnapshot;
            Need(actor.Kind == SoldierKind.Mage && session.Opening.Scenario.A.MageCount == 1 && session.Opening.Scenario.A.CavalryCount == 0 &&
                session.Opening.PlayerTroopCounts[3] == before + 1 && quota.Mage == 1 && quota.Demand("supply") == 1 &&
                BaseSupply.Available(state, "supply", 1) == supplyBefore - 1, "职业映射、军需锁定或兵力快照");
            var restored = JsonUtility.FromJson<BaseState>(JsonUtility.ToJson(state));
            BaseEconomy.Validate(restored);
            Need(restored.Campaign.ActiveBattle.Troops.Single().SoldierNumbers.Single() == number, "战中读档未保留法师编号");

            BaseDemoDefense.Configure(battle);
            // 固定敌方撤离，让正常胜利回调结算，排除随机战损干扰归队编号检查。
            foreach (var enemy in battle.Model.Units.Where(u => u.Side == PrototypeSide.Enemy)) enemy.Escaped = true;
            battle.Model.SetPaused(false);
            battle.Tick(.02);
            Need(BaseDemoDefense.Complete(state, battle), "首防结算");
            Need(state.Supply.Quotas.Count == 0 && state.Inventory.Equipment.Single(g => g.Product == "supply" && g.Level == 1).Count == supplyBefore - 1 &&
                state.Workers.Any(w => w.Kind == BaseTroopKind.Mage && !w.Away && w.SoldierNumbers.Contains(number)), "军需实扣或法师归队");
            BaseDemoDefense.AcknowledgeBatu(state);
            AtScene(state, "base", "home");
            var preview = BaseEconomy.Preview(state);
            Need(BaseEconomy.Rest(state, state.Day) && state.LastReport.FoodRequired == preview.FoodRequired, "返营日结口粮");
            BaseEconomy.Validate(state);
            Debug.Log("[VerifyMageOperations] PASS 首日兵源、正常招募、备战部署、军需、即死、倒地、俘虏招降、读档、归队和口粮日结");
        }

        private static void VerifySmithReward(BaseState state)
        {
            AtScene(state, "town", "town_blacksmith");
            string stone = BaseEquipmentCrafting.Stone;
            int before = BaseBackpack.Count(state, stone);
            Need(BaseDemoSideQuests.AcceptSmith(state) && BaseBackpack.Count(state, stone) == before + 3, "铁匠的试用石");
            BaseBattleLoot.ConsumeReceipt(state, BaseDemoSideQuests.SmithStonesId);
            BaseDemoSideQuests.Reforged(state);
            var packed = state.Copy();
            BaseBackpack.Receive(packed, stone, BaseBackpack.Room(packed, stone) - 99);
            int packedCount = BaseBackpack.Count(packed, stone);
            for (int attempt = 0; attempt < 2; attempt++)
            {
                try { BaseDemoSideQuests.FinishSmith(packed); throw new InvalidOperationException("不足100个空间仍领取成功"); }
                catch (BagFullException) { }
                Need(BaseDemoSideQuests.Smith(packed) == BaseSideQuestStage.Ready && BaseBackpack.Count(packed, stone) == packedCount,
                    "满包重试必须保留任务和库存");
            }
            Need(BaseDemoSideQuests.FinishSmith(state) && !BaseDemoSideQuests.FinishSmith(state) &&
                BaseBackpack.Count(state,stone) == before + 103 && state.Backpack.Items.Where(i=>i.ItemId==stone).All(i=>i.Count<=99),
                "重铸奖励整组领取、堆叠与幂等");
        }

        private static BasePrisoner HeatCapture(BaseState state)
        {
            var map = BaseSpatial.Layout(state,"outpost");
            state.Campaign.Location = map.Region;
            state.Campaign.CurrentMapId = map.SourceMapId;
            var approach = BaseStageWalk.Door(state,"demo_ruins_investigate");
            state.PlayerX = approach.X; state.PlayerY = approach.Y;
            BaseAdventure.Arrive(state);
            var session = BaseDemoRuins.Begin(state, "none");
            var run = new RealtimeCampaignBattle(session);
            BaseDemoRuins.Configure(run);
            var mages = run.Battle.Soldiers.Where(u=>u.Side==BattleSide.B && u.Kind==SoldierKind.Mage).ToArray();
            Need(!session.Opening.RuinsFirstClear && mages.Length==2 && !BaseDemoRuins.Civilians(session).Any(), "重刷阵容");
            var target = mages[0];
            int seed = Enumerable.Range(1,10000).First(n => BattleSimulation.StableRoll(n,target.Id,11)>.95 &&
                BattleSimulation.StableRoll(n,target.Id,23)<session.Opening.Difficulty.DropRate);
            run.Battle.Scenario.RandomSeed = session.Opening.Scenario.RandomSeed = seed;
            run.Model.CanTarget = (_,_) => false;
            run.Model.CanMove = _ => false;
            run.Model.SetPaused(false);
            for (int i=0; i<260 && mages.Any(u=>u.IsActive); i++) run.Tick(.2);
            Need(target.KnockedDown && mages.All(u=>!u.IsActive) && target.LastHitSourceId == -1, "热脉击倒法师 t=" + run.Model.Elapsed + " phase=" + run.Battle.Phase + " HP=" + string.Join(",",mages.Select(m=>$"{m.Health}/{m.Stats.Health}:down={m.KnockedDown}:dead={m.InstantDead}:last={m.LastHitSourceId}")));
            var controller = run.Battle.Commander(BattleSide.B,0);
            var body = run.Model.Units.Single(u=>u.Id==controller.Id+1);
            run.Model.ApplyResolvedHit(run.Model.Player, body, body.Health);
            run.Tick(.05);
            VerifyChenanAndRuins.WalkOutOfRuins(run);
            Need(BaseDemoRuins.Complete(state,run), "重刷遗迹完成");
            var captive = state.Aftermath.Prisoners.Single(p => p.Id == session.Opening.Id + ":" + target.Id && p.Status == "held").Copy();
            var save = new BaseSaveStore("Logs/demo-route-checkpoints/heat-captive-slot.json",
                d => JsonUtility.ToJson(d), JsonUtility.FromJson<BaseSaveDocument>, true);
            save.Save(state, "热脉击倒法师 · 等待处置");
            var reload = save.Load();
            int before = BaseTown.TroopCount(reload, BaseTroopKind.Mage);
            Need(BaseAftermath.Decide(reload, captive.Id, "recruit") && BaseTown.TroopCount(reload, BaseTroopKind.Mage) == before + 1,
                "读回实际古窟战后存档并招募法师");
            return captive;
        }

        private static BaseBattlePreparation MageDraft(BaseState state, int number)
        {
            var draft = new BaseBattlePreparation(state, BasePrepRoute.Defense) { SupplyGrade = 1 };
            foreach (var row in draft.Units.Where(u => u.Kind != SoldierKind.Hero))
            {
                row.Selected = row.SoldierNumber == number;
                if (row.Selected) row.TeamId = draft.PlayerId;
            }
            return draft;
        }

        private static void VerifyCasualties(BaseState state, int number, bool lethal)
        {
            // 在原有布阵格上替换一名敌兵，覆盖敌方法师的生成、俘获和劝降。
            var preset = state.Demo.Encounters.Defense;
            var enemyRow = preset.Units.Single(u => u.Id == "melee-" + preset.Melee);
            preset.Melee--;
            enemyRow.Id = "mage-" + (++preset.Mage);
            enemyRow.Kind = SoldierKind.Mage;
            preset.Validate();
            var draft = MageDraft(state, number);
            int supply = state.Inventory.Equipment.Single(e => e.Product == "supply" && e.Level == 1).Count;
            Need(draft.AutoPlace() > 0 && draft.Problem(state, true) == "", "战损测试布阵");
            var session = draft.Begin(state);
            var run = new RealtimeCampaignBattle(session);
            BaseDemoDefense.Configure(run);
            var ally = session.Battle.Soldiers.Single(u => u.Side == BattleSide.A && u.Kind == SoldierKind.Mage);
            var enemy = session.Battle.Soldiers.First(u => u.Side == BattleSide.B && u.Kind == SoldierKind.Mage);
            var allyActor = run.Model.Units.Single(u => u.Id == ally.Id + 1);
            var enemyActor = run.Model.Units.Single(u => u.Id == enemy.Id + 1);
            double probability = BaseDemoCombat.DeathChance(session, ally);
            Need(probability >= 0 && probability <= 1, "法师未参与原有即死率计算");
            // 选定产生两类自然结果的种子；即死率、俘获率和命中回调均沿用真实战斗规则。
            int seed = Enumerable.Range(1, 200000).First(n =>
                (lethal ? BattleSimulation.StableRoll(n, ally.Id, 11) < .05 : BattleSimulation.StableRoll(n, ally.Id, 11) > .95) &&
                BattleSimulation.StableRoll(n, enemy.Id, 11) > .95 &&
                BattleSimulation.StableRoll(n, enemy.Id, 23) < session.Opening.Difficulty.DropRate);
            run.Battle.Scenario.RandomSeed = session.Opening.Scenario.RandomSeed = seed;
            run.Model.ApplyResolvedHit(enemyActor, allyActor, allyActor.Health);
            run.Model.ApplyResolvedHit(run.Model.Player, enemyActor, enemyActor.Health);
            Need(ally.DeathResolved && ally.InstantDead == lethal && ally.KnockedDown != lethal && enemy.KnockedDown, "实时命中后法师死伤状态");
            foreach (var other in run.Model.Units.Where(u => u.Side == PrototypeSide.Enemy && u != enemyActor)) other.Escaped = true;
            run.Model.SetPaused(false);
            run.Tick(.02);
            Need(BaseDemoDefense.Complete(state, run), "战损分支未结算");
            var report = state.Campaign.Reports.Last();
            var returned = BasePersonnel.Soldiers(state, SoldierKind.Mage).SingleOrDefault(u => u.SoldierNumber == number);
            Need(report.Deployed == 1 && report.Killed == (lethal ? 1 : 0) && report.Returned == (lethal ? 0 : 1) &&
                (lethal ? returned == null : returned != null && returned.Health == 1 && !returned.Away), "死亡移除、倒地保留或战报人数");
            Need(report.GearLost == 1 && state.Supply.Quotas.Count == 0 &&
                state.Inventory.Equipment.Single(e => e.Product == "supply" && e.Level == 1).Count == supply - 1, "军需实扣与战损不一致");
            var prisoner = state.Aftermath.Prisoners.Single(p => p.BattleId == session.Opening.Id && p.Kind == SoldierKind.Mage && p.Status == "held");
            int previous = state.LastSoldierNumber;
            Need(BaseAftermath.Decide(state, prisoner.Id, "recruit") && !BaseAftermath.Decide(state, prisoner.Id, "recruit"), "普通法师俘虏未能单次归编");
            Need(BasePersonnel.Soldiers(state, SoldierKind.Mage).Any(u => u.SoldierNumber > previous && u.Recruited && u.Health == 1), "俘虏未作为受伤法师进入名册");
            BaseEconomy.Validate(state);
        }

        private static BaseState Stage(string id)
        {
            var asset = Resources.Load<TextAsset>("DemoStages/" + id + "-male");
            Need(asset != null, "缺少阶段档 " + id);
            var state = JsonUtility.FromJson<BaseState>(asset.text);
            BaseEconomy.Validate(state);
            Need(state.Demo.Rpg.HasProfessionCatalog, "阶段档尚未按职业配置重新生成");
            return state;
        }

        private static void AtScene(BaseState state, string region, string interaction)
        {
            var layout = BaseSpatial.Layout(state, region);
            state.Campaign.Location = region;
            if (BaseSceneInstances.Active(state)) state.Campaign.CurrentMapId = layout.SourceMapId;
            var item = layout.Objects.Single(o => o.Id == interaction);
            var point = item.Blocks ? BaseStageWalk.Door(state, interaction) : new BasePoint(item.X, item.Y);
            state.PlayerX = point.X;
            state.PlayerY = point.Y;
            BaseAdventure.Arrive(state);
        }

        private static void Need(bool value, string message)
        {
            if (!value) throw new InvalidOperationException(message);
        }
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BaseConquest.Base;
using BaseConquest.Battle;
using UnityEditor;
using UnityEngine;

namespace BaseConquest.Editor
{
    public static class VerifyBanditHideout
    {
        private const string DifficultyKey = "BaseConquest.GlobalDifficulty";
        private const string Output = "Logs/bandit-hideout";
        private const int CalibrationSamples = 32;
        private const int AcceptanceSamples = 200;

        public static void RunAndExit()
        {
            bool hadDifficulty = PlayerPrefs.HasKey(DifficultyKey);
            string prior = PlayerPrefs.GetString(DifficultyKey, "normal");
            int exit = 0;
            try
            {
                PlayerPrefs.SetString(DifficultyKey, "normal");
                string phase = Environment.GetEnvironmentVariable("BASE_HIDEOUT_CHECK") ?? "smoke";
                var fixture = CreateFixture();
                Directory.CreateDirectory(Output);
                if (phase == "smoke")
                {
                    VerifyRoute(fixture);
                    Debug.Log("[VerifyBanditHideout] PASS route, roster, old save, cancellation and checkpoint");
                }
                else if (phase == "calibrate")
                {
                    int baseline = Math.Min(BaseBanditHideout.EnemyCountCap, fixture.Demo.Rpg.TroopBaseline.Sum());
                    bool accepted = false;
                    for (int count = baseline; count >= 1; count--)
                    {
                        if (Batch(fixture, count, 31701, CalibrationSamples, "calibration") * 5 < CalibrationSamples * 3) continue;
                        int trial = Batch(fixture, count, 91101 + (baseline - count) * 1000, AcceptanceSamples, "acceptance");
                        if (trial * 5 < AcceptanceSamples * 3) continue;
                        File.WriteAllText(Path.Combine(Output, "selected.txt"), $"{count}\n{trial}\n{AcceptanceSamples}\n");
                        Debug.Log($"[VerifyBanditHideout] PASS calibrated count {count}, acceptance {trial}/{AcceptanceSamples}");
                        accepted = true;
                        break;
                    }
                    Need(accepted, "没有通过胜率验收的候选人数");
                }
                else if (phase == "configured")
                {
                    int count = DemoBalanceRuntime.Load().Rules.HideoutEnemyCount;
                    Need(count > 0 && new BaseBattlePreparation(fixture, BasePrepRoute.Hideout, 91101).KnownEnemyCount() == count,
                        "运行快照与旧档读取的窝点兵力不同");
                    string workbook = "../策划/DEMO/DEMO数值配置表.xlsx";
                    if (File.Exists(workbook))
                        Need(DemoBalanceTableReader.ReadVerifiedTables(workbook).HideoutEnemyCount == count, "正式配表与运行配置的窝点兵力不同");
                    VerifyRoute(fixture);
                    VerifyVictory(fixture);
                    VerifyBattlePreparation.Run();
                    VerifyDemoCheckpoint.VerifyCampHandoff();
                    Debug.Log($"[VerifyBanditHideout] PASS configured enemy count {count}");
                }
                else throw new ArgumentException("未知检查阶段 " + phase);
            }
            catch (Exception error) { exit = 2; Debug.LogException(error); }
            finally
            {
                if (hadDifficulty) PlayerPrefs.SetString(DifficultyKey, prior);
                else PlayerPrefs.DeleteKey(DifficultyKey);
            }
            EditorApplication.Exit(exit);
        }

        internal static void AddSoldiers(BaseState state, int melee, int ranged, int cavalry)
        {
            int[] counts = { melee, ranged, cavalry };
            for (int index = 0; index < counts.Length; index++)
            {
                if (counts[index] == 0) continue;
                var stats = state.Demo.Rpg.Bare(new RpgCharacter
                {
                    TemplateId = RpgRules.Template("soldier", index), Profession = index
                }, 1);
                state.Workers.Add(new BaseWorkerBucket
                {
                    Kind = (BaseTroopKind)index, Count = counts[index], Health = stats.Health,
                    MaxHealth = stats.Health, HasCombatProfile = true, CombatStats = stats
                });
            }
            var stock = state.Inventory.Equipment.Single(item => item.Product == "supply" && item.Level == 1);
            stock.Count = Math.Max(stock.Count, counts.Sum());
        }

        private static BaseState CreateFixture()
        {
            var state = JsonUtility.FromJson<BaseState>(Resources.Load<TextAsset>("DemoStages/camp-male").text);
            BaseEconomy.Validate(state);
            At(state, "home");
            Need(BaseDemo.AcknowledgeWaking(state), "定标档无法完成醒来");
            At(state, "exit_east");
            Need(BaseCampaign.Travel(state, "town"), "定标档无法前往灰岩镇");
            At(state, "demo_ruins_exit");
            Need(BaseCampaign.Travel(state, "outpost"), "定标档无法前往遗迹入口");
            BaseBanditHideout.Install(state);
            At(state, BaseBanditHideout.EntryId);
            AddSoldiers(state, 4, 3, 3);
            BaseEconomy.Validate(state);
            Need(state.Adventure.Members.Count == 1 && state.Workers.Sum(worker => worker.Count) == 10, "定标档不是一队长十兵");
            var player = state.Leaders.Single(leader => leader.Id == state.PlayerLeaderId);
            Need(player.Health == player.MaxHealth, "定标档主角不是满血");
            return state;
        }

        private static void At(BaseState state, string id)
        {
            var target = BaseSpatial.Objects(state).Single(node => node.Id == id);
            var point = target.Blocks ? BaseStageWalk.Door(state, id) : new BasePoint(target.X, target.Y);
            state.PlayerX = point.X;
            state.PlayerY = point.Y;
            BaseAdventure.Arrive(state);
        }

        private static BaseBattlePreparation Draft(BaseState state, int seed, int? count = null)
        {
            var draft = new BaseBattlePreparation(state, BasePrepRoute.Hideout, seed, count) { SupplyGrade = 1 };
            foreach (var unit in draft.Units)
            {
                unit.Selected = true;
                unit.TeamId = draft.PlayerId;
            }
            Need(draft.RequiredSupply(false) == 10 && draft.Problem(state, false) == "", "定标编队或一级军需不符合条件");
            Need(draft.AutoPlace() == 11 && draft.Problem(state, true) == "", "实际一键布置未能放下全部队伍");
            return draft;
        }

        private static RealtimeCampaignBattle Start(BaseState state, BaseBattlePreparation draft)
        {
            var session = draft.Begin(state);
            var opening = session.Opening;
            Need(opening.EventId == BaseBanditHideout.EventId && opening.RuinsExposureFraction == 0 &&
                opening.SupplySnapshot.FullConsumptionGrade == 1 && opening.SupplySnapshot.Demand("supply") == 10,
                "窝点开局的事件、侵蚀或军需不符");
            Need(opening.Scenario.A.Commanders.All(commander => commander.Strategy == SquadStrategy.Attack), "窝点没有默认自动接敌");
            Need(opening.Enemies.Count == draft.KnownEnemyCount() && opening.Enemies.All(unit => unit.Kind != SoldierKind.Hero), "敌军总数与预览不一致或含队长");
            foreach (var kind in BattleSimulation.TroopKinds)
                Need(opening.Enemies.Count(unit => unit.Kind == kind) == draft.KnownEnemyCount(kind), "敌军职业与预览不一致");
            var battle = new RealtimeCampaignBattle(session);
            Need(battle.Battle.Phase == BattlePhase.Fighting && battle.RuinsExposureFraction == 0, "正式战斗启动状态非法");
            battle.Model.SetPaused(false);
            return battle;
        }

        private static int Batch(BaseState fixture, int count, int firstSeed, int samples, string split)
        {
            int wins = 0;
            var rows = new List<string>(samples + 1) { "seed,melee,ranged,cavalry,outcome,seconds" };
            int workers = Math.Min(8, Environment.ProcessorCount);
            for (int start = 0; start < samples; start += workers)
            {
                int length = Math.Min(workers, samples - start);
                var runs = new RealtimeCampaignBattle[length];
                var compositions = new string[length];
                var outcomes = new BattleOutcome[length];
                for (int index = 0; index < length; index++)
                {
                    var state = fixture.Copy();
                    var draft = Draft(state, firstSeed + start + index, count);
                    runs[index] = Start(state, draft);
                    compositions[index] = string.Join(",", BattleSimulation.TroopKinds.Select(draft.KnownEnemyCount));
                }
                // Only the pure simulation runs in parallel. Fixture loading, setup and reporting stay on Unity's main thread.
                Parallel.For(0, length, index => outcomes[index] = AutoFight(runs[index]));
                for (int index = 0; index < length; index++)
                {
                    if (outcomes[index] == BattleOutcome.SideAWin) wins++;
                    rows.Add($"{firstSeed + start + index},{compositions[index]},{outcomes[index]},{runs[index].Model.Elapsed.ToString("0.0", CultureInfo.InvariantCulture)}");
                }
                File.WriteAllLines(Path.Combine(Output, $"{split}-{count}.csv"), rows);
                Debug.Log($"[VerifyBanditHideout] {split} {count}: {start + length}/{samples}, wins {wins}");
                if (split == "calibration" && (wins + samples - start - length) * 5 < samples * 3) return wins;
            }
            Debug.Log($"[VerifyBanditHideout] {split} {count}: {wins}/{samples} wins");
            return wins;
        }

        private static BattleOutcome AutoFight(RealtimeCampaignBattle run)
        {
            for (int step = 0; step < (run.Battle.Scenario.MaxRounds + 1) * 10 && run.Battle.Phase == BattlePhase.Fighting; step++)
                run.Tick(.1);
            return run.Battle.Report?.Outcome ?? BattleOutcome.RoundLimit;
        }

        private static void VerifyRoute(BaseState fixture)
        {
            var state = fixture.Copy();
            BaseBanditHideout.Install(state);
            Need(BaseSpatial.Objects(state).Count(node => node.Id == BaseBanditHideout.EntryId) == 1, "补挂入口不幂等");
            var first = Draft(state, 31701);
            Need(first.KnownEnemyPower(state) > 0 && !state.Campaign.HasActiveBattle && state.Supply.Quotas.Count == 0, "查看或布置消耗了资源");
            var different = Enumerable.Range(31702, 12).Select(seed => new BaseBattlePreparation(state, BasePrepRoute.Hideout, seed)).Any(d =>
                BattleSimulation.TroopKinds.Any(kind => d.KnownEnemyCount(kind) != first.KnownEnemyCount(kind)));
            Need(different, "多次重新交互没有抽取不同阵容");
            var run = Start(state, first);
            run.SaveCheckpoint(state);
            var roundtrip = JsonUtility.FromJson<BaseState>(JsonUtility.ToJson(state));
            BaseEconomy.Validate(roundtrip);
            var restored = new RealtimeCampaignBattle(new BaseCampaignSession(roundtrip.Campaign.ActiveBattle));
            Need(restored.Battle.Phase == BattlePhase.Fighting && restored.Session.Opening.Enemies.Count == first.KnownEnemyCount() &&
                restored.Battle.Soldiers.All(unit => unit.Side != BattleSide.B || unit.Kind != SoldierKind.Hero), "存档丢失窝点敌军快照");
            state = roundtrip;
            Need(restored.RetreatAll() && BaseBanditHideout.Complete(state, restored), "窝点退败无法结算");
            Need(BaseSceneRules.Entrance(state) && !state.Campaign.HasActiveBattle && state.Supply.Quotas.Count == 0 &&
                state.Demo.Stage == fixture.Demo.Stage && BaseRuinsRewards.Energy(state) == BaseRuinsRewards.Energy(fixture) &&
                state.Muster.Strategies.SequenceEqual(fixture.Muster.Strategies),
                "窝点退败没有返回入口或改动古窟剧情与体力");
            Need(!BaseBanditHideout.Complete(state, restored), "窝点战果可以重复结算");
        }

        private static void VerifyVictory(BaseState fixture)
        {
            for (int seed = 71101; seed < 71121; seed++)
            {
                var state = fixture.Copy();
                var run = Start(state, Draft(state, seed));
                if (AutoFight(run) != BattleOutcome.SideAWin) continue;
                string id = run.Session.Opening.Id;
                Need(BaseBanditHideout.Complete(state, run) && BaseSceneRules.Entrance(state) && !state.Campaign.HasActiveBattle,
                    "窝点胜利无法结算并回到入口");
                Need(state.Campaign.Reports.Count(report => report.BattleId == id) == 1 && BaseBattleLoot.Pending(state) &&
                    state.Aftermath.Loot.Count(loot => loot.BattleId == id) == 1 && !BaseBanditHideout.Complete(state, run) &&
                    state.Demo.Stage == fixture.Demo.Stage && BaseRuinsRewards.Energy(state) == BaseRuinsRewards.Energy(fixture),
                    "窝点胜利战果重复或附带了古窟剧情与体力领奖");
                Debug.Log($"[VerifyBanditHideout] PASS victory settlement, seed {seed}");
                return;
            }
            Need(false, "胜利结算复验未得到胜局");
        }

        private static void Need(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}

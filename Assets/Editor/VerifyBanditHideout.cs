using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
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
                    VerifyVictory(fixture);
                    Debug.Log("[VerifyBanditHideout] PASS route, entrance migration, checkpoint, survivor recruitment, rewards and sweep");
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
            var state = VerifyEntranceMigration(fixture);
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

        private static BaseState VerifyEntranceMigration(BaseState fixture)
        {
            var legacy = fixture.Copy();
            var oldEntrance = BaseSpatial.Layout(legacy).Objects.Single(item => item.Id == BaseBanditHideout.EntryId);
            oldEntrance.X = 12.5;
            oldEntrance.Y = 4.5;
            var gaps = new[] { 8, 9, 16, 17 }.Select(row => new GridPosition(15, row)).ToArray();
            foreach (var gap in gaps) BaseBattlefield.Board(legacy).Cell(gap).Terrain = TerrainKind.Ground;
            var state = JsonUtility.FromJson<BaseState>(JsonUtility.ToJson(legacy));
            var map = state.Campaign.Layouts.Single(item => item.SourceMapId == BaseBanditHideout.MapId);
            var mission = state.Campaign.Events.Single(item => item.Id == BaseBanditHideout.EventId);
            BaseEconomy.Validate(state);
            var entrance = BaseSpatial.Objects(state).Single(item => item.Id == BaseBanditHideout.EntryId);
            var board = BaseBattlefield.Board(state);
            Need(entrance.X == 18.5 && entrance.Y == 12.5 &&
                Enumerable.Range(4, 16).All(row => board.Cell(new GridPosition(15, row)).Terrain == TerrainKind.Wall) &&
                state.Campaign.Layouts.Single(item => item.SourceMapId == BaseBanditHideout.MapId) == map &&
                state.Campaign.Events.Single(item => item.Id == BaseBanditHideout.EventId) == mission,
                "旧档未将同一入口迁到断壁后，或副本地图与事件被重建");
            // Already-relocated saves also need the wall patch and a valid position if saved in the gap.
            board.Cell(gaps[0]).Terrain = TerrainKind.Ground;
            state.PlayerX = 15.5; state.PlayerY = 8.5;
            BaseEconomy.Validate(state);
            Need(board.Cell(gaps[0]).Terrain == TerrainKind.Wall && (int)state.PlayerX != 15,
                "已迁移窝点的旧档没有封墙或将主角移出旧缺口");
            var west = new GridPosition(14, 12);
            var east = new GridPosition(16, 12);
            var target = new GridPosition((int)entrance.X, (int)entrance.Y);
            var route = BaseStageWalk.Route(state, new BasePoint(14.5, 12.5), new BasePoint(entrance.X, entrance.Y));
            Need(route.Count > 0 && route.Any(point => point.Y < 4 || point.Y >= 20), "封墙后无法从端部绕行至窝点");
            var vision = new BattleVision(BaseSceneCombat.Project(state));
            vision.RefreshExploration(new[] { west });
            Need(!vision.TerrainVisible(BattleSide.A, target), "首次绕墙前浓雾中已显示窝点");
            vision.RefreshExploration(new[] { east });
            Need(vision.Visible(BattleSide.A, target), "绕到墙后未显示窝点");
            var restored = new BattleVision(BaseSceneCombat.Project(state));
            restored.RememberTerrain(BattleSide.A, vision.KnownTerrain(BattleSide.A));
            restored.RefreshExploration(new[] { west });
            Need(!restored.Visible(BattleSide.A, target) && restored.TerrainVisible(BattleSide.A, target), "已发现窝点没有在薄雾中保留");
            VerifyExplorationCache(state.Copy(), target);
            At(state, BaseBanditHideout.EntryId);
            return state;
        }

        private static void VerifyExplorationCache(BaseState state, GridPosition target)
        {
            var host = new GameObject("ExplorationSightCheck");
            try
            {
                var controller = host.AddComponent<BaseSceneController>();
                var companions = new BaseCompanionMovement();
                var type = typeof(BaseSceneController);
                type.GetProperty(nameof(controller.State)).GetSetMethod(true).Invoke(controller, new object[] { state });
                type.GetProperty(nameof(controller.Companions)).GetSetMethod(true).Invoke(controller, new object[] { companions });
                var method = type.GetMethod("RefreshExplorationSight", BindingFlags.Instance | BindingFlags.NonPublic);
                var memory = BaseSceneRules.Memory(state);
                Array.Clear(memory.Known, 0, memory.Known.Length);
                var objects = BaseSpatial.Objects(state);
                BattleVision Refresh() => (BattleVision)method.Invoke(controller, new object[] { objects, memory });

                state.PlayerX = 14.5;
                state.PlayerY = 12.5;
                var west = Refresh();
                int revision = west.Revision;
                var sameMemory = (bool[])memory.Known.Clone();
                memory.Known = sameMemory;
                state.PlayerX = 14.7;
                Need(Refresh() == west && west.Revision == revision && memory.Known == sameMemory && !west.TerrainVisible(BattleSide.A, target),
                    "原地／格内移动或复制存档记忆时重复计算迷雾，或浓雾提前露出窝点");

                var follower = companions.Agents.First();
                state.Adventure.Members.Add(follower.Id);
                follower.Position = new BasePoint(16.5, 12.5);
                follower.Visible = true;
                Need(Refresh() == west && west.Revision > revision && west.Visible(BattleSide.A, target), "跟随者跨墙后未刷新实时视野");
                follower.Visible = false;
                Need(Refresh() == west && !west.Visible(BattleSide.A, target) && west.TerrainVisible(BattleSide.A, target), "跟随者退出视野后未恢复薄雾");

                var wall = BaseBattlefield.Board(state).Cell(new GridPosition(15, 12));
                wall.Terrain = TerrainKind.Ground;
                var clear = Refresh();
                Need(clear != west && clear.Visible(BattleSide.A, target), "地形改变后仍使用旧视线");
                objects = objects.Append(new BaseMapObject("sight-test-blocker", "验收遮挡", 15.5, 12.5, 1, 1)).ToArray();
                var blocked = Refresh();
                Need(blocked != clear && !blocked.Visible(BattleSide.A, target) && blocked.TerrainVisible(BattleSide.A, target), "新增障碍未遮住实时视野");

                BaseSpatial.Layout(state).Safety = "safe";
                var safe = Refresh();
                revision = safe.Revision;
                state.PlayerX = 10.5;
                Need(safe.Visible(BattleSide.A, target) && Refresh() == safe && safe.Revision == revision, "安全区未全显示或移动时重复计算迷雾");
                Debug.Log("[VerifyBanditHideout] PASS exploration sight cache, observer movement, map blockers and safe-area fog");
            }
            finally { UnityEngine.Object.DestroyImmediate(host); }
        }

        private static void VerifyVictory(BaseState fixture)
        {
            bool won = false;
            for (int seed = 71101; seed < 71121; seed++)
            {
                var state = fixture.Copy();
                var rules = state.Demo.RuinsRewards;
                int coins = state.Inventory.Coins, food = state.Inventory.Food, materials = state.Inventory.Materials;
                rules.Energy = 0;
                Need(!BaseRuinsRewards.Cleared(state, BasePrepRoute.Hideout) &&
                    BaseRuinsSweep.Problem(state, BasePrepRoute.Hideout) != "", "窝点未首通便可以扫荡");
                var run = Start(state, Draft(state, seed));
                if (AutoFight(run) != BattleOutcome.SideAWin) continue;
                string id = run.Session.Opening.Id;
                Need(BaseBanditHideout.Complete(state, run) && BaseSceneRules.Entrance(state) && !state.Campaign.HasActiveBattle,
                    "窝点胜利无法结算并回到入口");
                Need(state.Campaign.Reports.Count(report => report.BattleId == id) == 1 && BaseBattleLoot.Pending(state) &&
                    state.Aftermath.Loot.Count(loot => loot.BattleId == id) == 1 && !BaseBanditHideout.Complete(state, run) &&
                    state.Demo.Stage == fixture.Demo.Stage,
                    "窝点胜利战果重复或触发了古窟剧情");
                var loot = state.Aftermath.Loot.Single(item => item.BattleId == id);
                Need(BaseRuinsRewards.Cleared(state, BasePrepRoute.Hideout) &&
                    !BaseRuinsRewards.Cleared(state, BasePrepRoute.Ruins) &&
                    BaseRuinsRewards.PendingBattle(state, BasePrepRoute.Hideout) == "" &&
                    BaseRuinsRewards.Energy(state) == 0 && state.Campaign.SettledIds.Contains(id + ":ruins-reward") &&
                    state.Inventory.Coins == coins + rules.Coins + rules.FirstCoins &&
                    state.Inventory.Food == food + rules.FirstFood && state.Inventory.Materials == materials + rules.FirstMaterials &&
                    loot.Items.Any(line => line.Reward.Template.StartsWith("novice_")) &&
                    loot.Items.Any(line => line.Reward.ItemId == "forging_stone" && line.Reward.Count == rules.ItemCount),
                    "窝点首通未免费结算同配表奖励，或串到了古窟");
                won = true;
                var survivors = state.Aftermath.Prisoners.Where(person => person.BattleId == id && person.Status == "held").ToArray();
                if (survivors.Length == 0) continue;
                Need(survivors.All(person => person.Kind != SoldierKind.Hero), "窝点的幸存者中出现了队长");
                int beforeCount = state.Workers.Sum(worker => worker.Count), beforeCoins = state.Inventory.Coins;
                Need(BaseBattleLoot.RecruitAll(state, id) == survivors.Length &&
                    survivors.All(person => person.Status == "recruited") &&
                    state.Workers.Sum(worker => worker.Count) == beforeCount + survivors.Length &&
                    state.Inventory.Coins == beforeCoins && BaseBattleLoot.RecruitAll(state, id) == 0,
                    "窝点幸存者未通过正常战果免费招募，或可被重复招募");
                Need(loot.Items.All(item=>item.Status=="taken")&&BaseBattleLoot.ConsumeReceipt(state,id).HasReceipt&&loot.Closed,
                    "窝点首通奖励未自动到账或无法展示奖励回执");
                VerifySweepAndReward(state);
                Debug.Log($"[VerifyBanditHideout] PASS victory settlement and recruited {survivors.Length} survivors, seed {seed}");
                return;
            }
            Need(won, "胜利结算复验未得到胜局");
            Need(false, "固定种子胜局未留下可招募幸存者");
        }

        private static void VerifySweepAndReward(BaseState state)
        {
            At(state, BaseBanditHideout.EntryId);
            AddSoldiers(state, 4, 3, 3);
            var player = state.Leaders.Single(leader => leader.Id == state.PlayerLeaderId);
            player.Health = player.MaxHealth;
            player.UnavailableDay = 0;
            const string caveReward = "check-cave-pending";
            state.Campaign.SettledIds.Add(caveReward);
            state.Campaign.Reports.Add(new BaseCampaignReport { BattleId = caveReward, EventId = BaseDemoRuins.EventId,
                Outcome = "SideAWin", Day = state.Day });
            state.Demo.RuinsRewards.PendingBattleId = caveReward;
            BaseEconomy.Validate(state);

            var draft = new BaseBattlePreparation(state, BasePrepRoute.Hideout, 73101, 1) { SupplyGrade = 1 };
            var soldiers = draft.Units.Where(unit => unit.Kind != SoldierKind.Hero).ToArray();
            var selected = soldiers.OrderByDescending(unit => unit.Health).Take(10).ToHashSet();
            foreach (var soldier in soldiers)
            {
                soldier.Selected = selected.Contains(soldier);
                soldier.TeamId = soldier.Selected ? draft.PlayerId : null;
            }
            Need(draft.RequiredSupply(false) == 10 && draft.Problem(state, false) == "", "扫荡验收必须恰好选中十名可配给的士兵");
            string before = JsonUtility.ToJson(state);
            var preview = draft.PreviewSweep(state);
            Need(preview.Problem == "" && preview.EnemyPower > 0 && preview.AllyPower / preview.EnemyPower > 1.5 &&
                preview.Exposure == 0 && JsonUtility.ToJson(state) == before, "窝点扫荡预览异常、串入侵蚀或修改了存档：" + preview.Problem);
            Need(BaseAutoResolve.Problem(state, BaseBanditHideout.EventId,
                new BaseEngagement { AllyPower = 15, EnemyPower = 10 }, BaseAutoResolve.SweepAdvantage) != "",
                "窝点扫荡放行了恰好 1.5 倍战力");

            var result = draft.Sweep(state);
            string id = BaseRuinsRewards.PendingBattle(state, BasePrepRoute.Hideout);
            var report = state.Campaign.Reports.Single(entry => entry.BattleId == id);
            Need(result.BattleId == id && report.EventId == BaseBanditHideout.EventId && report.Passive &&
                result.EnemyPower == preview.EnemyPower && result.AllyPower == preview.AllyPower &&
                BaseSceneRules.Entrance(state) && !state.Campaign.HasActiveBattle && state.Supply.Quotas.Count == 0 &&
                BaseRuinsRewards.PendingBattle(state) == caveReward && BaseRuinsRewards.Energy(state) == 0,
                "扫荡未沿用备战阵容、落地结算或待领状态串到了古窟");
            Need(!BaseRuinsRewards.Claim(state, BasePrepRoute.Hideout) && BaseRuinsRewards.PendingBattle(state, BasePrepRoute.Hideout) == id,
                "体力不足时扫荡奖励可领取或丢失");
            var copy = JsonUtility.FromJson<BaseState>(JsonUtility.ToJson(state));
            copy.Campaign.Reports.RemoveAll(entry => entry.EventId == BaseBanditHideout.EventId);
            BaseEconomy.Validate(copy);
            Need(BaseRuinsRewards.Cleared(copy, BasePrepRoute.Hideout) &&
                BaseRuinsRewards.PendingBattle(copy, BasePrepRoute.Hideout) == id &&
                BaseRuinsRewards.Skip(copy, BasePrepRoute.Hideout) && BaseRuinsRewards.PendingBattle(copy) == caveReward &&
                BaseRuinsRewards.Energy(copy) == 0, "读档后放弃窝点奖励扣了体力或清除了古窟奖励");

            int coins = state.Inventory.Coins, cost = BaseRuinsRewards.Cost(state);
            Need(BaseRuinsRewards.AddEnergy(state, cost) && BaseRuinsRewards.Claim(state, BasePrepRoute.Hideout) &&
                !BaseRuinsRewards.Claim(state, BasePrepRoute.Hideout) &&
                state.Inventory.Coins == coins + state.Demo.RuinsRewards.Coins && BaseRuinsRewards.Energy(state) == 0 &&
                BaseRuinsRewards.PendingBattle(state) == caveReward &&
                state.Aftermath.Loot.Single(item => item.BattleId == id).Items.Any(line => line.Reward.ItemId == "forging_stone"),
                "窝点重复领奖未按一次体力扣除并发放通用奖励");
            BaseEconomy.Validate(state);
        }

        private static void Need(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}

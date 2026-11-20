using System;
using System.Globalization;
using System.IO;
using System.Linq;
using BaseConquest.Base;
using BaseConquest.Battle;
using BaseConquest.RealtimePrototype;
using UnityEditor;
using UnityEngine;

namespace BaseConquest.Editor
{
    public static partial class VerifyDemoPlayerRoute
    {
        // Use the same seed list for competing tables. Freeze the release set apart from tuning samples.
        public static void RunRuinsBalance()
        {
            const string difficulty = "BaseConquest.GlobalDifficulty";
            bool saved = PlayerPrefs.HasKey(difficulty);
            string previous = PlayerPrefs.GetString(difficulty);
            try
            {
                PlayerPrefs.SetString(difficulty, "normal");
                int samples = int.Parse(RuinsArg("-ruinsSamples", "400"), CultureInfo.InvariantCulture);
                if (samples <= 0 || samples > 10000) throw new ArgumentOutOfRangeException(nameof(samples));
                bool tuning = Environment.GetCommandLineArgs().Contains("-ruinsTune");
                string holdout = RuinsArg("-ruinsHoldout", "1");
                if (holdout != "1" && holdout != "2") throw new ArgumentException("未知的冻结种子组");
                var random = new System.Random(tuning ? 617931 : holdout == "1" ? 437305 : 6819091);
                int[] seeds = Enumerable.Range(0, samples).Select(_ =>
                {
                    int value = random.Next();
                    return !tuning && random.Next(2) == 1 ? ~value : value;
                }).ToArray();
                Need(seeds.Distinct().Count() == seeds.Length, "遗迹试种不唯一");
                var openings = new[] { RuinsBalanceOpening("male"), RuinsBalanceOpening("female") };
                VerifyOpeningSeeds(openings[0], RuinsBalanceOpening("male", true));
                string path = Path.GetFullPath(RuinsArg("-ruinsCsv", "Logs/ruins-balance.csv"));
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                int playerFirst = 0;
                using (var csv = new StreamWriter(path, false, new System.Text.UTF8Encoding(false)))
                {
                    csv.WriteLine("seed,hero,first,first_frame,hero_half_frame,hero_half_seconds,galan_half_frame,galan_half_seconds,end_frame,end_reason,hero_hp,hero_max,galan_hp,galan_max,hero_attack,galan_attack,hero_defense,galan_defense,hero_first_interval,galan_first_interval,packets");
                    for (int i = 0; i < samples; i++)
                    {
                        var opening = openings[i % 2].Copy();
                        opening.Scenario.RandomSeed = opening.Checkpoint.Scenario.RandomSeed = seeds[i];
                        var session = new BaseCampaignSession(opening);
                        session.Bind();
                        var run = new RealtimeCampaignBattle(session);
                        BaseDemoRuins.Configure(run);
                        var player = run.Model.Player;
                        var controller = run.Model.Units.Single(u => u.Id == run.Battle.Commander(BattleSide.B, 0).Id + 1);
                        Need(BaseDemoRuins.Civilians(session).Count() == 2 && run.Battle.Soldiers.Count(u => u.Side == BattleSide.A) == 1 &&
                            session.Opening.Difficulty.Preset == "normal" && session.Opening.RuinsFirstClear, "首战阵容或难度错误");
                        Need(run.Strategy(0, SquadStrategy.Attack), "主角无法切换自动进攻");
                        run.Model.SetPaused(false);
                        int frame = 0;
                        int heroFrame = 0, galanFrame = 0;
                        string end = "timeout";
                        while (frame < 5400 && run.Battle.Phase == BattlePhase.Fighting)
                        {
                            run.Tick(RealtimeBattlePrototype.FixedSeconds);
                            frame++;
                            if (heroFrame == 0 && player.Health <= player.MaxHealth * .5) heroFrame = frame;
                            if (galanFrame == 0 && controller.Health <= controller.MaxHealth * .5) galanFrame = frame;
                            if (BaseDemoRuins.SoloRetreatGuide(run)) { end = "story_retreat"; break; }
                            if (run.RuinsExitOpen || run.Battle.Phase != BattlePhase.Fighting) { end = "outcome"; break; }
                            if (heroFrame != 0 && galanFrame != 0) { end = "both"; break; }
                        }
                        int firstFrame = heroFrame == 0 ? galanFrame : galanFrame == 0 ? heroFrame : Math.Min(heroFrame, galanFrame);
                        string first = firstFrame == 0 ? "neither" : heroFrame == galanFrame ? "tie" : heroFrame == firstFrame ? "hero" : "galan";
                        if (first == "hero") playerFirst++;
                        string Half(int tick) => tick == 0 ? "," : FormattableString.Invariant($"{tick},{tick * RealtimeBattlePrototype.FixedSeconds:0.000}");
                        string Interval(int actorId)
                        {
                            var launches = run.RpgAttackHistory.Where(packet => packet.SourceId == actorId && packet.TargetId >= 0).Take(2).ToArray();
                            return launches.Length < 2 ? "" : (launches[1].LaunchedAt - launches[0].LaunchedAt).ToString("0.000", CultureInfo.InvariantCulture);
                        }
                        var heroStats = run.Battle.Soldier(player.Id - 1).Stats;
                        var bossStats = run.Battle.Soldier(controller.Id - 1).Stats;
                        csv.WriteLine(FormattableString.Invariant($"{seeds[i]},{(i % 2 == 0 ? "male" : "female")},{first},{firstFrame},{Half(heroFrame)},{Half(galanFrame)},{frame},{end},{player.Health:0.000},{player.MaxHealth:0.000},{controller.Health:0.000},{controller.MaxHealth:0.000},{player.Damage:0.000},{controller.Damage:0.000},{heroStats.Defense:0.000},{bossStats.Defense:0.000},{Interval(player.Id)},{Interval(controller.Id)},{run.RpgAttackHistory.Count}"));
                        if (i % 8 == 0) csv.Flush();
                    }
                }
                Debug.Log($"[RuinsBalance] 主角先到半血 {playerFirst}/{samples}。逐局数据 {path}");
                if (!tuning) Need(samples == 400 && playerFirst >= 370, "冻结种子集主角先到半血不足370/400");
                Debug.Log("[RuinsBalance] PASS");
            }
            finally
            {
                if (saved) PlayerPrefs.SetString(difficulty, previous);
                else PlayerPrefs.DeleteKey(difficulty);
            }
        }

        // Reopening an uncommitted plan and reloading its checkpoint preserve the pending rolls.
        private static void VerifyOpeningSeeds(BaseCampaignBattle first, BaseCampaignBattle next)
        {
            Need(first.Scenario.RandomSeed == next.Scenario.RandomSeed &&
                first.Scenario.RandomSeed == first.Checkpoint.Scenario.RandomSeed, "重新打开备战或读档后待战种子改变");
            var restored = JsonUtility.FromJson<BaseCampaignBattle>(JsonUtility.ToJson(first));
            var left = new RealtimeCampaignBattle(new BaseCampaignSession(first.Copy()));
            var right = new RealtimeCampaignBattle(new BaseCampaignSession(restored));
            foreach (var battle in new[] { left, right })
            {
                BaseDemoRuins.Configure(battle);
                battle.Strategy(0, SquadStrategy.Attack);
                battle.Model.SetPaused(false);
                for (int tick = 0; tick < 1800 && battle.RpgAttackHistory.Count < 8; tick++)
                    battle.Tick(RealtimeBattlePrototype.FixedSeconds);
            }
            Need(left.RpgAttackHistory.Count >= 8 && left.RpgAttackHistory.Count == right.RpgAttackHistory.Count &&
                left.RpgAttackHistory.Zip(right.RpgAttackHistory, (a, b) =>
                    a.Id == b.Id && a.SourceId == b.SourceId && a.TargetId == b.TargetId &&
                    a.First == b.First && a.Second == b.Second && a.Hit == b.Hit &&
                    a.DamageApplied == b.DamageApplied).All(equal => equal), "读回开战档后攻击掷骰或命中结果不同");
        }

        public static void RunRuinsStoryBalance()
        {
            const string key = "BaseConquest.GlobalDifficulty";
            bool had = PlayerPrefs.HasKey(key);
            string previous = PlayerPrefs.GetString(key);
            try
            {
                PlayerPrefs.SetString(key, "normal");
                var rules = DemoBalanceRuntime.Load().Rules;
                string table = RuinsArg("-ruinsTable", "");
                if (table.Length > 0) rules = JsonUtility.FromJson<DemoBalancePackage>(File.ReadAllText(table)).Rules;
                string raw = File.ReadAllText(Path.Combine(Application.streamingAssetsPath, "BaseScenarios", BaseDemo.BalanceTemplate));
                var state = DemoStageSaveTools.Camp(raw, "male", rules);
                At(state, "home");
                Need(BaseDemo.AcknowledgeWaking(state), "试档从营地醒来");
                Travel(state, "exit_east", "town");
                At(state, "demo_azhu");
                int wins = 0, losses = 0;
                for (int i = 0; i < 16; i++)
                {
                    var duel = new RealtimeCampaignBattle(BaseLocalDuel.Create(state));
                    var rival = duel.Model.Units.Single(unit => unit.Side == PrototypeSide.Enemy);
                    duel.Battle.Scenario.RandomSeed = 18019 + 39439 * i;
                    duel.OutcomeResolver = r => r.Model.Player.Health <= r.Model.Player.MaxHealth * .5
                        ? BattleOutcome.SideBWin : rival.Health <= rival.MaxHealth * .5
                        ? BattleOutcome.SideAWin : BattleOutcome.None;
                    duel.Strategy(0, SquadStrategy.Attack);
                    Tick(duel, 5400);
                    if (duel.Battle.Report?.Outcome == BattleOutcome.SideAWin) wins++;
                    if (duel.Battle.Report?.Outcome == BattleOutcome.SideBWin) losses++;
                    Need(duel.RpgAttackHistory.Any(hit => hit.ImpactApplied), "阿铸切磋没有实际命中");
                }
                Debug.Log($"[RuinsStoryBalance] 阿铸实名切磋，主角胜 {wins} / 负 {losses}");
                Need(wins > 0 && wins + losses == 16, "阿铸切磋没有打出一次真实胜利或出现僵局");
                foreach (int teamSize in new[] { 2, 3 })
                {
                    var party = DemoStageSaveTools.OpenFirstRuins(raw, "male", rules);
                    if (teamSize == 2) party.Adventure.Members.Remove("partner_farm");
                    var draft = new BaseBattlePreparation(party, BasePrepRoute.Ruins);
                    draft.AutoPlace();
                    var run = new RealtimeCampaignBattle(draft.Begin(party));
                    BaseDemoRuins.Configure(run);
                    Need(run.Session.Opening.Leaders.Count == teamSize, "随行队长数量错误");
                    var boss = run.Battle.Commander(BattleSide.B, 0);
                    Need(boss.Rpg.TemplateId == "captain_awakened_mage", "首次迦兰模板错误");
                    ApproachController(run, true, 5400);
                    Need(BaseDemoRuins.Complete(party, run), $"{teamSize}人队未打通救援");
                    Need(BaseDemoStories.AcceptController(party, party.Demo.Flow.FirstControllerId), "无法在洞口招募迦兰");
                    var recruit = party.Leaders.Single(l => l.Id == "partner_recruit:" + party.Demo.Flow.FirstControllerId);
                    Need(recruit.Rpg.TemplateId == boss.Rpg.TemplateId && recruit.Rpg.SkillId == "inspire", "招募前后模板或技能偏离");
                    Debug.Log($"[RuinsStoryBalance] {teamSize}名队长实战救援、撤离、招募迦兰 PASS");
                }
                if (table.Length == 0) VerifyMageCombat.Run();
                Debug.Log("[RuinsStoryBalance] PASS");
            }
            finally
            {
                if (had) PlayerPrefs.SetString(key, previous);
                else PlayerPrefs.DeleteKey(key);
            }
        }

        internal static void VerifyRuinsSweepSeed(BaseState state)
        {
            VerifyBanditHideout.AddSoldiers(state, 4, 3, 3);
            BaseEconomy.Validate(state);
            BaseBattlePreparation Open(BaseState source)
            {
                var draft = new BaseBattlePreparation(source, BasePrepRoute.Ruins);
                foreach (var kind in BattleSimulation.TroopKinds)
                    draft.SetTeamCount(draft.PlayerId, kind, 10);
                return draft;
            }
            string original = JsonUtility.ToJson(state);
            int seed = BaseCampaign.NextBattleSeed(state, BaseDemoRuins.EventId);
            var first = Open(state).PreviewSweep(state);
            var loaded = JsonUtility.FromJson<BaseState>(original);
            var second = Open(loaded).PreviewSweep(loaded);
            Need(first.Problem == "" && second.Problem == "" && JsonUtility.ToJson(state) == original &&
                BaseCampaign.NextBattleSeed(loaded, BaseDemoRuins.EventId) == seed, "古窟读档或循环预览异常：" + first.Problem);
            var resolved = Open(state).Sweep(state);
            bool Same(BaseSweepPreview a, BaseSweepPreview b) => a.EnemyPower == b.EnemyPower &&
                a.AllyPower == b.AllyPower && a.Seconds == b.Seconds && a.Losses.Count == b.Losses.Count &&
                a.Losses.Zip(b.Losses, (x,y) => x.UnitId == y.UnitId && x.Dead == y.Dead &&
                    x.Before == y.Before && x.After == y.After).All(equal => equal);
            Need(Same(first, second) && Same(second, resolved) &&
                BaseCampaign.NextBattleSeed(state, BaseDemoRuins.EventId) != seed, "古窟预览、读档、实际扫荡或下一场不同步");
            Debug.Log("[RuinsStoryBalance] 古窟循环预览、读档和正式结算逐人伤损一致；下一场种子已更新");
        }

        private static string RuinsArg(string key, string fallback)
        {
            string[] args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, key);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : fallback;
        }

        public static void RunBattleSeedSmoke()
        {
            RuinsBalanceOpening("male", true, true);
            Debug.Log("[BattleSeed] PASS old save migration, repeated settlement, retained 256 reports and reload");
        }

        private static BaseCampaignBattle RuinsBalanceOpening(string gender, bool verifyRetreat = false, bool verifyHistory = false)
        {
            var state = JsonUtility.FromJson<BaseState>(Resources.Load<TextAsset>("DemoStages/camp-" + gender).text);
            BaseEconomy.Validate(state);
            string table = RuinsArg("-ruinsTable", "");
            if (!string.IsNullOrEmpty(table))
            {
                var package = JsonUtility.FromJson<DemoBalancePackage>(File.ReadAllText(table));
                package.Validate();
                var rules = state.Demo.Rpg;
                rules.Templates = package.Rules.Templates.Select(t => t.Copy()).ToList();
                rules.Range = package.Rules.Range.ToArray();
                rules.Speed = package.Rules.Speed.ToArray();
                rules.Hit = package.Rules.Hit.ToArray();
                rules.Evasion = package.Rules.Evasion.ToArray();
                foreach (var leader in state.Leaders.Concat(state.Demo.Candidates))
                {
                    leader.Rpg.Rules = rules;
                    leader.PersonalEquipment.BaseStats = rules.Bare(leader.Rpg, leader.Rpg.Level);
                    leader.Health = leader.MaxHealth = BaseRpg.Stats(leader).Health;
                }
            }
            var hero = state.Leaders.Single(l => l.Id == state.PlayerLeaderId);
            Need(hero.Rpg.Level == 1 && hero.Health == hero.MaxHealth && hero.PersonalEquipment.Items.Count == 4 &&
                hero.PersonalEquipment.Items.Any(item => item.Template == "captains_ring"), "需要满血初装主角");
            At(state, "home");
            Need(BaseDemo.AcknowledgeWaking(state), "首日营地尚未醒来");
            Travel(state, "exit_east", "town");
            Travel(state, "demo_ruins_exit", "outpost");
            At(state, "demo_ruins_investigate");
            if (verifyHistory)
            {
                for (int i = 0; i < 256; i++)
                {
                    string id = "seed-history:" + i;
                    state.Campaign.SettledIds.Add(id);
                    state.Campaign.Reports.Add(new BaseCampaignReport { BattleId = id, EventId = BaseDemoRuins.EventId,
                        Day = state.Day, Outcome = "Retreat", Title = "古窟撤退" });
                }
                BaseEconomy.Validate(state);
                Need(state.Campaign.SettledBattleCount == 256, "旧存档历史计数未迁移");
            }
            var draft = new BaseBattlePreparation(state, BasePrepRoute.Ruins);
            draft.AutoPlace();
            var session = draft.Begin(state);
            var actors = session.Battle.Soldiers.OrderBy(unit => unit.Id).Select(unit =>
                FormattableString.Invariant($"{unit.Side}/{unit.Kind}[{unit.Rpg?.TemplateId}]:HP={unit.Health:0.##},攻击={unit.Stats.Damage:0.##},防御={unit.Stats.Defense:0.##}"));
            Debug.Log($"[RuinsBalance] {gender} 起始阵容 {string.Join(" / ", actors)}；配表 {state.Demo.ConfigHash}");
            Need(state.Campaign.ActiveBattle.HasCheckpoint && state.Campaign.ActiveBattle.HasRealtimeState,
                "无法复现正式首通的部署落点");
            var opening = state.Campaign.ActiveBattle.Copy();
            if (verifyRetreat)
            {
                var live = new RealtimeCampaignBattle(session);
                BaseDemoRuins.Configure(live);
                Need(BaseDemoCombat.Fail(state, live), "首战撤退无法提交");
                if (verifyHistory)
                {
                    int committed = BaseCampaign.NextBattleSeed(state, BaseDemoRuins.EventId);
                    Need(state.Campaign.SettledBattleCount == 257 && !BaseCampaign.Settle(state, session),
                        "重复提交同场战果推进了场次");
                    state.Campaign.Reports.RemoveRange(0, state.Campaign.Reports.Count - 256);
                    state = JsonUtility.FromJson<BaseState>(JsonUtility.ToJson(state));
                    Need(state.Campaign.SettledBattleCount == 257 &&
                        BaseCampaign.NextBattleSeed(state.Copy(), BaseDemoRuins.EventId) == committed,
                        "战报裁剪或读档改变了待战种子");
                }
                BaseSceneRules.LeaveDungeon(state);
                At(state, "demo_ruins_investigate");
                var next = new BaseBattlePreparation(state, BasePrepRoute.Ruins);
                next.AutoPlace();
                var retry = next.Begin(state);
                Need(retry.Opening.Scenario.RandomSeed != opening.Scenario.RandomSeed, "真实撤退后仍复用了上一场种子");
            }
            return opening;
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using BaseConquest.Base;
using BaseConquest.Battle;
using BaseConquest.RealtimePrototype;

namespace BaseConquest.Editor
{
    // 战力标定台：不进玩家流程、不改玩法，只在编辑器里批量跑确定性战斗模拟，
    // 产出「阵容 → 真实胜负／战损／耗时」的真值数据集，用于回归战力公式系数。
    // 每场对局都跑一次镜像局（交换双方），用于分离「侧别系统偏差」与真实战力差。
    public static class PowerCalibration
    {
        private const string BalancePath = "Assets/Resources/DemoBalance/DemoBalance.json";
        private static readonly string OutDir = Path.Combine("Logs", "power-calibration");
        private const double Step = .25;
        // 模拟是确定性的，单一随机种子会把某个种子的偶然性当成规律；每个对局跑多个种子取平均。
        private static readonly int[] Seeds = { 12345, 23456, 34567 };
        private static RpgRules rules;
        private static string only;
        private static bool diag;
        // 队长拴绳半径（CommanderLoadout.AttackRange，默认9）。调大用于验证「骑兵够不到敌人」是否由它造成。
        private static int lead = 9;
        private static readonly List<string> Rows = new List<string>();

        // 单位级诊断：逐帧采样，回答「这个单位到底有没有在打、有没有在走」。
        private sealed class UnitDiag
        {
            public int Frames, MoveFrames, IdleFrames, WindupFrames, NoTargetFrames, NoGoalFrames, Attacks, Landed, GoalHereFrames, StuckFrames;
            public double Distance = 0, MinEnemyGap = double.PositiveInfinity, Damage = 0;
        }

        private sealed class Side
        {
            public int Melee, Ranged, Cavalry, Heroes = 1, Level = 1, Profession;
            public string Skill = "", Trait = "";
        }
        private sealed class Match
        {
            public string Name;
            public Side A = new Side(), B = new Side();
        }

        [MenuItem("基地征服/战力标定/跑一批对局")]
        public static void Run()
        {
            Directory.CreateDirectory(OutDir);
            Debug.Log("[PowerCalibration] 开始");
            try
            {
                rules = LoadRules();
                only = Environment.GetCommandLineArgs().FirstOrDefault(a => a.StartsWith("--only="))?.Substring("--only=".Length) ?? "";
                diag = Environment.GetCommandLineArgs().Any(a => a == "--diag");
                lead = int.TryParse(Environment.GetCommandLineArgs().FirstOrDefault(a => a.StartsWith("--lead="))?.Substring("--lead=".Length), out int parsed) ? parsed : 9;
                if (lead != 9) Debug.Log("[PowerCalibration] 队长拴绳半径改为 " + lead + "（默认9）");
                if (only != "") Debug.Log("[PowerCalibration] 只跑前缀 " + only);
                var header = "match,swap,seed,outcome,seconds,aAlive,bAlive,aHp,bHp,aEscaped,bEscaped,oldA,oldB"
                    + ",aM,aR,aC,aH,aLvl,aProf,aSkill,aTrait,bM,bR,bC,bH,bLvl,bProf,bSkill,bTrait"
                    + ",newWin,newA,newB,newSec";
                Rows.Add(header);
                int total = 0, done = 0, failed = 0;
                foreach (var match in Matches().Where(m => only == "" || m.Name.StartsWith(only)))
                {
                    total += 2 * Seeds.Length;
                    foreach (int swap in new[] { 0, 1 })
                        foreach (int seed in Seeds)
                        {
                            try { Simulate(match, swap == 1, seed); done++; }
                            catch (Exception e) { failed++; Debug.Log("[PowerCalibration] 对局失败 " + match.Name + " swap" + swap + " seed" + seed + "：" + e.Message); }
                        }
                }
                File.WriteAllText(Path.Combine(OutDir, "raw.csv"), string.Join("\n", Rows), Encoding.UTF8);
                Debug.Log("[PowerCalibration] 完成 " + done + "/" + total + " 场（失败 " + failed + "），输出 " + Path.Combine(OutDir, "raw.csv"));
            }
            catch (Exception e) { Debug.LogError("[PowerCalibration] 失败：" + e); }
            EditorApplication.Exit(0);
        }

        private static RpgRules LoadRules()
        {
            string path = Path.Combine(Environment.CurrentDirectory, BalancePath);
            if (!File.Exists(path)) throw new FileNotFoundException("配表快照不存在：" + path);
            var package = JsonUtility.FromJson<DemoBalancePackage>(File.ReadAllText(path));
            var r = package?.Rules ?? throw new InvalidOperationException("配表快照缺RPG规则");
            r.Validate();
            return r;
        }

        // 第一批：兵种配比 × 规模（射程／移速／集火效应的主要证据来源）
        private static IEnumerable<Match> Matches()
        {
            var aShapes = new[]
            {
                new[] { 1, 0, 0 }, new[] { 0, 1, 0 }, new[] { 0, 0, 1 },
                new[] { 1, 1, 0 }, new[] { 1, 0, 1 }, new[] { 0, 1, 1 }, new[] { 1, 1, 1 }
            };
            var bShapes = new[] { new[] { 1, 0, 0 }, new[] { 0, 1, 0 }, new[] { 1, 1, 0 }, new[] { 0, 0, 1 } };
            foreach (int size in new[] { 4, 8 })
                foreach (var a in aShapes)
                    foreach (var b in bShapes)
                        yield return new Match
                        {
                            Name = "shape" + size + "_A" + string.Join("", a) + "_B" + string.Join("", b),
                            A = new Side { Melee = a[0] * size, Ranged = a[1] * size, Cavalry = a[2] * size },
                            B = new Side { Melee = b[0] * size, Ranged = b[1] * size, Cavalry = b[2] * size }
                        };
            // 第二批：等级差（双方同兵种，只有等级不同）
            foreach (int level in new[] { 1, 3, 5, 7, 9 })
                yield return new Match
                {
                    Name = "level_A5_B" + level,
                    A = new Side { Melee = 6, Level = 5 }, B = new Side { Melee = 6, Level = level }
                };
            // 第三批：队长技能。同职业（战士）对照，乙方固定用职业默认的横扫，
            // 否则双方默认技能相同，测不出差异（连射／突进属于远程／骑兵，无法在同职业内对照）。
            foreach (string skill in new[] { "sweep", "inspire", "heal" })
                yield return new Match
                {
                    Name = "skill_" + skill,
                    A = new Side { Melee = 4, Ranged = 2, Skill = skill },
                    B = new Side { Melee = 4, Ranged = 2, Skill = "sweep" }
                };
            // 第四批：特性（对称阵容，只有 A 有特性）
            foreach (string trait in new[] { "frenzy", "tough", "fragile", "advantage" })
                yield return new Match
                {
                    Name = "trait_" + trait,
                    A = new Side { Melee = 4, Ranged = 2, Trait = trait }, B = new Side { Melee = 4, Ranged = 2 }
                };
        }

        private static void Simulate(Match m, bool swap, int seed)
        {
            var first = swap ? m.B : m.A;
            var second = swap ? m.A : m.B;
            var scenario = BuildScenario(first, second, seed);
            var opening = new BaseCampaignBattle
            {
                Id = "calib:" + m.Name, EventId = "calib", Scenario = scenario, Rpg = rules,
                RealtimeRules = true, RealtimeRulesVersion = 1, Leaders = Leaders(first),
                Troops = Troops(first), Difficulty = new BaseDifficulty { DropRate = 0 }
            };
            var session = new BaseCampaignSession(opening);
            session.Battle.AutoDeploy(BattleSide.A);
            BaseRpg.BindBattle(session);
            BindEnemyProfiles(session, second);
            if (!session.Battle.Start(out string problem)) throw new InvalidOperationException("开战失败：" + problem);
            // 开战瞬间（全员满血）用新公式推算一次，与真值对照，用于验证 C# 实现。
            var forecast = BasePower.Engage(
                session.Battle.Soldiers.Where(u => u.Side == BattleSide.A).ToList(),
                session.Battle.Soldiers.Where(u => u.Side == BattleSide.B).ToList(), rules);
            var run = new RealtimeCampaignBattle(session);
            run.Model.SetPaused(false);
            int cap = (scenario.MaxRounds + 1) * 4;
            var diagMap = diag ? new Dictionary<int, UnitDiag>() : null;
            for (int frame = 0; frame < cap && session.Battle.Phase != BattlePhase.Finished; frame++)
            {
                run.Tick(Step);
                if (diagMap != null) Sample(run, diagMap);
            }
            if (diagMap != null) DumpDiag(m, swap, session, run, diagMap);

            double aFull = 0, aNow = 0, bFull = 0, bNow = 0;
            int aAlive = 0, bAlive = 0, aEscaped = 0, bEscaped = 0;
            double oldA = 0, oldB = 0;
            foreach (var u in session.Battle.Soldiers)
            {
                double power = BaseRpg.Power(u, rules);
                if (u.Side == BattleSide.A)
                {
                    aFull += u.Stats.Health; aNow += Math.Max(0, u.Health); oldA += power;
                    if (u.IsAlive) { aAlive++; if (u.Escaped) aEscaped++; }
                }
                else
                {
                    bFull += u.Stats.Health; bNow += Math.Max(0, u.Health); oldB += power;
                    if (u.IsAlive) { bAlive++; if (u.Escaped) bEscaped++; }
                }
            }
            string outcome = session.Battle.Phase != BattlePhase.Finished ? "Timeout"
                : session.Battle.Report?.Outcome.ToString() ?? "?";
            if (only != "" && !swap && seed == Seeds[0])
                foreach (var u in session.Battle.Soldiers)
                    Debug.Log("[PowerCalibration]   明细 " + u.Side + " " + u.Kind + "/" + u.CombatKind
                        + " 血" + u.Stats.Health.ToString("0") + " 攻" + u.Stats.Damage.ToString("0")
                        + " 防" + u.Stats.Defense.ToString("0") + " 级" + u.Stats.Level
                        + " 模板" + (u.Rpg?.TemplateId ?? "无") + " 战力" + BaseRpg.Power(u, rules).ToString("0.00"));
            Rows.Add(string.Join(",", new[]
            {
                m.Name, swap ? "1" : "0", seed.ToString(), outcome, run.Model.Elapsed.ToString("0.00"),
                aAlive.ToString(), bAlive.ToString(),
                (aNow / Math.Max(1e-9, aFull)).ToString("0.000"), (bNow / Math.Max(1e-9, bFull)).ToString("0.000"),
                aEscaped.ToString(), bEscaped.ToString(), oldA.ToString("0.000"), oldB.ToString("0.000"),
                first.Melee.ToString(), first.Ranged.ToString(), first.Cavalry.ToString(), first.Heroes.ToString(), first.Level.ToString(), first.Profession.ToString(), first.Skill, first.Trait,
                second.Melee.ToString(), second.Ranged.ToString(), second.Cavalry.ToString(), second.Heroes.ToString(), second.Level.ToString(), second.Profession.ToString(), second.Skill, second.Trait,
                (forecast.AllyWins ? "SideAWin" : "SideBWin"), forecast.AllySurvival.ToString("0.000"), forecast.EnemySurvival.ToString("0.000"), forecast.Seconds.ToString("0.00")
            }));
            Debug.Log("[PowerCalibration] " + m.Name + (swap ? " 镜像" : " 正序") + " → " + outcome
                + " 旧战力 " + oldA.ToString("0.00") + " vs " + oldB.ToString("0.00")
                + " 血量比 " + (aNow / Math.Max(1e-9, aFull)).ToString("0.00") + " / " + (bNow / Math.Max(1e-9, bFull)).ToString("0.00"));
        }

        private static void Sample(RealtimeCampaignBattle run, Dictionary<int, UnitDiag> map)
        {
            foreach (var u in run.Model.Units)
            {
                if (!map.TryGetValue(u.Id, out var d)) map[u.Id] = d = new UnitDiag();
                if (!u.Alive) continue;
                d.Frames++;
                if (u.Moving) d.MoveFrames++; else d.IdleFrames++;
                if (u.HitAt >= 0 && !u.HitResolved) d.WindupFrames++;
                if (u.TargetId < 0) d.NoTargetFrames++;
                if (!u.Moving && u.MoveGoal < 0) d.NoGoalFrames++;
                // 想去别处却没动＝被堵；目标格就是脚下＝自锁（攻击位判定与实际攻击判定不一致的典型症状）
                if (!u.Moving && u.MoveGoal >= 0) { if (u.MoveGoal == u.Cell) d.GoalHereFrames++; else d.StuckFrames++; }
                foreach (var e in run.Model.Units)
                    if (e.Alive && e.Side != u.Side)
                        d.MinEnemyGap = Math.Min(d.MinEnemyGap, RealtimeBattlePrototype.BodyGap(u, e));
                d.Distance = u.DistanceTravelled;
            }
        }

        private static void DumpDiag(Match m, bool swap, BaseCampaignSession session, RealtimeCampaignBattle run, Dictionary<int, UnitDiag> map)
        {
            foreach (var packet in run.RpgAttackHistory)
            {
                if (!map.TryGetValue(packet.SourceId, out var d)) continue;
                d.Attacks++; d.Damage += packet.Attack; if (packet.Hit) d.Landed++;
            }
            Debug.Log("[PowerCalibration] 诊断 " + m.Name + (swap ? " 镜像" : " 正序") + "  用时" + run.Model.Elapsed.ToString("0.0") + "秒  帧数≈" + (int)(run.Model.Elapsed / (Step / 4)));
            foreach (var side in new[] { BattleSide.A, BattleSide.B })
            {
                var units = session.Battle.Soldiers.Where(u => u.Side == side).ToArray();
                foreach (var u in units)
                {
                    var d = map.TryGetValue(u.Id + 1, out var found) ? found : new UnitDiag();
                    Debug.Log("[PowerCalibration]   诊断单位 " + side + " " + u.CombatKind + " 血" + u.Health.ToString("0") + "/" + u.Stats.Health.ToString("0")
                        + " 存活帧" + d.Frames + " 移动帧" + d.MoveFrames + "(" + (100.0 * d.MoveFrames / Math.Max(1, d.Frames)).ToString("0") + "%)"
                        + " 前摇帧" + d.WindupFrames + " 无目标帧" + d.NoTargetFrames + " 无去向帧" + d.NoGoalFrames
                        + " 被堵帧" + d.StuckFrames + " 自锁帧" + d.GoalHereFrames
                        + " 走了" + d.Distance.ToString("0.0") + " 最近敌距" + (double.IsPositiveInfinity(d.MinEnemyGap) ? "从未" : d.MinEnemyGap.ToString("0.00"))
                        + " 出手" + d.Attacks + " 命中" + d.Landed + " 攻击力合计" + d.Damage.ToString("0"));
                }
            }
        }

        // 乙方没有战役敌军清单，BindBattle 会跳过它们的RPG档案，导致两边战力口径不一致。
        // 这里按同一规则补齐，保证标定数据里双方战力可比。
        private static void BindEnemyProfiles(BaseCampaignSession session, Side side)
        {
            foreach (var u in session.Battle.Soldiers.Where(u => u.Side == BattleSide.B && u.Rpg?.Version != 1))
            {
                bool hero = u.Kind == SoldierKind.Hero;
                int p = hero ? side.Profession : BaseRpg.Profession(u.CombatKind == SoldierKind.Hero ? SoldierKind.Melee : u.CombatKind);
                u.Rpg = hero
                    ? BaseRpg.Character("calib:b:hero:" + u.Id, p, false, false, rules, side.Level)
                    : new RpgCharacter { Version = 1, Id = "calib:b:" + u.Id, Profession = p, TemplateId = RpgRules.Template("soldier", p), Rules = rules };
                if (hero && !string.IsNullOrEmpty(side.Skill)) u.Rpg.SkillId = side.Skill;
                if (hero && !string.IsNullOrEmpty(side.Trait)) u.Rpg.Traits.Add(new RpgTrait { Id = side.Trait, Value = rules.Traits.Single(t => t.Id == side.Trait).Max });
            }
        }

        private static List<BaseLeader> Leaders(Side side)
        {
            var list = new List<BaseLeader>();
            for (int slot = 0; slot < side.Heroes; slot++)
            {
                var leader = new BaseLeader { Id = "calib:a:" + slot, Name = "标定队长" + slot };
                leader.Rpg = BaseRpg.Character(leader.Id, side.Profession, false, false, rules, side.Level);
                if (!string.IsNullOrEmpty(side.Skill)) leader.Rpg.SkillId = side.Skill;
                if (!string.IsNullOrEmpty(side.Trait)) leader.Rpg.Traits.Add(new RpgTrait { Id = side.Trait, Value = rules.Traits.Single(t => t.Id == side.Trait).Max });
                BaseRpg.ValidateCharacter(leader.Rpg, rules);
                leader.PersonalEquipment = new BasePersonalLoadout { Version = 1, Weapon = new[] { "sword", "bow", "spear" }[side.Profession], InitialWeapon = new[] { "sword", "bow", "spear" }[side.Profession], BaseStats = rules.Bare(leader.Rpg, side.Level) };
                // BaseRpg.Stats 取的是 Training.Level，不是 Rpg.Level；不设的话队长永远按1级算战力。
                leader.Training = BaseTraining.FromProfile(leader.PersonalEquipment.BaseStats);
                leader.Health = leader.MaxHealth = leader.PersonalEquipment.BaseStats.Health;
                list.Add(leader);
            }
            return list;
        }

        private static List<BaseWorkerBucket> Troops(Side side)
        {
            var list = new List<BaseWorkerBucket>();
            int[] counts = { side.Melee, side.Ranged, side.Cavalry };
            for (int p = 0; p < 3; p++)
            {
                var template = new RpgCharacter { Version = 1, Id = "calib:troop:" + p, Profession = p, TemplateId = RpgRules.Template("soldier", p) };
                var stats = rules.Bare(template, 1);
                for (int n = 0; n < counts[p]; n++)
                    list.Add(new BaseWorkerBucket { Kind = (BaseTroopKind)p, HasCombatProfile = true, CombatStats = stats.Clone(), Health = stats.Health, MaxHealth = stats.Health });
            }
            return list;
        }

        private static BattleScenario BuildScenario(Side a, Side b, int seed)
        {
            var board = BattleBoard.CreateDefault(20, 14);
            foreach (var p in board.Positions())
                board.Cell(p).Zone = p.X < 3 ? DeploymentZone.A : p.X >= board.Width - 3 ? DeploymentZone.B : DeploymentZone.None;
            return new BattleScenario
            {
                Board = board, A = Army(a), B = Army(b),
                MaxRounds = 300, StalemateRounds = 30, CommanderControl = true, MobileBattleRules = true,
                Objective = new ObjectiveConfig { Kind = ObjectiveKinds.Annihilation }, RandomSeed = seed
            };
        }

        private static ArmyDefinition Army(Side side)
        {
            var army = new ArmyDefinition
            {
                MeleeCount = side.Melee, RangedCount = side.Ranged, CavalryCount = side.Cavalry, HeroCount = side.Heroes,
                Capacity = Math.Max(6, side.Melee + side.Ranged + side.Cavalry + side.Heroes)
            };
            for (int p = 0; p < 3; p++)
            {
                var template = new RpgCharacter { Version = 1, Id = "calib:soldier:" + p, Profession = p, TemplateId = RpgRules.Template("soldier", p) };
                var stats = rules.Bare(template, 1);
                if (p == 0) army.Melee = stats; else if (p == 1) army.Ranged = stats; else army.Cavalry = stats;
            }
            for (int slot = 0; slot < side.Heroes; slot++)
            {
                var character = BaseRpg.Character("calib:hero:" + slot, side.Profession, false, false, rules, side.Level);
                army.CommanderStats.Add(rules.Bare(character, side.Level));
                army.CommanderNames.Add("队长" + slot);
                army.Commanders.Add(new CommanderLoadout { Melee = side.Melee, Ranged = side.Ranged, Cavalry = side.Cavalry, AttackRange = lead, AlertRange = Math.Min(lead, 6), GuardRange = 3 });
            }
            return army;
        }
    }
}

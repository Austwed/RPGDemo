using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BaseConquest.Battle;
using UnityEditor;
using UnityEngine;
using BaseConquest.Base;

namespace BaseConquest.Editor
{
    // 跳过战斗探针：验证「扫荡≈真实战斗」「自动迎战明显更痛且额外扣资源」「坚守按时间比例结算」。
    // 只读，不写存档；改 Losses / Engage / BaseRaidLoot 后必须重跑，退出码非 0 视为破坏规则。
    public static class VerifySkipBattle
    {
        private const string StagePath = "Assets/Resources/DemoStages/defense-male.json";
        private static int failures;

        public static void Run()
        {
            try
            {
                var template = Load();
                var rules = template.Demo?.Rpg ?? throw new InvalidOperationException("阶段档缺 Demo RPG 规则");
                // 真值对照必须用「只有普通兵」的编制：132 场样本里的阵亡率是全体口径，队长在那边也会真死；
                // 而 Demo 队长只倒地不真死，混在一起比会天然偏低。用 30 人是为了把取整粒度压到 3 个点以内。
                var pure = BuildSession(rules, 10, 10, 10, 0);
                var mixed = BuildSession(rules, 4, 4, 3, 3);
                Debug.Log("[Skip] 样本：普通兵编制 " + pure.Battle.Soldiers.Count + " 人；带队长编制 " + mixed.Battle.Soldiers.Count + " 人");

                // 1) 伤亡拆分：旧实现把 injury 同时当死亡比例和幸存者掉血比例，中高伤损多死一倍。
                Debug.Log("[Skip] injury | 旧阵亡 | 新阵亡 | 新幸存者均血 | 真实阵亡 | 真实幸存者均血");
                var truth = new (double injury, double dead, double hp)[] { (.2, .05, .83), (.4, .17, .76), (.6, .36, .59), (.85, .61, .48) };
                foreach (var row in truth)
                {
                    var e = new BaseEngagement { AllyWins = true, AllySurvival = 1 - row.injury };
                    var losses = BaseAutoResolve.Losses(pure, e);
                    var live = losses.Where(l => !l.Dead).ToArray();
                    double newDead = losses.Count(l => l.Dead) / (double)losses.Count;
                    double newHp = live.Length == 0 ? 0 : live.Average(l => l.After / Math.Max(1e-9, Stat(pure, l)));
                    var old = OldLosses(pure, e);
                    double oldDead = old.Count(l => l.Dead) / (double)old.Count;
                    Debug.Log("[Skip] " + row.injury.ToString("0.00") + " | " + oldDead.ToString("P0") + " | " + newDead.ToString("P0")
                        + " | " + newHp.ToString("P0") + " | " + row.dead.ToString("P0") + " | " + row.hp.ToString("P0"));
                    Check(Math.Abs(newDead - row.dead) <= .08, "扫荡阵亡比例偏离真值超过 8 个点：injury=" + row.injury);
                    Check(Math.Abs(newHp - row.hp) <= .12, "扫荡幸存者血量偏离真值超过 12 个点：injury=" + row.injury);
                    Check(oldDead > newDead + .05, "旧实现在中高伤损下应明显多死：injury=" + row.injury);
                }

                // 2) 血量守恒：带队长时死亡由普通兵承担，但整队剩余总血量必须仍然等于 1−injury。
                foreach (double injury in new[] { .3, .5, .7 })
                {
                    var e = new BaseEngagement { AllyWins = true, AllySurvival = 1 - injury };
                    var losses = BaseAutoResolve.Losses(mixed, e);
                    double before = losses.Sum(l => Stat(mixed, l));
                    double after = losses.Sum(l => l.Dead ? 0 : l.After);
                    double remain = after / Math.Max(1e-9, before);
                    Debug.Log("[Skip] injury=" + injury.ToString("0.00") + " → 整队剩余 " + remain.ToString("P1") + "（应为 " + (1 - injury).ToString("P0") + "）");
                    Check(Math.Abs(remain - (1 - injury)) <= .06, "整队剩余血量必须等于 1−injury：injury=" + injury);
                }

                // 3) 自动迎战：同一 injury 下必须比扫荡更痛。
                foreach (double injury in new[] { .3, .5, .6 })
                {
                    var e = new BaseEngagement { AllyWins = true, AllySurvival = 1 - injury };
                    int sweep = BaseAutoResolve.Losses(pure, e).Count(l => l.Dead);
                    int auto = BaseAutoResolve.Losses(pure, e, 0, BaseAutoResolve.AutoDefenseSeverity).Count(l => l.Dead);
                    Debug.Log("[Skip] injury=" + injury.ToString("0.00") + " → 扫荡阵亡 " + sweep + " 人／自动迎战阵亡 " + auto + " 人");
                    Check(auto > sweep, "自动迎战必须比扫荡更痛：injury=" + injury);
                }

                // 4) 坚守分支：能撑很久却只守 30 秒时，剩余血量应按时间比例接近满血，而不是「判胜却全灭」。
                var allies = mixed.Battle.Soldiers.Where(u => u.Side == BattleSide.A && u.IsAlive).ToList();
                var enemy = new List<BattleSoldier>
                {
                    new BattleSoldier { Id = 800, Side = BattleSide.B, Kind = SoldierKind.Melee, Health = 100,
                        Stats = new SoldierStats { Health = 100, Damage = 6, Defense = 4 } }
                };
                double hold = 30;
                var engaged = BasePower.Engage(allies, enemy, rules, hold);
                double endure = engaged.AllyHealth / Math.Max(1e-9, BasePower.Output(enemy, allies, rules));
                Debug.Log("[Skip] 坚守 " + hold + "s：判胜 " + engaged.AllyWins + "，剩余血量 " + engaged.AllySurvival.ToString("P1")
                    + "，能撑 " + endure.ToString("0.0") + "s，按时间应为 " + Math.Max(0, (endure - hold) / Math.Max(1e-9, endure)).ToString("P1"));
                Check(engaged.AllyWins, "以多打少且只守 30 秒应判胜");
                Check(Math.Abs(engaged.AllySurvival - Math.Max(0, (endure - hold) / Math.Max(1e-9, endure))) < .02, "坚守剩余血量必须按时间比例结算");
                Check(engaged.AllySurvival > .9, "能撑近 900 秒却只守 30 秒，剩余血量应接近满血");

                // 4) 自动迎战的经济代价：按敌方人头掠（口径见 BaseRaidLoot），打赢也至少轻掠，且写进战报净变化。
                var wallet = template.Copy();
                wallet.Inventory.Food = 300; wallet.Inventory.Materials = 120; wallet.Inventory.Coins = 800;
                var mission = new BaseCampaignEvent { Kind = "defense" };
                mission.EnemyArmy = Enumerable.Range(0, 10).Select(i => new BaseEnemyMember
                {
                    Id = "probe:" + i, Name = "剑士", Kind = SoldierKind.Melee, Stats = new SoldierStats(), Health = 100
                }).ToList();
                var report = new BaseCampaignReport { BattleId = "probe", Deployed = 10, Killed = 0, EnemyDeployed = 10 };
                int taken = BaseRaidLoot.Apply(wallet, mission, report, win: true, auto: true);
                Debug.Log("[Skip] 自动迎战资源：粮 300→" + wallet.Inventory.Food + "／料 120→" + wallet.Inventory.Materials
                    + "／钱 800→" + wallet.Inventory.Coins + "；战报净变化 粮 " + report.FoodChange + "／料 " + report.MaterialChange + "／钱 " + report.CoinChange);
                Check(taken == 10 + 20, "自动迎战零战损也应按轻掠扣 10 粮＋20 钱，实际 " + taken);
                Check(wallet.Inventory.Food == 290 && wallet.Inventory.Materials == 120 && wallet.Inventory.Coins == 780, "自动迎战轻掠应为 粮 −10／钱 −20，不扣原料");
                Check(report.FoodChange == -10 && report.CoinChange == -20, "掠走的部分必须计入战报净变化");
                Check(!string.IsNullOrEmpty(report.RaidNote), "战报必须留下自动迎战的掠夺说明");

                Debug.Log(failures == 0 ? "[Skip] PASS：扫荡贴真值、自动迎战更痛并扣资源、坚守按时间结算" : "[Skip] FAIL：" + failures + " 项");
                EditorApplication.Exit(failures == 0 ? 0 : 1);
            }
            catch (Exception error)
            {
                Debug.LogError("[Skip] 探针异常：" + error);
                EditorApplication.Exit(1);
            }
        }

        // 旧实现：injury 同时当死亡比例和幸存者掉血比例。
        private static List<BaseSweepLoss> OldLosses(BaseCampaignSession session, BaseEngagement e)
        {
            var losses = new List<BaseSweepLoss>();
            var allies = session.Battle.Soldiers.Where(u => u.Side == BattleSide.A && u.IsAlive).OrderBy(u => u.Id).ToArray();
            double injury = Math.Max(0, Math.Min(1, 1 - e.AllySurvival));
            var troops = allies.Where(u => u.Kind != SoldierKind.Hero).ToArray();
            int lost = (int)Math.Floor(troops.Length * injury + 1e-8);
            var dead = new HashSet<int>(troops.Take(lost).Select(u => u.Id));
            foreach (var unit in allies)
                losses.Add(new BaseSweepLoss
                {
                    UnitId = unit.Id, Leader = unit.Kind == SoldierKind.Hero, Dead = dead.Contains(unit.Id),
                    Before = unit.Health,
                    After = dead.Contains(unit.Id) ? 0 : Math.Max(1, unit.Health - unit.Stats.Health * injury)
                });
            return losses;
        }

        private static double Stat(BaseCampaignSession session, BaseSweepLoss loss)
        {
            var unit = session.Battle.Soldiers.First(u => u.Id == loss.UnitId);
            return unit.Stats.Health;
        }

        private static BaseCampaignSession BuildSession(RpgRules rules, int melee, int ranged, int cavalry, int leaders)
        {
            var opening = new BaseCampaignBattle { DemoCombat = true, Rpg = rules, Scenario = new BattleScenario() };
            var session = new BaseCampaignSession(opening);
            // 场景默认编制会自带一批 A 方单位，探针要的是可控样本，先清空再摆。
            session.Battle.Soldiers.Clear();
            int id = 900;
            foreach (var (kind, count) in new[] { (SoldierKind.Melee, melee), (SoldierKind.Ranged, ranged), (SoldierKind.Cavalry, cavalry) })
            {
                float health = kind == SoldierKind.Melee ? 100f : kind == SoldierKind.Ranged ? 75f : 100f;
                float damage = kind == SoldierKind.Melee ? 12f : kind == SoldierKind.Ranged ? 14f : 10f;
                float defense = kind == SoldierKind.Melee ? 4f : kind == SoldierKind.Ranged ? 2f : 3f;
                for (int i = 0; i < count; i++)
                    session.Battle.Soldiers.Add(new BattleSoldier
                    {
                        Id = id++, Side = BattleSide.A, Kind = kind, Health = health,
                        Stats = new SoldierStats { Health = health, Damage = damage, Defense = defense, Level = 1 }
                    });
            }
            var leaderHealth = new[] { 600f, 450f, 450f };
            for (int slot = 0; slot < leaders; slot++)
            {
                var leader = new BaseLeader { Id = "probe-leader-" + slot, Name = "队长" + (slot + 1) };
                opening.Leaders.Add(leader);
                session.Battle.Soldiers.Add(new BattleSoldier
                {
                    Id = id++, Side = BattleSide.A, Kind = SoldierKind.Hero, Health = leaderHealth[slot], OriginCommanderSlot = slot,
                    Stats = new SoldierStats { Health = leaderHealth[slot], Damage = 30, Defense = 8, Level = 1 }
                });
            }
            return session;
        }

        private static void Check(bool ok, string message)
        {
            if (ok) return;
            failures++;
            Debug.LogError("[Skip] 断言失败：" + message);
        }

        private static BaseState Load()
        {
            string path = Path.Combine(Environment.CurrentDirectory, StagePath);
            if (!File.Exists(path)) throw new FileNotFoundException("阶段档不存在：" + path);
            var state = JsonUtility.FromJson<BaseState>(File.ReadAllText(path));
            if (state == null) throw new InvalidOperationException("阶段档解析失败");
            return state;
        }
    }
}

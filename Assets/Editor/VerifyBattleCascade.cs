using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using BaseConquest.Base;
using BaseConquest.Battle;

namespace BaseConquest.Editor
{
    // 战内实时 N 探针：直接走生产代码 BaseDemoCombat.DeathChance，证明普通兵即死率不是开战冻结的快照，
    // 而是每次倒地判定时按「开战兵力 − 本场我方已真死 + 本场敌方已倒地」重算。只读，不改任何存档。
    public static class VerifyBattleCascade
    {
        private const string BalancePath = "Assets/Resources/DemoBalance/DemoBalance.json";
        private static int failures;

        public static void Run()
        {
            try
            {
                var rules = LoadRules();
                int baseline = rules.TroopBaseline.Sum();
                var opening = new BaseCampaignBattle
                {
                    DemoCombat = true,
                    PlayerTroopCounts = rules.TroopBaseline.ToArray(),
                    Rpg = rules,
                    Scenario = new BattleScenario()
                };
                var session = new BaseCampaignSession(opening);
                for (int i = 0; i < baseline; i++)
                    session.Battle.Soldiers.Add(new BattleSoldier { Id = 900 + i, Side = BattleSide.A, Kind = SoldierKind.Melee, Health = 100, Stats = new SoldierStats { Health = 100, Damage = 12, Defense = 4 } });
                session.Battle.Soldiers.Add(new BattleSoldier { Id = 950, Side = BattleSide.B, Kind = SoldierKind.Melee, Health = 100, Stats = new SoldierStats { Health = 100, Damage = 12, Defense = 4 } });

                var probe = session.Battle.Soldiers.First(u => u.Id == 900);
                var enemyProbe = session.Battle.Soldiers.First(u => u.Id == 950);
                Debug.Log("[Cascade] 合计基准 B=" + baseline + "（" + string.Join("/", rules.TroopBaseline) + "）");

                double atBase = BaseDemoCombat.DeathChance(session, probe);
                Report("满编 " + baseline + " 人（等于合计基准）", baseline, atBase);
                Check(Math.Abs(atBase - rules.SoldierDeathBase) < 1e-9, "基准兵力时即死率应等于基准概率 " + rules.SoldierDeathBase);

                session.Battle.Soldiers.First(u => u.Id == 903).InstantDead = true;
                double afterOne = BaseDemoCombat.DeathChance(session, probe);
                Report("本场已真死 1 人", baseline - 1, afterOne);
                Check(afterOne < atBase, "我方死人后 N 下降、即死率必须跟着下降");

                session.Battle.Soldiers.First(u => u.Id == 902).InstantDead = true;
                double afterTwo = BaseDemoCombat.DeathChance(session, probe);
                Report("本场已真死 2 人", baseline - 2, afterTwo);
                Check(afterTwo < afterOne, "继续减员应继续下降");

                session.Battle.Soldiers.First(u => u.Id == 901).KnockedDown = true;
                double stillSame = BaseDemoCombat.DeathChance(session, probe);
                Report("我方再倒地 1 人（未死）", baseline - 2, stillSame);
                Check(Math.Abs(stillSame - afterTwo) < 1e-9, "我方倒地不计入 N，只有真死才改变分母");

                session.Battle.Soldiers.First(u => u.Id == 950).KnockedDown = true;
                double withOneEnemy = BaseDemoCombat.DeathChance(session, probe);
                Report("缴获 1 名敌方倒地", baseline - 1, withOneEnemy);
                Check(withOneEnemy > afterTwo, "敌方倒地计入 N，即死率应回升");

                session.Battle.Soldiers.Add(new BattleSoldier { Id = 951, Side = BattleSide.B, Kind = SoldierKind.Melee, Health = 100, KnockedDown = true, Stats = new SoldierStats { Health = 100, Damage = 12, Defense = 4 } });
                double withTwoEnemy = BaseDemoCombat.DeathChance(session, probe);
                Report("缴获 2 名敌方倒地", baseline, withTwoEnemy);
                Check(Math.Abs(withTwoEnemy - atBase) < 1e-9, "减员与缴获抵消后应回到基准即死率");

                double enemyChance = BaseDemoCombat.DeathChance(session, enemyProbe);
                Report("同一刻敌方普通兵的即死率", baseline, enemyChance);
                Check(Math.Abs(enemyChance - withTwoEnemy) < 1e-9, "双方共用同一 N，同一刻概率必须相同");

                // 共用 N 的核心承诺：合计相同，编队不影响概率。全战士、全弓兵、全骑兵、平均分配都必须同值。
                foreach (var comp in new[] { new[] { baseline, 0, 0 }, new[] { 0, baseline, 0 }, new[] { 0, 0, baseline }, new[] { baseline / 3, baseline / 3, baseline - 2 * (baseline / 3) } })
                {
                    var mixOpening = new BaseCampaignBattle { DemoCombat = true, PlayerTroopCounts = comp, Rpg = rules, Scenario = new BattleScenario() };
                    var mixSession = new BaseCampaignSession(mixOpening);
                    var mixProbe = new BattleSoldier { Id = 990, Side = BattleSide.A, Kind = SoldierKind.Melee, Health = 100, Stats = new SoldierStats { Health = 100, Damage = 12, Defense = 4 } };
                    mixSession.Battle.Soldiers.Add(mixProbe);
                    double mix = BaseDemoCombat.DeathChance(mixSession, mixProbe);
                    Debug.Log("[Cascade] 编队 " + string.Join("/", comp) + " → 合计 N=" + comp.Sum() + "，即死率 " + mix.ToString("P1"));
                    Check(Math.Abs(mix - atBase) < 1e-9, "合计相同则编队不得改变即死率：" + string.Join("/", comp));
                }

                // 旧分兵种口径的硬伤：我方某个兵种为 0 时，敌方同兵种永远打不死、只会倒地可俘。共用后应消失。
                var emptyOpening = new BaseCampaignBattle { DemoCombat = true, PlayerTroopCounts = new[] { baseline, 0, 0 }, Rpg = rules, Scenario = new BattleScenario() };
                var emptySession = new BaseCampaignSession(emptyOpening);
                var enemyArcher = new BattleSoldier { Id = 970, Side = BattleSide.B, Kind = SoldierKind.Ranged, Health = 100, Stats = new SoldierStats { Health = 75, Damage = 14, Defense = 2 } };
                emptySession.Battle.Soldiers.Add(enemyArcher);
                double noArcher = BaseDemoCombat.DeathChance(emptySession, enemyArcher);
                Debug.Log("[Cascade] 我方 0 弓兵、合计仍 " + baseline + " → 敌方弓兵即死率 " + noArcher.ToString("P1"));
                Check(Math.Abs(noArcher - atBase) < 1e-9, "共用 N 后缺编兵种不再免死");

                Debug.Log(failures == 0 ? "[Cascade] PASS：N 在战内实时重算" : "[Cascade] FAIL：" + failures + " 项");
                EditorApplication.Exit(failures == 0 ? 0 : 1);
            }
            catch (Exception error)
            {
                Debug.LogError("[Cascade] 探针异常：" + error);
                EditorApplication.Exit(1);
            }
        }

        private static void Report(string note, int n, double p)
        {
            Debug.Log("[Cascade] " + note + " → 判定用 N=" + n + "，即死率 " + p.ToString("P1"));
        }

        private static void Check(bool ok, string message)
        {
            if (ok) return;
            failures++;
            Debug.LogError("[Cascade] 断言失败：" + message);
        }

        private static RpgRules LoadRules()
        {
            string path = Path.Combine(Environment.CurrentDirectory, BalancePath);
            if (!File.Exists(path)) throw new FileNotFoundException("配表快照不存在：" + path);
            var package = JsonUtility.FromJson<DemoBalancePackage>(File.ReadAllText(path));
            var rules = package?.Rules ?? throw new InvalidOperationException("配表快照缺RPG规则");
            rules.Validate();
            return rules;
        }
    }
}

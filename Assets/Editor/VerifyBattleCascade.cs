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
                int baseline = rules.TroopBaseline[0];
                var opening = new BaseCampaignBattle
                {
                    DemoCombat = true,
                    PlayerTroopCounts = new[] { baseline, baseline, baseline },
                    Rpg = rules,
                    Scenario = new BattleScenario()
                };
                var session = new BaseCampaignSession(opening);
                for (int i = 0; i < baseline; i++)
                    session.Battle.Soldiers.Add(new BattleSoldier { Id = 900 + i, Side = BattleSide.A, Kind = SoldierKind.Melee, Health = 100, Stats = new SoldierStats { Health = 100, Damage = 12, Defense = 4 } });
                session.Battle.Soldiers.Add(new BattleSoldier { Id = 950, Side = BattleSide.B, Kind = SoldierKind.Melee, Health = 100, Stats = new SoldierStats { Health = 100, Damage = 12, Defense = 4 } });

                var probe = session.Battle.Soldiers.First(u => u.Id == 900);
                var enemyProbe = session.Battle.Soldiers.First(u => u.Id == 950);
                Debug.Log("[Cascade] 基准 B=" + baseline);

                double atBase = BaseDemoCombat.DeathChance(session, probe);
                Report("满编 " + baseline + " 人（等于基准）", baseline, atBase);
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

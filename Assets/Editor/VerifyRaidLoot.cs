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
    // 掠夺探针（2026-11-06 改口径）：确认「守军全灭才掠、按残余人数 × 剩余秒数折算、Demo 只扣钱」。
    // 只读，不改存档；改 BaseRaidLoot 后必须重跑，退出码非 0 视为破坏规则。
    public static class VerifyRaidLoot
    {
        private const string StagePath = "Assets/Resources/DemoStages/defense-male.json";
        private static int failures;

        public static void Run()
        {
            try
            {
                var template = Load();

                // 1) 折算公式本身。
                Check(BaseRaidLoot.Value(2, 20) == 40, "2 人 × 20 秒应为 40，实际 " + BaseRaidLoot.Value(2, 20));
                Check(BaseRaidLoot.Value(0, 20) == 0, "没有残余敌兵不应掠夺");
                Check(BaseRaidLoot.Value(2, 0) == 0, "剩余 0 秒不应掠夺");
                Check(BaseRaidLoot.Value(3, 7.4) == 22, "3 人 × 7.4 秒应四舍五入为 22，实际 " + BaseRaidLoot.Value(3, 7.4));

                // 2) 胜利（敌方全灭或撑到撤离）不掠。
                var win = Run(template.Copy(), 12, enemyLeft: 0, seconds: 30, win: true, auto: false);
                Check(win.Take == 0 && string.IsNullOrEmpty(win.Note), "胜利不应掠夺");

                // 3) 失败：2 人 × 20 秒 = 40 钱，粮草与原料不动。
                var loss = Run(template.Copy(), 12, enemyLeft: 2, seconds: 20, win: false, auto: false);
                Check(loss.Coins == 40 && loss.Food == 0 && loss.Materials == 0, "2 人 × 20 秒应只扣 40 钱，实际 " + loss);

                // 4) 同一批敌人，剩余时间越少抢得越少。
                var early = Run(template.Copy(), 12, enemyLeft: 2, seconds: 25, win: false, auto: false);
                var late = Run(template.Copy(), 12, enemyLeft: 2, seconds: 5, win: false, auto: false);
                Check(early.Take > late.Take, "剩余时间越多应抢得越多（" + early.Take + " vs " + late.Take + "）");

                // 5) 钱包不足时只扣现有，不出现负数。
                var poor = template.Copy(); poor.Inventory.Coins = 10;
                var clamped = Run(poor, 12, enemyLeft: 4, seconds: 20, win: false, auto: false);
                Check(clamped.Coins == 10 && poor.Inventory.Coins == 0, "钱包不足应扣到 0 为止，实际 " + clamped);
                BaseEconomy.Validate(poor);

                // 6) 三个开关字段全 0 时不掠（开发者测试入侵）。
                var silent = Run(template.Copy(), 12, enemyLeft: 4, seconds: 20, win: false, auto: false,
                    mission: new BaseCampaignEvent { Kind = "defense", FoodRaid = 0, MaterialRaid = 0, CoinRaid = 0 });
                Check(silent.Take == 0 && string.IsNullOrEmpty(silent.Note), "开关全 0 时不应扣任何资源");

                // 7) 无人迎战：敌方全员 × 满倒计时，并在战报写明原因。
                var unguarded = Run(template.Copy(), 12, enemyLeft: 12, seconds: 30, win: false, auto: true, outcome: "Unopposed");
                Check(unguarded.Coins == 360, "无人迎战 12 人 × 30 秒应为 360，实际 " + unguarded);
                Check(unguarded.Note.Contains("无人迎战"), "无人迎战的说明应写明原因：" + unguarded.Note);

                // 8) 游戏结束时不再结算掠夺。
                var over = template.Copy(); over.Aftermath.GameOver = true;
                var ended = Run(over, 12, enemyLeft: 2, seconds: 20, win: false, auto: false);
                Check(ended.Take == 0, "游戏结束后不应再掠夺");

                // 9) 战报说明写清人数与秒数。
                Check(loss.Note.Contains("2 人") && loss.Note.Contains("20"), "战报应写清残余人数与剩余秒数：" + loss.Note);

                // 10) 与库存无关：钱包同为 1000 时，被掠量只由人数与秒数决定。
                var rich = template.Copy(); rich.Inventory.Coins = 1000;
                var half = template.Copy(); half.Inventory.Coins = 1000; half.Inventory.Food = 3;
                var richLoot = Run(rich, 12, enemyLeft: 3, seconds: 10, win: false, auto: false);
                var halfLoot = Run(half, 12, enemyLeft: 3, seconds: 10, win: false, auto: false);
                Check(richLoot.Take == halfLoot.Take, "掠夺量只按人数与秒数，不应随其他库存变");

                Debug.Log(failures == 0 ? "[Loot] PASS：守军全灭才掠、按残余 × 剩余秒数折算、Demo 只扣钱" : "[Loot] FAIL：" + failures + " 项");
                EditorApplication.Exit(failures == 0 ? 0 : 1);
            }
            catch (Exception error)
            {
                Debug.LogError("[Loot] 探针异常：" + error);
                EditorApplication.Exit(1);
            }
        }

        private struct Taken
        {
            public int Take, Food, Materials, Coins;
            public string Note;
            public override string ToString() => "粮" + Food + "／原料" + Materials + "／钱" + Coins;
        }

        private static Taken Run(BaseState s, int rosterCount, int enemyLeft, double seconds, bool win, bool auto,
            string outcome = "SideBWin", BaseCampaignEvent mission = null)
        {
            var e = mission ?? new BaseCampaignEvent { Kind = "defense" };
            e.Kind = "defense";
            var r = new BaseCampaignReport { Outcome = outcome, Deployed = 10, Killed = 5, Passive = auto, EnemyDeployed = rosterCount };
            int before = s.Inventory.Coins;
            int taken = BaseRaidLoot.Apply(s, e, r, win, auto, enemyLeft, seconds);
            int after = s.Inventory.Coins;
            Check(before - after == taken, "实扣钱币应等于返回值：报 " + taken + "，实扣 " + (before - after));
            return new Taken { Take = taken, Food = -r.FoodChange, Materials = -r.MaterialChange, Coins = -r.CoinChange, Note = r.RaidNote ?? "" };
        }

        private static void Check(bool ok, string message) { if (ok) return; failures++; Debug.LogError("[Loot] 断言失败：" + message); }

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

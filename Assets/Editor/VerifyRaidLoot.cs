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
    // 掠夺探针：确认「不按库存百分比、按敌方人头、战损 30%/50% 分档、自动迎战至少轻掠且比亲自指挥亏」。
    // 只读，不改存档；改 BaseRaidLoot / 09d 掠夺单价后必须重跑，退出码非 0 视为破坏规则。
    public static class VerifyRaidLoot
    {
        private const string StagePath = "Assets/Resources/DemoStages/defense-male.json";
        private static int failures;

        public static void Run()
        {
            try
            {
                var template = Load();
                var rules = template.Demo?.Rpg ?? throw new InvalidOperationException("阶段档缺 Demo RPG 规则");
                Debug.Log("[Loot] 掠夺单价（每名敌军）：口粮 " + rules.OpeningDefenseLoss.Food + " 天／原料 "
                    + rules.OpeningDefenseLoss.Materials + "／钱币 " + rules.OpeningDefenseLoss.Coins);

                // 口径来自配表：3 天口粮 / 1 原料 / 6 钱。
                Check(rules.OpeningDefenseLoss.Food == 3 && rules.OpeningDefenseLoss.Materials == 1 && rules.OpeningDefenseLoss.Coins == 6,
                    "09d「按人头掠夺」应为 3 天口粮／1 原料／6 钱，实际 " + rules.OpeningDefenseLoss.Food + "/" + rules.OpeningDefenseLoss.Materials + "/" + rules.OpeningDefenseLoss.Coins);
                var fresh = new BaseCampaignEvent();
                Check(fresh.FoodRaid == 3 && fresh.MaterialRaid == 1 && fresh.CoinRaid == 6, "事件默认掠夺单价应为每人 3 天／1 原料／6 钱");
                Check(rules.OpeningDefenseLoss.Food <= BaseRaidLoot.MaxRationDays, "配表口粮天数不应超过夹取上限 " + BaseRaidLoot.MaxRationDays);

                // 日耗口径：近战/弓兵 1，骑兵 2，队长 2。
                Check(BaseRaidLoot.Ration(template, SoldierKind.Melee) == 1, "近战日耗应为 1");
                Check(BaseRaidLoot.Ration(template, SoldierKind.Ranged) == 1, "弓兵日耗应为 1");
                Check(BaseRaidLoot.Ration(template, SoldierKind.Cavalry) == 2, "骑兵日耗应为 2");
                Check(BaseRaidLoot.Ration(template, SoldierKind.Hero) == 2, "队长日耗应为 2");

                // 分档边界：30% 与 50% 都含在更高一档里。
                Check(BaseRaidLoot.Tier(.29, true, false) == 0, "战损 29% 亲自指挥不应被掠");
                Check(BaseRaidLoot.Tier(.30, true, false) == 1, "战损 30% 应进轻掠");
                Check(BaseRaidLoot.Tier(.49, true, false) == 1, "战损 49% 仍是轻掠");
                Check(BaseRaidLoot.Tier(.50, true, false) == 2, "战损 50% 应进重掠");
                Check(BaseRaidLoot.Tier(0, true, true) == 1, "自动迎战打赢也要至少轻掠");
                Check(BaseRaidLoot.Tier(0, false, false) == 2, "打输了不论战损都进重掠");

                // 12 名敌军：1 队长(2) + 5 近战(1) + 4 弓兵(1) + 2 骑兵(2) = 每天 2+5+4+4 = 15 粮。
                var roster = Roster(template, heroes: 1, melee: 5, ranged: 4, cavalry: 2);
                Check(roster.Count == 12, "探针名单应为 12 人");
                int perDay = roster.Sum(u => BaseRaidLoot.Ration(template, u.Kind));
                Check(perDay == 15, "12 人名单一天应吃 15 粮，实际 " + perDay);

                // 1) 亲自指挥、战损 20%：一分不扣。
                var none = Run(template.Copy(), roster, deployed: 10, lost: 2, win: true, auto: false);
                Check(none.Take == 0 && none.Food == 0 && none.Materials == 0 && none.Coins == 0, "战损 20% 亲自指挥不应扣资源");
                Check(string.IsNullOrEmpty(none.Note), "没被掠就不该写掠夺说明");

                // 2) 亲自指挥、战损 30%：轻掠 = 1 天口粮（15）＋24 钱，不扣原料。
                var light = Run(template.Copy(), roster, deployed: 10, lost: 3, win: true, auto: false);
                Check(light.Food == 15 && light.Materials == 0 && light.Coins == 24, "轻掠应为 粮15／原料0／钱24，实际 " + light);

                // 3) 亲自指挥、战损 50%：重掠 = 3 天口粮（45）＋12 原料＋72 钱。
                var heavy = Run(template.Copy(), roster, deployed: 10, lost: 5, win: true, auto: false);
                Check(heavy.Food == 45 && heavy.Materials == 12 && heavy.Coins == 72, "重掠应为 粮45／原料12／钱72，实际 " + heavy);

                // 4) 自动迎战打赢、零战损：仍按轻掠（仓促应战总会丢点东西）。
                var autoWin = Run(template.Copy(), roster, deployed: 10, lost: 0, win: true, auto: true);
                Check(autoWin.Food == 15 && autoWin.Coins == 24, "自动迎战打赢应按轻掠，实际 " + autoWin);

                // 5) 自动迎战打输：重掠，且明显多于打赢。
                var autoLose = Run(template.Copy(), roster, deployed: 10, lost: 6, win: false, auto: true);
                Check(autoLose.Food == 45 && autoLose.Coins == 72, "自动迎战打输应按重掠，实际 " + autoLose);
                Check(autoLose.Take > autoWin.Take, "打输的损失应明显高于打赢");

                // 6) 无人迎战：按重掠，且战报能说清原因。
                var unguarded = Run(template.Copy(), roster, deployed: 0, lost: 0, win: false, auto: true, outcome: "Unopposed");
                Check(unguarded.Food == 45 && unguarded.Coins == 72, "无人迎战应按重掠，实际 " + unguarded);
                Check(unguarded.Note.Contains("无人迎战"), "无人迎战的掠夺说明应写明原因：" + unguarded.Note);

                // 7) 库存不足时只扣现有，不出现负数。
                var poor = template.Copy();
                poor.Inventory.Food = 5; poor.Inventory.Materials = 0; poor.Inventory.Coins = 3;
                var clamped = Run(poor, roster, deployed: 10, lost: 5, win: true, auto: false);
                Check(clamped.Food == 5 && clamped.Materials == 0 && clamped.Coins == 3, "库存不足时应扣到 0 为止，实际 " + clamped);
                Check(poor.Inventory.Food == 0 && poor.Inventory.Materials == 0 && poor.Inventory.Coins == 0, "库存不应变成负数");
                BaseEconomy.Validate(poor);

                // 8) 旧档把整包损失（粮 15）塞进按人头字段时，口粮天数被夹到上限，不会一人卷走 15 天。
                var legacy = template.Copy();
                var legacyEvent = new BaseCampaignEvent { Kind = "defense", FoodRaid = 15, MaterialRaid = 8, CoinRaid = 10 };
                var clampedLegacy = Run(legacy, roster, deployed: 10, lost: 5, win: true, auto: false, mission: legacyEvent);
                Check(clampedLegacy.Food == BaseRaidLoot.MaxRationDays * perDay, "旧档整包数值应被夹到 " + BaseRaidLoot.MaxRationDays + " 天，实际 " + clampedLegacy);

                // 9) 掠夺单价由配表统一写入事件，随机入侵不再各写一份数字。
                var configured = new BaseCampaignEvent { Kind = "defense", FoodRaid = 0, MaterialRaid = 0, CoinRaid = 0 };
                BaseRaidLoot.ApplyRule(template, configured);
                Check(configured.FoodRaid == 3 && configured.MaterialRaid == 1 && configured.CoinRaid == 6, "ApplyRule 应按配表写入 3/1/6");

                // 10) 单价全 0（开发者的测试入侵）不该被「至少轻掠」兜出量来。
                var silentState = template.Copy();
                var silent = Run(silentState, roster, deployed: 10, lost: 6, win: true, auto: true,
                    mission: new BaseCampaignEvent { Kind = "defense", FoodRaid = 0, MaterialRaid = 0, CoinRaid = 0 });
                Check(silent.Take == 0 && string.IsNullOrEmpty(silent.Note), "掠夺单价为 0 时不应扣任何资源");

                // 11) 与库存多少无关：满仓和半仓被掠走的量必须一样。
                var rich = template.Copy();
                rich.Inventory.Food = 100000; rich.Inventory.Coins = 100000; rich.Inventory.Materials = 100000;
                var half = template.Copy();
                half.Inventory.Food = 500; half.Inventory.Coins = 500; half.Inventory.Materials = 500;
                var richLoot = Run(rich, roster, deployed: 10, lost: 5, win: true, auto: false);
                var halfLoot = Run(half, roster, deployed: 10, lost: 5, win: true, auto: false);
                Check(richLoot.Take == halfLoot.Take, "掠夺量只按人头，不应随库存变（满仓 " + richLoot.Take + " vs 半仓 " + halfLoot.Take + "）");

                Debug.Log(failures == 0 ? "[Loot] PASS：按人头掠夺、30%/50% 分档、自动迎战至少轻掠、库存不足不越界"
                    : "[Loot] FAIL：" + failures + " 项");
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

        private static Taken Run(BaseState s, List<BaseEnemyMember> roster, int deployed, int lost, bool win, bool auto,
            string outcome = "SideAWin", BaseCampaignEvent mission = null)
        {
            var e = mission ?? new BaseCampaignEvent { Kind = "defense" };
            e.Kind = "defense";
            e.EnemyArmy = roster.Select(u => u.Copy()).ToList();
            var r = new BaseCampaignReport
            {
                Outcome = outcome,
                Deployed = deployed,
                Killed = lost,
                Passive = auto,
                EnemyDeployed = roster.Count
            };
            int before = s.Inventory.Food + s.Inventory.Materials + s.Inventory.Coins;
            int taken = BaseRaidLoot.Apply(s, e, r, win, auto);
            int after = s.Inventory.Food + s.Inventory.Materials + s.Inventory.Coins;
            Check(before - after == taken, "实扣总量应等于返回值：报 " + taken + "，实扣 " + (before - after));
            return new Taken
            {
                Take = taken,
                Food = -r.FoodChange,
                Materials = -r.MaterialChange,
                Coins = -r.CoinChange,
                Note = r.RaidNote ?? ""
            };
        }

        private static List<BaseEnemyMember> Roster(BaseState s, int heroes, int melee, int ranged, int cavalry)
        {
            var army = s.Campaign.EnemySites[0].Capacity;
            var list = new List<BaseEnemyMember>();
            int id = 0;
            void Add(SoldierKind kind, int count, string name)
            {
                for (int i = 0; i < count; i++)
                {
                    var stats = (kind == SoldierKind.Hero ? army.CommanderProfile(0) : army.Stats(kind)).Clone();
                    list.Add(new BaseEnemyMember
                    {
                        Id = "probe:" + (id++), Name = name, Kind = kind, Stats = stats, Health = stats.Health,
                        Appearance = kind == SoldierKind.Hero ? "huoya" : ""
                    });
                }
            }
            Add(SoldierKind.Hero, heroes, "探针头目");
            Add(SoldierKind.Melee, melee, "剑士");
            Add(SoldierKind.Ranged, ranged, "弓兵");
            Add(SoldierKind.Cavalry, cavalry, "骑兵");
            return list;
        }

        private static void Check(bool ok, string message)
        {
            if (ok) return;
            failures++;
            Debug.LogError("[Loot] 断言失败：" + message);
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

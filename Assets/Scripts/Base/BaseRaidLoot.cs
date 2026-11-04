using System;
using System.Collections.Generic;
using System.Linq;
using BaseConquest.Battle;

namespace BaseConquest.Base
{
    // 掠夺结算（2026-11-04 用户裁决，取代旧的「按库存百分比扣」）。
    //
    // 旧口径：自动迎战按库存 20%／15%／15% 扣，打输再按整包固定值扣。
    // 问题：仓库越满被抢得越多，等于惩罚正常经营；而且「打输扣 15 粮」和来袭了多少人毫无关系。
    //
    // 新口径：按人头算账——每个敌人只带走他自己那点东西，库存多少不影响单价。
    //   人口径：本场实际参战的敌方单位数（含带队指挥），不随击杀数浮动，玩家能从来袭人数直接预判损失。
    //   战损口径：本场阵亡＋失联 ÷ 本场部署，两条线 30%／50%。
    //
    //   档位   触发                                每名敌军带走
    //   不掠   亲自指挥且战损 <30%                   —
    //   轻掠   战损 ≥30%，或自动迎战（无论战损）    1 天口粮＋2 钱（重掠的三分之一）
    //   重掠   战损 ≥50%，或这一战打输              3 天口粮＋1 原料＋6 钱
    //
    // 「3 天口粮」按敌人自己的日耗折算（近战/弓兵 1、骑兵 2、队长 2），所以马匪波掠走的粮更多。
    // 自动迎战永远至少轻掠：仓促应战营里总会丢点东西；但它同时把伤亡放大 1.35 倍（BaseAutoResolve），
    // 所以正常经营能靠它过关，只是比亲自指挥亏一点——是兜底，不是无脑罚款。
    public static class BaseRaidLoot
    {
        public const double LightThreshold = .30;
        public const double HeavyThreshold = .50;
        public const int LightDivisor = 3;
        // 旧档里的「首防失败损失」是整包数值（粮 15）。换成按人头后必须夹住，免得一个敌人卷走 15 天口粮。
        public const int MaxRationDays = 7;

        // 事件的三个 Raid 字段含义已改为「每名敌军带走多少」：FoodRaid 是口粮天数，不是粮草件数。
        public static void ApplyRule(BaseState s, BaseCampaignEvent e)
        {
            if (e == null) return;
            var loss = s?.Demo?.Rpg?.OpeningDefenseLoss;
            e.FoodRaid = loss?.Food ?? 3;
            e.MaterialRaid = loss?.Materials ?? 1;
            e.CoinRaid = loss?.Coins ?? 6;
        }

        public static int Tier(double casualty, bool win, bool auto)
        {
            if (!win) return 2;
            if (casualty >= HeavyThreshold) return 2;
            if (casualty >= LightThreshold) return 1;
            return auto ? 1 : 0;
        }

        // 返回实际扣掉的资源总量（0 表示没掠）。enemyHeads < 0 时取战报里的实际参战人头。
        public static int Apply(BaseState s, BaseCampaignEvent e, BaseCampaignReport r, bool win, bool auto, int enemyHeads = -1)
        {
            if (s == null || e == null || r == null || e.Kind != "defense") return 0;
            // 三个单价全为 0 表示这一战不掠（如开发者的测试入侵），不要被「轻掠至少 1 天」兜出量来。
            if (e.FoodRaid + e.MaterialRaid + e.CoinRaid <= 0) return 0;
            int heads = enemyHeads >= 0 ? enemyHeads : r.EnemyDeployed;
            if (heads <= 0) return 0;
            double casualty = r.Deployed > 0
                ? Math.Max(0, Math.Min(1, (double)(r.Killed + r.Missing) / r.Deployed))
                : (win ? 0 : 1);
            int tier = Tier(casualty, win, auto);
            if (tier <= 0) return 0;

            int days = Math.Max(0, Math.Min(MaxRationDays, e.FoodRaid));
            int materials = Math.Max(0, Math.Min(50, e.MaterialRaid));
            int coins = Math.Max(0, Math.Min(200, e.CoinRaid));
            if (tier == 1)
            {
                days = Math.Max(1, days / LightDivisor);
                materials /= LightDivisor;
                coins /= LightDivisor;
            }

            var raiders = e.EnemyArmy ?? new List<BaseEnemyMember>();
            // 每个敌人带走「自己」那几天的口粮：骑兵一天吃 2，马匪波自然比步匪波掠得多。
            double food = raiders.Count > 0
                ? raiders.Sum(unit => days * Ration(s, unit.Kind)) * (double)heads / raiders.Count
                : (double)heads * days * s.Rules.SoldierFood;
            int takeFood = Math.Min(Math.Max(0, s.Inventory.Food), Math.Max(0, (int)Math.Round(food)));
            int takeMaterials = Math.Min(Math.Max(0, s.Inventory.Materials), Math.Max(0, materials * heads));
            int takeCoins = Math.Min(Math.Max(0, s.Inventory.Coins), Math.Max(0, coins * heads));
            if (takeFood + takeMaterials + takeCoins <= 0) return 0;

            s.Inventory.Food -= takeFood;
            s.Inventory.Materials -= takeMaterials;
            s.Inventory.Coins -= takeCoins;
            r.FoodChange -= takeFood;
            r.MaterialChange -= takeMaterials;
            r.CoinChange -= takeCoins;

            string who = r.Outcome == "Unopposed" ? "无人迎战" : auto ? "自动迎战" : win ? "惨胜" : "迎战失利";
            string casualtyNote = r.Deployed > 0 ? "，战损 " + Math.Round(casualty * 100) + "%（" + (r.Killed + r.Missing) + "/" + r.Deployed + "）" : "";
            string per = days + " 天口粮" + (materials > 0 ? "＋" + materials + " 原料" : "") + "＋" + coins + " 钱";
            r.RaidNote = who + casualtyNote + " · 敌军 " + heads + " 人各带走 " + per
                + "：粮 −" + takeFood + (takeMaterials > 0 ? "／原料 −" + takeMaterials : "") + "／钱币 −" + takeCoins;
            return takeFood + takeMaterials + takeCoins;
        }

        // 该敌军一天的军粮：与 BaseEconomy.FoodRequired 同一套口径。
        public static int Ration(BaseState s, SoldierKind kind)
        {
            if (s == null) return 1;
            if (kind == SoldierKind.Hero) return BaseRpg.Active(s) ? s.Demo.Rpg.LeaderFood : s.Rules.LeaderFood;
            if (kind == SoldierKind.Cavalry) return s.Rules.CavalryFood;
            if (kind == SoldierKind.Ranged && BaseRpg.Active(s)) return s.Demo.Rpg.ArcherFood;
            return s.Rules.SoldierFood;
        }
    }
}

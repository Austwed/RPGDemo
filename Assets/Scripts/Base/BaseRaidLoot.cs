using System;
using System.Collections.Generic;
using System.Linq;
using BaseConquest.Battle;

namespace BaseConquest.Base
{
    // 掠夺结算（2026-11-06 用户裁决，取代 11-04 的「按人头 × 战损档位」）。
    //
    // 口径：守军全灭（含撤退、含无人迎战）时，敌军按「还能搬多久」折算损失。
    //   掠夺价值 = 敌方残余单位数 × 坚守剩余秒数 × ValuePerUnitSecond（占位 1）
    //   例：剩 2 名敌兵、倒计时还剩 20 秒 → 约价值 40 的物资。
    //   DEMO 修缮欠款上限取首防一次守胜赏金。
    //   敌方全灭，或守军撑到倒计时结束（敌军撤离），不产生掠夺。
    //
    // Demo 只扣金币，不动粮草、原料、军需和物品；完整版按钱币→粮草→原料→军需→物品
    // 的顺序扣该基地库存（见玩法案 §11.0B）。
    //
    // 事件上的 FoodRaid／MaterialRaid／CoinRaid 三个字段保留，但只作为「这一战掠不掠」的
    // 开关（三者全 0 = 不掠，开发者测试入侵用）；数值本身不再参与计算。
    public static class BaseRaidLoot
    {
        // 每名敌军每秒能搬走的价值。占位值，等 P100 平衡。
        public const int ValuePerUnitSecond = 1;

        // 首防欠款用一份固定守胜赏金作上限，避免连续失守累加到无法募兵。
        public static int DebtLimit(BaseState state)=>BaseDemo.Active(state)?Math.Max(0,state.Demo.Rpg?.OpeningDefenseReward?.Coins??0):0;
        public static int Charge(BaseState state,int enemies,double seconds)
        {
            int availableCredit=Math.Max(0,state.Inventory.Coins+DebtLimit(state));
            return Math.Min(Value(enemies,seconds),availableCredit);
        }
        public static bool ChargesRepairs(BaseCampaignEvent mission)=>
            mission?.Kind=="defense" && (mission.FoodRaid>0||mission.MaterialRaid>0||mission.CoinRaid>0);
        public static int Recovered(int down)=>Math.Max(0,down)/2;
        public static int Value(int enemyLeft, double secondsLeft)
        {
            if (enemyLeft <= 0 || secondsLeft <= 0) return 0;
            return (int)Math.Round(enemyLeft * secondsLeft * ValuePerUnitSecond, MidpointRounding.AwayFromZero);
        }

        // 事件的三个 Raid 字段含义已改为「是否掠夺」的开关；保留写入以免旧档与配表断裂。
        public static void ApplyRule(BaseState s, BaseCampaignEvent e)
        {
            if (e == null) return;
            var loss = s?.Demo?.Rpg?.OpeningDefenseLoss;
            e.FoodRaid = loss?.Food ?? 3;
            e.MaterialRaid = loss?.Materials ?? 1;
            e.CoinRaid = loss?.Coins ?? 6;
        }

        // 返回实际扣掉的钱币。enemyLeft < 0 时用战报里的敌军人数差推算；secondsLeft < 0 视为 0。
        public static int Apply(BaseState s, BaseCampaignEvent e, BaseCampaignReport r, bool win, bool auto,
            int enemyLeft = -1, double secondsLeft = -1)
        {
            if (s == null || r == null || !ChargesRepairs(e)) return 0;
            if (win || s.Aftermath.GameOver) return 0;

            if (enemyLeft < 0)
                enemyLeft = Math.Max(0, r.EnemyDeployed - r.EnemyLost - r.EnemyCaptured - r.EnemyReturned - r.EnemyDispersed);
            if (secondsLeft < 0) secondsLeft = 0;

            int value = Value(enemyLeft, secondsLeft);
            if (value <= 0) return 0;
            int take = Charge(s,enemyLeft,secondsLeft);

            s.Inventory.Coins -= take;
            r.CoinChange -= take;

            string who = r.Outcome == "Unopposed" ? "无人迎战" : auto ? "被动迎战" : "迎战失利";
            r.RaidNote = who + " · 敌军 " + enemyLeft + " 人 × 剩余 " + Math.Round(secondsLeft) + " 秒，设施修缮 " + take + " 币 · 余额 " + s.Inventory.Coins;
            return take;
        }

        // 该敌军一天的军粮：与 BaseEconomy.FoodRequired 同一套口径。掠夺不再用它，留给配表与战报。
        public static int Ration(BaseState s, SoldierKind kind)
        {
            if (s == null) return 1;
            if (kind == SoldierKind.Hero) return BaseRpg.Active(s) ? s.Demo.Rpg.LeaderFood : s.Rules.LeaderFood;
            if (BaseDemo.Active(s)) return s.Rules.SoldierFood;
            if (kind == SoldierKind.Mage && BaseRpg.Active(s)) return s.Demo.Rpg.MageFood;
            if (kind == SoldierKind.Cavalry) return s.Rules.CavalryFood;
            if (kind == SoldierKind.Ranged && BaseRpg.Active(s)) return s.Demo.Rpg.ArcherFood;
            return s.Rules.SoldierFood;
        }
    }
}

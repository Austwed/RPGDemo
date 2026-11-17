using System;
using System.Collections.Generic;
using System.Linq;

namespace BaseConquest.Battle
{
    // 目标进度的一次只读快照；由 BattleObjectiveView 生成，供顶部UI与离线检查共用。
    public sealed class ObjectiveProgress
    {
        // 任务方固定我方；显示的目标始终是"我方任务"，不随观察阵营切换。
        public BattleSide MissionSide = BattleSide.A;
        public BattleSide TargetSide = BattleSide.B;
        public string Kind = ObjectiveKinds.Annihilation;
        public string Title = "";
        // 是否已由既有公开信号宣布／结算获胜。
        public bool Declared;
        // 情报是否精确（真实视野内）；false 时不应画百分比，用"未知／情报不足"表达。
        public bool Exact;
        // 已确认的敌方指挥者失能数与敌方普通兵阵亡数。Exact 为 false 时只是已确认值。
        public int CommandersDown, CommandersTotal, TroopsLost, TroopsDeployed;
        // 精确分母可用时为累计实际部署普通兵；未知时 -1。
        public int ExactDeployed = -1;
        // 是否已确认敌方所有已入场指挥者失能。情报不足时为 false（不提前勾选）。
        public bool AllCommandersDown;
        // 已确认满足主要歼灭条件（指挥者全倒 + 普通兵累计阵亡严格过半）。
        public bool Satisfied;
        // 结束时我方目标是否未达成。
        public bool Failed;
        public bool Preparing;
        public int HoldRound, HoldTarget;
    }

    // 纯只读目标视图：从当前模型与真实视野取得进度，绝不写单位、目标、指令、视野或结果。
    public static class BattleObjectiveView
    {
        public static string KindDisplayName(string kind) => kind == ObjectiveKinds.Annihilation ? "歼灭" : kind == ObjectiveKinds.Hold ? "坚守" : "未知目标";

        public static ObjectiveProgress Current(BattleSimulation battle, BattleSide observer)
        {
            var p = Annihilation(battle, observer);
            if (p.Kind == ObjectiveKinds.Hold) { p.HoldRound = battle.RoundNumber; p.HoldTarget = battle.Scenario.Objective.HoldRounds; p.Satisfied |= battle.HoldVictorySatisfied; }
            return p;
        }

        public static ObjectiveProgress Annihilation(BattleSimulation battle, BattleSide observer)
        {
            var p = new ObjectiveProgress { Kind = battle?.Scenario?.Objective?.Kind ?? ObjectiveKinds.Annihilation };
            if (battle == null) return p;
            p.Title = string.IsNullOrWhiteSpace(battle.Scenario?.Objective?.Title) ? "" : battle.Scenario.Objective.Title;
            var target = p.TargetSide;
            p.Preparing = battle.Phase == BattlePhase.Deployment;
            // 任务方固定我方：显示的是我方任务，不因切换到敌方测试视角而变成灭我方。
            p.Declared = battle.VictoryDeclared || battle.Report?.Outcome == BattleOutcome.SideAWin;
            var heroOutcome = battle.Vision.Status(observer, target, SoldierKind.Hero);
            // 情报精确需同时满足"是观察方自己的阵营或关闭迷雾"，由 Vision.Status 承担；普通兵同理。
            var troopMelee = battle.Vision.Status(observer, target, SoldierKind.Melee);
            var troopRanged = battle.Vision.Status(observer, target, SoldierKind.Ranged);
            var troopCavalry = battle.Vision.Status(observer, target, SoldierKind.Cavalry);
            p.Exact = heroOutcome.Exact;
            // 已入场指挥者总数＝棋盘上出现的敌方指挥者（含已倒地），不是"仍存活"。
            p.CommandersTotal = p.Exact ? battle.Soldiers.Count(s => s.Side == target && s.Kind == SoldierKind.Hero) : -1;
            // 指挥者"失能"＝已确认倒地；精确时等于全部非存活，情报不足时只算已确认目击的倒地数。
            p.CommandersDown = heroOutcome.Defeated;
            p.TroopsLost = troopMelee.Defeated + troopRanged.Defeated + troopCavalry.Defeated;
            p.TroopsDeployed = p.Exact ? battle.EnemyTroopsDeployed : -1;
            p.ExactDeployed = p.Exact ? battle.EnemyTroopsDeployed : -1;
            // 全部已入场指挥者失能：精确时按真实全部指挥者判断；情报不足时不得提前勾选。
            p.AllCommandersDown = heroOutcome.Exact
                ? battle.Soldiers.Any(s => s.Side == target && s.Kind == SoldierKind.Hero) && !battle.Soldiers.Any(s => s.Side == target && s.Kind == SoldierKind.Hero && s.IsAlive)
                : false;
            // 主要歼灭条件：指挥者全倒 + 普通兵累计阵亡严格过半（0 普通兵时该子条件无需达成）。
            bool troopSatisfied = p.Exact
                ? (battle.EnemyTroopsDeployed == 0 || battle.EnemyTroopsLost * 2 > battle.EnemyTroopsDeployed)
                : false;
            p.Satisfied = p.AllCommandersDown && troopSatisfied;
            p.Failed = battle.Phase == BattlePhase.Finished && battle.Report != null && battle.Report.Outcome != BattleOutcome.SideAWin;
            return p;
        }

        // 顶部UI用的一行／两行文案；Exact 为 false 时不承诺百分比。
        public static string Line1(ObjectiveProgress p)
        {
            string title = string.IsNullOrWhiteSpace(p.Title) ? KindDisplayName(p.Kind) : p.Title.Trim();
            if (title.Length > 10) title = title.Substring(0, 9) + "…";
            string head = title + " · 我方任务";
            return head;
        }

        public static string Line2(ObjectiveProgress p)
        {
            if (p.Failed) return "目标未达成";
            if (p.Declared) return "已宣布胜利";
            if (p.Kind == ObjectiveKinds.Hold) return "坚守 " + p.HoldRound + "／" + p.HoldTarget + " 轮 · 或提前歼灭";
            if (p.Preparing) return "指挥者全倒 · 小兵阵亡过半";
            string commanders = p.Exact && p.CommandersTotal >= 0 ? "倒地 " + p.CommandersDown + "／" + p.CommandersTotal : "确认倒地 " + p.CommandersDown;
            if (!p.Exact)
            {
                // 情报不足：只报已确认的失能与阵亡数，不画假百分比。
                return commanders + " · 阵亡 " + p.TroopsLost + " · 总量未知";
            }
            string troops = p.ExactDeployed > 0 ? "阵亡 " + p.TroopsLost + "／" + p.ExactDeployed : "无需杀兵";
            return commanders + " · " + troops;
        }

        // 精确战斗中显示完成度；未知、准备或已公开结果时返回 -1（不画）。
        public static float Progress01(ObjectiveProgress p)
        {
            if (p.Declared || p.Failed || p.Preparing) return -1;
            if (p.Kind == ObjectiveKinds.Hold) return Math.Max(0f, Math.Min(1f, p.HoldRound / (float)Math.Max(1,p.HoldTarget)));
            if (!p.Exact) return -1;
            float commanderPart = p.CommandersTotal > 0 ? (float)p.CommandersDown / p.CommandersTotal : 0f;
            // 严格过半需要 floor(累计部署/2)+1 人；这是目标完成度，并非阵亡率。
            float troopPart = p.ExactDeployed > 0 ? (float)p.TroopsLost / (p.ExactDeployed / 2 + 1) : 1f;
            return Math.Max(0f, Math.Min(1f, Math.Min(commanderPart, troopPart)));
        }
    }
}

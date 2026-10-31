using System;
using System.Collections.Generic;
using System.Linq;
using BaseConquest.Battle;

namespace BaseConquest.Base
{
    // 战力系统（Demo 用简单闭式，不做逐帧推演）
    //
    //   战力 = √（有效生命 × 每秒输出）
    //
    // 两条关键修正（相对旧实现 BaseRpg.Power）：
    //   ① 每秒输出按「当前对手」的防御、等级、闪避计算，不再固定拿参照兵当靶子——
    //      兵种相克（远程／骑兵／近战）因此能体现出来。
    //   ② 接敌窗口：射程远的一方在对手能还手之前先打一段，直接从对手有效生命里扣。
    //
    // 胜负：战力大者胜（与损耗指数无关的解析结论）。
    // 剩余血量比：1 −（弱方战力／强方战力）²。
    // 系数与验证：Logs/power-calibration/ 与 .workbuddy/工具/power_fit.py。
    // 真值对照（136 场，多种子平均）：胜负准确率 78.7%（旧 79.4%），
    // 剩余血量预测 RMSE 0.179（旧 0.314，误差降约四成）。
    public sealed class BaseEngagement
    {
        public string Problem = "";
        public double AllyPower, EnemyPower;          // √(有效生命 × 每秒输出)
        public double Advantage => EnemyPower > 0 ? AllyPower / EnemyPower : 0;
        public bool AllyWins;
        public double AllySurvival, EnemySurvival;    // 剩余血量比（0~1）
        public double Seconds;                        // 预计耗时
        public bool TimedOut;                         // 僵持超时：不能当作「稳赢」
        public int AllyCount, EnemyCount;
        public double AllyHealth, EnemyHealth;        // 有效生命（含易伤与治疗折算）
    }

    public static class BasePower
    {
        public const double MaxSeconds = 300;         // 与真实战斗 MaxRounds 对齐的僵持上限
        private const int Subdivision = 2;           // PrototypeArena.Subdivision：1 格 = 2 细格

        // 单兵「内在战力」：对手无关，用于养成展示。保留旧形态（√(生命×输出÷承伤)），
        // 补齐攻速与暴伤对称两处遗漏。
        public static double Rating(BattleSoldier soldier, RpgRules rules)
        {
            if (soldier?.Stats == null || rules == null) return 0;
            var stats = soldier.Stats;
            int profession = Profession(soldier);
            var reference = rules.Templates.FirstOrDefault(t => t.Id == "soldier_warrior");
            if (reference == null) return 0;
            double outgoing = 0, incoming = 0;
            for (int die = 1; die <= 10; die++)
            {
                var attack = new RpgAttackRoll((int)rules.Hit[profession], (int)rules.Evasion[0], 0, 0, die);
                var defense = new RpgAttackRoll((int)rules.Hit[0], (int)rules.Evasion[profession], 0, 0, die);
                if (attack.Hit) outgoing += RpgCombatRules.Damage(stats.Damage, reference.Defense,
                    rules.ArmorK * stats.Level, rules.ArmorCap, attack.Critical ? stats.CritMultiplier : 1) / 10;
                // 参照兵打我时按同一条暴击规则取暴伤（旧公式这里只用规则默认值，装备暴伤只算给己方）
                if (defense.Hit) incoming += RpgCombatRules.Damage(reference.Attack, stats.Defense,
                    rules.ArmorK, rules.ArmorCap, defense.Critical ? Math.Max(1, stats.CritMultiplier) : 1) / 10;
            }
            double interval = AttackInterval(soldier, rules);
            double received = 1 + ((soldier.Rpg?.Trait("fragile") ?? 0) - (soldier.Rpg?.Trait("tough") ?? 0)) / 100;
            return Math.Sqrt(Math.Max(0, stats.Health) * (outgoing / interval)
                / (reference.Health * Math.Max(1e-6, incoming * received / interval)));
        }

        public static BaseEngagement Engage(IEnumerable<BattleSoldier> allies, IEnumerable<BattleSoldier> enemies,
            RpgRules rules, double holdSeconds = 0)
        {
            var result = new BaseEngagement();
            var a = allies?.Where(u => u != null && u.IsAlive).ToList() ?? new List<BattleSoldier>();
            var b = enemies?.Where(u => u != null && u.IsAlive).ToList() ?? new List<BattleSoldier>();
            result.AllyCount = a.Count; result.EnemyCount = b.Count;
            if (a.Count == 0 || b.Count == 0) { result.Problem = "双方至少需要一名可战单位"; return result; }
            if (rules == null) { result.Problem = "缺少RPG战力规则"; return result; }

            result.AllyHealth = a.Sum(u => EffectiveHealth(u, rules) + Healing(u, rules));
            result.EnemyHealth = b.Sum(u => EffectiveHealth(u, rules) + Healing(u, rules));
            double dpsA = Output(a, b, rules), dpsB = Output(b, a, rules);
            double reachA = Average(a, u => rules.Range[Profession(u)] / (double)Subdivision);
            double reachB = Average(b, u => rules.Range[Profession(u)] / (double)Subdivision);
            double speedA = Average(a, u => rules.Speed[Profession(u)] / (double)Subdivision);
            double speedB = Average(b, u => rules.Speed[Profession(u)] / (double)Subdivision);

            // 接敌窗口：射程远的一方在对手进入射程前独占输出。
            double closing = Math.Max(1e-6, speedA + speedB);
            double freeA = reachA > reachB ? dpsA * (reachA - reachB) / closing : 0;
            double freeB = reachB > reachA ? dpsB * (reachB - reachA) / closing : 0;
            double liveA = Math.Max(1e-9, result.AllyHealth - freeB);
            double liveB = Math.Max(1e-9, result.EnemyHealth - freeA);

            result.AllyPower = Math.Sqrt(liveA * dpsA);
            result.EnemyPower = Math.Sqrt(liveB * dpsB);
            double surviveA = liveA / Math.Max(1e-9, dpsB);   // A 能撑住的秒数
            double killB = liveB / Math.Max(1e-9, dpsA);      // A 消灭 B 需要的秒数
            // 坚守目标：只要撑过规定秒数就算达成，不要求歼灭。
            result.AllyWins = holdSeconds > 0 ? surviveA >= holdSeconds : result.AllyPower > result.EnemyPower;
            result.Seconds = holdSeconds > 0 ? holdSeconds : Math.Min(killB, surviveA);
            result.TimedOut = result.Seconds > MaxSeconds;
            double ratio = result.AllyWins
                ? Math.Min(1, (result.EnemyPower / result.AllyPower) * (result.EnemyPower / result.AllyPower))
                : Math.Min(1, (result.AllyPower / result.EnemyPower) * (result.AllyPower / result.EnemyPower));
            if (result.AllyWins)
            {
                result.AllySurvival = Math.Max(0, 1 - ratio);
                result.EnemySurvival = 0;
            }
            else
            {
                result.AllySurvival = 0;
                result.EnemySurvival = Math.Max(0, 1 - ratio);
            }
            return result;
        }

        // 一方打另一方的每秒期望伤害：按对手血量加权，用对手实际的防御、等级与闪避。
        public static double Output(List<BattleSoldier> attackers, List<BattleSoldier> defenders, RpgRules rules)
        {
            if (attackers.Count == 0 || defenders.Count == 0) return 0;
            double weight = defenders.Sum(d => Math.Max(0, d.Health));
            if (weight <= 0) weight = defenders.Count;
            double total = 0;
            foreach (var attacker in attackers)
            {
                double perSecond = 0;
                foreach (var target in defenders)
                {
                    double share = Math.Max(0, target.Health) / weight;
                    double sum = 0;
                    for (int die = 1; die <= 10; die++)
                    {
                        var roll = new RpgAttackRoll((int)rules.Hit[Profession(attacker)],
                            (int)rules.Evasion[Profession(target)], 0, 0, die);
                        if (roll.Hit) sum += RpgCombatRules.Damage(attacker.Stats.Damage, target.Stats.Defense,
                            rules.ArmorK * attacker.Stats.Level, rules.ArmorCap,
                            roll.Critical ? Math.Max(1, attacker.Stats.CritMultiplier) : 1) / 10;
                    }
                    perSecond += share * sum;
                }
                total += perSecond / AttackInterval(attacker, rules) * SkillBoost(attacker, rules);
            }
            return total;
        }

        private static double AttackInterval(BattleSoldier soldier, RpgRules rules)
        {
            double speed = (soldier.Rpg?.Trait("frenzy") ?? 0) / 100
                + (Skill(soldier, rules)?.Id == "rapid_fire" ? rules.Skills.First(s => s.Id == "rapid_fire").Amount : 0);
            return RpgCombatRules.Interval(rules.BaseInterval, speed, rules.MinimumInterval);
        }

        private static double SkillBoost(BattleSoldier soldier, RpgRules rules)
        {
            var skill = Skill(soldier, rules);
            if (skill == null) return 1;
            if (skill.Id == "inspire") return 1 + skill.Amount * Duty(skill);
            if (skill.Id == "sweep") return 1 + (skill.Amount - 1) / Math.Max(1, skill.Cooldown);
            return 1;
        }

        private static double Healing(BattleSoldier soldier, RpgRules rules)
        {
            var skill = Skill(soldier, rules);
            return skill != null && skill.Id == "heal" ? skill.Amount * skill.Duration / Math.Max(1, skill.Cooldown) : 0;
        }

        private static double EffectiveHealth(BattleSoldier soldier, RpgRules rules)
        {
            double received = 1 + ((soldier.Rpg?.Trait("fragile") ?? 0) - (soldier.Rpg?.Trait("tough") ?? 0)) / 100;
            return Math.Max(0, soldier.Health) * Math.Max(.05, received);
        }

        private static double Duty(RpgSkillDefinition skill) =>
            skill.Cooldown > 0 ? Math.Min(1, skill.Duration / skill.Cooldown) : 1;

        private static RpgSkillDefinition Skill(BattleSoldier soldier, RpgRules rules) =>
            soldier.Rpg == null || string.IsNullOrEmpty(soldier.Rpg.SkillId)
                ? null : rules.Skills.FirstOrDefault(s => s.Id == soldier.Rpg.SkillId);

        private static int Profession(BattleSoldier soldier)
        {
            var stats = soldier.Stats;
            return BaseRpg.Profession(stats.HasProfessionKind ? stats.ProfessionKind
                : stats.HasCombatKind ? stats.CombatKind : SoldierKind.Melee);
        }

        private static double Average(List<BattleSoldier> units, Func<BattleSoldier, double> pick) =>
            units.Count == 0 ? 0 : units.Average(pick);
    }
}

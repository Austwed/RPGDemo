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
    // 四职业系数用 PowerCalibrationMatrix 的训练种子拟合，留出种子另行验证。
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

        // 平地训练的 48 场镜像对战共记录 9471 次弓兵出手。分母是对剑、弓、骑、法各自的射击次数。
        private static readonly double[] ArcherMoving = {0, 0, 253.0/3434, 0};
        private static readonly double[] ArcherPressed = {9.0/2618, 0, 193.0/3434, 0};
        private static readonly double[] ArcherBoth = {0, 0, 10.0/3434, 0};

        // 单兵「内在战力」：对手无关，用于养成展示。保留旧形态（√(生命×输出÷承伤)），
        // 补齐攻速与暴伤对称两处遗漏。
        public static double Rating(BattleSoldier soldier, RpgRules rules) => Rating(soldier, rules, null);

        public static double FormationRating(BattleSoldier[] soldiers, RpgRules rules)
        {
            var inspiration = SquadInspiration(soldiers, rules);
            return soldiers.Sum(unit => Rating(unit, rules, inspiration.GetValueOrDefault(unit.OriginCommanderSlot))
                * Math.Sqrt(unit.Health / unit.Stats.Health));
        }

        private static Dictionary<int,RpgSkillDefinition> SquadInspiration(IEnumerable<BattleSoldier> units, RpgRules rules)
        {
            var squads = new Dictionary<int,RpgSkillDefinition>();
            foreach (var leader in units.Where(unit => unit.IsAlive && unit.Kind == SoldierKind.Hero && unit.OriginCommanderSlot >= 0))
            {
                var skill = Skill(leader, rules);
                if (skill?.Id == "inspire") squads[leader.OriginCommanderSlot] = skill;
            }
            return squads;
        }

        private static double Rating(BattleSoldier soldier, RpgRules rules, RpgSkillDefinition inspiration)
        {
            if (soldier?.Stats == null || rules == null) return 0;
            var stats = soldier.Stats;
            int profession = Profession(soldier);
            var reference = rules.Templates.FirstOrDefault(t => t.Id == rules.DefaultTemplateId("soldier",0));
            if (reference == null) return 0;
            var referenceUnit = new BattleSoldier
            {
                Kind = SoldierKind.Melee,
                Stats = new SoldierStats
                {
                    Level = 1, Damage = (float)reference.Attack, Defense = (float)reference.Defense,
                    Health = (float)reference.Health, CritMultiplier = (float)rules.CriticalMultiplier
                },
                Health = (float)reference.Health
            };
            double outgoing = ExpectedDps(soldier, referenceUnit, rules, 1, true, inspiration: inspiration);
            double incoming = ExpectedDps(referenceUnit, soldier, rules, 1, !rules.IsRanged(profession));
            return Math.Sqrt(Math.Max(0, stats.Health) * outgoing / (reference.Health * Math.Max(1e-6, incoming)));
        }

        public static BaseEngagement Engage(IEnumerable<BattleSoldier> allies, IEnumerable<BattleSoldier> enemies,
            RpgRules rules, double holdSeconds = 0, string difficulty = "normal")
        {
            var result = new BaseEngagement();
            var a = allies?.Where(u => u != null && u.IsAlive).ToList() ?? new List<BattleSoldier>();
            var b = enemies?.Where(u => u != null && u.IsAlive).ToList() ?? new List<BattleSoldier>();
            result.AllyCount = a.Count; result.EnemyCount = b.Count;
            if (a.Count == 0 || b.Count == 0) { result.Problem = "双方至少需要一名可战单位"; return result; }
            if (rules == null) { result.Problem = "缺少RPG战力规则"; return result; }

            result.AllyHealth = a.Sum(u => EffectiveHealth(u, rules) + Healing(u, rules));
            result.EnemyHealth = b.Sum(u => EffectiveHealth(u, rules) + Healing(u, rules));
            double dpsA = Output(a, b, rules,difficulty=="easy"?2:1), dpsB = Output(b, a, rules,difficulty=="easy"?.5:difficulty=="hard"?2:1);
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
            if (holdSeconds == 0 && rules.HasProfessionCatalog)
            {
                // 连续输出只计交火。用 264 场训练对局折算接敌、换目标和残局耗时，参战规模上限取训练样本的 16 人。
                int engaged = Math.Min(16, Math.Min(a.Count, b.Count));
                result.Seconds = .62 + 1.69 * result.Seconds + 1.16 * engaged;
            }
            result.TimedOut = result.Seconds > MaxSeconds;
            if (holdSeconds > 0)
            {
                // 坚守分支按时间线性结算：双方 DPS 恒定，撑过 N 秒时的剩余血量就是「还能撑的秒数减去已用掉的秒数」。
                // 用战力平方比会把「撑住 3 倍时间」算成「接近全灭」，自动迎战因此出现判胜却全军覆没。
                result.AllySurvival = result.AllyWins ? Math.Max(0, Math.Min(1, (liveA - dpsB * holdSeconds) / Math.Max(1e-9, liveA))) : 0;
                result.EnemySurvival = Math.Max(0, Math.Min(1, (liveB - dpsA * holdSeconds) / Math.Max(1e-9, liveB)));
                return result;
            }
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
        public static double Output(List<BattleSoldier> attackers, List<BattleSoldier> defenders, RpgRules rules,double difficulty=1)
        {
            if (attackers.Count == 0 || defenders.Count == 0) return 0;
            double weight = defenders.Sum(d => Math.Max(0, d.Health));
            if (weight <= 0) weight = defenders.Count;
            double total = 0;
            int meleeTargets = defenders.Count(unit => !rules.IsRanged(BaseRpg.Profession(unit.CombatKind)));
            int frontline = attackers.Count(unit => !rules.IsRanged(BaseRpg.Profession(unit.CombatKind)));
            var inspiration = SquadInspiration(attackers, rules);
            foreach (var attacker in attackers)
            {
                double perSecond = 0;
                var skill = Skill(attacker, rules);
                var squadBuff = inspiration.GetValueOrDefault(attacker.OriginCommanderSlot);
                // 半径四细格的横扫，训练局中打中集群约六人，五名前排时约四人。多个近战分摊同一批前排。
                double swept = skill?.Id == "sweep" ? Math.Max(1, Math.Min(defenders.Count, Math.Min(
                    6 * Math.Pow(skill.Radius / 4, 2), .8 * meleeTargets / Math.Max(1, frontline)))) : 1;
                foreach (var target in defenders)
                {
                    double share = Math.Max(0, target.Health) / weight;
                    if (rules.HasProfessionCatalog)
                    {
                        perSecond += share * ExpectedDps(attacker,target,rules,difficulty,meleeTargets > 0,swept,squadBuff);
                        continue;
                    }
                    double sum = 0;
                    for (int die = 1; die <= 10; die++)
                    {
                        var roll = new RpgAttackRoll((int)rules.Hit[Profession(attacker)],
                            (int)rules.Evasion[Profession(target)], 0, 0, die);
                        if (roll.Hit) sum += RpgCombatRules.Damage(rules.AttackDamageType(Profession(attacker)),attacker.Stats.Damage, target.Stats.Defense,
                            rules.ArmorK * attacker.Stats.Level, rules.ArmorCap,
                            roll.Critical ? Math.Max(1, attacker.Stats.CritMultiplier) : 1,
                            1) / 10;
                    }
                    perSecond += share * sum;
                }
                total += rules.HasProfessionCatalog?perSecond:perSecond / AttackInterval(attacker, rules) * SkillBoost(attacker, rules);
            }
            return total;
        }

        private static double ExpectedDps(BattleSoldier attacker,BattleSoldier target,RpgRules rules,double difficulty,bool meleePresent,double sweepTargets=1,RpgSkillDefinition inspiration=null)
        {
            if(target.DeveloperInvulnerable)return 0;
            int profession=Profession(attacker), enemy=Profession(target);
            double raw=attacker.Stats.Damage*attacker.DrinkMultiplier;
            var skill=Skill(attacker,rules);
            string normalType=rules.AttackDamageType(profession);
            double received=Received(target);
            bool ranged=rules.IsRanged(BaseRpg.Profession(attacker.CombatKind));
            int baseEdge=(int)(attacker.Rpg?.Trait("advantage")??0)-(int)(attacker.Rpg?.Trait("disadvantage")??0);
            if(!ranged && rules.IsRanged(BaseRpg.Profession(target.CombatKind))) baseEdge++;
            bool archer=ranged && attacker.ProfessionKind==SoldierKind.Ranged;
            double pressure = meleePresent ? ArcherPressed[enemy] : 0;
            double overlap = meleePresent ? ArcherBoth[enemy] : 0;
            double once = ArcherMoving[enemy] + pressure - 2*overlap;
            double Expected(string damageType,double power)
            {
                double sum=0;
                for(int die=1;die<=10;die++)
                {
                    var roll=new RpgAttackRoll((int)rules.Hit[profession],(int)rules.Evasion[enemy],0,0,die);
                    if(!roll.Hit) continue;
                    double probability=SelectedProbability(die,baseEdge);
                    if(archer && damageType!="magic")
                        probability=(1-once-overlap)*probability + once*SelectedProbability(die,baseEdge-1)
                            + overlap*SelectedProbability(die,baseEdge-2);
                    sum+=probability*RpgCombatRules.Damage(damageType,power,target.Stats.Defense,rules.ArmorK*attacker.Stats.Level,
                        rules.ArmorCap,roll.Critical?Math.Max(1,attacker.Stats.CritMultiplier):1,received,difficulty);
                }
                return sum;
            }
            double normal=Expected(normalType,raw),rate=1/AttackInterval(attacker,rules);
            if (inspiration == null && skill?.Id == "inspire") inspiration = skill;
            if (inspiration != null)
            {
                double uptime = Duty(inspiration);
                normal = normal * (1 - uptime) + Expected(normalType,raw * (1 + inspiration.Amount)) * uptime;
            }
            if(skill==null)return normal*rate;
            if(skill.Id=="sweep")return normal*rate+sweepTargets*Expected(skill.DamageType,raw*skill.Amount)/skill.Cooldown;
            if(skill.Id=="charge")
            {
                double chargedRate=Math.Min(rate,1/skill.Cooldown);
                return normal*(rate-chargedRate)+Expected(skill.DamageType,raw*skill.Amount)*chargedRate;
            }
            return normal*rate;
        }

        // P(max(d1,d2)=face) or P(min(d1,d2)=face), for two independent D10.
        private static double SelectedProbability(int face,int edge) =>
            edge > 0 ? (2*face-1)/100.0 : edge < 0 ? (21-2*face)/100.0 : .1;

        private static double AttackInterval(BattleSoldier soldier, RpgRules rules)
        {
            var skill = Skill(soldier, rules);
            double speed = (soldier.Rpg?.Trait("frenzy") ?? 0) / 100;
            if (skill?.Id == "rapid_fire")
                speed += skill.Amount * (rules.HasProfessionCatalog ? Duty(skill) : 1);
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
            return Math.Max(0, soldier.Health) * (rules.HasProfessionCatalog?1:Math.Max(.05,Received(soldier)));
        }

        private static double Received(BattleSoldier soldier) =>
            1+((soldier.Rpg?.Trait("fragile")??0)-(soldier.Rpg?.Trait("tough")??0))/100;

        private static double Duty(RpgSkillDefinition skill) =>
            skill.Cooldown > 0 ? Math.Min(1, skill.Duration / skill.Cooldown) : 1;

        private static RpgSkillDefinition Skill(BattleSoldier soldier, RpgRules rules) =>
            soldier.Rpg == null || string.IsNullOrEmpty(soldier.Rpg.SkillId)
                ? null : rules.Skills.FirstOrDefault(s => s.Id == soldier.Rpg.SkillId);

        private static int Profession(BattleSoldier soldier)
        {
            return BaseRpg.Profession(soldier.ProfessionKind);
        }

        private static double Average(List<BattleSoldier> units, Func<BattleSoldier, double> pick) =>
            units.Count == 0 ? 0 : units.Average(pick);
    }
}

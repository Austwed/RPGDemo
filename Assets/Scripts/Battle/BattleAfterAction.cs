using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
namespace BaseConquest.Battle
{
    public sealed class TroopBattleReport
    {
        public SoldierKind Kind;
        public int Deployed, Standing, Killed, Escaped, Stock;
        public int InstantKilled;
    }
    public sealed class CommanderBattleReport
    {
        public int Slot;
        public string Name, Status;
        public float Health, MaxHealth, Damage, Healing;
    }
    public sealed class ArmyBattleReport
    {
        public BattleSide Side;
        public int Deployed, Standing, Killed, Escaped, Stock;
        public int CommandersStanding, CommandersDowned, CommandersRescued, CommandersEscaped, CommandersStock;
        public float Damage, Healing;
        public readonly List<TroopBattleReport> Troops = new List<TroopBattleReport>();
        public readonly List<CommanderBattleReport> Commanders = new List<CommanderBattleReport>();
    }
    public sealed partial class BattleSimulation
    {
        private void RefreshArmyReports()
        {
            Report.Armies.Clear();
            foreach (BattleSide side in Enum.GetValues(typeof(BattleSide)))
            {
                var row = new ArmyBattleReport { Side = side, CommandersStock = Math.Max(0, Scenario.Army(side).HeroCount - Soldiers.Count(s => s.Side == side && s.Kind == SoldierKind.Hero)) };
                var units = Soldiers.Where(s => s.Side == side && !s.DeveloperSpawned).ToList();
                foreach (var kind in TroopKinds)
                {
                    var troop = units.Where(s => s.Kind == kind).ToList();
                    var t = new TroopBattleReport { Kind = kind, Deployed = troop.Count, Standing = troop.Count(s => s.IsActive), Killed = troop.Count(s => !s.IsAlive), Escaped = troop.Count(s => s.IsAlive && s.Escaped), Stock = Math.Max(0, Scenario.Army(side).Count(kind) - troop.Count) };
                    t.InstantKilled=troop.Count(s=>s.InstantDead); row.Troops.Add(t); row.Deployed += t.Deployed; row.Standing += t.Standing; row.Killed += t.Killed; row.Escaped += t.Escaped; row.Stock += t.Stock;
                }
                foreach (var s in units.Where(s => s.Kind == SoldierKind.Hero).OrderBy(s => s.OriginCommanderSlot))
                {
                    string state;
                    if (s.InstantDead) { row.CommandersDowned++; state="即死"; }
                    else if (s.Rescued) { row.CommandersRescued++; state = "已救出"; }
                    else if (s.IsAlive && s.Escaped) { row.CommandersEscaped++; state = "健康撤离"; }
                    else if (!s.IsAlive) { row.CommandersDowned++; state = s.CarriedById >= 0 ? "携带中·未救出" : "倒地·未救出"; }
                    else { row.CommandersStanding++; state = "存活留场"; }
                    row.Commanders.Add(new CommanderBattleReport { Slot = s.OriginCommanderSlot, Name = UnitName(s), Status = state, Health = s.Health, MaxHealth = s.Stats.Health, Damage = s.DamageDealt, Healing = s.HealingDone });
                }
                row.Damage = units.Sum(s => s.DamageDealt); row.Healing = units.Sum(s => s.HealingDone);
                Report.Armies.Add(row);
            }
        }
    }
    public static class BattleReportFormatting
    {
        public static string Format(BattleReport report, BattleSide observer)
        {
            if (report == null) return "尚未结算";
            var text = new StringBuilder();
            text.AppendLine("回合 " + report.Rounds + " · 指令 " + report.CommandsIssued + " · 技能 " + report.AbilitiesUsed);
            text.AppendLine("普通兵按实际部署统计；留场、倒地、撤离互不重复，即死是倒地中的永久损失，库存另列。\n");
            foreach (var a in report.Armies)
            {
                var color = a.Side == observer ? "#5CB9FF" : "#FFAA62";
                text.AppendLine("<color=" + color + "><b>" + (a.Side == observer ? "我方" : "敌方") + "</b></color>");
                text.AppendLine("普通兵：部署 " + a.Deployed + "，留场 " + a.Standing + "，倒地 " + a.Killed + "（其中即死 "+a.Troops.Sum(t=>t.InstantKilled)+"），撤离 " + a.Escaped + "，未部署 " + a.Stock);
                text.AppendLine("兵种                 部署 / 留场 / 倒地 / 撤离 / 未部署");
                foreach (var t in a.Troops) text.AppendLine(BattleSimulation.KindName(t.Kind) + "                  " + t.Deployed + " / " + t.Standing + " / " + t.Killed + " / " + t.Escaped + " / " + t.Stock);
                text.AppendLine("指挥者：留场 " + a.CommandersStanding + "，倒地未救 " + a.CommandersDowned + "，救出 " + a.CommandersRescued + "，健康撤离 " + a.CommandersEscaped + "，未部署 " + a.CommandersStock);
                foreach (var h in a.Commanders) text.AppendLine("<color=" + color + ">" + h.Name + "</color> · " + h.Status + " · 生命 " + h.Health.ToString("0.#") + "/" + h.MaxHealth.ToString("0.#") + " · 伤害 " + h.Damage.ToString("0.#") + " · 治疗 " + h.Healing.ToString("0.#"));
                text.AppendLine("实际造成伤害 " + a.Damage.ToString("0.#") + " · 实际恢复生命 " + a.Healing.ToString("0.#") + "\n");
            }
            text.AppendLine("直接结算保留活着的残军，不算额外击杀；救出不等于复活。");
            return text.ToString();
        }
    }
}

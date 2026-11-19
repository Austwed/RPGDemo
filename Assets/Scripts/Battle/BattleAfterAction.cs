using System;
using System.Collections.Generic;
using System.Linq;
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
        public bool Recruited;
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
                    if (s.InstantDead) { row.CommandersDowned++; state="阵亡"; }
                    else if (s.Rescued) { row.CommandersRescued++; state = "已救出"; }
                    else if (s.IsAlive && s.Escaped) { row.CommandersEscaped++; state = "健康撤离"; }
                    else if (!s.IsAlive) { row.CommandersDowned++; state = s.CarriedById >= 0 ? "携带中·未救出" : "倒地·未救出"; }
                    else { row.CommandersStanding++; state = "存活留场"; }
                    row.Commanders.Add(new CommanderBattleReport { Slot = s.OriginCommanderSlot, Name = RawCommanderName(side,s.OriginCommanderSlot),
                        Recruited = Scenario.Army(side).CommanderRecruited.ElementAtOrDefault(s.OriginCommanderSlot),
                        Status = state, Health = s.Health, MaxHealth = s.Stats.Health, Damage = s.DamageDealt, Healing = s.HealingDone });
                }
                row.Damage = units.Sum(s => s.DamageDealt); row.Healing = units.Sum(s => s.HealingDone);
                Report.Armies.Add(row);
            }
        }
    }
}

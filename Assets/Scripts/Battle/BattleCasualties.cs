using System;
using System.Linq;
namespace BaseConquest.Battle
{
    public sealed partial class BattleSimulation
    {
        public static double StableRoll(int seed, int identity, int salt)
        {
            unchecked { uint x = (uint)seed ^ ((uint)(identity + 1) * 0x85ebca6bu) ^ ((uint)salt * 0xc2b2ae35u); x ^= x >> 16; x *= 0x7feb352du; x ^= x >> 15; x *= 0x846ca68bu; x ^= x >> 16; return x / 4294967296.0; }
        }
        public Func<BattleSoldier,double> CasualtyChance;
        public void ResolveCasualties()
        {
            foreach (var unit in Soldiers.Where(s => !s.IsAlive && !s.DeathResolved))
            {
                unit.DeathResolved = true;
                unit.InstantDead = !unit.ProtectFromInstantDeath && (!Scenario.InstantDeathEnabled ? unit.Kind != SoldierKind.Hero : StableRoll(Scenario.RandomSeed, unit.Id, 11) < (CasualtyChance?.Invoke(unit) ?? (unit.Kind == SoldierKind.Hero ? Scenario.LeaderInstantDeath : Scenario.TroopInstantDeath)));
                if (CasualtyChance != null && !unit.InstantDead) { unit.KnockedDown = true; unit.Health = 1; }
            }
        }
    }
}

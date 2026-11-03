using System;
using System.Collections.Generic;
using System.Linq;

namespace BaseConquest.Battle
{
    [Serializable]
    public sealed class BattleEffectRules
    {
        public float RangedCloseMultiplier = 0.5f, WeakMultiplier = 0.7f, InspireMultiplier = 1.3f, HealPerRound = 30;
        public int WeakDuration = 1, InspireDuration = 3, MarchDuration = 2, HealDuration = 3, MarchSpeedBonus = 1, Cooldown = 15;
        public int[] OpeningCooldowns = { 3, 6, 9 };
        public static void Validate(BattleEffectRules r)
        {
            if (r == null || new[] { r.RangedCloseMultiplier, r.WeakMultiplier, r.InspireMultiplier, r.HealPerRound }.Any(v => float.IsNaN(v) || float.IsInfinity(v) || v < 0) || r.RangedCloseMultiplier > 10 || r.WeakMultiplier > 10 || r.InspireMultiplier > 10 || r.HealPerRound > 100000 || new[] { r.WeakDuration, r.InspireDuration, r.MarchDuration, r.HealDuration, r.Cooldown }.Any(v => v < 1 || v > 1000) || r.MarchSpeedBonus < 1 || r.MarchSpeedBonus > 12 || r.OpeningCooldowns == null || r.OpeningCooldowns.Length != 3 || r.OpeningCooldowns.Any(v => v < 0 || v > r.Cooldown)) throw new ArgumentException("战技／效果配置非法");
        }
        public BattleEffectRules Clone() { Validate(this); return new BattleEffectRules { RangedCloseMultiplier = RangedCloseMultiplier, WeakMultiplier = WeakMultiplier, InspireMultiplier = InspireMultiplier, HealPerRound = HealPerRound, WeakDuration = WeakDuration, InspireDuration = InspireDuration, MarchDuration = MarchDuration, HealDuration = HealDuration, MarchSpeedBonus = MarchSpeedBonus, Cooldown = Cooldown, OpeningCooldowns = (int[])OpeningCooldowns.Clone() }; }
    }
    public sealed partial class BattleSimulation
    {
        private int ActingRound => RoundNumber + 1;
        public bool IsWeak(BattleSoldier s) => s != null && Math.Max(s.WeakTerrainUntil, s.WeakCrowdUntil) >= ActingRound;
        public bool IsMarching(BattleSoldier s) => s != null && s.MarchUntil >= ActingRound;
        private readonly HashSet<int> pinnedCavalry = new HashSet<int>();
        private bool WarriorNear(BattleSoldier s) => Soldiers.Any(e => e.IsActive && e.Side != s.Side && e.ProfessionKind == SoldierKind.Melee && Vision.CanSee(s.Side, e) && GridPosition.Distance(s.Cell, e.Cell) <= 1);
        public bool CavalryPinned(BattleSoldier s) => ModernRules && s?.IsActive == true && s.ProfessionKind == SoldierKind.Cavalry && (resolvingRound ? pinnedCavalry.Contains(s.Id) : WarriorNear(s));
        private void SnapshotCavalryPins()
        {
            pinnedCavalry.Clear();
            foreach (var s in Soldiers) if (CavalryPinned(s)) pinnedCavalry.Add(s.Id);
        }
        public int MovementSpeed(BattleSoldier s) => CavalryPinned(s) ? 1 : Math.Max(1, s.Stats.MoveDistance + (IsMarching(s) ? Scenario.Effects.MarchSpeedBonus : 0));
        public bool ArcherInCloseCombat(BattleSoldier s) => s.CombatKind == SoldierKind.Ranged && Soldiers.Any(e => e.IsActive && e.Side != s.Side && Vision.CanSee(s.Side, e) && GridPosition.Distance(s.Cell, e.Cell) <= 1);
        public float AttackPower(BattleSoldier s) => !ModernRules ? s.Stats.Damage : s.Stats.Damage * s.DrinkMultiplier * (s.InspiredUntil >= ActingRound ? Scenario.Effects.InspireMultiplier : 1) * (IsWeak(s) ? Scenario.Effects.WeakMultiplier : 1) * (ArcherInCloseCombat(s) ? Scenario.Effects.RangedCloseMultiplier : 1);
        private float ExpectedDamageAgainst(BattleSoldier source, BattleSoldier target)
        {
            float attack = AttackPower(source), chance = IsWeak(source) ? 0 : source.Stats.CritChance;
            return CombatNumbers.Hit(source.Stats,target.Stats,attack*(1+chance*(source.Stats.CritMultiplier-1))) * IncomingAttackMultiplier(source, target);
        }
        public float IncomingAttackMultiplier(BattleSoldier source, BattleSoldier target) => (ModernRules && source.CombatKind == SoldierKind.Ranged && target.ProfessionKind == SoldierKind.Cavalry && target.MovedThisRound ? 0.5f : 1f) * (ModernRules && source.CombatKind==SoldierKind.Ranged && Board.Cell(target.Cell)?.FixedCover==true && Board.Walkable(target.Cell) ? 0.75f : 1f);
        public static string CommanderSkillName(int slot) => slot == 0 ? "鼓舞" : slot == 1 ? "行军" : "治愈";
        public string CommanderSkillDescription(int slot) => CommanderSkillName(slot) + "：" + (slot == 0 ? "本队攻击＋" + ((Scenario.Effects.InspireMultiplier - 1) * 100).ToString("0") + "%，" + Scenario.Effects.InspireDuration + "轮" : slot == 1 ? "本队速度＋" + Scenario.Effects.MarchSpeedBonus + "，免恶劣地形／解除地形虚弱，" + Scenario.Effects.MarchDuration + "轮" : "伤害后每轮恢复" + Scenario.Effects.HealPerRound.ToString("0") + "生命，" + Scenario.Effects.HealDuration + "轮") + "；CD" + Scenario.Effects.Cooldown;
        public int CommanderSkillCooldown(BattleSide side, int slot)
        {
            var q = CommanderSquad(side, slot); return q == null ? 0 : Phase == BattlePhase.Deployment ? Scenario.Effects.OpeningCooldowns[slot] : Math.Max(0, q.SkillReadyRound - RoundNumber);
        }
        public float CommanderSkillChargeFraction(BattleSide side, int slot, float progress = 1, bool animating = false)
        {
            var q = CommanderSquad(side, slot); if (q == null) return 0;
            int total = Phase == BattlePhase.Deployment ? Scenario.Effects.Cooldown : q.SkillChargeRounds;
            float remaining = CommanderSkillCooldown(side, slot);
            // 仅对本轮仍在恢复或刚好恢复的技能插值；此前已满充的技能保持100%。
            if (animating && q.LastSkillCastRound != RoundNumber && (remaining > 0 || q.SkillReadyRound == RoundNumber)) remaining += 1 - Math.Max(0, Math.Min(1, progress));
            return total <= 0 ? 1 : Math.Max(0, Math.Min(1, 1 - remaining / total));
        }
        public bool CommanderSkillQueued(BattleSide side, int slot) => CommanderSquad(side, slot)?.SkillQueued == true;
        public bool CanQueueCommanderSkill(BattleSide side, int slot)
        {
            var hero = Commander(side, slot); return ModernRules && Phase == BattlePhase.Fighting && hero?.IsActive == true && !IsExit(side, hero.Cell) && !hero.IsTransit && hero.Command != SquadCommand.Retreat && hero.RescueTargetId < 0 && hero.CarryingId < 0 && CommanderSkillCooldown(side, slot) == 0 && !CommanderSkillQueued(side, slot);
        }
        public bool QueueCommanderSkill(BattleSide side, int slot)
        {
            if (!CanQueueCommanderSkill(side, slot)) return false; CommanderSquad(side, slot).SkillQueued = true; CommandsIssued++; return true;
        }
        private void InitializeCommanderSkills()
        {
            foreach (var q in Squads) if (q.CommanderSlot >= 0) { q.SkillChargeRounds = Scenario.Effects.Cooldown; q.LastSkillCastRound = -1; q.SkillReadyRound = RoundNumber + Scenario.Effects.OpeningCooldowns[q.CommanderSlot]; q.SkillQueued = false; }
        }
        public bool CastCommanderSkill(BattleSide side, int slot, bool countCommand = true)
        {
            if (!CanQueueCommanderSkill(side, slot)) return false;
            var q = CommanderSquad(side, slot); var hero = Commander(side, slot);
            q.SkillReadyRound = RoundNumber + Scenario.Effects.Cooldown;
            q.SkillChargeRounds = Scenario.Effects.Cooldown; q.LastSkillCastRound = RoundNumber;
            AbilitiesUsed++; if (countCommand) CommandsIssued++;
            InvalidateUndo();
            var recipients = q.Soldiers.Where(s => s.IsActive).ToList();
            foreach (var unit in recipients)
            {
                if (slot == 0) { unit.InspiredUntil = Math.Max(unit.InspiredUntil, RoundNumber + Scenario.Effects.InspireDuration); unit.BuffGainSerial++; }
                else if (slot == 1) { unit.MarchUntil = Math.Max(unit.MarchUntil, RoundNumber + Scenario.Effects.MarchDuration); unit.WeakTerrainUntil = 0; unit.BuffGainSerial++; }
                else { unit.RegenUntil = Math.Max(unit.RegenUntil, RoundNumber + Scenario.Effects.HealDuration); unit.RegenSourceId = hero.Id; unit.HealGainSerial++; }
            }
            int duration = slot == 0 ? Scenario.Effects.InspireDuration : slot == 1 ? Scenario.Effects.MarchDuration : Scenario.Effects.HealDuration;
            Notice(UnitName(hero) + "施放" + CommanderSkillName(slot) + "，影响" + recipients.Count + "人（指挥者" + recipients.Count(s => s.Kind == SoldierKind.Hero) + "／小兵" + recipients.Count(s => s.Kind != SoldierKind.Hero) + "），持续" + duration + "回合（从下一轮起），CD" + Scenario.Effects.Cooldown, hero.Side, hero.Id);
            return true;
        }
        public int ResolveQueuedCommanderSkills()
        {
            int count = 0;
            foreach (var q in Squads.Where(q => q.SkillQueued).ToArray())
            {
                q.SkillQueued = false;
                if (CastCommanderSkill(q.Side, q.CommanderSlot, false)) count++;
                else { var hero = Soldier(q.CommanderId); Notice(UnitName(hero) + "的" + CommanderSkillName(q.CommanderSlot) + "施放失败：当前角色倒地或受控，无法施放", q.Side, hero?.Id ?? -1); }
            }
            return count;
        }
        public int AutoCastCommanderSkills()
        {
            if (!ModernRules || Phase != BattlePhase.Fighting) return 0;
            int count = 0;
            foreach (BattleSide side in Enum.GetValues(typeof(BattleSide)))
                for (int slot = 0; slot < Scenario.Army(side).HeroCount; slot++)
                    if (CastCommanderSkill(side, slot, false)) count++;
            return count;
        }
        private void RegisterMovementStrain(BattleSoldier s, GridPosition cell)
        {
            if (!ModernRules) return;
            // 同格额外脚力只用于寻路避让，不造成虚弱。
            int speed = MovementSpeed(s), terrain = IsMarching(s) ? 1 : Board.Cell(cell).FootworkCost;
            if (terrain <= speed) return;
            if (!IsWeak(s)) s.DebuffGainSerial++;
            int expiry = ActingRound + Scenario.Effects.WeakDuration;
            s.WeakTerrainUntil = Math.Max(s.WeakTerrainUntil, expiry);
        }
        private bool HealAfterDamage()
        {
            bool changed = false;
            foreach (var s in Soldiers.Where(s => s.IsActive && s.RegenUntil >= ActingRound && s.Health < s.Stats.Health))
            {
                float amount = Math.Min(Scenario.Effects.HealPerRound, s.Stats.Health - s.Health); if (amount <= 0) continue;
                float before = s.Health; s.Health += amount; changed = true; var source = Soldier(s.RegenSourceId);
                if (source != null) source.HealingDone += amount;
                Notice(UnitName(source) + "的治愈为" + UnitName(s) + "恢复" + amount.ToString("0.#") + "生命（" + before.ToString("0.#") + "→" + s.Health.ToString("0.#") + "），余血" + s.Health.ToString("0.#"), s.Side, s.Id, source?.Id ?? -1);
            }
            return changed;
        }
        private void ReportEffectExpirations()
        {
            if (!ModernRules) return;
            foreach (BattleSide side in Enum.GetValues(typeof(BattleSide)))
            {
                var units = Soldiers.Where(s => s.Side == side && s.IsActive).ToList();
                var counts = new[] { units.Count(s => s.InspiredUntil > 0 && s.InspiredUntil == RoundNumber), units.Count(s => s.MarchUntil > 0 && s.MarchUntil == RoundNumber), units.Count(s => s.RegenUntil > 0 && s.RegenUntil == RoundNumber), units.Count(s => Math.Max(s.WeakTerrainUntil, s.WeakCrowdUntil) > 0 && Math.Max(s.WeakTerrainUntil, s.WeakCrowdUntil) == RoundNumber) };
                var names = new[] { "鼓舞", "行军", "治愈", "虚弱" };
                for (int i = 0; i < counts.Length; i++) if (counts[i] > 0) Notice(SideName(side) + "的" + names[i] + "效果结束（" + counts[i] + "人）", side);
            }
        }
        public int EffectVisualMask(BattleSoldier s, int round = -1)
        {
            if (s?.IsActive != true) return 0; if (round < 0) round = ActingRound;
            return (s.InspiredUntil > 0 && s.InspiredUntil >= round || s.MarchUntil > 0 && s.MarchUntil >= round ? 1 : 0) | (s.RegenUntil > 0 && s.RegenUntil >= round ? 2 : 0) | (Math.Max(s.WeakTerrainUntil, s.WeakCrowdUntil) > 0 && Math.Max(s.WeakTerrainUntil, s.WeakCrowdUntil) >= round || ArcherInCloseCombat(s) || CavalryPinned(s) ? 4 : 0);
        }
        internal void RefreshProximityEffects()
        {
            if (!ModernRules) return;
            foreach (var s in Soldiers.Where(s => s.CombatKind == SoldierKind.Ranged)) { bool close = s.IsActive && ArcherInCloseCombat(s); if (close && !s.CloseSuppressed) s.DebuffGainSerial++; s.CloseSuppressed = close; }
        }
        public string EffectsSummary(BattleSoldier s)
        {
            var labels = new List<string>();
            if(s.DrinkMultiplier>1)labels.Add(s.DrinkLabel);
            if(s.HasProvisionEffects)labels.Add(ProvisionEffects.Summary(s.Kind,s.ProvisionAttackFactor,s.ProvisionWeaponBonus,s.ProvisionArmorBonus,true));
            if(s.ProfessionKind==SoldierKind.Cavalry&&s.MovedThisRound)labels.Add("机动（本轮远程伤害×0.5）");
            if(CavalryPinned(s))labels.Add("战士近身（移动1，轮初判定）");
            if(Board.Cell(s.Cell)?.FixedCover==true&&Board.Walkable(s.Cell))labels.Add("固定掩体（远程伤害×0.75）");
            if (IsWeak(s)) labels.Add("虚弱" + (Math.Max(s.WeakTerrainUntil, s.WeakCrowdUntil) - RoundNumber) + "轮（攻击×" + Scenario.Effects.WeakMultiplier.ToString("0.##") + "，禁暴击）");
            if (ArcherInCloseCombat(s)) labels.Add("贴身压制（攻击×" + Scenario.Effects.RangedCloseMultiplier.ToString("0.##") + "）");
            if (s.InspiredUntil >= ActingRound) labels.Add("鼓舞" + (s.InspiredUntil - RoundNumber) + "轮");
            if (IsMarching(s)) labels.Add("行军" + (s.MarchUntil - RoundNumber) + "轮");
            if (s.RegenUntil >= ActingRound) labels.Add("治愈" + (s.RegenUntil - RoundNumber) + "轮");
            return labels.Count == 0 ? "" : string.Join("／", labels);
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;

namespace BaseConquest.RealtimePrototype
{
    public sealed partial class RealtimeBattlePrototype
    {
        internal bool SwordContactPinEnabled;
        internal Func<PrototypeUnit, bool> SwordForContactPin;
        internal Func<bool> SwordContactCombatOpen;
        internal Func<PrototypeUnit, PrototypeUnit, bool> PursuitContactPin;
        private readonly HashSet<int> contactPins = new();
#if UNITY_EDITOR
        private readonly HashSet<int> lastHeldContactPins = new();
        private readonly Dictionary<int, int> contactPinSteps = new();
        private readonly Dictionary<int, int> contactPinStarts = new();
#endif

        private bool ContactsAllowed => SwordContactPinEnabled && !OnlyExitMovement &&
            Outcome == PrototypeOutcome.Fighting && (SwordContactCombatOpen?.Invoke() ?? true);

        public bool ContactPinActive(PrototypeUnit unit) =>
            unit?.Alive == true && ContactsAllowed && contactPins.Contains(unit.Id);

#if UNITY_EDITOR
        public double SwordContactPinnedSeconds(PrototypeUnit unit) =>
            contactPinSteps.GetValueOrDefault(unit.Id) * FixedSeconds;

        public int SwordContactPinStarts(PrototypeUnit unit) => contactPinStarts.GetValueOrDefault(unit.Id);
#endif

        internal void RefreshSwordContactPins()
        {
            contactPins.Clear();
            if (!ContactsAllowed) return;
            foreach (var sword in Units)
            {
                if (!sword.Alive || sword.Ranged || sword.Mounted ||
                    sword.ObjectOrder || sword.Order == PrototypeOrder.Retreat || sword.JumpDuration > 0 ||
                    sword.TargetId < 0) continue;

                var target = Units.FirstOrDefault(unit => unit.Id == sword.TargetId);
                if (target?.Alive != true || target.Side == sword.Side || target.JumpDuration > 0 || target.ObjectOrder) continue;
                bool soldierPin = sword.Role == PrototypeRole.Melee && target.Role != PrototypeRole.Leader &&
                    (SwordForContactPin?.Invoke(sword) ?? true);
                if ((soldierPin || PursuitContactPin?.Invoke(sword, target) == true) &&
                    (CanTarget?.Invoke(sword, target) ?? true) && ReadyTarget(sword, target))
                    contactPins.Add(target.Id);
            }
        }

#if UNITY_EDITOR
        private void SampleSwordContactHolds()
        {
            if (!ContactsAllowed)
            {
                lastHeldContactPins.Clear();
                return;
            }
            foreach (var target in Units)
            {
                if (!target.Alive || target.Moving || !contactPins.Contains(target.Id)) continue;
                contactPinSteps[target.Id] = contactPinSteps.GetValueOrDefault(target.Id) + 1;
                if (!lastHeldContactPins.Contains(target.Id))
                    contactPinStarts[target.Id] = contactPinStarts.GetValueOrDefault(target.Id) + 1;
            }
            lastHeldContactPins.Clear();
            foreach (var target in Units)
                if (target.Alive && !target.Moving && contactPins.Contains(target.Id))
                    lastHeldContactPins.Add(target.Id);
        }
#endif

        private bool DefendWhileContactPinned(PrototypeUnit unit)
        {
            if (!ContactPinActive(unit) || unit.Order is not (PrototypeOrder.Retreat or PrototypeOrder.Move))
                return false;

            // Keep the pending order, while still permitting a normal attack from the held position.
            var target = PreferredTarget(unit, Units.Where(enemy => enemy.Side != unit.Side &&
                (CanTarget?.Invoke(unit, enemy) ?? true) && ReadyTarget(unit, enemy)));
            unit.TargetId = target?.Id ?? -1;
            unit.AttackSlot = target == null ? -1 : unit.Cell;
            if (target != null && Face(unit, (target.BodyCenter - unit.BodyCenter).Normalized) &&
                Elapsed + 1e-9 >= unit.ReadyAt)
                BeginAttack(unit);
            return true;
        }
    }
}

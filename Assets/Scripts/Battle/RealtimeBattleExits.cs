using System.Linq;
using BaseConquest.Base;
using BaseConquest.RealtimePrototype;

namespace BaseConquest.Battle
{
    public sealed partial class RealtimeCampaignBattle
    {
        private bool battlefieldExits;
        public bool HasBattlefieldExits => battlefieldExits;
        public bool VictoryExitOpen { get; private set; }
        private readonly System.Collections.Generic.HashSet<int> victoryMovers = new();
        private bool noVictoryMover;
        private bool victoryExitReached;
        private bool SettleVictoryHere => Session.Opening.EventId == BaseDemoDefense.EventId ||
            BaseDemoFinale.Encounter(Session);
        private PrototypePoint AllyExitPoint
        {
            get
            {
                var exit = Battle.ExitCell(BattleSide.A);
                return new PrototypePoint(exit.X + .5, exit.Z + .5);
            }
        }
        private bool AtAllyExit(PrototypeUnit actor)
        {
            var exit = Battle.ExitCell(BattleSide.A);
            return (int)actor.Position.X == exit.X && (int)actor.Position.Z == exit.Z;
        }

        public void EnableBattlefieldExits()
        {
            string eventId = Session.Opening.EventId;
            battlefieldExits = Session.Opening.DemoCombat &&
                (eventId is BaseDemoRuins.EventId or BaseDemoDefense.EventId or BaseBanditHideout.EventId ||
                 BaseDemoFinale.Encounter(Session) && eventId != BaseDemoDefense.SecondEventId);
            if (battlefieldExits && Battle.Phase == BattlePhase.Fighting &&
                Session.Opening.HasRealtimeState && Session.Opening.RealtimeState.BattlefieldVictoryPending)
            {
                if (SettleVictoryHere) FinishRealtimeOutcome(BattleOutcome.SideAWin);
                else LockBattlefieldVictory(resume: true);
            }
        }

        public bool OrderBattlefieldExit(System.Collections.Generic.IReadOnlyCollection<int> selected)
        {
            if (!battlefieldExits || Battle.Phase != BattlePhase.Fighting || AwaitingRescueDialogue || DefensePursuitReady) return false;
            if (RuinsExitOpen) return OrderRuinsExit(AllyExitPoint, selected);
            var commanders = Model.Units
                .Where(unit => unit.Alive && unit.Side == PrototypeSide.Ally && unit.Role == PrototypeRole.Leader &&
                    (VictoryExitOpen ? victoryMovers.Contains(unit.Id) : Model.CanMove?.Invoke(unit) ?? true))
                .ToArray();
            if (VictoryExitOpen)
            {
                var captain = commanders.OrderByDescending(unit => selected?.Contains(unit.Id - 1) == true)
                    .ThenBy(unit => (unit.Position - AllyExitPoint).Length).FirstOrDefault();
                return captain != null && Issue(new[] { captain.Id - 1 }, PrototypeOrder.Retreat, AllyExitPoint);
            }
            var ids = commanders.Where(unit => selected == null || selected.Contains(unit.Id - 1))
                .Select(unit => unit.Id - 1).ToArray();
            return ids.Length > 0 && Issue(ids, PrototypeOrder.Retreat, AllyExitPoint);
        }

        private void TryExitOnMovement(PrototypeUnit actor)
        {
            if (!battlefieldExits || RuinsExitOpen || !actor.Alive || actor.Side != PrototypeSide.Ally || !AtAllyExit(actor)) return;
            if (VictoryExitOpen)
            {
                victoryExitReached |= victoryMovers.Contains(actor.Id) &&
                    (actor.Role == PrototypeRole.Leader || actor.Order == PrototypeOrder.Retreat);
                return;
            }
            if (actor.Role != PrototypeRole.Leader ||
                !actor.Steering && !(actor.ManualOrder && actor.Order == PrototypeOrder.Move) || DefensePursuitReady) return;
            Model.CancelWindup(actor);
            actor.Escaped = true;
        }

        private void PrepareBattlefieldExitStep()
        {
            if (!battlefieldExits || VictoryExitOpen || RuinsExitOpen) return;
            var allies = Model.Units.Where(unit => unit.Side == PrototypeSide.Ally).ToArray();
            foreach (var captain in allies) TryExitOnMovement(captain);
            foreach (var captain in allies.Where(unit => unit.Role == PrototypeRole.Leader && unit.Escaped))
                if (allies.Any(unit => unit.Squad == captain.Squad && unit.Alive && unit.Order != PrototypeOrder.Retreat))
                    Model.IssueOrder(captain.Squad, -1, PrototypeOrder.Retreat, AllyExitPoint);

            // Living troops still fight after the last captain falls.
            Model.LeaderlessAllyAssault = !allies.Any(unit => unit.Role == PrototypeRole.Leader && unit.Alive);
        }

        private void LockBattlefieldVictory(bool resume = false)
        {
            VictoryExitOpen = true;
            var canMove = Model.CanMove;
            Model.CanTarget = (_, _) => false;
            Model.CanMove = unit => unit.Side == PrototypeSide.Ally && (canMove?.Invoke(unit) ?? true);
            Model.OnlyExitMovement = true;
            Model.Arrows.Clear();
            foreach (var actor in Model.Units)
            {
                Model.CancelWindup(actor);
                actor.TargetId = actor.AttackSlot = actor.MoveGoal = -1;
                if (resume || actor.Side != PrototypeSide.Ally || actor.Escaped) continue;
                actor.ManualOrder = actor.Steering = actor.SteeringStopped = false;
                actor.Order = PrototypeOrder.Auto;
            }
            var movable = Model.Units.Where(unit => unit.Side == PrototypeSide.Ally && unit.Alive &&
                (canMove?.Invoke(unit) ?? true)).ToArray();
            victoryMovers.UnionWith(movable.Select(unit => unit.Id));
            if (movable.Length == 0) noVictoryMover = true;
            else if (!movable.Any(unit => unit.Role == PrototypeRole.Leader))
                Model.IssueOrder(-1, -1, PrototypeOrder.Retreat, AllyExitPoint);
        }

        private BattleOutcome ResolveBattlefieldExit(BattleOutcome proposed)
        {
            if (!battlefieldExits || RuinsExitOpen) return proposed;
            if (noVictoryMover || victoryExitReached) return BattleOutcome.SideAWin;
            var allies = Model.Units.Where(unit => unit.Side == PrototypeSide.Ally).ToArray();
            if (VictoryExitOpen)
                return allies.Any(unit => victoryMovers.Contains(unit.Id) &&
                        (unit.Alive && AtAllyExit(unit) && unit.Role == PrototypeRole.Leader ||
                         unit.Escaped && unit.Order == PrototypeOrder.Retreat)) ||
                    !allies.Any(unit => unit.Alive && victoryMovers.Contains(unit.Id))
                    ? BattleOutcome.SideAWin : BattleOutcome.None;
            if (proposed == BattleOutcome.SideAWin && !(BaseDemoRuins.Encounter(Session) && Session.Opening.RuinsFirstClear))
            {
                if (SettleVictoryHere) return proposed;
                LockBattlefieldVictory();
                return ResolveBattlefieldExit(BattleOutcome.None);
            }
            if (!allies.Any(unit => unit.Alive))
                return allies.Any(unit => unit.Escaped) ? BattleOutcome.Retreat : proposed;
            return proposed;
        }
    }
}

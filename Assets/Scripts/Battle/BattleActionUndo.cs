using System;
using System.Collections.Generic;
using System.Linq;

namespace BaseConquest.Battle
{
    public sealed partial class BattleSimulation
    {
        private Action undoAction;
        private int undoRound = -1;
        public bool CanUndo => undoAction != null && undoRound == RoundNumber && Phase != BattlePhase.Finished;
        public void InvalidateUndo() { undoAction = null; undoRound = -1; }
        public Action<bool> BeginUserActionGroup()
        {
            var previous = undoAction; int previousRound = undoRound;
            UserAction(() => true);
            return committed => { if (!committed) { undoAction = previous; undoRound = previousRound; } };
        }
        public bool UserAction(Func<bool> action)
        {
            if (Phase == BattlePhase.Finished) return false;
            var units = Soldiers.Select(s => new UnitOrderState(s)).ToArray();
            var groups = Squads.Select(q => new GroupOrderState(q)).ToArray();
            var ids = new HashSet<int>(Soldiers.Select(s => s.Id));
            int commands = CommandsIssued, events = Events.Count;
            var skills = (GridPosition?[])queuedSkills.Clone(); var heroes = (int[])queuedHeroes.Clone();
            if (!action()) return false;
            undoRound = RoundNumber;
            undoAction = () =>
            {
                foreach (var s in Soldiers.Where(s => !ids.Contains(s.Id)).ToArray())
                {
                    Soldiers.Remove(s); Squads[s.SquadId].Soldiers.Remove(s);
                    if (s.Kind == SoldierKind.Hero) Squads[s.SquadId].CommanderId = -1;
                    if (Phase == BattlePhase.Deployment) Scenario.Placements.RemoveAll(p => p.Cell == s.Cell && p.Side == s.Side && p.Kind == s.Kind);
                }
                foreach (var state in units) state.Restore(); foreach (var group in groups) group.Restore();
                CommandsIssued = commands; Array.Copy(skills, queuedSkills, 2); Array.Copy(heroes, queuedHeroes, 2);
                if (Events.Count > events) Events.RemoveRange(events, Events.Count - events);
                RefreshRoles(); Vision.Refresh();
            };
            return true;
        }
        public bool UndoLastAction()
        {
            if (!CanUndo) return false;
            var action = undoAction; InvalidateUndo(); action(); return true;
        }
        private sealed class UnitOrderState
        {
            private readonly BattleSoldier s;
            private readonly SquadCommand command;
            private readonly GridPosition move;
            private readonly int focus, target, rescue, manual, retained;
            private readonly bool requested, allUnits, resume;
            public UnitOrderState(BattleSoldier s)
            { this.s = s; command = s.Command; move = s.MoveGoal; focus = s.FocusTargetId; target = s.TargetId; rescue = s.RescueTargetId; manual = s.ManualRescuerId; requested = s.RescueRequested; allUnits = s.RescueAnyUnit; resume = s.ResumeFollowing; retained = s.RetainedCombatTargetId; }
            public void Restore() { s.Command = command; s.MoveGoal = move; s.FocusTargetId = focus; s.TargetId = target; s.RescueTargetId = rescue; s.ManualRescuerId = manual; s.RescueRequested = requested; s.RescueAnyUnit = allUnits; s.ResumeFollowing = resume; s.RetainedCombatTargetId = retained; }
        }
        private sealed class GroupOrderState
        {
            private readonly BattleSquad q;
            private readonly SquadCommand issued;
            private readonly bool guard, arrived, alert, skillQueued;
            private readonly GridPosition center, requested;
            private readonly GridPosition[] cells;
            private readonly int[] targets;
            public GroupOrderState(BattleSquad q) { this.q = q; issued = q.IssuedCommand; skillQueued = q.SkillQueued; guard = q.GuardAssigned; arrived = q.GuardArrived; alert = q.GuardAlert; center = q.GuardCenter; requested = q.GuardRequestedCell; cells = q.GuardCells.ToArray(); targets = q.GuardTargets.ToArray(); }
            public void Restore() { q.SkillQueued = skillQueued; q.IssuedCommand = issued; q.GuardAssigned = guard; q.GuardArrived = arrived; q.GuardAlert = alert; q.GuardCenter = center; q.GuardRequestedCell = requested; q.GuardCells.Clear(); q.GuardCells.UnionWith(cells); q.GuardTargets.Clear(); q.GuardTargets.UnionWith(targets); }
        }
    }
}

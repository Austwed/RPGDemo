using System.Collections.Generic;
using System.Linq;

namespace BaseConquest.Battle
{
    public enum CellCommandKind { Inspect, Move, Guard, CancelGuard, Focus, Retreat, Automatic, Deploy, Withdraw, Details, Rescue, SpecifyRescuer, AssignRescuer, CancelRescuer, EndDeployment, RestoreGround }
    public sealed class BattleCellCommand
    {
        public CellCommandKind Kind;
        public string Label;
        public int UnitId = -1;
        public SoldierKind SoldierKind;
    }
    public struct BattleArmyStatus
    {
        public int InField, Lost, Stock;
    }
    public sealed partial class BattleSimulation
    {
        // 部署资源空选择相当于全部在场队长；这不会改变常规指挥选择。
        public List<int> DeploymentSlots(BattleSide side, IEnumerable<int> commanderIds) => commanderIds
            .Select(Soldier).Where(s => s != null && s.Side == side && s.Kind == SoldierKind.Hero && s.IsActive)
            .Select(s => Squads[s.SquadId].CommanderSlot).Distinct().OrderBy(slot => slot).ToList();
        public BattleArmyStatus ArmyStatus(BattleSide side, SoldierKind kind, IEnumerable<int> commanderIds)
        {
            var slots = DeploymentSlots(side, commanderIds);
            var units = slots.Count == 0 ? Soldiers.Where(s => s.Side == side) : slots.SelectMany(slot => CommanderSquad(side, slot).Soldiers);
            var scope = slots.Count == 0 ? Enumerable.Range(0, Scenario.Army(side).HeroCount) : slots;
            return new BattleArmyStatus { InField = units.Count(s => s.Kind == kind && s.IsActive), Lost = units.Count(s => s.Kind == kind && !s.IsAlive), Stock = scope.Sum(slot => CommanderRemaining(side, slot, kind)) };
        }
        public int DeploymentSource(BattleSide side, SoldierKind kind, IEnumerable<int> commanderIds, GridPosition? cell = null)
        {
            var slots = DeploymentSlots(side, commanderIds);
            if (slots.Count == 0 && Phase != BattlePhase.Finished) slots = Enumerable.Range(0, Scenario.Army(side).HeroCount).Where(slot => Commander(side, slot)?.IsActive == true).ToList();
            foreach (int slot in slots)
                if (CommanderRemaining(side, slot, kind) > 0 && (!cell.HasValue || CanDeploy(side, kind, cell.Value, slot))) return slot;
            return -1;
        }
        public static IEnumerable<GridPosition> DragCells(GridPosition from, GridPosition to)
        {
            int dx = System.Math.Abs(to.X - from.X), dz = System.Math.Abs(to.Z - from.Z), sx = System.Math.Sign(to.X - from.X), sz = System.Math.Sign(to.Z - from.Z), error = dx - dz;
            while (true) { yield return from; if (from == to) yield break; int twice = error * 2; if (twice > -dz) { error -= dz; from.X += sx; } if (twice < dx) { error += dx; from.Z += sz; } }
        }
        public string Behavior(BattleSoldier s)
        {
            if (s == null) return "无单位";
            if (s.Rescued) return "已救出";
            if (s.Escaped) return "已撤离";
            if (s.CarriedById >= 0) return "正在由" + UnitName(Soldier(s.CarriedById)) + "携带救援";
            if (s.IsDowned) return "倒地，等待救援";
            if (!s.IsAlive) return "已阵亡";
            if (s.CarryingId >= 0) return "正在救援" + UnitName(Soldier(s.CarryingId)) + "，前往撤离点";
            if (s.RescueTargetId >= 0) return "正在前往救援" + UnitName(Soldier(s.RescueTargetId));
            if (s.ResumeFollowing && !RetainedCombatValid(s)) return Squads[s.SquadId].IssuedCommand == SquadCommand.Auto ? "移动已完成，恢复小队策略" : "脱战／越界，重新跟随队长前往" + s.MoveGoal;
            if (s.Command == SquadCommand.Retreat) return "正在撤退至" + ExitCell(s.Side);
            if (s.Command == SquadCommand.Move && s.Cell != s.MoveGoal) return "正在前往" + s.MoveGoal;
            if (s.LastAction == RoundAction.Attack && Soldier(s.TargetId) is BattleSoldier victim && victim.IsActive && Vision.CanSee(s.Side, victim)) return "正在攻击" + UnitName(victim);
            if (s.Command == SquadCommand.Move) return s.Cell == s.MoveGoal ? "已到达" + s.MoveGoal + "，待命（未驻守）" : "正在前往" + s.MoveGoal;
            var q = Squads[s.SquadId];
            if (s.TargetId >= 0 && Soldier(s.TargetId) is BattleSoldier target && target.IsActive && Vision.CanSee(s.Side, target)) return (s.LastAction == RoundAction.Attack ? "正在攻击" : "正在接近") + UnitName(target);
            if (s.Command == SquadCommand.Focus && Soldier(s.FocusTargetId) is BattleSoldier focus && Vision.CanSee(s.Side, focus)) return "正在集火" + UnitName(focus);
            if (q.GuardAssigned || s.Command == SquadCommand.Hold) return "正在驻守" + (q.GuardAssigned ? q.GuardCenter : s.MoveGoal);
            if (!ModernRules || q.CommanderSlot < 0) return "自动作战";
            return s.LastAction == RoundAction.Move ? (string.IsNullOrEmpty(s.LastDecision) ? "正在前往" : s.LastDecision) + s.Cell : "按" + StrategyName(Scenario.Army(s.Side).Commanders[q.CommanderSlot].Strategy) + "策略行动";
        }
        public int WithdrawUnits(BattleSide side, IEnumerable<int> ids)
        {
            if (Phase != BattlePhase.Deployment) return 0;
            var targets = ids.Distinct().Select(Soldier).Where(s => s != null && s.Side == side).OrderBy(s => s.Kind == SoldierKind.Hero ? 0 : 1).ToArray();
            if (targets.Length == 0) return 0;
            InvalidateUndo(); int before = Soldiers.Count;
            foreach (var target in targets) if (Soldier(target.Id) != null) Remove(target.Cell);
            return before - Soldiers.Count;
        }
    }
}

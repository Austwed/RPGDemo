using System;
using System.Collections.Generic;
using System.Linq;

namespace BaseConquest.Battle
{
    public sealed partial class BattleSimulation
    {
        private IEnumerable<BattleSoldier> RoleTargets(BattleSoldier s, IEnumerable<BattleSoldier> enemies)
        {
            var allies = Squads[s.SquadId].Soldiers.Where(u => u.IsActive && (u.Kind == SoldierKind.Hero || u.CombatKind == SoldierKind.Ranged)).ToArray();
            return enemies.OrderBy(e => s.CombatKind == SoldierKind.Cavalry ? e.CombatKind == SoldierKind.Ranged ? 0 : 1 : s.Kind == SoldierKind.Melee && allies.Any(a => e.TargetId == a.Id || e.FocusTargetId == a.Id || CanAttack(e, e.Cell, a)) ? 0 : 1)
                .ThenBy(e => GridPosition.Distance(s.Cell, e.Cell)).ThenByDescending(e => e.IsTransit).ThenBy(e => e.Id);
        }
        private Intent PlanRoleAttack(BattleSoldier s, List<BattleSoldier> enemies, Dictionary<GridPosition, List<BattleSoldier>> occupied, Intent intent)
        {
            var targets = RoleTargets(s, enemies).ToList(); var inRange = targets.FirstOrDefault(e => CanAttack(s, s.Cell, e));
            if (s.CombatKind == SoldierKind.Cavalry && targets.Count > 0 && targets[0].CombatKind == SoldierKind.Ranged) inRange = CanAttack(s, s.Cell, targets[0]) ? targets[0] : null;
            if (!s.IsTransit && inRange != null) { intent.Target = inRange; intent.Action = RoundAction.Attack; intent.Decision = s.Kind == SoldierKind.Melee ? "近战掩护：拦截威胁" : "兵种分工：射程内攻击"; return intent; }
            foreach (var target in targets)
            {
                if (s.CombatKind == SoldierKind.Cavalry)
                {
                    var flank = FindPath(s, p => p.Z != target.Cell.Z && CanStandToAttack(s, p, occupied) && CanAttack(s, p, target), occupied);
                    if (flank.Count > 1) { SetMove(intent, flank); intent.Target = target; intent.GoalCell = target.Cell; intent.Decision = "骑兵侧翼：优先接近远程"; return intent; }
                }
                var planned = PlanFocus(s, target, occupied); if (planned.Action != RoundAction.Wait) { planned.Decision = s.CombatKind == SoldierKind.Ranged ? "弓手保持射击距离" : s.Kind == SoldierKind.Melee ? "近战前排：拦截威胁" : "骑兵接敌"; return planned; }
            }
            return intent;
        }
        private Intent PlanRangedRetreat(BattleSoldier s, Dictionary<GridPosition, List<BattleSoldier>> occupied, Intent intent)
        {
            if (s.CombatKind != SoldierKind.Ranged || !ArcherInCloseCombat(s)) return null;
            var enemies = Soldiers.Where(e => e.IsActive && e.Side != s.Side && Vision.CanSee(s.Side, e)).ToList();
            var close = enemies.Where(e => GridPosition.Distance(s.Cell, e.Cell) <= 1 && (s.Cell==e.Cell||Board.HeightConnection(s.Cell,e.Cell))).ToList(); var q = Squads[s.SquadId]; var leader = CommanderFor(s);
            var centre = q.GuardAssigned ? q.GuardCenter : leader?.Cell ?? s.Cell; int radius = q.GuardAssigned ? q.ActualAttackRange : leader?.IsActive == true ? Math.Max(LeadRadius(q), GuardConfiguration(q).AttackRange) : Board.Width + Board.Height;
            var paths = new Dictionary<GridPosition, List<GridPosition>> { [s.Cell] = new List<GridPosition> { s.Cell } }; var open = new Queue<GridPosition>(); open.Enqueue(s.Cell);
            while (open.Count > 0)
            {
                var from = open.Dequeue(); if (paths[from].Count - 1 >= MovementSpeed(s)) continue;
                foreach (var direction in Directions)
                {
                    var next = from + direction;
                    if (!Board.CanStep(from,next) || !AttackRouteAllowed(s, next) || paths.ContainsKey(next) || GridPosition.Distance(next, centre) > radius || occupied.TryGetValue(next, out var units) && units.Any(u => u.Id != s.Id)) continue;
                    paths[next] = new List<GridPosition>(paths[from]) { next }; open.Enqueue(next);
                }
            }
            int distance = close.Min(e => GridPosition.Distance(s.Cell, e.Cell));
            var destination = paths.Keys.Where(p => close.Min(e => GridPosition.Distance(p, e.Cell)) > distance)
                .OrderByDescending(p => close.Min(e => GridPosition.Distance(p, e.Cell))).ThenByDescending(p => enemies.Any(e => CanAttack(s, p, e))).ThenBy(p => GridPosition.Distance(p, centre)).ThenBy(p => p.Z * Board.Width + p.X).Select(p => (GridPosition?)p).FirstOrDefault();
            if (!destination.HasValue) return null;
            SetMove(intent, paths[destination.Value]); intent.GoalCell = destination; intent.Decision = "弓手被近身，后撤保持距离"; return intent;
        }
    }
}

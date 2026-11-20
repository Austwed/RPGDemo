using System;
using System.Collections.Generic;
using System.Linq;

namespace BaseConquest.Battle
{
    public sealed partial class BattleSimulation
    {
        public static GridPosition SideExit(BattleBoard board, bool left)
        {
            int edge = left ? 0 : board.Width - 1;
            int mid = board.Height / 2;
            var center = new GridPosition(edge,mid);
            if (board.Walkable(center)) return center;
            return board.Positions().Where(board.Walkable)
                .OrderBy(p => Math.Abs(p.X-edge)+Math.Abs(p.Z-mid))
                .ThenBy(p => Math.Abs(p.Z-mid)).First();
        }
        public GridPosition ExitCell(BattleSide side) => Scenario.CustomExits ? side == BattleSide.A ? Scenario.ExitA : Scenario.ExitB : SideExit(Board, side == BattleSide.A);
        public bool IsExit(BattleSide side, GridPosition cell) => ModernRules ? cell == ExitCell(side) : Board.Cell(cell).Zone == (side == BattleSide.A ? DeploymentZone.A : DeploymentZone.B);
        public bool VictoryDeclared { get; private set; }
        public bool RoutInProgress => VictoryDeclared && Soldiers.Any(s => s.Side == BattleSide.B && s.IsActive);
        private bool EnemyCommandersDefeated => ModernRules && Soldiers.Any(s => s.Side == BattleSide.B && s.Kind == SoldierKind.Hero) &&
            !Soldiers.Any(s => s.Side == BattleSide.B && s.Kind == SoldierKind.Hero && s.IsAlive);
        public int EnemyTroopsDeployed => Soldiers.Count(s => s.Side == BattleSide.B && s.Kind != SoldierKind.Hero);
        public int EnemyTroopsLost => Soldiers.Count(s => s.Side == BattleSide.B && s.Kind != SoldierKind.Hero && !s.IsAlive);
        private bool CommanderVictorySatisfied => EnemyCommandersDefeated && (EnemyTroopsDeployed == 0 || EnemyTroopsLost * 2 > EnemyTroopsDeployed);
        public bool HoldVictorySatisfied => Scenario.Objective.Kind == ObjectiveKinds.Hold && RoundNumber >= Scenario.Objective.HoldRounds && Soldiers.Any(s => s.Side == BattleSide.A && s.IsActive);
        private void DeclareCommanderVictory()
        {
            foreach (var s in Soldiers.Where(s => s.Side == BattleSide.B && s.IsActive)) { SetOrder(s, SquadCommand.Retreat); s.RescueTargetId = -1; }
            foreach (var q in Squads.Where(q => q.Side == BattleSide.B)) { q.IssuedCommand = SquadCommand.Retreat; ClearGuardState(q); }
            VictoryDeclared = true;
            Notice(HoldVictorySatisfied ? "已坚守指定回合，我方胜利，入侵余兵撤退" : "已满足敌方指挥者失能和兵力损失的胜利条件，我方胜利，敌方余兵停止攻击并撤离", BattleSide.A);
        }
        public bool AdvanceRout()
        {
            return RoutInProgress && AdvanceRound(); // 保留旧入口兼容，仍经过正常回合。
        }
        private static void ClearGuardState(BattleSquad q) { q.GuardAssigned = q.GuardArrived = q.GuardAlert = false; q.GuardTargets.Clear(); }
        public bool ToggleGuardCommanders(IEnumerable<int> ids, GridPosition cell, out bool cancelled)
        {
            var groups = SelectedCommanderSquads(ids); cancelled = groups.Any(q => q.GuardAssigned && (q.GuardRequestedCell == cell || q.GuardCenter == cell));
            if (!cancelled) return GuardCommanders(ids, cell);
            foreach (var q in groups.Where(q => q.GuardAssigned && (q.GuardRequestedCell == cell || q.GuardCenter == cell)))
            { ClearGuardState(q); q.GuardCells.Clear(); q.IssuedCommand = SquadCommand.Auto; foreach (var s in q.Soldiers.Where(CanCommand)) SetOrder(s, SquadCommand.Auto); }
            CommandsIssued++; return true;
        }
        public bool SetCommanderRanges(BattleSide side, int slot, int guard, int alert, int attack)
        {
            if (!Control.Allows("range")) return false;
            var q = CommanderSquad(side, slot);
            if (q == null || Phase == BattlePhase.Finished || guard < 1 || guard > alert || alert > attack || attack > 32) return false;
            var c = Scenario.Army(side).Commanders[slot]; c.GuardRange = guard; c.AlertRange = alert; c.AttackRange = attack;
            if (q.GuardAssigned) RefreshGuardCells(q); return true;
        }
        private CommanderLoadout GuardConfiguration(BattleSquad q) => Scenario.Army(q.Side).Commanders[q.CommanderSlot];
        private void RefreshGuardCells(BattleSquad q)
        {
            q.GuardCells.Clear(); var c = GuardConfiguration(q);
            var distances = Board.Positions().Where(Board.Walkable).OrderBy(p => GridPosition.Distance(p, q.GuardCenter)).Select(p => GridPosition.Distance(p, q.GuardCenter)).ToArray();
            int needed = Math.Max(1, q.Soldiers.Count(s => s.IsActive));
            int radius = Math.Max(c.GuardRange, distances.Length == 0 ? c.GuardRange : distances[Math.Min(needed, distances.Length) - 1]);
            q.ActualGuardRange = radius; q.ActualAlertRange = Math.Max(c.AlertRange, radius + 1); q.ActualAttackRange = Math.Max(c.AttackRange, q.ActualAlertRange + 1);
            foreach (var p in Board.Positions()) if (Board.Walkable(p) && GridPosition.Distance(p, q.GuardCenter) <= radius) q.GuardCells.Add(p);
        }
        private void PrepareGuards()
        {
            if (!ModernRules) return;
            foreach (var q in Squads.Where(q => q.GuardAssigned))
            {
                var leader = Soldier(q.CommanderId); if (leader?.IsActive != true) { ClearGuardState(q); continue; }
                if (leader.Cell == q.GuardCenter) q.GuardArrived = true;
                if (!q.GuardArrived) continue;
                var c = GuardConfiguration(q);
                RefreshGuardCells(q);
                q.GuardTargets.RemoveWhere(id => Soldier(id)?.IsActive != true || !Vision.CanSee(q.Side, Soldier(id)) || GridPosition.Distance(Soldier(id).Cell, q.GuardCenter) > q.ActualAttackRange);
                foreach (var e in Soldiers.Where(s => s.IsActive && s.Side != q.Side && Vision.CanSee(q.Side, s) && GridPosition.Distance(s.Cell, q.GuardCenter) <= q.ActualAlertRange)) q.GuardTargets.Add(e.Id);
                q.GuardAlert = q.GuardTargets.Count > 0;
            }
        }
        private Intent PlanGuardPatrol(BattleSoldier s, Dictionary<GridPosition, List<BattleSoldier>> occupied, Intent intent)
        {
            var q = Squads[s.SquadId]; var c = GuardConfiguration(q);
            if (GridPosition.Distance(s.Cell, q.GuardCenter) > q.ActualAttackRange)
            { SetMove(intent, FindPath(s, p => q.GuardCells.Contains(p), occupied)); intent.Decision = "接近驻守队伍"; return intent; }
            var enemies = q.GuardTargets.Select(Soldier).Where(e => e?.IsActive == true && Vision.CanSee(s.Side, e) && GridPosition.Distance(e.Cell, q.GuardCenter) <= q.ActualAttackRange)
                .OrderBy(e => GridPosition.Distance(e.Cell, q.GuardCenter)).ThenByDescending(e => e.IsTransit).ThenBy(e => e.Id).ToList();
            if (enemies.Count > 0)
            {
                enemies = RoleTargets(s, enemies).ToList();
                var target = enemies.FirstOrDefault(e => CanAttack(s, s.Cell, e));
                if (!s.IsTransit && target != null) { intent.Action = RoundAction.Attack; intent.Target = target; intent.Decision = "驻守预警：攻击威胁"; return intent; }
                Func<GridPosition, bool> allowed = p => GridPosition.Distance(p, q.GuardCenter) <= q.ActualAttackRange;
                foreach (var e in enemies)
                {
                    var path = FindPath(s, p => allowed(p) && CanStandToAttack(s, p, occupied) && CanAttack(s, p, e), occupied, allowed);
                    if (path.Count > 1) { SetMove(intent, path); intent.Target = e; intent.Decision = "驻守预警：范围内追击"; return intent; }
                }
                return intent;
            }
            if (!q.GuardCells.Contains(s.Cell)) { SetMove(intent, FindPath(s, p => q.GuardCells.Contains(p), occupied)); intent.Decision = "威胁消退，返回驻守范围"; return intent; }
            // 整轮间确定性游荡，不每帧生成路线；同一种子／命令保持可复现。
            var cells = q.GuardCells.Where(p => !occupied.TryGetValue(p, out var list) || list.Count(u => u.Id != s.Id) < 2).OrderBy(p => p.Z * Board.Width + p.X).ToList();
            if (cells.Count > 0 && (RoundNumber + s.Id) % 3 == 0)
            {
                int index = (int)((uint)(Scenario.RandomSeed + s.Id * 17 + RoundNumber * 7) % (uint)cells.Count); var destination = cells[index];
                SetMove(intent, FindPath(s, p => p == destination, occupied, p => q.GuardCells.Contains(p)));
            }
            intent.Decision = "驻守范围内游荡"; return intent;
        }
        private Intent PlanWithoutCommander(BattleSoldier s, List<BattleSoldier> enemies, Dictionary<GridPosition, List<BattleSoldier>> occupied, Intent intent)
        {
            if (ModernRules && (s.Kind != SoldierKind.Hero||s.CombatKind!=SoldierKind.Melee) && enemies.Count > 0) return PlanRoleAttack(s, enemies, occupied, intent);
            var target = enemies.FirstOrDefault(e => CanAttack(s, s.Cell, e));
            if (!s.IsTransit && target != null) { intent.Action = RoundAction.Attack; intent.Target = target; return intent; }
            if (enemies.Count > 0) return PlanFocus(s, enemies[0], occupied);
            return PlanUnseenMovement(s, occupied, intent);
        }

        private Intent PlanUnseenMovement(BattleSoldier unit, Dictionary<GridPosition,List<BattleSoldier>> occupied, Intent intent)
        {
            if (unit.Command != SquadCommand.Auto) return intent;
            if (unit.Side == BattleSide.B)
            {
                var target = ExitCell(BattleSide.A);
                SetMove(intent, FindPath(unit, p => p == target, occupied));
            }
            else if (unit.Kind == SoldierKind.Hero)
            {
                var player = Soldiers.FirstOrDefault(s => s.IsActive && s.Side == unit.Side && s.Kind == SoldierKind.Hero &&
                    s.OriginCommanderSlot == Scenario.PlayerLeaderSlot);
                if (player != null && player != unit)
                {
                    int range = GuardConfiguration(Squads[player.SquadId]).AttackRange;
                    if (GridPosition.Distance(unit.Cell,player.Cell) > range)
                        SetMove(intent, ReturnToCommander(unit,player,occupied,range));
                }
            }
            return intent;
        }
        private List<GridPosition> FindWeightedPath(BattleSoldier s, Func<GridPosition, bool> goal, Dictionary<GridPosition, List<BattleSoldier>> occupied, Func<GridPosition, bool> allowed)
        {
            int serial = 0; var frontier = new SortedSet<RouteNode>(Comparer<RouteNode>.Create((a, b) => { int order = a.Cost.CompareTo(b.Cost); return order != 0 ? order : a.Serial.CompareTo(b.Serial); }));
            var costs = new Dictionary<GridPosition, double> { [s.Cell] = 0 }; var previous = new Dictionary<GridPosition, GridPosition>();
            frontier.Add(new RouteNode { Cell = s.Cell, Cost = 0, Serial = serial++ });
            while (frontier.Count > 0)
            {
                var node = frontier.Min; frontier.Remove(node); if (costs[node.Cell] != node.Cost) continue;
                if (goal(node.Cell))
                {
                    var path = new List<GridPosition> { node.Cell }; var cursor = node.Cell;
                    while (previous.TryGetValue(cursor, out var parent)) { cursor = parent; path.Add(cursor); } path.Reverse(); return path;
                }
                foreach (var d in reservations == null ? Directions : Directions.Skip((s.Id + RoundNumber) % 4).Concat(Directions.Take((s.Id + RoundNumber) % 4)))
                {
                    var next = node.Cell + d;
                    if (!Board.CanStep(node.Cell,next) || !AttackRouteAllowed(s,next) || allowed != null && !allowed(next)) continue;
                    if (occupied.TryGetValue(next, out var units) && units.Any(u => u.Side != s.Side && Vision.CanSee(s.Side, u))) continue;
                    double cost = node.Cost + PathFootwork(s, next, node.MoveProgress + 1, occupied);
                    if (costs.TryGetValue(next, out var old) && old <= cost) continue;
                    costs[next] = cost; previous[next] = node.Cell; frontier.Add(new RouteNode { Cell = next, Cost = cost, MoveProgress = node.MoveProgress + 1, Serial = serial++ });
                }
            }
            return new List<GridPosition>();
        }
    }
}

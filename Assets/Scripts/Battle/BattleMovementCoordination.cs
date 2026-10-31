using System;
using System.Collections.Generic;
using System.Linq;

namespace BaseConquest.Battle
{
    public sealed partial class BattleSimulation
    {
        private Dictionary<(BattleSide side, int step, GridPosition cell), HashSet<int>> reservations;
        private HashSet<(BattleSide side, int step, GridPosition cell)> negotiatedCrowding;
        private Dictionary<int, List<GridPosition>> plannedLeaderRoutes;
        private Dictionary<int, GridPosition> plannedFormationGoals;
        private readonly Dictionary<(int id, GridPosition cell, int count, int configured), int> leadRadii = new Dictionary<(int, GridPosition, int, int), int>();
        public static IEnumerable<(GridPosition cell, int side)> RangeBoundary(IEnumerable<GridPosition> cells)
        {
            var set = new HashSet<GridPosition>(cells);
            foreach (var cell in set.OrderBy(p => p.Z).ThenBy(p => p.X)) for (int side = 0; side < 4; side++) if (!set.Contains(cell + Directions[side])) yield return (cell, side);
        }
        public IEnumerable<GridPosition> AppliedRangeCells(GridPosition center, int radius) => Board.Positions().Where(p => Board.Walkable(p) && GridPosition.Distance(p, center) <= radius);
        public int FootworkToEnter(GridPosition cell, bool shared) => (Board.Cell(cell)?.FootworkCost ?? 1) + (shared ? 2 : 0);
        public int LeadRadius(BattleSquad q)
        {
            var leader = Soldier(q.CommanderId); return leader == null ? 0 : LeadRadiusAt(q, leader.Cell);
        }
        private int LeadRadiusAt(BattleSquad q, GridPosition center)
        {
            int configured = Math.Max(1, Scenario.Army(q.Side).Commanders[q.CommanderSlot].LeadRange);
            int needed = Math.Max(1, q.Soldiers.Count(s => s.IsActive));
            var key = (q.Id, center, needed, configured); if (leadRadii.TryGetValue(key, out int cached)) return cached;
            var distances = Board.Positions().Where(Board.Walkable).Select(p => GridPosition.Distance(p, center)).OrderBy(d => d).ToArray();
            return leadRadii[key] = Math.Max(configured, distances.Length == 0 ? configured : distances[Math.Min(needed - 1, distances.Length - 1)]);
        }
        private bool IsLedMovement(BattleSoldier s) => ModernRules && s.Kind != SoldierKind.Hero && (s.ResumeFollowing ? !RetainedCombatValid(s) && Squads[s.SquadId].IssuedCommand == SquadCommand.Move : s.Command == SquadCommand.Move) && s.RescueTargetId < 0 && s.CarryingId < 0 && CommanderFor(s)?.IsActive == true;
        private int MovingCombatRadius(BattleSoldier s) => GuardConfiguration(Squads[s.SquadId]).Strategy==SquadStrategy.Attack ? GuardConfiguration(Squads[s.SquadId]).AttackRange : Math.Max(LeadRadius(Squads[s.SquadId]), GuardConfiguration(Squads[s.SquadId]).AttackRange);
        private bool RetainedCombatValid(BattleSoldier s)
        {
            var leader = CommanderFor(s); var target = Soldier(s.RetainedCombatTargetId);
            return leader?.IsActive == true && target?.IsActive == true && target.Side != s.Side && Vision.CanSee(s.Side, target)
                && GridPosition.Distance(s.Cell, leader.Cell) <= MovingCombatRadius(s) && GridPosition.Distance(target.Cell, leader.Cell) <= MovingCombatRadius(s);
        }
        private Intent PlanResumingFollower(BattleSoldier s, Dictionary<GridPosition, List<BattleSoldier>> occupied, Intent intent)
        {
            if (!RetainedCombatValid(s))
            {
                if (Squads[s.SquadId].IssuedCommand == SquadCommand.Auto)
                {
                    var enemies = Soldiers.Where(t => t.Side != s.Side && t.IsActive && Vision.CanSee(s.Side, t)).OrderBy(t => GridPosition.Distance(s.Cell, t.Cell)).ThenByDescending(t => t.IsTransit).ThenBy(t => t.Id).ToList();
                    return PlanSquadStrategy(s, enemies, occupied, intent);
                }
                PlanLedMovement(s, occupied, intent); intent.Decision = "脱战／越界，重新跟随队长"; return intent;
            }
            var leader = CommanderFor(s); var target = Soldier(s.RetainedCombatTargetId);
            intent.Target = target;
            if (!s.IsTransit && CanAttack(s, s.Cell, target)) { intent.Action = RoundAction.Attack; intent.Decision = "保留交战，队长行军不打断"; return intent; }
            int radius = MovingCombatRadius(s);
            SetMove(intent, FindPath(s, p => CanStandToAttack(s, p, occupied) && CanAttack(s, p, target), occupied, p => GridPosition.Distance(p, leader.Cell) <= radius));
            intent.Decision = "追击原目标，受队长追击上限约束"; return intent;
        }
        private Intent PlanLedMovement(BattleSoldier s, Dictionary<GridPosition, List<BattleSoldier>> occupied, Intent intent)
        {
            var leader = CommanderFor(s); int radius = LeadRadius(Squads[s.SquadId]);
            GridPosition stepCenter = plannedLeaderRoutes != null && plannedLeaderRoutes.TryGetValue(leader.Id, out var route) ? route[route.Count - 1] : leader.Cell;
            GridPosition goal = plannedFormationGoals != null && plannedFormationGoals.TryGetValue(s.Id, out var planned) ? planned : s.MoveGoal;
            // Plan/Preview只生成意图，不写单位；真实目标只在AdvanceRound提交一次。
            if (goal != s.MoveGoal) intent.ReplannedGoal = goal;
            if (GridPosition.Distance(s.Cell, stepCenter) > radius)
                SetMove(intent, FindPath(s, p => GridPosition.Distance(p, stepCenter) <= Math.Max(1, radius - 1), occupied));
            else SetMove(intent, FindPath(s, p => p == goal, occupied));
            intent.GoalCell = goal; intent.Decision = "同步前往，率领半径 " + radius; return intent;
        }
        private Dictionary<int, GridPosition> PlanFormationGoals(List<BattleSoldier> actors, Dictionary<GridPosition, List<BattleSoldier>> occupied)
        {
            var result = new Dictionary<int, GridPosition>();
            var groups = actors.Where(s => IsLedMovement(s) && Squads[s.SquadId].IssuedCommand == SquadCommand.Move && CommanderFor(s).Command == SquadCommand.Move).GroupBy(s => s.SquadId).OrderBy(g => g.Key).ToList();
            var reserved = new HashSet<(BattleSide, GridPosition)>();
            foreach (var g in groups) { var h = Soldier(Squads[g.Key].CommanderId); reserved.Add((h.Side, h.MoveGoal)); }
            var known = new Dictionary<BattleSide, Dictionary<GridPosition, List<BattleSoldier>>>();
            var components = new Dictionary<(BattleSide, GridPosition), HashSet<GridPosition>>();
            var invalid = new List<BattleSoldier>();
            // 先保留全部仍有效的落点，失效者不能抢走其他成员的已有目标。
            foreach (var g in groups)
            {
                var q = Squads[g.Key]; var final = Soldier(q.CommanderId).MoveGoal; int radius = LeadRadiusAt(q, final);
                if (!known.ContainsKey(q.Side)) known[q.Side] = KnownOccupancy(q.Side, occupied);
                foreach (var member in g.OrderBy(s => s.Id))
                {
                    var reachable = FormationReachable(member, known[q.Side], components);
                    if (Board.Walkable(member.MoveGoal) && GridPosition.Distance(member.MoveGoal, final) <= radius && reachable.Contains(member.MoveGoal) && !reserved.Contains((member.Side, member.MoveGoal)))
                    { result[member.Id] = member.MoveGoal; reserved.Add((member.Side, member.MoveGoal)); }
                    else invalid.Add(member);
                }
            }
            foreach (var member in invalid)
            {
                var q = Squads[member.SquadId]; var final = Soldier(q.CommanderId).MoveGoal; int radius = LeadRadiusAt(q, final);
                var reachable = FormationReachable(member, known[q.Side], components);
                var preferred = final + new GridPosition(member.Kind == SoldierKind.Ranged ? member.Side == BattleSide.A ? -1 : 1 : member.Kind == SoldierKind.Melee ? member.Side == BattleSide.A ? 1 : -1 : 0, 0);
                var goal = Board.Positions().Where(Board.Walkable).Where(p => GridPosition.Distance(p, final) <= radius && reachable.Contains(p) && !reserved.Contains((q.Side,p)))
                    .OrderBy(p => known[q.Side].TryGetValue(p, out var units) && units.Any(u => u.Id != member.Id) ? 1 : 0)
                    .ThenBy(p => GridPosition.Distance(p, preferred)).ThenBy(p => p.Z * Board.Width + p.X).Select(p => (GridPosition?)p).FirstOrDefault();
                var destination = goal ?? member.Cell; result[member.Id] = destination; reserved.Add((q.Side,destination));
            }
            return result;
        }
        private HashSet<GridPosition> FormationReachable(BattleSoldier member, Dictionary<GridPosition, List<BattleSoldier>> known, Dictionary<(BattleSide, GridPosition), HashSet<GridPosition>> cache)
        {
            if (cache.TryGetValue((member.Side,member.Cell), out var cached)) return cached;
            var visited = new HashSet<GridPosition> { member.Cell }; var frontier = new Queue<GridPosition>(); frontier.Enqueue(member.Cell);
            while (frontier.Count > 0)
            {
                var from = frontier.Dequeue();
                foreach (var direction in Directions)
                {
                    var next = from + direction;
                    if (!Board.CanStep(from,next) || visited.Contains(next) || known.TryGetValue(next, out var units) && units.Any(u => u.Side != member.Side)) continue;
                    visited.Add(next); frontier.Enqueue(next);
                }
            }
            // 同阵营同连通区复用一次洪泛，避免每名成员／每个候选反复完整寻路。
            foreach (var cell in visited) cache[(member.Side,cell)] = visited;
            return visited;
        }
        private int PathFootwork(BattleSoldier s, GridPosition cell, int step, Dictionary<GridPosition, List<BattleSoldier>> occupied)
        {
            bool knownTerrain = !Scenario.FogEnabled || Vision.Explored(s.Side, cell);
            int terrain = knownTerrain ? IsMarching(s) ? 1 : Board.Cell(cell).FootworkCost : 1;
            bool shared;
            if (reservations != null && step <= Math.Max(1, MovementSpeed(s)))
                shared = negotiatedCrowding.Contains((s.Side, step, cell)) || reservations.TryGetValue((s.Side, step, cell), out var ids) && ids.Any(id => id != s.Id);
            else shared = occupied.TryGetValue(cell, out var units) && units.Any(u => u.Side == s.Side && u.Id != s.Id);
            return terrain + (shared ? 2 : 0);
        }
        private List<Intent> CoordinatedIntents(List<BattleSoldier> actors, Dictionary<GridPosition, List<BattleSoldier>> occupied)
        {
            if (!ModernRules) return actors.Select(s => Plan(s, occupied)).ToList();
            try
            {
                negotiatedCrowding = new HashSet<(BattleSide, int, GridPosition)>();
                plannedFormationGoals = PlanFormationGoals(actors, occupied);
                var intents = actors.Select(s => Plan(s, occupied)).ToList();
                int horizon = actors.Select(s => MovementSpeed(s)).DefaultIfEmpty(1).Max();
                // 只预约本行动的子步；两轮有限协商，不逐兵扫描全军、不反复震荡。
                for (int pass = 0; pass < 2; pass++)
                {
                    var resolved = ResolveMoves(intents, occupied, actors);
                    plannedLeaderRoutes = actors.Where(s => s.Kind == SoldierKind.Hero).ToDictionary(s => s.Id, s => resolved.TryGetValue(s.Id, out var path) ? path : new List<GridPosition> { s.Cell });
                    reservations = new Dictionary<(BattleSide, int, GridPosition), HashSet<int>>();
                    foreach (var intent in intents.Where(i => i.Action != RoundAction.Escape))
                        for (int step = 1; step <= horizon; step++)
                        {
                            int progress = intent.Action == RoundAction.Move ? Math.Min(step, Math.Min(MovementSpeed(intent.Actor), intent.Path.Count - 1)) : 0;
                            var cell = progress > 0 ? intent.Path[progress] : intent.Actor.Cell; var key = (intent.Actor.Side, step, cell);
                            if (!reservations.TryGetValue(key, out var ids)) reservations[key] = ids = new HashSet<int>(); ids.Add(intent.Actor.Id);
                        }
                    foreach (var entry in reservations) if (entry.Value.Count > 1) negotiatedCrowding.Add(entry.Key);
                    intents = actors.Select(s => Plan(s, occupied)).ToList();
                }
                return intents;
            }
            finally { reservations = null; negotiatedCrowding = null; plannedLeaderRoutes = null; plannedFormationGoals = null; }
        }
        private bool LedStepAllowed(BattleSoldier s, GridPosition next, Dictionary<int, GridPosition> positions, Dictionary<int, GridPosition> candidates, HashSet<int> allowed)
        {
            if (AttackMemberLimited(s))
            {
                var commander = CommanderFor(s); var centre = positions[commander.Id];
                if (allowed.Contains(commander.Id)) centre = candidates[commander.Id];
                int radius = GuardConfiguration(Squads[s.SquadId]).AttackRange;
                int attackBefore = GridPosition.Distance(positions[s.Id], centre), attackAfter = GridPosition.Distance(next, centre);
                if (attackAfter > radius && (attackBefore <= radius || attackAfter >= attackBefore)) return false;
            }
            if (!IsLedMovement(s)) return true;
            var leader = CommanderFor(s); if (!positions.TryGetValue(leader.Id, out var center)) return true;
            if (allowed.Contains(leader.Id)) center = candidates[leader.Id];
            int before = GridPosition.Distance(positions[s.Id], center), after = GridPosition.Distance(next, center);
            return after <= LeadRadius(Squads[s.SquadId]) || before > LeadRadius(Squads[s.SquadId]) && after < before;
        }
        private void RecordFootwork(List<Intent> intents, Dictionary<int, List<GridPosition>> routes)
        {
            int horizon = routes.Values.Select(r => r.Count - 1).DefaultIfEmpty(0).Max();
            for (int step = 1; step <= horizon; step++)
            {
                var occupancy = intents.Where(i => i.Action != RoundAction.Escape).GroupBy(i => (i.Actor.Side, cell: routes.TryGetValue(i.Actor.Id, out var r) ? r[Math.Min(step, r.Count - 1)] : i.Actor.Cell)).ToDictionary(g => g.Key, g => g.Count());
                foreach (var intent in intents) if (routes.TryGetValue(intent.Actor.Id, out var path) && step < path.Count) { bool shared = occupancy[(intent.Actor.Side, path[step])] > 1; intent.Actor.LastFootwork += (IsMarching(intent.Actor) ? 1 : Board.Cell(path[step]).FootworkCost) + (shared ? 2 : 0); RegisterMovementStrain(intent.Actor, path[step]); }
            }
        }
    }
}

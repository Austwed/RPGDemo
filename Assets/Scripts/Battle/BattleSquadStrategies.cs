using System;
using System.Collections.Generic;
using System.Linq;

namespace BaseConquest.Battle
{
    public sealed partial class BattleSimulation
    {
        private bool AttackMemberLimited(BattleSoldier s) => ModernRules && s.Kind != SoldierKind.Hero && CommanderFor(s)?.IsActive == true
            && !Squads[s.SquadId].GuardAssigned && GuardConfiguration(Squads[s.SquadId]).Strategy == SquadStrategy.Attack
            && (s.Command == SquadCommand.Auto || s.Command == SquadCommand.Focus || s.ResumeFollowing) && s.RescueTargetId < 0 && s.CarryingId < 0
            && !(s.ResumeFollowing && Squads[s.SquadId].IssuedCommand==SquadCommand.Move && !RetainedCombatValid(s));
        private bool AttackAreaContains(BattleSoldier s, GridPosition cell) => !AttackMemberLimited(s) || GridPosition.Distance(cell, CommanderFor(s).Cell) <= GuardConfiguration(Squads[s.SquadId]).AttackRange;
        // 限制战斗决策中的索敌／寻路；下达新移动或选择救援者时的通路验证仍可到达圈外。
        private bool AttackRouteAllowed(BattleSoldier s, GridPosition cell) => !planningActorPath || !AttackMemberLimited(s) || GridPosition.Distance(cell, CommanderFor(s).Cell) <= Math.Max(GuardConfiguration(Squads[s.SquadId]).AttackRange, GridPosition.Distance(s.Cell, CommanderFor(s).Cell));
        public static string StrategyName(SquadStrategy value) => value == SquadStrategy.Obey?"手动":value == SquadStrategy.Protect ? "驻守" : value == SquadStrategy.Flee ? "逃跑" : "进攻";
        public bool SetCommanderStrategy(BattleSide side, int slot, SquadStrategy strategy)
        {
            if (!Control.Allows("strategy")) return false;
            var q = CommanderSquad(side, slot); if (q == null || Phase == BattlePhase.Finished || !Enum.IsDefined(typeof(SquadStrategy), strategy)) return false;
            Scenario.Army(side).Commanders[slot].Strategy = strategy; q.StrategyThreats.Clear(); CommandsIssued++; return true;
        }
        public BattleSoldier StrategyAnchor(BattleSoldier leader)
        {
            if (leader?.IsActive != true) return null;
            var candidate = Soldiers.Where(s => s.Side == leader.Side && s.Kind == SoldierKind.Hero && s.IsActive && s.Id != leader.Id && s.Command != SquadCommand.Retreat)
                .OrderByDescending(s => s.OriginCommanderSlot).ThenBy(s => s.Id).FirstOrDefault();
            if (candidate == null) return null;
            // 最高序号支持队与次高序号互相守护时，前者作为固定安全锚点。
            if (leader.OriginCommanderSlot > candidate.OriginCommanderSlot && GuardConfiguration(Squads[candidate.SquadId]).Strategy != SquadStrategy.Attack) return null;
            return candidate;
        }
        private bool OwnDeployment(BattleSide side, GridPosition p) => Board.Cell(p).Zone == (side == BattleSide.A ? DeploymentZone.A : DeploymentZone.B);
        private GridPosition StrategyCentre(BattleSoldier leader)
        {
            var anchor = StrategyAnchor(leader); if (anchor != null) return anchor.Cell;
            return Board.Positions().Where(p => Board.Walkable(p) && OwnDeployment(leader.Side, p)).OrderBy(p => GridPosition.Distance(p, leader.Cell)).ThenBy(p => p.Z * Board.Width + p.X).Select(p => (GridPosition?)p).FirstOrDefault() ?? leader.Cell;
        }
        private int StrategyGuardRadius(BattleSquad q, GridPosition centre)
        {
            var distances = Board.Positions().Where(Board.Walkable).Select(p => GridPosition.Distance(p, centre)).OrderBy(d => d).ToArray();
            return Math.Max(GuardConfiguration(q).GuardRange, distances.Length == 0 ? 1 : distances[Math.Min(Math.Max(1, q.ActiveCount), distances.Length) - 1]);
        }
        private void PrepareStrategies()
        {
            if (!ModernRules) return;
            foreach (var q in Squads)
            {
                var leader = Soldier(q.CommanderId);
                // 手动与进攻都不需要维护搜索圈：手动原地待命，进攻按队长半径即时筛选。
                if (leader?.IsActive != true || q.GuardAssigned || q.IssuedCommand != SquadCommand.Auto || GuardConfiguration(q).Strategy == SquadStrategy.Attack || GuardConfiguration(q).Strategy == SquadStrategy.Obey) { q.StrategyThreats.Clear(); continue; }
                var centre = GuardConfiguration(q).Strategy == SquadStrategy.Flee ? leader.Cell : StrategyCentre(leader);
                var c = GuardConfiguration(q); int inner = StrategyGuardRadius(q, centre), alert = Math.Max(c.AlertRange, inner + 1), attack = Math.Max(c.AttackRange, alert + 1);
                q.StrategyThreats.RemoveWhere(id => Soldier(id)?.IsActive != true || !Vision.CanSee(q.Side, Soldier(id)) || GridPosition.Distance(Soldier(id).Cell, centre) > attack);
                foreach (var e in Soldiers.Where(s => s.IsActive && s.Side != q.Side && Vision.CanSee(q.Side, s) && GridPosition.Distance(s.Cell, centre) <= alert)) q.StrategyThreats.Add(e.Id);
            }
        }
        private Intent PlanSquadStrategy(BattleSoldier s, List<BattleSoldier> enemies, Dictionary<GridPosition, List<BattleSoldier>> occupied, Intent intent)
        {
            var q = Squads[s.SquadId]; var leader = Soldier(q.CommanderId); if (leader?.IsActive != true) return PlanWithoutCommander(s, enemies, occupied, intent);
            var c = GuardConfiguration(q);
            // 手动档（2026-11-06 P36）：以当前站位为锚点，不自主移动、不追击；只在自身射程内攻击或还击。
            // 与「驻守」的区别：驻守会回守区、会主动接近守区内的威胁，手动不会。
            if (c.Strategy == SquadStrategy.Obey)
            {
                var inRange = enemies.FirstOrDefault(e => CanAttack(s, s.Cell, e));
                if (!s.IsTransit && inRange != null) { intent.Action = RoundAction.Attack; intent.Target = inRange; intent.Decision = "手动：原地还击"; }
                else { intent.GoalCell = s.Cell; intent.Decision = "手动：原地待命"; }
                return intent;
            }
            if (c.Strategy == SquadStrategy.Attack)
            {
                if (s.Kind != SoldierKind.Hero) enemies = enemies.Where(e => AttackAreaContains(s, e.Cell)).ToList();
                if (enemies.Count > 0 || s.Id == leader.Id) return PlanWithoutCommander(s, enemies, occupied, intent);
                if (GridPosition.Distance(s.Cell, leader.Cell) > FollowRadius || s.IsTransit) SetMove(intent, ReturnToCommander(s, leader, occupied, FollowRadius - 1));
                intent.Destination = intent.Action == RoundAction.Move ? intent.Destination : s.Cell; intent.GoalCell = leader.Cell; intent.Decision = "进攻：跟随队长"; return intent;
            }
            var anchor = StrategyAnchor(leader); var centre = c.Strategy == SquadStrategy.Flee ? leader.Cell : StrategyCentre(leader);
            int inner = StrategyGuardRadius(q, centre), alert = Math.Max(c.AlertRange, inner + 1), outer = Math.Max(c.AttackRange, alert + 1);
            intent.GoalCell = centre;
            if (c.Strategy == SquadStrategy.Flee && s.Id == leader.Id)
            {
                var nearby = enemies.Where(e => GridPosition.Distance(e.Cell, leader.Cell) <= alert).ToList();
                if (nearby.Count > 0) return PlanFleeLeader(s, nearby, anchor, occupied, intent);
                return PlanSafeLeader(s, anchor, centre, inner, occupied, intent);
            }
            var threats = enemies.Where(e => GridPosition.Distance(e.Cell, centre) <= outer && (q.StrategyThreats.Contains(e.Id) || GridPosition.Distance(e.Cell, centre) <= alert || c.Strategy == SquadStrategy.Flee && GridPosition.Distance(e.Cell, s.Cell) <= alert))
                .OrderBy(e => c.Strategy == SquadStrategy.Flee ? GridPosition.Distance(e.Cell, s.Cell) : e.TargetId == leader.Id || anchor != null && e.TargetId == anchor.Id ? -1 : GridPosition.Distance(e.Cell, centre)).ThenByDescending(e => e.IsTransit).ThenBy(e => e.Id).ToList();
            if (GridPosition.Distance(s.Cell, centre) > outer) { SetMove(intent, FindPath(s, p => GridPosition.Distance(p, centre) <= inner, occupied)); intent.Decision = "返回移动守区"; return intent; }
            if (s.Kind != SoldierKind.Hero) threats = RoleTargets(s, threats).ToList();
            var target = threats.FirstOrDefault(e => CanAttack(s, s.Cell, e));
            if (!s.IsTransit && target != null) { intent.Action = RoundAction.Attack; intent.Target = target; intent.Decision = c.Strategy == SquadStrategy.Flee ? "逃跑：士兵反击近处威胁" : "驻守：清除守区威胁"; return intent; }
            Func<GridPosition, bool> allowed = p => GridPosition.Distance(p, centre) <= outer;
            foreach (var e in threats)
            {
                var path = FindPath(s, p => allowed(p) && CanStandToAttack(s, p, occupied) && CanAttack(s, p, e), occupied, allowed);
                if (path.Count > 1) { SetMove(intent, path); intent.Target = e; intent.Decision = "护卫队伍：接近威胁"; return intent; }
            }
            if (s.Id == leader.Id) return PlanSafeLeader(s, anchor, centre, inner, occupied, intent);
            SetMove(intent, FindPath(s, p => GridPosition.Distance(p, centre) <= inner && (!occupied.TryGetValue(p, out var group) || group.All(u => u.Id == s.Id)), occupied));
            intent.Decision = "士兵守护移动目标"; return intent;
        }
        private Intent PlanSafeLeader(BattleSoldier s, BattleSoldier anchor, GridPosition centre, int radius, Dictionary<GridPosition, List<BattleSoldier>> occupied, Intent intent)
        {
            Func<GridPosition, bool> safe = p => anchor == null ? OwnDeployment(s.Side, p) : GridPosition.Distance(p, anchor.Cell) <= radius && Vision.Observes(anchor,p);
            bool preferZone = Board.Positions().Any(p => Board.Walkable(p) && safe(p) && OwnDeployment(s.Side, p));
            if (!safe(s.Cell) || preferZone && !OwnDeployment(s.Side, s.Cell))
            {
                Func<GridPosition, bool> available = p => safe(p) && (!occupied.TryGetValue(p, out var units) || units.All(u => u.Id == s.Id));
                var path = FindPath(s, p => available(p) && (!preferZone || OwnDeployment(s.Side, p)), occupied);
                if (path.Count == 0 && preferZone) path = FindPath(s, available, occupied);
                SetMove(intent, path);
            }
            intent.GoalCell = anchor?.Cell ?? (intent.Path.Count > 0 ? intent.Path.Last() : centre); intent.Decision = anchor == null ? "安全策略：优先留在部署区" : "安全策略：守护高序号友方队长"; return intent;
        }
        private Intent PlanFleeLeader(BattleSoldier s, List<BattleSoldier> threats, BattleSoldier anchor, Dictionary<GridPosition, List<BattleSoldier>> occupied, Intent intent)
        {
            var routes = new Dictionary<GridPosition, List<GridPosition>> { [s.Cell] = new List<GridPosition> { s.Cell } }; var frontier = new Queue<GridPosition>(); frontier.Enqueue(s.Cell);
            while (frontier.Count > 0)
            {
                var from = frontier.Dequeue(); if (routes[from].Count - 1 >= MovementSpeed(s)) continue;
                foreach (var d in Directions)
                {
                    var next = from + d; if (!Board.CanStep(from,next) || routes.ContainsKey(next) || occupied.TryGetValue(next, out var group) && group.Any(u => u.Side != s.Side)) continue;
                    routes[next] = new List<GridPosition>(routes[from]) { next }; frontier.Enqueue(next);
                }
            }
            int current = threats.Min(e => GridPosition.Distance(e.Cell, s.Cell));
            var best = routes.Keys.Where(p => p == s.Cell || !occupied.TryGetValue(p, out var group) || group.Count(u => u.Id != s.Id) < 2).OrderByDescending(p => threats.Min(e => GridPosition.Distance(e.Cell, p)))
                .ThenBy(p => OwnDeployment(s.Side, p) ? 0 : anchor != null && Vision.Observes(anchor,p) ? 1 : 2)
                .ThenBy(p => occupied.TryGetValue(p, out var group) && group.Any(u => u.Id != s.Id) ? 1 : 0).ThenBy(p => routes[p].Count).ThenBy(p => p.Z * Board.Width + p.X).First();
            if (best != s.Cell && threats.Min(e => GridPosition.Distance(e.Cell, best)) >= current) SetMove(intent, routes[best]);
            intent.GoalCell = best; intent.Decision = "逃跑：队长远离已发现威胁"; return intent;
        }
    }
}

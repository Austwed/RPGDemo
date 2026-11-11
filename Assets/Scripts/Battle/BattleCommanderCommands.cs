using System;
using System.Collections.Generic;
using System.Linq;

namespace BaseConquest.Battle
{
    [Serializable]
    public sealed class CommanderLoadout
    {
        public int Melee, Ranged, Cavalry;
        public int GuardRange = 3, AlertRange = 6, AttackRange = 9, LeadRange = 3;
        public SquadStrategy Strategy = SquadStrategy.Attack;
        public int Total => Melee + Ranged + Cavalry;
        public int Count(SoldierKind kind) => kind == SoldierKind.Melee ? Melee : kind == SoldierKind.Ranged ? Ranged : kind == SoldierKind.Cavalry ? Cavalry : 0;
        public void Set(SoldierKind kind, int count) { if (kind == SoldierKind.Melee) Melee = count; else if (kind == SoldierKind.Ranged) Ranged = count; else if (kind == SoldierKind.Cavalry) Cavalry = count; }
        public CommanderLoadout Clone() => new CommanderLoadout { Melee = Melee, Ranged = Ranged, Cavalry = Cavalry, GuardRange = GuardRange, AlertRange = AlertRange, AttackRange = AttackRange, LeadRange = Math.Max(1, Math.Min(32, LeadRange)), Strategy = Strategy };
    }

    public sealed partial class BattleSimulation
    {
        public const int CommanderCapacity = 6; // 本轮测试规模，不是成品养成上限。
        public int Capacity(BattleSide side) => Scenario.Army(side).Capacity;
        public static readonly SoldierKind[] TroopKinds = { SoldierKind.Melee, SoldierKind.Ranged, SoldierKind.Cavalry };
        public int CommanderSquadId(BattleSide side, int slot) => (int)side * 3 + slot;
        public BattleSquad CommanderSquad(BattleSide side, int slot) => slot >= 0 && slot < Scenario.Army(side).HeroCount ? Squads[CommanderSquadId(side, slot)] : null;
        public BattleSoldier Commander(BattleSide side, int slot) => Soldier(CommanderSquad(side, slot)?.CommanderId ?? -1);
        public BattleSoldier CommanderFor(BattleSoldier member) => member == null ? null : Scenario.CommanderControl ? Soldier(Squads[member.SquadId].CommanderId) : member;
        public bool SelectableCommander(BattleSoldier s) => s != null && s.IsActive && (!Scenario.CommanderControl || s.Kind == SoldierKind.Hero);
        public int Unassigned(BattleSide side, SoldierKind kind) => Math.Max(0, Scenario.Army(side).Count(kind) - Scenario.Army(side).Commanders.Sum(c => c.Count(kind)));
        public int CommanderRemaining(BattleSide side, int slot, SoldierKind kind)
        {
            var squad = CommanderSquad(side, slot); if (squad == null) return 0;
            if (ModernRules && squad.MergedInto >= 0) return 0;
            int budget = kind == SoldierKind.Hero ? 1 : ModernRules ? squad.SupplySlots.Sum(i => Scenario.Army(side).Commanders[i].Count(kind)) : Scenario.Army(side).Commanders[slot].Count(kind);
            return Math.Max(0, budget - squad.Soldiers.Count(s => s.Kind == kind && !s.DeveloperSpawned));
        }
        private void NormalizeFormation(BattleSide side)
        {
            if (Scenario.Leaderless(side)) return;
            var army = Scenario.Army(side); bool fresh = army.Commanders == null || army.Commanders.Count == 0;
            army.Commanders ??= new List<CommanderLoadout>();
            while (army.Commanders.Count < 3) army.Commanders.Add(new CommanderLoadout());
            while (army.Commanders.Count > 3) army.Commanders.RemoveAt(army.Commanders.Count - 1);
            foreach (SoldierKind kind in TroopKinds)
            {
                int left = army.Count(kind);
                for (int slot = 0; slot < 3; slot++)
                {
                    var loadout = army.Commanders[slot];
                    int other = loadout.Total - loadout.Count(kind);
                    int count = slot < army.HeroCount ? Math.Min(left, Math.Min(Math.Max(0, loadout.Count(kind)), Math.Max(0, Capacity(side) - other))) : 0;
                    loadout.Set(kind, count); left -= count;
                }
            }
            if (fresh) DistributeFormation(side);
        }
        public bool AdjustAllocation(BattleSide side, int slot, SoldierKind kind, int delta)
        {
            var squad = CommanderSquad(side, slot);
            if (!Scenario.CommanderControl || Phase != BattlePhase.Deployment || squad == null || !TroopKinds.Contains(kind) || Math.Abs(delta) != 1) return false;
            var loadout = Scenario.Army(side).Commanders[slot]; int count = loadout.Count(kind);
            if (delta > 0 && (Unassigned(side, kind) == 0 || loadout.Total >= Capacity(side))) return false;
            if (delta < 0 && count <= squad.Soldiers.Count(s => s.Kind == kind && !s.DeveloperSpawned)) return false;
            loadout.Set(kind, count + delta); return true;
        }
        public bool DistributeFormation(BattleSide side)
        {
            if (Phase != BattlePhase.Deployment || Soldiers.Any(s => s.Side == side)) return false;
            var army = Scenario.Army(side);
            foreach (var c in army.Commanders) { c.Melee = c.Ranged = c.Cavalry = 0; }
            foreach (SoldierKind kind in TroopKinds)
                for (int n = 0; n < army.Count(kind); n++)
                {
                    int slot = Enumerable.Range(0, army.HeroCount).Where(i => army.Commanders[i].Total < Capacity(side)).OrderBy(i => army.Commanders[i].Count(kind)).ThenBy(i => army.Commanders[i].Total).ThenBy(i => i).DefaultIfEmpty(-1).First();
                    if (slot < 0) break;
                    var c = army.Commanders[slot]; c.Set(kind, c.Count(kind) + 1);
                }
            return true;
        }
        private int DeploymentSlot(BattleSide side, SoldierKind kind, int requested)
        {
            if (!Scenario.CommanderControl) return -1;
            if (Scenario.Leaderless(side)) return requested;
            if (requested >= 0) return requested;
            for (int slot = 0; slot < Scenario.Army(side).HeroCount; slot++)
                if (CommanderRemaining(side, slot, kind) > 0 && (kind == SoldierKind.Hero || Commander(side, slot)?.IsActive == true)) return slot;
            return -1;
        }
        private int FormationStock(BattleSide side, SoldierKind kind)
        {
            int result = 0;
            for (int slot = 0; slot < Scenario.Army(side).HeroCount; slot++)
            {
                // 战前未入场队长仍可部署；战中队长不能复活，其失能后预备队不可继续投放。
                if (Phase != BattlePhase.Deployment && (kind == SoldierKind.Hero || Commander(side, slot)?.IsActive != true)) continue;
                result += CommanderRemaining(side, slot, kind);
            }
            return result;
        }
        private List<BattleSquad> SelectedCommanderSquads(IEnumerable<int> commanderIds)
        {
            var leaders = commanderIds.Distinct().Select(Soldier).ToList();
            if (Phase != BattlePhase.Fighting || leaders.Count == 0 || leaders.Any(s => !SelectableCommander(s)) || leaders.Any(s => s.Side != leaders[0].Side)) return new List<BattleSquad>();
            return leaders.Where(s => s.Command != SquadCommand.Retreat).Select(s => Squads[s.SquadId]).ToList();
        }
        public bool MoveCommanders(IEnumerable<int> ids, GridPosition center)
        {
            if (!Control.Allows("order")) return false;
            if (ModernRules) return MoveModernCommanders(ids, center);
            var squads = SelectedCommanderSquads(ids); if (squads.Count == 0) return false;
            if (!MoveUnits(squads.SelectMany(q => q.Soldiers).Where(CanCommand).Select(s => s.Id), center)) return false;
            foreach (var q in squads) { q.IssuedCommand = SquadCommand.Move; q.GuardCells.Clear(); }
            return true;
        }
        public bool FocusCommanders(IEnumerable<int> ids, int targetId)
        {
            if (!Control.Allows("order")) return false;
            var squads = SelectedCommanderSquads(ids); var target = Soldier(targetId);
            if (squads.Count == 0 || target == null || !target.IsActive || target.Side == squads[0].Side || !Vision.CanSee(squads[0].Side, target)) return false;
            foreach (var q in squads)
            {
                ClearGuardState(q);
                q.IssuedCommand = SquadCommand.Focus; q.GuardCells.Clear();
                foreach (var s in q.Soldiers.Where(CanCommand)) SetOrder(s, SquadCommand.Focus, targetId);
            }
            CommandsIssued++; return true;
        }
        public bool OrderCommanders(IEnumerable<int> ids, SquadCommand command)
        {
            if (!Control.Allows("order")) return false;
            if (command != SquadCommand.Hold && command != SquadCommand.Auto && command != SquadCommand.Retreat) return false;
            var squads = SelectedCommanderSquads(ids); if (squads.Count == 0) return false;
            foreach (var q in squads)
            {
                ClearGuardState(q);
                q.IssuedCommand = command; q.GuardCells.Clear(); if (command == SquadCommand.Auto || command == SquadCommand.Retreat) q.SkillQueued = false;
                foreach (var s in q.Soldiers.Where(CanCommand)) SetOrder(s, command);
                if (command == SquadCommand.Hold) EstablishGuard(q);
            }
            CommandsIssued++; return true;
        }
        public bool RetreatArmy(BattleSide side)
        {
            if (!Control.Allows("order")) return false;
            if (ModernRules) return false; // 新流程仅开放所选队撤退，不允许全军入口绕过指挥权限。
            if (Phase != BattlePhase.Fighting) return false;
            bool changed = false;
            foreach (var q in Squads.Where(q => q.Side == side))
            {
                changed |= Retreat(q.Id); q.IssuedCommand = SquadCommand.Retreat; q.GuardCells.Clear();
            }
            return changed;
        }
        private void InheritCommanderOrder(BattleSoldier s)
        {
            var q = Squads[s.SquadId]; var leader = Soldier(q.CommanderId);
            if (q.IssuedCommand == SquadCommand.Focus) { SetOrder(s, SquadCommand.Focus, leader?.FocusTargetId ?? -1); return; }
            if (q.IssuedCommand == SquadCommand.Retreat) { SetOrder(s, SquadCommand.Retreat); return; }
            if (q.IssuedCommand == SquadCommand.Hold || q.IssuedCommand == SquadCommand.Move)
            {
                GridPosition center = leader?.MoveGoal ?? s.Cell;
                var occupied = KnownOccupancy(s.Side, Occupancy());
                var goal = Board.Positions().Where(p => Board.Walkable(p) && (q.GuardCells.Count == 0 || q.GuardCells.Contains(p)) && !Soldiers.Any(u => u.IsActive && u.Id != s.Id && u.MoveGoal == p) && (!occupied.TryGetValue(p, out var units) || units.All(u => u.Id == s.Id)))
                    .OrderBy(p => GridPosition.Distance(p, center)).Select(p => (GridPosition?)p).FirstOrDefault(p => FindPath(s, c => c == p.Value, occupied).Count > 0);
                if (goal.HasValue) { SetOrder(s, SquadCommand.Move); s.MoveGoal = goal.Value; return; }
                SetOrder(s, SquadCommand.Hold); return;
            }
            SetOrder(s, SquadCommand.Auto);
        }
        private void EstablishGuard(BattleSquad q)
        {
            var alive = q.Soldiers.Where(s => s.IsActive).ToList(); if (alive.Count == 0) return;
            // 首版驻守区域是当前队形包围矩形，不允许成员追出边界。
            int x0 = alive.Min(s => s.Cell.X), x1 = alive.Max(s => s.Cell.X), z0 = alive.Min(s => s.Cell.Z), z1 = alive.Max(s => s.Cell.Z);
            foreach (GridPosition p in Board.Positions()) if (Board.Walkable(p) && p.X >= x0 && p.X <= x1 && p.Z >= z0 && p.Z <= z1) q.GuardCells.Add(p);
        }
        private void UpdateCommanderAreas()
        {
            if (!Scenario.CommanderControl) return;
            foreach (var q in Squads)
                if ((q.IssuedCommand == SquadCommand.Move || q.IssuedCommand == SquadCommand.Focus) && q.Soldiers.Any(s => s.IsActive) && q.Soldiers.Where(s => s.IsActive).All(s => s.Command == SquadCommand.Hold))
                { q.IssuedCommand = SquadCommand.Hold; q.GuardCells.Clear(); EstablishGuard(q); }
        }
        private Intent PlanCommanderGuard(BattleSoldier s, List<BattleSoldier> enemies, Dictionary<GridPosition, List<BattleSoldier>> occupied, Intent intent)
        {
            var q = Squads[s.SquadId];
            if (q.GuardCells.Count == 0) return intent;
            var path = FindPath(s, p => q.GuardCells.Contains(p) && CanStandToAttack(s, p, occupied) && enemies.Any(t => CanAttack(s, p, t)), occupied, p => q.GuardCells.Contains(p));
            if (path.Count > 1) SetMove(intent, path);
            return intent;
        }
        public const int FollowRadius = 3, GuardFollowRadius = 2;
        private List<GridPosition> ReturnToCommander(BattleSoldier s, BattleSoldier leader, Dictionary<GridPosition, List<BattleSoldier>> occupied, int radius) =>
            FindPath(s, p => GridPosition.Distance(p, leader.Cell) <= radius && (!occupied.TryGetValue(p, out var units) || units.All(u => u.Side == s.Side)), occupied);
        private Intent PlanCommanderProtection(BattleSoldier s, List<BattleSoldier> enemies, Dictionary<GridPosition, List<BattleSoldier>> occupied, Intent intent)
        {
            var q = Squads[s.SquadId]; var leader = Soldier(q.CommanderId); bool guarding = s.Command == SquadCommand.Hold;
            int radius = guarding ? GuardFollowRadius : FollowRadius;
            if (leader?.IsActive == true && s.Id != leader.Id && GridPosition.Distance(s.Cell, leader.Cell) > radius)
            {
                SetMove(intent, ReturnToCommander(s, leader, occupied, Math.Max(1, radius - 1))); intent.Decision = "回到指挥者身边"; return intent;
            }
            if (leader?.IsActive == true)
                enemies = enemies.OrderByDescending(t => t.TargetId == leader.Id || t.FocusTargetId == leader.Id || CanAttack(t, t.Cell, leader))
                    .ThenBy(t => GridPosition.Distance(leader.Cell, t.Cell)).ThenByDescending(t => t.IsTransit).ThenBy(t => t.Id).ToList();
            if (s.Kind != SoldierKind.Hero) enemies = RoleTargets(s, enemies).ToList();
            var target = enemies.FirstOrDefault(t => CanAttack(s, s.Cell, t));
            if (!s.IsTransit && target != null) { intent.Action = RoundAction.Attack; intent.Target = target; intent.Decision = "优先保护指挥者"; return intent; }
            if (leader?.IsActive != true) return intent;
            if (s.Id == leader.Id && !guarding && enemies.Count > 0) return PlanFocus(s, enemies[0], occupied);
            Func<GridPosition, bool> permitted = p => GridPosition.Distance(p, leader.Cell) <= radius && (!guarding || q.GuardCells.Count == 0 || q.GuardCells.Contains(p));
            foreach (var enemy in enemies)
            {
                var path = FindPath(s, p => permitted(p) && CanStandToAttack(s, p, occupied) && CanAttack(s, p, enemy), occupied, permitted);
                if (path.Count > 1) { SetMove(intent, path); intent.Target = enemy; intent.Decision = "队形内护卫指挥者"; return intent; }
            }
            if (s.Id == leader.Id && !guarding && Scenario.FogEnabled)
            {
                var path = FindPath(s, p => p != s.Cell && !Vision.Explored(s.Side, p), occupied);
                if (path.Count == 0) path = FindPath(s, p => p != s.Cell && !Vision.Visible(s.Side, p), occupied);
                SetMove(intent, path); intent.Decision = "小队长带队侦察";
            }
            return intent;
        }
        private Intent PlanCommanderAuto(BattleSoldier s, Dictionary<GridPosition, List<BattleSoldier>> occupied, Intent intent)
        {
            var q = Squads[s.SquadId]; var leader = Soldier(q.CommanderId);
            if (leader?.IsActive != true) return intent;
            if (s.Id != leader.Id && GridPosition.Distance(s.Cell, leader.Cell) > 3)
            {
                SetMove(intent, FindPath(s, p => GridPosition.Distance(p, leader.Cell) <= 2 && (!occupied.TryGetValue(p, out var units) || units.All(u => u.Side == s.Side)), occupied));
                return intent;
            }
            var target = Soldiers.Where(t => t.IsActive && t.Side != s.Side && Vision.CanSee(s.Side, t)).OrderBy(t => GridPosition.Distance(leader.Cell, t.Cell)).ThenBy(t => t.Id).FirstOrDefault();
            if (target != null) return PlanFocus(s, target, occupied);
            if (s.Id == leader.Id && Scenario.FogEnabled)
            {
                var path = FindPath(s, p => p != s.Cell && !Vision.Explored(s.Side, p), occupied);
                if (path.Count == 0) path = FindPath(s, p => p != s.Cell && !Vision.Visible(s.Side, p), occupied);
                SetMove(intent, path); intent.Decision = "小队长带队侦察";
            }
            return intent;
        }
    }
}

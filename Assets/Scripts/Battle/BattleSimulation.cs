using System;
using System.Collections.Generic;
using System.Linq;

namespace BaseConquest.Battle
{
    // 轮初快照 -> 每人一个意图 -> 占格冲突 -> 同时结算。表现层决定何时推进，不参与规则。
    public sealed partial class BattleSimulation
    {
        public readonly BattleScenario Scenario;
        public BattleBoard Board => Scenario.Board;
        public readonly BattleVision Vision;
        public readonly List<BattleSquad> Squads = new List<BattleSquad>();
        public readonly List<BattleSoldier> Soldiers = new List<BattleSoldier>();
        public BattlePhase Phase { get; private set; } = BattlePhase.Deployment;
        // 控制权限（P36）：默认本地全开；远程防守换成 BattleControlPolicy.RemoteDefense，只放策略与查看。
        public BattleControlPolicy Control = BattleControlPolicy.Local;
        public BattleReport Report { get; private set; }
        public int RoundNumber { get; private set; }
        public int CommandsIssued { get; private set; }
        public int AbilitiesUsed { get; private set; }
        public string LastRoundSummary { get; private set; } = "等待开始";
        private int nextId, idleRounds;
        private readonly int[] skillCooldowns = new int[2];
        private readonly GridPosition?[] queuedSkills = new GridPosition?[2];
        private readonly int[] queuedHeroes = { -1, -1 };
        private static readonly GridPosition[] Directions = { new GridPosition(1, 0), new GridPosition(0, 1), new GridPosition(-1, 0), new GridPosition(0, -1) };

        private sealed class Intent
        {
            public BattleSoldier Actor;
            public RoundAction Action;
            public GridPosition Destination;
            public BattleSoldier Target;
            public GridPosition? ObstacleTarget;
            public float AttackPower;
            public bool CriticalAllowed;
            public GridPosition? GoalCell, ReplannedGoal;
            public string Decision = "";
            public double DetourRounds = double.PositiveInfinity, ClearRounds = double.PositiveInfinity;
            public readonly List<GridPosition> Path = new List<GridPosition>();
            public readonly List<BattleSoldier> SkillTargets = new List<BattleSoldier>();
        }

        public BattleSimulation(BattleScenario scenario)
        {
            Scenario = (scenario ?? new BattleScenario()).Clone();
            if (ModernRules) { Board.Cell(ExitCell(BattleSide.A)).Terrain = TerrainKind.Ground; Board.Cell(ExitCell(BattleSide.B)).Terrain = TerrainKind.Ground; }
            Vision = new BattleVision(this);
            if (Scenario.CommanderControl)
            {
                foreach (BattleSide side in Enum.GetValues(typeof(BattleSide)))
                {
                    NormalizeFormation(side);
                    for (int slot = 0; slot < 3; slot++)
                    {
                        bool independent = Scenario.Leaderless(side) && slot == 0;
                        Squads.Add(new BattleSquad { Id = Squads.Count, Side = side, Kind = independent ? SoldierKind.Melee : SoldierKind.Hero, CommanderSlot = independent ? -1 : slot, Name = independent ? SideName(side) + "士兵" : SideName(side) + "小队" + (slot + 1) });
                    }
                }
            }
            else
            {
                // 旧兵种队仅供底层回归检查，游戏UI不开放单兵操控。
                for (int side = 0; side < 2; side++) for (int kind = 0; kind < 3; kind++)
                    Squads.Add(new BattleSquad { Id = Squads.Count, Side = (BattleSide)side, Kind = (SoldierKind)kind, Name = SideName((BattleSide)side) + KindName((SoldierKind)kind) });
                foreach (var kind in new[] { SoldierKind.Cavalry, SoldierKind.Mage })
                    foreach (BattleSide side in Enum.GetValues(typeof(BattleSide)))
                        Squads.Add(new BattleSquad { Id = Squads.Count, Side = side, Kind = kind, Name = SideName(side) + KindName(kind) });
            }
            if (ModernRules)
                foreach (BattleSide side in Enum.GetValues(typeof(BattleSide)))
                {
                    captainSlots[(int)side] = Scenario.Army(side).CaptainSlot;
                    if (!Scenario.Leaderless(side))
                        foreach (var q in Squads.Where(q => q.Side == side)) q.SupplySlots.Add(q.CommanderSlot);
                }
            UnitPlacement[] initial = Scenario.Placements.ToArray();
            Scenario.Placements.Clear();
            if (Scenario.CommanderControl && initial.Any(p => p.CommanderSlot < 0)) initial = initial.OrderBy(p => p.Kind == SoldierKind.Hero ? 0 : 1).ToArray();
            foreach (UnitPlacement p in initial) Deploy(p.Side, p.Kind, p.Cell, p.CommanderSlot);
            Vision.Refresh();
        }

        public int Remaining(BattleSide side, SoldierKind kind) => Scenario.CommanderControl && !Scenario.Leaderless(side) ? FormationStock(side, kind) : Math.Max(0, Scenario.Army(side).Count(kind) - Soldiers.Count(s => s.Side == side && s.Kind == kind && !s.DeveloperSpawned));
        public BattleSoldier Soldier(int id) => Soldiers.Find(s => s.Id == id);
        public BattleSoldier At(GridPosition cell) => Soldiers.Find(s => s.IsActive && s.Cell == cell);
        public List<BattleSoldier> UnitsAt(GridPosition cell) => Soldiers.Where(s => s.IsActive && s.Cell == cell).OrderBy(s => s.IsTransit).ThenBy(s => s.Id).ToList();
        public BattleSoldier AttackTargetAt(GridPosition cell) => UnitsAt(cell).OrderByDescending(s => s.IsTransit).ThenBy(s => s.Id).FirstOrDefault();
        public int SkillCooldown(BattleSide side) => skillCooldowns[(int)side];
        public bool SkillQueued(BattleSide side) => queuedSkills[(int)side].HasValue;
        public int SquadId(BattleSide side, SoldierKind kind) => Scenario.CommanderControl ? CommanderSquadId(side, 0) : kind == SoldierKind.Cavalry ? 6 + (int)side : kind == SoldierKind.Mage ? 8 + (int)side : (int)side * 3 + (int)kind;

        public bool Deploy(BattleSide side, SoldierKind kind, GridPosition cell, int commanderSlot = -1)
        {
            commanderSlot = DeploymentSlot(side, kind, commanderSlot);
            if (!CanDeploy(side, kind, cell, commanderSlot)) return false;
            SoldierStats stats = (kind==SoldierKind.Hero ? Scenario.Army(side).CommanderProfile(commanderSlot) : Scenario.Army(side).Stats(kind)).Clone();
            var s = new BattleSoldier { Id = nextId++, Side = side, Kind = kind, SquadId = Scenario.CommanderControl && !Scenario.Leaderless(side) ? CommanderSquadId(side, commanderSlot) : SquadId(side, kind), Cell = cell, PreviousCell = cell, MoveGoal = cell, Stats = stats, Health = stats.Health };
            s.OriginCommanderSlot = commanderSlot;
            if(kind==SoldierKind.Hero)s.AppearanceId=CommanderAppearance(side,commanderSlot);
            Soldiers.Add(s); Squads[s.SquadId].Soldiers.Add(s);
            if (Scenario.CommanderControl && kind == SoldierKind.Hero) Squads[s.SquadId].CommanderId = s.Id;
            if (Scenario.CommanderControl && Phase == BattlePhase.Fighting) InheritCommanderOrder(s);
            s.BattlePower = stats.BaseBattlePower;
            if (Phase == BattlePhase.Deployment) Scenario.Placements.Add(new UnitPlacement { Side = side, Kind = kind, Cell = cell, CommanderSlot = commanderSlot });
            else { idleRounds = 0; RefreshRoles(); }
            Vision.Refresh();
            return true;
        }
        public string CommanderAppearance(BattleSide side,int slot)
        {
            var appearances=Scenario.Army(side).CommanderAppearances;
            return appearances!=null&&slot>=0&&slot<appearances.Count ? appearances[slot]??"" : "";
        }
        public bool CanDeploy(BattleSide side, SoldierKind kind, GridPosition cell, int commanderSlot = -1)
        {
            if (Scenario.Leaderless(side))
            {
                if (Phase != BattlePhase.Deployment || commanderSlot != -1 || !TroopKinds.Contains(kind)) return false;
            }
            else if (Scenario.CommanderControl)
            {
                commanderSlot = DeploymentSlot(side, kind, commanderSlot);
                if (CommanderSquad(side, commanderSlot) == null || CommanderRemaining(side, commanderSlot, kind) == 0 || (kind != SoldierKind.Hero && Commander(side, commanderSlot)?.IsActive != true)) return false;
            }
            if (Phase == BattlePhase.Finished || !Board.Walkable(cell) || Remaining(side, kind) == 0 || !Enum.IsDefined(typeof(SoldierKind), kind)) return false;
            if (Board.Cell(cell).Zone != (side == BattleSide.A ? DeploymentZone.A : DeploymentZone.B)) return false;
            var group = UnitsAt(cell);
            return group.All(s => s.Side == side) && group.Count < (Phase == BattlePhase.Deployment ? 1 : 2);
        }

        public bool Remove(GridPosition cell)
        {
            if (Phase != BattlePhase.Deployment) return false;
            BattleSoldier s = At(cell); if (s == null) return false;
            if (Scenario.CommanderControl && s.Kind == SoldierKind.Hero)
                foreach (var member in Squads[s.SquadId].Soldiers.Where(u => u.Id != s.Id).ToArray()) Remove(member.Cell);
            Soldiers.Remove(s); Squads[s.SquadId].Soldiers.Remove(s);
            if (Scenario.CommanderControl && s.Kind == SoldierKind.Hero) Squads[s.SquadId].CommanderId = -1;
            Scenario.Placements.RemoveAll(p => p.Cell == cell);
            Vision.Refresh();
            return true;
        }

        public bool Paint(GridPosition cell, BoardBrush brush)
        {
            if (ModernRules && (cell == ExitCell(BattleSide.A) || cell == ExitCell(BattleSide.B)) && (brush == BoardBrush.Water || brush == BoardBrush.Wall || brush == BoardBrush.Building)) return false;
            if (Phase != BattlePhase.Deployment || !Board.Paint(cell, brush)) return false;
            leadRadii.Clear();
            BattleSoldier s = At(cell);
            if (s != null && (!Board.Walkable(cell) || Board.Cell(cell).Zone != (s.Side == BattleSide.A ? DeploymentZone.A : DeploymentZone.B))) Remove(cell);
            Vision.Refresh();
            return true;
        }

        public void ApplyArmy(BattleSide side, ArmyDefinition definition)
        {
            if (Phase != BattlePhase.Deployment || Scenario.Leaderless(side)) return;
            if (side == BattleSide.A) Scenario.A = definition.Clone(); else Scenario.B = definition.Clone();
            foreach (SoldierKind kind in Enum.GetValues(typeof(SoldierKind)))
            {
                var members = Soldiers.FindAll(s => s.Side == side && s.Kind == kind);
                for (int i = members.Count - 1; i >= Scenario.Army(side).Count(kind); i--) Remove(members[i].Cell);
                foreach (BattleSoldier s in Soldiers.FindAll(u => u.Side == side && u.Kind == kind)) { s.Stats = Scenario.Army(side).Stats(kind).Clone(); s.Health = s.Stats.Health; }
            }
            if (Scenario.CommanderControl)
            {
                NormalizeFormation(side);
                foreach (var unit in Soldiers.Where(u => u.Side == side).ToArray())
                {
                    int slot = Squads[unit.SquadId].CommanderSlot;
                    if (slot >= Scenario.Army(side).HeroCount || CommanderRemaining(side, slot, unit.Kind) == 0 && Squads[unit.SquadId].Soldiers.Count(u => u.Kind == unit.Kind) > (unit.Kind == SoldierKind.Hero ? 1 : Scenario.Army(side).Commanders[slot].Count(unit.Kind))) Remove(unit.Cell);
                }
            }
            Vision.Refresh();
        }

        public int AutoDeploy()
        {
            if (Phase != BattlePhase.Deployment) return 0;
            if (ModernRules) return AutoDeployFormation(BattleSide.A) + AutoDeployFormation(BattleSide.B);
            int total = 0;
            foreach (BattleSide side in Enum.GetValues(typeof(BattleSide)))
            {
                var cells = Board.Positions().Where(p => Board.Walkable(p) && Board.Cell(p).Zone == (side == BattleSide.A ? DeploymentZone.A : DeploymentZone.B) && At(p) == null)
                    .OrderBy(p => Math.Abs(p.Z - Board.Height / 2)).ThenBy(p => side == BattleSide.A ? -p.X : p.X).ToList();
                int index = 0;
                foreach (SoldierKind kind in DeploymentKinds())
                    while (Remaining(side, kind) > 0 && index < cells.Count) { if (Deploy(side, kind, cells[index++])) total++; }
            }
            return total;
        }
        public int AutoDeploy(BattleSide side)
        {
            if (Phase == BattlePhase.Finished) return 0;
            if (ModernRules) return AutoDeployFormation(side);
            int total = 0;
            foreach (SoldierKind kind in DeploymentKinds())
                foreach (GridPosition p in Board.Positions().OrderBy(p => UnitsAt(p).Count).ThenBy(p => Math.Abs(p.Z - Board.Height / 2)).ThenBy(p => side == BattleSide.A ? -p.X : p.X))
                    while (CanDeploy(side, kind, p)) { Deploy(side, kind, p); total++; }
            return total;
        }

        private IEnumerable<SoldierKind> DeploymentKinds() => Scenario.CommanderControl ? new[] { SoldierKind.Hero, SoldierKind.Melee, SoldierKind.Ranged, SoldierKind.Cavalry, SoldierKind.Mage } : Enum.GetValues(typeof(SoldierKind)).Cast<SoldierKind>();

        public bool Start(out string message)
        {
            if (Phase != BattlePhase.Deployment) { message = "当前已开战"; return false; }
            if (!Soldiers.Exists(s => s.Side == BattleSide.A) || !Soldiers.Exists(s => s.Side == BattleSide.B)) { message = "双方各至少需要投放一名单位"; return false; }
            if (Scenario.CommanderControl && (!Soldiers.Any(s => s.Side == BattleSide.A && s.Kind == SoldierKind.Hero) || !Scenario.BanditHideout && !Soldiers.Any(s => s.Side == BattleSide.B && s.Kind == SoldierKind.Hero)))
            { message = Scenario.BanditHideout ? "请先部署我方队长" : "双方至少各部署一名小队长"; return false; }
            if (Scenario.BanditHideout && TroopKinds.Any(kind => Remaining(BattleSide.B, kind) > 0)) { message = "敌军部署未完成"; return false; }
            foreach (BattleSoldier s in Soldiers) s.BattlePower = s.Stats.BaseBattlePower;
            InvalidateUndo(); Phase = BattlePhase.Fighting; InitializeCommanderSkills(); RefreshRoles(); Vision.Refresh(); message = "已开战，等待推进第一轮。库存可在轮次之间继续部署。"; return true;
        }

        private bool CanCommand(BattleSoldier s) => Phase == BattlePhase.Fighting && s != null && s.IsActive && s.Command != SquadCommand.Retreat;
        private List<BattleSoldier> CommandableGroup(int id) => id >= 0 && id < Squads.Count ? Squads[id].Soldiers.Where(CanCommand).ToList() : new List<BattleSoldier>();
        private void SetOrder(BattleSoldier s, SquadCommand command, int target = -1)
        {
            s.Command = command; s.FocusTargetId = target; s.TargetId = target; s.ResumeFollowing = false; s.RetainedCombatTargetId = -1;
            if (command == SquadCommand.Hold) s.MoveGoal = s.Cell;
            if (s.Kind == SoldierKind.Hero) CancelSkill(s.Side);
        }
        // 旧单兵方法保留为底层行动工具和回归夹具；游戏输入仅调用Commanders队级入口。
        public bool MoveUnit(int id, GridPosition destination)
        {
            BattleSoldier s = Soldier(id);
            if (!CanCommand(s) || !Board.Walkable(destination) || (At(destination) is BattleSoldier blocker && blocker.Side != s.Side && Vision.CanSee(s.Side, blocker))) return false;
            if (FindPath(s, p => p == destination, Occupancy()).Count == 0) return false;
            SetOrder(s, s.Cell == destination ? SquadCommand.Hold : SquadCommand.Move); s.MoveGoal = destination; CommandsIssued++; return true;
        }
        public bool AttackUnit(int id, int targetId)
        {
            BattleSoldier s = Soldier(id), target = Soldier(targetId);
            if (!CanCommand(s) || target == null || !target.IsActive || target.Side == s.Side || !Vision.CanSee(s.Side, target)) return false;
            SetOrder(s, SquadCommand.Focus, targetId); CommandsIssued++; return true;
        }
        public bool HoldUnit(int id) => OrderUnit(id, SquadCommand.Hold);
        public bool AutoUnit(int id) => OrderUnit(id, SquadCommand.Auto);
        public bool RetreatUnit(int id) => OrderUnit(id, SquadCommand.Retreat);
        private bool OrderUnit(int id, SquadCommand command)
        {
            BattleSoldier s = Soldier(id); if (!CanCommand(s)) return false;
            SetOrder(s, command); CommandsIssued++; return true;
        }
        public bool Move(int id, GridPosition center)
        {
            return MoveUnits(CommandableGroup(id).Select(s => s.Id), center);
        }
        public bool MoveUnits(IEnumerable<int> ids, GridPosition center)
        {
            var members = ids.Distinct().Select(Soldier).Where(CanCommand).OrderBy(s => Scenario.CommanderControl && s.Kind == SoldierKind.Hero ? 0 : 1).ThenBy(s => s.Id).ToList();
            if (members.Count == 0 || !Board.Walkable(center) || members.Any(s => s.Side != members[0].Side)) return false;
            if (At(center) is BattleSoldier enemy && enemy.Side != members[0].Side && Vision.CanSee(members[0].Side, enemy)) return false;
            if (members.Count == 1) return MoveUnit(members[0].Id, center);
            var occupied = KnownOccupancy(members[0].Side, Occupancy()); var used = new HashSet<GridPosition>(); var goals = new List<GridPosition>();
            GridPosition anchor = members[0].Cell;
            // A 项：落点优先避开尚未离场的队友当前站位，减少成员被分到队友脚下、执行时要等对方先走的无谓等待。
            var allyCells = members.Where(s => !s.IsTransit).Select(s => s.Cell).ToHashSet();
            foreach (BattleSoldier s in members)
            {
                GridPosition preferred = Scenario.CommanderControl ? center : new GridPosition(center.X + s.Cell.X - anchor.X, center.Z + s.Cell.Z - anchor.Z);
                if (Scenario.CommanderControl && s.Kind == SoldierKind.Ranged) preferred = center + new GridPosition(s.Side == BattleSide.A ? -1 : 1, 0);
                // R1：候选限制在目标周边合理范围，避免为了优先全图空格把落点推到远处；范围内再按贴近度优先、避让队友其次。
                int reach = Math.Max(2, members.Count);
                var choices = Board.Positions().Where(p => Board.Walkable(p) && !used.Contains(p) && GridPosition.Distance(p, preferred) <= reach && (!occupied.TryGetValue(p, out var occupants) || occupants.All(u => members.Contains(u))))
                    .OrderBy(p => GridPosition.Distance(p, preferred))
                    .ThenBy(p => allyCells.Contains(p) && p != s.Cell ? 1 : 0)
                    .ThenBy(p => p.Z * Board.Width + p.X);
                GridPosition? goal = null;
                foreach (GridPosition p in choices) if (p == s.Cell || FindPath(s, c => c == p, occupied).Count > 0) { goal = p; break; }
                if (!goal.HasValue) goal = s.Cell;   // A 项：单人无合法落点时留在原地，不让整队命令无故失败（原来直接 return false）。
                used.Add(goal.Value); goals.Add(goal.Value);
            }
            for (int i = 0; i < members.Count; i++) { SetOrder(members[i], members[i].Cell == goals[i] ? SquadCommand.Hold : SquadCommand.Move); members[i].MoveGoal = goals[i]; }
            CommandsIssued++; return true;
        }
        public bool AttackUnits(IEnumerable<int> ids, int target) { bool changed = false; foreach (int id in ids.Distinct()) changed |= AttackUnit(id, target); return changed; }
        public bool OrderUnits(IEnumerable<int> ids, SquadCommand order)
        {
            if (order != SquadCommand.Hold && order != SquadCommand.Auto && order != SquadCommand.Retreat) return false;
            bool changed = false; foreach (int id in ids.Distinct()) changed |= OrderUnit(id, order); return changed;
        }
        public void CancelOrders(BattleSide side)
        {
            if (Phase != BattlePhase.Fighting) return;
            if (Scenario.CommanderControl) OrderCommanders(Soldiers.Where(s => s.Side == side && SelectableCommander(s)).Select(s => s.Id), SquadCommand.Auto);
            else OrderUnits(Soldiers.Where(s => s.Side == side).Select(s => s.Id), SquadCommand.Auto);
            CancelSkill(side); foreach (var q in Squads.Where(q => q.Side == side)) q.SkillQueued = false;
        }
        public bool Focus(int id, int targetId)
        {
            var members = CommandableGroup(id); BattleSoldier target = Soldier(targetId);
            if (members.Count == 0 || target == null || !target.IsActive || target.Side == members[0].Side || !Vision.CanSee(members[0].Side, target)) return false;
            foreach (BattleSoldier s in members) SetOrder(s, SquadCommand.Focus, targetId);
            CommandsIssued++; return true;
        }
        public bool Auto(int id) => OrderGroup(id, SquadCommand.Auto);
        public bool Hold(int id) => OrderGroup(id, SquadCommand.Hold);
        public bool Retreat(int id) => OrderGroup(id, SquadCommand.Retreat);
        private bool OrderGroup(int id, SquadCommand command)
        {
            var members = CommandableGroup(id); if (members.Count == 0) return false;
            foreach (BattleSoldier s in members) SetOrder(s, command);
            CommandsIssued++; return true;
        }

        public bool QueueSkill(BattleSide side, GridPosition point)
        {
            BattleSoldier hero = Soldiers.Find(s => s.Side == side && s.Kind == SoldierKind.Hero && s.IsActive);
            if (Phase != BattlePhase.Fighting || !Board.Contains(point) || hero == null || hero.IsTransit || hero.Command == SquadCommand.Retreat || SkillCooldown(side) > 0 || GridPosition.Distance(hero.Cell, point) > Scenario.SkillRange || SkillTargets(hero, point).Count == 0) return false;
            queuedSkills[(int)side] = point; queuedHeroes[(int)side] = hero.Id; return true;
        }
        public void CancelSkill(BattleSide side) { queuedSkills[(int)side] = null; queuedHeroes[(int)side] = -1; }
        private List<BattleSoldier> SkillTargets(BattleSoldier hero, GridPosition center) => Soldiers.FindAll(s => s.IsActive && s.Side != hero.Side && Vision.CanSee(hero.Side, s) && GridPosition.Distance(s.Cell, center) <= Scenario.SkillRadius && Board.HasLineOfSight(hero.Cell, s.Cell));

        public bool AdvanceRound(bool automaticSkills = false)
        {
            if (Phase != BattlePhase.Fighting) return false;
            foreach (var unit in Soldiers) unit.MovedThisRound = false;
            ResolveCasualties(); ResolveLeadership();
            if (CommanderVictorySatisfied && !VictoryDeclared) CheckOutcome();
            if (Phase != BattlePhase.Fighting) return false;
            int boundarySkills = ResolveQueuedCommanderSkills();
            if (automaticSkills) boundarySkills += AutoCastCommanderSkills();
            // 同轮脱离或战士倒下都不解除限制；预览和下一轮重新按当前位置判断。
            SnapshotCavalryPins();
            InvalidateUndo(); leadRadii.Clear(); resolvingRound = true; PrepareRescues(); PrepareGuards(); PrepareStrategies();
            var active = Soldiers.Where(s => s.IsActive).OrderBy(s => s.Id).ToList();
            var occupied = Occupancy();
            foreach (BattleSoldier s in Soldiers) { s.PreviousCell = s.Cell; s.PreviousTransit = s.IsTransit; s.PreviousShared = occupied.TryGetValue(s.Cell, out var group) && group.Count > 1; s.LastAction = RoundAction.Wait; s.LastAttackCritical = false; s.LastDamageTaken = 0; s.LastFootwork = 0; s.LastMovePath.Clear(); }
            for (int i = 0; i < 2; i++) if (skillCooldowns[i] > 0) skillCooldowns[i]--;
            var intents = CoordinatedIntents(active, occupied);
            foreach(var intent in intents)if(intent.Actor.DeveloperImmobile&&(intent.Action==RoundAction.Move||intent.Action==RoundAction.Escape)){intent.Action=RoundAction.Wait;intent.Decision="开发者：禁止移动";}
            foreach (var intent in intents) if (intent.ReplannedGoal.HasValue) intent.Actor.MoveGoal = intent.ReplannedGoal.Value;
            foreach (var intent in intents) { intent.AttackPower = AttackPower(intent.Actor); intent.CriticalAllowed = !IsWeak(intent.Actor); }
            queuedSkills[0] = queuedSkills[1] = null;
            var moveWinners = ResolveMoves(intents, occupied, active);
            RecordFootwork(intents, moveWinners);
            bool changed = boundarySkills > 0;
            int moves = 0, attacks = 0, waits = 0;
            foreach (Intent intent in intents)
            {
                BattleSoldier s = intent.Actor;
                s.TargetId = intent.Target?.Id ?? -1; s.LastDecision = intent.Decision;
                if (intent.Action == RoundAction.Move && moveWinners.TryGetValue(s.Id, out var route) && route.Count > 1) { s.MovedThisRound = true; s.LastMovePath.AddRange(route); s.Cell = route[route.Count - 1]; s.LastAction = RoundAction.Move; changed = true; moves++; }
                else if (intent.Action == RoundAction.Attack)
                {
                    s.LastAction = RoundAction.Attack; s.TargetId = intent.Target?.Id??-1; s.LastTargetCell = intent.ObstacleTarget??intent.Target.PreviousCell; attacks++;
                }
                else if (intent.Action == RoundAction.Skill)
                {
                    s.LastAction = RoundAction.Skill; s.LastTargetCell = intent.Destination;
                    skillCooldowns[(int)s.Side] = Scenario.SkillCooldown; AbilitiesUsed++;
                }
                else if (intent.Action == RoundAction.Escape) s.LastAction = RoundAction.Escape;
                else waits++;
            }
            // 所有攻击资格由轮初状态确定，不因本轮移动或同轮倒下取消已确定的攻击。
            foreach (Intent intent in intents)
            {
                if (intent.Action == RoundAction.Attack)
                {
                    if(intent.ObstacleTarget.HasValue){changed|=DamageObstacle(intent.Actor,intent.ObstacleTarget.Value,intent.AttackPower);continue;}
                    intent.Actor.LastAttackCritical = intent.CriticalAllowed && IsCritical(intent.Actor, intent.Target);
                    changed |= Damage(intent.Actor, intent.Target, intent.AttackPower * (intent.Actor.LastAttackCritical ? intent.Actor.Stats.CritMultiplier : 1));
                }
                if (intent.Action == RoundAction.Skill) foreach (BattleSoldier target in intent.SkillTargets) changed |= Damage(intent.Actor, target, Scenario.SkillDamage);
            }
            changed |= HealAfterDamage();
            foreach (Intent intent in intents)
                if (intent.Action == RoundAction.Escape && intent.Actor.IsAlive) { intent.Actor.Escaped = true; changed = true; }
            changed |= ResolveExitArrivals();
            ResolveCasualties(); changed |= ResolveRescueActions(intents);
            ResolveLeadership();
            RefreshRoles();
            RoundNumber++; resolvingRound = false; ReportEffectExpirations();
            Vision.Refresh(true);
            foreach (BattleSoldier s in Soldiers)
            {
                if (!s.IsActive) { s.TargetId = -1; continue; }
                if (s.ResumeFollowing && s.RescueTargetId < 0 && s.CarryingId < 0 && !RetainedCombatValid(s)) SetOrder(s, Squads[s.SquadId].IssuedCommand == SquadCommand.Auto ? SquadCommand.Auto : SquadCommand.Move);
                if (!ModernRules && s.Command == SquadCommand.Move && s.Cell == s.MoveGoal && !s.IsTransit) SetOrder(s, SquadCommand.Hold);
                if (!s.ResumeFollowing && s.Command == SquadCommand.Focus && !(Soldier(s.FocusTargetId)?.IsActive ?? false)) SetOrder(s, SquadCommand.Hold);
                else if (!s.ResumeFollowing && s.Command == SquadCommand.Focus && !Vision.CanSee(s.Side, Soldier(s.FocusTargetId))) SetOrder(s, SquadCommand.Auto);
                if (!(Soldier(s.TargetId)?.IsActive ?? false)) s.TargetId = -1;
            }
            FinishCommanderMoves(); UpdateCommanderAreas();
            idleRounds = changed || skillCooldowns.Any(c => c > 0) || ModernRules && Squads.Any(q => Soldier(q.CommanderId)?.IsActive == true && q.SkillReadyRound > RoundNumber) ? 0 : idleRounds + 1;
            LastRoundSummary = "第 " + RoundNumber + " 轮：移动 " + moves + "，普攻 " + attacks + "，等待 " + waits;
            CheckOutcome(); return true;
        }

        private bool planningActorPath;
        private Intent Plan(BattleSoldier s, Dictionary<GridPosition, List<BattleSoldier>> occupied)
        {
            bool previous=planningActorPath;planningActorPath=true;
            try {return PlanObstacleRoute(s,PlanActor(s,occupied),occupied);} finally {planningActorPath=previous;}
        }
        private Intent PlanActor(BattleSoldier s, Dictionary<GridPosition, List<BattleSoldier>> occupied)
        {
            occupied = KnownOccupancy(s.Side, occupied);
            var intent = new Intent { Actor = s, Action = RoundAction.Wait, Destination = s.Cell };
            if (RoutInProgress && s.Side == BattleSide.B)
            {
                if (IsExit(s.Side, s.Cell)) intent.Action = RoundAction.Escape;
                else SetMove(intent, FindPath(s, p => IsExit(s.Side, p), occupied));
                return intent;
            }
            if (ModernRules && s.Kind == SoldierKind.Hero && IsExit(s.Side, s.Cell)) { intent.Action = RoundAction.Escape; intent.Decision = "队长已到撤离点，离场"; return intent; }
            var rescue = PlanRescue(s, occupied); if (rescue != null) return rescue;
            if (AttackMemberLimited(s) && !AttackAreaContains(s, s.Cell))
            {
                SetMove(intent, FindPath(s, p => AttackAreaContains(s, p), occupied));
                intent.Decision = "已在队长进攻范围外，优先归队"; return intent;
            }
            if (s.ResumeFollowing && CommanderFor(s)?.IsActive == true) return PlanResumingFollower(s, occupied, intent);
            if (s.Command == SquadCommand.Retreat)
            {
                if (IsExit(s.Side, s.Cell)) intent.Action = RoundAction.Escape;
                else SetMove(intent, FindPath(s, p => IsExit(s.Side, p), occupied));
                return intent;
            }
            if (!s.IsTransit && s.Kind == SoldierKind.Hero && queuedHeroes[(int)s.Side] == s.Id && queuedSkills[(int)s.Side].HasValue)
            {
                GridPosition point = queuedSkills[(int)s.Side].Value;
                var targets = SkillTargets(s, point);
                if (skillCooldowns[(int)s.Side] == 0 && GridPosition.Distance(s.Cell, point) <= Scenario.SkillRange && targets.Count > 0)
                {
                    intent.Action = RoundAction.Skill; intent.Destination = point; intent.SkillTargets.AddRange(targets); return intent;
                }
            }
            if (ModernRules && (s.Command == SquadCommand.Auto || s.Command == SquadCommand.Hold) && (!Squads[s.SquadId].GuardAssigned || Squads[s.SquadId].GuardArrived))
            { var retreatingArcher = PlanRangedRetreat(s, occupied, intent); if (retreatingArcher != null) return retreatingArcher; }
            if (ModernRules && Squads[s.SquadId].GuardAssigned && Squads[s.SquadId].GuardArrived) return PlanGuardPatrol(s, occupied, intent);
            if (s.Command == SquadCommand.Move)
            {
                if (ModernRules && s.Cell == s.MoveGoal && !s.IsTransit)
                {
                    var target = Soldiers.Where(t => t.Side != s.Side && t.IsActive && Vision.CanSee(s.Side, t) && CanAttack(s, s.Cell, t)).OrderBy(t => GridPosition.Distance(s.Cell, t.Cell)).ThenByDescending(t => t.IsTransit).ThenBy(t => t.Id).FirstOrDefault();
                    if (target != null) { intent.Action = RoundAction.Attack; intent.Target = target; }
                    intent.Decision = "移动已到达，原地待命（不驻守／追击）"; return intent;
                }
                var leader = ModernRules ? Soldier(Squads[s.SquadId].CommanderId) : null;
                if (ModernRules && s.Kind != SoldierKind.Hero && leader?.IsActive == true)
                {
                    return PlanLedMovement(s, occupied, intent);
                }
                else SetMove(intent, FindPath(s, p => p == s.MoveGoal, occupied));
                return intent;
            }
            if (s.Command == SquadCommand.Focus) return PlanFocus(s, Soldier(s.FocusTargetId), occupied);
            var enemies = Soldiers.Where(t => t.Side != s.Side && t.IsActive && Vision.CanSee(s.Side, t))
                .OrderBy(t => GridPosition.Distance(s.Cell, t.Cell)).ThenByDescending(t => t.IsTransit).ThenBy(t => t.Id).ToList();
            if (ModernRules && CommanderFor(s)?.IsActive != true) return PlanWithoutCommander(s, enemies, occupied, intent);
            if (ModernRules && s.Command == SquadCommand.Auto) return PlanSquadStrategy(s, enemies, occupied, intent);
            if (ModernRules && s.Command == SquadCommand.Hold) return PlanCommanderProtection(s, enemies, occupied, intent);
            BattleSoldier inRange = enemies.Find(t => CanAttack(s, s.Cell, t));
            if (!s.IsTransit && inRange != null) { intent.Action = RoundAction.Attack; intent.Target = inRange; return intent; }
            if (s.Command == SquadCommand.Hold) return Scenario.CommanderControl ? PlanCommanderGuard(s, enemies, occupied, intent) : intent;
            if (Scenario.CommanderControl && s.Command == SquadCommand.Auto) return PlanCommanderAuto(s, occupied, intent);
            if (enemies.Count > 0)
            {
                var path = FindPath(s, p => CanStandToAttack(s, p, occupied) && enemies.Exists(t => CanAttack(s, p, t)), occupied);
                SetMove(intent, path);
                if (path.Count > 0) intent.Target = enemies.Find(t => CanAttack(s, path[path.Count - 1], t));
                else if (s.Command == SquadCommand.Focus) intent.Target = enemies[0];
            }
            else if (s.Command == SquadCommand.Auto) return PlanUnseenMovement(s, occupied, intent);
            return intent;
        }
        private bool CanAttack(BattleSoldier s, GridPosition from, BattleSoldier target) => AttackAreaContains(s, from) && AttackAreaContains(s, target.Cell) && GridPosition.Distance(from, target.Cell) <= s.Stats.Range && Board.HasLineOfSight(from, target.Cell);
        private static bool CanStandToAttack(BattleSoldier s, GridPosition point, Dictionary<GridPosition, List<BattleSoldier>> occupied) => !s.IsTransit || (point != s.Cell && (!occupied.TryGetValue(point, out var group) || group.All(u => u.Id == s.Id)));
        private void SetMove(Intent intent, List<GridPosition> path)
        {
            intent.Path.AddRange(path);
            if (path.Count > 1) { intent.Action = RoundAction.Move; intent.Destination = path[Math.Min(MovementSpeed(intent.Actor), path.Count - 1)]; }
        }

        private sealed class RouteNode
        {
            public GridPosition Cell;
            public double Cost;
            public int Serial;
            public bool ClearedEnemy;
            public int MoveProgress;
        }
        private sealed class FocusRoute
        {
            public readonly List<GridPosition> Path = new List<GridPosition>();
            public double Cost = double.PositiveInfinity;
        }
        private Intent PlanFocus(BattleSoldier s, BattleSoldier goal, Dictionary<GridPosition, List<BattleSoldier>> occupied)
        {
            var intent = new Intent { Actor = s, Action = RoundAction.Wait, Destination = s.Cell, Target = goal };
            if (goal == null || !goal.IsActive || goal.Side == s.Side || !Vision.CanSee(s.Side, goal) || !AttackAreaContains(s, goal.Cell)) { intent.Target = null; intent.Decision = "锁定目标离开视野或队长进攻范围"; return intent; }
            if (!s.IsTransit && CanAttack(s, s.Cell, goal)) { intent.Action = RoundAction.Attack; intent.Decision = "攻击锁定目标"; return intent; }
            Func<GridPosition, bool> attackPosition = p => CanStandToAttack(s, p, occupied) && CanAttack(s, p, goal);
            var detour = FindPath(s, attackPosition, occupied);
            intent.DetourRounds = detour.Count > 0 ? Math.Ceiling((detour.Count - 1) / (double)MovementSpeed(s)) : double.PositiveInfinity;
            var clear = FindClearRoute(s, goal, attackPosition, occupied); intent.ClearRounds = clear.Cost;
            // 敌军格仅在估算图中假定击杀后可进入；实际意图始终停在挡路单位前，绝不穿敌。
            if (clear.Path.Count > 0 && clear.Cost < intent.DetourRounds)
            {
                for (int i = 1; i < clear.Path.Count; i++)
                {
                    if (!occupied.TryGetValue(clear.Path[i], out var group) || !group.Any(u => u.Side != s.Side)) continue;
                    BattleSoldier blocker = group.Where(u => u.Side != s.Side).OrderByDescending(u => u.IsTransit).ThenBy(u => u.Id).First();
                    intent.Target = blocker;
                    intent.Decision = "清除挡路 #" + (blocker.Id + 1) + "（清障 " + CostLabel(clear.Cost) + "／绕路 " + CostLabel(intent.DetourRounds) + " 轮）";
                    if (CanAttack(s, s.Cell, blocker)) intent.Action = RoundAction.Attack;
                    else
                    {
                        int approach = i - 1;
                        for (int j = 1; j < i; j++) if (CanAttack(s, clear.Path[j], blocker)) { approach = j; break; }
                        SetMove(intent, clear.Path.Take(approach + 1).ToList());
                    }
                    return intent;
                }
            }
            SetMove(intent, detour);
            intent.Decision = detour.Count > 0 ? "绕路接近目标（绕路 " + CostLabel(intent.DetourRounds) + "／清障 " + CostLabel(intent.ClearRounds) + " 轮）" : "无可达路线，无法清除挡路敌军";
            return intent;
        }
        private static string CostLabel(double cost) => double.IsPositiveInfinity(cost) ? "不可行" : cost.ToString("0");
        private FocusRoute FindClearRoute(BattleSoldier s, BattleSoldier goal, Func<GridPosition, bool> destination, Dictionary<GridPosition, List<BattleSoldier>> occupied)
        {
            var result = new FocusRoute();
            if (s.IsTransit || s.Stats.Damage <= 0 || !occupied.Values.Any(group => group.Any(u => u.Side != s.Side && u.Id != goal.Id))) return result;
            int serial = 0;
            var queue = new SortedSet<RouteNode>(Comparer<RouteNode>.Create((a, b) => { int cost = a.Cost.CompareTo(b.Cost); return cost != 0 ? cost : a.Serial.CompareTo(b.Serial); }));
            var costs = new Dictionary<(GridPosition cell, bool cleared, int progress), double> { [(s.Cell, false, 0)] = 0 };
            var previous = new Dictionary<(GridPosition cell, bool cleared, int progress), (GridPosition cell, bool cleared, int progress)>();
            queue.Add(new RouteNode { Cell = s.Cell, Cost = 0, Serial = serial++ });
            while (queue.Count > 0)
            {
                RouteNode node = queue.Min; queue.Remove(node);
                var nodeKey = (cell: node.Cell, cleared: node.ClearedEnemy, progress: node.MoveProgress);
                if (node.Cost != costs[nodeKey]) continue;
                if (node.ClearedEnemy && destination(node.Cell))
                {
                    result.Cost = node.Cost; var cursor = nodeKey; result.Path.Add(cursor.cell);
                    while (previous.TryGetValue(cursor, out var parent)) { cursor = parent; result.Path.Add(cursor.cell); }
                    result.Path.Reverse(); return result;
                }
                foreach (GridPosition direction in Directions)
                {
                    GridPosition next = node.Cell + direction;
                    if (!Board.CanStep(node.Cell,next) || !AttackRouteAllowed(s, next) || next == goal.Cell) continue;
                    double cost = node.Cost;
                    int progress = node.MoveProgress;
                    bool cleared = node.ClearedEnemy;
                    if (occupied.TryGetValue(next, out var group))
                        foreach (BattleSoldier enemy in group.Where(u => u.Side != s.Side)) { cost += Math.Ceiling(enemy.Health / (double)ExpectedDamageAgainst(s, enemy)); cleared = true; progress = 0; }
                    if (double.IsInfinity(cost) || double.IsNaN(cost)) continue;
                    if (progress == 0) cost++;
                    progress = (progress + 1) % MovementSpeed(s);
                    var key = (cell: next, cleared, progress);
                    if (costs.TryGetValue(key, out double existing) && existing <= cost) continue;
                    costs[key] = cost; previous[key] = nodeKey;
                    queue.Add(new RouteNode { Cell = next, Cost = cost, Serial = serial++, ClearedEnemy = cleared, MoveProgress = progress });
                }
            }
            return result;
        }

        private List<GridPosition> FindPath(BattleSoldier s, Func<GridPosition, bool> goal, Dictionary<GridPosition, List<BattleSoldier>> occupied, Func<GridPosition, bool> allowed = null)
        {
            if (ModernRules) return FindWeightedPath(s, goal, occupied, allowed);
            var queue = new Queue<GridPosition>(); var previous = new Dictionary<GridPosition, GridPosition>();
            queue.Enqueue(s.Cell); previous[s.Cell] = s.Cell;
            while (queue.Count > 0)
            {
                GridPosition current = queue.Dequeue();
                if (goal(current))
                {
                    var path = new List<GridPosition> { current };
                    while (current != s.Cell) { current = previous[current]; path.Add(current); }
                    path.Reverse(); return path;
                }
                foreach (GridPosition direction in Directions)
                {
                    GridPosition next = current + direction;
                    if (!Board.CanStep(current,next) || !AttackRouteAllowed(s, next) || (allowed != null && !allowed(next)) || previous.ContainsKey(next) || (occupied.TryGetValue(next, out var blockers) && blockers.Any(b => b.Side != s.Side && Vision.CanSee(s.Side, b)))) continue;
                    previous[next] = current; queue.Enqueue(next);
                }
            }
            return new List<GridPosition>();
        }

        public UnitOrderPreview PreviewOrder(int id)
        {
            BattleSoldier s = Soldier(id);
            var preview = new UnitOrderPreview { SoldierId = id, Command = s?.Command ?? SquadCommand.Hold };
            if (s == null || !s.IsActive) { preview.Status = "单位无法行动"; return preview; }
            if (Phase != BattlePhase.Fighting) { preview.Status = Phase == BattlePhase.Deployment ? "等待开战" : "战斗结束"; return preview; }
            var actors = Soldiers.Where(u => u.IsActive && Vision.CanSee(s.Side, u)).OrderBy(u => u.Id).ToList();
            var occupied = KnownOccupancy(s.Side, Occupancy());
            var intents = CoordinatedIntents(actors, occupied);
            Intent intent = intents.Find(i => i.Actor.Id == id);
            preview.Path.AddRange(intent.Path);
            BattleSoldier locked = s.ResumeFollowing ? intent.Target : s.Command == SquadCommand.Focus ? Soldier(s.FocusTargetId) : intent.Target;
            if (locked != null && (!Vision.CanSee(s.Side, locked) || !AttackAreaContains(s,locked.Cell))) locked = null;
            preview.LockedTargetId = locked?.Id ?? -1;
            preview.TargetCell = locked?.Cell ?? intent.GoalCell;
            if (!preview.TargetCell.HasValue)
            {
                if (s.Command == SquadCommand.Move) preview.TargetCell = s.MoveGoal;
                else if (s.Command == SquadCommand.Retreat || s.CarryingId >= 0) preview.TargetCell = ExitCell(s.Side);
                else if (s.RescueTargetId >= 0) preview.TargetCell = Soldier(s.RescueTargetId)?.Cell;
                else if (ModernRules && Squads[s.SquadId].GuardAssigned) preview.TargetCell = Squads[s.SquadId].GuardCenter;
                else if (intent.Path.Count > 1) preview.TargetCell = intent.Path.Last();
                else if (s.Command == SquadCommand.Hold) preview.TargetCell = s.MoveGoal;
            }
            preview.ImmediateTargetId = intent.Target?.Id ?? -1; preview.DetourRounds = intent.DetourRounds; preview.ClearRounds = intent.ClearRounds;
            if (intent.Action == RoundAction.Skill) preview.TargetCell = intent.Destination;
            preview.NextStepBlocked = MovementBlocked(intent, ResolveMoves(intents, occupied, actors));
            preview.Status = intent.Action == RoundAction.Skill ? "技能已排队" : intent.Action == RoundAction.Attack ? "目标在射程内" : preview.NextStepBlocked ? "本轮路径途中占用或争格等待" : intent.Action == RoundAction.Move ? "按路径前进" : s.Command == SquadCommand.Hold ? "驻守原地" : s.Command == SquadCommand.Move || s.Command == SquadCommand.Focus || s.Command == SquadCommand.Retreat ? "没有可达路径或已到位" : "等待有效目标";
            if (!string.IsNullOrEmpty(intent.Decision)) preview.Status = intent.Decision + (preview.NextStepBlocked ? " · 本轮途中争格等待" : "");
            if (s.IsTransit) preview.Status = "通行单位（禁攻） · " + preview.Status;
            return preview;
        }
        public UnitOrderPreview PreviewCommand(int id, GridPosition point)
        {
            var s = Soldier(id); if (s == null || !s.IsActive || !Board.Walkable(point)) return PreviewOrder(id);
            var occupied = KnownOccupancy(s.Side, Occupancy()); var opponent = AttackTargetAt(point); if (opponent?.Side == s.Side || opponent != null && !Vision.CanSee(s.Side, opponent)) opponent = null;
            var preview = new UnitOrderPreview { SoldierId = id, Command = opponent == null ? SquadCommand.Move : SquadCommand.Focus, TargetCell = point, LockedTargetId = opponent?.Id ?? -1 };
            Intent intent = opponent == null ? new Intent { Actor = s, Destination = s.Cell, Action = RoundAction.Wait } : PlanFocus(s, opponent, occupied);
            if (opponent == null) SetMove(intent, FindPath(s, p => p == point, occupied));
            preview.Path.AddRange(intent.Path); preview.ImmediateTargetId = intent.Target?.Id ?? -1; preview.DetourRounds = intent.DetourRounds; preview.ClearRounds = intent.ClearRounds;
            var actors = Soldiers.Where(u => u.IsActive && Vision.CanSee(s.Side, u)).OrderBy(u => u.Id).ToList(); var intents = CoordinatedIntents(actors, occupied);
            intents[intents.FindIndex(i => i.Actor.Id == id)] = intent;
            preview.NextStepBlocked = MovementBlocked(intent, ResolveMoves(intents, occupied, actors));
            preview.Status = string.IsNullOrEmpty(intent.Decision) ? "拖动预览" : "拖动预览 · " + intent.Decision; return preview;
        }

        private Dictionary<GridPosition, List<BattleSoldier>> Occupancy() => Soldiers.Where(s => s.IsActive).GroupBy(s => s.Cell).ToDictionary(g => g.Key, g => g.ToList());
        private Dictionary<GridPosition, List<BattleSoldier>> KnownOccupancy(BattleSide side, Dictionary<GridPosition, List<BattleSoldier>> occupied) => !Scenario.FogEnabled ? occupied : occupied.Where(pair => pair.Value.Any(s => s.Side == side || Vision.CanSee(side, s))).ToDictionary(pair => pair.Key, pair => pair.Value.Where(s => s.Side == side || Vision.CanSee(side, s)).ToList());
        private void RefreshRoles()
        {
            foreach (var group in Occupancy().Values)
            {
                var ranked = group.OrderByDescending(s => s.BattlePower).ThenBy(s => s.Cell == s.PreviousCell && !s.IsTransit ? 0 : 1).ThenBy(s => s.Id).ToList();
                for (int i = 0; i < ranked.Count; i++) ranked[i].IsTransit = i > 0;
            }
        }
        private bool MovementBlocked(Intent intent, Dictionary<int, List<GridPosition>> routes) => intent.Action == RoundAction.Move && routes[intent.Actor.Id].Count - 1 < Math.Min(MovementSpeed(intent.Actor), intent.Path.Count - 1);
        private Dictionary<int, List<GridPosition>> ResolveMoves(List<Intent> intents, Dictionary<GridPosition, List<BattleSoldier>> occupied, List<BattleSoldier> actors)
        {
            var rank = new Dictionary<int, int>(); var actorById = actors.ToDictionary(s => s.Id);
            for (int i = 0; i < actors.Count; i++) rank[actors[(i + RoundNumber) % actors.Count].Id] = i;
            var positions = actors.ToDictionary(s => s.Id, s => s.Cell);
            var routes = intents.Where(i => i.Action == RoundAction.Move).ToDictionary(i => i.Actor.Id, i => new List<GridPosition> { i.Actor.Cell });
            var stopped = new HashSet<int>();
            int maxSteps = intents.Where(i => i.Action == RoundAction.Move).Select(i => Math.Min(MovementSpeed(i.Actor), i.Path.Count - 1)).DefaultIfEmpty(0).Max();
            // 一次移动行动按相邻格逐步解决通行；每一步仍检查容量，途中受阻就停止本轮移动。
            for (int step = 1; step <= maxSteps; step++)
            {
                var candidates = intents.Where(i => i.Action == RoundAction.Move && !stopped.Contains(i.Actor.Id) && step <= MovementSpeed(i.Actor) && step < i.Path.Count).ToDictionary(i => i.Actor.Id, i => i.Path[step]);
                var allowed = new HashSet<int>(candidates.Keys.Where(id => Board.CanStep(positions[id],candidates[id]) && (!occupied.TryGetValue(candidates[id], out var group) || group.All(s => s.Side == actorById[id].Side))));
                bool changed;
                do
                {
                    changed = false;
                    var endings = intents.Where(i => i.Action != RoundAction.Escape).GroupBy(i => allowed.Contains(i.Actor.Id) ? candidates[i.Actor.Id] : positions[i.Actor.Id]);
                    foreach (var group in endings)
                    {
                        var staying = group.Where(i => !allowed.Contains(i.Actor.Id)).ToList();
                        var arriving = group.Where(i => allowed.Contains(i.Actor.Id)).OrderBy(i => rank[i.Actor.Id]).ToList();
                        BattleSide owner = staying.Count > 0 ? staying[0].Actor.Side : arriving[0].Actor.Side;
                        int capacity = 2 - staying.Count;
                        foreach (Intent i in arriving)
                            if (i.Actor.Side != owner || capacity-- <= 0) { allowed.Remove(i.Actor.Id); changed = true; }
                    }
                    foreach (var intent in intents.Where(i => allowed.Contains(i.Actor.Id)).ToArray())
                        if (!LedStepAllowed(intent.Actor, candidates[intent.Actor.Id], positions, candidates, allowed)) { allowed.Remove(intent.Actor.Id); changed = true; }
                } while (changed);
                foreach (int id in candidates.Keys)
                    if (allowed.Contains(id)) { positions[id] = candidates[id]; routes[id].Add(candidates[id]); } else stopped.Add(id);
            }
            return routes;
        }
        private bool IsCritical(BattleSoldier source, BattleSoldier target)
        {
            // 无状态随机：同种子、回合、单位和目标得同结果；预览不抽样，自动与跳过不受调用频率影响。
            if (source.Stats.Damage <= 0 || source.Stats.CritChance <= 0) return false;
            if (source.Stats.CritChance >= 1) return true;
            unchecked
            {
                uint value = (uint)Scenario.RandomSeed ^ ((uint)(RoundNumber + 1) * 0x9E3779B9u) ^ ((uint)(source.Id + 1) * 0x85EBCA6Bu) ^ ((uint)(target.Id + 1) * 0xC2B2AE35u);
                value ^= value >> 16; value *= 0x7FEB352Du; value ^= value >> 15; value *= 0x846CA68Bu; value ^= value >> 16;
                return value / 4294967296.0 < source.Stats.CritChance;
            }
        }
        private bool Damage(BattleSoldier source, BattleSoldier target, float value)
        {
            if(target.DeveloperInvulnerable)return false;
            float actual = Math.Min(target.Health, CombatNumbers.Hit(source.Stats,target.Stats,value) * IncomingAttackMultiplier(source, target));
            target.Health -= actual; target.LastDamageTaken += actual; source.DamageDealt += actual;
            if (!target.IsAlive) ResolveCasualties();
            if (actual > 0 && ModernRules) AddEvent(new BattleEvent { SourceId = source.Id, TargetId = target.Id, Damage = actual, RemainingHealth = target.Health, Critical = source.LastAttackCritical, Lethal = !target.IsAlive && target.Kind != SoldierKind.Hero, Downed = !target.IsAlive && target.Kind == SoldierKind.Hero, SeenByA = source.Side == BattleSide.A || target.Side == BattleSide.A || Vision.CanSee(BattleSide.A, source) || Vision.CanSee(BattleSide.A, target), SeenByB = source.Side == BattleSide.B || target.Side == BattleSide.B || Vision.CanSee(BattleSide.B, source) || Vision.CanSee(BattleSide.B, target) });
            return actual > 0;
        }

        private void CheckOutcome()
        {
            if (VictoryDeclared)
            {
                if (!RoutInProgress || RoundNumber >= Scenario.MaxRounds || idleRounds >= Scenario.StalemateRounds) Phase = BattlePhase.Finished;
                RefreshBattleReport(); return;
            }
            bool a = Soldiers.Exists(s => s.Side == BattleSide.A && s.IsActive), b = Soldiers.Exists(s => s.Side == BattleSide.B && s.IsActive);
            // 库存尚可投放时保留增援机会；全部撤离仍沿用主动撤退结束。
            a |= HasReserves(BattleSide.A) && !Soldiers.Exists(s => s.Side == BattleSide.A && s.Escaped) && (!Scenario.CampaignRules || Scenario.PlayerLeaderSlot < 0 || Soldiers.Any(s => s.Side == BattleSide.A && s.IsActive) || Remaining(BattleSide.A,SoldierKind.Hero)>0);
            b |= HasReserves(BattleSide.B) && !Soldiers.Exists(s => s.Side == BattleSide.B && s.Escaped);
            bool escaped = Soldiers.Exists(s => s.Escaped);
            BattleOutcome result = BattleOutcome.None;
            if (CommanderVictorySatisfied || a && HoldVictorySatisfied) { result = BattleOutcome.SideAWin; DeclareCommanderVictory(); }
            else if (!a && !b) result = escaped ? BattleOutcome.Retreat : BattleOutcome.Draw;
            else if (!a) result = Soldiers.Exists(s => s.Side == BattleSide.A && s.Escaped) ? BattleOutcome.Retreat : BattleOutcome.SideBWin;
            else if (!b) result = Soldiers.Exists(s => s.Side == BattleSide.B && s.Escaped) ? BattleOutcome.Retreat : BattleOutcome.SideAWin;
            else if (RoundNumber >= Scenario.MaxRounds) result = BattleOutcome.RoundLimit;
            else if (idleRounds >= Scenario.StalemateRounds && Scenario.Objective.Kind != ObjectiveKinds.Hold) result = BattleOutcome.Stalemate;
            if (result == BattleOutcome.None) return;
            Phase = VictoryDeclared && RoutInProgress ? BattlePhase.Fighting : BattlePhase.Finished;
            Report = new BattleReport { Outcome = result, Rounds = RoundNumber, CommandsIssued = CommandsIssued, AbilitiesUsed = AbilitiesUsed };
            RefreshBattleReport();
        }
        private void RefreshBattleReport()
        {
            if (Report == null) return;
            Report.Rounds = RoundNumber; Report.CommandsIssued = CommandsIssued; Report.AbilitiesUsed = AbilitiesUsed; Report.Squads.Clear();
            foreach (BattleSquad squad in Squads)
            {
                var row = new SquadReport { Name = squad.Name, Side = squad.Side, InitialCount = squad.Soldiers.Count };
                foreach (BattleSoldier s in squad.Soldiers)
                {
                    if (!s.IsAlive) row.Defeated++; else if (s.Escaped) row.Escaped++; else row.Standing++;
                    row.DamageDealt += s.DamageDealt;
                }
                Report.Squads.Add(row);
            }
            RefreshArmyReports();
        }
        private bool HasReserves(BattleSide side) => Enum.GetValues(typeof(SoldierKind)).Cast<SoldierKind>().Any(kind => Remaining(side, kind) > 0);
        public static string SideName(BattleSide side) => side == BattleSide.A ? "我方" : "敌方";
        public static string KindName(SoldierKind kind) => kind == SoldierKind.Melee ? "剑士" : kind == SoldierKind.Ranged ? "弓兵" : kind == SoldierKind.Cavalry ? "骑兵" : kind == SoldierKind.Mage ? "法师" : "队长";
    }
}

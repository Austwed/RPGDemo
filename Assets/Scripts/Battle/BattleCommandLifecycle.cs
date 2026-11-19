using System;
using System.Collections.Generic;
using System.Linq;

namespace BaseConquest.Battle
{
    [Serializable] public sealed class BattleEvent
    {
        public int Round, SourceId = -1, TargetId = -1;
        public float Damage, RemainingHealth;
        public bool Critical, Missed, Lethal, Downed, SeenByA, SeenByB;
        public string Message, SourceName, TargetName;
        public int[] NamedIds;
        public string[] NamedNames;
        public BattleEvent Copy() { var x=(BattleEvent)MemberwiseClone();x.NamedIds=NamedIds?.ToArray();x.NamedNames=NamedNames?.ToArray();return x; }
    }

    public sealed partial class BattleSimulation
    {
        public bool ModernRules => Scenario.CommanderControl && Scenario.MobileBattleRules;
        public readonly List<BattleEvent> Events = new List<BattleEvent>();
        private readonly int[] captainSlots = { -1, -1 };
        private bool resolvingRound;
        public int CaptainSlot(BattleSide side) => captainSlots[(int)side];
        public string UnitName(BattleSoldier s) => s == null ? "未知单位" : (s.DeveloperSpawned ? "测试·" : "") + SideName(s.Side) + (s.Kind == SoldierKind.Hero ? CommanderDisplayName(s.Side, s.OriginCommanderSlot) : UnitDisplayNames.Mark(KindName(s.Kind) + "#" + (s.SoldierNumber > 0 ? s.SoldierNumber : s.Id + 1),s.Recruited)) + (RuinsCivilianIds.Contains(s.Id)&&Phase==BattlePhase.Fighting&&Commander(BattleSide.B,0)?.IsActive==true ? " · 受控" : "");
        public readonly List<string> CommanderNamesA = new List<string>();
        public string CommanderDisplayName(BattleSide side,int slot)=>UnitDisplayNames.Mark(RawCommanderName(side,slot),slot>=0&&slot<Scenario.Army(side).CommanderRecruited.Count&&Scenario.Army(side).CommanderRecruited[slot]);
        private string RawCommanderName(BattleSide side, int slot)
        {
            if(side==BattleSide.A&&slot>=0&&slot<CommanderNamesA.Count)return CommanderNamesA[slot];
            var names=Scenario.Army(side).CommanderNames;
            if(names!=null&&slot>=0&&slot<names.Count&&!string.IsNullOrWhiteSpace(names[slot]))return names[slot];
            return (side==BattleSide.A ? new[] {"韩铮","陆远","沈毅"} : new[] {"赵烈","许骁","秦策"})[Math.Max(0,Math.Min(2,slot))];
        }
        public string CommanderRole(BattleSoldier s) => s == null ? "指挥者" : s.OriginCommanderSlot == CaptainSlot(s.Side) ? "队长" : "副队长";
        public bool PlayerCanSee(BattleSide side, BattleSoldier s) => s != null && (Vision.CanSee(side, s) || ModernRules && Scenario.ShowEnemiesThroughFog);
        public bool CanInspectCell(BattleSide side, GridPosition cell) => Vision.Visible(side, cell);
        public bool CanInspectUnit(BattleSide side, BattleSoldier unit) => Vision.CanSee(side, unit);
        public void SetFog(bool enabled, bool reveal)
        {
            Scenario.FogEnabled = enabled; Scenario.ShowEnemiesThroughFog = reveal; Vision.Refresh();
        }
        private void AddEvent(BattleEvent e)
        {
            e.Round = RoundNumber + (resolvingRound ? 1 : 0);
            e.SourceName = e.SourceId >= 0 ? UnitName(Soldier(e.SourceId)) : null; e.TargetName = e.TargetId >= 0 ? UnitName(Soldier(e.TargetId)) : null;
            e.NamedNames = e.NamedIds?.Select(id => UnitName(Soldier(id))).ToArray();
            Events.Add(e); if (Events.Count > 160) Events.RemoveAt(0);
        }
        private void Notice(string message, BattleSide side, int target = -1, params int[] others)
        {
            AddEvent(new BattleEvent { Message = message, SourceId = others.Length > 0 ? others[0] : -1, TargetId = target, NamedIds = others.Skip(1).ToArray(), SeenByA = side == BattleSide.A, SeenByB = side == BattleSide.B });
        }
        public IEnumerable<BattleEvent> KnownEvents(BattleSide side) => Events.Where(e => !Scenario.FogEnabled || Scenario.ShowEnemiesThroughFog || (side == BattleSide.A ? e.SeenByA : e.SeenByB));
        public bool CommanderEvacuationActive(BattleSide side) => ModernRules && !Soldiers.Any(s => s.Side == side && s.Kind == SoldierKind.Hero && s.IsActive) && Soldiers.Any(s => s.Side == side && s.Kind == SoldierKind.Hero && s.IsAlive && s.Escaped);
        private bool ResolveExitArrivals()
        {
            if (!ModernRules) return false; bool changed = false;
            foreach (var s in Soldiers.Where(s => s.IsActive && !s.DeveloperImmobile && IsExit(s.Side, s.Cell) && (s.Kind == SoldierKind.Hero || s.Command == SquadCommand.Retreat || s.CarryingId >= 0)).ToArray())
            { s.Escaped = true; changed = true; Notice(UnitName(s) + "抵达撤离点，已离场", s.Side, s.Id); }
            return changed;
        }
        private void ResolveLeadership()
        {
            if (!ModernRules) return;
            foreach (BattleSide side in Enum.GetValues(typeof(BattleSide)))
            {
                if (Scenario.Leaderless(side)) continue;
                int old = captainSlots[(int)side];
                if (Commander(side, old)?.IsActive != true)
                {
                    int preferred = Scenario.Army(side).CaptainSlot;
                    int next = Enumerable.Range(0, Scenario.Army(side).HeroCount).OrderBy(i => i == preferred ? -1 : i).Where(i => Commander(side, i)?.IsActive == true).DefaultIfEmpty(-1).First();
                    captainSlots[(int)side] = next;
                    if (next != old) Notice(next < 0 ? SideName(side) + "已无可行动指挥者，无法下达部队命令" : UnitName(Commander(side, next)) + "接任队长", side, Commander(side, next)?.Id ?? -1);
                }
                int captain = captainSlots[(int)side];
                if (captain < 0)
                {
                    foreach (var q in Squads.Where(q => q.Side == side)) ClearGuardState(q);
                    foreach (var q in Squads.Where(q => q.Side == side)) { q.IssuedCommand = CommanderEvacuationActive(side) ? SquadCommand.Retreat : SquadCommand.Auto; q.SkillQueued = false; }
                    foreach (var s in Soldiers.Where(s => s.Side == side && s.IsActive && s.Command != SquadCommand.Retreat)) SetOrder(s, CommanderEvacuationActive(side) ? SquadCommand.Retreat : SquadCommand.Auto);
                    continue;
                }
                var recipient = CommanderSquad(side, captain);
                foreach (var source in Squads.Where(q => q.Side == side && q.Id != recipient.Id && q.MergedInto < 0).ToArray())
                {
                    var leader = Soldier(source.CommanderId); if (leader == null || leader.IsActive) continue;
                    foreach (var member in source.Soldiers.ToArray())
                    {
                        member.SquadId = recipient.Id; recipient.Soldiers.Add(member);
                        if (!member.IsActive || member.Command == SquadCommand.Retreat || member.RescueTargetId >= 0 || member.CarryingId >= 0) continue;
                        var activeLeader = Soldier(recipient.CommanderId);
                        SetOrder(member, recipient.IssuedCommand, activeLeader?.FocusTargetId ?? -1);
                        if (recipient.IssuedCommand == SquadCommand.Move) member.MoveGoal = activeLeader.MoveGoal;
                    }
                    source.Soldiers.Clear(); source.MergedInto = recipient.Id;
                    recipient.SupplySlots.UnionWith(source.SupplySlots);
                    Notice(UnitName(leader) + "所部并入" + UnitName(Soldier(recipient.CommanderId)) + "，保留全部兵力", side, recipient.CommanderId, leader.Id);
                    if (recipient.IssuedCommand == SquadCommand.Move || recipient.IssuedCommand == SquadCommand.Hold)
                    {
                        int before = CommandsIssued;
                        if (recipient.IssuedCommand == SquadCommand.Hold) GuardCommanders(new[] { recipient.CommanderId }, Soldier(recipient.CommanderId).MoveGoal);
                        else MoveCommanders(new[] { recipient.CommanderId }, Soldier(recipient.CommanderId).MoveGoal);
                        CommandsIssued = before;
                    }
                }
            }
        }
        public bool GuardCommanders(IEnumerable<int> ids, GridPosition position)
        {
            var squads = SelectedCommanderSquads(ids); if (squads.Count == 0 || !Board.Walkable(position)) return false;
            if (!MoveCommanders(ids, position)) return false;
            foreach (var q in squads)
            {
                q.IssuedCommand = SquadCommand.Hold; q.GuardCells.Clear();
                q.GuardAssigned = true; q.GuardCenter = Soldier(q.CommanderId).MoveGoal; q.GuardArrived = Soldier(q.CommanderId).Cell == q.GuardCenter;
                q.GuardRequestedCell = position;
                q.GuardAlert = false; q.GuardTargets.Clear();
                foreach (var member in q.Soldiers.Where(CanCommand)) SetOrder(member, SquadCommand.Move);
                RefreshGuardCells(q);
            }
            return true;
        }
        private void FinishCommanderMoves()
        {
            if (!ModernRules) return;
            foreach (var q in Squads.Where(q => q.IssuedCommand == SquadCommand.Move && !q.GuardAssigned))
            {
                var leader = Soldier(q.CommanderId);
                if (leader?.IsActive != true || leader.Command != SquadCommand.Move || leader.Cell != leader.MoveGoal || leader.IsTransit || leader.RescueTargetId >= 0 || leader.CarryingId >= 0) continue;
                q.IssuedCommand = SquadCommand.Auto; q.GuardCells.Clear();
                foreach (var member in q.Soldiers.Where(s => CanCommand(s) && s.Command == SquadCommand.Move && !s.ResumeFollowing && s.RescueTargetId < 0 && s.CarryingId < 0)) SetOrder(member, SquadCommand.Auto);
            }
        }
        public int AutoDeploySelected(BattleSide side, IEnumerable<int> ids) { var slots = DeploymentSlots(side, ids); return AutoDeployFormation(side, slots.Count == 0 ? null : slots); }
        private int AutoDeployFormation(BattleSide side, IEnumerable<int> onlySlots = null)
        {
            if (Phase == BattlePhase.Finished) return 0;
            var slots = (onlySlots ?? (Scenario.Leaderless(side) ? new[] { -1 } : Enumerable.Range(0, Scenario.Army(side).HeroCount))).Distinct().OrderBy(i => i).ToList();
            var cells = Board.Positions().Where(p => Board.Walkable(p) && Board.Cell(p).Zone == (side == BattleSide.A ? DeploymentZone.A : DeploymentZone.B)).ToList();
            if (cells.Count == 0) return 0;
            // 指挥者成组位于中部，小兵按近战前排、弓手后排、骑兵侧翼分配。
            int centerX = (int)Math.Round(cells.Average(p => p.X)); int centerZ = (int)Math.Round(cells.Average(p => p.Z)); int forward = side == BattleSide.A ? 1 : -1, total = 0;
            // 骑兵占两格，开局挤在一起就再也散不开（2026-11-06 裁决：骑兵间距拉大）。
            // 先挑四邻域最空的格子，其次才看格子本身有没有人。
            int NeighbourLoad(GridPosition p)
            {
                int load = 0;
                for (int dx = -1; dx <= 1; dx++) for (int dz = -1; dz <= 1; dz++)
                {
                    if (dx == 0 && dz == 0) continue;
                    var n = new GridPosition(p.X + dx, p.Z + dz);
                    if (n.X < 0 || n.Z < 0 || n.X >= Board.Width || n.Z >= Board.Height) continue;
                    if (At(n) != null) load++;
                }
                return load;
            }
            foreach (var kind in new[] { SoldierKind.Hero, SoldierKind.Melee, SoldierKind.Cavalry, SoldierKind.Ranged })
                foreach (int slot in slots)
                {
                    int dx = kind == SoldierKind.Melee ? forward : kind == SoldierKind.Ranged ? -forward : 0;
                    int dz = kind == SoldierKind.Cavalry ? 2 : kind == SoldierKind.Hero ? slot - 1 : 0;
                    var ideal = new GridPosition(centerX + dx, centerZ + dz);
                    foreach (var p in cells.OrderBy(p => kind == SoldierKind.Cavalry ? NeighbourLoad(p) : 0).ThenBy(p => UnitsAt(p).Count).ThenBy(p => kind == SoldierKind.Ranged ? forward * p.X : kind == SoldierKind.Melee ? -forward * p.X : GridPosition.Distance(p, ideal)).ThenBy(p => GridPosition.Distance(p, ideal)).ThenBy(p => p.Z * Board.Width + p.X))
                        while (CanDeploy(side, kind, p, slot)) { Deploy(side, kind, p, slot); total++; }
                }
            return total;
        }
        private bool MoveModernCommanders(IEnumerable<int> ids, GridPosition target)
        {
            var squads = SelectedCommanderSquads(ids); if (squads.Count == 0 || !Board.Walkable(target)) return false;
            var occupancy = KnownOccupancy(squads[0].Side, Occupancy());
            if (occupancy.TryGetValue(target, out var blockers) && blockers.Any(u => u.Side != squads[0].Side)) return false;
            var members = squads.SelectMany(q => q.Soldiers).Where(CanCommand).ToList();
            if (members.Count == 0) return false;
            var assigned = new Dictionary<int, GridPosition>(); var used = new HashSet<GridPosition>();
            // 落点除避开已分配格外，还避开尚未离场的队友当前站位：否则成员会被分到别人脚下，
            // 实际执行时要等对方先走，产生无谓等待与抖动。队长不受此限（队长先动，成员随后让位）。
            var occupiedByAllies = members.Where(s => !s.IsTransit).Select(s => s.Cell).ToHashSet();
            // 首选队长精确到点击格；其他指挥者占相邻合法格，避免三人挤同一格。
            foreach (var q in squads)
            {
                var leader = Soldier(q.CommanderId); GridPosition? goal = null;
                foreach (var p in Board.Positions().Where(Board.Walkable).Where(p => !used.Contains(p)).OrderBy(p => GridPosition.Distance(p, target)).ThenBy(p => p.Z * Board.Width + p.X))
                {
                    if (occupancy.TryGetValue(p, out var units) && units.Any(u => !members.Contains(u))) continue;
                    if (q == squads[0] && p != target) break;
                    if (FindPath(leader, c => c == p, occupancy).Count == 0) continue;
                    goal = p; break;
                }
                if (!goal.HasValue) return false; assigned[leader.Id] = goal.Value; used.Add(goal.Value);
            }
            foreach (var s in members.Where(s => s.Kind != SoldierKind.Hero))
            {
                var leaderGoal = assigned[Squads[s.SquadId].CommanderId]; var preferred = leaderGoal + new GridPosition(s.Kind == SoldierKind.Ranged ? s.Side == BattleSide.A ? -1 : 1 : s.Kind == SoldierKind.Melee ? s.Side == BattleSide.A ? 1 : -1 : 0, 0);
                // R1：候选先限制在队长目标的有效率领区域内，避免为优先空格把成员推到圈外（届时目标不可达、与跟随行为不一致）。
                // 区域不足时退化为以队长目标为中心的可达最小距离，仍不越圈。
                int lead = Math.Max(1, LeadRadiusAt(Squads[s.SquadId], leaderGoal));
                GridPosition? goal = null;
                foreach (var p in Board.Positions().Where(Board.Walkable).Where(p => !used.Contains(p)).Where(p => GridPosition.Distance(p, leaderGoal) <= lead)
                    .OrderBy(p => occupiedByAllies.Contains(p) && p != s.Cell ? 1 : 0)
                    .ThenBy(p => GridPosition.Distance(p, preferred)).ThenBy(p => p.Z * Board.Width + p.X))
                {
                    if (p == leaderGoal && p != s.Cell) continue;   // 队长目标格留给队长
                    if (occupancy.TryGetValue(p, out var units) && units.Any(u => !members.Contains(u))) continue;
                    // 交战中成员同样校验可达，避免分到墙后死角；仅当目的地就是自己当前格时免检。
                    if (p != s.Cell && FindPath(s, c => c == p, occupancy).Count == 0) continue;
                    goal = p; break;
                }
                // 率领区域内若无合法可达格（拥挤／阻塞），合法退化为留在原地，等待下一轮重算，不分配圈外目标。
                if (!goal.HasValue) goal = s.Cell;
                assigned[s.Id] = goal.Value; used.Add(goal.Value);
            }
            foreach (var s in members)
            {
                int engaged = s.ResumeFollowing ? s.RetainedCombatTargetId : s.Command == SquadCommand.Focus ? s.FocusTargetId : s.TargetId;
                var enemy = Soldier(engaged);
                bool busy = s.Kind != SoldierKind.Hero && (s.CarryingId >= 0 || s.RescueTargetId >= 0 || enemy?.IsActive == true && enemy.Side != s.Side && Vision.CanSee(s.Side, enemy));
                if (busy) { s.ResumeFollowing = true; s.RetainedCombatTargetId = enemy?.IsActive == true ? engaged : -1; }
                else SetOrder(s, SquadCommand.Move);
                s.MoveGoal = assigned[s.Id];
            }
            foreach (var q in squads) { ClearGuardState(q); q.IssuedCommand = SquadCommand.Move; q.GuardCells.Clear(); }
            CommandsIssued++; return true;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using BaseConquest.Battle;
using BaseConquest.RealtimePrototype;
using BaseConquest.Story;

namespace BaseConquest.Base
{
    public enum BasePrepRoute { Ruins, Defense, Second, BlackCrow, Travel, Hideout }

    public sealed class BasePrepUnit
    {
        public string Id, Name, TeamId;
        public SoldierKind Kind;
        public double Health, MaxHealth;
        public int SoldierNumber => Pick?.SoldierNumber ?? 0;
        public bool Selected;
        internal int SelectionOrder;
        internal BaseSoldierPick Pick;
        internal SoldierStats Stats;
        internal string SoldierTemplateId;
        internal BaseLeader Leader;
    }

    // A draft is owned by the currently open HUD. The save only receives the validated opening.
    public sealed class BaseBattlePreparation
    {
        private readonly string stamp, mapKey, arrivedPartyId, purpose;
        private readonly RealtimeBattlePrototype geometry;
        private readonly BasePrepUnit[] units;
        private readonly BattleSoldier[] enemyReference;
        private readonly int hideoutSeed;
        private readonly bool draftTravelTroops;
        public bool DraftTravelTroops => draftTravelTroops;
        private readonly List<BaseEnemyMember> hideoutEnemies;
        private static readonly PrototypePoint East = new PrototypePoint(1, 0);
        public BasePrepRoute Route { get; }
        public BattleBoard Board { get; }
        public IReadOnlyList<BasePrepUnit> Units => units;
        public string PlayerId { get; }
        public bool TravelingParty => Route == BasePrepRoute.Travel && arrivedPartyId != null;
        public Dictionary<string, GridPosition> Positions { get; } = new Dictionary<string, GridPosition>();
        public int SupplyGrade { get; set; } = 1;
        private int lastSelection;
        public int TeamCount(string teamId, SoldierKind kind) => units.Count(u => u.Selected && u.TeamId == teamId && u.Kind == kind);

        public bool SetTeamCount(string teamId, SoldierKind kind, int requested)
        {
            if (!units.Any(u => u.Kind == SoldierKind.Hero && u.Selected && u.Id == teamId)) return false;
            var included = units.Where(u => u.Selected && u.Kind == kind && u.TeamId == teamId)
                .OrderByDescending(u => u.SelectionOrder).ThenByDescending(u => u.SoldierNumber).ToArray();
            int limit = BaseMuster.BattleSoldierLimit - units.Count(u => u.Selected && u.Kind != SoldierKind.Hero);
            int target = Math.Max(0, Math.Min(requested, included.Length + limit));
            if (target < included.Length)
                foreach (var u in included.Take(included.Length - target)) u.Selected = false;
            else
                foreach (var u in units.Where(u => u.Kind == kind && !u.Selected).OrderByDescending(u => u.Health / u.MaxHealth).ThenBy(u => u.SoldierNumber).Take(target - included.Length))
                {
                    u.Selected = true;
                    u.TeamId = teamId;
                    u.SelectionOrder = ++lastSelection;
                }
            Prune();
            return true;
        }

        public bool ToggleSoldier(string teamId, BasePrepUnit unit)
        {
            if (unit == null || unit.Kind == SoldierKind.Hero || !units.Contains(unit) ||
                !units.Any(u => u.Kind == SoldierKind.Hero && u.Selected && u.Id == teamId) ||
                !unit.Selected && units.Count(u => u.Selected && u.Kind != SoldierKind.Hero) >= BaseMuster.BattleSoldierLimit) return false;
            unit.Selected = !unit.Selected || unit.TeamId != teamId;
            if (unit.TeamId != teamId) Recall(unit.Id);
            unit.TeamId = teamId;
            if (unit.Selected) unit.SelectionOrder = ++lastSelection;
            Prune();
            return true;
        }

        public BaseBattlePreparation(BaseState state, BasePrepRoute route, int? hideoutSeed = null, int? hideoutEnemyCount = null, bool draftTravelTroops = true)
        {
            BaseEconomy.Validate(state);
            Route = route;
            this.draftTravelTroops = draftTravelTroops;
            PlayerId = state.PlayerLeaderId;
            purpose = route == BasePrepRoute.Defense || route == BasePrepRoute.Second ? "defense" : "expedition";
            stamp = BaseMuster.Snapshot(state);
            mapKey = state.Campaign.CurrentMapId ?? state.Campaign.Location;
            var party = route is BasePrepRoute.BlackCrow or BasePrepRoute.Hideout or BasePrepRoute.Travel
                ? state.Muster.Parties.FirstOrDefault(p => p.Purpose == purpose) : null;
            if (route == BasePrepRoute.BlackCrow && state.Campaign.CurrentMapId != BaseDemoFinale.Camp)
                throw new InvalidOperationException("请到黑鸦寨备战");
            arrivedPartyId = party?.Id;
            var leaders = party == null ? BaseAdventure.Members(state).Where(BaseDevelopment.CanFight)
                : party.Leaders.Select(p => state.Leaders.Single(l => l.Id == p.LeaderId)).Where(l => TravelingParty || BaseAdventure.Alive(l) && l.Health > 0);
            var rows = leaders.OrderByDescending(l => l.Id == PlayerId).Select(l => new BasePrepUnit
            {
                Id = l.Id, Name = BasePersonnel.LeaderName(l), TeamId = l.Id, Kind = SoldierKind.Hero, Leader = l,
                Health = l.Health, MaxHealth = l.MaxHealth, Stats = BaseCommanderIdentity.Stats(l, new ArmyDefinition().Hero),
                Selected = true
            }).ToList();
            var teams = rows.ToArray();
            var assignedTeams = new Dictionary<int, string>();
            if (party != null)
                foreach (var allocation in BaseMuster.CurrentFormation(state, party) ?? Enumerable.Empty<BaseCommanderAllocation>())
                    if (allocation.SoldierNumbers != null && teams.Any(team => team.Id == allocation.LeaderId))
                        foreach (int number in allocation.SoldierNumbers) assignedTeams[number] = allocation.LeaderId;

            if (route != BasePrepRoute.Ruins || BaseRuinsRewards.Cleared(state, route))
                foreach (var (worker, index) in state.Workers.Select((worker, index) => (worker, index)))
                {
                    bool eligible = party == null ? BaseDevelopment.Eligible(state, purpose, worker) :
                        worker.Away && worker.PartyId == party.Id && (TravelingParty || worker.Health >= worker.MaxHealth * BaseDevelopment.DutyThreshold);
                    if (!eligible) continue;
                    var kind = BaseCampaign.ToBattle(worker.Kind);
                    for (int i = 0; i < worker.Count; i++)
                    {
                        var pick = BaseSoldierPick.At(state, index, i);
                        rows.Add(new BasePrepUnit
                        {
                            Id = "soldier:" + pick.SoldierNumber, Name = BattleSimulation.KindName(kind) + " #" + pick.SoldierNumber,
                            SoldierTemplateId = worker.TemplateId,
                            TeamId = assignedTeams.GetValueOrDefault(pick.SoldierNumber, teams.FirstOrDefault()?.Id),
                            Kind = kind, Pick = pick, Health = worker.Health, MaxHealth = worker.MaxHealth,
                            Stats = worker.HasCombatProfile ? worker.CombatStats.Clone() : new ArmyDefinition().Stats(kind).Clone(),
                            Selected = party != null && teams.Length > 0
                        });
                    }
                }
            units = rows.ToArray();
            if (party == null && (route != BasePrepRoute.Travel || draftTravelTroops))
                foreach (var (team, index) in teams.Select((team, index) => (team, index)))
                {
                    var desired = state.Muster.SlotTroops[BaseMuster.BarracksSlot(state,team.Id,index)];
                    foreach (var kind in BattleSimulation.TroopKinds)
                        SetTeamCount(team.Id, kind, desired.Count(BaseCampaign.ToBase(kind)));
                }
            if (Route != BasePrepRoute.Travel)
            {
                Board = Project(state);
                geometry = new RealtimeBattlePrototype(new PrototypeArena(Board.Cells.Select(c => !c.Walkable).ToArray(),
                    Board.Cells.Select(c => c.BlocksShots).ToArray(), null, Board.Width, Board.Height));
            }
            if (route == BasePrepRoute.Hideout)
            {
                this.hideoutSeed = hideoutSeed ?? BaseCampaign.NextBattleSeed(state, BaseBanditHideout.EventId);
                hideoutEnemies = BaseBanditHideout.DrawEnemies(state, this.hideoutSeed, hideoutEnemyCount);
                enemyReference = hideoutEnemies.Select(e => new BattleSoldier { Kind = e.Kind, Health = e.Health,
                    Stats = e.EffectiveStats(state.Demo.Rpg), Rpg = e.Rpg, OriginCommanderSlot = e.CommanderSlot }).ToArray();
            }
            else if (route == BasePrepRoute.Ruins && BaseRuinsSweep.Problem(state) == "")
            {
                try
                {
                    var reference = BaseDemoRuins.Begin(state.Copy(), "none");
                    enemyReference = reference.Battle.Soldiers.Where(u => u.Side == BattleSide.B && u.IsAlive).ToArray();
                }
                catch (InvalidOperationException) { }
            }
        }

        private BattleBoard Project(BaseState state)
        {
            if (Route == BasePrepRoute.Hideout)
            {
                state = state.Copy();
                BaseBanditHideout.Enter(state);
            }
            if (Route == BasePrepRoute.Ruins && BaseSceneRules.Entrance(state))
            {
                state = state.Copy();
                BaseSceneRules.EnterDungeon(state);
            }
            return BaseSceneCombat.Project(state, purpose == "defense" && BaseDesignRoutes.Active(state) ? "perimeter" : null);
        }

        private IEnumerable<BasePrepUnit> Taking(bool deployed) => units.Where(u => u.Selected && (!deployed || Positions.ContainsKey(u.Id)));
        public int RequiredSupply(bool deployed) => Route == BasePrepRoute.Travel ? 0 : Taking(deployed).Count(u => u.Kind != SoldierKind.Hero);
        public int AvailableSupply(BaseState state, int level) => level is 1 or 2
            ? BaseSupply.Available(state, "supply", level) + state.Supply.Quotas
                .Where(q => arrivedPartyId != null && q.PartyId == arrivedPartyId && q.Phase == "prepared")
                .SelectMany(q => q.Equipment).Where(e => e.Product == "supply" && e.Level == level).Sum(e => e.Count)
            : 0;

        public string SupplyProblem(BaseState state, bool deployed)
        {
            int required = RequiredSupply(deployed);
            if (required == 0) return "";
            if (SupplyGrade is not (1 or 2)) return StoryText.Get("story.ui.prep.validation.supply_grade", "请选择一级或二级军需");
            return required <= AvailableSupply(state, SupplyGrade) ? "" :
                StoryText.Get("story.ui.prep.validation.supply", "所选等级的军需库存不足");
        }

        public string Problem(BaseState state, bool deployed)
        {
            if (BaseSceneCombat.Active(state) || state.Aftermath.GameOver || stamp != BaseMuster.Snapshot(state) ||
                mapKey != (state.Campaign.CurrentMapId ?? state.Campaign.Location))
                return StoryText.Get("story.ui.prep.validation.changed", "现场已变化，请重新备战");
            if (Route == BasePrepRoute.Hideout && !BaseBanditHideout.CanEnter(state)) return BaseBanditHideout.ApproachText;
            if (TravelingParty && units.Any(unit => !unit.Selected)) return "请为每名随队士兵选定队长";
            if (!units.Any(u => u.Id == PlayerId && u.Selected)) return "主角必须出战";
            var leaders = Taking(deployed).Where(u => u.Kind == SoldierKind.Hero).Select(u => u.Id).ToHashSet();
            if (!leaders.Contains(PlayerId)) return StoryText.Get("story.ui.prep.validation.player", "请先部署主角");
            var soldiers = Taking(deployed).Where(u => u.Kind != SoldierKind.Hero).ToArray();
            if (units.Count(u => u.Selected && u.Kind != SoldierKind.Hero) > BaseMuster.BattleSoldierLimit) return "出战士兵最多24人";
            if (soldiers.Any(u => !leaders.Contains(u.TeamId))) return StoryText.Get("story.ui.prep.validation.captain", "请为出战士兵指定在场队长");
            if (deployed && Positions.Any(pair => !CanPlace(pair.Key, pair.Value))) return StoryText.Get("story.ui.prep.validation.cells", "请修正无效落点");
            return SupplyProblem(state, deployed);
        }

        internal static PrototypePoint Anchor(GridPosition cell) => new PrototypePoint(cell.X + .75, cell.Z + .25);

        public bool CanPlace(string id, GridPosition cell)
        {
            var unit = units.FirstOrDefault(u => u.Id == id);
            if (Board == null || unit?.Selected != true || !Board.Walkable(cell) || Board.Cell(cell).Zone != DeploymentZone.A ||
                unit.Kind != SoldierKind.Hero && !Positions.ContainsKey(unit.TeamId ?? "") ||
                Positions.Any(pair => pair.Key != id && pair.Value == cell)) return false;
            var point = Anchor(cell);
            var probe = new PrototypeUnit { Facing = East, Position = point };
            return geometry.Fits(probe, geometry.Arena.CellAt(point), East, false);
        }

        public bool Place(string id, GridPosition cell)
        {
            if (!CanPlace(id, cell)) return false;
            Positions[id] = cell;
            return true;
        }

        public void Recall(string id)
        {
            Positions.Remove(id);
            if (units.Any(u => u.Id == id && u.Kind == SoldierKind.Hero))
                foreach (var unit in units.Where(u => u.TeamId == id)) Positions.Remove(unit.Id);
        }

        public void Prune()
        {
            foreach (string id in Positions.Keys.ToArray())
                if (!CanPlace(id, Positions[id])) Recall(id);
        }

        public int AutoPlace()
        {
            Prune();
            var zone = Board.Positions().Where(cell => Board.Walkable(cell) && Board.Cell(cell).Zone == DeploymentZone.A).ToArray();
            if (zone.Length == 0) return 0;
            var center = new GridPosition((int)zone.Average(p => p.X), (int)zone.Average(p => p.Z));
            var cells = zone.OrderBy(cell => GridPosition.Distance(cell, center)).ToArray();
            int added = 0;
            foreach (var unit in Taking(false).OrderBy(u => u.Kind == SoldierKind.Hero ? 0 : 1)
                .ThenByDescending(u => u.Health / u.MaxHealth))
            {
                if (Positions.ContainsKey(unit.Id)) continue;
                var candidates = Positions.TryGetValue(unit.TeamId ?? "", out var lead)
                    ? cells.OrderBy(cell => GridPosition.Distance(cell, lead)).ToArray() : cells;
                foreach (var cell in candidates)
                {
                    if (!Place(unit.Id, cell)) continue;
                    added++;
                    break;
                }
            }
            return added;
        }

        internal BaseMusterPlan Plan(BaseState state, bool deployed)
        {
            var selected = Taking(deployed).ToArray();
            var preset = state.Muster.Presets.Single(p => p.Purpose == purpose).Copy();
            preset.SelectLeaders(selected.Where(u => u.Kind == SoldierKind.Hero).Select(u => u.Id));
            foreach (var kind in BattleSimulation.TroopKinds)
                preset.SetCount(BaseCampaign.ToBase(kind), selected.Count(u => u.Kind == kind));
            var picks = selected.Where(u => u.Pick != null).Select(u => u.Pick);
            if (Route != BasePrepRoute.Travel) return BaseMuster.Preview(state, purpose, true, preset, picks);
            var teams = preset.Candidates.Select(id =>
            {
                var row = new BaseCommanderAllocation { LeaderId = id, SoldierNumbers = selected.Where(u => u.Pick != null && u.TeamId == id)
                    .Select(u => u.SoldierNumber).ToList() };
                foreach (var kind in BattleSimulation.TroopKinds)
                    row.Requested.Set(BaseCampaign.ToBase(kind), selected.Count(u => u.Kind == kind && u.TeamId == id));
                return row;
            }).ToList();
            return BaseMuster.Preview(state, purpose, true, preset, picks, "", teams);
        }

        public BaseMusterPlan TravelPlan(BaseState state)
        {
            var problem = Problem(state, false);
            if (problem != "") throw new InvalidOperationException(problem);
            return Plan(state, false);
        }

        internal void CommitTravelParty(BaseState state)
        {
            if (!TravelingParty)
            {
                var plan = TravelPlan(state);
                if (!BaseMuster.Confirm(state, plan))
                    throw new InvalidOperationException("出征队伍有变化，请重新整备");
                return;
            }
            var problem = Problem(state, false);
            if (problem != "") throw new InvalidOperationException(problem);
            var party = state.Muster.Parties.Single(p => p.Id == arrivedPartyId);
            party.CommanderTroops = party.Leaders.Select(leader =>
            {
                var members = units.Where(unit => unit.Pick != null && unit.TeamId == leader.LeaderId).ToList();
                var row = new BaseCommanderAllocation { LeaderId = leader.LeaderId, SoldierNumbers = members.Select(unit => unit.SoldierNumber).ToList() };
                foreach (var kind in BattleSimulation.TroopKinds) row.Actual.Set(BaseCampaign.ToBase(kind), members.Count(unit => unit.Kind == kind));
                row.Requested = row.Actual.Copy();
                return row;
            }).ToList();
        }

        internal BasePreparedRoster Roster(BaseState state, bool deployed)
        {
            var problem = Problem(state, deployed);
            if (problem != "") throw new InvalidOperationException(problem);
            var selected = Taking(deployed).ToArray();
            return new BasePreparedRoster
            {
                Leaders = selected.Where(u => u.Kind == SoldierKind.Hero).Select(u => u.Id).ToArray(),
                Units = selected,
                Positions = deployed ? new Dictionary<string, GridPosition>(Positions) : null,
                ArrivedPartyId = arrivedPartyId,
                SupplyGrade = RequiredSupply(deployed) == 0 ? 0 : SupplyGrade
            };
        }

        public (double ally, double? enemy) Power(BaseState state, bool deployed)
        {
            var rule = state.Supply.Rules.FirstOrDefault(r => r.Product == "supply");
            var chosen = Taking(deployed).ToArray();
            var leaders = chosen.Where(unit => unit.Kind == SoldierKind.Hero).ToArray();
            double attackBonus = SupplyGrade == 1 ? rule?.LowBonus ?? 0 : SupplyGrade == 2 ? rule?.HighBonus ?? 0 : 0;
            double defenseBonus = SupplyGrade == 1 ? rule?.LowDefense ?? 0 : SupplyGrade == 2 ? rule?.HighDefense ?? 0 : 0;
            var allies = chosen.Select(u =>
            {
                var stats = u.Stats.Clone();
                stats.Health = (float)u.MaxHealth;
                if (u.Kind != SoldierKind.Hero)
                {
                    stats.Damage += (float)attackBonus;
                    stats.Defense += (float)defenseBonus;
                }
                stats.Damage *= (float)BaseEconomy.AttackFactor(state);
                int slot = Array.FindIndex(leaders, team => team.Id == u.TeamId);
                var actor = new BattleSoldier { Kind = u.Kind, Health = (float)u.Health, Stats = stats, OriginCommanderSlot = slot,
                    Rpg = u.Leader?.Rpg, AppearanceId = u.Leader == null ? "" : BaseCommanderIdentity.Appearance(u.Leader) };
                if (slot >= 0) BaseWine.Apply(actor, leaders[slot].Leader, state.Day);
                return actor;
            }).ToArray();
            var rules = state.Demo?.Rpg;
            if (enemyReference?.Length > 0 && rules != null && allies.Length > 0)
            {
                var comparison = BasePower.Engage(allies, enemyReference, rules,0,state.Aftermath.Difficulty.Preset);
                return (comparison.AllyPower, comparison.EnemyPower);
            }
            return (rules == null ? allies.Sum(u => u.Stats.BaseBattlePower) : BasePower.FormationRating(allies, rules), null);
        }

        public double AllyPower(BaseState state, bool deployed) => Power(state, deployed).ally;
        public double? KnownEnemyPower(BaseState state) => Power(state, false).enemy;
        public int? KnownEnemyCount() => enemyReference?.Length;
        public int KnownEnemyCount(SoldierKind kind) => enemyReference?.Count(unit => unit.Kind == kind) ?? 0;

        public BaseCampaignSession Begin(BaseState state)
        {
            if (Route == BasePrepRoute.Travel) throw new InvalidOperationException("行军编队不能在营地开战");
            var roster = Roster(state, true);
            var plan = arrivedPartyId == null ? Plan(state, true) : null;
            var session = Route == BasePrepRoute.Ruins ? BaseDemoRuins.Begin(state, "none", plan, roster)
                : Route == BasePrepRoute.Hideout ? BaseBanditHideout.Begin(state, plan, roster, hideoutEnemies, hideoutSeed)
                : Route == BasePrepRoute.BlackCrow ? BaseDemoFinale.Begin(state, "none", plan, roster)
                : BaseDemoDefense.Begin(state, "none", plan, roster, Route == BasePrepRoute.Second);
            BattleScenePlacement.Start(state, session);
            if (session.Battle.Phase == BattlePhase.Deployment)
                throw new InvalidOperationException("战斗部署未提交");
            var run = new RealtimeCampaignBattle(session);
            run.SaveCheckpoint(state);
            return session;
        }

        public BaseSweepPreview PreviewSweep(BaseState state) => BaseRuinsSweep.Preview(state, "none", this);
        public BaseSweepPreview Sweep(BaseState state) => BaseRuinsSweep.Resolve(state, "none", this);
        internal BaseCampaignSession BeginSweep(BaseState state, string mode)
        {
            var roster = Roster(state, false);
            var plan = Plan(state, false);
            return Route == BasePrepRoute.Hideout ? BaseBanditHideout.Begin(state, plan, roster, hideoutEnemies, hideoutSeed)
                : BaseDemoRuins.Begin(state, mode, plan, roster);
        }
    }

    internal sealed class BasePreparedRoster
    {
        public string[] Leaders;
        public BasePrepUnit[] Units;
        public Dictionary<string, GridPosition> Positions;
        public int SupplyGrade;
        public string ArrivedPartyId;
        public readonly Dictionary<string, BaseWorkerBucket> Troops = new Dictionary<string, BaseWorkerBucket>();

        public void CaptureTroops(BaseState state, BaseOutsideParty party)
        {
            var departing = state.Workers.Where(worker => worker.Away && worker.PartyId == party.Id).ToArray();
            foreach (var unit in Units.Where(u => u.Pick != null))
            {
                var troop = departing.Single(worker => worker.SoldierNumbers.Contains(unit.SoldierNumber));
                Troops.Add(unit.Id, troop.Slice(troop.SoldierNumbers.IndexOf(unit.SoldierNumber), 1));
            }
        }
    }
}

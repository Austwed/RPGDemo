using System;
using System.Collections.Generic;
using System.Linq;
using BaseConquest.Battle;
using BaseConquest.RealtimePrototype;

namespace BaseConquest.Base
{
    public sealed class LanDuelMatch
    {
        public const int Width = 20, Height = 14, DeploymentWidth = 5;
        private readonly RealtimeCampaignBattle run;
        private readonly Dictionary<int, LanDuelMember> origins = new Dictionary<int, LanDuelMember>();
        private readonly int[] lastCommand = { -1, -1 };
        private readonly bool[] pauseVotes = new bool[2];
        private bool paused;
        private double resumeDelay;
        private int revision;

        public LanDuelMatch(LanDuelRoster hostRoster, LanDuelRoster guestRoster, BaseState hostState)
        {
            var rules = hostState?.Demo?.Rpg ?? throw new InvalidOperationException("当前营地缺少演武规则");
            var scenario = new BattleScenario
            {
                Board = BattleBoard.CreateDefault(Width, Height), CampaignRules = true,
                TestAutoDeployment = false, PlayerLeaderSlot = 0, FogEnabled = true,
                ShowEnemiesThroughFog = false, MaxRounds = 300, RandomSeed = Guid.NewGuid().GetHashCode(),
                Objective = new ObjectiveConfig { Title = "营地模拟对抗" }
            };
            var opening = new BaseCampaignBattle
            {
                Id = "lan:duel", EventId = "lan:duel", StartedDay = 1, MapRegion = "duel",
                Scenario = scenario, Rpg = rules.Copy(), RealtimeRules = true, RealtimeRulesVersion = 1,
                TroopSlots = new List<int>()
            };
            var deployments = new Dictionary<GridPosition, LanDuelMember>();
            AddSide(opening, hostRoster, BattleSide.A, deployments);
            AddSide(opening, guestRoster, BattleSide.B, deployments);
            var session = new BaseCampaignSession(opening);
            if (session.Battle.Soldiers.Count != deployments.Count) throw new InvalidOperationException("有单位无法部署");
            session.Bind();
            if (!session.Battle.Start(out var error)) throw new InvalidOperationException(error);
            run = new RealtimeCampaignBattle(session);
            run.Model.BothSidesHuman = true;
            foreach (var actor in run.Model.Units)
            {
                var soldier = run.Battle.Soldier(actor.Id - 1);
                var original = deployments[soldier.Cell];
                origins.Add(actor.Id, original);
                // Apply the same final equipment, food, wine and provision snapshot on either side.
                soldier.Stats = original.stats.Clone();
                soldier.Health = original.hp;
                actor.Health = original.hp;
                actor.MaxHealth = soldier.Stats.Health;
                actor.AttackDamage = soldier.Stats.Damage;
                // Both players' main characters get the same cavalry shove protection.
                actor.Player = actor.Role == PrototypeRole.Leader && actor.Squad == 0;
                if (actor.Role == PrototypeRole.Leader)
                    run.SetDuelManualSkills((BattleSide)(int)actor.Side, actor.Squad);
            }
            run.Battle.Vision.Refresh();
            run.OutcomeResolver = battle =>
            {
                bool HasCommander(PrototypeSide side) => battle.Model.Units.Any(u =>
                    u.Side == side && u.Role == PrototypeRole.Leader && u.Alive);
                bool hostCanCommand = HasCommander(PrototypeSide.Ally);
                bool guestCanCommand = HasCommander(PrototypeSide.Enemy);
                if (!hostCanCommand || !guestCanCommand)
                    return hostCanCommand ? BattleOutcome.SideAWin : guestCanCommand ? BattleOutcome.SideBWin : BattleOutcome.Draw;
                return battle.Model.Elapsed >= scenario.MaxRounds ? BattleOutcome.Draw : BattleOutcome.None;
            };
            run.Model.SetPaused(false);
        }

        private static void AddSide(BaseCampaignBattle opening, LanDuelRoster roster, BattleSide side,
            Dictionary<GridPosition, LanDuelMember> deployments)
        {
            string problem = roster == null ? "缺少演武编队" : roster.Problem(opening.Rpg);
            if (problem != "") throw new ArgumentException(problem);
            var captains = roster.units.Where(u => u.kind == (int)SoldierKind.Hero).ToArray();
            var army = opening.Scenario.Army(side);
            army.HeroCount = captains.Length;
            army.Capacity = BaseMuster.BattleSoldierLimit;
            foreach (var kind in BattleSimulation.TroopKinds) army.SetCount(kind, roster.units.Count(u => u.kind == (int)kind));
            foreach (var captain in captains)
            {
                BaseRpg.ValidateCharacter(captain.profile, opening.Rpg);
                if (opening.Rpg.TemplateIdentity(captain.profile.TemplateId) == "soldier" ||
                    !opening.Rpg.Skills.Any(s => s.Id == captain.profile.SkillId))
                    throw new ArgumentException("当前版本无法识别队长技能");
                var equipment = captain.gear ?? Array.Empty<BasePersonalItem>();
                if (equipment.Select(i => i.Slot).Distinct().Count() != equipment.Length ||
                    equipment.Any(i => BasePersonalEquipment.Definition(opening.Rpg, i.Template) == null))
                    throw new ArgumentException("当前版本无法识别队长装备");
                army.CommanderNames.Add(captain.name);
                army.CommanderAppearances.Add(captain.appearance);
                army.CommanderStats.Add(captain.stats.Clone());
                var loadout = new CommanderLoadout { Strategy = SquadStrategy.Obey };
                foreach (var kind in BattleSimulation.TroopKinds)
                    loadout.Set(kind, roster.units.Count(u => u.team == captain.id && u.kind == (int)kind));
                army.Commanders.Add(loadout);
            }
            foreach (var m in roster.units.OrderByDescending(u => u.kind == (int)SoldierKind.Hero))
            {
                int slot = Array.FindIndex(captains, u => u.id == m.team);
                var kind = (SoldierKind)m.kind;
                var cell = new GridPosition(side == BattleSide.A ? m.x : Width - 1 - m.x, m.z);
                deployments.Add(cell, m);
                opening.Scenario.Placements.Add(new UnitPlacement { Side = side, Kind = kind, Cell = cell, CommanderSlot = slot });
                var gear = kind == SoldierKind.Hero ? new BasePersonalLoadout
                {
                    Version = 1, Weapon = BaseRpg.Weapon(m.profile.Profession),
                    BaseStats = m.stats.Clone(), Items = (m.gear ?? Array.Empty<BasePersonalItem>()).Select(i => i.Copy()).ToList()
                } : null;
                if (side == BattleSide.B)
                {
                    opening.Enemies.Add(new BaseEnemyMember { Id = m.id, Name = m.name, Appearance = m.appearance,
                        Kind = kind, CommanderSlot = slot, Health = m.hp, Stats = m.stats.Clone(), Equipment = gear,
                        Rpg = kind == SoldierKind.Hero ? m.profile.Copy() : new RpgCharacter
                        {
                            Version = 1, Id = m.id, Profession = BaseRpg.Profession(kind),
                            TemplateId = string.IsNullOrEmpty(m.soldierTemplateId)
                                ? opening.Rpg.DefaultTemplateId("soldier", BaseRpg.Profession(kind)) : m.soldierTemplateId
                        } });
                }
                else if (kind == SoldierKind.Hero)
                {
                    opening.Leaders.Add(new BaseLeader { Id = m.id, Name = m.name, AppearanceId = m.appearance,
                        Health = m.hp, MaxHealth = m.stats.Health, Rpg = m.profile.Copy(), PersonalEquipment = gear,
                        Training = new BaseTrainingProgress { Level = m.profile.Level }, HasCombatProfile = true,
                        CombatStats = m.stats.Clone() });
                }
                else
                {
                    opening.Troops.Add(new BaseWorkerBucket { Kind = BaseCampaign.ToBase(kind), Count = 1, TemplateId = m.soldierTemplateId,
                        Health = m.hp, MaxHealth = m.stats.Health, CombatStats = m.stats.Clone(), HasCombatProfile = true });
                    opening.TroopSlots.Add(slot);
                }
            }
        }

        public bool Apply(int side, LanDuelCommand cmd)
        {
            if ((uint)side > 1 || cmd == null || cmd.seq <= lastCommand[side] || run.Battle.Phase != BattlePhase.Fighting)
                return false;
            lastCommand[side] = cmd.seq;
            if (cmd.kind == "pause")
            {
                if (paused) return false;
                pauseVotes[side] = !pauseVotes[side];
                paused = pauseVotes[0] && pauseVotes[1];
                if (paused) run.Model.SetPaused(true);
                revision++;
                return true;
            }
            if (cmd.kind == "resume")
            {
                if (!paused || resumeDelay > 0) return false;
                pauseVotes[0] = pauseVotes[1] = false;
                resumeDelay = 3;
                revision++;
                return true;
            }
            if (paused) return false;
            var captain = run.Model.Units.FirstOrDefault(u => u.Id == cmd.actor && (int)u.Side == side &&
                u.Role == PrototypeRole.Leader && u.Alive);
            if (captain == null) return false;
            if (cmd.kind == "skill")
            {
                if (!run.CastSkill((BattleSide)side, captain.Squad)) return false;
                revision++;
                return true;
            }
            var point = new PrototypePoint(cmd.x, cmd.z);
            PrototypeOrder order;
            int targetId = -1;
            switch (cmd.kind)
            {
                case "move": order = PrototypeOrder.Move; break;
                case "guard": order = PrototypeOrder.Guard; point = captain.Position; break;
                case "attack":
                    var target = run.Model.Units.FirstOrDefault(u => u.Id == cmd.target && (int)u.Side != side && u.Alive);
                    if (target == null || !run.Battle.Vision.CanSee((BattleSide)side, run.Battle.Soldier(target.Id - 1))) return false;
                    // Reaffirming an active focus must not restart the attack windup.
                    if (captain.ManualOrder && captain.Order == PrototypeOrder.Focus && captain.SquadTargetId == target.Id)
                        return true;
                    point = target.Position;
                    targetId = target.Id;
                    order = PrototypeOrder.Focus;
                    break;
                default: return false;
            }
            if (!PrototypeArena.Finite(point.X) || !PrototypeArena.Finite(point.Z) ||
                !run.Model.IssueOrder(-1, captain.Id, order, point, targetId, side: captain.Side)) return false;
            run.Model.MarkDirectOrder(new[] { captain.Squad }, order, targetId, order == PrototypeOrder.Focus, captain.Side);
            revision++;
            return true;
        }

        public void Tick(double seconds)
        {
            if (!PrototypeArena.Finite(seconds) || seconds <= 0 || run.Battle.Phase != BattlePhase.Fighting) return;
            if (resumeDelay > 0)
            {
                double countdown = Math.Min(seconds, resumeDelay);
                resumeDelay -= countdown;
                seconds -= countdown;
                if (resumeDelay == 0) { paused = false; run.Model.SetPaused(false); }
            }
            if (!paused && seconds > 0) run.Tick(Math.Min(.25, seconds));
            revision++;
        }

        private readonly List<MageSpellVisual> spellFrame = new();

        public LanDuelFrame View(int side)
        {
            if ((uint)side > 1) throw new ArgumentOutOfRangeException(nameof(side));
            bool finished = run.Battle.Phase == BattlePhase.Finished;
            var observer = (BattleSide)side;
            var visible = run.Battle.Board.Positions().Select(p => run.Battle.Vision.Visible(observer, p)).ToArray();
            var frame = new LanDuelFrame { tick = revision, seconds = (float)run.Model.Elapsed,
                result = !finished ? 0 : run.Battle.Report.Outcome == BattleOutcome.SideAWin ? 1 :
                    run.Battle.Report.Outcome == BattleOutcome.SideBWin ? 2 : 3,
                paused = paused, voteA = pauseVotes[0], voteB = pauseVotes[1], resumeIn = (float)resumeDelay,
                width = Width, height = Height, visibleCells = visible
            };
            if (finished)
            {
                frame.aDeployed = run.Model.Units.Count(u => u.Side == PrototypeSide.Ally);
                frame.bDeployed = run.Model.Units.Count - frame.aDeployed;
                frame.aStanding = run.Model.Units.Count(u => u.Side == PrototypeSide.Ally && u.Alive);
                frame.bStanding = run.Model.Units.Count(u => u.Side == PrototypeSide.Enemy && u.Alive);
            }
#if UNITY_EDITOR || LAN_DUEL_QA
            if (finished && LanDuelDeviceCheck.TraceMatchups)
            {
                var hits = run.RpgAttackHistory.Where(p => p.TargetId > 0 && p.Settled && (!p.Hit || p.ImpactApplied && p.DamageApplied > 0))
                    .OrderByDescending(p => p.ImpactApplied);
                var arrows = hits.Where(p => run.Battle.Soldier(p.SourceId-1).ProfessionKind == SoldierKind.Ranged);
                frame.matchupRolls = new[]
                {
                    arrows.FirstOrDefault(p => p.Context.HasFlag(RpgShotContext.MovingCavalry)),
                    arrows.FirstOrDefault(p => p.Context == RpgShotContext.None && run.Battle.Soldier(p.TargetId-1).ProfessionKind == SoldierKind.Cavalry),
                    arrows.FirstOrDefault(p => p.Context.HasFlag(RpgShotContext.MeleePressure)),
                    hits.FirstOrDefault(p => p.DamageType == "magic" && p.Context != RpgShotContext.None)
                };
            }
#endif
            MageSpellVisual.Collect(run, observer, spellFrame);
            frame.spells = spellFrame.ToArray();
            frame.units = run.Model.Units.Where(u => (int)u.Side == side ||
                run.Battle.Vision.CanSee(observer, run.Battle.Soldier(u.Id - 1))).Select(u =>
            {
                var m = origins[u.Id];
                bool ownCaptain = (int)u.Side == side && u.Role == PrototypeRole.Leader;
                bool attacking = u.Alive && u.SwingUntil > run.Model.Elapsed && u.HitAt >= 0;
                double windup = u.Ranged ? .24 : .22;
                var facing = attacking ? u.AttackFacing : u.Facing;
                return new LanDuelActor { id = u.Id, side = (int)u.Side, kind = m.kind, slot = u.Squad,
                    name = m.name, appearance = m.appearance, footprintCells=u.FootprintLength,
#if UNITY_EDITOR || LAN_DUEL_QA
                    verifiedTemplate = LanDuelDeviceCheck.TraceMatchups ? run.Battle.Soldier(u.Id - 1).Rpg?.TemplateId : null,
#endif
                    attackRangeFine=(float)(u.Reach*PrototypeArena.Subdivision), x = (float)u.Position.X, z = (float)u.Position.Z,
                    fx = (float)facing.X, fz = (float)facing.Z, hp = u.Alive ? (float)u.Health : 0,
                    moving = u.Moving, attackPose = attacking ? (float)u.AttackPoseTime(run.Model.Elapsed, windup / (windup + .16)) : -1,
                    sightRadius = (int)u.Side == side ? run.Battle.Vision.SightRadius(run.Battle.Soldier(u.Id-1)) : 0,
                    maxHp = (float)u.MaxHealth, skill = ownCaptain ? run.SkillName(observer, u.Squad) : "",
                    cooldown = ownCaptain ? (float)run.SkillCooldown(observer, u.Squad) : 0 };
            }).ToArray();
            return frame;
        }
    }
}

namespace BaseConquest.Battle
{
    public sealed partial class RealtimeCampaignBattle
    {
        internal void SetDuelManualSkills(BattleSide side, int slot)
        {
            var skill = Skill(side, slot);
            if (skill != null) skill.Auto = false;
        }
    }
}

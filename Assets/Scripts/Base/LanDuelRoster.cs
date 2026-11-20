using System;
using System.Collections.Generic;
using System.Linq;
using BaseConquest.Battle;

namespace BaseConquest.Base
{
    // The wire carries only selected fighters and equipped items. Both camps may own
    // the same local soldier ID. The battle gives every placed unit its own match ID.
    [Serializable] public sealed class LanDuelMember
    {
        public string id, name, team, appearance, soldierTemplateId;
        public int kind, x, z;
        public float hp;
        public SoldierStats stats;
        public RpgCharacter profile;
        public BasePersonalItem[] gear;
    }

    [Serializable] public sealed class LanDuelRoster
    {
        public LanDuelMember[] units;

        public static LanDuelRoster From(BaseState state, BaseBattlePreparation draft)
        {
            string problem = draft.Problem(state, false);
            if (problem != "") throw new InvalidOperationException(problem);
            var selected = draft.Units.Where(u => u.Selected).OrderByDescending(u => u.Kind == SoldierKind.Hero).ToArray();
            var count = selected.Count(u => u.Kind != SoldierKind.Hero);
            int grade = draft.SupplyGrade;
            if (grade < 0 || grade > 2 || grade != 0 && draft.AvailableSupply(state, grade) < count)
                throw new InvalidOperationException("所选等级的军需库存不足");
            var supply = state.Supply.Rules.First(s => s.Product == "supply");
            double attack = grade == 1 ? supply.LowBonus : grade == 2 ? supply.HighBonus : 0;
            double defense = grade == 1 ? supply.LowDefense : grade == 2 ? supply.HighDefense : 0;
            var rows = new List<LanDuelMember>();
            // Place initial formations near the middle of the player's deployment zone.
            int[] lanes = {7,6,8,5,9,4,10,3,11,2,12,1};
            foreach (var unit in selected)
            {
                var stats = unit.Stats.Clone();
                stats.Health = (float)unit.MaxHealth;
                var actor = new BattleSoldier { Kind = unit.Kind, Stats = stats };
                var leader = selected.First(u => u.Id == unit.TeamId).Leader;
                BaseWine.Apply(actor, leader, state.Day);
                if (unit.Kind != SoldierKind.Hero)
                {
                    stats.Damage += (float)attack;
                    stats.Defense += (float)defense;
                }
                stats.Damage *= (float)BaseEconomy.AttackFactor(state) * actor.DrinkMultiplier;
                int index = rows.Count;
                string appearance = unit.Kind == SoldierKind.Mage ? "mage" : unit.Leader == null ? "" : BaseCommanderIdentity.Appearance(unit.Leader);
                if (unit.Id == state.PlayerLeaderId && string.IsNullOrEmpty(appearance)) appearance = "player";
                rows.Add(new LanDuelMember
                {
                    id = unit.Id, team = unit.TeamId, name = unit.Leader?.Name ?? unit.Name, kind = (int)unit.Kind,
                    x = LanDuelMatch.DeploymentWidth - 1 - index / lanes.Length, z = lanes[index % lanes.Length],
                    hp = (float)unit.Health, stats = stats, appearance = appearance, soldierTemplateId = unit.SoldierTemplateId,
                    profile = unit.Leader?.Rpg?.Copy(),
                    gear = unit.Leader?.PersonalEquipment?.Items.Select(i =>
                        new BasePersonalItem { Template = i.Template, Quality = i.Quality, Slot = i.Slot }).ToArray()
                });
            }
            var roster = new LanDuelRoster { units = rows.ToArray() };
            problem = roster.Problem(state.Demo.Rpg);
            if (problem != "") throw new InvalidOperationException(problem);
            return roster;
        }

        public string Problem(RpgRules rules = null)
        {
            if (units == null || units.Length == 0 || units.Length > BaseMuster.BattleSoldierLimit + 3)
                return "演武编队人数无效";
            var ids = new HashSet<string>();
            var cells = new HashSet<GridPosition>();
            var captains = new HashSet<string>();
            foreach (var u in units)
            {
                if (u == null || string.IsNullOrWhiteSpace(u.id) || u.id.Length > 160 || !ids.Add(u.id) ||
                    string.IsNullOrWhiteSpace(u.name) || u.name.Length > 128 || u.appearance?.Length > 96 ||
                    !Enum.IsDefined(typeof(SoldierKind), u.kind) || u.stats == null || !u.stats.PersonalProfileValid ||
                    u.stats.Damage > 2000000 || u.stats.Defense > 2000000 || !(u.hp > 0 && u.hp <= u.stats.Health))
                    return "演武单位资料无效";
                if (u.x <= 0 || u.x >= LanDuelMatch.DeploymentWidth || u.z <= 0 || u.z >= LanDuelMatch.Height - 1 ||
                    !cells.Add(new GridPosition(u.x, u.z))) return "请在己方区域内分开部署每名单位";
                if (u.kind == (int)SoldierKind.Hero)
                {
                    if (!string.IsNullOrEmpty(u.soldierTemplateId) || u.team != u.id || u.profile?.Version != 1 || u.profile.Traits == null || u.profile.Traits.Count > 3 ||
                        u.gear?.Length > 6 || (u.gear ?? Array.Empty<BasePersonalItem>()).Any(i => i == null ||
                        string.IsNullOrEmpty(i.Template) || i.Template.Length > 80 || !BasePersonalEquipment.Slots.Contains(i.Slot) ||
                        i.Quality < 0 || i.Quality > 5)) return "演武队长资料无效";
                    captains.Add(u.id);
                }
                else if (u.profile?.Version > 0 || u.gear?.Length > 0 || !string.IsNullOrEmpty(u.soldierTemplateId) &&
                    (u.soldierTemplateId.Length > 128 || rules != null && (rules.TemplateIdentity(u.soldierTemplateId) != "soldier" ||
                     !rules.MatchesTemplate(u.soldierTemplateId, BaseRpg.Profession((SoldierKind)u.kind)))))
                    return "普通士兵资料无效";
            }
            if (captains.Count < 1 || captains.Count > 3 || units.Length - captains.Count > BaseMuster.BattleSoldierLimit ||
                units.Any(u => !captains.Contains(u.team))) return "请为出战士兵指定一名出战队长";
            return "";
        }
    }

    [Serializable] public sealed class LanDuelCommand
    {
        public int seq, actor, target;
        public string kind;
        public float x, z;
    }

    [Serializable] public sealed class LanDuelActor
    {
        public int id, side, kind, slot, sightRadius, footprintCells;
        public float attackRangeFine;
        public string name, appearance, skill;
        public float hp, maxHp, x, z, fx, fz, cooldown, attackPose;
        public bool moving;
#if UNITY_EDITOR || LAN_DUEL_QA
        public string verifiedTemplate;
#endif
    }

    [Serializable] public sealed class LanDuelFrame
    {
        public int tick, result, width, height, aDeployed, bDeployed, aStanding, bStanding;
        public float seconds, resumeIn;
        public bool paused, voteA, voteB;
        public bool[] visibleCells;
        public LanDuelActor[] units;
        public MageSpellVisual[] spells;
#if UNITY_EDITOR || LAN_DUEL_QA
        public RpgAttackPacket[] matchupRolls;
#endif
    }
}

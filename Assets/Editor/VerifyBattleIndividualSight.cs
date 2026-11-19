using System;
using System.Linq;
using System.Reflection;
using BaseConquest.Base;
using BaseConquest.Battle;
using BaseConquest.RealtimePrototype;

namespace BaseConquest.Editor
{
    public static class VerifyBattleIndividualSight
    {
        public static void Run()
        {
            var guard = new SoldierStats { SightRadius = 5 };
            var scout = guard.Clone();
            scout.SightRadius = 8;
            Check(BaseDevelopment.ProfileKey(true,guard) != BaseDevelopment.ProfileKey(true,scout),
                "工人或归队士兵会合并不同视野的属性");
            foreach (var kind in BattleSimulation.TroopKinds)
            foreach (var side in new[] { BattleSide.A, BattleSide.B })
            {
                VerifyHeightMap(kind, side);
                VerifyDuel(kind, (int)side);
            }
            UnityEngine.Debug.Log("[IndividualSight] PASS: 12 combinations of troop, side, and battlefield");
        }

        private static void Check(bool pass, string message)
        {
            if (!pass) throw new InvalidOperationException(message);
        }

        private static void VerifyHeightMap(SoldierKind kind, BattleSide side)
        {
            var enemySide = side == BattleSide.A ? BattleSide.B : BattleSide.A;
            GridPosition At(int x, int z) => new GridPosition(side == BattleSide.A ? x : 31-x, z);
            var battle = new BattleSimulation(new BattleScenario
            {
                Board = BattleBoard.CreateDefault(32, 14), TestAutoDeployment = false,
                HeightVision = true, ShowEnemiesThroughFog = false
            });
            var vision = battle.Vision;
            Check(battle.Deploy(side, SoldierKind.Hero, At(1,1), 0) &&
                battle.Deploy(enemySide, SoldierKind.Hero, At(30,1), 0), "PVE commander setup");
            foreach (var captain in battle.Soldiers) captain.Stats.SightRadius = 2;
            var scout = battle.DeveloperSpawn(true, At(11,6), kind, side);
            var other = battle.DeveloperSpawn(true, At(12,7), kind, side);
            var target = battle.DeveloperSpawn(true, At(19,6), SoldierKind.Melee, enemySide);
            string label = $"PVE {side} {kind} ";
            Check(vision.SightRadius(scout) == 10, label+"legacy flat baseline");
            scout.Stats.SightRadius = 7;
            other.Stats.SightRadius = target.Stats.SightRadius = 2;
            void Expect(bool seen, string step)
            {
                vision.Refresh();
                Check(vision.CanSee(side,target) == seen && vision.Observes(scout,target.Cell) == seen,
                    label+step);
                Check(!vision.CanSee(enemySide,scout), label+step+" enemy kept private sight");
            }
            Expect(false,"distance 8");
            scout.Cell = At(12,6);
            Expect(true,"distance 7");
            scout.Cell = At(11,6);
            Expect(false,"leave range");
            Check(vision.Explored(side,target.Cell),label+"terrain remembered");
            scout.Cell = At(12,6);
            target.Cell = At(23,6);
            battle.Board.Cell(scout.Cell).Elevation = 2;
            Check(vision.SightRadius(scout) == 11, label+"elevation bonus");
            Expect(true,"elevated distance 11");
            battle.Board.Cell(At(18,6)).Terrain = TerrainKind.Wall;
            Expect(false,"wall hides enemy");
            battle.Board.Cell(At(18,6)).Terrain = TerrainKind.Ground;
            target.Cell = At(24,6);
            Expect(false,"elevated distance 12");
            scout.Stats.SightRadius = 0;
            Check(vision.SightRadius(scout) == 14,label+"legacy elevation fallback");
            scout.Stats.SightRadius = 7;
            target.Cell = At(23,6);
            Expect(true,"before scout death");
            Check(battle.DeveloperUnit(true,scout.Id,DeveloperUnitAction.KnockDown),label+"knockdown");
            Expect(false,"scout lost");
            battle.Scenario.DefenderSide = (int)side;
            battle.Scenario.DefenderVisionEnabled = true;
            Check(vision.CanSee(side,target) && !vision.Visible(enemySide,At(1,1)),label+"home defence");
        }

        private static LanDuelRoster Roster(SoldierKind kind, int radius)
        {
            var captain = new LanDuelMember { id="player", team="player", name="队长", appearance="player",
                kind=(int)SoldierKind.Hero, x=1, z=12, hp=200, stats=new SoldierStats { Health=200, SightRadius=2 },
                profile=new RpgCharacter { Version=1, Id="player", TemplateId="captain_awakened_warrior", SkillId="inspire" } };
            var soldier = new LanDuelMember { id="soldier:1", team="player", name="侦察兵", kind=(int)kind,
                x=3, z=5, hp=100, stats=new SoldierStats { Health=100, SightRadius=radius, HasCombatKind=true,
                    CombatKind=kind, HasProfessionKind=true, ProfessionKind=kind } };
            return new LanDuelRoster { units=new[] { captain,soldier } };
        }

        private static void VerifyDuel(SoldierKind kind, int side)
        {
            var state = new BaseState { Demo=new BaseDemoProgress { Rpg=RpgRules.Defaults() } };
            var duel = new LanDuelMatch(Roster(kind,side==0 ? 5 : 0),Roster(kind,side==1 ? 5 : 0),state);
            // Stage the two scouts on either side of a visibility boundary, then advance normal authority ticks.
            var field = typeof(LanDuelMatch).GetField("run",BindingFlags.NonPublic|BindingFlags.Instance);
            var run = (RealtimeCampaignBattle)field.GetValue(duel);
            var scout = run.Model.Units.Single(u => (int)u.Side==side && u.Role!=PrototypeRole.Leader);
            var target = run.Model.Units.Single(u => (int)u.Side!=side && u.Role!=PrototypeRole.Leader);
            var commander = run.Model.Units.Single(u => (int)u.Side==side && u.Role==PrototypeRole.Leader);
            string label = $"LAN {side} {kind} ";
            int X(int x) => side==0 ? x : LanDuelMatch.Width-1-x;
            void Place(PrototypeUnit u, int x) =>
                Check(run.Model.PlaceNearest(u,new PrototypePoint(X(x)+.25,6.25)), label+"fixture placement");
            int seq = 0;
            bool Focus() => duel.Apply(side,new LanDuelCommand { seq=++seq, kind="attack", actor=commander.Id, target=target.Id });
            var receiving = duel.View(1-side).units.Single(u=>u.id==target.Id);
            Check(receiving.sightRadius==6,label+"legacy duel baseline");
            run.Battle.Soldier(target.Id-1).Stats.SightRadius = 2;
            Place(target,13);
            void Expect(bool visible, string step)
            {
                duel.Tick(.02);
                var view=duel.View(side);
                Check(view.visibleCells[6*view.width+X(13)] == visible &&
                    view.units.Any(u=>u.id==target.Id) == visible, label+step+" live snapshot");
                Check(!duel.View(1-side).units.Any(u=>u.id==scout.Id),label+step+" independent opponent sight");
                Check(view.units.Single(u=>u.id==scout.Id).sightRadius==5,label+step+" owner stat");
                if (visible) Check(view.units.Single(u=>u.id==target.Id).sightRadius==0,label+step+" enemy stat private");
                Check(Focus()==visible,label+step+" authoritative focus");
            }
            Place(scout,7);
            Expect(false,"distance 6");
            Place(scout,8);
            Expect(true,"distance 5");
            Place(scout,7);
            Expect(false,"left radius");
            Place(scout,8);
            Expect(true,"returned");
            run.Model.ApplyResolvedHit(target,scout,scout.Health);
            Check(!scout.Alive,label+"resolved lethal hit");
            Expect(false,"after death");
        }
    }
}

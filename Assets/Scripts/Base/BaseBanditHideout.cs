using System;
using System.Collections.Generic;
using System.Linq;
using BaseConquest.Battle;
using BaseConquest.Story;

namespace BaseConquest.Base
{
    public static class BaseBanditHideout
    {
        public const string EntryId = "demo_bandit_hideout";
        public const string MapId = "demo_bandit_hideout_map";
        public const string EventId = "demo:bandit-hideout";
        public const int EnemyCountCap = 12;
        internal const string Region = "hideout";
        private const string EntranceMapId = "demo_ruins";
        private static readonly GridPosition[] Neighbors = {
            new GridPosition(1, 0), new GridPosition(-1, 0), new GridPosition(0, 1), new GridPosition(0, -1)
        };

        public static string Title => StoryText.Get("story.ui.hideout.title", "土匪窝点");
        public static string Objective => StoryText.Get("story.ui.hideout.objective", "击败全部土匪");
        public static string VictoryText => StoryText.Get("story.ui.hideout.victory", "土匪已被击退");
        public static string DefeatText => StoryText.Get("story.ui.hideout.defeat", "未能击退土匪");
        public static string ReportText => StoryText.Get("story.ui.hideout.report", "按实际参战结算军需和战损，结算后返回窝点入口。");
        public static string ReturnText => StoryText.Get("story.ui.hideout.return", "结算并返回入口");
        internal static string ApproachText => StoryText.Get("story.ui.hideout.approach", "请到土匪窝点入口备战");
        internal static string CountText(int total, int melee, int ranged, int cavalry) => string.Format(
            StoryText.Get("story.ui.hideout.enemy_counts", "士兵 {0} 人 · 剑 {1} · 弓 {2} · 骑 {3}"), total, melee, ranged, cavalry);
        internal static bool Installed(BaseState state) => state.Campaign.Layouts?.Any(map => map?.SourceMapId == MapId) == true;

        public static bool InHideout(BaseState state) => BaseSceneInstances.Active(state)
            ? state.Campaign.Location == "custom" && state.Campaign.CurrentMapId == MapId
            : state.Campaign.Location == Region;

        internal static bool BattleActive(BaseState state) => InHideout(state)
            && state.Campaign.HasActiveBattle && OwnsBattlefield(state.Campaign.ActiveBattle);

        internal static bool OwnsBattlefield(BaseCampaignBattle battle) => battle?.EventId == EventId
            && (battle.MapRegion == Region || battle.MapRegion == "custom" && battle.MapId == MapId);

        private static int WallColumn(BattleBoard board) => Math.Min(15, board.Width - 4);

        internal static bool CanEnter(BaseState state)
        {
            if (!BaseSceneRules.Entrance(state)) return false;
            var entrance = BaseSpatial.Objects(state).SingleOrDefault(item => item.Id == EntryId);
            return BaseInteraction.CanReach(state, entrance);
        }

        public static void Install(BaseState state)
        {
            if (!BaseSceneRules.Active(state) || !BaseRpg.Active(state) || !BaseDemoRuins.Enabled(state)) return;
            var campaign = state.Campaign;
            if (campaign.Layouts == null || campaign.RegionMaps == null || campaign.Events == null) return;
            bool instances = BaseSceneInstances.Active(state);
            var entrance = campaign.Layouts.SingleOrDefault(map => map != null
                && (instances ? map.SourceMapId == EntranceMapId : map.Region == "outpost"));
            var dungeon = campaign.Layouts.SingleOrDefault(map => map?.SourceMapId == BaseSceneRules.DungeonId);
            var entranceBoard = campaign.RegionMaps.SingleOrDefault(map => map?.Id == (instances ? EntranceMapId : "outpost"))?.Board;
            var dungeonBoard = campaign.RegionMaps.SingleOrDefault(map => map?.Id == (instances ? BaseSceneRules.DungeonId : "dungeon"))?.Board;
            var ruins = entrance?.Objects?.SingleOrDefault(o => o?.Id == "demo_ruins_investigate");
            if (ruins == null || dungeon == null || entranceBoard == null || dungeonBoard == null) return;
            if(state.Demo.StoryScene?.Version!=2)InstallOutdoorEntrance(entrance, entranceBoard, ruins);
            var mission = campaign.Events.SingleOrDefault(e => e?.Id == EventId);
            if (mission != null)
            {
                mission.FoodReward = mission.MaterialReward = mission.CoinReward = 0;
                mission.ItemRewards.Clear();
            }
            if (Installed(state)) return;

            var map = dungeon.Copy();
            map.Region = instances ? "custom" : Region;
            map.SourceMapId = MapId;
            map.ParentMapId = instances ? EntranceMapId : null;
            map.Title = Title;
            map.Objects.RemoveAll(o => o.Id == "demo_ruins_investigate" || o.Id == "demo_dungeon_exit");
            foreach (var decoration in map.Objects)
            {
                decoration.VisualOnly = true;
                decoration.SourceObjectId = MapId + ":" + decoration.Id;
            }
            campaign.Layouts.Add(map);
            string boardId = instances ? MapId : Region;
            campaign.RegionMaps.RemoveAll(regionMap => regionMap.Id == boardId);
            campaign.RegionMaps.Add(new BaseRegionMap { Id = boardId, Board = dungeonBoard.Clone() });
            if (instances) campaign.WorldJourney?.Nodes?.FirstOrDefault(n => n.Id == "camp")?.MapIds.Add(MapId);
            if (mission == null)
                campaign.Events.Add(new BaseCampaignEvent { Id = EventId, Title = Title, Kind = "attack",
                    ScenarioFile = "24_据点进攻.json", FoodRaid = 0, MaterialRaid = 0, CoinRaid = 0,
                    FoodReward = 0, MaterialReward = 0, CoinReward = 0, ItemRewards = new List<BaseLootReward>() });
        }

        private static void InstallOutdoorEntrance(BaseMapLayout map, BattleBoard board, BaseMapPlacement ruins)
        {
            int wall = WallColumn(board);
            // 封住墙内缺口，保留两端绕行。
            for (int row = 4; row <= 19 && row < board.Height - 1; row++)
            {
                var cell = board.Cell(new GridPosition(wall, row));
                if (cell.Terrain != TerrainKind.Ground || cell.Ramp != RampAxis.None ||
                    map.Objects.Any(item => item.Object.Contains(wall + .5, row + .5))) continue;
                cell.Terrain = TerrainKind.Wall;
                cell.Zone = DeploymentZone.None;
            }

            var entrance = map.Objects.SingleOrDefault(item => item.Id == EntryId);
            if (entrance != null && entrance.X >= wall + 1) return;
            var spot = FindEntrance(map, board, ruins, wall);
            if (spot == null) throw new InvalidOperationException("断壁后没有可达的同层副本入口位置");
            if (entrance == null)
            {
                entrance = new BaseMapPlacement { Id = EntryId, Name = Title, SourceObjectId = "demo:portal:bandit-hideout",
                    AssetId = "dungeon-entrance", Kind = "demo-prop" };
                map.Objects.Add(entrance);
            }
            entrance.X = spot.Value.X;
            entrance.Y = spot.Value.Y;
        }

        private static BasePoint? FindEntrance(BaseMapLayout map, BattleBoard board, BaseMapPlacement portal, int wall)
        {
            var objects = map.Objects.Where(o => o.Id != EntryId).Select(o => o.Object).ToArray();
            var origin = new GridPosition((int)map.SpawnX, (int)map.SpawnY);
            var target = new BasePoint(wall + 3.5, Math.Min(12.5, board.Height - 1.5));
            var level = board.SurfaceLevel(portal.X, portal.Y);
            var reached = new HashSet<GridPosition> { origin };
            var open = new Queue<GridPosition>();
            open.Enqueue(origin);
            bool Walkable(double x, double y) => BaseSpatial.Geometry(objects, x, y, board.Width, board.Height)
                && BaseSpatial.TerrainWalkable(board, x, y, objects);
            while (open.Count > 0)
            {
                var cell = open.Dequeue();
                foreach (var offset in Neighbors)
                {
                    var next = cell + offset;
                    if (reached.Contains(next) || !board.CanStep(cell, next) || !Walkable(next.X + .5, next.Z + .5)
                        || !Walkable((cell.X + next.X + 1) / 2d, (cell.Z + next.Z + 1) / 2d)) continue;
                    reached.Add(next);
                    open.Enqueue(next);
                }
            }
            bool Available(BasePoint point) => point.X >= wall + 1 && Walkable(point.X, point.Y)
                && Math.Abs(board.SurfaceLevel(point.X, point.Y) - level) < .000001
                && objects.All(o => Math.Abs(o.X - point.X) >= (o.BoundsWidth + 1) / 2
                    || Math.Abs(o.Y - point.Y) >= (o.BoundsHeight + 1) / 2);
            if (reached.Contains(new GridPosition((int)target.X, (int)target.Y)) && Available(target)) return target;
            return reached.Select(cell => new BasePoint(cell.X + .5, cell.Z + .5)).Where(Available)
                .OrderBy(point => point.Distance(target)).ThenBy(point => point.X).ThenBy(point => point.Y)
                .Select(point => (BasePoint?)point).FirstOrDefault();
        }

        internal static List<BaseEnemyMember> DrawEnemies(BaseState state, int seed, int? count)
        {
            int size = count ?? state.Demo.Rpg.HideoutEnemyCount;
            if (size == 0)
            {
                try { size = DemoBalanceRuntime.Load().Rules.HideoutEnemyCount; }
                catch (InvalidOperationException) { }
                if (size == 0) size = EnemyCountCap;
            }
            if (size < 1 || size > EnemyCountCap) throw new ArgumentOutOfRangeException(nameof(count));
            var random = new Random(seed);
            var enemies = new List<BaseEnemyMember>(size);
            for (int i = 0; i < size; i++)
            {
                var kind = BattleSimulation.TroopKinds[random.Next(BattleSimulation.TroopKinds.Length)];
                int profession = BaseRpg.Profession(kind);
                var id = $"{EventId}:{seed}:unit:{i}";
                var character = new RpgCharacter { Version = 1, Id = id, Profession = profession,
                    TemplateId = state.Demo.Rpg.DefaultTemplateId("soldier", profession), Rules = state.Demo.Rpg };
                var stats = state.Demo.Rpg.Bare(character, 1);
                enemies.Add(new BaseEnemyMember { Id = id, Name = BattleSimulation.KindName(kind), Kind = kind,
                    CommanderSlot = -1, Health = stats.Health, Stats = stats, Rpg = character });
            }
            return enemies;
        }

        internal static void Enter(BaseState state)
        {
            if (!CanEnter(state)) throw new InvalidOperationException(ApproachText);
            state.Campaign.Location = BaseSceneInstances.Active(state) ? "custom" : Region;
            if (BaseSceneInstances.Active(state)) state.Campaign.CurrentMapId = MapId;
            var map = BaseSpatial.Layout(state);
            state.PlayerX = map.SpawnX;
            state.PlayerY = map.SpawnY;
            BaseAdventure.Arrive(state);
        }

        private static void Leave(BaseState state)
        {
            state.Campaign.Location = BaseSceneInstances.Active(state) ? "custom" : "outpost";
            if (BaseSceneInstances.Active(state)) state.Campaign.CurrentMapId = EntranceMapId;
            var portal = BaseSpatial.Objects(state).Single(o => o.Id == EntryId);
            var spot = BaseSpatial.Nearest(state, new BasePoint(portal.X, portal.Y - .9));
            state.PlayerX = spot.X;
            state.PlayerY = spot.Y;
            BaseAdventure.Arrive(state);
        }

        internal static BaseCampaignSession Begin(BaseState state, BaseMusterPlan plan, BasePreparedRoster roster,
            IReadOnlyList<BaseEnemyMember> enemies, int seed)
        {
            Enter(state);
            BaseRuinsRewards.Skip(state, BasePrepRoute.Hideout);
            var mission = state.Campaign.Events.Single(e => e.Id == EventId);
            mission.Status = "pending";
            mission.EnemyRecoveryDay = 0;
            mission.EnemyArmy = enemies.Select(e => e.Copy()).ToList();
            var army = new ArmyDefinition { HeroCount = 0, CaptainSlot = -1 };
            foreach (var kind in BattleSimulation.TroopKinds) army.SetCount(kind, enemies.Count(e => e.Kind == kind));
            var document = new BattleScenarioDocument { Title = Title, Scenario = new BattleScenario {
                Board = BaseSceneCombat.Project(state), A = new ArmyDefinition { HeroCount = 1, MeleeCount = 0,
                    RangedCount = 0, CavalryCount = 0, Commanders = new List<CommanderLoadout> { new CommanderLoadout() } },
                B = army, BanditHideout = true, RandomSeed = seed,
                MaxRounds = 90, Objective = new ObjectiveConfig { Title = Objective }
            } };
            var opening = BaseCampaign.Begin(state, EventId, document, "none", plan, true, roster);
            var session = new BaseCampaignSession(opening);
            if (!opening.ManualDeployment) session.Battle.AutoDeploy(BattleSide.A);
            if (session.Battle.Soldiers.Count(u => u.Side == BattleSide.A) != roster.Units.Length
                || session.Battle.Soldiers.Count(u => u.Side == BattleSide.B) != enemies.Count)
                throw new InvalidOperationException("无法部署全部参战单位");
            session.Bind();
            if (!session.Battle.Start(out string problem)) throw new InvalidOperationException(problem);
            return session;
        }

        public static bool Complete(BaseState state, RealtimeCampaignBattle run)
        {
            if (run == null || !OwnsBattlefield(run.Session.Opening) || !InHideout(state)
                || state.Campaign.ActiveBattle?.Id != run.Session.Opening.Id) return false;
            bool victory = run.Battle.Report?.Outcome == BattleOutcome.SideAWin;
            if (victory)
            {
                run.SaveCheckpoint(state);
                if (!BaseCampaign.Settle(state, run.Session)) return false;
            }
            else if (!BaseDemoCombat.Fail(state, run)) return false;
            var mission = state.Campaign.Events.Single(e => e.Id == EventId);
            mission.EnemyArmy.Clear();
            mission.EnemyRecoveryDay = 0;
            Leave(state);
            if (victory)
            {
                string battleId = run.Session.Opening.Id;
                BaseRuinsRewards.RecordVictory(state, battleId, BasePrepRoute.Hideout);
                var loot = state.Aftermath.Loot.Single(item => item.BattleId == battleId);
                if (loot.Items.Count == 0 && !state.Aftermath.Prisoners.Any(p => p.BattleId == battleId && p.Status == "held"))
                    BaseBattleLoot.Finish(state, battleId);
            }
            return true;
        }
    }

    public sealed partial class BaseSceneController
    {
        private void BeginBanditHideout(BaseBattlePreparation prepared)
        {
            Companions.Capture(State);
            var before = State.Copy();
            BaseCampaignSession encounter = null;
            if (!Change(next => encounter = prepared.Begin(next), ""))
            {
                Hud.RefreshPreparedFailure();
                return;
            }
            try
            {
                if (!DeploymentWorldActive) RefreshDemoRegion();
                BeginSceneEncounter(encounter, null, null, FinishBanditHideout, BaseBanditHideout.Objective, prepared);
            }
            catch (Exception error)
            {
                MapSession.LeaveBattle();
                SaveStore.Save(before);
                State = before;
                EndDeploymentWorld(false);
                if (combatController != null)
                {
                    combatController.gameObject.SetActive(false);
                    BattleBoardView.DestroyObject(combatController.gameObject);
                    combatController = null;
                }
                RefreshDemoRegion();
                ReportMessage("未启动战斗，已恢复战前人员与军需：" + error.Message);
            }
        }

        private bool FinishBanditHideout(RealtimeCampaignBattle run, bool abandon, bool mainMenu)
        {
            var next = State.Copy();
            if (!BaseBanditHideout.Complete(next, run)) return false;
            BaseEconomy.Validate(next);
            SaveStore.Save(next);
            State = next;
            FinishLocalScene(mainMenu, () =>
            {
                if (BaseRuinsRewards.PendingBattle(State, BasePrepRoute.Hideout) != "") Hud.OpenRuinsReward(BasePrepRoute.Hideout);
                else if (BaseBattleLoot.Pending(State)) Hud.OpenLoot(run.Session.Opening.Id);
            });
            return true;
        }
    }
}

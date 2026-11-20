using System;
using System.Collections.Generic;
using System.Linq;
using BaseConquest.Battle;
using BaseConquest.Presentation;
using UnityEngine;

namespace BaseConquest.Base
{
    public sealed partial class BaseHud
    {
        private UnityEngine.UI.Button developerModeButton;

        private void BindDeveloperModeButton(UnityEngine.UI.Button backpack, bool authoredResources)
        {
            if (!RuntimeDeveloperOptions.Available) return;
            var bag = (RectTransform)backpack.transform;
            const float gap = 8;
            float width = 92;
            if (authoredResources && resourceStrip.Find("Stats/Troops") is RectTransform troops)
            {
                var bagLeft = bag.parent.InverseTransformPoint(bag.TransformPoint(bag.rect.min)).x;
                var cardRight = troops.parent.IsChildOf(bag.parent)
                    ? bag.parent.InverseTransformPoint(troops.TransformPoint(troops.rect.max)).x : bagLeft - width - gap;
                width = Mathf.Clamp(bagLeft - cardRight - gap * 2, 56, width);
            }
            var position = bag.anchoredPosition + new Vector2(
                -bag.rect.width * bag.pivot.x - width * .5f - gap, bag.rect.height * (.5f - bag.pivot.y));
            developerModeButton = MakeButton(bag.parent, width < 76 ? "开发" : "开发者", position, new Vector2(width, bag.rect.height), () =>
            {
                if (owner.DeveloperAvailable && !owner.DeveloperUiBlocked)
                    RuntimeDeveloperOptions.Enabled = !RuntimeDeveloperOptions.Enabled;
            }, bag.anchorMin);
            developerModeButton.name = "DeveloperModeToggle";
            if (!authoredResources && bag.parent.Find("任务") is RectTransform tasks)
            {
                tasks.anchoredPosition = new Vector2(position.x - (width + tasks.rect.width) * .5f - gap, tasks.anchoredPosition.y);
                var bounds = summary.rectTransform.offsetMax;
                bounds.x = tasks.anchoredPosition.x - tasks.rect.width * tasks.pivot.x - gap;
                summary.rectTransform.offsetMax = bounds;
            }
            if (Application.platform == RuntimePlatform.Android) ResizeTouchButton(developerModeButton, 52, 18);
            RefreshDeveloperModeButton(RuntimeDeveloperOptions.Enabled);
        }

        internal void RefreshDeveloperModeButton(bool enabled)
        {
            if (developerModeButton == null) return;
            var palette = DemoUiTheme.Current;
            developerModeButton.targetGraphic.color = enabled ? palette.Gold : palette.Button;
        }
    }

    public sealed partial class BaseSceneController
    {
        private BaseExplorationDiagnostics developerDiagnostics;

        internal void HideDeveloperDiagnostics()
        {
            if (developerDiagnostics != null) developerDiagnostics.Root.SetActive(false);
        }

        internal void RemoveDeveloperDiagnostics()
        {
            developerDiagnostics?.Dispose();
            developerDiagnostics = null;
        }

        internal string RefreshDeveloperDiagnostics()
        {
            var board = regionView.Board;
            var map = BaseSpatial.Objects(State);
            var actorObstacles = Companions.PlayerObstacles(State).ToArray();
            var position = new BasePoint(State.PlayerX, State.PlayerY);
            var facing = BaseInteraction.Facing;
            var target = Nearby;
            developerDiagnostics ??= new BaseExplorationDiagnostics(regionView.Root.transform, WorldPoint);
            developerDiagnostics.Root.SetActive(true);
            developerDiagnostics.RefreshGround(State, board, map);
            developerDiagnostics.RefreshActors(State, position, facing, target,
                DisplayObjects.Where(o => o.Kind == "npc" && SceneObjectVisible(o)).Concat(Companions.Footprints), actorObstacles);

            developerDiagnostics.BeginRoutes();
            foreach (var actor in Companions.Agents)
                if (actor.Visible) developerDiagnostics.AddRoute(actor.Position, actor.Route, actor.Waypoint);
            foreach (var worker in workerTravel.Active)
                developerDiagnostics.AddRoute(worker.Position, worker.Route, worker.Waypoint);
            if (BaseDemoPrologue.Village(State) && State.Demo.Intro is BaseDemoIntroState intro && !intro.ReporterGone)
                developerDiagnostics.AddRoute(new BasePoint(intro.ReporterX, intro.ReporterY), intro.ReporterRoute, intro.ReporterWaypoint);
            if (State.Campaign.Location == "base" && State.Demo?.Flow?.FarmWalking == true && !State.Demo.Flow.FarmOfferShown)
                developerDiagnostics.AddRoute(new BasePoint(State.Demo.Flow.FarmNpcX, State.Demo.Flow.FarmNpcY), farmRoute, farmRouteIndex);
            developerDiagnostics.EndRoutes();

            var cell = new GridPosition((int)Math.Floor(position.X), (int)Math.Floor(position.Y));
            var ground = board.Cell(cell);
            var player = State.Leaders.FirstOrDefault(actor => actor.Id == State.PlayerLeaderId);
            string title = State.Campaign.CurrentMapId ?? State.Campaign.Location;
            string subject = target == null ? "—" : target.Name + $" · {target.Distance(position.X, position.Y):0.00} 格";
            return $"{title}   地格 ({cell.X}, {cell.Z})   高度 {board.SurfaceLevel(position.X, position.Y):0.00}\n" +
                $"位置 ({position.X:0.00}, {position.Y:0.00})   朝向 ({facing.X:0.0}, {facing.Y:0.0})   生命 {player?.Health:0.#}/{player?.MaxHealth:0.#}\n" +
                $"地形 {DeveloperTerrain.Name(ground?.Terrain ?? default)}   交互 {subject}\n" +
                $"浅色：地格   红：阻挡   黄：交互范围   绿：可交互   蓝：路线";
        }
    }

    internal sealed class BaseExplorationDiagnostics : IDisposable
    {
        public GameObject Root { get; }
        private readonly Material material;
        private readonly Func<double, double, Vector3> worldPoint;
        private readonly Mesh grid, terrain, footprints, reach, routes;
        private readonly List<Vector3> vertices = new List<Vector3>();
        private readonly List<Color32> colors = new List<Color32>();
        private readonly List<int> indices = new List<int>();
        private readonly List<BaseObjectRect> staticBlockers = new List<BaseObjectRect>();
        private BaseState drawnState;
        private BattleBoard drawnBoard;
        private static readonly Color32 GridColor = new Color32(229, 238, 247, 88);
        private static readonly Color32 BlockColor = new Color32(255, 77, 79, 220);
        private static readonly Color32 ReachColor = new Color32(255, 208, 85, 200);
        private static readonly Color32 ReadyColor = new Color32(115, 245, 120, 240);
        private static readonly Color32 RouteColor = new Color32(55, 202, 255, 235);

        public BaseExplorationDiagnostics(Transform parent, Func<double, double, Vector3> worldPoint)
        {
            this.worldPoint = worldPoint;
            Root = new GameObject("DeveloperDiagnostics");
            Root.transform.SetParent(parent, false);
            material = new Material(Resources.Load<Shader>("Presentation/DevelopmentLines"));
            grid = AddMesh("DebugGrid", 0);
            terrain = AddMesh("BlockedTerrain", 1);
            footprints = AddMesh("CollisionFootprints", 2);
            reach = AddMesh("InteractionReach", 3);
            routes = AddMesh("ActiveRoutes", 4);
        }

        private Mesh AddMesh(string name, int priority)
        {
            var layer = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            layer.transform.SetParent(Root.transform, false);
            var mesh = new Mesh { name = name };
            mesh.MarkDynamic();
            layer.GetComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = layer.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.sortingOrder = 500 + priority;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return mesh;
        }

        public void RefreshGround(BaseState state, BattleBoard board, BaseMapObject[] map)
        {
            if (drawnState == state && drawnBoard == board) return;
            drawnState = state;
            drawnBoard = board;
            staticBlockers.Clear();
            foreach (var item in map)
            {
                if (item.Blocks && !item.Id.StartsWith("partner", StringComparison.Ordinal)) staticBlockers.AddRange(item.Parts);
                staticBlockers.AddRange(BaseSceneRules.EntranceCollisionParts(item));
            }
            foreach (var tile in board.Positions())
            {
                double x = tile.X, z = tile.Z;
                Line(x, z, x + 1, z, GridColor);
                Line(x, z, x, z + 1, GridColor);
                if (tile.X == board.Width - 1) Line(x + 1, z, x + 1, z + 1, GridColor);
                if (tile.Z == board.Height - 1) Line(x, z + 1, x + 1, z + 1, GridColor);
            }
            Complete(grid);
            foreach (var tile in board.Positions())
            {
                double x = tile.X, z = tile.Z;
                if (!BaseSpatial.TerrainWalkable(board, x + .5, z + .5, map))
                {
                    Line(x + .18, z + .18, x + .82, z + .82, BlockColor);
                    Line(x + .82, z + .18, x + .18, z + .82, BlockColor);
                    continue;
                }
                if (!BaseSpatial.TerrainWalkable(board, x + 1, z + .5, map)) Line(x + 1, z, x + 1, z + 1, BlockColor);
                if (!BaseSpatial.TerrainWalkable(board, x + .5, z + 1, map)) Line(x, z + 1, x + 1, z + 1, BlockColor);
            }
            Rectangle(new BaseObjectRect(board.Width * .5, board.Height * .5, board.Width, board.Height), -BaseMap.Radius, BlockColor);
            Complete(terrain);
        }

        public void RefreshActors(BaseState state, BasePoint player, BasePoint facing, BaseMapObject target,
            IEnumerable<BaseMapObject> characters, IEnumerable<BaseMapObject> actorObstacles)
        {
            foreach (var part in staticBlockers) Rectangle(part, BaseMap.Radius, BlockColor);
            foreach (var obstacle in actorObstacles)
                foreach (var part in obstacle.Parts) Rectangle(part, BaseMap.Radius, BlockColor);
            Rectangle(new BaseObjectRect(player.X, player.Y, BaseMap.Radius * 2, BaseMap.Radius * 2), 0, ReadyColor);
            Complete(footprints);

            foreach (var actor in characters)
            {
                if (!actor.VisualOnly && actor.Distance(player.X, player.Y) <= 5)
                    DrawReach(state, actor);
            }
            if (target != null && target.Kind != "npc" && !state.Leaders.Any(actor => actor.Id == target.Id) && target.Id != "stand-from-bench")
                DrawReach(state, target);
            double direction = Math.Atan2(facing.Y, facing.X);
            double angle = Math.Acos(BaseInteraction.FrontCosine);
            Line(player.X, player.Y, player.X + Math.Cos(direction) * 1.2, player.Y + Math.Sin(direction) * 1.2, ReachColor);
            double arcX = player.X, arcY = player.Y;
            for (int step = 0; step <= 16; step++)
            {
                double side = direction - angle + 2 * angle * step / 16;
                double x = player.X + Math.Cos(side) * BaseInteraction.Distance;
                double y = player.Y + Math.Sin(side) * BaseInteraction.Distance;
                Line(arcX, arcY, x, y, ReachColor);
                arcX = x;
                arcY = y;
            }
            Line(arcX, arcY, player.X, player.Y, ReachColor);
            Complete(reach);
        }

        private void DrawReach(BaseState state, BaseMapObject target)
        {
            double radius = BaseInteraction.DistanceFor(state, target);
            var ink = BaseInteraction.CanReach(state, target) ? ReadyColor : ReachColor;
            foreach (var part in target.Parts)
            {
                const int steps = 6;
                BasePoint previous = new BasePoint(part.X + part.Width * .5 + radius, part.Y - part.Height * .5);
                for (int corner = 0; corner < 4; corner++)
                {
                    double centerX = part.X + (corner == 0 || corner == 3 ? .5 : -.5) * part.Width;
                    double centerY = part.Y + (corner < 2 ? .5 : -.5) * part.Height;
                    for (int index = 0; index <= steps; index++)
                    {
                        double angle = Math.PI * .5 * (corner + (double)index / steps);
                        var point = new BasePoint(centerX + Math.Cos(angle) * radius, centerY + Math.Sin(angle) * radius);
                        Line(previous.X, previous.Y, point.X, point.Y, ink);
                        previous = point;
                    }
                }
            }
        }

        public void BeginRoutes() => ClearBuffers();
        public void AddRoute(BasePoint start, IReadOnlyList<BasePoint> points, int next)
        {
            if (points == null) return;
            for (int index = next; index < points.Count; index++)
            {
                var end = points[index];
                Line(start.X, start.Y, end.X, end.Y, RouteColor);
                start = end;
            }
        }
        public void EndRoutes() => Complete(routes);

        private void Rectangle(BaseObjectRect part, double padding, Color32 ink)
        {
            double left = part.X - part.Width * .5 - padding, right = part.X + part.Width * .5 + padding;
            double bottom = part.Y - part.Height * .5 - padding, top = part.Y + part.Height * .5 + padding;
            Line(left, bottom, right, bottom, ink);
            Line(right, bottom, right, top, ink);
            Line(right, top, left, top, ink);
            Line(left, top, left, bottom, ink);
        }

        private void Line(double fromX, double fromY, double toX, double toY, Color32 ink)
        {
            indices.Add(vertices.Count);
            vertices.Add(worldPoint(fromX, fromY) + Vector3.up * .08f);
            colors.Add(ink);
            indices.Add(vertices.Count);
            vertices.Add(worldPoint(toX, toY) + Vector3.up * .08f);
            colors.Add(ink);
        }

        private void ClearBuffers()
        {
            vertices.Clear();
            colors.Clear();
            indices.Clear();
        }

        private void Complete(Mesh mesh)
        {
            mesh.Clear(false);
            mesh.SetVertices(vertices);
            mesh.SetColors(colors);
            mesh.SetIndices(indices, MeshTopology.Lines, 0);
            ClearBuffers();
        }

        public void Dispose()
        {
            if (Root != null) Root.SetActive(false);
            foreach (var mesh in new[] { grid, terrain, footprints, reach, routes }) BattleBoardView.DestroyObject(mesh);
            BattleBoardView.DestroyObject(material);
            BattleBoardView.DestroyObject(Root);
        }
    }
}

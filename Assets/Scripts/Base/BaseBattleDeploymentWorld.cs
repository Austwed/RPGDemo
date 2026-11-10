using System.Collections.Generic;
using System.Linq;
using BaseConquest.Battle;
using BaseConquest.Presentation;
using UnityEngine;
using UnityEngine.InputSystem;

namespace BaseConquest.Base
{
    public sealed partial class BaseSceneController
    {
        private BaseBattlePreparation deploymentDraft;
        private BattleBoardView deploymentView;
        private GameObject deploymentObjects;
        private Mesh deploymentZoneMesh;
        private Material deploymentZoneMaterial;
        private readonly Dictionary<string, BattlePawn2D> deploymentUnits = new Dictionary<string, BattlePawn2D>();
        internal bool DeploymentWorldActive => deploymentView != null;

        internal void BeginDeploymentWorld(BaseBattlePreparation draft)
        {
            if (deploymentDraft == draft && deploymentView != null) return;
            EndDeploymentWorld();

            var preview = State.Copy();
            bool dungeonEntry = draft.Route == BasePrepRoute.Ruins && BaseSceneRules.Entrance(preview);
            if (dungeonEntry) BaseSceneRules.EnterDungeon(preview);
            deploymentDraft = draft;
            if (dungeonEntry)
            {
                // The ruins portal opens a view of its actual destination without publishing the transition.
                var appearance = BaseBattlefield.Board(preview);
                deploymentView = new BattleBoardView(draft.Board, runtimeRoot, mapMaterial, mapMaterial) { FlatView = true, ObliqueBaseView = true };
                deploymentView.SetSurfaceRegion("dungeon");
                var objects = BaseSpatial.Objects(preview).ToArray();
                deploymentView.SetAuthoredBuildings(objects.Where(obj => obj.Blocks && (!string.IsNullOrEmpty(obj.AssetId) || !string.IsNullOrEmpty(obj.SourceObjectId))).Select(obj => obj.Id));
                deploymentView.SetCombatAppearance(appearance);
                var landmarks = new GameObject("RegionLandmarks").transform;
                landmarks.SetParent(deploymentView.Root.transform, false);
                foreach (var obj in objects.Where(obj => !string.IsNullOrEmpty(obj.SourceObjectId) || !string.IsNullOrEmpty(obj.AssetId)))
                    AuthoredMapView.Create(landmarks, obj, DeploymentWorldPoint(obj.X, obj.Y), mapMaterial, false);
                EnvironmentRoadEdges.Build(preview, draft.Board, deploymentView.Root.transform, false);
                regionView.Root.SetActive(false);
                deploymentView.Camera.gameObject.AddComponent<AudioListener>();
            }
            else
            {
                deploymentView = regionView;
                explorationActors.gameObject.SetActive(false);
                deploymentView.SetCombatMode(true, true);
            }

            WorldCamera = deploymentView.Camera;
            deploymentObjects = new GameObject("DeploymentWorld");
            deploymentObjects.transform.SetParent(deploymentView.Root.transform, false);
            var zones = draft.Board.Positions().Where(cell => draft.Board.Cell(cell).Zone == DeploymentZone.A && draft.Board.Walkable(cell)).ToArray();
            if (BaseSceneRules.Active(preview))
            {
                var sight = new BattleVision(draft.Board);
                var known = BaseSceneRules.Memory(preview).Known;
                foreach (var cell in zones) known[cell.Z * draft.Board.Width + cell.X] = true;
                sight.RememberTerrain(BattleSide.A, known);
                sight.RefreshExploration(new[] { new GridPosition((int)preview.PlayerX, (int)preview.PlayerY) }, BaseSceneRules.FogEnabled(preview));
                deploymentView.RefreshVision(sight, BattleSide.A, false);
            }
            BuildDeploymentZones(zones);
        }

        private Vector3 DeploymentWorldPoint(double x, double z)
        {
            var board = deploymentDraft.Board;
            return new Vector3(((float)x - board.Width * .5f) * BattleBoardView.CellSize, 0,
                ((float)z - board.Height * .5f) * BattleBoardView.CellSize);
        }

        private void BuildDeploymentZones(IEnumerable<GridPosition> cells)
        {
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            float radius = BattleBoardView.CellSize * .485f;
            foreach (var cell in cells)
            {
                var center = BattleBoardView.World(cell, deploymentDraft.Board) + Vector3.up * .12f;
                int index = vertices.Count;
                vertices.Add(center + new Vector3(-radius, 0, -radius));
                vertices.Add(center + new Vector3(-radius, 0, radius));
                vertices.Add(center + new Vector3(radius, 0, radius));
                vertices.Add(center + new Vector3(radius, 0, -radius));
                triangles.AddRange(new[] { index, index + 1, index + 2, index, index + 2, index + 3 });
            }
            deploymentZoneMesh = new Mesh { name = "AllyDeploymentCells" };
            deploymentZoneMesh.SetVertices(vertices);
            deploymentZoneMesh.SetTriangles(triangles, 0);
            deploymentZoneMaterial = BattlePawn2D.Transparent(mapMaterial);
            deploymentZoneMaterial.SetColor("_BaseColor", new Color(1f, .82f, .47f, .48f));
            var obj = new GameObject("AllyDeploymentCells", typeof(MeshFilter), typeof(MeshRenderer));
            obj.transform.SetParent(deploymentObjects.transform, false);
            obj.GetComponent<MeshFilter>().sharedMesh = deploymentZoneMesh;
            var render = obj.GetComponent<MeshRenderer>();
            render.sharedMaterial = deploymentZoneMaterial;
            render.sortingOrder = -29900;
        }

        internal void SetDeploymentViewport(Rect rect)
        {
            if (deploymentView == null) return;
            var camera = deploymentView.Camera;
            var board = deploymentDraft.Board;
            camera.rect = rect;
            float aspect = Mathf.Max(.1f, camera.pixelWidth / (float)Mathf.Max(1, camera.pixelHeight));
            camera.aspect = aspect;
            camera.orthographicSize = Mathf.Max(board.Height * BattleBoardView.CellSize * .5f + .25f,
                (board.Width * BattleBoardView.CellSize * .5f + .25f) / aspect);
            camera.transform.SetPositionAndRotation(new Vector3(0, 30, 0), Quaternion.Euler(90, 0, 0));
        }

        internal bool PickDeploymentCell(Vector2 screen, out GridPosition cell)
        {
            cell = default;
            return deploymentView != null && deploymentView.Pick(screen, out cell);
        }

        internal void RefreshDeploymentUnits()
        {
            if (deploymentDraft == null) return;
            foreach (var pair in deploymentUnits)
                pair.Value.gameObject.SetActive(deploymentDraft.Positions.ContainsKey(pair.Key));
            foreach (var unit in deploymentDraft.Units)
            {
                if (!deploymentDraft.Positions.TryGetValue(unit.Id, out var cell)) continue;
                if (!deploymentUnits.TryGetValue(unit.Id, out var pawn))
                {
                    var obj = new GameObject("DeploymentUnit_" + unit.Id);
                    obj.transform.SetParent(deploymentObjects.transform, false);
                    pawn = obj.AddComponent<BattlePawn2D>();
                    bool main = unit.Id == deploymentDraft.PlayerId;
                    string appearance = main ? BaseDemoIntro.PlayerArt(State) : unit.Leader == null ? null : BaseCommanderIdentity.Appearance(unit.Leader);
                    pawn.Initialize(new BattleSoldier { Kind = unit.Kind, Side = BattleSide.A, AppearanceId = appearance, Stats = unit.Stats, Health = (float)unit.Health }, mapMaterial);
                    if (main) pawn.EnablePlayerHero(appearance);
                    else if (unit.Leader != null) pawn.EnableCompanion(appearance);
                    deploymentUnits.Add(unit.Id, pawn);
                }
                var anchor = BaseBattlePreparation.Anchor(cell);
                var feet = DeploymentWorldPoint(anchor.X - (unit.Footprint == 2 ? .25 : 0), anchor.Z);
                pawn.SyncExploration(feet, new Color(.47f, .77f, .64f));
            }
        }

        internal void EndDeploymentWorld(bool restore = true)
        {
            if (deploymentView == null) return;
            var view = deploymentView;
            deploymentView = null;
            deploymentDraft = null;
            if (deploymentObjects != null) deploymentObjects.SetActive(false);
            BattleBoardView.DestroyObject(deploymentObjects);
            BattleBoardView.DestroyObject(deploymentZoneMesh);
            BattleBoardView.DestroyObject(deploymentZoneMaterial);
            deploymentUnits.Clear();

            if (view != regionView)
            {
                view.Root.SetActive(false);
                view.Dispose();
                BattleBoardView.DestroyObject(view.Root);
            }
            if (!restore || regionView == null) return;
            WorldCamera = regionView.Camera;
            regionView.Root.SetActive(true);
            explorationActors.gameObject.SetActive(true);
            regionView.SetCombatMode(false, true);
            regionView.Camera.ResetAspect();
            regionView.UpdateCamera(0, 0);
            RefreshSceneSight(true);
        }

        private void UpdateDeploymentWorld()
        {
            Hud.TickDialogue(Time.unscaledDeltaTime);
            var keyboard = Keyboard.current;
            if (keyboard == null || developerPanel?.BlocksGameInput == true) return;
            if (Hud.DialogueOpen && (keyboard.eKey.wasPressedThisFrame || keyboard.spaceKey.wasPressedThisFrame)) Hud.ContinueDialogue();
            else if (keyboard.escapeKey.wasPressedThisFrame) Hud.ClosePanel();
        }
    }
}

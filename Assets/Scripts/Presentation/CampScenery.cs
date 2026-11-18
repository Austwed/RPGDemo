using System.Collections.Generic;
using System.Linq;
using BaseConquest.Battle;
using UnityEngine;

namespace BaseConquest.Presentation
{
    internal sealed class CampScenery : MonoBehaviour
    {
        private const float Cell = BattleBoardView.CellSize;
        private Mesh ground;
        private Material[] materials;

        public static GameObject Create(Transform parent, BattleBoard board, Camera camera, Material grass, Material source,
            float maxViewSize, bool camp, bool dungeon)
        {
            var root = new GameObject(camp ? "CampScenery" : "ExplorationScenery", typeof(MeshFilter), typeof(MeshRenderer));
            root.transform.SetParent(parent, false);
            root.AddComponent<CampScenery>().Build(board, camera, grass, source, maxViewSize, camp, dungeon);
            return root;
        }

        private static float GroundHeight(float x, float z, BattleBoard board)
        {
            float outside = Mathf.Max(0, Mathf.Max(x - board.Width, Mathf.Min(x, z - board.Height)));
            return Mathf.SmoothStep(0, 1, Mathf.Clamp01(outside / 5)) *
                Mathf.Lerp(.2f, 1.4f, Mathf.PerlinNoise(x * .22f, z * .22f)) - .01f;
        }

        private void Build(BattleBoard board, Camera camera, Material grass, Material source,
            float maxViewSize, bool camp, bool dungeon)
        {
            Material SceneryTint(Color tint)
            {
                var material = new Material(dungeon ? source : grass);
                if (dungeon) material.SetTexture("_BaseMap", PixelDemoArt.Load("Terrain", "dungeon-wall"));
                material.SetColor("_BaseColor", tint);
                return material;
            }

            materials = new[]
            {
                grass,
                SceneryTint(dungeon ? new Color(.35f, .34f, .31f) : new Color(.89f, .95f, .86f)),
                SceneryTint(dungeon ? new Color(.29f, .28f, .25f) : new Color(.81f, .9f, .77f))
            };
            var vertices = new List<Vector3>();
            var uv = new List<Vector2>();
            var triangles = new List<int>[materials.Length];
            for (int i = 0; i < triangles.Length; i++) triangles[i] = new List<int>();

            Vector3 Point(float x, float z) => new Vector3((x - board.Width * .5f) * Cell,
                GroundHeight(x, z, board), (z - board.Height * .5f) * Cell);
            void Quad(int x, int z, int terrain)
            {
                int first = vertices.Count;
                vertices.Add(Point(x, z));
                vertices.Add(Point(x, z + 1));
                vertices.Add(Point(x + 1, z + 1));
                vertices.Add(Point(x + 1, z));
                uv.Add(Vector2.zero);
                uv.Add(Vector2.up);
                uv.Add(Vector2.one);
                uv.Add(Vector2.right);
                var indices = triangles[terrain];
                indices.Add(first); indices.Add(first + 1); indices.Add(first + 2);
                indices.Add(first); indices.Add(first + 2); indices.Add(first + 3);
            }

            // Budget the ground projection from any playable edge, including a raised player focus.
            float highestGround = board.Cells.Max(cell => cell.Elevation) * BaseTerrainView.LevelHeight;
            var up = camera.transform.up;
            int paddingX = Mathf.CeilToInt(maxViewSize * camera.aspect / Cell) + 1;
            int paddingZ = Mathf.CeilToInt((maxViewSize + highestGround * up.y) / (up.z * Cell)) + 3;
            for (int z = -paddingZ; z < board.Height + paddingZ; z++)
                for (int x = -paddingX; x < board.Width + paddingX; x++)
                {
                    if (x >= 0 && x < board.Width && z >= 0 && z < board.Height) continue;
                    int distance = Mathf.Max(Mathf.Max(-x - 1, x - board.Width), Mathf.Max(-z - 1, z - board.Height));
                    float variation = distance * .11f + (Mathf.PerlinNoise(x * .22f, z * .22f) - .5f) * 1.15f;
                    int terrain = variation > .8f ? 2 : dungeon || variation > .3f ? 1 : 0;
                    Quad(x, z, terrain);
                }

            ground = new Mesh { name = "Exploration scenery ground" };
            ground.SetVertices(vertices);
            ground.SetUVs(0, uv);
            ground.subMeshCount = materials.Length;
            for (int i = 0; i < triangles.Length; i++) ground.SetTriangles(triangles[i], i);
            ground.RecalculateBounds();
            GetComponent<MeshFilter>().sharedMesh = ground;
            var renderer = GetComponent<MeshRenderer>();
            renderer.sharedMaterials = materials;
            renderer.sortingOrder = -32000;
            if (dungeon) return;

            var random = new System.Random(711);
            var trees = new[] { "tree-oak-a", "tree-oak-b", "tree-pine" };
            void Prop(string id, float x, float z, float width)
            {
                var texture = Resources.Load<Texture2D>("Environment/" + id);
                var facade = SpriteFacade.Create(transform, texture, Point(x, z), Cell * width, source, camera);
                facade.Surface.sharedMaterial.SetColor("_BaseColor", new Color(.88f, .92f, .84f));
            }
            void Tree(float x, float z) => Prop(trees[random.Next(trees.Length)], x, z,
                Mathf.Lerp(1.65f, 2.65f, (float)random.NextDouble()));
            if (!camp)
            {
                Tree(-2.1f, board.Height * .22f);
                Tree(-2.5f, board.Height * .77f);
                Tree(board.Width + 2.3f, board.Height * .7f);
                Tree(board.Width * .18f, board.Height + 1.8f);
                Tree(board.Width * .7f, -1.9f);
                return;
            }
            for (int x = 1; x < board.Width + 9; x += 2)
            {
                Tree(x + (float)random.NextDouble(), board.Height + 1f + (float)random.NextDouble() * 2.6f);
                if (Mathf.Abs(x - 12) > 3) Tree(x, -2.3f - (float)random.NextDouble() * 3);
            }
            for (int z = -1; z < board.Height + 3; z += 2)
                if (Mathf.Abs(z - 6.5f) > 2) Tree(board.Width + 1.7f + (float)random.NextDouble() * 3, z);
            // Open ground continues beyond the west exit, between the trees.
            Tree(-2.1f, 11.4f);
            Tree(-4.6f, 9.3f);
            Tree(-2.8f, 3.4f);
            Tree(-5.8f, 5f);
            Tree(-4.5f, 14.2f);
            Prop("bush-round", -1.7f, 8.7f, .9f);
            Prop("rock-scatter", -1.9f, 4.4f, .65f);
            for (int z = -2; z < board.Height + 4; z += 3)
            {
                if (Mathf.Abs(z - 6.5f) > 1.4f) Prop("grass-tuft", -1.45f - (float)random.NextDouble(), z, .65f);
            }
            Prop("rock-large", board.Width + 1.2f, board.Height - 3.3f, 1.35f);
            Prop("bush-round", board.Width + 1.45f, 2.2f, 1.15f);
        }

        private void OnDestroy()
        {
            BattleBoardView.DestroyObject(ground);
            for (int i = 1; i < materials.Length; i++) BattleBoardView.DestroyObject(materials[i]);
        }
    }
}

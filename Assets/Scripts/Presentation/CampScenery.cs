using System.Collections.Generic;
using BaseConquest.Battle;
using UnityEngine;

namespace BaseConquest.Presentation
{
    internal sealed class CampScenery : MonoBehaviour
    {
        private const float Cell = BattleBoardView.CellSize;
        private const int Padding = 14;
        private Mesh ground;
        private Material[] materials;

        public static void Create(Transform parent, BattleBoard board, Camera camera, Material grass, Material source)
        {
            var root = new GameObject("CampScenery", typeof(MeshFilter), typeof(MeshRenderer));
            root.transform.SetParent(parent, false);
            root.AddComponent<CampScenery>().Build(board, camera, grass, source);
        }

        internal static Vector3 ClampPan(Vector3 center, BattleBoard board, float size, float aspect, bool oblique, float zoom)
        {
            float horizontal = size * aspect;
            float vertical = size / (oblique ? .8660254f : 1f);
            float inset = Mathf.Lerp(.35f, 1f, Mathf.InverseLerp(.65f, 1f, zoom));
            float Limit(float halfMap, float view) => Mathf.Max(0,
                Mathf.Min(halfMap - view * inset, halfMap + (Padding - 1) * Cell - view));
            float x = Limit(board.Width * Cell / 2, horizontal);
            float z = Limit(board.Height * Cell / 2, vertical);
            return new Vector3(Mathf.Clamp(center.x, -x, x), 0, Mathf.Clamp(center.z, -z, z));
        }

        private static float RiverBank(float z) => -1.9f - Mathf.Sin(z * .27f) * .65f - Mathf.Sin(z * .095f) * .42f;
        private static float ShallowLimit(float z) => RiverBank(z) - 1.05f - Mathf.Sin(z * .47f) * .28f;

        private static float GroundHeight(float x, float z, BattleBoard board)
        {
            float outside = Mathf.Max(0, Mathf.Max(x - board.Width, Mathf.Min(x, z - board.Height)));
            return Mathf.SmoothStep(0, 1, Mathf.Clamp01(outside / 5)) *
                Mathf.Lerp(.2f, 1.4f, Mathf.PerlinNoise(x * .22f, z * .22f)) - .025f;
        }

        private void Build(BattleBoard board, Camera camera, Material grass, Material source)
        {
            Material Terrain(string texture, Color tint)
            {
                var material = new Material(source);
                material.SetTexture("_BaseMap", PixelDemoArt.Load("Terrain", texture));
                material.SetColor("_BaseColor", tint);
                return material;
            }

            materials = new[]
            {
                grass,
                Terrain("grass", new Color(.89f, .95f, .86f)),
                Terrain("sand", new Color(.77f, .76f, .55f)),
                Terrain("shallow-water", new Color(.68f, .83f, .79f)),
                Terrain("deep-water", new Color(.65f, .79f, .76f)),
                Terrain("grass", new Color(.81f, .9f, .77f))
            };
            var vertices = new List<Vector3>();
            var uv = new List<Vector2>();
            var triangles = new List<int>[materials.Length];
            for (int i = 0; i < triangles.Length; i++) triangles[i] = new List<int>();

            Vector3 Point(float x, float z) => new Vector3((x - board.Width * .5f) * Cell,
                GroundHeight(x, z, board), (z - board.Height * .5f) * Cell);
            void Quad(float x0, float x1, float x2, float x3, int z, int terrain)
            {
                int first = vertices.Count;
                vertices.Add(Point(x0, z));
                vertices.Add(Point(x1, z + 1));
                vertices.Add(Point(x2, z + 1));
                vertices.Add(Point(x3, z));
                uv.Add(Vector2.zero);
                uv.Add(Vector2.up);
                uv.Add(Vector2.one);
                uv.Add(Vector2.right);
                var indices = triangles[terrain];
                indices.Add(first); indices.Add(first + 1); indices.Add(first + 2);
                indices.Add(first); indices.Add(first + 2); indices.Add(first + 3);
            }

            for (int z = -Padding; z < board.Height + Padding; z++)
            {
                float near = RiverBank(z), next = RiverBank(z + 1);
                float grassEdge = near * .55f + Mathf.Sin(z * 1.23f) * .17f;
                float nextGrassEdge = next * .55f + Mathf.Sin((z + 1) * 1.23f) * .17f;
                float shallow = ShallowLimit(z), nextShallow = ShallowLimit(z + 1);
                Quad(near, next, nextGrassEdge, grassEdge, z, 2);
                Quad(grassEdge, nextGrassEdge, 0, 0, z, 0);
                Quad(shallow, nextShallow, next, near, z, 3);
                for (int depth = 0; depth < 6; depth++)
                    Quad(shallow - 1 - depth, nextShallow - 1 - depth,
                        nextShallow - depth, shallow - depth, z, 4);
                Quad(shallow - 7, nextShallow - 7, nextShallow - 6, shallow - 6, z, 3);
                for (int x = 0; x < Padding; x++)
                    Quad(shallow - 8 - x, nextShallow - 8 - x, nextShallow - 7 - x, shallow - 7 - x, z, x == 0 ? 2 : 1);

                for (int x = 0; x < board.Width + Padding; x++)
                {
                    if (x < board.Width && z >= 0 && z < board.Height) continue;
                    float distance = Mathf.Max(x - board.Width, z - board.Height);
                    float slope = Mathf.PerlinNoise(x * .22f, z * .22f);
                    int terrain = distance > 1 && slope > .53f ? 5 : distance > 0 || z < -2 ? 1 : 0;
                    Quad(x, x, x + 1, x + 1, z, terrain);
                }
            }

            ground = new Mesh { name = "Camp banks and slopes" };
            ground.SetVertices(vertices);
            ground.SetUVs(0, uv);
            ground.subMeshCount = materials.Length;
            for (int i = 0; i < triangles.Length; i++) ground.SetTriangles(triangles[i], i);
            ground.RecalculateBounds();
            GetComponent<MeshFilter>().sharedMesh = ground;
            var renderer = GetComponent<MeshRenderer>();
            renderer.sharedMaterials = materials;
            renderer.sortingOrder = -32000;

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
            for (int z = -8; z < board.Height + 9; z += 2)
                Tree(RiverBank(z) - 9.4f - (float)random.NextDouble() * 2, z + (float)random.NextDouble());
            for (int x = 1; x < board.Width + 9; x += 2)
            {
                Tree(x + (float)random.NextDouble(), board.Height + 1f + (float)random.NextDouble() * 2.6f);
                if (Mathf.Abs(x - 12) > 3) Tree(x, -2.3f - (float)random.NextDouble() * 3);
            }
            for (int z = -1; z < board.Height + 3; z += 2)
                if (Mathf.Abs(z - 6.5f) > 2) Tree(board.Width + 1.7f + (float)random.NextDouble() * 3, z);
            for (int z = -2; z < board.Height + 2; z += 2)
            {
                float y = z + .35f;
                Prop("rock-scatter", RiverBank(y) + .24f, y, .7f);
                if (Mathf.Abs(y - 6.5f) > 1.4f) Prop("grass-tuft", RiverBank(y) * .35f, y + .65f, .65f);
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

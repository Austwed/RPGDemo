using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace BaseConquest.Presentation
{
    [DefaultExecutionOrder(100)]
    internal sealed class SceneryOcclusion : MonoBehaviour
    {
        private const int MaxMasks = 32;
        private static readonly int MaskCount = Shader.PropertyToID("_ActorFadeCount");
        private static readonly int MaskBounds = Shader.PropertyToID("_ActorFadeMasks");
        private static readonly Dictionary<Renderer, (Mesh Mesh, Texture2D Atlas, Rect Frame)> Actors = new();
        private static readonly List<(Renderer Surface, Projection Image)> ProjectedActors = new();
        private static readonly ConditionalWeakTable<Texture2D, System.Collections.BitArray> OpaquePixels = new();
        private static Camera cachedCamera;
        private static int cachedFrame = -1;

        private readonly Vector4[] masks = new Vector4[MaxMasks];
        private MaterialPropertyBlock properties;
        private Camera worldCamera;
        private Renderer surface;
        private Mesh mesh;
        private Texture2D texture;
        private Rect frame;
        private int previousCount;

        public static void TrackFrame(Renderer actor, Mesh mesh, Texture2D atlas, Rect frame) => Actors[actor] = (mesh, atlas, frame);

        public static void Untrack(Renderer actor)
        {
            Actors.Remove(actor);
            cachedFrame = -1;
        }

        public static void Attach(Renderer surface, Camera camera)
        {
            if (camera == null || surface == null) return;
            var shader = Resources.Load<Shader>("Presentation/SunSprite");
            if (shader == null) return;
            surface.sharedMaterial.shader = shader;
            var fade = surface.gameObject.AddComponent<SceneryOcclusion>();
            fade.surface = surface;
            fade.worldCamera = camera;
            fade.mesh = surface.GetComponent<MeshFilter>().sharedMesh;
            fade.texture = surface.sharedMaterial.GetTexture("_BaseMap") as Texture2D;
            var offset = surface.sharedMaterial.GetTextureOffset("_BaseMap");
            var scale = surface.sharedMaterial.GetTextureScale("_BaseMap");
            fade.frame = new Rect(offset, scale);
        }

        private readonly struct Projection
        {
            public readonly Rect Bounds;
            public readonly Texture2D Texture;
            private readonly Rect frame;
            private readonly Vector2 origin, right, up;
            private readonly float determinant;
            private readonly int width, height;

            public Projection(Camera camera, Transform transform, Bounds meshBounds, Texture2D texture, Rect frame, bool xz)
            {
                Texture = texture;
                width = texture.width;
                height = texture.height;
                this.frame = frame;
                Vector3 min = meshBounds.min, size = meshBounds.size;
                origin = camera.WorldToViewportPoint(transform.TransformPoint(min));
                right = (Vector2)camera.WorldToViewportPoint(transform.TransformPoint(min + Vector3.right * size.x)) - origin;
                Vector3 vertical = xz ? Vector3.forward * size.z : Vector3.up * size.y;
                up = (Vector2)camera.WorldToViewportPoint(transform.TransformPoint(min + vertical)) - origin;
                determinant = right.x * up.y - right.y * up.x;
                Vector2 low = origin + Vector2.Min(Vector2.zero, right) + Vector2.Min(Vector2.zero, up);
                Vector2 high = origin + Vector2.Max(Vector2.zero, right) + Vector2.Max(Vector2.zero, up);
                Bounds = Rect.MinMaxRect(low.x, low.y, high.x, high.y);
            }

            public bool OpaqueAt(Vector2 point, System.Collections.BitArray pixels)
            {
                if (Mathf.Abs(determinant) < 1e-10f) return false;
                Vector2 delta = point - origin;
                float u = (delta.x * up.y - delta.y * up.x) / determinant;
                float v = (right.x * delta.y - right.y * delta.x) / determinant;
                if (u < 0 || u >= 1 || v < 0 || v >= 1) return false;
                int x = Mathf.Clamp((int)((frame.x + frame.width * u) * width), 0, width - 1);
                int y = Mathf.Clamp((int)((frame.y + frame.height * v) * height), 0, height - 1);
                return pixels[y * width + x];
            }
        }

        private static System.Collections.BitArray Pixels(Texture2D source)
        {
            if (OpaquePixels.TryGetValue(source, out var cached)) return cached;
            Color32[] colors;
            if (source.isReadable) colors = source.GetPixels32();
            else
            {
                // Read only the first time this texture can overlap an actor. Imported atlases stay non-readable.
                var target = RenderTexture.GetTemporary(source.width, source.height, 0, RenderTextureFormat.ARGB32);
                var previous = RenderTexture.active;
                var copy = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false);
                try
                {
                    Graphics.Blit(source, target);
                    RenderTexture.active = target;
                    copy.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0, false);
                    colors = copy.GetPixels32();
                }
                finally
                {
                    RenderTexture.active = previous;
                    RenderTexture.ReleaseTemporary(target);
                    Destroy(copy);
                }
            }
            var pixels = new System.Collections.BitArray(colors.Length);
            for (int i = 0; i < colors.Length; i++) pixels[i] = colors[i].a >= 64;
            OpaquePixels.Add(source, pixels);
            return pixels;
        }

        private static bool SilhouettesOverlap(Camera camera, Projection actor, Projection decor)
        {
            var a = actor.Bounds;
            var b = decor.Bounds;
            if (!a.Overlaps(b)) return false;
            var overlap = Rect.MinMaxRect(Mathf.Max(0, Mathf.Max(a.xMin, b.xMin)), Mathf.Max(0, Mathf.Max(a.yMin, b.yMin)),
                Mathf.Min(1, Mathf.Min(a.xMax, b.xMax)), Mathf.Min(1, Mathf.Min(a.yMax, b.yMax)));
            if (overlap.width <= 0 || overlap.height <= 0) return false;
            var actorPixels = Pixels(actor.Texture);
            var decorPixels = Pixels(decor.Texture);
            int columns = Mathf.Clamp(Mathf.CeilToInt(overlap.width * camera.pixelWidth * .5f), 1, 64);
            int rows = Mathf.Clamp(Mathf.CeilToInt(overlap.height * camera.pixelHeight * .5f), 1, 96);
            for (int y = 0; y < rows; y++)
                for (int x = 0; x < columns; x++)
                {
                    var point = new Vector2(overlap.x + overlap.width * (x + .5f) / columns,
                        overlap.y + overlap.height * (y + .5f) / rows);
                    if (actor.OpaqueAt(point, actorPixels) && decor.OpaqueAt(point, decorPixels)) return true;
                }
            return false;
        }

        private static void UpdateProjectedActors(Camera camera)
        {
            if (cachedCamera == camera && cachedFrame == Time.frameCount) return;
            cachedCamera = camera;
            cachedFrame = Time.frameCount;
            ProjectedActors.Clear();
            foreach (var entry in Actors)
            {
                var actor = entry.Key;
                var image = entry.Value;
                if (actor == null || !actor.enabled || actor.forceRenderingOff ||
                    !actor.gameObject.activeInHierarchy || actor.gameObject.scene != camera.gameObject.scene ||
                    (camera.cullingMask & (1 << actor.gameObject.layer)) == 0 || image.Atlas == null)
                    continue;
                var projected = new Projection(camera, actor.transform, image.Mesh.bounds, image.Atlas, image.Frame, true);
                if (projected.Bounds.Overlaps(new Rect(0, 0, 1, 1))) ProjectedActors.Add((actor, projected));
            }
        }

        private void LateUpdate()
        {
            if (worldCamera == null || !worldCamera.enabled || !surface.enabled || texture == null) return;
            UpdateProjectedActors(worldCamera);
            if (ProjectedActors.Count == 0 && previousCount == 0) return;
            var decor = new Projection(worldCamera, surface.transform, mesh.bounds, texture, frame, false);
            int count = 0;
            foreach (var actor in ProjectedActors)
            {
                if (actor.Surface.sortingOrder >= surface.sortingOrder || !SilhouettesOverlap(worldCamera, actor.Image, decor))
                    continue;
                var bounds = actor.Image.Bounds;
                masks[count++] = new Vector4(bounds.center.x, bounds.center.y, bounds.width * .62f, bounds.height * .62f);
                if (count == masks.Length) break;
            }
            if (count == 0 && previousCount == 0) return;
            properties ??= new MaterialPropertyBlock();
            surface.GetPropertyBlock(properties);
            properties.SetInt(MaskCount, count);
            if (count > 0)
                properties.SetVectorArray(MaskBounds, masks);
            surface.SetPropertyBlock(properties);
            previousCount = count;
        }
    }
}

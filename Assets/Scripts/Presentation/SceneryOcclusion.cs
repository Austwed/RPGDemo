using System.Collections.Generic;
using UnityEngine;

namespace BaseConquest.Presentation
{
    [DefaultExecutionOrder(100)]
    internal sealed class SceneryOcclusion : MonoBehaviour
    {
        private const int MaxMasks = 32;
        private static readonly int MaskCount = Shader.PropertyToID("_ActorFadeCount");
        private static readonly int MaskBounds = Shader.PropertyToID("_ActorFadeMasks");
        private static readonly List<Renderer> Actors = new List<Renderer>();
        private static readonly List<(Renderer Surface, Rect Bounds)> ProjectedActors =
            new List<(Renderer, Rect)>();
        private static Camera cachedCamera;
        private static int cachedFrame = -1;

        private readonly Vector4[] masks = new Vector4[MaxMasks];
        private MaterialPropertyBlock properties;
        private Camera worldCamera;
        private Renderer surface;
        private int previousCount;

        public static void Track(Renderer actor)
        {
            Actors.Add(actor);
            cachedFrame = -1;
        }

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
        }

        private static Vector3 Absolute(Vector3 direction) =>
            new Vector3(Mathf.Abs(direction.x), Mathf.Abs(direction.y), Mathf.Abs(direction.z));

        private static Rect ViewportBounds(Camera camera, Bounds bounds, float padding = 0)
        {
            var center = camera.WorldToViewportPoint(bounds.center);
            float width = 2 * camera.orthographicSize * camera.aspect;
            float height = 2 * camera.orthographicSize;
            float x = (Vector3.Dot(Absolute(camera.transform.right), bounds.extents) + padding) / width;
            float y = (Vector3.Dot(Absolute(camera.transform.up), bounds.extents) + padding) / height;
            return Rect.MinMaxRect(center.x - x, center.y - y, center.x + x, center.y + y);
        }

        private static void UpdateProjectedActors(Camera camera)
        {
            if (cachedCamera == camera && cachedFrame == Time.frameCount) return;
            cachedCamera = camera;
            cachedFrame = Time.frameCount;
            ProjectedActors.Clear();
            foreach (var actor in Actors)
            {
                if (actor == null || !actor.enabled || actor.forceRenderingOff ||
                    !actor.gameObject.activeInHierarchy || actor.gameObject.scene != camera.gameObject.scene ||
                    (camera.cullingMask & (1 << actor.gameObject.layer)) == 0)
                    continue;
                var bounds = ViewportBounds(camera, actor.bounds, .22f);
                if (bounds.Overlaps(new Rect(0, 0, 1, 1)))
                    ProjectedActors.Add((actor, bounds));
            }
        }

        private void LateUpdate()
        {
            if (worldCamera == null || !worldCamera.enabled || !surface.enabled) return;
            UpdateProjectedActors(worldCamera);
            var decorBounds = ViewportBounds(worldCamera, surface.bounds);
            int count = 0;
            foreach (var actor in ProjectedActors)
            {
                if (actor.Surface.sortingOrder >= surface.sortingOrder || !actor.Bounds.Overlaps(decorBounds))
                    continue;
                var bounds = actor.Bounds;
                masks[count++] = new Vector4(bounds.center.x, bounds.center.y, bounds.width * .66f, bounds.height * .66f);
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

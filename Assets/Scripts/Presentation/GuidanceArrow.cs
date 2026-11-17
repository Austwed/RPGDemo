using UnityEngine;
using UnityEngine.UI;

namespace BaseConquest.Presentation
{
    internal sealed class GuidanceArrow : MaskableGraphic
    {
        private RectTransform frame, target;
        private Vector2 lower, upper;
        private float topInset;
        private bool map;

        public static GuidanceArrow Create(RectTransform parent, string name, float size)
        {
            var rect = UiPrefabBinding.Rect(name, parent);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
            rect.sizeDelta = new Vector2(size, size);
            var arrow = rect.gameObject.AddComponent<GuidanceArrow>();
            arrow.frame = parent;
            arrow.raycastTarget = false;
            arrow.Hide();
            return arrow;
        }

        public void Hide() => gameObject.SetActive(false);

        public void PointTo(RectTransform button)
        {
            if (!button.gameObject.activeInHierarchy) { Hide(); return; }
            target = button;
            map = false;
            Show();
        }

        public void PointToMap(Vector2 bottom, Vector2 top, float header = 0)
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(frame, bottom, null, out lower);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(frame, top, null, out upper);
            target = null;
            topInset = header;
            map = true;
            Show();
        }

        private void Show()
        {
            if (transform.GetSiblingIndex() != frame.childCount - 1) transform.SetAsLastSibling();
            gameObject.SetActive(true);
            LateUpdate();
        }

        private void LateUpdate()
        {
            if (!map)
            {
                if (target == null || !target.gameObject.activeInHierarchy) { Hide(); return; }
                var bounds = target.rect;
                lower = frame.InverseTransformPoint(target.TransformPoint(new Vector3(bounds.center.x, bounds.yMin)));
                upper = frame.InverseTransformPoint(target.TransformPoint(new Vector3(bounds.center.x, bounds.yMax)));
            }
            var area = frame.rect;
            var center = (lower + upper) * .5f;
            bool up = center.y > area.center.y;
            float radius = rectTransform.rect.height * .5f;
            float bob = 4 * Mathf.Sin(Time.unscaledTime * 5);
            Vector2 pos = up ? lower - Vector2.up * (radius + 6 + bob) : upper + Vector2.up * (radius + 6 + bob);
            float angle = up ? 180 : 0;
            float yLimit = area.yMax - (map ? topInset : 0);
            pos.y = Mathf.Clamp(pos.y, area.yMin + radius, yLimit - radius);
            if (map)
            {
                pos.x = Mathf.Clamp(pos.x, area.xMin + radius, area.xMax - radius);
                if (center.x < area.xMin || center.x > area.xMax || center.y > yLimit || center.y < area.yMin)
                {
                    var direction = center - pos;
                    angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg + 90;
                }
            }
            rectTransform.localPosition = pos;
            rectTransform.localRotation = Quaternion.Euler(0, 0, angle);
        }

        private static readonly Vector2[] Shape =
        {
            new Vector2(-.19f,.48f), new Vector2(.19f,.48f), new Vector2(.19f,.06f),
            new Vector2(.46f,.06f), new Vector2(0,-.48f), new Vector2(-.46f,.06f), new Vector2(-.19f,.06f)
        };

        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            var size = rectTransform.rect.size;
            void Draw(float scale, Color32 fill)
            {
                int start = mesh.currentVertCount;
                foreach (var vertex in Shape) mesh.AddVert(Vector2.Scale(vertex, size) * scale, fill, Vector2.zero);
                mesh.AddTriangle(start, start + 1, start + 2);
                mesh.AddTriangle(start, start + 2, start + 6);
                mesh.AddTriangle(start + 5, start + 3, start + 4);
            }
            Draw(1, new Color32(61, 33, 15, 255));
            Draw(.76f, new Color32(255, 205, 77, 255));
        }
    }
}

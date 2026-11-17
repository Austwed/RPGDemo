using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace BaseConquest.Presentation
{
    internal static class InventoryItemIcons
    {
        private static readonly Dictionary<string, Sprite> sprites = new Dictionary<string, Sprite>();

        public static Sprite Load(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            if (!sprites.TryGetValue(key, out var sprite))
                sprites[key] = sprite = Resources.Load<Sprite>("PixelDemo/ItemIcons/" + key);
            return sprite;
        }

        public static void Draw(Transform parent, Sprite sprite, Vector2 anchor, Vector2 offset, float size)
        {
            if (sprite == null) return;
            var node = new GameObject("ItemIcon", typeof(RectTransform), typeof(Image));
            var rect = (RectTransform)node.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = anchor;
            rect.anchoredPosition = offset;
            rect.sizeDelta = new Vector2(size, size);
            var image = node.GetComponent<Image>();
            image.sprite = sprite;
            image.raycastTarget = false;
            image.preserveAspect = true;
        }

        public static void LeadLabel(Text label, Sprite sprite, float size)
        {
            if (sprite == null) return;
            var rect = label.rectTransform;
            rect.offsetMin += Vector2.right * (size + 5);
            label.alignment = TextAnchor.MiddleLeft;
            Draw(rect, sprite, new Vector2(0, .5f), Vector2.left * (size * .5f + 5), size);
        }
    }
}

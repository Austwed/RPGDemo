using System.Collections.Generic;
using BaseConquest.Base;
using UnityEngine;
using UnityEngine.UI;

namespace BaseConquest.Presentation
{
    internal static class InventoryItemIcons
    {
        private static readonly Dictionary<string, Sprite> sprites = new Dictionary<string, Sprite>();
        private static readonly Color32[] qualityFills =
        {
            new Color32(224, 156, 151, 255), // 破损
            new Color32(193, 153, 121, 255), // 较差
            new Color32(248, 247, 243, 255), // 一般
            new Color32(169, 202, 154, 255), // 良好
            new Color32(163, 190, 228, 255), // 极佳
            new Color32(235, 198, 107, 255)  // 传奇
        };
        private static readonly HashSet<string> advancedGoods = new HashSet<string>
        {
            BaseWine.Strong,
            BaseTraining.Book(Battle.SoldierKind.Melee, 3),
            BaseTraining.Book(Battle.SoldierKind.Ranged, 3),
            BaseTraining.Book(Battle.SoldierKind.Cavalry, 3)
        };

        internal readonly struct Icon
        {
            public readonly Sprite Sprite;
            public readonly Color32 Fill;

            public Icon(Sprite sprite, Color32 fill)
            {
                Sprite = sprite;
                Fill = fill;
            }
        }

        public static Icon Load(string key, int quality = 2, string category = null)
        {
            if (string.IsNullOrEmpty(key)) return default;
            if (!sprites.TryGetValue(key, out var sprite))
                sprites[key] = sprite = Resources.Load<Sprite>("PixelDemo/ItemIcons/" + key);
            return sprite == null ? default : new Icon(sprite, FillFor(key, quality, category));
        }

        internal static Color32 FillFor(string key, int quality, string category)
        {
            if (key == "captains_ring") return qualityFills[2];
            if (category == "quest" || key == BaseBackpack.RoyalSword)
                return new Color32(240, 171, 108, 255);
            if (advancedGoods.Contains(key)) return qualityFills[3];
            return quality >= 0 && quality < qualityFills.Length ? qualityFills[quality] : qualityFills[2];
        }

        internal static Color32 FrameFor(Color32 fill) =>
            new Color32((byte)(fill.r * .8f), (byte)(fill.g * .8f), (byte)(fill.b * .8f), fill.a);

        public static void Draw(Transform parent, Icon icon, Vector2 anchor, Vector2 offset, float size)
        {
            if (icon.Sprite == null) return;
            var frame = Layer(parent, "ItemIcon");
            var rect = frame.rectTransform;
            rect.anchorMin = rect.anchorMax = anchor;
            rect.anchoredPosition = offset;
            rect.sizeDelta = new Vector2(size, size);

            frame.color = FrameFor(icon.Fill);
            var fill = Inset(rect, "QualityFill", 1);
            fill.color = icon.Fill;
            var image = Inset(fill.rectTransform, "Art", 1);
            image.sprite = icon.Sprite;
            image.preserveAspect = true;
        }

        private static Image Layer(Transform parent, string name)
        {
            var node = new GameObject(name, typeof(RectTransform), typeof(Image));
            node.transform.SetParent(parent, false);
            var image = node.GetComponent<Image>();
            image.raycastTarget = false;
            return image;
        }

        private static Image Inset(RectTransform parent, string name, float padding)
        {
            var image = Layer(parent, name);
            var rect = image.rectTransform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.one * padding;
            rect.offsetMax = -rect.offsetMin;
            return image;
        }

        public static void LeadLabel(Text label, Icon icon, float size)
        {
            if (icon.Sprite == null) return;
            var rect = label.rectTransform;
            rect.offsetMin += Vector2.right * (size + 5);
            label.alignment = TextAnchor.MiddleLeft;
            Draw(rect, icon, new Vector2(0, .5f), Vector2.left * (size * .5f + 5), size);
        }
    }
}

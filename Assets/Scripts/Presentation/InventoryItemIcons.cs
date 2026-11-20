using System.Collections.Generic;
using BaseConquest.Base;
using UnityEngine;
using UnityEngine.UI;

namespace BaseConquest.Presentation
{
    internal static class InventoryItemIcons
    {
        private static readonly Dictionary<string, Sprite> sprites = new Dictionary<string, Sprite>();
        private static Sprite cellBorder;
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
            {
                if (key == BaseDemoFinale.BrokenSwordItem)
                {
                    var art = Resources.Load<Texture2D>("PixelDemo/Props/broken-sword-v1");
                    if (art != null) sprite = Sprite.Create(art, new Rect(0,0,art.width,art.height), Vector2.one*.5f);
                }
                else sprite = Resources.Load<Sprite>("PixelDemo/ItemIcons/" + key);
                sprites[key] = sprite;
            }
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

        public static UnityEngine.UI.Image DrawCell(UnityEngine.UI.Button cell, Icon icon)
        {
            var background = cell.GetComponent<UnityEngine.UI.Image>();
            var fill = icon.Fill;
            fill.a = 170;
            background.color = fill;

            // Unity's Outline paints full offset copies of the square behind the translucent fill.
            var outline = cell.GetComponent<UnityEngine.UI.Outline>();
            if (outline != null) outline.enabled = false;
            var border = Inset(background.rectTransform, "QualityFrame", 0);
            border.sprite = CellBorder();
            border.type = UnityEngine.UI.Image.Type.Sliced;
            border.fillCenter = false;
            border.color = FrameFor(icon.Fill);

            var art = Inset(background.rectTransform, "ItemIcon", 1);
            art.sprite = icon.Sprite;
            art.preserveAspect = true;
            return border;
        }

        private static Sprite CellBorder()
        {
            if (cellBorder != null) return cellBorder;
            var texture = new Texture2D(3, 3, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave
            };
            var pixels = new Color32[9];
            System.Array.Fill(pixels, new Color32(255, 255, 255, 255));
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            cellBorder = Sprite.Create(texture, new Rect(0, 0, 3, 3), Vector2.one * .5f, 100, 0, SpriteMeshType.FullRect, Vector4.one);
            cellBorder.hideFlags = HideFlags.HideAndDontSave;
            return cellBorder;
        }

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

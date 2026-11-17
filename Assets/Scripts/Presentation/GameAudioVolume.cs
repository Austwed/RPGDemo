using UnityEngine;
using UnityEngine.UI;

namespace BaseConquest.Presentation
{
    public sealed class GameAudioVolume : MonoBehaviour
    {
        private const string MusicKey = "BaseConquest.Audio.Music";
        private const string EffectsKey = "BaseConquest.Audio.Effects";
        private static float? music, effects;
        private bool changed;

        public static float Music => music ??= Mathf.Clamp01(PlayerPrefs.GetFloat(MusicKey, 1));
        public static float Effects => effects ??= Mathf.Clamp01(PlayerPrefs.GetFloat(EffectsKey, 1));

        internal static void AddControls(RectTransform parent, Font font, Vector2 anchor, Vector2 position, float width)
        {
            var layout = Rect("AudioVolumes", parent, position, new Vector2(width, 116));
            layout.anchorMin = layout.anchorMax = anchor;
            layout.anchoredPosition = position;
            var controls = layout.gameObject.AddComponent<GameAudioVolume>();
            controls.AddSlider(layout, font, "MusicVolume", "音乐", 29, width, Music, value =>
            {
                music = value;
                PlayerPrefs.SetFloat(MusicKey, value);
            });
            controls.AddSlider(layout, font, "EffectsVolume", "音效", -29, width, Effects, value =>
            {
                effects = value;
                PlayerPrefs.SetFloat(EffectsKey, value);
            });
        }

        private static RectTransform Rect(string name, Transform parent, Vector2 position, Vector2 size)
        {
            var node = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform)node.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
            rect.sizeDelta = size;
            rect.anchoredPosition = position;
            return rect;
        }

        private void AddSlider(RectTransform parent, Font font, string id, string title, float y, float width,
            float current, System.Action<float> set)
        {
            var theme = DemoUiTheme.Current;
            float trackWidth = width - 204;
            Text Label(string name, float x, string value)
            {
                var label = Rect(name, parent, new Vector2(x, y), new Vector2(100, 42)).gameObject.AddComponent<Text>();
                label.font = font;
                label.fontSize = 22;
                label.alignment = TextAnchor.MiddleCenter;
                label.color = theme.Ink;
                label.raycastTarget = false;
                label.text = value;
                return label;
            }

            Label(id + "Label", -width / 2 + 49, title);
            var percent = Label(id + "Value", width / 2 - 46, current.ToString("0%"));
            var slider = Rect(id, parent, new Vector2(0, y), new Vector2(trackWidth, 46));
            slider.gameObject.AddComponent<Image>().color = Color.clear;
            var track = Rect("Track", slider, Vector2.zero, new Vector2(trackWidth - 24, 12));
            var rail = track.gameObject.AddComponent<Image>();
            rail.color = theme.Muted;
            rail.raycastTarget = false;
            var fill = Rect("Fill", track, Vector2.zero, Vector2.zero);
            var filled = fill.gameObject.AddComponent<Image>();
            filled.color = theme.Gold;
            filled.raycastTarget = false;
            var handles = Rect("HandleArea", slider, Vector2.zero, new Vector2(trackWidth - 24, 46));
            var handle = Rect("Handle", handles, Vector2.zero, new Vector2(26, 32));
            var grip = handle.gameObject.AddComponent<Image>();
            grip.color = theme.Button;
            var rim = handle.gameObject.AddComponent<Outline>();
            rim.effectColor = theme.Line;
            rim.effectDistance = new Vector2(1.5f, -1.5f);
            var control = slider.gameObject.AddComponent<Slider>();
            control.fillRect = fill;
            control.handleRect = handle;
            control.targetGraphic = grip;
            control.maxValue = 100;
            control.wholeNumbers = true;
            control.SetValueWithoutNotify(current * 100);
            control.onValueChanged.AddListener(percentValue =>
            {
                float value = percentValue / 100;
                set(value);
                percent.text = value.ToString("0%");
                changed = true;
            });
        }

        private void OnDisable()
        {
            if (!changed) return;
            PlayerPrefs.Save();
            changed = false;
        }
    }
}

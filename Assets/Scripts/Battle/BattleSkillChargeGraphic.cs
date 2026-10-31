using UnityEngine;
using UnityEngine.UI;
namespace BaseConquest.Battle
{
    // 标准Image保证充能底色参与Canvas渲染，不依赖自定义网格。
    public sealed class BattleSkillChargeGraphic : Image
    {
        private float charge;
        private bool ready;
        private Image fill, boundary;
        private Outline frame;
        public float Charge => charge;
        public bool Ready => ready;
        public void Show(float value, bool available)
        {
            charge = Mathf.Clamp01(value); ready = available;
            EnsureVisuals();
            color = new Color(0.055f, 0.075f, 0.10f, 1);
            fill.color = ready ? new Color(0.94f, 0.96f, 0.98f, 1) : new Color(0.60f, 0.66f, 0.72f, 1);
            fill.rectTransform.anchorMax = new Vector2(1, charge);
            fill.gameObject.SetActive(charge > 0);
            boundary.rectTransform.anchorMin = new Vector2(0, charge);
            boundary.rectTransform.anchorMax = new Vector2(1, charge);
            boundary.gameObject.SetActive(charge > 0 && charge < 1);
            frame.effectColor = ready ? new Color(1, 0.86f, 0.43f, 1) : new Color(0.30f, 0.37f, 0.44f, 1);
            frame.effectDistance = Vector2.one * (ready ? 2 : 1);
        }
        private void EnsureVisuals()
        {
            if (fill != null) return;
            raycastTarget = false;
            fill = Layer("ChargeFill");
            fill.rectTransform.anchorMin = Vector2.zero;
            fill.rectTransform.anchorMax = Vector2.one;
            fill.rectTransform.offsetMin = fill.rectTransform.offsetMax = Vector2.zero;
            boundary = Layer("ChargeBoundary");
            boundary.color = Color.white;
            boundary.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            boundary.rectTransform.sizeDelta = new Vector2(0, 2);
            boundary.rectTransform.anchoredPosition = Vector2.zero;
            frame = gameObject.AddComponent<Outline>();
            frame.useGraphicAlpha = false;
        }
        private Image Layer(string name)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(transform, false);
            var layer = go.GetComponent<Image>(); layer.raycastTarget = false; return layer;
        }
    }
}

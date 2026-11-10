using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BaseConquest.Presentation
{
    public sealed class DemoButtonTint : MonoBehaviour, ISelectHandler, IDeselectHandler
    {
        private Outline outline;
        private Color restingColor;
        private Vector2 restingDistance;
        private bool focused;

        public void OnSelect(BaseEventData eventData)
        {
            if (focused) return;
            outline = GetComponent<Outline>();
            if (outline == null) return;
            focused = true;
            restingColor = outline.effectColor;
            restingDistance = outline.effectDistance;
            outline.effectColor = DemoUiTheme.Current.Ink;
            outline.effectDistance = restingDistance + new Vector2(1, -1);
        }

        public void OnDeselect(BaseEventData eventData) => Restore();
        private void OnDisable() => Restore();

        private void Restore()
        {
            if (!focused) return;
            focused = false;
            if (outline == null) return;
            outline.effectColor = restingColor;
            outline.effectDistance = restingDistance;
        }
    }
}

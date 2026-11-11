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
        private bool restingEnabled, focused, guided;

        public void OnSelect(BaseEventData eventData)
        {
            focused = true;
            ApplyOutline();
        }

        public void OnDeselect(BaseEventData eventData) => ClearFocus();
        private void OnDisable() => ClearFocus();

        public void SetGuide(bool enabled)
        {
            guided = enabled;
            ApplyOutline();
        }

        private void ClearFocus()
        {
            focused = false;
            ApplyOutline();
        }

        private void ApplyOutline()
        {
            if (outline == null && (focused || guided))
            {
                outline = GetComponent<Outline>();
                if (outline == null) return;
                restingColor = outline.effectColor;
                restingDistance = outline.effectDistance;
                restingEnabled = outline.enabled;
            }
            if (outline == null) return;
            outline.effectColor = guided ? DemoUiTheme.Current.Gold : focused ? DemoUiTheme.Current.Ink : restingColor;
            outline.effectDistance = guided ? new Vector2(4, -4) : focused ? restingDistance + new Vector2(1, -1) : restingDistance;
            outline.enabled = guided || restingEnabled;
            if (!guided && !focused) outline = null;
        }
    }
}

using UnityEngine;
using UnityEngine.EventSystems;

namespace BaseConquest.Base
{
    public sealed class BaseJoystick : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        public Vector2 Direction { get; private set; }
        public RectTransform Handle;
        private int pointer = int.MinValue;
        public void OnPointerDown(PointerEventData e) { if (pointer != int.MinValue) return; pointer = e.pointerId; OnDrag(e); }
        public void OnDrag(PointerEventData e)
        {
            if (e.pointerId != pointer) return;
            var rect = (RectTransform)transform;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rect, e.position, e.pressEventCamera, out var local)) return;
            float radius = rect.rect.width * 0.32f;
            Direction = Vector2.ClampMagnitude(local / radius, 1);
            if (Handle != null) Handle.anchoredPosition = Direction * radius;
        }
        public void OnPointerUp(PointerEventData e) { if (e.pointerId == pointer) ResetInput(); }
        public void ResetInput() { pointer = int.MinValue; Direction = Vector2.zero; if (Handle != null) Handle.anchoredPosition = Vector2.zero; }
        private void OnDisable() => ResetInput();
    }
}

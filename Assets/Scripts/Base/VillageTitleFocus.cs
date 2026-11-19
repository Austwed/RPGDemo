using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BaseConquest.Base
{
    [RequireComponent(typeof(Button))]
    public sealed class VillageTitleFocus : MonoBehaviour, ISelectHandler, IDeselectHandler, IPointerEnterHandler, IPointerExitHandler
    {
        private GameObject mark;
        private Button button;
        private bool selected, hovered;

        public void Bind(Button target)
        {
            button = target;
            mark = transform.Find("FocusMark").gameObject;
            Refresh();
        }

        public void OnSelect(BaseEventData data) { selected = true; Refresh(); }
        public void OnDeselect(BaseEventData data) { selected = false; Refresh(); }
        public void OnPointerEnter(PointerEventData data) { hovered = true; Refresh(); }
        public void OnPointerExit(PointerEventData data) { hovered = false; Refresh(); }
        private void OnEnable() { selected = EventSystem.current?.currentSelectedGameObject == gameObject; Refresh(); }
        private void OnDisable() { selected = hovered = false; Refresh(); }
        private void Refresh()
        {
            if (mark != null) mark.SetActive(button.interactable && (selected || hovered));
        }
    }
}

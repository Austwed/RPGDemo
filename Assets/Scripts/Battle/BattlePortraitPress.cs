using UnityEngine;
using UnityEngine.EventSystems;

namespace BaseConquest.Battle
{
    public sealed class BattlePortraitPress : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        public BattleSceneController Owner;
        public int Slot;
        public void OnPointerDown(PointerEventData e) => Owner.BeginPortraitSelection(Slot, e.position);
        public void OnPointerUp(PointerEventData e) { if (Owner.PortraitSelectionDragging) e.eligibleForClick = false; Owner.EndPortraitSelection(); }
        public void OnPointerExit(PointerEventData e) => Owner.PortraitPressEnded();
        public void OnBeginDrag(PointerEventData e) { Owner.MovePortraitSelection(e.position); if (Owner.PortraitSelectionDragging) e.eligibleForClick = false; }
        public void OnDrag(PointerEventData e) { Owner.MovePortraitSelection(e.position); if (Owner.PortraitSelectionDragging) e.eligibleForClick = false; }
        public void OnEndDrag(PointerEventData e) => Owner.EndPortraitSelection();
    }
}

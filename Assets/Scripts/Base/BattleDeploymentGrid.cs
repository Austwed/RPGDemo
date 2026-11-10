using System;
using BaseConquest.Battle;
using UnityEngine;
using UnityEngine.EventSystems;

namespace BaseConquest.Base
{
    // The authored viewport receives UI events. The world camera resolves each pointer to a terrain cell.
    public sealed class BattleDeploymentGrid : MonoBehaviour, IPointerDownHandler, IDragHandler, IEndDragHandler, IPointerUpHandler
    {
        private BaseSceneController owner;
        private Action<GridPosition, bool, bool> select;
        private GridPosition? last;
        private int pointer = int.MinValue;
        private Rect viewport;

        internal void Bind(BaseSceneController controller, Action<GridPosition, bool, bool> onSelect)
        {
            owner = controller;
            select = onSelect;
            Canvas.ForceUpdateCanvases();
            RefreshViewport();
        }

        private void LateUpdate() => RefreshViewport();

        private void RefreshViewport()
        {
            if (owner == null || !owner.DeploymentWorldActive || Screen.width < 1 || Screen.height < 1) return;
            var rect = (RectTransform)transform;
            var min = RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(rect.rect.min));
            var max = RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(rect.rect.max));
            var current = Rect.MinMaxRect(Mathf.Clamp01(min.x / Screen.width), Mathf.Clamp01(min.y / Screen.height),
                Mathf.Clamp01(max.x / Screen.width), Mathf.Clamp01(max.y / Screen.height));
            if (current.width <= 0 || current.height <= 0 || current == viewport) return;
            viewport = current;
            owner.SetDeploymentViewport(current);
        }

        public void OnPointerDown(PointerEventData data)
        {
            if (pointer != int.MinValue || data.button == PointerEventData.InputButton.Middle) return;
            pointer = data.pointerId;
            last = null;
            data.useDragThreshold = false;
            Read(data, false);
        }

        public void OnDrag(PointerEventData data)
        {
            if (pointer == data.pointerId && data.button == PointerEventData.InputButton.Left) Read(data, true);
        }

        public void OnEndDrag(PointerEventData data) => OnPointerUp(data);

        public void OnPointerUp(PointerEventData data)
        {
            if (pointer != data.pointerId) return;
            pointer = int.MinValue;
            last = null;
        }

        private void Read(PointerEventData data, bool dragging)
        {
            if (owner == null || !owner.PickDeploymentCell(data.position, out var cell)) { last = null; return; }
            var previous = last;
            if (previous == cell) return;
            last = cell;
            if (dragging && previous.HasValue)
            {
                int steps = Mathf.Max(Mathf.Abs(previous.Value.X - cell.X), Mathf.Abs(previous.Value.Z - cell.Z));
                for (int step = 1; step < steps; step++)
                {
                    float t = (float)step / steps;
                    select(new GridPosition(Mathf.RoundToInt(Mathf.Lerp(previous.Value.X, cell.X, t)), Mathf.RoundToInt(Mathf.Lerp(previous.Value.Z, cell.Z, t))), false, true);
                }
            }
            select(cell, data.button == PointerEventData.InputButton.Right, dragging);
        }
    }
}

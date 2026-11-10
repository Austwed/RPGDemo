using System;
using System.Linq;
using BaseConquest.Battle;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BaseConquest.Base
{
    public sealed class BattleDeploymentGrid : MaskableGraphic, IPointerDownHandler, IDragHandler, IEndDragHandler, IPointerUpHandler
    {
        private BaseBattlePreparation draft;
        private Action<GridPosition, bool, bool> select;
        private GridPosition? last;
        private int pointer = int.MinValue;

        internal void Bind(BaseBattlePreparation preparation, Action<GridPosition, bool, bool> onSelect)
        {
            draft = preparation;
            select = onSelect;
            SetVerticesDirty();
        }

        private Rect Area()
        {
            var rect = GetPixelAdjustedRect();
            float tile = Mathf.Min((rect.width - 12) / draft.Board.Width, (rect.height - 12) / draft.Board.Height);
            var size = new Vector2(tile * draft.Board.Width, tile * draft.Board.Height);
            return new Rect(rect.center - size / 2, size);
        }

        protected override void OnPopulateMesh(VertexHelper vertices)
        {
            vertices.Clear();
            if (draft?.Board == null) return;
            var area = Area();
            float tile = area.width / draft.Board.Width;
            foreach (var position in draft.Board.Positions())
            {
                var cell = draft.Board.Cell(position);
                var color = cell.Terrain switch
                {
                    TerrainKind.Water => new Color32(65, 92, 98, 255),
                    TerrainKind.Wall => new Color32(71, 61, 57, 255),
                    TerrainKind.Building => new Color32(97, 69, 51, 255),
                    TerrainKind.Swamp => new Color32(101, 99, 64, 255),
                    _ => cell.Surface == GroundSurface.Road || cell.Surface == GroundSurface.Cobble
                        ? new Color32(138, 129, 107, 255) : new Color32(107, 115, 71, 255)
                };
                if (cell.Zone == DeploymentZone.A && cell.Walkable) color = Color.Lerp(color, new Color32(255, 219, 135, 255), .74f);
                else if (cell.Elevation > 0) color = Color.Lerp(color, Color.white, cell.Elevation * .06f);
                var min = area.min + new Vector2(position.X * tile, position.Z * tile);
                float inset = cell.Zone == DeploymentZone.A ? .65f : .28f;
                Quad(vertices, min + Vector2.one * inset, min + Vector2.one * (tile - inset), color);
            }

            var commanders = draft.Units.Where(unit => unit.Kind == SoldierKind.Hero && unit.Selected).Select(unit => unit.Id).ToArray();
            foreach (var unit in draft.Units)
            {
                if (!draft.Positions.TryGetValue(unit.Id, out var cell)) continue;
                var center = area.min + new Vector2((cell.X + .5f) * tile, (cell.Z + .5f) * tile);
                var teamColor = Array.IndexOf(commanders, unit.TeamId) switch
                {
                    1 => new Color32(70, 163, 187, 255),
                    2 => new Color32(176, 103, 159, 255),
                    _ => new Color32(52, 119, 76, 255)
                };
                float halfSize = tile * (unit.Kind == SoldierKind.Hero ? .4f : .29f);
                var size = Vector2.one * halfSize;
                Quad(vertices, center - size, center + size, new Color32(46, 35, 27, 255));
                size *= .76f;
                Quad(vertices, center - size, center + size, teamColor);
                if (unit.Kind == SoldierKind.Hero || unit.Kind == SoldierKind.Ranged)
                    Quad(vertices, center - size * .42f, center + size * .42f, new Color32(249, 228, 171, 255));
                else if (unit.Kind == SoldierKind.Cavalry)
                    Quad(vertices, center - new Vector2(size.x * .68f, size.y * .23f), center + new Vector2(size.x * .68f, size.y * .23f), new Color32(249, 228, 171, 255));
            }
        }

        private static void Quad(VertexHelper vertices, Vector2 min, Vector2 max, Color32 color)
        {
            int index = vertices.currentVertCount;
            vertices.AddVert(new Vector3(min.x, min.y), color, Vector2.zero);
            vertices.AddVert(new Vector3(min.x, max.y), color, Vector2.zero);
            vertices.AddVert(new Vector3(max.x, max.y), color, Vector2.zero);
            vertices.AddVert(new Vector3(max.x, min.y), color, Vector2.zero);
            vertices.AddTriangle(index, index + 1, index + 2);
            vertices.AddTriangle(index, index + 2, index + 3);
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
            if (draft?.Board == null || !RectTransformUtility.ScreenPointToLocalPointInRectangle(rectTransform, data.position, data.pressEventCamera, out var local)) return;
            var area = Area();
            if (!area.Contains(local)) { last = null; return; }
            float tile = area.width / draft.Board.Width;
            var cell = new GridPosition(Mathf.FloorToInt((local.x - area.xMin) / tile), Mathf.FloorToInt((local.y - area.yMin) / tile));
            var previous = last;
            if (previous == cell) return;
            last = cell;
            if (dragging && previous.HasValue)
            {
                int steps = Mathf.Max(Mathf.Abs(previous.Value.X - cell.X), Mathf.Abs(previous.Value.Z - cell.Z));
                for (int step = 1; step < steps; step++)
                {
                    float t = (float)step / steps;
                    select?.Invoke(new GridPosition(Mathf.RoundToInt(Mathf.Lerp(previous.Value.X, cell.X, t)), Mathf.RoundToInt(Mathf.Lerp(previous.Value.Z, cell.Z, t))), false, true);
                }
            }
            select?.Invoke(cell, data.button == PointerEventData.InputButton.Right, dragging);
        }
    }
}

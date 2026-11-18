using BaseConquest.Presentation;
using UnityEngine;
using UnityEngine.UI;

namespace BaseConquest.Battle
{
    [ExecuteAlways]
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class BattleAvatarGraphic : MaskableGraphic
    {
        [SerializeField] private string appearanceId = "";
        [SerializeField] private bool playerHero;
        private SoldierKind kind = SoldierKind.Hero;

        public string AppearanceId
        {
            get => appearanceId;
            set { if (appearanceId == value) return; appearanceId = value; SetAllDirty(); }
        }

        public bool PlayerHero
        {
            get => playerHero;
            set { if (playerHero == value) return; playerHero = value; SetAllDirty(); }
        }

        public SoldierKind Kind
        {
            get => kind;
            set { if (kind == value) return; kind = value; SetAllDirty(); }
        }

        public override Texture mainTexture => DemoPortraits.HalfBody(appearanceId, playerHero, kind);

        protected override void OnPopulateMesh(VertexHelper vertices)
        {
            vertices.Clear();
            if (mainTexture == null) return;
            Rect rect = rectTransform.rect;
            float half = Mathf.Min(rect.width, rect.height) / 2;
            Vector2 center = rect.center;
            vertices.AddVert(center + new Vector2(-half, -half), color, Vector2.zero);
            vertices.AddVert(center + new Vector2(-half, half), color, Vector2.up);
            vertices.AddVert(center + new Vector2(half, half), color, Vector2.one);
            vertices.AddVert(center + new Vector2(half, -half), color, Vector2.right);
            vertices.AddTriangle(0, 1, 2);
            vertices.AddTriangle(0, 2, 3);
        }
    }
}

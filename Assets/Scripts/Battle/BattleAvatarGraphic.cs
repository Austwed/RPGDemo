using UnityEngine;
using UnityEngine.UI;

namespace BaseConquest.Battle
{
    [ExecuteAlways]
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class BattleAvatarGraphic : MaskableGraphic
    {
        public SoldierKind Kind = SoldierKind.Hero;
        [SerializeField] private bool playerHero;
        private bool dialoguePortrait;
        private Texture2D heroAtlas;
        [SerializeField] private string appearanceId="";
        private Texture2D companionAtlas;
        private Texture2D recommended;
        private Rect recommendedUV;
        public string AppearanceId {get=>appearanceId;set{value=value??"";if(appearanceId==value)return;appearanceId=value;companionAtlas=null;SetAllDirty();}}
        public bool PlayerHero {get=>playerHero;set{if(playerHero==value)return;playerHero=value;SetAllDirty();}}
        public bool DialoguePortrait { get => dialoguePortrait; set { if (dialoguePortrait == value) return; dialoguePortrait = value; SetAllDirty(); } }
        public override Texture mainTexture {get{
            var result=DialoguePortrait ? BaseConquest.Presentation.DemoPortraits.ForDialogue(AppearanceId,PlayerHero) :
                BaseConquest.Presentation.DemoPortraits.For(AppearanceId,PlayerHero);
            recommended=result.Origin==BaseConquest.Presentation.DemoPortraits.Source.Portrait||result.Origin==BaseConquest.Presentation.DemoPortraits.Source.CharacterSheet?result.Texture:null;
            recommendedUV=result.Uv;companionAtlas=result.Origin==BaseConquest.Presentation.DemoPortraits.Source.LegacyCompanion?result.Texture:null;heroAtlas=result.Origin==BaseConquest.Presentation.DemoPortraits.Source.LegacyHero?result.Texture:null;
            return result.Texture!=null?result.Texture:Texture2D.whiteTexture;}}
        public Color BodyColor = new Color(0.55f, 0.82f, 1);
        private Color crown = Color.yellow;
        public Color Crown { get => crown; set { if (crown == value) return; crown = value; SetVerticesDirty(); } }
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear(); var r = rectTransform.rect;
            var texture=mainTexture;
            if(recommended!=null)
            {
                float size=Mathf.Min(r.width,r.height);var area=new Rect(r.center.x-size*.5f,r.center.y-size*.5f,size,size);
                vh.AddVert(new Vector2(area.xMin,area.yMin),Color.white,new Vector2(recommendedUV.xMin,recommendedUV.yMin));vh.AddVert(new Vector2(area.xMin,area.yMax),Color.white,new Vector2(recommendedUV.xMin,recommendedUV.yMax));vh.AddVert(new Vector2(area.xMax,area.yMax),Color.white,new Vector2(recommendedUV.xMax,recommendedUV.yMax));vh.AddVert(new Vector2(area.xMax,area.yMin),Color.white,new Vector2(recommendedUV.xMax,recommendedUV.yMin));vh.AddTriangle(0,1,2);vh.AddTriangle(0,2,3);return;
            }
            if(!PlayerHero&&companionAtlas!=null)
            {
                var clip=BaseConquest.Presentation.CommanderArt.For(AppearanceId).Clips[0];var uv=BaseConquest.Presentation.CommanderArt.UV(clip,companionAtlas);
                float width=Mathf.Min(r.width,r.height*clip.width/clip.height),height=width*clip.height/clip.width;
                var area=new Rect(r.center.x-width*.5f,r.center.y-height*.5f,width,height);
                vh.AddVert(new Vector2(area.xMin,area.yMin),Color.white,new Vector2(uv.xMin,uv.yMin));vh.AddVert(new Vector2(area.xMin,area.yMax),Color.white,new Vector2(uv.xMin,uv.yMax));
                vh.AddVert(new Vector2(area.xMax,area.yMax),Color.white,new Vector2(uv.xMax,uv.yMax));vh.AddVert(new Vector2(area.xMax,area.yMin),Color.white,new Vector2(uv.xMax,uv.yMin));vh.AddTriangle(0,1,2);vh.AddTriangle(0,2,3);return;
            }
            if(PlayerHero&&heroAtlas!=null)
            {
                vh.AddVert(new Vector2(r.xMin,r.yMin),Color.white,new Vector2(0,7/8f));vh.AddVert(new Vector2(r.xMin,r.yMax),Color.white,new Vector2(0,1));
                vh.AddVert(new Vector2(r.xMax,r.yMax),Color.white,new Vector2(1/8f,1));vh.AddVert(new Vector2(r.xMax,r.yMin),Color.white,new Vector2(1/8f,7/8f));vh.AddTriangle(0,1,2);vh.AddTriangle(0,2,3);return;
            }
            AddRect(vh, new Rect(r.xMin + r.width * 0.28f, r.yMin + r.height * 0.05f, r.width * 0.44f, r.height * 0.5f), BodyColor);
            int start = vh.currentVertCount; var center = new Vector2(r.center.x, r.yMin + r.height * 0.65f); float radius = r.width * 0.2f;
            vh.AddVert(center, new Color(0.98f, 0.84f, 0.68f), Vector2.zero);
            for (int i = 0; i < 20; i++) { float a = i * Mathf.PI * 2 / 20; vh.AddVert(center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius, new Color(0.98f, 0.84f, 0.68f), Vector2.zero); }
            for (int i = 0; i < 20; i++) vh.AddTriangle(start, start + i + 1, start + (i + 1) % 20 + 1);
            float w = r.width, h = r.height;
            if (Kind != SoldierKind.Hero)
            {
                var star = new Vector2(r.center.x, r.yMin + h * 0.3f);
                for (int i = 0; i < 4; i++)
                {
                    int n = vh.currentVertCount; float a = i * Mathf.PI / 2;
                    vh.AddVert(star, crown, Vector2.zero); vh.AddVert(star + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * w * 0.12f, crown, Vector2.zero);
                    vh.AddVert(star + new Vector2(Mathf.Cos(a + Mathf.PI / 4), Mathf.Sin(a + Mathf.PI / 4)) * w * 0.035f, crown, Vector2.zero); vh.AddTriangle(n, n + 1, n + 2);
                    vh.AddVert(star + new Vector2(Mathf.Cos(a - Mathf.PI / 4), Mathf.Sin(a - Mathf.PI / 4)) * w * 0.035f, crown, Vector2.zero); vh.AddTriangle(n, n + 3, n + 1);
                }
                if (Kind == SoldierKind.Melee) { AddRect(vh, new Rect(r.xMin + w * 0.08f, r.yMin + h * 0.22f, w * 0.16f, h * 0.25f), Color.gray); AddRect(vh, new Rect(r.xMin + w * 0.78f, r.yMin + h * 0.18f, w * 0.045f, h * 0.38f), Color.white); }
                else if (Kind == SoldierKind.Ranged) { AddRect(vh, new Rect(r.xMin + w * 0.8f, r.yMin + h * 0.18f, w * 0.04f, h * 0.4f), new Color(0.6f, 0.3f, 0.1f)); AddRect(vh, new Rect(r.xMin + w * 0.72f, r.yMin + h * 0.36f, w * 0.22f, h * 0.02f), Color.white); }
                else AddRect(vh, new Rect(r.xMin + w * 0.1f, r.yMin, w * 0.8f, h * 0.16f), new Color(0.45f, 0.26f, 0.12f));
                return;
            }
            AddRect(vh, new Rect(r.xMin + w * 0.27f, r.yMin + h * 0.83f, w * 0.46f, h * 0.07f), crown);
            for (int i = 0; i < 3; i++)
            {
                float x = r.xMin + w * (0.28f + i * 0.15f); int n = vh.currentVertCount;
                vh.AddVert(new Vector2(x, r.yMin + h * 0.88f), crown, Vector2.zero); vh.AddVert(new Vector2(x + w * 0.15f, r.yMin + h * 0.88f), crown, Vector2.zero); vh.AddVert(new Vector2(x + w * 0.075f, r.yMin + h * 0.99f), crown, Vector2.zero); vh.AddTriangle(n, n + 1, n + 2);
            }
        }
        private static void AddRect(VertexHelper vh, Rect r, Color color)
        {
            int n = vh.currentVertCount; vh.AddVert(new Vector2(r.xMin, r.yMin), color, Vector2.zero); vh.AddVert(new Vector2(r.xMax, r.yMin), color, Vector2.zero); vh.AddVert(new Vector2(r.xMax, r.yMax), color, Vector2.zero); vh.AddVert(new Vector2(r.xMin, r.yMax), color, Vector2.zero); vh.AddTriangle(n, n + 1, n + 2); vh.AddTriangle(n, n + 2, n + 3);
        }
    }
}

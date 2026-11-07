using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
namespace BaseConquest.Presentation
{
    // 色板照原型 `:root`：纸底、墨绿字、绿色强调。改这里等于改全工程。
    [CreateAssetMenu(menuName="Demo/UI主题")]
    public sealed class DemoUiTheme : ScriptableObject
    {
        public Color Panel=new Color(.9686f,.9647f,.9255f,.98f),Inset=new Color(.9098f,.9333f,.8745f,.98f),Button=new Color(1f,.9961f,.9804f,1f),Ink=new Color(.149f,.2314f,.2039f),Copper=new Color(.2588f,.4196f,.3137f),Muted=new Color(.4431f,.502f,.4706f);
        public Color Line=new Color(.851f,.867f,.816f),Shade=new Color(.114f,.188f,.153f,.68f);
        private static DemoUiTheme cached;
        public static DemoUiTheme Current=>cached!=null?cached:cached=Resources.Load<DemoUiTheme>("Presentation/DemoUI/Theme")??CreateInstance<DemoUiTheme>();
        public static Sprite Skin(string name)=>Resources.Load<Sprite>("PixelDemo/UI/"+name+"-v1");
        // 原型面板＝纸底＋1 px 描边，没有像素外框；旧外框节点留着也只是关掉，避免影响已有预制。
        public void Frame(RectTransform parent,bool dialogue=false)
        {
            var image=parent.GetComponent<Image>();
            if(image!=null){image.sprite=null;image.type=Image.Type.Simple;image.color=dialogue?new Color(Panel.r,Panel.g,Panel.b,.94f):Panel;}
            var outline=parent.GetComponent<Outline>()??parent.gameObject.AddComponent<Outline>();
            outline.effectColor=new Color(Line.r,Line.g,Line.b,.95f);outline.effectDistance=new Vector2(1,-1);outline.useGraphicAlpha=false;
            var child=parent.Find("DemoPixelFrame");if(child!=null)child.gameObject.SetActive(false);
        }
        public void Surface(Image image,bool button=false,bool inset=false)
        {
            image.color=button?Button:inset?Inset:Panel;
            var outline=image.GetComponent<Outline>()??image.gameObject.AddComponent<Outline>();outline.effectColor=new Color(Line.r,Line.g,Line.b,.90f);outline.effectDistance=new Vector2(1,-1);outline.useGraphicAlpha=true;
        }
        public void StyleButton(UnityEngine.UI.Button b)
        {
            bool slot=b.name.StartsWith("BagSlot_")||b.name.StartsWith("EquipmentSlot_")||b.name.StartsWith("物品格_")||b.name.StartsWith("装备槽_");
            var image=b.GetComponent<Image>();Surface(image,!slot,slot);
            image.sprite=null;image.type=Image.Type.Simple;image.color=slot?Inset:Button;
            b.transition=Selectable.Transition.ColorTint;
            var c=b.colors;c.normalColor=Color.white;c.highlightedColor=new Color(.91f,.93f,.87f);c.selectedColor=c.highlightedColor;c.pressedColor=new Color(.84f,.88f,.80f);c.disabledColor=new Color(.80f,.80f,.76f,.55f);c.fadeDuration=.06f;b.colors=c;
            if(b.GetComponent<DemoButtonTint>()==null)b.gameObject.AddComponent<DemoButtonTint>();
        }
        // protectedNodes 是 prefab 自带的节点：它们的颜色、字体、按钮态由美术在 prefab 里决定，主题不得回写。
        // 不传（或传 null）＝全量刷主题，用于 C# 主导的旧路径与本次新造的节点。
        public void Apply(GameObject root,HashSet<Transform> protectedNodes=null)
        {
            bool Guarded(Component c)=>protectedNodes!=null&&protectedNodes.Contains(c.transform);
            foreach(var image in root.GetComponentsInChildren<Image>(true))
            {
                if(Guarded(image)||image.sprite!=null||image.color.a<.35f)continue;
                // 文字节点不该有底：模板里的文本样板自带一层白色背板，浅色主题下会挡住场景。
                if(image.GetComponent<Text>()!=null){image.enabled=false;continue;}
                bool shade=image.name.Contains("Shade")||image.name.Contains("Backdrop");
                if(shade){image.color=Shade;continue;}
                Surface(image,image.GetComponent<UnityEngine.UI.Button>()!=null,image.name.Contains("物品格")||image.name.Contains("装备槽"));
            }
            foreach(var button in root.GetComponentsInChildren<UnityEngine.UI.Button>(true))if(button.GetComponent<Image>()!=null&&!Guarded(button))StyleButton(button);
            foreach(var text in root.GetComponentsInChildren<Text>(true))
            {if(text.name=="NpcStatusMarker"||Guarded(text))continue;text.color=Ink;}
        }
    }
    // 第六态：被 EventSystem 选中（键盘／手柄聚焦）时描边加粗。着色交给 ColorTint，这里只管焦点环。
    public sealed class DemoButtonTint : MonoBehaviour, ISelectHandler, IDeselectHandler
    {
        private Button button;private Image image;private bool focused;
        private void Awake(){button=GetComponent<Button>();image=GetComponent<Image>();}
        public void OnSelect(BaseEventData eventData){focused=true;ApplyFocus();}
        public void OnDeselect(BaseEventData eventData){focused=false;ApplyFocus();}
        private void ApplyFocus()
        {
            var target=image!=null?image:GetComponent<Image>();
            var ring=target!=null?target.GetComponent<Outline>():null;
            if(ring==null)return;
            var theme=DemoUiTheme.Current;
            ring.effectDistance=focused?new Vector2(2,-2):new Vector2(1,-1);
            ring.effectColor=focused?new Color(theme.Ink.r,theme.Ink.g,theme.Ink.b,.95f):new Color(theme.Line.r,theme.Line.g,theme.Line.b,.90f);
        }
    }
}

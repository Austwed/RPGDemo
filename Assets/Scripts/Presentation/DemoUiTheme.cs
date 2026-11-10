using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
namespace BaseConquest.Presentation
{
    // Palette from the interactive prototype's wood theme.
    [CreateAssetMenu(menuName="Demo/UI主题")]
    public sealed class DemoUiTheme : ScriptableObject
    {
        public Color Panel=new Color32(243,226,188,255),Inset=new Color32(234,211,167,255),Button=new Color32(233,206,157,255),Ink=new Color32(58,42,28,255),Copper=new Color32(137,97,36,255),Muted=new Color32(114,87,57,255);
        public Color Line=new Color32(73,53,37,255),Shade=new Color32(32,21,13,174);
        public Color Wood=new Color32(75,58,44,255),Gold=new Color32(229,171,64,255);
        private static DemoUiTheme cached;
        public static DemoUiTheme Current=>cached!=null?cached:cached=Resources.Load<DemoUiTheme>("Presentation/DemoUI/Theme")??CreateInstance<DemoUiTheme>();
        public static Sprite Skin(string name)=>Resources.Load<Sprite>("PixelDemo/UI/"+name+"-v1");
        public void Frame(RectTransform parent,bool dialogue=false)
        {
            var image=parent.GetComponent<Image>();
            if(image!=null){image.sprite=null;image.type=Image.Type.Simple;image.color=dialogue?new Color(Panel.r,Panel.g,Panel.b,.94f):Panel;}
            var outline=parent.GetComponent<Outline>()??parent.gameObject.AddComponent<Outline>();
            outline.effectColor=new Color(Line.r,Line.g,Line.b,.95f);outline.effectDistance=new Vector2(2,-2);outline.useGraphicAlpha=false;
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
            var c=b.colors;c.normalColor=Color.white;c.highlightedColor=new Color(1,.96f,.84f);c.selectedColor=c.highlightedColor;c.pressedColor=new Color(.88f,.73f,.50f);c.disabledColor=new Color(.69f,.63f,.55f,.6f);c.fadeDuration=.06f;b.colors=c;
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
}

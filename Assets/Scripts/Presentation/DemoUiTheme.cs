using UnityEngine;
using UnityEngine.UI;
namespace BaseConquest.Presentation
{
    [CreateAssetMenu(menuName="Demo/UI主题")]
    public sealed class DemoUiTheme : ScriptableObject
    {
        public Color Panel=new Color(.23f,.21f,.19f,.98f),Inset=new Color(.16f,.15f,.14f,.98f),Button=new Color(.39f,.33f,.24f,1),Ink=new Color(.96f,.92f,.83f),Copper=new Color(.72f,.55f,.32f),Muted=new Color(.63f,.59f,.51f);
        private static DemoUiTheme cached;
        public static DemoUiTheme Current=>cached!=null?cached:cached=Resources.Load<DemoUiTheme>("Presentation/DemoUI/Theme")??CreateInstance<DemoUiTheme>();
        public static Sprite Skin(string name)=>Resources.Load<Sprite>("PixelDemo/UI/"+name+"-v1");
        public void Frame(RectTransform parent,bool dialogue=false)
        {
            var sprite=Skin(dialogue?"dialogue-frame":"window-frame");if(sprite==null)return;
            var child=parent.Find("DemoPixelFrame") as RectTransform;
            if(child==null){var go=new GameObject("DemoPixelFrame",typeof(RectTransform),typeof(Image));child=(RectTransform)go.transform;child.SetParent(parent,false);child.SetAsFirstSibling();}
            child.anchorMin=Vector2.zero;child.anchorMax=Vector2.one;child.offsetMin=child.offsetMax=Vector2.zero;
            var image=child.GetComponent<Image>();image.sprite=sprite;image.type=Image.Type.Sliced;image.color=Color.white;image.raycastTarget=false;
        }
        public void Surface(Image image,bool button=false,bool inset=false)
        {
            image.color=button?Button:inset?Inset:Panel;
            var outline=image.GetComponent<Outline>()??image.gameObject.AddComponent<Outline>();outline.effectColor=new Color(Copper.r,Copper.g,Copper.b,.65f);outline.effectDistance=new Vector2(1,-1);outline.useGraphicAlpha=true;
        }
        public void StyleButton(UnityEngine.UI.Button b)
        {
            bool slot=b.name.StartsWith("BagSlot_")||b.name.StartsWith("EquipmentSlot_")||b.name.StartsWith("物品格_")||b.name.StartsWith("装备槽_");var image=b.GetComponent<Image>();Surface(image,!slot,slot);var c=b.colors;c.normalColor=Color.white;c.highlightedColor=new Color(1.28f,1.23f,1.12f);c.selectedColor=c.highlightedColor;c.pressedColor=new Color(.72f,.65f,.52f);c.disabledColor=new Color(.55f,.53f,.5f,.6f);c.fadeDuration=.08f;b.colors=c;
            var normal=slot?null:Skin("button-normal");var selected=slot?null:Skin("button-selected");
            if(normal!=null&&selected!=null){image.sprite=normal;image.type=Image.Type.Sliced;image.color=Color.white;b.transition=Selectable.Transition.SpriteSwap;var state=b.spriteState;state.highlightedSprite=selected;state.selectedSprite=selected;state.pressedSprite=selected;state.disabledSprite=normal;b.spriteState=state;if(b.GetComponent<DemoButtonTint>()==null)b.gameObject.AddComponent<DemoButtonTint>();}
        }
        public void Apply(GameObject root)
        {
            foreach(var image in root.GetComponentsInChildren<Image>(true))
            {
                if(image.sprite!=null||image.color.a<.35f)continue;
                bool shade=image.name.Contains("Shade")||image.name.Contains("Backdrop");
                if(shade){image.color=new Color(.10f,.08f,.06f,.64f);continue;}
                Surface(image,image.GetComponent<UnityEngine.UI.Button>()!=null,image.name.Contains("物品格")||image.name.Contains("装备槽")||image.name.Contains("Viewport"));
            }
            foreach(var button in root.GetComponentsInChildren<UnityEngine.UI.Button>(true))if(button.GetComponent<Image>()!=null)StyleButton(button);
            foreach(var text in root.GetComponentsInChildren<Text>(true))
            {if(text.name=="NpcStatusMarker")continue;text.color=Ink;}
        }
    }
    public sealed class DemoButtonTint : MonoBehaviour
    {
        private Button button;private Image image;
        private void Awake(){button=GetComponent<Button>();image=GetComponent<Image>();}
        private void Update(){if(button!=null&&image!=null)image.color=button.IsInteractable()?Color.white:new Color(.55f,.53f,.5f,.6f);}
    }
}

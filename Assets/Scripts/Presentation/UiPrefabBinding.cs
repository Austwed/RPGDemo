using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace BaseConquest.Presentation
{
    // The prefab owns presentation. Controllers still own values, visibility and actions.
    [DisallowMultipleComponent]
    public sealed class UiPrefabBinding : MonoBehaviour
    {
        public const string Folder = "Presentation/UI/";
        public static bool ExportDefaults;
        [HideInInspector] public string ResourceKey;
        [NonSerialized] private readonly Dictionary<Transform, Appearance> authored = new Dictionary<Transform, Appearance>();
        [NonSerialized] private readonly HashSet<Transform> claimed = new HashSet<Transform>();
        [NonSerialized] private readonly Dictionary<Transform, Appearance> repeated = new Dictionary<Transform, Appearance>();
        [NonSerialized] private readonly Dictionary<Transform, Transform> prototypes = new Dictionary<Transform, Transform>();
        [NonSerialized] private bool completed;
        private bool legacyDemo, sceneLayout;

        private sealed class Appearance
        {
            public Vector2 Min, Max, Pivot, Position, Size;
            public Vector3 Scale;
            public Quaternion Rotation;
            public bool Layout, StaticText;
            public Color? Ink, Background;
            public Font Font;
            public int FontSize, BestMin, BestMax;
            public FontStyle FontStyle;
            public TextAnchor Alignment;
            public float LineSpacing;
            public bool BestFit;
            public string Text;
            public Sprite Sprite;
            public Image.Type ImageType;
            public ColorBlock? ButtonColors;
            // 只有试点页面接管描边与文字阴影：否则 Box/Label 创建节点时会把 prefab 里的美术值写回代码默认值。
            public Color? OutlineColor, ShadowColor;
            public Vector2? OutlineDistance, ShadowDistance;
            public bool OutlineUseAlpha;
            public CanvasScaler.ScaleMode? ScaleMode;
            public CanvasScaler.ScreenMatchMode MatchMode;
            public Vector2 ReferenceResolution;
            public float Match, ScaleFactor, PixelsPerUnit;
            public int? SortOrder;
            public Appearance(Transform t, UiVisualNode node, bool authoredOwns)
            {
                var r = t as RectTransform;
                if (r != null) { Min=r.anchorMin;Max=r.anchorMax;Pivot=r.pivot;Position=r.anchoredPosition;Size=r.sizeDelta;Scale=r.localScale;Rotation=r.localRotation; }
                bool dynamic=t.GetComponentInParent<UiDynamicContent>(true)!=null;
                Layout=node.UseAuthoredLayout&&!dynamic;StaticText=node.OverrideStaticText&&!dynamic;
                var text=t.GetComponent<Text>();
                if(text!=null) { Ink=text.color;Font=text.font;FontSize=text.fontSize;FontStyle=text.fontStyle;Alignment=text.alignment;LineSpacing=text.lineSpacing;Text=text.text;BestFit=text.resizeTextForBestFit;BestMin=text.resizeTextMinSize;BestMax=text.resizeTextMaxSize; }
                var image=t.GetComponent<Image>();if(image!=null){Background=image.color;Sprite=image.sprite;ImageType=image.type;}
                var button=t.GetComponent<Button>();if(button!=null)ButtonColors=button.colors;
                if(authoredOwns)
                {
                    var outline=t.GetComponent<Outline>();if(outline!=null){OutlineColor=outline.effectColor;OutlineDistance=outline.effectDistance;OutlineUseAlpha=outline.useGraphicAlpha;}
                    var shadow=t.GetComponent<Shadow>();if(shadow!=null&&shadow.GetType()==typeof(Shadow)){ShadowColor=shadow.effectColor;ShadowDistance=shadow.effectDistance;}
                }
                var scaler=t.GetComponent<CanvasScaler>();if(scaler!=null){ScaleMode=scaler.uiScaleMode;MatchMode=scaler.screenMatchMode;ReferenceResolution=scaler.referenceResolution;Match=scaler.matchWidthOrHeight;ScaleFactor=scaler.scaleFactor;PixelsPerUnit=scaler.referencePixelsPerUnit;}
                var canvas=t.GetComponent<Canvas>();if(canvas!=null)SortOrder=canvas.sortingOrder;
            }
            public void Apply(Transform t)
            {
                var r=t as RectTransform;
                if(Layout&&r!=null&&t.GetComponent<UiVisualNode>()?.UseAuthoredLayout!=false&&t.GetComponentInParent<UiDynamicContent>(true)==null){r.anchorMin=Min;r.anchorMax=Max;r.pivot=Pivot;r.anchoredPosition=Position;r.sizeDelta=Size;r.localScale=Scale;r.localRotation=Rotation;}
                var text=t.GetComponent<Text>();if(text!=null&&Ink.HasValue){text.color=Ink.Value;if(Font!=null)text.font=Font;text.fontSize=FontSize;text.fontStyle=FontStyle;text.alignment=Alignment;text.lineSpacing=LineSpacing;text.resizeTextForBestFit=BestFit;text.resizeTextMinSize=BestMin;text.resizeTextMaxSize=BestMax;if(StaticText)text.text=Text;}
                var image=t.GetComponent<Image>();if(image!=null&&Background.HasValue){image.color=Background.Value;image.sprite=Sprite;image.type=ImageType;}
                var button=t.GetComponent<Button>();if(button!=null&&ButtonColors.HasValue)button.colors=ButtonColors.Value;
                var outline=t.GetComponent<Outline>();if(outline!=null&&OutlineColor.HasValue){outline.effectColor=OutlineColor.Value;outline.effectDistance=OutlineDistance.Value;outline.useGraphicAlpha=OutlineUseAlpha;}
                var shadow=t.GetComponent<Shadow>();if(shadow!=null&&shadow.GetType()==typeof(Shadow)&&ShadowColor.HasValue){shadow.effectColor=ShadowColor.Value;shadow.effectDistance=ShadowDistance.Value;}
                var scaler=t.GetComponent<CanvasScaler>();if(scaler!=null&&ScaleMode.HasValue){scaler.uiScaleMode=ScaleMode.Value;scaler.screenMatchMode=MatchMode;scaler.referenceResolution=ReferenceResolution;scaler.matchWidthOrHeight=Match;scaler.scaleFactor=ScaleFactor;scaler.referencePixelsPerUnit=PixelsPerUnit;}
                var canvas=t.GetComponent<Canvas>();if(canvas!=null&&SortOrder.HasValue)canvas.sortingOrder=SortOrder.Value;
            }
        }

        public static GameObject Create(string key, string name, Transform parent, bool canvas = false, GameObject source = null, bool sceneLayout = false)
        {
            bool demo=BaseConquest.Base.BaseBattleGateway.DemoReleaseMode||BaseConquest.Base.BaseDemoRelease.Enabled(BaseConquest.Base.BaseBattleGateway.DesignOpening?.Opening);
            GameObject prefab=null;
            if(!ExportDefaults)
            {
                prefab=source;
                if(prefab==null)prefab=(demo?Resources.Load<GameObject>("Presentation/DemoUI/"+key):null)??Resources.Load<GameObject>("Presentation/ReadableUI/"+key)??Resources.Load<GameObject>(Folder+key);
            }
            if(prefab==null&&!ExportDefaults&&key.StartsWith("Page_",StringComparison.Ordinal))prefab=(demo?Resources.Load<GameObject>("Presentation/DemoUI/CommonPanel"):null)??Resources.Load<GameObject>("Presentation/ReadableUI/CommonPanel")??Resources.Load<GameObject>(Folder+"CommonPanel");
            var root=prefab!=null ? Instantiate(prefab,parent,false) : new GameObject(name,typeof(RectTransform));
            root.name=name;root.transform.SetParent(parent,false);
            var binding=Ensure<UiPrefabBinding>(root);binding.ResourceKey=key;binding.authored.Clear();binding.claimed.Clear();binding.repeated.Clear();binding.prototypes.Clear();binding.completed=false;
            binding.sceneLayout=sceneLayout&&prefab!=null;
            binding.legacyDemo=demo;
            var rootMarker=Ensure<UiVisualNode>(root);rootMarker.BindingName=name;if(prefab==null||canvas)rootMarker.UseAuthoredLayout=!canvas;
            if(prefab!=null)foreach(var node in root.GetComponentsInChildren<UiVisualNode>(true))
                if(!node.Decoration)binding.authored[node.transform]=new Appearance(node.transform,node,binding.sceneLayout);
            binding.claimed.Add(root.transform);
            if(canvas){Ensure<Canvas>(root);Ensure<CanvasScaler>(root);Ensure<GraphicRaycaster>(root);}
            Ensure<UiSystemFont>(root);
            root.SetActive(true);return root;
        }
        public static T Ensure<T>(GameObject obj) where T:Component => obj.GetComponent<T>() ?? obj.AddComponent<T>();

        public static RectTransform Rect(string name, Transform parent)
        {
            var binding=Owner(parent);Transform found=null;
            if(binding!=null&&!binding.completed)
                foreach(Transform child in parent)
                {
                    var node=child.GetComponent<UiVisualNode>();
                    if(node!=null&&!node.Decoration&&node.BindingName==name&&!binding.claimed.Contains(child)){found=child;break;}
                }
            bool dynamic=parent.GetComponentInParent<UiDynamicContent>(true)!=null;
            Transform prototype=null;
            if(binding!=null&&!binding.completed&&dynamic&&found==null)
            {
                // A new quantity/name is still the same kind of data row. Reuse the visual
                // example by role, and repeat its immutable style when the live list is longer.
                Transform source=parent;if(binding.prototypes.TryGetValue(parent,out var original))source=original;
                bool text=name=="Text"||name=="Label";
                foreach(Transform child in source)
                {
                    if(!binding.authored.ContainsKey(child))continue;
                    var node=child.GetComponent<UiVisualNode>();if(node==null||node.Decoration)continue;
                    bool compatible=text ? child.GetComponent<Text>()!=null : child.GetComponent<Button>()!=null;
                    if(!compatible)continue;prototype=child;
                    if(source==parent&&!binding.claimed.Contains(child)){found=child;break;}
                }
            }
            if(found==null)
            {
                var obj=new GameObject(name,typeof(RectTransform));obj.transform.SetParent(parent,false);found=obj.transform;
                if(prototype!=null){binding.repeated[found]=binding.authored[prototype];binding.prototypes[found]=prototype;}
            }
            var marker=Ensure<UiVisualNode>(found.gameObject);marker.BindingName=name;
            // List content is arranged from live records, never from example record positions.
            if(binding?.sceneLayout!=true||!binding.authored.ContainsKey(found))
            {
                if(parent.name=="Content"||parent.GetComponent<UiDynamicContent>()!=null||!marker.UseAuthoredLayout)marker.UseAuthoredLayout=false;
                if(parent.GetComponent<UiVisualNode>()?.UseAuthoredLayout==false&&binding?.ResourceKey!="PartyMemberCard")marker.UseAuthoredLayout=false;
            }
            binding?.claimed.Add(found);return (RectTransform)found;
        }
        private static UiPrefabBinding Owner(Transform t)
        {while(t!=null){var b=t.GetComponent<UiPrefabBinding>();if(b!=null)return b;t=t.parent;}return null;}
        public static void Complete(GameObject root)
        {
            var binding=root.GetComponent<UiPrefabBinding>();if(binding==null||binding.completed)return;
            // prefab 主导时，把「来自 prefab 的节点」保护起来：主题只负责本次新造的节点。
            var protectedNodes=binding.sceneLayout
                ?new HashSet<Transform>(binding.authored.Keys.Where(t=>t!=null)):null;
            // 嵌套的子面板（例如背包里每个人物卡）有自己的 prefab 与自己的 Complete，父面板的主题刷新不得越界。
            if(protectedNodes!=null)
                foreach(var nested in root.GetComponentsInChildren<UiPrefabBinding>(true))
                    if(nested!=binding)
                        foreach(var t in nested.GetComponentsInChildren<Transform>(true))protectedNodes.Add(t);
            foreach(var pair in binding.authored)
                if(pair.Key!=null&&binding.claimed.Contains(pair.Key))pair.Value.Apply(pair.Key);
            foreach(var pair in binding.repeated)if(pair.Key!=null)pair.Value.Apply(pair.Key);
            // Remove unused sample records, including those under an inactive page.
            var unused=binding.authored.Keys.Where(t=>t!=null&&!binding.claimed.Contains(t)).ToArray();
            var set=new HashSet<Transform>(unused);
            foreach(var t in unused)if(t!=null&&!set.Contains(t.parent)){t.gameObject.SetActive(false);DestroyView(t.gameObject);}
            binding.authored.Clear();binding.claimed.Clear();binding.repeated.Clear();binding.prototypes.Clear();binding.completed=true;
            if(binding.legacyDemo||ExportDefaults||root.GetComponent<DemoUiRevision>()?.UseThemeColors==true){DemoUiTheme.Current.Apply(root,protectedNodes);Ensure<DemoUiRevision>(root).Version=14;}
            if(Application.isPlaying&&root.GetComponent<Canvas>()!=null)Ensure<GameFrameCanvas>(root).Apply();
            Ensure<UiSystemFont>(root).Refresh();
        }
        public static void DestroyView(UnityEngine.Object value)
        {if(value==null)return;if(Application.isPlaying)Destroy(value);else DestroyImmediate(value);}

        public static string PageKey(string title,string viewKey)
        {
            string key=viewKey??title??"通用窗口";
            if(key.StartsWith("departure:choice:",StringComparison.Ordinal))return "Page_departure_choice";
            string[] variable={"loot:","personnel:","bag-item:","town-good:","supply:","party:","append:","campaign-preview:","departure:","muster:"};
            foreach(var prefix in variable)if(key.StartsWith(prefix,StringComparison.Ordinal)){key=prefix.TrimEnd(':');break;}
            if(key.StartsWith("backpack:",StringComparison.Ordinal))key="backpack";
            if(key.StartsWith("government:army:",StringComparison.Ordinal))key="government:army";
            if(key.EndsWith(" · 安排工作",StringComparison.Ordinal))key="partner";
            return "Page_"+string.Concat(key.Select(c=>char.IsLetterOrDigit(c)||c=='_'||c=='-' ? c : '_'));
        }
    }
    public static class UiComponentExtensions
    {
        public static T GetOrAdd<T>(this GameObject obj) where T:Component => UiPrefabBinding.Ensure<T>(obj);
    }
}

using UnityEngine;
using UnityEngine.UI;
namespace BaseConquest.Presentation
{
    // The prefab does not redistribute an operating-system font binary.
    [ExecuteAlways, DisallowMultipleComponent]
    public sealed class UiSystemFont : MonoBehaviour
    {
        private static Font fallback;
        private const string FallbackName="SLG · 临时系统字体";
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        public static void ResetFallback()
        {
            if(fallback!=null){if(Application.isPlaying)Destroy(fallback);else DestroyImmediate(fallback);}
            fallback=null;
        }
        private float nextRefresh;
        private void OnEnable()=>Refresh();
        private void Update()
        {
            // Scene/prefab instance overrides can be applied after OnEnable in edit mode.
            if(Time.realtimeSinceStartup<nextRefresh)return;
            nextRefresh=Time.realtimeSinceStartup+.5f;Refresh();
        }
        public void Refresh()
        {
            if(fallback==null){fallback=Font.CreateDynamicFontFromOSFont(new[]{"Microsoft YaHei UI","Microsoft YaHei","Noto Sans CJK SC","Arial"},20);fallback.name=FallbackName;fallback.hideFlags=HideFlags.DontSave;}
            bool repaired=false;
            foreach(var label in GetComponentsInChildren<Text>(true))
            {
                // Only replace this component's own temporary font. Built-in fonts also use DontSave.
                bool missing=label.font==null||(label.font.name==FallbackName&&label.font!=fallback);
                if(missing){label.font=fallback;repaired=true;}
                if((!Application.isPlaying||missing)&&label.isActiveAndEnabled){if(label.font.dynamic)label.font.RequestCharactersInTexture(label.text,label.fontSize,label.fontStyle);label.SetAllDirty();if(label.isActiveAndEnabled)label.Rebuild(CanvasUpdate.PreRender);}
            }
            // An idle edit-mode Canvas may otherwise retain an empty font texture after reload/save.
            if(!Application.isPlaying||repaired)Canvas.ForceUpdateCanvases();
        }
    }
}

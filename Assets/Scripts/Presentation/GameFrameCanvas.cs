using UnityEngine;
using UnityEngine.UI;
namespace BaseConquest.Presentation
{
    [DefaultExecutionOrder(1000)]
    public sealed class GameFrameCanvas : MonoBehaviour
    {
        private RectTransform frame;
        public static Rect Area
        {
            get
            {
                var safe=Screen.safeArea;var r=GameFrameLayout.Fit(Mathf.Max(1,Screen.width),Mathf.Max(1,Screen.height),safe.x,safe.y,safe.width,safe.height);
                return new Rect((float)r.X,(float)r.Y,(float)r.Width,(float)r.Height);
            }
        }
        public static float Scale=>Area.width/1280;
        public static Rect Compose(Rect local)
        {
            var a=Area;return new Rect((a.x+local.x*a.width)/Mathf.Max(1,Screen.width),(a.y+local.y*a.height)/Mathf.Max(1,Screen.height),local.width*a.width/Mathf.Max(1,Screen.width),local.height*a.height/Mathf.Max(1,Screen.height));
        }
        public void Apply()
        {
            if(!Application.isPlaying)return;
            ApplyLayout(Area,Screen.width,Screen.height);
        }
        // Overlay UI must not add a second URP Base camera: it clears the map's color target.
        // The inner rectangle supplies the same 1280x720 coordinates within the safe game area.
        public void ApplyLayout(Rect area,float screenWidth,float screenHeight)
        {
            var canvas=GetComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceOverlay;canvas.worldCamera=null;
            float scale=Mathf.Max(.0001f,area.width/1280);
            var scaler=GetComponent<CanvasScaler>();if(scaler!=null){scaler.uiScaleMode=CanvasScaler.ScaleMode.ConstantPixelSize;scaler.scaleFactor=scale;}
            canvas.scaleFactor=scale;
            if(frame==null)
            {
                var existing=transform.Find("游戏显示区域 · 16比9");
                frame=existing as RectTransform;
                if(frame==null){var obj=new GameObject("游戏显示区域 · 16比9",typeof(RectTransform),typeof(RectMask2D));frame=(RectTransform)obj.transform;frame.SetParent(transform,false);}
            }
            frame.anchorMin=frame.anchorMax=frame.pivot=Vector2.one*.5f;frame.sizeDelta=new Vector2(1280,720);
            frame.localScale=Vector3.one;frame.localRotation=Quaternion.identity;
            frame.anchoredPosition=(area.center-new Vector2(screenWidth,screenHeight)*.5f)/scale;
            // New dynamic pages also join the display rectangle, retaining their authored anchors.
            for(int i=0;i<transform.childCount;){var child=transform.GetChild(i);if(child==frame){i++;continue;}child.SetParent(frame,false);}
        }
        private void LateUpdate()=>Apply();
    }
}

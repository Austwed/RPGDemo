using UnityEngine;

namespace BaseConquest.Presentation
{
    // Live list records keep their data-driven row count. Designers control their spacing here.
    [DisallowMultipleComponent]
    public sealed class UiPageLayout : MonoBehaviour
    {
        [Range(.75f,2f)] public float RowHeight=1;
        [Range(.65f,1f)] public float ContentWidth=1;
        public void Apply(RectTransform content,float originalHeight)
        {
            foreach(RectTransform child in content)
            {
                var p=child.anchoredPosition;p.x*=ContentWidth;p.y*=RowHeight;child.anchoredPosition=p;
                var size=child.sizeDelta;size.x*=ContentWidth;child.sizeDelta=size;
            }
            content.sizeDelta=new Vector2(content.sizeDelta.x,originalHeight*RowHeight);
        }
    }
}

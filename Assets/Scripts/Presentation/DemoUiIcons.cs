using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace BaseConquest.Presentation
{
    public static class DemoUiIcons
    {
        public static readonly string[] Keys={"attack","coins","equipment","food","guard","materials","quest","recruit","retreat","save","supplies","training"};
        public static Sprite Load(string key)=>Array.IndexOf(Keys,key)<0?null:Resources.Load<Sprite>("PixelDemo/Icons/icon-"+key+"-v1");
        public static Image Attach(Button button,string key)
        {
            var sprite=Load(key);if(button==null||sprite==null)return null;
            var child=button.transform.Find("DemoIcon") as RectTransform;
            if(child==null){var go=new GameObject("DemoIcon",typeof(RectTransform),typeof(Image));child=(RectTransform)go.transform;child.SetParent(button.transform,false);}
            child.anchorMin=child.anchorMax=new Vector2(0,.5f);child.pivot=Vector2.one*.5f;child.anchoredPosition=new Vector2(22,0);child.sizeDelta=new Vector2(32,32);
            var image=child.GetComponent<Image>();image.sprite=sprite;image.preserveAspect=true;image.raycastTarget=false;
            var label=button.GetComponentInChildren<Text>();if(label!=null)
            {
                var rect=label.rectTransform;
                rect.anchorMin=new Vector2(0,rect.anchorMin.y);
                rect.anchorMax=new Vector2(1,rect.anchorMax.y);
                rect.offsetMin=new Vector2(40,rect.offsetMin.y);
                rect.offsetMax=new Vector2(-4,rect.offsetMax.y);
            }
            return image;
        }
    }
    public sealed class DemoResourceIcons : MonoBehaviour
    {
        private static readonly string[] Names={"粮食","原料","钱币","军需"},Ids={"food","materials","coins","supplies"};
        private readonly List<RectTransform> icons=new List<RectTransform>();
        private readonly TextGenerator positions=new TextGenerator();
        private string previousText;private Vector2 previousSize;private int previousFontSize;
        private Text label;
        private void Awake()
        {
            label=GetComponent<Text>();
            for(int i=0;i<Ids.Length;i++)
            {
                var sprite=DemoUiIcons.Load(Ids[i]);var go=new GameObject("ResourceIcon_"+Ids[i],typeof(RectTransform),typeof(Image));var rect=(RectTransform)go.transform;rect.SetParent(transform,false);
                rect.anchorMin=rect.anchorMax=new Vector2(0,1);rect.pivot=Vector2.one*.5f;rect.sizeDelta=new Vector2(32,32);
                var image=go.GetComponent<Image>();image.sprite=sprite;image.preserveAspect=true;image.raycastTarget=false;go.SetActive(false);icons.Add(rect);
            }
        }
        private void LateUpdate()
        {
            if(label==null)return;var size=label.rectTransform.rect.size;if(previousText==label.text&&previousSize==size&&previousFontSize==label.fontSize)return;previousText=label.text;previousSize=size;previousFontSize=label.fontSize;
            var visible=System.Text.RegularExpressions.Regex.Replace(label.text??"",@"<[^>]+>","");positions.Populate(visible,label.GetGenerationSettings(size));var chars=positions.characters;
            for(int i=0;i<icons.Count;i++)
            {
                int index=visible.IndexOf(Names[i],StringComparison.Ordinal);bool show=index>=0&&index<chars.Count&&icons[i].GetComponent<Image>().sprite!=null;
                icons[i].gameObject.SetActive(show);if(show){var p=chars[index].cursorPos;var bounds=label.rectTransform.rect;icons[i].anchoredPosition=new Vector2(p.x-bounds.xMin-18,p.y-bounds.yMax-16);}
            }
        }
    }
}

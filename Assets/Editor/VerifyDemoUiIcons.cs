using System;
using System.IO;
using System.Linq;
using BaseConquest.Presentation;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace BaseConquest.Editor
{
    public static class VerifyDemoUiIcons
    {
        private static void Need(bool ok,string message){if(!ok)throw new InvalidOperationException(message);}
        public static void Run()
        {
            Need(DemoUiIcons.Keys.Length==12&&DemoUiIcons.Keys.Distinct().Count()==12,"图标语义清单错误");
            foreach(string key in DemoUiIcons.Keys)
            {
                string path="Assets/Resources/PixelDemo/Icons/icon-"+key+"-v1.png";AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceUpdate|ImportAssetOptions.ForceSynchronousImport);
                var importer=(TextureImporter)AssetImporter.GetAtPath(path);var sprite=DemoUiIcons.Load(key);
                Need(sprite!=null&&sprite.rect.width==32&&sprite.rect.height==32&&importer.filterMode==FilterMode.Point&&!importer.mipmapEnabled&&importer.textureCompression==TextureImporterCompression.Uncompressed,"图标资源或导入错误："+key);
                var root=new GameObject("Button",typeof(RectTransform),typeof(Image),typeof(Button));
                try{var icon=DemoUiIcons.Attach(root.GetComponent<Button>(),key);Need(icon!=null&&icon.sprite==sprite&&!icon.raycastTarget&&icon.rectTransform.sizeDelta==new Vector2(32,32),"图标节点错误："+key);}
                finally{UnityEngine.Object.DestroyImmediate(root);}
            }
            string code=string.Join("\n",new[]{"Assets/Scripts/Base/BaseHud.cs","Assets/Scripts/Base/BaseTownHud.cs","Assets/Scripts/Base/BaseDevelopmentHud.cs","Assets/Scripts/Battle/BattleMobileHud.cs"}.Select(File.ReadAllText));
            foreach(string key in DemoUiIcons.Keys.Except(new[]{"food","materials","coins","supplies"}))Need(code.Contains("\""+key+"\""),"图标无界面使用点："+key);
            var canvas=new GameObject("ResourceFixture",typeof(RectTransform),typeof(Canvas));
            var textObject=new GameObject("Summary",typeof(RectTransform),typeof(Text));textObject.transform.SetParent(canvas.transform,false);
            try
            {
                var text=textObject.GetComponent<Text>();text.font=Font.CreateDynamicFontFromOSFont("Microsoft YaHei",20);text.fontSize=20;text.lineSpacing=1.5f;text.alignment=TextAnchor.UpperLeft;text.supportRichText=true;
                text.rectTransform.sizeDelta=new Vector2(852,60);text.text="营地 · 第 1 天　　 粮食 100（<color=#EE7777>+1</color>）　　 原料 100（<color=#EE7777>+1</color>）　　 钱币 100（+1）\n　　军需 100（+1）   兵力：剑士 1／弓兵 1／骑兵 1";
                var icons=textObject.AddComponent<DemoResourceIcons>();typeof(DemoResourceIcons).GetMethod("Awake",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(icons,null);
                Canvas.ForceUpdateCanvases();typeof(DemoResourceIcons).GetMethod("LateUpdate",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(icons,null);
                foreach(string key in new[]{"food","materials","coins","supplies"}){var icon=textObject.transform.Find("ResourceIcon_"+key);Need(icon!=null&&icon.gameObject.activeSelf,"资源顶栏图标未显示："+key);}
                Vector2 P(string key)=>((RectTransform)textObject.transform.Find("ResourceIcon_"+key)).anchoredPosition;
                Need(P("food").x<P("materials").x&&P("materials").x<P("coins").x&&P("food").y-P("supplies").y>=32,"资源顶栏图标未按字段和行排列："+P("food")+" "+P("materials")+" "+P("coins")+" "+P("supplies"));
            }
            finally{UnityEngine.Object.DestroyImmediate(canvas);}
            Debug.Log("[VerifyDemoUiIcons] PASS");
        }
    }
}

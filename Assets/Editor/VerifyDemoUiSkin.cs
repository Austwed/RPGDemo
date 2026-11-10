using System;
using BaseConquest.Presentation;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace BaseConquest.Editor
{
    public static class VerifyDemoUiSkin
    {
        private static void Need(bool ok,string message){if(!ok)throw new InvalidOperationException(message);}
        public static void RunWoodUiChecks()
        {
            try
            {
                Run();
                VerifyDemoTrade.Run();
                VerifyChenanAndRuins.RunAll();
                UiSkinScreenshot.Run();
            }
            catch (Exception error)
            {
                Debug.LogException(error);
                EditorApplication.Exit(2);
            }
        }
        public static void Run()
        {
            foreach(string id in new[]{"button-normal","button-selected","window-frame","dialogue-frame"})
            {
                string path="Assets/Resources/PixelDemo/UI/"+id+"-v1.png";AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceUpdate|ImportAssetOptions.ForceSynchronousImport);
                var importer=(TextureImporter)AssetImporter.GetAtPath(path);var sprite=DemoUiTheme.Skin(id);
                Need(sprite!=null&&sprite.border.x>0&&sprite.border.y>0&&sprite.border.z>0&&sprite.border.w>0,"皮肤 Sprite 或九宫格边框错误："+id);
                Need(importer.filterMode==FilterMode.Point&&!importer.mipmapEnabled&&importer.npotScale==TextureImporterNPOTScale.None&&importer.textureCompression==TextureImporterCompression.Uncompressed,"皮肤导入设置错误："+id);
                Vector4 border=id=="window-frame"?new Vector4(30,30,30,30)
                    :id=="dialogue-frame"?new Vector4(30,30,30,46)
                    :new Vector4(12,10,12,10);
                Need(sprite.border==border,"九宫格边界与 UI 实施说明书 §4.1 不符："+id+" 实际 "+sprite.border);
            }
            var root=new GameObject("UiSkinFixture",typeof(RectTransform),typeof(Image));
            var button=new GameObject("Button",typeof(RectTransform),typeof(Image),typeof(Button));button.transform.SetParent(root.transform,false);
            try
            {
                DemoUiTheme.Current.StyleButton(button.GetComponent<Button>());
                var styled=button.GetComponent<Button>();var styledImage=button.GetComponent<Image>();
                Need(styled.transition==Selectable.Transition.ColorTint&&styledImage.sprite==null&&styledImage.type==Image.Type.Simple,"按钮应为扁平配色（原型 .btn）");
                Need(styled.colors.highlightedColor!=Color.white&&styledImage.GetComponent<Outline>()!=null,"按钮悬停色或描边缺失");
                var tint=button.GetComponent<DemoButtonTint>();
                Need(tint!=null&&tint is UnityEngine.EventSystems.ISelectHandler&&tint is UnityEngine.EventSystems.IDeselectHandler,"按钮缺少键盘聚焦第六态");
                var outline=button.GetComponent<Outline>();
                var rest=outline!=null?outline.effectDistance:Vector2.zero;
                ((UnityEngine.EventSystems.ISelectHandler)tint).OnSelect(new UnityEngine.EventSystems.BaseEventData(null));
                Need(outline!=null&&outline.effectDistance!=rest,"聚焦时描边未变化");
                ((UnityEngine.EventSystems.IDeselectHandler)tint).OnDeselect(new UnityEngine.EventSystems.BaseEventData(null));
                Need(outline.effectDistance==rest,"失焦后描边未还原");
                DemoUiTheme.Current.Frame((RectTransform)root.transform);
                Need(root.GetComponent<Image>().color==DemoUiTheme.Current.Panel&&root.GetComponent<Outline>()!=null,"面板应为暖纸底与深木描边");
                var theme = DemoUiTheme.Current;
                Need(theme.Panel.r > theme.Panel.g && theme.Panel.g > theme.Panel.b && theme.Ink.r < .3f && theme.Gold.r > .85f,
                    "暖木、暖纸和金色强调色板不匹配");
                Need(root.transform.Find("DemoPixelFrame")==null,"面板不再使用像素外框");
                DemoUiTheme.Current.Frame((RectTransform)root.transform,true);
                Need(root.GetComponent<Image>().color.a<DemoUiTheme.Current.Panel.a,"对白底色应比面板半透明");
            }
            finally{UnityEngine.Object.DestroyImmediate(root);}
            Debug.Log("[VerifyDemoUiSkin] PASS");
        }
    }
}

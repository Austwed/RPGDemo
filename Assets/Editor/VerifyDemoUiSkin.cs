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
                Need(button.GetComponent<Image>().type==Image.Type.Sliced&&button.GetComponent<Button>().spriteState.selectedSprite==DemoUiTheme.Skin("button-selected"),"按钮状态未接入");
                var tint=button.GetComponent<DemoButtonTint>();
                Need(tint!=null&&tint is UnityEngine.EventSystems.ISelectHandler&&tint is UnityEngine.EventSystems.IDeselectHandler,"按钮缺少键盘聚焦第六态");
                var outline=button.GetComponent<Outline>();
                var rest=outline!=null?outline.effectDistance:Vector2.zero;
                ((UnityEngine.EventSystems.ISelectHandler)tint).OnSelect(new UnityEngine.EventSystems.BaseEventData(null));
                Need(outline!=null&&outline.effectDistance!=rest,"聚焦时描边未变化");
                ((UnityEngine.EventSystems.IDeselectHandler)tint).OnDeselect(new UnityEngine.EventSystems.BaseEventData(null));
                Need(outline.effectDistance==rest,"失焦后描边未还原");
                DemoUiTheme.Current.Frame((RectTransform)root.transform);var frame=root.transform.Find("DemoPixelFrame").GetComponent<Image>();
                Need(frame.sprite==DemoUiTheme.Skin("window-frame")&&frame.type==Image.Type.Sliced&&!frame.raycastTarget,"窗口边框错误");
                DemoUiTheme.Current.Frame((RectTransform)root.transform,true);Need(frame.sprite==DemoUiTheme.Skin("dialogue-frame"),"对白边框错误");
            }
            finally{UnityEngine.Object.DestroyImmediate(root);}
            Debug.Log("[VerifyDemoUiSkin] PASS");
        }
    }
}

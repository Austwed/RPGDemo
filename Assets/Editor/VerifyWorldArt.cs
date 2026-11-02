using System;
using BaseConquest.Base;
using UnityEditor;
using UnityEngine;

namespace BaseConquest.Editor
{
    public static class VerifyWorldArt
    {
        private static void Need(bool ok,string message){if(!ok)throw new InvalidOperationException(message);}
        public static void Run()
        {
            foreach(string id in new[]{"world-background","world-home","world-camp","world-enemy-camp"})
            {
                string path="Assets/Resources/PixelDemo/World/"+id+"-v1.png";AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceUpdate|ImportAssetOptions.ForceSynchronousImport);
                var importer=(TextureImporter)AssetImporter.GetAtPath(path);Need(importer.filterMode==FilterMode.Point&&!importer.mipmapEnabled&&importer.textureCompression==TextureImporterCompression.Uncompressed,"世界地图导入设置错误："+id);
            }
            var state=new BaseWorldJourneyState {BackgroundResource=""};var before=JsonUtility.ToJson(state);
            Need(BaseWorldJourneyHud.BackgroundSprite(state.BackgroundResource)?.name=="world-background-v1"&&JsonUtility.ToJson(state)==before,"默认底图或存档字段错误");
            foreach(var pair in new[]{new[]{"camp","world-camp-v1"},new[]{"home","world-home-v1"},new[]{"bandit","world-enemy-camp-v1"}})
                Need(BaseWorldJourneyHud.NodeSprite(new BaseWorldNode {Id=pair[0]})?.name==pair[1],"世界地点图标错误："+pair[0]);
            Need(BaseWorldJourneyHud.NodeSprite(new BaseWorldNode {Id="other"})==null,"其他地点不应使用指定图标");
            Debug.Log("[VerifyWorldArt] PASS");
        }
    }
}

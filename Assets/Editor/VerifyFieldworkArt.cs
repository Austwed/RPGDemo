using System;
using System.IO;
using BaseConquest.Battle;
using UnityEditor;
using UnityEngine;

namespace BaseConquest.Editor
{
    public static class VerifyFieldworkArt
    {
        private static void Need(bool yes,string message){if(!yes)throw new InvalidOperationException(message);}
        public static void Run()
        {
            foreach(string id in new[]{"trap-bear","sandbag-row-3"})
            {
                string path="Assets/Resources/PixelDemo/Buildings/"+id+"-v1.png";
                var importer=AssetImporter.GetAtPath(path) as TextureImporter;Need(importer!=null,"缺少工事贴图："+id);
                importer.filterMode=FilterMode.Point;importer.mipmapEnabled=false;importer.textureCompression=TextureImporterCompression.Uncompressed;importer.npotScale=TextureImporterNPOTScale.None;importer.SaveAndReimport();
            }
            var board=BattleBoard.CreateDefault(12,10);board.Cell(new GridPosition(2,2)).HiddenTrap=true;
            for(int x=4;x<=6;x++)board.Cell(new GridPosition(x,4)).SandbagGroup=1;
            string before=JsonUtility.ToJson(board);var host=new GameObject("FieldworkArtVerification");var source=new Material(Shader.Find("Universal Render Pipeline/Unlit"));BattleBoardView view=null;
            try
            {
                view=new BattleBoardView(board,host.transform,source,source){FlatView=true,ObliqueBaseView=true};view.SetCombatMode(false);
                var trap=view.Root.transform.Find("FieldworkArt/Fieldwork_2_2");Need(trap!=null&&trap.gameObject.activeSelf,"和平状态陷阱未显示");
                for(int x=4;x<=6;x++){var part=view.Root.transform.Find("FieldworkArt/Fieldwork_"+x+"_4");Need(part!=null&&part.gameObject.activeSelf,"沙袋分片缺失");var uv=part.GetComponent<MeshFilter>().sharedMesh.uv;Need(Mathf.Abs(uv[0].x-(x-4)/3f)<.0001f&&Mathf.Abs(uv[2].x-(x-3)/3f)<.0001f,"沙袋纹理分片错误");}
                view.SetCombatMode(true);Need(!trap.gameObject.activeSelf,"战斗中陷阱不应显示");Need(view.Root.transform.Find("FieldworkArt/Fieldwork_4_4").gameObject.activeSelf,"战斗中沙袋应显示");
                board.Cell(new GridPosition(2,2)).HiddenTrap=false;view.SetCombatMode(false);Need(!trap.gameObject.activeSelf,"消耗后陷阱仍显示");board.Cell(new GridPosition(2,2)).HiddenTrap=true;
                for(int x=4;x<=6;x++)board.Cell(new GridPosition(x,4)).SandbagGroup=0;view.Refresh();Need(!view.Root.transform.Find("FieldworkArt/Fieldwork_4_4").gameObject.activeSelf,"拆除后沙袋仍显示");
                for(int x=4;x<=6;x++)board.Cell(new GridPosition(x,4)).SandbagGroup=1;Need(JsonUtility.ToJson(board)==before,"表现改变了棋盘数据");
                Directory.CreateDirectory("Logs");File.WriteAllText("Logs/fieldwork-art-verification.txt","PASS: point/no mip, trap peace/combat/consumed, three-segment sandbag, combat, demolition, board unchanged.\n");Debug.Log("[VerifyFieldworkArt] PASS");
            }
            finally{view?.Dispose();UnityEngine.Object.DestroyImmediate(source);UnityEngine.Object.DestroyImmediate(host);}
        }
    }
}

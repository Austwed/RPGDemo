using System;
using System.IO;
using System.Linq;
using BaseConquest.Base;
using BaseConquest.Battle;
using BaseConquest.Presentation;
using UnityEditor;
using UnityEngine;

namespace BaseConquest.Editor
{
    public static class VerifyFieldworkArt
    {
        private static void Need(bool yes,string message){if(!yes)throw new InvalidOperationException(message);}
        public static void RunInn()
        {
            var asset=Resources.Load<TextAsset>("DemoStages/camp-male");Need(asset!=null,"缺少营地阶段档");
            var state=JsonUtility.FromJson<BaseState>(asset.text);
            var marker=BaseInn.Markers(state,"town").Single();
            Need(marker.Id=="town_inn"&&marker.AssetId=="town_inn"&&marker.Kind=="facility"&&marker.Blocks&&marker.Width==1&&marker.Height==1,"城镇旅店标记或交互属性不符");
            var catalog=Resources.Load<BuildingAppearanceCatalog>("BuildingAppearances");
            Need(catalog?.Find(marker.AssetId)?.Texture==PixelDemoArt.Building("town_inn")&&PixelDemoArt.Building("town_inn")!=null,"旅店专用贴图无法通过运行目录加载");
            Directory.CreateDirectory("Logs");File.WriteAllText("Logs/inn-art-verification.txt","PASS: town inn marker, interaction dimensions, dedicated catalog texture.\n");
            Debug.Log("[VerifyInnArt] PASS");
        }
        public static void RunFarm()
        {
            var mature=PixelDemoArt.Building("farm-mature");var immature=PixelDemoArt.Building("farm-immature");
            Need(mature!=null&&immature!=null&&mature!=immature,"粮田 A/B 资源缺失或重复");
            var host=new GameObject("FarmArtVerification");var source=new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            try
            {
                var placement=new BaseMapObject("farm","farm",3,3,3,2,true,"farm","farm","facility");
                var view=AuthoredMapView.Create(host.transform,placement,Vector3.zero,source,true);
                var renderer=view.GetComponentInChildren<Renderer>();
                Need(renderer!=null&&renderer.sharedMaterial.GetTexture("_BaseMap")==mature,"粮田未使用成熟状态贴图");
                Need(renderer.sortingOrder< -30000,"粮田未处于直立角色和物件下方");
                view.SetExploration(false);view.SetExploration(true);
                Need(renderer.sortingOrder< -30000,"视图切换改变了粮田排序");
                Directory.CreateDirectory("Logs");File.WriteAllText("Logs/farm-art-verification.txt","PASS: distinct A/B textures, default mature texture, ground sorting across view switches.\n");
                Debug.Log("[VerifyFarmArt] PASS");
            }
            finally{UnityEngine.Object.DestroyImmediate(host);UnityEngine.Object.DestroyImmediate(source);}
        }
        public static void Run()
        {
            foreach(string id in new[]{"trap-bear","sandbag-row-3"})
            {
                string path="Assets/Resources/PixelDemo/Buildings/"+id+"-v1.png";
                var importer=AssetImporter.GetAtPath(path) as TextureImporter;Need(importer!=null,"缺少工事贴图："+id);
                Need(importer.filterMode==FilterMode.Point&&!importer.mipmapEnabled&&importer.textureCompression==TextureImporterCompression.Uncompressed&&importer.npotScale==TextureImporterNPOTScale.None,"工事贴图导入设置不符合规格："+id);
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
                var bag=view.Root.transform.Find("FieldworkArt/Fieldwork_4_4").GetComponent<Renderer>();
                var legacy=new BattleSimulation(new BattleScenario{Board=board,FogEnabled=true,HeightVision=false});
                view.RefreshVision(legacy.Vision,BattleSide.A,false);Need(!bag.enabled,"未发现区域的沙袋不应透出迷雾");
                view.RefreshVision(legacy.Vision,BattleSide.A,false,true);Need(bag.enabled,"显示全部地图时沙袋应显示");
                var heightVision=new BattleVision(board);view.RefreshVision(heightVision,BattleSide.A,false);Need(!bag.enabled,"高度迷雾下未知沙袋不应显示");
                var known=new bool[board.Width*board.Height];known[4*board.Width+4]=true;heightVision.RememberTerrain(BattleSide.A,known);
                view.RefreshVision(heightVision,BattleSide.A,false);Need(bag.enabled,"已知地形的沙袋应受迷雾正常覆盖");
                board.Cell(new GridPosition(2,2)).HiddenTrap=false;view.SetCombatMode(false);Need(!trap.gameObject.activeSelf,"消耗后陷阱仍显示");board.Cell(new GridPosition(2,2)).HiddenTrap=true;
                Need(bag.enabled,"退出战斗后工事显示没有恢复");
                for(int x=4;x<=6;x++)board.Cell(new GridPosition(x,4)).SandbagGroup=0;view.Refresh();Need(!view.Root.transform.Find("FieldworkArt/Fieldwork_4_4").gameObject.activeSelf,"拆除后沙袋仍显示");
                for(int x=4;x<=6;x++)board.Cell(new GridPosition(x,4)).SandbagGroup=1;Need(JsonUtility.ToJson(board)==before,"表现改变了棋盘数据");
                Directory.CreateDirectory("Logs");File.WriteAllText("Logs/fieldwork-art-verification.txt","PASS: point/no mip, trap peace/combat/consumed, three-segment sandbag, fog visibility, combat, demolition, board unchanged.\n");Debug.Log("[VerifyFieldworkArt] PASS");
            }
            finally{view?.Dispose();UnityEngine.Object.DestroyImmediate(source);UnityEngine.Object.DestroyImmediate(host);}
        }
    }
}

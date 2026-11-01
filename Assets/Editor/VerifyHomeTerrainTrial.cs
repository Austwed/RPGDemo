using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using BaseConquest.Battle;
using UnityEditor;
using UnityEngine;

namespace BaseConquest.Editor
{
    /// 故乡地形候选只是「换贴图」，不许碰棋盘数据、相机、缩放和任何物体变换。
    /// 这里把开关来回扳一次，逐项证明。
    public static class VerifyHomeTerrainTrial
    {
        private static void Need(bool yes,string reason){if(!yes)throw new InvalidOperationException(reason);}
        private static object Private(object target,string field)=>target.GetType().GetField(field,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(target);
        private static string Fix(Vector3 v)=>"("+v.x.ToString("0.###")+","+v.y.ToString("0.###")+","+v.z.ToString("0.###")+")";
        private static readonly string[] FieldNames={"baseGrassMaterial","baseRaisedGrassMaterial","baseRaisedRoadMaterial","baseCliffMaterial","baseStairMaterial","waterMaterial","wallMaterial","swampMaterial","buildingMaterial"};
        private static IEnumerable<KeyValuePair<string,Material>> Skins(BattleBoardView boardView)
        {
            foreach(string field in FieldNames)
            {
                var skin=(Material)Private(boardView,field);
                if(skin!=null)yield return new KeyValuePair<string,Material>(field,skin);
            }
            if(Private(boardView,"surfaceMaterials")is IDictionary map)
                foreach(DictionaryEntry entry in map)
                    if(entry.Value is Material skin)yield return new KeyValuePair<string,Material>("surfaceMaterials["+entry.Key+"]",skin);
        }
        private static string Materials(BattleBoardView boardView)
        {
            var report=new StringBuilder();
            foreach(var pair in Skins(boardView))
            {
                var texture=pair.Value.GetTexture("_BaseMap");var scale=pair.Value.GetTextureScale("_BaseMap");var offset=pair.Value.GetTextureOffset("_BaseMap");
                report.Append(pair.Key).Append(" tex=").Append(texture==null?"<none>":texture.name).Append('#').Append(texture==null?"-":texture.GetEntityId().ToString());
                report.Append(" scale=").Append(scale.x.ToString("0.###")).Append(',').Append(scale.y.ToString("0.###"));
                report.Append(" offset=").Append(offset.x.ToString("0.###")).Append(',').Append(offset.y.ToString("0.###")).Append('\n');
            }
            return report.ToString();
        }
        private static string Row(string snapshot,string name)=>snapshot.Split('\n').FirstOrDefault(line=>line.StartsWith(name+" ",StringComparison.Ordinal))??"<missing:"+name+">";
        private static string Transforms(Transform root)
        {
            var report=new StringBuilder();
            foreach(var node in root.GetComponentsInChildren<Transform>(true))
                report.Append(node.name).Append(" p=").Append(Fix(node.localPosition)).Append(" r=").Append(Fix(node.localEulerAngles))
                    .Append(" s=").Append(Fix(node.localScale)).Append(" on=").Append(node.gameObject.activeSelf).Append('\n');
            return report.ToString();
        }
        private static string Board(BattleBoard board)
        {
            var report=new StringBuilder();
            foreach(var p in board.Positions())
            {var c=board.Cell(p);report.Append(p.X).Append(',').Append(p.Z).Append(':').Append((int)c.Terrain).Append(',').Append((int)c.Surface).Append(',').Append(c.Elevation).Append(',').Append((int)c.Ramp).Append(';');}
            return report.ToString();
        }
        /// 同机位各拍一张离屏图，供对比观感；batchmode 无图形设备时如实报告，不谎称成功。
        private static string Shots(BattleBoardView boardView)
        {
            Directory.CreateDirectory("Logs/home-terrain-trial");var notes=new StringBuilder();
            foreach(string tag in new[]{"original","trial"})
            {
                boardView.SetHomeTerrainTrial(tag=="trial");
                var lens=boardView.Camera;int width=960,height=600;
                RenderTexture surface=null;Texture2D shot=null;
                try
                {
                    surface=new RenderTexture(width,height,24);lens.targetTexture=surface;lens.Render();
                    // 有没有真的渲染出来，只能问 RenderTexture 自己；数像素亮度会把未初始化的显存读成有效画面。
                    if(!surface.IsCreated())throw new InvalidOperationException("RenderTexture.Create failed：本次环境没有可用的图形设备");
                    RenderTexture.active=surface;shot=new Texture2D(width,height,TextureFormat.RGB24,false);
                    shot.ReadPixels(new Rect(0,0,width,height),0,0);shot.Apply();
                    if(!shot.isReadable)throw new InvalidOperationException("离屏像素不可读");
                    string path="Logs/home-terrain-trial/view-"+tag+".png";File.WriteAllBytes(path,shot.EncodeToPNG());
                    var pixels=shot.GetPixels32();long bright=0;foreach(var pixel in pixels)if(pixel.r+pixel.g+pixel.b>24)bright++;
                    notes.Append(tag+" shot OK: "+width+"x"+height+", 非黑像素 "+bright+"/"+pixels.Length+"\n");
                }
                catch(Exception error)
                {
                    notes.Append(tag+" shot 未产出: "+error.Message+"\n");
                    string stale="Logs/home-terrain-trial/view-"+tag+".png";if(File.Exists(stale))File.Delete(stale);
                }
                finally
                {
                    RenderTexture.active=null;lens.targetTexture=null;
                    if(surface!=null)UnityEngine.Object.DestroyImmediate(surface);
                    if(shot!=null)UnityEngine.Object.DestroyImmediate(shot);
                }
            }
            boardView.SetHomeTerrainTrial(false);return notes.ToString();
        }
        private static string Camera(Camera lens)=>"pos="+Fix(lens.transform.position)+" rot="+Fix(lens.transform.eulerAngles)+" ortho="+lens.orthographicSize.ToString("0.###");
        [MenuItem("SLG/其他工具/验证/故乡地形试点不变性")]
        public static void Run()
        {
            Need(BaseConquest.Presentation.HomeTerrainTrial.Texture(GroundSurface.Grass)!=null&&BaseConquest.Presentation.HomeTerrainTrial.Texture(GroundSurface.DirtPath)!=null,"试点贴图缺失，先跑「生成/故乡地形64像素候选」");
            bool restore=BaseConquest.Presentation.HomeTerrainTrial.Enabled;
            var report=new StringBuilder();GameObject host=null;Material skinSource=null;BattleBoardView boardView=null;
            try
            {
                BaseConquest.Presentation.HomeTerrainTrial.Enabled=false;
                host=new GameObject("VerifyHomeTerrainTrial"){hideFlags=HideFlags.HideAndDontSave};
                skinSource=new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                var probe=BattleBattlefieldProbe.Create();var slope=new GridPosition(8,6);
                Need(Math.Abs(probe.SurfaceLevel(slope,0,.5))<1e-6&&Math.Abs(probe.SurfaceLevel(slope,1,.5)-1)<1e-6,"东西坡道夹具未从H0连接至H1");
                boardView=new BattleBoardView(probe,host.transform,skinSource,skinSource){FlatView=true,ObliqueBaseView=true};
                boardView.SetCombatMode(false);boardView.UpdateCamera(0,0);boardView.Zoom(.65f);
                var pawn=new GameObject("ProbePawn"){hideFlags=HideFlags.HideAndDontSave};pawn.transform.SetParent(boardView.Root.transform,false);pawn.transform.localPosition=new Vector3(1.5f,.25f,-2.5f);
                boardView.Refresh();
                string layout=Transforms(boardView.Root.transform),skins=Materials(boardView),board=Board(boardView.Board),camera=Camera(boardView.Camera),zoom=((float)Private(boardView,"zoomFactor")).ToString("0.###");
                Need(boardView.SetHomeTerrainTrial(true),"候选贴图已生成却开启失败");
                string trialSkins=Materials(boardView);
                Need(trialSkins!=skins,"开启试点后没有任何材质变化，贴图没接上");
                Need(Board(boardView.Board)==board,"开启试点改动了棋盘数据");
                Need(Camera(boardView.Camera)==camera&&((float)Private(boardView,"zoomFactor")).ToString("0.###")==zoom,"开启试点改动了相机或缩放");
                Need(Transforms(boardView.Root.transform)==layout,"开启试点改动了场景变换（含人物）");
                Need(boardView.SetHomeTerrainTrial(false),"关闭试点失败");
                Need(Materials(boardView)==skins,"关闭试点后材质没有完全恢复到原版");
                Need(Board(boardView.Board)==board,"关闭试点改动了棋盘数据");
                Need(Camera(boardView.Camera)==camera&&((float)Private(boardView,"zoomFactor")).ToString("0.###")==zoom,"关闭试点改动了相机或缩放");
                Need(Transforms(boardView.Root.transform)==layout,"关闭试点改动了场景变换（含人物）");
                string Again(bool on)
                {
                    boardView.SetHomeTerrainTrial(on);return Materials(boardView);
                }
                Need(Again(true)==trialSkins&&Again(false)==skins,"反复切换后与原版/候选状态不一致");
                report.AppendLine("PASS board JSON, camera, zoom, scene transforms unchanged across trail on/off/on/off.");
                report.AppendLine("PASS material set fully restored to pre-trial textures, scales and offsets.");
                var sides=new[]{"wallMaterial","waterMaterial","swampMaterial"};
                var changed=new List<string>();var constant=new List<string>();
                foreach(var pair in Skins(boardView))
                {
                    string before=Row(skins,pair.Key),trial=Row(trialSkins,pair.Key);
                    if(string.Equals(before,trial,StringComparison.Ordinal))constant.Add(pair.Key);else changed.Add(pair.Key);
                }
                Need(Row(trialSkins,"baseGrassMaterial").Contains("grass-64-v2"),"试点开启后草地应使用 Cainos grass-64-v2");
                Need(Row(trialSkins,"baseCliffMaterial").Contains("cliff-64-v1"),"试点开启后崖壁侧面应使用 cliff-64-v1");
                Need(Row(trialSkins,"baseRaisedRoadMaterial").Contains("path-64-v1"),"试点开启后路面应使用 Cainos path-64-v1");
                Need(Row(trialSkins,"baseStairMaterial").Contains("stair-64-v1"),"试点开启后阶梯应使用 Cainos stair-64-v1");
                report.AppendLine("跟随试点换图: "+(changed.Count==0?"（无）":string.Join(", ",changed)));
                report.AppendLine("全程不动: "+(constant.Count==0?"（无）":string.Join(", ",constant)));
                foreach(string name in sides)
                {
                    string before=Row(skins,name);
                    if(before.StartsWith("<missing:",StringComparison.Ordinal))continue;
                    Need(before==Row(trialSkins,name),name+" 在试点开启期间发生变化，需要与美术一并确认");
                }
                report.AppendLine("全程不动: wall（构造期即换 Cainos 砖，与开关无关）・water・swamp；cliff/stair/路/草地随试点切换：确认");
                Directory.CreateDirectory("Logs/home-terrain-trial");report.AppendLine(Shots(boardView));
                File.WriteAllText("Logs/home-terrain-trial/invariance.txt",report.ToString());
                Debug.Log("[VerifyHomeTerrainTrial] PASS");
            }
                finally
            {
                BaseConquest.Presentation.HomeTerrainTrial.Enabled=restore;
                boardView?.Dispose();
                if(skinSource!=null)UnityEngine.Object.DestroyImmediate(skinSource);
                if(host!=null)UnityEngine.Object.DestroyImmediate(host);
            }
        }
    }
    internal static class BattleBattlefieldProbe
    {
        // 一格三用：平地草地、抬升（出侧面）、坡道与泥径，保证参与和不参与试点的材质都被建出来。
        public static BattleBoard Create()
        {
            var board=BattleBoard.CreateDefault(12,10);
            foreach(var p in board.Positions())
            {
                var cell=board.Cell(p);cell.Terrain=TerrainKind.Ground;cell.BuildingId="";
                if(p.Z>=6)cell.Surface=GroundSurface.DirtPath;
                else if(p.X>=4&&p.X<=7)cell.Surface=GroundSurface.Road;
                else cell.Surface=GroundSurface.Grass;
                if(p.X>=9&&p.Z>=6)cell.Elevation=1;
                // Ramp stores the high-end level: west H0 -> ramp H0..H1 -> east H1.
                if(p.X==8&&p.Z>=6){cell.Elevation=1;cell.Ramp=RampAxis.EastWest;cell.Surface=GroundSurface.Road;}
            }
            board.EnsureValid();return board;
        }
    }
}

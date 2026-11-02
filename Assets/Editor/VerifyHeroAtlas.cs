using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using BaseConquest.Battle;
using BaseConquest.Presentation;

namespace BaseConquest.EditorTools
{
    // batchmode：-executeMethod BaseConquest.EditorTools.VerifyHeroAtlas.Run
    // 两种用法：
    //  ① HERO_ATLAS_DIR + HERO_ATLAS_ID：把 资源素材 里打好的主角图集以临时 id 拷进 Resources 验证，最后删掉临时文件；
    //  ② 只设 HERO_ATLAS_ID（可逗号分隔，如 player,female）：直接验证 Resources 里已入库的主角图集。
    // 检查：导入设置、spec 读取、格子／地线、「屏幕身高与普通角色一致」。普通角色基准用阿禾（ahe，56px 普通规格）。
    public static class VerifyHeroAtlas
    {
        const string TempId="zz-verify-hero";
        const string BaseId="ahe";
        public static void Run()
        {
            string source=System.Environment.GetEnvironmentVariable("HERO_ATLAS_DIR");string character=System.Environment.GetEnvironmentVariable("HERO_ATLAS_ID");
            var made=new System.Collections.Generic.List<string>();int failures=0;
            void Need(bool ok,string message){if(!ok){failures++;Debug.LogError("[VerifyHeroAtlas] FAIL "+message);}else Debug.Log("[VerifyHeroAtlas] ok "+message);}
            var ids=new System.Collections.Generic.List<string>();
            try
            {
                if(!string.IsNullOrEmpty(source))
                {
                    foreach(var motion in new[]{"walk","attack","idle"})
                    {
                        string png=Path.Combine(source,character+"-"+motion+".png"),spec=Path.Combine(source,character+"-"+motion+".spec.json");
                        if(!File.Exists(png))continue;
                        string dst=PixelDemoCharacterImport.Folder+TempId+"-"+motion+"-v2";
                        File.Copy(png,dst+".png",true);File.Copy(spec,dst+".spec.json",true);made.Add(dst+".png");made.Add(dst+".spec.json");
                    }
                    AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);ids.Add(TempId);
                }
                else ids.AddRange((character??"").Split(',').Select(s=>s.Trim()).Where(s=>s.Length>0));
                Need(ids.Count>0,"待验证 id："+string.Join(",",ids));
                var shader=Shader.Find("Universal Render Pipeline/Unlit")??Shader.Find("Unlit/Transparent");var source2=new Material(shader);
                var parent=new GameObject("VerifyHeroAtlas").transform;
                foreach(var id in ids)
                foreach(var motion in new[]{"walk","attack","idle"})
                {
                    var texture=PixelDemoArt.Motion(id,motion);if(texture==null)continue;
                    var importer=(TextureImporter)AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(texture));
                    var spec=PixelDemoArt.Spec(id,motion);
                    string tag=id+" "+motion;
                    Need(spec!=null&&spec.cell>0&&spec.body>0,tag+" spec 读取");if(spec==null)continue;
                    int columns=motion=="idle"?texture.width/spec.cell:8;
                    Need(texture.width==spec.cell*columns&&texture.height==spec.cell*4,tag+" 导入尺寸 "+texture.width+"x"+texture.height+" = "+columns+"x4 格 "+spec.cell);
                    Need(importer.filterMode==FilterMode.Point&&!importer.mipmapEnabled&&importer.npotScale==TextureImporterNPOTScale.None&&importer.textureCompression==TextureImporterCompression.Uncompressed,tag+" 点采样／无 mipmap／不缩放／不压缩");
                    string baseMotion=motion=="idle"?"walk":motion;
                    var normal=PixelDemoArt.Motion(BaseId,baseMotion);
                    var hero=new PixelCharacterSheet(parent,source2,PixelDemoArt.Load("Characters",id==TempId?"player":id),null,id);
                    var baseSheet=new PixelCharacterSheet(parent,source2,PixelDemoArt.Load("Characters",BaseId),null,BaseId);
                    var pose=motion=="walk"?HeroPose.Walk:motion=="attack"?HeroPose.Attack:HeroPose.Idle;var basePose=motion=="attack"?HeroPose.Attack:HeroPose.Walk;
                    hero.Set(HeroFacing.South,pose,.1,false,false,false);baseSheet.Set(HeroFacing.South,basePose,.1,false,false,false);
                    // 世界空间中每个贴图像素的边长 × 角色像素身高 = 屏幕身高；主角 72px 与普通 56px 应得到同一身高
                    float heroTexel=hero.Mesh.bounds.size.x/spec.cell*hero.Root.transform.localScale.x;
                    float baseCell=normal!=null?normal.height/4f:64f;float baseTexel=baseSheet.Mesh.bounds.size.x/baseCell*baseSheet.Root.transform.localScale.x;
                    float heroHeight=heroTexel*spec.body,baseHeight=baseTexel*PixelDemoArt.ArtHeightOf(BaseId,baseMotion);
                    Need(Mathf.Abs(heroHeight-baseHeight)<1e-4f,tag+" 屏幕身高一致 hero="+heroHeight.ToString("F4")+" normal("+BaseId+")="+baseHeight.ToString("F4")+"，像素密度 ×"+(baseTexel/heroTexel).ToString("F3"));
                    // 地线：网格顶边 z = 地线 × 像素边长，即地线以下的格子部分在 z<0
                    float top=hero.Mesh.vertices.Max(v=>v.z)*hero.Root.transform.localScale.x;float expectTop=spec.foot*heroTexel;
                    Need(Mathf.Abs(top-expectTop)<1e-4f,tag+" 地线 "+spec.foot+" 对齐脚点");
                    hero.Dispose();baseSheet.Dispose();
                }
                Object.DestroyImmediate(parent.gameObject);
            }
            finally
            {
                foreach(var p in made){AssetDatabase.DeleteAsset(p);}
                AssetDatabase.Refresh();
            }
            Debug.Log("[VerifyHeroAtlas] done failures="+failures);
            EditorApplication.Exit(failures==0?0:1);
        }
    }
}

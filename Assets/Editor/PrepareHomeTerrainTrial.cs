using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace BaseConquest.Editor
{
    public static class PrepareHomeTerrainTrial
    {
        [MenuItem("SLG/其他工具/生成/故乡地形64像素候选")]
        public static void Run()
        {
            const string output="Assets/Resources/HomeTerrainTrial";Directory.CreateDirectory(output);
            var report=new StringBuilder("GPT candidate import; area-average normalization only. Visual approval pending.\n");
            foreach(var pair in new[]{(Source:"草地_GPT原图.png",Id:"grass"),(Source:"泥径_GPT原图.png",Id:"dirt")})
            {
                var source=new Texture2D(2,2,TextureFormat.RGBA32,false);
                var normalized=new Texture2D(64,64,TextureFormat.RGBA32,false);
                try
                {
                    if(!source.LoadImage(File.ReadAllBytes(Path.Combine("..","资源素材","地形样板","故乡试点_2026-11-01",pair.Source))))throw new InvalidOperationException("无法加载GPT原图");
                    var pixels=source.GetPixels32();var target=new Color32[4096];
                    for(int y=0;y<64;y++)for(int x=0;x<64;x++)
                    {
                        long r=0,g=0,b=0;int count=0;
                        for(int sy=y*source.height/64;sy<(y+1)*source.height/64;sy++)for(int sx=x*source.width/64;sx<(x+1)*source.width/64;sx++)
                        {var p=pixels[sy*source.width+sx];r+=p.r;g+=p.g;b+=p.b;count++;}
                        target[y*64+x]=new Color32((byte)(r/count),(byte)(g/count),(byte)(b/count),255);
                    }
                    normalized.SetPixels32(target);normalized.Apply();string path=output+"/"+pair.Id+"-64-v1.png";
                    File.WriteAllBytes(path,normalized.EncodeToPNG());AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);
                    var importer=(TextureImporter)AssetImporter.GetAtPath(path);importer.filterMode=FilterMode.Point;importer.wrapMode=TextureWrapMode.Repeat;importer.mipmapEnabled=false;importer.textureCompression=TextureImporterCompression.Uncompressed;importer.isReadable=true;importer.npotScale=TextureImporterNPOTScale.None;importer.SaveAndReimport();
                    double Difference(Color32 a,Color32 b)=> (Math.Abs(a.r-b.r)+Math.Abs(a.g-b.g)+Math.Abs(a.b-b.b))/3d;
                    double interior=0,horizontal=0,vertical=0;
                    for(int n=0;n<64;n++){horizontal+=Difference(target[n*64],target[n*64+63]);vertical+=Difference(target[n],target[63*64+n]);}
                    for(int y=0;y<64;y++)for(int x=0;x<63;x++)interior+=Difference(target[y*64+x],target[y*64+x+1]);
                    interior/=64*63;horizontal/=64;vertical/=64;
                    report.AppendLine(pair.Id+": 64x64, point/repeat/no mip; mean RGB difference interior="+interior.ToString("F3")+", horizontal seam="+horizontal.ToString("F3")+", vertical seam="+vertical.ToString("F3"));
                }
                finally{UnityEngine.Object.DestroyImmediate(source);UnityEngine.Object.DestroyImmediate(normalized);}
            }
            Directory.CreateDirectory("Logs/home-terrain-trial");File.WriteAllText("Logs/home-terrain-trial/import-report.txt",report.ToString());
        }
    }
}

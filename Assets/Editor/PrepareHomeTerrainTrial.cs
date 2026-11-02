using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
                    if(!source.LoadImage(File.ReadAllBytes(Path.Combine("..","资源素材","现役","场景","素材_地形-故乡试点-2026-11-01",pair.Source))))throw new InvalidOperationException("无法加载GPT原图");
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
                    report.AppendLine(Tiling.Seam(target,pair.Id));
                    Tiling.WriteTiles(target,pair.Id);
                }
                finally{UnityEngine.Object.DestroyImmediate(source);UnityEngine.Object.DestroyImmediate(normalized);}
            }
            Directory.CreateDirectory("Logs/home-terrain-trial");File.WriteAllText("Logs/home-terrain-trial/import-report.txt",report.ToString());
        }
    }
    /// 重复拼接检查：平铺后能看见问题的不是“平均差多少”，而是“接边这一对像素是否比画面里绝大多数相邻像素都突兀”。
    internal static class Tiling
    {
        private const int Size=64;
        private static double Diff(Color32 a,Color32 b)=>(Math.Abs(a.r-b.r)+Math.Abs(a.g-b.g)+Math.Abs(a.b-b.b))/3d;
        private static double[] Percentiles(List<double> sample,List<double> population)
        {
            var sorted=population.OrderBy(v=>v).ToArray();
            return sample.Select(v=>
            {
                int below=0;foreach(var s in sorted){if(s<v)below++;else break;}
                return below*100d/sorted.Length;
            }).ToArray();
        }
        public static string Seam(Color32[] tile,string id)
        {
            double interior=0,horizontal=0,vertical=0;
            for(int n=0;n<Size;n++){horizontal+=Diff(tile[n*Size],tile[n*Size+Size-1]);vertical+=Diff(tile[n],tile[(Size-1)*Size+n]);}
            for(int y=0;y<Size;y++)for(int x=0;x<Size-1;x++)interior+=Diff(tile[y*Size+x],tile[y*Size+x+1]);
            interior/=Size*(Size-1);horizontal/=Size;vertical/=Size;
            // 平铺时的四条接边界：(63,y)|(0,y)、(x,63)|(x,0)，再加四个瓦片交点的对角相接。
            var seams=new List<double>();
            for(int y=0;y<Size;y++)seams.Add(Diff(tile[y*Size+Size-1],tile[y*Size]));
            for(int x=0;x<Size;x++)seams.Add(Diff(tile[(Size-1)*Size+x],tile[x]));
            seams.Add(Diff(tile[(Size-1)*Size+Size-1],tile[0]));
            seams.Add(Diff(tile[(Size-1)*Size],tile[Size-1]));
            // 常模 = 画面内部所有相邻像素对，不含跨接边界那一对。
            var population=new List<double>();
            for(int y=0;y<Size;y++)for(int x=0;x<Size-1;x++)population.Add(Diff(tile[y*Size+x],tile[y*Size+x+1]));
            for(int x=0;x<Size;x++)for(int y=0;y<Size-1;y++)population.Add(Diff(tile[y*Size+x],tile[(y+1)*Size+x]));
            var sorted=population.OrderBy(v=>v).ToArray();
            var pct=Percentiles(seams,population);
            double p95=sorted[(int)(sorted.Length*.95)],p99=sorted[(int)(sorted.Length*.99)];
            int over=sorted.Count(v=>v>p95),seamOver=seams.Count(v=>v>p95);
            return id+": 64x64, point/repeat/no mip; mean RGB difference interior="+interior.ToString("F3")
                +", horizontal seam="+horizontal.ToString("F3")+", vertical seam="+vertical.ToString("F3")
                +"\n  "+id+" tiling: seam mean="+seams.Average().ToString("F3")+" max="+seams.Max().ToString("F3")
                +" | population mean="+population.Average().ToString("F3")+" P95="+p95.ToString("F3")+" P99="+p99.ToString("F3")
                +"\n  "+id+" tiling: seam percentile avg="+pct.Average().ToString("F1")+"% max="+pct.Max().ToString("F1")
                +"% | seam above P95 = "+seamOver+"/"+seams.Count+" (population itself "+over+"/"+sorted.Length+")";
        }
        /// 直接产出平铺实物图，数字之外让肉眼也能判：4x4 平铺后是否看得见规律网格。
        public static void WriteTiles(Color32[] tile,string id)
        {
            foreach(int factor in new[]{2,4})
            {
                int size=Size*factor;var texture=new Texture2D(size,size,TextureFormat.RGBA32,false);
                for(int ty=0;ty<factor;ty++)for(int tx=0;tx<factor;tx++)for(int y=0;y<Size;y++)for(int x=0;x<Size;x++)
                    texture.SetPixel(tx*Size+x,ty*Size+y,tile[y*Size+x]);
                texture.Apply();string path="Logs/home-terrain-trial/"+id+"-tiled-"+factor+"x"+factor+".png";
                File.WriteAllBytes(path,texture.EncodeToPNG());UnityEngine.Object.DestroyImmediate(texture);
            }
        }
    }
}

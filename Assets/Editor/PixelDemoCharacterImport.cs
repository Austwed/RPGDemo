using System.IO;
using UnityEditor;
using UnityEngine;

namespace BaseConquest.EditorTools
{
    // 角色像素图集放进 Assets/Resources/PixelDemo/Characters/ 即按像素规格导入：点采样、关 mipmap、不压缩、
    // 不做 NPOT 缩放（WrapMode、alphaIsTransparency 不动，沿用各图集原值）。主角规格图集（如 8×格子宽度超过 1024）会被旧 meta 里的 maxTextureSize 压小，这里按图片实际尺寸抬高上限。
    // 只在设置不符时改，已符合的旧图集重导入时 meta 不变。图集不切 Sprite：Texture 类型，由 PixelCharacterSheet 按 4 行方格算 UV。
    public sealed class PixelDemoCharacterImport : AssetPostprocessor
    {
        public const string Folder="Assets/Resources/PixelDemo/Characters/";
        private void OnPreprocessTexture()
        {
            if(!assetPath.Replace('\\','/').StartsWith(Folder)||!assetPath.EndsWith(".png"))return;
            var importer=(TextureImporter)assetImporter;
            if(importer.textureType!=TextureImporterType.Default)importer.textureType=TextureImporterType.Default;
            if(importer.filterMode!=FilterMode.Point)importer.filterMode=FilterMode.Point;
            if(importer.mipmapEnabled)importer.mipmapEnabled=false;
            if(importer.npotScale!=TextureImporterNPOTScale.None)importer.npotScale=TextureImporterNPOTScale.None;
            if(importer.textureCompression!=TextureImporterCompression.Uncompressed)importer.textureCompression=TextureImporterCompression.Uncompressed;
            int side=LargestSide(assetPath);
            if(side>importer.maxTextureSize){int max=2048;while(max<side)max*=2;importer.maxTextureSize=max;}
            var platform=importer.GetDefaultPlatformTextureSettings();
            if(side>platform.maxTextureSize){int max=2048;while(max<side)max*=2;platform.maxTextureSize=max;importer.SetPlatformTextureSettings(platform);}
        }
        // PNG 头里直接读宽高（IHDR 固定在第 16～23 字节），避免为了量尺寸先导入一次。
        public static int LargestSide(string path)
        {
            try
            {
                using(var stream=File.OpenRead(path)){var head=new byte[24];if(stream.Read(head,0,24)<24)return 0;
                int w=head[16]<<24|head[17]<<16|head[18]<<8|head[19],h=head[20]<<24|head[21]<<16|head[22]<<8|head[23];return Mathf.Max(w,h);}
            }
            catch(IOException){return 0;}
        }
    }
}
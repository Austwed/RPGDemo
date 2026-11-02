using System;
using UnityEditor;
using UnityEngine;

namespace BaseConquest.Editor
{
    public sealed class DemoUiSpriteImport : AssetPostprocessor
    {
        private void OnPreprocessTexture()
        {
            string path=assetPath.Replace('\\','/');
            if(!path.StartsWith("Assets/Resources/PixelDemo/UI/",StringComparison.Ordinal)||!path.EndsWith(".png",StringComparison.OrdinalIgnoreCase))return;
            var importer=(TextureImporter)assetImporter;
            importer.textureType=TextureImporterType.Sprite;importer.spriteImportMode=SpriteImportMode.Single;
            importer.filterMode=FilterMode.Point;importer.mipmapEnabled=false;importer.npotScale=TextureImporterNPOTScale.None;importer.textureCompression=TextureImporterCompression.Uncompressed;
            string name=System.IO.Path.GetFileNameWithoutExtension(path);
            importer.spriteBorder=name.StartsWith("button-",StringComparison.Ordinal)?new Vector4(12,10,12,10):name=="dialogue-frame-v1"?new Vector4(30,30,30,46):new Vector4(30,30,30,30);
        }
    }
}

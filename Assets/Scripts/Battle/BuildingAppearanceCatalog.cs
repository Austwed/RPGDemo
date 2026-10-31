using System;
using System.Linq;
using UnityEngine;
namespace BaseConquest.Battle
{
    [Serializable] public sealed class BuildingAppearance
    {
        public string BuildingId;
        public Texture2D Texture;
        public Color Tint=Color.white;
    }
    [CreateAssetMenu(menuName="SLG/建筑贴图配置",fileName="BuildingAppearances")]
    public sealed class BuildingAppearanceCatalog : ScriptableObject
    {
        public BuildingAppearance[] Buildings=Array.Empty<BuildingAppearance>();
        public BuildingAppearance Find(string id)
        {
            string facade=id=="town_recruit"?"barracks":id=="town_quest"?"government":id=="town_market"?"workshop":id;
            var original=Buildings?.FirstOrDefault(b=>b!=null&&b.BuildingId==facade);
            if(original?.Texture!=null)return original;
            var recommended=BaseConquest.Presentation.PixelDemoArt.Building(id);
            return recommended!=null?new BuildingAppearance {BuildingId=id,Texture=recommended}:original;
        }
    }
}

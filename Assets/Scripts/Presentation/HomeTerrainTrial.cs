using BaseConquest.Base;
using BaseConquest.Battle;
using UnityEngine;

namespace BaseConquest.Presentation
{
    public static class HomeTerrainTrial
    {
        // 2026-11-01 韦德拍板：环境材质统一 Cainos「Pixel Art Top Down - Basic」，默认开启；
        // 旧 GPT 规范化贴图（grass-64-v1 / dirt-64-v1）与 PixelDemoArt 地面图暂不使用，仅留文件回滚。
        public static bool Enabled;
        // 2026-11-01 韦德拍板「环境材质统一 Cainos」后从故乡试点全局化：所有地图生效；关闭 Enabled 仍可整体回滚到旧贴图。
        public static bool Eligible(BaseState state)=>true;
        public static Texture2D Texture(GroundSurface surface)=>surface==GroundSurface.Cobble?Stone():Resources.Load<Texture2D>("HomeTerrainTrial/"+(surface==GroundSurface.Grass?"grass-64-v2":surface==GroundSurface.Road||surface==GroundSurface.DirtPath?"path-64-v1":"missing"));
        // 抬升地崖壁侧立面：TX Tileset Wall 砖墙裁块（也用于全高墙 wallMaterial）。
        public static Texture2D Cliff()=>Resources.Load<Texture2D>("HomeTerrainTrial/cliff-64-v1");
        // 阶梯踏步：TX Tileset Wall 的水平层理砖块，读作台阶。
        public static Texture2D Stair()=>Resources.Load<Texture2D>("HomeTerrainTrial/stair-64-v1");
        // 镇区石板路面（cobble 路缘）：TX Tileset Stone Ground 平整区裁块。
        public static Texture2D Stone()=>Resources.Load<Texture2D>("HomeTerrainTrial/stone-64-v1");
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)] private static void Reset()=>Enabled=true;
    }
}

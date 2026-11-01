using BaseConquest.Base;
using BaseConquest.Battle;
using UnityEngine;

namespace BaseConquest.Presentation
{
    public static class HomeTerrainTrial
    {
        public static bool Enabled;
        public static bool Eligible(BaseState state)=>BaseDemo.Active(state)&&(BaseDemoPrologue.Village(state)||state.Campaign.CurrentMapId==BaseDemoFinale.Home);
        // grass v2 来自 Asset Store 免费包 Cainos「Pixel Art Top Down - Basic」（对接画风）；v1 是 GPT 规范化版，留作回滚。
        public static Texture2D Texture(GroundSurface surface)=>Resources.Load<Texture2D>("HomeTerrainTrial/"+(surface==GroundSurface.Grass?"grass-64-v2":surface==GroundSurface.Road||surface==GroundSurface.DirtPath?"dirt-64-v1":"missing"));
        // 抬升地的崖壁侧立面：与顶面同一免费包的砖墙裁块，解决“顶面新画风、侧面旧画风”。
        public static Texture2D Cliff()=>Resources.Load<Texture2D>("HomeTerrainTrial/cliff-64-v1");
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)] private static void Reset()=>Enabled=false;
    }
}

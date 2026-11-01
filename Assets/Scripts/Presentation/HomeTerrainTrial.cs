using BaseConquest.Base;
using BaseConquest.Battle;
using UnityEngine;

namespace BaseConquest.Presentation
{
    public static class HomeTerrainTrial
    {
        public static bool Enabled;
        public static bool Eligible(BaseState state)=>BaseDemo.Active(state)&&(BaseDemoPrologue.Village(state)||state.Campaign.CurrentMapId==BaseDemoFinale.Home);
        public static Texture2D Texture(GroundSurface surface)=>Resources.Load<Texture2D>("HomeTerrainTrial/"+(surface==GroundSurface.Grass?"grass":surface==GroundSurface.Road||surface==GroundSurface.DirtPath?"dirt":"missing")+"-64-v1");
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)] private static void Reset()=>Enabled=false;
    }
}

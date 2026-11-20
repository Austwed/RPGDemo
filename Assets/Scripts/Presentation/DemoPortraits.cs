using BaseConquest.Base;
using BaseConquest.Battle;
using UnityEngine;

namespace BaseConquest.Presentation
{
    public static class DemoPortraits
    {
        public static Texture2D HalfBody(string appearance, bool player = false, SoldierKind kind = SoldierKind.Hero)
        {
            string art = BaseDemoNpcs.ResolveNpcArt(appearance ?? "", portrait: true);
            if (art == "mounted-azhu") art = "azhu";
            if (PixelDemoArt.TroopId(art) == "cavalry") art = "mounted-cavalry";
            string fallback = player ? "player" : kind switch
            {
                SoldierKind.Melee => "infantry",
                SoldierKind.Ranged => "archer",
                SoldierKind.Mage => "mage",
                SoldierKind.Cavalry => "mounted-cavalry",
                _ => "enemy-captain"
            };
            return PixelDemoArt.Load("DialoguePortraits", art) ?? PixelDemoArt.Load("DialoguePortraits", fallback);
        }
    }
}

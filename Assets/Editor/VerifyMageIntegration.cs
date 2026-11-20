using System;
using BaseConquest.Battle;
using BaseConquest.Presentation;
using UnityEngine;

namespace BaseConquest.Editor
{
    public static class VerifyMageIntegration
    {
        public static void Run()
        {
            var sandbox = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "../UserSettings/IsolatedPlayerData"));
            if (System.IO.Directory.Exists(sandbox) && BaseConquest.Base.BaseBattleGateway.PlayerDataDirectory != sandbox)
                throw new InvalidOperationException("隔离工程使用了错误的游戏数据目录");
            VerifyMageCombat.Run();
            VerifyMageOperations.Run();
            var move = PixelDemoArt.Load("Characters", "mage");
            var cast = PixelDemoArt.Motion("mage", "attack");
            var down = PixelDemoArt.Motion("mage", "down");
            if (!move || move.width != 320 || move.height != 256 ||
                !cast || cast.width != 512 || cast.height != 512 ||
                !down || down.width != 1024 || down.height != 512 ||
                !Resources.Load<Texture2D>("PixelDemo/DialoguePortraits/mage-v1"))
                throw new InvalidOperationException("法师贴图或行列规格不符");
            foreach (var id in new[] { "basic_staff", "training_mage_basic", "training_mage_advanced" })
                if (!Resources.Load<Sprite>("PixelDemo/ItemIcons/" + id))
                    throw new InvalidOperationException("物品图标无法载入 " + id);
            if (PixelDemoArt.CastFrame("mage", 4, .5999) != 1 || PixelDemoArt.CastFrame("mage", 4, .6) != 2 ||
                MageSpellVisual.Art(false) == MageSpellVisual.Art(true))
                throw new InvalidOperationException("伸掌与出手对齐失败");
            Debug.Log("[P52 Integration] PASS combat, operations, resource import, release pose and impact art");
        }
    }
}

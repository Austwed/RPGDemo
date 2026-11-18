using System;
using System.IO;
using System.Linq;
using BaseConquest.Battle;
using BaseConquest.Base;
using BaseConquest.Presentation;
using UnityEngine;

namespace BaseConquest.Editor
{
    public static class VerifyPortraits
    {
        private static void Need(bool ok, string message)
        {
            if (!ok) throw new InvalidOperationException(message);
        }

        public static void Run()
        {
            string[] ids = { "player", "female", "ahe", "azhu", "galan", "chief", "batu", "huoya", "infantry", "archer",
                "mounted-cavalry", "laozhou", "chunshen", "merchant", "villager", "horn", "baron", "chenan", "villager-girl",
                "recruiter", "ancient-saber", "enemy-captain" };
            var textures = Resources.LoadAll<Texture2D>("PixelDemo/DialoguePortraits");
            Need(textures.Select(texture => texture.name).OrderBy(name => name)
                .SequenceEqual(ids.Select(id => id + "-v1").OrderBy(name => name)), "半身像目录存在缺项或重复外观");
            Need(Resources.LoadAll<Texture2D>("PixelDemo/Portraits").Length == 0, "旧头像仍在运行资源中");
            var fixture = new GameObject("PortraitFixture", typeof(RectTransform), typeof(CanvasRenderer));
            try
            {
                var avatar = fixture.AddComponent<BattleAvatarGraphic>();
                foreach (string id in ids)
                {
                    avatar.AppearanceId = id;
                    var expected = PixelDemoArt.Load("DialoguePortraits", id);
                    Need(avatar.mainTexture == expected && expected.width == 256 && expected.height == 256 &&
                        expected.filterMode == FilterMode.Point, "半身像格式或共用路径错误：" + id);
                }
                Need(DemoPortraits.HalfBody("azhu") == DemoPortraits.HalfBody("mounted-azhu"), "骑乘切换生成了第二张阿铸半身像");
                foreach (string role in new[] { "guard", "survivor", "rescued-villager" })
                    Need(DemoPortraits.HalfBody(BaseScriptNames.Portrait(role)) == DemoPortraits.HalfBody("villager"), "通用村民半身像未复用：" + role);
                foreach (var (kind, id) in new[] { (SoldierKind.Melee, "infantry"), (SoldierKind.Ranged, "archer"), (SoldierKind.Cavalry, "mounted-cavalry") })
                {
                    avatar.AppearanceId = "";
                    avatar.Kind = kind;
                    Need(avatar.mainTexture == DemoPortraits.HalfBody(id), "普通兵详情缺少兵种半身像：" + id);
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(fixture); }
            Debug.Log($"[VerifyPortraits] PASS · {ids.Length} 张 256×256 半身像与身份复用");
        }
    }
}

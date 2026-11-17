using System;
using System.IO;
using System.Text;
using BaseConquest.Battle;
using BaseConquest.Base;
using BaseConquest.Presentation;
using UnityEngine;

namespace BaseConquest.Editor
{
    public static class VerifyPortraits
    {
        private static void Need(bool ok,string message){if(!ok)throw new InvalidOperationException(message);}
        public static void Run()
        {
            foreach(string path in new[]{"Assets/Scripts/Battle/BattleMobileHud.cs","Assets/Scripts/Base/BaseNpcDialogueHud.cs","Assets/Scripts/Base/BasePartyInventoryHud.cs"})
                Need(File.ReadAllText(path).Contains("GetOrAdd<BattleAvatarGraphic>"),"头像入口未使用统一组件："+path);
            string[] ids={"player","female","ahe","azhu","galan","chief","batu","huoya","saber","archer","rider","laozhou","chunshen","merchant","villager","horn","baron","chenan","guard","survivor","villager-girl","rescued-villager","recruiter","ancient-saber"};
            foreach(string id in new[]{"chenan","recruiter","ancient-saber"})
                Need(DemoPortraits.For(id).Origin==DemoPortraits.Source.Portrait,"缺少专属头像："+id);
            var report=new StringBuilder("# Demo 头像来源\n\n");
            foreach(string id in ids)
            {
                bool hero=id=="player"||id=="female";var expected=DemoPortraits.For(id,hero);
                Need(expected.Texture!=null,"无法解析头像："+id);
                var views=new GameObject[3];
                try
                {
                    for(int i=0;i<views.Length;i++)
                    {views[i]=new GameObject("PortraitFixture",typeof(RectTransform),typeof(CanvasRenderer));var avatar=views[i].AddComponent<BattleAvatarGraphic>();avatar.PlayerHero=hero;avatar.AppearanceId=id;Need(avatar.mainTexture==expected.Texture,"头像入口不一致："+id+"/"+i);}
                    var t=expected.Texture;report.AppendLine("- "+id+" → "+expected.Id+" / "+expected.Origin+" / "+t.width+"×"+t.height+(expected.Origin==DemoPortraits.Source.Portrait&&t.width==128&&t.height==128?"（128×128）":"（图集或其他尺寸）"));
                }
                finally{foreach(var view in views)if(view!=null)UnityEngine.Object.DestroyImmediate(view);}
            }
            foreach(string role in new[]{"recruiter","innkeeper","peddler","lord","blacksmith","scout","guard","girl","survivor","rescued-villager"})
            {string id=BaseScriptNames.Portrait(role);var result=DemoPortraits.For(id);Need(result.Texture!=null,"NPC 头像角色无法解析："+role);report.AppendLine("- 角色键 "+role+" → "+id+" / "+result.Origin+" / "+result.Texture.width+"×"+result.Texture.height);}
            report.AppendLine("\n- 旧头像 `portrait-player-front-v2` 不再由头像入口加载；保留资源。\n");
            Directory.CreateDirectory("Logs");File.WriteAllText("Logs/verify-portraits.md",report.ToString(),Encoding.UTF8);Debug.Log("[VerifyPortraits] PASS");
        }
    }
}

using System;
using System.IO;
using System.Linq;
using BaseConquest.Base;
using UnityEditor;
using UnityEngine;

namespace BaseConquest.Editor
{
    public static class VerifyNpcFallback
    {
        private static void Need(bool ok,string message){if(!ok)throw new InvalidOperationException(message);}
        public static void Run()
        {
            // 2026-11-02：villager-girl（小芸）专属图集与头像已入库，不再是「缺图待补」对象。
            // 它必须从下面的临时造图样本里移出——那段 finally 会 DeleteAsset，
            // 若不移除会误删正式资源；接线断言改在下方单独锁定。
            var ids=new[]{"guard","survivor","rescued-villager"};
            var fallback=new[]{"villager","villager","villager"};
            var names=new[]{BaseScriptNames.Guard,"幸存村民","获救百姓甲"};
            var objects=new[]{"town_guard","demo_survivor","demo_rescued_1"};
            var state=JsonUtility.FromJson<BaseState>(Resources.Load<TextAsset>("DemoStages/camp-male").text);
            for(int i=0;i<ids.Length;i++)
            {
                string id=ids[i],expected=fallback[i],target="Assets/Resources/PixelDemo/Characters/"+id+"-v1.png";
                string portrait="Assets/Resources/PixelDemo/DialoguePortraits/"+id+"-v1.png";
                Need(!File.Exists(target)&&!File.Exists(portrait),"正式资源已存在，不可覆盖："+id);
                Need(BaseDemoNpcs.ResolveNpcArt(id)==expected&&BaseDemoNpcs.ResolveNpcArt(id,true)==expected,"缺图回退错误："+id);
                string source=AssetDatabase.GetAssetPath(Resources.Load<Texture2D>("PixelDemo/Characters/"+expected+"-v1"));Need(!string.IsNullOrEmpty(source),"缺少回退图："+expected);
                try
                {
                    File.Copy(source,target);AssetDatabase.ImportAsset(target,ImportAssetOptions.ForceSynchronousImport);
                    Need(BaseDemoNpcs.ResolveNpcArt(id)==id&&BaseDemoNpcs.ResolveNpcArt(id,true)==expected,"专属角色图切换错误："+id);
                    Need(BaseDemoNpcs.Present(state,new BaseMapObject(objects[i],names[i],4,4,.5,.5,false,"",expected,"npc")).AssetId==id,"场景对象未切换："+id);
                    File.Copy("Assets/Resources/PixelDemo/DialoguePortraits/"+expected+"-v1.png",portrait);
                    AssetDatabase.ImportAsset(portrait,ImportAssetOptions.ForceSynchronousImport);
                    Need(BaseDemoNpcs.ResolveNpcArt(id,true)==id&&BaseDemoNpcs.DialogueArt(names[i],expected)==id,"专属头像切换错误："+id);
                }
                finally{AssetDatabase.DeleteAsset(target);AssetDatabase.DeleteAsset(portrait);}
            }
            var old=new BaseMapObject("town_guard","旧卫兵",21,4,.6,.6,false,"","villager","npc");
            Need(BaseDemoNpcs.Present(state,old).AssetId=="villager","旧档对象回退错误");
            // 小芸（villager-girl）专属图接线：图集/头像/场景外观/对话框四处都指向自己，不再回退 villager。
            // 2026-11-02 定：她是可动 NPC（静态四向待机 + 四向行走），不是站桩 NPC。
            Need(File.Exists("Assets/Resources/PixelDemo/Characters/villager-girl-v1.png"),"小芸专属图集缺失");
            Need(File.Exists("Assets/Resources/PixelDemo/DialoguePortraits/villager-girl-v1.png"),"小芸专属半身像缺失");
            Need(BaseDemoNpcs.ResolveNpcArt("villager-girl")=="villager-girl","小芸图集未接线（仍在回退）");
            Need(BaseDemoNpcs.ResolveNpcArt("villager-girl",true)=="villager-girl","小芸头像未接线（仍在回退）");
            Need(BaseDemoNpcs.Present(state,new BaseMapObject("town_girl",BaseScriptNames.Girl,15,11.5,.6,.6,false,"","villager","npc")).AssetId=="villager-girl","小芸场景外观未接线");
            Need(BaseDemoNpcs.DialogueArt(BaseScriptNames.Girl,"villager")=="villager-girl","小芸对话头像未接线");
            var reserved=ids.Concat(new[]{"villager-girl"}).ToArray();
            Need(BaseDemoNpcs.MotionChecks(state,true).Count(x=>reserved.Contains(x.Id.Substring("motion-check:".Length)))==reserved.Length,"检查台缺少预留 NPC（含小芸）");
            Debug.Log("[VerifyNpcFallback] PASS");
        }
    }
}

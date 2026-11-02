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
            var ids=new[]{"scout","guard","survivor","villager-girl","rescued-villager"};
            var fallback=new[]{"chenan","villager","ahe","villager","villager"};
            var names=new[]{BaseScriptNames.Scout,BaseScriptNames.Guard,"幸存村民",BaseScriptNames.Girl,"获救百姓甲"};
            var objects=new[]{"demo_march_exit","town_guard","demo_survivor","town_girl","demo_rescued_1"};
            var state=JsonUtility.FromJson<BaseState>(Resources.Load<TextAsset>("DemoStages/camp-male").text);
            for(int i=0;i<ids.Length;i++)
            {
                string id=ids[i],expected=fallback[i],target="Assets/Resources/PixelDemo/Characters/"+id+"-v1.png";
                string portrait="Assets/Resources/PixelDemo/Portraits/portrait-"+id+"-v1.png";
                Need(!File.Exists(target)&&!File.Exists(portrait),"正式资源已存在，不可覆盖："+id);
                Need(BaseDemoNpcs.ResolveNpcArt(id)==expected&&BaseDemoNpcs.ResolveNpcArt(id,true)==expected,"缺图回退错误："+id);
                string source=AssetDatabase.GetAssetPath(Resources.Load<Texture2D>("PixelDemo/Characters/"+expected+"-v1"));Need(!string.IsNullOrEmpty(source),"缺少回退图："+expected);
                try
                {
                    File.Copy(source,target);AssetDatabase.ImportAsset(target,ImportAssetOptions.ForceSynchronousImport);
                    Need(BaseDemoNpcs.ResolveNpcArt(id)==id&&BaseDemoNpcs.ResolveNpcArt(id,true)==expected,"专属角色图切换错误："+id);
                    Need(BaseDemoNpcs.Present(state,new BaseMapObject(objects[i],names[i],4,4,.5,.5,false,"",expected,"npc")).AssetId==id,"场景对象未切换："+id);
                    File.Copy(source,portrait);AssetDatabase.ImportAsset(portrait,ImportAssetOptions.ForceSynchronousImport);
                    Need(BaseDemoNpcs.ResolveNpcArt(id,true)==id&&BaseDemoNpcs.DialogueArt(names[i],expected)==id,"专属头像切换错误："+id);
                }
                finally{AssetDatabase.DeleteAsset(target);AssetDatabase.DeleteAsset(portrait);}
            }
            var old=new BaseMapObject("town_guard","旧卫兵",21,4,.6,.6,false,"","villager","npc");
            Need(BaseDemoNpcs.Present(state,old).AssetId=="villager","旧档对象回退错误");
            Need(BaseDemoNpcs.MotionChecks(state).Count(x=>ids.Contains(x.Id.Substring("motion-check:".Length)))==ids.Length,"检查台缺少预留 NPC");
            Debug.Log("[VerifyNpcFallback] PASS");
        }
    }
}

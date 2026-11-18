using System;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
namespace BaseConquest.Base
{
    public static class DepartureAdviceOptions
    {
        private const string Key="BaseConquest.HideDepartureCompanionAdvice";
        public static bool Disabled {get=>PlayerPrefs.GetInt(Key,0)!=0;set{PlayerPrefs.SetInt(Key,value?1:0);PlayerPrefs.Save();}}
    }
    public sealed partial class BaseHud
    {
        private void OpenHelp()
        {
            BeginPanel("帮助");RowButton("返回",0,800,OpenMenu);row+=60;
            string[] titles={"游戏操作","游戏规则","游戏术语"};
            string[] texts={BaseExperienceRules.Operations,BaseExperienceRules.Rules,BaseExperienceRules.Terms};
            for(int i=0;i<3;i++)
            {
                float x=(i-1)*292;
                Label(content,titles[i],new Vector2(x,-row-24),new Vector2(276,42),23,TextAnchor.MiddleCenter,new Vector2(.5f,1));
                Label(content,texts[i],new Vector2(x,-row-394),new Vector2(270,680),18,TextAnchor.UpperLeft,new Vector2(.5f,1));
            }
            row+=750;EndPanel();
        }
        private bool CompanionAdvice(string location,string eventId,string mode,bool bypass)
        {
            var candidates=BaseDepartureAdvice.Candidates(owner.State,location,DepartureAdviceOptions.Disabled);
            if(candidates.Length==0)return false;
            BeginPanel("出征 · 还有空位");
            Line("常驻小队 "+owner.State.Adventure.Members.Count+"/3。邀请伙伴会释放其岗位并改变生产。",76,20);
            foreach(var person in candidates)
            {
                string id=person.Id;
                RowButton("加入 "+person.Name,0,800,()=>{
                    owner.Change(next=>{if(!BaseDepartureAdvice.Candidates(next,location,false).Any(l=>l.Id==id)||!BaseAdventure.SetMember(next,id,true))throw new InvalidOperationException("伙伴状态已变化，请重新查看");},"已加入伙伴；岗位已释放，生产安排已更新");
                    OpenDeparture(location,eventId,mode,bypass,true);
                });row+=60;
            }
            RowButton("继续出征",-210,400,()=>OpenDeparture(location,eventId,mode,bypass,true));
            RowButton("不再提示",210,400,()=>{DepartureAdviceOptions.Disabled=true;OpenDeparture(location,eventId,mode,bypass,true);});row+=60;
            EndPanel();return true;
        }
    }
}

using System;
using System.Linq;
using UnityEngine;
namespace BaseConquest.Base
{
    public static class DepartureAdviceOptions
    {
        private const string Key="BaseConquest.HideDepartureCompanionAdvice";
        public static bool Disabled {get=>PlayerPrefs.GetInt(Key,0)!=0;set{PlayerPrefs.SetInt(Key,value?1:0);PlayerPrefs.Save();}}
    }
    public sealed partial class BaseHud
    {
        private void OpenHelp() => OpenHelp(OpenMenu);
        private void OpenTitleHelp() => OpenHelp(OpenMainMenu);
        private void OpenHelp(Action back)
        {
            BeginPanel("帮助", "help");
            RowButton("返回菜单",0,800,back);
            row+=60;
            foreach (var topic in BaseExperienceRules.Help)
            {
                RowButton(topic.Title,0,800,()=>
                {
                    BeginPanel("帮助 · "+topic.Title,"help:"+topic.Title);
                    RowButton("返回帮助",0,800,()=>OpenHelp(back));
                    row+=60;
                    Line(topic.Text,300,20);
                    EndPanel();
                });
                row+=60;
            }
            EndPanel();
        }
        private bool CompanionAdvice(string location,string eventId,string mode)
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
                    OpenDeparture(location,eventId,mode,true);
                });row+=60;
            }
            RowButton("继续出征",-210,400,()=>OpenDeparture(location,eventId,mode,true));
            RowButton("不再提示",210,400,()=>{DepartureAdviceOptions.Disabled=true;OpenDeparture(location,eventId,mode,true);});row+=60;
            EndPanel();return true;
        }
    }
}

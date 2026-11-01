using System;
using System.Linq;
using BaseConquest.Battle;
using BaseConquest.Presentation;
using UnityEngine;
using UnityEngine.UI;

namespace BaseConquest.Base
{
    public sealed partial class BaseHud
    {
        private string achievementTab="main";
        private GameObject achievementToast;
        private Text achievementToastText;
        public void OpenAchievements()
        {
            var s=owner.State;BeginPanel("成就","achievements");interactionObject="";
            if(!BaseAchievements.Active(s)){Line("本局未启用Demo成就。",65);EndPanel();return;}
            Line("已解锁 "+s.Achievements.Records.Count+" / 9 · 奖励待领 "+s.Achievements.Records.Count(r=>r.ClaimedDay==0)+"；奖励全部手动领取。",58,21);
            string[] tabs={"main","side","hidden"},names={"主线成就","支线成就","隐藏成就"};
            for(int i=0;i<tabs.Length;i++){string tab=tabs[i];RowButton((achievementTab==tab?"● ":"")+names[i],(i-1)*280,265,()=>{achievementTab=tab;OpenAchievements();});}row+=62;
            foreach(var d in s.Achievements.Definitions.Where(d=>d.Category==achievementTab))
            {
                var r=BaseAchievements.Record(s,d.Id);string id=d.Id;
                Line(d.Name+" · "+(r==null?"未解锁":r.ClaimedDay>0?"已领取 · 第"+r.ClaimedDay+"天":"已达成 · 第"+r.UnlockedDay+"天"),45,23);
                Line(d.Category=="hidden"&&r==null?"隐藏条件，解锁后揭示":d.Condition,53,19);
                Line("奖励："+BaseAchievements.RewardName(d)+(d.Template!=""?"（一般品质）":""),40,19);
                string problem=BaseAchievements.ClaimProblem(s,id);
                RowButton(r?.ClaimedDay>0?"已领取":"领取奖励",0,800,()=>{owner.Change(next=>{if(!BaseAchievements.Claim(next,id))throw new InvalidOperationException(BaseAchievements.ClaimProblem(next,id));},"成就奖励已领取："+d.Name);OpenAchievements();},problem=="");row+=64;
                if(r!=null&&r.ClaimedDay==0&&problem!="")Line(problem,40,18);
            }
            EndPanel();
        }
        public void RefreshAchievementNotice(double seconds)
        {
            var s=owner.State;string id=BaseAchievements.AdvanceNotice(s,seconds);
            if(id==null){if(achievementToast!=null)achievementToast.SetActive(false);return;}
            if(achievementToast==null)
            {
                achievementToast=UiPrefabBinding.Create("AchievementToast","成就通知层",root.parent,true);
                var canvas=achievementToast.GetComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceOverlay;canvas.sortingOrder=400;
                var scaler=achievementToast.GetComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;scaler.referenceResolution=new Vector2(1280,720);scaler.screenMatchMode=CanvasScaler.ScreenMatchMode.Expand;
                achievementToast.GetComponent<GraphicRaycaster>().enabled=false;
                var box=Box("AchievementNotice",achievementToast.transform,Vector2.one,new Vector2(-240,-112),new Vector2(440,82),new Color(.08f,.19f,.2f,.97f));
                achievementToastText=Label(box,"",Vector2.zero,new Vector2(400,70),23);achievementToastText.raycastTarget=false;box.GetComponent<Image>().raycastTarget=false;
                UiPrefabBinding.Complete(achievementToast);
            }
            achievementToast.SetActive(true);achievementToastText.text="成就达成 · "+s.Achievements.Definitions.Single(d=>d.Id==id).Name+"\n奖励待领取 · 菜单 → 成就";
        }
        public void DisposeAchievementNotice()
        {if(achievementToast!=null){achievementToast.SetActive(false);UiPrefabBinding.DestroyView(achievementToast);achievementToast=null;achievementToastText=null;}}
    }
}

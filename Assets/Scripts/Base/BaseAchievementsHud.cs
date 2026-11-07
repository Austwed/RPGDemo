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
        // 成就归在「任务与成就」面板的第三页；非 Demo 局没有任务列表，退回独立面板。
        public void OpenAchievements()
        {
            if(owner.State==null)return;
            if(BaseDemo.Active(owner.State)){OpenQuests("achievement");return;}
            activeObject="achievements";interactionObject="";
            BeginPanel("成就","achievements");
            AchievementBody(owner.State);
            EndPanel();
        }
        // 只画内容、不建面板，供任务面板复用。
        private void AchievementBody(BaseState s)
        {
            if(!BaseAchievements.Active(s)){Line("本局未启用 Demo 成就。",40,DemoUiMetrics.FontBody);return;}
            Line("已解锁 "+s.Achievements.Records.Count+" / 9 · 奖励待领 "+s.Achievements.Records.Count(r=>r.ClaimedDay==0)+"；奖励全部手动领取。",24,DemoUiMetrics.FontBody);
            row+=6;
            string[] tabs={"main","side","hidden"},names={"主线成就","支线成就","隐藏成就"};
            for(int i=0;i<tabs.Length;i++){string tab=tabs[i];var tabButton=MakeButton(content,(achievementTab==tab?"● ":"")+names[i],new Vector2((i-1)*200,-row-20),new Vector2(180,36),()=>{achievementTab=tab;RepaintAchievements();},new Vector2(.5f,1));var tabLabel=tabButton.GetComponentInChildren<Text>();if(tabLabel!=null)tabLabel.fontSize=DemoUiMetrics.FontBody;}row+=44;
            var definitions=s.Achievements.Definitions.Where(d=>d.Category==achievementTab).ToList();
            if(definitions.Count==0)Line("这一类还没有成就。",40,DemoUiMetrics.FontBody);
            foreach(var d in definitions)AchievementCard(s,d);
        }
        private void RepaintAchievements()
        {
            if(BaseDemo.Active(owner.State))OpenDemoQuestPanel();else OpenAchievements();
        }
        // 一张成就卡：名称 + 领取态 + 条件 + 奖励 + 领取按钮。条件未满足时按钮禁用并写明原因。
        private void AchievementCard(BaseState s,BaseAchievementDefinition d)
        {
            var record=BaseAchievements.Record(s,d.Id);
            string problem=BaseAchievements.ClaimProblem(s,d.Id);
            bool claimed=record!=null&&record.ClaimedDay>0;
            bool claimable=!claimed&&problem=="";
            float height=96+(claimed?0:40)+(claimed||problem==""?0:20);
            var card=Box("AchievementCard",content,new Vector2(.5f,1),new Vector2(0,-row-height/2),new Vector2(850,height),new Color(.16f,.15f,.14f,.72f));
            var outline=card.gameObject.GetOrAdd<Outline>();outline.effectColor=new Color(.72f,.55f,.32f,.35f);outline.effectDistance=new Vector2(1,-1);
            Color phase=claimed?new Color(.58f,.62f,.60f):record!=null?new Color(.56f,.81f,.61f):new Color(.76f,.79f,.74f);
            Box("Accent",card,new Vector2(0,1),new Vector2(4,-height/2),new Vector2(4,height-16),new Color(phase.r,phase.g,phase.b,.79f));
            Label(card,d.Name,new Vector2(278,-10),new Vector2(520,26),DemoUiMetrics.FontSubhead,TextAnchor.MiddleLeft,new Vector2(0,1));
            var status=Label(card,claimed?"已领取 · 第"+record.ClaimedDay+"天":record!=null?"可领取 · 第"+record.UnlockedDay+"天达成":"未解锁",new Vector2(-154,-10),new Vector2(280,26),DemoUiMetrics.FontBody,TextAnchor.MiddleRight,new Vector2(1,1));
            status.color=phase;
            Label(card,d.Category=="hidden"&&record==null?"隐藏条件，解锁后揭示":d.Condition,new Vector2(425,-38),new Vector2(814,20),DemoUiMetrics.FontBody,TextAnchor.MiddleLeft,new Vector2(0,1));
            var reward=Label(card,"奖励："+BaseAchievements.RewardName(d)+(d.Template!=""?"（一般品质）":""),new Vector2(425,-60),new Vector2(814,18),DemoUiMetrics.FontCaption,TextAnchor.MiddleLeft,new Vector2(0,1));
            reward.color=DemoUiTheme.Current.Muted;
            if(!claimed)
            {
                string id=d.Id;
                MakeButton(card,"领取奖励",new Vector2(118,-height+40),new Vector2(200,36),()=>{owner.Change(next=>{if(!BaseAchievements.Claim(next,id))throw new InvalidOperationException(BaseAchievements.ClaimProblem(next,id));},"成就奖励已领取："+d.Name);RepaintAchievements();},new Vector2(0,1)).interactable=claimable;
                if(!claimable)Label(card,problem,new Vector2(532,-height+40),new Vector2(600,20),DemoUiMetrics.FontCaption,TextAnchor.MiddleLeft,new Vector2(0,1)).color=DemoUiTheme.Current.Muted;
            }
            row+=height+10;
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
                achievementToastText=Label(box,"",Vector2.zero,new Vector2(400,70),DemoUiMetrics.FontSubhead);achievementToastText.raycastTarget=false;box.GetComponent<Image>().raycastTarget=false;
                UiPrefabBinding.Complete(achievementToast);
            }
            achievementToast.SetActive(true);achievementToastText.text="成就达成 · "+s.Achievements.Definitions.Single(d=>d.Id==id).Name+"\n奖励待领取 · 菜单 → 成就";
        }
        public void DisposeAchievementNotice()
        {if(achievementToast!=null){achievementToast.SetActive(false);UiPrefabBinding.DestroyView(achievementToast);achievementToast=null;achievementToastText=null;}}
    }
}

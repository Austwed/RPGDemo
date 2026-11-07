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
        // 版式照原型 `case 'quests'` 的成就分支：三列卡片网格（原型 .grid 三列），每张卡 = 名称＋标签＋条件＋奖励＋按钮。
        private void AchievementBody(BaseState s)
        {
            if(!BaseAchievements.Active(s)){Line("本局未启用 Demo 成就。",40,DemoUiMetrics.FontBody);return;}
            var note=Label(content,"已解锁 "+s.Achievements.Records.Count+" / 9 · 奖励待领 "+s.Achievements.Records.Count(r=>r.ClaimedDay==0)+"；奖励全部手动领取。",new Vector2(0,-row-9),new Vector2(850,18),DemoUiMetrics.FontCaption,TextAnchor.MiddleLeft,new Vector2(.5f,1));
            note.color=DemoUiTheme.Current.Muted;
            row+=30;
            string[] tabs={"main","side","hidden"},names={"主线","支线","隐藏"};
            for(int i=0;i<tabs.Length;i++)
            {
                string tab=tabs[i];bool active=achievementTab==tab;
                var button=MakeButton(content,names[i],new Vector2(-330+i*96,-row-17),new Vector2(88,34),()=>{achievementTab=tab;RepaintAchievements();},new Vector2(.5f,1));
                var label=button.GetComponentInChildren<Text>();
                if(label!=null){label.fontSize=DemoUiMetrics.FontBody;label.fontStyle=active?FontStyle.Bold:FontStyle.Normal;label.color=active?DemoUiTheme.Current.Ink:DemoUiTheme.Current.Muted;}
                if(active)Solid("TabUnderline",button.transform,new Vector2(.5f,0),new Vector2(0,2),new Vector2(72,2),DemoUiTheme.Current.Copper);
            }
            row+=48;
            var definitions=s.Achievements.Definitions.Where(d=>d.Category==achievementTab).ToList();
            if(definitions.Count==0){Line("这一类还没有成就。",28,DemoUiMetrics.FontCaption);return;}
            for(int i=0;i<definitions.Count;i++)AchievementCard(s,definitions[i],i%3,i/3);
            row+=(definitions.Count+2)/3*148;
        }
        private void RepaintAchievements()
        {
            if(BaseDemo.Active(owner.State))OpenDemoQuestPanel();else OpenAchievements();
        }
        // 一张成就卡（285×136，三列）：名称＋领取态标签、条件、奖励、领取按钮。未满足条件时按钮禁用并写原因。
        private void AchievementCard(BaseState s,BaseAchievementDefinition d,int column,int line)
        {
            var record=BaseAchievements.Record(s,d.Id);
            string problem=BaseAchievements.ClaimProblem(s,d.Id);
            bool claimed=record!=null&&record.ClaimedDay>0;
            bool claimable=!claimed&&problem=="";
            var card=Box("AchievementCard",content,new Vector2(.5f,1),new Vector2(-298+column*298,-row-line*148-68),new Vector2(285,136),claimed?new Color(.16f,.16f,.14f,.72f):claimable?new Color(.22f,.26f,.19f,.78f):new Color(.16f,.16f,.14f,.72f));
            var outline=card.gameObject.GetOrAdd<Outline>();
            outline.effectColor=claimable?new Color(.55f,.72f,.50f,.85f):new Color(.72f,.55f,.32f,.30f);outline.effectDistance=new Vector2(1,-1);
            Label(card,d.Name,new Vector2(97,-10),new Vector2(170,20),DemoUiMetrics.FontBody,TextAnchor.MiddleLeft,new Vector2(0,1)).fontStyle=FontStyle.Bold;
            var tag=Label(card,claimed?"已领取":record!=null?"可领取":"未解锁",new Vector2(-52,-10),new Vector2(80,20),DemoUiMetrics.FontCaption,TextAnchor.MiddleRight,new Vector2(1,1));
            tag.color=claimable?new Color(.60f,.80f,.58f):DemoUiTheme.Current.Muted;
            var condition=Label(card,d.Category=="hidden"&&record==null?"隐藏条件，解锁后揭示":d.Condition,new Vector2(142,-39),new Vector2(261,34),DemoUiMetrics.FontCaption,TextAnchor.UpperLeft,new Vector2(0,1));
            condition.color=DemoUiTheme.Current.Muted;
            var reward=Label(card,"奖励："+BaseAchievements.RewardName(d),new Vector2(142,-76),new Vector2(261,16),DemoUiMetrics.FontCaption,TextAnchor.MiddleLeft,new Vector2(0,1));
            reward.color=DemoUiTheme.Current.Muted;
            if(claimed)return;
            string id=d.Id;
            var button=MakeButton(card,"领取",new Vector2(60,-108),new Vector2(96,28),()=>{owner.Change(next=>{if(!BaseAchievements.Claim(next,id))throw new InvalidOperationException(BaseAchievements.ClaimProblem(next,id));},"成就奖励已领取："+d.Name);RepaintAchievements();},new Vector2(0,1));
            var label=button.GetComponentInChildren<Text>();if(label!=null)label.fontSize=DemoUiMetrics.FontCaption;
            button.interactable=claimable;
            if(!claimable)Label(card,problem,new Vector2(222,-108),new Vector2(120,16),DemoUiMetrics.FontCaption,TextAnchor.MiddleLeft,new Vector2(0,1)).color=DemoUiTheme.Current.Muted;
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

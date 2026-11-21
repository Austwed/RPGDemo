using System;
using BaseConquest.Presentation;
using BaseConquest.Story;
namespace BaseConquest.Base
{
    public sealed partial class BaseHud
    {
        public void ToggleQuests(){if(PanelOpen&&(activeObject=="quests"||activeObject=="demo-story"||activeObject=="achievements"))ClosePanel();else OpenQuests();}
        // tab 只在 Demo 面板生效（main／side／achievement）；不传就沿用上次选中的页。
        public void OpenQuests(string tab=null)
        {
            if(owner.State==null)return;
            if(owner.State.Aftermath.GameOver){OpenGameOver();return;}
            if(BaseDemo.Active(owner.State)){questTab=string.IsNullOrEmpty(tab)?"tasks":tab;OpenDemoQuestPanel();return;}
            if(!string.IsNullOrEmpty(tab))return;
            OpenTownQuestPanel();
        }
        // 非 Demo 的城镇委托：单委托面板，没有主线／支线可分，不套 tab。
        private void OpenTownQuestPanel()
        {
            activeObject="quests";interactionObject="";var s=owner.State;
            BeginPanel("任务","quests");
            string accept=BaseQuests.AcceptProblem(s),claim=BaseQuests.ClaimProblem(s);
            RowButton("接取委托",-210,400,()=>{owner.Change(next=>{if(!BaseQuests.Accept(next))throw new InvalidOperationException("接取条件已变化，请重新查看");},StoryText.Get("story.quest.town_defence.accepted","已接取城镇防务委托"));OpenTownQuestPanel();},accept=="");
            RowButton("交付 · 领取图册",210,400,()=>{if(claim==BagFullException.Notice){ShowBagFull();return;}owner.Change(next=>{if(!BaseQuests.Claim(next))throw new InvalidOperationException("交付条件已变化或背包已满，请重新查看");},StoryText.Get("story.quest.town_defence.delivered","获得营造图册1本，已开放营造入门剧情条件"));OpenTownQuestPanel();},claim==""||claim==BagFullException.Notice);
            row+=66;
            Line(BaseQuests.Title,34,DemoUiMetrics.FontPanelTitle);Line("状态："+BaseQuests.Status(s),24,DemoUiMetrics.FontBody);
            Line(StoryText.Get("story.quest.town_defence.description","“附近的军队威胁着往来商旅。协助我们恢复安全，城镇会提供营造图册，帮助你建设据点。”"),44,DemoUiMetrics.FontBody);
            Line(StoryText.Get("story.quest.town_defence.objective","目标：亲自赢得1场迎战／进攻，或让荒野据点归属己方。"),24,DemoUiMetrics.FontBody);
            Line(StoryText.Get("story.quest.town_defence.reward","奖励：营造图册 ×1 ＋ 营造入门剧情条件"),22,DemoUiMetrics.FontBody);
            row+=6;
            if(s.Quests.AcceptedDay>0)
            {
                bool tracked=BaseQuestLog.TrackedId(s)=="quest:town_defence";
                RowButton(tracked?"● 取消追踪":"○ 追踪",0,800,()=>{owner.Change(next=>{if(!BaseQuestLog.ToggleTrack(next,"quest:town_defence"))throw new InvalidOperationException("任务状态已变化");},tracked?"已取消追踪":"已显示在常驻任务栏");OpenTownQuestPanel();});
                row+=54;
            }
            if(s.Quests.AcceptedDay==0)Line(accept=="" ? "可以接取。" : accept,24,DemoUiMetrics.FontBody);
            else if(s.Quests.ClaimedDay>0)Line(StoryText.Get("story.quest.town_defence.claimed","第{day}天已领取。图册使用后不会自动补发。").Replace("{day}",s.Quests.ClaimedDay.ToString()),24,DemoUiMetrics.FontBody);
            else Line(claim=="" ? StoryText.Get("story.quest.town_defence.ready","可以交付，确认后发放一次奖励。") : claim,24,DemoUiMetrics.FontBody);
            EndPanel();
        }
    }
}

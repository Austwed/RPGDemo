using System;
using System.Collections.Generic;
using System.Linq;
using BaseConquest.Battle;
using BaseConquest.Presentation;
using BaseConquest.Story;
using UnityEngine;

namespace BaseConquest.Base
{
    public sealed partial class BaseHud
    {
        private readonly BasePartnerDialogue dialogue=new BasePartnerDialogue();
        public void OpenAzhuAfterDuel(bool won=true)
        {
            NpcDialogue(won?new[]{new NpcLine("阿铸","azhu","[捂着震麻的手腕大笑] 痛快！真是好快的剑！王都禁卫出来的果然名不虚传！"),new NpcLine("阿铸","azhu","窝在镇上打铁太闷了，要是你不嫌弃，算我阿铸一个如何？")}:new[]{new NpcLine("阿铸","azhu","[收刀入鞘] 承让！你胸口似乎还有旧伤未愈，等养足了精神再来找我！")},new[]{new NpcReply(won?"欢迎加入":"再切磋一次",()=>{if(won)owner.JoinDemoPartner("partner_craft");else owner.BeginAzhuDuel();}),new NpcReply("暂缓邀请",ClosePanel)});
        }
        private void OpenPartner(string id){var leader=owner.State.Leaders.FirstOrDefault(l=>l.Id==id);if(leader!=null)RenderPartner(id,BasePartnerDialogue.Greeting(leader));}
        private void PartnerSpeech(BaseLeader leader,string key)
        {
            var avatar=Rect("DialogueAvatar",content,new Vector2(.5f,1),new Vector2(-340,-row-96),new Vector2(170,180)).gameObject.AddComponent<BattleAvatarGraphic>();
            avatar.PlayerHero=leader.Id==owner.State.PlayerLeaderId;avatar.AppearanceId=BaseCommanderIdentity.Appearance(leader);avatar.raycastTarget=false;
            Label(content,BasePersonnel.LeaderName(leader)+"\n"+BaseCommanderIdentity.RoleName(leader),new Vector2(-340,-row-215),new Vector2(180,58),18,TextAnchor.MiddleCenter,new Vector2(.5f,1));
            var speech=Label(content,BasePartnerDialogue.Resolve(leader,key),new Vector2(100,0),new Vector2(620,180),22,TextAnchor.UpperLeft,new Vector2(.5f,1));speech.name="DialogueSpeech";speech.supportRichText=false;
            float height=Mathf.Max(180,speech.preferredHeight+20);speech.rectTransform.sizeDelta=new Vector2(620,height);speech.rectTransform.anchoredPosition=new Vector2(100,-row-height/2-16);row+=Mathf.Max(258,height+32);
        }
        private void RenderPartner(string id,string key)
        {
            var s=owner.State;var leader=s.Leaders.FirstOrDefault(l=>l.Id==id);if(leader==null)return;
            activeObject=id;interactionObject="";
            bool joined=BaseAdventure.Contains(s,id),nearby=BaseInteraction.CanReach(s,owner.PresentedObject(id));string problem=BaseAdventure.ChangeProblem(s,id,!joined);
            bool paused=BaseAdventure.PauseFor(s,id)!=null;
            var replies=new List<NpcReply>{new NpcReply("聊天对话",()=>RenderPartner(id,dialogue.Chat(leader)))};
            if(joined)
            {
                replies.Add(new NpcReply(paused?"恢复随行 · 保留的名额":"暂离队伍 · 入住当地旅店",()=>ConfirmPartnerDisposition(id,paused?"resume":"pause"),nearby&&BaseAdventure.PauseProblem(s,id,paused)==""));
                replies.Add(new NpcReply("回到基地 · 带本队士兵",()=>ConfirmPartnerDisposition(id,"home"),nearby&&BaseAdventure.ReturnHomeProblem(s,id)==""));
                problem=BaseAdventure.ReturnHomeProblem(s,id);
            }
            else replies.Add(new NpcReply("邀请加入冒险小队",()=>ConfirmAdventure(id,true),nearby&&problem==""));
            if(id=="partner_craft"&&BaseSceneRules.Active(s))
            {replies.Add(new NpcReply("原地比武",owner.BeginAzhuDuel,nearby&&!paused));replies.Add(new NpcReply("去迎敌战场演练",owner.BeginAzhuFieldDuel,nearby&&!paused&&BaseDemoDefense.Won(s)));}
            replies.Add(new NpcReply("任职安排",()=>OpenPartnerJobs(id),!paused));
            replies.Add(new NpcReply("查看角色详情",()=>OpenPersonnelDetail("leader:"+id,BaseMuster.Snapshot(s),()=>OpenPartner(id))));replies.Add(new NpcReply("退出对话",ClosePanel));
            NpcDialogue(new[]{new NpcLine(BasePersonnel.LeaderName(leader),BaseCommanderIdentity.Appearance(leader),BasePartnerDialogue.Resolve(leader,key)+(paused?"\n我会在这里等你，队伍名额仍为我保留。":"")+(problem!=""?"\n"+problem:""),leader.Id==s.PlayerLeaderId)},replies.ToArray());
        }
        private void ConfirmPartnerDisposition(string id,string action)
        {
            string title=action=="pause"?"确认暂离队伍":action=="resume"?"确认恢复随行":"确认本队回营";
            BeginPanel(title,"partner-disposition:"+id);
            Line(action=="pause"?"伙伴入住当地旅店，释放随行与营帐名额，保留装备和背包。每人每天2金币、先付首日，最多额外寄存3人；"+BaseScriptNames.Village+"扎营点免费。士兵留在出征队，不入住旅店。":action=="resume"?"恢复伙伴随行及暂存的实际编组，不补血、不复制人员。":"队长、本队实际士兵与随身物品立即回到本领地基地，不耗日；保留战损，今日不生产。",110,21);
            RowButton("确认",-210,400,()=>
            {
                bool changed=owner.Change(next=>
                {
                    if(!BaseInteraction.CanReach(next,owner.PresentedObject(id)))throw new InvalidOperationException("请在伙伴旁交谈");
                    bool ok=action=="pause"?BaseAdventure.Pause(next,id):action=="resume"?BaseAdventure.Resume(next,id):BaseAdventure.SendHome(next,id);
                    if(!ok)throw new InvalidOperationException(action=="home"?BaseAdventure.ReturnHomeProblem(next,id):BaseAdventure.PauseProblem(next,id,action=="resume"));
                },action=="pause"?"伙伴已入住，名额释放":action=="resume"?"伙伴恢复随行":"伙伴与本队士兵已返营");
                if(changed&&(action=="home"||action=="pause"))ClosePanel();else OpenPartner(id);
            });RowButton("取消",210,400,()=>OpenPartner(id));row+=65;EndPanel();
        }
        private void OpenPartnerJobs(string id)
        {
            var leader=owner.State.Leaders.FirstOrDefault(l=>l.Id==id);if(leader==null)return;activeObject=id;
            BeginPanel(BasePersonnel.LeaderName(leader)+" · 任职安排","partner-jobs:"+id);PartnerSpeech(leader,BasePartnerDialogue.Prefix(leader)+".jobs");
            RowButton("返回对话",0,800,()=>OpenPartner(id));row+=62;
            ManagerButtons(id,()=>OpenPartnerJobs(id));
            Line("当前任务："+PersonState(leader)+"；生命 "+leader.Health.ToString("0.#")+"/"+leader.MaxHealth.ToString("0.#"),60,19);
            Line("粮田增益 "+(BaseDevelopment.Bonus(leader,"farm")*100).ToString("0.#")+"%／工坊 "+(BaseDevelopment.Bonus(leader,"workshop")*100).ToString("0.#")+"%",56,19);
            Line(BaseAdventure.Contains(owner.State,id)?"在安全营地选择设施任职，会退出随行小队并担任管理员；主角固定随行。":"任职后走到设施附近；加入冒险小队会释放实际岗位，保留锁定意向。",70,18);
            EndPanel();
        }
    }
}

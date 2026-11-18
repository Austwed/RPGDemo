using System;
using System.Collections.Generic;
using System.Linq;

namespace BaseConquest.Base
{
    public sealed partial class BaseHud
    {
        private readonly BasePartnerDialogue dialogue=new BasePartnerDialogue();
        public void OpenAzhuAfterDuel(bool won=true)
        {
            NpcDialogue(won?new[]{
                new NpcLine("阿铸","azhu","［甩了甩手］痛快！你这一剑，我都来不及收枪。"),
                new NpcLine("阿铸","azhu","老闷在铺里也没意思。你那儿缺人吗？算我一个。")
            }:new[]{new NpcLine("阿铸","azhu","［收枪］到这儿吧。歇口气，想再练就来找我。")},
                new[]{new NpcReply(won?"欢迎加入":"再切磋一次",()=>{if(won)owner.JoinDemoPartner("partner_craft");else owner.BeginAzhuDuel();}),new NpcReply("暂缓邀请",ClosePanel)});
        }
        private void OpenPartner(string id){var leader=owner.State.Leaders.FirstOrDefault(l=>l.Id==id);if(leader!=null)RenderPartner(id,BasePartnerDialogue.Greeting(leader));}
        private void RenderPartner(string id,string key)
        {
            var s=owner.State;var leader=s.Leaders.FirstOrDefault(l=>l.Id==id);if(leader==null)return;
            activeObject=id;interactionObject="";
            bool joined=BaseAdventure.Contains(s,id),nearby=BaseInteraction.CanReach(s,owner.PresentedObject(id));
            bool paused=BaseAdventure.PauseFor(s,id)!=null;
            string problem=joined?BaseAdventure.PauseProblem(s,id,paused):BaseAdventure.ChangeProblem(s,id,true);
            var replies=new List<NpcReply>{new NpcReply("聊天对话",()=>RenderPartner(id,dialogue.Chat(leader)))};
            if(joined)
            {
                replies.Add(new NpcReply(paused?"恢复随行":"在这里等候",()=>ConfirmPartnerDisposition(id,paused),nearby&&problem==""));
                if(s.Campaign.Location=="base"&&BaseAdventure.ChangeProblem(s,id,false)=="")
                    replies.Add(new NpcReply("留在营地",()=>ConfirmAdventure(id,false),nearby));
            }
            else replies.Add(new NpcReply("邀请加入冒险小队",()=>ConfirmAdventure(id,true),nearby&&problem==""));
            replies.Add(new NpcReply("退出对话",ClosePanel));
            NpcDialogue(new[]{new NpcLine(BasePersonnel.LeaderName(leader),BaseCommanderIdentity.Appearance(leader),BasePartnerDialogue.Resolve(leader,key)+(paused?"\n我会在这里等你，队伍名额仍为我保留。":"")+(problem!=""?"\n"+problem:""),leader.Id==s.PlayerLeaderId)},replies.ToArray());
        }
        private void ConfirmPartnerDisposition(string id,bool resume)
        {
            string title=resume?"确认恢复随行":"确认暂离队伍";
            BeginPanel(title,"partner-disposition:"+id);
            Line(resume?"伙伴与本队士兵恢复随行。":"伙伴和本队士兵留在这里等候，保留随行名额。",78,21);
            RowButton("确认",-210,400,()=>
            {
                bool changed=owner.Change(next=>
                {
                    if(!BaseInteraction.CanReach(next,owner.PresentedObject(id)))throw new InvalidOperationException("请在伙伴旁交谈");
                    bool ok=resume?BaseAdventure.Resume(next,id):BaseAdventure.Pause(next,id);
                    if(!ok)throw new InvalidOperationException(BaseAdventure.PauseProblem(next,id,resume));
                },"");
                if(!changed){OpenPartner(id);return;}
                if(!resume){OpenPartner(id);return;}
                var leader=owner.State.Leaders.Single(l=>l.Id==id);
                NpcDialogue(new[]{new NpcLine(BasePersonnel.LeaderName(leader),BaseCommanderIdentity.Appearance(leader),"歇够了骨头都快生锈了，咱们这就出发！")},new[]{new NpcReply("出发",()=>{ClosePanel();OpenPartner(id);})});
            });RowButton("取消",210,400,()=>OpenPartner(id));row+=65;EndPanel();
        }
    }
}

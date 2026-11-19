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
        private void OpenPartner(string id)
        {
            var state = owner.State;
            var leader = state.Leaders.FirstOrDefault(l => l.Id == id);
            if (leader != null) RenderPartner(id, BasePartnerDialogue.Greeting(state, leader));
        }
        private void RenderPartner(string id,string key)
        {
            var s=owner.State;var leader=s.Leaders.FirstOrDefault(l=>l.Id==id);if(leader==null)return;
            activeObject=id;interactionObject="";
            bool joined=BaseAdventure.Contains(s,id),nearby=BaseInteraction.CanReach(s,owner.PresentedObject(id));
            bool paused=BaseAdventure.PauseFor(s,id)!=null;
            string problem=joined?BaseAdventure.PauseProblem(s,id,paused):BaseAdventure.ChangeProblem(s,id,true);
            var replies=new List<NpcReply>{new NpcReply("聊天对话",()=>RenderPartner(id,dialogue.Chat(owner.State, leader)))};
            if(joined)
            {
                replies.Add(new NpcReply(paused?"恢复随行":"在这里等候",()=>SetPartnerWaiting(id,paused),nearby&&problem==""));
                if(s.Campaign.Location=="base"&&BaseAdventure.ChangeProblem(s,id,false)=="")
                    replies.Add(new NpcReply("留在营地",()=>SetPartnerFollowing(id,false),nearby));
            }
            else replies.Add(new NpcReply("邀请随行",()=>SetPartnerFollowing(id,true),nearby&&(BaseAdventure.Full(s)||BaseAdventure.WaitingHere(s,leader)||problem=="")));
            if (BaseAdventure.WaitingHere(s,leader) || joined && (s.Campaign.Location != "base" || s.Muster.Parties.Count != 0) && problem == "")
                replies.Add(new NpcReply("回到营地",()=>SendPartnerHome(id),nearby));
            replies.Add(new NpcReply("退出对话",ClosePanel));
            NpcDialogue(new[]{new NpcLine(BasePersonnel.LeaderName(leader),BaseCommanderIdentity.Appearance(leader),BasePartnerDialogue.Resolve(leader,key),leader.Id==s.PlayerLeaderId)},replies.ToArray());
        }
        private void SetPartnerWaiting(string id, bool resume)
        {
            if (owner.Change(next =>
            {
                if (!BaseInteraction.CanReach(next, owner.PresentedObject(id)))
                    throw new InvalidOperationException("请在伙伴旁交谈");
                bool changed = resume ? BaseAdventure.Resume(next,id) : BaseAdventure.Pause(next,id);
                if (!changed) throw new InvalidOperationException(BaseAdventure.PauseProblem(next,id,resume));
            }, "")) ClosePanel();
        }

        private void SetPartnerFollowing(string id, bool follow)
        {
            if (follow && BaseAdventure.Full(owner.State)) { OpenFullPartyReply(id,ClosePanel); return; }
            if (owner.Change(next =>
            {
                if (!BaseInteraction.CanReach(next, owner.PresentedObject(id)))
                    throw new InvalidOperationException("请在伙伴旁交谈");
                var person = next.Leaders.Single(l => l.Id == id);
                if (!(follow && BaseAdventure.WaitingHere(next,person) ? BaseAdventure.InviteWaiting(next,person) : BaseAdventure.SetMember(next, id, follow)))
                    throw new InvalidOperationException(BaseAdventure.ChangeProblem(next, id, follow));
            }, "")) ClosePanel();
        }

        private void OpenFullPartyReply(string id, Action done)
        {
            var person = owner.State.Leaders.Single(l => l.Id == id);
            NpcDialogue(new[]{new NpcLine(BasePersonnel.LeaderName(person), BaseCommanderIdentity.Appearance(person), BaseAdventure.FullPartyReply)},
                new[]{new NpcReply("知道了",done)});
        }

        internal void OpenRecruitDestination(string id, Action done = null)
        {
            var person = owner.State.Leaders.Single(l => l.Id == id);
            Action finish = done ?? ClosePanel;
            NpcDialogue(new[]{new NpcLine(BasePersonnel.LeaderName(person), BaseCommanderIdentity.Appearance(person),
                "队伍人满了，我在这儿等，还是先回营地？")},
                new[]{new NpcReply("先回营地",()=>SendPartnerHome(id,finish)),new NpcReply("先在这里等",finish)});
        }

        private void SendPartnerHome(string id, Action done = null)
        {
            if (!owner.Change(next => {
                if (!BaseAdventure.SendToCamp(next,id)) throw new InvalidOperationException("现在无法回营。");
            }, "")) return;
            (done ?? ClosePanel)();
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;

namespace BaseConquest.Base
{
    public sealed partial class BaseHud
    {
        private readonly BasePartnerDialogue dialogue=new BasePartnerDialogue();
        public void OpenAzhuAfterDuel(bool won)
        {
            string praise = won ? "［甩了甩手］痛快！你这一剑，我都来不及收枪。" :
                "前些天伤得那么重，还没养好就能跟我打成这样。你的剑法，我服。";
            NpcDialogue(new[]
            {
                new NpcLine("阿铸", "azhu", praise),
                new NpcLine("阿铸", "azhu", "老闷在铺里也没意思。你那儿缺人吗？算我一个。")
            }, new[] { new NpcReply("欢迎加入", () => owner.JoinDemoPartner("partner_craft")),
                new NpcReply("暂缓邀请", ClosePanel) });
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
                if (paused) replies.Add(new NpcReply("恢复随行", () => ResumePartner(id), nearby && problem == ""));
                replies.Add(new NpcReply("留在此地", () => LeavePartnerHere(id),
                    nearby && BaseAdventure.LeaveHereProblem(s, id) == ""));
            }
            else replies.Add(new NpcReply("邀请随行", () => FollowPartner(id),
                nearby && (BaseAdventure.Full(s) || BaseAdventure.WaitingHere(s, leader) || problem == "")));
            if (BaseAdventure.WaitingHere(s,leader) || joined && (s.Campaign.Location != "base" || s.Muster.Parties.Count != 0) && problem == "")
                replies.Add(new NpcReply("回到营地",()=>SendPartnerHome(id),nearby));
            replies.Add(new NpcReply("退出对话",ClosePanel));
            NpcDialogue(new[]{new NpcLine(BasePersonnel.LeaderName(leader),BaseCommanderIdentity.Appearance(leader),BasePartnerDialogue.Resolve(leader,key),leader.Id==s.PlayerLeaderId)},replies.ToArray());
        }
        private void ResumePartner(string id)
        {
            if (owner.Change(next =>
            {
                if (!BaseInteraction.CanReach(next, owner.PresentedObject(id)))
                    throw new InvalidOperationException("请在伙伴旁交谈");
                if (!BaseAdventure.Resume(next, id))
                    throw new InvalidOperationException(BaseAdventure.PauseProblem(next, id, true));
            }, "")) ClosePanel();
        }

        private void LeavePartnerHere(string id)
        {
            if (owner.Change(next =>
            {
                if (!BaseInteraction.CanReach(next, owner.PresentedObject(id)))
                    throw new InvalidOperationException("请在伙伴旁交谈");
                if (!BaseAdventure.LeaveHere(next, id))
                    throw new InvalidOperationException(BaseAdventure.LeaveHereProblem(next, id));
            }, "")) ClosePanel();
        }

        private void FollowPartner(string id)
        {
            if (BaseAdventure.Full(owner.State)) { OpenFullPartyReply(id, ClosePanel); return; }
            if (owner.Change(next =>
            {
                if (!BaseInteraction.CanReach(next, owner.PresentedObject(id)))
                    throw new InvalidOperationException("请在伙伴旁交谈");
                var person = next.Leaders.Single(l => l.Id == id);
                if (!(BaseAdventure.WaitingHere(next, person) ? BaseAdventure.InviteWaiting(next, person) :
                    BaseAdventure.SetMember(next, id, true)))
                    throw new InvalidOperationException(BaseAdventure.ChangeProblem(next, id, true));
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

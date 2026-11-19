using System.Collections.Generic;
using System.Linq;
using BaseConquest.Story;

namespace BaseConquest.Base
{
    // Session-only cursors: chatting never mutates the simulation or invokes a save transaction.
    public sealed class BasePartnerDialogue
    {
        private readonly Dictionary<string,int> cursors=new Dictionary<string,int>();
        public static string Prefix(BaseState state, BaseLeader leader)
        {
            var flow = state.Demo?.Flow;
            if (flow?.ControllerPledged == true && leader.Id == "partner_recruit:" + flow.FirstControllerId)
                return "story.partner_galan";
            if (BaseDemo.Active(state) && leader.Id.StartsWith("partner_recruit:",System.StringComparison.Ordinal))
                return "story.partner_default";

            return BaseCommanderIdentity.Appearance(leader) switch
            {
                "ahe" => "story.partner_farm",
                "azhu" => "story.partner_craft",
                "chenan" => "story.partner_test",
                _ => "story.partner_default"
            };
        }
        public static string Greeting(BaseState state, BaseLeader leader) => Prefix(state, leader) + ".greeting";
        public static string Resolve(BaseLeader leader,string key)=>StoryText.Get(key,key=="story.partner_galan.camp_chat" ? "村民们都怕我，我躲远点。" : StoryText.Get("story.partner_default.greeting",BasePersonnel.LeaderName(leader)+" · "+BaseCommanderIdentity.RoleName(leader)));
        public string Chat(BaseState state, BaseLeader leader)
        {
            string prefix = Prefix(state, leader);
            if (prefix == "story.partner_galan" && state.Campaign.Location == "base" && !BaseAdventure.Contains(state,leader.Id) &&
                !leader.WaitingAtRecruitment && string.IsNullOrEmpty(leader.FacilityId)) return prefix + ".camp_chat";
            var lines=StoryText.Find("","").Where(e=>e.key.StartsWith(prefix+".chat.",System.StringComparison.Ordinal)).OrderBy(e=>e.key,System.StringComparer.Ordinal).ToArray();
            if(lines.Length==0)return prefix + ".greeting";
            cursors.TryGetValue(leader.Id,out int cursor);cursors[leader.Id]=(cursor+1)%lines.Length;return lines[cursor%lines.Length].key;
        }
    }
}

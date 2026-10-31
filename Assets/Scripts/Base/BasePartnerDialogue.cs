using System.Collections.Generic;
using System.Linq;
using BaseConquest.Story;

namespace BaseConquest.Base
{
    // Session-only cursors: chatting never mutates the simulation or invokes a save transaction.
    public sealed class BasePartnerDialogue
    {
        private readonly Dictionary<string,int> cursors=new Dictionary<string,int>();
        public static string Prefix(BaseLeader leader)
        {
            string appearance=BaseCommanderIdentity.Appearance(leader);
            return "story."+(appearance=="ahe"?"partner_farm":appearance=="azhu"?"partner_craft":appearance=="chenan"?"partner_test":"partner_default");
        }
        public static string Greeting(BaseLeader leader)=>Prefix(leader)+".greeting";
        public static string Resolve(BaseLeader leader,string key)=>StoryText.Get(key,StoryText.Get("story.partner_default.greeting",BasePersonnel.LeaderName(leader)+" · "+BaseCommanderIdentity.RoleName(leader)));
        public string Chat(BaseLeader leader)
        {
            var lines=StoryText.Find("","").Where(e=>e.key.StartsWith(Prefix(leader)+".chat.",System.StringComparison.Ordinal)).OrderBy(e=>e.key,System.StringComparer.Ordinal).ToArray();
            if(lines.Length==0)return Greeting(leader);
            cursors.TryGetValue(leader.Id,out int cursor);cursors[leader.Id]=(cursor+1)%lines.Length;return lines[cursor%lines.Length].key;
        }
    }
}

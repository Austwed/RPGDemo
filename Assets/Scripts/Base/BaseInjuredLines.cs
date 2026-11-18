using System;
using System.Collections.Generic;
using System.Linq;
namespace BaseConquest.Base
{
    public sealed partial class BaseHud
    {
        // 主角生命低于 60% 时，每位 NPC 每日可先说一句带伤应答。见 01C §8.5.1。
        public const double InjuredThreshold=.6;
        private sealed class InjuredLine
        {
            public string Speaker,Art,Text;
        }
        private static readonly Dictionary<string,InjuredLine> InjuredLines=new Dictionary<string,InjuredLine>
        {
            {"INJ_TOWN_GUARD",new InjuredLine{Speaker=BaseScriptNames.Guard,Art="villager",Text="伤成这样还在外头走？先找地方包扎。"}},
            {"INJ_TOWN_MO",new InjuredLine{Speaker=BaseScriptNames.Peddler,Art=BaseScriptNames.Portrait("merchant"),Text="脸都白了！钱币没了还能挣，人可赔不起。"}},
            {"INJ_TOWN_XIAOYUN",new InjuredLine{Speaker=BaseScriptNames.Girl,Art="villager",Text="你的袖子在滴血！先按住伤口，别再走了。"}},
            {"INJ_TOWN_BALONG",new InjuredLine{Speaker=BaseScriptNames.Blacksmith,Art=BaseScriptNames.Portrait("blacksmith"),Text="铁甲破了还能换。你这伤，得老老实实躺一晚。"}},
            {"INJ_TOWN_LEIEN",new InjuredLine{Speaker=BaseScriptNames.Recruiter,Art=BaseScriptNames.Portrait("recruiter"),Text="挂彩了？能回来就能再整队。先把伤养好。"}},
            {"INJ_CAMP_ZHOU",new InjuredLine{Speaker=BaseScriptNames.LaoZhou,Art=BaseScriptNames.Portrait("laozhou"),Text="又是一身伤。进帐躺下，天塌了也等明早。"}},
            {"INJ_CAMP_CHUN",new InjuredLine{Speaker=BaseScriptNames.ChunShen,Art=BaseScriptNames.Portrait("chunshen"),Text="锅里有麦粥。你进去坐下，我给你盛一碗。"}},
            {"INJ_CAMP_AHE",new InjuredLine{Speaker="阿禾",Art="ahe",Text="你流了这么多血。下回叫上我，我能帮你盯后头。"}},
            {"INJ_CAMP_AZHU",new InjuredLine{Speaker="阿铸",Art="azhu",Text="谁把你伤成这样？今晚先歇着，下回叫上我。"}},
            // 迦兰在 Demo 里只是遗迹战队长／收编对话，没有营地点击入口；文案先登记，等有交互位再接。
            {"INJ_CAMP_JIALAN",new InjuredLine{Speaker=BaseScriptNames.Galan,Art=BaseScriptNames.Portrait("galan"),Text="你的气息乱了。回帐躺一夜，别让伤口再裂开。"}}
        };
        public static bool Wounded(BaseState s)
        {
            var leader=s?.Leaders?.FirstOrDefault(l=>l.Id==s.PlayerLeaderId);
            return leader!=null&&BaseAdventure.Alive(leader)&&leader.MaxHealth>0&&leader.Health<leader.MaxHealth*InjuredThreshold;
        }
        public static string InjuredText(BaseState s,string id)
        {
            if(!Wounded(s))return "";
            return InjuredLines.TryGetValue(id??"",out var line)?line.Text:"";
        }
        private static bool CanSpeakInjury(BaseState s,string id)
        {
            return InjuredText(s,id)!="" && (s.InjuryDialogueDay!=s.Day || s.InjuryDialogueIds?.Contains(id)!=true);
        }
        public static bool RecordInjuryDialogue(BaseState s,string id)
        {
            if(!CanSpeakInjury(s,id))return false;
            if(s.InjuryDialogueDay!=s.Day)
            {
                s.InjuryDialogueDay=s.Day;
                s.InjuryDialogueIds=new List<string>();
            }
            (s.InjuryDialogueIds??=new List<string>()).Add(id);
            return true;
        }
        private bool SaveInjuryDialogue(BaseState s,string id)
        {
            if(!CanSpeakInjury(s,id))return false;
            bool recorded=false;
            return owner.Change(next=>recorded=RecordInjuryDialogue(next,id),owner.Message)&&recorded;
        }

        private NpcLine[] WithInjury(BaseState s,string id,params NpcLine[] lines)
        {
            if(!SaveInjuryDialogue(s,id))return lines;
            var line=InjuredLines[id];
            var all=new NpcLine[lines.Length+1];
            all[0]=new NpcLine(line.Speaker,line.Art,line.Text);
            Array.Copy(lines,0,all,1,lines.Length);
            return all;
        }
        private void InjuryLine(BaseState s,string id)
        {
            if(SaveInjuryDialogue(s,id))Line(InjuredLines[id].Text,72,18);
        }
    }
}

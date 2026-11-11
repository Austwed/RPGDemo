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
            {"INJ_TOWN_MAIGE",new InjuredLine{Speaker=BaseScriptNames.InnKeeper,Art=BaseScriptNames.Portrait("innkeeper"),Text="[倒吸凉气] 哎呀，这一身血。二楼那间空着，两枚钱币，热水管够。先把人保住再说。"}},
            {"INJ_TOWN_GUARD",new InjuredLine{Speaker=BaseScriptNames.Guard,Art="villager",Text="甲片裂成这样，仗打得够呛。人活着回来就成，锤子我这儿有，能替你敲回去。"}},
            {"INJ_TOWN_MO",new InjuredLine{Speaker=BaseScriptNames.Peddler,Art=BaseScriptNames.Portrait("merchant"),Text="嘶，这脸色白得像张纸！钱币没了可以再赚，人要是倒了可就本利全空喽。"}},
            {"INJ_TOWN_XIAOYUN",new InjuredLine{Speaker=BaseScriptNames.Girl,Art="villager",Text="[惊呼捂嘴] 血……你袖子一直在滴血！你站着别动，我去叫人！"}},
            {"INJ_TOWN_BALONG",new InjuredLine{Speaker=BaseScriptNames.Blacksmith,Art=BaseScriptNames.Portrait("blacksmith"),Text="人受了伤得睡觉，铁甲被砍豁了口子就得换！别拿自己的肉身去硬抗刀剑！"}},
            {"INJ_TOWN_LEIEN",new InjuredLine{Speaker=BaseScriptNames.Recruiter,Art=BaseScriptNames.Portrait("recruiter"),Text="挂彩了？胜败乃兵家常事，只要队长还没咽气，回营睡一觉、补几个新兵照样翻盘！"}},
            {"INJ_CAMP_ZHOU",new InjuredLine{Speaker=BaseScriptNames.LaoZhou,Art=BaseScriptNames.Portrait("laozhou"),Text="又是一身伤回来。你当我这把老骨头还能替你扛？进帐躺下，天塌了也等明早。"}},
            {"INJ_CAMP_CHUN",new InjuredLine{Speaker=BaseScriptNames.ChunShen,Art=BaseScriptNames.Portrait("chunshen"),Text="[心疼叹气] 旧的没长好，又添新的。锅里给你留了麦粥，喝完再睡。"}},
            {"INJ_CAMP_AHE",new InjuredLine{Speaker="阿禾",Art="ahe",Text="你流了好多血！要是一个人扛不住，千万带上我们一起上阵分担啊！"}},
            {"INJ_CAMP_AZHU",new InjuredLine{Speaker="阿铸",Art="azhu",Text="[拍胸脯] 哪个不开眼的把你伤成这样？歇一晚养好伤，明天叫上我，去砍翻他们！"}},
            // 迦兰在 Demo 里只是遗迹战队长／收编对话，没有营地点击入口；文案先登记，等有交互位再接。
            {"INJ_CAMP_JIALAN",new InjuredLine{Speaker=BaseScriptNames.Galan,Art=BaseScriptNames.Portrait("galan"),Text="你的气息乱了。伤口拖久了，燃脉会慢慢空掉。回帐里躺一夜。"}}
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

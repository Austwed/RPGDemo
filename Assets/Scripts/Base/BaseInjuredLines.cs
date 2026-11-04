using System;
using System.Collections.Generic;
using System.Linq;
namespace BaseConquest.Base
{
    public sealed partial class BaseHud
    {
        // 01C §8.5.1 带伤状态专属反应：主角当前生命 < 60% 时，点对应 NPC 先插播一句，
        // 随后照旧进入常规对白／面板。文案逐字取自 01C，不在代码里另写。
        // 判定不覆盖「倒地／死亡」：Fate 非 active 的角色不参与带伤应答。
        public const double InjuredThreshold=.6;
        private sealed class InjuredLine
        {
            public string Speaker,Art,Text;
        }
        private static readonly Dictionary<string,InjuredLine> InjuredLines=new Dictionary<string,InjuredLine>
        {
            {"INJ_TOWN_MAIGE",new InjuredLine{Speaker=BaseScriptNames.InnKeeper,Art=BaseScriptNames.Portrait("innkeeper"),Text="[倒吸凉气] 哎呀，这一身血。二楼那间空着，两枚钱币，热水管够。先把人保住再说。"}},
            {"INJ_TOWN_GUARD",new InjuredLine{Speaker=BaseScriptNames.Guard,Art=BaseScriptNames.Portrait("guard"),Text="甲片裂成这样，仗打得够呛。人活着回来就成，锤子我这儿有，能替你敲回去。"}},
            {"INJ_TOWN_MO",new InjuredLine{Speaker=BaseScriptNames.Peddler,Art=BaseScriptNames.Portrait("merchant"),Text="嘶，这脸色白得像张纸！钱币没了可以再赚，人要是倒了可就本利全空喽。"}},
            {"INJ_TOWN_XIAOYUN",new InjuredLine{Speaker=BaseScriptNames.Girl,Art=BaseScriptNames.Portrait("girl"),Text="[惊呼捂嘴] 血……你袖子一直在滴血！你站着别动，我去叫人！"}},
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
        // 对白入口用：带伤时把应答插到第一句；不带伤或没有对应文案时原样返回。
        private NpcLine[] WithInjury(BaseState s,string id,params NpcLine[] lines)
        {
            if(!Wounded(s)||!InjuredLines.TryGetValue(id??"",out var line))return lines;
            var all=new NpcLine[lines.Length+1];
            all[0]=new NpcLine(line.Speaker,line.Art,line.Text);
            Array.Copy(lines,0,all,1,lines.Length);
            return all;
        }
        // 面板入口用：在面板顶部补一行，不改变原有行序与按钮。
        private void InjuryLine(BaseState s,string id)
        {
            string text=InjuredText(s,id);
            if(text!="")Line(text,72,18);
        }
    }
}

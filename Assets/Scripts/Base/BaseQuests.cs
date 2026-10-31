using System;
using BaseConquest.Story;
using System.Linq;

namespace BaseConquest.Base
{
    [Serializable] public sealed class BaseQuestState
    {
        public int Version, Revision, AcceptedDay, ProofDay, ClaimedDay;
        public string ProofBattleId="", ProofSiteId="";
        public BaseQuestState Copy()=>(BaseQuestState)MemberwiseClone();
    }
    // First real town commission. Proof and receipt are independent of trimmed battle reports.
    public static class BaseQuests
    {
        public static string Title=>StoryText.Get("story.quest.town_defence.title","营造入门 · 城镇防务委托");
        public const string Contact="town_quest";
        public static void Normalize(BaseState s)
        {
            if(s.Quests!=null&&s.Quests.Version!=0)return;
            if(s.Quests!=null&&(s.Quests.Revision!=0||s.Quests.AcceptedDay!=0||s.Quests.ProofDay!=0||s.Quests.ClaimedDay!=0||!string.IsNullOrEmpty(s.Quests.ProofBattleId)||!string.IsNullOrEmpty(s.Quests.ProofSiteId)))throw new ArgumentException("委托缺版本，拒绝覆盖已有进度");
            s.Quests=new BaseQuestState {Version=1};
        }
        public static void Validate(BaseState s)
        {
            Normalize(s);var q=s.Quests;
            if(q.Version!=1||q.Revision<0||q.Revision>=int.MaxValue||q.AcceptedDay<0||q.AcceptedDay>s.Day||q.ProofDay<0||q.ProofDay>s.Day||q.ClaimedDay<0||q.ClaimedDay>s.Day||q.ProofBattleId==null||q.ProofSiteId==null||q.ProofBattleId.Length>200||q.ProofSiteId.Length>100)throw new ArgumentException("委托版本、日期或记录非法");
            bool proof=q.ProofBattleId!=""||q.ProofSiteId!="";
            if(q.AcceptedDay==0&&(proof||q.ProofDay!=0||q.ClaimedDay!=0)||proof!=(q.ProofDay>0)||q.ProofDay>0&&q.ProofDay<q.AcceptedDay||q.ClaimedDay>0&&(!proof||q.ClaimedDay<q.ProofDay))throw new ArgumentException("委托接取、完成和领取顺序非法");
        }
        private static string OwnedSite(BaseState s)=>s.Campaign.EnemySites.FirstOrDefault(x=>(BaseSceneInstances.Active(s)||x.Id=="outpost")&&x.Owner=="player")?.Id??"";
        public static bool Ready(BaseState s)=>s.Quests.AcceptedDay>0&&s.Quests.ClaimedDay==0&&(s.Quests.ProofDay>0||OwnedSite(s)!="");
        public static string Status(BaseState s)=>s.Quests.ClaimedDay>0 ? "已领取奖励" : Ready(s) ? "目标完成 · 回城交付" : s.Quests.AcceptedDay>0 ? "进行中" : "尚未接取";
        private static string ContactProblem(BaseState s)
        {
            if(BaseDemo.Active(s))return "Demo使用领主遗迹调查，不开放旧防务委托";
            if(s.Aftermath.GameOver)return "游戏已结束";
            if(s.Campaign.HasActiveBattle)return "战斗期间不能交付委托";
            if(s.Campaign.Location!="town")return "前往城镇，与镇务官交互";
            var contact=BaseSpatial.Objects(s,"town").SingleOrDefault(x=>x.Id==Contact);
            if(contact==null)return "此地图没有配置镇务官";
            if(!BaseInteraction.CanReach(s,contact))return "在镇务官边缘一格以内、同层操作";
            if(!s.Leaders.Any(x=>x.Id==s.PlayerLeaderId&&x.Health>0))return "主角无法交互";
            return "";
        }
        public static string AcceptProblem(BaseState s)
        {
            string problem=ContactProblem(s);if(problem!="")return problem;
            return s.Quests.AcceptedDay>0 ? "已接取此委托" : s.Quests.Revision>=int.MaxValue-1 ? "委托记录已达上限" : "";
        }
        public static bool Accept(BaseState s)
        {
            if(AcceptProblem(s)!="")return false;
            s.Quests.AcceptedDay=s.Day;s.Quests.Revision++;return true;
        }
        public static bool RecordVictory(BaseState s,BaseCampaignReport report)
        {
            var q=s.Quests;
            if(q.AcceptedDay==0||q.ProofDay>0||q.ClaimedDay>0||q.Revision>=int.MaxValue-1||s.Aftermath.GameOver||report==null||report.Passive||report.Outcome!="SideAWin"||report.Day<q.AcceptedDay||report.Day>s.Day||!s.Campaign.SettledIds.Contains(report.BattleId)||!s.Campaign.Reports.Any(x=>x.BattleId==report.BattleId&&x.Outcome=="SideAWin"&&!x.Passive))return false;
            q.ProofDay=s.Day;q.ProofBattleId=report.BattleId;q.Revision++;return true;
        }
        public static string ClaimProblem(BaseState s)
        {
            string problem=ContactProblem(s);if(problem!="")return problem;
            var q=s.Quests;
            if(q.ClaimedDay>0)return "奖励已经领取，不会重复发放";
            if(!Ready(s))return q.AcceptedDay==0 ? "先与镇务官接取委托" : "亲自取得一场迎战／进攻胜利，或攻占荒野据点";
            if(q.Revision>=int.MaxValue-1)return "委托记录已达上限";
            if(!s.Backpack.StoryFlags.Contains(BaseBackpack.Story)&&s.Backpack.StoryFlags.Count>=1000)return "剧情条件记录已满";
            if(!BasePartyInventory.PartyFreeCell(s,out _,out _))return "随行成员背包已满，请先腾出一格";
            if(s.Backpack.NextId>=int.MaxValue-1||s.Backpack.Revision>=int.MaxValue-1)return "物品记录已满";
            return "";
        }
        public static bool Claim(BaseState s)
        {
            if(ClaimProblem(s)!="")return false;
            if(!BasePartyInventory.PartyFreeCell(s,out var holder,out int cell))return false;
            s.Backpack.Items.Add(new BaseBagItem {Id="bag:"+s.Backpack.NextId++,ItemId=BaseBackpack.Blueprint,Count=1,Holder=holder,Cell=cell});s.Backpack.Revision++;
            var q=s.Quests;if(q.ProofDay==0){q.ProofDay=s.Day;q.ProofSiteId=OwnedSite(s);}
            if(!s.Backpack.StoryFlags.Contains(BaseBackpack.Story))s.Backpack.StoryFlags.Add(BaseBackpack.Story);
            q.ClaimedDay=s.Day;q.Revision++;return true;
        }
    }
}

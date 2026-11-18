using System;
using System.Collections.Generic;
using System.Linq;

namespace BaseConquest.Base
{
    [Serializable] public sealed class BaseAchievementDefinition
    {
        public string Id,Category,Name,Condition,Template="",ItemId="";
        public int Coins,Food,Materials;
        public BaseAchievementDefinition Copy()=>(BaseAchievementDefinition)MemberwiseClone();
    }
    [Serializable] public sealed class BaseAchievementRecord
    {
        public string Id;
        public int UnlockedDay,ClaimedDay;
        public bool Notified;
        public BaseAchievementRecord Copy()=>(BaseAchievementRecord)MemberwiseClone();
    }
    [Serializable] public sealed class BaseAchievementState
    {
        public int Version;
        public string ActiveNoticeId="";
        public double NoticeSecondsLeft;
        public List<BaseAchievementDefinition> Definitions=new List<BaseAchievementDefinition>();
        public List<BaseAchievementRecord> Records=new List<BaseAchievementRecord>();
        public BaseAchievementState Copy()=>Version==0?new BaseAchievementState():new BaseAchievementState {Version=Version,ActiveNoticeId=ActiveNoticeId,NoticeSecondsLeft=NoticeSecondsLeft,Definitions=Definitions.Select(d=>d.Copy()).ToList(),Records=Records.Select(r=>r.Copy()).ToList()};
    }
    public static class BaseAchievements
    {
        public const string Dawn="dawn",Startup="startup",Ruins="ruins",Sword="sword",Trio="trio",Harvest="harvest",Mercy="mercy",Caution="caution",Reunion="reunion";
        internal const string CautionCondition = "沉眠古窟部署了会受侵蚀的队员后返回备战";
        public static void Enable(BaseState s)
        {
            s.Achievements=new BaseAchievementState {Version=1,Definitions=new List<BaseAchievementDefinition> {
                new BaseAchievementDefinition {Id=Dawn,Category="main",Name="新的一天",Condition="首次在营帐休息并推进至次日",Template="novice_bow"},
                new BaseAchievementDefinition {Id=Startup,Category="main",Name="启动资金",Condition="首次成功修复解锁或升级设施（含营帐）",Materials=10},
                new BaseAchievementDefinition {Id=Ruins,Category="main",Name="遗迹初探",Condition="首次击败遗迹异族队长并获胜",Coins=30},
                new BaseAchievementDefinition {Id=Sword,Category="side",Name="村好剑",Condition="成功购买一把新手短剑",Template="novice_light_helmet"},
                new BaseAchievementDefinition {Id=Trio,Category="side",Name="三人行",Condition="实际随行首次达到主角与两名队友",Template="novice_spear"},
                new BaseAchievementDefinition {Id=Harvest,Category="side",Name="有米下锅",Condition="一次真实日结中粮田实际产出粮食",Food=20},
                new BaseAchievementDefinition {Id=Mercy,Category="hidden",Name="破咒不伤人",Condition="单场遗迹胜利，且我方未对任一受控村民造成有效伤害（可重刷）",Template="novice_light_armor"},
                new BaseAchievementDefinition {Id=Caution,Category="hidden",Name="谨慎为上",Condition=CautionCondition,Coins=20},
                new BaseAchievementDefinition {Id=Reunion,Category="hidden",Name="片刻小别",Condition="在营地外让伙伴暂离，回到等候地点接回随行",ItemId="forging_stone"}
            }};
        }
        public static bool Active(BaseState s)=>s?.Achievements?.Version==1;
        public static BaseAchievementRecord Record(BaseState s,string id)=>s?.Achievements?.Records?.FirstOrDefault(r=>r?.Id==id);
        public static bool Unlock(BaseState s,string id)
        {
            if(!Active(s)||Record(s,id)!=null)return false;
            if(!s.Achievements.Definitions.Any(d=>d.Id==id))throw new ArgumentException("未知成就："+id);
            s.Achievements.Records.Add(new BaseAchievementRecord {Id=id,UnlockedDay=s.Day});return true;
        }
        public static void PartyChanged(BaseState s)
        {if(Active(s)&&BaseAdventure.Members(s).Count()==3)Unlock(s,Trio);}
        public static string NextNotice(BaseState s)=>!Active(s)?null:s.Achievements.Definitions.FirstOrDefault(d=>Record(s,d.Id)?.Notified==false)?.Id;
        public static bool MarkNotified(BaseState s,string id)
        {var r=Active(s)?Record(s,id):null;if(r==null||r.Notified)return false;r.Notified=true;return true;}
        public static string AdvanceNotice(BaseState s,double seconds)
        {
            if(!Active(s))return null;if(double.IsNaN(seconds)||double.IsInfinity(seconds)||seconds<0)throw new ArgumentOutOfRangeException(nameof(seconds));
            var a=s.Achievements;
            if(a.ActiveNoticeId!=""){a.NoticeSecondsLeft=Math.Max(0,a.NoticeSecondsLeft-seconds);if(a.NoticeSecondsLeft<=0)a.ActiveNoticeId="";}
            if(a.ActiveNoticeId=="")
            {string next=NextNotice(s);if(next!=null){MarkNotified(s,next);a.ActiveNoticeId=next;a.NoticeSecondsLeft=3;}}
            return a.ActiveNoticeId==""?null:a.ActiveNoticeId;
        }
        public static string RewardName(BaseAchievementDefinition d)=>d.Template!=""?BasePersonalEquipment.Definition(d.Template).Name+" ×1":d.ItemId!=""?"锻造石 ×1":d.Coins>0?"钱币 ×"+d.Coins:d.Food>0?"粮食 ×"+d.Food:"原料 ×"+d.Materials;
        public static string ClaimProblem(BaseState s,string id)
        {
            if(!Active(s))return "本局未启用成就";
            var d=s.Achievements.Definitions.FirstOrDefault(x=>x.Id==id);var r=Record(s,id);
            if(d==null||r==null)return "尚未解锁";
            if(r.ClaimedDay>0)return "奖励已经领取";
            if(!BasePartyInventory.Peaceful(s)||s.Campaign.HasActiveBattle)return "请在非战斗时领取";
            if(s.Inventory.Coins>100000000-d.Coins||s.Inventory.Food>100000000-d.Food||s.Inventory.Materials>100000000-d.Materials)return "资源库存已满，奖励仍保留";
            if(d.Template!=""||d.ItemId!="")
            {
                if(!BasePartyInventory.PartyFreeCell(s,out _,out _))return "随行背包已满，奖励仍保留";
                if(s.Backpack.Revision>=int.MaxValue-1||s.PersonalEquipment.NextId>=int.MaxValue-401||s.Backpack.NextId>=int.MaxValue-1)return "物品记录已满";
            }
            return "";
        }
        public static bool Claim(BaseState s,string id)
        {
            if(ClaimProblem(s,id)!="")return false;
            var d=s.Achievements.Definitions.Single(x=>x.Id==id);var r=Record(s,id);
            if(d.Template!=""||d.ItemId!="")
            {
                BasePartyInventory.PartyFreeCell(s,out string holder,out int cell);
                if(d.Template!="")s.PersonalEquipment.Warehouse.Add(new BasePersonalItem {Id="personal:"+s.PersonalEquipment.NextId++,Template=d.Template,Quality=2,Holder=holder,Cell=cell});
                else s.Backpack.Items.Add(new BaseBagItem {Id="bag:"+s.Backpack.NextId++,ItemId=d.ItemId,Count=1,Holder=holder,Cell=cell});
                s.Backpack.Revision++;
            }
            s.Inventory.Coins+=d.Coins;s.Inventory.Food+=d.Food;s.Inventory.Materials+=d.Materials;r.ClaimedDay=s.Day;return true;
        }
        public static void Validate(BaseState s)
        {
            var a=s.Achievements;if(a==null||a.Version==0)return;
            if(a.ActiveNoticeId==null||double.IsNaN(a.NoticeSecondsLeft)||double.IsInfinity(a.NoticeSecondsLeft)||a.NoticeSecondsLeft<0||a.NoticeSecondsLeft>3||a.ActiveNoticeId!=""&&Record(s,a.ActiveNoticeId)?.Notified!=true)throw new ArgumentException("成就通知状态非法");
            if(a.Version!=1||a.Definitions?.Count!=9||a.Records==null||a.Definitions.Any(d=>d==null||string.IsNullOrEmpty(d.Id)||string.IsNullOrEmpty(d.Name)||string.IsNullOrEmpty(d.Condition)||!new[]{"main","side","hidden"}.Contains(d.Category)||new[]{d.Coins,d.Food,d.Materials}.Any(n=>n<0||n>1000000)||d.Template==null||d.ItemId==null||d.Template!=""&&BasePersonalEquipment.Definition(d.Template)==null||d.ItemId!=""&&!s.Backpack.Catalog.Any(item=>item.Id==d.ItemId))||a.Definitions.Select(d=>d.Id).Distinct().Count()!=9||a.Definitions.GroupBy(d=>d.Category).Any(g=>g.Count()!=3))throw new ArgumentException("成就定义非法");
            if(a.Records.Any(r=>r==null||!a.Definitions.Any(d=>d.Id==r.Id)||r.UnlockedDay<1||r.UnlockedDay>s.Day||r.ClaimedDay<0||r.ClaimedDay>0&&(r.ClaimedDay<r.UnlockedDay||r.ClaimedDay>s.Day))||a.Records.Select(r=>r.Id).Distinct().Count()!=a.Records.Count)throw new ArgumentException("成就解锁／领奖记录非法");
        }
    }
}

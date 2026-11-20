using System;
using System.Collections.Generic;
using System.Linq;
using BaseConquest.Story;
namespace BaseConquest.Base
{
    [Serializable] public sealed class BaseDemoFlowState
    {
        public int HiredKinds;
        public bool CampConstructionOrdered;
        public bool HideoutRecruitClueSeen, HideoutRecruitCluePending;
        public bool RecruitmentNotice, RecruitOpened, HomeEnterSeen, RestLessonDone, FarmOfferPending, FarmOfferShown, NoviceShop;
        public bool AzhuDuelWon, SmithGiftOffered, ControllerAccepted, ControllerPledged, BanditCleared;
        public BaseSideQuestStage SmithLesson, ZhouTraining;
        public string FirstControllerId="";
        public double FarmX,FarmY,FarmNpcX,FarmNpcY;public bool FarmWalking;
        public bool FirstIntelEligible,FirstIntelOffered;
        public int FreeFirstRaidDay;
        public List<BaseRaidIntelSnapshot> RaidIntel=new List<BaseRaidIntelSnapshot>();
        public List<string> NotifiedRaids=new List<string>();
        public BaseDemoFlowState Copy(){var copy=(BaseDemoFlowState)MemberwiseClone();copy.RaidIntel=RaidIntel?.Select(x=>x.Copy()).ToList();copy.NotifiedRaids=NotifiedRaids?.ToList();return copy;}
    }
    [Serializable] public sealed class BaseRaidIntelEntry
    {
        public string EventId,Title;public int Day;
        public BaseRaidIntelEntry Copy()=>(BaseRaidIntelEntry)MemberwiseClone();
    }
    [Serializable] public sealed class BaseRaidIntelSnapshot
    {
        public int Day,Window;public bool HasRaid;
        public List<BaseRaidIntelEntry> Entries=new List<BaseRaidIntelEntry>();
        public BaseRaidIntelSnapshot Copy()=>new BaseRaidIntelSnapshot{Day=Day,Window=Window,HasRaid=HasRaid,Entries=Entries.Select(x=>x.Copy()).ToList()};
    }
    public static class BaseRaidIntel
    {
        public static bool FillMissingPrices(BaseState s,RpgRules latest)
        {
            var rules=s?.Demo?.Rpg;if(rules==null||rules.IntelPricingConfigured||latest?.IntelPricingConfigured!=true)return false;
            if(latest.ThreeDayIntelPrice<0||latest.FiveDayIntelPrice<0||latest.ThreeDayIntelPrice>100000000||latest.FiveDayIntelPrice>100000000)throw new ArgumentException("商人消息价格非法");
            rules.ThreeDayIntelPrice=latest.ThreeDayIntelPrice;rules.FiveDayIntelPrice=latest.FiveDayIntelPrice;rules.IntelPricingConfigured=true;return true;
        }
        public static bool OfferFirst(BaseState s)
        {
            if(!BaseDemoFlow.Active(s)||!s.Demo.Flow.FirstIntelEligible||s.Demo.Flow.FirstIntelOffered)return false;
            if(!s.Campaign.Events.Any(e=>e.Id==BaseDemoDefense.EventId)&&s.Demo.Stage==BaseDemoStage.RuinsDelivered)BaseDemoDefense.ScheduleOnDelivery(s);
            s.Demo.Flow.FirstIntelOffered=true;return true;
        }
        private static BaseCampaignEvent UnclaimedFirst(BaseState s)
        {
            if(!BaseDemoFlow.Active(s)||!s.Demo.Flow.FirstIntelOffered||s.Demo.Flow.FreeFirstRaidDay!=0)return null;
            return s.Campaign.Events.FirstOrDefault(e=>e.Id==BaseDemoDefense.EventId&&e.Status=="pending");
        }
        public static bool CanClaimFirst(BaseState s)=>UnclaimedFirst(s)!=null;
        public static bool ClaimFirst(BaseState s)
        {
            var e=UnclaimedFirst(s);if(e==null)return false;
            s.Demo.Flow.FreeFirstRaidDay=e.AvailableDay;return true;
        }
        public static bool Known(BaseState s,BaseCampaignEvent e)
        {
            if(e==null)return false;if(!BaseDemo.Active(s)||e.AvailableDay<=s.Day)return true;
            var flow=s.Demo?.Flow;if(flow==null||e.Status!="pending")return false;
            return e.Id==BaseDemoDefense.EventId&&flow.FreeFirstRaidDay==e.AvailableDay||(flow.RaidIntel?.Any(x=>x.Window==5&&x.Entries.Any(i=>i.EventId==e.Id&&i.Day==e.AvailableDay))??false);
        }
        public static BaseRaidIntelSnapshot Get(BaseState s,int window)=>s.Demo.Flow.RaidIntel?.FirstOrDefault(x=>x.Day==s.Day&&x.Window==window);
        public static BaseRaidIntelSnapshot Buy(BaseState s,int window)
        {
            if(!BaseDemoFlow.Active(s)||(window!=3&&window!=5))throw new ArgumentException("消息类型非法");
            var flow=s.Demo.Flow;var existing=Get(s,window);if(existing!=null)return existing;
            var rules=s.Demo.Rpg;if(rules==null||!rules.IntelPricingConfigured)throw new InvalidOperationException("消息价格待配置");
            int price=window==3?rules.ThreeDayIntelPrice:rules.FiveDayIntelPrice;
            if(price<0||price>100000000||s.Inventory.Coins<price)throw new InvalidOperationException("消息价格非法或钱币不足");
            var events=s.Campaign.Events.Where(e=>e.Kind=="defense"&&e.Status=="pending"&&e.AvailableDay>s.Day&&e.AvailableDay<=s.Day+window).OrderBy(e=>e.AvailableDay).ThenBy(e=>e.Id,StringComparer.Ordinal).ToList();
            var snapshot=new BaseRaidIntelSnapshot{Day=s.Day,Window=window,HasRaid=events.Count>0};
            snapshot.Entries=events.Select(e=>new BaseRaidIntelEntry{EventId=e.Id,Title=e.Title,Day=e.AvailableDay}).ToList();
            flow.RaidIntel??=new List<BaseRaidIntelSnapshot>();s.Inventory.Coins-=price;flow.RaidIntel.Add(snapshot);return snapshot;
        }
    }
    public sealed class BaseDailyDelta
    {
        public int Food,Materials,Coins,Supply;
        public static BaseDailyDelta Read(BaseState s)
        {
            if(s.Day>=1000000)return new BaseDailyDelta();
            var copy=s.Copy();int supply=copy.Inventory.Equipment.Where(e=>e.Product=="supply").Sum(e=>e.Count);
            BaseEconomy.CompleteProductionDay(copy,false);
            return new BaseDailyDelta{Food=copy.Inventory.Food-s.Inventory.Food,Materials=copy.Inventory.Materials-s.Inventory.Materials,Coins=copy.Inventory.Coins-s.Inventory.Coins,Supply=copy.Inventory.Equipment.Where(e=>e.Product=="supply").Sum(e=>e.Count)-supply};
        }
    }
    public static class BaseDemoFlow
    {
        public static bool Active(BaseState s)=>BaseDemo.Active(s)&&s.Demo.Flow!=null;
        public static bool Recruited(BaseState s)=>Active(s)&&s.Demo.Flow.HiredKinds==7;
        public static string RecruitProgress(BaseState s)=>string.Join("  ",new[]{BaseTroopKind.Melee,BaseTroopKind.Ranged,BaseTroopKind.Cavalry}.Select((k,n)=>((s.Demo.Flow.HiredKinds&(1<<n))!=0?"■ ":"□ ")+(n==0?"剑士":n==1?"弓兵":"骑兵")+"×1"));
        public static void Hired(BaseState s,BaseTroopKind kind){if(Active(s)&&BaseDemoDefense.Delivered(s)){if(kind!=BaseTroopKind.Mage)s.Demo.Flow.HiredKinds|=1<<(int)kind;s.Demo.Flow.FirstIntelEligible=true;}}
        public static string RestAdvice(BaseState s, BaseDayPreview preview = null)
        {
            // 开局缺工与缺粮显示在日结预估旁，后果由实际日结处理。
            if(!Active(s)||s.Demo.Flow.RestLessonDone||s.Day>2||!Recruited(s)||!s.Demo.Defense.WorkshopRepaired||!s.Demo.Defense.BarracksRepaired)return "";
            if(BaseEconomy.ProductionWorkersAt(s,"workshop")<1)
                return StoryText.Get("story.ui.rest.workshop_shortage","工坊无人值守，今日不生产军需。");
            int missing=(preview??BaseEconomy.Preview(s)).NextMissing;
            return missing==0?"":StoryText.Get("story.ui.rest.food_shortage","明日缺粮 {missing} 份。请增派粮田工人，或到灰岩镇买粮。")
                .Replace("{missing}",missing.ToString());
        }
        public static void Validate(BaseState s)
        {
            if(!Active(s))return;var f=s.Demo.Flow;
            f.RaidIntel??=new List<BaseRaidIntelSnapshot>();
            f.NotifiedRaids??=new List<string>();
            if(!Enum.IsDefined(typeof(BaseSideQuestStage),f.SmithLesson)||!Enum.IsDefined(typeof(BaseSideQuestStage),f.ZhouTraining)||f.ZhouTraining==BaseSideQuestStage.Ready)throw new ArgumentException("营地支线进度非法");
            if(f.NotifiedRaids.Count>100000||f.NotifiedRaids.Any(string.IsNullOrWhiteSpace)||f.NotifiedRaids.Distinct().Count()!=f.NotifiedRaids.Count)throw new ArgumentException("袭击通知记录非法");
            if(f.FreeFirstRaidDay<0||f.FreeFirstRaidDay>1000000||f.RaidIntel.Count>2000000||f.RaidIntel.Any(x=>x==null||x.Day<1||x.Day>s.Day||(x.Window!=3&&x.Window!=5)||x.Entries==null||x.Entries.Any(e=>e==null||string.IsNullOrEmpty(e.EventId)||string.IsNullOrEmpty(e.Title)||e.Day<=x.Day||e.Day>x.Day+x.Window)||x.HasRaid!=(x.Entries.Count>0))||f.RaidIntel.GroupBy(x=>new{x.Day,x.Window}).Any(g=>g.Count()!=1))throw new ArgumentException("商人消息快照非法");
            var controller=s.Aftermath.Prisoners.FirstOrDefault(p=>p.Id==f.FirstControllerId);
            if(f.FirstControllerId==null||f.FirstControllerId!=""&&controller==null||f.ControllerAccepted&&controller==null||f.ControllerPledged&&(!f.ControllerAccepted||controller?.Status!="recruited"))throw new ArgumentException("首个控制者的身份／接纳记录非法");
            if(new[]{f.FarmX,f.FarmY,f.FarmNpcX,f.FarmNpcY}.Any(v=>double.IsNaN(v)||double.IsInfinity(v))||f.HiredKinds<0||f.HiredKinds>7||double.IsNaN(f.FarmX)||double.IsInfinity(f.FarmX)||double.IsNaN(f.FarmY)||double.IsInfinity(f.FarmY))throw new ArgumentException("Demo经营教学状态非法");
        }
        public static bool SmithVisit(BaseState s)=>Active(s)&&s.Demo.RuinsDay>0&&s.Campaign.Location=="town"&&!s.Leaders.Any(l=>l.Id=="partner_craft");
        public static BasePoint SmithApproach(BaseState s)
        {var smith=BaseSpatial.Layout(s,"town")?.Objects.FirstOrDefault(o=>o.Id=="town_smith");return smith==null?new BasePoint(14,7.5):new BasePoint(smith.X+1.5,smith.Y-smith.Height/2-.6);}
    }
}

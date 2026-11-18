using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BaseConquest.Battle;

namespace BaseConquest.Base
{
    [Serializable] public sealed class BaseTownGood
    {
        public string Id;
        public int BuyPrice, SellPrice, DailyStock, Remaining;
        public BaseTownGood Copy() => (BaseTownGood)MemberwiseClone();
    }
    [Serializable] public sealed class BaseTownRecruit
    {
        public BaseTroopKind Kind;
        public int Price, DailyCount, Remaining;
        // 连续没有出现在当日兵源里的天数；达到 2 天后下一次分配强制给 1 个。
        public int MissingDays;
        public SoldierStats Stats;
        public BaseTownRecruit Copy() { var x = (BaseTownRecruit)MemberwiseClone(); x.Stats = Stats?.Clone(); return x; }
    }
    [Serializable] public sealed class BaseTownState
    {
        public int Version, StockDay, Revision, RecruitRulesVersion;
        public List<BaseTownGood> Goods = new List<BaseTownGood>();
        public List<BaseTownRecruit> Recruits = new List<BaseTownRecruit>();
        public BaseTownState Copy() => new BaseTownState { Version=Version, StockDay=StockDay, Revision=Revision, RecruitRulesVersion=RecruitRulesVersion, Goods=Goods.Select(g=>g.Copy()).ToList(), Recruits=Recruits.Select(r=>r.Copy()).ToList() };
    }
    // 报价只在当前状态有效；成功后递增序号，旧按钮和重复确认不能再次扣款。
    public sealed class BaseTownQuote
    {
        public readonly string Item, Stamp;
        public readonly bool Buy, Hire;
        public readonly int Count, Total, Day, Revision;
        internal BaseTownQuote(string item,bool buy,bool hire,int count,int total,BaseState s,string stamp)
        { Item=item; Buy=buy; Hire=hire; Count=count; Total=total; Day=s.Day; Revision=s.Town.Revision; Stamp=stamp; }
    }
    public static class BaseTown
    {
        public static string[] GoodsFor(BaseState s)=>BaseRpg.Active(s)?new[]{"food","materials","supply1","supply2"}:Goods;
        public static readonly string[] Goods = { "food", "materials", "sword1", "bow1", "armor1", "sword2", "bow2", "armor2" };
        private const int ResourceLimit = 100000000;
        public static bool DemoRecruitment(BaseState s)=>BaseDemo.Active(s)&&s.Town?.RecruitRulesVersion>=1;
        public static int TroopCount(BaseState s,BaseTroopKind kind)=>s.Workers.Where(worker=>worker.Kind==kind).Sum(worker=>worker.Count);
        public static int TroopTotal(BaseState s)=>s.Workers.Sum(worker=>worker.Count);
        // 2026-11-04 起与普通兵即死率共用同一个合计口径：N 与 B 都是三兵种之和，编队不影响概率。
        public static double HireProbability(BaseState s)=>BaseDemoCombat.Probability(BaseDemoCombat.BaselineTotal(s.Demo?.Rpg),TroopTotal(s),s.Demo?.Rpg);
        public static double HireProbability(BaseState s,BaseTroopKind kind)=>HireProbability(s);
        // 每日兵源总量：第 1 日固定 6（教学 2/2/2）；此后基准 150% 以内至少 1 人，超过断货。
        public static int DemoDailyTotal(BaseState s)
        {
            if(s.Day<=1)return FirstDayStock*3;
            int baseline=BaseDemoCombat.BaselineTotal(s.Demo?.Rpg);if(baseline<=0)return FirstDayStock*3;
            if(TroopTotal(s)>baseline*(1+StockOverflowRatio))return 0;
            int stock=(int)Math.Floor(baseline*(s.Demo?.Rpg?.RecruitSupplyFactor??.5)*(1-HireProbability(s)));
            return Math.Max(1,stock);
        }
        public static int DemoDailyCount(BaseState s,BaseTroopKind kind)=>s.Town.Recruits.Single(item=>item.Kind==kind).DailyCount;
        public const int FirstDayStock=2;
        public const double StockOverflowRatio=.5;   // 超出基准 50% 即断货
        public const int StockMissingFallback=2;     // 连续缺位 2 天后，下次分配强制给 1 个
        public const double PriceDiscountFloor=.5;   // 报价最低打到基准原价的五折
        // 日初把当日总兵源随机分配到三兵种；连续缺位 2 天的兵种先占 1 个再随机。
        // 种子取生成种子×日期，读档、重进城镇都不会重掷，但每个经营日都不同。
        public static void AllocateStock(BaseState s)
        {
            var offers=s.Town.Recruits.OrderBy(item=>item.Kind).ToList();
            if(offers.Count!=3)throw new InvalidOperationException("城镇兵源必须包含三种兵种");
            if(s.Day<=1)
            {
                foreach(var offer in offers){offer.DailyCount=offer.Remaining=FirstDayStock;offer.MissingDays=0;}
                return;
            }
            int total=DemoDailyTotal(s),left=total;
            var give=new int[3];
            foreach(int index in Enumerable.Range(0,3).Where(i=>offers[i].MissingDays>=StockMissingFallback).OrderByDescending(i=>offers[i].MissingDays).ThenBy(i=>i))
            {if(left<=0)break;give[index]=1;left--;}
            if(left>0)
            {
                int seed=unchecked((s.Demo?.Rpg?.GenerationSeed??7919)*397^s.Day*7919^(int)offers[0].Kind*104729);
                var random=new Random(seed);
                for(int n=0;n<left;n++)give[random.Next(3)]++;
            }
            for(int index=0;index<3;index++)
            {
                var offer=offers[index];
                offer.DailyCount=offer.Remaining=give[index];
                offer.MissingDays=give[index]>0?0:Math.Min(999,offer.MissingDays+1);
            }
        }
        public static int HirePrice(BaseState s,BaseTroopKind kind)
        {
            var offer=s.Town.Recruits.Single(item=>item.Kind==kind);
            if(!DemoRecruitment(s))return offer.Price;
            int baseline=BaseDemoCombat.BaselineTotal(s.Demo?.Rpg);
            if(baseline<=0)return offer.Price;
            // 以基准原价为原点：高于基准每 1% 涨 2%（+50% 即翻倍），低于基准每 1% 降 1%，最低五折。
            double ratio=(double)(TroopTotal(s)-baseline)/baseline;
            double factor=ratio>0?1+(s.Demo?.Rpg?.RecruitPriceSensitivity??2)*ratio:Math.Max(PriceDiscountFloor,1+ratio);
            return (int)Math.Ceiling(offer.Price*factor);
        }
        public static void EnableDemoRecruitment(BaseState s)
        {
            if(!BaseDemo.Active(s)||s.Town.RecruitRulesVersion>1)throw new InvalidOperationException("只能为新Demo初始化兵源规则");
            // 版本 1 是分兵种口径的旧档；升到 2 后重掷当日兵源，避免用旧库存继续报价。
            s.Town.RecruitRulesVersion=2;
            foreach(var offer in s.Town.Recruits){if(BaseRpg.Active(s))offer.Price=s.Demo.Rpg.RecruitBasePrices[(int)offer.Kind];offer.MissingDays=0;}
            AllocateStock(s);
        }
        public static void Normalize(BaseState s)
        {
            if(s.Town!=null && s.Town.Version!=0)return;
            if(s.Town!=null && (s.Town.StockDay!=0 || s.Town.Revision!=0 || s.Town.RecruitRulesVersion!=0 || s.Town.Goods?.Count>0 || s.Town.Recruits?.Count>0))throw new ArgumentException("城镇数据缺版本，拒绝重置已有余量");
            var town=new BaseTownState { Version=1,StockDay=s.Day };
            foreach(string id in Goods)
            {
                bool high=id.EndsWith("2"), armor=id.StartsWith("armor");
                int buy=id=="food" ? 2 : id=="materials" ? 3 : (armor ? 18 : 12)*(high ? 2 : 1);
                int sell=id=="food" || id=="materials" ? 1 : (armor ? 14 : 9)*(high ? 2 : 1);
                int stock=id=="food" ? 80 : id=="materials" ? 40 : high ? 2 : 6;
                town.Goods.Add(new BaseTownGood {Id=id,BuyPrice=buy,SellPrice=sell,DailyStock=stock,Remaining=stock});
            }
            var army=new ArmyDefinition();
            foreach(BaseTroopKind kind in Enum.GetValues(typeof(BaseTroopKind)))
            {
                int count=kind==BaseTroopKind.Melee ? 4 : kind==BaseTroopKind.Ranged ? 3 : 1;
                town.Recruits.Add(new BaseTownRecruit {Kind=kind,Price=kind==BaseTroopKind.Melee ? 30 : kind==BaseTroopKind.Ranged ? 40 : 60,DailyCount=count,Remaining=count,Stats=army.Stats(ToBattle(kind)).Clone()});
            }
            s.Town=town;
        }
        public static void Validate(BaseState s)
        {
            Normalize(s); var t=s.Town;
            if(t.RecruitRulesVersion<0||t.RecruitRulesVersion>2||t.RecruitRulesVersion>0&&!BaseDemo.Active(s))throw new ArgumentException("城镇招兵规则版本非法");
            if(t.Version!=1 || t.StockDay!=s.Day || t.Revision<0 || t.Revision>=int.MaxValue || t.Goods==null || t.Recruits==null)throw new ArgumentException("城镇版本、日期或交易序号非法");
            if(t.Goods.Count!=GoodsFor(s).Length || t.Goods.Any(g=>g==null || !GoodsFor(s).Contains(g.Id) || g.BuyPrice<1 || g.BuyPrice>1000000 || g.SellPrice<1 || g.SellPrice>=g.BuyPrice || g.DailyStock<1 || g.DailyStock>100000 || g.Remaining<0 || g.Remaining>g.DailyStock) || t.Goods.Select(g=>g.Id).Distinct().Count()!=GoodsFor(s).Length)throw new ArgumentException("城镇商品价格或供货余量非法");
            if(t.Recruits.Count!=3 || t.Recruits.Any(r=>r==null || !Enum.IsDefined(typeof(BaseTroopKind),r.Kind) || r.Price<1 || r.Price>1000000 || r.DailyCount<(DemoRecruitment(s)?0:1) || r.DailyCount>10000 || r.Remaining<0 || r.Remaining>r.DailyCount || r.MissingDays<0 || r.MissingDays>999 || !BaseAftermath.ValidStats(r.Stats)) || t.Recruits.Select(r=>r.Kind).Distinct().Count()!=3)throw new ArgumentException("城镇兵源价格、属性或余量非法");
        }
        public static void AdvanceDay(BaseState s)
        {
            Normalize(s);var t=s.Town;
            if(t.StockDay==s.Day)return;
            if(t.StockDay!=s.Day-1)throw new InvalidOperationException("城镇供货必须逐经营日补充");
            t.StockDay=s.Day;
            foreach(var g in t.Goods)g.Remaining=g.DailyStock;
            if(DemoRecruitment(s))AllocateStock(s);else foreach(var r in t.Recruits)r.Remaining=r.DailyCount;
            BasePersonalEquipment.AdvanceDay(s);
        }
        public static SoldierKind ToBattle(BaseTroopKind kind) => kind==BaseTroopKind.Melee ? SoldierKind.Melee : kind==BaseTroopKind.Ranged ? SoldierKind.Ranged : SoldierKind.Cavalry;
        public static string Name(string id) => id=="food" ? "粮草" : id=="materials" ? "原料" : (id.EndsWith("2") ? "二级" : "一级")+BaseEconomy.ProductName(id.Substring(0,id.Length-1));
        private static BaseEquipmentStock Equipment(BaseState s,string id) => s.Inventory.Equipment.Single(e=>e.Product==id.Substring(0,id.Length-1) && e.Level==(id.EndsWith("2") ? 2 : 1));
        public static int Owned(BaseState s,string id) => id=="food" ? s.Inventory.Food : id=="materials" ? s.Inventory.Materials : Equipment(s,id).Count;
        public static int Available(BaseState s,string id) => id=="food" || id=="materials" ? Owned(s,id) : BaseSupply.Available(s,Equipment(s,id).Product,Equipment(s,id).Level);
        public static int TradePrice(BaseState s,string id,bool buy)=>BaseRpg.Active(s)?BaseRpg.Price(s,BaseRpg.Value(s,id),buy):buy?s.Town.Goods.Single(g=>g.Id==id).BuyPrice:s.Town.Goods.Single(g=>g.Id==id).SellPrice;
        public static int MaxTrade(BaseState s,string id,bool buy)
        {
            var g=s.Town.Goods.Single(x=>x.Id==id);
            return buy ? Math.Min(g.Remaining,Math.Min(s.Inventory.Coins/TradePrice(s,id,true),ResourceLimit-Owned(s,id))) : Math.Min(Available(s,id),(ResourceLimit-s.Inventory.Coins)/TradePrice(s,id,false));
        }
        public static int MaxHire(BaseState s,BaseTroopKind kind)
        {
            var r=s.Town.Recruits.Single(x=>x.Kind==kind);
            return Math.Min(r.Remaining,Math.Min(s.Inventory.Coins/HirePrice(s,kind),10000-s.Workers.Sum(w=>w.Count)));
        }
        public static bool AtMerchant(BaseState s) => s.Campaign.Location=="town" &&
            BaseSpatial.Objects(s,"town").Any(o=>(o.Id=="town_peddler"||o.Id=="town_market")&&BaseInteraction.CanReach(s,o));
        internal static bool CanOpenMarket(BaseState s)=>AtCounter(s,false);
        private static bool AtCounter(BaseState s,bool hire)
        {
            if(s.Aftermath.GameOver || BaseSceneCombat.Active(s) || s.Campaign.Location!="town" ||
                BaseBattleLoot.Pending(s) || s.Muster.Parties.Any(p=>p.InCombat))return false;
            var player=s.Leaders.Single(l=>l.Id==s.PlayerLeaderId);
            return player.Health>0 && (hire ? BaseInteraction.At(s,"town","town_recruit") : AtMerchant(s));
        }
        private static string StatsKey(SoldierStats p) => string.Join(":",new double[]{p.Health,p.Damage,p.Defense,p.Range,p.MoveDistance,p.CritChance,p.CritMultiplier,p.Penetration,p.Resistance,(p.HasCombatKind ? 1 : 0),(int)p.CombatKind,(p.HasProfessionKind?1:0),(int)p.ProfessionKind}.Select(n=>n.ToString("R",CultureInfo.InvariantCulture)));
        internal static string Stamp(BaseState s) => BaseMuster.Snapshot(s)+"|trade:"+(BaseRpg.Active(s)?BaseRpg.TradeModifier(s):0)+"|baselines:"+(BaseRpg.Active(s)?string.Join(",",s.Demo.Rpg.TroopBaseline):"")+"|town:"+s.Town.Revision+":"+s.Town.StockDay+":"+s.Town.RecruitRulesVersion+"|"+string.Join("|",s.Town.Goods.Select(g=>g.Id+":"+g.BuyPrice+":"+g.SellPrice+":"+g.DailyStock+":"+g.Remaining))+"|"+string.Join("|",s.Town.Recruits.Select(r=>r.Kind+":"+r.Price+":"+r.DailyCount+":"+r.Remaining+":"+StatsKey(r.Stats)));
        public static BaseTownQuote QuoteTrade(BaseState s,string id,bool buy,int count)
        {
            BaseEconomy.Validate(s);
            if(!AtCounter(s,false))throw new InvalidOperationException("请在城镇接近交易摊位后交易");
            return PreviewTrade(s,id,buy,count) ??
                throw new InvalidOperationException("数量超出钱币、今日供货或可用库存；已锁装备不能出售");
        }
        private static BaseTownQuote PreviewTrade(BaseState s,string id,bool buy,int count,string stamp=null) =>
            count<1 || !s.Town.Goods.Any(g=>g.Id==id) || count>MaxTrade(s,id,buy)
                ? null : new BaseTownQuote(id,buy,false,count,checked(count*TradePrice(s,id,buy)),s,stamp??Stamp(s));
        public static BaseTownQuote QuoteHire(BaseState s,BaseTroopKind kind,int count)
        {
            BaseEconomy.Validate(s);
            if(BaseDemoFlow.Active(s)&&!BaseDemoDefense.Delivered(s))throw new InvalidOperationException("请先完成领主的遗迹任务，取得募兵许可");
            if(!AtCounter(s,true))throw new InvalidOperationException("请在城镇接近募兵处后招募");
            var r=s.Town.Recruits.SingleOrDefault(x=>x.Kind==kind);
            if(r==null || count<1 || count>MaxHire(s,kind))throw new InvalidOperationException("数量超出今日兵源、钱币或总兵力上限");
            return new BaseTownQuote(kind.ToString(),true,true,count,checked(count*HirePrice(s,kind)),s,Stamp(s));
        }
        public static bool Confirm(BaseState s,BaseTownQuote q)
        {
            BaseEconomy.Validate(s);
            if(q==null || q.Day!=s.Day || q.Revision!=s.Town.Revision || s.Town.Revision>=int.MaxValue-1 || !AtCounter(s,q.Hire) || q.Stamp!=Stamp(s))return false;
            BaseTownQuote actual;
            try { actual=q.Hire ? QuoteHire(s,(BaseTroopKind)Enum.Parse(typeof(BaseTroopKind),q.Item),q.Count) : PreviewTrade(s,q.Item,q.Buy,q.Count,q.Stamp); }
            catch(ArgumentException){return false;}catch(InvalidOperationException){return false;}
            if(actual?.Total!=q.Total)return false;
            if(q.Hire)
            {
                var r=s.Town.Recruits.Single(x=>x.Kind.ToString()==q.Item);
                s.Workers.Add(new BaseWorkerBucket { Kind=r.Kind,Count=q.Count,Health=r.Stats.Health,MaxHealth=r.Stats.Health,HasCombatProfile=true,CombatStats=r.Stats.Clone(),UnavailableDay=BaseDemoFlow.Active(s)?0:s.Day });
                r.Remaining-=q.Count;BaseDemoFlow.Hired(s,r.Kind);
            }
            else
            {
                int delta=q.Buy ? q.Count : -q.Count;
                if(q.Item=="food")s.Inventory.Food+=delta;
                else if(q.Item=="materials")s.Inventory.Materials+=delta;
                else Equipment(s,q.Item).Count+=delta;
                if(q.Buy)s.Town.Goods.Single(g=>g.Id==q.Item).Remaining-=q.Count;
            }
            s.Inventory.Coins+=q.Buy ? -q.Total : q.Total; s.Town.Revision++;
            BaseDevelopment.Allocate(s);BaseEconomy.Validate(s);return true;
        }
    }
}

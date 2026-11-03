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
        public static bool DemoRecruitment(BaseState s)=>BaseDemo.Active(s)&&s.Town?.RecruitRulesVersion==1;
        public static int TroopCount(BaseState s,BaseTroopKind kind)=>s.Workers.Where(worker=>worker.Kind==kind).Sum(worker=>worker.Count);
        public static double HireProbability(BaseState s,BaseTroopKind kind)=>BaseDemoCombat.Probability(BaseDemoCombat.Baseline(ToBattle(kind),s.Demo?.Rpg),TroopCount(s,kind),s.Demo?.Rpg);
        // Temporary Demo defaults: daily stock freezes at day start; current manpower reprices each new quote.
        // 第1日固定每兵种 2 人（教学买兵），次日起按基准 B 与实际兵力浮动。
        public static int DemoDailyCount(BaseState s,BaseTroopKind kind)=>s.Day<=1?FirstDayStock:(int)Math.Floor(BaseDemoCombat.Baseline(ToBattle(kind),s.Demo?.Rpg)*(s.Demo?.Rpg?.RecruitSupplyFactor??.5)*(1-HireProbability(s,kind)));
        public const int FirstDayStock=2;
        public static int HirePrice(BaseState s,BaseTroopKind kind)
        {var offer=s.Town.Recruits.Single(item=>item.Kind==kind);return DemoRecruitment(s)?(int)Math.Ceiling(offer.Price*(1+(s.Demo?.Rpg?.RecruitPriceSensitivity??1)*HireProbability(s,kind))):offer.Price;}
        public static void EnableDemoRecruitment(BaseState s)
        {
            if(!BaseDemo.Active(s)||s.Town.RecruitRulesVersion!=0)throw new InvalidOperationException("只能为新Demo初始化兵源规则");
            s.Town.RecruitRulesVersion=1;
            foreach(var offer in s.Town.Recruits){if(BaseRpg.Active(s))offer.Price=s.Demo.Rpg.RecruitBasePrices[(int)offer.Kind];offer.Remaining=offer.DailyCount=DemoDailyCount(s,offer.Kind);}
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
            if(t.RecruitRulesVersion<0||t.RecruitRulesVersion>1||t.RecruitRulesVersion==1&&!BaseDemo.Active(s))throw new ArgumentException("城镇招兵规则版本非法");
            if(t.Version!=1 || t.StockDay!=s.Day || t.Revision<0 || t.Revision>=int.MaxValue || t.Goods==null || t.Recruits==null)throw new ArgumentException("城镇版本、日期或交易序号非法");
            if(t.Goods.Count!=GoodsFor(s).Length || t.Goods.Any(g=>g==null || !GoodsFor(s).Contains(g.Id) || g.BuyPrice<1 || g.BuyPrice>1000000 || g.SellPrice<1 || g.SellPrice>=g.BuyPrice || g.DailyStock<1 || g.DailyStock>100000 || g.Remaining<0 || g.Remaining>g.DailyStock) || t.Goods.Select(g=>g.Id).Distinct().Count()!=GoodsFor(s).Length)throw new ArgumentException("城镇商品价格或供货余量非法");
            if(t.Recruits.Count!=3 || t.Recruits.Any(r=>r==null || !Enum.IsDefined(typeof(BaseTroopKind),r.Kind) || r.Price<1 || r.Price>1000000 || r.DailyCount<(DemoRecruitment(s)?0:1) || r.DailyCount>10000 || r.Remaining<0 || r.Remaining>r.DailyCount || !BaseAftermath.ValidStats(r.Stats)) || t.Recruits.Select(r=>r.Kind).Distinct().Count()!=3)throw new ArgumentException("城镇兵源价格、属性或余量非法");
        }
        public static void AdvanceDay(BaseState s)
        {
            Normalize(s);var t=s.Town;
            if(t.StockDay==s.Day)return;
            if(t.StockDay!=s.Day-1)throw new InvalidOperationException("城镇供货必须逐经营日补充");
            t.StockDay=s.Day;
            foreach(var g in t.Goods)g.Remaining=g.DailyStock;
            foreach(var r in t.Recruits){if(DemoRecruitment(s))r.DailyCount=DemoDailyCount(s,r.Kind);r.Remaining=r.DailyCount;}
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
        private static bool AtCounter(BaseState s,bool hire)
        {
            if(s.Aftermath.GameOver || s.Campaign.HasActiveBattle || s.Campaign.Location!="town" || s.Muster.Parties.Any(p=>p.InCombat))return false;
            var player=s.Leaders.Single(l=>l.Id==s.PlayerLeaderId);
            return player.Health>0 && BaseInteraction.At(s,"town",hire ? "town_recruit" : "town_market");
        }
        private static string StatsKey(SoldierStats p) => string.Join(":",new double[]{p.Health,p.Damage,p.Defense,p.Range,p.MoveDistance,p.CritChance,p.CritMultiplier,p.Penetration,p.Resistance,(p.HasCombatKind ? 1 : 0),(int)p.CombatKind,(p.HasProfessionKind?1:0),(int)p.ProfessionKind}.Select(n=>n.ToString("R",CultureInfo.InvariantCulture)));
        private static string Stamp(BaseState s) => BaseMuster.Snapshot(s)+"|trade:"+(BaseRpg.Active(s)?BaseRpg.TradeModifier(s):0)+"|baselines:"+(BaseRpg.Active(s)?string.Join(",",s.Demo.Rpg.TroopBaseline):"")+"|town:"+s.Town.Revision+":"+s.Town.StockDay+":"+s.Town.RecruitRulesVersion+"|"+string.Join("|",s.Town.Goods.Select(g=>g.Id+":"+g.BuyPrice+":"+g.SellPrice+":"+g.DailyStock+":"+g.Remaining))+"|"+string.Join("|",s.Town.Recruits.Select(r=>r.Kind+":"+r.Price+":"+r.DailyCount+":"+r.Remaining+":"+StatsKey(r.Stats)));
        public static BaseTownQuote QuoteTrade(BaseState s,string id,bool buy,int count)
        {
            BaseEconomy.Validate(s);
            if(!AtCounter(s,false))throw new InvalidOperationException("请在城镇接近交易摊位后交易");
            var g=s.Town.Goods.SingleOrDefault(x=>x.Id==id);
            if(g==null || count<1 || count>MaxTrade(s,id,buy))throw new InvalidOperationException("数量超出钱币、今日供货或可用库存；已锁装备不能出售");
            return new BaseTownQuote(id,buy,false,count,checked(count*TradePrice(s,id,buy)),s,Stamp(s));
        }
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
            try { actual=q.Hire ? QuoteHire(s,(BaseTroopKind)Enum.Parse(typeof(BaseTroopKind),q.Item),q.Count) : QuoteTrade(s,q.Item,q.Buy,q.Count); }
            catch(ArgumentException){return false;}catch(InvalidOperationException){return false;}
            if(actual.Total!=q.Total)return false;
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

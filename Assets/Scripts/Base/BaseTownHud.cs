using System;
using System.Linq;
using BaseConquest.Battle;

namespace BaseConquest.Base
{
    public sealed partial class BaseHud
    {
        private bool townBuying=true;
        private int townQuantity=1;
        private BaseTroopKind townKind;
        private void TownRules(Action back)
        {
            string production=BaseDemoFlow.Active(owner.State)?"当天可安排生产":"当天不生产";
            if(BaseRpg.Active(owner.State)){var r=owner.State.Demo.Rpg;RuleInfo("Demo交易与招兵规则","商品由基础价值计价：买入×"+r.BuyFactor.ToString("0.##")+"向上取整，卖出×"+r.SellFactor.ToString("0.##")+"向下取整，最低1币。商人和坏脾气各取队伍中最强一项相抵。绑定与剧情物品不可出售。\n\n军需只有一个品类，分一级与二级；同一份配给覆盖任意兵种的攻击和防御，不分别购买剑弓甲。商品每日限量，进出与读档不刷新。\n\n普通兵每日兵源按日初人数计算，兵源系数为"+r.RecruitSupplyFactor.ToString("0.##")+"；报价按当前人数更新，敏感度为"+r.RecruitPriceSensitivity.ToString("0.##")+"。新兵"+production+"，从明日计入口粮。",back);return;}
            if(BaseTown.DemoRecruitment(owner.State))
            {
                RuleInfo("Demo招兵规则","普通兵满血加入基地，进入既有岗位、编队、军需与战损流程，不自动补进在外队伍。新兵"+production+"，从明日计入口粮。\n\n第1日固定每兵种 2 人（合计 6）；之后每日兵源总量=floor(B×兵源系数×(1-p))，B＝合计基准 12，在日初按全体实际兵力算一次，再把总量随机分到剑士／弓兵／骑兵三种（可能三个同兵种，也可能缺某个兵种）。只要兵力不超过基准的 150%（12 人基准就是 18 人），当天至少保 1 人可买，超过则断货。某个兵种连续两天没出现在兵源里，第三天必给 1 个。当天招走后不补货，重进或读档不刷新。\n\n单价以基准原价为原点：兵力高于基准每 1% 涨 2%（高出 50%、也就是 18 人时翻倍），低于基准每 1% 降 1%，最低打到五折。每次只能招 1 人，招完立即按新兵力重算报价；人员、日期、钱币或库存改变后旧报价失效。兵力越富余，价格越高、供应越少；打空了则货足价低，靠招兵与俘虏把人补回基准。\n\nB＝剑士 4＋弓兵 4＋骑兵 4＝合计 12，基础价沿用当前快照。p是双方共用的普通兵即死率，N是三种普通兵的合计，包含基地与所有外出队伍，同一士兵只计一次；编成不影响概率，只影响战术。出征调岗不会降低N或全军口粮；只有真实死亡、招募／收编等人员变化才改变兵力。",back);return;
            }
            RuleInfo(BaseScriptNames.Town+"交易与招募规则","交易摊位买卖粮草、原料和两级剑／弓／甲。卖出价低于买入价，价格固定；仅可出售库存中未被军需配额锁定的部分。出售由商人单独回收，不增加今日可购数量。\n\n商品与三种普通兵源每天限量补满；仅在经营日过夜后刷新，重复进出、查看页面和存读档均不补货。买卖与招募不推进日期。\n\n新募士兵满血加入基地普通兵源，按正常单位参与编队、军需、伤亡、休养与岗位管理，不自动追加到在外队伍，也不带劝降的黄色姓名。今日已实际行动，招募当天不生产；次日起按现有策略分配。今天的口粮已经结清，不重复扣粮，新增人员从明天开始计入口粮；今日缺粮效果不会被购物重算。\n\n招募确定成功并消耗钱币；难度中的劝降概率用于俘虏，不影响付费招募。本版先接普通兵，居民、队长雇佣及动态物价后续再做。",back);
        }
        private void OpenTownMarket()
        {
            BeginPanel(BaseScriptNames.Town+" · 交易摊位","town-market:"+townBuying);
            RowButton(townBuying ? "买入 ✓" : "买入",-290,270,()=>{townBuying=true;OpenTownMarket();});
            RowButton(townBuying ? "卖出" : "卖出 ✓",0,270,()=>{townBuying=false;OpenTownMarket();});
            RowButton("规则 i",290,270,()=>TownRules(OpenTownMarket));row+=62;
            Line("钱币 "+owner.State.Inventory.Coins+" · 第"+owner.State.Day+"天 · 过夜后补货",48,21);
            RowButton("购买个人装备",-210,400,OpenPersonalShop);RowButton("出售个人装备",210,400,OpenPersonalSell);row+=60;
            RowButton("购买锻造石",0,820,OpenForgeShop);row+=60;
            RowButton("购买酒类补给",0,820,OpenProgressShop);row+=60;
            foreach(var g in owner.State.Town.Goods)
            {
                string id=g.Id;int available=BaseTown.Available(owner.State,id),owned=BaseTown.Owned(owner.State,id);
                RowButton(BaseTown.Name(id)+" · "+(townBuying ? "买价 "+BaseTown.TradePrice(owner.State,id,true)+"币 · 今日可购 "+g.Remaining+"/"+g.DailyStock : "卖价 "+BaseTown.TradePrice(owner.State,id,false)+"币 · 可卖 "+available+" / 库存 "+owned),0,820,()=>{townQuantity=1;OpenTownGood(id);});row+=59;
            }
            EndPanel();
        }
        private void TownQuantityButtons(int max,Action refresh)
        {
            RowButton("−1",-315,190,()=>{townQuantity=Math.Max(1,townQuantity-1);refresh();},townQuantity>1);
            RowButton("＋1",-105,190,()=>{townQuantity=Math.Min(max,townQuantity+1);refresh();},townQuantity<max);
            RowButton("＋5",105,190,()=>{townQuantity=Math.Min(max,townQuantity+5);refresh();},townQuantity<max);
            RowButton("最多 "+max,315,190,()=>{townQuantity=max;refresh();},max>0);row+=62;
        }
        private void TownConfirm(BaseTownQuote quote,string message,Action refresh)
        {
            if(quote!=null)owner.Change(s=>{if(!BaseTown.Confirm(s,quote))throw new InvalidOperationException("报价已变化，请重新选择数量");},message);
            refresh();
        }
        private void OpenTownGood(string id)
        {
            var s=owner.State;var g=s.Town.Goods.Single(x=>x.Id==id);int max=BaseTown.MaxTrade(s,id,townBuying);
            townQuantity=max==0 ? 0 : Math.Max(1,Math.Min(townQuantity,max));
            BeginPanel("交易 · "+BaseTown.Name(id),"town-good:"+id+":"+townBuying);
            RowButton("返回商品",-210,400,OpenTownMarket);RowButton("规则 i",210,400,()=>TownRules(()=>OpenTownGood(id)));row+=62;
            Line((townBuying ? "买入" : "卖出")+" "+townQuantity+"件 · 单价 "+(townBuying ? BaseTown.TradePrice(owner.State,id,true) : BaseTown.TradePrice(owner.State,id,false))+"币 · "+(townBuying ? "花费 " : "获得 ")+(long)townQuantity*(townBuying ? BaseTown.TradePrice(owner.State,id,true) : BaseTown.TradePrice(owner.State,id,false))+"币",58,23);
            TownQuantityButtons(max,()=>OpenTownGood(id));
            BaseTownQuote quote=null;string reason="";
            if(max>0)try{quote=BaseTown.QuoteTrade(s,id,townBuying,townQuantity);}catch(Exception error){reason=error.Message;}
            RowButton(townBuying ? "确认买入" : "确认卖出",0,820,()=>TownConfirm(quote,(townBuying ? "已买入 " : "已卖出 ")+BaseTown.Name(id)+" ×"+townQuantity,()=>OpenTownGood(id)),quote!=null);row+=62;
            Line("钱币 "+s.Inventory.Coins+" · 库存 "+BaseTown.Owned(s,id)+" · 可卖 "+BaseTown.Available(s,id)+" · 已锁 "+(BaseTown.Owned(s,id)-BaseTown.Available(s,id)),60,20);
            Line("今日可购 "+g.Remaining+" / "+g.DailyStock+" · 本次最多 "+max+(townBuying ? "（钱币可买 "+s.Inventory.Coins/BaseTown.TradePrice(owner.State,id,true)+"件）" : ""),48,20);
            if(max==0)Line(townBuying ? "暂时不能买入：钱币不足、今日供货已空或库存已满。" : "暂时不能卖出：没有可用库存，或钱币已达上限。已锁装备先取消军需准备才能出售。",85,19);
            if(reason!="")Line(reason,70,19);
            EndPanel();
        }
        private void OpenTownRecruit()
        {
            if(BaseDemo.Active(owner.State)&&!BaseDemoDefense.Delivered(owner.State))
            {NpcDialogue(new[]{new NpcLine(BaseScriptNames.Recruiter,BaseScriptNames.Portrait("recruiter"),"［斜睨一眼］生面孔？没领主大人的手令，私募甲兵可是掉脑袋的罪过，请回吧。")},new[]{new NpcReply("先去拜访领主",ClosePanel)});return;}
            // TOWN_01_OPEN：拿到许可后首次进募兵处，由雷恩说明开放范围与建议兵种，只播一次。
            if(BaseDemoFlow.Active(owner.State)&&!owner.State.Demo.Flow.RecruitOpened)
            {
                owner.Change(next=>next.Demo.Flow.RecruitOpened=true,"");
                NpcDialogue(new[]{
                    new NpcLine(BaseScriptNames.Recruiter,BaseScriptNames.Portrait("recruiter"),BaseScriptNames.Lord+"的手令到了！"+BaseScriptNames.Camp+"既已立寨，我这儿的剑士、弓手和骑兵任你挑。"),
                    new NpcLine(BaseScriptNames.Recruiter,BaseScriptNames.Portrait("recruiter"),"刚立营寨百废待兴，建议先将【剑士、弓兵、骑兵】各募一人，能战也能垦荒。")
                },new[]{new NpcReply("开始招募",()=>{ClosePanel();OpenTownRecruit();})});
                return;
            }
            var s=owner.State;var offer=s.Town.Recruits.Single(r=>r.Kind==townKind);int max=BaseTown.MaxHire(s,townKind),price=BaseTown.HirePrice(s,townKind);
            townQuantity=1;// 每次只能买 1 人，价格按买完后的兵力重新报价
            BeginPanel(BaseScriptNames.Town+" · 募兵处","town-recruit");
            foreach(var r in s.Town.Recruits)
            {
                var kind=r.Kind;float x=kind==BaseTroopKind.Melee ? -290 : kind==BaseTroopKind.Ranged ? 0 : 290;
                RowButton((townKind==kind ? "✓ " : "")+BaseEconomy.TroopName(kind)+" · 余 "+r.Remaining+"人",x,270,()=>{townKind=kind;townQuantity=1;OpenTownRecruit();});
            }
            row+=62;
            if(BaseTown.DemoRecruitment(s))
            {
                int count=BaseTown.TroopTotal(s),baseline=BaseDemoCombat.BaselineTotal(s.Demo?.Rpg);double probability=BaseTown.HireProbability(s);
                int daily=s.Town.Recruits.Sum(r=>r.DailyCount);
                Line("全军普通兵 "+count+"人 · 合计基准 "+baseline+" · 普通兵即死率 "+probability.ToString("P0")+"（三兵种共用）· 今日兵源 "+daily+"人",58,19);
                int foodPer=townKind==BaseTroopKind.Cavalry?s.Rules.CavalryFood:townKind==BaseTroopKind.Ranged&&BaseRpg.Active(s)?s.Demo.Rpg.ArcherFood:s.Rules.SoldierFood;
                Line("全军下一日口粮：当前 "+BaseEconomy.FoodRequired(s)+" → 招募后 "+(BaseEconomy.FoodRequired(s)+(long)foodPer*townQuantity)+"；今日已付口粮不重算。",65,18);
                int baselinePrice=offer.Price;int diff=count-baseline;
                Line("基准原价 "+baselinePrice+"币 · 现有"+count+"人"+(diff==0?"（正好等于基准）":diff>0?"（高出基准 "+diff+" 人，约 +"+(diff*100/baseline)+"%，报价每1%涨2%）":"（低于基准 "+(-diff)+" 人，报价每1%降1%，最低五折）")+"；兵源按兵种随机分配，连续两天缺位的次日必出 1 人",56,18);
            }
            Line("招募 1 人 · "+price+"币/人 · 持有 "+s.Inventory.Coins+"币（每次只能招 1 人，招后价格重算）",58,21);
            BaseTownQuote quote=null;string reason="";
            if(max>0)try{quote=BaseTown.QuoteHire(s,townKind,townQuantity);}catch(Exception error){reason=error.Message;}
            BaseConquest.Presentation.DemoUiIcons.Attach(RowButton("确认招募",-210,400,()=>TownConfirm(quote,"已招募 "+BaseEconomy.TroopName(townKind)+" ×"+townQuantity+"，加入基地；"+(BaseDemoFlow.Active(s)?"今日可安排生产":"今日不生产"),OpenTownRecruit),quote!=null),"recruit");
            RowButton("规则 i",210,400,()=>TownRules(OpenTownRecruit));row+=62;
            var p=offer.Stats;
            var kindForDisplay=BaseCampaign.ToBattle(townKind);int profession=BaseRpg.Profession(kindForDisplay);
            double range=BaseRpg.Active(s)?s.Demo.Rpg.Range[profession]:RealtimeCombatRules.Range(kindForDisplay);
            double speed=BaseRpg.Active(s)?s.Demo.Rpg.Speed[profession]:RealtimeCombatRules.Speed(kindForDisplay);
            Line(BaseEconomy.TroopName(townKind)+" · 生命 "+p.Health.ToString("0.#")+"/"+p.Health.ToString("0.#")+" · 攻 "+p.Damage.ToString("0.#")+" · 防 "+p.Defense.ToString("0.#")+" · 射程 "+range.ToString("0.##")+"细格 · 移速 "+speed.ToString("0.##")+"细格／秒",65,19);
            Line("今日可募 "+offer.Remaining+" / "+offer.DailyCount+" · 钱币可募 "+s.Inventory.Coins/price+"人 · 本次只能招 1 人",55,20);
            Line("已有总兵力 "+s.Workers.Sum(w=>w.Count)+" / 10000人（含外出兵力）",45,19);
            if(BaseTown.DemoRecruitment(s))
            {
                Line("今日库存按日初冻结；明日兵源总量预计 "+BaseTown.DemoDailyTotal(s)+"人，按三兵种随机分配（连续缺位两天次日必出 1 人）。兵力越接近基准，货源越少、报价越高。",70,18);
                var last=s.Campaign.Reports.LastOrDefault();if(last!=null)Line("最近一战：永久损失 "+last.Killed+"人 · 伤员 "+last.Wounded+"人 · 返回士兵 "+last.Returned+"人；倒地恢复行动不等于满血。",70,18);
            }
            Line(BaseDemoFlow.Active(s)?"满血加入基地，今天即可安排生产；新兵从明天开始计入口粮。":"加入基地，今日不生产；次日起正常安排岗位。新兵从明天开始计入口粮。",65,19);
            if(max==0)Line("暂时不能招募：钱币不足、今日兵源已空或总兵力达到10000人。",65,19);
            if(reason!="")Line(reason,65,19);
            EndPanel();
        }
    }
}

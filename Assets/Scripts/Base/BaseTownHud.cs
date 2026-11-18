using System;
using System.Collections.Generic;
using System.Linq;
using BaseConquest.Battle;
using BaseConquest.Presentation;
using BaseConquest.Story;
using UnityEngine;
using UnityEngine.UI;

namespace BaseConquest.Base
{
    public sealed partial class BaseHud
    {
        private void TownRules(Action back)
        {
            string production="当天可安排生产";
            if(BaseRpg.Active(owner.State)){var r=owner.State.Demo.Rpg;RuleInfo("Demo交易与招兵规则","商品由基础价值计价：买入×"+r.BuyFactor.ToString("0.##")+"向上取整，卖出×"+r.SellFactor.ToString("0.##")+"向下取整，最低1币。商人和坏脾气各取队伍中最强一项相抵。绑定与剧情物品不可出售。\n\n军需只有一个品类，分一级与二级；同一份配给覆盖任意兵种的攻击和防御，不分别购买剑弓甲。商品每日限量，进出与读档不刷新。\n\n普通兵每日兵源按日初人数计算，兵源系数为"+r.RecruitSupplyFactor.ToString("0.##")+"；报价按当前人数更新，敏感度为"+r.RecruitPriceSensitivity.ToString("0.##")+"。新兵"+production+"，从明日计入口粮。",back);return;}
            if(BaseTown.DemoRecruitment(owner.State))
            {
                RuleInfo("Demo招兵规则","普通兵满血加入基地，进入既有岗位、编队、军需与战损流程，不自动补进在外队伍。新兵"+production+"，从明日计入口粮。\n\n第1日固定每兵种 2 人（合计 6）；之后每日兵源总量=floor(B×兵源系数×(1-p))，B＝合计基准 12，在日初按全体实际兵力算一次，再把总量随机分到剑士／弓兵／骑兵三种（可能三个同兵种，也可能缺某个兵种）。只要兵力不超过基准的 150%（12 人基准就是 18 人），当天至少保 1 人可买，超过则断货。某个兵种连续两天没出现在兵源里，第三天必给 1 个。当天招走后不补货，重进或读档不刷新。\n\n单价以基准原价为原点：兵力高于基准每 1% 涨 2%（高出 50%、也就是 18 人时翻倍），低于基准每 1% 降 1%，最低打到五折。每次只能招 1 人，招完立即按新兵力重算报价；人员、日期、钱币或库存改变后旧报价失效。兵力越富余，价格越高、供应越少；打空了则货足价低，靠招兵与俘虏把人补回基准。\n\nB＝剑士 4＋弓兵 4＋骑兵 4＝合计 12，基础价沿用当前快照。p是双方共用的普通兵即死率，N是三种普通兵的合计，包含基地与所有外出队伍，同一士兵只计一次；编队不影响概率，只影响战术。出征调岗不会降低N或全军口粮；只有真实死亡、招募／收编等人员变化才改变兵力。",back);return;
            }
            RuleInfo(BaseScriptNames.Town+"交易与招募规则","交易摊位买卖粮草、原料和两级剑／弓／甲。卖出价低于买入价，价格固定；仅可出售库存中未被军需配额锁定的部分。出售由商人单独回收，不增加今日可购数量。\n\n商品与三种普通兵源每天限量补满；仅在经营日过夜后刷新，重复进出、查看页面和存读档均不补货。买卖与招募不推进日期。\n\n新募士兵满血加入基地普通兵源，按正常单位参与编队、军需、伤亡与岗位管理，不自动追加到在外队伍，也不带劝降姓名标记。招募当天即可按现有策略排班生产。今天的口粮已经结清，不重复扣粮，新增人员从明天开始计入口粮；今日缺粮效果不会被购物重算。\n\n招募确定成功并消耗钱币；难度中的劝降概率用于俘虏，不影响付费招募。本版先接普通兵，居民、队长雇佣及动态物价后续再做。",back);
        }
        private const string TradeViewKey="town-trade";
        private enum TradeVendor { Merchant, Smith, Recruiter }
        private enum TradeShelf { Goods, Equipment, Intel, Reforge, Recruits }
        private TradeVendor tradeVendor;
        private TradeShelf tradeShelf;
        private string tradePerson="",tradeSelection="",tradeSale="",tradeFeedback="";
        private int tradeQuantity=1,tradeRosterPage;
        private bool tradeBuying=true;
        private readonly Dictionary<string,(Button Button,Color Fill,bool SoldOut)> tradeOffers = new();

        private void OpenTownMarket()=>OpenTownChat("town_peddler");

        private NpcReply TradeEntry(TradeVendor vendor,TradeShelf shelf)
        {
            string label=shelf switch
            {
                TradeShelf.Goods=>StoryText.Get("story.ui.trade.market_entry","买卖东西"),
                TradeShelf.Equipment=>StoryText.Get("story.ui.trade.smith_goods_entry","看看商品"),
                TradeShelf.Intel=>StoryText.Get("story.ui.trade.intel_entry","小道消息"),
                TradeShelf.Reforge=>StoryText.Get("story.ui.trade.reforge_entry","重铸"),
                _=>throw new ArgumentOutOfRangeException(nameof(shelf))
            };
            return new NpcReply(label,()=>StartTrade(vendor,shelf));
        }

        private void StartTrade(TradeVendor vendor,TradeShelf shelf)
        {
            if (vendor == TradeVendor.Merchant)
            {
                if (!BaseTown.CanOpenMarket(owner.State))
                {
                    ClosePanel();
                    return;
                }
                // Town transactions validate the same stall or merchant that opened this panel.
                interactionObject = "";
            }
            tradeVendor=vendor;
            tradePerson=owner.State.PlayerLeaderId;
            tradeRosterPage=0;
            tradeShelf=shelf;
            tradeSelection=tradeSale=tradeFeedback="";
            tradeQuantity=1;
            tradeBuying=true;
            if(shelf==TradeShelf.Intel)PrepareIntelTrade();
            RenderTownTrade(false);
        }

        private static string TradeShelfName(TradeShelf shelf)=>shelf switch
        {
            TradeShelf.Goods=>"买卖东西",
            TradeShelf.Equipment=>"装备",
            TradeShelf.Intel=>"小道消息",
            TradeShelf.Reforge=>"重铸",
            _=>"招募"
        };

        private static void BindTradeButton(Button button,string title,Action click,bool enabled=true)
        {
            button.GetComponentInChildren<Text>(true).text=title;
            button.onClick.RemoveAllListeners();
            button.interactable=enabled;
            if(enabled)button.onClick.AddListener(()=>click());
        }

        private Button TradeButton(Transform parent,string title,Action click,bool enabled=true,string template="TradeAction",bool selected=false)
        {
            var button=CloneSceneUi(template,parent).GetComponent<Button>();
            BindTradeButton(button,title,()=>{if(!owner.Busy)click();},enabled);
            if (selected)
            {
                button.GetComponent<Image>().color = DemoUiTheme.Current.Gold;
                var colors = button.colors;
                colors.disabledColor = Color.white;
                button.colors = colors;
            }
            return button;
        }

        private RectTransform TradeLine(Transform parent,string text)
        {
            var line = CloneSceneUi("TradeText", parent);
            line.GetComponent<Text>().text = text;
            return line;
        }

        private void TradeOffer(Transform list,string id,string title,InventoryItemIcons.Icon? itemIcon=null,bool soldOut=false)
        {
            var button = TradeButton(list,title,()=>SelectTradeOffer(id));
            tradeOffers[id] = (button,button.targetGraphic.color,soldOut);
            StyleTradeOffer(id,tradeSale==""&&tradeSelection==id);
            InventoryItemIcons.LeadLabel(button.GetComponentInChildren<Text>(true), itemIcon ?? InventoryItemIcons.Load(id), 26);
        }

        private void StyleTradeOffer(string id,bool selected)
        {
            if (!tradeOffers.TryGetValue(id,out var row)) return;
            var theme = DemoUiTheme.Current;
            Color fill = selected ? theme.Gold : row.Fill;
            row.Button.targetGraphic.color = row.SoldOut ? Color.Lerp(fill,theme.Muted,.5f) : fill;
        }

        private void SelectTradeOffer(string id)
        {
            string previous = tradeSelection;
            bool fromBag = tradeSale != "";
            tradeSelection = id;
            tradeSale = tradeFeedback = "";
            tradeQuantity = 1;
            if (tradeShelf == TradeShelf.Goods) tradeBuying = true;
            if (!fromBag && (tradeShelf is TradeShelf.Goods or TradeShelf.Equipment or TradeShelf.Reforge))
            {
                StyleTradeOffer(previous,false);
                StyleTradeOffer(id,true);
                RefreshTradeDetails();
            }
            else RenderTownTrade();
        }

        private void TradeCommit(Func<BaseState, bool> action, string message, bool recruitment = false)
        {
            bool showHideoutClue = false;
            bool ok = owner.Change(next =>
            {
                if (!action(next)) throw new InvalidOperationException(
                    StoryText.Get("story.ui.trade.changed", "交易条件已变化，请重新确认"));
                if (recruitment && BaseDemoFlow.Active(next) && BaseBanditHideout.Installed(next) && !next.Demo.Flow.HideoutRecruitClueSeen)
                {
                    next.Demo.Flow.HideoutRecruitClueSeen = true;
                    showHideoutClue = true;
                }
            }, message);
            tradeFeedback = ok ? message : owner.Message;
            if (ok && showHideoutClue) OpenRecruiterHideoutClue();
            else RenderTownTrade();
        }

        private void OpenRecruiterHideoutClue()
        {
            string name = BaseScriptNames.Recruiter;
            string portrait = BaseScriptNames.Portrait("recruiter");
            NpcDialogue(new[]
            {
                new NpcLine(name, portrait, StoryText.Get("story.ui.hideout.recruiter_clue_1",
                    "野外那截破旧城墙背后，藏着个小土匪营地。那帮人隔三岔五就袭扰路过的百姓。")),
                new NpcLine(name, portrait, StoryText.Get("story.ui.hideout.recruiter_clue_2",
                    "你要是有心，就去跟他们碰一碰。营地里都是他们搜刮来的不义之财，你能帮我们处理掉他们，那些东西就算你的辛苦酬劳。")),
                new NpcLine(name, portrait, StoryText.Get("story.ui.hideout.recruiter_clue_3",
                    "要是能打服他们，也能把肯归顺的人招来做事。坏是坏了点，可有了正经差事，或许能帮你的营地早点站稳。"))
            }, Array.Empty<NpcReply>(), onCompleted: () => RenderTownTrade(false));
        }

        private void RenderTownTrade(bool keepScroll=true)
        {
            var state=owner.State;
            var people=BaseAdventure.Members(state).Where(l=>BasePartyInventory.AccessibleLeader(state,l)).ToArray();
            var person=people.FirstOrDefault(l=>l.Id==tradePerson)??people.FirstOrDefault();
            tradePerson=person?.Id??"";
            bool sameView=PanelOpen&&panel!=null&&panelViewKey==TradeViewKey;
            var view=sameView?panel:OpenScenePanel("TownTrade",TradeViewKey);
            var offers=Ui<ScrollRect>(view,"OfferList");
            var details=Ui<ScrollRect>(view,"Details");
            var right=Ui<ScrollRect>(view,"RightBody");
            var rightTabs=Ui<RectTransform>(view,"RightTabs");
            float listPosition=sameView&&keepScroll?offers.verticalNormalizedPosition:1;
            float bagPosition=sameView&&keepScroll?right.verticalNormalizedPosition:1;
            if(sameView)
            {
                ClearPanelChildren(offers.content);
                ClearPanelChildren(details.content);
                ClearPanelChildren(right.content);
                ClearPanelChildren(rightTabs);
            }
            tradeOffers.Clear();
            var vendor=tradeVendor==TradeVendor.Recruiter?BaseScriptNames.Recruiter
                :tradeVendor==TradeVendor.Smith?BaseScriptNames.Blacksmith:BaseScriptNames.Peddler;
            string portrait=tradeVendor==TradeVendor.Recruiter?"recruiter":tradeVendor==TradeVendor.Smith?"blacksmith":"merchant";
            string shelfTitle=CurrentTradeShelfTitle();
            Ui<Text>(view,"Title").text=BaseScriptNames.Town+" · "+shelfTitle;
            Ui<Text>(view,"Wallet").text="钱币 "+state.Inventory.Coins;
            Ui<Text>(view,"SellerName").text=vendor;
            var avatar=Ui<RectTransform>(view,"SellerPortrait").gameObject.GetOrAdd<BattleAvatarGraphic>();
            avatar.AppearanceId=BaseScriptNames.Portrait(portrait);
            avatar.raycastTarget=false;
            Ui<Text>(view,"SellerSpeech").text=tradeShelf==TradeShelf.Reforge||tradeFeedback==""?shelfTitle:tradeFeedback;
            BindTradeButton(Ui<Button>(view,"Return"),"返回对话",()=>{if(!owner.Busy)ReturnToTradeDialogue();});
            BindTradeButton(Ui<Button>(view,"Close"),"关闭",()=>{if(!owner.Busy)ClosePanel();});

            Ui<RectTransform>(view,"Tabs").gameObject.SetActive(false);
            bool showParty=tradeShelf!=TradeShelf.Intel;
            rightTabs.gameObject.SetActive(showParty);
            right.gameObject.SetActive(showParty);
            if(tradeShelf==TradeShelf.Recruits)
            {
                RenderRecruitTrade(offers.content,details.content,rightTabs,right.content);
            }
            else
            {
                RenderTradeShelf(offers.content,details.content,person);
                if(showParty)
                {
                    BuildInventoryPersonTabs(rightTabs,people,tradePerson,member=>{
                        tradePerson=member.Id;
                        tradeSale="";
                        if(tradeShelf==TradeShelf.Reforge)tradeSelection=tradeFeedback="";
                        RenderTownTrade(false);
                    });
                    if(person!=null)
                    {
                        var card=BuildTradeInventoryCard(right.content,person,
                            tradeShelf==TradeShelf.Reforge?tradeSelection:tradeSale,SelectTradeBagItem);
                        var layout=card.gameObject.GetComponent<LayoutElement>()??card.gameObject.AddComponent<LayoutElement>();
                        layout.minWidth=layout.preferredWidth=card.rect.width;
                        layout.preferredHeight=card.rect.height;
                    }
                }
            }
            Canvas.ForceUpdateCanvases();
            offers.verticalNormalizedPosition=listPosition;
            right.verticalNormalizedPosition=bagPosition;
            details.verticalNormalizedPosition=1;
        }

        private string CurrentTradeShelfTitle()
            => tradeVendor==TradeVendor.Smith&&tradeShelf==TradeShelf.Equipment?"商品":TradeShelfName(tradeShelf);

        private void RefreshTradeDetails()
        {
            var details=Ui<ScrollRect>(panel,"Details");
            ClearPanelChildren(details.content);
            var recipient=owner.State.Leaders.FirstOrDefault(l=>l.Id==tradePerson);
            RenderTradeShelf(null,details.content,recipient);
            Ui<Text>(panel,"SellerSpeech").text=tradeShelf==TradeShelf.Reforge||tradeFeedback==""?CurrentTradeShelfTitle():tradeFeedback;
            details.StopMovement();
            details.verticalNormalizedPosition=1;
        }

        private void ReturnToTradeDialogue()
        {
            if(OfferSmithLessonOnTradeExit()) return;
            switch(tradeVendor)
            {
                case TradeVendor.Merchant: OpenTownChat("town_peddler"); break;
                case TradeVendor.Smith: OpenSmithFather(); break;
                default:
                    NpcDialogue(WithInjury(owner.State,"INJ_TOWN_LEIEN",
                        new NpcLine(BaseScriptNames.Recruiter,BaseScriptNames.Portrait("recruiter"),"还缺哪一队的人？接着挑，我把名册拿给你。")),
                        new[]{new NpcReply("开始招募",OpenTownRecruit),new NpcReply("离开",ClosePanel)});
                    break;
            }
        }

        private void SelectTradeBagItem(BaseInventoryEntry item)
        {
            if(owner.Busy||item==null)return;
            tradeFeedback="";
            if(tradeShelf==TradeShelf.Reforge)
            {
                tradeSelection=item.Id;
                tradeQuantity=1;
            }
            else
            {
                tradeSale=item.Id;
            }
            RenderTownTrade();
        }

        private void RenderTradeShelf(RectTransform offers,RectTransform details,BaseLeader person)
        {
            var shelfDetails=tradeSale==""?details:null;
            switch(tradeShelf)
            {
                case TradeShelf.Goods: RenderGoodsTrade(offers,shelfDetails); break;
                case TradeShelf.Equipment: RenderEquipmentTrade(offers,shelfDetails,person); break;
                case TradeShelf.Intel: RenderIntelTrade(offers,details); break;
                case TradeShelf.Reforge: RenderReforgeTrade(offers,details,person); break;
            }
            if(tradeSale!="")RenderTradeSale(details);
        }

        private void TradeQuantity(RectTransform parent,int maximum)
        {
            tradeQuantity=maximum==0?0:Mathf.Clamp(tradeQuantity,1,maximum);
            var controls=CloneSceneUi("TradeQuantity",parent);
            TradeButton(controls,"−",()=>{tradeQuantity--;RefreshTradeDetails();},tradeQuantity>1,"TradeTab");
            TradeLine(controls,tradeQuantity.ToString());
            TradeButton(controls,"＋",()=>{tradeQuantity++;RefreshTradeDetails();},tradeQuantity<maximum,"TradeTab");
            TradeButton(controls,"最多",()=>{tradeQuantity=maximum;RefreshTradeDetails();},tradeQuantity<maximum,"TradeTab");
        }

        private void TradeStockOffer(Transform offers,string id,string name,int price,int remaining)
            => TradeOffer(offers,id,name+" · "+price+"币",soldOut:remaining==0);

        private string PurchaseProblem(int remaining,int price,string capacityProblem)
            => remaining==0 ? "今日售罄" : owner.State.Inventory.Coins<price ? "钱币不足" : capacityProblem;

        private void TradeStockDetails(RectTransform details,string name,int price,int remaining,int dailyStock,int maximum,
            string verb,Func<BaseState,int,bool> submit,string message,string blocked)
        {
            TradeLine(details,name+" · 单价 "+price+"币");
            TradeLine(details,"今日剩余 "+remaining+"/"+dailyStock);

            var controls = CloneSceneUi("TradeQuantity",details);
            Action<int> update = null;
            var decrease = TradeButton(controls,"−",()=>update(tradeQuantity-1),template:"TradeTab");
            var amount = TradeLine(controls,"").GetComponent<Text>();
            var increase = TradeButton(controls,"＋",()=>update(tradeQuantity+1),template:"TradeTab");
            var most = TradeButton(controls,"最多",()=>update(maximum),template:"TradeTab");
            var confirm = TradeButton(details,"",()=>
            {
                int count=tradeQuantity;
                if (count>0) TradeCommit(next=>submit(next,count),message);
            });
            var confirmText = confirm.GetComponentInChildren<Text>(true);

            update = requested =>
            {
                tradeQuantity = maximum==0 ? 0 : Mathf.Clamp(requested,1,maximum);
                amount.text = tradeQuantity.ToString();
                decrease.interactable = tradeQuantity>1;
                increase.interactable = most.interactable = tradeQuantity<maximum;
                confirm.interactable = tradeQuantity>0;
                confirmText.text = "确认"+verb+" · "+(long)price*tradeQuantity+"币";
            };
            update(tradeQuantity);
            if (maximum==0) TradeLine(details,blocked);
        }

        private void RenderGoodsTrade(RectTransform offers,RectTransform details)
        {
            var s=owner.State;
            var supply = s.PersonalEquipment.Supplies.FirstOrDefault(item => item.Item == tradeSelection);
            var good=s.Town.Goods.FirstOrDefault(g=>g.Id==tradeSelection)??s.Town.Goods.FirstOrDefault();
            if(tradeSelection!=BaseEquipmentCrafting.Stone && supply == null)
                tradeSelection=good?.Id??BaseEquipmentCrafting.Stone;
            if(offers!=null)
            {
                foreach(var g in s.Town.Goods)
                    TradeStockOffer(offers,g.Id,BaseTown.Name(g.Id),BaseTown.TradePrice(s,g.Id,true),g.Remaining);
                RenderStoneOffer(offers);
                RenderSupplyOffers(offers);
            }
            if(details==null)return;
            if (supply != null)
            {
                RenderSupplyDetails(details, supply);
            }
            else if(tradeSelection==BaseEquipmentCrafting.Stone)
            {
                RenderStoneDetails(details);
            }
            else
            {
                string id=good.Id, name=BaseTown.Name(id);
                bool buy=tradeBuying;
                int price=BaseTown.TradePrice(s,id,buy), available=BaseTown.Available(s,id);
                string stamp=BaseTown.Stamp(s);
                string verb=buy ? "买入" : "卖出";
                TradeStockDetails(details,name,price,good.Remaining,good.DailyStock,BaseTown.MaxTrade(s,id,buy),
                    verb,(next,count)=>BaseTown.Confirm(next,new BaseTownQuote(id,buy,false,count,checked(count*price),s,stamp)),
                    "已"+verb+" "+name, buy ? PurchaseProblem(good.Remaining,price,"库存已满") : available==0 ? "当前无可卖库存" : "钱币已达上限");
                TradeLine(details,name+" · 库存 "+BaseTown.Owned(s,id)+" · 可卖 "+available);
                TradeButton(details,buy ? "卖出" : "买入",()=>{tradeBuying=!buy;tradeQuantity=1;RefreshTradeDetails();});
            }
            TradeButton(details,"规则 i",()=>TownRules(()=>RenderTownTrade(false)));
        }

        private void OpenTownRecruit()
        {
            if(BaseDemo.Active(owner.State)&&!BaseDemoDefense.Delivered(owner.State))
            {
                NpcDialogue(WithInjury(owner.State,"INJ_TOWN_LEIEN",new NpcLine(BaseScriptNames.Recruiter,BaseScriptNames.Portrait("recruiter"),
                    "外乡人？没爵士的手令，我一个兵也不能给你。")),
                    new[]{new NpcReply("先去拜访领主",ClosePanel)});
                return;
            }
            if(BaseDemoFlow.Active(owner.State)&&!owner.State.Demo.Flow.RecruitOpened)
            {
                owner.Change(next=>next.Demo.Flow.RecruitOpened=true,"");
                NpcDialogue(WithInjury(owner.State,"INJ_TOWN_LEIEN",new[]{
                    new NpcLine(BaseScriptNames.Recruiter,BaseScriptNames.Portrait("recruiter"),
                        "手令给我看看。好，你们营地可以来招剑士、弓兵和骑兵了。"),
                    new NpcLine(BaseScriptNames.Recruiter,BaseScriptNames.Portrait("recruiter"),
                        "三种兵各募一个吧。营里缺人，他们也能下地干活。")
                }),new[]{new NpcReply("开始招募",()=>{ClosePanel();OpenTownRecruit();})});
                return;
            }
            StartTrade(TradeVendor.Recruiter,TradeShelf.Recruits);
        }

        private void RenderRecruitTrade(RectTransform offers,RectTransform details,RectTransform rosterTabs,RectTransform roster)
        {
            var s=owner.State;
            var offer=s.Town.Recruits.FirstOrDefault(r=>r.Kind.ToString()==tradeSelection)??s.Town.Recruits.First();
            tradeSelection=offer.Kind.ToString();
            foreach(var entry in s.Town.Recruits)
                TradeOffer(offers,entry.Kind.ToString(),BaseEconomy.TroopName(entry.Kind)+" · 今日余 "+entry.Remaining);

            var kind=offer.Kind;
            int maximum=BaseTown.MaxHire(s,kind);
            int price=BaseTown.HirePrice(s,kind);
            var quote=maximum>0?BaseTown.QuoteHire(s,kind,1):null;
            var stats=offer.Stats;
            TradeLine(details,BaseEconomy.TroopName(kind)+" · 生命 "+stats.Health.ToString("0.#")+" · 攻 "+stats.Damage.ToString("0.#")+" · 防 "+stats.Defense.ToString("0.#"));
            TradeLine(details,"今日可募 "+offer.Remaining+"/"+offer.DailyCount+" · 全军 "+BaseTown.TroopTotal(s)+"人");
            TradeButton(details,"招募 1 人 · "+price+"币",
                ()=>TradeCommit(next=>BaseTown.Confirm(next,quote),"已招募 "+BaseEconomy.TroopName(kind),recruitment:true),quote!=null);
            if(maximum==0)TradeLine(details,"当前无法招募");
            TradeButton(details,"规则 i",()=>TownRules(()=>RenderTownTrade(false)));

            const int pageSize=18;
            int count=s.Workers.Sum(w=>w.Count),pages=Math.Max(1,(count+pageSize-1)/pageSize);
            tradeRosterPage=Mathf.Clamp(tradeRosterPage,0,pages-1);
            TradeButton(rosterTabs,"上一页",()=>{tradeRosterPage--;RenderTownTrade(false);},tradeRosterPage>0,"TradeTab");
            TradeButton(rosterTabs,(tradeRosterPage+1)+"/"+pages+" 下一页",()=>{tradeRosterPage++;RenderTownTrade(false);},tradeRosterPage+1<pages,"TradeTab");
            foreach(var unit in BasePersonnel.Units(s).Where(u=>!u.IsLeader).Skip(tradeRosterPage*pageSize).Take(pageSize))
                AddPersonnelRow(roster,unit,unit.State,null,available:false,useCurrentStats:true);
            if(count==0)Ui<LayoutElement>(TradeLine(roster,"暂无士兵"),"").flexibleWidth=1;
        }

    }
}

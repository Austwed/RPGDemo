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
        private const string TradeViewKey="town-trade";
        private enum TradeVendor { Merchant, Smith, Recruiter }
        private enum TradeShelf { Goods, Equipment, Intel, Reforge, Recruits }
        private TradeVendor tradeVendor;
        private TradeShelf tradeShelf;
        private string tradePerson="",tradeSelection="",tradeSale="",tradeFeedback="";
        private int tradeQuantity=1;
        private bool tradeBuying=true, tradeFirstOfferOnly;
        private bool CanSwitchTradeMode => tradeShelf is TradeShelf.Goods or TradeShelf.Equipment;
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

        private void StartTrade(TradeVendor vendor,TradeShelf shelf,bool freeFirst=false)
        {
            if (vendor == TradeVendor.Merchant)
            {
                if (!BaseTown.CanOpenMarket(owner.State) && !(freeFirst && BaseRaidIntel.CanClaimFirst(owner.State)))
                {
                    ClosePanel();
                    return;
                }
                // Town transactions validate the same stall or merchant that opened this panel.
                interactionObject = "";
            }
            tradeFirstOfferOnly = freeFirst;
            tradeVendor=vendor;
            tradePerson=owner.State.PlayerLeaderId;
            tradeShelf=shelf;
            tradeSelection=tradeSale=tradeFeedback="";
            tradeQuantity=1;
            tradeBuying=true;
            if (freeFirst) tradeSelection="first-raid";
            else if(shelf==TradeShelf.Intel) PrepareIntelTrade();
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
            StyleTradeOffer(id, (tradeSale == "" ? tradeSelection : tradeSale) == id);
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
            if (CanSwitchTradeMode && !tradeBuying)
            {
                bool bag = BasePartyInventory.Entries(owner.State).Any(item => item.Id == id && item.Holder == tradePerson);
                tradeSale = bag ? id : "";
                tradeSelection = bag ? "" : id;
                tradeFeedback = "";
                tradeQuantity = 1;
                RenderTownTrade();
                return;
            }
            string previous = tradeSelection;
            bool fromBag = tradeSale != "";
            tradeSelection = id;
            tradeSale = tradeFeedback = "";
            tradeQuantity = 1;
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
            bool ok = owner.Change(next =>
            {
                if (!action(next)) throw new InvalidOperationException(
                    StoryText.Get("story.ui.trade.changed", "交易条件已变化，请重新确认"));
                if (recruitment && BaseDemoFlow.Active(next) && BaseBanditHideout.Installed(next) && !next.Demo.Flow.HideoutRecruitClueSeen)
                    next.Demo.Flow.HideoutRecruitCluePending = true;
            }, message);
            if (ok) owner.OpeningAudio?.Play(recruitment ? VillageOpeningAudio.Cue.Recruit : VillageOpeningAudio.Cue.Trade);
            tradeFeedback = ok ? message : owner.Message;
            RenderTownTrade();
        }

        internal bool OfferRecruiterHideoutClueOnExit(Action after = null)
        {
            if (!PanelOpen || panelViewKey != TradeViewKey || tradeShelf != TradeShelf.Recruits) return false;
            var flow = owner.State.Demo?.Flow;
            if (flow?.HideoutRecruitCluePending != true || flow.HideoutRecruitClueSeen) return false;
            if (!owner.Change(next =>
            {
                next.Demo.Flow.HideoutRecruitCluePending = false;
                next.Demo.Flow.HideoutRecruitClueSeen = true;
            }, "")) return false;
            OpenRecruiterHideoutClue(after ?? ClosePanel);
            return true;
        }

        private void OpenRecruiterHideoutClue(Action after)
        {
            string name = BaseScriptNames.Recruiter;
            string portrait = BaseScriptNames.Portrait("recruiter");
            NpcDialogue(new[]
            {
                new NpcLine(name, portrait, StoryText.Get("story.ui.hideout.recruiter_clue_1",
                    "镇外那截破墙背后有个匪窝。过路人常在那里遭劫。")),
                new NpcLine(name, portrait, StoryText.Get("story.ui.hideout.recruiter_clue_2",
                    "你要是肯去清剿，他们抢来的东西就归你。")),
                new NpcLine(name, portrait, StoryText.Get("story.ui.hideout.recruiter_clue_3",
                    "把他们打服了，肯归顺的也能招来替营地做事。"))
            }, Array.Empty<NpcReply>(), onCompleted: after);
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
                ClearPanelChildren(rightTabs.GetComponent<ScrollRect>().content);
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
            BindTradeButton(Ui<Button>(view,"Return"),"返回对话",()=>{if(!owner.Busy)ReturnToTradeDialogue();},!tradeFirstOfferOnly);
            var speech = Ui<RectTransform>(view, "SellerSpeech");
            var mode = view.Find("TradeMode") as RectTransform;
            if (mode == null && CanSwitchTradeMode)
            {
                mode = (RectTransform)MakeButton(view, "", Vector2.zero, new Vector2(Mathf.Max(160, speech.rect.width), 42), SwitchTradeMode).transform;
                mode.gameObject.name = "TradeMode";
                mode.anchorMin = speech.anchorMin;
                mode.anchorMax = speech.anchorMax;
                mode.pivot = new Vector2(.5f, 1f);
                mode.anchoredPosition = speech.anchoredPosition + new Vector2(0, speech.rect.height * (1 - speech.pivot.y) - 60);
                mode.sizeDelta = new Vector2(Mathf.Max(160, speech.rect.width), 42);
            }
            if (mode != null)
            {
                mode.gameObject.SetActive(CanSwitchTradeMode);
                mode.GetComponentInChildren<Text>().text = tradeBuying ? "我想卖点东西" : "我想买点东西";
            }
            BindTradeButton(Ui<Button>(view,"Close"),"关闭",()=>{if(!owner.Busy)ClosePanel();});

            Ui<RectTransform>(view,"Tabs").gameObject.SetActive(false);
            bool showParty=tradeShelf!=TradeShelf.Intel;
            rightTabs.gameObject.SetActive(showParty && tradeShelf != TradeShelf.Recruits);
            right.gameObject.SetActive(showParty);
            if(tradeShelf==TradeShelf.Recruits)
            {
                RenderRecruitTrade(offers.content,details.content,right.content);
            }
            else
            {
                RenderTradeShelf(offers.content,details.content,person);
                if(showParty)
                {
                    BuildInventoryPersonTabs(rightTabs,people,tradePerson,member=>{
                        tradePerson=member.Id;
                        tradeSale="";
                        if (!tradeBuying) tradeSelection = "";
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
            LayoutRecruitmentPanels(view, tradeShelf == TradeShelf.Recruits);
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
            if(OfferSmithLessonOnTradeExit() || OfferRecruiterHideoutClueOnExit(OpenRecruiterDialogue)) return;
            switch(tradeVendor)
            {
                case TradeVendor.Merchant: OpenTownChat("town_peddler"); break;
                case TradeVendor.Smith: OpenSmithFather(); break;
                default: OpenRecruiterDialogue(); break;
            }
        }

        private void OpenRecruiterDialogue() => NpcDialogue(WithInjury(owner.State,"INJ_TOWN_LEIEN",
            new NpcLine(BaseScriptNames.Recruiter,BaseScriptNames.Portrait("recruiter"),"还缺哪一队的人？接着挑，我把名册拿给你。")),
            new[]{new NpcReply("开始招募",OpenTownRecruit),new NpcReply("离开",ClosePanel)});

        private void SelectTradeBagItem(BaseInventoryEntry item)
        {
            if(owner.Busy||item==null)return;
            tradeFeedback="";
            if(tradeShelf==TradeShelf.Reforge)
            {
                tradeSelection=item.Id;
                tradeQuantity=1;
            }
            else if (CanSwitchTradeMode)
            {
                tradeBuying = false;
                tradeSelection = "";
                tradeSale = item.Id;
            }
            RenderTownTrade();
        }

        private void RenderTradeShelf(RectTransform offers,RectTransform details,BaseLeader person)
        {
            if (CanSwitchTradeMode && !tradeBuying)
            {
                RenderSellTrade(offers, details);
                return;
            }
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

        private void SwitchTradeMode()
        {
            tradeBuying = !tradeBuying;
            tradeSelection = tradeSale = tradeFeedback = "";
            tradeQuantity = 1;
            RenderTownTrade(false);
        }

        private void RenderSellTrade(RectTransform offers, RectTransform details)
        {
            var s = owner.State;
            var goods = tradeShelf == TradeShelf.Goods
                ? s.Town.Goods.Where(g => BaseTown.Available(s, g.Id) > 0).ToArray()
                : s.Town.Goods.Take(0).ToArray();
            var items = BasePartyInventory.Entries(s)
                .Where(item => item.Holder == tradePerson && BaseBattleLoot.CanSell(s, item.Id))
                .OrderBy(item => item.Cell).ToArray();

            if (offers != null)
            {
                foreach (var good in goods)
                    TradeOffer(offers, good.Id, $"{BaseTown.Name(good.Id)} · {BaseTown.TradePrice(s, good.Id, false)}币");
                foreach (var item in items)
                    TradeOffer(offers, item.Id, $"{item.Name} ×{item.Count} · {BaseBattleLoot.SellPrice(s, item)}币");
            }
            if (tradeSale != "" && BasePartyInventory.Find(s, tradeSale) != null)
            {
                RenderTradeSale(details);
                return;
            }
            var selected = goods.FirstOrDefault(good => good.Id == tradeSelection);
            if (selected == null)
            {
                TradeLine(details, goods.Length + items.Length == 0 ? "暂无可出售商品" : "请选择要出售的商品");
                return;
            }
            RenderTownResourceTrade(details, selected.Id, selected.Remaining, selected.DailyStock);
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
            string verb,Func<BaseState,int,bool> submit,string message,string blocked,string description=null,string stockText=null)
        {
            TradeLine(details,name+" · 单价 "+price+"币");
            TradeDescription(details, description);
            TradeLine(details,stockText ?? ("今日剩余 "+remaining+"/"+dailyStock));

            var controls = CloneSceneUi("TradeQuantity",details);
            Action<int> update = null;
            var decrease = TradeButton(controls,"−",()=>update(tradeQuantity-1),template:"TradeTab");
            var amount = TradeLine(controls,"").GetComponent<Text>();
            var increase = TradeButton(controls,"＋",()=>update(tradeQuantity+1),template:"TradeTab");
            var most = TradeButton(controls,"最多",()=>update(maximum),template:"TradeTab");
            var confirm = TradeButton(details,"",()=>
            {
                if (maximum==0 && blocked==BagFullException.Notice) { ShowBagFull(); return; }
                int count=tradeQuantity;
                if (count>0) TradeCommit(next=>submit(next,count),message);
            });
            var confirmText = confirm.GetComponentInChildren<Text>(true);

            update = requested =>
            {
                tradeQuantity = maximum==0 ? (blocked==BagFullException.Notice ? 1 : 0) : Mathf.Clamp(requested,1,maximum);
                amount.text = tradeQuantity.ToString();
                decrease.interactable = tradeQuantity>1;
                increase.interactable = most.interactable = tradeQuantity<maximum;
                confirm.interactable = tradeQuantity>0;
                confirmText.text = "确认"+verb+" · "+(long)price*tradeQuantity+"币";
            };
            update(tradeQuantity);
            if (maximum==0 && blocked!=BagFullException.Notice) TradeLine(details,blocked);
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
                RenderTownResourceTrade(details, good.Id, good.Remaining, good.DailyStock);
            }
        }

        private void TradeDescription(RectTransform details, string description)
        {
            if (string.IsNullOrWhiteSpace(description)) return;
            var line = TradeLine(details, description);
            var layout = line.GetComponent<LayoutElement>();
            if (layout != null) layout.preferredHeight = description.Length > 24 ? 66 : 34;
        }

        private void RenderTownResourceTrade(RectTransform details, string id, int remaining, int dailyStock)
        {
            var s = owner.State;
            string name = BaseTown.Name(id);
            bool buy = tradeBuying;
            int price = BaseTown.TradePrice(s, id, buy), available = BaseTown.Available(s, id);
            string stamp = BaseTown.Stamp(s);
            string verb = buy ? "买入" : "卖出";
            string description = id switch
            {
                "food" => StoryText.Get("story.ui.trade.food_description", "用于每日口粮和行军路粮。"),
                "materials" => StoryText.Get("story.ui.trade.materials_description", "用于工坊生产和设施修建。"),
                "supply1" => BaseSupply.GradeText(s, 1),
                "supply2" => BaseSupply.GradeText(s, 2),
                _ => ""
            };
            TradeStockDetails(details, name, price, remaining, dailyStock, BaseTown.MaxTrade(s,id,buy),
                verb, (next,count) => BaseTown.Confirm(next,new BaseTownQuote(id,buy,false,count,checked(count*price),s,stamp)),
                "已"+verb+" "+name, buy ? PurchaseProblem(remaining,price,"库存已满") : available==0 ? "当前无可卖库存" : "钱币已达上限",
                description, buy ? null : "库存 "+BaseTown.Owned(s,id)+" · 可卖 "+available);
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

        private void RenderRecruitTrade(RectTransform offers,RectTransform details,RectTransform roster)
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
            TradeLine(details,BaseEconomy.TroopName(kind)+" · 生命 "+stats.Health.ToString("0")+" · 攻 "+stats.Damage.ToString("0")+" · 防 "+stats.Defense.ToString("0"));
            TradeLine(details,"今日可募 "+offer.Remaining+"/"+offer.DailyCount+" · 全军 "+BaseTown.TroopTotal(s)+"人");
            TradeButton(details,"招募 1 人 · "+price+"币",
                ()=>TradeCommit(next=>BaseTown.Confirm(next,quote),"已招募 "+BaseEconomy.TroopName(kind),recruitment:true),quote!=null);
            if(maximum==0)TradeLine(details,"当前无法招募");

            foreach(var unit in BasePersonnel.Soldiers(s))
                AddPersonnelRow(roster,unit,unit.State,null,available:false);
            if(roster.childCount==0)Ui<LayoutElement>(TradeLine(roster,"暂无士兵"),"").flexibleWidth=1;
        }

    }
}

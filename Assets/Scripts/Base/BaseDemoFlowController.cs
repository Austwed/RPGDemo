using System;
using System.Linq;
using BaseConquest.Presentation;
using BaseConquest.Story;
using UnityEngine;
namespace BaseConquest.Base
{
    public sealed partial class BaseSceneController
    {
        private bool OfferFirstRaidIntel()
        {
            if(!BaseDemoFlow.Active(State)||!State.Demo.Flow.FirstIntelEligible||State.Demo.Flow.FirstIntelOffered)return false;
            if(!Change(s=>{if(!BaseRaidIntel.OfferFirst(s))throw new InvalidOperationException("消息引导不可用");},""))return false;
            if(!BaseRaidIntel.CanClaimFirst(State))return false;
            Hud.OpenFirstRaidIntel();return true;
        }
        private void TickDemoFlow(double seconds=-1)
        {
            if(!BaseDemoFlow.Active(State)||State.Demo.Stage==BaseDemoStage.Waking||BaseDemoPrologue.Village(State)||BaseBattleGateway.MainMenu||Hud.PanelOpen||BaseBattleLoot.Pending(State)||ConstructionMode)return;
            var f=State.Demo.Flow;
            var due=State.Campaign.Events.FirstOrDefault(e=>e.Kind=="defense"&&e.Status=="pending"&&e.AvailableDay<=State.Day&&!(f.NotifiedRaids??new System.Collections.Generic.List<string>()).Contains(e.Id+":"+e.AvailableDay));
            if(due!=null){string key=due.Id+":"+due.AvailableDay;if(Change(s=>{s.Demo.Flow.NotifiedRaids??=new System.Collections.Generic.List<string>();s.Demo.Flow.NotifiedRaids.Add(key);},"袭击通知"))Hud.OpenDemoScoutWarning();return;}
            if(BaseDemoDefense.MerchantReminderDue(State))
            {
                if(CommitChange(s=>s.Demo.Defense.MerchantDone=true,"",false))Hud.OpenDemoMerchant();
                return;
            }
            if(BaseDemoFlow.Recruited(State)&&!f.RecruitmentNotice&&!BaseDemoDefense.Won(State)&&
                State.Campaign.Location=="town"&&BaseInteraction.At(State,"town","town_recruit"))
            {if(Change(s=>s.Demo.Flow.RecruitmentNotice=true,""))Hud.OpenRecruitmentComplete();return;}
            if(State.Campaign.Location=="town"&&State.Demo.RuinsDay>0&&!f.NoviceShop&&new BasePoint(State.PlayerX,State.PlayerY).Distance(BaseDemoFlow.SmithApproach(State))<=2)
            {if(Change(s=>s.Demo.Flow.NoviceShop=true,"铁匠铺已开放新手装备"))Hud.OpenSmithGoods();return;}
            if(State.Campaign.Location!="base"||!f.FarmOfferPending||f.FarmOfferShown)return;
            bool recruited=State.Leaders.Any(l=>l.Id=="partner_farm");
            if(!recruited)
            {
                var obj=BaseSpatial.Objects(State).FirstOrDefault(o=>o.Id=="demo_ahe");if(obj==null)return;
                if(!f.FarmWalking){f.FarmNpcX=obj.X;f.FarmNpcY=obj.Y;f.FarmWalking=true;}
                var objects=BaseSpatial.Objects(State);var board=BaseBattlefield.Board(State);var origin=new BasePoint(f.FarmNpcX,f.FarmNpcY);
                var goal=BaseSpatial.Nearest(State,new BasePoint(f.FarmX,f.FarmY));
                bool Ground(double x,double y)=>BaseSpatial.Geometry(objects,x,y,board.Width,board.Height)&&BaseSpatial.TerrainWalkable(board,x,y,objects);
                if(origin.Distance(goal)>.65)
                {
                    if(farmRoute==null||farmRouteIndex>=farmRoute.Count){farmRoute=BaseMap.FindRoute(origin,goal,null,Ground,false,board.Width,board.Height);farmRouteIndex=0;}
                    if(farmRouteIndex<farmRoute.Count)
                    {var next=farmRoute[farmRouteIndex];double d=origin.Distance(next),step=Math.Min(d,Math.Min(seconds<0?Time.deltaTime:seconds,.05)*2.8);if(d>.001){double x=f.FarmNpcX,y=f.FarmNpcY;BaseSpatial.Move(State,ref x,ref y,(next.X-x)/d*step,(next.Y-y)/d*step);f.FarmNpcX=x;f.FarmNpcY=y;}if(d<=.08)farmRouteIndex++;}
                }
                InvalidateObjects();if(npcViews.TryGetValue("demo_ahe",out var npc))npc.MoveTo(WorldPoint(f.FarmNpcX,f.FarmNpcY));
                if (new BasePoint(f.FarmNpcX, f.FarmNpcY).Distance(goal) > .8 ||
                    !BaseDemoRuins.CanMeet(State, "partner_farm")) return;
            }

            bool offerReady = false;
            if (CommitChange(s =>
            {
                if (!BaseDemoFlow.TryShowFarmOffer(s, recruited)) return;
                offerReady = true;
            }, "", false) && offerReady)
                Hud.OpenAheFarmOffer(recruited);
        }
        private System.Collections.Generic.List<BasePoint> farmRoute;private int farmRouteIndex;
    }
    public sealed partial class BaseHud
    {
        public void OpenFirstRaidIntel()
        {
            string line = StoryText.Get("story.ui.intel.first_offer",
                "东家，回营前听我一句。黑鸦寨有点动静，这条消息算我送你的。");
            NpcDialogue(new[] { new NpcLine(BaseScriptNames.Merchant, BaseScriptNames.Portrait("merchant"), line) },
                new[] { new NpcReply("那我看看", () => StartTrade(TradeVendor.Merchant, TradeShelf.Intel, freeFirst: true)) });
        }

        private void PrepareIntelTrade()
        {
            var s=owner.State;
            if(s.Demo.Rpg?.IntelPricingConfigured==false)
            {
                RpgRules latest=null;
                try{latest=DemoBalanceRuntime.Load().Rules;}catch(InvalidOperationException){}catch(ArgumentException){}
                if(latest?.IntelPricingConfigured==true)owner.Change(next=>BaseRaidIntel.FillMissingPrices(next,latest),"");
            }
        }

        private void RenderIntelTrade(RectTransform offers,RectTransform details)
        {
            var s=owner.State;
            bool configured=s.Demo.Rpg?.IntelPricingConfigured==true;
            bool free = BaseRaidIntel.CanClaimFirst(s);
            bool claimed = s.Demo.Flow.FreeFirstRaidDay > 0;
            bool showFree = free && tradeSelection == "" || (free || claimed) && tradeSelection == "first-raid";
            int selected=tradeSelection=="10"?10:5;
            tradeSelection=showFree ? "first-raid" : selected.ToString();
            if (free || showFree) TradeOffer(offers, "first-raid", StoryText.Get("story.ui.intel.first_reopen", "免费首防消息"));
            foreach(int days in tradeFirstOfferOnly ? Array.Empty<int>() : new[]{5,10})
            {
                var existing=BaseRaidIntel.Get(s,days);
                int price=RaidIntelPrice(s,days);
                string name = days == 5 ? "五日军情" : "十日军情";
                TradeOffer(offers, days.ToString(), name + (existing != null || !configured ? "" : " · " + price + "币"));
            }

            if (showFree)
            {
                if (claimed)
                {
                    TradeLine(details, StoryText.Get("story.ui.intel.first_claimed","黑鸦寨第{day}天就到。这几天赶紧备些粮和军需。")
                        .Replace("{day}", s.Demo.Flow.FreeFirstRaidDay.ToString()));
                }
                else
                {
                    TradeLine(details, "首防的确切日期");
                    TradeButton(details, "领取免费首防消息 · 0币", () =>
                        TradeCommit(BaseRaidIntel.ClaimFirst, "已记录首防日期"));
                }
                return;
            }

            var snapshot=BaseRaidIntel.Get(s,selected);
            if(snapshot==null)
            {
                int price=RaidIntelPrice(s,selected);
                TradeLine(details,selected==5?"未来五日 · 确切袭击日期":"未来十日 · 确切日期与每波兵力");
                TradeButton(details,(selected==5?"购买五日军情":"购买十日军情")+" · "+price+"币",
                    ()=>TradeCommit(next=>RaidIntelPrice(next,selected)==price&&BaseRaidIntel.Buy(next,selected)!=null,"消息已记录"),
                    configured&&s.Inventory.Coins>=price);
                if(!configured)TradeLine(details,"消息价格待配置");
                else if(s.Inventory.Coins<price)TradeLine(details,"钱币不足");
                return;
            }

            bool Active(BaseRaidIntelEntry entry)=>s.Campaign.Events.Any(e=>e.Id==entry.EventId&&e.Status=="pending"&&e.AvailableDay==entry.Day);
            if(!snapshot.HasRaid)TradeLine(details,"次日起 "+selected+" 天：无已知袭击。");
            else foreach(var entry in snapshot.Entries)
                TradeLine(details,entry.Title+" · 第 "+entry.Day+" 天"+(string.IsNullOrEmpty(entry.Composition)?"":" · "+entry.Composition)+(Active(entry)?"":" · 已失效"));
        }

        private static int RaidIntelPrice(BaseState s,int days)=>days==5?(s.Demo.Rpg?.ThreeDayIntelPrice??0):(s.Demo.Rpg?.FiveDayIntelPrice??0);

        public void OpenDemoMerchant()
        {
            string warning=StoryText.Get("story.scene.defense.merchant_bandit_lead",
                "黑鸦寨那边有动静。我回镇上摆摊，你要是想听仔细的，带几个钱来找我。");
            NpcDialogue(new[]{new NpcLine(BaseScriptNames.Merchant,BaseScriptNames.Portrait("merchant"),warning)},
                Array.Empty<NpcReply>(),onCompleted:ClosePanel);
        }
        public void OpenRecruitmentComplete()=>NpcDialogue(new[]{
            new NpcLine(BaseScriptNames.Recruiter,BaseScriptNames.Portrait("recruiter"),"人齐了。新募的弟兄今儿就能下田，也能进工坊。"),
            new NpcLine(BaseScriptNames.Recruiter,BaseScriptNames.Portrait("recruiter"),"古窟底下还有余财。再打赢，领那份赏要费些体力。"),
            new NpcLine(BaseScriptNames.Recruiter,BaseScriptNames.Portrait("recruiter"),"记着，那股黑风伤人，普通弟兄进去得当心。")
        },new[]{new NpcReply("回营地安排生产",ClosePanel)});
        public void OpenSmithGoods()=>NpcDialogue(new[]{new NpcLine(BaseScriptNames.Blacksmith,BaseScriptNames.Portrait("blacksmith"),"听说古窟的事办成了？我新打了些甲和兵刃，过来看看。")},new[]{new NpcReply("去看看新装备",ClosePanel)});
        public void OpenAheFarmOffer(bool recruited)
        {
            var lines=new[]{
                new NpcLine("阿禾","ahe","我在田埂上长大的。这块地让我管吧，什么时候浇水我心里有数。")
            };
            NpcDialogue(lines,new[]{
                new NpcReply(recruited?"知道了":"邀请阿禾加入",()=>{if(recruited)ClosePanel();else owner.JoinDemoPartner("partner_farm", false);}),
                new NpcReply("这次先不安排",()=>OpenAheResponse("成，先照旧安排。往后田里缺人，再来叫我。"))
            });
        }
    }
}

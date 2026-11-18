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
            if(!Change(s=>{if(!BaseRaidIntel.OfferFirst(s))throw new InvalidOperationException("消息引导不可用");},"消息引导"))return false;
            if(!BaseRaidIntel.CanClaimFirst(State))return false;
            Hud.OpenFirstRaidIntel();return true;
        }
        private void TickDemoFlow(double seconds=-1)
        {
            if(!BaseDemoFlow.Active(State)||State.Demo.Stage==BaseDemoStage.Waking||BaseDemoPrologue.Village(State)||BaseBattleGateway.MainMenu||Hud.PanelOpen||BaseBattleLoot.Pending(State)||ConstructionMode)return;
            if(BaseDemoFinale.Rebuilding(State))return;
            var f=State.Demo.Flow;
            var due=State.Campaign.Events.FirstOrDefault(e=>e.Kind=="defense"&&e.Status=="pending"&&e.AvailableDay<=State.Day&&!(f.NotifiedRaids??new System.Collections.Generic.List<string>()).Contains(e.Id+":"+e.AvailableDay));
            if(due!=null){string key=due.Id+":"+due.AvailableDay;if(Change(s=>{s.Demo.Flow.NotifiedRaids??=new System.Collections.Generic.List<string>();s.Demo.Flow.NotifiedRaids.Add(key);},"袭击通知"))Hud.OpenDemoScoutWarning();return;}
            if(State.Campaign.Location=="base"&&BaseDemoDefense.Won(State)&&State.Demo.Defense.BatuStoryDay>0&&!State.Demo.Defense.MerchantDone)
            {TickDemoMerchant(seconds);return;}
            if(BaseDemoFlow.Recruited(State)&&!f.RecruitmentNotice)
            {if(Change(s=>s.Demo.Flow.RecruitmentNotice=true,"招募建设人手已完成"))Hud.OpenRecruitmentComplete();return;}
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
                if(new BasePoint(f.FarmNpcX,f.FarmNpcY).Distance(goal)>.8||new BasePoint(f.FarmNpcX,f.FarmNpcY).Distance(new BasePoint(State.PlayerX,State.PlayerY))>2)return;
            }
            if(Change(s=>{s.Demo.Flow.FarmOfferShown=true;s.Demo.Flow.FarmOfferPending=false;if(!recruited&&!BaseDemoRuins.Meet(s,"partner_farm"))throw new InvalidOperationException("请靠近阿禾交谈");},"阿禾向你介绍种植特性"))Hud.OpenAheFarmOffer(recruited);
        }
        private System.Collections.Generic.List<BasePoint> merchantRoute;private int merchantRouteIndex;
        private void TickDemoMerchant(double seconds)
        {
            var progress=State.Demo.Defense;var marker=State.Campaign.Layouts.Single(map=>map.Region=="base").Objects.FirstOrDefault(obj=>obj.Id=="demo_merchant");if(marker==null)return;
            var objects=BaseSpatial.Objects(State);var board=BaseBattlefield.Board(State);var origin=new BasePoint(progress.MerchantX,progress.MerchantY);var goal=BaseSpatial.Nearest(State,new BasePoint(State.PlayerX,State.PlayerY));
            bool Ground(double x,double y)=>BaseSpatial.Geometry(objects,x,y,board.Width,board.Height)&&BaseSpatial.TerrainWalkable(board,x,y,objects);
            if(origin.Distance(goal)>1.5)
            {
                if(merchantRoute==null||merchantRouteIndex>=merchantRoute.Count){merchantRoute=BaseMap.FindRoute(origin,goal,null,Ground,false,board.Width,board.Height);merchantRouteIndex=0;}
                if(merchantRouteIndex<merchantRoute.Count){var next=merchantRoute[merchantRouteIndex];double distance=origin.Distance(next),step=Math.Min(distance,Math.Min(seconds<0?Time.deltaTime:seconds,.05)*2.8);if(distance>.001){double x=progress.MerchantX,y=progress.MerchantY;BaseSpatial.Move(State,ref x,ref y,(next.X-x)/distance*step,(next.Y-y)/distance*step);progress.MerchantX=x;progress.MerchantY=y;}if(distance<=.08)merchantRouteIndex++;}
                marker.X=progress.MerchantX;marker.Y=progress.MerchantY;InvalidateObjects();if(npcViews.TryGetValue("demo_merchant",out var npc))npc.MoveTo(WorldPoint(progress.MerchantX,progress.MerchantY));
                if(new BasePoint(progress.MerchantX,progress.MerchantY).Distance(new BasePoint(State.PlayerX,State.PlayerY))>2)return;
            }
            Hud.OpenDemoMerchant();
        }
        private System.Collections.Generic.List<BasePoint> farmRoute;private int farmRouteIndex;
    }
    public sealed partial class BaseHud
    {
        public void OpenFirstRaidIntel()
        {
            string offer = StoryText.Get("story.ui.intel.first_offer", "东家，回营前听我一句。黑鸦寨有点动静，这条消息算我送你的。");
            NpcDialogue(new[]{new NpcLine(BaseScriptNames.Merchant,BaseScriptNames.Portrait("merchant"),offer)},new[]{
                new NpcReply("领取免费首防消息",()=>{
                    if(!owner.Change(next=>{if(!BaseRaidIntel.ClaimFirst(next))throw new InvalidOperationException("首防消息不可用");},"已记录首防日期"))return;
                    string text=StoryText.Get("story.ui.intel.first_claimed","黑鸦寨第 {day} 天来袭。回去看看日历，趁早备好粮食和军需。")
                        .Replace("{day}",owner.State.Demo.Flow.FreeFirstRaidDay.ToString());
                    NpcDialogue(new[]{new NpcLine(BaseScriptNames.Merchant,BaseScriptNames.Portrait("merchant"),text)},new[]{new NpcReply("知道了",ClosePanel)});
                }),new NpcReply("离开",ClosePanel)});
        }
        private void PrepareIntelTrade()
        {
            var s=owner.State;
            if(s.Demo.Rpg?.IntelPricingConfigured==false)
            {
                RpgRules latest=null;
                try{latest=DemoBalanceRuntime.Load().Rules;}catch(InvalidOperationException){}catch(ArgumentException){}
                if(latest?.IntelPricingConfigured==true)owner.Change(next=>BaseRaidIntel.FillMissingPrices(next,latest),"消息价格已载入");
            }
        }

        private void RenderIntelTrade(RectTransform offers,RectTransform details)
        {
            var s=owner.State;
            bool configured=s.Demo.Rpg?.IntelPricingConfigured==true;
            bool free = BaseRaidIntel.CanClaimFirst(s);
            bool showFree = free && (tradeSelection == "first-raid" || tradeSelection == "");
            int selected=tradeSelection=="5"?5:3;
            tradeSelection=showFree ? "first-raid" : selected.ToString();
            if (free) TradeOffer(offers, "first-raid", StoryText.Get("story.ui.intel.first_reopen", "免费首防消息"));
            foreach(int days in new[]{3,5})
            {
                var existing=BaseRaidIntel.Get(s,days);
                int price=RaidIntelPrice(s,days);
                TradeOffer(offers,days.ToString(),days+"日消息 · "+(existing!=null?"免费复习":configured?price+"币":"价格待配置"));
            }

            if (showFree)
            {
                TradeButton(details, "查看首防消息", OpenFirstRaidIntel);
                return;
            }

            var snapshot=BaseRaidIntel.Get(s,selected);
            if(snapshot==null)
            {
                int price=RaidIntelPrice(s,selected);
                TradeLine(details,selected==3?"三日消息 · 查看未来三天是否有袭击":"五日消息 · 查看未来五天的具体袭击");
                TradeButton(details,"购买 "+selected+" 日消息 · "+price+"币",
                    ()=>TradeCommit(next=>RaidIntelPrice(next,selected)==price&&BaseRaidIntel.Buy(next,selected)!=null,"消息已记录"),
                    configured&&s.Inventory.Coins>=price);
                if(!configured)TradeLine(details,"消息价格待配置");
                else if(s.Inventory.Coins<price)TradeLine(details,"钱币不足");
                return;
            }

            bool Active(BaseRaidIntelEntry entry)=>s.Campaign.Events.Any(e=>e.Id==entry.EventId&&e.Status=="pending"&&e.AvailableDay==entry.Day);
            if(!snapshot.HasRaid)TradeLine(details,"次日起 "+selected+" 天：无已知袭击。");
            else if(selected==3)TradeLine(details,snapshot.Entries.Any(Active)?"次日起三天内有袭击。":"原消息已失效。");
            else foreach(var entry in snapshot.Entries)
                TradeLine(details,entry.Title+" · 第 "+entry.Day+" 天"+(Active(entry)?"":" · 已失效"));
        }

        private static int RaidIntelPrice(BaseState s,int days)=>days==3?(s.Demo.Rpg?.ThreeDayIntelPrice??0):(s.Demo.Rpg?.FiveDayIntelPrice??0);

        public void OpenDemoMerchant()
        {
            string warning=StoryText.Get("story.scene.defense.merchant_bandit_lead",
                "带人来的是黑鸦寨的巴图。寨里吃了亏，多半还会派人来。");
            NpcDialogue(new[]{
                new NpcLine(BaseScriptNames.Merchant,BaseScriptNames.Portrait("merchant"),"东家，我走南路回来，听说你们刚打了一仗。"),
                new NpcLine(BaseScriptNames.Merchant,BaseScriptNames.Portrait("merchant"),warning),
                new NpcLine(BaseScriptNames.Merchant,BaseScriptNames.Portrait("merchant"),"黑鸦寨在霍恩辖地外。走营地南口上官道，一天就到。")},
                new[]{new NpcReply("记下敌营位置",()=>{if(owner.Change(next=>{next.Demo.Defense.MerchantDone=true;next.Campaign.Layouts.Single(map=>map.Region=="base").Objects.RemoveAll(obj=>obj.Id=="demo_merchant");},"已记录敌营位置与道路")){ClosePanel();owner.RefreshDemoRegion();}})});
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
                new NpcLine("阿禾","ahe","田里来了人手。我想跟你讨件差事。"),
                new NpcLine("阿禾","ahe","我从小跟庄稼打交道。你肯让我管这片田，收成还能好些。")
            };
            NpcDialogue(lines,new[]{
                new NpcReply(recruited?"知道了":"邀请阿禾加入",()=>{if(recruited)ClosePanel();else owner.JoinDemoPartner("partner_farm");}),
                new NpcReply("这次先不安排",()=>OpenAheResponse("成，先照旧安排。往后田里缺人，再来叫我。"))
            });
        }
    }
}

using System;
using System.Linq;
using BaseConquest.Presentation;
using UnityEngine;
namespace BaseConquest.Base
{
    public sealed partial class BaseSceneController
    {
        private bool OfferFirstRaidIntel()
        {
            if(!BaseDemoFlow.Active(State)||!State.Demo.Flow.FirstIntelEligible||State.Demo.Flow.FirstIntelOffered)return false;
            if(!Change(s=>{if(!BaseRaidIntel.OfferFirst(s))throw new InvalidOperationException("消息引导不可用");},"消息引导"))return false;
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
                InvalidateObjects();foreach(var npc in npcViews.Where(n=>n!=null&&n.name.StartsWith("demo_ahe ·")))npc.MoveTo(WorldPoint(f.FarmNpcX,f.FarmNpcY));
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
                marker.X=progress.MerchantX;marker.Y=progress.MerchantY;InvalidateObjects();foreach(var npc in npcViews.Where(view=>view!=null&&view.name.StartsWith("demo_merchant ·")))npc.MoveTo(WorldPoint(progress.MerchantX,progress.MerchantY));
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
            NpcDialogue(new[]{new NpcLine(BaseScriptNames.Merchant,BaseScriptNames.Portrait("merchant"),"待写")},new[]{new NpcReply("领取免费首防消息",()=>{if(owner.Change(next=>{if(!BaseRaidIntel.ClaimFirst(next))throw new InvalidOperationException("首防消息不可用");},"已记录首防日期"))NpcDialogue(new[]{new NpcLine(BaseScriptNames.Merchant,BaseScriptNames.Portrait("merchant"),"首防日期：第 "+owner.State.Demo.Flow.FreeFirstRaidDay+" 天。查看日历，准备粮食与军需。\n待写")},new[]{new NpcReply("知道了",ClosePanel)});}),new NpcReply("离开",ClosePanel)});
        }
        public void OpenRaidIntel()
        {
            var s=owner.State;if(!BaseDemoFlow.Active(s))return;
            if(s.Demo.Rpg?.IntelPricingConfigured==false)
            {
                RpgRules latest=null;try{latest=DemoBalanceRuntime.Load().Rules;}catch(InvalidOperationException){}catch(ArgumentException){}
                if(latest?.IntelPricingConfigured==true&&owner.Change(next=>BaseRaidIntel.FillMissingPrices(next,latest),"消息价格已载入"))s=owner.State;
            }
            BeginPanel("商人消息");
            foreach(int window in new[]{3,5})
            {
                int days=window;var snapshot=BaseRaidIntel.Get(s,days);bool configured=s.Demo.Rpg?.IntelPricingConfigured==true;int price=days==3?(s.Demo.Rpg?.ThreeDayIntelPrice??0):(s.Demo.Rpg?.FiveDayIntelPrice??0);
                RowButton(days+"日消息 · "+(snapshot!=null?"免费复习":configured?price+"钱":"价格待配置"),0,800,()=>{if(owner.Change(next=>BaseRaidIntel.Buy(next,days),"消息已记录"))OpenRaidIntel();},snapshot!=null||configured&&s.Inventory.Coins>=price);row+=60;
                if(snapshot==null)continue;
                if(!snapshot.HasRaid)Line("次日起 "+days+" 天：无已知袭击。",50,18);
                else if(days==3)
                {
                    bool active=snapshot.Entries.Any(entry=>s.Campaign.Events.Any(e=>e.Id==entry.EventId&&e.Status=="pending"&&e.AvailableDay==entry.Day));
                    Line(active?"次日起三天内有袭击。":"原消息已失效。",50,18);
                }
                else foreach(var entry in snapshot.Entries)
                {
                    bool active=s.Campaign.Events.Any(e=>e.Id==entry.EventId&&e.Status=="pending"&&e.AvailableDay==entry.Day);
                    Line(entry.Title+" · 第 "+entry.Day+" 天"+(active?"":" · 已失效"),50,18);
                }
            }
            RowButton("关闭",0,800,ClosePanel);row+=60;EndPanel();
        }
        public void OpenDemoMerchant()
        {
            string warning="待写";
            NpcDialogue(new[]{new NpcLine(BaseScriptNames.Merchant,BaseScriptNames.Portrait("merchant"),"我常走这条路，听说营地遇袭，特意来送个消息。"),new NpcLine(BaseScriptNames.Merchant,BaseScriptNames.Portrait("merchant"),warning),new NpcLine(BaseScriptNames.Merchant,BaseScriptNames.Portrait("merchant"),"他们的寨子就在通往"+BaseScriptNames.Village+"的路上。从营地南边上世界道路，走一天就到。")},
                new[]{new NpcReply("记下敌营位置",()=>{if(owner.Change(next=>{next.Demo.Defense.MerchantDone=true;next.Campaign.Layouts.Single(map=>map.Region=="base").Objects.RemoveAll(obj=>obj.Id=="demo_merchant");},"已记录敌营位置与道路")){ClosePanel();owner.RefreshDemoRegion();}})});
        }
        public void OpenRecruitmentComplete()=>NpcDialogue(new[]{
            new NpcLine(BaseScriptNames.Recruiter,BaseScriptNames.Portrait("recruiter"),"兵强马壮！新募的弟兄今天就能下地干活，或是进工坊打铁。"),
            new NpcLine(BaseScriptNames.Recruiter,BaseScriptNames.Portrait("recruiter"),"对了，城外那座"+BaseScriptNames.Ruins+"虽已破除主阵，底下仍有残余财货，消耗体力即可反复搜刮。"),
            new NpcLine(BaseScriptNames.Recruiter,BaseScriptNames.Portrait("recruiter"),"但切记——古窟瘴气蚀骨，普通士卒进去会不断折损生机，千万慎用凡躯涉险！")
        },new[]{new NpcReply("回营地安排生产",ClosePanel)});
        public void OpenSmithGoods()=>NpcDialogue(new[]{new NpcLine(BaseScriptNames.Blacksmith,BaseScriptNames.Portrait("blacksmith"),"听说你拔掉了古窟那颗毒钉！老头子连夜赶制了一批轻重甲胄和兵刃，快来看看！")},new[]{new NpcReply("去看看新装备",ClosePanel)});
        public void OpenAheFarmOffer(bool recruited)
        {
            var lines=new[]{new NpcLine("阿禾","ahe","看你往田里派了人手，有件事我可得毛遂自荐！"),new NpcLine("阿禾","ahe","我从小跟庄稼打交道，要是让我当粮田管事，收成还能多一成！"),new NpcLine("角色特性","","可在角色详情页查看阿禾特性【种植专精：粮田产出 +10%】。任命管事将退出随行小队，次日结算起生效。")};
            NpcDialogue(lines,new[]{new NpcReply(recruited?"我会查看你的特性":"任命阿禾为粮田管事",()=>{if(recruited)ClosePanel();else owner.JoinDemoPartner("partner_farm");}),new NpcReply("这次先不安排",ClosePanel)});
        }
    }
}

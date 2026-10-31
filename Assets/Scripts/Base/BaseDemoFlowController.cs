using System;
using System.Linq;
using BaseConquest.Presentation;
using UnityEngine;
namespace BaseConquest.Base
{
    public sealed partial class BaseSceneController
    {
        private void TickDemoFlow(double seconds=-1)
        {
            if(!BaseDemoFlow.Active(State)||State.Demo.Stage==BaseDemoStage.Waking||BaseDemoPrologue.Village(State)||BaseBattleGateway.MainMenu||Hud.PanelOpen||BaseBattleLoot.Pending(State)||ConstructionMode)return;
            var f=State.Demo.Flow;
            if(State.Campaign.Location=="base"&&BaseDemoDefense.Won(State)&&State.Demo.Defense.BatuStoryDay>0&&!State.Demo.Defense.MerchantDone)
            {TickDemoMerchant(seconds);return;}
            if(State.Campaign.Location=="perimeter"&&!f.StakesNotice)
            {
                if(Change(s=>{BaseDemoFlow.GrantStakes(s);s.Demo.Flow.StakesNotice=true;},"获得【成品拒马×20】")){Hud.OpenFieldReminder();}return;
            }
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
        public void OpenDemoMerchant()
        {
            int arrival=BaseDemoDefense.SecondArrival(owner.State);string warning=arrival>0?"我听说他们的主力还会来，大约还有 "+Math.Max(0,arrival-owner.State.Day)+" 天。":"敌寨的动静我也听说了，路上要小心。";
            NpcDialogue(new[]{new NpcLine("行商","villager","我常走这条路，听说营地遇袭，特意来送个消息。"),new NpcLine("行商","villager",warning),new NpcLine("行商","villager","他们的寨子就在通往故乡的路上。从营地南边上世界道路，走一天就到。")},
                new[]{new NpcReply("记下敌营位置",()=>{if(owner.Change(next=>{next.Demo.Defense.MerchantDone=true;next.Campaign.Layouts.Single(map=>map.Region=="base").Objects.RemoveAll(obj=>obj.Id=="demo_merchant");},"已记录敌营位置与道路")){ClosePanel();owner.RefreshDemoRegion();}})});
        }
        public void OpenRecruitmentComplete()=>NpcDialogue(new[]{new NpcLine("建设人手已到齐","","剑士、弓兵和骑兵已招募。现在就能安排他们生产。已经通关的遗迹可以反复挑战，消耗体力领取副本奖励；部分遗迹会严重侵蚀普通士兵，进入前请查看警告并调整队伍。")},new[]{new NpcReply("回营地安排生产",ClosePanel)});
        public void OpenSmithGoods()=>NpcDialogue(new[]{new NpcLine("老铁匠","chenan","听说你拔掉了古窟那颗毒钉！老头子连夜赶制了一批轻重甲胄和兵刃，快来看看！")},new[]{new NpcReply("去看看新装备",ClosePanel)});
        public void OpenFieldReminder()
        {
            bool azhu=BaseAdventure.Following(owner.State,"partner_craft");
            NpcDialogue(new[]{new NpcLine(azhu?"阿铸":"战前准备",azhu?"azhu":"","获得【成品拒马×20】。战前可在右侧小菜单布置，能减速步卒、阻断骑兵。")},new[]{new NpcReply("开始战前布置",()=>{ClosePanel();owner.ArmConstruction();}),new NpcReply("先查看战场",ClosePanel)});
        }
        public void OpenAheFarmOffer(bool recruited)
        {
            var lines=new[]{new NpcLine("阿禾","ahe","看你往田里派了人手，有件事我可得毛遂自荐！"),new NpcLine("阿禾","ahe","我从小跟庄稼打交道，要是让我当粮田管事，收成还能多一成！"),new NpcLine("角色特性","","可在角色详情页查看阿禾特性【种植专精：粮田产出 +10%】。任命管事将退出随行小队，次日结算起生效。")};
            NpcDialogue(lines,new[]{new NpcReply(recruited?"我会查看你的特性":"任命阿禾为粮田管事",()=>{if(recruited)ClosePanel();else owner.JoinDemoPartner("partner_farm");}),new NpcReply("这次先不安排",ClosePanel)});
        }
    }
}

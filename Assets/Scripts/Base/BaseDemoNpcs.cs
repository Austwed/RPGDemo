using System;
using System.Collections.Generic;
using System.Linq;
namespace BaseConquest.Base
{
    public static class BaseDemoNpcs
    {
        // Presentation overlays keep saved author IDs and all quest transactions intact.
        public static BaseMapObject Present(BaseState state,BaseMapObject original)
        {
            if(!BaseDemo.Active(state))return original;
            string name=null,appearance="chenan";
            switch(original.Id)
            {
                case "town_quest":name="领主 · 交谈";appearance="horn";break;
                case "town_guard":name="镇卫兵 · 交谈";appearance="villager";break;
                case "town_peddler":name="行商老莫 · 交谈";appearance="merchant";break;
                case "town_girl":name="镇民小芸 · 交谈";appearance="villager";break;
                case "prologue_report":
                    if(BaseDemoIntro.Active(state)){var report=state.Demo.Intro;return new BaseMapObject(original.Id,"报信村民 · 交谈",report.ReporterX,report.ReporterY,.5,.5,false,"demo-npc:reporter","villager","npc");}
                    name="报信村民";appearance="villager";break;
                case "demo_ahe":name="阿禾 · 交谈";appearance="ahe";if(BaseDemoFlow.Active(state)&&state.Demo.Flow.FarmWalking)return new BaseMapObject(original.Id,name,state.Demo.Flow.FarmNpcX,state.Demo.Flow.FarmNpcY,.5,.5,false,"demo-npc:ahe-offer","ahe","npc");break;
                case "demo_azhu":name="阿铸 · 交谈";appearance="azhu";if(BaseDemoFlow.SmithVisit(state))return new BaseMapObject(original.Id,name,BaseDemoFlow.SmithApproach(state).X,BaseDemoFlow.SmithApproach(state).Y,.5,.5,false,"demo-npc:smith","azhu","npc");break;
                case "demo_broken_sword":if(!BaseDemoRelease.Enabled(state)){name="幸存村民 · 交谈";appearance="ahe";}break;
                case "demo_survivor":name="幸存村民 · 交谈";appearance="ahe";break;
                case "demo_march_exit":if(!BaseDemoFinale.Opened(state))name="营地斥候 · 交谈";break;
            }
            return name==null?original:new BaseMapObject(original.Id,name,original.X,original.Y,original.Width,original.Height,false,string.IsNullOrEmpty(original.SourceObjectId)?"demo-npc:"+original.Id:original.SourceObjectId,appearance,"npc");
        }
        public static bool NearAzhu(BaseState s)
        {
            if(!BaseDemoRuins.CanExplore(s)||s.Campaign.Location!="town"||BaseSceneCombat.Active(s))return false;
            var npc=BaseSpatial.Objects(s).FirstOrDefault(o=>o.Id=="demo_azhu");
            var board=BaseBattlefield.Board(s);
            return npc!=null&&new BasePoint(npc.X,npc.Y).Distance(new BasePoint(s.PlayerX,s.PlayerY))<=2&&Math.Abs(board.SurfaceLevel(s.PlayerX,s.PlayerY)-board.SurfaceLevel(npc.X,npc.Y))<.000001;
        }
        public static bool CanInviteAzhu(BaseState s)=>BaseSceneRules.Active(s)&&!s.Demo.AzhuInvitationSeen&&!s.Demo.MetCompanions.Contains("partner_craft")&&!s.Leaders.Any(l=>l.Id=="partner_craft")&&!BaseBattleLoot.Pending(s)&&!s.Aftermath.GameOver&&NearAzhu(s);
        public static bool InviteAzhu(BaseState s)
        {
            if(!CanInviteAzhu(s))return false;
            s.Demo.AzhuInvitationSeen=true;return true;
        }
        public static string StatusMarker(BaseState s,string id)
        {
            if(!BaseDemo.Active(s))return "";
            if(BaseDemoPrologue.Village(s)&&BaseDemoIntro.Active(s))
            {
                int step=s.Demo.Intro.Step;
                if(id=="intro_villager"&&step==2)return "!";
                if(id=="demo_captain"&&new[]{1,10,4}.Contains(step))return "!";
                if(id=="prologue_report"&&step==6&&s.Demo.Intro.ReporterArrived)return "!";
                return "";
            }
            bool main=id=="town_quest"&&(s.Demo.Stage==BaseDemoStage.VisitLord||s.Demo.Stage==BaseDemoStage.RuinsCleared)||id=="demo_rescuer"&&s.Demo.Stage==BaseDemoStage.Waking
                ||(id=="demo_rescued_1"||id=="demo_rescued_2")&&s.Demo.Rescue?.Stage==1
                ||id=="demo_survivor"&&s.Demo.Stage!=BaseDemoStage.Finished;
            if(main)return "!";
            bool optional=id=="demo_ahe"&&!s.Leaders.Any(l=>l.Id=="partner_farm")||id=="demo_azhu"&&!s.Leaders.Any(l=>l.Id=="partner_craft");
            return optional?"?":"";
        }
        private static BaseMapObject[] RescuedTown(BaseState s)=>s.Demo.Rescue?.Stage==3?new[]{new BaseMapObject("demo_rescued_1","获救百姓甲 · 交谈",19.8,9.5,.5,.5,false,"demo-npc:rescued:1","villager","npc"),new BaseMapObject("demo_rescued_2","获救百姓乙 · 交谈",18.2,9.5,.5,.5,false,"demo-npc:rescued:2","villager","npc")}:Array.Empty<BaseMapObject>();
        // 旧档兼容：三个镇上闲聊 NPC 是在 156f189 才写进 town 布局的，而布局随存档序列化，
        // 旧档里不存在。这里参照 MarketMerchant 的运行时注入先例，缺哪个补哪个，新档不受影响。
        public static IEnumerable<BaseMapObject> TownChatCompat(BaseState state,string region)
        {
            if(!BaseDemo.Active(state)||(region??state.Campaign.Location)!="town")yield break;
            var objects=BaseSpatial.Layout(state,"town")?.Objects;
            if(objects==null)yield break;
            if(objects.All(o=>o.Id!="town_guard"))yield return new BaseMapObject("town_guard","镇卫兵 · 交谈",21,4,.6,.6,false,"demo-npc:town_guard","villager","npc");
            if(objects.All(o=>o.Id!="town_peddler"))yield return new BaseMapObject("town_peddler","行商老莫 · 交谈",5.5,4.5,.6,.6,false,"demo-npc:town_peddler","merchant","npc");
            if(objects.All(o=>o.Id!="town_girl"))yield return new BaseMapObject("town_girl","镇民小芸 · 交谈",15,11.5,.6,.6,false,"demo-npc:town_girl","villager","npc");
        }
        public static BaseMapObject[] Additional(BaseState state)
        {
            if(!BaseDemo.Active(state))return Array.Empty<BaseMapObject>();
            if(BaseDemoFlow.SmithVisit(state)&&state.Leaders.Any(l=>l.Id=="partner_craft"))return new[]{new BaseMapObject("demo_azhu","阿铸 · 交谈",BaseDemoFlow.SmithApproach(state).X,BaseDemoFlow.SmithApproach(state).Y,.5,.5,false,"demo-npc:smith","azhu","npc")}.Concat(RescuedTown(state)).ToArray();
            if(!BaseSceneRules.Active(state)&&state.Demo.RuinsDay>0&&BaseDemoRuins.InRuins(state)&&!state.Campaign.HasActiveBattle)
                return new[]{new BaseMapObject("demo_rescued_1","获救百姓甲 · 交谈",13,7.5,.5,.5,false,"demo-npc:rescued:1","villager","npc"),new BaseMapObject("demo_rescued_2","获救百姓乙 · 交谈",14,7.5,.5,.5,false,"demo-npc:rescued:2","villager","npc")};
            if(BaseSceneRules.Active(state)&&state.Demo.Rescue!=null)
            {
                var r=state.Demo.Rescue;
                if(BaseSceneRules.Entrance(state)&&(r.Stage==1||r.Stage==2))return new[]{new BaseMapObject("demo_rescued_1","获救百姓甲 · 交谈",r.FirstX,r.FirstY,.5,.5,false,"demo-npc:rescued:1","villager","npc"),new BaseMapObject("demo_rescued_2","获救百姓乙 · 交谈",r.SecondX,r.SecondY,.5,.5,false,"demo-npc:rescued:2","villager","npc")};
                if(state.Campaign.Location=="town"&&r.Stage==3)return new[]{new BaseMapObject("demo_rescued_1","获救百姓甲 · 交谈",19.8,9.5,.5,.5,false,"demo-npc:rescued:1","villager","npc"),new BaseMapObject("demo_rescued_2","获救百姓乙 · 交谈",18.2,9.5,.5,.5,false,"demo-npc:rescued:2","villager","npc")};
            }
            if(state.Demo.Stage==BaseDemoStage.PrologueBriefing)
            {
                if(BaseDemoIntro.Active(state))return BaseDemoIntro.Additional(state);
                var point=BaseSpatial.Layout(state)?.Objects.FirstOrDefault(o=>o.Id=="prologue_report");
                if(point!=null)return new[]{new BaseMapObject("demo_captain","队长 · 交谈",point.X+1,point.Y+.3,.5,.5,false,"demo-npc:captain","chenan","npc")};
            }
            if(state.Demo.Stage==BaseDemoStage.Waking&&state.Campaign.Location=="base")
            {
                var home=BaseSpatial.Layout(state)?.Objects.FirstOrDefault(o=>o.Id=="home");
                if(home!=null)return new[]{new BaseMapObject("demo_rescuer","救助者 · 交谈",home.X+home.Width/2+.35,home.Y-.5,.5,.5,false,"demo-npc:rescuer","chenan","npc")};
            }
            return Array.Empty<BaseMapObject>();
        }
    }
}

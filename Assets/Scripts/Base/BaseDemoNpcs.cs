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
                case "demo_rescuer":name=BaseScriptNames.LaoZhou+" · 交谈";appearance="laozhou";break;
                case "demo_chunshen":name=BaseScriptNames.ChunShen+" · 交谈";appearance="chunshen";break;
                case "town_quest":name="领主 · 交谈";appearance="horn";break;
                case "town_guard":name="镇卫兵 · 交谈";appearance="villager";break;
                case "town_peddler":
                    // 2026-11-01：老莫接管补给摊位（town_market 14,9 3×1.5 下方），布局随存档序列化，旧档在这里强制归位。
                    return new BaseMapObject(original.Id,"行商老莫 · 交谈",14,7.75,.6,.6,false,string.IsNullOrEmpty(original.SourceObjectId)?"demo-npc:"+original.Id:original.SourceObjectId,"merchant","npc");
                case "town_girl":name="镇民小芸 · 交谈";appearance="villager";break;
                case "prologue_report":
                    if(BaseDemoIntro.Active(state)){var report=state.Demo.Intro;return new BaseMapObject(original.Id,"报信村民 · 交谈",report.ReporterX,report.ReporterY,.5,.5,false,"demo-npc:reporter","villager","npc");}
                    name="报信村民";appearance="villager";break;
                case "demo_ahe":name="阿禾 · 交谈";appearance="ahe";if(BaseDemoFlow.Active(state)&&state.Demo.Flow.FarmWalking)return new BaseMapObject(original.Id,name,state.Demo.Flow.FarmNpcX,state.Demo.Flow.FarmNpcY,.5,.5,false,"demo-npc:ahe-offer","ahe","npc");break;
                case "demo_azhu":name="阿铸 · 交谈";appearance="azhu";if(BaseDemoFlow.SmithVisit(state))return new BaseMapObject(original.Id,name,BaseDemoFlow.SmithApproach(state).X,BaseDemoFlow.SmithApproach(state).Y,.5,.5,false,"demo-npc:smith","azhu","npc");break;
                case "demo_broken_sword":if(!BaseDemoRelease.Enabled(state)){name="幸存村民 · 交谈";appearance="ahe";}break;
                case "demo_survivor":name="幸存村民 · 交谈";appearance="ahe";break;
                case "demo_march_exit":if(!BaseDemoFinale.Opened(state))name="营地斥候 · 交谈";break;
                case "town_blacksmith":
                    // 2026-11-02：老铁匠专属图 baron-v1 入库。布局随存档序列化，旧档 AssetId 还是 chenan，这里运行时强制换新。
                    name=original.Name;appearance="baron";break;
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
        public static BaseMapPlacement[] CampResidents()=>new[]{new BaseMapPlacement {Id="demo_rescuer",Name=BaseScriptNames.LaoZhou,X=4.8,Y=8.2,Width=.5,Height=.5,SourceObjectId="demo-npc:rescuer",AssetId="laozhou",Kind="npc"},new BaseMapPlacement {Id="demo_chunshen",Name=BaseScriptNames.ChunShen,X=2.8,Y=8.2,Width=.5,Height=.5,SourceObjectId="demo-npc:chunshen",AssetId="chunshen",Kind="npc"}};
        // 全项目只有三类单位，检查台按这三类陈列：
        // ① 三职业角色（同规格：四向 + 移动 + 攻击）——剧情人物，与士兵共用一套动作规格；
        // ② 三职业士兵（saber／archer／rider，四向 + 移动 + 攻击）——不分敌我阵营，敌我共用同一套图；
        // ③ 交互 NPC——只有四向，是否有移动按作用分配（商人走摊、镇民站桩）。
        // 敌方队长不是独立兵种：galan 就是遗迹战队长、chief 就是匪寨战队长，直接用角色图集。
        // enemy-captain 只是「野外随机队长的兜底外观」，不陈列（见 PixelDemoArt 的 wilderness 别名）。
        private static readonly string[] MotionCast={"player","female","ahe","mounted-azhu","galan","chief","batu","huoya"};
        private static readonly string[] MotionSoldiers={"saber","archer","rider"};
        private static readonly string[] MotionNpcs={"baron","chunshen","horn","laozhou","merchant","villager"};
        // 陈列名与图集 id 分离：占位形象按其饰演的角色称呼，便于核对。
        private static string MotionFigureName(string id)=>id switch
        {
            "ahe"=>"阿禾","archer"=>"弓兵","batu"=>"巴图","saber"=>"剑士","baron"=>BaseScriptNames.Blacksmith,
            "chief"=>"大当家","chunshen"=>BaseScriptNames.ChunShen,"female"=>"女主角",
            "galan"=>BaseScriptNames.Galan,"horn"=>BaseScriptNames.Lord,"huoya"=>BaseScriptNames.Huoya,
            "rider"=>"骑兵","laozhou"=>BaseScriptNames.LaoZhou,"merchant"=>"行商","mounted-azhu"=>"阿铸",
            "player"=>"男主角","villager"=>"百姓",_=>id
        };
        // 分类说明：类别是设定口径（角色/士兵/NPC），后半段是该图集实测能播什么，两者并列便于发现缺件。
        public static string MotionClass(string id)
        {
            bool walk=BaseConquest.Presentation.PixelDemoArt.Motion(id,"walk")!=null;
            bool attack=BaseConquest.Presentation.PixelDemoArt.Motion(id,"attack")!=null;
            string art=walk&&attack?"待机／行走／攻击":walk?"待机／行走":"待机四向";
            if(Array.IndexOf(MotionSoldiers,id)>=0)return "三职业士兵 · "+art;
            if(Array.IndexOf(MotionNpcs,id)>=0)return "交互 NPC · "+art;
            return "三职业角色 · "+art;
        }
        // 两排陈列（2026-11-02，用户指定）：战斗（角色+士兵）与非战斗（NPC），全部在横向土路（y=6）下方。
        // 步长按实际横向占位分配：骑乘 64px×1.45 约 1.45 单位宽，给 2.8；其余 2.0。
        // 战斗排 11 人按 2.0/2.8 步长合计 24.4，起点压到 1.2 才不顶出东边界（24）。
        public static BaseMapObject[] MotionChecks(BaseState state)
        {
            if(!BaseDemo.Active(state)||BaseDemoPrologue.Village(state)||state.Campaign.Location!="base")return Array.Empty<BaseMapObject>();
            var rows=new List<BaseMapObject>();
            double x=1.2;
            void Place(string id,double y,double step)
            {
                // 第 9 参是 assetId＝图集加载用的 id，必须保持角色 id；陈列名只进 Name（名牌与面板显示），
                // 传错会把 Resources.Load("巴图") 之类打空，人偶全体落占位符（2026-11-02 踩过）。
                rows.Add(new BaseMapObject("motion-check:"+id,MotionFigureName(id),x,y,.5,.5,false,"motion-check:"+id,id,"npc"));
                x+=step;
            }
            // 战斗排（y=4.4，紧贴路下）：三职业角色在前，三职业士兵殿后。
            foreach(string id in MotionCast)Place(id,4.4,BaseConquest.Presentation.PixelDemoArt.Mounted(id)?2.8:2.0);
            foreach(string id in MotionSoldiers)Place(id,4.4,BaseConquest.Presentation.PixelDemoArt.Mounted(id)?2.8:2.0);
            // 非战斗排（y=1.6）：起点让开木屋（env house-small 中心 2.5,2，画幅盖到 x≈4.4）。
            x=5.0;
            foreach(string id in MotionNpcs)Place(id,1.6,2.0);
            return rows.ToArray();
        }
        public static IEnumerable<BaseMapObject> CampResidentsCompat(BaseState state,string region)
        {
            if(!BaseDemo.Active(state)||BaseDemoPrologue.Village(state)||(region??state.Campaign.Location)!="base")yield break;
            var objects=BaseSpatial.Layout(state,"base")?.Objects;if(objects==null)yield break;
            foreach(var resident in CampResidents())if(objects.All(o=>o.Id!=resident.Id))yield return Present(state,resident.Object);
        }
        // 旧档兼容：三个镇上闲聊 NPC 是在 156f189 才写进 town 布局的，而布局随存档序列化，
        // 旧档里不存在。这里参照 MarketMerchant 的运行时注入先例，缺哪个补哪个，新档不受影响。
        public static IEnumerable<BaseMapObject> TownChatCompat(BaseState state,string region)
        {
            if(!BaseDemo.Active(state)||(region??state.Campaign.Location)!="town")yield break;
            var objects=BaseSpatial.Layout(state,"town")?.Objects;
            if(objects==null)yield break;
            if(objects.All(o=>o.Id!="town_guard"))yield return new BaseMapObject("town_guard","镇卫兵 · 交谈",21,4,.6,.6,false,"demo-npc:town_guard","villager","npc");
            if(objects.All(o=>o.Id!="town_peddler"))yield return new BaseMapObject("town_peddler","行商老莫 · 交谈",14,7.75,.6,.6,false,"demo-npc:town_peddler","merchant","npc");
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
            return Array.Empty<BaseMapObject>();
        }
    }
}

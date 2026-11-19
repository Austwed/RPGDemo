using System;
using System.Collections.Generic;
using System.Linq;
using BaseConquest.Story;
using UnityEngine.SceneManagement;

namespace BaseConquest.Base
{
    public sealed partial class BaseSceneController
    {
        public void EnterDemo()
        {
            if(Busy||State?.Campaign.HasActiveBattle==true)return;
            BaseBattleGateway.UseDemo();BaseBattleGateway.MainMenu=false;
            SceneLoadingCover.LoadExploration();
        }
    }
    public sealed partial class BaseHud
    {
        // 2026-11-03 用户定：敌军已抵营门时的休息拦截，一律用「陈安当面拦下」的对白弹出，
        // 不依赖陈安此刻是否在玩家旁边（他是营地斥候，报信不要求站位）。
        public void OpenDemoScoutWarning()
        {
            activeObject="demo-scout-warning";interactionObject="";
            NpcDialogue(new[]{new NpcLine(BaseScriptNames.Scout,BaseScriptNames.Portrait("chenan"),"大人，匪兵到了！快从西口过去！")},
                new[]{new NpcReply("这就去迎敌",ClosePanel)});
        }
        public void OpenDemoStory(bool showCompletion = false)
        {
            if(OpenIntroStory())return;
            var state=owner.State;if(!BaseDemo.Active(state))return;
            if(BaseDemoFinale.Rebuilding(state)&&state.Campaign.CurrentMapId==BaseDemoFinale.Home){OpenRebuild();return;}
            if(BaseDemoDefense.Won(state)&&state.Demo.Defense.BatuStoryDay==0){OpenDemoDefenseAftermath();return;}
            if(BaseDemoFinale.SecondRaidReportPending(state)){OpenSecondRaidVictoryReport();return;}
            activeObject="demo-story";interactionObject="";
            if(BaseDemoPrologue.Village(state)){OpenPrologueStory();return;}
            if(state.Demo.Stage==BaseDemoStage.Waking)
            {
                NpcDialogue(new[]{
                    new NpcLine(BaseScriptNames.ChunShen,BaseScriptNames.Portrait("chunshen"),"醒了！老头子快来看，人可算睁眼了！"),
                    new NpcLine("你","","嘶……这是哪儿？我睡了多久？",true),
                    new NpcLine(BaseScriptNames.LaoZhou,BaseScriptNames.Portrait("laozhou"),"灰岩镇西边的营地。你昏过去好些天了。"),
                    new NpcLine(BaseScriptNames.LaoZhou,BaseScriptNames.Portrait("laozhou"),"水都喂不进去，还能醒，你是真命大。"),
                    new NpcLine("你","","搭档呢？你回村时，见着西桥头的人了吗？",true),
                    new NpcLine(BaseScriptNames.LaoZhou,BaseScriptNames.Portrait("laozhou"),"西桥断了，满地乱箭。我没见着你搭档，兴许是突围了。"),
                    new NpcLine(BaseScriptNames.LaoZhou,BaseScriptNames.Portrait("laozhou"),"粮仓还有人翻吃的。我牵马绕过去，从东头把你驮了回来。"),
                    new NpcLine(BaseScriptNames.LaoZhou,BaseScriptNames.Portrait("laozhou"),"这剑是从你胸口拔出来的。我替你收着，拿好。"),
                    new NpcLine("你","","禁卫军的秘银剑……得查查它怎么到了古裔手里。",true),
                    new NpcLine(BaseScriptNames.ChunShen,BaseScriptNames.Portrait("chunshen"),"你伤刚好，别急着动气。阿禾就在外头麦田里忙活呢。"),
                    new NpcLine(BaseScriptNames.LaoZhou,BaseScriptNames.Portrait("laozhou"),"霍恩爵士接济过乡亲。你去灰岩镇问问，他兴许知道那队古裔。")
                },new[]{new NpcReply("起身，拜访领主",()=>{if(owner.Change(next=>{if(!BaseDemo.AcknowledgeWaking(next))throw new InvalidOperationException("醒来流程未完成，请重试");},""))ClosePanel();})},guide:true);return;
            }
            if(state.Demo.Stage==BaseDemoStage.Finished)
            {
                NpcDialogue(new[]{new NpcLine("你","",StoryText.Get("story.scene.home_rebuild.finished", "乡亲们总算有地方落脚了。搭档，等我。我去西山找你。"),true)},
                    Array.Empty<NpcReply>(),lockExitUntilFinished:showCompletion,
                    onCompleted:showCompletion ? OpenDemoCompletion : ClosePanel);
                return;
            }
            ClosePanel();
        }
        private void OpenPrologueStory()
        {
            var state=owner.State;
            if(state.Demo.Stage==BaseDemoStage.PrologueBriefing)
            {
                NpcDialogue(new[]{
                    new NpcLine("报信村民","villager","西边来了十几个异族，全穿着重甲！"),
                    new NpcLine("搭档",BaseDemoIntro.CaptainArt(state),"西桥那边交给我。带乡亲们往东走，别回头。"),
                    new NpcLine("你","","乡亲们，跟紧我，我们往东撤！",true)
                },new[]{new NpcReply("带村民向东撤离",owner.BeginDemoEscape)},guide:true);return;
            }
            else if(state.Demo.Stage==BaseDemoStage.PrologueWounded)
            {
                NpcDialogue(new[]{
                    new NpcLine("旁白","",StoryText.Get("story.scene.prologue.pursuit_stab","［古裔追兵用王室的剑刺穿你的胸口，随即力竭倒下。］")),
                    new NpcLine("你","",StoryText.Get("story.scene.prologue.pursuit_blackout","王室……怎么会……搭档……［失去意识］"),true),
                    new NpcLine("古裔喽啰","ancient-saber",StoryText.Get("story.scene.prologue.pursuit_goon","都倒了？别管了，去粮仓搬吃的。"))
                },Array.Empty<NpcReply>(),lockExitUntilFinished:true,onCompleted:owner.RecoverDemoPrologue,guide:true);return;
            }
            ClosePanel();
        }
        // TOWN_04_NPC_GUARD／MERCHANT／GIRL：镇上三个闲聊路人，只给世界观与线索，不推进流程。
        public void OpenTownChat(string id)
        {
            activeObject=id;
            var state=owner.State;
            string who=id=="town_guard"?BaseScriptNames.Guard:id=="town_peddler"?BaseScriptNames.Peddler:BaseScriptNames.Girl;
            string art=id=="town_peddler"?BaseScriptNames.Portrait("merchant"):BaseScriptNames.Portrait(id=="town_guard"?"guard":"girl");
            string text=id=="town_guard"?"爵士话说得硬。卫队守城墙都缺人，哪还顾得上外头？"
                :id=="town_peddler"?(BaseDemo.Active(state)&&!BaseDemoFinale.Later(state)
                    ?"黑鸦寨堵了去白桦村的官道。粮草器械我这儿有，战利品也收。"
                    :StoryText.Get("story.ui.trade.market_greeting","粮草在这边。要卖战利品，拿来我瞧瞧。"))
                :state.Demo?.RuinsDay>0?"听说采石的那两个人救出来了？太好了。"
                :"我家狗一到林子边就夹尾巴。沉眠古窟底下，怕是有东西。";
            var replies=new List<NpcReply>{new NpcReply("点点头走开",ClosePanel)};
            // 2026-11-01 韦德拍板：撤掉独立的「商人」，行商老莫接管补给摊位，交易入口并进他的对话。
            if(id=="town_peddler")
            {
                replies.Insert(0,TradeEntry(TradeVendor.Merchant,TradeShelf.Goods));
                if(BaseDemoFlow.Active(state))replies.Insert(1,TradeEntry(TradeVendor.Merchant,TradeShelf.Intel));
            }
            string injury=id=="town_guard"?"INJ_TOWN_GUARD":id=="town_peddler"?"INJ_TOWN_MO":"INJ_TOWN_XIAOYUN";
            NpcDialogue(WithInjury(state,injury,new NpcLine(who,art,text)),replies.ToArray());
        }
        private void OpenDemoLord()
        {
            var state=owner.State;activeObject="demo-lord";interactionObject="town_quest";
            if(state.Demo.Stage==BaseDemoStage.RuinsCleared||BaseDemoDefense.Delivered(state))
            {
                bool deliver=state.Demo.Stage==BaseDemoStage.RuinsCleared;bool unassigned=deliver&&state.Demo.AcceptedDay==0;
                // TOWN_03_B（没接委托就先把人救回来）／TOWN_03_C（接了委托正常交付）／交付后的日常。
                var questLines=new List<NpcLine>();
                if(unassigned)
                {
                    questLines.Add(new NpcLine(BaseScriptNames.Lord,BaseScriptNames.Portrait("lord"),"我正要派人查古窟。你也是为这事来的？"));
                    questLines.Add(new NpcLine("你","","两个采石的镇民已经脱险，困住他们的古裔也制住了。",true));
                    questLines.Add(new NpcLine(BaseScriptNames.Lord,BaseScriptNames.Portrait("lord"),"这份赏金和建设许可，你拿去。"));
                    questLines.Add(new NpcLine(BaseScriptNames.Lord,BaseScriptNames.Portrait("lord"),"募兵处我会打招呼。回去把工坊和兵营修起来。"));
                }
                else if(deliver)
                {
                    questLines.Add(new NpcLine("你","","爵士，两个镇民都救出来了，控制他们的古裔也已制住。",true));
                    questLines.Add(new NpcLine(BaseScriptNames.Lord,BaseScriptNames.Portrait("lord"),"原来窟里有古裔。往后得让哨探多留意。"));
                    questLines.Add(new NpcLine(BaseScriptNames.Lord,BaseScriptNames.Portrait("lord"),"这是答应你的赏金和许可。工坊、兵营都准修。"));
                    questLines.Add(new NpcLine(BaseScriptNames.Lord,BaseScriptNames.Portrait("lord"),"去募兵处。剑士、弓兵、骑兵各募一名，守好南边。"));
                }
                else if (!BaseDemoDefense.Won(state))
                {
                    bool repaired = state.Demo.Defense.WorkshopRepaired && state.Demo.Defense.BarracksRepaired;
                    if (!repaired)
                        questLines.Add(new NpcLine(BaseScriptNames.Lord,BaseScriptNames.Portrait("lord"),"先把工坊和兵营修好。"));
                    if (!BaseDemoFlow.Recruited(state))
                        questLines.Add(new NpcLine(BaseScriptNames.Lord,BaseScriptNames.Portrait("lord"),"带手令去募兵处找雷恩。"));
                    if (repaired && BaseEconomy.ProductionWorkersAt(state,"workshop")==0)
                        questLines.Add(new NpcLine(BaseScriptNames.Lord,BaseScriptNames.Portrait("lord"),"回营先分好人手。修起来以后，总得有人做事。"));
                }
                bool gift = BaseDemoDefense.Won(state) && !BaseFacilityLayout.Owned(state, "caravan") && state.Layout?.OfficeGift != true;
                string construction = BaseDemo.ExperimentalManagementHidden(state) ? "商队" : "办公设施和商队";
                if (gift) questLines.Add(new NpcLine(BaseScriptNames.Lord,BaseScriptNames.Portrait("lord"),"营地守住了。再给你一道手令，回去建" + construction + "。"));
                var replies = new List<NpcReply>();
                if (gift) replies.Add(new NpcReply("领取建设许可", () => owner.OrderCampConstruction(true)));
                if (deliver) replies.Add(new NpcReply("领赏，接下手令", owner.DeliverDemoRuins));
                else if (!gift && BaseFacilityLayout.CanOrder(state)) replies.Add(new NpcReply("营地建设", () => owner.OrderCampConstruction()));
                replies.Add(new NpcReply("告辞", ClosePanel));
                if (questLines.Count==0) questLines.Add(new NpcLine(BaseScriptNames.Lord,BaseScriptNames.Portrait("lord"),"守好南边。"));
                NpcDialogue(questLines.ToArray(), replies.ToArray(), guide:deliver&&BaseQuestLog.Cue(state).ObjectId=="town_quest",
                    skipSummary:deliver ? "你向领主交了差，领到赏金和建设许可，募兵处也向你开放了。" : null); return;
            }
            bool available=state.Demo.Stage==BaseDemoStage.VisitLord;
            var lordLines=available?new[]{
                new NpcLine(BaseScriptNames.Lord,BaseScriptNames.Portrait("lord"),"听乡亲说，是你拖住了追兵。伤好些了？"),
                new NpcLine("你","","多谢爵士接济。您可知道袭村的古裔从哪儿来？",true),
                new NpcLine("你","","乡亲们暂时住在镇外的营地。我想修工坊、招些人手，得请您给道许可。",true),
                new NpcLine(BaseScriptNames.Lord,BaseScriptNames.Portrait("lord"),"哨探也没摸清他们的底细。"),
                new NpcLine(BaseScriptNames.Lord,BaseScriptNames.Portrait("lord"),"你想修工坊、招兵？先帮我办件事。"),
                new NpcLine(BaseScriptNames.Lord,BaseScriptNames.Portrait("lord"),"镇东的沉眠古窟近日在冒黑雾，两个采石的镇民还没回来。"),
                new NpcLine(BaseScriptNames.Lord,BaseScriptNames.Portrait("lord"),"听说你扛得住瘴气。进窟查查，他们要是还活着，就带回来。"),
                new NpcLine(BaseScriptNames.Lord,BaseScriptNames.Portrait("lord"),"事办妥，赏金和建设许可归你，募兵处也会放行。")
            }:new[]{
                new NpcLine(BaseScriptNames.Lord,BaseScriptNames.Portrait("lord"),"古窟走东口。若嫌人手少，可以去铁匠铺问问。"),
                new NpcLine("你","","我去看看。两个人在里头多耽搁一天，就多一分险。",true)
            };
            NpcDialogue(lordLines,new[]{new NpcReply("接受"+BaseScriptNames.Ruins+"调查",()=>{if(owner.Change(next=>{if(!BaseDemo.AcceptLordQuest(next))throw new InvalidOperationException("请先了解营地现状，并在领主同层一格以内接取");},BaseScriptNames.Ruins+"调查已接取并保存节点"))ClosePanel();},unlocked:available),new NpcReply("暂且告辞",ClosePanel)},lockExitUntilFinished:available,guide:available&&BaseQuestLog.Cue(state).ObjectId=="town_quest",
                skipSummary:available ? "你接下了领主的任务，需要前往东边的遗迹探明情况" : null);
        }
        public void OpenAzhuInvitation()
        {
            activeObject="demo-partner";interactionObject="";
            NpcDialogue(new[]{
                new NpcLine("阿铸","azhu","等一下。你下盘稳，虎口一层老茧。练过吧？"),
                new NpcLine("阿铸","azhu","我叫阿铸，巴隆是我爹。过两招？点到为止。")
            },new[]{
                new NpcReply("好，请赐教",owner.BeginAzhuDuel),
                new NpcReply("眼下有要事，改日再比",()=>NpcDialogue(
                    new[]{new NpcLine("阿铸","azhu","成。我就在铺里拉风箱，想比了来叫我。")},
                    Array.Empty<NpcReply>(),onCompleted:ClosePanel))
            });
        }
        public void OpenRuinsRetreatMonologue()
        {
            NpcDialogue(new[]{new NpcLine("你","",StoryText.Get("story.scene.ruins.solo_retreat", "对方人多势众，优势不在我，或许镇上那位年轻铁匠可以帮忙。"),true)},
                Array.Empty<NpcReply>(), onCompleted: ClosePanel, quietAdvance: true);
        }
        public void OpenDemoPartner(string id)
        {
            var state=owner.State;bool met=state.Demo.MetCompanions.Contains(id);activeObject="demo-partner";interactionObject="";
            if(id=="partner_farm"){OpenAheInvitation(met);return;}
            if(state.Leaders.Any(l=>l.Id==id)){NpcDialogue(new[]{new NpcLine("阿铸","azhu","我在这儿帮我爹拉风箱。要出征，过来喊一声。")},new[]{new NpcReply("稍后再谈",ClosePanel)});return;}
            var baseLines=met?new[]{new NpcLine("阿铸","azhu","出阵我能骑马护住后排，留营就帮你看工坊。")}:new[]{
                new NpcLine("你","","门边那杆长枪是你的？",true),
                new NpcLine("阿铸","azhu","是我的。我叫阿铸，巴隆是我爹。来过两招？")
            };
            NpcDialogue(WithInjury(state,met?"INJ_CAMP_AZHU":"",baseLines),new[]{
                new NpcReply(met?"欢迎加入":"原地切磋",()=>{if(met)owner.JoinDemoPartner(id);else if(BaseSceneRules.Active(state))owner.BeginAzhuDuel();else owner.MeetDemoPartner(id);}),
                new NpcReply("暂不邀请",ClosePanel)
            },guide:BaseQuestLog.Cue(state).ObjectId==id);
        }
        private void OpenAheInvitation(bool met)
        {
            const string id="partner_farm";
            var state=owner.State;
            var lines=met?new[]{new NpcLine("阿禾","ahe","我能在后头放箭，也能替你看田。进古窟得留神那股瘴气。")}:new[]{
                new NpcLine("阿禾","ahe","你能下地啦？我爹把你驮回来时，吓得我半天没说出话。"),
                new NpcLine("你","","你把乡亲们都带到了？",true),
                new NpcLine("阿禾","ahe","都安顿好啦。我那张猎弓还算准，出去探路带上我呗？")
            };
            NpcDialogue(WithInjury(state,met?"INJ_CAMP_AHE":"",lines),new[]{
                new NpcReply("好，一起走",()=>owner.JoinDemoPartner(id)),
                new NpcReply("先在营地照看父母",()=>owner.JoinDemoPartner(id, false)),
                new NpcReply("暂不邀请",ClosePanel)
            },guide:BaseQuestLog.Cue(state).ObjectId==id);
        }
        internal void OpenAheResponse(string text,Action after=null) =>
            NpcDialogue(new[]{new NpcLine("阿禾","ahe",text)},Array.Empty<NpcReply>(),onCompleted:after??ClosePanel);
        private bool OpenDemoInteraction(string id)
        {
            if(id=="town_smith"||id=="town_blacksmith"){OpenSmithFather();return true;}
            if(!BaseDemoRuins.Enabled(owner.State)||BaseDemoPrologue.Village(owner.State))return false;
            if (id == BaseBanditHideout.EntryId)
            {
                if (BaseBanditHideout.CanEnter(owner.State)) OpenBattlePreparation(BasePrepRoute.Hideout);
                else owner.ReportMessage(BaseBanditHideout.ApproachText);
                return true;
            }
            if(OpenDemoFinaleInteraction(id))return true;
            if(id=="demo_ahe"||id=="demo_azhu"){OpenDemoPartner(id=="demo_ahe"?"partner_farm":"partner_craft");return true;}
            if(id=="demo_rescued_1"||id=="demo_rescued_2"){OpenRescuedCivilian(id);return true;}
            if(id==BaseDemoStories.GalanMarker){OpenWaitingGalan();return true;}
            if(id=="demo_ruins_investigate"){OpenRuinsChallenge();return true;}
            if(BaseDemoFinale.Opened(owner.State))return false;
            if(OpenDemoDefenseInteraction(id))return true;
            if(id=="demo_ruins_exit"||id=="demo_ruins_return"){owner.TravelDemo(id=="demo_ruins_exit"?"outpost":"town");return true;}
            return false;
        }
    }
}

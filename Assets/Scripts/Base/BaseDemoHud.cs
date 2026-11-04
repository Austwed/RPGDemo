using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine.SceneManagement;

namespace BaseConquest.Base
{
    public sealed partial class BaseSceneController
    {
        public void EnterDemo()
        {
            if(Busy||State?.Campaign.HasActiveBattle==true)return;
            BaseBattleGateway.UseDemo();BaseBattleGateway.MainMenu=false;
            SceneManager.LoadScene(BaseBattleGateway.ExplorationScene);
        }
    }
    public sealed partial class BaseHud
    {
        // 2026-11-03 用户定：敌军已抵营门时的休息拦截，一律用「陈安当面拦下」的对白弹出，
        // 不依赖陈安此刻是否在玩家旁边（他是营地斥候，报信不要求站位）。
        public void OpenDemoScoutWarning()
        {
            activeObject="demo-scout-warning";interactionObject="";
            NpcDialogue(new[]{new NpcLine(BaseScriptNames.Scout,BaseScriptNames.Portrait("chenan"),"大人，匪众已杀到营门外，这会儿歇息就是等死，快上迎敌战场！")},
                new[]{new NpcReply("这就去迎敌",ClosePanel)});
        }
        public void OpenDemoStory()
        {
            if(OpenIntroStory())return;
            var state=owner.State;if(!BaseDemo.Active(state))return;
            if(BaseDemoDefense.Won(state)&&state.Demo.Defense.BatuStoryDay==0){OpenDemoDefenseAftermath();return;}
            activeObject="demo-story";interactionObject="";
            if(BaseDemoPrologue.Village(state)){OpenPrologueStory();return;}
            BeginPanel(state.Demo.Stage==BaseDemoStage.Waking ? "新营地 · 醒来" : state.Demo.Stage==BaseDemoStage.Finished?"Demo结束 · 带着断剑继续追查":BaseDemoDefense.Won(state)?"当前目标 · 攻营与返乡":BaseDemoDefense.Delivered(state)?"当前目标 · 营地整备与防守":"当前目标 · "+BaseScriptNames.Ruins+"调查");
            if(state.Demo.Stage==BaseDemoStage.Waking)
            {
                NpcDialogue(new[]{
                    new NpcLine(BaseScriptNames.ChunShen,BaseScriptNames.Portrait("chunshen"),"醒了！老头子快来看，人可算睁眼了！"),
                    new NpcLine("你","","［按住胸口］嘶……这是哪儿？我睡了多久？",true),
                    new NpcLine(BaseScriptNames.LaoZhou,BaseScriptNames.Portrait("laozhou"),"[摇头] 断流滩，灰岩镇外头。你昏睡了好些日子，水都喂不进去——能睁眼，是你命大。"),
                    new NpcLine("你","","{Partner}呢？你们回村时，见着西桥头的人了吗？",true),
                    new NpcLine(BaseScriptNames.LaoZhou,BaseScriptNames.Portrait("laozhou"),"［叹气］西桥头被烧成了白地，没见着尸首，兴许是突围走了……"),
                    new NpcLine(BaseScriptNames.LaoZhou,BaseScriptNames.Portrait("laozhou"),"这柄剑是从你胸口拔出来的，我一直替你收着。拿好，别再攥这么紧了。"),
                    new NpcLine("你","","［眼神微凛］王都禁卫的秘银剑……古裔背后果然有人指使。",true),
                    new NpcLine(BaseScriptNames.ChunShen,BaseScriptNames.Portrait("chunshen"),"你伤刚好，别急着动气。阿禾就在外头麦田里忙活呢。"),
                    new NpcLine(BaseScriptNames.LaoZhou,BaseScriptNames.Portrait("laozhou"),"想在这片荒滩立寨找人，得先去北边灰岩镇求见领主霍恩爵士。")
                },new[]{new NpcReply("起身，拜访领主",()=>{if(owner.Change(next=>{if(!BaseDemo.AcknowledgeWaking(next))throw new InvalidOperationException("醒来流程未完成，请重试");},"当前目标：从东侧出口拜访领主"))ClosePanel();})});return;
            }
            else
            {
                Line(BaseDemo.Objective(state),95,23);
                if(state.Demo.Stage==BaseDemoStage.RuinsAssigned)Line(BaseDemoRuins.Enabled(state)?"阿禾在营地，阿铸在"+BaseScriptNames.Town+"；可邀请，也可暂不邀请。从"+BaseScriptNames.Town+"东侧进入"+BaseScriptNames.Ruins+"，靠近异常者确认救助战。受控村民可制服，不会误杀；击败控制来源后带证据交差。":"此旧Demo未保存"+BaseScriptNames.Ruins+"与伙伴属性快照。继续原阶段，重新开始Demo可体验新增流程，旧档不自动换数值。",130,21);
                if(state.Demo.Stage==BaseDemoStage.RuinsCleared)Line("控制来源消失后，失踪村民恢复神志并获救。你已取得调查证据；己方伤亡与军需已按普通战争处理，不会自动补满。经"+BaseScriptNames.Ruins+"西侧出口回"+BaseScriptNames.Town+"，向领主交差。",130,21);
                if(BaseDemoDefense.Delivered(state))Line(BaseDemoDefense.Enabled(state)?BaseDemoDefense.Won(state)?BaseDemoFinale.Enabled(state)?state.Demo.Stage==BaseDemoStage.Finished?"你带着队长断剑与幸存者证词，准备继续追查。Demo到此结束；完整故事留待后续。可以关闭查看当前进度，或从菜单重开，不会免费恢复战损。":"首防后先补损，从营地南侧世界出口出征。攻营并处理完战果后，沿路返乡调查断剑；两段道路各1天，规划不耗日。":"此旧Demo没有攻营／结尾快照；重新开始才能体验新增内容。":"工坊与兵营的修复点在建筑前方，每座原料"+BaseDemoDefense.RepairMaterialCost(state)+"／钱币"+BaseDemoDefense.RepairCoinCost(state)+"。两座修好后，安排工坊实际产出首件军需；随后明示1天后入侵。到达后从营地左侧进入迎敌战场，不能休息自动处理。":"此旧Demo未保存修复／防守快照。继续原阶段，重新开始Demo才能体验新增内容。",145,21);
                RowButton("关闭",0,800,ClosePanel);row+=60;
            }
            Line("新局读取Demo数值快照，已有存档保留原值。\n快照标识："+(string.IsNullOrEmpty(state.Demo.ConfigHash)?state.Demo.BalanceHash:state.Demo.ConfigHash).Substring(0,12),85,18);
            EndPanel();
        }
        private void OpenPrologueStory()
        {
            var state=owner.State;
            if(state.Demo.Stage==BaseDemoStage.PrologueBriefing)
            {
                NpcDialogue(new[]{
                    new NpcLine("报信村民","ahe","不好了！村头远处来了大批敌人，他们正朝村子过来！"),
                    new NpcLine("队长",BaseDemoIntro.CaptainArt(state),"村头那边交给我。带乡亲们往东走，别回头。"),
                    new NpcLine("你","","乡亲们，跟紧我，我们往东撤！",true)
                },new[]{new NpcReply("带村民向东撤离",owner.BeginDemoEscape)});return;
            }
            else if(state.Demo.Stage==BaseDemoStage.PrologueWounded)
            {
                NpcDialogue(new[]{
                    new NpcLine("残血古裔追兵","saber","咳……明明吸了瘴毒，这人类的剑怎么还这么快……"),
                    new NpcLine("你","","说！西桥头那个拿剑的人在哪——？！",true),
                    new NpcLine("旁白","","【一道寒光自浓雾背后突刺而出，狠辣地贯穿了你的胸口！】"),
                    new NpcLine("你","","［吐血倒地］这道暗金云纹……是王都玄甲禁卫的秘银制式长剑？！",true),
                    new NpcLine("你","","为什么王室的剑会跟古裔混在一起……{Partner}……快逃……",true),
                    new NpcLine("古裔喽啰","archer","啧，两个斥候全折了，这人类也穿了心窝子……同归于尽了，快去搬粮！")
                },new[]{new NpcReply("获救后 · 在新营地醒来",owner.RecoverDemoPrologue)});return;
            }
            else
            {
                BeginPanel("序章 · 撤离");Line(BaseDemo.Objective(state),75,23);
                Line(state.Demo.PrologueAttempts>0?"［以剑撑地］还不能倒在这儿……我答应过要去"+BaseScriptNames.Town+"汇合的！（再次向东进入撤离区域即可重战）":"带村民向东走。达到撤离区域后，两名被队长打伤的精英追兵将从村头方向出现。",115,21);
                RowButton("继续",0,800,ClosePanel);row+=60;
            }
            EndPanel();
        }
        // TOWN_04_NPC_GUARD／MERCHANT／GIRL：镇上三个闲聊路人，只给世界观与线索，不推进流程。
        public void OpenTownChat(string id)
        {
            activeObject=id;
            var state=owner.State;
            string who=id=="town_guard"?BaseScriptNames.Guard:id=="town_peddler"?BaseScriptNames.Peddler:BaseScriptNames.Girl;
            string art=id=="town_peddler"?BaseScriptNames.Portrait("merchant"):BaseScriptNames.Portrait(id=="town_guard"?"guard":"girl");
            // 主线之后行商会透露下次流寇的大致日期：给区间不给准确日，保留「去打探」的价值。
            string rumor=id=="town_peddler"?BaseDemoRaids.Rumor(state):"";
            string text=id=="town_guard"?"爵士老爷嘴上硬气，其实镇里的卫队连守城墙都勉强，哪腾得出手管外头的荒滩。"
                :id=="town_peddler"?"南边通往"+BaseScriptNames.Village+"的官道被【"+BaseScriptNames.Bandit+"】彻底封死了，听说他们最近发了一笔来路不明的横财。要采买粮草器械，或者出手沿途的战利品，都在我这补给摊上，价钱公道。"
                    +(string.IsNullOrEmpty(rumor)?"":"\n\n［压低声音］"+rumor)
                :"只要一靠近镇西的树林，家里的猎犬就会夹着尾巴狂叫，那座"+BaseScriptNames.Ruins+"底下一定藏着脏东西。";
            var replies=new List<NpcReply>{new NpcReply("点点头走开",ClosePanel)};
            // 2026-11-01 韦德拍板：撤掉独立的「商人」，行商老莫接管补给摊位，交易入口并进他的对话。
            if(id=="town_peddler")replies.Insert(0,new NpcReply("看看货品",()=>{ClosePanel();OpenTownMarket();}));
            NpcDialogue(new[]{new NpcLine(who,art,text)},replies.ToArray());
        }
        private void OpenDemoLord()
        {
            var state=owner.State;activeObject="demo-lord";interactionObject="town_quest";
            if(state.Demo.Stage==BaseDemoStage.RuinsCleared||BaseDemoDefense.Delivered(state))
            {
                bool deliver=state.Demo.Stage==BaseDemoStage.RuinsCleared;bool unassigned=deliver&&state.Demo.AcceptedDay==0;var reward=state.Demo.RuinsReward;
                // TOWN_03_B（没接委托就先把人救回来）／TOWN_03_C（接了委托正常交付）／未交付时的日常催促。
                var questLines=new List<NpcLine>();
                if(unassigned)
                {
                    questLines.Add(new NpcLine(BaseScriptNames.Lord,BaseScriptNames.Portrait("lord"),"哦？正要找人去查探镇西的"+BaseScriptNames.Ruins+"，你竟已经把里面的古裔祭司给料理了？"));
                    questLines.Add(new NpcLine("获救镇民","villager","爵士大人！多亏这位壮士手下留情，斩断了古裔妖术，不然我们哥俩早没命了！"));
                    questLines.Add(new NpcLine(BaseScriptNames.Lord,BaseScriptNames.Portrait("lord"),"［抚掌赞叹］果然是王都禁卫出身，干净利落！这份赏金与【建设许可】归你了。"));
                    questLines.Add(new NpcLine(BaseScriptNames.Lord,BaseScriptNames.Portrait("lord"),"我已传令募兵官向你敞开兵源。快去招募人手、重修"+BaseScriptNames.Camp+"的工坊与兵营吧！"));
                }
                else if(deliver)
                {
                    questLines.Add(new NpcLine("获救镇民","villager","恩人来了！爵士大人，就是他把我们从古窟的毒雾里救出来的！"));
                    questLines.Add(new NpcLine(BaseScriptNames.Lord,BaseScriptNames.Portrait("lord"),"干得漂亮！古裔居然敢在"+BaseScriptNames.Town+"眼皮底下施展“织魂术”，看来山雨欲来啊。"));
                    questLines.Add(new NpcLine(BaseScriptNames.Lord,BaseScriptNames.Portrait("lord"),"这是答应你的赏金与【建设许可】。"+BaseScriptNames.Camp+"的工坊与兵营，即日起准予重修！"));
                    questLines.Add(new NpcLine(BaseScriptNames.Lord,BaseScriptNames.Portrait("lord"),"镇上的【募兵处】也已为你放行。先去募齐剑士、弓兵与骑兵，替我守好南面门户！"));
                }
                else
                {
                    questLines.Add(new NpcLine(BaseScriptNames.Lord,BaseScriptNames.Portrait("lord"),"营地的工坊与兵营还需要分别修复。"));
                    questLines.Add(new NpcLine(BaseScriptNames.Lord,BaseScriptNames.Portrait("lord"),"有了许可，你可以到募兵处招募人手。"));
                    questLines.Add(new NpcLine(BaseScriptNames.Lord,BaseScriptNames.Portrait("lord"),"回营地修复设施之前，记得安排好建设人手。"));
                }
                questLines.Add(new NpcLine(BaseScriptNames.Lord,BaseScriptNames.Portrait("lord"),"调查奖赏：粮草 "+reward.Food+"，原料 "+reward.Materials+"，钱币 "+reward.Coins+"。修复各需原料"+BaseDemoDefense.RepairMaterialCost(state)+"、钱币"+BaseDemoDefense.RepairCoinCost(state)+"。"));
                NpcDialogue(questLines.ToArray(),new[]{new NpcReply(deliver?"交付证据并领取许可":"已领取",owner.DeliverDemoRuins,deliver),new NpcReply("告辞",ClosePanel)});return;
            }
            bool available=state.Demo.Stage==BaseDemoStage.VisitLord;
            var lordLines=new List<NpcLine>{
                new NpcLine(BaseScriptNames.Lord,BaseScriptNames.Portrait("lord"),available?"你就是从白桦村死里逃生的那名禁卫旧部？断流滩的垦民都推举你领头。":"沉眠古窟就在镇西荒野。若觉得独木难支，不妨去镇上铁匠铺寻个帮手。"),
                new NpcLine("你","",available?"白桦村已被古裔焚毁。恳请爵士准许我们在断流滩修筑营寨、招募兵马。":"我记住了。先去"+BaseScriptNames.Ruins+"看看。",true)};
            if(available)lordLines.AddRange(new[]{
                new NpcLine(BaseScriptNames.Lord,BaseScriptNames.Portrait("lord"),"［捻须沉吟］准你们立寨倒也不难，但灰岩镇的军饷与地契，从不白送外人。"),
                new NpcLine(BaseScriptNames.Lord,BaseScriptNames.Portrait("lord"),"镇西那座【沉眠古窟】近日黑雾翻涌，两名采石镇民误入其中，至今下落不明。"),
                new NpcLine(BaseScriptNames.Lord,BaseScriptNames.Portrait("lord"),"听说你是燃脉者，不惧古窟瘴气。只要你能查明异动、救回活口，手令与赏金双手奉上。")});
            NpcDialogue(lordLines.ToArray(),new[]{new NpcReply(available?"接受"+BaseScriptNames.Ruins+"调查":"已接取",()=>{if(owner.Change(next=>{if(!BaseDemo.AcceptLordQuest(next))throw new InvalidOperationException("请先了解营地现状，并在领主同层一格以内接取");},BaseScriptNames.Ruins+"调查已接取并保存节点"))ClosePanel();},available),new NpcReply("暂且告辞",ClosePanel)});
        }
        public void OpenAzhuInvitation()
        {
            activeObject="demo-partner";interactionObject="";
            NpcDialogue(new[]{new NpcLine("阿铸","azhu","站住！看你走路下盘稳如磐石，虎口全是剑茧，绝不是普通垦民！"),new NpcLine("阿铸","azhu","我叫阿铸，天生燃脉！手痒好些天了，敢不敢就在这儿跟我过两招？点到为止！")},new[]{new NpcReply("好，请赐教",owner.BeginAzhuDuel),new NpcReply("眼下有要事，改日再比",ClosePanel)});
        }
        public void OpenRuinsRetreatGuide(Action leave)
        {
            NpcDialogue(new[]{new NpcLine("你","","敌众我寡，独自硬拼太吃亏了。先撤出去，找个帮手再来。"+BaseScriptNames.Town+"里那位阿铸身手不错，或许愿意同行。",true)},new[]{new NpcReply("撤出"+BaseScriptNames.Ruins+"，寻找帮手",leave)});
        }
        public void OpenDemoPartner(string id)
        {
            var state=owner.State;bool ahe=id=="partner_farm",met=state.Demo.MetCompanions.Contains(id);activeObject="demo-partner";interactionObject="";
            string name=ahe?"阿禾":"阿铸",appearance=ahe?"ahe":"azhu";
            if(!ahe&&state.Leaders.Any(l=>l.Id==id)){NpcDialogue(new[]{new NpcLine("阿铸","azhu","我趁休沐帮老爷子拉拉风箱！要出征随时喊我，我的马刀可没生锈！")},new[]{new NpcReply("稍后再谈",ClosePanel)});return;}
            NpcDialogue(met?new[]{new NpcLine(name,appearance,ahe?"我擅长在后排射击，连射能加快出手。"+BaseScriptNames.Ruins+"的气息会伤到我；回营后我也能照看粮田。你来决定。":"你的身手我服了！我能用突进快速接敌，追上威胁后排的敌人。若留下，我就照看工坊。")}:ahe?new[]{
                new NpcLine("阿禾","ahe","你能下地走动啦！那一剑差点穿了心窝，真是命大！"),
                new NpcLine("你","","多亏你和周叔冒险回村背我出来，这份情我记下了。",true),
                new NpcLine("阿禾","ahe","别客气！我虽是凡躯，但手里的猎弓还算准，带我一个吧？")
            }:new[]{
                new NpcLine("你","","这把短剑做得不错。你也是这里的铁匠？",true),
                new NpcLine("阿铸","azhu","我是铁匠的儿子阿铸。看你像是练家子，来过几招？点到为止。")
            },new[]{new NpcReply(met?(ahe?"好，一起走":"邀请加入小队"):ahe?"交谈，了解阿禾":"询问短剑并原地切磋",()=>{if(met)owner.JoinDemoPartner(id);else if(!ahe&&BaseSceneRules.Active(state))owner.BeginAzhuDuel();else owner.MeetDemoPartner(id);}),new NpcReply("再次比武",owner.BeginAzhuDuel,!ahe&&BaseSceneRules.Active(state)&&met),new NpcReply("去迎敌战场演练",owner.BeginAzhuFieldDuel,!ahe&&BaseSceneRules.Active(state)&&BaseDemoDefense.Won(state)),new NpcReply(ahe?"先在营地照看父母":"暂不邀请",ClosePanel)});
        }
        private bool OpenDemoInteraction(string id)
        {
            if(id=="town_smith"||id=="town_blacksmith"){OpenSmithFather();return true;}
            if(!BaseDemoRuins.Enabled(owner.State)||BaseDemoPrologue.Village(owner.State))return false;
            if(OpenDemoFinaleInteraction(id))return true;
            if(id=="demo_ahe"||id=="demo_azhu"){OpenDemoPartner(id=="demo_ahe"?"partner_farm":"partner_craft");return true;}
            if(id=="demo_rescued_1"||id=="demo_rescued_2"){OpenRescuedCivilian(id);return true;}
            if(id=="demo_ruins_investigate"){OpenRuinsChallenge();return true;}
            if(BaseDemoFinale.Opened(owner.State))return false;
            if(OpenDemoDefenseInteraction(id))return true;
            if(id=="demo_ruins_exit"||id=="demo_ruins_return"){owner.TravelDemo(id=="demo_ruins_exit"?"outpost":"town");return true;}
            return false;
        }
    }
}

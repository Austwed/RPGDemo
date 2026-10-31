using System;
using System.Collections.Generic;
using System.Linq;
using BaseConquest.Battle;
using BaseConquest.Presentation;
using UnityEngine;
using UnityEngine.UI;

namespace BaseConquest.Base
{
    public sealed partial class BaseHud
    {
        private int introFarmDraft=-1;
        internal void OpenPracticeInstruction(string text,Action start)
        {root.GetComponent<Canvas>().sortingOrder=300;NpcDialogue(new[]{new NpcLine("搭档",BaseDemoIntro.CaptainArt(owner.State),text)},new[]{new NpcReply("明白，开始练习",start)});}
        internal void OpenPursuitArrival(Action begin)
        {
            NpcDialogue(new[]{new NpcLine("旁白","","【低沉的古裔骨号声穿透浓雾，西侧村头传来兵刃交击声】"),
                new NpcLine("你","","西桥头的动静停了……{Partner}还没跟上来！",true),
                new NpcLine("古裔追兵","enemy-captain","拦住他们！祭司大人有令，带“燃脉”气息的活口一个不留！"),
                new NpcLine("你","","［瞳孔微缩］他们身上的伤口是{Partner}的剑路！是从西桥头漏过来的！",true),
                new NpcLine("你","","阿禾，带乡亲们直奔东山口！这两个残兵交给我！",true)},new[]{new NpcReply("让阿禾带村民离开，准备迎敌",begin)});
        }
        private RectTransform heroChoiceLayer;
        private Button[] heroChoiceTargets;
        private Button heroChoiceConfirm;
        private Text heroChoiceHint;
        private string selectedHero="";
        private void OpenHeroChoice()
        {
            if(heroChoiceLayer!=null){heroChoiceLayer.gameObject.SetActive(true);return;}
            modal.gameObject.SetActive(false);activeObject="";interactionObject="";Joystick.ResetInput();controls.gameObject.SetActive(false);mapLabels.gameObject.SetActive(false);var header=root.Find("Header");if(header!=null)header.gameObject.SetActive(false);
            heroChoiceLayer=StretchLayer("HeroChoice",root);heroChoiceLayer.gameObject.GetOrAdd<UnityEngine.UI.Image>().color=new Color(0,0,0,0);
            heroChoiceHint=Label(heroChoiceLayer,"点击长椅旁的人物，选择你的主角",new Vector2(0,-100),new Vector2(800,55),23,TextAnchor.MiddleCenter,new Vector2(.5f,1));
            heroChoiceTargets=new Button[2];
            for(int n=0;n<2;n++)
            {
                string hero=n==0?"male":"female";
                var button=MakeButton(heroChoiceLayer,n==0?"男主角":"女主角",Vector2.zero,new Vector2(100,150),()=>ChooseHeroPreview(hero));
                button.name="HeroChoice_"+hero;button.transition=Selectable.Transition.None;button.GetComponent<Image>().color=Color.clear;
                var label=button.GetComponentInChildren<Text>();label.alignment=TextAnchor.LowerCenter;label.fontSize=17;
                heroChoiceTargets[n]=button;
            }
            heroChoiceConfirm=MakeButton(heroChoiceLayer,"确认选择",new Vector2(120,55),new Vector2(210,52),()=>owner.SelectDemoHero(selectedHero),new Vector2(.5f,0));heroChoiceConfirm.interactable=false;
            MakeButton(heroChoiceLayer,"返回主菜单",new Vector2(-120,55),new Vector2(210,52),owner.ToMainMenu,new Vector2(.5f,0));
            owner.BeginHeroChoice();UpdateHeroChoice();
        }
        private void ChooseHeroPreview(string hero)
        {
            selectedHero=hero;heroChoiceHint.text=(hero=="male"?"已选择男主角":"已选择女主角")+" · 可点击另一人切换";
            for(int n=0;n<heroChoiceTargets.Length;n++)heroChoiceTargets[n].GetComponentInChildren<Text>().color=(n==0?"male":"female")==hero?new Color(1,.82f,.35f):Color.white;
            heroChoiceConfirm.interactable=true;owner.FocusHeroChoice(hero);
        }
        internal void UpdateHeroChoice()
        {
            if(heroChoiceLayer==null||!heroChoiceLayer.gameObject.activeSelf||owner.State?.Demo?.Intro==null)return;
            var intro=owner.State.Demo.Intro;var points=new[]{owner.WorldPoint(owner.State.PlayerX,owner.State.PlayerY),owner.WorldPoint(intro.CaptainX,intro.CaptainY)};
            for(int n=0;n<2;n++)
            {
                var screen=owner.WorldCamera.WorldToScreenPoint(points[n]+owner.WorldCamera.transform.up*.75f);
                RectTransformUtility.ScreenPointToLocalPointInRectangle(heroChoiceLayer,screen,null,out var local);
                heroChoiceTargets[n].GetComponent<RectTransform>().anchoredPosition=local;
            }
        }
        internal bool OpenIntroInteraction(string id)
        {
            var s=owner.State;if(!BaseDemoIntro.Active(s)||!BaseDemoPrologue.Village(s))return false;
            var i=s.Demo.Intro;if(i.Step==0){OpenHeroChoice();return true;}
            if(id==BaseDemoIntro.DummyId){if(i.Step==4)owner.BeginDemoPractice();else NpcDialogue(new[]{new NpcLine("练习木桩","","村里的练习木桩，搭档常在这里练剑。")},new[]{new NpcReply("看看别处",ClosePanel)});return true;}
            if(id=="demo_captain"&&i.Step==1&&!BaseDemoIntro.At(s,id)){owner.ReportMessage(BaseDemoIntro.Objective(s));ClosePanel();return true;}
            if(id=="intro_farm"||id=="intro_smith")
            {
                if(activeObject!="intro-farm"||!PanelOpen)introFarmDraft=i.FarmWorkers;
                activeObject="intro-farm";BeginPanel(id=="intro_farm"?"村庄粮田 · 工人安排":"小铁匠铺 · 工人安排","intro-farm");
                int food=(int)Math.Floor(introFarmDraft*s.Rules.FarmPerWorker),need=BaseDemoIntro.VillageFood(s);
                Line("方案：粮田 "+introFarmDraft+" 名剑士 · 铁匠铺 "+(4-introFarmDraft)+" 名剑士\n当前实际粮田 "+i.FarmWorkers+" 人；确认方案后才调动。",85,21);
                Line("预计粮食日产 "+food+" · 口粮 "+need+" · 净增 "+(food-need).ToString("+0;-0;0")+"\n预计工坊产能 "+((4-introFarmDraft)*s.Rules.WorkshopPerWorker)+" 工作点／日。",75,20);
                ArmyCounter("粮田人数",introFarmDraft,2,4,workers=>{introFarmDraft=workers;OpenIntroInteraction(id);},true);
                RowButton("确认方案",0,820,()=>{
                    int before=owner.State.Demo.Intro.FarmWorkers;
                    int workers=introFarmDraft;
                    if(owner.Change(next=>{if(!BaseDemoIntro.AssignFarm(next,workers))throw new InvalidOperationException("请靠近粮田或铁匠铺再安排岗位");},"已调整村庄工人岗位")){
                        if(workers!=before)owner.EnqueueWorkerTransfer(BaseTroopKind.Melee,workers>before?"intro_smith":"intro_farm",workers>before?"intro_farm":"intro_smith",Math.Abs(workers-before));OpenIntroInteraction(id);
                    }
                });row+=60;
                RowButton("安排完成，去找搭档",0,820,()=>owner.AdvanceDemoIntro(BaseDemoIntro.FinishFarm,"粮田已有四人，去与搭档交谈"),i.Step==9&&i.FarmWorkers==4);row+=60;EndPanel();return true;
            }
            if(id=="intro_villager"&&i.Step==2)
            {
                NpcDialogue(new[]{new NpcLine("阿禾","ahe","你们可算来了！今早露水重，这批晚麦得趁晌午前抢播下去。"),
                    new NpcLine("你","","田里现在只有两名弟兄，忙不过来？",true),new NpcLine("阿禾","ahe","是啊！铁匠铺农具昨晚都修齐了，能不能把那两位弟兄调来麦田？"),
                    new NpcLine("搭档",BaseDemoIntro.CaptainArt(s),"派工一向归你拍板，去粮田把人手调成四个并按【确认】吧。")},
                    new[]{new NpcReply("我们去粮田安排",()=>owner.AdvanceDemoIntro(BaseDemoIntro.Patrol,"阿禾委托：将铁匠铺两名剑士调往粮田"))});return true;
            }
            if(id=="demo_captain")
            {
                if(i.Step==1)
                {
                    NpcDialogue(new[]{new NpcLine("搭档",BaseDemoIntro.CaptainArt(s),"醒了？昨晚后山风大，听见你在屋里翻身到半夜。"),
                        new NpcLine("你","","习惯了。以前在王都值夜，风声比这紧得多。",true),
                        new NpcLine("搭档",BaseDemoIntro.CaptainArt(s),"［轻笑］卸了那身官袍整一年，你摸剑柄的毛病还没改。"),
                        new NpcLine("搭档",BaseDemoIntro.CaptainArt(s),"官印我那年就扔了，跟着你进的山。往后别再提这事。"),
                        new NpcLine("你","","粮仓那差事辞了也好，至少不用天天防着背后的冷箭。",true),
                        new NpcLine("搭档",BaseDemoIntro.CaptainArt(s),"走吧，阿禾天刚亮就在麦田张望，八成是秋播缺人手了。")},
                        new[]{new NpcReply("一起去，先向阿禾打个招呼",()=>owner.AdvanceDemoIntro(BaseDemoIntro.Meet,"搭档加入队伍：移动角色、调整视野，向阿禾打招呼"))});return true;
                }
                if(i.Step==10)
                {
                    NpcDialogue(new[]{new NpcLine("搭档",BaseDemoIntro.CaptainArt(s),"给，昨晚趁油灯没灭，替你把松掉的剑柄青绳结重编了一遍。"),new NpcLine("你","","是以前禁卫营的盘结手法……可惜剑刃有些卷口了。",true),new NpcLine("搭档",BaseDemoIntro.CaptainArt(s),"等秋麦入仓，咱们一起去灰岩镇，找那位老铁匠用好钢重淬一遍。"),new NpcLine("搭档",BaseDemoIntro.CaptainArt(s),"先打开【背包】把剑挂上腰，让我看看你的剑法生疏了没有！")},new[]{new NpcReply("接过短剑，准备演练",()=>owner.AdvanceDemoIntro(BaseDemoIntro.ReceiveSword,"搭档退出随行，训练短剑已放入背包"))});return true;
                }
                if(i.Step==4)
                {
                    NpcDialogue(new[]{new NpcLine("搭档",BaseDemoIntro.CaptainArt(s),"我会带你练习选择、暂停、移动、驻守和鼓舞，再看看鼓舞的效果，最后向我出手。")},new[]{new NpcReply("开始演练",owner.BeginDemoPractice),new NpcReply("再看看装备",ClosePanel)});return true;
                }
                NpcDialogue(new[]{new NpcLine("搭档",BaseDemoIntro.CaptainArt(s),"我就在附近，慢慢来。")},new[]{new NpcReply("继续",ClosePanel)});return true;
            }
            if(id=="demo_villager_1"||id=="demo_villager_2"||id=="intro_villager")
            {
                // PRO_02：村民按交谈次数给首次／再次两套台词，记在序章场景状态里。
                bool zhou=id=="demo_villager_1";
                bool again=i.Talked!=null&&i.Talked.Contains(id);
                string text=id=="intro_villager"?"多谢帮忙，忙完了再一起聊聊。"
                    :zhou?(again?"有你们俩禁卫出身的大人守着村子，咱们睡得踏实！":"前两天进西山捡柴，林子里安静得邪门，连只山雀都见不着。")
                    :(again?"等这茬晚麦收进仓，冬里大伙儿就能喝上热麦粥了。":"阿禾那孩子心细，天没亮就去翻晒麦种了，快去搭把手吧。");
                string who=id=="intro_villager"?"阿禾":zhou?BaseScriptNames.LaoZhou:BaseScriptNames.ChunShen;
                string art=id=="intro_villager"?"ahe":zhou?BaseScriptNames.Portrait("laozhou"):BaseScriptNames.Portrait("chunshen");
                NpcDialogue(new[]{new NpcLine(who,art,text)},new[]{new NpcReply("回头见",()=>{
                    owner.Change(next=>{var st=next.Demo.Intro;if(st.Talked==null)st.Talked=new List<string>();if(!st.Talked.Contains(id))st.Talked.Add(id);},"");
                    ClosePanel();})});return true;
            }
            if(id=="intro_militia_1"||id=="intro_militia_2")
            {NpcDialogue(new[]{new NpcLine("护村剑士","infantry","刀枪都擦亮了，只要二位一声令下，随时能上阵！")},new[]{new NpcReply("辛苦了",ClosePanel)});return true;}
            if(i.Step>=6&&i.Step<=8)return false;
            return false;
        }
        private void IntroReminder(string text)=>NpcDialogue(new[]{new NpcLine("搭档",BaseDemoIntro.CaptainArt(owner.State),text)},new[]{new NpcReply("明白了",ClosePanel)});
        internal bool OpenIntroStory()
        {
            var s=owner.State;if(!BaseDemoIntro.Active(s)||!BaseDemoPrologue.Village(s))return false;
            if(s.Demo.Intro.Step==0){OpenHeroChoice();return true;}
            if(s.Demo.Intro.Step==1)return OpenIntroInteraction("demo_captain");
            if(BaseDemoIntro.Daily(s)||s.Demo.Intro.Step==7){owner.ReportMessage(BaseDemoIntro.Objective(s));ClosePanel();return true;}
            if(s.Demo.Intro.Step==6)
            {
                if(!s.Demo.Intro.ReporterArrived){IntroReminder("报信的乡亲正从村头跑来，等他到搭档身边再问问情况。");return true;}
                string partner=BaseDemoIntro.PlayerArt(s)=="female"?"他":"她";
                NpcDialogue(new[]{new NpcLine("报信村民","villager","［气喘吁吁］不好了！西边黑林子里涌出成群的灰瞳古裔！"),
                    new NpcLine("你","","［脸色骤变］多少人？离村口还有多远？！",true),
                    new NpcLine("报信村民","villager","怕有上百号！他们连粮车都不抢，领头的祭司托着罗盘直奔村里搜人！"),
                    new NpcLine("报信村民","villager","我再去东头通知采药的乡亲，你们快撤吧！"),
                    new NpcLine("阿禾","ahe","上百号古裔……咱们村里满打满算才四个民兵弟兄，怎么挡得住？"),
                    new NpcLine("搭档",BaseDemoIntro.CaptainArt(s),"西村口那道窄桥能卡死敌阵。我带那四名剑士去桥头挡一阵！"),
                    new NpcLine("你","","你疯了？五个人去挡上百古裔？要守一起守，你带阿禾他们走！",true),
                    new NpcLine("搭档",BaseDemoIntro.CaptainArt(s),"阿禾他们是凡躯，没有你护着，连东山口都逃不出去！"),
                    new NpcLine("搭档",BaseDemoIntro.CaptainArt(s),"别忘了还约好去灰岩镇找老铁匠淬剑呢。我拆了木桥就追上来！"),
                    new NpcLine("搭档",BaseDemoIntro.CaptainArt(s),"护村队的弟兄，跟我去西桥头迎敌！——快带乡亲往东走，别回头！"),
                    new NpcLine("阿禾","ahe","［咬紧牙关］走吧！往东村口撤，绝不能白费"+partner+"拿命争来的时间！")},
                    new[]{new NpcReply("我会把大家带出去。你一定跟上",()=>owner.AdvanceDemoIntro(BaseDemoIntro.Depart,"搭档前往村头，乡亲们正等着撤离"))});return true;
            }
            return false;
        }
    }
}

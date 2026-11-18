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
        private int introFarmDraft=-1;
        internal void OpenPracticeResult(bool completed)
        {
            string text=completed
                ?StoryText.Get("story.scene.prologue.practice_won","这剑没生疏。等等，村头那人怎么跑得这样急？")
                :StoryText.Get("story.scene.prologue.practice_retry","先把剑收了。你想再练，就过来叫我。");
            NpcDialogue(new[]{new NpcLine("搭档",BaseDemoIntro.CaptainArt(owner.State),text)},Array.Empty<NpcReply>(),onCompleted:ClosePanel);
        }
        internal void OpenPursuitRetry()
        {
            NpcDialogue(new[]{new NpcLine("你","",StoryText.Get("story.scene.prologue.pursuit_retry",
                "还不能倒在这儿。先带乡亲们往东走。"),true)},Array.Empty<NpcReply>(),onCompleted:ClosePanel);
        }
        internal void OpenPursuitArrival(Action begin)
        {
            NpcDialogue(new[]{new NpcLine("旁白","","村西的兵刃声忽然停了。"),
                new NpcLine("你","","搭档还没跟上来！",true),
                new NpcLine("古裔追兵","galan","截住东口！别让他们过去！"),
                new NpcLine("你","",StoryText.Get("story.scene.prologue.pursuit_royal_sword","王室的剑怎么会在你们手里？"),true),
                new NpcLine("你","","这伤像是搭档留下的。他们从西桥过来。",true),
                new NpcLine("你","","阿禾，带乡亲们直奔东山口！这两个残兵交给我！",true)},new[]{new NpcReply("让阿禾带村民离开，准备迎敌",begin)},guide:true);
        }
        private RectTransform heroChoiceLayer;
        private Button[] heroChoiceTargets;
        private Button heroChoiceConfirm;
        private Text heroChoiceHint;
        private string selectedHero="";
        private RectTransform heroNameLayer;
        private void OpenHeroChoice()
        {
            if(heroChoiceLayer!=null){heroChoiceLayer.gameObject.SetActive(true);return;}
            modal.gameObject.SetActive(false);activeObject="";interactionObject="";Joystick.ResetInput();controls.gameObject.SetActive(false);mapLabels.gameObject.SetActive(false);summary.transform.parent.gameObject.SetActive(false);
            heroChoiceLayer=StretchLayer("HeroChoice",root);heroChoiceLayer.gameObject.GetOrAdd<UnityEngine.UI.Image>().color=new Color(0,0,0,0);
            heroChoiceHint=Label(heroChoiceLayer,"点击长椅旁的人物，选择你的主角",new Vector2(0,-100),new Vector2(800,55),23,TextAnchor.MiddleCenter,new Vector2(.5f,1));
            StyleMapName(heroChoiceHint);
            heroChoiceTargets=new Button[2];
            for(int n=0;n<2;n++)
            {
                string hero=n==0?"male":"female";
                var button=MakeButton(heroChoiceLayer,n==0?"男主角":"女主角",Vector2.zero,new Vector2(100,150),()=>ChooseHeroPreview(hero));
                button.name="HeroChoice_"+hero;button.transition=Selectable.Transition.None;button.GetComponent<Image>().color=Color.clear;
                var label=button.GetComponentInChildren<Text>();label.alignment=TextAnchor.LowerCenter;label.fontSize=17;StyleMapName(label);
                heroChoiceTargets[n]=button;
            }
            heroChoiceConfirm=MakeButton(heroChoiceLayer,"确认选择",new Vector2(120,55),new Vector2(210,52),OpenHeroName,new Vector2(.5f,0));heroChoiceConfirm.interactable=false;
            MakeButton(heroChoiceLayer,"返回主菜单",new Vector2(-120,55),new Vector2(210,52),owner.ToMainMenu,new Vector2(.5f,0));
            owner.BeginHeroChoice();UpdateHeroChoice();
        }
        private void OpenHeroName()
        {
            if(heroNameLayer!=null)return;
            heroNameLayer=StretchLayer("HeroName",heroChoiceLayer);
            heroNameLayer.gameObject.GetOrAdd<Image>().color=new Color(0,0,0,.58f);
            var card=Box("NameConfirmation",heroNameLayer,new Vector2(.5f,.5f),Vector2.zero,new Vector2(490,258),DemoUiTheme.Current.Panel);
            Label(card,"为主角取名",new Vector2(0,88),new Vector2(440,45),27,TextAnchor.MiddleCenter);
            var box=Box("PlayerNameInput",card,new Vector2(.5f,.5f),new Vector2(0,24),new Vector2(388,54),DemoUiTheme.Current.Panel);
            DemoUiTheme.Current.Surface(box.GetComponent<Image>(),inset:true);
            var typed=Label(box,"",Vector2.zero,new Vector2(354,42),23);
            typed.supportRichText=false;
            var placeholder=Label(box,"点击输入姓名",Vector2.zero,new Vector2(354,42),21);
            var input=box.gameObject.AddComponent<InputField>();
            input.targetGraphic=box.GetComponent<Image>();input.textComponent=typed;input.placeholder=placeholder;
            input.characterLimit=BaseDemoIntro.PlayerNameLimit;
            var hint=Label(card,"最多 12 个字。姓名将用于存档名称。",new Vector2(0,-22),new Vector2(440,30),17,TextAnchor.MiddleCenter);
            var confirm=MakeButton(card,"确认姓名",new Vector2(102,-85),new Vector2(174,48),()=>
            {
                if(!BaseDemoIntro.TryPlayerName(input.text,out var name))return;
                owner.SelectDemoHero(selectedHero,name);
                if(heroNameLayer!=null&&!string.IsNullOrEmpty(owner.Message))hint.text=owner.Message;
            });
            confirm.name="ConfirmPlayerName";confirm.interactable=false;
            input.onValueChanged.AddListener(value=>
            {
                confirm.interactable=BaseDemoIntro.TryPlayerName(value,out _);
                hint.text=confirm.interactable||string.IsNullOrWhiteSpace(value)?"最多 12 个字。姓名将用于存档名称。":"请使用文字或数字，去掉点号等特殊符号。";
            });
            MakeButton(card,"返回选角",new Vector2(-102,-85),new Vector2(174,48),()=>
            {
                heroNameLayer.gameObject.SetActive(false);
                UnityEngine.Object.Destroy(heroNameLayer.gameObject);heroNameLayer=null;
            });
        }
        private void ChooseHeroPreview(string hero)
        {
            selectedHero=hero;heroChoiceHint.text=(hero=="male"?"已选择男主角":"已选择女主角")+" · 可点击另一人切换";
            for(int n=0;n<heroChoiceTargets.Length;n++)heroChoiceTargets[n].GetComponentInChildren<Text>().color=(n==0?"male":"female")==hero?new Color(1,.82f,.35f):Color.white;
            heroChoiceConfirm.interactable=true;owner.FocusHeroChoice(hero);RefreshQuestCue();
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
            RefreshQuestCue();
        }
        internal bool OpenIntroInteraction(string id)
        {
            var s=owner.State;if(!BaseDemoIntro.Active(s)||!BaseDemoPrologue.Village(s))return false;
            var i=s.Demo.Intro;if(i.Step==0){OpenHeroChoice();return true;}
            if(id==BaseDemoIntro.DummyId){if(i.Step==4)owner.BeginDemoPractice();else NpcDialogue(new[]{new NpcLine("练习木桩","","村里的练习木桩，搭档常在这里练剑。")},new[]{new NpcReply("看看别处",ClosePanel)});return true;}
            if(id=="demo_captain"&&i.Step==1&&!BaseDemoIntro.At(s,id)){ClosePanel();return true;}
            if(id=="intro_farm"||id=="intro_smith")
            {
                OpenIntroProduction(id);
                return true;
            }
            if(id=="intro_villager"&&i.Step==2)
            {
                NpcDialogue(new[]{new NpcLine("阿禾","ahe","可算等到你们了！这茬晚麦得赶在晌午前下种。"),
                    new NpcLine("你","","田里现在只有两名弟兄，忙不过来？",true),new NpcLine("阿禾","ahe","农具昨晚修齐了。能把铁匠铺那两位弟兄调过来吗？"),
                    new NpcLine("搭档",BaseDemoIntro.CaptainArt(s),"去粮田把人手调成四个。你一点【确认】，他们就过来。")},
                    new[]{new NpcReply("我们去粮田安排",()=>owner.AdvanceDemoIntro(BaseDemoIntro.Patrol))},guide:true);return true;
            }
            if(id=="demo_captain")
            {
                if(i.Step==1)
                {
                    string portrait=BaseDemoIntro.CaptainArt(s);
                    var meet=new NpcReply("一起去，先向阿禾打个招呼",()=>owner.AdvanceDemoIntro(BaseDemoIntro.Meet));
                    NpcDialogue(new[]{new NpcLine("搭档",portrait,"昨夜山风刮得紧。我听你翻了半宿，没睡踏实吧？"),
                        new NpcLine("你","","习惯了。以前在王都值夜，风声比这紧得多。",true),
                        new NpcLine("搭档",portrait,"［轻笑］卸了那身官袍整一年，你摸剑柄的毛病还没改。"),
                        new NpcLine("搭档",portrait,"那年我把官印一放，就跟你进了山。是我自己选的。"),
                        new NpcLine("你","","辞了也好。在王都，背后的冷箭比明刀难防。",true),
                        new NpcLine("搭档",portrait,"走吧，阿禾天刚亮就在麦田张望，八成是秋播缺人手了。")},
                        new[]{meet,new NpcReply("聊聊从前在王都值夜",()=>NpcDialogue(new[]{
                            new NpcLine("你","","那年在宫门外站了一夜，冻得连剑都拔不出鞘。",true),
                            new NpcLine("搭档",portrait,"那时候哪想过还能睡到天亮。走吧，别让阿禾等急了。")
                        },new[]{meet},guide:true))},guide:true);return true;
                }
                if(i.Step==10)
                {
                    NpcDialogue(new[]{new NpcLine("搭档",BaseDemoIntro.CaptainArt(s),"给。昨晚灯还亮着，我把剑柄上松了的青绳结重新打好了。"),new NpcLine("你","","是以前禁卫营的盘结手法……可惜剑刃有些卷口了。",true),new NpcLine("搭档",BaseDemoIntro.CaptainArt(s),"等秋麦入仓，咱们去灰岩镇，请老铁匠把剑重淬一遍。"),new NpcLine("搭档",BaseDemoIntro.CaptainArt(s),"打开【背包】把剑佩上，我看看你还记得几招。")},new[]{new NpcReply("接过短剑，准备演练",()=>owner.AdvanceDemoIntro(BaseDemoIntro.ReceiveSword))},guide:true);return true;
                }
                if(i.Step==4)
                {
                    NpcDialogue(new[]{new NpcLine("搭档",BaseDemoIntro.CaptainArt(s),"先拿木桩找找手感。等我说好了，你再朝我出剑。")},new[]{new NpcReply("开始演练",owner.BeginDemoPractice),new NpcReply("再看看装备",ClosePanel)},guide:true);return true;
                }
                NpcDialogue(new[]{new NpcLine("搭档",BaseDemoIntro.CaptainArt(s),"我就在附近，慢慢来。")},new[]{new NpcReply("继续",ClosePanel)});return true;
            }
            if(id=="demo_villager_1"||id=="demo_villager_2"||id=="intro_villager")
            {
                bool again=i.Talked!=null&&i.Talked.Contains(id);
                string text=id=="intro_villager"?"多谢帮忙，忙完了再一起聊聊。"
                    :again?"又碰上了。今天过得还顺当？":id=="demo_villager_1"?"早啊，出来走走？":"早。今天天气不错。";
                string who=id=="intro_villager"?"阿禾":"村民";
                string art=id=="intro_villager"?"ahe":"villager";
                NpcDialogue(new[]{new NpcLine(who,art,text)},new[]{new NpcReply("回头见",()=>{
                    owner.Change(next=>{var st=next.Demo.Intro;if(st.Talked==null)st.Talked=new List<string>();if(!st.Talked.Contains(id))st.Talked.Add(id);},"");
                    ClosePanel();})});return true;
            }
            if (id == "intro_chunshen" || id == "intro_laozhou")
            {
                bool chunshen = id == "intro_chunshen";
                var manager = BaseDemoIntro.VillageManager(chunshen ? "intro_farm" : "intro_smith");
                string greeting = chunshen
                    ? "早啊。阿禾在田埂那头看麦种，我正好在这儿照应。"
                    : "早。农具昨晚都修好了，谁家要取就来找我。";
                NpcDialogue(new[] { new NpcLine(manager.Name, manager.Art, greeting) }, new[] { new NpcReply("回头见", ClosePanel) });
                return true;
            }
            if(id=="intro_militia_1"||id=="intro_militia_2")
            {NpcDialogue(new[]{new NpcLine("护村剑士","saber","刀枪都擦亮了，只要二位一声令下，随时能上阵！")},new[]{new NpcReply("辛苦了",ClosePanel)});return true;}
            if(i.Step>=6&&i.Step<=8)return false;
            return false;
        }

        private void OpenIntroProduction(string id)
        {
            var s=owner.State;var i=s.Demo.Intro;
            bool farm=id=="intro_farm",canAssign=BaseDemoIntro.Daily(s)&&i.Step>=9;
            if(activeObject!=id||!PanelOpen)introFarmDraft=i.FarmWorkers;
            activeObject=id;
            var (actions,filters,list)=OpenFacilityPanel(id,farm?"村庄粮田":"小铁匠铺","岗位与生产","村庄工人");
            const float height = 370;
            float shrink = panel.rect.height - height;
            panel.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);
            foreach (string path in new[] { "Left", "Left/List", "Right", "Right/List" })
            {
                var rect = (RectTransform)panel.Find(path);
                rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, rect.rect.height - shrink);
            }
            var feedbackPosition = panelFeedback.rectTransform.anchoredPosition;
            panelFeedback.rectTransform.anchoredPosition = feedbackPosition + Vector2.up * shrink;
            questFarmTarget=null;
            int food=(int)Math.Floor(introFarmDraft*s.Rules.FarmPerWorker),need=BaseDemoIntro.VillageFood(s);
            int onsite=farm?introFarmDraft:4-introFarmDraft;
            var manager = BaseDemoIntro.VillageManager(id);
            FacilityText(actions,$"负责人 · {manager.Name}     岗位 · {onsite}/4");
            FacilityText(actions,$"粮食预计 +{food} · 口粮 {need} · 净增 {(food-need):+0;-0;0}");
            FacilityText(actions,$"铁匠铺预计 {(4-introFarmDraft)*s.Rules.WorkshopPerWorker:0.#} 工作点／日");
            if(canAssign)
            {
                var counter=FacilityCounter(actions,"VillageFarmWorkers",$"粮田工人  {introFarmDraft}",
                    ()=>{introFarmDraft--;OpenIntroProduction(id);},()=>{introFarmDraft++;OpenIntroProduction(id);},introFarmDraft>2,introFarmDraft<4);
                counter.Find("Unlock").gameObject.SetActive(false);
                bool pending=introFarmDraft!=i.FarmWorkers;
                var apply=FacilityButton(actions,"Apply","确认方案",()=>{
                    int before=owner.State.Demo.Intro.FarmWorkers,step=owner.State.Demo.Intro.Step,workers=introFarmDraft;
                    if(!owner.Change(next=>{if(!BaseDemoIntro.AssignFarm(next,workers))throw new InvalidOperationException("请靠近粮田或铁匠铺再安排岗位");},""))return;
                    if (workers != before)
                    {
                        owner.OpeningAudio?.Play(VillageOpeningAudio.Cue.Assignment);
                        owner.EnqueueWorkerTransfer(BaseTroopKind.Melee, workers > before ? "intro_smith" : "intro_farm", workers > before ? "intro_farm" : "intro_smith", Math.Abs(workers - before));
                    }
                    if(step==9&&owner.State.Demo.Intro.Step==10)
                        NpcDialogue(new[]{
                            new NpcLine("阿禾","ahe","人齐了！趁地还润着，这就把晚麦种下。"),
                            new NpcLine("搭档",BaseDemoIntro.CaptainArt(owner.State),"田里的事妥了。来找我拿剑，咱们过两招。")
                        },Array.Empty<NpcReply>(),onCompleted:ClosePanel,guide:true);
                    else OpenIntroProduction(id);
                },pending,pending);
                questFarmTarget=introFarmDraft<4?(RectTransform)counter.Find("Plus"):(RectTransform)apply.transform;
                FacilityButton(actions,"Cancel","取消方案",()=>{introFarmDraft=owner.State.Demo.Intro.FarmWorkers;OpenIntroProduction(id);},pending);
            }
            else FacilityText(actions,BaseDemoIntro.Daily(s)?"先到田埂边听阿禾说说今天的人手安排。":"村民正准备撤离，暂时不派工。");
            FacilityText(filters,$"粮田 {introFarmDraft} 人 · 铁匠铺 {4-introFarmDraft} 人",true);
            for(int n=1;n<=4;n++)
            {
                bool onFarm=n<=introFarmDraft;
                string post=onFarm?"粮田":"铁匠铺";
                FacilityText(list,$"村庄剑士 {n} · {post}"+(onFarm==(n<=i.FarmWorkers)?"":" · 待调配"));
            }
            FinishFacilityPanel();
        }
        private void IntroReminder(string text)=>NpcDialogue(new[]{new NpcLine("搭档",BaseDemoIntro.CaptainArt(owner.State),text)},new[]{new NpcReply("明白了",ClosePanel)});
        internal bool OpenIntroStory()
        {
            var s=owner.State;if(!BaseDemoIntro.Active(s)||!BaseDemoPrologue.Village(s))return false;
            if(s.Demo.Intro.Step==0){OpenHeroChoice();return true;}
            if(s.Demo.Intro.Step==1)return OpenIntroInteraction("demo_captain");
            if(BaseDemoIntro.Daily(s)||s.Demo.Intro.Step==7){ClosePanel();return true;}
            if(s.Demo.Intro.Step==6)
            {
                if(!s.Demo.Intro.ReporterArrived){IntroReminder("村头有人跑来了。等他到跟前，再问出了什么事。");return true;}
                string partner=BaseDemoIntro.PlayerArt(s)=="female"?"他":"她";
                NpcDialogue(new[]{new NpcLine("报信村民","villager","西边来了十几个异族，全穿着重甲！"),
                    new NpcLine("你","","十几个重甲？他们离村口还有多远？",true),
                    new NpcLine("报信村民","villager","刚过林子，瞧都没瞧我一眼，直奔村里去了。"),
                    new NpcLine("报信村民","villager","我再去东头通知采药的乡亲，你们快撤吧！"),
                    new NpcLine("阿禾","ahe","咱村就四个护村弟兄。怎么撤？我去叫人。"),
                    new NpcLine("搭档",BaseDemoIntro.CaptainArt(s),"西村口桥窄，我带那四名剑士在桥头拖住他们。"),
                    new NpcLine("你","","五个人挡十几个重甲？我去守桥，你带乡亲往东。",true),
                    new NpcLine("搭档",BaseDemoIntro.CaptainArt(s),"东山口那段路得你护着。阿禾他们还指望你。"),
                    new NpcLine("搭档",BaseDemoIntro.CaptainArt(s),"还约了去灰岩镇淬剑呢。我拆了木桥就赶上来！"),
                    new NpcLine("搭档",BaseDemoIntro.CaptainArt(s),"护村队跟我上西桥！你带乡亲往东，别回头！"),
                    new NpcLine("阿禾","ahe","往东走！"+partner+"在替我们争时间，别停下！")},
                    new[]{new NpcReply("我会把大家带出去。你一定跟上",()=>owner.AdvanceDemoIntro(BaseDemoIntro.Depart))},guide:true);return true;
            }
            return false;
        }
    }
}

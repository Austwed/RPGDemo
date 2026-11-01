using System;
using System.Collections.Generic;
using System.Linq;
using BaseConquest.Battle;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BaseConquest.Base
{
    public sealed partial class BaseSceneController
    {
        private BaseState beforeDemoAttack;
        public void PrepareDemoJourney()
        {
            if(!Change(BaseDemoFinale.OpenWorld,"已取得敌营位置与返乡道路；沿路行军才耗日"))return;
            RefreshDemoRegion();TravelConnection("demo_march_exit");
        }
        public void BeginDemoAttack(string mode,bool briefingConfirmed=false)
        {
            if(!State.Demo.Finale.AttackBriefingSeen&&!briefingConfirmed){Hud.OpenBanditCampBriefing(()=>BeginDemoAttack(mode,true));return;}
            if(briefingConfirmed&&!Change(next=>next.Demo.Finale.AttackBriefingSeen=true,"敌营战前对白已记录"))return;
            Companions.Capture(State);var before=State.Copy();BaseCampaignSession encounter=null;
            if(!Change(next=>encounter=BaseDemoFinale.Begin(next,mode),"队伍与军需已锁定，就近落位后暂停指挥"))return;
            beforeDemoAttack=before;
            try{BeginSceneEncounter(encounter,BaseDemoFinale.Configure,null,FinishDemoAttack,"攻占阻路营地 · 击败驻军");}
            catch(Exception error)
            {
                MapSession.LeaveBattle();SaveStore.Save(before);State=before;beforeDemoAttack=null;
                if(combatController!=null){combatController.gameObject.SetActive(false);BattleBoardView.DestroyObject(combatController.gameObject);combatController=null;}
                RefreshDemoRegion();ReportMessage("未启动进攻，已恢复现场战前状态："+error.Message);
            }
        }
        private bool FinishDemoAttack(RealtimeCampaignBattle run,bool abandon,bool mainMenu)
        {
            if(beforeDemoAttack==null)return false;
            bool victory=run?.Battle.Report?.Outcome==BattleOutcome.SideAWin;var next=run==null?beforeDemoAttack.Copy():State.Copy();
            if(victory?!BaseDemoFinale.Complete(next,run):run!=null&&!BaseDemoCombat.Fail(next,run))return false;
            if(!victory&&run!=null)BaseDemoCombat.RetreatPosition(next,false);
            if(victory)BattleScenePlacement.Capture(next,run);
            BaseEconomy.Validate(next);SaveStore.Save(next);State=next;beforeDemoAttack=null;
            // BANDIT_01_WIN：攻下敌营后先搜主帐拿物证，只播一次，再走战果处理。
            if(BaseSceneRules.Active(State)){FinishLocalScene(mainMenu,()=>{if(victory&&BaseDemoFlow.Active(State)&&!State.Demo.Flow.BanditCleared)Hud.OpenBanditVictory();else if(BaseBattleLoot.Pending(State))Hud.OpenLoot();else Hud.OpenDemoStory();});return true;}
            if(Application.isPlaying){BaseBattleGateway.MainMenu=mainMenu;BaseBattleGateway.RequestedId=null;BaseBattleGateway.ShowPrisoners=BaseBattleLoot.Pending(next);SceneManager.LoadScene(BaseBattleGateway.ExplorationScene);}
            else
            {
                var previous=combatController;combatController=null;if(previous!=null){previous.gameObject.SetActive(false);BattleBoardView.DestroyObject(previous.gameObject);}
                RefreshDemoRegion();if(BaseBattleLoot.Pending(State))Hud.OpenLoot();else Hud.OpenDemoStory();
            }
            return true;
        }
        public void InvestigateDemoSword()
        {if(Change(next=>{if(!BaseDemoFinale.Investigate(next))throw new InvalidOperationException("先处理攻营战果，返乡到断剑旁同层一格内调查");},"已记录队长断剑，Demo结束节点已保存"))Hud.OpenDemoStory();}
        private void ReloadTravelRegion()
        {if(Application.isPlaying)SceneManager.LoadScene(BaseBattleGateway.ExplorationScene);else{RefreshDemoRegion();Hud.ClosePanel();}}
    }
    public sealed partial class BaseHud
    {
        // BANDIT_01_WIN_01/02：匪首伏诛后搜主帐，拿到密信与金叶，南下返乡的路打通。
        public void OpenBanditVictory()
        {
            owner.Change(next=>next.Demo.Flow.BanditCleared=true,"");
            NpcDialogue(new[]{
                new NpcLine("你","","匪首已伏诛。搜一下主帐，看看他们和王都来人留了什么书信。",true),
                new NpcLine("搜出物证","","在火盆旁寻获半封未烧尽的密信与王都金叶——南下返回【"+BaseScriptNames.Village+"】的道路已彻底畅通！")
            },new[]{new NpcReply("处理战果",()=>{ClosePanel();if(BaseBattleLoot.Pending(owner.State))OpenLoot();else OpenDemoStory();})});
        }
        public void OpenBanditCampBriefing(Action proceed)
        {
            var lines=new System.Collections.Generic.List<NpcLine>{new NpcLine(BaseScriptNames.Chief,BaseScriptNames.Portrait("chief"),"你们倒敢找上门来。弟兄们，守住寨子！")};
            if(owner.State.Demo.Defense.BatuFate=="escaped")lines.Add(new NpcLine(BaseScriptNames.Batu,BaseScriptNames.Portrait("batu"),"这次我不会再小看你们。"));
            else lines.Add(new NpcLine(BaseScriptNames.Chief,BaseScriptNames.Portrait("chief"),"巴图死在你们手里，这笔账得算清楚。"));
            lines.Add(new NpcLine(BaseScriptNames.Huoya,BaseScriptNames.Portrait("huoya"),"弓手看住两边，别让他们冲散阵形！"));
            // BANDIT_01_START_02：顺带点出山道两侧的枯树与巨石可以拿来用（两者都有实际交互与伤害）。
            lines.Add(new NpcLine("你","","山道狭窄，注意利用两侧的【枯树】阻路，击碎高处【巨石】可顺势碾碎敌阵！",true));
            NpcDialogue(lines.ToArray(),new[]{new NpcReply("准备攻营",proceed),new NpcReply("暂不进攻",ClosePanel)});
        }
        private bool OpenDemoFinaleInteraction(string id)
        {
            if(!BaseDemoFinale.Enabled(owner.State))return false;
            if(id=="demo_march_exit"&&!BaseDemoFinale.Opened(owner.State))
            {
                if(owner.State.Demo.Defense.BatuStoryDay==0){OpenDemoDefenseAftermath();return true;}
                activeObject="demo-roads";interactionObject="";
                NpcDialogue(new[]{new NpcLine("营地斥候","chenan","缴获记录确认了敌营位置，就在营地和你的"+BaseScriptNames.Village+"之间。无论是否招降俘虏，我们都掌握了这条线索。"),new NpcLine("营地斥候","chenan","先整备损失，再带队沿路出发吧。两段道路各走一天；攻下敌营并处理战果后，就能通往"+BaseScriptNames.Village+"。")},new[]{new NpcReply("查看路线并选择出征队",owner.PrepareDemoJourney,!BaseBattleLoot.Pending(owner.State)),new NpcReply("先留营整备",ClosePanel)});return true;
            }
            if(id=="demo_camp_attack")
            {
                if(BaseDemoFinale.Later(owner.State)){OpenDemoStory();return true;}
                activeObject="demo-attack";interactionObject="";BeginPanel(BaseScriptNames.Bandit+" · 军需与进攻");
                Line("使用已经沿路抵达的真实出征队。确认后就近落位，直接进入暂停指挥。击败驻军后保留实际战损、处理战利品，再前往"+BaseScriptNames.Village+"。",125,22);
                Line("开战后失败／撤退保留双方战损和军需消耗，退回当地进攻点，不重复耗日。整备时可重选军需；改队伍需沿世界道路返营。",110,21);
                foreach(string choice in new[]{"full","half","none"}){string mode=choice;RowButton("确认进攻 · "+(mode=="full"?"尽量配足军需":mode=="half"?"最多半数供给":"不使用军需"),0,800,()=>owner.BeginDemoAttack(mode));row+=60;}
                RowButton("暂不进攻",0,800,ClosePanel);row+=60;EndPanel();return true;
            }
            if(id!="demo_broken_sword"&&id!="demo_survivor")return false;
            if(owner.State.Demo.Stage==BaseDemoStage.Finished){OpenDemoStory();return true;}
            // HOME_01_ENTER：首次踏入废墟只播一次，随行者各追加一句。
            // 断剑是搭档的佩剑，与古裔无关——迦兰只评论现场残留的织魂术气息，不碰断剑（规格 §2.4）。
            if(BaseDemoFlow.Active(owner.State)&&!owner.State.Demo.Flow.HomeEnterSeen)
            {
                owner.Change(next=>next.Demo.Flow.HomeEnterSeen=true,"");
                var s=owner.State;
                var enterLines=new List<NpcLine>{
                    new NpcLine("你","","[环顾四周焦黑的残垣] 离开时还是炊烟袅袅，如今只剩断壁残垣了。",true)};
                if(BaseAdventure.Following(s,"partner_farm"))enterLines.Add(new NpcLine("阿禾","ahe","［眼圈泛红］我家的老屋和麦场全塌了……但只要人还在，总有一天能重建起来。"));
                if(BaseAdventure.Following(s,"partner_craft"))enterLines.Add(new NpcLine("阿铸","azhu","村口铁匠炉都被砸穿了。走，去西桥头看看，你同伴一定留下了线索！"));
                if(s.Leaders.Any(l=>BaseAdventure.Following(s,l.Id)&&l.IsAlien))
                    enterLines.Add(new NpcLine(BaseScriptNames.Galan,BaseScriptNames.Portrait("galan"),"这片焦土上还残留着织魂术的气息……下手的不是寻常部族。"));
                NpcDialogue(enterLines.ToArray(),new[]{new NpcReply("走向西桥头",()=>{ClosePanel();OpenDemoFinaleInteraction(id);})});
                return true;
            }
            activeObject="demo-sword";interactionObject="";
            NpcDialogue(new[]{
                new NpcLine("旁白","","在烧焦的桥桩与乱箭之间，半截断裂的剑刃正斜插在冻土里。"),
                new NpcLine("你","","［指尖微颤］剑柄上这道被火燎焦的禁卫青绳结……是{Partner}的佩剑！",true),
                new NpcLine("你","","断口平滑如镜，是王室秘银重剑斩断的。地上没有血泊，只有向西进山的囚车辙印！",true),
                new NpcLine("你","","［将断剑收入怀中］还活着……不管幕后黑手是古裔还是王都，我定会把你救回来！",true)
            },new[]{new NpcReply("记录断剑 · 完成本次Demo",owner.InvestigateDemoSword),new NpcReply("稍后调查",ClosePanel)});return true;
        }
    }
}

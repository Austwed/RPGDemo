using System;
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
            if(BaseSceneRules.Active(State)){FinishLocalScene(mainMenu,()=>{if(BaseBattleLoot.Pending(State))Hud.OpenLoot();else Hud.OpenDemoStory();});return true;}
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
        public void OpenBanditCampBriefing(Action proceed)
        {
            var lines=new System.Collections.Generic.List<NpcLine>{new NpcLine("大当家","enemy-captain","你们倒敢找上门来。弟兄们，守住寨子！")};
            if(owner.State.Demo.Defense.BatuFate=="escaped")lines.Add(new NpcLine("巴图","enemy-captain","这次我不会再小看你们。"));
            else lines.Add(new NpcLine("大当家","enemy-captain","巴图死在你们手里，这笔账得算清楚。"));
            lines.Add(new NpcLine("豁牙","enemy-captain","弓手看住两边，别让他们冲散阵形！"));
            NpcDialogue(lines.ToArray(),new[]{new NpcReply("准备攻营",proceed),new NpcReply("暂不进攻",ClosePanel)});
        }
        private bool OpenDemoFinaleInteraction(string id)
        {
            if(!BaseDemoFinale.Enabled(owner.State))return false;
            if(id=="demo_march_exit"&&!BaseDemoFinale.Opened(owner.State))
            {
                if(owner.State.Demo.Defense.BatuStoryDay==0){OpenDemoDefenseAftermath();return true;}
                activeObject="demo-roads";interactionObject="";
                NpcDialogue(new[]{new NpcLine("营地斥候","chenan","缴获记录确认了敌营位置，就在营地和你的故乡之间。无论是否招降俘虏，我们都掌握了这条线索。"),new NpcLine("营地斥候","chenan","先整备损失，再带队沿路出发吧。两段道路各走一天；攻下敌营并处理战果后，就能通往故乡。")},new[]{new NpcReply("查看路线并选择出征队",owner.PrepareDemoJourney,!BaseBattleLoot.Pending(owner.State)),new NpcReply("先留营整备",ClosePanel)});return true;
            }
            if(id=="demo_camp_attack")
            {
                if(BaseDemoFinale.Later(owner.State)){OpenDemoStory();return true;}
                activeObject="demo-attack";interactionObject="";BeginPanel("阻路土匪营地 · 军需与进攻");
                Line("使用已经沿路抵达的真实出征队。确认后就近落位，直接进入暂停指挥。击败驻军后保留实际战损、处理战利品，再前往故乡。",125,22);
                Line("开战后失败／撤退保留双方战损和军需消耗，退回当地进攻点，不重复耗日。整备时可重选军需；改队伍需沿世界道路返营。",110,21);
                foreach(string choice in new[]{"full","half","none"}){string mode=choice;RowButton("确认进攻 · "+(mode=="full"?"尽量配足军需":mode=="half"?"最多半数供给":"不使用军需"),0,800,()=>owner.BeginDemoAttack(mode));row+=60;}
                RowButton("暂不进攻",0,800,ClosePanel);row+=60;EndPanel();return true;
            }
            if(id!="demo_broken_sword"&&id!="demo_survivor")return false;
            if(owner.State.Demo.Stage==BaseDemoStage.Finished){OpenDemoStory();return true;}
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

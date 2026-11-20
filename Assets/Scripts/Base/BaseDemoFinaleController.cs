using System;
using System.Collections.Generic;
using System.Linq;
using BaseConquest.Battle;
using BaseConquest.Presentation;
using BaseConquest.Story;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace BaseConquest.Base
{
    public sealed partial class BaseSceneController
    {
        private BaseState beforeDemoAttack;
        public void PrepareDemoJourney()
        {
            if(BaseDemoFinale.Opened(State)){TravelConnection("demo_march_exit");return;}
            if(!Change(BaseDemoFinale.OpenWorld,"已取得敌营位置与返乡道路；沿路行军才耗日"))return;
            RefreshDemoRegion();TravelConnection("demo_march_exit");
        }
        public void BeginDemoAttack(string mode,bool briefingConfirmed=false,BaseBattlePreparation prepared=null)
        {
            if(!State.Demo.Finale.AttackBriefingSeen&&!briefingConfirmed)
            {if(prepared!=null)ShowDeploymentBriefing();Hud.OpenBanditCampBriefing(()=>BeginDemoAttack(mode,true,prepared));return;}
            Companions.Capture(State);var before=State.Copy();BaseCampaignSession encounter=null;
            if (!Change(next =>
            {
                next.Demo.Finale.AttackBriefingSeen = true;
                encounter = prepared == null ? BaseDemoFinale.Begin(next, mode) : prepared.Begin(next);
            }, "队伍与军需已锁定")) { if (prepared != null) Hud.RefreshPreparedFailure(); return; }
            beforeDemoAttack=before;
            try{BeginSceneEncounter(encounter,BaseDemoFinale.Configure,null,FinishDemoAttack,"攻占阻路营地 · 击败驻军",prepared);}
            catch(Exception error)
            {
                MapSession.LeaveBattle();SaveStore.Save(before);State=before;beforeDemoAttack=null;
                EndDeploymentWorld(false);
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
            // 攻寨对白播完后进入原有战果处理。
            if(BaseSceneRules.Active(State)){FinishLocalScene(mainMenu,()=>{if(victory&&BaseDemoFlow.Active(State)&&!State.Demo.Flow.BanditCleared)Hud.OpenBanditVictory();else if(BaseBattleLoot.Pending(State))Hud.OpenLoot();else Hud.OpenDemoStory();});return true;}
            if(Application.isPlaying){BaseBattleGateway.MainMenu=mainMenu;BaseBattleGateway.RequestedId=null;BaseBattleGateway.ShowPrisoners=BaseBattleLoot.Pending(next);SceneLoadingCover.LoadExploration();}
            else
            {
                var previous=combatController;combatController=null;if(previous!=null){previous.gameObject.SetActive(false);BattleBoardView.DestroyObject(previous.gameObject);}
                RefreshDemoRegion();if(BaseBattleLoot.Pending(State))Hud.OpenLoot();else Hud.OpenDemoStory();
            }
            return true;
        }
        public void InvestigateDemoSword()
        {
            if(!Change(next=>{if(!BaseDemoFinale.Investigate(next))throw new InvalidOperationException("请靠近断剑。");},""))return;
            RefreshDemoRegion();
        }
        private readonly List<BasePoint>[] volunteerRoutes=new List<BasePoint>[2];
        private readonly int[] volunteerSteps=new int[2];
        private readonly BasePoint[] volunteerPosts=new BasePoint[2];
        private void ResetVolunteerRoutes(){volunteerRoutes[0]=volunteerRoutes[1]=null;}
        internal void TickVillageReturn()
        {
            if(State?.Campaign.CurrentMapId!=BaseDemoFinale.Home || !BaseDemoFinale.Rebuilding(State) ||
                State.Demo.Finale.RebuildMask!=7 || BaseDemoFinale.HomeVolunteers(State))
            {
                ResetVolunteerRoutes();
                return;
            }
            if(ConstructionMode||BaseBattleGateway.MainMenu)return;
            // Repairing the house finishes its dialogue before rebuilding the scene with the two arrivals.
            if(Hud.DialogueOpen && !npcViews.ContainsKey(BaseDemoFinale.Builder))return;
            var map=BaseSceneInstances.Layout(State,BaseDemoFinale.Home);
            bool allArrived=true;
            for(int i=0;i<2;i++)
            {
                var id=i==0 ? BaseDemoFinale.Builder : BaseDemoFinale.Helper;
                var person=map.Objects.Single(o=>o.Id==id);
                var route=volunteerRoutes[i];
                if(route==null)
                {
                    var target=volunteerPosts[i]=BaseDemoFinale.VolunteerPost(State,i);
                    volunteerRoutes[i]=route=BaseStageWalk.Route(State,new BasePoint(person.X,person.Y),target);
                    volunteerSteps[i]=0;
                }
                bool arrived=BaseStageWalk.Advance(ref person.X,ref person.Y,volunteerPosts[i],Time.deltaTime,route,ref volunteerSteps[i]);
                allArrived &= arrived;
                if(npcViews.TryGetValue(id,out var npc)) npc.MoveTo(WorldPoint(person.X,person.Y));
            }
            InvalidateObjects();
            if(allArrived && CommitChange(next=>{
                if(!BaseDemoFinale.WelcomeVolunteers(next))throw new InvalidOperationException("村民尚未走到麦田边");
            },"",fromInteraction:false))InvalidateObjects();
        }

        internal void CollectDemoCampSupplies()
        {
            if(!Change(next=>
            {
                if(!BaseDemoFinale.ClaimCampSupplies(next))throw new InvalidOperationException(
                    StoryText.Get("story.ui.finale.camp_supplies_unavailable","此处已没有可搜查的物资。"));
            },""))return;
            RefreshDemoRegion();
            Hud.OpenDemoCampSuppliesFound();
        }

        private void ReloadTravelRegion()
        {if(Application.isPlaying)SceneLoadingCover.LoadExploration();else{RefreshDemoRegion();Hud.ClosePanel();}}
    }
    public sealed partial class BaseHud
    {
        public void OpenBanditVictory()
        {
            owner.Change(next=>next.Demo.Flow.BanditCleared=true,"");
            NpcDialogue(new[]{
                new NpcLine("旁白","","黑鸦寨已拿下，通往白桦村的南路开了。")
            },new[]{new NpcReply("处理战果",()=>{ClosePanel();if(BaseBattleLoot.Pending(owner.State))OpenLoot();else OpenDemoStory();})});
        }
        public void OpenBanditCampBriefing(Action proceed)
        {
            bool batuEscaped = owner.State.Demo.Defense.BatuFate == "escaped";
            var lines = new List<NpcLine>{new NpcLine(BaseScriptNames.Chief,BaseScriptNames.Portrait("chief"),
                batuEscaped ? "镇外营地的人还敢上山？弓手，给我看住山道！" : "巴图就折在你们手里？弓手，守住山道！")};
            if (batuEscaped) lines.Add(new NpcLine(BaseScriptNames.Batu,BaseScriptNames.Portrait("batu"),"寨主，上回丢的脸，今天我自己挣回来！"));
            lines.Add(new NpcLine(BaseScriptNames.Huoya,BaseScriptNames.Portrait("huoya"),"山道窄，他们铺不开。弟兄们，顶住！"));
            NpcDialogue(lines.ToArray(),new[]{new NpcReply("准备攻营",proceed),new NpcReply("暂不进攻",ClosePanel)});
        }
        internal void OpenDemoCampSuppliesFound()
        {
            NpcDialogue(new[]{new NpcLine("旁白","",StoryText.Get("story.ui.finale.camp_supplies_collected","他们走得急，这些东西都没带走。"))},
                Array.Empty<NpcReply>(),onCompleted:ClosePanel);
        }

        internal bool TryOpenHomeArrival()
        {
            var state=owner.State;
            if(PanelOpen||owner.ConstructionMode||BaseBattleGateway.MainMenu||!BaseDemoFlow.Active(state)||
                state.Campaign.CurrentMapId!=BaseDemoFinale.Home||BaseBattleLoot.Pending(state))return false;
            if(state.Demo.Stage==BaseDemoStage.Finished && !state.Demo.Finale.EndingSeen)
            {OpenDemoStory(showCompletion:true);return true;}
            if(state.Demo.Stage!=BaseDemoStage.AttackWon||state.Demo.Flow.HomeEnterSeen)return false;
            activeObject="demo-home-arrival";
            var lines=new List<NpcLine>{
                new NpcLine("旁白","","门板歪了，粮仓也被搬空。巷子里听不见人声。")
            };
            if(BaseAdventure.Following(state,"partner_farm"))
                lines.Add(new NpcLine("阿禾","ahe","院子翻成这样……人还在，咱们慢慢收拾。"));
            if(BaseAdventure.Following(state,"partner_craft"))
                lines.Add(new NpcLine("阿铸","azhu","门口的石台都磕掉了一角。走，去西桥头看看。"));
            string controllerId=state.Demo.Flow.FirstControllerId;
            if(!string.IsNullOrEmpty(controllerId)&&BaseAdventure.Following(state,"partner_recruit:"+controllerId))
                lines.Add(new NpcLine(BaseScriptNames.Galan,BaseScriptNames.Portrait("galan"),"看这几列脚印。散开的时候，间距还是一样。来的人练过。"));
            NpcDialogue(lines.ToArray(),Array.Empty<NpcReply>(),lockExitUntilFinished:true,onCompleted:FinishArrival,guide:true);
            return true;

            void FinishArrival()
            {
                owner.Change(next=>next.Demo.Flow.HomeEnterSeen=true,"");
                ClosePanel();
            }
        }

        private bool OpenDemoFinaleInteraction(string id)
        {
            if(!BaseDemoFinale.Enabled(owner.State))return false;
            if (id == BaseDemoNpcs.CampScout)
            {
                var state = owner.State;
                if (BaseDemoFinale.SecondRaidReportPending(state)) OpenSecondRaidVictoryReport();
                else if (BaseDemoDefense.AnyDue(state)) OpenDemoScoutWarning();
                else if (!BaseDemoDefense.Won(state)) OpenChenanRoadChat();
                else
                {
                    string advice = BaseDemoFinale.Later(state)
                        ? "寨子那头的路通了。出了南口，沿官道就能回白桦村。"
                        : "大人，趁他们还没再打过来，咱们先去黑鸦寨。走营地南口上官道，我把路给你标好了。";
                    NpcDialogue(new[] { new NpcLine(BaseScriptNames.Scout, "chenan", advice) }, Array.Empty<NpcReply>(), onCompleted: ClosePanel);
                }
                return true;
            }
            if (id == "demo_march_exit" && !BaseDemoFinale.Opened(owner.State))
            {
                if (BaseDemoFinale.SecondRaidReportPending(owner.State)) OpenSecondRaidVictoryReport();
                else if (BaseDemoDefense.AnyDue(owner.State)) OpenDemoScoutWarning();
                else if (owner.State.Demo.Defense.BatuStoryDay == 0) OpenDemoDefenseAftermath();
                else owner.PrepareDemoJourney();
                return true;
            }
            if(id=="demo_camp_attack")
            {
                if(BaseDemoFinale.Later(owner.State)){OpenDemoStory();return true;}
                OpenBattlePreparation(BasePrepRoute.BlackCrow);return true;
            }
            if(id==BaseDemoFinale.CampSuppliesMarker){owner.CollectDemoCampSupplies();return true;}
            var current=owner.State;
            if(current.Campaign.CurrentMapId==BaseDemoFinale.HomeField && id=="demo_broken_sword")
            {
                activeObject=id;interactionObject="";
                NpcDialogue(new[]{new NpcLine("断剑","","一把断裂的剑躺在地上。剑柄上沾着泥的青绳结，看起来很熟悉。")},
                    new[]{new NpcReply("这是搭档的剑……收起来吧。",owner.InvestigateDemoSword),
                          new NpcReply("先去村庄看看吧，或许呢？",ClosePanel)});
                return true;
            }
            if(current.Campaign.CurrentMapId!=BaseDemoFinale.Home)return false;
            activeObject=id;interactionObject="";
            if(id==BaseDemoFinale.Builder && BaseDemoFinale.HomeVolunteers(current))
            {
                bool first=!current.Demo.Finale.VillageRebuilt;
                NpcDialogue(new[]{new NpcLine("村民","villager",first?
                    "走到这一步真不容易，多亏了您。村子往后还要修，我们也能出把力。您吩咐吧。":"还要在哪儿盖房？您指给我看。")},
                    new[]{new NpcReply("村庄还需要建一些设施……",()=>LeaveVillageTalk(true)),new NpcReply("没什么事了。",()=>LeaveVillageTalk(false))});
                return true;
            }
            if(id!="home"&&id!="farm"&&id!="workshop"&&id!="barracks")return false;
            if(BaseDemoFinale.HomeRepaired(current,id)){OpenSettlementFacility(id);return true;}
            if(id=="home"&&current.Demo.Finale.HouseDialogueStep>0){OpenVillageHouse();return true;}
            string text=id=="farm"?"田埂上全是马蹄印。那天清早，阿禾还蹲在这里清点麦种。":
                id=="workshop"?"风箱倒在墙根，钳子和铁料被翻了一地。":"自家的门板歪在门框上。";
            string cost="修理 · 原料 "+BaseDemoDefense.RepairMaterialCost(current)+"　钱币 "+BaseDemoDefense.RepairCoinCost(current);
            NpcDialogue(new[]{new NpcLine("旁白","",text)},new[]{new NpcReply(cost,()=>{
                if(!BaseDemoFinale.CanPayRepair(owner.State)){owner.ReportMessage("原料或钱币不足");return;}
                if(id=="home")
                {
                    if(owner.Change(next=>{if(!BaseDemoFinale.BeginHouseTalk(next))throw new InvalidOperationException("请到家门口修理。");},"")) OpenVillageHouse(true);
                }
                else FinishVillageRepair(id);
            }),new NpcReply("稍后再来",ClosePanel)});
            return true;
        }
        private bool GalanAtHouse()
        {
            var state=owner.State;var id=state.Demo.Flow?.FirstControllerId;
            return !string.IsNullOrEmpty(id) && BaseAdventure.Following(state,"partner_recruit:"+id);
        }
        private static NpcLine Thug(string line)=>new NpcLine("古裔喽啰","ancient-saber",line);
        private static NpcLine GalanAside()=>new NpcLine(BaseScriptNames.Galan,BaseScriptNames.Portrait("galan"),"少蛊惑人心，我已经是这位大人的忠仆了。");
        private void OpenVillageHouse(bool opening=false)
        {
            if(owner.State.Demo.Finale.HouseDialogueStep==2){HouseDecision();return;}
            var lines=new List<NpcLine>();
            if(opening)
            {
                lines.Add(Thug("外边什么动静？人类？！"));
                lines.Add(new NpcLine("旁白","","你一剑砍服了他们。"));
            }
            lines.Add(Thug("小的没眼力见，请问有什么吩咐？"));
            NpcDialogue(lines.ToArray(),new[]{new NpcReply("你们是上次进攻村子的异族？",HouseConfession),
                new NpcReply("你们在里面做什么？",HouseConfession)});
        }
        private void HouseConfession()
        {
            if(!owner.Change(s=>{if(!BaseDemoFinale.HearHouseConfession(s))throw new InvalidOperationException("请到家门口交谈。");},""))return;
            HouseDecision(new[]{Thug("你是……你不是死了吗？！哎哟，瞧我这乌鸦嘴！"),
                Thug("我们是跟着来的。动手的是首领和精锐大人，哪轮得着我们？我们几个平日就干些端茶递水的活。"),
                Thug("那天闻见这屋里有股香味，就钻进来翻点吃的。等我们出来，首领早带人走了，像是有急事，连叫都没叫我们一声。")});
        }
        private void HouseDecision(NpcLine[] lines=null)
        {
            bool galan=GalanAtHouse();
            NpcDialogue(lines??new[]{Thug("大人，还有什么吩咐？")},new[]{
                new NpcReply("滚吧，别让我再见到你们。",()=>{
                    var goodbye=new List<NpcLine>{Thug("大人慈悲！小的这就告辞，再不踏进村子半步。")};
                    if(galan){goodbye.Add(Thug("不过，屋外那位可是跟我们一起出的门，半道上就没影了。如今跟了您，您可得留个心眼。"));goodbye.Add(GalanAside());}
                    ResolveHouse(goodbye);
                }),new NpcReply(galan?"你们认识他吗？（指向屋外的迦兰）":"你们认不认识一个叫迦兰的古裔？",()=>{
                    var answer=new List<NpcLine>{Thug("迦兰？当然认识。跟我们一起出发的，半路上就不见了。")};
                    if(galan){answer.Add(Thug("哎哟，这不就是迦兰队长吗？别来无恙啊！"));answer.Add(GalanAside());}
                    HouseDecision(answer.ToArray());
                }),new NpcReply("我信不过你们，受死吧。",()=>ResolveHouse(new List<NpcLine>{Thug("呃啊！")}))
            });
        }
        private void ResolveHouse(List<NpcLine> lines)
        {
            if(!RepairVillage("home"))return;
            NpcDialogue(lines.ToArray(),Array.Empty<NpcReply>(),lockExitUntilFinished:true,onCompleted:()=>owner.RefreshDemoRegion());
        }
        private bool RepairVillage(string id)=>owner.Change(s=>{
            if(!BaseDemoFinale.RepairHome(s,id))throw new InvalidOperationException("请到设施旁修理，并备齐原料和钱币。");
        },"");
        private void FinishVillageRepair(string id){if(RepairVillage(id))owner.RefreshDemoRegion();}
        private void LeaveVillageTalk(bool build)
        {
            if(!owner.State.Demo.Finale.VillageRebuilt)
            {
                if(!owner.Change(s=>{if(!BaseDemoFinale.FinishRebuild(s))throw new InvalidOperationException("请到麦田旁与村民交谈。");},""))return;
            }
            ClosePanel();
            if(build)owner.ArmVillagePlacement();
            else FinishVillageConstructionVisit();
        }
        internal void FinishVillageConstructionVisit()
        {
            if(owner.State.Demo.Stage!=BaseDemoStage.Finished || owner.State.Demo.Finale.EndingSeen)return;
            OpenDemoStory(showCompletion:true);
        }

        private void OpenChenanRoadChat()
        {
            NpcLine Line(string key, string fallback) =>
                new NpcLine(BaseScriptNames.Scout, "chenan", StoryText.Get("story.scene.chenan.road." + key, fallback));
            NpcDialogue(new[] { Line("opening", "村子不好回去咯。") }, new[] {
                new NpcReply(StoryText.Get("story.scene.chenan.road.why", "为什么？"), () =>
                    NpcDialogue(new[] {
                        Line("bandits", "路上有个土匪寨子。上次运气好，他们在办宴会，我们从小路偷摸过来的。"),
                        Line("lookouts", "现在远远就能看到那边不少放哨的，就等着镇上的人路过打劫呢。")
                    }, Array.Empty<NpcReply>(), onCompleted: ClosePanel)),
                new NpcReply("回头见", ClosePanel)
            });
        }
    }
}

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
            if(Application.isPlaying){BaseBattleGateway.MainMenu=mainMenu;BaseBattleGateway.RequestedId=null;BaseBattleGateway.ShowPrisoners=BaseBattleLoot.Pending(next);SceneManager.LoadScene(BaseBattleGateway.ExplorationScene);}
            else
            {
                var previous=combatController;combatController=null;if(previous!=null){previous.gameObject.SetActive(false);BattleBoardView.DestroyObject(previous.gameObject);}
                RefreshDemoRegion();if(BaseBattleLoot.Pending(State))Hud.OpenLoot();else Hud.OpenDemoStory();
            }
            return true;
        }
        public void InvestigateDemoSword()
        {
            if(Change(next=>{if(!BaseDemoFinale.Investigate(next))throw new InvalidOperationException("先处理战果，返乡后到断剑旁同层一格内调查");},"已记录断剑，开始返乡重建"))Hud.OpenRebuild();
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

        private int rebuildProject=-1;
        private GridPosition? rebuildCell;
        private float rebuildViewBefore;
        public bool RebuildPlacementMode { get; private set; }
        public bool HomeRebuildOverview { get; private set; }
        public int RebuildProject => rebuildProject;
        public string RebuildPlacementProblem => rebuildProject<0 ? StoryText.Get("story.ui.finale.rebuild_select_project","请先选择设施") :
            !rebuildCell.HasValue ? StoryText.Get("story.ui.finale.rebuild_select_cell","点击地图选择空地") :
            BaseDemoFinale.RebuildProblem(State,rebuildProject,rebuildCell.Value.X,rebuildCell.Value.Z);

        internal void ArmHomeRebuild()
        {
            if(Busy||!BaseDemoFinale.Rebuilding(State)||State.Campaign.CurrentMapId!=BaseDemoFinale.Home)return;
            Hud.ClosePanel();
            if(Hud.PanelOpen)return;
            StopConstruction();ClearSelection();
            constructionOpen=true;RebuildPlacementMode=true;
            regionView.UpdateCamera(Hud.HomeRebuildMapInset,0);
            SelectNextRebuildProject();Hud.RefreshSummary();
        }

        private void SelectNextRebuildProject()
        {
            int mask=State.Demo.Finale.RebuildMask;
            if(mask==7){rebuildProject=-1;rebuildCell=null;Hud.RefreshConstructionPalette();return;}
            SelectRebuildProject(Enumerable.Range(0,3).First(i=>(mask&(1<<i))==0));
        }

        public void SelectRebuildProject(int item)
        {
            if(!RebuildPlacementMode||HomeRebuildOverview||item<0||item>=3)return;
            rebuildProject=item;rebuildCell=null;
            regionView.SelectCell(null);Hud.RefreshConstructionPalette();
        }

        private void SelectRebuildCell(GridPosition cell)
        {
            if(rebuildProject<0||HomeRebuildOverview||(State.Demo.Finale.RebuildMask&(1<<rebuildProject))!=0)return;
            rebuildCell=cell;regionView.SelectCell(cell);
            Hud.RefreshConstructionPalette();
        }

        internal void TintRebuildPreview(bool available)
        {
            if(rebuildCell.HasValue)regionView.TintSelection(available?new Color(.35f,1,.55f):new Color(1,.32f,.25f));
        }

        public bool ConfirmRebuildPlacement()
        {
            if(!RebuildPlacementMode||HomeRebuildOverview||Hud.PanelOpen||RebuildPlacementProblem!="")return false;
            int item=rebuildProject;var cell=rebuildCell.Value;
            if(!Change(next=>{if(!BaseDemoFinale.PlaceRebuild(next,item,cell.X,cell.Z))throw new InvalidOperationException(BaseDemoFinale.RebuildProblem(next,item,cell.X,cell.Z));},""))return false;
            rebuildCell=null;
            RefreshDemoRegion();SelectNextRebuildProject();return true;
        }

        public bool WithdrawRebuildPlacement()
        {
            if(!RebuildPlacementMode||HomeRebuildOverview||Hud.PanelOpen||rebuildProject<0)return false;
            int item=rebuildProject;
            if((State.Demo.Finale.RebuildMask&(1<<item))==0)return false;
            if(!Change(next=>{if(!BaseDemoFinale.RemoveRebuild(next,item))throw new InvalidOperationException("无法撤回设施");},""))return false;
            RefreshDemoRegion();SelectRebuildProject(item);return true;
        }

        public void ShowHomeRebuildOverview()
        {
            if(!RebuildPlacementMode||HomeRebuildOverview||Hud.PanelOpen||State.Demo.Finale.RebuildMask!=7)return;
            rebuildViewBefore=WorldCamera.orthographicSize;
            HomeRebuildOverview=true;rebuildCell=null;
            regionView.SelectCell(null);FrameRebuiltVillage();Hud.RefreshSummary();
        }

        public void ReturnToHomeRebuild()
        {
            if(!HomeRebuildOverview)return;
            ResetHomeRebuildOverview();Hud.RefreshSummary();
        }

        private void FrameRebuiltVillage()
        {
            regionView.UpdateCamera(0,0);
            regionView.SetViewSize(regionView.FullViewSize);
            regionView.Follow(Vector3.zero);
        }

        private void ResetHomeRebuildOverview()
        {
            if(!HomeRebuildOverview)return;
            HomeRebuildOverview=false;
            regionView.UpdateCamera(Hud.HomeRebuildMapInset,0);
            regionView.SetViewSize(rebuildViewBefore);regionView.Follow(PlayerCameraTarget);
        }

        public bool FinishHomeRebuild()
        {
            if(!HomeRebuildOverview||Hud.PanelOpen)return false;
            if(!Change(next=>{if(!BaseDemoFinale.FinishRebuild(next))throw new InvalidOperationException("请先完成三项重建");},""))return false;
            StopConstruction();FrameRebuiltVillage();Hud.OpenDemoStory(showCompletion: true);return true;
        }
        private void ReloadTravelRegion()
        {if(Application.isPlaying)SceneManager.LoadScene(BaseBattleGateway.ExplorationScene);else{RefreshDemoRegion();Hud.ClosePanel();}}
    }
    public sealed partial class BaseHud
    {
        private RectTransform rebuildPalette,rebuildOverview;
        private readonly Button[] rebuildProjects=new Button[3];
        private Color[] rebuildProjectColors;
        private Button rebuildPlace,rebuildWithdraw,rebuildConfirm,rebuildResume,rebuildFinish;
        private Text rebuildStatus;
        internal float HomeRebuildGutter
        {
            get
            {
                if(rebuildPalette==null||controls.rect.width<=0)return 206f;
                var edge=controls.InverseTransformPoint(rebuildPalette.TransformPoint(new Vector3(rebuildPalette.rect.xMax,0)));
                return Mathf.Clamp(edge.x-controls.rect.xMin+11,0,controls.rect.width*.35f);
            }
        }
        internal float HomeRebuildMapInset => HomeRebuildGutter / (controls.rect.width>0?controls.rect.width:1280f);
        public void OpenRebuild()=>owner.ArmHomeRebuild();

        private void BuildHomeRebuildPalette()
        {
            var wood=new Color(.33f,.24f,.16f,.98f);
            rebuildPalette=Box("HomeRebuildProjects",controls,new Vector2(0,1),new Vector2(103,-ExplorationHudLayout.HeaderHeight-228),new Vector2(184,434),wood);
            Label(rebuildPalette,StoryText.Get("story.ui.finale.rebuild_title","返乡重建"),new Vector2(0,188),new Vector2(160,36),22,TextAnchor.MiddleCenter);
            for(int i=0;i<rebuildProjects.Length;i++)
            {
                int item=i;
                rebuildProjects[i]=MakeButton(rebuildPalette,BaseDemoFinale.RebuildName(i),new Vector2(0,145-i*47),new Vector2(160,42),()=>owner.SelectRebuildProject(item));
            }
            rebuildStatus=Label(rebuildPalette,"",new Vector2(0,1),new Vector2(156,50),17,TextAnchor.MiddleCenter);
            rebuildPlace=MakeButton(rebuildPalette,"放置",new Vector2(-41,-49),new Vector2(76,42),()=>owner.ConfirmRebuildPlacement());
            var placementColors=rebuildPlace.colors;placementColors.fadeDuration=0;rebuildPlace.colors=placementColors;
            rebuildWithdraw=MakeButton(rebuildPalette,"撤回",new Vector2(41,-49),new Vector2(76,42),()=>owner.WithdrawRebuildPlacement());
            rebuildConfirm=MakeButton(rebuildPalette,"确认完成",new Vector2(0,-103),new Vector2(160,43),owner.ShowHomeRebuildOverview);
            MakeButton(rebuildPalette,"关闭",new Vector2(0,-172),new Vector2(160,43),owner.StopConstruction);

            rebuildOverview=Box("HomeRebuildOverview",controls,new Vector2(.5f,0),new Vector2(0,38),new Vector2(304,58),wood);
            MakeButton(rebuildOverview,"返回重建",new Vector2(-73,0),new Vector2(134,42),owner.ReturnToHomeRebuild);
            rebuildFinish=MakeButton(rebuildOverview,"结束 Demo",new Vector2(73,0),new Vector2(134,42),()=>owner.FinishHomeRebuild());
            rebuildResume=MakeButton(controls,"继续重建",new Vector2(103,-ExplorationHudLayout.HeaderHeight-41),new Vector2(154,44),OpenRebuild,new Vector2(0,1));
            rebuildPalette.gameObject.SetActive(false);rebuildOverview.gameObject.SetActive(false);rebuildResume.gameObject.SetActive(false);
        }

        private void RefreshHomeRebuildPalette(bool visible)
        {
            if(rebuildPalette==null)return;
            bool placing=visible&&!owner.HomeRebuildOverview;
            rebuildPalette.gameObject.SetActive(placing);
            rebuildOverview.gameObject.SetActive(visible&&owner.HomeRebuildOverview);
            rebuildResume.gameObject.SetActive(!owner.ConstructionMode&&!PanelOpen&&!owner.Busy&&BaseDemoFinale.Rebuilding(owner.State)&&owner.State.Campaign.CurrentMapId==BaseDemoFinale.Home);
            if(!placing)return;
            if(rebuildProjectColors==null)rebuildProjectColors=rebuildProjects.Select(button=>button.targetGraphic.color).ToArray();
            int mask=owner.State.Demo.Finale.RebuildMask,selected=owner.RebuildProject;
            for(int i=0;i<rebuildProjects.Length;i++)
            {
                var button=rebuildProjects[i];
                button.GetComponentInChildren<Text>().text=BaseDemoFinale.RebuildName(i)+((mask&(1<<i))!=0?" · 已放置":"");
                button.targetGraphic.color=i==selected?DemoUiTheme.Current.Gold:rebuildProjectColors[i];
            }
            string problem=owner.RebuildPlacementProblem;
            owner.TintRebuildPreview(problem=="");
            rebuildStatus.text=mask==7&&selected<0?StoryText.Get("story.ui.finale.rebuild_ready","三项设施已就位"):
                problem==""?StoryText.Get("story.ui.finale.rebuild_available","可放置 · 确认此处"):problem;
            rebuildPlace.interactable=problem=="";
            rebuildWithdraw.interactable=selected>=0&&(mask&(1<<selected))!=0;
            rebuildConfirm.interactable=mask==7;
        }
        public void OpenBanditVictory()
        {
            owner.Change(next=>next.Demo.Flow.BanditCleared=true,"");
            NpcDialogue(new[]{
                new NpcLine("你","","匪首已死。把寨里抢来的军资清点一下。",true),
                new NpcLine("旁白","","黑鸦寨已拿下，通往白桦村的南路开了。")
            },new[]{new NpcReply("处理战果",()=>{ClosePanel();if(BaseBattleLoot.Pending(owner.State))OpenLoot();else OpenDemoStory();})});
        }
        public void OpenBanditCampBriefing(Action proceed)
        {
            var lines=new List<NpcLine>{new NpcLine(BaseScriptNames.Chief,BaseScriptNames.Portrait("chief"),"断流滩的还敢上山？弓手，给我看住山道！")};
            if(owner.State.Demo.Defense.BatuFate=="escaped")lines.Add(new NpcLine(BaseScriptNames.Batu,BaseScriptNames.Portrait("batu"),"寨主，上回丢的脸，今天我自己挣回来！"));
            else lines.Add(new NpcLine(BaseScriptNames.Chief,BaseScriptNames.Portrait("chief"),"你们还敢来？巴图的账，正好在这儿算。"));
            lines.Add(new NpcLine(BaseScriptNames.Huoya,BaseScriptNames.Portrait("huoya"),"山道窄，他们铺不开。弟兄们，顶住！"));
            lines.Add(new NpcLine("你","","砍倒枯树能堵路。巨石顺着推的方向滚，自己人也得避开。",true));
            NpcDialogue(lines.ToArray(),new[]{new NpcReply("准备攻营",proceed),new NpcReply("暂不进攻",ClosePanel)});
        }
        internal void OpenDemoCampSuppliesFound()
        {
            NpcDialogue(new[]{new NpcLine("你","",StoryText.Get("story.ui.finale.camp_supplies_collected","已找到黑鸦寨剩余的物资。"),true)},
                Array.Empty<NpcReply>(),onCompleted:ClosePanel);
        }

        internal bool TryOpenHomeArrival()
        {
            var state=owner.State;
            if(PanelOpen||owner.ConstructionMode||BaseBattleGateway.MainMenu||!BaseDemoFlow.Active(state)||
                state.Campaign.CurrentMapId!=BaseDemoFinale.Home||state.Demo.Stage!=BaseDemoStage.AttackWon||
                state.Demo.Flow.HomeEnterSeen||BaseDemoFinale.Rebuilding(state)||BaseBattleLoot.Pending(state))return false;
            activeObject="demo-home-arrival";
            var lines=new List<NpcLine>{
                new NpcLine("你","","门板歪了，粮仓也被搬空。走前巷子里还有人声。",true)
            };
            if(BaseAdventure.Following(state,"partner_farm"))
                lines.Add(new NpcLine("阿禾","ahe","院子翻成这样……人还在，咱们慢慢收拾。"));
            if(BaseAdventure.Following(state,"partner_craft"))
                lines.Add(new NpcLine("阿铸","azhu","门口的石台都磕掉了一角。走，去西桥头看看。"));
            string controllerId=state.Demo.Flow.FirstControllerId;
            if(!string.IsNullOrEmpty(controllerId)&&BaseAdventure.Following(state,"partner_recruit:"+controllerId))
                lines.Add(new NpcLine(BaseScriptNames.Galan,BaseScriptNames.Portrait("galan"),"你看这些脚印，散开了还走得齐。来的人受过操练。"));
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
            if(id=="demo_march_exit"&&!BaseDemoFinale.Opened(owner.State))
            {
                if(owner.State.Demo.Defense.BatuStoryDay==0){OpenDemoDefenseAftermath();return true;}
                if(BaseDemoFinale.OccupiedBySecondDefense(owner.State)){owner.PrepareDemoJourney();return true;}
                activeObject="demo-roads";interactionObject="";
                NpcDialogue(new[]{
                    new NpcLine(BaseScriptNames.Scout,"chenan","大人，断流滩南边的官道能走了，沿路可到黑鸦寨。"),
                    new NpcLine(BaseScriptNames.Scout,"chenan","大人，营地到寨子、寨子到村，各走一天。攻下寨子、清点完战果，就能返乡。")
                },new[]{new NpcReply("查看路线并选择出征队",owner.PrepareDemoJourney,!BaseBattleLoot.Pending(owner.State)),new NpcReply("先留营整备",ClosePanel)});return true;
            }
            if(id=="demo_camp_attack")
            {
                if(BaseDemoFinale.Later(owner.State)){OpenDemoStory();return true;}
                OpenBattlePreparation(BasePrepRoute.BlackCrow);return true;
            }
            if(id==BaseDemoFinale.CampSuppliesMarker){owner.CollectDemoCampSupplies();return true;}
            if(owner.State.Campaign.CurrentMapId==BaseDemoFinale.Home&&BaseDemoFinale.IsReturnField(id))
            {
                activeObject=id;
                NpcDialogue(new[]{new NpcLine("你","","阿禾那天还催着下种。田埂上怎么只剩马蹄印了。",true)},
                    Array.Empty<NpcReply>(),onCompleted:ClosePanel);
                return true;
            }
            if(id!="demo_broken_sword"&&id!="demo_survivor")return false;
            if(owner.State.Demo.Stage==BaseDemoStage.Finished){OpenDemoStory();return true;}
            if(BaseDemoFinale.Rebuilding(owner.State)){OpenRebuild();return true;}
            interactionObject="";
            if(id=="demo_survivor")
            {
                activeObject=id;
                NpcDialogue(new[]{new NpcLine("幸存村民",BaseScriptNames.Portrait("survivor"),
                    StoryText.Get("story.scene.home_rebuild.survivor","去西桥头看看吧，那天动静全在那边。"))},
                    Array.Empty<NpcReply>(),onCompleted:ClosePanel);
                return true;
            }
            activeObject="demo-sword";
            NpcDialogue(new[]{
                new NpcLine("旁白","","断桥旁散着乱箭，一截断剑斜插在泥地里。"),
                new NpcLine("你","","拨开泥，还是那个青绳结。这是搭档的剑。",true),
                new NpcLine("你","","这断口像是秘银重剑斩的。地上没见着血泊，车辙往西进了山。",true),
                new NpcLine("你","","搭档会不会在车上？沿着车辙进西山，也许能找到线索。",true)
            },new[]{new NpcReply("记录断剑 · 开始重建",owner.InvestigateDemoSword),new NpcReply("稍后调查",ClosePanel)});return true;
        }
    }
}

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
        {
            if(Change(next=>{if(!BaseDemoFinale.Investigate(next))throw new InvalidOperationException("先处理攻营战果，返乡到断剑旁同层一格内调查");},"已记录断剑，开始返乡重建"))Hud.OpenRebuild();
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
            StopConstruction();FrameRebuiltVillage();Hud.OpenDemoStory();return true;
        }
        private void ReloadTravelRegion()
        {if(Application.isPlaying)SceneManager.LoadScene(BaseBattleGateway.ExplorationScene);else{RefreshDemoRegion();Hud.ClosePanel();}}
    }
    public sealed partial class BaseHud
    {
        private RectTransform rebuildPalette,rebuildOverview;
        private readonly Button[] rebuildProjects=new Button[3];
        private Color[] rebuildProjectColors;
        private Button rebuildPlace,rebuildWithdraw,rebuildConfirm,rebuildResume;
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
            MakeButton(rebuildOverview,"结束 Demo",new Vector2(73,0),new Vector2(134,42),()=>owner.FinishHomeRebuild());
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
                NpcDialogue(new[]{new NpcLine(BaseScriptNames.Scout,"chenan","缴获记录确认了敌营位置，就在营地和你的"+BaseScriptNames.Village+"之间。无论是否招降俘虏，我们都掌握了这条线索。"),new NpcLine(BaseScriptNames.Scout,"chenan","先整备损失，再带队沿路出发吧。两段道路各走一天；攻下敌营并处理战果后，就能通往"+BaseScriptNames.Village+"。")},new[]{new NpcReply("查看路线并选择出征队",owner.PrepareDemoJourney,!BaseBattleLoot.Pending(owner.State)),new NpcReply("先留营整备",ClosePanel)});return true;
            }
            if(id=="demo_camp_attack")
            {
                if(BaseDemoFinale.Later(owner.State)){OpenDemoStory();return true;}
                OpenBattlePreparation(BasePrepRoute.BlackCrow);return true;
            }
            if(id!="demo_broken_sword"&&id!="demo_survivor")return false;
            if(owner.State.Demo.Stage==BaseDemoStage.Finished){OpenDemoStory();return true;}
            if(BaseDemoFinale.Rebuilding(owner.State)){OpenRebuild();return true;}
            var lines=new List<NpcLine>();
            var s=owner.State;
            if(BaseDemoFlow.Active(s)&&!s.Demo.Flow.HomeEnterSeen)
            {
                owner.Change(next=>next.Demo.Flow.HomeEnterSeen=true,"");
                lines.Add(new NpcLine("你","","[环顾四周焦黑的残垣] 离开时还是炊烟袅袅，如今只剩断壁残垣了。",true));
                if(BaseAdventure.Following(s,"partner_farm"))lines.Add(new NpcLine("阿禾","ahe","［眼圈泛红］我家的老屋和麦场全塌了……但只要人还在，总有一天能重建起来。"));
                if(BaseAdventure.Following(s,"partner_craft"))lines.Add(new NpcLine("阿铸","azhu","村口铁匠炉都被砸穿了。走，去西桥头看看，你同伴一定留下了线索！"));
                if(s.Leaders.Any(l=>BaseAdventure.Following(s,l.Id)&&l.IsAlien))
                    lines.Add(new NpcLine(BaseScriptNames.Galan,BaseScriptNames.Portrait("galan"),"这片焦土上还残留着织魂术的气息……下手的不是寻常部族。"));
            }
            interactionObject="";
            if(id=="demo_survivor")
            {
                activeObject=id;
                lines.Add(new NpcLine("幸存村民",BaseScriptNames.Portrait("survivor"),StoryText.Get("story.scene.home_rebuild.survivor","去西桥头看看。")));
                NpcDialogue(lines.ToArray(),Array.Empty<NpcReply>(),onCompleted:ClosePanel);
                return true;
            }
            activeObject="demo-sword";
            lines.AddRange(new[]{
                new NpcLine("旁白","","在烧焦的桥桩与乱箭之间，半截断裂的剑刃正斜插在冻土里。"),
                new NpcLine("你","","［指尖微颤］剑柄上这道被火燎焦的禁卫青绳结……是{Partner}的佩剑！",true),
                new NpcLine("你","","断口平滑如镜，是王室秘银重剑斩断的。地上没有血泊，只有向西进山的囚车辙印！",true),
                new NpcLine("你","","［将断剑收入怀中］还活着……不管幕后黑手是古裔还是王都，我定会把你救回来！",true)
            });
            NpcDialogue(lines.ToArray(),new[]{new NpcReply("记录断剑 · 开始重建",owner.InvestigateDemoSword),new NpcReply("稍后调查",ClosePanel)});return true;
        }
    }
}

using BaseConquest.Presentation;
using System;
using System.Linq;
using BaseConquest.Base;
using BaseConquest.Story;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
namespace BaseConquest.Battle
{
    public sealed partial class BattleSceneController
    {
        private GameObject campaignMenu;
        private Font saveMenuFont;
        private enum CampaignMenuPage { Main, Options, Audio, Help, ConfirmMainMenu, ConfirmQuit, ConfirmDefenseRetreat }
        internal bool UsesCampaignSystemMenu => DeploymentDraftActive || IsCampaignBattle && !IsStandaloneTest;
        internal bool CampaignMenuOpen => campaignMenu != null && !battleBagOpen;
        public void OpenCampaignSaves(bool loading=false)
        {
            if(IsRealtimeCampaign)loading=true;
            battleBagOpen=false;autoRun=false;if(Application.isPlaying)PersistCampaignCheckpoint(true);
            if(campaignMenu!=null)UiPrefabBinding.DestroyView(campaignMenu);
            if(saveMenuFont==null)saveMenuFont=Font.CreateDynamicFontFromOSFont(new[] {"Microsoft YaHei","Arial"},18);
            campaignMenu=UiPrefabBinding.Create("BattleSaves","战役存读档",transform,true);campaignMenu.GetComponent<Canvas>().renderMode=RenderMode.ScreenSpaceOverlay;campaignMenu.GetComponent<Canvas>().sortingOrder=3000;
            var scaler=campaignMenu.GetComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;scaler.referenceResolution=new Vector2(1280,720);
            RectTransform Box(string name,float y,float height){var obj=UiPrefabBinding.Rect(name,campaignMenu.transform).gameObject;var r=obj.GetComponent<RectTransform>();obj.GetOrAdd<Image>();r.anchorMin=r.anchorMax=new Vector2(.5f,.5f);r.sizeDelta=new Vector2(900,height);r.anchoredPosition=new Vector2(0,y);obj.GetComponent<Image>().color=new Color(.08f,.14f,.18f,.98f);return r;}
            void Button(string text,float x,float y,float width,Action click,bool enabled=true){var r=Box(text,y,43);r.sizeDelta=new Vector2(width,43);r.anchoredPosition=new Vector2(x,y);var b=r.gameObject.GetOrAdd<Button>();b.interactable=enabled;b.onClick.AddListener(()=>click());var t=UiPrefabBinding.Rect("Label",r).gameObject;t.GetOrAdd<Text>();var tr=t.GetComponent<RectTransform>();tr.anchorMin=Vector2.zero;tr.anchorMax=Vector2.one;tr.offsetMin=tr.offsetMax=Vector2.zero;var label=t.GetComponent<Text>();label.font=saveMenuFont;label.fontSize=18;label.color=Color.white;label.alignment=TextAnchor.MiddleCenter;label.text=text;label.raycastTarget=false;}
            Box("遮罩",0,700);
            Button(IsRealtimeCampaign?"战中不存档":loading ? "切换到保存" : "切换到读档",-280,300,250,()=>OpenCampaignSaves(!loading),!IsRealtimeCampaign&&!CampaignGameOver);
            Button("关闭",0,300,250,CloseCampaignMenu);
            Button("回主菜单",280,300,250,CampaignMainMenu);
            var catalog=Application.isPlaying ? BaseBattleGateway.Catalog() : null;var entries=catalog?.Entries()??new System.Collections.Generic.List<BaseSaveEntry>();int row=0;
            foreach(string key in loading ? BaseSaveCatalog.Slots.Concat(BaseSaveCatalog.AutoSlots) : BaseSaveCatalog.Slots)
            {
                string id=key;var entry=entries.FirstOrDefault(e=>e.Id==id);string name=(BaseSaveCatalog.AutoSlots.Contains(id)?"自动 ":"手动 ")+id+" · "+(entry==null ? "空" : entry.Error!=null ? "损坏" : "第"+entry.Day+"天 · "+entry.Name);float y=230-row++*52;
                Button(name,-165,y,550,()=>{},false);
                bool recover = loading && entry?.CanRecover == true;
                Button(loading ? (recover ? "恢复上次存档" : "读取") : "保存",215,y,160,()=>{
                    try {
                        if(loading){BaseBattleGateway.Session().LoadPlayableSlot(id,recover);BaseBattleGateway.MainMenu=false;BaseBattleGateway.RequestedId=null;campaignBattleId="";SceneLoadingCover.LoadExploration();}
                        else { if(entry!=null){Button("确认覆盖此槽",0,-300,460,()=>{catalog.SaveSlot(id,BaseBattleGateway.Store().Load(),"战斗手动存档"+id);OpenCampaignSaves();});return;}catalog.SaveSlot(id,BaseBattleGateway.Store().Load(),"战斗手动存档"+id);OpenCampaignSaves();}
                    }catch(Exception e){feedback="存读档失败，原状态保留："+e.Message;Button(e.Message,0,-300,780,()=>OpenCampaignSaves(loading));}
                },loading ? recover || entry?.CanLoad == true : !CampaignGameOver);
            }
            Button("选项",-280,-260,240,OpenCampaignOptions,!CampaignGameOver);
            Button("背包 · B",150,-260,560,()=>OpenBattleBackpack(),!CampaignGameOver);
            if(!loading)Button("取消战场准备并返营",0,-190,500,ReturnCampaign,battle.Phase==BattlePhase.Deployment&&!CampaignGameOver);
            UiPrefabBinding.Complete(campaignMenu);
        }
        public void OpenCampaignMenu() => BuildCampaignMenu(CampaignMenuPage.Main);
        public void OpenCampaignOptions() => BuildCampaignMenu(CampaignMenuPage.Options);

        private void CloseCampaignMenu()
        {
            if (campaignMenu == null) return;
            campaignMenu.SetActive(false);
            UiPrefabBinding.DestroyView(campaignMenu);
            campaignMenu = null;
            lastSpaceInputFrame = Time.frameCount;
            hud?.Refresh();
        }

        private static string CampaignMenuText(string key, string fallback) => StoryText.Get("story.ui.battle.menu." + key, fallback);

        private void BuildCampaignMenu(CampaignMenuPage page, string exitFailure = null, int helpTopic = -1)
        {
            if (ExternalDialogue) return;
            ManualPause();
            skipping = false;
            CancelGesture();
            CloseContext();
            formationOpen = false;
            battleBagOpen = false;
            CloseCampaignMenu();
            if (saveMenuFont == null) saveMenuFont = Font.CreateDynamicFontFromOSFont(new[] { "Microsoft YaHei", "Arial" }, 18);
            campaignMenu = UiPrefabBinding.Create("BattleOptions", "战斗选项", transform, true);
            var canvas = campaignMenu.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 3000;
            var scaler = campaignMenu.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280, 720);

            var shade = UiPrefabBinding.Rect("Backdrop", campaignMenu.transform);
            shade.SetAsFirstSibling();
            shade.anchorMin = Vector2.zero;
            shade.anchorMax = Vector2.one;
            shade.offsetMin = shade.offsetMax = Vector2.zero;
            shade.gameObject.GetOrAdd<Image>().color = DemoUiTheme.Current.Shade;
            var panel = UiPrefabBinding.Rect("选项面板", campaignMenu.transform);
            panel.anchorMin = panel.anchorMax = new Vector2(.5f, .5f);
            panel.pivot = new Vector2(.5f, .5f);
            panel.anchoredPosition = Vector2.zero;
            bool compact = UsesCampaignSystemMenu;
            bool confirmation = page == CampaignMenuPage.ConfirmMainMenu || page == CampaignMenuPage.ConfirmQuit;
            bool demoBattle = BaseBattleGateway.DemoMode && !IsStandaloneTest;
            bool canWithdraw = !DeploymentDraftActive && realtime != null && battle?.Phase == BattlePhase.Fighting &&
                (realtime.HasBattlefieldExits && !realtime.RuinsExitOpen && !realtime.VictoryExitOpen ||
                 !demoBattle && !IsAzhuDuel && !IsDemoPractice && !IsDemoPursuit && !IsSecondDefense && !IsStandaloneTest);
            bool demo = demoBattle;
            float height = page == CampaignMenuPage.Audio ? 272 : page == CampaignMenuPage.Options ? 370 :
                page == CampaignMenuPage.Help ? (helpTopic < 0 ? 536 : 460) :
                compact ? (confirmation ? 334 : canWithdraw ? 432 : 368) : 480;
            panel.sizeDelta = new Vector2(720, height);
            panel.gameObject.GetOrAdd<Image>();
            DemoUiTheme.Current.Frame(panel);

            void Label(RectTransform parent, string title, int fontSize)
            {
                var rect = UiPrefabBinding.Rect("Label", parent);
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.offsetMin = new Vector2(10, 3);
                rect.offsetMax = new Vector2(-10, -3);
                var text = rect.gameObject.GetOrAdd<Text>();
                text.font = saveMenuFont;
                text.fontSize = fontSize;
                text.alignment = TextAnchor.MiddleCenter;
                text.color = DemoUiTheme.Current.Ink;
                text.text = title;
                text.raycastTarget = false;
            }

            void Option(string id, string title, float y, Action action, bool enabled = true)
            {
                var rect = UiPrefabBinding.Rect(id, panel);
                rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f);
                rect.anchoredPosition = new Vector2(0, y);
                rect.sizeDelta = new Vector2(620, 52);
                var button = rect.gameObject.GetOrAdd<Button>();
                button.targetGraphic = rect.gameObject.GetOrAdd<Image>();
                DemoUiTheme.Current.StyleButton(button);
                button.interactable = enabled;
                button.onClick.AddListener(() => action());
                Label(rect, title, 21);
            }

            string back = DeploymentDraftActive ? CampaignMenuText("return_deployment", "返回部署") : "返回战斗";
            if (page == CampaignMenuPage.Main)
            {
                float y = compact ? canWithdraw ? 155 : 124 : 181;
                Option("Continue", back, y, CloseCampaignMenu, !CampaignGameOver);
                Option("Options", "选项", y -= 62, OpenCampaignOptions, !CampaignGameOver);
                Option("Help", "帮助", y -= 62, () => BuildCampaignMenu(CampaignMenuPage.Help));
                Option("MainMenu", "回主菜单", y -= 62, () =>
                {
                    if (demo) BaseBattleGateway.ExitDemoSession(false);
                    else if (UsesCampaignSystemMenu) BuildCampaignMenu(CampaignMenuPage.ConfirmMainMenu);
                    else CampaignMainMenu();
                });
                if (UsesCampaignSystemMenu || demo)
                    Option("Quit", CampaignMenuText("quit", "退出游戏"), y -= 62, () =>
                    {if(demo)BaseBattleGateway.ExitDemoSession(true);else BuildCampaignMenu(CampaignMenuPage.ConfirmQuit);});
                if (canWithdraw)
                    Option("RealtimeRetreat", realtime.DefensePursuitReady ? "结束追击" : realtime.HasBattlefieldExits ? "去出口" : "撤退", y -= 62,
                        () => {CloseCampaignMenu();RealtimeRetreat();});
                if (IsStandaloneTest)
                {
                    Option("Restart", "重置本预设（不保存剧情）", y -= 62, () => RestartStandaloneTest?.Invoke());
                    Option("LeaveTest", "退出独立测试（不保存剧情）", y -= 62, () => AbandonRealtimeBattle());
                }
                else if (!canWithdraw && !demo && !UsesCampaignSystemMenu && IsRealtimeCampaign && !CampaignGameOver && !campaignSaved && !IsSecondDefense)
                    Option("Withdraw", realtime?.Session.Opening.DemoCombat == true ? "立即撤出，结算战损" : "放弃本场，返回战前", y -= 62, () => AbandonRealtimeBattle());
                else if (!UsesCampaignSystemMenu && battle?.Phase == BattlePhase.Deployment)
                    Option("CancelPreparation", "取消战场准备并返营", y -= 62, ReturnCampaign, CanReturnCampaign);
            }
            else if (page == CampaignMenuPage.Options)
            {
                float y = height * .5f - 61;
                Option("Back", "返回菜单", y, OpenCampaignMenu);
                Option("Audio", "声音设置", y -= 62, () => BuildCampaignMenu(CampaignMenuPage.Audio));
                Option("BattleAssist", "战斗辅助显示：" + (MapDisplayOptions.BattleAssist ? "开启" : "关闭"), y -= 62, () =>
                {
                    MapDisplayOptions.BattleAssist = !MapDisplayOptions.BattleAssist;
                    boardView.SetGridOutlines(MapDisplayOptions.BattleAssist);
                    SyncViews();
                    OpenCampaignOptions();
                });
                if (RuntimeDeveloperOptions.Available)
                    Option("Developer", "开发者模式：" + (RuntimeDeveloperOptions.Enabled ? "开启" : "关闭"), y -= 62, () =>
                    {
                        RuntimeDeveloperOptions.Enabled = !RuntimeDeveloperOptions.Enabled;
                        OpenCampaignOptions();
                    });
                Option("Continue", back, y - 62, CloseCampaignMenu);
            }
            else if (page == CampaignMenuPage.Help)
            {
                if (helpTopic < 0)
                {
                    float y = height * .5f - 94;
                    for (int i = 0; i < BaseExperienceRules.Help.Count; i++)
                    {
                        int index = i;
                        Option("HelpTopic" + i, BaseExperienceRules.Help[i].Title, y,
                            () => BuildCampaignMenu(CampaignMenuPage.Help, helpTopic: index));
                        y -= 62;
                    }
                    Option("Back", "返回菜单", -height * .5f + 42, OpenCampaignMenu);
                }
                else
                {
                    var topic = BaseExperienceRules.Help[helpTopic];
                    var content = UiPrefabBinding.Rect("HelpContent", panel);
                    content.anchorMin = Vector2.zero;
                    content.anchorMax = Vector2.one;
                    content.offsetMin = new Vector2(40, 90);
                    content.offsetMax = new Vector2(-40, -76);
                    Label(content, topic.Text, 19);
                    content.GetComponentInChildren<Text>().alignment = TextAnchor.UpperLeft;
                    Option("Back", "返回帮助", -height * .5f + 42, () => BuildCampaignMenu(CampaignMenuPage.Help));
                }
                var heading = UiPrefabBinding.Rect("HelpHeading", panel);
                heading.anchorMin = new Vector2(0, 1);
                heading.anchorMax = Vector2.one;
                heading.offsetMin = new Vector2(40, -61);
                heading.offsetMax = new Vector2(-40, -14);
                Label(heading, helpTopic < 0 ? "帮助" : BaseExperienceRules.Help[helpTopic].Title, 25);
            }
            else if (page == CampaignMenuPage.ConfirmDefenseRetreat)
            {
                var message=UiPrefabBinding.Rect("WithdrawalEstimate",panel);
                message.anchorMin=message.anchorMax=new Vector2(.5f,.5f);
                message.anchoredPosition=new Vector2(0,84);
                message.sizeDelta=new Vector2(650,88);
                Label(message,"预计撤退损失\n"+FirstDefenseSettlement(true),20);
                Option("Withdraw","确认撤退",0,()=>{CloseCampaignMenu();CommitRealtimeRetreat();});
                Option("Cancel","留在战场",-62,CloseCampaignMenu);
            }
            else if (page == CampaignMenuPage.Audio)
                Option("Back", "返回选项", 96, OpenCampaignOptions);
            else
            {
                bool quit = page == CampaignMenuPage.ConfirmQuit;
                var message = UiPrefabBinding.Rect("ExitMessage", panel);
                message.anchorMin = message.anchorMax = new Vector2(.5f, .5f);
                message.anchoredPosition = new Vector2(0, 95);
                message.sizeDelta = new Vector2(620, 124);
                Label(message, exitFailure ?? (DeploymentDraftActive
                    ? CampaignMenuText("exit_draft", "当前部署将被放弃。")
                    : CampaignMenuText("exit_battle", "按当前战斗规则结算后离开。")), 20);
                Option("Confirm", quit ? CampaignMenuText("confirm_quit", "确认退出游戏") : CampaignMenuText("confirm_title", "确认回主菜单"), -14, () => ConfirmCampaignExit(page));
                Option("Cancel", "取消", -76, OpenCampaignMenu);
            }
            UiPrefabBinding.Complete(campaignMenu);
            if (page == CampaignMenuPage.Audio)
                GameAudioVolume.AddControls(panel, saveMenuFont, new Vector2(.5f, .5f), new Vector2(0, -12), 620);
            hud?.Refresh();
        }

        private void ConfirmCampaignExit(CampaignMenuPage page)
        {
            bool quit = page == CampaignMenuPage.ConfirmQuit;
            if(BaseBattleGateway.DemoMode&&!IsStandaloneTest){BaseBattleGateway.ExitDemoSession(quit);return;}
            if (DeploymentDraftActive)
            {
                var scene = GetComponentInParent<BaseSceneController>();
                if (scene == null || deploymentExit == null) return;
                deploymentExit.Invoke();
                if (!quit) scene.ToMainMenu();
            }
            else if (IsSceneEncounter || IsRealtimeCampaign)
            {
                if (!LeaveRealtimeBattle(true, !quit)) { BuildCampaignMenu(page, feedback); return; }
            }
            else
            {
                if (!PersistCampaignCheckpoint(true)) { BuildCampaignMenu(page, feedback); return; }
                if (!quit) LoadCampaignMainMenu();
            }
            if (quit) Application.Quit();
        }
    }
}

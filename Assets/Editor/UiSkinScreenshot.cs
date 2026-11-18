using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BaseConquest.Base;
using BaseConquest.Battle;
using BaseConquest.Presentation;
using BaseConquest.RealtimePrototype;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UI;
using Object = UnityEngine.Object;
using TouchPhase = UnityEngine.InputSystem.TouchPhase;

namespace BaseConquest.Editor
{
    // P8（W8）C4～C6 离屏截图验收：
    //   Unity.exe -batchmode -projectPath RPGDemo -executeMethod BaseConquest.Editor.UiSkinScreenshot.Run
    // 不加 -nographics、不加 -quit。使用本次检查专用进度验证正式 Demo 流程。
    // 产物：Logs/wood-ui/。
    // RunPlayerRouteViews 通过连续路线的存档补拍首防后对白、世界地图与返乡重建。
    // 截图沿用 UiInventoryCapture 的双遍合成：世界相机渲一张，屏幕空间 Overlay 的 Canvas 再单独渲到透明底按 alpha 叠回。
    public static class UiSkinScreenshot
    {
        private static readonly string OutDir = Path.Combine("Logs", "wood-ui");
        private static double deadline, stageAt, nextLog;
        private static int frameDeadline;
        private static string stage = "boot";
        private static BaseSceneController controller;
        private static BattleSceneController defenseBattle;
        private static GameObject defenseHud;
        private static Camera defenseCamera;
        private static Rect defenseViewport;
        private static int defenseSupplyStock;
        private static Camera ruinsEntranceCamera;
        private static string ruinsEntranceMap;
        private static bool failed, androidTouch, homeCopyOnly;
        private static bool campSceneryOnly;
        private static BaseDayPreview civilianDayPreview;
        private static BaseWorkerJourney civilianCrossing;
        private static Transform civilianCrossingView;
        private static readonly List<string> Log = new List<string>();

        private static BaseSceneController C => Object.FindAnyObjectByType<BaseSceneController>();
        private static BaseHud H => C?.Hud;

        public static void Run()
        {
            Directory.CreateDirectory(OutDir);
            if (!Application.isBatchMode) throw new InvalidOperationException("界面检查须在独立后台编辑器运行");
            Environment.SetEnvironmentVariable("BASE_CONQUEST_UI_CHECK_SAVES",
                Path.GetFullPath(Path.Combine(OutDir, "check-saves", Guid.NewGuid().ToString("N"))));
            UnityEditor.SceneManagement.EditorSceneManager.OpenScene(DemoStartup.Launcher);
            deadline = EditorApplication.timeSinceStartup + 480;
            EditorApplication.update += Tick;
            EditorApplication.EnterPlaymode();
        }

        public static void RunPlayerRouteViews()
        {
            stage = "route-boot";
            Run();
        }

        public static void RunAndroidTouch()
        {
            androidTouch = true;
            Run();
        }

        public static void RunOpeningCopy()
        {
            stage = "opening-copy";
            Run();
        }

        public static void RunFullCampInvitation()
        {
            stage = "full-camp-boot";
            Run();
        }

        public static void RunHomeCopy()
        {
            homeCopyOnly = true;
            stage = "route-boot";
            Run();
        }

        public static void RunCivilianWork()
        {
            CivilianStage("civilian-boot");
            Run();
        }

        public static void RunCampScenery()
        {
            campSceneryOnly = true;
            Run();
        }

        private static void Tick()
        {
            try { TickStage(); }
            catch (Exception error) { Finish(2, stage + "：" + error); }
        }

        private static void TickStage()
        {
            if (failed || EditorApplication.timeSinceStartup > deadline) { Finish(2, "超时或前置失败：" + stage); return; }
            if (!EditorApplication.isPlaying) { Note("等待进入 playmode"); return; }
            if (stage.StartsWith("civilian-", StringComparison.Ordinal)) { TickCivilianWork(); return; }
            switch (stage)
            {
                case "opening-copy":
                    controller = C;
                    if (controller?.State == null || controller.Busy || Object.FindAnyObjectByType<SceneLoadingCover>() != null) return;
                    VerifyOpeningCopy();
                    VerifyRuinsConfrontationCopy();
                    Finish(0, "OK");
                    break;
                case "full-camp-boot":
                    controller = C;
                    if (controller?.State == null || controller.Busy || Object.FindAnyObjectByType<SceneLoadingCover>() != null) return;
                    LoadRouteView("full-camp-before-ahe");
                    break;
                case "route-full-camp-before-ahe":
                    if (!RouteViewReady(state => state.Campaign.Location == "base" && !BaseRpg.HasRoom(state) &&
                        state.Adventure.Members.Count == BaseAdventure.Capacity)) return;
                    VerifyFullCampInvitation();
                    Finish(0, "OK");
                    break;
                case "route-boot":
                    controller = C;
                    if (controller?.State == null || controller.Busy || Object.FindAnyObjectByType<SceneLoadingCover>() != null) return;
                    LoadRouteView(homeCopyOnly ? "home-arrival" : "drill");
                    break;
                case "route-drill":
                    if (!RouteViewReady(state => BaseDemoPrologue.Village(state) && state.Demo.Intro.Step == 4)) return;
                    VerifyOpeningDrillControls();
                    LoadRouteView("first-intel");
                    break;
                case "route-first-intel":
                    if (!RouteViewReady(state => state.Campaign.Location == "town" && BaseRaidIntel.CanClaimFirst(state))) return;
                    VerifyFirstIntelReopened();
                    LoadRouteView("merchant");
                    break;
                case "route-merchant":
                    if (!RouteViewReady(state => state.Campaign.Location == "base" && BaseDemoDefense.Won(state))) return;
                    H.ClosePanel();
                    H.OpenDemoMerchant();
                    var merchantLine = RequirePanel("NpcDialogue").Find("DialogueText").GetComponent<Text>();
                    if (!H.ContinueDialogue() || !H.ContinueDialogue() || !H.ContinueDialogue() ||
                        !merchantLine.text.Contains("巴图") || !merchantLine.text.Contains("黑鸦寨") ||
                        merchantLine.text.Contains("待写"))
                        throw new InvalidOperationException("首防后老莫对白未播放到第二句");
                    CaptureViewports("ui-player-route-merchant-after-defense");
                    Log.Add("[PASS] 首防后老莫在对白交代巴图、黑鸦寨与出征线索");
                    H.ClosePanel();
                    LoadRouteView("second-occupation");
                    break;
                case "route-second-occupation":
                    if (!RouteViewReady(state => BaseDemoFinale.SecondRaidReportPending(state) && H.DialogueOpen)) return;
                    var report = RequirePanel("NpcDialogue");
                    if (!report.Find("Replies").GetComponentsInChildren<Button>().Any(button => button.interactable))
                        H.ContinueDialogue();
                    var reportLine = report.Find("DialogueText").GetComponent<Text>().text;
                    if (report.Find("SpeakerName").GetComponent<Text>().text != "陈安" ||
                        !reportLine.Contains("残兵都跑了") || !reportLine.Contains("白桦村") || !reportLine.Contains("剩下的物资"))
                        throw new InvalidOperationException("第二波胜利存档未接陈安的占领报告");
                    CaptureViewports("ui-player-route-second-occupation");
                    Click("知道了");
                    if (BaseDemoFinale.SecondRaidReportPending(controller.State))
                        throw new InvalidOperationException("陈安报信确认后没有记录进度");
                    Log.Add("[PASS] 第二波胜利后载入可看陈安报信，确认后保存");
                    LoadRouteView("camp-remnants");
                    break;
                case "route-camp-remnants":
                    if (!RouteViewReady(state => state.Campaign.CurrentMapId == BaseDemoFinale.Camp)) return;
                    Approach(BaseDemoFinale.CampSuppliesMarker);
                    H.OpenObject(BaseDemoFinale.CampSuppliesMarker);
                    H.ContinueDialogue();
                    if (!controller.State.Demo.Finale.CampSuppliesTaken ||
                        !RequirePanel("NpcDialogue").Find("DialogueText").GetComponent<Text>().text.Contains("剩余的物资"))
                        throw new InvalidOperationException("已占领黑鸦寨的搜查入口未领取或显示反馈");
                    CaptureViewports("ui-player-route-camp-remnants");
                    Log.Add("[PASS] 已占领黑鸦寨的地标可搜查一次并显示领取反馈");
                    LoadRouteView("world");
                    break;
                case "route-world":
                    if (!RouteViewReady(BaseWorldJourney.OnWorld)) return;
                    // The continuous rules run grants rewards but does not dismiss their receipts.
                    if (H.PanelOpen && (string)Field(H, "panelViewKey") == "reward-receipt")
                    {
                        H.ClosePanel();
                        frameDeadline = Time.frameCount + 2;
                        return;
                    }
                    var journey = Object.FindAnyObjectByType<BaseWorldJourneyHud>();
                    var map = journey?.transform.Find("游戏显示区域 · 16比9/WorldMap");
                    if (map == null || !map.gameObject.activeInHierarchy)
                    {
                        if (EditorApplication.timeSinceStartup - stageAt > 30)
                            throw new InvalidOperationException("存档已进入世界，但行军地图仍未显示");
                        return;
                    }
                    var journeyDetails = map.parent.Find("JourneyPanel");
                    if (map.Find("Node_bandit").GetComponentInChildren<Text>().text != "黑鸦寨" ||
                        map.Find("Node_home").GetComponentInChildren<Text>().text != "白桦村")
                        throw new InvalidOperationException("世界地图节点未显示可区分的短名称");
                    if (!BaseWorldJourney.Marching(controller.State))
                    {
                        ClickHudButton(map.Find("Node_home"));
                        if (!journeyDetails.GetComponentsInChildren<Text>().Any(label => label.text.Contains("被黑鸦寨封锁")))
                            throw new InvalidOperationException("未攻寨时未显示返乡路的阻断原因");
                        ClickHudButton(map.Find("Node_bandit"));
                    }
                    stage = "route-world-shot"; frameDeadline = Time.frameCount + 20;
                    break;
                case "route-world-shot":
                    if (!RouteViewReady(BaseWorldJourney.OnWorld)) return;
                    var journeyPanel = Object.FindAnyObjectByType<BaseWorldJourneyHud>()?.transform.Find("游戏显示区域 · 16比9/JourneyPanel");
                    string marchLabel = BaseWorldJourney.Marching(controller.State) ? "继续行军" : "确认行军";
                    var confirm = journeyPanel?.GetComponentsInChildren<Button>().SingleOrDefault(button =>
                        button.interactable && button.GetComponentInChildren<Text>()?.text == marchLabel);
                    if (confirm == null)
                        throw new InvalidOperationException("选择黑鸦寨后未出现可用的行军确认按钮");
                    var actions = (RectTransform)confirm.transform.parent;
                    var frame = (RectTransform)journeyPanel.parent;
                    if (actions.anchorMin.y != 0 || actions.anchorMax.y != 0 ||
                        !RectWithin((RectTransform)confirm.transform, frame))
                        throw new InvalidOperationException("行军确认按钮未固定在游戏画面内的右栏底部");
                    CaptureViewports("ui-player-route-world");
                    Log.Add("[PASS] 连续路线存档载入世界地图，显示路线与行军面板");
                    LoadRouteView("home-arrival");
                    break;
                case "route-home-arrival":
                    if (!RouteViewReady(state => state.Campaign.CurrentMapId == BaseDemoFinale.Home && !BaseDemoFinale.Rebuilding(state))) return;
                    if (H.PanelOpen && (string)Field(H, "panelViewKey") == "reward-receipt")
                    {
                        H.ClosePanel();
                        frameDeadline = Time.frameCount + 2;
                        return;
                    }
                    CheckHomeArrival();
                    if (homeCopyOnly) Finish(0, "OK");
                    else LoadRouteView("rebuild");
                    break;
                case "route-rebuild":
                    if (!RouteViewReady(state => state.Campaign.CurrentMapId == BaseDemoFinale.Home && BaseDemoFinale.Rebuilding(state))) return;
                    CheckHomeRebuild();
                    Finish(0, "OK");
                    break;
                case "boot":
                    controller = C;
                    if (controller?.Message?.StartsWith("开局失败", StringComparison.Ordinal) == true)
                        throw new InvalidOperationException(controller.Message);
                    if (controller?.State == null || Object.FindAnyObjectByType<SceneLoadingCover>() != null)
                    { Note("等待序章进度"); return; }
                    if (!BaseDemoPrologue.Village(controller.State)) throw new InvalidOperationException("新档未进入白桦村");
                    if (campSceneryOnly)
                    {
                        if (Object.FindObjectsByType<Transform>().Any(t => t.name == "CampScenery"))
                            throw new InvalidOperationException("灰岩营地布景出现在白桦村");
                    }
                    else
                    {
                        CaptureViewports("ui-skin-prologue");
                        VerifyOpeningCopy();
                    }
                    controller.LoadDemoStage("camp", "female");
                    stage = "loading"; stageAt = EditorApplication.timeSinceStartup;
                    break;
                case "loading":
                    controller = C;
                    if (controller?.State == null) { if (EditorApplication.timeSinceStartup - stageAt > 150) Finish(2, "阶段档载入超时"); return; }
                    if (controller.State.Campaign.Location != "base") return;
                    H?.ClosePanel();
                    stage = campSceneryOnly ? "camp-scenery-shot" : "wake";
                    frameDeadline = Time.frameCount + (campSceneryOnly ? 45 : 5);
                    break;
                case "camp-scenery-shot":
                    if (Time.frameCount < frameDeadline) return;
                    CheckCampScenerySpawnAndEdge();
                    stage = "camp-scenery-edge"; frameDeadline = Time.frameCount + 4;
                    break;
                case "camp-scenery-edge":
                    if (Time.frameCount < frameDeadline) return;
                    Capture("exploration-camp-edge");
                    CheckCampSceneryFullView();
                    stage = "camp-scenery-wide"; frameDeadline = Time.frameCount + 4;
                    break;
                case "camp-scenery-wide":
                    if (Time.frameCount < frameDeadline) return;
                    Capture("exploration-camp-wide");
                    CheckCampNames();
                    CheckSceneryOcclusion();
                    H.ClosePanel();
                    if (!controller.Change(s => BaseDemo.AcknowledgeWaking(s), "镜头验收"))
                        throw new InvalidOperationException(controller.Message);
                    Approach("exit_east");
                    controller.TravelDemo("town");
                    stage = "exploration-town-edge"; frameDeadline = Time.frameCount + 12;
                    break;
                case "exploration-town-edge":
                    if (Time.frameCount < frameDeadline) return;
                    CheckTownFraming();
                    Finish(failed ? 2 : 0, failed ? "截图失败" : "OK");
                    break;
                case "wake":
                    // camp 阶段档停在 Waking：Demo 里「先了解处境」之前不许出营地，所以先完成醒来目标。
                    if (Time.frameCount < frameDeadline) return;
                    if (controller.State.Demo.Stage == BaseDemoStage.Waking)
                    {
                        if (!controller.Change(s =>
                        {
                            if (s.Demo.Stage != BaseDemoStage.Waking) return;
                            var home = BaseSpatial.Objects(s).Single(o => o.Id == "home");
                            // 营帐是带地基的设施，直接写中心点会落进不可通行格；走门前格。
                            var spot = home.Blocks ? BaseStageWalk.Door(s, home.Id) : new BasePoint(home.X, home.Y);
                            s.PlayerX = spot.X; s.PlayerY = spot.Y; BaseAdventure.Arrive(s);
                            if (!BaseDemo.AcknowledgeWaking(s)) throw new InvalidOperationException("醒来目标未完成");
                        }, "截图：完成醒来")) { Finish(2, "醒来失败：" + controller.Message); return; }
                    }
                    stage = "camp-shot"; frameDeadline = Time.frameCount + 45;
                    break;
                case "camp-shot":
                    if (Time.frameCount < frameDeadline) return;
                    CaptureViewports("ui-skin-camp-hud");
                    H.OpenMenu();
                    stage = "camp-menu-shot"; frameDeadline = Time.frameCount + 20;
                    break;
                case "camp-menu-shot":
                    if (Time.frameCount < frameDeadline) return;
                    RequirePanel("CampMenu");
                    Capture("ui-skin-camp-menu");
                    VerifyResourceHud();
                    H.OpenLandMap();
                    stage = "landmap-shot"; frameDeadline = Time.frameCount + 20;
                    break;
                case "landmap-shot":
                    if (Time.frameCount < frameDeadline) return;
                    Capture("ui-skin-landmap");
                    H.OpenQuests();
                    stage = "quests-shot"; frameDeadline = Time.frameCount + 20;
                    break;
                case "quests-shot":
                    if (Time.frameCount < frameDeadline) return;
                    Capture("ui-skin-quests");
                    H.OpenQuests("achievement");
                    stage = "achievements-shot"; frameDeadline = Time.frameCount + 20;
                    break;
                case "achievements-shot":
                    if (Time.frameCount < frameDeadline) return;
                    Capture("ui-skin-achievements");
                    H.ClosePanel();
                    H.OpenBackpack();
                    stage = "backpack-shot"; frameDeadline = Time.frameCount + 20;
                    break;
                case "backpack-shot":
                    if (Time.frameCount < frameDeadline) return;
                    VerifyPartyCardLayout(1);
                    CaptureViewports("ui-skin-backpack");
                    VerifyAuthoredLayout();
                    // 选中一件物品，验证说明区已改为点选后才出现的小弹框。
                    var firstItem = BasePartyInventory.Entries(controller.State).FirstOrDefault(e => e != null && !string.IsNullOrEmpty(e.Holder) && !e.Worn);
                    if (firstItem != null)
                    {
                        var hudType = H.GetType();
                        hudType.GetField("inventorySelected", BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(H, firstItem.Id);
                        hudType.GetMethod("OpenInventory", BindingFlags.Instance | BindingFlags.NonPublic)?.Invoke(H, new object[] { false });
                        stage = "backpack-menu-shot"; frameDeadline = Time.frameCount + 20;
                        break;
                    }
                    OpenWarehouseShot();
                    break;
                case "backpack-menu-shot":
                    if (Time.frameCount < frameDeadline) return;
                    Capture("ui-skin-backpack-menu");
                    VerifyBackpackEquip();
                    OpenWarehouseShot();
                    break;
                case "warehouse-shot":
                    if (Time.frameCount < frameDeadline) return;
                    VerifyWarehouseUi();
                    H.ClosePanel();
                    BaseBattleGateway.MainMenu = true;
                    try { H.OpenMenu(); } finally { BaseBattleGateway.MainMenu = false; }
                    stage = "menu-shot"; frameDeadline = Time.frameCount + 40;
                    break;
                case "menu-shot":
                    if (Time.frameCount < frameDeadline) return;
                    Capture("ui-skin-mainmenu");
                    H.ClosePanel();
                    Approach("exit_east");
                    controller.TravelDemo("town");
                    if (controller.State.Campaign.Location != "town")
                        throw new InvalidOperationException("前往城镇被拒：" + controller.Message);
                    stage = "to-town"; frameDeadline = Time.frameCount + 90; stageAt = EditorApplication.timeSinceStartup;
                    break;
                case "to-town":
                    controller = C;
                    if (controller?.State == null) return;
                    if (controller.State.Campaign.Location != "town")
                    {
                        if (EditorApplication.timeSinceStartup - stageAt > 60) Finish(2, "未到达城镇");
                        return;
                    }
                    if (Time.frameCount < frameDeadline) return;
                    Approach("town_quest");
                    stage = "town-arrive"; frameDeadline = Time.frameCount + 30;
                    break;
                case "town-arrive":
                    if (Time.frameCount < frameDeadline) return;
                    var mapLabels = (RectTransform[])Field(H, "labels");
                    var authoredLabels = (BaseMapObject[])Field(H, "labelObjects");
                    int lordLabel = Array.FindIndex(authoredLabels, label => label.Id == "town_quest");
                    if (lordLabel < 0) throw new InvalidOperationException("缺少领主名牌：" + string.Join(",", authoredLabels.Select(label => label.Id)));
                    var lordLabelText = mapLabels[lordLabel].GetComponent<Text>();
                    if (lordLabelText.color.grayscale < .72f)
                        throw new InvalidOperationException("场景人物名牌被面板主题改成了深色：" + lordLabelText.color);
                    H.OpenObject("town_quest");
                    VerifySavedDialogueColor(RequirePanel("NpcDialogue"));
                    if (!H.DialogueExitLocked) throw new InvalidOperationException("首次领主对白没有锁定退出");
                    H.ClosePanel();
                    if (!H.DialogueOpen) throw new InvalidOperationException("中途关闭了首次领主对白");
                    FinishDialogue();
                    if (H.DialogueExitLocked) throw new InvalidOperationException("领主叙述完成后仍锁定退出");
                    stage = "dialogue-shot"; frameDeadline = Time.frameCount + 45;
                    break;
                case "dialogue-shot":
                    if (Time.frameCount < frameDeadline) return;
                    RequirePanel("NpcDialogue");
                    CaptureViewports("ui-skin-dialogue", mobile: true);
                    H.ClosePanel();
                    ApproachMerchantAwayFromStall();
                    H.OpenObject("town_peddler");
                    var merchantDialogue = RequirePanel("NpcDialogue");
                    VerifySavedDialogueColor(merchantDialogue);
                    var pendingReplies = merchantDialogue.Find("Replies").GetComponentsInChildren<Button>();
                    if (pendingReplies.Length != 3 || pendingReplies.Any(button =>
                        button.interactable || button.GetComponentInChildren<Text>().text != "..."))
                        throw new InvalidOperationException("行商对白未原位显示禁用的占位选项");
                    if (merchantDialogue.Find("AdvanceButton").GetComponentInChildren<Text>().text != "→")
                        throw new InvalidOperationException("对白继续按钮未使用右箭头");
                    Capture("ui-skin-dialogue-placeholders");
                    FinishDialogue();
                    if (pendingReplies.Any(button => !button.interactable || button.GetComponentInChildren<Text>().text == "..."))
                        throw new InvalidOperationException("行商对白播完后未原位开放实际选项");
                    Click(BaseConquest.Story.StoryText.Get("story.ui.trade.market_entry", "买卖东西"));
                    var merchantMarket = RequirePanel("TownTrade");
                    if (merchantMarket.Find("Tabs").gameObject.activeInHierarchy)
                        throw new InvalidOperationException("交易面板仍显示业务页签");
                    VerifyMerchantCommonGoods();
                    BuySelectedForgeStone();
                    stage = "trade-shot"; frameDeadline = Time.frameCount + 30;
                    break;
                case "trade-shot":
                    if (Time.frameCount < frameDeadline) return;
                    VerifyTradeCardLayout(RequirePanel("TownTrade"));
                    CaptureViewports("ui-skin-trade");
                    H.ClosePanel();
                    VerifyTownReforge();
                    VerifyAzhuDuelDialogue();
                    Approach("town_quest");
                    if (controller.State.Demo.AcceptedDay == 0)
                    {
                        H.OpenObject("town_quest");
                        H.ContinueDialogue();
                        if (!H.DialogueExitLocked || !RequirePanel("NpcDialogue").Find("DialogueText").GetComponent<Text>().text.Contains("拖住了追兵"))
                            throw new InvalidOperationException("领主委托第一页或退出锁定错误");
                        FinishDialogue();
                        if (!RequirePanel("NpcDialogue").Find("DialogueText").GetComponent<Text>().text.Contains("募兵处也会放行"))
                            throw new InvalidOperationException("领主委托漏播建设和募兵条件");
                        Click("接受" + BaseScriptNames.Ruins + "调查");
                        if (controller.State.Demo.AcceptedDay == 0)
                            throw new InvalidOperationException("对白未能接取古窟调查");
                    }
                    Approach("demo_ruins_exit");
                    controller.TravelDemo("outpost");
                    stage = "ruins-entrance"; frameDeadline = Time.frameCount + 15;
                    break;
                case "ruins-entrance":
                    if (Time.frameCount < frameDeadline) return;
                    VerifyRuinsEntrance();
                    ruinsEntranceCamera = controller.WorldCamera;
                    ruinsEntranceMap = controller.State.Campaign.CurrentMapId;
                    controller.Interact();
                    Press(RequirePanel("BattlePreparation"), "Continue");
                    stage = "ruins-deployment"; frameDeadline = Time.frameCount + 5;
                    break;
                case "ruins-deployment":
                    if (Time.frameCount < frameDeadline) return;
                    var ruinsPlacement = RequireBattleDraft();
                    if (controller.WorldCamera == ruinsEntranceCamera || controller.State.Campaign.CurrentMapId != ruinsEntranceMap || controller.State.Campaign.HasActiveBattle)
                        throw new InvalidOperationException("从遗迹入口预览地牢时写入了进度或未显示实际地牢");
                    VerifyDeploymentPointer(ruinsPlacement);
                    Capture("ui-skin-ruins-deployment");
                    Press(BattleSafeArea(ruinsPlacement), "DraftPreparationControls/Back");
                    if (controller.State.Campaign.CurrentMapId != ruinsEntranceMap || !BaseSceneRules.Entrance(controller.State) ||
                        Object.FindObjectsByType<BattleSceneController>().Any(b => b.DeploymentDraftActive) ||
                        !Object.FindObjectsByType<Renderer>().Any(r => r.name == "PixelDemo_dungeon-entrance"))
                        throw new InvalidOperationException("返回备战没有恢复遗迹入口");
                    Press(RequirePanel("BattlePreparation"), "Continue");
                    var cancelRuins = RequireBattleDraft();
                    var cancelDraft = (BaseBattlePreparation)Field(H, "battleDraft");
                    var cancelPositions = cancelDraft.Positions.ToDictionary(pair => pair.Key, pair => pair.Value);
                    Press(BattleSafeArea(cancelRuins), "DraftPreparationControls/Start");
                    FinishDialogue();
                    Click("追问他们为何不能动");
                    FinishDialogue();
                    Click("暂不交战");
                    if (RequireBattleDraft() != cancelRuins || H.PanelOpen ||
                        !cancelPositions.OrderBy(pair => pair.Key).SequenceEqual(cancelDraft.Positions.OrderBy(pair => pair.Key)))
                        throw new InvalidOperationException("古窟追问后取消没有保留战场和落点");
                    if (BaseAchievements.Record(controller.State, BaseAchievements.Caution) != null)
                        throw new InvalidOperationException("免疫队伍误触发了古窟危险警告");
                    Press(BattleSafeArea(cancelRuins), "DraftPreparationControls/Back");
                    H.ClosePanel();
                    if (!controller.Change(state => VerifyBanditHideout.AddSoldiers(state, 1, 0, 0), "界面检查"))
                        throw new InvalidOperationException(controller.Message);
                    VerifyRuinsWarning();
                    if (H.PanelOpen || controller.State.Campaign.CurrentMapId != ruinsEntranceMap ||
                        controller.State.Campaign.HasActiveBattle || Object.FindObjectsByType<BattleSceneController>().Any(b => b.DeploymentDraftActive))
                        throw new InvalidOperationException("退出部署后没有恢复遗迹入口");
                    Log.Add("[PASS] 古窟免疫队伍跳过警告、普通士兵弹出确认，追问后取消保留部署及落点，退出备战返回入口");
                    VerifyRuinsSoloRetreat();
                    VerifyHideoutUi();
                    controller.LoadDemoStage("defense", "female");
                    stage = "defense-ready"; stageAt = EditorApplication.timeSinceStartup;
                    break;
                case "defense-ready":
                    controller = C;
                    if (controller?.State == null || controller.State.Demo.Stage != BaseDemoStage.DefenseWarned
                        || Object.FindAnyObjectByType<SceneLoadingCover>() != null) return;
                    H.OpenBackpack();
                    if (RequirePanel("PartyInventory").Find("Cards").childCount != 3)
                        throw new InvalidOperationException("整备阶段背包应同屏显示三名队员");
                    stage = "defense-inventory-shot"; frameDeadline = Time.frameCount + 5;
                    break;
                case "defense-inventory-shot":
                    if (Time.frameCount < frameDeadline) return;
                    VerifyPartyCardLayout(3);
                    CaptureViewports("ui-skin-backpack-three");
                    VerifyTwoMemberBackpack();
                    VerifyBackpackTransfer();
                    VerifyFacilityRosters();
                    VerifyRecruitRoster();
                    controller.LoadDemoStage("defense", "female");
                    stage = "defense-rosters-done"; frameDeadline = Time.frameCount + 15;
                    break;
                case "defense-rosters-done":
                    controller = C;
                    if (Time.frameCount < frameDeadline || controller?.State?.Campaign.Location != "perimeter" ||
                        Object.FindAnyObjectByType<SceneLoadingCover>() != null) return;
                    H.ClosePanel();
                    if (!controller.Change(s => s.Muster.Strategies = new List<int>
                        { (int)SquadStrategy.Obey, (int)SquadStrategy.Attack, (int)SquadStrategy.Obey }, "界面检查"))
                        throw new InvalidOperationException(controller.Message);
                    Approach("demo_defense_start");
                    H.OpenObject("demo_defense_start");
                    RequirePanel("BattlePreparation");
                    stage = "defense-prep-shot"; frameDeadline = Time.frameCount + 15;
                    break;
                case "defense-prep-shot":
                    if (Time.frameCount < frameDeadline) return;
                    var preparation = RequirePanel("BattlePreparation");
                    VerifyBelowTopBar(preparation);
                    if (preparation.Find("Backpack") != null || ((RectTransform)preparation.Find("Continue")).anchoredPosition.x >
                        ((RectTransform)preparation.Find("Reset")).anchoredPosition.x)
                        throw new InvalidOperationException("备战页仍有额外背包按钮或部署按钮没有置左");
                    if (Environment.GetEnvironmentVariable("BASE_CONQUEST_UI_CHECK_SAVED_LAYOUT") == "1" &&
                        ((RectTransform)preparation.Find("Close")).anchoredPosition != new Vector2(1025, -9))
                        throw new InvalidOperationException("启动后备战关闭按钮未读取保存的位置");
                    var defenseDraft = (BaseBattlePreparation)Field(H, "battleDraft");
                    if (defenseDraft.RequiredSupply(false) <= 0 || preparation.Find("Continue").GetComponent<Button>().interactable)
                        throw new InvalidOperationException("编入士兵后仍可未选军需进场");
                    Press(preparation, "Help");
                    if (!preparation.Find("HelpOverlay").gameObject.activeInHierarchy)
                        throw new InvalidOperationException("备战说明未打开");
                    H.ClosePanel();
                    if (preparation.Find("HelpOverlay").gameObject.activeInHierarchy ||
                        !ReferenceEquals(defenseDraft, Field(H, "battleDraft")))
                        throw new InvalidOperationException("退出备战说明清空了草案");
                    Press(preparation, "Candidates/SortHealth");
                    preparation = RequirePanel("BattlePreparation");
                    var roster = preparation.Find("Candidates/List").GetComponent<ScrollRect>();
                    VerifyRosterRow(roster.content.GetChild(0), true);
                    Press(roster.content.GetChild(0), "Info");
                    preparation = RequirePanel("BattlePreparation");
                    if (!preparation.Find("Candidates/Detail").gameObject.activeInHierarchy ||
                        !ReferenceEquals(defenseDraft, Field(H, "battleDraft")))
                        throw new InvalidOperationException("备战名册详情未打开或改变了草案");
                    Press(preparation, "Candidates/Detail/Back");
                    preparation = RequirePanel("BattlePreparation");
                    if (defenseDraft.AvailableSupply(controller.State, 2) >= defenseDraft.RequiredSupply(false))
                        throw new InvalidOperationException("检查用首防阵容没有二级军需缺口");
                    var gradeTwo = preparation.Find("Supply/Grade2").GetComponent<Button>();
                    if (gradeTwo.interactable)
                    {
                        Press(preparation, "Supply/Grade2");
                        if (preparation.Find("Continue").GetComponent<Button>().interactable)
                            throw new InvalidOperationException("二级军需不足仍可进场");
                    }
                    if (defenseDraft.AvailableSupply(controller.State, 1) < defenseDraft.RequiredSupply(false))
                    {
                        // The stage fixture can start with one more soldier than the level I stock.
                        H.ClosePanel();
                        if (!controller.Change(s => s.Inventory.Equipment.Single(e => e.Product == "supply" && e.Level == 1).Count += 2,
                            "检查一级军需选择"))
                            throw new InvalidOperationException(controller.Message);
                        H.OpenObject("demo_defense_start");
                        preparation = RequirePanel("BattlePreparation");
                    }
                    Press(preparation, "Supply/Grade1");
                    if (((BaseBattlePreparation)Field(H, "battleDraft")).SupplyGrade != 1)
                        throw new InvalidOperationException("一级军需未作为整场档位保存");
                    var teamRows = preparation.Find("Teams");
                    var secondTeam = teamRows.GetChild(1);
                    ClickHudButton(secondTeam);
                    if (RequirePanel("BattlePreparation") != preparation || teamRows.GetChild(1) != secondTeam)
                        throw new InvalidOperationException("点击队长时整页或队长行被重建");
                    if (teamRows.GetChild(1).Find("Portrait").GetComponent<Image>().color != DemoUiTheme.Current.Gold)
                        throw new InvalidOperationException("点击队长整行后，分配目标没有变为金色");
                    ClickHudButton(teamRows.GetChild(0));
                    var firstTeam = teamRows.GetChild(0);
                    var role = new[] { "Melee", "Ranged", "Cavalry" }.FirstOrDefault(kind =>
                        firstTeam.Find(kind + "/Minus").GetComponent<Button>().interactable);
                    if (role != null)
                    {
                        var field = firstTeam.Find(role + "/Input").GetComponent<InputField>();
                        string initial = field.text;
                        Press(firstTeam, role + "/Minus");
                        if (RequirePanel("BattlePreparation") != preparation || field.text == initial)
                            throw new InvalidOperationException("备战减兵未原位更新");
                        Press(firstTeam, role + "/Plus");
                        if (RequirePanel("BattlePreparation") != preparation || field.text != initial)
                            throw new InvalidOperationException("备战连续加减没有保留原数量控件");
                    }
                    var guide = (Graphic)Field(H, "questArrow");
                    var continueButton = (RectTransform)preparation.Find("Continue");
                    if (((RectTransform)Field(H, "questBar")).gameObject.activeSelf || !guide.gameObject.activeInHierarchy ||
                        Mathf.Abs(guide.transform.position.x - continueButton.TransformPoint(continueButton.rect.center).x) > 1)
                        throw new InvalidOperationException("备战面板上的任务箭头未对准进入战场或任务条遮住了面板");
                    Log.Add("[PASS] 备战点击整行切换金色头像，任务箭头指向进入战场");
                    defenseSupplyStock = controller.State.Inventory.Equipment.Single(e => e.Product == "supply" && e.Level == 1).Count;
                    CaptureViewports("ui-skin-battle-preparation");
                    var fieldCamera = controller.WorldCamera;
                    Press(preparation, "Continue");
                    var deploy = RequireBattleDraft();
                    if (deploy.PreviewBoard.Camera != fieldCamera)
                        throw new InvalidOperationException("迎战部署替换了当前战场相机");
                    CaptureViewports("ui-skin-battle-deployment-entry");
                    var draftArea = BattleSafeArea(deploy);
                    var banner = (RectTransform)draftArea.Find("FormalBanner");
                    if (banner == null || !banner.gameObject.activeInHierarchy || banner.anchorMin.x != 0 || banner.anchorMax.x != 1)
                        throw new InvalidOperationException("战况没有汇入横贯屏幕的顶栏");
                    var summary = banner.Find("FormalSummary");
                    if (!summary.gameObject.activeInHierarchy) throw new InvalidOperationException("战况未默认展开");
                    Press(banner, "DraftSummaryToggle");
                    if (summary.gameObject.activeInHierarchy) throw new InvalidOperationException("战况不能收起");
                    Press(banner, "DraftSummaryToggle");
                    if (!summary.gameObject.activeInHierarchy) throw new InvalidOperationException("战况不能重新展开");
                    var firstTroopRow = draftArea.Find("CommanderSlot0/DraftTroops");
                    var secondTroopRow = draftArea.Find("CommanderSlot1/DraftTroops");
                    Canvas.ForceUpdateCanvases();
                    var cards = firstTroopRow.GetComponentsInChildren<Button>().Select(b => (RectTransform)b.transform).ToArray();
                    float portraitWidth = ((RectTransform)draftArea.Find("CommanderSlot0/CommanderPortrait_0")).rect.width;
                    if (draftArea.Find("ControlledArmy").gameObject.activeInHierarchy ||
                        cards.Length != 3 || cards.Any(card => card.rect.width < portraitWidth - .01f || card.rect.height < 44) ||
                        cards[0].anchoredPosition.y <= cards[1].anchoredPosition.y + cards[1].rect.height ||
                        cards[1].anchoredPosition.y <= cards[2].anchoredPosition.y + cards[2].rect.height)
                        throw new InvalidOperationException("兵种卡未按头像全宽竖向排列");
                    if (cards.Where((card, index) => card.GetComponentsInChildren<BattleAvatarGraphic>().Length > 0 ||
                        !card.GetComponentInChildren<Text>().text.StartsWith(BattleSimulation.KindName(BattleSimulation.TroopKinds[index]) + "  ")).Any())
                        throw new InvalidOperationException("兵种卡未用文字显示兵种和余量");
                    Press(draftArea, "CommanderSlot0/TroopToggle");
                    if (firstTroopRow.gameObject.activeInHierarchy || !secondTroopRow.gameObject.activeInHierarchy)
                        throw new InvalidOperationException("兵种卡未按队独立收起");
                    Press(draftArea, "CommanderSlot0/TroopToggle");
                    if (!firstTroopRow.gameObject.activeInHierarchy)
                        throw new InvalidOperationException("兵种卡未重新展开");
                    Log.Add("[PASS] 战况统一进横幅，三张全宽兵种卡竖排且可按队独立折叠");
                    VerifyContinuousDeployment(deploy);
                    Press(draftArea, "DraftPreparationControls/Clear");
                    if (deploy.PreviewSimulation.Soldiers.Any(s => s.Side == BattleSide.A))
                        throw new InvalidOperationException("一键撤销未清空全部落点");
                    VerifyDeploymentPointer(deploy);
                    var resetDraft = (BaseBattlePreparation)Field(H, "battleDraft");
                    var firstSoldier = resetDraft.Units.First(u => u.TeamId == resetDraft.PlayerId &&
                        u.Selected && u.Kind != SoldierKind.Hero);
                    PlaceByPointer(deploy, resetDraft, firstSoldier.Id);
                    foreach (var leader in resetDraft.Units.Where(u => u.Selected && u.Kind == SoldierKind.Hero && u.Id != resetDraft.PlayerId))
                        if (!resetDraft.Place(leader.Id, resetDraft.Board.Positions().First(p => resetDraft.CanPlace(leader.Id, p))))
                            throw new InvalidOperationException("未能摆放默认选中检查用的队长");
                    Press(BattleSafeArea(deploy), "DraftPreparationControls/Back");
                    Press(RequirePanel("BattlePreparation"), "Continue");
                    defenseBattle = RequireBattleDraft();
                    defenseHud = defenseBattle.PreviewHud.Root;
                    defenseCamera = defenseBattle.PreviewBoard.Camera;
                    defenseViewport = defenseCamera.rect;
                    if (defenseBattle.PreviewSimulation.Soldiers.Count(s => s.Side == BattleSide.A) != 4)
                        throw new InvalidOperationException("从备战返回丢失了原落点");
                    stage = "defense-deployment-shot"; frameDeadline = Time.frameCount + 15;
                    break;
                case "defense-deployment-shot":
                    if (Time.frameCount < frameDeadline) return;
                    CaptureViewports("ui-skin-battle-deployment", mobile: true);
                    VerifyDeploymentSpace(defenseBattle);
                    FinishDialogue();
                    Click("拔剑迎战");
                    stage = "battle"; frameDeadline = Time.frameCount + 60; stageAt = EditorApplication.timeSinceStartup;
                    break;
                case "battle":
                    if (Field(controller, "combatController") == null)
                    {
                        if (EditorApplication.timeSinceStartup - stageAt > 30) Finish(2, "首防未开战：" + controller.Message);
                        frameDeadline = Time.frameCount + 20;
                        return;
                    }
                    if (Time.frameCount < frameDeadline) return;
                    if (!defenseBattle || defenseBattle.DeploymentDraftActive || defenseBattle.PreviewHud.Root != defenseHud ||
                        defenseBattle.PreviewBoard.Camera != defenseCamera || defenseCamera.rect != defenseViewport ||
                        defenseBattle.PreviewSimulation.Phase != BattlePhase.Fighting)
                        throw new InvalidOperationException("开战时没有沿用同一战场、相机和战斗 HUD");
                    Log.Add("[PASS] 确认开战沿用原战场相机、同一战斗 HUD，未重新建场");
                    var opening = defenseBattle.Realtime.Session.Opening;
                    if (opening.SupplySnapshot.FullConsumptionGrade != 1 || opening.SupplySnapshot.Demand("supply") != 1 ||
                        controller.State.Inventory.Equipment.Single(e => e.Product == "supply" && e.Level == 1).Count != defenseSupplyStock)
                        throw new InvalidOperationException("开战未按实际落位人数锁定军需，或提前扣除了库存");
                    Log.Add("[PASS] 备战军需按计划校验，实际开战按一个落位士兵锁定且未扣库存");
                    VerifyOpeningBattleSelection(defenseBattle);
                    if (androidTouch) VerifyBattleTouch(defenseBattle);
                    if (Object.FindObjectsByType<RectTransform>().Any(t => t.name == "BattleJoystick"))
                        throw new InvalidOperationException("正式战斗仍显示角色操控摇杆");
                    CaptureViewports("ui-icons-battle-hud");
                    stage = "done"; frameDeadline = Time.frameCount + 60;
                    break;
                case "done":
                    if (Time.frameCount < frameDeadline) return;
                    exitSaveFiles=SnapshotSaves();
                    defenseBattle.OpenCampaignMenu();
                    var systemMenu=((GameObject)Field(defenseBattle,"campaignMenu")).GetComponentsInChildren<RectTransform>(true).Single(rect=>rect.name=="选项面板");
                    Press(systemMenu,"MainMenu");
                    stage="discarded";stageAt=EditorApplication.timeSinceStartup;
                    break;
                case "discarded":
                    if(C==null||C.Hud==null||!BaseBattleGateway.MainMenu||defenseBattle)
                    {
                        if(EditorApplication.timeSinceStartup-stageAt>20)throw new InvalidOperationException("回主菜单未丢弃当前战斗");
                        return;
                    }
                    if(C.State!=null||!exitSaveFiles.SequenceEqual(SnapshotSaves()))
                        throw new InvalidOperationException("回主菜单仍恢复了会话或改写了磁盘存档");
                    Log.Add("[PASS] 战中回主菜单直接丢弃会话，存档文件逐字节不变");
                    Finish(0,"OK");
                    break;
            }
        }

        private static string[] exitSaveFiles;
        private static string[] SnapshotSaves()=>Directory.GetFiles(BaseBattleGateway.SaveDirectory,"*.json*")
            .OrderBy(path=>path,StringComparer.Ordinal).Select(path=>Path.GetFileName(path)+":"+Convert.ToBase64String(File.ReadAllBytes(path))).ToArray();

        private static void VerifyOpeningDrillControls()
        {
            H.ClosePanel();
            controller.BeginDemoPractice();
            var drill = controller.GetComponentsInChildren<BattleSceneController>().SingleOrDefault(battle => battle.IsDemoPractice);
            if (drill?.Realtime == null) throw new InvalidOperationException("无法从序章快照进入演练：" + controller.Message);

            var area = BattleSafeArea(drill);
            var portrait = area.Find("CommanderSlot0/CommanderPortrait_0");
            var pause = area.Find("RealtimeControls/Pause");
            var skill = area.Find("CommanderSlot0/战技");
            var attack = area.Find("RealtimeControls/Attack");
            var close = area.Find("CommanderDetails/关闭详情");
            var arrow = area.Find("PracticeArrow");
            var buttons = new[] { portrait, pause, skill, attack, close };
            if (buttons.Any(button => button?.GetComponent<Outline>() == null) || arrow == null ||
                area.Find("ControlledArmy").gameObject.activeInHierarchy || area.Find("自动").gameObject.activeInHierarchy ||
                area.Find("RealtimeControls").GetComponentsInChildren<Button>().Length != 5 ||
                ((RectTransform)skill).anchoredPosition.y <= ((RectTransform)portrait).anchoredPosition.y)
                throw new InvalidOperationException("演练缺少正式场景按钮或边框组件");
            var intro = controller.State.Demo.Intro;
            var tickGuide = typeof(BaseSceneController).GetMethod("TickPracticeGuide", BindingFlags.Instance | BindingFlags.NonPublic);
            drill.PreviewHud.Refresh();
            var resting = buttons.ToDictionary(button => button, ReadOutline);
            if ((intro.DrillFlags & BaseDemoIntro.Select) != 0 || drill.SelectedUnits.Count != 0)
                throw new InvalidOperationException("序章演练开场应由玩家主动选中角色");

            (Color Color, Vector2 Distance, bool Enabled) ReadOutline(Transform target)
            {
                var outline = target.GetComponent<Outline>();
                return (outline.effectColor, outline.effectDistance, outline.enabled);
            }

            void ExpectCue(Transform target = null, bool mapTarget = false)
            {
                if (arrow.gameObject.activeInHierarchy != (target != null || mapTarget))
                    throw new InvalidOperationException("演练没有显示或清除箭头");
                if (arrow.gameObject.activeInHierarchy && arrow.GetComponent<CanvasRenderer>() == null)
                    throw new InvalidOperationException("演练箭头缺少 CanvasRenderer");
                if (target is RectTransform rect)
                {
                    var center = area.InverseTransformPoint(rect.TransformPoint(rect.rect.center));
                    if (Mathf.Abs(area.InverseTransformPoint(arrow.position).x - center.x) > 1)
                        throw new InvalidOperationException("演练箭头未对准按钮中心");
                }
                foreach (var button in buttons)
                {
                    var expected = button == target ? (DemoUiTheme.Current.Gold, new Vector2(4, -4), true) : resting[button];
                    if (!ReadOutline(button).Equals(expected) || button == target && !button.gameObject.activeInHierarchy)
                        throw new InvalidOperationException("演练提示边框状态不对：" + button.name);
                }
            }

            void ShowStep(string expected)
            {
                BaseDemoIntro.AdvanceTutorialDelay(intro, 1);
                tickGuide.Invoke(controller, null);
                if (!area.Find("PracticeInstruction").GetComponentInChildren<Text>().text.Contains(expected))
                    throw new InvalidOperationException("演练正文没有指向当前控件：" + expected);
                drill.PreviewHud.Refresh();
            }

            void ClearDelayCue()
            {
                tickGuide.Invoke(controller, null);
                ExpectCue();
            }

            void HoldPortrait()
            {
                var pointer = new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left };
                ExecuteEvents.Execute(portrait.gameObject, pointer, ExecuteEvents.pointerDownHandler);
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
                typeof(BattleSceneController).GetField("portraitPressTime", flags).SetValue(drill, Time.unscaledTime - .5f);
                typeof(BattleSceneController).GetMethod("UpdatePortraitHold", flags).Invoke(drill, null);
                ExecuteEvents.Execute(portrait.gameObject, pointer, ExecuteEvents.pointerUpHandler);
                EventSystem.current.SetSelectedGameObject(null);
                if (!drill.DetailsVisible || drill.DetailsSlot != 0)
                    throw new InvalidOperationException("长按左下角头像没有打开主角详情");
            }

            ShowStep("左下角");
            ExpectCue(portrait);
            CaptureViewports("ui-player-route-drill-select");
            ClickHudButton(portrait);
            ClearDelayCue();
            ShowStep("右下角");
            ExpectCue(pause);
            ClickHudButton(pause);
            ClearDelayCue();
            if (!drill.Automatic || (intro.DrillFlags & BaseDemoIntro.Pause) == 0)
                throw new InvalidOperationException("战斗时间按钮没有推进演练");

            // The continuous route executes movement, holding and the dummy demonstration.
            intro.DrillFlags |= BaseDemoIntro.Move | BaseDemoIntro.Guard;
            ShowStep(drill.Realtime.SkillName(BattleSide.A, 0));
            ExpectCue(skill);
            ClickHudButton(skill);
            ClearDelayCue();
            HoldPortrait();
            ExpectCue();
            if (!drill.Realtime.Model.Paused || area.Find("CommanderDetails/Strategy_Attack").gameObject.activeInHierarchy)
                throw new InvalidOperationException("查看详情时未暂时暂停，或旧策略按钮仍可见");
            ClickHudButton(close);
            if (drill.Realtime.Model.Paused || !drill.Automatic)
                throw new InvalidOperationException("关闭详情后未恢复战斗");

            intro.DummyPhase = 2;
            ShowStep("进攻");
            ExpectCue(attack);
            Canvas.ForceUpdateCanvases();
            var attackBounds = RectTransformUtility.CalculateRelativeRectTransformBounds(attack.parent, attack);
            foreach (var strategy in new[] { "Manual", "Defense", "Pause", "Speed" })
            {
                var other = attack.parent.Find(strategy);
                if (other?.gameObject.activeInHierarchy != true ||
                    attackBounds.Intersects(RectTransformUtility.CalculateRelativeRectTransformBounds(attack.parent, other)))
                    throw new InvalidOperationException("进攻与其他策略按钮重叠");
            }
            if (!attack.GetComponent<Image>().raycastTarget)
                throw new InvalidOperationException("进攻按钮未接收界面输入");
            Capture("ui-player-route-drill-strategy");
            ClickHudButton(attack);
            ExpectCue(mapTarget: true);
            if (intro.DummyPhase != 3 || !drill.Automatic || !drill.WithdrawSceneEncounter())
                throw new InvalidOperationException("演练策略或返回村庄失败");
            Log.Add("[PASS] 序章演练实际控件有指引箭头，查看详情暂时暂停并恢复，进攻在统一操作栏");
        }

        private static void LoadRouteView(string fixture)
        {
            var path = Path.Combine("Logs", "demo-route-checkpoints", fixture + ".json");
            var state = JsonUtility.FromJson<BaseState>(File.ReadAllText(path));
            // The rules-only route did not advance presentation time between achievements.
            while (BaseAchievements.AdvanceNotice(state, 3) != null) { }
            BaseBattleGateway.MainMenu = false;
            controller.SaveStore.RestoreSession(state);
            if (fixture == "merchant")
                UnityEngine.SceneManagement.SceneManager.LoadScene(BaseBattleGateway.ExplorationScene);
            else controller.LoadSaved();
            stage = "route-" + fixture;
            frameDeadline = Time.frameCount + 20;
            stageAt = EditorApplication.timeSinceStartup;
        }

        private static void VerifyFirstIntelReopened()
        {
            H.ClosePanel();
            H.OpenFirstRaidIntel();
            FinishDialogue();
            Click("离开");
            Approach("town_peddler");
            H.OpenObject("town_peddler");
            FinishDialogue();
            string reopen = BaseConquest.Story.StoryText.Get("story.ui.intel.first_reopen", "免费首防消息");
            string intel = BaseConquest.Story.StoryText.Get("story.ui.trade.intel_entry", "小道消息");
            Click(intel);
            Click("查看首防消息");
            FinishDialogue();
            Click("领取免费首防消息");
            FinishDialogue();
            int day = controller.State.Demo.Flow.FreeFirstRaidDay;
            var raid = controller.State.Campaign.Events.Single(item => item.Id == BaseDemoDefense.EventId);
            if (day != raid.AvailableDay || !BaseRaidIntel.Known(controller.State, raid) ||
                !BaseCalendar.Quarter(controller.State, BaseCalendar.QuarterNumber(day)).Single(item => item.Day == day).Raids.Contains(raid.Title))
                throw new InvalidOperationException("补领后首防消息和日历日期不一致");
            Capture("ui-player-route-first-intel");
            Click("知道了");
            H.OpenObject("town_peddler");
            FinishDialogue();
            Click(intel);
            if (RequirePanel("TownTrade").GetComponentsInChildren<Text>().Any(label => label.text == reopen))
                throw new InvalidOperationException("补领后小道消息仍显示免费首防消息");
            Log.Add("[PASS] 免费首防消息离开后可从老莫补领，日历日期一致，领取后入口消失");
        }

        private static bool RectWithin(RectTransform child, RectTransform ancestor)
        {
            Canvas.ForceUpdateCanvases();
            var bounds = new Vector3[4];
            child.GetWorldCorners(bounds);
            var area = ancestor.rect;
            return bounds.Select(ancestor.InverseTransformPoint).All(point =>
                point.x >= area.xMin - .1f && point.x <= area.xMax + .1f &&
                point.y >= area.yMin - .1f && point.y <= area.yMax + .1f);
        }

        private static bool RouteViewReady(Func<BaseState, bool> matches)
        {
            controller = C;
            bool ready = Time.frameCount >= frameDeadline && controller?.State != null && !controller.Busy &&
                Object.FindAnyObjectByType<SceneLoadingCover>() == null && matches(controller.State);
            if (!ready && EditorApplication.timeSinceStartup - stageAt > 30)
                throw new InvalidOperationException("连续路线存档未进入预期界面：" + controller?.Message);
            return ready;
        }

        private static void CheckHomeArrival()
        {
            var arrival = RequirePanel("NpcDialogue");
            if (controller.State.Demo.Flow.HomeEnterSeen)
                throw new InvalidOperationException("首次返乡开场读完前就写入了已读标记");
            H.ContinueDialogue();
            if (!arrival.Find("DialogueText").GetComponent<Text>().text.Contains("粮仓也被搬空"))
                throw new InvalidOperationException("首次入村未独立播放返乡开场");
            CaptureViewports("ui-player-route-home-arrival");
            string galanId = "partner_recruit:" + controller.State.Demo.Flow.FirstControllerId;
            bool shouldHearGalan = BaseAdventure.Following(controller.State, galanId);
            bool heardGalan = false;
            for (int i = 0; i < 24 && H.DialogueOpen; i++)
            {
                H.ContinueDialogue();
                if (H.DialogueOpen && arrival.Find("SpeakerName").GetComponent<Text>().text == "迦兰" &&
                    arrival.Find("DialogueText").GetComponent<Text>().text.Contains("受过操练"))
                    heardGalan = true;
            }
            if (heardGalan != shouldHearGalan || H.PanelOpen || !controller.State.Demo.Flow.HomeEnterSeen ||
                !controller.SaveStore.Load().Demo.Flow.HomeEnterSeen)
                throw new InvalidOperationException("返乡开场没有按迦兰实际随行情况播放，或读完没有回到探索并保存");

            var field = BaseSpatial.Objects(controller.State).Single(item => item.Id == "intro_farm" || item.Id == "village_field");
            Approach(field.Id);
            var day = controller.State.Day;
            H.OpenObject(field.Id);
            H.ContinueDialogue();
            if (!RequirePanel("NpcDialogue").Find("DialogueText").GetComponent<Text>().text.Contains("马蹄印"))
                throw new InvalidOperationException("麦田未播放可选独白");
            CaptureViewports("ui-player-route-home-field");
            FinishDialogue();
            if (H.PanelOpen || controller.State.Day != day || BaseDemoFinale.Rebuilding(controller.State))
                throw new InvalidOperationException("麦田独白改变了日程或重建进度");

            Approach("demo_survivor");
            var initialPosition = new BasePoint(controller.State.PlayerX, controller.State.PlayerY);
            H.OpenObject("demo_survivor");
            var dialogue = RequirePanel("NpcDialogue");
            var speech = dialogue.Find("DialogueText").GetComponent<Text>();
            bool bridgeMentioned = false;
            for (int i = 0; i < 24 && H.DialogueOpen; i++)
            {
                H.ContinueDialogue();
                if (speech.text.Contains("西桥头") && dialogue.Find("SpeakerName").GetComponent<Text>().text == "幸存村民")
                {
                    bridgeMentioned = true;
                    CaptureViewports("ui-player-route-home-survivor");
                    break;
                }
            }
            if (!bridgeMentioned) throw new InvalidOperationException("幸存村民对白未指向西桥头");
            FinishDialogue();
            if (H.PanelOpen || BaseDemoFinale.Rebuilding(controller.State) ||
                new BasePoint(controller.State.PlayerX, controller.State.PlayerY).Distance(initialPosition) > .01)
                throw new InvalidOperationException("幸存者对话未退回原地探索");
            Approach("demo_broken_sword");
            H.OpenObject("demo_broken_sword");
            FinishDialogue();
            Click("记录断剑 · 开始重建");
            if (!controller.RebuildPlacementMode || !BaseDemoFinale.Rebuilding(controller.State))
                throw new InvalidOperationException("未从断剑调查进入地图重建");
            Log.Add("[PASS] 首次入村独立播放并保存，迦兰按实际随行发言，麦田可选独白不改变进度，调查断剑进入返乡重建");
        }

        private static void CheckHomeRebuild()
        {
            var source = (RectTransform)controller.transform.Find("UI 布局源/BaseShell/Controls/HomeRebuildProjects");
            if (source == null) throw new InvalidOperationException("返乡重建缺少场景布局源");
            var rebuildHud = typeof(BaseSceneController).GetMethod("RebuildHud", BindingFlags.Instance | BindingFlags.NonPublic);
            var sourceImage = source.GetComponent<Image>();
            var authoredPosition = source.anchoredPosition;
            var authoredColor = sourceImage.color;
            try
            {
                source.anchoredPosition += new Vector2(8, 0);
                sourceImage.color = new Color(.82f, .67f, .47f);
                rebuildHud.Invoke(controller, null);
                var moved = (RectTransform)Field(H, "rebuildPalette");
                if (moved.anchoredPosition != source.anchoredPosition || moved.GetComponent<Image>().color != sourceImage.color)
                    throw new InvalidOperationException("返乡工具栏未读取场景保存的位置和颜色");
            }
            finally
            {
                source.anchoredPosition = authoredPosition;
                sourceImage.color = authoredColor;
                rebuildHud.Invoke(controller, null);
            }
            ClickHudButton((Field(H, "rebuildResume") as Button)?.transform);
            var palette = (RectTransform)Field(H, "rebuildPalette");
            if (H.PanelOpen || !controller.RebuildPlacementMode || !palette.gameObject.activeInHierarchy ||
                ((Button)Field(H, "interact")).gameObject.activeInHierarchy || ((Text)Field(H, "hint")).gameObject.activeInHierarchy)
                throw new InvalidOperationException("返乡重建没有显示场景操作栏");
            var state = controller.State;
            var occupied = new GridPosition((int)state.PlayerX, (int)state.PlayerY);
            var view = (BattleBoardView)Field(controller, "regionView");
            var pick = typeof(BaseSceneController).GetMethod("SelectScreenPoint", BindingFlags.Instance | BindingFlags.NonPublic);
            void PickCell(GridPosition cell)
            {
                var screen = (Vector2)controller.WorldCamera.WorldToScreenPoint(controller.WorldPoint(cell.X + .5, cell.Z + .5));
                pick.Invoke(controller, new object[] { screen });
                if (!((BattleBoardView)Field(controller, "regionView")).SelectedCell.Equals((GridPosition?)cell))
                    throw new InvalidOperationException("点击地图后未预览目标格：" + cell);
            }
            PickCell(occupied);
            if (palette.Find("放置").GetComponent<Button>().interactable || controller.ConfirmRebuildPlacement() || state.Demo.Finale.RebuildMask != 0)
                throw new InvalidOperationException("占用格未阻止设施放置");
            CaptureViewports("ui-player-route-home-rebuild-blocked");
            var placedCells = new List<GridPosition>();
            for (int item = 0; item < 3; item++)
            {
                view = (BattleBoardView)Field(controller, "regionView");
                state = controller.State;
                var cell = view.Board.Positions().Where(p => BaseDemoFinale.RebuildProblem(state, item, p.X, p.Z) == "" &&
                    placedCells.All(other => Mathf.Abs(p.X - other.X) + Mathf.Abs(p.Z - other.Z) > 2))
                    .OrderBy(p => Mathf.Abs(p.X - occupied.X) + Mathf.Abs(p.Z - occupied.Z))
                    .First(p =>
                    {
                        var screen = controller.WorldCamera.WorldToScreenPoint(controller.WorldPoint(p.X + .5, p.Z + .5));
                        return view.Pick(screen, out var hit) && hit.Equals(p);
                    });
                PickCell(cell);
                if (item == 0) CaptureViewports("ui-player-route-home-rebuild");
                if (state.Demo.Finale.RebuildMask != (1 << item) - 1)
                    throw new InvalidOperationException("仅点击预览就提前提交了重建");
                ClickHudButton(((RectTransform)Field(H, "rebuildPalette")).Find("放置"));
                placedCells.Add(cell);
                if (item == 0)
                {
                    controller.SelectRebuildProject(0);
                    ClickHudButton(((RectTransform)Field(H, "rebuildPalette")).Find("撤回"));
                    if (controller.State.Demo.Finale.RebuildMask != 0) throw new InvalidOperationException("重建设施未撤回");
                    PickCell(cell);
                    ClickHudButton(((RectTransform)Field(H, "rebuildPalette")).Find("放置"));
                }
            }
            var saved = controller.SaveStore.Load();
            if (saved.Demo.Finale.RebuildMask != 7 || saved.Demo.Stage == BaseDemoStage.Finished)
                throw new InvalidOperationException("重建落位未保存，或提前结束 Demo");
            var landmarks = ((BattleBoardView)Field(controller, "regionView")).Root.transform.Find("RegionLandmarks");
            foreach (var facility in new[] { ("demo_rebuild_tent", "camp-tent-v1"), ("demo_rebuild_warehouse", "warehouse-v1") })
            {
                var landmark = landmarks.Cast<Transform>().SingleOrDefault(obj => obj.name.StartsWith(facility.Item1 + " · ", StringComparison.Ordinal));
                var texture = landmark?.GetComponentInChildren<SpriteFacade>()?.Surface.sharedMaterial.GetTexture("_BaseMap");
                if (texture == null || texture.name != facility.Item2) throw new InvalidOperationException("重建设施未加载正式贴图：" + facility.Item1);
            }
            ClickHudButton(((RectTransform)Field(H, "rebuildPalette")).Find("确认完成"));
            if (!controller.HomeRebuildOverview || H.PanelOpen) throw new InvalidOperationException("完成前缺少村庄全景");
            CaptureViewports("ui-player-route-home-rebuilt-village");
            ClickHudButton(((RectTransform)Field(H, "rebuildOverview")).Find("结束 Demo"));
            if (controller.State.Demo.Stage != BaseDemoStage.Finished || controller.HomeRebuildOverview || !H.DialogueOpen)
                throw new InvalidOperationException("村庄全景未接完成状态和收尾对白");
            H.ContinueDialogue();
            CaptureViewports("ui-player-route-home-ending");
            Log.Add("[PASS] 重建实际点格预览、占用格拒绝、撤回重放、保存三处设施并先展示全景再接收尾对白");
        }

        private static void VerifyResourceHud()
        {
            var strip = Field(H, "resourceStrip") as RectTransform;
            var stats = strip?.Find("Stats");
            var order = new[] { "Calendar", "Food", "Supplies", "Materials", "Coins", "Energy", "Troops" };
            if (stats == null || !stats.Cast<Transform>().Select(item => item.name).SequenceEqual(order))
                throw new InvalidOperationException("资源栏七项的顺序不符");
            if (strip.parent.Find("任务") != null || strip.parent.Find("i") != null || string.IsNullOrWhiteSpace(strip.Find("Location").GetComponent<Text>().text))
                throw new InvalidOperationException("资源栏仍有旧入口或缺少地点名");
            var positions = stats.Cast<RectTransform>().Select(item => item.anchoredPosition).ToArray();
            if (positions.Any(position => Mathf.Abs(position.y - positions[0].y) > .01f) || !positions.Select(position => position.x).SequenceEqual(positions.Select(position => position.x).OrderBy(x => x)))
                throw new InvalidOperationException("七项资源没有按顺序同排显示");

            var menu = RequirePanel("CampMenu");
            var removedMenuItems = new[] { "当前剧情目标", "资源与人员信息", "日历", "体力" };
            if (menu.GetComponentsInChildren<Button>().Select(b => b.GetComponentInChildren<Text>()?.text)
                .Any(title => title != null && removedMenuItems.Any(removed => title.StartsWith(removed, StringComparison.Ordinal))))
                throw new InvalidOperationException("系统菜单仍含剧情目标、资源说明、日历或体力入口");
            Click("任务 · N");
            if ((string)Field(H, "panelViewKey") != "quests") throw new InvalidOperationException("菜单任务入口没有打开任务列表");
            H.ClosePanel();

            if (Object.FindObjectsByType<RectTransform>().Any(t => t.name == "TrackedQuest"))
                throw new InvalidOperationException("仍显示常驻任务追踪");

            var expectedTitles = new[] { "日历", "粮食", "军需", "原料", "钱币", "体力", "兵力" };
            for (int i = 0; i < order.Length; i++)
            {
                ClickHudButton(stats.Find(order[i]));
                var popup = Field(H, "resourcePopover") as RectTransform;
                if (!H.ResourcePopoverOpen || H.PanelOpen || !popup.Find("Window/Title").GetComponent<Text>().text.StartsWith(expectedTitles[i], StringComparison.Ordinal))
                    throw new InvalidOperationException("资源入口没有打开对应的下拉：" + order[i]);
                var scroll = popup.Find("Window/Body").GetComponent<ScrollRect>();
                var body = scroll.content;
                if (body.childCount == 0 || popup.GetComponentsInChildren<Text>().Any(t => t.resizeTextForBestFit))
                    throw new InvalidOperationException("资源下拉缺少明细或开启了字号自适应：" + order[i]);
                if (body.rect.width > scroll.viewport.rect.width + 1)
                    throw new InvalidOperationException("资源明细超出可见宽度：" + order[i]);
                if (i == 0)
                {
                    var calendar = body.Find("ResourceCalendar");
                    int start = BaseCalendar.QuarterStart(controller.State.Day);
                    if (calendar.Find("Days").childCount != 15 || !calendar.Find("Days").GetChild(0).GetComponentInChildren<Text>().text.StartsWith(start.ToString(), StringComparison.Ordinal))
                        throw new InvalidOperationException("日历未显示本季度 15 天");
                    CaptureViewports("ui-skin-calendar");
                    ClickHudButton(calendar.Find("Next"));
                    var next = ((Transform)Field(H, "resourcePopover")).Find("Window/Body/Viewport/Content/ResourceCalendar");
                    if (next.Find("Next").GetComponent<Button>().interactable || !next.Find("Days").GetChild(0).GetComponentInChildren<Text>().text.StartsWith((start + 15).ToString(), StringComparison.Ordinal))
                        throw new InvalidOperationException("日历没有切到下季度或允许越界");
                    ClickHudButton(next.Find("Previous"));
                    if (BaseCalendar.QuarterNumber(controller.State.Day) == 1 && ((Transform)Field(H, "resourcePopover")).Find("Window/Body/Viewport/Content/ResourceCalendar/Previous").GetComponent<Button>().interactable)
                        throw new InvalidOperationException("日历允许翻到首季度以前");
                }
                else
                {
                    string name = i == 1 ? "ui-skin-resources" : "ui-skin-resource-" + order[i].ToLowerInvariant();
                    if (i == 1 || i == 6) CaptureViewports(name);
                    else Capture(name);
                }
            }

            Canvas.ForceUpdateCanvases();
            var lastPopup = (RectTransform)Field(H, "resourcePopover");
            var outside = lastPopup.TransformPoint(new Vector3(lastPopup.rect.xMax - 18, lastPopup.rect.yMin + 100, 0));
            var pointer = new PointerEventData(EventSystem.current) { position = RectTransformUtility.WorldToScreenPoint(null, outside) };
            var hits = new List<RaycastResult>();
            EventSystem.current.RaycastAll(pointer, hits);
            if (hits.Any(hit => hit.gameObject.transform.IsChildOf(lastPopup)))
                throw new InvalidOperationException("下拉挡住了弹窗外的地图区域");
            var state = controller.State;
            int x = Mathf.FloorToInt((float)state.PlayerX), y = Mathf.FloorToInt((float)state.PlayerY);
            controller.ClearSelection();
            controller.SelectCell(x, y);
            if (controller.SelectedCell == null || !H.ResourcePopoverOpen)
                throw new InvalidOperationException("资源下拉打开时不能照常选中地图格");
            controller.ClearSelection();
            var dismiss = controller.GetType().GetMethod("DismissResourcePopoverOutside", BindingFlags.Instance | BindingFlags.NonPublic);
            dismiss.Invoke(controller, new object[] { pointer.position });
            controller.SelectCell(x, y);
            if (H.ResourcePopoverOpen || H.PanelOpen || controller.SelectedCell == null)
                throw new InvalidOperationException("点击外部没有关闭下拉并保留同帧地图交互");
            controller.ClearSelection();
            Log.Add("[PASS] 七项资源下拉、日历翻页、点击外部关闭且地图可操作、菜单任务册、移除常驻追踪");
        }

        private static void ClickHudButton(Transform node)
        {
            var button = node == null ? null : node.GetComponent<Button>();
            if (button == null || !button.isActiveAndEnabled || !button.interactable)
                throw new InvalidOperationException("HUD 按钮不可操作：" + node?.name);
            button.onClick.Invoke();
        }

        private static void VerifyOpeningCopy()
        {
            const string playerName = "青岚";
            var state = controller.State;
            if (!BaseDemoPrologue.Village(state) || state.Demo.Intro.Step != 0)
                throw new InvalidOperationException("新档未停在白桦村选角");

            var arrow = Field(H, "questArrow") as MaskableGraphic;
            if (arrow == null || !arrow.isActiveAndEnabled || arrow.GetComponent<CanvasRenderer>() == null)
                throw new InvalidOperationException("选角任务箭头未显示或缺少 CanvasRenderer");
            Canvas.ForceUpdateCanvases();
            Log.Add("[PASS] 开局选角任务箭头进入画布渲染");

            var choice = Field(H, "heroChoiceLayer") as RectTransform;
            if (choice == null) throw new InvalidOperationException("新档未打开选角界面");
            ClickHudButton(choice.Find("HeroChoice_female"));
            ClickHudButton(choice.Find("确认选择"));
            var nameDialog = choice.Find("HeroName/NameConfirmation");
            var input = nameDialog.Find("PlayerNameInput").GetComponent<InputField>();
            var confirm = nameDialog.Find("ConfirmPlayerName").GetComponent<Button>();
            if (confirm.interactable) throw new InvalidOperationException("空姓名可以确认");
            input.text = "错/名";
            if (confirm.interactable) throw new InvalidOperationException("包含路径符号的姓名可以确认");
            input.text = playerName;
            if (!confirm.interactable) throw new InvalidOperationException("合法中文姓名不能确认");
            CaptureViewports("ui-skin-player-name");
            ClickHudButton(nameDialog.Find("返回选角"));
            string directory = BaseBattleGateway.SaveDirectory;
            if (controller.State.Demo.Intro.Step != 0 || !string.IsNullOrEmpty(controller.State.Demo.Intro.PlayerName)
                || Directory.Exists(directory) && Directory.EnumerateFiles(directory, "*.json").Any())
                throw new InvalidOperationException("取消填写姓名写入了进度");

            ClickHudButton(choice.Find("确认选择"));
            var currentNameDialog = ((RectTransform)Field(H, "heroNameLayer")).Find("NameConfirmation");
            currentNameDialog.Find("PlayerNameInput").GetComponent<InputField>().text = playerName;
            ClickHudButton(currentNameDialog.Find("ConfirmPlayerName"));
            state = controller.State;
            var catalog = BaseBattleGateway.Catalog();
            var namedEntry = catalog.Entries().FirstOrDefault(e => e.Error == null && e.PlayerName == playerName);
            if (state.Demo.Intro.Step != 1 || state.Demo.Intro.Hero != "female" ||
                state.Leaders.Single(l => l.Id == state.PlayerLeaderId).Name != playerName || namedEntry == null ||
                !Directory.EnumerateFiles(directory, playerName + "-auto*.json").Any() ||
                catalog.LoadSlot(namedEntry.Id).Demo.Intro.PlayerName != playerName)
                throw new InvalidOperationException("自定义姓名没有保存到主角和新版自动档");
            Log.Add("[PASS] 选角后填写姓名，拒绝空白与非法符号，取消零写入，中文姓名保存并可重读");

            H.ClosePanel();
            H.OpenQuests("achievement");
            H.ClosePanel();
            Canvas.ForceUpdateCanvases();
            var taskBar = (RectTransform)Field(H, "questBar");
            var pointer = new PointerEventData(EventSystem.current)
            {
                position = RectTransformUtility.WorldToScreenPoint(null, taskBar.TransformPoint(taskBar.rect.center)),
                button = PointerEventData.InputButton.Left
            };
            var taskBackground = taskBar.GetComponent<Image>();
            if (!taskBackground.isActiveAndEnabled || !taskBackground.raycastTarget ||
                !RectTransformUtility.RectangleContainsScreenPoint(taskBar, pointer.position) ||
                !taskBackground.Raycast(pointer.position, null) ||
                !ExecuteEvents.Execute(taskBar.gameObject, pointer, ExecuteEvents.pointerClickHandler) ||
                (string)Field(H, "questTab") != "tasks" || RequirePanel("CommonPanel").Find("Title").GetComponent<Text>().text != "任务与成就")
                throw new InvalidOperationException("点探索任务栏没有直接进入任务页");
            H.ClosePanel();
            Log.Add("[PASS] 点击探索任务栏直达任务，即使此前查看的是成就");
            VerifyBenchInteraction();
            Approach("demo_captain");
            H.OpenObject("demo_captain");
            FinishDialogue();
            Click("聊聊从前在王都值夜");
            H.ContinueDialogue();
            if (!RequirePanel("NpcDialogue").Find("DialogueText").GetComponent<Text>().text.Contains("宫门外"))
                throw new InvalidOperationException("长椅旧事分支未播放");
            FinishDialogue();
            if (controller.State.Demo.Intro.Step != 1)
                throw new InvalidOperationException("追问旧事提前推进了序章");
            Click("一起去，先向阿禾打个招呼");
            Approach("intro_villager");
            H.OpenObject("intro_villager");
            FinishDialogue();
            Click("我们去粮田安排");
            Approach("intro_farm");
            H.OpenObject("intro_farm");
            var villageFarm = RequirePanel("FacilityPanel");
            var villageActions = villageFarm.Find("Left/List").GetComponent<ScrollRect>().content;
            if (((RectTransform)villageFarm).rect.height > 400 || villageActions.Find("VillageFarmWorkers/Unlock").gameObject.activeSelf)
                throw new InvalidOperationException("村庄派工没有使用紧凑布局，或显示了尚未开放的解锁操作");
            Canvas.ForceUpdateCanvases();
            typeof(BaseHud).GetMethod("RefreshQuestCue", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(H, null);
            Capture("ui-village-farm-compact");
            var villagePlus = villageActions.Find("VillageFarmWorkers/Plus");
            for (int i = 0; i < 2; i++)
            {
                Press(villageActions, "VillageFarmWorkers/Plus");
                if (RequirePanel("FacilityPanel") != villageFarm || villageActions.Find("VillageFarmWorkers/Plus") != villagePlus)
                    throw new InvalidOperationException("白桦村加派工人时重建了面板");
            }
            Click("确认方案");
            H.ContinueDialogue();
            if (controller.State.Demo.Intro.Step != 10 || controller.State.Demo.Intro.FarmWorkers != 4 ||
                !RequirePanel("NpcDialogue").Find("DialogueText").GetComponent<Text>().text.Contains("晚麦"))
                throw new InvalidOperationException("调工到四人后未接入阿禾回应");
            H.ContinueDialogue();
            H.ContinueDialogue();
            if (!RequirePanel("NpcDialogue").Find("DialogueText").GetComponent<Text>().text.Contains("来找我拿剑"))
                throw new InvalidOperationException("调工后未接入搭档领剑指引");
            FinishDialogue();
            if (H.DialogueOpen || BaseQuestLog.Cue(controller.State).ObjectId != "demo_captain")
                throw new InvalidOperationException("调工回应结束后未继续领剑主线");
            Log.Add("[PASS] 长椅旧事汇回原主线，粮田实际调到四人后接阿禾与搭档对白");
        }

        private static void VerifyBenchInteraction()
        {
            foreach (string hero in new[] { "player", "female" })
            {
                var atlas = PixelDemoArt.Motion(hero, "sit");
                if (atlas == null || atlas.width != 320 || atlas.height != 80)
                    throw new InvalidOperationException("缺少完整坐姿图集：" + hero);
            }
            var bench = controller.MapObjects.Single(o => o.Id == "village_bench");
            var view = ((Transform)Field(controller, "player")).GetComponent<PlayerHeroView>();
            var move = typeof(BaseSceneController).GetMethod("MoveExploration", BindingFlags.Instance | BindingFlags.NonPublic);
            foreach (int side in new[] { -1, 1 })
            {
                double x = bench.X + side * bench.Width * .29;
                double y = bench.Y - bench.Height / 2 - BaseMap.Radius - .06;
                if (!controller.Change(s => { s.PlayerX = x; s.PlayerY = y; }, "") ||
                    controller.Nearby?.Id != bench.Id + (side < 0 ? ":seat:left" : ":seat:right"))
                    throw new InvalidOperationException("长椅没有对应的可达座位");
                controller.Interact();
                for (int n = 0; n < 9; n++) move.Invoke(controller, new object[] { Vector2.zero, .05 });
                if (view.Pose != HeroPose.Seated || view.Facing != HeroFacing.South || view.AtlasFrame != 3 ||
                    controller.State.PlayerX != x || controller.State.PlayerY != y || controller.State.Demo.Intro.Step != 1)
                    throw new InvalidOperationException("长椅坐姿变更了实际位置或剧情");
                if (side < 0)
                {
                    CaptureViewports("ui-skin-bench-sit");
                    controller.Interact();
                }
                for (int n = 0; n < 8; n++) move.Invoke(controller, new object[] { side < 0 ? Vector2.zero : Vector2.down, .05 });
                if (view.Pose != HeroPose.Idle || controller.State.PlayerX != x || controller.State.PlayerY != y)
                    throw new InvalidOperationException("完成起身后没有回到原站位");
            }
            Log.Add("[PASS] 白桦村长椅两端可分别坐下，E／触屏和方向输入可起身，保持剧情与存档位置");
        }

        private static void VerifyAuthoredLayout()
        {
            int index = BasePartyInventory.FreeCell(controller.State, controller.State.PlayerLeaderId);
            if (index < 0) throw new InvalidOperationException("场景配色检查需要一个空背包格");
            string path = "BagSlot_" + index;
            var source = (RectTransform)controller.transform.Find("UI 布局源/PartyMemberCard/" + path);
            var image = source.GetComponent<Image>();
            var originalPosition = source.anchoredPosition;
            var originalColor = image.color;
            try
            {
                source.anchoredPosition += new Vector2(7, -3);
                image.color = new Color32(190, 141, 84, 255);
                H.ClosePanel();
                H.OpenBackpack();
                var card = RequirePanel("PartyInventory").Find("Cards").GetChild(0);
                var live = (RectTransform)card.Find(path);
                if (Vector2.Distance(source.anchoredPosition, live.anchoredPosition) > .01f
                    || Vector4.Distance(image.color, live.GetComponent<Image>().color) > .005f)
                    throw new InvalidOperationException("背包格运行时覆盖了场景位置或配色");
                Log.Add("[PASS] 场景编辑的背包格位置与配色在重新打开时生效");
            }
            finally { source.anchoredPosition = originalPosition; image.color = originalColor; }
        }

        private static Rect SceneRect(Transform node, Transform relativeTo)
        {
            var corners = new Vector3[4];
            ((RectTransform)node).GetWorldCorners(corners);
            var min = relativeTo.InverseTransformPoint(corners[0]);
            var max = relativeTo.InverseTransformPoint(corners[2]);
            return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }

        private static void VerifyPartyCardLayout(int count)
        {
            var view = (RectTransform)RequirePanel("PartyInventory");
            var source = controller.transform.Find("UI 布局源");
            var authoredView = (RectTransform)source.Find("PartyInventory/FrameWidths/" + count);
            var authoredCard = (RectTransform)source.Find("PartyMemberCard");
            var group = (RectTransform)view.Find("Cards");
            Canvas.ForceUpdateCanvases();
            var cards = group.Cast<RectTransform>().Select(card => SceneRect(card, view)).ToArray();
            if (view.rect.size != authoredView.rect.size || cards.Length != count ||
                cards.Any(card => Vector2.Distance(card.size, authoredCard.rect.size) > .1f) ||
                Mathf.Abs(cards.Average(card => card.center.x) - SceneRect(group, view).center.x) > .1f ||
                cards.Any(card => Mathf.Abs(card.center.y - cards[0].center.y) > .1f) ||
                cards.Zip(cards.Skip(1), (left, right) => left.xMax > right.xMin).Any(overlaps => overlaps))
                throw new InvalidOperationException("独立背包没有沿用固定外窗和等宽居中角色卡：" + count);
            foreach (var cell in group.GetComponentsInChildren<PartyInventoryCell>())
            {
                if (cell.Cell < 0 && string.IsNullOrEmpty(cell.Slot)) continue;
                var rect = ((RectTransform)cell.transform).rect;
                if (Mathf.Abs(rect.width - rect.height) > .1f)
                    throw new InvalidOperationException("物品格或装备槽应为正方形：" + cell.name);
                if (!string.IsNullOrEmpty(cell.ItemId) && cell.transform.Find("ItemIcon") != null &&
                    cell.GetComponentInChildren<Text>(true).text != "")
                    throw new InvalidOperationException("有物品时只显示图标和堆叠数量：" + cell.name);
            }
            foreach (var person in controller.State.Leaders.Where(leader => BaseCommanderIdentity.Appearance(leader) == "azhu"))
            {
                var card = group.Find("人物与背包_" + person.Id);
                if (card == null) continue;
                var image = card.Find("Portrait/全身像/骑乘取景")?.GetComponent<RawImage>();
                if (image == null || Mathf.Abs((1 - image.uvRect.yMax) * image.texture.height - 14) > .01f ||
                    Mathf.Abs((1 - image.uvRect.yMin) * image.texture.height - 60) > .01f ||
                    Mathf.Abs(image.rectTransform.rect.height / ((RectTransform)image.transform.parent).rect.height - .875f) > .01f)
                    throw new InvalidOperationException("阿铸背包全身像应从头巾裁到马蹄，并与步行人物使用相同的可见高度");
                Log.Add("[PASS] 阿铸背包取景从头巾到马蹄，枪尖不占人物显示高度");
            }
            Log.Add("[PASS] 背包 " + count + " 人，外窗与角色卡尺寸从场景读取，卡组居中");
        }

        private static void VerifyTwoMemberBackpack()
        {
            H.ClosePanel();
            string member = controller.State.Adventure.Members.Last();
            if (!controller.Change(state => state.Adventure.Members.Remove(member), "界面检查"))
                throw new InvalidOperationException(controller.Message);
            H.OpenBackpack();
            VerifyPartyCardLayout(2);
            CaptureViewports("ui-skin-backpack-two");
            H.ClosePanel();
            if (!controller.Change(state => state.Adventure.Members.Add(member), "界面检查"))
                throw new InvalidOperationException(controller.Message);
            H.OpenBackpack();
        }

        private static void VerifyHideoutUi()
        {
            void StandWest()
            {
                if (!controller.Change(s =>
                {
                    var west = BaseSpatial.Nearest(s, new BasePoint(14.5, 12.5));
                    s.PlayerX = west.X; s.PlayerY = west.Y;
                    BaseAdventure.Arrive(s);
                }, "界面检查")) throw new InvalidOperationException(controller.Message);
            }
            StandWest();
            var refreshSight = controller.GetType().GetMethod("RefreshSceneSight", BindingFlags.Instance | BindingFlags.NonPublic);
            void RefreshSight() => refreshSight.Invoke(controller, new object[] { true });
            RefreshSight();
            var entrance = BaseSpatial.Objects(controller.State).Single(o => o.Id == BaseBanditHideout.EntryId);
            var cell = new GridPosition((int)entrance.X, (int)entrance.Y);
            var landmarks = ((BattleBoardView)Field(controller, "regionView")).Root.transform.Find("RegionLandmarks");
            var landmark = landmarks.Cast<Transform>().Single(view => view.name.StartsWith(entrance.Id + " · "));
            bool Presented() => controller.PresentedObject(entrance.Id) != null && landmark.gameObject.activeInHierarchy;
            if (controller.SceneCellKnown(cell) || controller.PresentedObject(entrance.Id) != null || landmark.gameObject.activeInHierarchy)
                throw new InvalidOperationException("浓雾未遮住窝点贴图及名称");
            Capture("ui-bandit-hideout-before-wall");
            Approach(BaseBanditHideout.EntryId);
            RefreshSight();
            if (!Presented()) throw new InvalidOperationException("绕到墙后仍不能发现窝点");
            CaptureViewports("ui-bandit-hideout-entrance");
            StandWest();
            RefreshSight();
            if (((BattleVision)Field(controller, "explorationSight")).Visible(BattleSide.A, cell) || !Presented())
                throw new InvalidOperationException("回到墙前后窝点未在薄雾下保留贴图及名称");
            Capture("ui-bandit-hideout-thin-fog");
            Approach(BaseBanditHideout.EntryId);
            RefreshSight();
            H.OpenObject(BaseBanditHideout.EntryId);
            var preparation = RequirePanel("BattlePreparation");
            var draft = (BaseBattlePreparation)Field(H, "battleDraft");
            if (draft.Route != BasePrepRoute.Hideout || draft.KnownEnemyCount() <= 0 ||
                !preparation.Find("Enemy/Value").GetComponent<Text>().text.Contains("敌方"))
                throw new InvalidOperationException("窝点没有进入备战并公开敌方阵容");
            if (preparation.Find("Sweep").gameObject.activeInHierarchy || preparation.Find("Reward").gameObject.activeInHierarchy ||
                !preparation.GetComponentsInChildren<Text>().Any(label => label.text.Contains("体力") && label.text.Contains("首通")))
                throw new InvalidOperationException("窝点首战没有显示体力规则，或提前显示扫荡及待领奖");
            if (draft.RequiredSupply(false) == 0)
            {
                Press(preparation.Find("Teams").GetChild(0), "Melee/Plus");
                preparation = RequirePanel("BattlePreparation");
            }
            var supply = preparation.Find("Supply");
            if (new[] { "Grade1", "Grade2", "Stock1", "Stock2" }.Any(path => ((RectTransform)supply.Find(path)).rect.width != 144))
                throw new InvalidOperationException("军需档位按钮或库存标签没有按三列尺寸排布");
            Press(preparation, "Supply/Grade1");
            preparation = RequirePanel("BattlePreparation");
            CaptureViewports("ui-bandit-hideout-preparation");
            Press(preparation, "Continue");
            var battle = RequireBattleDraft();
            Press(BattleSafeArea(battle), "DraftPreparationControls/Auto");
            CaptureViewports("ui-bandit-hideout-deployment");
            Capture("ui-bandit-hideout-deployment-wide", 1680, 720);
            VerifyBattleMenu(battle, draft);
            if (controller.State.Campaign.HasActiveBattle || !BaseSceneRules.Entrance(controller.State))
                throw new InvalidOperationException("窝点部署提前切图或写入战斗");
            Press(BattleSafeArea(battle), "DraftPreparationControls/Back");
            if (!ReferenceEquals(draft, Field(H, "battleDraft"))) throw new InvalidOperationException("返回备战重抽了窝点");
            Press(RequirePanel("BattlePreparation"), "Continue");
            battle = RequireBattleDraft();
            Press(BattleSafeArea(battle), "DraftPreparationControls/Start");
            if (H.DialogueOpen || battle.Realtime == null || !BaseBanditHideout.InHideout(controller.State) ||
                battle.Realtime.Session.Opening.Enemies.Count != draft.KnownEnemyCount() ||
                battle.Realtime.Battle.Soldiers.Any(unit => unit.Side == BattleSide.B && unit.Kind == SoldierKind.Hero) ||
                battle.Realtime.RuinsExposureFraction != 0)
                throw new InvalidOperationException("窝点开战存在剧情、侵蚀或阵容偏差");
            CaptureViewports("ui-bandit-hideout-battle");
            VerifyBattleMenu(battle, null);
            if (!battle.WithdrawSceneEncounter() || !BaseSceneRules.Entrance(controller.State))
                throw new InvalidOperationException("窝点撤退没有回到入口");
            Log.Add("[PASS] 浓雾隐藏窝点，绕墙后发现，薄雾保留贴图与名称；预览阵容与实战一致，撤退回入口");
        }

        private static void VerifyRuinsSoloRetreat()
        {
            Approach("demo_ruins_investigate");
            H.OpenObject("demo_ruins_investigate");
            var prep = RequirePanel("BattlePreparation");
            if (((BaseBattlePreparation)Field(H, "battleDraft")).RequiredSupply(false) > 0)
                Press(prep, "Supply/Grade1");
            Press(RequirePanel("BattlePreparation"), "Continue");
            var battle = RequireBattleDraft();
            VerifyDeploymentPointer(battle);
            Press(BattleSafeArea(battle), "DraftPreparationControls/Start");
            FinishDialogue();
            Click("准备交战");
            CaptureViewports("ui-skin-ruins-combat", mobile: true);
            var player = battle.Realtime.Model.Player;
            player.Health = player.MaxHealth * .5;
            var retreat = controller.GetType().GetMethod("TickRuinsRetreatGuide", BindingFlags.Instance | BindingFlags.NonPublic);
            if (!(bool)retreat.Invoke(controller, null) || controller.State.Campaign.HasActiveBattle || !BaseSceneRules.Entrance(controller.State))
                throw new InvalidOperationException("主角半血没有真实撤退并回到遗迹入口");
            var dialogue = RequirePanel("NpcDialogue");
            H.ContinueDialogue();
            if (dialogue.Find("DialogueText").GetComponent<Text>().text != "对方人多势众，优势不在我，或许镇上那位年轻铁匠可以帮忙。" ||
                new[] { "DialogueHint", "AdvanceButton", "Replies" }.Any(path => dialogue.Find(path).gameObject.activeInHierarchy))
                throw new InvalidOperationException("撤退独白仍有额外提示或选项");
            Capture("ui-skin-ruins-solo-retreat");
            H.ContinueDialogue();
            if (H.PanelOpen) throw new InvalidOperationException("独白收起后又显示了其他面板");
            Log.Add("[PASS] 古窟单人半血真实撤退，只有主角独白，收起后回探索");
        }

        private static void VerifyRuinsConfrontationCopy()
        {
            int battleStarts = 0;
            void Open() => H.OpenRuinsConfrontation(() => battleStarts++);
            Open();
            FinishDialogue();
            Click("追问他们为何不能动");
            H.ContinueDialogue();
            var view = RequirePanel("NpcDialogue");
            if (!view.Find("DialogueText").GetComponent<Text>().text.Contains("手脚都动不了") || battleStarts != 0)
                throw new InvalidOperationException("古窟追问未切入主角的追问页");
            FinishDialogue();
            if (!view.Find("DialogueText").GetComponent<Text>().text.Contains("他们就得拦你") ||
                view.Find("Replies").GetComponentsInChildren<Button>().Count(button => button.interactable) != 2 ||
                battleStarts != 0)
                throw new InvalidOperationException("古窟追问没有回到开战或暂缓");
            Click("暂不交战");
            if (H.DialogueOpen || battleStarts != 0)
                throw new InvalidOperationException("暂缓古窟没有返回原处");
            Open();
            FinishDialogue();
            Click("追问他们为何不能动");
            FinishDialogue();
            Click("准备交战");
            if (battleStarts != 1) throw new InvalidOperationException("追问支线没有调用原战斗入口");
            H.ClosePanel();
            Log.Add("[PASS] 古窟对峙追问两页，播完可暂缓或从原入口开战");
        }

        private static void ApproachMerchantAwayFromStall()
        {
            if (!controller.Change(state =>
            {
                var objects = BaseSpatial.Objects(state);
                var merchant = objects.Single(item => item.Id == "town_peddler");
                var stall = objects.Single(item => item.Id == "town_market");
                var board = BaseBattlefield.Board(state);
                var approach = Enumerable.Range(0, 16).Select(i =>
                {
                    double angle = Math.PI * i / 8;
                    return new BasePoint(merchant.X + 1.1 * Math.Cos(angle), merchant.Y + 1.1 * Math.Sin(angle));
                }).First(point => BaseSpatial.Walkable(state, point.X, point.Y) &&
                    BaseInteraction.WithinReach(board, merchant, point.X, point.Y) &&
                    !BaseInteraction.WithinReach(board, stall, point.X, point.Y));
                state.PlayerX = approach.X;
                state.PlayerY = approach.Y;
                BaseInteraction.Walk(new BasePoint(merchant.X - approach.X, merchant.Y - approach.Y));
                BaseAdventure.Arrive(state);
            }, "")) throw new InvalidOperationException(controller.Message);
        }

        private static void VerifyMerchantCommonGoods()
        {
            var opening = controller.State;
            if (opening.Demo.AcceptedDay != 0) throw new InvalidOperationException("商店回归必须在接取领主委托前完成");
            foreach (string id in new[] { "food", "materials", "supply1", "supply2", BaseWine.Strong, BaseWine.Sake, BaseEquipmentCrafting.Stone })
            {
                var state = controller.State;
                bool stone = id == BaseEquipmentCrafting.Stone, wine = id == BaseWine.Strong || id == BaseWine.Sake;
                string name = wine || stone ? state.Backpack.Catalog.Single(item => item.Id == id).Name : BaseTown.Name(id);
                int price = stone ? BaseEquipmentCrafting.BuyPrice(state) : wine ? BaseProgressSupplies.Price(state, id) : BaseTown.TradePrice(state, id, true);
                var shop = RequirePanel("TownTrade");
                var offer = shop.Find("OfferList").GetComponentsInChildren<Button>()
                    .Single(button => button.GetComponentInChildren<Text>().text.StartsWith(name + " · ", StringComparison.Ordinal));
                if (offer.GetComponentInChildren<Text>().text != name + " · " + price + "币")
                    throw new InvalidOperationException(name + "的货架行应只显示名称及单价");
                offer.onClick.Invoke();
                if (RequirePanel("TownTrade") != shop || !offer.gameObject.activeInHierarchy)
                    throw new InvalidOperationException("切换货架商品时重建了商店或货架行");
                var details = shop.Find("Details");
                var label = details.GetComponentsInChildren<Text>().Single(text => text.text.StartsWith(name + " · 单价 ", StringComparison.Ordinal));
                Button Control(string text) => details.GetComponentsInChildren<Button>()
                    .Single(button => button.GetComponentInChildren<Text>().text == text);
                if (!label.text.Contains(price + "币") || !Control($"确认买入 · {price}币").interactable)
                    throw new InvalidOperationException(name + "的单价或买入状态与报价不同");
                var plus = Control("＋");
                var minus = Control("−");
                Control("最多");
                if (id == "food")
                {
                    plus.onClick.Invoke();
                    plus.onClick.Invoke();
                    if (RequirePanel("TownTrade") != shop || !Control($"确认买入 · {3 * price}币").interactable)
                        throw new InvalidOperationException("连续加量没有在同一面板更新报价");
                    minus.onClick.Invoke();
                    minus.onClick.Invoke();
                    Click($"确认买入 · {price}币");
                    if (controller.State.Inventory.Food != state.Inventory.Food + 1 || controller.State.Inventory.Coins != state.Inventory.Coins - price)
                        throw new InvalidOperationException("接委托前未能在老莫处购买粮草");
                }
                if (wine)
                {
                    Click($"确认买入 · {price}币");
                    if (BaseBackpack.Count(controller.State, id) != BaseBackpack.Count(state, id) + 1 ||
                        controller.State.Inventory.Coins != state.Inventory.Coins - price)
                        throw new InvalidOperationException("接委托前未能在老莫处购买" + name);
                }
                if (id == "supply2" && state.Town.Goods.Single(good => good.Id == id).Remaining == 1)
                {
                    var originalFill = offer.targetGraphic.color;
                    Click($"确认买入 · {price}币");
                    var soldOut = RequirePanel("TownTrade").Find("OfferList").GetComponentsInChildren<Button>()
                        .Single(button => button.GetComponentInChildren<Text>().text == name + " · " + price + "币");
                    soldOut.onClick.Invoke();
                    details = RequirePanel("TownTrade").Find("Details");
                    if (!soldOut.interactable || soldOut.targetGraphic.color.grayscale >= originalFill.grayscale ||
                        !details.GetComponentsInChildren<Text>().Any(text => text.text.StartsWith("今日剩余 0/", StringComparison.Ordinal)) ||
                        Control("确认买入 · 0币").interactable)
                        throw new InvalidOperationException("售罄项未变暗，或无法查看已售罄详情");
                    Capture("ui-skin-merchant-sold-out");
                }
            }
            Log.Add("[PASS] 接领主委托前在老莫本人旁打开商店，物资、锻造石与两种酒使用共用单价和买入控件，连续加量原位更新");
        }

        private static void BuySelectedForgeStone()
        {
            var before = controller.State;
            int price = BaseEquipmentCrafting.BuyPrice(before);
            int available = BasePartyInventory.AvailableMaterial(before, BaseEquipmentCrafting.Stone);
            Click("确认买入 · " + price + "币");
            var after = controller.State;
            if (after.Inventory.Coins != before.Inventory.Coins - price ||
                after.PersonalEquipment.StonesRemaining != before.PersonalEquipment.StonesRemaining - 1 ||
                BasePartyInventory.AvailableMaterial(after, BaseEquipmentCrafting.Stone) != available + 1)
                throw new InvalidOperationException("老莫购买一颗锻造石未正确扣款、减库存并发货");
            Log.Add("[PASS] 老莫货架购买一颗锻造石，扣款 " + price + " 币，随身锻造石 " + available + " → " + (available + 1));
        }

        private static void VerifyTownReforge()
        {
            Approach("town_blacksmith");
            H.OpenObject("town_blacksmith");
            FinishDialogue();
            Click(BaseConquest.Story.StoryText.Get("story.ui.trade.smith_goods_entry", "看看商品"));
            var equipment = RequirePanel("TownTrade");
            string weaponName = BasePersonalEquipment.Definition("basic_sword").Name;
            equipment.Find("OfferList").GetComponentsInChildren<Button>().Single(button =>
                button.GetComponentInChildren<Text>().text.StartsWith(weaponName + " · ", StringComparison.Ordinal)).onClick.Invoke();
            var weaponDetails = equipment.Find("Details").GetComponentsInChildren<Text>();
            if (!weaponDetails.Any(label => label.text.Contains("攻击距离")) || weaponDetails.Any(label => label.text.Contains("换装")))
                throw new InvalidOperationException("装备商品详情应显示攻击距离和自身属性");
            Capture("ui-skin-smith-weapon-details");
            H.ClosePanel();
            H.OpenObject("town_blacksmith");
            FinishDialogue();
            Click(BaseConquest.Story.StoryText.Get("story.ui.trade.reforge_entry", "重铸"));
            var opening = controller.State;
            var basic = BasePartyInventory.Entries(opening).Single(item => item.Holder == opening.PlayerLeaderId && item.Slot == "weapon");
            int openingStones = BasePartyInventory.AvailableMaterial(opening, BaseEquipmentCrafting.Stone);
            Click(basic.Name + " · 已穿戴");
            var openingDetails = RequirePanel("TownTrade").Find("Details");
            if (openingDetails.GetComponentsInChildren<Button>().Single(button => button.GetComponentInChildren<Text>().text == "确认重铸").interactable ||
                !openingDetails.GetComponentsInChildren<Text>().Any(label => label.text.Contains("新手短剑")) ||
                BasePartyInventory.AvailableMaterial(controller.State, BaseEquipmentCrafting.Stone) != openingStones)
                throw new InvalidOperationException("开局短剑未显示重铸引导或仍允许消耗锻造石");
            Capture("ui-skin-town-reforge-basic");
            H.ClosePanel();
            if (!controller.Change(next =>
                {
                    const string template = "novice_sword";
                    string id = "personal:" + next.PersonalEquipment.NextId;
                    bool stockUnlocked = next.Demo.Flow.NoviceShop;
                    next.Demo.Flow.NoviceShop = true;
                    if (!BasePersonalEquipment.Buy(next, template, next.PlayerLeaderId, BasePersonalEquipment.BuyPrice(next, template)) ||
                        !BasePersonalEquipment.Equip(next, next.PlayerLeaderId, id, "weapon"))
                        throw new InvalidOperationException("离屏重铸样本无法购买并穿戴新手短剑");
                    next.Demo.Flow.NoviceShop = stockUnlocked;
                }, "离屏重铸样本：穿戴新手短剑"))
                throw new InvalidOperationException(controller.Message);
            H.OpenObject("town_blacksmith");
            FinishDialogue();
            Click(BaseConquest.Story.StoryText.Get("story.ui.trade.reforge_entry", "重铸"));
            var before = controller.State;
            var weapon = BasePartyInventory.Entries(before).Single(item =>
                item.Holder == before.PlayerLeaderId && item.Slot == "weapon" && BaseEquipmentCrafting.CanSelectReforge(before, item));
            var quote = BaseEquipmentCrafting.Quote(before, weapon.Id, 1);
            if (quote == null) throw new InvalidOperationException("巴隆无法为穿戴的新手短剑报价");
            Click(weapon.Name + " · 已穿戴");
            var details = RequirePanel("TownTrade").Find("Details");
            string probabilities = "升档 " + (quote.Up / 100f).ToString("0.##") + "% ／ 留级 " +
                (quote.Stay / 100f).ToString("0.##") + "% ／ 降档 " + (quote.Down / 100f).ToString("0.##") + "%";
            if (!details.GetComponentsInChildren<Text>().Any(label => label.text == probabilities))
                throw new InvalidOperationException("巴隆重铸页显示的升档／留级／降档概率与报价不符");
            var upgraded = before.Copy();
            BasePartyInventory.Find(upgraded, weapon.Id).Gear.Quality++;
            float previousAttack = BasePersonalEquipment.Stats(before.Leaders.Single(person => person.Id == before.PlayerLeaderId)).Damage;
            float upgradedAttack = BasePersonalEquipment.Stats(upgraded.Leaders.Single(person => person.Id == before.PlayerLeaderId)).Damage;
            string preview = "攻击 " + previousAttack.ToString("0.####") + " → " + upgradedAttack.ToString("0.####");
            if (upgradedAttack <= previousAttack || !details.GetComponentsInChildren<Text>().Any(label => label.text.Contains("升档后") && label.text.Contains(preview)))
                throw new InvalidOperationException("重铸确认前未显示新手短剑的实际升档收益");
            Capture("ui-skin-town-reforge-odds");
            int available = BasePartyInventory.AvailableMaterial(before, BaseEquipmentCrafting.Stone);
            Click("确认重铸");
            var after = controller.State;
            var result = BasePartyInventory.Find(after, weapon.Id);
            if (result == null || after.PersonalEquipment.ReforgeRolls != before.PersonalEquipment.ReforgeRolls + 1 ||
                BasePartyInventory.AvailableMaterial(after, BaseEquipmentCrafting.Stone) != available - quote.Stones ||
                Math.Abs(result.Quality - quote.Quality) > 1)
                throw new InvalidOperationException("确认重铸后未正确扣除一颗锻造石并结算原武器品质");
            string outcome = BasePersonalEquipment.Qualities[quote.Quality] + " → " + BasePersonalEquipment.Qualities[result.Quality];
            float actualAttack = BasePersonalEquipment.Stats(after.Leaders.Single(person => person.Id == after.PlayerLeaderId)).Damage;
            string changes = previousAttack == actualAttack ? "属性未变" : "攻击 " + previousAttack.ToString("0.####") + " → " + actualAttack.ToString("0.####");
            var trade = RequirePanel("TownTrade");
            var confirm = trade.Find("Details").GetComponentsInChildren<Button>().Single(button => button.GetComponentInChildren<Text>().text == "确认重铸");
            var feedback = confirm.transform.parent.GetChild(confirm.transform.GetSiblingIndex() + 1).GetComponent<Text>();
            if (feedback == null || !feedback.text.Contains(outcome) || !feedback.text.Contains(changes) || trade.Find("SellerSpeech").GetComponent<Text>().text.Contains(outcome))
                throw new InvalidOperationException("重铸结果与实际属性变化应在确认按钮下方显示");
            Capture("ui-skin-town-reforge-result");
            Log.Add("[PASS] 巴隆重铸 " + weapon.Name + "，" + probabilities + "，结果 " + outcome + "，消耗 " + quote.Stones + " 颗");
            H.ClosePanel();
        }

        private static void VerifyAzhuDuelDialogue()
        {
            Approach("demo_azhu");
            var tickInvitation = controller.GetType().GetMethod("TickAzhuInvitation", BindingFlags.Instance | BindingFlags.NonPublic);
            tickInvitation.Invoke(controller, null);
            FinishDialogue();
            Capture("ui-skin-azhu-invitation");
            Click("眼下有要事，改日再比");
            FinishDialogue();
            tickInvitation.Invoke(controller, null);
            if (H.DialogueOpen || Field(controller, "combatController") != null || !controller.State.Demo.AzhuInvitationSeen)
                throw new InvalidOperationException("拒绝阿铸叫停邀约后重复弹出对白或开启比武");
            Log.Add("[PASS] 靠近阿铸触发一次性叫停对白，拒绝后可继续探索");
            H.OpenObject("demo_azhu");
            var replies = RequirePanel("NpcDialogue").Find("Replies");
            var buttons = replies.GetComponentsInChildren<Button>();
            if (buttons.Length != 2 || buttons.Any(button => button.GetComponentInChildren<Text>().text != "..."))
                throw new InvalidOperationException("未解锁的比武选项生成了占位");
            FinishDialogue();
            var player = controller.State.Leaders.Single(l => l.Id == controller.State.PlayerLeaderId);
            double health = player.Health;
            Click("原地切磋");
            var duel = (BattleSceneController)Field(controller, "combatController");
            if (duel?.Realtime == null || !duel.IsAzhuDuel)
                throw new InvalidOperationException("原地比武未开战");
            duel.ResumeAfterDialogue(true);
            duel.AdvanceRealtime(.001);
            var footprints = duel.GetComponentsInChildren<Renderer>().Where(renderer => renderer.name == "GroundOccupation" && renderer.enabled).ToArray();
            var ink = new MaterialPropertyBlock();
            Color FootprintColor(Renderer renderer) { renderer.GetPropertyBlock(ink); return ink.GetColor("_BaseColor"); }
            if (!footprints.Any(renderer => FootprintColor(renderer).b > .8f) ||
                !footprints.Any(renderer => FootprintColor(renderer).r > .8f && Mathf.Abs(renderer.transform.localScale.x - renderer.transform.localScale.z) > .1f))
                throw new InvalidOperationException("战斗占用未显示我方蓝色与敌方骑兵红色占用");
            var debuffRing = duel.GetComponentsInChildren<LineRenderer>(true).First(ring => ring.name == "DebuffPurpleAura");
            if (Mathf.Abs((debuffRing.GetPosition(0).z + debuffRing.GetPosition(16).z) * .5f) > .01f)
                throw new InvalidOperationException("战斗状态圈没有对齐单位落脚点");
            Capture("ui-battle-occupation");
            var opponent = duel.Realtime.Model.Units.Single(u => u.Side == PrototypeSide.Enemy);
            opponent.Health = opponent.MaxHealth * .5;
            duel.AdvanceRealtime(.04);
            var finish = duel.GetType().GetMethod("ContinueFinishedSceneDialogue", BindingFlags.Instance | BindingFlags.NonPublic);
            if (!(bool)finish.Invoke(duel, null) || Field(controller, "combatController") != null ||
                controller.State.Leaders.Single(l => l.Id == controller.State.PlayerLeaderId).Health != health ||
                !controller.State.Demo.Flow.AzhuDuelWon)
                throw new InvalidOperationException("比武胜利没有恢复状态并接入阿铸对白");
            FinishDialogue();
            Capture("ui-skin-azhu-duel-dialogue");
            Click("暂缓邀请");
            Log.Add("[PASS] 隐藏未解锁比武选项，原地比武胜利恢复状态并直进阿铸对白");
            Approach("demo_azhu");
            H.OpenObject("demo_azhu");
            FinishDialogue();
            Click("欢迎加入");
            FinishDialogue();
            var receipt = RequirePanel("CommonPanel");
            var receiptLabels = receipt.GetComponentsInChildren<Text>();
            var shortSword = receiptLabels.FirstOrDefault(label => label.text.Contains("新手短剑"));
            var destination = receiptLabels.FirstOrDefault(label => label.text.EndsWith("的背包", StringComparison.Ordinal));
            if ((string)Field(H, "panelViewKey") != "reward-receipt" ||
                shortSword == null || destination == null || destination.alignment != TextAnchor.MiddleRight ||
                destination.rectTransform.anchoredPosition.x <= shortSword.rectTransform.anchoredPosition.x ||
                !receiptLabels.Any(label => label.text.Contains("锻造石")) ||
                receipt.GetComponentsInChildren<Button>().Any(button => button.interactable && button.GetComponentInChildren<Text>()?.text.Contains("领取") == true))
                throw new InvalidOperationException("老铁匠的赠礼没有自动入包并显示简明奖励小窗");
            CaptureViewports("ui-skin-reward-receipt", mobile: true);
            H.ClosePanel();
            Log.Add("[PASS] 阿铸入队后完成赠礼对白，奖励自动入包并在小窗显示");
            var partner = controller.PresentedObject("partner_craft");
            if (partner == null) throw new InvalidOperationException("入队后找不到当前随行的阿铸");
            var fromPartner = new BasePoint(controller.State.PlayerX - partner.X, controller.State.PlayerY - partner.Y);
            BaseInteraction.Walk(fromPartner);
            if (BaseInteraction.CanReach(controller.State, partner))
                throw new InvalidOperationException("背后的阿铸仍在可交互范围");
            BaseInteraction.Walk(new BasePoint(-fromPartner.X, -fromPartner.Y));
            H.OpenObject("partner_craft");
            FinishDialogue();
            var partnerChoices = RequirePanel("NpcDialogue").Find("Replies").GetComponentsInChildren<Button>()
                .Select(button => button.GetComponentInChildren<Text>().text).ToArray();
            if (!partnerChoices.Contains("聊天对话") ||
                partnerChoices.Any(text => text is "原地比武" or "任职安排" or "查看角色详情" or "留在营地" || text.Contains("带本队士兵")))
                throw new InvalidOperationException("灰岩镇伙伴对话仍有旧功能或营地专用选项");
            H.ClosePanel();
            Log.Add("[PASS] 阿铸初次切磋可招募，入队后对话精简，镇内不显示留营操作");
        }

        private static void VerifyRuinsWarning()
        {
            Approach("demo_ruins_investigate");
            H.OpenObject("demo_ruins_investigate");
            var preparation = RequirePanel("BattlePreparation");
            var draft = (BaseBattlePreparation)Field(H, "battleDraft");
            if (draft.RequiredSupply(false) == 0)
            {
                Press(preparation.Find("Teams").GetChild(0), "Melee/Plus");
                preparation = RequirePanel("BattlePreparation");
            }
            Press(preparation, "Supply/Grade1");
            Press(RequirePanel("BattlePreparation"), "Continue");
            var deployed = RequireBattleDraft();
            var field = BattleSafeArea(deployed);
            Press(field, "DraftPreparationControls/Auto");
            if (!field.Find("BottomInformation").GetComponentsInChildren<Text>().Any(label =>
                label.text.Contains("普通参战成员") && label.text.Contains("每秒")))
                throw new InvalidOperationException("普通士兵实际落位后，部署内未显示侵蚀影响");
            Capture("ui-skin-ruins-vulnerable-warning");
            Press(field, "DraftPreparationControls/Start");
            FinishDialogue();
            Click("暂不交战");
            if (H.PanelOpen || RequireBattleDraft() != deployed || controller.State.Campaign.HasActiveBattle)
                throw new InvalidOperationException("拒绝交战后未保留当前部署");
            Press(field, "DraftPreparationControls/Back");
            RequirePanel("BattlePreparation");
            if (BaseAchievements.Record(controller.State, BaseAchievements.Caution) == null)
                throw new InvalidOperationException("从受侵蚀阵容返回备战未记入谨慎为上");
            H.ClosePanel();
        }

        private static PartyInventoryCell InventoryCell(Transform view, string id) =>
            view.GetComponentsInChildren<PartyInventoryCell>().Single(cell => cell.ItemId == id);

        private static void VerifyBackpackEquip()
        {
            var view = RequirePanel("PartyInventory");
            var gear = BasePartyInventory.Entries(controller.State).First(entry =>
                entry.Holder == controller.State.PlayerLeaderId && entry.Slot == "weapon");
            ClickHudButton(InventoryCell(view, gear.Id).transform);
            view = RequirePanel("PartyInventory");
            Capture("ui-skin-backpack-equipment-popup");
            Press(view.Find("InventoryItemMenu"), "Primary");
            if (BasePartyInventory.Find(controller.State, gear.Id).Worn) throw new InvalidOperationException("背包贴格小窗无法卸下装备");
            view = RequirePanel("PartyInventory");
            Press(view.Find("InventoryItemMenu"), "Primary");
            if (!BasePartyInventory.Find(controller.State, gear.Id).Worn) throw new InvalidOperationException("背包贴格小窗无法穿戴装备");
            view = RequirePanel("PartyInventory");
            var card = view.Find("Cards").GetChild(0);
            Press(card, "Sort");
            if (!BasePartyInventory.Find(controller.State, gear.Id).Worn) throw new InvalidOperationException("整理背包改变了已装备物品");
            Log.Add("[PASS] 独立背包的贴格装备详情、卸下、穿戴及整理");
        }

        private static void VerifyBackpackTransfer()
        {
            string player = controller.State.PlayerLeaderId;
            var person = BasePartyInventory.People(controller.State, false).First(member =>
                member.Id != player && BasePartyInventory.FreeCell(controller.State, member.Id) >= 0);
            string item = "";
            if (!controller.Change(state =>
            {
                item = "bag:" + state.Backpack.NextId++;
                state.Backpack.Items.Add(new BaseBagItem { Id = item, ItemId = BaseEquipmentCrafting.Stone,
                    Count = 1, Holder = player, Cell = BasePartyInventory.FreeCell(state, player) });
                state.Backpack.Revision++;
            }, "界面检查")) throw new InvalidOperationException(controller.Message);
            H.OpenBackpack();
            var view = RequirePanel("PartyInventory");
            var card = view.Find("Cards/人物与背包_" + person.Id);
            var locked = card.Find("EquipmentSlot_5").GetComponent<Button>();
            if (BasePersonalEquipment.SlotsFor(person).Count() < 6 && locked.interactable)
                throw new InvalidOperationException("未解锁的装备槽仍可点击");
            ClickHudButton(InventoryCell(view, item).transform);
            Press(RequirePanel("PartyInventory").Find("InventoryItemMenu"), "Transfer");
            Click("转交给 " + person.Name);
            if (BasePartyInventory.Find(controller.State, item).Holder != person.Id)
                throw new InvalidOperationException("独立背包转交未进入指定队友的背包");
            Log.Add("[PASS] 三人背包的锁定装备槽及贴格跨人转交");
        }

        private static Transform RequireInventoryPopup(Transform view, Transform cell, bool onLeft)
        {
            var popup = view.Find("InventoryItemMenu");
            if (popup == null) throw new InvalidOperationException("点选物品后未打开共用贴格小窗");
            var bounds = ((RectTransform)view).rect;
            var anchor = SceneRect(cell, view);
            var box = SceneRect(popup, view);
            if ((onLeft ? Mathf.Abs(anchor.xMin - box.xMax - 8) : Mathf.Abs(box.xMin - anchor.xMax - 8)) > .1f ||
                box.xMin < bounds.xMin - .1f || box.xMax > bounds.xMax + .1f ||
                box.yMin < bounds.yMin - .1f || box.yMax > bounds.yMax + .1f)
                throw new InvalidOperationException("物品详情越过面板边缘或与选中的物品格分离");
            return popup;
        }

        private static void VerifyWarehouseUi()
        {
            if (!controller.Change(state =>
            {
                if (!BaseBackpack.Add(state, BaseEquipmentCrafting.Stone, 3)) throw new InvalidOperationException("检查用锻造石发放失败");
            }, "界面检查")) throw new InvalidOperationException(controller.Message);
            H.OpenWarehouse();
            var view = RequirePanel("PartyWarehouse");
            Canvas.ForceUpdateCanvases();
            var grid = (RectTransform)view.Find("Grid");
            var card = view.Find("Card").GetChild(0);
            var gridBounds = SceneRect(grid, view);
            var cardBounds = SceneRect(card, view);
            if (view.Find("Details") != null || grid.childCount != 24 ||
                gridBounds.xMax >= cardBounds.xMin || gridBounds.height < cardBounds.height * .9f ||
                gridBounds.width < ((RectTransform)view).rect.width * .65f ||
                card.GetComponentsInChildren<PartyInventoryCell>().Count(cell => cell.Cell >= 0) != BasePartyInventory.Capacity)
                throw new InvalidOperationException("仓库左格右包布局或格子容量错误");
            CaptureViewports("ui-skin-warehouse");

            var stored = BasePartyInventory.Entries(controller.State).First(entry =>
                entry.Holder == "" && entry.Stack?.ItemId == BaseEquipmentCrafting.Stone && entry.Count >= 3);
            string player = controller.State.PlayerLeaderId;
            var before = BasePartyInventory.Entries(controller.State).Select(entry => entry.Id).ToHashSet();
            ClickHudButton(InventoryCell(view, stored.Id).transform);
            if (RequirePanel("PartyWarehouse") != view) throw new InvalidOperationException("点物品时重建了仓库");
            var popup = RequireInventoryPopup(view, InventoryCell(view, stored.Id).transform, false);
            CaptureViewports("ui-skin-warehouse-take-popup");
            Press(popup, "QuantityDown");
            if (RequirePanel("PartyWarehouse") != view || RequireInventoryPopup(view, InventoryCell(view, stored.Id).transform, false) != popup)
                throw new InvalidOperationException("减少取出数量时重建了仓库或详情小窗");
            Press(popup, "Store");
            var withdrawn = BasePartyInventory.Entries(controller.State).Single(entry =>
                !before.Contains(entry.Id) && entry.Holder == player && entry.Stack?.ItemId == BaseEquipmentCrafting.Stone);
            if (withdrawn.Count != stored.Count - 1 || BasePartyInventory.Find(controller.State, stored.Id).Count != 1)
                throw new InvalidOperationException("仓库贴格取出没有应用选择的数量");

            view = RequirePanel("PartyWarehouse");
            ClickHudButton(InventoryCell(view, withdrawn.Id).transform);
            view = RequirePanel("PartyWarehouse");
            popup = RequireInventoryPopup(view, InventoryCell(view, withdrawn.Id).transform, true);
            Capture("ui-skin-warehouse-store-popup");
            Press(popup, "Close");
            if (RequirePanel("PartyWarehouse") != view || view.Find("InventoryItemMenu") != null)
                throw new InvalidOperationException("关闭物品小窗时未保留现有仓库");
            ClickHudButton(InventoryCell(view, withdrawn.Id).transform);
            ClickHudButton(InventoryCell(RequirePanel("PartyWarehouse"), withdrawn.Id).transform);
            if (BasePartyInventory.Find(controller.State, withdrawn.Id).Holder != "")
                throw new InvalidOperationException("仓库双击快速存入失败");

            view = RequirePanel("PartyWarehouse");
            var source = InventoryCell(view, withdrawn.Id);
            var destination = view.Find("Card").GetComponentsInChildren<PartyInventoryCell>()
                .First(cell => cell.Cell >= 0 && string.IsNullOrEmpty(cell.ItemId));
            var pointer = new PointerEventData(EventSystem.current) { position = RectTransformUtility.WorldToScreenPoint(null, source.transform.position) };
            source.OnBeginDrag(pointer);
            destination.OnDrop(pointer);
            source.OnEndDrag(pointer);
            if (BasePartyInventory.Find(controller.State, withdrawn.Id).Holder != player)
                throw new InvalidOperationException("从仓库拖到角色背包失败");
            view = RequirePanel("PartyWarehouse");
            if (view.Find("InventoryItemMenu") == null) ClickHudButton(InventoryCell(view, withdrawn.Id).transform);
            view = RequirePanel("PartyWarehouse");
            Press(RequireInventoryPopup(view, InventoryCell(view, withdrawn.Id).transform, true), "Store");
            if (BasePartyInventory.Find(controller.State, withdrawn.Id).Holder != "")
                throw new InvalidOperationException("仓库贴格存入失败");
            H.ClosePanel();
            H.OpenWarehouse();
            if (RequirePanel("PartyWarehouse").Find("InventoryItemMenu") != null)
                throw new InvalidOperationException("重新打开仓库时残留物品详情");
            Log.Add("[PASS] 仓库 24 格铺满左侧、右侧共用背包，贴格双向避让、数量、存取、快取、拖放和重开");
        }

        private static void VerifyRosterRow(Transform row, bool infoVisible)
        {
            var source = (RectTransform)controller.transform.Find("UI 布局源/PersonnelRosterRow");
            var label = row.Find("Label")?.GetComponent<Text>();
            if (row.name != source.name || Mathf.Abs(((RectTransform)row).rect.height - source.rect.height) > .1f ||
                label == null || label.resizeTextForBestFit || !label.text.Contains("生命") ||
                row.Find("Info").gameObject.activeSelf != infoVisible)
                throw new InvalidOperationException("名册入口未正确使用场景共用士兵行：" + row.parent.name);
        }

        private static void VerifyTradeCardLayout(Transform view)
        {
            Canvas.ForceUpdateCanvases();
            var source = (RectTransform)controller.transform.Find("UI 布局源/PartyMemberCard");
            var right = view.Find("RightBody").GetComponent<ScrollRect>();
            var card = (RectTransform)right.content.GetChild(0);
            if (Vector2.Distance(card.rect.size, source.rect.size) > .1f ||
                card.GetComponentsInChildren<PartyInventoryCell>().Count(cell => cell.Cell >= 0) != BasePartyInventory.Capacity)
                throw new InvalidOperationException("交易右栏未保留共用背包卡的尺寸和格数");
            Log.Add("[PASS] 交易右栏使用与独立背包一致的角色卡尺寸与格数");
        }

        private static void CivilianStage(string key)
        {
            stage = key;
            stageAt = EditorApplication.timeSinceStartup;
        }

        private static void TickCivilianWork()
        {
            if (EditorApplication.timeSinceStartup - stageAt > 60)
                throw new InvalidOperationException("村民走查等待超时：" + (controller != null ? controller.Message : stage));
            controller = C;
            if (controller?.State == null || controller.Busy || Object.FindAnyObjectByType<SceneLoadingCover>() != null) return;

            var state = controller.State;
            var travel = (BaseWorkerTravel)Field(controller, "workerTravel");
            if (travel.Active.Count(j => j.AppearanceId == "villager") > 2)
                throw new InvalidOperationException("同一时刻出现了超过两名村民");
            if (stage.StartsWith("civilian-rapid-", StringComparison.Ordinal) &&
                travel.Active.Any(j => j.AppearanceId == "villager" && j != civilianCrossing))
                throw new InvalidOperationException("连续确认的中间方案产生了额外村民旅程");
            switch (stage)
            {
                case "civilian-boot":
                    controller.LoadDemoStage("camp", "male");
                    CivilianStage("civilian-farm-out");
                    break;
                case "civilian-farm-out":
                    if (state.Demo.Stage != BaseDemoStage.Waking || VisibleCivilianJourneys("farm").Length < 2) return;
                    H.ClosePanel();
                    CaptureViewports("ui-civilian-walk-to-farm");
                    var tent = BaseStageWalk.Door(state, "home");
                    Approach("home");
                    if (!controller.Change(s =>
                    {
                        if (!BaseDemo.AcknowledgeWaking(s)) throw new InvalidOperationException("没有完成灰岩营地苏醒");
                        s.PlayerX = tent.X;
                        s.PlayerY = tent.Y;
                        s.Inventory.Food = s.Inventory.Coins = s.Inventory.Materials = 0;
                        s.TodayFoodConsumed = 0;
                        s.TodayFoodMissing = s.TodayFoodRequired;
                    }, "村民生产走查准备")) throw new InvalidOperationException(controller.Message);
                    VerifyCivilianRoster("farm", 2, "ui-civilian-farm-roster");
                    Approach("home");
                    civilianDayPreview = BaseEconomy.Preview(controller.State);
                    H.OpenObject("home");
                    var home = RequirePanel("FacilityPanel");
                    if (!home.Find("Right/List").GetComponent<ScrollRect>().content.GetComponentsInChildren<Text>()
                        .Any(t => t.text.Contains($"预计产粮 +{civilianDayPreview.FoodProduced} · 明日口粮 {civilianDayPreview.FoodRequired} · 扣粮后库存 {civilianDayPreview.NextFood}")) ||
                        civilianDayPreview.FoodProduced != (int)controller.State.Rules.FarmPerWorker ||
                        civilianDayPreview.NextFood <= 0 || controller.State.Workers.Count != 0)
                        throw new InvalidOperationException("零兵、零资源时的营帐预估未显示村民半工与净粮");
                    CaptureViewports("ui-civilian-before-rest");
                    Press(home.Find("Left/List").GetComponent<ScrollRect>().content, "Rest");
                    CivilianStage("civilian-rest");
                    break;
                case "civilian-rest":
                    if (state.Day != civilianDayPreview.Day + 1) return;
                    var report = state.LastReport;
                    if (!state.HasReport || report.FoodProduced != civilianDayPreview.FoodProduced ||
                        report.FoodRequired != civilianDayPreview.FoodRequired || state.Inventory.Food != civilianDayPreview.NextFood ||
                        state.Inventory.Coins != 0 || state.Inventory.Materials != 0 || state.Workers.Count != 0)
                        throw new InvalidOperationException("实际日结与营帐预估不符，或村民改变了军队和库存口径");
                    H.ClosePanel();
                    H.OpenReport();
                    if (!RequirePanel("CommonPanel").GetComponentsInChildren<Text>()
                        .Any(t => t.text.Contains($"产粮{report.FoodProduced} - 口粮{report.FoodConsumed} = {report.FoodAfter}")))
                        throw new InvalidOperationException("日结报告没有显示对应产粮与口粮");
                    CaptureViewports("ui-civilian-after-rest");
                    Log.Add("[PASS] 两名村民从营帐出工，零兵零库存时名册、营帐预估与实际日结一致");
                    controller.LoadDemoStage("defense", "male");
                    CivilianStage("civilian-perimeter");
                    break;
                case "civilian-perimeter":
                    if (state.Demo.Stage != BaseDemoStage.DefenseWarned || state.Campaign.Location != "perimeter") return;
                    H.ClosePanel();
                    Approach("demo_defense_return");
                    controller.TravelDemo("base");
                    CivilianStage("civilian-workshop-out");
                    break;
                case "civilian-workshop-out":
                    if (state.Campaign.Location != "base" || VisibleCivilianJourneys("workshop").Length < 2) return;
                    H.ClosePanel();
                    CaptureViewports("ui-civilian-walk-to-workshop");
                    VerifyCivilianRoster("farm", 0, "ui-civilian-farm-full");
                    VerifyCivilianRoster("workshop", 2, "ui-civilian-workshop-roster");
                    CivilianStage("civilian-split-posts");
                    break;
                case "civilian-split-posts":
                    if (!CivilianTravelIdle()) return;
                    ChangeCivilianFarmPost("Minus", 1);
                    VerifyCivilianRoster("farm", 1, "ui-civilian-split-farm");
                    VerifyCivilianRoster("workshop", 1, "ui-civilian-split-workshop");
                    CivilianStage("civilian-split-walk");
                    break;
                case "civilian-split-walk":
                    if (!TrackCivilianCrossing("farm", out bool reachedFarmLeg) || !reachedFarmLeg) return;
                    CivilianStage("civilian-replace");
                    break;
                case "civilian-replace":
                    if (!CivilianTravelIdle()) return;
                    civilianCrossing = null;
                    ChangeCivilianFarmPost("Plus", 0);
                    VerifyCivilianRoster("farm", 0, "ui-civilian-farm-replaced");
                    VerifyCivilianRoster("workshop", 2, null);
                    CivilianStage("civilian-return");
                    break;
                case "civilian-return":
                    if (!TrackCivilianCrossing("workshop", out _)) return;
                    ChangeCivilianFarmPost("Minus", 1);
                    frameDeadline = Time.frameCount + 3;
                    CivilianStage("civilian-rapid-restore");
                    break;
                case "civilian-rapid-restore":
                    if (Time.frameCount < frameDeadline) return;
                    ChangeCivilianFarmPost("Plus", 0);
                    CivilianStage("civilian-rapid-crossing");
                    break;
                case "civilian-rapid-crossing":
                    if (!TrackCivilianCrossing("workshop", out bool reachedWorkshopLeg) || !reachedWorkshopLeg) return;
                    CaptureViewports("ui-civilian-via-tent");
                    CivilianStage("civilian-rapid-arrival");
                    break;
                case "civilian-rapid-arrival":
                    if (!CivilianTravelIdle()) return;
                    frameDeadline = Time.frameCount + 20;
                    CivilianStage("civilian-rapid-settled");
                    break;
                case "civilian-rapid-settled":
                    if (Time.frameCount < frameDeadline) return;
                    if (!CivilianTravelIdle()) throw new InvalidOperationException("最终排岗不变仍追加了村民旅程");
                    Log.Add("[PASS] 确认士兵调工后村民先补粮田，田坊之间经营帐保持同一演员；连续调工只采纳最终岗位");
                    Finish(0, "OK");
                    break;
            }
        }

        private static bool CivilianTravelIdle()
        {
            var travel = (BaseWorkerTravel)Field(controller, "workerTravel");
            return travel.Active.Count == 0 && travel.Pending == 0;
        }

        private static (BaseWorkerJourney journey, Transform view)[] VisibleCivilianJourneys(string destination)
        {
            var travel = (BaseWorkerTravel)Field(controller, "workerTravel");
            var actors = (Dictionary<BaseWorkerJourney, Transform>)Field(controller, "workerTravelViews");
            var door = BaseStageWalk.Door(controller.State, destination);
            return travel.Active.Where(journey => journey.AppearanceId == "villager" &&
                    journey.Route.Last().Distance(door) < .08 && actors.ContainsKey(journey))
                .Select(journey => (journey, view: actors[journey]))
                .Where(actor => actor.view != null && actor.view.gameObject.activeInHierarchy &&
                    actor.view.GetComponent<CompanionSpriteView>()?.AppearanceId == "villager").ToArray();
        }

        private static bool TrackCivilianCrossing(string destination, out bool reachedSecondLeg)
        {
            reachedSecondLeg = false;
            var (journey, view) = VisibleCivilianJourneys(destination).SingleOrDefault();
            if (journey == null) return false;
            var tent = BaseStageWalk.Door(controller.State, "home");
            int node = journey.Route.FindIndex(point => point.Distance(tent) < .08);
            if (node < 0 || node == journey.Route.Count - 1)
                throw new InvalidOperationException("粮田与工坊切换的单条旅程应经过营帐");
            if (civilianCrossing != null && (civilianCrossing != journey || civilianCrossingView != view))
                throw new InvalidOperationException("村民换岗经过营帐时重建了旅程或演员");
            civilianCrossing = journey;
            civilianCrossingView = view;
            reachedSecondLeg = journey.Waypoint > node;
            return true;
        }

        private static Transform OpenCivilianFacility(string facility)
        {
            H.ClosePanel();
            Approach(facility);
            H.OpenObject(facility);
            return RequirePanel("FacilityPanel");
        }

        private static void VerifyCivilianRoster(string facility, int civilians, string screenshot)
        {
            var view = OpenCivilianFacility(facility);
            RequireCivilianRoster(view, civilians);
            if (screenshot != null) CaptureViewports(screenshot);
            H.ClosePanel();
        }

        private static void RequireCivilianRoster(Transform view, int civilians)
        {
            var list = view.Find("Right/List").GetComponent<ScrollRect>().content;
            var expected = civilians == 0 ? Array.Empty<string>() : new[] { $"村民 ×{civilians} · 补位" };
            if (!list.GetComponentsInChildren<Text>().Select(t => t.text).Where(t => t.StartsWith("村民 ×", StringComparison.Ordinal)).SequenceEqual(expected))
                throw new InvalidOperationException("实际设施名册的村民人数与预期不符");
        }

        private static void ChangeCivilianFarmPost(string adjustment, int expected)
        {
            var farm = OpenCivilianFacility("farm");
            var actions = farm.Find("Left/List").GetComponent<ScrollRect>().content;
            int old = BaseEconomy.CivilianWorkersAt(controller.State, "farm");
            Press(actions, "Melee/" + adjustment);
            farm = RequirePanel("FacilityPanel");
            RequireCivilianRoster(farm, expected);
            if (BaseEconomy.CivilianWorkersAt(controller.State, "farm") != old)
                throw new InvalidOperationException("调工草案未确认就变更实际村民岗位");
            Press(farm.Find("Left/List").GetComponent<ScrollRect>().content, "FacilityActionRow/Apply");
            if (BaseEconomy.CivilianWorkersAt(controller.State, "farm") != expected ||
                BaseEconomy.CivilianWorkersAt(controller.State, "workshop") != 2 - expected)
                throw new InvalidOperationException("确认士兵调工后没有按粮田优先重排村民");
            H.ClosePanel();
        }

        private static void VerifyFacilityRosters()
        {
            H.ClosePanel();
            Approach("demo_defense_return");
            controller.TravelDemo("base");
            if (controller.State.Campaign.Location != "base") throw new InvalidOperationException(controller.Message);
            Approach("farm");
            controller.Interact();
            var view = RequirePanel("FacilityPanel");
            VerifyFacilityPortrait(view);
            var actions = view.Find("Left/List").GetComponent<ScrollRect>().content;
            var list = view.Find("Right/List").GetComponent<ScrollRect>();
            if (actions.GetComponentsInChildren<Button>().Any(button => button.name == "Unlock" || button.name == "ManagerLock"))
                throw new InvalidOperationException("正式粮田界面仍显示自动调工专用的岗位锁");
            var rows = list.content.Cast<RectTransform>().Where(node => node.name == "PersonnelRosterRow").ToArray();
            if (rows.Length == 0) throw new InvalidOperationException("粮田点名名册没有显示可调士兵");
            foreach (var row in rows) VerifyRosterRow(row, false);
            CaptureViewports("ui-skin-facility-farm");
            Canvas.ForceUpdateCanvases();
            float scrollTop = list.content.anchoredPosition.y;
            float actionTop = actions.anchoredPosition.y;
            list.verticalNormalizedPosition = 0;
            if (list.content.anchoredPosition.y <= scrollTop || actions.anchoredPosition.y != actionTop)
                throw new InvalidOperationException("粮田名册没有独立滚动");
            list.verticalNormalizedPosition = 1;
            int assigned = BaseEconomy.WorkersAt(controller.State, "farm");
            var farmAdjuster = actions.Find("Melee/Plus");
            ClickHudButton(rows.First(row => row.GetComponent<Button>().interactable));
            if (BaseEconomy.WorkersAt(controller.State, "farm") != assigned)
                throw new InvalidOperationException("点名调配未确认就写入了岗位");
            var draft = (BaseState)Field(H, "workerDraft");
            if (BaseEconomy.WorkersAt(draft, "farm") == assigned)
                throw new InvalidOperationException("点名没有更新设施草案");
            if (RequirePanel("FacilityPanel") != view || farmAdjuster == null ||
                actions.Find("Melee/Plus") != farmAdjuster || view.Find("Right/List").GetComponent<ScrollRect>() != list)
                throw new InvalidOperationException("粮田点名时重建了窗口或固定控件");
            Press(actions, "FacilityActionRow/Cancel");
            if (BaseEconomy.WorkersAt(controller.State, "farm") != assigned ||
                BaseEconomy.WorkersAt((BaseState)Field(H, "workerDraft"), "farm") != assigned)
                throw new InvalidOperationException("取消点名后岗位未恢复");
            if (view.Find("Right/Filters").gameObject.activeSelf)
                throw new InvalidOperationException("生产设施的右栏应只显示工人名册");
            var manage = view.Find("PortraitColumn/Manage").GetComponent<Button>();
            var managerOptions = view.Find("PortraitColumn/ManagerOptions");
            if (managerOptions != null && manage.gameObject.activeInHierarchy && manage.interactable)
            {
                ClickHudButton(manage.transform);
                if (!managerOptions.gameObject.activeInHierarchy ||
                    view.Find("Right/List").GetComponent<ScrollRect>() != list)
                    throw new InvalidOperationException("任免负责人应在左侧肖像栏操作，并保留工人名册");
            }
            H.ClosePanel();
            Approach("workshop");
            H.OpenObject("workshop");
            view = RequirePanel("FacilityPanel");
            VerifyFacilityPortrait(view);
            actions = view.Find("Left/List").GetComponent<ScrollRect>().content;
            var workshop = controller.State.Muster.Facilities.Single(f => f.FacilityId == "workshop");
            if (!actions.GetComponentsInChildren<Button>().Any(b => b.name == "Grade1") ||
                actions.GetComponentsInChildren<Button>().Any(b => b.name == "Grade2") != (workshop.MaxProductLevel >= 2))
                throw new InvalidOperationException("工坊生产档位没有按解锁显示");
            CaptureViewports("ui-skin-facility-workshop");
            H.ClosePanel();
            Approach("barracks");
            H.OpenObject("barracks");
            view = RequirePanel("FacilityPanel");
            VerifyFacilityPortrait(view);
            var rangeDraft = view.Find("Left").GetComponentsInChildren<InputField>().First();
            rangeDraft.text = "17";
            Press(view, "Right/Filters/Melee");
            if (RequirePanel("FacilityPanel") != view || rangeDraft == null || rangeDraft.text != "17")
                throw new InvalidOperationException("兵营切筛选时丢失左侧范围草案");
            list = view.Find("Right/List").GetComponent<ScrollRect>();
            var soldier = list.content.Cast<RectTransform>().First(node => node.name == "PersonnelRosterRow");
            VerifyRosterRow(soldier, false);
            CaptureViewports("ui-skin-facility-barracks");
            list.verticalNormalizedPosition = 0;
            ClickHudButton(soldier);
            if (RequirePanel("FacilityPanel") != view || rangeDraft == null || rangeDraft.text != "17" ||
                view.Find("Right/Title").GetComponent<Text>().text != "单位详情")
                throw new InvalidOperationException("兵营名册无法在右侧打开对应士兵详情");
            Press(view, "Right/Filters/Back");
            H.ClosePanel();
            Approach("home");
            H.OpenObject("home");
            view = RequirePanel("FacilityPanel");
            VerifyFacilityPortrait(view);
            actions = view.Find("Left/List").GetComponent<ScrollRect>().content;
            list = view.Find("Right/List").GetComponent<ScrollRect>();
            if (actions.Find("Rest") == null || !list.content.GetComponentsInChildren<Text>().Any(t =>
                t.text.Contains("扣粮后库存 " + BaseEconomy.Preview(controller.State).NextFood)))
                throw new InvalidOperationException("营帐休息操作或日结预估缺失");
            if (actions.Find("Warehouse") == null || actions.Find("Deputy") != null || actions.Find("Government") != null)
                throw new InvalidOperationException("营帐应保留仓库入口，建设移回领主");
            CaptureViewports("ui-skin-facility-home");
            int day = controller.State.Day;
            Press(actions, "Rest");
            if (!H.DialogueOpen || controller.State.Day != day)
                throw new InvalidOperationException("来袭时营帐休息没有接入对白拦截");
            H.ClosePanel();
            Log.Add("[PASS] 四个设施三种横屏，独立肖像栏，粮田局部滚动和取消草案，工坊解锁，兵营详情，营帐预估与来袭拦截");
            VerifyLordConstruction();
        }

        private static void VerifyFullCampInvitation()
        {
            const string ahe = "partner_farm";
            H.ClosePanel();
            var priorParty = controller.State.Adventure.Members.ToArray();
            Approach("demo_ahe");
            H.OpenObject("demo_ahe");
            FinishDialogue();
            Click("好，一起走");
            if (!H.DialogueOpen || !controller.State.Adventure.Members.SequenceEqual(priorParty) ||
                !controller.State.Leaders.Any(leader => leader.Id == ahe && leader.MapRegion == "base"))
                throw new InvalidOperationException("满队邀请阿禾没有直接登记为驻营队长");
            var response = RequirePanel("NpcDialogue").Find("DialogueText").GetComponent<Text>();
            H.ContinueDialogue();
            if (!response.text.Contains("留营")) throw new InvalidOperationException("满队邀请未用对白交代去向");
            Capture("ui-skin-ahe-full-following");
            FinishDialogue();
            H.ClosePanel();

            Approach("farm");
            H.OpenObject("farm");
            var farm = RequirePanel("FacilityPanel");
            Press(farm, "PortraitColumn/Manage");
            Press(farm.Find("PortraitColumn/ManagerOptions").GetComponent<ScrollRect>().content, ahe);
            if (H.DialogueOpen) FinishDialogue();
            RequirePanel("FacilityPanel");
            if (controller.State.Leaders.Single(leader => leader.Id == ahe).FacilityId != "farm" ||
                !controller.State.Adventure.Members.SequenceEqual(priorParty))
                throw new InvalidOperationException("营地满队时阿禾没有直接任职粮田");
            H.ClosePanel();
            Log.Add("[PASS] 实际对白满队邀请阿禾后入营，保留原随行队伍，点名即可任命粮田负责人");

            Approach("home");
            H.OpenObject("home");
            var tent = RequirePanel("FacilityPanel");
            var roster = tent.Find("Right/List").GetComponent<ScrollRect>();
            IEnumerable<Transform> RosterRows() => roster.content.Cast<Transform>().Where(row => row.name == "PersonnelRosterRow");
            Transform Captain(string name) => RosterRows().Single(row => row.GetComponentInChildren<Text>().text.Contains(name));
            Button FollowButton() => tent.Find("Left/List").GetComponent<ScrollRect>().content
                .GetComponentsInChildren<Button>().Single(button => button.name == "CaptainFollow");
            if (tent.Find("Right/Title").GetComponent<Text>().text != "队长名册" || RosterRows().Count() != controller.State.Leaders.Count)
                throw new InvalidOperationException("营帐没有显示完整的队长名册");
            ClickHudButton(Captain("阿禾"));
            if (RequirePanel("FacilityPanel") != tent || FollowButton().interactable)
                throw new InvalidOperationException("满队时营帐不应允许阿禾加入随行");
            CaptureViewports("ui-skin-tent-captains-full");
            ClickHudButton(Captain("迦兰"));
            ClickHudButton(FollowButton().transform);
            if (RequirePanel("FacilityPanel") != tent || controller.State.Adventure.Members.Count != priorParty.Length - 1)
                throw new InvalidOperationException("点留在营地后没有直接腾出随行位或打断了当前名册");
            ClickHudButton(Captain("阿禾"));
            ClickHudButton(FollowButton().transform);
            if (!BaseAdventure.Contains(controller.State, ahe) || controller.State.Adventure.Members.Count != priorParty.Length ||
                controller.State.Leaders.Single(leader => leader.Id == ahe).FacilityId != "")
                throw new InvalidOperationException("腾出位置后未从营帐正确邀请阿禾随行并释放岗位");
            Log.Add("[PASS] 营帐管理满队名册，在同一页更换随行队长");
            Press(tent.Find("Left/List").GetComponent<ScrollRect>().content, "Training");
            Click("返回营帐");
            RequirePanel("FacilityPanel");
            H.ClosePanel();

            H.OpenBackpack();
            string player = controller.State.PlayerLeaderId;
            var stone = BasePartyInventory.Entries(controller.State).First(item =>
                item.Holder == player && item.Stack?.ItemId == BaseEquipmentCrafting.Stone);
            ClickHudButton(InventoryCell(RequirePanel("PartyInventory"), stone.Id).transform);
            var itemMenu = RequirePanel("PartyInventory").Find("InventoryItemMenu");
            for (int count = stone.Count; count > 1; count--) Press(itemMenu, "QuantityDown");
            if (stone.Count > 1 && itemMenu.Find("Destroy").GetComponentInChildren<Text>().text != "销毁 ×1")
                throw new InvalidOperationException("销毁按钮没有标明将销毁的数量");
            Press(itemMenu, "Destroy");
            if ((BasePartyInventory.Find(controller.State, stone.Id)?.Count ?? 0) != stone.Count - 1)
                throw new InvalidOperationException("销毁选定数量仍需额外确认或数量错误");
            H.ClosePanel();

            H.OpenSlots(true);
            Click("保存到此槽");
            var catalog = BaseBattleGateway.Catalog();
            string slot = BaseSaveCatalog.Slots[0];
            int coins = catalog.LoadSlot(slot).Inventory.Coins;
            H.ClosePanel();
            if (!controller.Change(state => state.Inventory.Coins++, "")) throw new InvalidOperationException(controller.Message);
            H.OpenSlots(true);
            Click("覆盖此槽（保留备份）");
            if (catalog.LoadSlot(slot).Inventory.Coins != coins + 1 || catalog.LoadSlot(slot, true).Inventory.Coins != coins)
                throw new InvalidOperationException("手动槽直接覆盖未写入新档或未保留原档备份");
            Log.Add("[PASS] 从营帐打开训练，销毁直接提交，手动槽覆盖保留旧内容备份");
        }

        private static void VerifyFacilityPortrait(Transform view)
        {
            Canvas.ForceUpdateCanvases();
            var root = (RectTransform)view;
            var portrait = view.Find("PortraitColumn");
            var image = portrait?.Find("Portrait");
            if (((RectTransform)Field(H, "questBar")).gameObject.activeSelf || image == null ||
                (image.gameObject.activeInHierarchy && image.GetComponent<BattleAvatarGraphic>() == null) ||
                SceneRect(portrait, root).xMax > SceneRect(view.Find("Left"), root).xMin + .1f ||
                view.GetComponentsInChildren<Button>().Any(button => button.name == "Rules"))
                throw new InvalidOperationException("设施应有独立的左侧肖像栏，并移除重复的规则按钮");
        }

        private static void VerifyLordConstruction()
        {
            Approach("exit_east");
            controller.TravelDemo("town");
            if (controller.State.Campaign.Location != "town") throw new InvalidOperationException(controller.Message);
            Approach("town_quest");
            H.OpenObject("town_quest");
            FinishDialogue();
            var reply = RequirePanel("NpcDialogue");
            if (reply.Find("DialogueText").GetComponent<Text>().text.Contains("调查奖赏"))
                throw new InvalidOperationException("领主对白末页不应重复播报资源数字");
            Capture("ui-skin-lord-construction-entry");
            Click("营地建设");
            if (!controller.State.Demo.Flow.CampConstructionOrdered)
                throw new InvalidOperationException("领主未登记营地建设");
            Approach("travel_base");
            controller.TravelDemo("base");
            if (controller.State.Campaign.Location != "base") throw new InvalidOperationException(controller.Message);

            var hudRoot = (RectTransform)Field(H, "root");
            var resume = hudRoot.GetComponentsInChildren<Button>().Single(button => button.name == "CampConstructionResume");
            ClickHudButton(resume.transform);
            var palette = hudRoot.GetComponentsInChildren<RectTransform>().Single(node => node.name == "CampConstructionPalette");
            var moveFarm = palette.GetComponentsInChildren<Button>().Single(button => button.name == "Project-farm");
            ClickHudButton(moveFarm.transform);
            var state = controller.State;
            string original = BaseFacilityLayout.Snapshot(state);
            int materials = state.Inventory.Materials;
            var farm = BaseFacilityLayout.BaseLayout(state).Objects.Single(item => item.Id == "farm");
            double offsetX = string.IsNullOrEmpty(farm.Shape) ? .5 : farm.Object.BoundsWidth / 2 % 1;
            double offsetY = string.IsNullOrEmpty(farm.Shape) ? .5 : farm.Object.BoundsHeight / 2 % 1;
            var landing = BaseBattlefield.Board(state).Positions().First(cell =>
                (cell.X + offsetX != farm.X || cell.Z + offsetY != farm.Y) &&
                BaseFacilityLayout.Problem(state, "farm", cell.X + offsetX, cell.Z + offsetY) == "");
            controller.SelectCell(landing.X, landing.Z);
            var confirm = palette.GetComponentsInChildren<Button>().Single(button => button.name == "ConfirmLayout");
            if (!confirm.interactable || BaseFacilityLayout.Snapshot(controller.State) != original || controller.State.Inventory.Materials != materials)
                throw new InvalidOperationException("预览移动时提前提交了营地布局或未能生成草案");
            CaptureViewports("ui-skin-camp-construction");
            ClickHudButton(palette.GetComponentsInChildren<Button>().Single(button => button.name == "ExitLayout").transform);
            if (controller.CampPlacementMode || !controller.State.Demo.Flow.CampConstructionOrdered ||
                BaseFacilityLayout.Snapshot(controller.State) != original || controller.State.Inventory.Materials != materials)
                throw new InvalidOperationException("放弃布局后仍改变了建设位置或资源");
            ClickHudButton(resume.transform);
            ClickHudButton(moveFarm.transform);
            controller.SelectCell(landing.X, landing.Z);
            ClickHudButton(confirm.transform);
            var placed = BaseFacilityLayout.BaseLayout(controller.State).Objects.Single(item => item.Id == "farm");
            if (controller.CampPlacementMode || controller.State.Demo.Flow.CampConstructionOrdered ||
                placed.X != landing.X + offsetX || placed.Y != landing.Z + offsetY ||
                controller.State.Inventory.Materials != materials)
                throw new InvalidOperationException("确认布局未在实际地图应用移动后的粮田");
            Approach("farm");
            controller.Interact();
            VerifyFacilityPortrait(RequirePanel("FacilityPanel"));
            H.ClosePanel();
            Log.Add("[PASS] 与领主谈建设后回营，预览和放弃零扣费，确认后可在新位置使用粮田");
        }

        private static void VerifyRecruitRoster()
        {
            H.ClosePanel();
            Approach("exit_east");
            controller.TravelDemo("town");
            if (controller.State.Campaign.Location != "town") throw new InvalidOperationException(controller.Message);
            Approach("town_recruit");
            H.OpenObject("town_recruit");
            if (H.DialogueOpen) { FinishDialogue(); Click("开始招募"); }
            var trade = RequirePanel("TownTrade");
            var roster = trade.Find("RightBody").GetComponent<ScrollRect>();
            var rows = roster.GetComponentsInChildren<RectTransform>().Where(node => node.name == "PersonnelRosterRow").ToArray();
            if (rows.Length == 0 || rows.Length != BasePersonnel.Units(controller.State).Count(unit => !unit.IsLeader) ||
                !roster.vertical || trade.Find("RightTabs").gameObject.activeSelf)
                throw new InvalidOperationException("军官处没有在单一滚动列表中显示现役士兵");
            foreach (var row in rows)
            {
                VerifyRosterRow(row, false);
                if (row.GetComponent<Button>().interactable) throw new InvalidOperationException("军官右侧现役名册不是只读");
            }
            Capture("ui-skin-recruit-roster");
            var state = controller.State;
            var kind = state.Town.Recruits.First(offer => BaseTown.MaxHire(state, offer.Kind) > 0).Kind;
            var offerButton = trade.Find("OfferList").GetComponentsInChildren<Button>().Single(button =>
                button.GetComponentInChildren<Text>().text.TrimStart('●', ' ').StartsWith(BaseEconomy.TroopName(kind) + " · 今日余 "));
            ClickHudButton(offerButton.transform);
            var quote = BaseTown.QuoteHire(state, kind, 1);
            Click("招募 1 人 · " + quote.Total + "币");
            if (H.DialogueOpen || !controller.State.Demo.Flow.HideoutRecruitCluePending || controller.State.Demo.Flow.HideoutRecruitClueSeen ||
                BaseTown.TroopTotal(controller.State) != BaseTown.TroopTotal(state) + 1 ||
                controller.State.Inventory.Coins != state.Inventory.Coins - quote.Total ||
                !RequirePanel("TownTrade").Find("Details").GetComponentsInChildren<Text>().Any(text =>
                    text.text == "招募 1 人 · " + BaseTown.HirePrice(controller.State, kind) + "币"))
                throw new InvalidOperationException("首次购兵未等待退出，或逐人招募未结算重报价");
            Press(trade, "Close");
            if (!H.DialogueOpen || controller.State.Demo.Flow.HideoutRecruitCluePending || !controller.State.Demo.Flow.HideoutRecruitClueSeen)
                throw new InvalidOperationException("关闭招募时未显示一次窝点线索");
            H.ContinueDialogue();
            if (!RequirePanel("NpcDialogue").Find("DialogueText").GetComponent<Text>().text.Contains("土匪营地"))
                throw new InvalidOperationException("雷恩没有介绍断壁后的窝点");
            Capture("ui-skin-recruit-clue");
            FinishDialogue();
            if (H.PanelOpen) throw new InvalidOperationException("关闭招募后，读完线索应返回探索");
            H.OpenObject("town_recruit");
            var secondKind = controller.State.Town.Recruits.First(offer => BaseTown.MaxHire(controller.State, offer.Kind) > 0).Kind;
            var secondOffer = RequirePanel("TownTrade").Find("OfferList").GetComponentsInChildren<Button>().Single(button =>
                button.GetComponentInChildren<Text>().text.TrimStart('●', ' ').StartsWith(BaseEconomy.TroopName(secondKind) + " · 今日余 "));
            ClickHudButton(secondOffer.transform);
            Click("招募 1 人 · " + BaseTown.QuoteHire(controller.State, secondKind, 1).Total + "币");
            if (H.DialogueOpen || BaseTown.TroopTotal(controller.State) != BaseTown.TroopTotal(state) + 2 ||
                controller.State.Demo.Flow.HideoutRecruitCluePending)
                throw new InvalidOperationException("同一存档第二次购兵重复登记了窝点线索");
            Press(RequirePanel("TownTrade"), "Return");
            H.ContinueDialogue();
            if (!H.DialogueOpen || RequirePanel("NpcDialogue").Find("DialogueText").GetComponent<Text>().text.Contains("土匪营地"))
                throw new InvalidOperationException("再次退出招募时重播了窝点线索");
            H.ClosePanel();

            // Restore the same saved pre-close flag pair to exercise the alternate exit button.
            if (!controller.Change(next =>
                {
                    next.Demo.Flow.HideoutRecruitClueSeen = false;
                    next.Demo.Flow.HideoutRecruitCluePending = true;
                }, "")) throw new InvalidOperationException(controller.Message);
            H.OpenObject("town_recruit");
            Press(RequirePanel("TownTrade"), "Return");
            H.ContinueDialogue();
            if (!controller.State.Demo.Flow.HideoutRecruitClueSeen ||
                !RequirePanel("NpcDialogue").Find("DialogueText").GetComponent<Text>().text.Contains("土匪营地"))
                throw new InvalidOperationException("返回对话时未先播放待播的窝点线索");
            FinishDialogue();
            if (!H.DialogueOpen) throw new InvalidOperationException("返回对话读完线索后应保留雷恩交谈入口");
            H.ClosePanel();
            Log.Add("[PASS] 军官名册单列表滚动，逐人招募后重报价，关闭和返回对话时各自交付待播线索");
        }

        private static void OpenWarehouseShot()
        {
            H.ClosePanel();
            Approach("home");
            H.OpenObject("home");
            Click("仓库");
            stage = "warehouse-shot"; frameDeadline = Time.frameCount + 20;
        }

        private static Transform RequirePanel(string name)
        {
            var value = Field(H, "panel") as RectTransform;
            if (!H.PanelOpen || value == null || value.name != name)
                throw new InvalidOperationException("未进入 " + name + "：" + controller.Message);
            return value;
        }

        private static void Approach(string id)
        {
            if (!controller.Change(s =>
            {
                var target = BaseSpatial.Objects(s).Single(o => o.Id == id);
                var point = target.Blocks ? BaseStageWalk.Door(s, id) : new BasePoint(target.X, target.Y);
                s.PlayerX = point.X; s.PlayerY = point.Y;
                BaseInteraction.Walk(new BasePoint(target.X - point.X, target.Y - point.Y));
                BaseAdventure.Arrive(s);
            }, "界面检查")) throw new InvalidOperationException(controller.Message);
        }

        private static void FinishDialogue()
        {
            RequirePanel("NpcDialogue");
            for (int i = 0; i < 80 && H.ContinueDialogue(); i++) { }
        }

        private static void Click(string label)
        {
            var button = ((RectTransform)Field(H, "panel")).GetComponentsInChildren<Button>()
                .FirstOrDefault(b => b.interactable && b.GetComponentInChildren<Text>()?.text == label);
            if (button == null) throw new InvalidOperationException("找不到可操作的「" + label + "」");
            button.onClick.Invoke();
        }
        private static void Press(Transform root, string path)
        {
            var button = root.Find(path)?.GetComponent<Button>();
            if (button?.interactable != true) throw new InvalidOperationException(root.name + "/" + path + " 不可操作");
            button.onClick.Invoke();
        }
        private static void VerifyBelowTopBar(Transform panelRoot)
        {
            var header = panelRoot.GetComponentInParent<Canvas>().GetComponentsInChildren<RectTransform>(true)
                .Single(t => t.name == "ResourceStrip").parent as RectTransform;
            if (!header.gameObject.activeInHierarchy) return;
            Canvas.ForceUpdateCanvases();
            var corners = new Vector3[4];
            ((RectTransform)panelRoot).GetWorldCorners(corners);
            float panelTop = corners[1].y;
            header.GetWorldCorners(corners);
            if (panelTop > corners[0].y + .01f) throw new InvalidOperationException("探索顶栏压住备战标题");
        }
        private static void VerifySavedDialogueColor(Transform view)
        {
            if (Environment.GetEnvironmentVariable("BASE_CONQUEST_UI_CHECK_SAVED_LAYOUT") == "1" &&
                !((Color32)view.GetComponent<Image>().color).Equals(new Color32(241, 222, 182, 255)))
                throw new InvalidOperationException("对白框未读取已保存的场景颜色");
        }

        private static void VerifyRuinsEntrance()
        {
            if (!BaseSceneRules.Entrance(controller.State)) throw new InvalidOperationException("没有进入遗迹入口地图");
            var marker = BaseSpatial.Objects(controller.State).Single(o => o.Id == "demo_ruins_investigate");
            if (!controller.Change(s =>
            {
                var p = BaseSpatial.Nearest(s, new BasePoint(marker.X, marker.Y - .35));
                s.PlayerX = p.X;
                s.PlayerY = p.Y;
                BaseInteraction.Walk(new BasePoint(0, 1));
                BaseAdventure.Arrive(s);
            }, "检查遗迹遮挡")) throw new InvalidOperationException(controller.Message);
            var avatar = (Transform)Field(controller, "player");
            var portal = Object.FindObjectsByType<Renderer>().Where(r => r.name == "PixelDemo_dungeon-entrance")
                .OrderBy(r => (r.transform.position - avatar.position).sqrMagnitude).First();
            var sprite = avatar.GetComponentsInChildren<MeshRenderer>().Single(r => r.name == "PixelDemoCharacter");
            var walk = typeof(BaseSceneController).GetMethod("MoveExploration", BindingFlags.Instance | BindingFlags.NonPublic);
            foreach (double offset in new[] { .10, .18 })
            {
                double target = marker.Y + offset;
                for (int step = 0; step < 40 && target - controller.State.PlayerY > .001; step++)
                    walk.Invoke(controller, new object[] { Vector2.up, Math.Min(.02, (target - controller.State.PlayerY) / 4.3) });
                if (Math.Abs(controller.State.PlayerY - target) > .02)
                    throw new InvalidOperationException("主角向北未走到门洞取样位置：" + offset);
                if (sprite.sortingOrder <= portal.sortingOrder)
                    throw new InvalidOperationException("向北穿过门槛时贴图排序错误：" + offset);
            }
            for (int step = 0; step < 30; step++) walk.Invoke(controller, new object[] { Vector2.up, .01 });
            if (Math.Abs(controller.State.PlayerX - marker.X) > .02 || controller.State.PlayerY < marker.Y + .18 ||
                controller.State.PlayerY > marker.Y + .241 || sprite.sortingOrder <= portal.sortingOrder ||
                !BaseInteraction.CanReach(controller.State, marker))
                throw new InvalidOperationException("遗迹门前没有正确阻挡移动、显示角色或保留交互");
            var facing=avatar.GetComponent<PlayerHeroView>();
            walk.Invoke(controller,new object[]{Vector2.right,0d});
            double y=controller.State.PlayerY;
            walk.Invoke(controller,new object[]{Vector2.up,.01d});
            if(facing==null||facing.Facing!=HeroFacing.North||BaseInteraction.Facing.Y!=1||Math.Abs(controller.State.PlayerY-y)>.001)
                throw new InvalidOperationException("被门槛挡住时单独朝北输入未更新人物与交互朝向");
            Capture("ui-skin-ruins-entrance");
            Log.Add("[PASS] 北向移动停在门槛外，角色完整且仍可交互");
        }

        private static BattleSceneController RequireBattleDraft()
        {
            var battle = Object.FindObjectsByType<BattleSceneController>().SingleOrDefault(b => b.DeploymentDraftActive);
            if (battle == null) throw new InvalidOperationException("正式战斗部署未打开：" + controller.Message);
            if (battle.PreviewBoard.Camera != controller.WorldCamera || !battle.PreviewHud.Root.activeInHierarchy ||
                battle.PreviewSimulation.Phase != BattlePhase.Deployment || controller.State.Campaign.HasActiveBattle ||
                !battle.DraftPlacementActive ||
                BattleSafeArea(battle).Find("DraftPreparationControls/Start")?.GetComponentInChildren<Text>()?.text != "开战" ||
                Object.FindObjectsByType<RectTransform>().Any(t => t.name == "ResourceStrip" || t.name == "BattleDeployment"))
                throw new InvalidOperationException("部署应由正常战场和战斗 HUD 承载，且尚未提交开战");
            return battle;
        }

        private static RectTransform BattleSafeArea(BattleSceneController battle) =>
            battle.PreviewHud.Root.GetComponentsInChildren<RectTransform>(true).Single(t => t.name == "SafeArea");

        private static void VerifyBattleMenu(BattleSceneController battle, BaseBattlePreparation draft)
        {
            var menu = (RectTransform)BattleSafeArea(battle).Find("测试");
            var source = (RectTransform)controller.transform.Find("UI 布局源/BaseShell/Header/菜单");
            if (menu.anchorMax != source.anchorMax || menu.anchoredPosition != source.anchoredPosition || menu.sizeDelta != source.sizeDelta)
                throw new InvalidOperationException("战场菜单没有沿用探索菜单的位置和尺寸");

            Transform Overlay() => ((GameObject)Field(battle, "campaignMenu")).GetComponentsInChildren<RectTransform>(true)
                .Single(rect => rect.name == "选项面板");
            Press(menu.parent, menu.name);
            var positions = draft?.Positions.ToDictionary(p => p.Key, p => p.Value);
            if (draft != null)
            {
                var cell = positions[draft.PlayerId];
                var screen = BattleScreen(battle, draft.Board, cell);
                battle.PointerDown(-1, screen);
                battle.PointerUp(-1, screen);
                if (battle.TryDraftDeploymentCell(cell, true) || !draft.Positions.ContainsKey(draft.PlayerId))
                    throw new InvalidOperationException("菜单打开时还能撤回地图上的单位");
                battle.SpaceAction();
                if (controller.State.Campaign.HasActiveBattle || !battle.DeploymentDraftActive)
                    throw new InvalidOperationException("菜单打开时空格还能提交开战");
            }
            else
            {
                if (!battle.Realtime.Model.Paused) throw new InvalidOperationException("打开菜单没有立即暂停战斗");
                battle.SpaceAction();
                if (!battle.Realtime.Model.Paused) throw new InvalidOperationException("菜单打开时空格恢复了战斗");
            }
            CaptureViewports(draft != null ? "ui-bandit-hideout-deployment-menu" : "ui-bandit-hideout-battle-menu");
            var overlay = Overlay();
            if (overlay.Find("MainMenu")?.GetComponent<Button>()?.interactable != true ||
                overlay.Find("Quit")?.GetComponent<Button>()?.interactable != true)
                throw new InvalidOperationException("战场菜单缺少回主菜单或退出游戏入口");
            Press(overlay, "Continue");
            if (Field(battle, "campaignMenu") != null || (draft != null && !positions.OrderBy(p => p.Key)
                    .SequenceEqual(draft.Positions.OrderBy(p => p.Key))) || (draft == null && !battle.Realtime.Model.Paused))
                throw new InvalidOperationException("返回战场没有保留部署或战斗暂停状态");
            Log.Add(draft != null ? "[PASS] 部署菜单位置、两个退出入口、拦截地图和空格、返回后保留落点" : "[PASS] 战斗菜单位置、两个退出入口、返回后保持暂停直至手动继续");
        }

        private static void VerifyDeploymentPointer(BattleSceneController battle)
        {
            var root = BattleSafeArea(battle);
            var draft = (BaseBattlePreparation)Field(H, "battleDraft");
            Canvas.ForceUpdateCanvases();
            if (root.Find("DraftPreparationControls/Start").GetComponent<Button>().interactable)
                throw new InvalidOperationException("主角未布置便可开战");
            PlaceByPointer(battle, draft, draft.PlayerId);
            Log.Add(androidTouch ? "[PASS] Touchscreen 点按战场部署主角，开战前未写进度" : "[PASS] 进场直接点选落位，开战前未写进度");
        }

        private static void PlaceByPointer(BattleSceneController battle, BaseBattlePreparation draft, string id)
        {
            int visible = 0, picked = 0, unblocked = 0;
            foreach (var point in draft.Board.Positions().Where(p => draft.CanPlace(id, p) && !draft.Positions.ContainsValue(p)))
            {
                var screen = (Vector2)battle.PreviewBoard.Camera.WorldToScreenPoint(BattleBoardView.World(point, draft.Board));
                if (!battle.PreviewBoard.Camera.pixelRect.Contains(screen)) continue;
                visible++;
                if (!battle.PreviewBoard.Pick(screen, out var hit) || hit != point) continue;
                picked++;
                if (battle.PreviewHud.BlocksPointer(screen)) continue;
                unblocked++;
                if (androidTouch)
                {
                    WithBattleTouch(battle, "ReadPointer", sample =>
                    {
                        sample(1, TouchPhase.Began, screen);
                        sample(1, TouchPhase.Ended, screen);
                    });
                }
                else
                {
                    battle.PointerDown(-1, screen);
                    battle.PointerUp(-1, screen);
                }
                if (!draft.Positions.TryGetValue(id, out var chosen)) continue;
                if (chosen != point) throw new InvalidOperationException("战场点击没有落在所选地格");
                return;
            }
            throw new InvalidOperationException($"从战场坐标点击未放置 {id}：可见 {visible} 格，拾取 {picked} 格，无遮挡 {unblocked} 格，反馈 {battle.Feedback}");
        }

        private static void VerifyContinuousDeployment(BattleSceneController battle)
        {
            var draft = (BaseBattlePreparation)Field(H, "battleDraft");
            var next = draft.Units.Where(u => u.Selected && u.Kind == SoldierKind.Hero && u.Id != draft.PlayerId).First();
            int length = draft.Units.Count(u => u.Selected && u.Kind != SoldierKind.Hero && u.TeamId == draft.PlayerId) + 2;
            Canvas.ForceUpdateCanvases();
            bool CanPoint(GridPosition cell)
            {
                if (!draft.CanPlace(draft.PlayerId, cell)) return false;
                var screen = BattleScreen(battle, draft.Board, cell);
                return !battle.PreviewHud.BlocksPointer(screen) && battle.PreviewBoard.Pick(screen, out var hit) && hit == cell;
            }
            var stroke = draft.Board.Positions().Select(p => Enumerable.Range(0, length)
                .Select(i => new GridPosition(p.X, p.Z + i)).ToArray()).FirstOrDefault(cells => cells.All(CanPoint));
            if (stroke == null) throw new InvalidOperationException("找不到连续可点的部署格");
            var from = BattleScreen(battle, draft.Board, stroke[0]);
            var to = BattleScreen(battle, draft.Board, stroke[stroke.Length - 1]);
            battle.PointerDown(-1, from);
            battle.PointerMove(-1, to);
            battle.PointerUp(-1, to);
            if (!draft.Positions.TryGetValue(draft.PlayerId, out var first) || first != stroke[0] ||
                !draft.Positions.TryGetValue(next.Id, out var second) || second != stroke[stroke.Length - 1])
                throw new InvalidOperationException("快划未补齐跨过的格或未自动衔接下一位队长");
            Log.Add("[PASS] 一次快划连续摆放首位队长、所属士兵和下一位队长");
        }

        private static Vector2 BattleScreen(BattleSceneController battle, BattleBoard board, GridPosition cell) =>
            battle.PreviewBoard.Camera.WorldToScreenPoint(BattleBoardView.World(cell, board));

        private static void WithBattleTouch(BattleSceneController battle, string reader,
            Action<Action<int, TouchPhase, Vector2>> verify)
        {
            var read = typeof(BattleSceneController).GetMethod(reader, BindingFlags.Instance | BindingFlags.NonPublic);
            // Batch mode has no focused Game view. Drive the player's Input System buffer explicitly.
            var update = typeof(InputSystem).GetMethod("Update", BindingFlags.Static | BindingFlags.NonPublic,
                null, new[] { typeof(InputUpdateType) }, null);
            var settings = InputSystem.settings;
            var originalEditorRouting = settings.editorInputBehaviorInPlayMode;
            var originalBackgroundInput = settings.backgroundBehavior;
            bool originalRunInBackground = Application.runInBackground;
            Touchscreen device = null;
            try
            {
                Application.runInBackground = true;
                settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
                settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
                device = InputSystem.AddDevice<Touchscreen>();
                verify((id, phase, screen) =>
                {
                    InputSystem.QueueStateEvent(device, new TouchState { touchId = id, phase = phase, position = screen });
                    update.Invoke(null, new object[] { InputUpdateType.Dynamic });
                    if (phase == TouchPhase.Began && !device.touches.Any(t => t.touchId.ReadValue() == id && t.press.wasPressedThisFrame))
                        throw new InvalidOperationException("合成触点没有送入 Play Mode 输入缓冲区");
                    read.Invoke(battle, null);
                });
            }
            finally
            {
                if (device != null) InputSystem.RemoveDevice(device);
                settings.editorInputBehaviorInPlayMode = originalEditorRouting;
                settings.backgroundBehavior = originalBackgroundInput;
                Application.runInBackground = originalRunInBackground;
            }
        }

        private static void VerifyBattleTouch(BattleSceneController battle)
        {
            var view = battle.PreviewBoard;
            var board = battle.PreviewSimulation.Board;
            var camera = view.Camera;
            float originalSize = camera.orthographicSize;
            var originalOffset = (Vector3)Field(view, "panOffset");
            var originalInspection = battle.RealtimeFineInformation;
            var originalFeedback = battle.Feedback;
            Canvas.ForceUpdateCanvases();
            bool MapPoint(Vector2 point) => camera.pixelRect.Contains(point) && !battle.PreviewHud.BlocksPointer(point) && view.Pick(point, out _);
            void NoCommand()
            {
                if (battle.RealtimeFineInformation != originalInspection || battle.Feedback != originalFeedback)
                    throw new InvalidOperationException("战场触屏手势误触发检视或指令");
            }
            try
            {
                view.SetViewSize(view.FullViewSize);
                Vector2 a = camera.pixelRect.center - Vector2.right * 48, b = camera.pixelRect.center + Vector2.right * 48;
                Vector2 outward = Vector2.right * 42;
                if (!MapPoint(a) || !MapPoint(b) || !MapPoint(a - outward) || !MapPoint(b + outward))
                    throw new InvalidOperationException("战场中央没有足够空间验证触屏手势");
                WithBattleTouch(battle, "ReadTouchPointer", sample =>
                {
                    var beforePan = camera.transform.position;
                    sample(1, TouchPhase.Began, a);
                    NoCommand();
                    sample(1, TouchPhase.Moved, a - outward);
                    sample(1, TouchPhase.Ended, a - outward);
                    NoCommand();
                    if (camera.transform.position == beforePan) throw new InvalidOperationException("单指拖动未平移战场");

                    float beforeZoom = camera.orthographicSize;
                    sample(2, TouchPhase.Began, a);
                    sample(3, TouchPhase.Began, b);
                    sample(2, TouchPhase.Moved, a - outward);
                    sample(3, TouchPhase.Moved, b + outward);
                    sample(2, TouchPhase.Ended, a - outward);
                    sample(3, TouchPhase.Ended, b + outward);
                    NoCommand();
                    if (camera.orthographicSize >= beforeZoom) throw new InvalidOperationException("双指捏合未缩放战场");

                    var target = board.Positions().Select(cell => (cell, point: BattleScreen(battle, board, cell)))
                        .First(hit => hit.cell != battle.InformationCell && MapPoint(hit.point));
                    sample(4, TouchPhase.Began, target.point);
                    NoCommand();
                    sample(4, TouchPhase.Canceled, target.point);
                    NoCommand();
                    sample(5, TouchPhase.Began, target.point);
                    NoCommand();
                    sample(5, TouchPhase.Ended, target.point);
                    if (battle.InformationCell != target.cell)
                        throw new InvalidOperationException("触屏抬手未检视目标地格");
                });
            }
            finally
            {
                view.SetViewSize(originalSize);
                view.Follow(originalOffset);
            }
            Log.Add("[PASS] Touchscreen 战斗单指平移、双指缩放与取消均不误下令，点按抬手检视目标地格");
        }

        private static void VerifyDeploymentSpace(BattleSceneController battle)
        {
            // Batch mode has no focused Game view. Exercise the command shared by both Space input paths.
            battle.SpaceAction();
            if (!H.DialogueOpen)
                throw new InvalidOperationException("空格对应的开战操作未提交：" + battle.Feedback);
        }

        private static void VerifyOpeningBattleSelection(BattleSceneController battle)
        {
            var leaders = battle.Realtime.Model.Units.Where(unit => unit.Alive &&
                unit.Role == PrototypeRole.Leader && unit.Side == PrototypeSide.Ally).OrderBy(unit => unit.Squad).ToArray();
            var strategies = leaders.Select(unit => unit.Strategy).ToArray();
            if (leaders.Length != 3 || !battle.SelectedUnits.SequenceEqual(leaders.Where(unit =>
                    unit.Strategy == PrototypeStrategy.Obey).Select(unit => unit.Id - 1)) ||
                battle.SelectedUnits.Count != 2 || !battle.RealtimeStatus.Contains("进行中"))
                throw new InvalidOperationException("开战未按编队顺位仅选中手动队长");
            var select = battle.GetType().GetMethod("SelectOpeningCommanders", BindingFlags.Instance | BindingFlags.NonPublic);
            for (int count = 3; count >= 0; count--)
            {
                for (int i = 0; i < leaders.Length; i++) leaders[i].Strategy = i < count ? PrototypeStrategy.Obey : PrototypeStrategy.Attack;
                select.Invoke(battle, null);
                if (!battle.SelectedUnits.SequenceEqual(leaders.Take(count).Select(unit => unit.Id - 1)))
                    throw new InvalidOperationException($"开战默认选择在 {count} 名手动队长时不正确");
            }
            for (int i = 0; i < leaders.Length; i++) leaders[i].Strategy = strategies[i];
            var player = battle.Realtime.Model.Player;
            var board = battle.PreviewSimulation.Board;
            var center = player.BodyCenter;
            var position = new Vector3(((float)center.X - board.Width * .5f) * BattleBoardView.CellSize, 0,
                ((float)center.Z - board.Height * .5f) * BattleBoardView.CellSize);
            var camera = battle.PreviewBoard.Camera;
            if (!battle.RealtimePointer(camera.WorldToScreenPoint(position + camera.transform.up * .55f)) ||
                battle.SelectedUnits.Count > 0)
                throw new InvalidOperationException("空选点击友军格重新选中了队长");
            battle.ClickCommanderPortrait(0);
            if (battle.SelectedUnits.Count == 0)
                throw new InvalidOperationException("主动点击头像未恢复指挥");
            battle.ClickCommanderPortrait(0);
            if (battle.SelectedUnits.Count > 0)
                throw new InvalidOperationException("再次点击当前头像未取消选中");
            Canvas.ForceUpdateCanvases();
            var area = BattleSafeArea(battle);
            Vector2 PortraitCenter(int slot)
            {
                var rect = (RectTransform)area.Find($"CommanderSlot{slot}/CommanderPortrait_{slot}");
                return RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(rect.rect.center));
            }
            battle.BeginPortraitSelection(0, PortraitCenter(0));
            battle.MovePortraitSelection(PortraitCenter(2));
            battle.EndPortraitSelection();
            if (battle.SelectedUnits.Count != 3 || leaders.Any(unit => !battle.SelectedUnits.Contains(unit.Id - 1)))
                throw new InvalidOperationException("滑过三张头像后没有选择三名队长");
            battle.BeginPortraitSelection(0, PortraitCenter(0));
            battle.MovePortraitSelection(PortraitCenter(2));
            battle.EndPortraitSelection();
            if (battle.SelectedUnits.Count != 0) throw new InvalidOperationException("滑过已选头像后未取消选择");
            battle.ResumeAfterDialogue(true);
            select.Invoke(battle, null);
            Log.Add("[PASS] 混合策略与零至三名手动队长默认选择、空选点格查看、头像点击和滑选");
        }
        private static object Field(object target, string name)
            => target?.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(target);

        private static void Note(string message)
        {
            if (EditorApplication.timeSinceStartup < nextLog) return;
            nextLog = EditorApplication.timeSinceStartup + 15;
            Debug.Log("[UiSkin] " + message + "（阶段 " + stage + "）");
        }

        private static void CheckCampScenerySpawnAndEdge()
        {
            var view = (BattleBoardView)Field(controller, "regionView");
            var scenery = view.Root.transform.Find("CampScenery");
            var state = controller.State;
            var layout = BaseSpatial.Layout(state);
            if (scenery == null || scenery.GetComponentsInChildren<Collider>().Length > 0 ||
                view.Board.Width != layout.Columns || view.Board.Height != layout.Rows)
                throw new InvalidOperationException("布景未建立、含有碰撞体积或修改了正式棋盘");
            Log.Add($"出生点 ({state.PlayerX:F2}, {state.PlayerY:F2})；探索视野 {view.Camera.orthographicSize:F2}");
            if (scenery.GetComponent<MeshRenderer>().sharedMaterials.Any(material =>
                material.GetTexture("_BaseMap")?.name.StartsWith("grass", StringComparison.Ordinal) != true))
                throw new InvalidOperationException("灰岩营地外围仍有非草地材质");
            Capture("exploration-camp-spawn");

            var move = typeof(BaseSceneController).GetMethod("MoveExploration", BindingFlags.Instance | BindingFlags.NonPublic);
            view.SetViewSize(view.FullViewSize * BattleBoardView.DefaultExplorationZoom);
            view.Follow(controller.PlayerCameraTarget);
            var startCamera = view.Camera.transform.position;
            for (int i = 0; i < 20; i++)
            {
                move.Invoke(controller, new object[] { Vector2.left, .05 });
                RequireCentered(view, controller.PlayerCameraTarget);
            }
            double edgeX = state.PlayerX, edgeY = state.PlayerY;
            if (edgeX < 0 || edgeX >= .65 || BaseSpatial.Walkable(controller.State, -.5, edgeY) ||
                view.Board.Cell(new GridPosition(-1, (int)edgeY)) != null)
                throw new InvalidOperationException($"西边缘的移动或布置边界异常：x={edgeX}");
            if (Vector3.Distance(startCamera, view.Camera.transform.position) < 1f)
                throw new InvalidOperationException("镜头没有跟随主角走到西边缘");
            var sceneryPoint = view.Camera.WorldToScreenPoint(controller.WorldPoint(-1.3, edgeY));
            if (!view.Camera.pixelRect.Contains(sceneryPoint) || view.Pick(sceneryPoint, out _))
                throw new InvalidOperationException("地图外草地未入画或被误判为部署格");
            Log.Add($"[PASS] 向西走到 x={edgeX:F2}，逐步核对主角始终对准画面中心，外景无可拾取棋盘格");
        }

        private static void CheckCampSceneryFullView()
        {
            var view = (BattleBoardView)Field(controller, "regionView");
            var point = view.Camera.pixelRect.center;
            controller.ZoomExplorationFactor(point, 100);
            if (Mathf.Abs(view.Camera.orthographicSize / view.FullViewSize - 1f) > .0001f)
                throw new InvalidOperationException("未恢复原来的最远探索范围");
            RequireCentered(view, controller.PlayerCameraTarget);
            controller.ZoomExplorationFactor(point, .001f);
            if (Mathf.Abs(view.Camera.orthographicSize / view.FullViewSize - .3f) > .0001f)
                throw new InvalidOperationException("最近档改变了");
            RequireCentered(view, controller.PlayerCameraTarget);
            view.SetViewSize(view.FullViewSize * BattleBoardView.DefaultExplorationZoom);
            CheckOuterCorners(view, view.Root.transform.Find("CampScenery"));
            Log.Add("[PASS] 复原最远档 1、最近档 .3，两个极限保持居中；默认视野下四个边角有外围地表覆盖");
        }

        private static void CheckTownFraming()
        {
            H.ClosePanel();
            if (controller.State.Campaign.Location != "town") throw new InvalidOperationException(controller.Message);
            var view = (BattleBoardView)Field(controller, "regionView");
            Approach("travel_base");
            view.SetViewSize(view.FullViewSize * BattleBoardView.DefaultExplorationZoom);
            view.Follow(controller.PlayerCameraTarget);
            var scenery = view.Root.transform.Find("ExplorationScenery");
            if (scenery == null)
                throw new InvalidOperationException("灰岩镇缺少外围布景");
            RequireCentered(view, controller.PlayerCameraTarget);
            CheckOuterCorners(view, scenery);
            Capture("exploration-town-centered-west");
            Capture("exploration-town-centered-west-narrow", 1024, 640);
        }

        private static void RequireCentered(BattleBoardView view, Vector3 target)
        {
            var point = view.Camera.WorldToViewportPoint(target);
            if (Mathf.Abs(point.x - .5f) > .0001f || Mathf.Abs(point.y - .5f) > .0001f)
                throw new InvalidOperationException($"角色没有居中：{point}");
        }

        private static void CheckOuterCorners(BattleBoardView view, Transform scenery)
        {
            var mesh = scenery.GetComponent<MeshFilter>();
            float raisedFocus = view.Board.Cells.Max(cell => cell.Elevation) * BaseTerrainView.LevelHeight + 1.2f;
            for (int side = 0; side < 4; side++)
            {
                bool east = (side & 1) != 0, north = (side & 2) != 0;
                var target = new Vector3((east ? 1 : -1) * view.Board.Width * BattleBoardView.CellSize * .5f,
                    raisedFocus, (north ? 1 : -1) * view.Board.Height * BattleBoardView.CellSize * .5f + 1.2f);
                view.Follow(target);
                RequireCentered(view, target);
                var corner = new Vector3(east ? .999f : .001f, north ? .999f : .001f);
                if (!BaseTerrainView.Raycast(mesh, view.Camera.ViewportPointToRay(corner), out _))
                    throw new InvalidOperationException($"布景未覆盖视口边角 {corner}");
            }
            view.Follow(controller.PlayerCameraTarget);
        }

        private static void CheckSceneryOcclusion()
        {
            var type = typeof(PixelCharacterSheet).Assembly.GetType("BaseConquest.Presentation.SceneryOcclusion");
            var root = new GameObject("Occlusion check");
            var camera = root.AddComponent<Camera>();
            camera.transform.SetPositionAndRotation(new Vector3(100, 10, 100), Quaternion.Euler(90, 0, 0));
            camera.orthographic = true;
            camera.orthographicSize = 2;
            camera.aspect = 1;
            camera.cullingMask = 1 << 30;
            const int size = 16;
            Texture2D Half(bool right, bool readable)
            {
                var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
                var pixels = new Color32[size * size];
                for (int y = 0; y < size; y++)
                    for (int x = 0; x < size; x++)
                        if (right ? x >= 10 : x < 6) pixels[y * size + x] = Color.white;
                texture.SetPixels32(pixels);
                texture.Apply(false, !readable);
                return texture;
            }

            var actorImage = Half(true, false);
            var decorImage = Half(false, true);
            var actorMesh = new Mesh { vertices = new[] { new Vector3(-1, 0, -1), new Vector3(-1, 0, 1),
                new Vector3(1, 0, 1), new Vector3(1, 0, -1) }, triangles = new[] { 0, 1, 2, 0, 2, 3 } };
            var actorObject = new GameObject("Actor", typeof(MeshFilter), typeof(MeshRenderer)) { layer = 30 };
            actorObject.transform.position = new Vector3(100, 0, 100);
            actorObject.GetComponent<MeshFilter>().sharedMesh = actorMesh;
            var actor = actorObject.GetComponent<Renderer>();
            var decorObject = GameObject.CreatePrimitive(PrimitiveType.Quad);
            decorObject.transform.SetPositionAndRotation(actorObject.transform.position, camera.transform.rotation);
            decorObject.transform.localScale = new Vector3(2, 2, 1);
            var material = new Material(Shader.Find("BaseConquest/SunSprite"));
            material.SetTexture("_BaseMap", decorImage);
            var decor = decorObject.GetComponent<Renderer>();
            decor.sharedMaterial = material;
            decor.sortingOrder = 1;
            type.GetMethod("Attach").Invoke(null, new object[] { decor, camera });
            type.GetMethod("TrackFrame").Invoke(null, new object[] { actor, actorMesh, actorImage, new Rect(0, 0, 1, 1) });
            try
            {
                var fade = decorObject.GetComponent(type);
                var properties = new MaterialPropertyBlock();
                int Sample()
                {
                    type.GetField("cachedFrame", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, -1);
                    type.GetMethod("LateUpdate", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(fade, null);
                    decor.GetPropertyBlock(properties);
                    return properties.GetInt("_ActorFadeCount");
                }
                if (Sample() != 0) throw new InvalidOperationException("只有透明画布交叠也触发了虚化");
                actorObject.transform.position += Vector3.left * .8f;
                if (Sample() != 1) throw new InvalidOperationException("可见角色实际被盖住时没有虚化");
                actor.sortingOrder = 2;
                if (Sample() != 0) throw new InvalidOperationException("角色处于前景仍触发虚化");
                actor.sortingOrder = 0;
                actor.forceRenderingOff = true;
                if (Sample() != 0) throw new InvalidOperationException("隐藏角色触发了虚化");
            }
            finally
            {
                type.GetMethod("Untrack").Invoke(null, new object[] { actor });
                foreach (var item in new Object[] { root, actorObject, decorObject, material, actorMesh, actorImage, decorImage }) Object.DestroyImmediate(item);
            }
            Log.Add("[PASS] 画布交叠、角色在前及隐藏均无虚化，真实像素遮挡产生虚化");
        }

        private static void CheckCampNames()
        {
            var strip = (RectTransform)Field(H, "resourceStrip");
            if (strip.Find("Location").GetComponent<Text>().text != BaseScriptNames.Camp)
                throw new InvalidOperationException("界面地名未使用灰岩营地");

            var oldState = controller.State.Copy();
            BaseSpatial.Layout(oldState).Title = "断流滩新营地";
            BaseSpatial.Layout(oldState, "town").Objects.Single(obj => obj.Id == "travel_base").Name = "← 返回营地";
            BaseEconomy.Validate(oldState);
            if (BaseCampaign.LocationName(oldState) != BaseScriptNames.Camp ||
                BaseSpatial.Layout(oldState, "town").Objects.Single(obj => obj.Id == "travel_base").Name != "← 返回" + BaseScriptNames.Camp)
                throw new InvalidOperationException("旧地图的营地标签没有随读取更新");

            H.OpenDemoStory();
            var dialogue = RequirePanel("NpcDialogue");
            for (int i = 0; i < 5; i++) H.ContinueDialogue();
            if (dialogue.Find("SpeakerName").GetComponent<Text>().text != "老周" ||
                !dialogue.Find("DialogueText").GetComponent<Text>().text.Contains("灰岩镇西边的营地。"))
                throw new InvalidOperationException("苏醒对白未使用老周的口语称呼");
            Log.Add("[PASS] 实际顶栏为灰岩营地，旧地图标签可读，老周对白用口语称呼");
        }

        private static void CaptureViewports(string name, bool mobile = false)
        {
            Capture(name);
            Capture(name + "-1080p", 1920, 1080);
            Capture(name + "-narrow", 1024, 640);
            if (mobile)
            {
                Capture(name + "-854x480", 854, 480);
                Capture(name + "-960x432", 960, 432);
            }
        }

        private static void Capture(string name, int w = 1280, int h = 720)
        {
            var cams = Camera.allCameras.Where(c => c.enabled && c.targetTexture == null).OrderBy(c => c.depth).ToList();
            if (cams.Count == 0 && controller?.WorldCamera != null) cams.Add(controller.WorldCamera);
            if (cams.Count == 0) { Log.Add("[失败] " + name + "：找不到相机"); failed = true; return; }
            var fit = GameFrameLayout.Fit(w, h, 0, 0, w, h);
            var area = new Rect((float)fit.X, (float)fit.Y, (float)fit.Width, (float)fit.Height);
            foreach (var frame in Object.FindObjectsByType<GameFrameCanvas>()) frame.ApplyLayout(area, w, h);
            var prev = RenderTexture.active;
            var tex = CaptureWorld(cams, area, w, h);
            var top = cams.LastOrDefault();
            var canvases = Object.FindObjectsByType<Canvas>().Where(c => c.isRootCanvas && c.isActiveAndEnabled && c.renderMode == RenderMode.ScreenSpaceOverlay).OrderBy(c => c.sortingOrder).ToArray();
            var layers = new Dictionary<GameObject, int>();
            if (top != null && canvases.Length > 0)
            {
                var uiRt = RenderTexture.GetTemporary(w, h, 24, RenderTextureFormat.ARGB32);
                var rect = top.rect; float aspect = top.aspect; var target = top.targetTexture;
                int mask = top.cullingMask; var clear = top.clearFlags; var bg = top.backgroundColor;
                foreach (var canvas in canvases)
                {
                    foreach (var t in canvas.GetComponentsInChildren<Transform>(true)) { layers[t.gameObject] = t.gameObject.layer; t.gameObject.layer = 31; }
                    canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = top; canvas.planeDistance = Mathf.Min(top.farClipPlane * .5f, top.nearClipPlane + 1f);
                }
                Canvas.ForceUpdateCanvases();
                top.targetTexture = uiRt; top.rect = new Rect(0, 0, 1, 1); top.aspect = w / (float)h; top.cullingMask = 1 << 31; top.clearFlags = CameraClearFlags.SolidColor; top.backgroundColor = new Color(0, 0, 0, 0); top.Render();
                top.cullingMask = mask; top.clearFlags = clear; top.backgroundColor = bg;
                foreach (var canvas in canvases) { canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.worldCamera = null; }
                foreach (var pair in layers) if (pair.Key != null) pair.Key.layer = pair.Value;
                var ui = Read(uiRt, w, h);
                var a = tex.GetPixels32(); var b = ui.GetPixels32();
                for (int i = 0; i < a.Length; i++)
                {
                    int al = b[i].a; if (al == 0) continue;
                    a[i] = new Color32((byte)Mathf.Min(255, b[i].r + a[i].r * (255 - al) / 255), (byte)Mathf.Min(255, b[i].g + a[i].g * (255 - al) / 255), (byte)Mathf.Min(255, b[i].b + a[i].b * (255 - al) / 255), 255);
                }
                tex.SetPixels32(a); tex.Apply(); Object.DestroyImmediate(ui);
                top.targetTexture = target; top.rect = rect; top.aspect = aspect;
                RenderTexture.active = prev;
                RenderTexture.ReleaseTemporary(uiRt);
            }
            foreach (var frame in Object.FindObjectsByType<GameFrameCanvas>()) frame.Apply();
            RenderTexture.active = prev;
            File.WriteAllBytes(Path.Combine(OutDir, name + ".png"), tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            Log.Add("## " + name + " ｜ 地点：" + controller?.State?.Campaign.Location + " ｜ 阶段：" + controller?.State?.Demo?.Stage
                + " ｜ 页面：" + (H != null && H.PanelOpen ? "有模态页" : "无模态页") + " ｜ Canvas=" + canvases.Length);
            Debug.Log("[UiSkin] 截图 " + name);
        }

        private static Texture2D CaptureWorld(IEnumerable<Camera> cameras, Rect gameArea, int width, int height)
        {
            var liveArea = GameFrameCanvas.Area;
            var liveScreen = new Rect(0, 0, Screen.width, Screen.height);
            var screen = new Rect(0, 0, width, height);
            var pixels = new Color32[width * height];
            foreach (var camera in cameras)
            {
                bool background = camera.cullingMask == 0;
                var source = background ? liveScreen : liveArea;
                var destination = background ? screen : gameArea;
                var viewport = camera.pixelRect;
                var scale = new Vector2(destination.width / source.width, destination.height / source.height);
                var min = Vector2.Max(destination.min, destination.min + Vector2.Scale(viewport.min - source.min, scale));
                var max = Vector2.Min(destination.max, destination.min + Vector2.Scale(viewport.max - source.min, scale));
                int left = Mathf.RoundToInt(min.x), right = Mathf.RoundToInt(max.x);
                int bottom = Mathf.RoundToInt(min.y), top = Mathf.RoundToInt(max.y);
                if (left >= right || bottom >= top) continue;

                int cameraWidth = right - left, cameraHeight = top - bottom;
                var target = RenderTexture.GetTemporary(cameraWidth, cameraHeight, 24);
                var active = RenderTexture.active;
                var savedTarget = camera.targetTexture;
                var savedRect = camera.rect;
                float savedAspect = camera.aspect;
                try
                {
                    // A camera renders across its entire target texture. Compose that image at the live viewport's position.
                    camera.targetTexture = target;
                    camera.rect = new Rect(0, 0, 1, 1);
                    camera.aspect = cameraWidth / (float)cameraHeight;
                    camera.Render();
                    var image = Read(target, cameraWidth, cameraHeight);
                    var cameraPixels = image.GetPixels32();
                    Object.DestroyImmediate(image);
                    for (int row = 0; row < cameraHeight; row++)
                        Array.Copy(cameraPixels, row * cameraWidth, pixels, (bottom + row) * width + left, cameraWidth);
                }
                finally
                {
                    camera.targetTexture = savedTarget;
                    camera.rect = savedRect;
                    camera.aspect = savedAspect;
                    RenderTexture.active = active;
                    RenderTexture.ReleaseTemporary(target);
                }
            }
            var result = new Texture2D(width, height, TextureFormat.RGBA32, false);
            result.SetPixels32(pixels);
            result.Apply();
            return result;
        }

        private static Texture2D Read(RenderTexture rt, int w, int h)
        {
            RenderTexture.active = rt; var tex = new Texture2D(w, h, TextureFormat.RGBA32, false); tex.ReadPixels(new Rect(0, 0, w, h), 0, 0); tex.Apply(); return tex;
        }

        private static void Finish(int code, string message)
        {
            EditorApplication.update -= Tick;
            Log.Insert(0, "exit=" + code + " " + message + " ｜ " + DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
            string report = campSceneryOnly ? "ui-exploration-framing-report.txt" : androidTouch ? "ui-android-touch-report.txt" :
                stage == "opening-copy" ? "ui-opening-copy-report.txt" :
                stage.StartsWith("civilian-", StringComparison.Ordinal) ? "ui-civilian-work-report.txt" :
                stage.StartsWith("route-", StringComparison.Ordinal) ? "ui-player-route-views-report.txt" : "ui-skin-report.txt";
            File.WriteAllText(Path.Combine(OutDir, report), string.Join("\n", Log));
            if (code == 0) Debug.Log("[UiSkin] " + message); else Debug.LogError("[UiSkin] " + message);
            failed = code != 0;
            EditorApplication.isPlaying = false;
            EditorApplication.Exit(code);
        }
    }
}

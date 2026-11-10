using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BaseConquest.Base;
using BaseConquest.Battle;
using BaseConquest.Presentation;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace BaseConquest.Editor
{
    // P8（W8）C4～C6 离屏截图验收：
    //   Unity.exe -batchmode -projectPath RPGDemo -executeMethod BaseConquest.Editor.UiSkinScreenshot.Run
    // 不加 -nographics、不加 -quit。使用本次检查专用进度验证正式 Demo 流程。
    // 产物：Logs/wood-ui/。
    // 世界地图不在此脚本内：Demo 阶段档的 WorldJourney.Nodes／Entries 为空且全流程走领地内直达，
    // 游戏内没有可进入的世界地图画面，见 LOG 记录；C6 以 VerifyWorldArt 的静态断言为准。
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
        private static bool failed;
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

        private static void Tick()
        {
            try { TickStage(); }
            catch (Exception error) { Finish(2, stage + "：" + error); }
        }

        private static void TickStage()
        {
            if (failed || EditorApplication.timeSinceStartup > deadline) { Finish(2, "超时或前置失败：" + stage); return; }
            if (!EditorApplication.isPlaying) { Note("等待进入 playmode"); return; }
            switch (stage)
            {
                case "boot":
                    controller = C;
                    if (controller?.Message?.StartsWith("开局失败", StringComparison.Ordinal) == true)
                        throw new InvalidOperationException(controller.Message);
                    if (controller?.State == null || Object.FindAnyObjectByType<SceneLoadingCover>() != null)
                    { Note("等待序章进度"); return; }
                    if (!BaseDemoPrologue.Village(controller.State)) throw new InvalidOperationException("新档未进入白桦村");
                    CaptureViewports("ui-skin-prologue");
                    controller.LoadDemoStage("camp", "female");
                    stage = "loading"; stageAt = EditorApplication.timeSinceStartup;
                    break;
                case "loading":
                    controller = C;
                    if (controller?.State == null) { if (EditorApplication.timeSinceStartup - stageAt > 150) Finish(2, "阶段档载入超时"); return; }
                    if (controller.State.Campaign.Location != "base") return;
                    H?.ClosePanel();
                    stage = "wake"; frameDeadline = Time.frameCount + 5;
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
                    RequirePanel("PartyInventory");
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
                    OpenWarehouseShot();
                    break;
                case "warehouse-shot":
                    if (Time.frameCount < frameDeadline) return;
                    RequirePanel("PartyWarehouse");
                    Capture("ui-skin-warehouse");
                    H.ClosePanel();
                    BaseBattleGateway.MainMenu = true;
                    try { H.OpenMenu(); } finally { BaseBattleGateway.MainMenu = false; }
                    stage = "menu-shot"; frameDeadline = Time.frameCount + 40;
                    break;
                case "menu-shot":
                    if (Time.frameCount < frameDeadline) return;
                    Capture("ui-skin-mainmenu");
                    H.ClosePanel();
                    if (!controller.Change(s =>
                    {
                        var exit = BaseSpatial.Objects(s).Single(o => o.Id == "exit_east");
                        s.PlayerX = exit.X; s.PlayerY = exit.Y; BaseAdventure.Arrive(s);
                        if (!BaseCampaign.Travel(s, "town")) throw new InvalidOperationException("前往城镇失败");
                    }, "截图：前往城镇")) { Finish(2, "前往城镇被拒：" + controller.Message); return; }
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
                    CaptureViewports("ui-skin-dialogue");
                    H.ClosePanel();
                    Approach("town_peddler");
                    H.OpenObject("town_peddler");
                    var merchantDialogue = RequirePanel("NpcDialogue");
                    VerifySavedDialogueColor(merchantDialogue);
                    var pendingReplies = merchantDialogue.Find("Replies").GetComponentsInChildren<Button>();
                    if (pendingReplies.Length != 5 || pendingReplies.Any(button =>
                        button.interactable || button.GetComponentInChildren<Text>().text != "..."))
                        throw new InvalidOperationException("行商对白未原位显示禁用的占位选项");
                    if (merchantDialogue.Find("AdvanceButton").GetComponentInChildren<Text>().text != "→")
                        throw new InvalidOperationException("对白继续按钮未使用右箭头");
                    Capture("ui-skin-dialogue-placeholders");
                    FinishDialogue();
                    if (pendingReplies.Any(button => !button.interactable || button.GetComponentInChildren<Text>().text == "..."))
                        throw new InvalidOperationException("行商对白播完后未原位开放实际选项");
                    Click(BaseConquest.Story.StoryText.Get("story.ui.trade.market_entry", "买卖物资"));
                    var merchantMarket = RequirePanel("TownTrade");
                    if (merchantMarket.Find("Tabs").gameObject.activeInHierarchy)
                        throw new InvalidOperationException("交易面板仍显示业务页签");
                    var stoneOffer = merchantMarket.Find("OfferList").GetComponentsInChildren<Button>()
                        .SingleOrDefault(button => button.GetComponentInChildren<Text>().text.StartsWith("锻造石 · "));
                    if (stoneOffer == null) throw new InvalidOperationException("普通物资货架缺少锻造石商品");
                    stoneOffer.onClick.Invoke();
                    if (!RequirePanel("TownTrade").Find("Details").GetComponentsInChildren<Text>().Any(label => label.text.StartsWith("锻造石 · 单价 ")))
                        throw new InvalidOperationException("从货架选中锻造石后未显示数量与报价");
                    stage = "trade-shot"; frameDeadline = Time.frameCount + 30;
                    break;
                case "trade-shot":
                    if (Time.frameCount < frameDeadline) return;
                    CaptureViewports("ui-skin-trade");
                    H.ClosePanel();
                    Approach("town_quest");
                    if (!controller.Change(s =>
                    {
                        if (s.Demo.AcceptedDay == 0 && !BaseDemo.AcceptLordQuest(s)) throw new InvalidOperationException("未能接取古窟调查");
                    }, "界面检查")) throw new InvalidOperationException(controller.Message);
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
                    Press(BattleSafeArea(ruinsPlacement), "测试");
                    if (controller.State.Campaign.CurrentMapId != ruinsEntranceMap || !BaseSceneRules.Entrance(controller.State) ||
                        Object.FindObjectsByType<BattleSceneController>().Any(b => b.DeploymentDraftActive) ||
                        !Object.FindObjectsByType<Renderer>().Any(r => r.name == "PixelDemo_dungeon-entrance"))
                        throw new InvalidOperationException("返回备战没有恢复遗迹入口");
                    Press(RequirePanel("BattlePreparation"), "Continue");
                    var cancelRuins = BattleSafeArea(RequireBattleDraft());
                    Press(cancelRuins, "Deployment");
                    FinishDialogue();
                    Click("暂不进入，先调整随行");
                    if (H.PanelOpen || controller.State.Campaign.CurrentMapId != ruinsEntranceMap ||
                        controller.State.Campaign.HasActiveBattle || Object.FindObjectsByType<BattleSceneController>().Any(b => b.DeploymentDraftActive))
                        throw new InvalidOperationException("取消遗迹交战没有恢复入口或清空草案");
                    Log.Add("[PASS] 遗迹返回备战及对白拒绝均恢复入口，未提交战斗");
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
                    CaptureViewports("ui-skin-backpack-three");
                    H.ClosePanel();
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
                    Press(draftArea, "CommanderSlot0/TroopToggle");
                    if (firstTroopRow.gameObject.activeInHierarchy || !secondTroopRow.gameObject.activeInHierarchy)
                        throw new InvalidOperationException("兵种卡未按队独立收起");
                    Press(draftArea, "CommanderSlot0/TroopToggle");
                    if (!firstTroopRow.gameObject.activeInHierarchy)
                        throw new InvalidOperationException("兵种卡未重新展开");
                    Log.Add("[PASS] 战况统一进横幅，三张全宽兵种卡竖排且可按队独立折叠");
                    VerifyContinuousDeployment(deploy);
                    Press(draftArea, "PreparationControls/一键清场");
                    if (deploy.PreviewSimulation.Soldiers.Any(s => s.Side == BattleSide.A))
                        throw new InvalidOperationException("一键撤销未清空全部落点");
                    VerifyDeploymentPointer(deploy);
                    var resetDraft = (BaseBattlePreparation)Field(H, "battleDraft");
                    var firstSoldier = resetDraft.Units.First(u => u.TeamId == resetDraft.PlayerId &&
                        u.Selected && u.Kind != SoldierKind.Hero);
                    PlaceByPointer(deploy, resetDraft, firstSoldier.Id);
                    Press(BattleSafeArea(deploy), "测试");
                    Press(RequirePanel("BattlePreparation"), "Continue");
                    defenseBattle = RequireBattleDraft();
                    defenseHud = defenseBattle.PreviewHud.Root;
                    defenseCamera = defenseBattle.PreviewBoard.Camera;
                    defenseViewport = defenseCamera.rect;
                    if (defenseBattle.PreviewSimulation.Soldiers.Count(s => s.Side == BattleSide.A) != 2)
                        throw new InvalidOperationException("从备战返回丢失了原落点");
                    stage = "defense-deployment-shot"; frameDeadline = Time.frameCount + 15;
                    break;
                case "defense-deployment-shot":
                    if (Time.frameCount < frameDeadline) return;
                    CaptureViewports("ui-skin-battle-deployment");
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
                    VerifyEmptyBattleSelection(defenseBattle);
                    if (Object.FindObjectsByType<RectTransform>().Any(t => t.name == "BattleJoystick"))
                        throw new InvalidOperationException("正式战斗仍显示角色操控摇杆");
                    CaptureViewports("ui-icons-battle-hud");
                    stage = "done"; frameDeadline = Time.frameCount + 60;
                    break;
                case "done":
                    if (Time.frameCount < frameDeadline) return;
                    Finish(0, "OK");
                    break;
            }
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

            var trackerText = (Text)Field(H, "trackedQuest");
            var tracker = trackerText.transform.parent;
            if (tracker.gameObject.activeSelf) throw new InvalidOperationException("没有追踪任务时常驻栏应隐藏");
            string quest = BaseQuestLog.Entries(controller.State).First(entry => entry.Trackable).Id;
            bool folded = BaseQuestLog.Collapsed(controller.State);
            if (!controller.Change(s => BaseQuestLog.ToggleTrack(s, quest), "界面检查")) throw new InvalidOperationException(controller.Message);
            if (trackerText.preferredHeight > trackerText.rectTransform.rect.height + 1)
                throw new InvalidOperationException("任务追踪的固定字号文字被截断");
            Capture("ui-skin-tracked-quest");
            ClickHudButton(tracker.Find("折叠"));
            if (BaseQuestLog.Collapsed(controller.State) == folded) throw new InvalidOperationException("任务追踪折叠按钮未切换");
            ClickHudButton(tracker);
            if ((string)Field(H, "panelViewKey") != "quests") throw new InvalidOperationException("任务追踪入口没有打开任务列表");
            H.ClosePanel();
            if (!controller.Change(s => { BaseQuestLog.ToggleTrack(s, quest); BaseQuestLog.SetCollapsed(s, folded); }, "界面检查"))
                throw new InvalidOperationException(controller.Message);

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
            Log.Add("[PASS] 七项资源下拉、日历翻页、点击外部关闭且地图可操作、系统菜单与两处任务入口");
        }

        private static void ClickHudButton(Transform node)
        {
            var button = node == null ? null : node.GetComponent<Button>();
            if (button == null || !button.isActiveAndEnabled || !button.interactable)
                throw new InvalidOperationException("HUD 按钮不可操作：" + node?.name);
            button.onClick.Invoke();
        }

        private static void VerifyAuthoredLayout()
        {
            var source = (RectTransform)controller.transform.Find("UI 布局源/PartyMemberCard/BagSlot_0");
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
                var live = (RectTransform)card.Find("BagSlot_0");
                if (Vector2.Distance(source.anchoredPosition, live.anchoredPosition) > .01f
                    || Vector4.Distance(image.color, live.GetComponent<Image>().color) > .005f)
                    throw new InvalidOperationException("背包格运行时覆盖了场景位置或配色");
                Log.Add("[PASS] 场景编辑的背包格位置与配色在重新打开时生效");
            }
            finally { source.anchoredPosition = originalPosition; image.color = originalColor; }
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
            var portal = Object.FindObjectsByType<Renderer>().Single(r => r.name == "PixelDemo_dungeon-entrance");
            var avatar = (Transform)Field(controller, "player");
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
                BattleSafeArea(battle).Find("Deployment")?.GetComponentInChildren<Text>()?.text != "开战" ||
                Object.FindObjectsByType<RectTransform>().Any(t => t.name == "ResourceStrip" || t.name == "BattleDeployment"))
                throw new InvalidOperationException("部署应由正常战场和战斗 HUD 承载，且尚未提交开战");
            return battle;
        }

        private static RectTransform BattleSafeArea(BattleSceneController battle) =>
            battle.PreviewHud.Root.GetComponentsInChildren<RectTransform>(true).Single(t => t.name == "SafeArea");

        private static void VerifyDeploymentPointer(BattleSceneController battle)
        {
            var root = BattleSafeArea(battle);
            var draft = (BaseBattlePreparation)Field(H, "battleDraft");
            Canvas.ForceUpdateCanvases();
            if (root.Find("Deployment").GetComponent<Button>().interactable)
                throw new InvalidOperationException("主角未布置便可开战");
            PlaceByPointer(battle, draft, draft.PlayerId);
            Log.Add("[PASS] 进场直接点选落位，开战前未写进度");
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
                battle.PointerDown(-1, screen);
                battle.PointerUp(-1, screen);
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

        private static void VerifyDeploymentSpace(BattleSceneController battle)
        {
            // Batch mode has no focused Game view. Exercise the command shared by both Space input paths.
            battle.SpaceAction();
            if (!H.DialogueOpen)
                throw new InvalidOperationException("空格对应的开战操作未提交：" + battle.Feedback);
        }

        private static void VerifyEmptyBattleSelection(BattleSceneController battle)
        {
            if (battle.SelectedUnits.Count > 0 || !battle.RealtimeStatus.Contains("进行中"))
                throw new InvalidOperationException("正式开战未以空选运行");
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
            Log.Add("[PASS] 空格共用的开战操作、空选点友军格只查看、头像可选中和取消");
        }
        private static object Field(object target, string name)
            => target?.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(target);

        private static void Note(string message)
        {
            if (EditorApplication.timeSinceStartup < nextLog) return;
            nextLog = EditorApplication.timeSinceStartup + 15;
            Debug.Log("[UiSkin] " + message + "（阶段 " + stage + "）");
        }

        private static void CaptureViewports(string name)
        {
            Capture(name);
            Capture(name + "-1080p", 1920, 1080);
            Capture(name + "-narrow", 1024, 640);
        }

        private static void Capture(string name, int w = 1280, int h = 720)
        {
            var cams = Camera.allCameras.Where(c => c.enabled && c.targetTexture == null).OrderBy(c => c.depth).ToList();
            if (cams.Count == 0 && controller?.WorldCamera != null) cams.Add(controller.WorldCamera);
            if (cams.Count == 0) { Log.Add("[失败] " + name + "：找不到相机"); failed = true; return; }
            var fit = GameFrameLayout.Fit(w, h, 0, 0, w, h);
            var area = new Rect((float)fit.X, (float)fit.Y, (float)fit.Width, (float)fit.Height);
            foreach (var frame in Object.FindObjectsByType<GameFrameCanvas>()) frame.ApplyLayout(area, w, h);
            var rects = cams.ToDictionary(c => c, c => c.rect);
            var aspects = cams.ToDictionary(c => c, c => c.aspect);
            var targets = cams.ToDictionary(c => c, c => c.targetTexture);
            var rt = new RenderTexture(w, h, 24); rt.Create();
            var uiRt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32); uiRt.Create();
            var prev = RenderTexture.active;
            foreach (var camera in cams)
            {
                camera.rect = new Rect(0, 0, 1, 1);
                camera.targetTexture = rt;
                camera.aspect = w / (float)h;
                camera.Render();
            }
            var tex = Read(rt, w, h);
            var top = cams.LastOrDefault();
            var canvases = Object.FindObjectsByType<Canvas>().Where(c => c.isRootCanvas && c.isActiveAndEnabled && c.renderMode == RenderMode.ScreenSpaceOverlay).OrderBy(c => c.sortingOrder).ToArray();
            var layers = new Dictionary<GameObject, int>();
            if (top != null && canvases.Length > 0)
            {
                int mask = top.cullingMask; var clear = top.clearFlags; var bg = top.backgroundColor;
                foreach (var canvas in canvases)
                {
                    foreach (var t in canvas.GetComponentsInChildren<Transform>(true)) { layers[t.gameObject] = t.gameObject.layer; t.gameObject.layer = 31; }
                    canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = top; canvas.planeDistance = Mathf.Min(top.farClipPlane * .5f, top.nearClipPlane + 1f);
                }
                Canvas.ForceUpdateCanvases();
                top.targetTexture = uiRt; top.aspect = w / (float)h; top.cullingMask = 1 << 31; top.clearFlags = CameraClearFlags.SolidColor; top.backgroundColor = new Color(0, 0, 0, 0); top.Render();
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
            }
            foreach (var cam in cams) { cam.targetTexture = targets[cam]; cam.rect = rects[cam]; cam.aspect = aspects[cam]; }
            foreach (var frame in Object.FindObjectsByType<GameFrameCanvas>()) frame.Apply();
            RenderTexture.active = prev; rt.Release(); uiRt.Release();
            File.WriteAllBytes(Path.Combine(OutDir, name + ".png"), tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            Log.Add("## " + name + " ｜ 地点：" + controller?.State?.Campaign.Location + " ｜ 阶段：" + controller?.State?.Demo?.Stage
                + " ｜ 页面：" + (H != null && H.PanelOpen ? "有模态页" : "无模态页") + " ｜ Canvas=" + canvases.Length);
            Debug.Log("[UiSkin] 截图 " + name);
        }

        private static Texture2D Read(RenderTexture rt, int w, int h)
        {
            RenderTexture.active = rt; var tex = new Texture2D(w, h, TextureFormat.RGBA32, false); tex.ReadPixels(new Rect(0, 0, w, h), 0, 0); tex.Apply(); return tex;
        }

        private static void Finish(int code, string message)
        {
            EditorApplication.update -= Tick;
            Log.Insert(0, "exit=" + code + " " + message + " ｜ " + DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
            File.WriteAllText(Path.Combine(OutDir, "ui-skin-report.txt"), string.Join("\n", Log));
            if (code == 0) Debug.Log("[UiSkin] " + message); else Debug.LogError("[UiSkin] " + message);
            failed = code != 0;
            EditorApplication.isPlaying = false;
            EditorApplication.Exit(code);
        }
    }
}

using BaseConquest.Base;
using BaseConquest.Presentation;
using BaseConquest.Story;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace BaseConquest.Battle
{
    public sealed partial class BattleMobileHud
    {
        public readonly GameObject Root;
        public readonly Canvas Canvas;
        private readonly BattleSceneController owner;
        private readonly BaseSceneController practiceScene;
        private readonly Font font;
        private readonly RectTransform safe, infoPanel, logPanel;
        private readonly RectTransform realtimeMinimap;
        private readonly List<RectTransform> hits = new List<RectTransform>();
        private readonly Button step, automatic, fast, skip, cancel, switchSide, drawer;
        private readonly Button draftStart;
        private readonly Button undo, withdraw, preparationUndo, pursue, settlement, failureRestart, testRestart, reportButton;
        private RectTransform realtimeControls, practiceInstruction;
        private readonly Button[] realtimeStrategies = new Button[3];
        private Button realtimePause, realtimeSpeed;
        private GuidanceArrow practiceArrow;
        private Text practiceInstructionText;
        private readonly BattleResultPanel battleResult;
        private readonly RectTransform draftControls, preparationPanel, fightingPanel, resultPanel;
        private readonly Text[] troopStatus = new Text[BattleSimulation.TroopKinds.Length];
        private readonly RectTransform cellMenu, cellMenuContent;
        private readonly Text cellMenuTitle;
        private readonly ScrollRect cellMenuScroll;
        private readonly List<Button> cellMenuButtons = new List<Button>();
        private readonly RectTransform details, detailBackdrop;
        private readonly Button closeDetails;
        private DemoButtonTint practiceCue;
        private readonly Text detailsInfo, detailsEffects;
        private readonly RectTransform detailEffectsPanel;
        private readonly BattleAvatarGraphic detailsPortrait;
        private readonly RectTransform[] rangeRows = new RectTransform[4];
        private readonly BattleLogLinks logLinks;
        private readonly Text[] rangeLabels = new Text[4];
        // P36 三档：手动／进攻／驻守。逃跑保留枚举值以兼容旧档，但不再进 UI；旧档读取时按驻守处理。
        private static readonly SquadStrategy[] TierStrategies = { SquadStrategy.Obey, SquadStrategy.Attack, SquadStrategy.Protect };
        private readonly Button[] strategyButtons = new Button[TierStrategies.Length];
        private readonly RectTransform armyPanel;
        private readonly List<GameObject> testHidden = new List<GameObject>();
        private readonly BattleSkillChargeGraphic[] skillCharge = new BattleSkillChargeGraphic[3];
        private readonly Button[] skills = new Button[3];
        private readonly Button[] cards = new Button[BattleSimulation.TroopKinds.Length], portraits = new Button[3];
        private readonly RectTransform[] portraitGraphics = new RectTransform[3];
        private readonly Text[] portraitLabels = new Text[3];
        private readonly Text information, log, status, heading;
        private readonly ScrollRect logScroll, informationScroll;
        private readonly Text objectiveTitle, objectiveProgress;
        private readonly Image objectiveBarFill;
        private readonly RectTransform objectiveBar;
        private readonly Image objectivePanel;
        private readonly RectTransform objectivePanelRect;
        private readonly Vector2 drawerPosition, detailsSize, effectsSize, objectiveFillSize, cellMenuOffset;
        private readonly float cellMenuWidth;
        private readonly Vector2[] portraitPositions = new Vector2[3];
        private readonly Quaternion[] portraitRotations = new Quaternion[3];
        private readonly Dictionary<Button, (Image Surface, Text Label, Color Background, Color Ink)> restingColors =
            new Dictionary<Button, (Image, Text, Color, Color)>();
        private readonly Color objectiveTitleColor, objectiveProgressColor;
        private string informationKey;
        private int eventCount = -1;
        private bool showedDraft;
        public BattleMobileHud(BattleSceneController owner, Transform parent, Font font)
        {
            this.owner = owner; this.font = font;
            var sceneOwner = parent.GetComponentInParent<BaseSceneController>();
            practiceScene = owner.IsDemoPractice ? sceneOwner : null;
            var sceneShell = sceneOwner?.SceneUiTemplate("BattleShell");
            Root = UiPrefabBinding.Create("BattleShell", "BattleHud", parent, true, sceneShell, sceneShell != null);
            Canvas = Root.GetComponent<Canvas>(); Canvas.renderMode = RenderMode.ScreenSpaceOverlay; Canvas.sortingOrder = 50;
            Root.GetComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            safe = Rect("SafeArea", Root.transform, Vector2.zero, Vector2.zero, Vector2.zero); safe.anchorMax = Vector2.one; safe.offsetMin = safe.offsetMax = Vector2.zero;
            CreateFormalBanner();
            var map = Box("Minimap", safe, new Vector2(0, 1), new Vector2(10, -10), new Vector2(66, 48)); realtimeMinimap=map;Label("小地图\n占位", map, 12);if(owner.IsSceneEncounter)map.gameObject.SetActive(false);
            drawer = Button("测试", safe, new Vector2(0, 1), new Vector2(82, -10), new Vector2(66, 44), owner.ToggleDrawer);
            switchSide = Button("切换阵营", safe, new Vector2(0, 1), new Vector2(154, -10), new Vector2(90, 44), owner.SwitchSide);
            var objective = Box("Objective", safe, new Vector2(0.5f, 1), new Vector2(0, -10), new Vector2(210, 44)); objective.name = "Objective";
            objectivePanelRect = objective;
            objectivePanel = objective.GetComponent<Image>();
            objectiveTitle = Label("", objective, 11); objectiveTitle.name = "ObjectiveTitle"; objectiveTitle.alignment = TextAnchor.MiddleCenter; objectiveTitle.rectTransform.offsetMin = new Vector2(6, 27); objectiveTitle.rectTransform.offsetMax = new Vector2(-6, -2);
            objectiveBar = Rect("ObjectiveBar", objective, new Vector2(0, 1), new Vector2(8, -20), new Vector2(194, 4)); objectiveBar.gameObject.GetOrAdd<Image>().color = new Color(1, 1, 1, 0.18f);
            objectiveBarFill = Rect("ObjectiveBarFill", objectiveBar, Vector2.zero, Vector2.zero, new Vector2(194, 4)).gameObject.GetOrAdd<Image>(); objectiveBarFill.color = DemoUiTheme.Current.Copper;
            objectiveProgress = Label("", objective, 10); objectiveProgress.name = "ObjectiveProgress"; objectiveProgress.alignment = TextAnchor.MiddleCenter; objectiveProgress.rectTransform.offsetMin = new Vector2(6, 2); objectiveProgress.rectTransform.offsetMax = new Vector2(-6, -27);
            objectiveTitle.horizontalOverflow = objectiveProgress.horizontalOverflow = HorizontalWrapMode.Overflow;
            objectiveTitle.verticalOverflow = objectiveProgress.verticalOverflow = VerticalWrapMode.Overflow;
            automatic = Button("自动", safe, new Vector2(1, 1), new Vector2(-124, -10), new Vector2(50, 44), owner.ToggleAutomatic);
            fast = Button("3倍", safe, new Vector2(1, 1), new Vector2(-68, -10), new Vector2(50, 44), owner.ToggleFast);
            skip = Button("跳过", safe, new Vector2(1, 1), new Vector2(-12, -10), new Vector2(50, 44), owner.SkipBattle);
            var army = Box("ControlledArmy", safe, new Vector2(0, 1), new Vector2(10, -76), new Vector2(118, 264)); armyPanel = army; heading = Label("", army, 12); heading.rectTransform.anchorMin = new Vector2(0, 0.85f);
            preparationPanel = Box("PreparationControls", safe, new Vector2(1, 0), new Vector2(-12, 44), new Vector2(296, 112));
            draftControls = Rect("DraftPreparationControls", safe, new Vector2(1, 0), new Vector2(-14, 44), new Vector2(438, 44));
            DraftButton("Back", StoryText.Get("story.ui.prep.deployment_back", "返回备战"), 0, owner.ReturnFromDeploymentDraft);
            DraftButton("Auto", StoryText.Get("story.ui.prep.deployment_auto", "一键布置"), 111, owner.AutoPlaceDraftDeployment);
            DraftButton("Clear", StoryText.Get("story.ui.prep.deployment_clear", "一键撤销"), 222, owner.ClearDraftDeployment);
            draftStart = DraftButton("Start", StoryText.Get("story.ui.prep.deployment_confirm", "开战"), 333, owner.ToggleDraftDeployment);
            fightingPanel = Box("FightingControls", safe, new Vector2(1, 0), new Vector2(-12, 44), new Vector2(296, 122));
            resultPanel = Box("ResultControls", safe, new Vector2(1, 0), new Vector2(-12, 44), new Vector2(296, 62));
            var kinds = BattleSimulation.TroopKinds;
            for (int i = 0; i < kinds.Length; i++)
            {
                var kind = kinds[i]; cards[i] = Button("", army, new Vector2(0, 1), new Vector2(4, -36 - i * 56), new Vector2(110, 50), () => owner.ChooseDeployment(kind)); cards[i].name = "Deploy_" + kind;
                troopStatus[i] = cards[i].GetComponentInChildren<Text>(); troopStatus[i].fontSize = 12;
            }
            Button("一键布兵", preparationPanel, Vector2.zero, new Vector2(8, 58), new Vector2(88, 44), owner.AutoDeployControlled);
            Button("一键清场", preparationPanel, Vector2.zero, new Vector2(8, 8), new Vector2(88, 44), owner.StartManualDeployment);
            withdraw = Button("撤回所选单位", preparationPanel, Vector2.zero, new Vector2(104, 58), new Vector2(88, 44), owner.WithdrawSelected);
            preparationUndo = Button("撤回上一操作", preparationPanel, Vector2.zero, new Vector2(104, 8), new Vector2(88, 44), owner.UndoOneAction);
            Button("开战", preparationPanel, Vector2.zero, new Vector2(200, 58), new Vector2(88, 44), owner.StartBattle);
            step = Button("下一轮", fightingPanel, Vector2.zero, new Vector2(8, 54), new Vector2(280, 60), owner.AdvanceOne);
            step.GetComponentInChildren<Text>().fontSize = 18;
            CreateRealtimeControls();
            cancel = Button("清除所有指令", fightingPanel, Vector2.zero, new Vector2(8, 8), new Vector2(136, 38), owner.CancelAllOrders);
            undo = Button("清除上一指令", fightingPanel, Vector2.zero, new Vector2(152, 8), new Vector2(136, 38), owner.UndoOneAction);
            testRestart = Button("重开测试", safe, new Vector2(1, 0), new Vector2(-12, 168), new Vector2(92, 38), owner.RestartTestBattle); testRestart.name = "BattleTestRestart";
            pursue = Button("乘胜追击", resultPanel, Vector2.zero, new Vector2(8, 8), new Vector2(136, 44), owner.PursueVictory);
            settlement = Button("直接结算", resultPanel, Vector2.zero, new Vector2(152, 8), new Vector2(136, 44), owner.SettleVictory);
            failureRestart = Button("重开测试", resultPanel, Vector2.zero, new Vector2(152, 8), new Vector2(136, 44), owner.RestartTestBattle);
            testHidden.Add(map.gameObject); testHidden.Add(switchSide.gameObject);
            infoPanel = Box("SelectedUnitInformation", safe, new Vector2(1, 1), new Vector2(-12, -76), new Vector2(212, 205));
            var dismissInformation=infoPanel.gameObject.GetOrAdd<Button>();dismissInformation.targetGraphic=infoPanel.GetComponent<Image>();dismissInformation.onClick.AddListener(owner.DismissInformationNotice);
            var infoViewport = Rect("InformationViewport", infoPanel, Vector2.zero, Vector2.zero, Vector2.zero); infoViewport.anchorMax = Vector2.one; infoViewport.offsetMin = new Vector2(8, 6); infoViewport.offsetMax = new Vector2(-8, -8); infoViewport.gameObject.GetOrAdd<RectMask2D>();
            var infoContent = Rect("InformationContent", infoViewport, new Vector2(0, 1), Vector2.zero, Vector2.zero); infoContent.anchorMin = new Vector2(0, 1); infoContent.anchorMax = Vector2.one; infoContent.pivot = new Vector2(0, 1);
            information = Label("点击格查看全部单位信息", infoContent, 12); information.alignment = TextAnchor.UpperLeft; information.verticalOverflow = VerticalWrapMode.Overflow;
            informationScroll = infoPanel.gameObject.GetOrAdd<ScrollRect>(); informationScroll.viewport = infoViewport; informationScroll.content = infoContent; informationScroll.horizontal = false; informationScroll.movementType = ScrollRect.MovementType.Clamped;
            logPanel = Box("BattleLogPanel", safe, new Vector2(1, 1), new Vector2(-12, -290), new Vector2(212, 150));
            var logTitle = Label("战斗日志", logPanel, 13); logTitle.rectTransform.anchorMin = new Vector2(0, 0.91f);
            var viewport = Rect("LogViewport", logPanel, Vector2.zero, Vector2.zero, Vector2.zero); viewport.anchorMax = Vector2.one; viewport.offsetMin = new Vector2(8, 6); viewport.offsetMax = new Vector2(-8, -26); viewport.gameObject.GetOrAdd<RectMask2D>();
            var content = Rect("LogContent", viewport, new Vector2(0, 1), Vector2.zero, new Vector2(190, 100)); content.pivot = new Vector2(0, 1); content.anchorMax = Vector2.one; content.anchorMin = new Vector2(0, 1);
            log = Label("战斗尚未开始", content, 12); log.alignment = TextAnchor.UpperLeft; log.horizontalOverflow = HorizontalWrapMode.Wrap; log.verticalOverflow = VerticalWrapMode.Overflow;
            log.raycastTarget = true; logLinks = log.gameObject.GetOrAdd<BattleLogLinks>(); logLinks.Owner = owner;
            logScroll = logPanel.gameObject.GetOrAdd<ScrollRect>(); logScroll.viewport = viewport; logScroll.content = content; logScroll.horizontal = false; logScroll.movementType = ScrollRect.MovementType.Clamped;
            for (int i = 0; i < 3; i++)
            {
                int slot = i; var module = Rect("CommanderSlot" + i, safe, Vector2.zero, new Vector2(10 + i * 100, 44), new Vector2(92, 130));
                portraits[i] = Button("", module, Vector2.zero, new Vector2(0, owner.FormalFieldHud ? 0 : 44), new Vector2(92, 86), () => owner.ClickCommanderPortrait(slot)); portraits[i].name = "CommanderPortrait_" + i;
                CreateFormalTroops(module, slot);
                var press = portraits[i].gameObject.GetOrAdd<BattlePortraitPress>(); press.Owner = owner; press.Slot = i;
                portraitGraphics[i] = Rect("CommanderIcon", portraits[i].transform, new Vector2(0.5f, 0.7f), Vector2.zero, new Vector2(38, 38)); var avatar = portraitGraphics[i].gameObject.GetOrAdd<BattleAvatarGraphic>(); avatar.color = Color.white; avatar.raycastTarget = false;
                portraitLabels[i] = Label("", portraits[i].transform, 11); portraitLabels[i].rectTransform.anchorMax = new Vector2(1, 0.43f);
                skills[i] = Button("战技", module, Vector2.zero, new Vector2(0, owner.FormalFieldHud ? 91 : 0), new Vector2(owner.FormalFieldHud ? 60 : 44, 38), () => owner.ClickCommanderSkill(slot)); skills[i].GetComponentInChildren<Text>().fontSize = 12; skills[i].GetComponentInChildren<Text>().fontStyle = FontStyle.Bold; var chargeRect = Rect("SkillCharge", skills[i].transform, Vector2.zero, Vector2.zero, Vector2.zero); chargeRect.anchorMax = Vector2.one; chargeRect.offsetMin = chargeRect.offsetMax = Vector2.zero; chargeRect.SetAsFirstSibling(); skillCharge[i] = chargeRect.gameObject.GetOrAdd<BattleSkillChargeGraphic>(); skillCharge[i].raycastTarget = false;
                skills[i].transition = Selectable.Transition.None;
                var skillShadow = skills[i].GetComponentInChildren<Text>().gameObject.GetOrAdd<Shadow>();
                skillShadow.effectColor = new Color(0, 0, 0, 0.95f); skillShadow.effectDistance = new Vector2(1, -1);
            }
            var statusPanel = Box("BottomInformation", safe, Vector2.zero, Vector2.zero, new Vector2(640, 38)); statusPanel.anchorMax = new Vector2(1, 0); statusPanel.offsetMin = new Vector2(0, 0); statusPanel.offsetMax = new Vector2(0, 38); status = Label("", statusPanel, 12);
            status.rectTransform.offsetMin = new Vector2(12, 3); status.rectTransform.offsetMax = new Vector2(-12, -3); status.horizontalOverflow = HorizontalWrapMode.Wrap;
            cellMenu = Box("CellCommandMenu", safe, Vector2.zero, Vector2.zero, new Vector2(224, 100));
            cellMenuTitle = Label("格子快捷菜单", cellMenu, 12); cellMenuTitle.alignment = TextAnchor.UpperLeft; cellMenuTitle.rectTransform.anchorMin = new Vector2(0, 1); cellMenuTitle.rectTransform.anchorMax = Vector2.one; cellMenuTitle.rectTransform.offsetMin = new Vector2(8, -26); cellMenuTitle.rectTransform.offsetMax = new Vector2(-8, -4);
            var cellViewport = Rect("CellCommandViewport", cellMenu, Vector2.zero, Vector2.zero, Vector2.zero); cellViewport.anchorMax = Vector2.one; cellViewport.offsetMin = new Vector2(4, 4); cellViewport.offsetMax = new Vector2(-4, -28); cellViewport.gameObject.GetOrAdd<RectMask2D>();
            cellMenuContent = Rect("CellCommands", cellViewport, new Vector2(0, 1), Vector2.zero, new Vector2(216, 60)); cellMenuContent.anchorMin = new Vector2(0, 1); cellMenuContent.anchorMax = Vector2.one; cellMenuContent.pivot = new Vector2(0, 1);
            cellMenuScroll = cellMenu.gameObject.GetOrAdd<ScrollRect>(); cellMenuScroll.viewport = cellViewport; cellMenuScroll.content = cellMenuContent; cellMenuScroll.horizontal = false; cellMenuScroll.movementType = ScrollRect.MovementType.Clamped; cellMenu.gameObject.SetActive(false);
            detailBackdrop = Box("CommanderDetailBackdrop", safe, Vector2.zero, Vector2.zero, Vector2.zero); detailBackdrop.anchorMax = Vector2.one; detailBackdrop.offsetMin = detailBackdrop.offsetMax = Vector2.zero; detailBackdrop.GetComponent<Image>().color = DemoUiTheme.Current.Shade;
            details = Box("CommanderDetails", safe, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(720, 540));
            var figure = Rect("UnitPortrait", details, new Vector2(0, 1), new Vector2(18, -18), new Vector2(105, 160));
            detailsPortrait = figure.gameObject.GetOrAdd<BattleAvatarGraphic>(); detailsPortrait.raycastTarget = false;
            detailsInfo = Label("", details, 13); detailsInfo.alignment = TextAnchor.UpperLeft;
            detailsInfo.rectTransform.anchorMin = new Vector2(0, 1); detailsInfo.rectTransform.anchorMax = Vector2.one;
            detailsInfo.rectTransform.offsetMin = new Vector2(142, -230); detailsInfo.rectTransform.offsetMax = new Vector2(-236, -14);
            detailEffectsPanel = Box("UnitStatusEffects", details, new Vector2(0, 1), new Vector2(508, -14), new Vector2(196, 472));
            var effectsTitle = Label("状态效果", detailEffectsPanel, 14); effectsTitle.rectTransform.anchorMin = new Vector2(0, 1); effectsTitle.rectTransform.offsetMin = new Vector2(8, -30); effectsTitle.rectTransform.offsetMax = new Vector2(-8, -4);
            detailsEffects = Label("", detailEffectsPanel, 12); detailsEffects.alignment = TextAnchor.UpperLeft; detailsEffects.rectTransform.offsetMin = new Vector2(8, 8); detailsEffects.rectTransform.offsetMax = new Vector2(-8, -36);
            for (int i = 0; i < 4; i++)
            {
                int range = i; var row = Rect("RangeRow" + i, details, new Vector2(0, 1), new Vector2(16, -244 - i * 44), new Vector2(468, 40)); rangeRows[i] = row;
                rangeLabels[i] = Label("", row, 13); rangeLabels[i].rectTransform.anchorMax = new Vector2(0.7f, 1);
                Button("−", row, new Vector2(1, 1), new Vector2(-48, 0), new Vector2(40, 40), () => owner.AdjustCommanderRange(range, -1)).name = "RangeMinus_" + i;
                Button("＋", row, new Vector2(1, 1), Vector2.zero, new Vector2(40, 40), () => owner.AdjustCommanderRange(range, 1)).name = "RangePlus_" + i;
            }
            for (int i = 0; i < TierStrategies.Length; i++)
            {
                var strategy = TierStrategies[i]; strategyButtons[i] = Button(BattleSimulation.StrategyName(strategy), details, new Vector2(0, 1), new Vector2(16 + i * 158, -430), new Vector2(152, 42), () => owner.ChooseSquadStrategy(strategy)); strategyButtons[i].name = "Strategy_" + strategy;if(strategy==SquadStrategy.Attack)DemoUiIcons.Attach(strategyButtons[i],"attack");
            }
            closeDetails = Button("关闭详情", details, new Vector2(0.5f, 0), new Vector2(0, 10), new Vector2(130, 38), owner.CloseCommanderDetails);
            if (practiceScene != null) CreatePracticeGuide();
            if (Application.isPlaying && EventSystem.current == null)
            {
                var es = new GameObject("BattleEventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule)); es.transform.SetParent(Root.transform, false); es.GetComponent<InputSystemUIInputModule>().AssignDefaultActions();
            }
            battleResult = new BattleResultPanel(safe, font, owner); hits.Add(battleResult.Blocker);
            reportButton = Button("查看战报", resultPanel, Vector2.zero, new Vector2(8, 8), new Vector2(136,44), () => { battleResult.Open(); Refresh(); });
            UiPrefabBinding.Complete(Root);
            if (owner.FormalFieldHud) ApplyFormalLayout(sceneShell != null && sceneShell.transform.Find("SafeArea/FormalBanner") != null);
            if (owner.UsesCampaignSystemMenu) ApplySystemMenuLayout(sceneOwner?.SceneUiTemplate("BaseShell")?.transform.Find("Header/菜单") as RectTransform);
            if (Application.platform == RuntimePlatform.Android) ApplyAndroidControls();
            drawerPosition = ((RectTransform)drawer.transform).anchoredPosition;
            detailsSize = details.sizeDelta;
            effectsSize = detailEffectsPanel.sizeDelta;
            objectiveFillSize = objectiveBarFill.rectTransform.sizeDelta;
            cellMenuOffset = cellMenu.anchoredPosition;
            cellMenuWidth = cellMenu.rect.width;
            objectiveTitleColor = objectiveTitle.color;
            objectiveProgressColor = objectiveProgress.color;
            for (int i = 0; i < portraits.Length; i++)
            {
                portraitPositions[i] = ((RectTransform)portraits[i].transform).anchoredPosition;
                portraitRotations[i] = portraitGraphics[i].localRotation;
            }
            foreach (var button in new[] { automatic, fast, realtimePause, realtimeSpeed }.Concat(cards).Concat(portraits).Concat(strategyButtons).Concat(realtimeStrategies).Concat(formalTroops.Cast<Button>()))
            {
                var image = button.GetComponent<Image>();
                var text = button.GetComponentInChildren<Text>(true);
                restingColors.Add(button, (image, text, image.color, text.color));
            }
            Refresh();
        }
        private RectTransform Rect(string name, Transform parent, Vector2 anchor, Vector2 pos, Vector2 size)
        {
            var r = UiPrefabBinding.Rect(name, parent); r.anchorMin = r.anchorMax = r.pivot = anchor; r.anchoredPosition = pos; r.sizeDelta = size; return r;
        }
        private RectTransform Box(string name, Transform parent, Vector2 anchor, Vector2 pos, Vector2 size)
        {
            var r = Rect(name, parent, anchor, pos, size); r.gameObject.GetOrAdd<Image>().color = DemoUiTheme.Current.Panel; hits.Add(r); return r;
        }
        private Text Label(string value, Transform parent, int size, string name = "Label")
        {
            var r = Rect(name, parent, Vector2.zero, Vector2.zero, Vector2.zero); r.anchorMax = Vector2.one; r.offsetMin = r.offsetMax = Vector2.zero;
            var t = r.gameObject.GetOrAdd<Text>(); t.font = font; t.fontSize = size; t.text = value; t.color = DemoUiTheme.Current.Ink; t.alignment = TextAnchor.MiddleCenter; t.raycastTarget = false; return t;
        }
        private Button Button(string name, Transform parent, Vector2 anchor, Vector2 pos, Vector2 size, Action action)
        {
            var r = Box(name, parent, anchor, pos, size); var b = r.gameObject.GetOrAdd<Button>(); b.targetGraphic = r.GetComponent<Image>(); DemoUiTheme.Current.StyleButton(b); Label(name, r, 13); if (action != null) b.onClick.AddListener(() => action()); return b;
        }

        private Button DraftButton(string name, string text, float x, Action action)
        {
            var button = Button(name, draftControls, Vector2.zero, new Vector2(x, 2), new Vector2(104, 40), action);
            button.GetComponentInChildren<Text>(true).text = text;
            return button;
        }

        private void SetHighlighted(Button button, bool marked)
        {
            var resting = restingColors[button];
            resting.Surface.color = marked ? DemoUiTheme.Current.Gold : resting.Background;
            resting.Label.color = marked ? DemoUiTheme.Current.Ink : resting.Ink;
        }
        public void ShowMenu(Vector2 point)
        {
            if (!owner.ContextCell.HasValue) return;
            var commands = owner.CellCommands(owner.ContextCell.Value); Canvas.ForceUpdateCanvases();
            while (cellMenuButtons.Count < commands.Count)
            {
                int index = cellMenuButtons.Count;
                var button = Button("", cellMenuContent, new Vector2(0, 1), new Vector2(0, -index * 36), new Vector2(Mathf.Max(1, cellMenuWidth - 8), 32), null);
                button.GetComponentInChildren<Text>().fontSize = 11; cellMenuButtons.Add(button);
            }
            for (int i = 0; i < cellMenuButtons.Count; i++)
            {
                var button = cellMenuButtons[i]; button.gameObject.SetActive(i < commands.Count); button.onClick.RemoveAllListeners();
                if (i >= commands.Count) continue;
                var command = commands[i]; button.name = "CellCommand_" + command.Kind + "_" + command.UnitId; button.GetComponentInChildren<Text>().text = command.Label;
                button.onClick.AddListener(() => owner.ConfirmCellCommand(command));
            }
            cellMenuTitle.text = "格 " + owner.ContextCell.Value + " · 快捷操作";
            cellMenuContent.sizeDelta = new Vector2(0, commands.Count * 36);
            float height = Mathf.Min(commands.Count * 36 + 32, Mathf.Max(100, safe.rect.height - 24));
            PositionCellMenu(point, height);
        }
        public void CloseMenu() => cellMenu.gameObject.SetActive(false);
        public void ShowRealtimeCandidates(Vector2 point,List<BattleSoldier> units,Action<BattleSoldier> choose)
        {
            Canvas.ForceUpdateCanvases();
            while(cellMenuButtons.Count<units.Count)cellMenuButtons.Add(Button("",cellMenuContent,new Vector2(0,1),Vector2.zero,new Vector2(Mathf.Max(1, cellMenuWidth - 8),36),null));
            for(int i=0;i<cellMenuButtons.Count;i++){
                var button=cellMenuButtons[i];button.gameObject.SetActive(i<units.Count);button.onClick.RemoveAllListeners();if(i>=units.Count)continue;
                var unit=units[i];button.name="UnitCandidate_"+unit.Id;button.GetComponent<RectTransform>().anchoredPosition=new Vector2(0,-i*40);button.GetComponentInChildren<Text>().text=(unit.Side==BattleSide.A?"选中 ":"指定目标 ")+owner.PreviewSimulation.UnitName(unit);button.onClick.AddListener(()=>choose(unit));
            }
            cellMenuTitle.text="选择单位";cellMenuContent.sizeDelta=new Vector2(0,units.Count*40);
            float height=Mathf.Min(units.Count*40+34,safe.rect.height-24);
            PositionCellMenu(point, height);
        }
        private void PositionCellMenu(Vector2 point, float height)
        {
            cellMenu.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(safe, point, Canvas.worldCamera, out var local);
            var corner = local - safe.rect.min + cellMenuOffset;
            cellMenu.anchoredPosition = new Vector2(
                Mathf.Clamp(corner.x + 10, 4, Mathf.Max(4, safe.rect.width - cellMenuWidth - 4)),
                Mathf.Clamp(corner.y - height / 2, 4, Mathf.Max(4, safe.rect.height - height - 4)));
            cellMenu.gameObject.SetActive(true);
            cellMenu.SetAsLastSibling();
            cellMenuScroll.StopMovement();
            cellMenuScroll.verticalNormalizedPosition = 1;
        }
        public bool ReportVisible => battleResult?.Visible == true;
        public bool ContextMenuVisible => cellMenu.gameObject.activeSelf;
        public void CloseReport() => battleResult?.Close();
        public bool BlocksPointer(Vector2 point) => hits.Any(r => r.gameObject.activeInHierarchy && RectTransformUtility.RectangleContainsScreenPoint(r, point, Canvas.worldCamera));
        public IEnumerable<int> PortraitSlotsBetween(Vector2 from, Vector2 to)
        {
            var touched = new List<KeyValuePair<float, int>>(); var camera = Canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : Canvas.worldCamera;
            for (int i = 0; i < portraits.Length; i++)
            {
                if (!portraits[i].interactable || !portraits[i].gameObject.activeInHierarchy) continue;
                var corners = new Vector3[4]; portraits[i].GetComponent<RectTransform>().GetWorldCorners(corners);
                var a = RectTransformUtility.WorldToScreenPoint(camera, corners[0]); var b = RectTransformUtility.WorldToScreenPoint(camera, corners[2]);
                float t0 = 0, t1 = 1; bool crossed = true;
                for (int axis = 0; axis < 2; axis++)
                {
                    float delta = to[axis] - from[axis];
                    if (Mathf.Abs(delta) < 0.001f) { if (from[axis] < a[axis] || from[axis] > b[axis]) crossed = false; continue; }
                    float entry = (a[axis] - from[axis]) / delta, exit = (b[axis] - from[axis]) / delta;
                    t0 = Mathf.Max(t0, Mathf.Min(entry, exit)); t1 = Mathf.Min(t1, Mathf.Max(entry, exit));
                }
                if (crossed && t0 <= t1) touched.Add(new KeyValuePair<float, int>(t0, i));
            }
            return touched.OrderBy(p => p.Key).Select(p => p.Value);
        }
        public void Refresh()
        {
            var b = owner.PreviewSimulation; Root.GetComponent<CanvasScaler>().scaleFactor = owner.HudScale;
            if (owner.DeploymentDraftActive) { RefreshDraft(b); return; }
            showedDraft = false;
            draftControls.gameObject.SetActive(false);
            armyPanel.gameObject.SetActive(!owner.DrawerVisible && !owner.FormalFieldHud);
            ((RectTransform)drawer.transform).anchoredPosition = drawerPosition + (owner.DrawerVisible ? Vector2.right * 278 : Vector2.zero);
            foreach (var element in testHidden) element.SetActive(!owner.DrawerVisible);
            drawer.GetComponentInChildren<Text>().text = owner.DrawerVisible ? "收起测试" : "测试";
            if (owner.IsCampaignBattle) { drawer.GetComponentInChildren<Text>().text = "菜单"; drawer.interactable = !owner.IsAnimating && !owner.Skipping; switchSide.gameObject.SetActive(false); }
            else drawer.interactable = true;
            foreach (var portrait in portraits) portrait.transform.parent.gameObject.SetActive(!owner.DrawerVisible);
            var area=BaseConquest.Presentation.GameFrameCanvas.Area;
            bool won = b.VictoryDeclared || b.Report?.Outcome == BattleOutcome.SideAWin;
            RefreshObjective(b);
            bool after = won && !owner.PursuitChosen || b.Phase == BattlePhase.Finished;
            preparationPanel.gameObject.SetActive(!owner.DrawerVisible && !after && b.Phase == BattlePhase.Deployment);
            fightingPanel.gameObject.SetActive(!owner.IsRealtimeCampaign && !owner.DrawerVisible && !after && b.Phase == BattlePhase.Fighting);
            resultPanel.gameObject.SetActive(!owner.DrawerVisible && after && !owner.ContinuesThroughDialogue);
            bool finished = b.Phase == BattlePhase.Finished;
            pursue.gameObject.SetActive(won && !finished); settlement.gameObject.SetActive(won && !finished);
            failureRestart.gameObject.SetActive(!owner.IsCampaignBattle && (finished || !won)); reportButton.gameObject.SetActive(finished);
            pursue.interactable = b.Phase == BattlePhase.Fighting && !owner.IsAnimating; settlement.interactable = !owner.IsAnimating && !owner.Skipping && b.Phase != BattlePhase.Finished;
            settlement.GetComponentInChildren<Text>().text = b.Phase == BattlePhase.Finished ? "已结算" : "直接结算";
            pursue.GetComponentInChildren<Text>().text = "乘胜追击";
            testRestart.gameObject.SetActive(!owner.IsCampaignBattle && !owner.DrawerVisible && !after && b.Phase == BattlePhase.Fighting);
            bool fighting = b.Phase == BattlePhase.Fighting && !owner.VictoryChoicePending; automatic.interactable = fighting; skip.interactable = fighting; fast.interactable = !owner.Skipping;
            automatic.gameObject.SetActive(b.Phase == BattlePhase.Fighting && (!won || owner.PursuitChosen)); skip.gameObject.SetActive(b.Phase == BattlePhase.Fighting && (!won || owner.PursuitChosen)); fast.gameObject.SetActive(b.Phase == BattlePhase.Fighting);
            SetHighlighted(automatic, owner.Automatic); SetHighlighted(fast, owner.FastMode);
            step.GetComponentInChildren<Text>().text = "下一轮"; step.interactable = !owner.IsAnimating && !owner.Skipping;
            cancel.interactable = fighting && !owner.IsAnimating && !owner.Skipping;
            undo.interactable = preparationUndo.interactable = owner.CanUndoAction;
            switchSide.GetComponentInChildren<Text>().text = BattleSimulation.SideName(owner.ControlledSide) + "操作\n切换·测试"; switchSide.interactable = !owner.IsAnimating && !owner.Skipping;
            skip.GetComponentInChildren<Text>().text = owner.Skipping ? "停止" : "跳过";
            var slots = owner.SelectedDeploymentSlots;
            heading.text = (slots.Count == 0 ? BattleSimulation.SideName(owner.ControlledSide) + " · 全局" : slots.Count == 1 ? b.CommanderDisplayName(owner.ControlledSide, slots[0]) : "已选 " + slots.Count + " 队合计") + "\n场／损／备";
            var kinds = BattleSimulation.TroopKinds;
            for (int i = 0; i < kinds.Length; i++)
            {
                var k = kinds[i]; var armyStatus = b.ArmyStatus(owner.ControlledSide, k, owner.SelectedUnits);
                troopStatus[i].text = BattleSimulation.KindName(k) + "\n" + armyStatus.InField + "／" + armyStatus.Lost + "／" + armyStatus.Stock;

                cards[i].interactable = owner.CanChooseDeployment(k) || owner.DeploymentArmed && owner.DeploymentKind == k && !owner.IsAnimating;
                SetHighlighted(cards[i], owner.DeploymentArmed && owner.DeploymentKind == k);
            }
            withdraw.interactable = owner.CanWithdrawSelection;
            for (int i = 0; i < 3; i++)
            {
                var c = b.Commander(owner.ControlledSide, i); bool slotExists = i < b.Scenario.Army(owner.ControlledSide).HeroCount;
                portraits[i].transform.parent.gameObject.SetActive(slotExists);
                portraits[i].interactable = slotExists;
                skills[i].interactable = slotExists && owner.CanUseCommanderSkill(i);
                float chargeProgress = owner.CommanderSkillCharge(i);
                skillCharge[i].Show(chargeProgress, chargeProgress >= 1 && b.CanQueueCommanderSkill(owner.ControlledSide, i) || b.CommanderSkillQueued(owner.ControlledSide, i));
                var skillLabel = skills[i].GetComponentInChildren<Text>();
                skillLabel.color = skillCharge[i].Ready ? DemoUiTheme.Current.Ink : DemoUiTheme.Current.Panel;
                var labelShadow = skillLabel.GetComponent<Shadow>();
                if (labelShadow != null) labelShadow.enabled = !skillCharge[i].Ready;
                var chargeText = b.CommanderSkillQueued(owner.ControlledSide, i) ? "本轮后" : b.CommanderSkillCooldown(owner.ControlledSide, i) > 0 ? b.CommanderSkillCooldown(owner.ControlledSide, i).ToString() : "";
                skills[i].GetComponentInChildren<Text>().text = BattleSimulation.CommanderSkillName(i) + (chargeText == "" ? "" : "\n" + chargeText);
                portraitLabels[i].text = c == null ? slotExists ? b.CommanderDisplayName(owner.ControlledSide, i) + "\n待部署" : "空位" : b.CommanderDisplayName(c.Side, c.OriginCommanderSlot) + "\n" + (c.Rescued ? "已救出" : c.IsDowned ? "倒地·双击救援" : c.Escaped ? "已撤离" : c.Health.ToString("0") + "/" + c.Stats.Health.ToString("0"));
                portraitGraphics[i].GetComponent<BattleAvatarGraphic>().PlayerHero=(b.Scenario.CampaignRules||b.Scenario.PlayerHeroAppearance)&&PlayerHeroFrames.IsPlayer(true,owner.ControlledSide==BattleSide.A,i,b.Scenario.PlayerLeaderSlot);
                portraitGraphics[i].GetComponent<BattleAvatarGraphic>().AppearanceId=b.CommanderAppearance(owner.ControlledSide,i);
                portraitGraphics[i].localRotation = c?.IsDowned == true
                    ? portraitRotations[i] * Quaternion.Euler(0, 0, c.CarriedById >= 0 ? 90 : 90 + Mathf.Sin(Time.unscaledTime * 6) * 9)
                    : portraitRotations[i];
                bool selected = c != null && owner.SelectedUnits.Contains(c.Id);
                SetHighlighted(portraits[i], selected || c == null && owner.DeploymentSlotChosen && owner.DeploymentCommanderSlot == i);
                ((RectTransform)portraits[i].transform).anchoredPosition = portraitPositions[i] + (selected ? Vector2.up * 6 : Vector2.zero);
            }
            infoPanel.gameObject.SetActive(owner.InformationNoticeVisible);
            var descriptions = new List<string>();
            foreach (var inspected in owner.InformationUnits)
            {
                var commander = b.CommanderFor(inspected);
                descriptions.Add(b.UnitName(inspected) + "\n归属队长：" + (commander == null ? "无（自主作战）" : b.UnitName(commander) + (commander.IsActive ? "" : "（失能）"))
                    + "\n生命 " + CombatNumbers.RemainingHealth(inspected.Health) + "/" + inspected.Stats.Health.ToString("0")
                    + " · 攻击 " + b.AttackPower(inspected).ToString("0") + " · 防御 " + inspected.Stats.Defense.ToString("0")
                    + " · 视野 " + b.Vision.SightRadius(inspected) + " 格"
                    + "\n" + b.Behavior(inspected) + (b.EffectsSummary(inspected) == "" ? "" : "\n" + b.EffectsSummary(inspected)) + "\n长按单位查看详细属性");
            }
            information.text = descriptions.Count > 0 ? string.Join("\n\n", descriptions) : owner.InformationCell.HasValue && !b.Vision.Visible(owner.ControlledSide, owner.InformationCell.Value) ? "当前无视野，无法查看单位或地形" : "格内无可显示单位\n仅通过头像选队；地图队长点击只查看信息\n选队单击移动／集火\n双击空格或长按驻守此位置\n长按头像查看详情／设置范围";
            if (owner.InformationCell.HasValue) information.text += "\n\n" + CellInformation(b, owner.InformationCell.Value, owner.ControlledSide, owner.ShowsBattleExit(BattleSide.A), owner.ShowsBattleExit(BattleSide.B));
            informationScroll.content.sizeDelta = new Vector2(0, Mathf.Max(informationScroll.viewport.rect.height, information.preferredHeight + 8));
            string key = owner.InformationCell?.ToString() ?? "unit:" + owner.InformationUnitId;
            if (informationKey != key) { informationKey = key; informationScroll.StopMovement(); informationScroll.verticalNormalizedPosition = 1; }
            var known = b.KnownEvents(owner.ControlledSide).ToList(); log.supportRichText = true;
            logPanel.gameObject.SetActive(known.Count > 0);
            var recent = known.Skip(Math.Max(0, known.Count - 45)).ToList();
            bool darkLog = logPanel.GetComponent<Image>().color.grayscale < .5f;
            log.text = string.Join("\n", recent.Select(e => FormatEvent(b, e, owner.ControlledSide, darkLog)));
            logLinks.SetEntries(b, recent);
            logScroll.content.sizeDelta = new Vector2(0, Mathf.Max(logScroll.viewport.rect.height, log.preferredHeight + 8));
            if (known.Count != eventCount) { eventCount = known.Count; logScroll.verticalNormalizedPosition = 0; }
            status.text = BattleSimulation.SideName(owner.ControlledSide) + " · 第" + b.RoundNumber + "轮 · " + (owner.Automatic ? "自动" : "暂停") + " · " + (owner.FastMode ? "3" : "1") + "倍 · 已选" + owner.SelectedUnits.Count + "队\n" + owner.Feedback;
            if (b.VictoryDeclared && b.Phase == BattlePhase.Fighting) status.text = "已获胜 · 选择乘胜追击／直接结算 · 第" + b.RoundNumber + "轮\n" + owner.Feedback;
            if (b.Phase == BattlePhase.Finished) status.text = "战斗结束 · " + BattleSceneController.OutcomeName(b.Report.Outcome) + (owner.IsCampaignBattle ? " · 返回基地继续经营\n" + owner.Feedback : " · 可重开测试");
            status.transform.parent.gameObject.SetActive(!finished || !owner.ContinuesThroughDialogue);
            details.gameObject.SetActive(owner.DetailsVisible);
            detailBackdrop.gameObject.SetActive(owner.DetailsVisible);
            if (owner.DetailsVisible)
            {
                var unit = owner.DetailsUnit; var side = unit?.Side ?? owner.ControlledSide;
                bool commanderDetails = owner.DetailsCommander;
                var stats = unit?.Stats ?? b.Scenario.Army(side).CommanderProfile(owner.DetailsSlot);
                var leader = b.CommanderFor(unit);
                details.sizeDelta = new Vector2(detailsSize.x, commanderDetails ? detailsSize.y : detailsSize.y * (300f / 540f));
                detailsPortrait.Kind = unit?.Kind ?? SoldierKind.Hero;
                detailsPortrait.AppearanceId = unit?.AppearanceId ?? b.CommanderAppearance(side, owner.DetailsSlot);
                detailsPortrait.PlayerHero=(b.Scenario.CampaignRules||b.Scenario.PlayerHeroAppearance)&&PlayerHeroFrames.IsPlayer(detailsPortrait.Kind==SoldierKind.Hero,side==BattleSide.A,unit?.OriginCommanderSlot??owner.DetailsSlot,b.Scenario.PlayerLeaderSlot);
                detailsInfo.text = (unit == null ? b.CommanderDisplayName(side, owner.DetailsSlot) : b.UnitName(unit))
                    + "\n归属队长：" + (leader == null ? "无" : b.UnitName(leader))
                    + "\n生命 " + (unit?.Health ?? stats.Health).ToString("0") + "/" + stats.Health.ToString("0")
                    + " · 攻击 " + stats.Damage.ToString("0") + " · 防御 " + stats.Defense.ToString("0")
                    + "\n战力 " + stats.BaseBattlePower.ToString("0") + "（基础）"
                    + (unit == null ? "" : " · 视野 " + b.Vision.SightRadius(unit) + " 格")
                    + "\n攻击距离 " + stats.Range + " · 移动 " + (unit == null ? stats.MoveDistance : b.MovementSpeed(unit)) + " · 上轮脚力 " + (unit?.LastFootwork ?? 0)
                    + "\n暴击 " + stats.CritChance.ToString("P0") + " · 暴伤 " + stats.CritMultiplier.ToString("P0")
                    + "\n" + (unit == null ? "待部署" : b.Behavior(unit));
                for (int i = 0; i < 4; i++) { rangeRows[i].gameObject.SetActive(commanderDetails); foreach (var button in rangeRows[i].GetComponentsInChildren<Button>()) button.interactable = owner.CanEditDetailsCommander && !owner.IsAnimating && !owner.Skipping && b.Phase != BattlePhase.Finished; }
                foreach (var button in strategyButtons) button.gameObject.SetActive(commanderDetails);
                detailEffectsPanel.sizeDelta = new Vector2(effectsSize.x, Mathf.Max(1, effectsSize.y + details.sizeDelta.y - detailsSize.y));
                var state = unit == null ? "待部署" : b.EffectsSummary(unit).Replace("／", "\n");
                if (unit != null) { if (unit.IsDowned) state += "\n倒地，等待救援"; if (unit.CarriedById >= 0) state += "\n正在被携带"; if (unit.Escaped) state += "\n已撤离"; if (unit.IsTransit) state += "\n通行：不能攻击／战技"; }
                detailsEffects.text = string.IsNullOrWhiteSpace(state) ? "无临时状态效果" : state.Trim();
                if (commanderDetails)
                {
                    var c = b.Scenario.Army(side).Commanders[owner.DetailsSlot]; var q = b.CommanderSquad(side, owner.DetailsSlot);
                    detailsInfo.text += "\n" + b.CommanderSkillDescription(owner.DetailsSlot) + "\n驻守≤预警≤追击，范围外轮廓按格显示" + (q.GuardAssigned ? "\n实际范围 " + q.ActualGuardRange + "/" + q.ActualAlertRange + "/" + q.ActualAttackRange : "");
                    rangeLabels[0].text = "驻守范围 " + c.GuardRange; rangeLabels[1].text = "预警范围 " + c.AlertRange; rangeLabels[2].text = "攻击／追击上限 " + c.AttackRange; rangeLabels[3].text = "率领范围 " + c.LeadRange;
                    for (int i = 0; i < TierStrategies.Length; i++) { SetHighlighted(strategyButtons[i], c.Strategy == TierStrategies[i]); strategyButtons[i].interactable = owner.CanEditDetailsCommander && b.Phase != BattlePhase.Finished && !owner.IsAnimating && !owner.Skipping; }
                }
            }
            if(owner.IsRealtimeCampaign)RefreshRealtimeControls(b);
            RefreshFormalField(b);
            RefreshPracticeControl(b);
            battleResult?.Refresh(b, owner.ControlledSide, owner.IsAnimating, owner.Skipping, new Vector2(area.width, area.height) / owner.HudScale);
        }

        private void RefreshDraft(BattleSimulation battle)
        {
            if (!showedDraft)
            {
                showedDraft = true;
                preparationPanel.gameObject.SetActive(false);
                foreach (var control in new[] { automatic, fast, skip, switchSide, testRestart })
                    control.gameObject.SetActive(false);
                foreach (var panel in new[] { realtimeMinimap, fightingPanel, realtimeControls, resultPanel, infoPanel, logPanel, details, detailBackdrop })
                    panel.gameObject.SetActive(false);
            }

            bool placing = owner.DraftPlacementActive;
            var teams = owner.DraftTeams;
            drawer.GetComponentInChildren<Text>().text = "菜单";
            draftStart.interactable = owner.DraftCanConfirm;
            draftControls.gameObject.SetActive(placing);
            armyPanel.gameObject.SetActive(false);
            string erosion = owner.DraftErosionWarning;
            status.text = erosion == "" ? owner.Feedback : owner.Feedback + "\n" + erosion;
            for (int i = 0; i < portraits.Length; i++)
            {
                bool exists = i < teams.Count;
                portraits[i].transform.parent.gameObject.SetActive(exists);
                if (!exists) continue;
                var team = teams[i];
                var avatar = portraitGraphics[i].GetComponent<BattleAvatarGraphic>();
                avatar.PlayerHero = i == battle.Scenario.PlayerLeaderSlot;
                avatar.AppearanceId = battle.CommanderAppearance(BattleSide.A, i);
                portraitLabels[i].text = team.Name + $"\n{team.Health:0}/{team.MaxHealth:0}" + (owner.DraftTeamPlaced(i) ? "" : " · 待部署");
                portraits[i].interactable = placing;
                SetHighlighted(portraits[i], placing && owner.DraftTeamSlot == i);
                skills[i].gameObject.SetActive(false);
            }
            RefreshFormalField(battle);
        }

        private void RefreshRealtimeControls(BattleSimulation battle)
        {
            bool fighting=battle.Phase==BattlePhase.Fighting;
            foreach (var control in new[] { automatic, fast, skip }) control.gameObject.SetActive(false);
            realtimeMinimap.gameObject.SetActive(false);
            realtimeControls.gameObject.SetActive(fighting && !owner.DrawerVisible);
            realtimePause.GetComponentInChildren<Text>().text = owner.Automatic ? "暂停" : "继续";
            realtimeSpeed.GetComponentInChildren<Text>().text = owner.FastMode ? "3倍" : "1倍";
            SetHighlighted(realtimeSpeed, owner.FastMode);
            var selected = owner.SelectedRealtimeStrategies.Distinct().ToArray();
            for (int i = 0; i < realtimeStrategies.Length; i++)
            {
                realtimeStrategies[i].interactable = fighting && selected.Length > 0;
                SetHighlighted(realtimeStrategies[i], selected.Length == 1 && (int)selected[0] == (int)TierStrategies[i]);
            }
            if(fighting)foreach(var card in cards)card.interactable=false;
            for(int i=0;i<skills.Length;i++)
            {
                bool exists=i<battle.Scenario.Army(owner.ControlledSide).HeroCount;skills[i].gameObject.SetActive(exists);skills[i].interactable=owner.CanUseCommanderSkill(i);
                float charge=owner.CommanderSkillCharge(i);skillCharge[i].Show(charge,charge>=1&&skills[i].interactable);
                double cooldown=owner.Realtime?.SkillCooldown(owner.ControlledSide,i)??battle.Scenario.Effects.OpeningCooldowns[i];
                var label=skills[i].GetComponentInChildren<Text>();label.color=skillCharge[i].Ready?DemoUiTheme.Current.Ink:DemoUiTheme.Current.Panel;var shadow=label.GetComponent<Shadow>();if(shadow!=null)shadow.enabled=!skillCharge[i].Ready;
                label.text=(owner.Realtime?.SkillName(owner.ControlledSide,i)??BattleSimulation.CommanderSkillName(i))+(cooldown>1e-7?"\n"+Math.Ceiling(cooldown).ToString("0"):"");
            }
            for(int slot=0;slot<portraits.Length;slot++){var commander=battle.Commander(owner.ControlledSide,slot);if(commander?.IsDowned==true)portraitLabels[slot].text=battle.CommanderDisplayName(commander.Side,slot)+"\n倒地";}
            if(owner.InformationUnits.Count==0)information.text=owner.InformationCell.HasValue?(battle.CanInspectCell(owner.ControlledSide,owner.InformationCell.Value)?"格内无可显示单位":"当前无视野，无法查看单位或地形"):Application.platform == RuntimePlatform.Android
                ? "点格查看单位和地形\n点按或拖划头像选队，点空地移动，点敌人集火\n单指拖动战场平移，双指捏合缩放"
                : "点格子查看格内单位与地形\n点队长或头像选队，Shift点队长单选\n点空地移动／点敌人集火\n空格继续／暂停";
            else information.text=string.Join("\n\n",owner.InformationUnits.Select(unit=>battle.UnitName(unit)+"\n归属队长："+(battle.CommanderFor(unit) is BattleSoldier commander?battle.UnitName(commander):"无（自主作战）")+"\n生命 "+CombatNumbers.RemainingHealth(unit.Health)+" / "+unit.Stats.Health.ToString("0")+" · 攻击 "+owner.RealtimeAttack(unit).ToString("0")+" · 防御 "+unit.Stats.Defense.ToString("0")+"\n攻击距离 "+owner.RealtimeRange(unit).ToString("0")+"细格 · 移速 "+owner.RealtimeSpeed(unit).ToString("0")+"细格／秒\n视野 "+battle.Vision.SightRadius(unit)+" 格"));
            if(owner.InformationCell.HasValue)information.text+="\n\n"+owner.RealtimeFineInformation+"\n"+CellInformation(battle, owner.InformationCell.Value, owner.ControlledSide, owner.ShowsBattleExit(BattleSide.A), owner.ShowsBattleExit(BattleSide.B));
            if(!string.IsNullOrEmpty(owner.RealtimeRangeDescription))information.text+="\n\n"+owner.RealtimeRangeDescription;
            informationScroll.content.sizeDelta=new Vector2(0,Mathf.Max(informationScroll.viewport.rect.height,information.preferredHeight+8));
            objectiveProgress.text=battle.Phase==BattlePhase.Deployment?"布置队伍":battle.Scenario.Objective.Kind==ObjectiveKinds.Hold?
                "坚守 "+Math.Min(battle.Scenario.Objective.HoldRounds,owner.Realtime?.Model.Elapsed??0).ToString("0")+" / "+battle.Scenario.Objective.HoldRounds+"秒":"队长全倒＋敌兵损失过半";
            status.text=(battle.Phase==BattlePhase.Finished?"战斗结束 · "+BattleSceneController.OutcomeName(battle.Report.Outcome):owner.RealtimeStatus)+"\n"+owner.Feedback;
            if(owner.IsSceneEncounter)
            {
                bool exitOpen = owner.Realtime is RealtimeCampaignBattle run && (run.RuinsExitOpen || run.VictoryExitOpen) &&
                    battle.Phase == BattlePhase.Fighting;
                objectiveTitle.text = exitOpen ? "去出口" : owner.SceneEncounterObjective;
                objectiveProgress.text=battle.Phase==BattlePhase.Finished?owner.SceneEncounterResult:owner.IsDemoAttack?"击败驻军 · 战果处理后开放"+BaseConquest.Base.BaseScriptNames.Village+"道路":owner.IsSecondDefense?"击败全部来敌":owner.IsDemoDefense?"坚守 "+(owner.Realtime?.Model.Elapsed??0).ToString("0")+" / "+battle.Scenario.Objective.HoldRounds+"秒 · 击倒 "+owner.Realtime?.DefenseDown+"/"+owner.Realtime?.DefenseTarget:owner.IsDemoRuins?"击败控制来源 · 受控村民可制服":owner.SceneEncounterObjective;
                if (exitOpen) objectiveProgress.text = "我方出口";
                status.text=owner.Feedback;
            }
            if(owner.DetailsVisible)
            {
                foreach(var row in rangeRows)row.gameObject.SetActive(false);
                foreach (var button in strategyButtons) button.gameObject.SetActive(false);
                var unit=owner.DetailsUnit??battle.Commander(owner.ControlledSide,owner.DetailsSlot);
                if(unit!=null)detailsInfo.text=battle.UnitName(unit)+"\n生命 "+CombatNumbers.RemainingHealth(unit.Health)+" / "+unit.Stats.Health.ToString("0")+
                    "\n攻击 "+unit.Stats.Damage.ToString("0")+" · 防御 "+unit.Stats.Defense.ToString("0")+"\n移速 "+owner.RealtimeSpeed(unit).ToString("0")+"细格／秒 · 攻击距离 "+owner.RealtimeRange(unit).ToString("0")+"细格\n视野 "+battle.Vision.SightRadius(unit)+" 格";
                else detailsInfo.text=battle.CommanderDisplayName(owner.ControlledSide,owner.DetailsSlot)+"\n尚未部署，布兵后可查看本场实际属性";
                detailsEffects.text=(unit?.Rpg==null?"":BaseConquest.Base.BaseRpg.Describe(unit.Rpg)+"\n")+(owner.DetailsSlot>=0&&owner.Realtime!=null?owner.Realtime.SkillDescription(unit?.Side??owner.ControlledSide,unit?.OriginCommanderSlot??owner.DetailsSlot):"");
            }
            log.text=log.text.Replace("轮","秒");
        }
        // 顶部目标面板：只读查询，绝不写模型；任务方固定为我方，不代表玩法手感验收。
        private void RefreshObjective(BattleSimulation b)
        {
            var progress = BattleObjectiveView.Current(b, owner.ControlledSide);
            objectiveTitle.text = BattleObjectiveView.Line1(progress);
            objectiveProgress.text = BattleObjectiveView.Line2(progress);
            float fill = BattleObjectiveView.Progress01(progress);
            objectiveBar.gameObject.SetActive(fill >= 0);
            if (fill >= 0) objectiveBarFill.rectTransform.sizeDelta = new Vector2(objectiveFillSize.x * Mathf.Clamp01(fill), objectiveFillSize.y);
            bool dark = objectivePanel.color.grayscale < .5f;
            Color tone = progress.Declared ? (Color)(dark ? new Color32(206, 229, 161, 255) : new Color32(56, 85, 41, 255))
                : dark ? new Color32(255, 197, 154, 255) : new Color32(135, 62, 45, 255);
            objectiveTitle.color = progress.Failed || progress.Declared ? tone : objectiveTitleColor;
            objectiveProgress.color = progress.Failed || progress.Declared ? tone : objectiveProgressColor;
            if (progress.Failed || progress.Declared) objectiveBar.gameObject.SetActive(false);
        }
        public static string CellInformation(BattleSimulation b, GridPosition p, BattleSide observer, bool showExitA = true, bool showExitB = true)
        {
            if (!b.CanInspectCell(observer, p)) return "格 " + p + (b.Vision.Explored(observer, p) ? " · 已探索，当前无视野" : " · 未探索地形");
            var c = b.Board.Cell(p); string terrain = c.Terrain == TerrainKind.Water ? "水域：不可通行，可透视／射击" : c.Terrain == TerrainKind.Building ? "建筑：不可通行，阻挡视线／射击" : c.Terrain == TerrainKind.Wall ? "城墙：不可通行，阻挡视线／射击" : c.Terrain == TerrainKind.Swamp ? "沼泽：可通行，移速减半（行军期间免减速），不挡视线／射击" : "地面：可以通行、部署与交战";
            if(FortificationInfo.Description(c)!="")terrain=FortificationInfo.Description(c);
            terrain+="\n"+b.Board.HeightDescription(p);
            var opponent = observer == BattleSide.A ? BattleSide.B : BattleSide.A;
            bool ShowExit(BattleSide side) => side == BattleSide.A ? showExitA : showExitB;
            string exit = ShowExit(observer) && p == b.ExitCell(observer) ? " · 我方出口" :
                ShowExit(opponent) && p == b.ExitCell(opponent) ? " · 敌方出口" : "";
            string deployment = c.Zone == DeploymentZone.None ? "无部署权限" : (c.Zone == (observer == BattleSide.A ? DeploymentZone.A : DeploymentZone.B) ? "我方" : "敌方") + "部署区域";
            return "格 " + p + exit + "\n" + terrain + "\n" + deployment;
        }
        public static string RawEvent(BattleSimulation b, BattleEvent e, BattleSide observer = BattleSide.A) => UnitDisplayNames.ForObserver(
            e.Message ?? (e.SourceName ?? b.UnitName(b.Soldier(e.SourceId))) + " → " + (e.TargetName ?? b.UnitName(b.Soldier(e.TargetId))) + " " + e.Damage.ToString("0") + "伤害" + (e.Critical ? "（暴击）" : "") + "，余血" + CombatNumbers.RemainingHealth(e.RemainingHealth) + (e.Lethal ? "（致死）" : e.Downed ? "（击倒）" : ""), observer);
        public static string FormatEvent(BattleSimulation b, BattleEvent e, BattleSide observer, bool darkPanel = false)
        {
            string raw = RawEvent(b, e, observer);
            string ally = darkPanel ? "#9AD3FF" : "#275A8A";
            string enemy = darkPanel ? "#FFC59A" : "#924922";
            return "[" + e.Round + "] " + Regex.Replace(raw, "(我方|敌方)(?:金冠队长|银冠副队长|铜冠副队长|剑士?#[0-9]+|弓兵#[0-9]+|远程#[0-9]+|骑兵#[0-9]+|指挥者)?", m => "<color=" + (m.Value.StartsWith("我方") ? ally : enemy) + ">" + m.Value + "</color>");
        }
    }

}

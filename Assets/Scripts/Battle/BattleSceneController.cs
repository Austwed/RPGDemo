using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;

namespace BaseConquest.Battle
{
    public sealed partial class BattleSceneController : MonoBehaviour
    {
        public BattleSettings Settings;
        public static Action<BattleSettings, BattleScenario> SaveConfiguration;
        private BattleSimulation battle;
        private BattleBoardView boardView;
        private BattleOrderOverlay orderOverlay;
        private BattleRangeOverlay rangeOverlay;
        private readonly BattleSelection selection = new BattleSelection();
        private BattleHud hud;
        private DeveloperPanel developerPanel;
        private readonly Dictionary<int, BattleSoldierView> views = new Dictionary<int, BattleSoldierView>();
        private Transform unitsRoot;
        private Material material, lineMaterial;
        private Font font;
        [NonSerialized] private GUIStyle titleStyle, textStyle, smallStyle, buttonStyle, fieldStyle;
        private int tab;
        private BattleSide selectedSide;
        private SoldierKind selectedKind;
        private int selectedUnitId = -1;
        private bool groupSelected;
        private enum CommandTool { None, Move, Attack }
        private CommandTool commandTool;
        private BoardBrush brush;
        private int brushShape;
        private GridPosition? hover, strokeFrom, rectangleFrom;
        private GridPosition lastRectangleCell;
        private bool skillArmed, autoRun, animating, numericFocus;
        private int lastSpaceInputFrame = -1;
        private float presentation = 1, presentationSeconds = 1.1f;
        private string feedback = "点本方兵种卡后在部署区下兵；切换阵营可布置另一方，也可一键布兵。";
        private string boardWidth = "20", boardHeight = "14";
        private readonly Dictionary<string, string> values = new Dictionary<string, string>();
        private Vector2 panelScroll, reportScroll;
        private const float PanelWidth = 352;
        private float UiScale => BaseConquest.Presentation.GameFrameCanvas.Scale;
        private float UiWidth => 1280;
        private float UiHeight => 720;
        private int SelectedSquad => battle.SquadId(selectedSide, selectedKind);
        public Camera PreviewCamera => boardView?.Camera;
        public BattleBoardView PreviewBoard => boardView;
        public BattleSimulation PreviewSimulation => battle;
        public BattleOrderOverlay PreviewOrders => orderOverlay;
        public BattleHud PreviewHud => hud;
        public void RefreshSelectionPreview() => SyncViews();
        public bool SelectSoldierAtScreen(Vector2 point)
        {
            BattleSoldier s = PickUnit(point); return s != null && SelectSoldier(s.Id);
        }

        private static int playSession;
        [NonSerialized] private int initializedPlaySession = -1;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void NextPlaySession() => playSession++;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void RebuildPlayEntries()
        {
            foreach (var controller in FindObjectsByType<BattleSceneController>(FindObjectsInactive.Exclude))
                if (controller.initializedPlaySession != playSession) controller.BeginPlaySession();
        }
        private void BeginPlaySession()
        {
            if (initializedPlaySession >= 0 && initializedPlaySession != playSession && string.IsNullOrEmpty(BaseConquest.Base.BaseBattleGateway.RequestedId)) campaignBattleId = "";
            DisposePreview(); battle = null; views.Clear(); extraOrders.Clear();
            boardView = null; orderOverlay = null; rangeOverlay = null; unitsRoot = null;
            activeScenarioDocument = null; activeScenarioPath = null; scenarioFolder = null; scenarioFiles.Clear(); drawerOpen = false; panelScroll = Vector2.zero;
            fastMode = numericFocus = suppressInitialDeploy = false; scenarioSaveName = "自定义方案"; scenarioMessage = ""; tab = 0;
            selectedSide = BattleSide.A; initializedPlaySession = playSession; Initialize();
        }
        private void Awake() { if (Application.isPlaying) BeginPlaySession(); }

        private void OnEnable() { if (Application.isPlaying && battle == null) Initialize(); }
        private void Initialize()
        {
            if (battle != null) return;
            titleStyle = textStyle = smallStyle = buttonStyle = fieldStyle = null;
            // 编辑器在 Play 中重载脚本时，清理本入口之前生成的对象后重建，避免双棋盘。
            foreach (Transform child in transform.Cast<Transform>().ToArray())
                if (child.name == "CommanderRadiusOverlay" || child.name == "BattleHud" || child.name == "EditableChessboard" || child.name == "DeployedUnits" || child.name == "SelectedOrderOverlay" || child.name == "BattleArena" || child.name == "BattleCamera" || child.name == "Soldiers" || child.name == "CommandDestination" || child.name == "AbilityPreview")
                { child.gameObject.SetActive(false); BattleBoardView.DestroyObject(child.gameObject); }
            Shader shader = Settings != null ? Settings.UnitShader : null;
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");
            Shader lineShader = Settings != null ? Settings.LineShader : null;
            if (lineShader == null) lineShader = shader;
            material = new Material(shader); lineMaterial = new Material(lineShader);
            material.SetColor("_BaseColor", Color.white); lineMaterial.SetColor("_BaseColor", Color.white);
            font = Font.CreateDynamicFontFromOSFont(new[] { "Microsoft YaHei", "Noto Sans CJK SC", "Arial" }, 16);
            presentationSeconds = Settings != null ? Settings.RoundPresentationSeconds * 2 : 1.1f;
            if (DeploymentDraftActive)
            {
                InitializeDraftDeployment();
                hud = new BattleHud(this, transform, font);
                return;
            }
            if(!Application.isPlaying&&campaignSession!=null)ReplaceSimulation(campaignSession.Battle.Scenario,campaignSession.Battle);
            else if (!InitializeCampaignBattle()) ReplaceSimulation(Settings != null ? Settings.Scenario : new BattleScenario());
            hud = new BattleHud(this, transform, font);
            InitializeBattleDeveloperPanel();
            if (!IsCampaignBattle) TryLoadDefaultScenario();
        }
        private void InitializeBattleDeveloperPanel()
        {
            developerPanel=GetComponent<DeveloperPanel>()??gameObject.AddComponent<DeveloperPanel>();developerPanel.Initialize(this,font);
            developerPanel.enabled=true;
        }
        public void PreparePreview(BattleSettings settings, bool autoDeploy = true)
        {
            Settings = settings; suppressInitialDeploy = !autoDeploy; Initialize();
            if (autoDeploy) battle.AutoDeploy(); RefreshUnits(); SyncViews();
            boardView.UpdateCamera(0, 0);
        }
        private void ReplaceSimulation(BattleScenario scenario, BattleSimulation prepared = null)
        {
            combatText=null;
            if (boardView != null && boardView!=sharedMapView) { boardView.Dispose(); boardView.Root.SetActive(false); BattleBoardView.DestroyObject(boardView.Root); }
            if (unitsRoot != null) { unitsRoot.gameObject.SetActive(false); BattleBoardView.DestroyObject(unitsRoot.gameObject); }
            if (orderOverlay != null) { orderOverlay.Root.SetActive(false); BattleBoardView.DestroyObject(orderOverlay.Root); }
            foreach (var overlay in extraOrders) BattleBoardView.DestroyObject(overlay.Root); extraOrders.Clear();
            if (rangeOverlay != null) { rangeOverlay.Dispose(); BattleBoardView.DestroyObject(rangeOverlay.Root); }
            battle = prepared ?? new BattleSimulation(scenario);if(!RuntimeDeveloperOptions.Enabled)battle.ClearDeveloperEffects(); pursuitChosen = victoryResumeAutomatic = false; views.Clear();
            if (prepared == null && battle.ModernRules && battle.Scenario.TestAutoDeployment && !suppressInitialDeploy) battle.AutoDeploy();
            if (prepared == null && battle.ModernRules && !battle.Scenario.TestAutoDeployment && !suppressInitialDeploy) battle.AutoDeploy(BattleSide.B);
            boardView = sharedMapView ?? new BattleBoardView(battle.Board, transform, material, lineMaterial); boardView.FlatView = battle.ModernRules;
            float previousView=boardView.Camera.orthographicSize;bool preserve=sharedMapView!=null&&battle.Scenario.HeightVision;boardView.SetCombatMode(true,preserve,battle.Phase==BattlePhase.Deployment && !IsRealtimeCampaign);if(preserve)boardView.SetViewSize(previousView);
            if (Application.isPlaying && boardView.Camera.GetComponent<AudioListener>()==null) boardView.Camera.gameObject.AddComponent<AudioListener>();
            unitsRoot = new GameObject("DeployedUnits").transform; unitsRoot.SetParent(transform, false);
            orderOverlay = new BattleOrderOverlay(transform, lineMaterial);
            rangeOverlay = new BattleRangeOverlay(transform, lineMaterial);
            selection.Clear(); selectedUnitId = -1; groupSelected = false; commandTool = CommandTool.None;
            CancelGesture(); pendingInputs.Clear(); skipping = false; CloseContext();
            deployArmed = !battle.ModernRules; formationOpen = false; deploymentCommanderSlot = 0; deploymentSlotChosen = false; informationUnitId = -1; middleMapPan = false;
            informationNotice.Dismiss();informationCell = lastGroundTap = null; informationIsCell = inspectionTracksUnit = false; requestedPatient = detailsSlot = detailsUnitId = pressedPortrait = -1; suppressPortraitClick = false;
            presentation = 1; animating = autoRun = skillArmed = false; strokeFrom = rectangleFrom = hover = null;
            boardWidth = battle.Board.Width.ToString(); boardHeight = battle.Board.Height.ToString(); values.Clear();
            RefreshUnits(); SyncViews();
        }
        private void RefreshUnits()
        {
            campaignSession?.Bind();
            foreach (int id in views.Keys.ToArray())
                if (battle.Soldier(id) == null) { BattleBoardView.DestroyObject(views[id].Root); views.Remove(id); }
            foreach (BattleSoldier s in battle.Soldiers)
                if (!views.ContainsKey(s.Id)) views[s.Id] = new BattleSoldierView(s, unitsRoot, material, lineMaterial, battle.ModernRules);
        }
        internal bool ShowsBattleExit(BattleSide side)
        {
            if (DeploymentDraftActive || battle.Phase != BattlePhase.Fighting || !battle.ModernRules) return false;
            if (!IsRealtimeCampaign) return true;
            if (realtime?.HasBattlefieldExits != true || side == BattleSide.A && realtime.DefensePursuitReady) return false;
            return side == BattleSide.A || !realtime.RuinsExitOpen && !realtime.VictoryExitOpen;
        }
        private void UpdateBattleExitMarkers()
        {
            boardView.SetVisibleExits(font,
                ShowsBattleExit(BattleSide.A) ? battle.ExitCell(BattleSide.A) : (GridPosition?)null,
                ShowsBattleExit(BattleSide.B) ? battle.ExitCell(BattleSide.B) : (GridPosition?)null);
        }
        private void SyncViews()
        {
            if(realtime!=null){SyncRealtimeViews();return;}
            selection.Prune(battle, selectedSide); selectedUnitId = selection.Primary;
            if (battle.ModernRules && selectedUnitId >= 0 && informationUnitId < 0) informationUnitId = selectedUnitId;
            UpdateBattleExitMarkers();
            boardView.RefreshVision(battle.Vision, selectedSide, !battle.ModernRules && drawerOpen && tab == 0 && battle.Phase == BattlePhase.Deployment, battle.ModernRules && battle.Scenario.ShowEnemiesThroughFog);
            foreach (BattleSoldierView view in views.Values)
            {
                view.Sync(battle, -1, presentation, selectedUnitId, selection.Contains(view.Soldier.Id) || (battle.Scenario.CommanderControl && selection.Contains(battle.CommanderFor(view.Soldier)?.Id ?? -1)), DeploymentDraftActive);
                view.ApplyVisibility(battle, selectedSide, presentation);
            }
            if (inspectionTracksUnit)
            {
                var unit = battle.Soldier(informationUnitId);
                if (unit != null && (unit.IsActive || unit.IsDowned) && !unit.Escaped && battle.CanInspectUnit(selectedSide, unit) && views.TryGetValue(unit.Id, out var inspectedView))
                { informationCell = unit.Cell; boardView.FollowSelection(unit.Cell, inspectedView.Root.transform.position); }
                else { inspectionTracksUnit = false; boardView.SelectCell(null); }
            }
            rangeOverlay?.Refresh(battle, selectedSide, InspectingCell ? InformationUnits.FirstOrDefault(s => s.Kind == SoldierKind.Hero) : null);
            var displayIds = selection.Ids.ToList();
            if (battle.ModernRules)
                foreach (var inspected in InformationUnits.Reverse())
                    if (inspected.IsActive && battle.Vision.CanSee(selectedSide, inspected)) { displayIds.Remove(inspected.Id); displayIds.Insert(0, inspected.Id); }
            orderOverlay.Refresh(battle, displayIds.Count > 0 ? displayIds[0] : -1, presentation, dragTarget, selectedSide);
            while (extraOrders.Count < Mathf.Min(7, displayIds.Count - 1)) extraOrders.Add(new BattleOrderOverlay(transform, lineMaterial));
            for (int i = 0; i < extraOrders.Count; i++) extraOrders[i].Refresh(battle, !dragTarget.HasValue && i + 1 < displayIds.Count ? displayIds[i + 1] : -1, presentation, null, selectedSide);
        }
        public bool SelectSoldier(int id)
        {
            if (battle.ModernRules) return InspectUnit(id);
            BattleSoldier s = battle.CommanderFor(battle.Soldier(id)); if (!battle.SelectableCommander(s) || s.Side != selectedSide) return false;
            id = s.Id;
            selectedUnitId = id; groupSelected = false; selectedSide = s.Side; selectedKind = s.Kind;
            selection.Set(new[] { id });
            deployArmed = false;
            commandTool = CommandTool.None; skillArmed = false; values.Clear(); orderOverlay.Invalidate();
            feedback = "已选中" + battle.UnitName(s);
            SyncViews(); return true;
        }
        private void SelectGroup(BattleSide side, SoldierKind kind)
        {
            if (battle.Scenario.CommanderControl) { if (side != selectedSide) SwitchSide(); SelectCommanderSlot(deploymentCommanderSlot); return; }
            // 旧按兵种批选入口仅供回归夹具，成品流程选择小队长。
            ManualPause(); CancelGesture(); CloseContext(); pendingInputs.Clear();
            selectedSide = side; selectedKind = kind; selectedUnitId = -1; groupSelected = true;
            selection.Set(battle.Soldiers.Where(s => s.IsActive && s.Side == side && s.Kind == kind).Select(s => s.Id));
            deployArmed = false;
            commandTool = CommandTool.None; skillArmed = false; values.Clear(); orderOverlay.Invalidate();
        }
        public bool IssueSelectedMove(GridPosition cell) => realtime!=null ? RealtimeOrder(BaseConquest.RealtimePrototype.PrototypeOrder.Move,new BaseConquest.RealtimePrototype.PrototypePoint(cell.X+.25,cell.Z+.25)) : UserOrder(() => battle.Scenario.CommanderControl ? battle.MoveCommanders(selection.Ids, cell) : battle.MoveUnits(selection.Ids, cell));
        public bool IssueSelectedAttack(int target) => realtime!=null ? RealtimeOrder(BaseConquest.RealtimePrototype.PrototypeOrder.Focus,new BaseConquest.RealtimePrototype.PrototypePoint(battle.Soldier(target)?.Cell.X+.25??0,battle.Soldier(target)?.Cell.Z+.25??0),target) : UserOrder(() => battle.Scenario.CommanderControl ? battle.FocusCommanders(selection.Ids, target) : battle.AttackUnits(selection.Ids, target));
        public bool IssueSelectedHold() => battle.ModernRules ? HasSelection && IssueSelectedGuard(battle.Soldier(selection.Primary).Cell) : battle.Scenario.CommanderControl ? battle.OrderCommanders(selection.Ids, SquadCommand.Hold) : battle.OrderUnits(selection.Ids, SquadCommand.Hold);
        private bool IssueSelectedAuto() => UserOrder(() => battle.Scenario.CommanderControl ? battle.OrderCommanders(selection.Ids, SquadCommand.Auto) : battle.OrderUnits(selection.Ids, SquadCommand.Auto));
        private bool IssueSelectedRetreat() => UserOrder(() => battle.Scenario.CommanderControl ? battle.OrderCommanders(selection.Ids, SquadCommand.Retreat) : battle.OrderUnits(selection.Ids, SquadCommand.Retreat));
        private bool HasSelection => selection.Ids.Count > 0;
        private void ArmCommand(CommandTool tool)
        {
            if (!HasSelection || battle.Phase != BattlePhase.Fighting || animating) { feedback = "先选中单位，并在轮次之间下令"; return; }
            skillArmed = false; commandTool = commandTool == tool ? CommandTool.None : tool;
            feedback = commandTool == CommandTool.Move ? "左键选择移动格，或右键地面直接移动" : commandTool == CommandTool.Attack ? "左键选择对手作为锁定目标" : "命令选点已取消";
        }

        private int displayedTerrainRevision=-1;
        private bool externalDialogue;
        public bool ExternalDialogue {get=>externalDialogue;set{if(value&&!externalDialogue)CancelGesture();externalDialogue=value;if(hud!=null)hud.Root.SetActive(!value);}}
        private void Update()
        {
            if(ExternalDialogue)return;
            if (battle == null) return;
            if (CampaignMenuOpen)
            {
                if (Keyboard.current?.escapeKey.wasPressedThisFrame == true) CloseCampaignMenu();
                if (!DeploymentDraftActive && !BaseConquest.Base.BaseBattleGateway.DemoMode) PersistCampaignResult();
                return;
            }
            if (DeploymentDraftActive) { UpdateDraftDeployment(); return; }
            if(IsRealtimeCampaign&&realtime!=null){UpdateRealtime();return;}
            if(preparationInventoryOpen){if(Keyboard.current!=null&&(Keyboard.current.bKey.wasPressedThisFrame||Keyboard.current.escapeKey.wasPressedThisFrame))GetComponentInParent<BaseConquest.Base.BaseSceneController>()?.Hud.ClosePanel();return;}
            boardView.UpdateCamera(drawerOpen ? Mathf.Min(0.44f, PanelWidth / 1280f) : battle.ModernRules ? Mathf.Min(0.21f, 132f / 1280) : 0, battle.ModernRules ? 0.08f : 0);
            TickPresentation(Time.unscaledDeltaTime);
            if(displayedTerrainRevision!=battle.TerrainRevision){displayedTerrainRevision=battle.TerrainRevision;boardView.Refresh();}
            if(battleBagOpen&&campaignMenu!=null&&Keyboard.current!=null&&((battleJournalOpen ? Keyboard.current.nKey.wasPressedThisFrame : Keyboard.current.bKey.wasPressedThisFrame)||Keyboard.current.escapeKey.wasPressedThisFrame)){CloseBattleBackpack();return;}
            if(campaignMenu!=null || CampaignGameOver) { PersistCampaignResult(); hud?.Refresh(); return; }
            if(developerPanel?.BlocksGameInput!=true){ReadKeyboard(); if(campaignMenu!=null||preparationInventoryOpen)return; ReadPointer(); UpdateLongPress();}
            UpdatePortraitHold();
            if (skipping && !animating) RunSkipBatch();
            if (autoRun && !skipping && !animating && !gestureActive && battle.Phase == BattlePhase.Fighting) BeginRound();
            if (battle.Phase == BattlePhase.Finished) autoRun = false;
            PersistCampaignResult();
            PersistCampaignCheckpoint();
            if(campaignMenu!=null || CampaignGameOver) return;
            RefreshUnits(); SyncViews();
            hud?.Refresh();
        }
        private void ReadKeyboard()
        {
            Keyboard k = Keyboard.current; if (k == null || numericFocus || formationOpen) return;
            if (hud?.ReportVisible == true) { if (k.escapeKey.wasPressedThisFrame) hud.CloseReport(); return; }
            if (DetailsVisible) { if (k.escapeKey.wasPressedThisFrame) CloseCommanderDetails(); return; }
            if(k.nKey.wasPressedThisFrame){OpenBattleQuests();return;}
            if(k.bKey.wasPressedThisFrame){OpenBattleBackpack();return;}
            if (battle.Scenario.CommanderControl)
            {
                if (k.digit1Key.wasPressedThisFrame) SelectCommanderSlot(0);
                if (k.digit2Key.wasPressedThisFrame) SelectCommanderSlot(1);
                if (k.digit3Key.wasPressedThisFrame) SelectCommanderSlot(2);
            }
            else
            {
            if (k.digit1Key.wasPressedThisFrame) SelectKindFromKey(SoldierKind.Melee);
            if (k.digit2Key.wasPressedThisFrame) SelectKindFromKey(SoldierKind.Ranged);
            if (k.digit3Key.wasPressedThisFrame) SelectKindFromKey(SoldierKind.Hero);
            if (k.digit4Key.wasPressedThisFrame) SelectKindFromKey(SoldierKind.Cavalry);
            }
            if (k.spaceKey.wasPressedThisFrame) HandleSpaceInput();
            if (k.nKey.wasPressedThisFrame) SingleRound();
            if (k.qKey.wasPressedThisFrame && battle.Phase == BattlePhase.Fighting && !animating) { skillArmed = !skillArmed; commandTool = CommandTool.None; battle.CancelSkill(selectedSide); orderOverlay.Invalidate(); }
            if (k.escapeKey.wasPressedThisFrame) { skillArmed = false; commandTool = CommandTool.None; battle.CancelSkill(selectedSide); orderOverlay.Invalidate(); strokeFrom = rectangleFrom = null; }
            if (k.mKey.wasPressedThisFrame) ArmCommand(CommandTool.Move);
            if (k.fKey.wasPressedThisFrame) ArmCommand(CommandTool.Attack);
            if (k.hKey.wasPressedThisFrame && !animating) { feedback = IssueSelectedHold() ? "已驻守当前区域，按预警与追击范围接敌" : "请先选中可行动单位"; commandTool = CommandTool.None; skillArmed = false; }
            if (!IsRealtimeCampaign && k.aKey.wasPressedThisFrame && !animating && IssueSelectedAuto()) { commandTool = CommandTool.None; skillArmed = false; }
            if (!battle.ModernRules && k.tKey.wasPressedThisFrame && !animating && IssueSelectedRetreat()) { commandTool = CommandTool.None; skillArmed = false; }
        }
        private void SelectKindFromKey(SoldierKind kind)
        {
            if (battle.Phase == BattlePhase.Deployment) ChooseDeployment(kind); else SelectGroup(selectedSide, kind);
        }
        private void HandleSpaceInput()
        {
            // Game视图键事件与Input System共用入口，一帧只处理一次，兼容编辑器焦点与合成按键。
            if (lastSpaceInputFrame == Time.frameCount) return;
            lastSpaceInputFrame = Time.frameCount; SpaceAction();
        }
        public void StartBattle()
        {
            if (DeploymentDraftActive) return;
            if (IsCampaignBattle && campaignSession == null) { feedback = "战役进度未读取，请返回基地恢复存档"; return; }
            campaignSession?.Bind();
            if (battle.Start(out feedback)) { tab = 3; formationOpen = false; drawerOpen = deployArmed = false; strokeFrom = rectangleFrom = null; autoRun = false; hud?.CloseMenu();if(IsRealtimeCampaign)AttachRealtime(); }
            SyncViews(); hud?.Refresh();
        }
        private void SingleRound()
        {
            autoRun = false;
            if (animating) { feedback = "当前轮次播放结束后即可推进下一轮"; return; }
            BeginRound();
        }
        private void BeginRound()
        {
            if(IsRealtimeCampaign){ToggleRealtimePause();return;}
            campaignSession?.Bind();
            if (deploymentStrokeActive || modernPointerActive && deployArmed) { feedback = "部署手势结束后可推进下一轮"; return; }
            if (VictoryChoicePending) { autoRun = false; hud?.Refresh(); return; }
            bool wasVictory = battle.VictoryDeclared, wasAutomatic = autoRun;
            if (!battle.AdvanceRound(autoRun)) { SyncViews(); hud?.Refresh(); return; }
            if (!wasVictory && battle.VictoryDeclared) victoryResumeAutomatic = wasAutomatic;
            if (VictoryChoicePending) { autoRun = skipping = false; }
            presentation = 0; animating = true; skillArmed = false; feedback = battle.Scenario.FogEnabled ? "本轮已结算，视野与已知信息已更新" : battle.LastRoundSummary;
        }

        private void ApplyBrush(GridPosition center)
        {
            int size = brushShape == 1 ? 1 : 0;
            int painted = 0;
            for (int z = -size; z <= size; z++) for (int x = -size; x <= size; x++) if (battle.Paint(center + new GridPosition(x, z), brush)) painted++;
            boardView.Refresh(); feedback = painted > 0 ? "已更新 " + painted + " 格；受影响单位已返回备战席" : "未修改：部署区只能画在可通行地面上";
        }
        private BattleSoldier PickUnit(Vector2 screen)
        {
            if (!boardView.Camera.pixelRect.Contains(screen)) return null;
            BattleSoldier result = null;
            float nearest = Mathf.Max(10, boardView.Camera.pixelHeight / (boardView.Camera.orthographicSize * 2) * 0.4f);
            foreach (BattleSoldierView view in views.Values)
            {
                if ((!view.Soldier.IsActive && !view.Soldier.IsDowned) || !view.Root.activeSelf || !battle.PlayerCanSee(selectedSide, view.Soldier)) continue;
                Vector3 projected = boardView.Camera.WorldToScreenPoint(view.Root.transform.position + Vector3.up * 0.6f);
                float distance = Vector2.Distance(screen, new Vector2(projected.x, projected.y));
                if (projected.z > 0 && distance < nearest) { nearest = distance; result = view.Soldier; }
            }
            return result;
        }
        private void PaintStroke(GridPosition from, GridPosition to)
        {
            foreach (var cell in StrokeCells(from, to)) ApplyBrush(cell);
        }
        private static IEnumerable<GridPosition> StrokeCells(GridPosition from, GridPosition to)
        {
            int dx = Math.Abs(to.X - from.X), dz = Math.Abs(to.Z - from.Z), sx = Math.Sign(to.X - from.X), sz = Math.Sign(to.Z - from.Z), error = dx - dz;
            while (true)
            {
                yield return from;
                if (from == to) yield break;
                int doubled = error * 2;
                if (doubled > -dz) { error -= dz; from.X += sx; }
                if (doubled < dx) { error += dx; from.Z += sz; }
            }
        }
        private void PaintRectangle(GridPosition from, GridPosition to)
        {
            int painted = 0;
            for (int z = Math.Min(from.Z, to.Z); z <= Math.Max(from.Z, to.Z); z++)
                for (int x = Math.Min(from.X, to.X); x <= Math.Max(from.X, to.X); x++) if (battle.Paint(new GridPosition(x, z), brush)) painted++;
            boardView.Refresh(); feedback = painted > 0 ? "矩形填充完成，已更新 " + painted + " 格" : "未修改：部署区只能画在可通行地面上";
        }

        private void EnsureStyles()
        {
            if (titleStyle != null) return;
            titleStyle = new GUIStyle(GUI.skin.label) { font = font, fontSize = 25, fontStyle = FontStyle.Bold };
            textStyle = new GUIStyle(GUI.skin.label) { font = font, fontSize = 16, wordWrap = true };
            smallStyle = new GUIStyle(textStyle) { fontSize = 13 };
            buttonStyle = new GUIStyle(GUI.skin.button) { font = font, fontSize = 16, wordWrap = true };
            fieldStyle = new GUIStyle(GUI.skin.textField) { font = font, fontSize = 15 };
            WhiteText(titleStyle); WhiteText(textStyle); WhiteText(smallStyle); WhiteText(buttonStyle); WhiteText(fieldStyle);
        }
        private static void WhiteText(GUIStyle style)
        {
            style.normal.textColor = style.hover.textColor = style.active.textColor = style.focused.textColor = Color.white;
            style.onNormal.textColor = style.onHover.textColor = style.onActive.textColor = style.onFocused.textColor = Color.white;
        }
        private bool Button(string label, float height = 34) => GUILayout.Button(label, buttonStyle, GUILayout.Height(height));
        private void OnGUI()
        {
            var matrix = GUI.matrix; var color = GUI.color; var content = GUI.contentColor; var background = GUI.backgroundColor; bool enabled = GUI.enabled;
            try { GUI.enabled = true; GUI.color = GUI.contentColor = GUI.backgroundColor = Color.white; DrawRuntimeGUI(); }
            finally { GUI.matrix = matrix; GUI.color = color; GUI.contentColor = content; GUI.backgroundColor = background; GUI.enabled = enabled; }
        }
        private void DrawRuntimeGUI()
        {
            if (battle == null || ExternalDialogue || campaignMenu!=null || developerPanel?.BlocksGameInput==true) return;
            if (!numericFocus && Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Space) { HandleSpaceInput(); Event.current.Use(); }
            if (battle.ModernRules && !drawerOpen) { numericFocus = false; return; }
            EnsureStyles(); var frame=BaseConquest.Presentation.GameFrameCanvas.Area;GUI.matrix = Matrix4x4.TRS(new Vector3(frame.x,Screen.height-frame.yMax,0),Quaternion.identity,Vector3.one * UiScale);
            if (!battle.ModernRules) DrawHealthBars();
            if (!drawerOpen)
            {
                numericFocus = false; GUI.matrix = Matrix4x4.identity; return;
            }
            if (battle.ModernRules) { DrawScenarioPanel(); GUI.matrix = Matrix4x4.identity; return; }
            GUI.Box(new Rect(0, 0, PanelWidth, UiHeight), GUIContent.none);
            GUILayout.BeginArea(new Rect(14, 14, PanelWidth - 28, UiHeight - 28));
            panelScroll = GUILayout.BeginScrollView(panelScroll);
            GUILayout.Label("格子战斗测试场", titleStyle);
            GUILayout.Label(battle.Board.Width + " × " + battle.Board.Height + "  |  第 " + battle.RoundNumber + " 轮", textStyle);
            GUILayout.Label(battle.Phase == BattlePhase.Deployment ? "准备中：地图和数值可编辑" : animating ? "本轮动作播放中" : battle.Phase == BattlePhase.Finished ? "战斗结束" : "轮次之间：可以下令", smallStyle);
            GUILayout.Space(10);
            int newTab = GUILayout.Toolbar(tab, new[] { "地图", "部署", "数值", "战斗" }, buttonStyle, GUILayout.Height(34));
            if (newTab != tab) { tab = newTab; strokeFrom = rectangleFrom = null; }
            GUILayout.Space(8); DrawSideSelector();
            if (selectedUnitId >= 0 && battle.Soldier(selectedUnitId) is BattleSoldier pawn)
            {
                GUILayout.Label("单兵 #" + (pawn.Id + 1) + " · " + BattleSimulation.KindName(pawn.Kind) + " · " + CommandName(pawn.Command), textStyle);
                GUILayout.Label("生命 " + pawn.Health.ToString("F0") + "/" + pawn.Stats.Health.ToString("F0") + "  |  攻击距离 " + pawn.Stats.Range + " · 移动 " + pawn.Stats.MoveDistance + " 格", smallStyle);
                GUILayout.Label("伤害 " + pawn.Stats.Damage.ToString("0") + " · 暴击率 " + pawn.Stats.CritChance.ToString("P0") + " · 暴击伤害 " + pawn.Stats.CritMultiplier.ToString("P0"), smallStyle);
                UnitOrderPreview preview = orderOverlay.Current;
                if (preview != null)
                {
                    string locked = preview.LockedTargetId >= 0 ? "锁定 #" + (preview.LockedTargetId + 1) : preview.TargetCell.HasValue ? "技能选点 " + preview.TargetCell.Value : "未锁定目标";
                    GUILayout.Label(preview.Status + " · " + locked, smallStyle);
                }
            }
            if (tab == 0) DrawMapPanel(); else if (tab == 1) DrawDeploymentPanel(); else if (tab == 2) DrawValuesPanel(); else DrawCombatPanel();
            GUILayout.Space(12); GUI.enabled = true;
            if (Button("返回准备 / 重开当前配置")) { ReplaceSimulation(battle.Scenario); feedback = "已保留地图、参数和初始布兵，返回准备状态"; }
            GUI.enabled = battle.Phase == BattlePhase.Deployment;
            if (Button("保存测试配置"))
            {
                if (Settings != null && SaveConfiguration != null) { SaveConfiguration(Settings, battle.Scenario.Clone()); feedback = "配置已保存，包含棋盘、双方数值和初始布兵"; }
                else feedback = "保存配置需要在 Unity 编辑器内运行";
            }
            GUI.enabled = true;
            if (Button("读取已保存配置")) { ReplaceSimulation(Settings != null ? Settings.Scenario : new BattleScenario()); feedback = "已读取保存的配置"; }
            GUILayout.EndScrollView(); GUILayout.EndArea();
            if (!battle.ModernRules) GUI.Box(new Rect(PanelWidth + 8, UiHeight - 88, UiWidth - PanelWidth - 16, 80), GUIContent.none);
            string cellInfo = "";
            if (hover.HasValue)
            {
                BoardCell c = battle.Board.Cell(hover.Value);
                string terrain = c.Terrain == TerrainKind.Ground ? "可通行" : DeveloperTerrain.Name(c.Terrain) + (c.BlocksShots ? " · 阻挡射击" : " · 可射击穿过");
                if(FortificationInfo.Description(c)!="")terrain=FortificationInfo.Description(c);
                cellInfo = "列 " + (hover.Value.X + 1) + " / 行 " + (hover.Value.Z + 1) + "：" + terrain + "，" + (c.Zone == DeploymentZone.None ? "无部署权限" : c.Zone == DeploymentZone.A ? "我方部署区" : "敌方部署区") + "\n";
            }
            if (!battle.ModernRules) GUI.Label(new Rect(PanelWidth + 20, UiHeight - 80, UiWidth - PanelWidth - 40, 72), cellInfo + feedback, textStyle);
            if (battle.Phase == BattlePhase.Finished && !animating) DrawReport();
            numericFocus = GUI.GetNameOfFocusedControl().StartsWith("GridValue.", StringComparison.Ordinal);
            GUI.matrix = Matrix4x4.identity; GUI.enabled = true; GUI.color = GUI.backgroundColor = Color.white;
        }
        private void DrawSideSelector()
        {
            GUILayout.BeginHorizontal();
            for (int i = 0; i < 2; i++)
            {
                GUI.backgroundColor = (int)selectedSide == i ? (i == 0 ? new Color(0.35f, 0.75f, 1) : new Color(1, 0.65f, 0.35f)) : Color.white;
                if (Button(i == 0 ? "我方" : "敌方")) SelectGroup((BattleSide)i, selectedKind);
            }
            GUI.backgroundColor = Color.white; GUILayout.EndHorizontal();
        }
        private void DrawMapPanel()
        {
            GUI.enabled = battle.Phase == BattlePhase.Deployment;
            GUILayout.Label("左键拖动涂画；右键恢复地面", smallStyle);
            brushShape = GUILayout.Toolbar(brushShape, new[] { "单格", "3×3", "矩形" }, buttonStyle);
            string[] labels = { "我方部署区", "敌方部署区", "清除部署区", "恢复地面", "水域（可射）", "城墙（挡射）", "沼泽", "建筑（挡射）" };
            for (int row = 0; row < 4; row++)
            {
                GUILayout.BeginHorizontal();
                for (int col = 0; col < 2; col++)
                {
                    int index = row * 2 + col; GUI.backgroundColor = (int)brush == index ? new Color(0.67f, 0.88f, 0.42f) : Color.white;
                    if (Button(labels[index], 38)) { brush = (BoardBrush)index; strokeFrom = rectangleFrom = null; }
                }
                GUI.backgroundColor = Color.white; GUILayout.EndHorizontal();
            }
            GUILayout.Label("水域不可通行但可射；城墙和建筑阻挡移动、视线与射击，外观分别配置。", smallStyle);
            GUILayout.Space(8);
            GUILayout.BeginHorizontal(); GUILayout.Label("列 / 行", smallStyle, GUILayout.Width(66));
            GUI.SetNextControlName("GridValue.Width"); boardWidth = GUILayout.TextField(boardWidth, fieldStyle);
            GUI.SetNextControlName("GridValue.Height"); boardHeight = GUILayout.TextField(boardHeight, fieldStyle); GUILayout.EndHorizontal();
            if (Button("按尺寸重建棋盘（清空布兵）"))
            {
                if (int.TryParse(boardWidth, out int w) && int.TryParse(boardHeight, out int h) && w >= 6 && w <= 32 && h >= 6 && h <= 24) LoadBoard(BattleBoard.CreateDefault(w, h));
                else feedback = "列数支持 6～32，行数支持 6～24";
            }
            GUILayout.BeginHorizontal(); if (Button("甲围乙")) LoadBoard(BattleBoard.Siege(true)); if (Button("乙围甲")) LoadBoard(BattleBoard.Siege(false)); GUILayout.EndHorizontal();
            if (Button("默认对峙棋盘")) LoadBoard(BattleBoard.CreateDefault());
            GUI.enabled = true;
        }
        private void LoadBoard(BattleBoard board)
        {
            BattleScenario scenario = battle.Scenario.Clone(); scenario.Board = board; scenario.Placements.Clear();
            ReplaceSimulation(scenario); feedback = "棋盘已更新，重新投放双方单位即可测试";
        }
        private void DrawDeploymentPanel()
        {
            if (battle.ModernRules) battle.Scenario.TestAutoDeployment = GUILayout.Toggle(battle.Scenario.TestAutoDeployment, "测试重开时布满双方（关闭则手动部署我方）");
            GUI.enabled = battle.Phase == BattlePhase.Deployment;
            GUILayout.Label("选择兵种，左键点格／拖动下兵。\n右键撤回单位。战中库存可在轮次之间继续部署。", smallStyle);
            for (int i = 0; i < 4; i++)
            {
                SoldierKind kind = (SoldierKind)i; GUI.backgroundColor = selectedKind == kind ? new Color(0.7f, 0.87f, 0.43f) : Color.white;
                if (Button((i + 1) + "  " + BattleSimulation.KindName(kind) + "   备战席 " + battle.Remaining(selectedSide, kind), 44)) ChooseDeployment(kind);
            }
            GUI.backgroundColor = Color.white;
            if (HasSelection && Button("撤回所选单位"))
            {
                WithdrawSelected();
            }
            if (Button("自动填充双方备战席")) feedback = "自动投放 " + battle.AutoDeploy() + " 名；空间不足的单位留在备战席";
            if (Button("清空已部署单位")) { foreach (BattleSoldier s in battle.Soldiers.ToArray()) battle.Remove(s.Cell); feedback = "单位已返回备战席，地图和参数保留"; }
            if (Button("确认开战", 42)) StartBattle();
            GUI.enabled = true;
        }

        private string ValueField(string key, string initial, float width = 59)
        {
            if (!values.ContainsKey(key)) values[key] = initial;
            GUI.SetNextControlName("GridValue." + key); values[key] = GUILayout.TextField(values[key], fieldStyle, GUILayout.Width(width)); return values[key];
        }
        private void DrawValuesPanel()
        {
            GUI.enabled = battle.Phase == BattlePhase.Deployment;
            ArmyDefinition army = battle.Scenario.Army(selectedSide);
            GUILayout.Label("分别设置双方。应用后保留合法布兵，数量减少时多出的单位返回备战席。", smallStyle);
            GUILayout.BeginHorizontal(); GUILayout.Label("兵种", smallStyle, GUILayout.Width(58)); GUILayout.Label("人数", smallStyle, GUILayout.Width(59)); GUILayout.Label("生命", smallStyle, GUILayout.Width(59)); GUILayout.Label("伤害", smallStyle, GUILayout.Width(59)); GUILayout.Label("攻击距离", smallStyle); GUILayout.EndHorizontal();
            for (int i = 0; i < 4; i++)
            {
                SoldierKind kind = (SoldierKind)i; SoldierStats stats = army.Stats(kind); string prefix = i + ".";
                GUILayout.BeginHorizontal(); GUILayout.Label(BattleSimulation.KindName(kind), smallStyle, GUILayout.Width(58));
                ValueField(prefix + "count", army.Count(kind).ToString()); ValueField(prefix + "health", stats.Health.ToString(CultureInfo.InvariantCulture));
                ValueField(prefix + "damage", stats.Damage.ToString(CultureInfo.InvariantCulture)); ValueField(prefix + "range", stats.Range.ToString(), 46); GUILayout.EndHorizontal();
                GUILayout.BeginHorizontal(); GUILayout.Label("移动", smallStyle, GUILayout.Width(38)); ValueField(prefix + "move", stats.MoveDistance.ToString(), 35);
                GUILayout.Label("暴击率%", smallStyle, GUILayout.Width(64)); ValueField(prefix + "crit", (stats.CritChance * 100).ToString(CultureInfo.InvariantCulture), 40);
                GUILayout.Label("暴伤%", smallStyle, GUILayout.Width(52)); ValueField(prefix + "multiplier", (stats.CritMultiplier * 100).ToString(CultureInfo.InvariantCulture), 42); GUILayout.EndHorizontal();
                GUILayout.Space(6);
            }
            GUILayout.Label("人数 0～30，英雄 0～3；攻击距离／移动 1～12 格。一次移动沿路径前进最多指定格数，本轮不追加攻击。暴击率 0～100%，暴伤 100～1000%。", smallStyle);
            GUILayout.BeginHorizontal(); GUILayout.Label("随机种子", smallStyle, GUILayout.Width(90)); ValueField("seed", battle.Scenario.RandomSeed.ToString(), 120); GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal(); GUILayout.Label("视野半径", smallStyle, GUILayout.Width(90)); ValueField("vision", battle.Scenario.VisionRadius.ToString(), 120); GUILayout.EndHorizontal();
            GUI.enabled = true;
            bool fog = GUILayout.Toggle(battle.Scenario.FogEnabled, "战争迷雾（双方独立视野）");
            if (fog != battle.Scenario.FogEnabled) { battle.Scenario.FogEnabled = fog; battle.Vision.Refresh(); orderOverlay.Invalidate(); SyncViews(); }
            bool reveal = GUILayout.Toggle(battle.Scenario.ShowEnemiesThroughFog, "玩家透雾看敌人（单位仍按真实视野索敌）");
            if (reveal != battle.Scenario.ShowEnemiesThroughFog) ToggleTestFog(fog, reveal);
            GUI.enabled = battle.Phase == BattlePhase.Deployment;
            if (Button("应用" + BattleSimulation.SideName(selectedSide) + "数值"))
            {
                if (TryReadArmy(out ArmyDefinition definition) && int.TryParse(values["seed"], out int seed) && int.TryParse(values["vision"], out int vision) && vision >= 1 && vision <= 32) { battle.ApplyArmy(selectedSide, definition); battle.Scenario.RandomSeed = seed; battle.Scenario.VisionRadius = vision; battle.Vision.Refresh(); values.Clear(); feedback = "数值已应用；生命、移动、暴击与视野参数已更新"; }
                else feedback = "请输入合法数值：生命 > 0，伤害 ≥ 0，攻击距离／移动 1～12，暴击率 0～100%，暴伤 100～1000%，视野 1～32，种子为整数";
            }
            GUI.enabled = true;
        }
        private bool TryReadArmy(out ArmyDefinition army)
        {
            army = battle.Scenario.Army(selectedSide).Clone();
            for (int i = 0; i < 4; i++)
            {
                string prefix = i + ".";
                if (!int.TryParse(values[prefix + "count"], out int count) || count < 0 || count > (i == 2 ? 3 : 30)
                    || !float.TryParse(values[prefix + "health"], NumberStyles.Float, CultureInfo.InvariantCulture, out float health) || health <= 0 || float.IsInfinity(health) || float.IsNaN(health)
                    || !float.TryParse(values[prefix + "damage"], NumberStyles.Float, CultureInfo.InvariantCulture, out float damage) || damage < 0 || float.IsInfinity(damage) || float.IsNaN(damage)
                    || !int.TryParse(values[prefix + "range"], out int range) || range < 1 || range > 12
                    || !int.TryParse(values[prefix + "move"], out int move) || move < 1 || move > 12
                    || !float.TryParse(values[prefix + "crit"], NumberStyles.Float, CultureInfo.InvariantCulture, out float crit) || float.IsNaN(crit) || crit < 0 || crit > 100
                    || !float.TryParse(values[prefix + "multiplier"], NumberStyles.Float, CultureInfo.InvariantCulture, out float multiplier) || float.IsNaN(multiplier) || multiplier < 100 || multiplier > 1000) return false;
                army.SetCount((SoldierKind)i, count);
                SoldierStats stats = army.Stats((SoldierKind)i); stats.Health = health; stats.Damage = damage; stats.Range = range; stats.MoveDistance = move; stats.CritChance = crit / 100; stats.CritMultiplier = multiplier / 100;
            }
            return true;
        }
        private void DrawCombatPanel()
        {
            if (battle.Phase == BattlePhase.Deployment) { GUILayout.Label("先编辑地图和投放双方单位。", textStyle); if (Button("确认开战", 42)) StartBattle(); return; }
            GUILayout.Label("空格：自动时关闭自动，暂停时推进一轮。自动开关使用右上按钮。", smallStyle);
            GUI.enabled = battle.Phase == BattlePhase.Fighting;
            if (Button("推进下一轮  [N]", 40)) SingleRound();
            if (Button(autoRun ? "暂停连续推进" : "自动连续推进", 38)) ToggleAutomatic();
            GUILayout.Label("动作播放时长 " + CombatNumbers.Duration(presentationSeconds), smallStyle);
            presentationSeconds = GUILayout.HorizontalSlider(presentationSeconds, 0.1f, 1.5f);
            GUILayout.Space(8);
            if (battle.Scenario.CommanderControl)
            {
                for (int slot = 0; slot < battle.Scenario.Army(selectedSide).HeroCount; slot++)
                {
                    var q = battle.CommanderSquad(selectedSide, slot);
                    if (Button(q.Name + " · 在场 " + q.ActiveCount + " · " + CommandName(q.Command), 38)) SelectCommanderSlot(slot);
                }
            }
            else for (int i = 0; i < 4; i++)
            {
                SoldierKind kind = (SoldierKind)i; BattleSquad squad = battle.Squads[battle.SquadId(selectedSide, kind)];
                GUI.backgroundColor = groupSelected && selectedKind == kind ? new Color(0.7f, 0.87f, 0.43f) : Color.white;
                if (Button("批量选" + BattleSimulation.KindName(kind) + "  " + squad.ActiveCount + " 名  /  " + (squad.MixedCommands ? "混合命令" : CommandName(squad.Command)), 38)) SelectGroup(selectedSide, kind);
            }
            GUI.backgroundColor = Color.white; GUI.enabled = battle.Phase == BattlePhase.Fighting && !animating && HasSelection;
            GUILayout.Label(HasSelection ? "命令范围：当前选中 " + selection.Ids.Count + " 名单位" : "先点选或框选单位", smallStyle);
            GUILayout.BeginHorizontal();
            if (Button(commandTool == CommandTool.Move ? "取消移动选点" : "移动  [M]")) ArmCommand(CommandTool.Move);
            if (Button("驻守  [H]")) { IssueSelectedHold(); commandTool = CommandTool.None; skillArmed = false; feedback = "原地驻守并攻击攻击距离内目标，不追击"; }
            if (Button(commandTool == CommandTool.Attack ? "取消攻击选点" : "攻击  [F]")) ArmCommand(CommandTool.Attack);
            GUILayout.EndHorizontal();
            if (Button("恢复自动  [A]")) { IssueSelectedAuto(); commandTool = CommandTool.None; skillArmed = false; }
            if (Button("撤退到本方部署区  [T]")) feedback = IssueSelectedRetreat() ? "所选单位按格撤退，到本方部署格后花一次行动撤离" : "所选单位无法撤退";
            int cooldown = battle.SkillCooldown(selectedSide);
            GUI.enabled = battle.Phase == BattlePhase.Fighting && !animating && cooldown == 0;
            if (Button(cooldown > 0 ? "英雄技能冷却 " + cooldown + " 轮" : battle.SkillQueued(selectedSide) ? "技能已排队 / 重选  [Q]" : skillArmed ? "取消技能瞄准  [Q]" : "当前方英雄震荡  [Q]")) { battle.CancelSkill(selectedSide); skillArmed = !skillArmed; commandTool = CommandTool.None; orderOverlay.Invalidate(); }
            GUI.enabled = true;
            GUILayout.Label("点选切换、空地起拖框选；单位起拖或点目标下令。长按弹出前往／驻守。绿线为路径，橙线为等待，红框为锁定目标。", smallStyle);
        }
        private void DrawHealthBars()
        {
            foreach (BattleSoldierView view in views.Values)
            {
                BattleSoldier s = view.Soldier; if (!s.IsActive || !view.Root.activeSelf || !battle.Vision.CanSee(selectedSide, s)) continue;
                Vector3 screen = boardView.Camera.WorldToScreenPoint(view.Root.transform.position + Vector3.up * 1.5f);
                if (s.LastAttackCritical && presentation < 1) GUI.Label(new Rect((screen.x-BaseConquest.Presentation.GameFrameCanvas.Area.x) / UiScale - 22, (BaseConquest.Presentation.GameFrameCanvas.Area.yMax-screen.y) / UiScale - 22, 55, 22), "暴击", smallStyle);
                if (screen.z <= 0) continue; float x = (screen.x-BaseConquest.Presentation.GameFrameCanvas.Area.x) / UiScale, y = (BaseConquest.Presentation.GameFrameCanvas.Area.yMax-screen.y) / UiScale;
                GUI.color = new Color(0, 0, 0, 0.75f); GUI.DrawTexture(new Rect(x - 18, y, 36, 6), Texture2D.whiteTexture);
                GUI.color = s.Side == BattleSide.A ? new Color(0.35f, 0.85f, 1) : new Color(1, 0.6f, 0.35f);
                GUI.DrawTexture(new Rect(x - 17, y + 1, 34 * s.Health / s.Stats.Health, 4), Texture2D.whiteTexture);
            }
            GUI.color = Color.white;
        }
        private void DrawReport()
        {
            float width = Mathf.Min(710, UiWidth - 28), height = Mathf.Min(440, UiHeight - 115);
            Rect area = new Rect((UiWidth - width) / 2, (UiHeight - height) / 2 - 20, width, height);
            GUI.Box(area, GUIContent.none); GUILayout.BeginArea(new Rect(area.x + 20, area.y + 15, area.width - 40, area.height - 30));
            reportScroll = GUILayout.BeginScrollView(reportScroll);
            GUILayout.Label("战报 · " + OutcomeName(battle.Report.Outcome), titleStyle);
            GUILayout.Label(battle.Report.Rounds + " 轮  |  指令 " + battle.Report.CommandsIssued + "  |  技能 " + battle.Report.AbilitiesUsed, textStyle);
            GUILayout.Label("小队       在场 / 击倒 / 撤离       伤害", smallStyle);
            foreach (SquadReport row in battle.Report.Squads) GUILayout.Label(row.Name + "    " + row.Standing + " / " + row.Defeated + " / " + row.Escaped + "        " + row.DamageDealt.ToString("F0"), textStyle);
            GUILayout.Space(12); GUILayout.Label("地图、人数、攻击距离和指令都可以调整后重测。击倒只表示本场失去行动能力。", smallStyle);
            if (Button("保留配置，返回准备", 40)) { ReplaceSimulation(battle.Scenario); tab = 0; }
            GUILayout.EndScrollView(); GUILayout.EndArea();
        }
        public static string CommandName(SquadCommand c) => c == SquadCommand.Move ? "移动" : c == SquadCommand.Focus ? "攻击" : c == SquadCommand.Retreat ? "撤退" : c == SquadCommand.Hold ? "驻守" : "自动";
        public static string OutcomeName(BattleOutcome result) => result == BattleOutcome.SideAWin ? "我方胜利" : result == BattleOutcome.SideBWin ? "敌方胜利" : result == BattleOutcome.Draw ? "双方同时倒下" : result == BattleOutcome.Retreat ? "撤退结束" : result == BattleOutcome.Stalemate ? "僵持，无可执行进展" : "达到轮数上限";
        public void DisposePreview()
        {
            realtime=null;realtimeArrows.Clear();
            if(boardView!=sharedMapView) boardView?.Dispose(); rangeOverlay?.Dispose();
            hud?.Dispose(); hud = null;
            BattleBoardView.DestroyObject(material); BattleBoardView.DestroyObject(lineMaterial); BattleBoardView.DestroyObject(font);
            material = lineMaterial = null; font = null;
        }
        private void OnDestroy() {if(battleAudio!=null)battleAudio.ReleaseBattle(realtime);DisposePreview();if(saveMenuFont!=null)BattleBoardView.DestroyObject(saveMenuFont);if(campaignMenu!=null)BattleBoardView.DestroyObject(campaignMenu);}
    }
}

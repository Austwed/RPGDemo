using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;

namespace BaseConquest.Battle
{
    public sealed partial class BattleSceneController
    {
        private bool drawerOpen, fastMode, skipping, gestureActive, gestureDragging, gestureLong, gestureStartedOnUnit;
        private bool deployArmed = true;
        private bool cameraGesture;
        private bool quickSelection;
        private int gestureId;
        private float gestureStartTime, previousSpan;
        private Vector2 gestureStart, gestureCurrent, previousCenter;
        private GridPosition? gestureCell, menuPoint;
        private GridPosition? dragTarget;
        private readonly List<BattleOrderOverlay> extraOrders = new List<BattleOrderOverlay>();
        private readonly Queue<Action> pendingInputs = new Queue<Action>();
        public IReadOnlyList<int> SelectedUnits => selection.Ids;
        public bool Automatic => autoRun;
        public bool FastMode => fastMode;
        public bool QuickSelection => quickSelection;
        public bool Skipping => skipping;
        public bool IsAnimating => animating;
        public bool DrawerVisible => drawerOpen;
        public BattleSide ControlledSide => selectedSide;
        public SoldierKind DeploymentKind => selectedKind;
        public bool DeploymentArmed => deployArmed;
        private bool formationOpen;
        private int deploymentCommanderSlot;
        public bool FormationVisible => formationOpen;
        public int DeploymentCommanderSlot => deploymentCommanderSlot;
        public void ToggleFormation()
        {
            if (battle.ModernRules) { feedback = "出征编队已锁定，请在据点远征／触发战斗前编队"; return; }
            if (battle.Phase != BattlePhase.Deployment || animating || skipping) return;
            ManualPause(); CancelGesture(); CloseContext(); drawerOpen = false; formationOpen = !formationOpen; hud?.Refresh();
        }
        public void SelectCommanderSlot(int slot)
        {
            if (battle.ModernRules) { ClickCommanderPortrait(slot); return; }
            if (slot < 0 || slot >= battle.Scenario.Army(selectedSide).HeroCount || animating || skipping) return;
            deploymentCommanderSlot = slot; var leader = battle.Commander(selectedSide, slot);
            if (leader?.IsActive == true && !formationOpen) ToggleHero(leader.Id);
            else feedback = "部署小队 " + (slot + 1);
            hud?.Refresh();
        }
        public void ChangeAllocation(int slot, SoldierKind kind, int delta)
        {
            feedback = battle.AdjustAllocation(selectedSide, slot, kind, delta) ? "编制已调整" : "调整失败：兵力不足、带兵已满或需先撤回已部署成员"; hud?.Refresh();
        }
        public void ChangeCommanderCount(int delta)
        {
            if (battle.Phase != BattlePhase.Deployment || battle.Soldiers.Any(s => s.Side == selectedSide)) { feedback = "先撤回当前阵营再调整小队长人数"; return; }
            var army = battle.Scenario.Army(selectedSide).Clone(); army.HeroCount = Mathf.Clamp(army.HeroCount + delta, 1, 3);
            battle.ApplyArmy(selectedSide, army); deploymentCommanderSlot = Mathf.Clamp(deploymentCommanderSlot, 0, army.HeroCount - 1); hud?.Refresh();
        }
        public void RetreatAll()
        {
            if(IsRealtimeCampaign){RealtimeRetreat();return;}
            if (animating || skipping || battle.Phase != BattlePhase.Fighting) return;
            ManualPause(); CloseContext(); feedback = battle.RetreatArmy(selectedSide) ? "全军撤退，包括失能小队长的余兵" : "没有可撤退成员"; SyncViews(); hud?.Refresh();
        }
        public void DistributeFormation()
        {
            feedback = battle.DistributeFormation(selectedSide) ? "已均分混编兵力" : "先撤回当前阵营全部单位再均分"; hud?.Refresh();
        }
        public string Feedback => feedback;
        public float HudScale => UiScale;

        public void ToggleAutomatic() { if(IsRealtimeCampaign){ToggleRealtimePause();return;} if (battle.Phase != BattlePhase.Fighting || skipping || VictoryChoicePending) return; CloseContext(); autoRun = !autoRun; feedback = autoRun ? "自动连续推进" : "连续推进已暂停"; }
        public void ToggleFast() { fastMode = !fastMode; feedback = fastMode ? "棋盘 3 倍速" : "棋盘正常速度"; }
        public void ToggleQuickSelection() { if (battle.ModernRules) return; quickSelection = !quickSelection; feedback = quickSelection ? "快速选择开启：连续点选加入／移出" : "快速选择关闭：单击只选当前单位"; hud?.Refresh(); }
        public void SpaceAction()
        {
            if(IsRealtimeCampaign){ToggleRealtimePause();return;}
            if (battle.Phase != BattlePhase.Fighting || skipping) return;
            if (autoRun) { autoRun = false; feedback = "自动已关闭，本轮播放结束后暂停"; }
            else SingleRound();
        }
        public void AdvanceOne() { if(IsRealtimeCampaign&&realtime!=null){ToggleRealtimePause();return;} if (battle.Phase == BattlePhase.Deployment) StartBattle(); else if (battle.Phase == BattlePhase.Finished && !animating) RestartGame(); else if (!skipping) SingleRound(); }
        public void SwitchSide()
        {
            if (IsCampaignBattle) return;
            if (animating || skipping) return;
            ManualPause(); CancelGesture(); CloseContext(); pendingInputs.Clear(); selection.Clear();
            selectedSide = selectedSide == BattleSide.A ? BattleSide.B : BattleSide.A; values.Clear(); selectedUnitId = -1; groupSelected = false; commandTool = CommandTool.None; skillArmed = false;
            deploymentCommanderSlot = Mathf.Clamp(deploymentCommanderSlot, 0, Mathf.Max(0, battle.Scenario.Army(selectedSide).HeroCount - 1));
            deploymentSlotChosen = false; if (battle.ModernRules) deployArmed = false; informationUnitId = -1; informationCell = null; informationIsCell = inspectionTracksUnit = false; CloseCommanderDetails(); boardView.SelectCell(null);
            orderOverlay.Invalidate(); feedback = "测试切换：操作与视野已切换为" + BattleSimulation.SideName(selectedSide); SyncViews(); hud?.Refresh();
        }
        public void ChooseDeployment(SoldierKind kind)
        {
            if (battle.ModernRules && kind != SoldierKind.Hero && deployArmed && selectedKind == kind) { EndDeployment(); return; }
            if (!CanChooseDeployment(kind)) return;
            ManualPause(); CancelGesture(); CloseContext(); if (!battle.ModernRules) { selection.Clear(); selectedUnitId = -1; }
            selectedKind = kind; deployArmed = true; commandTool = CommandTool.None; skillArmed = false;
            feedback = "部署" + BattleSimulation.KindName(kind) + "：点本方部署格" + (battle.ModernRules ? (kind == SoldierKind.Hero ? "，队长席位" + (deploymentCommanderSlot + 1) : "，按所选队长序号使用库存") : "／拖动下兵") + "；再点同一兵种卡或队长头像返回指挥"; SyncViews(); hud?.Refresh();
        }
        public void EndDeployment() { CancelGesture(); CloseContext(); deployArmed = false; feedback = battle.ModernRules ? "点头像选队，点地格移动／点敌人集火，长按或右键打开格子菜单" : "指挥模式：点选单位、点目标或拖动下令"; hud?.Refresh(); }
        public void AutoDeployControlled()
        {
            if (animating || skipping || battle.Phase == BattlePhase.Finished) return;
            ManualPause(); CancelGesture(); int deployed = 0; UserOrder(() => { deployed = battle.ModernRules ? battle.AutoDeploySelected(selectedSide, selection.Ids) : battle.AutoDeploy(selectedSide); return deployed > 0; }); feedback = "已为" + BattleSimulation.SideName(selectedSide) + "部署 " + deployed + " 人"; RefreshUnits(); SyncViews(); hud?.Refresh();
        }
        public void WithdrawSelected()
        {
            if (battle.ModernRules) { WithdrawTargets(WithdrawalIds()); return; }
            if (battle.Phase != BattlePhase.Deployment) return;
            battle.InvalidateUndo();
            foreach (int id in selection.Ids.ToArray()) if (battle.Soldier(id) is BattleSoldier soldier && soldier.Side == selectedSide) battle.Remove(soldier.Cell);
            selection.Clear(); selectedUnitId = -1; feedback = "已撤回所选单位，库存返还"; RefreshUnits(); SyncViews(); hud?.Refresh();
        }
        public void RestartGame()
        {
            if (IsCampaignBattle) return;
            if (animating || skipping) return;
            formationOpen = false; ReplaceSimulation(battle.Scenario); tab = 1; deployArmed = !battle.ModernRules; drawerOpen = false;
            feedback = "已返回准备，保留地图、数值和战前布兵；战中增援返回库存"; hud?.Refresh();
        }
        private bool IsDeploying => (!drawerOpen || tab == 1) && deployArmed && battle.Phase != BattlePhase.Finished;
        private void DeployAt(GridPosition cell)
        {
            if (animating || skipping) { feedback = "本轮播放结束后可继续部署"; return; }
            int slot = FindDeploymentSource(selectedKind, cell);
            bool placed = (!battle.ModernRules || slot >= 0) && (deploymentStrokeActive ? battle.Deploy(selectedSide, selectedKind, cell, slot) : UserOrder(() => battle.Deploy(selectedSide, selectedKind, cell, slot)));
            if (placed && deploymentStrokeActive) deploymentStrokePlaced = true;
            feedback = placed ? "已部署" + BattleSimulation.KindName(selectedKind) + "，剩余库存 " + battle.Remaining(selectedSide, selectedKind) : "部署失败：需本方可通行部署格、有库存且不超出格子容量";
            if (placed && battle.ModernRules && selectedKind == SoldierKind.Hero)
            {
                var leader = battle.Commander(selectedSide, slot); selection.Set(new[] { leader.Id }); informationUnitId = leader.Id; informationIsCell = false; informationCell = null; deployArmed = false;
                feedback = "已选中" + battle.UnitName(leader) + "，可点左侧兵种逐个／拖划部署";
            }
            if (placed && battle.ModernRules && selectedKind != SoldierKind.Hero && !CanChooseDeployment(selectedKind)) deployArmed = false;
            RefreshUnits(); SyncViews(); hud?.Refresh();
        }
        public void ToggleDrawer() { if (IsCampaignBattle) { OpenCampaignMenu(); return; } ManualPause(); CancelGesture(); CloseContext(); formationOpen = false; drawerOpen = !drawerOpen; if (drawerOpen && battle.ModernRules) RefreshScenarioFiles(); hud?.Refresh(); }
        public void CancelAllOrders() { if(realtime!=null){RealtimeOrder(BaseConquest.RealtimePrototype.PrototypeOrder.Auto,new BaseConquest.RealtimePrototype.PrototypePoint(12,7));return;} if (animating || skipping) return; ManualPause(); CloseContext(); UserOrder(() => { if (battle.Phase != BattlePhase.Fighting) return false; battle.CancelOrders(selectedSide); return true; }); feedback = "己方手动命令与未执行技能已取消，撤退保持"; orderOverlay.Invalidate(); SyncViews(); hud?.Refresh(); }
        public void SkipBattle()
        {
            if(IsRealtimeCampaign)
            {
                // P44：只有副手战允许「观看 → 直接结算」，沿当前战场状态继续跑真实模型，不是战力估算。
                if(realtime==null||campaignSession?.Opening.Deputy!=true){feedback="即时首版不跳过战斗，可暂停或切换3倍速";return;}
                FastForwardDeputy();
                return;
            }
            if (battle.Phase != BattlePhase.Fighting) return;
            if (skipping) { skipping = false; feedback = "快速模拟已停止"; return; }
            autoRun = false; CancelGesture(); CloseContext(); skipping = true; feedback = "快速模拟中…可点停止";
        }
        // 副手战单向快进：第一次按下即交回副手，之后不再接受策略改动；反复按会继续推进同一场战斗。
        private void FastForwardDeputy()
        {
            if(battle.Phase!=BattlePhase.Fighting)return;
            battle.Control.Strategy=false;
            autoRun=true;realtime.Model.SetPaused(false);
            var watch=Stopwatch.StartNew();
            int bound=(battle.Scenario.MaxRounds+1)*8;int steps=0;
            while(battle.Phase==BattlePhase.Fighting&&steps++<bound&&watch.ElapsedMilliseconds<1200)realtime.Tick(.5);
            SyncRealtimeViews();hud?.Refresh();
            feedback=battle.Phase==BattlePhase.Finished?"副手已打完，战果按同一套规则结算":"已交给副手，战斗继续快进（"+realtime.Model.Elapsed.ToString("0.0")+"秒）";
        }
        private void RunSkipBatch()
        {
            var watch = Stopwatch.StartNew(); int count = 0;
            while (battle.Phase == BattlePhase.Fighting && !VictoryChoicePending && count++ < 16 && watch.ElapsedMilliseconds < 6) battle.AdvanceRound(true);
            presentation = 1; feedback = "快速模拟：第 " + battle.RoundNumber + " 轮";
            if (VictoryChoicePending) { skipping = false; autoRun = false; }
            if (battle.Phase == BattlePhase.Finished) { skipping = false; feedback = OutcomeName(battle.Report.Outcome); }
        }
        private void ManualPause() {ReleaseRealtimeSteering();autoRun=false;realtime?.Model.SetPaused(true);}
        public void TickPresentation(float delta)
        {
            if (animating) { presentation = Mathf.Min(1, presentation + delta * (fastMode ? 3 : 1) / Mathf.Max(0.1f, presentationSeconds)); if (presentation >= 1) animating = false; }
            if (!animating)
            {
                if (battle.Phase != BattlePhase.Fighting) pendingInputs.Clear();
                while (pendingInputs.Count > 0) pendingInputs.Dequeue()();
                int casts = battle.ResolveQueuedCommanderSkills();
                if (autoRun && !skipping && !VictoryChoicePending) casts += battle.AutoCastCommanderSkills();
                if (casts > 0) { orderOverlay.Invalidate(); SyncViews(); hud?.Refresh(); }
            }
        }
        private void CloseContext() { menuPoint = null; hud?.CloseMenu(); }
        public void ConfirmContext(bool hold)
        {
            if (!menuPoint.HasValue || animating) return;
            bool ok = hold ? battle.ModernRules ? IssueSelectedGuard(menuPoint.Value) : IssueSelectedHold() : IssueSelectedMove(menuPoint.Value);
            feedback = ok ? (hold ? "所选小队驻守当前队形覆盖区" : "所选小队前往目标，到达后待命（不驻守）") : "该目标不可达或所选单位无法接受指令";
            CloseContext(); orderOverlay.Invalidate(); SyncViews();
        }
        public void ToggleHero(int id, bool? additiveOverride = null)
        {
            BattleSoldier s = battle.CommanderFor(battle.Soldier(id)); if (!battle.SelectableCommander(s) || s.Side != selectedSide || skipping) return;
            id = s.Id;
            bool additive = additiveOverride ?? quickSelection;
            if (animating) { ManualPause(); pendingInputs.Enqueue(() => ToggleHero(id, additive)); return; }
            ManualPause(); CloseContext();
            selection.ToggleUnit(id, additive); SelectionChanged();
        }
        public void ToggleCellSelection(GridPosition cell, bool? additiveOverride = null)
        {
            if (animating || skipping) return;
            ManualPause(); selection.ToggleCell(battle, cell, selectedSide, additiveOverride ?? quickSelection); SelectionChanged();
        }
        private void SelectionChanged()
        {
            deployArmed = false;
            groupSelected = false; selectedUnitId = selection.Primary; commandTool = CommandTool.None; skillArmed = false;
            CloseContext(); orderOverlay.Invalidate(); feedback = selection.Ids.Count == 0 ? "已取消选择，原命令保留" : "已选 " + selection.Ids.Count + (battle.Scenario.CommanderControl ? " 名小队长" : " 名单位");
            SyncViews(); hud?.Refresh();
        }
        private bool ResolvePointer(Vector2 point, out GridPosition cell)
        {
            bool found = boardView.Pick(point, out cell); BattleSoldier picked = PickUnit(point);
            if (picked != null) { cell = picked.Cell; return true; } return found;
        }
        private bool IsUiPoint(Vector2 point) => DetailsVisible || formationOpen || (drawerOpen && point.x < PanelWidth * UiScale) || (hud?.BlocksPointer(point) ?? false);
        // Same device route for deployment and realtime combat; camera input never issues orders or pauses.
        public bool WaitForMovementRelease;
        public bool ReadMapCameraInput(float seconds)
        {
            var focused=UnityEngine.EventSystems.EventSystem.current?.currentSelectedGameObject;
            if(campaignMenu!=null||DetailsVisible||hud?.ReportVisible==true||developerPanel?.BlocksGameInput==true||formationOpen||numericFocus||focused!=null&&focused.GetComponent<UnityEngine.UI.InputField>()?.isFocused==true)
            {MapPanUp();return true;}
            var keyboard=Keyboard.current;
            if(WaitForMovementRelease)
            {
                bool held=keyboard!=null&&(keyboard.wKey.isPressed||keyboard.aKey.isPressed||keyboard.sKey.isPressed||keyboard.dKey.isPressed||keyboard.upArrowKey.isPressed||keyboard.downArrowKey.isPressed||keyboard.leftArrowKey.isPressed||keyboard.rightArrowKey.isPressed);
                if(held){MapPanUp();return true;}WaitForMovementRelease=false;
            }
            if(keyboard!=null)
            {
                var direction=new Vector2((keyboard.dKey.isPressed||keyboard.rightArrowKey.isPressed?1:0)-(keyboard.aKey.isPressed||keyboard.leftArrowKey.isPressed?1:0),
                    (keyboard.wKey.isPressed||keyboard.upArrowKey.isPressed?1:0)-(keyboard.sKey.isPressed||keyboard.downArrowKey.isPressed?1:0));
                boardView.Pan(-Vector2.ClampMagnitude(direction,1)*boardView.Camera.pixelHeight*.6f*Mathf.Clamp(seconds,0,.1f));
            }
            var mouse=Mouse.current;if(mouse==null)return false;
            Vector2 position=mouse.position.ReadValue();
            if(mouse.middleButton.wasPressedThisFrame)MapPanDown(position);
            if(mouse.middleButton.isPressed){MapPanMove(position);return true;}
            if(middleMapPan||mouse.middleButton.wasReleasedThisFrame){MapPanUp();return true;}
            if(!IsUiPoint(position)&&developerPanel?.BlocksPointer(position)!=true&&boardView.Camera.pixelRect.Contains(position))
            {
                Vector2 wheel=mouse.scroll.ReadValue();
                if(wheel.y!=0)boardView.ZoomAt(wheel.y>0?.9f:1/.9f,position);
                if(wheel.x!=0)boardView.Pan(new Vector2(-wheel.x*.35f,0));
            }
            return false;
        }
        private void ReadPointer()
        {
            Touchscreen screen = Touchscreen.current;
            if (screen != null)
            {
                var active = screen.touches.Where(t => t.press.isPressed).ToList();
                bool touched = active.Count > 0 || screen.touches.Any(t => t.press.wasReleasedThisFrame);
                if (active.Count >= 2)
                {
                    Vector2 a = active[0].position.ReadValue(), b = active[1].position.ReadValue(), center = (a + b) * 0.5f;
                    if (!cameraGesture) { CancelGesture(); CloseContext(); ManualPause(); previousCenter = center; previousSpan = Vector2.Distance(a, b); cameraGesture = true; }
                    if (!IsUiPoint(a) && !IsUiPoint(b)) { boardView.Pan(center - previousCenter); boardView.Zoom(previousSpan / Mathf.Max(1, Vector2.Distance(a, b))); }
                    previousCenter = center; previousSpan = Vector2.Distance(a, b); return;
                }
                if (cameraGesture) { if (active.Count == 0) cameraGesture = false; return; }
                if (touched)
                {
                    foreach (var t in screen.touches)
                    {
                        int id = t.touchId.ReadValue(); Vector2 p = t.position.ReadValue();
                        if (t.press.wasPressedThisFrame) PointerDown(id, p);
                        else if (t.press.isPressed) PointerMove(id, p);
                        if (t.press.wasReleasedThisFrame) PointerUp(id, p, t.phase.ReadValue() == UnityEngine.InputSystem.TouchPhase.Canceled);
                    }
                    return;
                }
            }
            if(IsRealtimeCampaign&&ReadMapCameraInput(Time.unscaledDeltaTime))return;
            Mouse mouse = Mouse.current; if (mouse == null) return; Vector2 position = mouse.position.ReadValue();
            if(!IsRealtimeCampaign)
            {
                if (mouse.middleButton.wasPressedThisFrame) MapPanDown(position);
                if (mouse.middleButton.isPressed) { MapPanMove(position); return; }
                if (mouse.middleButton.wasReleasedThisFrame) { MapPanUp(); return; }
            }
            if (mouse.leftButton.wasPressedThisFrame) PointerDown(-1, position);
            else if (mouse.leftButton.isPressed) PointerMove(-1, position);
            if (mouse.leftButton.wasReleasedThisFrame) PointerUp(-1, position);
            if (mouse.rightButton.wasPressedThisFrame) RightPointerDown(position);
            if (!gestureActive && !IsUiPoint(position)) hover = ResolvePointer(position, out var hovered) ? hovered : (GridPosition?)null;
            var wheel = mouse.scroll.ReadValue();
            if (!IsRealtimeCampaign&&!IsUiPoint(position))
            {
                if (wheel.x != 0 || Keyboard.current?.shiftKey.isPressed == true) boardView.Pan(new Vector2(wheel.x, wheel.y) * 0.35f);
                else if (wheel.y != 0) boardView.Zoom(wheel.y > 0 ? 0.9f : 1.1f);
            }
        }
        // 这些入口也用于不进入 Play 的原生手势验证，实际设备与测试走同一路由。
        public void PointerDown(int id, Vector2 position)
        {
            if (battle.ModernRules) { ModernPointerDown(id, position); return; }
            if (IsUiPoint(position) || skipping) return;
            if (gestureActive) { CancelGesture(); return; }
            ManualPause(); CloseContext();
            gestureActive = true; gestureId = id; gestureStart = gestureCurrent = position; gestureStartTime = Time.unscaledTime;
            gestureDragging = gestureLong = false; gestureCell = ResolvePointer(position, out var cell) ? cell : (GridPosition?)null;
            gestureStartedOnUnit = gestureCell.HasValue && battle.UnitsAt(cell).Any(s => battle.Vision.CanSee(selectedSide, s));
            if (battle.Phase == BattlePhase.Deployment && gestureCell.HasValue)
            {
                if (drawerOpen && tab == 0) { if (brushShape == 2) { rectangleFrom = cell; lastRectangleCell = cell; } else { ApplyBrush(cell); strokeFrom = cell; } }
                else if (IsDeploying && !gestureStartedOnUnit) { DeployAt(cell); strokeFrom = cell; }
            }
            else if (IsDeploying && gestureCell.HasValue) { DeployAt(cell); strokeFrom = cell; }
        }
        public void PointerMove(int id, Vector2 position)
        {
            if (battle.ModernRules && modernPointerActive) { ModernPointerMove(id, position); return; }
            if (!gestureActive || id != gestureId) return; gestureCurrent = position;
            if (gestureLong) return;
            GridPosition cell = default; bool onBoard = !IsUiPoint(position) && ResolvePointer(position, out cell);
            if (battle.Phase == BattlePhase.Deployment || IsDeploying)
            {
                if (!onBoard) return;
                if (rectangleFrom.HasValue) { lastRectangleCell = cell; boardView.ShowOutline(rectangleFrom.Value, cell, true); }
                else if (strokeFrom.HasValue && strokeFrom.Value != cell) { if (drawerOpen && tab == 0 && battle.Phase == BattlePhase.Deployment) PaintStroke(strokeFrom.Value, cell); else if (IsDeploying) DeployAt(cell); strokeFrom = cell; }
                return;
            }
            if (!gestureDragging && Vector2.Distance(position, gestureStart) > 10 * UiScale)
            {
                gestureDragging = true;
                if (gestureStartedOnUnit && gestureCell.HasValue)
                {
                    var members = battle.UnitsAt(gestureCell.Value).Where(s => s.Side == selectedSide).Select(battle.CommanderFor).Where(battle.SelectableCommander).Distinct().ToList();
                    if (members.Count > 0 && !members.Any(s => selection.Contains(s.Id))) { selection.Set(new[] { members[0].Id }); SelectionChanged(); }
                }
            }
            if (!gestureDragging) return;
            if (gestureStartedOnUnit)
            {
                dragTarget = onBoard ? cell : (GridPosition?)null;
                boardView.ShowOutline(cell, cell, onBoard); var enemy = VisibleEnemyAt(cell); feedback = onBoard ? (enemy != null ? "松手攻击目标" : battle.Board.Walkable(cell) ? "松手移动至此" : "不可通行：松手保留原指令") : "松手取消本次下令";
            }
            else if (gestureCell.HasValue && onBoard) boardView.ShowOutline(gestureCell.Value, cell, true);
        }
        private void UpdateLongPress()
        {
            if (battle.ModernRules) { ModernLongPress(); return; }
            if (gestureActive && !IsDeploying && !gestureDragging && !gestureLong && !animating && battle.Phase == BattlePhase.Fighting && HasSelection && Time.unscaledTime - gestureStartTime >= 0.42f) OpenLongPressMenu();
        }
        public void OpenLongPressMenu()
        {
            if (battle.ModernRules)
            {
                if (!modernPointerActive || modernPanning) return;
                modernHold = true; OpenCellContext(modernStart); return;
            }
            if (!gestureActive || gestureDragging || !gestureCell.HasValue || !HasSelection || animating) return;
            gestureLong = true; menuPoint = gestureCell; hud?.ShowMenu(gestureCurrent); feedback = "前往此处，或让选中单位各自在原地驻守";
        }
        public void PointerUp(int id, Vector2 position, bool canceled = false)
        {
            if (battle.ModernRules && modernPointerActive) { ModernPointerUp(id, position, canceled); return; }
            if (!gestureActive || id != gestureId) return;
            bool wasDrag = gestureDragging, wasLong = gestureLong, fromUnit = gestureStartedOnUnit; GridPosition? start = gestureCell;
            gestureActive = false; boardView.ShowOutline(default, default, false); dragTarget = null;
            if (canceled || IsUiPoint(position)) { rectangleFrom = strokeFrom = null; if (canceled) CloseContext(); return; }
            bool found = ResolvePointer(position, out var cell);
            if (battle.Phase == BattlePhase.Deployment)
            {
                if (rectangleFrom.HasValue) PaintRectangle(rectangleFrom.Value, found ? cell : lastRectangleCell);
                else if ((!drawerOpen || tab == 1) && fromUnit && found && !wasDrag && battle.At(cell)?.Side == selectedSide) SelectSoldier(battle.At(cell).Id);
                rectangleFrom = strokeFrom = null; RefreshUnits(); return;
            }
            if (IsDeploying) { rectangleFrom = strokeFrom = null; return; }
            if (battle.Phase != BattlePhase.Fighting || wasLong) return;
            bool additive = quickSelection;
            if (animating) { pendingInputs.Enqueue(() => HandleBattleRelease(found, cell, wasDrag, fromUnit, start, additive)); feedback = "本轮播放结束后处理点选／命令"; return; }
            HandleBattleRelease(found, cell, wasDrag, fromUnit, start, additive);
        }
        private void HandleBattleRelease(bool found, GridPosition cell, bool wasDrag, bool fromUnit, GridPosition? start, bool additive)
        {
            if (!found) { if (!wasDrag) { selection.Clear(); SelectionChanged(); } return; }
            if (wasDrag && !fromUnit && start.HasValue)
            {
                selection.Set(battle.Soldiers.Where(s => s.IsActive && s.Side == selectedSide && battle.SelectableCommander(s) && s.Cell.X >= Mathf.Min(start.Value.X, cell.X) && s.Cell.X <= Mathf.Max(start.Value.X, cell.X) && s.Cell.Z >= Mathf.Min(start.Value.Z, cell.Z) && s.Cell.Z <= Mathf.Max(start.Value.Z, cell.Z)).Select(s => s.Id)); SelectionChanged(); return;
            }
            if (wasDrag) { if (start.HasValue && battle.UnitsAt(start.Value).Any(s => s.Side == selectedSide)) CommandAt(cell); return; }
            var occupants = battle.UnitsAt(cell).Where(s => battle.Vision.CanSee(selectedSide, s)).ToList();
            if (skillArmed) { feedback = battle.QueueSkill(selectedSide, cell) ? "测试震荡技能已排队" : "测试技能无法选点"; skillArmed = false; }
            else if (commandTool != CommandTool.None) { CommandAt(cell); commandTool = CommandTool.None; }
            else if (occupants.Any(s => s.Side == selectedSide)) ToggleCellSelection(cell, additive);
            else CommandAt(cell);
        }
        private void CommandAt(GridPosition cell)
        {
            if (!HasSelection) return;
            var target = VisibleEnemyAt(cell);
            bool ok = commandTool == CommandTool.Move ? IssueSelectedMove(cell) : target != null && target.Side != selectedSide ? IssueSelectedAttack(target.Id) : commandTool == CommandTool.Attack ? false : IssueSelectedMove(cell);
            feedback = ok ? "所选单位指令已更新，将在下一轮执行" : "非法落点或不可达，原指令保留"; orderOverlay.Invalidate(); SyncViews();
        }
        private BattleSoldier VisibleEnemyAt(GridPosition cell) => battle.UnitsAt(cell).Where(s => s.Side != selectedSide && battle.Vision.CanSee(selectedSide, s)).OrderByDescending(s => s.IsTransit).ThenBy(s => s.Id).FirstOrDefault();
        private void CancelGesture() { FinishDeploymentStroke(); EndPortraitSelection(); middleMapPan = false; modernPointerActive = modernPanning = modernHold = false; gestureActive = gestureDragging = gestureLong = false; strokeFrom = rectangleFrom = null; dragTarget = null; boardView?.ShowOutline(default, default, false); }
    }
}

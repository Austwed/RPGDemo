using System;
using BaseConquest.Base;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;

namespace BaseConquest.Battle
{
    public sealed partial class BattleSceneController
    {
        private int informationUnitId = -1, modernPointerId;
        private bool modernPointerActive, modernPanning, modernHold, suppressInitialDeploy;
        private Vector2 modernStart, modernLast;
        private float modernStartTime, lastPortraitTime = -10;
        private int lastPortraitId = -1;
        private GridPosition? informationCell, lastGroundTap;
        private bool informationIsCell, inspectionTracksUnit;
        private int detailsUnitId = -1;
        private int requestedPatient = -1, detailsSlot = -1, pressedPortrait = -1;
        private float lastGroundTime = -10, portraitPressTime;
        private bool portraitHoldShown, suppressPortraitClick, lastGroundMoved;
        private bool cardGesture, cardDragging, cardAdd;
        private int cardStartSlot;
        private Vector2 cardStartPoint, cardLastPoint;
        private readonly HashSet<int> cardVisited = new HashSet<int>();
        private bool pursuitChosen, victoryResumeAutomatic;
        public bool VictoryChoicePending => battle.VictoryDeclared && battle.Phase == BattlePhase.Fighting && !pursuitChosen;
        public bool PursuitChosen => pursuitChosen;
        public void PursueVictory()
        {
            if (!battle.VictoryDeclared || battle.Phase != BattlePhase.Fighting || animating) return;
            pursuitChosen = true; autoRun = victoryResumeAutomatic;
            feedback = "已解除胜利暂停，保留原命令／策略；" + (autoRun ? "恢复自动推进" : "继续逐轮操作"); hud?.Refresh();
        }
        public bool CanUseCommanderSkill(int slot) => IsRealtimeCampaign ? realtime?.CanCastSkill(selectedSide,slot)==true : !skipping && !VictoryChoicePending && CommanderSkillCharge(slot) >= 1 && battle.CanQueueCommanderSkill(selectedSide, slot);
        public float CommanderSkillCharge(int slot) => IsRealtimeCampaign ? realtime?.SkillCharge(selectedSide,slot)??0 : battle.CommanderSkillChargeFraction(selectedSide, slot, presentation, animating);
        public void ClickCommanderSkill(int slot)
        {
            if (!CanUseCommanderSkill(slot)) return;
            if(IsRealtimeCampaign){if(realtime.CastSkill(selectedSide,slot))feedback=BattleSimulation.CommanderSkillName(slot)+"已施放："+realtime.SkillDescription(slot);SyncRealtimeViews();hud?.Refresh();return;}
            if (animating)
            {
                if (battle.UserAction(() => battle.QueueCommanderSkill(selectedSide, slot))) feedback = BattleSimulation.CommanderSkillName(slot) + "将在本轮动画结束后立即生效";
            }
            else if (battle.CastCommanderSkill(selectedSide, slot))
            { feedback = BattleSimulation.CommanderSkillName(slot) + "已立即生效，不占用移动／攻击行动"; orderOverlay.Invalidate(); SyncViews(); }
            hud?.Refresh();
        }
        public void TestSettlement() => RestartTestBattle(); // 正式结算接入前，测试按钮暂时复用同方案重开。
        public void SettleVictory()
        {
            if (animating || skipping) return;
            if (battle.SettleVictory()) { autoRun = false; CloseContext(); feedback = "已直接结算胜利，存活残军不虚构为击杀"; SyncViews(); hud?.Refresh(); }
        }
        public bool InspectingCell => informationIsCell && informationCell.HasValue;
        public bool PortraitSelectionDragging => cardDragging;
        public void BeginPortraitSelection(int slot, Vector2 point)
        {
            if (DeploymentDraftActive) return;
            if (DetailsVisible || skipping) return;
            cardGesture = true; cardDragging = false; cardStartSlot = slot; cardStartPoint = cardLastPoint = point; cardVisited.Clear();
            var leader = battle.Commander(selectedSide, slot); cardAdd = leader == null || !selection.Contains(leader.Id); PortraitPressStarted(slot);
        }
        public void MovePortraitSelection(Vector2 point)
        {
            if (!cardGesture || portraitHoldShown || DetailsVisible) return;
            if (!cardDragging && Vector2.Distance(cardStartPoint, point) <= 10 * UiScale) return;
            if (!cardDragging) { cardDragging = true; PortraitPressEnded(); ManualPause(); ApplyPortraitCard(cardStartSlot, cardAdd); cardVisited.Add(cardStartSlot); }
            foreach (int slot in hud.PortraitSlotsBetween(cardLastPoint, point))
                if (cardVisited.Add(slot)) ApplyPortraitCard(slot, cardAdd);
            cardLastPoint = point;
        }
        public void EndPortraitSelection() { cardGesture = cardDragging = false; PortraitPressEnded(); }
        private void ApplyPortraitCard(int slot, bool add)
        {
            if (animating) { pendingInputs.Enqueue(() => ApplyPortraitCard(slot, add)); return; }
            var leader = battle.Commander(selectedSide, slot); if (leader?.IsActive != true) return;
            if (selection.Contains(leader.Id) != add) selection.ToggleUnit(leader.Id, true);
            deploymentCommanderSlot = slot; deploymentSlotChosen = true; informationIsCell = inspectionTracksUnit = false; informationCell = null; boardView.SelectCell(null); informationUnitId = leader.Id; SelectionChanged();
        }
        public GridPosition? InformationCell => informationCell;
        public IReadOnlyList<BattleSoldier> InformationUnits => informationIsCell && informationCell.HasValue
            ? VisibleCellUnits(informationCell.Value)
            : battle.Soldier(informationUnitId) is BattleSoldier unit && battle.CanInspectUnit(selectedSide, unit)
                ? new List<BattleSoldier> { unit } : new List<BattleSoldier>();
        private List<BattleSoldier> VisibleCellUnits(GridPosition cell) => realtime!=null&&informationIsCell&&informationFineCell>=0?RealtimeFineUnits(informationFineCell):battle.Soldiers
            .Where(s => s.Cell == cell && !s.Escaped && s.CarriedById < 0 && (s.IsActive || s.IsDowned) && battle.CanInspectUnit(selectedSide, s))
            .OrderBy(s => s.IsTransit).ThenBy(s => s.Id).ToList();
        public int RescuePatientId => requestedPatient;
        public bool DetailsVisible => detailsSlot >= 0 || detailsUnitId >= 0;
        public BattleSoldier DetailsUnit => battle.Soldier(detailsUnitId);
        public bool DetailsCommander => DetailsUnit?.Kind == SoldierKind.Hero || DetailsUnit == null && detailsSlot >= 0;
        public bool CanEditDetailsCommander => DetailsCommander && (DetailsUnit == null || DetailsUnit.Side == selectedSide);
        public int DetailsSlot => detailsSlot;
        public bool CanUndoAction => (realtime==null) && !animating && !skipping && battle.CanUndo;
        public void RestartTestBattle()
        {
            if (IsCampaignBattle) return;
            autoRun = skipping = animating = false; detailsSlot = detailsUnitId = -1; CancelGesture(); CloseContext();
            if (activeScenarioDocument != null) { activeScenarioDocument.Scenario = battle.Scenario.Clone(); activeScenarioDocument.DeploymentMode = "explicit"; LoadScenarioDocument(activeScenarioDocument); } else ReplaceSimulation(battle.Scenario); feedback = "已重开测试，保留地图／数值／策略与战前布置"; hud?.Refresh();
        }
        public bool ChooseSquadStrategy(SquadStrategy strategy)
        {
            if(IsRealtimeCampaign)
            {
                if(detailsSlot<0||!CanEditDetailsCommander||!realtime.Strategy(detailsSlot,strategy))return false;
                feedback="小队策略："+BattleSimulation.StrategyName(strategy)+"；队长直接命令优先，士兵继续遵守策略";SyncRealtimeViews();hud?.Refresh();return true;
            }
            if (detailsSlot < 0 || !CanEditDetailsCommander || animating || skipping) return false;
            bool changed = battle.SetCommanderStrategy(selectedSide, detailsSlot, strategy);
            feedback = changed ? "小队策略：" + BattleSimulation.StrategyName(strategy) + "；手动命令保留，恢复自动后采用新策略" : "当前不能修改策略";
            SyncViews(); hud?.Refresh(); return changed;
        }
        public void StartManualDeployment()
        {
            if (DeploymentDraftActive) { ClearDraftDeployment(); return; }
            if (IsCampaignBattle) return;
            if (animating || skipping) return;
            activeScenarioDocument = null; activeScenarioPath = null;
            var s = battle.Scenario.Clone(); s.Placements.Clear(); s.TestAutoDeployment = false; suppressInitialDeploy = false;
            drawerOpen = false; ReplaceSimulation(s); feedback = "选择可用队长头像后部署；乙方已就位"; hud?.Refresh();
        }
        private bool UserOrder(Func<bool> action) { ManualPause(); return battle.ModernRules ? battle.UserAction(action) : action(); }
        public void UndoOneAction()
        {
            if (!CanUndoAction) return;
            ManualPause(); feedback = battle.UndoLastAction() ? "已撤回上一条未执行命令／部署" : "没有可撤回的行动"; requestedPatient = -1; RefreshUnits(); SyncViews(); hud?.Refresh();
        }
        public void OpenCommanderDetails(int slot) { if(IsRealtimeCampaign&&slot==0)realtime?.TutorialAction?.Invoke(BaseDemoIntro.Inspect); if (slot < 0 || slot >= battle.Scenario.Army(selectedSide).HeroCount) return; if (IsRealtimeCampaign && mapTouchIds.Count > 0) CancelGesture(); ManualPause(); detailsSlot = slot; detailsUnitId = battle.Commander(selectedSide, slot)?.Id ?? -1; hud?.Refresh(); }
        public void OpenUnitDetails(int id)
        {
            var unit = battle.Soldier(id); if (unit == null || !battle.CanInspectUnit(selectedSide, unit)) return;
            if (IsRealtimeCampaign && mapTouchIds.Count > 0) CancelGesture();
            ManualPause(); CloseContext(); detailsUnitId = id; detailsSlot = unit.Kind == SoldierKind.Hero ? unit.OriginCommanderSlot : -1; hud?.Refresh();
        }
        public bool FocusUnit(int id, bool allowDowned = false)
        {
            var unit = battle.Soldier(id);
            if (unit == null || (!unit.IsActive && !(allowDowned && unit.IsDowned)) || !battle.CanInspectUnit(selectedSide, unit)) return false;
            boardView.CenterOn(unit.Cell); boardView.SelectCell(unit.Cell); InspectUnit(id); return true;
        }
        public void CloseCommanderDetails() { detailsSlot = detailsUnitId = -1; suppressPortraitClick = false; hud?.Refresh(); }
        public void PortraitPressStarted(int slot) { pressedPortrait = slot; portraitPressTime = Time.unscaledTime; portraitHoldShown = false; }
        public void PortraitPressEnded() { pressedPortrait = -1; }
        private void UpdatePortraitHold()
        {
            if (pressedPortrait < 0 || portraitHoldShown || Time.unscaledTime - portraitPressTime < 0.42f) return;
            portraitHoldShown = suppressPortraitClick = true; OpenCommanderDetails(pressedPortrait);
        }
        public bool AdjustCommanderRange(int range, int delta)
        {
            if(IsRealtimeCampaign){feedback="即时警戒范围调整待接入";return false;}
            if (detailsSlot < 0 || !CanEditDetailsCommander || animating || skipping) return false;
            if (range == 3) { var lead = battle.Scenario.Army(selectedSide).Commanders[detailsSlot]; lead.LeadRange = Mathf.Clamp(lead.LeadRange + delta, 1, 32); SyncViews(); hud?.Refresh(); return true; }
            var c = battle.Scenario.Army(selectedSide).Commanders[detailsSlot]; int g = c.GuardRange, a = c.AlertRange, p = c.AttackRange;
            if (range == 0) g += delta; else if (range == 1) a += delta; else p += delta;
            bool changed = battle.SetCommanderRanges(selectedSide, detailsSlot, g, a, p);
            feedback = changed ? "已更新驻守／预警／追击上限" : "范围须满足1≤驻守≤预警≤追击≤32"; SyncViews(); hud?.Refresh(); return changed;
        }
        public int InformationUnitId => informationUnitId;
        public bool ModernInput => battle?.ModernRules == true;
        private bool deploymentSlotChosen;
        public bool DeploymentSlotChosen => deploymentSlotChosen;
        public IReadOnlyList<int> SelectedDeploymentSlots => battle.DeploymentSlots(selectedSide, selection.Ids);
        private int FindDeploymentSource(SoldierKind kind, GridPosition? cell = null)
        {
            if (!battle.ModernRules) return battle.Scenario.CommanderControl ? deploymentCommanderSlot : -1;
            if (kind != SoldierKind.Hero) return battle.DeploymentSource(selectedSide, kind, selection.Ids, cell);
            return deploymentSlotChosen && battle.CommanderRemaining(selectedSide, deploymentCommanderSlot, kind) > 0 && (!cell.HasValue || battle.CanDeploy(selectedSide, kind, cell.Value, deploymentCommanderSlot)) ? deploymentCommanderSlot : -1;
        }
        public bool CanChooseDeployment(SoldierKind kind)
        {
            if (animating || skipping || battle.Phase == BattlePhase.Finished || battle.Remaining(selectedSide, kind) == 0) return false;
            return !battle.ModernRules || FindDeploymentSource(kind) >= 0;
        }
        private List<int> WithdrawalIds()
        {
            if (battle.ModernRules && informationIsCell && informationCell.HasValue)
            {
                var viewed = VisibleCellUnits(informationCell.Value);
                if (viewed.Count > 0) return viewed.Where(s => s.Side == selectedSide).Select(s => s.Id).ToList();
            }
            return selection.Ids.ToList();
        }
        public bool CanWithdrawSelection => !animating && !skipping && battle.Phase == BattlePhase.Deployment && WithdrawalIds().Any(id => battle.Soldier(id)?.Side == selectedSide);
        private int WithdrawTargets(IEnumerable<int> ids)
        {
            if (animating || skipping) return 0;
            ManualPause(); CloseContext(); int count = battle.WithdrawUnits(selectedSide, ids);
            if (count > 0) { deployArmed = false; informationUnitId = -1; selection.Prune(battle, selectedSide); selectedUnitId = selection.Primary; }
            feedback = count > 0 ? "已撤回 " + count + " 人，库存返还；撤回队长会连同其小队" : "准备阶段可撤回查看的己方单位或头像选中的队伍";
            RefreshUnits(); SyncViews(); hud?.Refresh(); return count;
        }
        public bool InspectUnit(int id)
        {
            var s = battle.Soldier(id); if (s == null || !battle.CanInspectUnit(selectedSide, s)) return false;
            informationCell = s.Cell; informationIsCell = inspectionTracksUnit = true;ShowInformationNotice();
            informationUnitId = id; feedback = "查看格内全部单位"; SyncViews(); hud?.Refresh(); return true;
        }
        public void ClickCommanderPortrait(int slot)
        {
            if (DeploymentDraftActive) { SelectDraftCommander(slot); return; }
            if (suppressPortraitClick) { suppressPortraitClick = false; return; }
            if(realtime!=null){var commander=battle.Commander(BattleSide.A,slot);if(commander!=null)SelectRealtimeCommander(commander.Id,false,true);return;}
            if (slot < 0 || slot >= battle.Scenario.Army(selectedSide).HeroCount || skipping) return;
            if (animating) { ManualPause(); pendingInputs.Enqueue(() => ClickCommanderPortrait(slot)); return; }
            ShowInformationNotice();CloseContext(); deploymentCommanderSlot = slot; deploymentSlotChosen = true; informationIsCell = inspectionTracksUnit = false; informationCell = null; boardView.SelectCell(null);
            var s = battle.Commander(selectedSide, slot);
            if (s == null)
            {
                selection.Clear(); informationUnitId = selectedUnitId = -1; deployArmed = false;
                if (CanChooseDeployment(SoldierKind.Hero)) ChooseDeployment(SoldierKind.Hero);
                else { feedback = "此指挥者无法重新部署"; hud?.Refresh(); }
                return;
            }
            informationUnitId = s.Id; float now = Time.unscaledTime;
            if (s.IsDowned)
            {
                FocusUnit(s.Id, true);
                if (lastPortraitId == s.Id && now - lastPortraitTime <= 0.35f)
                {
                    BeginRescueOverride(s.Id);
                }
                lastPortraitId = s.Id; lastPortraitTime = now; hud?.Refresh(); return;
            }
            lastPortraitId = s.Id; lastPortraitTime = now;
            if (!s.IsActive || skipping) return;
            ManualPause(); deploymentCommanderSlot = slot;
            if (animating) { pendingInputs.Enqueue(() => ClickCommanderPortrait(slot)); return; }
            selection.ToggleUnit(s.Id, true); SelectionChanged(); informationUnitId = s.Id;
        }
        private void BeginRescueOverride(int patientId)
        {
            ManualPause(); var patient = battle.Soldier(patientId);
            if (patient?.IsDowned != true || patient.CarriedById >= 0) { requestedPatient = -1; feedback = "伤员已被携带或已离场，救援继续"; return; }
            bool matched = false;
            UserOrder(() => { matched = battle.RequestRescue(selectedSide, patientId); return patient.RescueRequested; });
            requestedPatient = patientId;
            var rescuer = battle.Soldiers.FirstOrDefault(u => u.RescueTargetId == patientId);
            feedback = matched ? "已自动匹配" + battle.UnitName(rescuer) + "；带起前单击己方单位可覆盖人选" : "暂无可达救援者，后续回合重试；也可点击己方单位指定";
            SyncViews(); hud?.Refresh();
        }
        public bool IssueSelectedGuard(GridPosition cell) => realtime!=null ? RealtimeOrder(BaseConquest.RealtimePrototype.PrototypeOrder.Guard,new BaseConquest.RealtimePrototype.PrototypePoint(cell.X+.25,cell.Z+.25)) : UserOrder(() => battle.GuardCommanders(selection.Ids, cell));
        public bool IssueSelectedGuardToggle(GridPosition cell)
        {
            bool cancelled = false; bool ok = UserOrder(() => battle.ToggleGuardCommanders(selection.Ids, cell, out cancelled));
            feedback = ok ? cancelled ? "已取消此处驻守，恢复小队策略" : "驻守此位置：" + cell : "该位置不可达，保留原命令"; SyncViews(); hud?.Refresh(); return ok;
        }
        private bool middleMapPan;
        private Vector2 middleMapLast;
        public void MapPanDown(Vector2 point) { if (IsUiPoint(point) || !boardView.Camera.pixelRect.Contains(point)) return; CancelGesture(); middleMapPan = true; middleMapLast = point; }
        public void MapPanMove(Vector2 point) { if (!middleMapPan) return; boardView.Pan(point - middleMapLast); middleMapLast = point; }
        public void MapPanUp() => middleMapPan = false;
        public void RetreatSelected()
        {
            if (animating || skipping || battle.Phase != BattlePhase.Fighting) return;
            ManualPause(); feedback = IssueSelectedRetreat() ? "所选队伍撤退，自动尝试救出倒地指挥者" : "先选择仍可指挥的小队"; SyncViews(); hud?.Refresh();
        }
        private bool deploymentDrag, deploymentStrokeActive, deploymentStrokePlaced;
        private GridPosition? deploymentStrokeLast;
        private Action<bool> finishDeploymentStroke;
        private readonly HashSet<GridPosition> deploymentStrokeCells = new HashSet<GridPosition>();
        private void FinishDeploymentStroke()
        {
            finishDeploymentStroke?.Invoke(deploymentStrokePlaced); finishDeploymentStroke = null;
            deploymentStrokeActive = deploymentStrokePlaced = deploymentDrag = false; deploymentStrokeLast = null; deploymentStrokeCells.Clear();
        }
        private void PaintDeployment(GridPosition cell)
        {
            var from = deploymentStrokeLast ?? cell;
            foreach (var p in BattleSimulation.DragCells(from, cell))
            {
                if (!deploymentStrokeCells.Add(p) || !deployArmed) continue;
                var screen = boardView.Camera.WorldToScreenPoint(BattleBoardView.World(p, battle.Board));
                if (IsUiPoint(new Vector2(screen.x, screen.y))) continue;
                if (battle.Phase == BattlePhase.Deployment && VisibleCellUnits(p).Count > 0) continue;
                DeployAt(p);
            }
            deploymentStrokeLast = cell;
        }
        private void ModernPointerDown(int id, Vector2 position)
        {
            if (IsUiPoint(position)) return;
            CloseContext(); FinishDeploymentStroke(); deploymentDrag = deployArmed;
            modernPointerActive = true; modernPointerId = id; modernPanning = modernHold = false;
            modernStart = modernLast = position; modernStartTime = Time.unscaledTime;
        }
        private void ModernPointerMove(int id, Vector2 position)
        {
            if (!modernPointerActive || modernPointerId != id || modernHold) return;
            if (!modernPanning && Vector2.Distance(modernStart, position) > 10 * UiScale)
            {
                modernPanning = true;
                if (deploymentDrag)
                {
                    deploymentStrokeActive = true; deploymentStrokePlaced = false; finishDeploymentStroke = battle.BeginUserActionGroup();
                    if (boardView.Pick(modernStart, out var start)) PaintDeployment(start);
                }
            }
            if (modernPanning && !IsUiPoint(position))
            {
                if (deploymentDrag) { if (boardView.Pick(position, out var cell)) PaintDeployment(cell); }
                else boardView.Pan(position - modernLast);
            }
            modernLast = position;
        }
        private void ModernLongPress()
        {
            if (!modernPointerActive || modernPanning || modernHold || Time.unscaledTime - modernStartTime < 0.42f || skipping || DetailsVisible) return;
            var unit = PickUnit(modernStart);
            if (unit != null) { modernHold = true; OpenUnitDetails(unit.Id); } else OpenLongPressMenu();
        }
        private void ModernPointerUp(int id, Vector2 position, bool canceled)
        {
            if (!modernPointerActive || modernPointerId != id) return;
            modernPointerActive = false; FinishDeploymentStroke();
            if (modernPanning || modernHold || canceled || IsUiPoint(position)) return;
            bool found = ResolvePointer(position, out var cell);
            if (!found) { selection.Clear(); informationUnitId = -1; informationCell = null; informationIsCell = inspectionTracksUnit = false; requestedPatient = -1; boardView.SelectCell(null); SelectionChanged(); return; }
            boardView.SelectCell(cell); informationCell = cell; informationIsCell = true;ShowInformationNotice();
            bool inspectDeployedUnit = battle.Phase == BattlePhase.Deployment && VisibleCellUnits(cell).Count > 0;
            if (deployArmed && battle.Phase != BattlePhase.Finished && !inspectDeployedUnit) { DeployAt(cell); return; }
            var inCell = VisibleCellUnits(cell);
            var unit = inCell.FirstOrDefault();
            informationUnitId = unit?.Id ?? -1; inspectionTracksUnit = unit != null;
            bool doubleGround = unit == null && lastGroundTap == cell && Time.unscaledTime - lastGroundTime <= 0.32f;
            lastGroundTap = unit == null ? cell : (GridPosition?)null; lastGroundTime = Time.unscaledTime;
            Action action = () =>
            {
                if (requestedPatient >= 0)
                {
                    var rescuer = inCell.FirstOrDefault(s => s.Side == selectedSide && s.IsActive);
                    bool assigned = rescuer != null && UserOrder(() => battle.AssignRescuer(selectedSide, requestedPatient, rescuer.Id));
                    feedback = assigned ? "已指定救援单位" : "请选择可行动且能到达伤员／撤离点的己方单位";
                    if (assigned) requestedPatient = -1; SyncViews(); hud?.Refresh(); return;
                }
                if (unit != null)
                {
                    InspectUnit(unit.Id);
                    if (unit.Side == selectedSide) { SyncViews(); hud?.Refresh(); return; }
                    if (HasSelection && inCell.Any(s => s.IsActive) && battle.Phase == BattlePhase.Fighting)
                    {
                        var target = inCell.FirstOrDefault(s => s.IsActive && s.IsTransit) ?? inCell.First(s => s.IsActive);
                        ManualPause(); feedback = IssueSelectedAttack(target.Id) ? "所选队伍集火目标" : "本方单位尚未发现该敌人；透雾仅用于测试显示";
                    }
                }
                else
                {
                    informationUnitId = -1;
                    if (HasSelection && battle.Phase == BattlePhase.Fighting)
                    {
                        ManualPause();
                        if (doubleGround) { bool ok = lastGroundMoved ? battle.GuardCommanders(selection.Ids, cell) : IssueSelectedGuard(cell); feedback = ok ? "驻守此位置：" + cell : "驻守命令失败"; lastGroundMoved = false; }
                        else { lastGroundMoved = IssueSelectedMove(cell); feedback = lastGroundMoved ? (battle.IsExit(selectedSide,cell) ? "前往撤离点，携带的救援对象会一起离场" : "指挥者前往" + cell) : "位置不可达，原命令保留"; }
                    }
                }
                SyncViews(); hud?.Refresh();
            };
            if (animating) { ManualPause(); pendingInputs.Enqueue(action); } else action();
        }
        public GridPosition? ContextCell => menuPoint;
        public void RightPointerDown(Vector2 point)
        {
            if (DeploymentDraftActive)
            {
                CancelGesture();
                if (!IsUiPoint(point) && ResolvePointer(point, out var draftCell)) TryDraftDeploymentCell(draftCell, true);
                return;
            }
            if (battle.ModernRules) { CancelGesture(); OpenCellContext(point); return; }
            if (IsUiPoint(point) || !ResolvePointer(point, out var cell) || !HasSelection || battle.Phase != BattlePhase.Fighting || animating || skipping) return;
            ManualPause(); menuPoint = cell; hud?.ShowMenu(point);
        }
        public void OpenCellContext(Vector2 point)
        {
            if (DetailsVisible || skipping || IsUiPoint(point) || !ResolvePointer(point, out var cell)) return;
            ManualPause();
            if (animating) { pendingInputs.Enqueue(() => ShowCellContext(cell, point)); return; }
            ShowCellContext(cell, point);
        }
        private void ShowCellContext(GridPosition cell, Vector2 point)
        {
            CloseContext(); lastGroundTap = null; lastGroundMoved = false; boardView.SelectCell(cell); informationCell = cell; informationIsCell = true;ShowInformationNotice();
            informationUnitId = VisibleCellUnits(cell).FirstOrDefault()?.Id ?? -1; inspectionTracksUnit = informationUnitId >= 0;
            menuPoint = cell; feedback = "格子快捷菜单：选择操作后执行"; SyncViews(); hud?.Refresh(); hud?.ShowMenu(point);
        }
        public List<BattleCellCommand> CellCommands(GridPosition cell)
        {
            var commands = new List<BattleCellCommand>();
            void Add(CellCommandKind kind, string label, int id = -1, SoldierKind soldierKind = SoldierKind.Melee) => commands.Add(new BattleCellCommand { Kind = kind, Label = label, UnitId = id, SoldierKind = soldierKind });
            Add(CellCommandKind.Inspect, "查看此格信息");
            var units = VisibleCellUnits(cell);
            bool commandable = battle.Phase == BattlePhase.Fighting && selection.Ids.Any(id => battle.Soldier(id) is BattleSoldier leader && leader.Side == selectedSide && leader.IsActive && leader.Command != SquadCommand.Retreat);
            // 未探索地形不通过菜单项的有无泄露；真正下令仍需经过模拟器验证。
            bool knownCell = battle.CanInspectCell(selectedSide, cell);
            bool mayWalk = !knownCell || battle.Board.Walkable(cell);
            bool seenEnemy = units.Any(u => u.Side != selectedSide && u.IsActive && battle.Vision.CanSee(selectedSide, u));
            if (commandable)
            {
                if (mayWalk && !seenEnemy) { Add(CellCommandKind.Move, "所选队伍前往此处"); Add(CellCommandKind.Guard, "所选队伍驻守此位置"); }
                if (SelectedDeploymentSlots.Any(slot => battle.CommanderSquad(selectedSide, slot) is BattleSquad q && q.GuardAssigned && (q.GuardRequestedCell == cell || q.GuardCenter == cell))) Add(CellCommandKind.CancelGuard, "取消此位置驻守");
                foreach (var enemy in units.Where(u => u.Side != selectedSide && u.IsActive && battle.Vision.CanSee(selectedSide, u)).OrderByDescending(u => u.IsTransit)) Add(CellCommandKind.Focus, "集火 " + battle.UnitName(enemy), enemy.Id);
                Add(CellCommandKind.Automatic, "清除所选队伍指令");
            }
            foreach (var unit in units.Where(u => u.Side == selectedSide))
            {
                if (battle.Phase == BattlePhase.Deployment) Add(CellCommandKind.Withdraw, unit.Kind == SoldierKind.Hero ? "撤回 " + battle.UnitName(unit) + "及其队伍" : "撤回 " + battle.UnitName(unit), unit.Id);
                if (unit.Kind == SoldierKind.Hero) Add(CellCommandKind.Details, "详情 " + battle.UnitName(unit), unit.Id);
                if (battle.Phase == BattlePhase.Fighting && unit.IsDowned) { Add(CellCommandKind.Rescue, "自动救援 " + battle.UnitName(unit), unit.Id); Add(CellCommandKind.SpecifyRescuer, "指定救援者：" + battle.UnitName(unit), unit.Id); }
                if (requestedPatient >= 0 && unit.IsActive) Add(CellCommandKind.AssignRescuer, "由 " + battle.UnitName(unit) + "执行救援", unit.Id);
            }
            if (requestedPatient >= 0) Add(CellCommandKind.CancelRescuer, "取消指定救援操作");
            foreach (var kind in BattleSimulation.TroopKinds)
                if (knownCell && CanChooseDeployment(kind) && FindDeploymentSource(kind, cell) >= 0) Add(CellCommandKind.Deploy, "部署 " + BattleSimulation.KindName(kind), soldierKind: kind);
            if (knownCell && deployArmed && selectedKind == SoldierKind.Hero && CanChooseDeployment(SoldierKind.Hero) && FindDeploymentSource(SoldierKind.Hero, cell) >= 0) Add(CellCommandKind.Deploy, "部署所选队长", soldierKind: SoldierKind.Hero);
            if (deployArmed) Add(CellCommandKind.EndDeployment, "取消兵力选择");

            return commands;
        }
        public void ConfirmCellCommand(BattleCellCommand command)
        {
            if (!menuPoint.HasValue || animating || skipping) return;
            var cell = menuPoint.Value;
            if (!CellCommands(cell).Any(c => c.Kind == command.Kind && c.UnitId == command.UnitId && c.SoldierKind == command.SoldierKind)) { CloseContext(); hud?.Refresh(); return; }
            CloseContext();
            switch (command.Kind)
            {
                case CellCommandKind.Inspect: ShowInformationNotice();informationCell = cell; informationIsCell = true; informationUnitId = VisibleCellUnits(cell).FirstOrDefault()?.Id ?? -1; inspectionTracksUnit = informationUnitId >= 0; feedback = "查看格内全部单位与地形"; break;
                case CellCommandKind.Move: feedback = IssueSelectedMove(cell) ? "所选队伍前往" + cell : "目标不可达，原命令保留"; break;
                case CellCommandKind.Guard: feedback = IssueSelectedGuard(cell) ? "驻守此位置：" + cell : "驻守目标不可达"; break;
                case CellCommandKind.CancelGuard: IssueSelectedGuardToggle(cell); break;
                case CellCommandKind.Focus: feedback = IssueSelectedAttack(command.UnitId) ? "所选队伍集火目标" : "目标已离开本方视野"; break;
                case CellCommandKind.Automatic: feedback = IssueSelectedAuto() ? "所选队伍已恢复自动策略" : "所选队伍无法接受命令"; break;
                case CellCommandKind.Retreat: RetreatSelected(); break;
                case CellCommandKind.Withdraw: WithdrawTargets(new[] { command.UnitId }); break;
                case CellCommandKind.Details: OpenCommanderDetails(battle.Soldier(command.UnitId).OriginCommanderSlot); break;
                case CellCommandKind.Deploy: if (!deployArmed || selectedKind != command.SoldierKind) ChooseDeployment(command.SoldierKind); if (deployArmed && selectedKind == command.SoldierKind) DeployAt(cell); break;
                case CellCommandKind.EndDeployment: EndDeployment(); break;
                case CellCommandKind.SpecifyRescuer: BeginRescueOverride(command.UnitId); break;
                case CellCommandKind.CancelRescuer: requestedPatient = -1; feedback = "已取消选择救援者，已下达救援任务保持"; break;
                case CellCommandKind.AssignRescuer: if (UserOrder(() => battle.AssignRescuer(selectedSide, requestedPatient, command.UnitId))) { requestedPatient = -1; feedback = "已指定救援单位"; } else feedback = "该单位无法到达伤员与撤离点"; break;
                case CellCommandKind.Rescue:
                    bool assigned = false;
                    UserOrder(() => { assigned = battle.RequestRescue(selectedSide, command.UnitId); return battle.Soldier(command.UnitId)?.RescueRequested == true; });
                    feedback = assigned ? "已匹配最快可用救援者" : "暂无可达救援者，后续回合继续尝试"; break;
                case CellCommandKind.RestoreGround: battle.Paint(cell, BoardBrush.Ground); boardView.Refresh(); RefreshUnits(); break;
            }
            SyncViews(); hud?.Refresh();
        }
        public void ToggleTestFog(bool enabled, bool reveal)
        {
            if (IsCampaignBattle) return;
            battle.SetFog(enabled, reveal); orderOverlay.Invalidate(); SyncViews(); hud?.Refresh();
        }
    }
}

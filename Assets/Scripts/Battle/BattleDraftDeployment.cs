using System;
using System.Collections.Generic;
using System.Linq;
using BaseConquest.Base;
using BaseConquest.Story;
using UnityEngine;
using UnityEngine.InputSystem;

namespace BaseConquest.Battle
{
    public sealed partial class BattleSceneController
    {
        private BaseBattlePreparation deploymentDraft;
        private Func<BaseState> deploymentState;
        private Action deploymentBack, deploymentExit, deploymentConfirm;
        private BasePrepUnit[] draftTeams;
        private BasePrepUnit draftArmedUnit;
        private SoldierKind? draftPreferredKind;
        private bool manualFieldEncounter;

        public bool DeploymentDraftActive => deploymentDraft != null;
        public bool DraftPlacementActive { get; private set; }
        private bool DraftInputBlocked => ExternalDialogue || campaignMenu != null;
        internal bool FormalFieldHud => DeploymentDraftActive || manualFieldEncounter;
        internal IReadOnlyList<BasePrepUnit> DraftTeams => draftTeams;
        internal int DraftTeamSlot { get; private set; }
        internal SoldierKind? DraftArmedKind => draftArmedUnit?.Kind;
        internal bool DraftCanConfirm => DeploymentDraftActive && !DraftInputBlocked && deploymentDraft.Positions.ContainsKey(deploymentDraft.PlayerId) &&
            deploymentDraft.Problem(deploymentState(), true) == "";
        internal bool DraftTeamPlaced(int slot) => deploymentDraft.Positions.ContainsKey(draftTeams[slot].Id);
        internal int DraftRemaining(int slot, SoldierKind kind) => deploymentDraft.Units.Count(u => u.Selected &&
            u.TeamId == draftTeams[slot].Id && u.Kind == kind && !deploymentDraft.Positions.ContainsKey(u.Id));
        internal string FieldMapName => DeploymentDraftActive && deploymentDraft.Route == BasePrepRoute.Hideout ? BaseBanditHideout.Title :
            DeploymentDraftActive && deploymentDraft.Route == BasePrepRoute.Ruins ? BaseScriptNames.Ruins :
            GetComponentInParent<BaseSceneController>()?.State is BaseState state ? BaseCampaign.LocationName(state) : "战场";
        internal string FieldDraftGoal => deploymentDraft.Route switch
        {
            BasePrepRoute.Defense => StoryText.Get("story.ui.prep.first_objective", "坚守到敌军撤离，或击败全部来敌。"),
            BasePrepRoute.Second => StoryText.Get("story.ui.prep.second_objective", "击败全部来敌。开战后无法撤退。"),
            BasePrepRoute.BlackCrow => StoryText.Get("story.ui.prep.attack_objective", "使用已抵达的队伍攻占黑鸦寨。"),
            BasePrepRoute.Hideout => BaseBanditHideout.Objective,
            _ => "击败控制来源，救出失踪者。"
        };
        internal (string Ally, string Enemy) FieldDraftSummary()
        {
            var placed = deploymentDraft.Units.Where(u => u.Selected && deploymentDraft.Positions.ContainsKey(u.Id)).ToArray();
            var enemy = deploymentDraft.KnownEnemyCount();
            return ($"我方已部署  队长 {placed.Count(u => u.Kind == SoldierKind.Hero)} · 剑 {placed.Count(u => u.Kind == SoldierKind.Melee)} · 弓 {placed.Count(u => u.Kind == SoldierKind.Ranged)} · 骑 {placed.Count(u => u.Kind == SoldierKind.Cavalry)}",
                "敌方  " + (enemy.HasValue ? $"队长 {deploymentDraft.KnownEnemyCount(SoldierKind.Hero)} · 剑 {deploymentDraft.KnownEnemyCount(SoldierKind.Melee)} · 弓 {deploymentDraft.KnownEnemyCount(SoldierKind.Ranged)} · 骑 {deploymentDraft.KnownEnemyCount(SoldierKind.Cavalry)}" : "暂无已知情报"));
        }

        public void ConfigureDeploymentDraft(BaseBattlePreparation draft, Func<BaseState> state, Action back, Action exit, Action confirm)
        {
            deploymentDraft = draft ?? throw new ArgumentNullException(nameof(draft));
            deploymentState = state ?? throw new ArgumentNullException(nameof(state));
            deploymentBack = back;
            deploymentExit = exit;
            deploymentConfirm = confirm;
            draftTeams = draft.Units.Where(u => u.Selected && u.Kind == SoldierKind.Hero).ToArray();
            DraftPlacementActive = true;
            ArmDraftTeam(0);
        }

        public void ActivateDeploymentEncounter(BaseCampaignSession encounter, Action<RealtimeCampaignBattle> configure,
            Action<double> advance, Func<RealtimeCampaignBattle, bool, bool, bool> finish, string objective)
        {
            if (!DeploymentDraftActive) throw new InvalidOperationException("当前没有待确认的部署");
            boardView.SetDraftDeploymentZones(null);
            deploymentDraft = null;
            deploymentState = null;
            deploymentBack = deploymentExit = deploymentConfirm = null;
            draftTeams = null;
            draftArmedUnit = null;
            DraftPlacementActive = false;
            manualFieldEncounter = true;
            ConfigureSceneEncounter(encounter, configure, advance, finish, objective);
            InitializeSceneEncounter();
            if (realtime != null)
            {
                autoRun = true;
                realtime.Model.SetPaused(false);
                feedback = HasSelection ? "点击空地移动，双击空地驻守。" : "点击格子查看战况。点击头像或按 1–3 选择指挥队长。";
            }
            InitializeBattleDeveloperPanel();
            hud?.Refresh();
        }

        private BattleSimulation BuildDraftPreview()
        {
            var state = deploymentState();
            var scenario = new BattleScenario { Board = deploymentDraft.Board, CampaignRules = true, HeightVision = true,
                ShowEnemiesThroughFog = false, TestAutoDeployment = false, PlayerLeaderSlot = Array.FindIndex(draftTeams, u => u.Id == deploymentDraft.PlayerId),
                B = new ArmyDefinition { HeroCount = 0, MeleeCount = 0, RangedCount = 0, CavalryCount = 0 } };
            var army = scenario.A;
            army.HeroCount = draftTeams.Length;
            army.Capacity = 90;
            foreach (var team in draftTeams)
            {
                army.CommanderNames.Add(team.Name);
                army.CommanderAppearances.Add(BaseCommanderIdentity.Appearance(team.Leader));
                army.CommanderStats.Add(team.Stats.Clone());
                var loadout = new CommanderLoadout();
                foreach (var kind in BattleSimulation.TroopKinds)
                    loadout.Set(kind, deploymentDraft.Units.Count(u => u.Selected && u.TeamId == team.Id && u.Kind == kind));
                army.Commanders.Add(loadout);
            }
            foreach (var kind in BattleSimulation.TroopKinds) army.SetCount(kind, army.Commanders.Sum(c => c.Count(kind)));
            var placed = deploymentDraft.Units.Where(u => u.Selected && deploymentDraft.Positions.ContainsKey(u.Id))
                .OrderBy(u => u.Kind == SoldierKind.Hero ? 0 : 1).ToArray();
            foreach (var unit in placed)
                scenario.Placements.Add(new UnitPlacement { Side = BattleSide.A, Kind = unit.Kind,
                    Cell = deploymentDraft.Positions[unit.Id], CommanderSlot = Array.FindIndex(draftTeams, u => u.Id == unit.TeamId) });
            var preview = new BattleSimulation(scenario);
            if (BaseSceneRules.Active(state))
            {
                string map = deploymentDraft.Route == BasePrepRoute.Ruins && BaseSceneRules.Entrance(state)
                    ? BaseSceneRules.DungeonId : BaseSpatial.Layout(state)?.SourceMapId ?? state.Campaign.Location;
                var memory = state.Demo.TerrainMemories?.FirstOrDefault(m => m.MapId == map && m.Width == preview.Board.Width && m.Height == preview.Board.Height);
                if (memory?.Known != null) preview.Vision.RememberTerrain(BattleSide.A, memory.Known);
            }
            foreach (var unit in placed)
            {
                var soldier = preview.At(deploymentDraft.Positions[unit.Id]);
                if (soldier == null) continue;
                soldier.Stats = unit.Stats.Clone();
                soldier.Stats.Health = (float)unit.MaxHealth;
                soldier.Health = (float)unit.Health;
            }
            return preview;
        }

        private void InitializeDraftDeployment()
        {
            var preview = BuildDraftPreview();
            ReplaceSimulation(preview.Scenario, preview);
            boardView.SetDraftDeploymentZones(deploymentDraft.Board, true);
            SyncViews();
            var cells = deploymentDraft.Board.Positions().Where(p => deploymentDraft.Board.Walkable(p) &&
                deploymentDraft.Board.Cell(p).Zone == DeploymentZone.A).ToArray();
            if (cells.Length > 0)
                boardView.CenterOn(new GridPosition((int)cells.Average(p => p.X), (int)cells.Average(p => p.Z)));
            boardView.UpdateCamera(132f / 1280f, .08f);
            feedback = StoryText.Get("story.ui.prep.deployment_help", "选择队长或兵种，点选或拖划己方部署格。可拖动调整位置，右键或长按撤回。");
        }

        public void RefreshDeploymentDraft(string problem = null)
        {
            if (!DeploymentDraftActive) return;
            if (problem != null) feedback = problem;
            hud?.Refresh();
        }

        private void RefreshDraftPositions()
        {
            foreach (var view in views.Values)
            {
                view.Root.SetActive(false);
                BattleBoardView.DestroyObject(view.Root);
            }
            views.Clear();
            battle = BuildDraftPreview();
            RefreshUnits();
            SyncViews();
            hud?.Refresh();
        }

        public void ToggleDraftDeployment()
        {
            if (!DeploymentDraftActive || DraftInputBlocked) return;
            string problem = deploymentDraft.Problem(deploymentState(), true);
            if (!string.IsNullOrEmpty(problem)) { RefreshDeploymentDraft(problem); return; }
            deploymentConfirm?.Invoke();
        }

        internal void ClearDraftDeployment()
        {
            if (!DraftPlacementActive || DraftInputBlocked) return;
            CancelGesture();
            deploymentDraft.Positions.Clear();
            ArmDraftTeam(0);
            feedback = "已撤销本次部署，可以重新布置。";
            RefreshDraftPositions();
        }

        internal void AutoPlaceDraftDeployment()
        {
            if (!DraftPlacementActive || DraftInputBlocked) return;
            int added = deploymentDraft.AutoPlace();
            if (DraftTeamSlot >= 0) AdvanceDraftQueue();
            feedback = added > 0 ? $"已部署 {added} 人。确认开战前可调整位置。" : "没有可继续部署的单位。";
            RefreshDraftPositions();
        }

        internal void ReturnFromDeploymentDraft()
        {
            if (!DeploymentDraftActive || DraftInputBlocked) return;
            deploymentBack?.Invoke();
        }

        private BasePrepUnit DraftNextUnit(int slot, SoldierKind? kind = null)
        {
            if (slot < 0 || slot >= draftTeams.Length) return null;
            if (!DraftTeamPlaced(slot)) return kind.HasValue ? null : draftTeams[slot];
            var pending = deploymentDraft.Units.Where(u => u.Selected && u.TeamId == draftTeams[slot].Id &&
                u.Kind != SoldierKind.Hero && !deploymentDraft.Positions.ContainsKey(u.Id));
            if (kind.HasValue) return pending.FirstOrDefault(u => u.Kind == kind.Value);
            foreach (var troopKind in BattleSimulation.TroopKinds)
            {
                var next = pending.FirstOrDefault(u => u.Kind == troopKind);
                if (next != null) return next;
            }
            return null;
        }

        private void ArmDraftTeam(int slot)
        {
            DraftTeamSlot = slot >= 0 && slot < draftTeams.Length ? slot : -1;
            draftPreferredKind = null;
            draftArmedUnit = DraftNextUnit(DraftTeamSlot);
        }

        private void AdvanceDraftQueue()
        {
            int current = DraftTeamSlot;
            if (current >= 0 && draftPreferredKind.HasValue)
            {
                var kinds = BattleSimulation.TroopKinds;
                int start = Array.IndexOf(kinds, draftPreferredKind.Value);
                for (int offset = 0; offset < kinds.Length; offset++)
                {
                    var kind = kinds[(start + offset) % kinds.Length];
                    var next = DraftNextUnit(current, kind);
                    if (next == null) continue;
                    draftPreferredKind = kind;
                    draftArmedUnit = next;
                    return;
                }
                draftPreferredKind = null;
            }
            for (int offset = 0; offset < draftTeams.Length; offset++)
            {
                int slot = (current + offset) % draftTeams.Length;
                var next = DraftNextUnit(slot);
                if (next == null) continue;
                DraftTeamSlot = slot;
                draftArmedUnit = next;
                return;
            }
            ArmDraftTeam(-1);
        }

        internal void SelectDraftCommander(int slot)
        {
            if (!DraftPlacementActive || DraftInputBlocked || slot < 0 || slot >= draftTeams.Length) return;
            ArmDraftTeam(DraftTeamSlot == slot ? -1 : slot);
            RefreshDeploymentDraft(DraftTeamSlot < 0 ? "已取消选择。" : "已选择" + draftTeams[slot].Name + "。点己方部署格落位。");
        }

        internal bool DraftCanChooseTroop(int slot, SoldierKind kind) => DraftNextUnit(slot, kind) != null;
        internal void SelectDraftTroop(SoldierKind kind) => SelectDraftTroop(DraftTeamSlot, kind);
        internal void SelectDraftTroop(int slot, SoldierKind kind)
        {
            if (!DraftPlacementActive || DraftInputBlocked) return;
            var unit = DraftNextUnit(slot, kind);
            if (unit == null) return;
            if (DraftTeamSlot == slot && draftArmedUnit?.Kind == kind)
            {
                ArmDraftTeam(-1);
                RefreshDeploymentDraft("已取消选择。");
                return;
            }
            DraftTeamSlot = slot;
            draftPreferredKind = kind;
            draftArmedUnit = unit;
            RefreshDeploymentDraft("点己方部署格放置" + unit.Name + "。");
        }

        private BasePrepUnit DraftUnitAt(GridPosition cell) => deploymentDraft.Units.FirstOrDefault(u =>
            deploymentDraft.Positions.TryGetValue(u.Id, out var point) && point == cell);

        public bool TryDraftDeploymentCell(GridPosition cell, bool recall = false)
        {
            if (!DraftPlacementActive || DraftInputBlocked) return false;
            var placed = DraftUnitAt(cell);
            if (recall)
            {
                if (placed == null) return false;
                deploymentDraft.Recall(placed.Id);
                if (DraftTeamSlot >= 0) ArmDraftTeam(DraftTeamSlot);
                feedback = "已撤回" + placed.Name + "。";
            }
            else if (placed != null)
            {
                draftArmedUnit = placed;
                draftPreferredKind = null;
                DraftTeamSlot = Array.FindIndex(draftTeams, u => u.Id == placed.TeamId);
                RefreshDeploymentDraft("已选择" + placed.Name + "。点另一格或拖动可调整位置。");
                return true;
            }
            else
            {
                if (draftArmedUnit == null) return false;
                if (!PlaceDraftUnit(cell)) { RefreshDeploymentDraft("此处不能部署，原位置保留。"); return false; }
            }
            RefreshDraftPositions();
            return true;
        }

        private bool PlaceDraftUnit(GridPosition cell)
        {
            var unit = draftArmedUnit;
            if (unit == null || !deploymentDraft.Place(unit.Id, cell)) return false;
            feedback = "已部署" + unit.Name + "。";
            AdvanceDraftQueue();
            return true;
        }

        private void UpdateDraftDeployment()
        {
            boardView.UpdateCamera(132f / 1280f, .08f);
            var keyboard = Keyboard.current;
            if (keyboard?.escapeKey.wasPressedThisFrame == true)
            {
                CancelGesture();
                if (DraftTeamSlot < 0) { (deploymentExit ?? deploymentBack)?.Invoke(); return; }
                ArmDraftTeam(-1);
                RefreshDeploymentDraft("已取消选择，保留当前站位。");
                return;
            }
            if (keyboard?.spaceKey.wasPressedThisFrame == true) { HandleSpaceInput(); return; }
            ReadPointer();
            if (gestureActive && gestureId != -1 && !gestureLong && gestureCell.HasValue &&
                Vector2.Distance(gestureStart, gestureCurrent) < 10 * UiScale && Time.unscaledTime - gestureStartTime >= .5f)
            {
                gestureLong = TryDraftDeploymentCell(gestureCell.Value, true);
            }
        }

        private void DraftPointerDown(int id, Vector2 point)
        {
            if (!DraftPlacementActive || DraftInputBlocked || IsUiPoint(point) || !ResolvePointer(point, out var cell)) return;
            gestureActive = true;
            gestureLong = gestureDragging = false;
            gestureStartedOnUnit = DraftUnitAt(cell) != null;
            gestureId = id;
            gestureCell = cell;
            strokeFrom = null;
            gestureStart = gestureCurrent = point;
            gestureStartTime = Time.unscaledTime;
        }

        private void DraftPointerMove(int id, Vector2 point)
        {
            if (!gestureActive || gestureId != id) return;
            gestureCurrent = point;
            if (gestureLong || gestureStartedOnUnit || draftArmedUnit == null) return;
            if (!gestureDragging && Vector2.Distance(gestureStart, point) <= 10 * UiScale) return;
            if (!gestureDragging) { gestureDragging = true; strokeFrom = gestureCell; }
            if (IsUiPoint(point) || !boardView.Pick(point, out var cell)) { strokeFrom = null; return; }
            ApplyDraftStroke(cell);
        }

        private void ApplyDraftStroke(GridPosition cell)
        {
            bool changed = false;
            foreach (var crossed in StrokeCells(strokeFrom ?? cell, cell))
            {
                if (draftArmedUnit == null) break;
                if (DraftUnitAt(crossed) == null && PlaceDraftUnit(crossed)) changed = true;
            }
            strokeFrom = cell;
            if (changed) RefreshDraftPositions();
        }

        private void DraftPointerUp(int id, Vector2 point, bool canceled)
        {
            if (!gestureActive || gestureId != id) return;
            gestureActive = false;
            if (canceled || gestureLong || IsUiPoint(point)) return;
            if (!gestureStartedOnUnit && (gestureDragging || Vector2.Distance(gestureStart, point) > 10 * UiScale))
            {
                if (!gestureDragging) strokeFrom = gestureCell;
                if (boardView.Pick(point, out var end)) ApplyDraftStroke(end);
                return;
            }
            if (!ResolvePointer(point, out var cell)) return;
            if (gestureStartedOnUnit && gestureCell.HasValue && gestureCell.Value != cell)
            {
                var from = DraftUnitAt(gestureCell.Value);
                if (from == null) return;
                if (DraftUnitAt(cell) != null) { RefreshDeploymentDraft("此处已有单位，原位置保留。"); return; }
                draftArmedUnit = from;
                draftPreferredKind = null;
                DraftTeamSlot = Array.FindIndex(draftTeams, u => u.Id == from.TeamId);
            }
            TryDraftDeploymentCell(cell);
        }
    }
}

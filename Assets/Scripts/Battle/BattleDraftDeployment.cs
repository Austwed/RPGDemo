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

        public bool DeploymentDraftActive => deploymentDraft != null;
        public bool DraftPlacementActive { get; private set; }
        internal IReadOnlyList<BasePrepUnit> DraftTeams => draftTeams;
        internal int DraftTeamSlot { get; private set; }
        internal SoldierKind? DraftArmedKind => draftArmedUnit?.Kind;
        internal bool DraftCanConfirm => deploymentDraft.Positions.ContainsKey(deploymentDraft.PlayerId) &&
            deploymentDraft.Problem(deploymentState(), true) == "";
        internal bool DraftTeamPlaced(int slot) => deploymentDraft.Positions.ContainsKey(draftTeams[slot].Id);
        internal int DraftSupply(int grade) => deploymentDraft.Supplies[draftTeams[DraftTeamSlot].Id][grade];
        internal string DraftSupplyProblem => deploymentDraft.InvalidSupplyTeams(deploymentState(), true).Contains(draftTeams[DraftTeamSlot].Id)
            ? StoryText.Get("story.ui.prep.validation.supply", "军需数量超过本队人数或库存") : "";
        internal int DraftTeamDeployedTroops => deploymentDraft.Units.Count(u => u.Selected && u.Kind != SoldierKind.Hero &&
            u.TeamId == draftTeams[DraftTeamSlot].Id && deploymentDraft.Positions.ContainsKey(u.Id));
        internal bool DraftCanChooseTroop(SoldierKind kind) => DraftPlacementActive && DraftUnitForKind(kind) != null;
        internal (int placed, int stock) DraftTroops(SoldierKind kind)
        {
            var team = DraftPlacementActive && draftTeams.Length > 0 ? draftTeams[DraftTeamSlot].Id : null;
            var troops = deploymentDraft.Units.Where(u => u.Selected && u.Kind == kind && (team == null || u.TeamId == team)).ToArray();
            int placed = troops.Count(u => deploymentDraft.Positions.ContainsKey(u.Id));
            return (placed, troops.Length - placed);
        }

        public void ConfigureDeploymentDraft(BaseBattlePreparation draft, Func<BaseState> state, Action back, Action exit, Action confirm)
        {
            deploymentDraft = draft ?? throw new ArgumentNullException(nameof(draft));
            deploymentState = state ?? throw new ArgumentNullException(nameof(state));
            deploymentBack = back;
            deploymentExit = exit;
            deploymentConfirm = confirm;
            draftTeams = draft.Units.Where(u => u.Selected && u.Kind == SoldierKind.Hero).ToArray();
            DraftPlacementActive = false;
            DraftTeamSlot = 0;
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
            ConfigureSceneEncounter(encounter, configure, advance, finish, objective);
            InitializeSceneEncounter();
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
            boardView.SetDraftDeploymentZones(deploymentDraft.Board);
            SyncViews();
            var cells = deploymentDraft.Board.Positions().Where(p => deploymentDraft.Board.Walkable(p) &&
                deploymentDraft.Board.Cell(p).Zone == DeploymentZone.A).ToArray();
            if (cells.Length > 0)
                boardView.CenterOn(new GridPosition((int)cells.Average(p => p.X), (int)cells.Average(p => p.Z)));
            boardView.UpdateCamera(132f / 1280f, .08f);
            feedback = StoryText.Get("story.ui.prep.deployment_start_hint", "点击部署后选择队长和兵种，再在己方部署区落位。");
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
            if (!DeploymentDraftActive || ExternalDialogue) return;
            if (DraftPlacementActive)
            {
                string problem = deploymentDraft.Problem(deploymentState(), true);
                if (!string.IsNullOrEmpty(problem)) { RefreshDeploymentDraft(problem); return; }
                deploymentConfirm?.Invoke();
                return;
            }
            DraftPlacementActive = true;
            boardView.SetDraftDeploymentZones(deploymentDraft.Board, true);
            SyncViews();
            draftArmedUnit = draftTeams.FirstOrDefault();
            RefreshDeploymentDraft(StoryText.Get("story.ui.prep.deployment_help", "选择队长或兵种，点选或拖划己方部署格。拖动已落位单位调整，右键或长按撤回。"));
        }

        internal void ChangeDraftSupply(int grade, int delta)
        {
            if (!DraftPlacementActive || ExternalDialogue || draftTeams.Length == 0) return;
            int[] quota = deploymentDraft.Supplies[draftTeams[DraftTeamSlot].Id];
            quota[grade] = Mathf.Clamp(quota[grade] + delta, 0, BaseMuster.BattleSoldierLimit);
            RefreshDeploymentDraft();
        }

        internal void AutoPlaceDraftDeployment()
        {
            if (!DraftPlacementActive || ExternalDialogue) return;
            int added = deploymentDraft.AutoPlace();
            draftArmedUnit = null;
            feedback = added > 0 ? $"已部署 {added} 人。确认开战前可调整位置。" : "没有可继续部署的单位。";
            RefreshDraftPositions();
        }

        internal void ReturnFromDeploymentDraft()
        {
            if (!DeploymentDraftActive || ExternalDialogue) return;
            deploymentBack?.Invoke();
        }

        private BasePrepUnit DraftUnitForKind(SoldierKind kind)
        {
            if (draftTeams.Length == 0 || !DraftTeamPlaced(DraftTeamSlot)) return null;
            return deploymentDraft.Units.FirstOrDefault(u => u.Selected && u.TeamId == draftTeams[DraftTeamSlot].Id &&
                u.Kind == kind && !deploymentDraft.Positions.ContainsKey(u.Id));
        }

        internal void SelectDraftCommander(int slot)
        {
            if (!DraftPlacementActive || ExternalDialogue || slot < 0 || slot >= draftTeams.Length) return;
            DraftTeamSlot = slot;
            draftArmedUnit = draftTeams[slot];
            RefreshDeploymentDraft("已选择" + draftArmedUnit.Name + "。点己方部署格放置或调整位置。");
        }

        internal void SelectDraftTroop(SoldierKind kind)
        {
            if (!DraftPlacementActive || ExternalDialogue) return;
            var unit = DraftUnitForKind(kind);
            if (unit == null) return;
            draftArmedUnit = unit;
            RefreshDeploymentDraft("点己方部署格放置" + unit.Name + "。");
        }

        private BasePrepUnit DraftUnitAt(GridPosition cell) => deploymentDraft.Units.FirstOrDefault(u =>
            deploymentDraft.Positions.TryGetValue(u.Id, out var point) && point == cell);

        public bool TryDraftDeploymentCell(GridPosition cell, bool recall = false)
        {
            if (!DraftPlacementActive || ExternalDialogue) return false;
            var placed = DraftUnitAt(cell);
            if (recall)
            {
                if (placed == null) return false;
                deploymentDraft.Recall(placed.Id);
                draftArmedUnit = placed;
                DraftTeamSlot = Array.FindIndex(draftTeams, u => u.Id == placed.TeamId);
                feedback = "已撤回" + placed.Name + "。";
            }
            else if (placed != null)
            {
                draftArmedUnit = placed;
                DraftTeamSlot = Array.FindIndex(draftTeams, u => u.Id == placed.TeamId);
                RefreshDeploymentDraft("已选择" + placed.Name + "。点另一格或拖动可调整位置。");
                return true;
            }
            else
            {
                var unit = draftArmedUnit;
                if (unit == null) return false;
                if (!deploymentDraft.Place(unit.Id, cell)) { RefreshDeploymentDraft("此处不能部署，原位置保留。"); return false; }
                draftArmedUnit = unit.Kind == SoldierKind.Hero ? null : DraftUnitForKind(unit.Kind);
                feedback = "已部署" + unit.Name + "。";
            }
            RefreshDraftPositions();
            return true;
        }

        private void UpdateDraftDeployment()
        {
            boardView.UpdateCamera(132f / 1280f, .08f);
            var keyboard = Keyboard.current;
            if (keyboard?.escapeKey.wasPressedThisFrame == true)
            {
                CancelGesture();
                if (!DraftPlacementActive) { (deploymentExit ?? deploymentBack)?.Invoke(); return; }
                DraftPlacementActive = false;
                draftArmedUnit = null;
                boardView.SetDraftDeploymentZones(deploymentDraft.Board);
                SyncViews();
                RefreshDeploymentDraft("已退出部署操作，保留当前站位。");
                return;
            }
            ReadPointer();
            if (gestureActive && gestureId != -1 && !gestureLong && gestureCell.HasValue &&
                Vector2.Distance(gestureStart, gestureCurrent) < 10 * UiScale && Time.unscaledTime - gestureStartTime >= .5f)
            {
                gestureLong = TryDraftDeploymentCell(gestureCell.Value, true);
            }
        }

        private void DraftPointerDown(int id, Vector2 point)
        {
            if (!DraftPlacementActive || IsUiPoint(point) || !ResolvePointer(point, out var cell)) return;
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
            if (gestureLong || gestureStartedOnUnit || draftArmedUnit == null || draftArmedUnit.Kind == SoldierKind.Hero) return;
            if (!gestureDragging)
            {
                if (Vector2.Distance(gestureStart, point) <= 10 * UiScale) return;
                gestureDragging = true;
                strokeFrom = gestureCell;
                if (strokeFrom.HasValue) TryDraftDeploymentCell(strokeFrom.Value);
            }
            if (IsUiPoint(point) || !ResolvePointer(point, out var cell) || strokeFrom == cell || DraftUnitAt(cell) != null) return;
            strokeFrom = cell;
            TryDraftDeploymentCell(cell);
        }

        private void DraftPointerUp(int id, Vector2 point, bool canceled)
        {
            if (!gestureActive || gestureId != id) return;
            gestureActive = false;
            if (canceled || gestureLong || IsUiPoint(point) || !ResolvePointer(point, out var cell)) return;
            if (gestureDragging)
            {
                if (strokeFrom != cell && DraftUnitAt(cell) == null) TryDraftDeploymentCell(cell);
                return;
            }
            if (gestureStartedOnUnit && gestureCell.HasValue && gestureCell.Value != cell)
            {
                var from = DraftUnitAt(gestureCell.Value);
                if (from == null) return;
                if (DraftUnitAt(cell) != null) { RefreshDeploymentDraft("此处已有单位，原位置保留。"); return; }
                draftArmedUnit = from;
                DraftTeamSlot = Array.FindIndex(draftTeams, u => u.Id == from.TeamId);
            }
            TryDraftDeploymentCell(cell);
        }
    }
}

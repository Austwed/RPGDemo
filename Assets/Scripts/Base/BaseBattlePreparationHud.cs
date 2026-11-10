using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BaseConquest.Battle;
using BaseConquest.Presentation;
using BaseConquest.Story;
using UnityEngine;
using UnityEngine.UI;

namespace BaseConquest.Base
{
    public sealed partial class BaseHud
    {
        private const string PreparationView = "battle-preparation", DeploymentView = "battle-deployment", SweepView = "prepared-ruins-sweep";
        private static readonly Color PrepInvalid = new Color32(150, 35, 28, 255);
        private BaseBattlePreparation battleDraft;
        private string prepTeam, prepFeedback;
        private SoldierKind? prepKind;
        private float prepRosterScroll;
        private bool prepInventory, prepProduction;
        private ScrollRect prepRoster;
        private BattleDeploymentGrid prepGrid;
        private BasePrepUnit armedUnit;
        private readonly List<(string Team, int Grade, InputField Field, Color Ink)> prepSupplyFields = new List<(string, int, InputField, Color)>();
        private readonly Dictionary<string, RectTransform> deployCards = new Dictionary<string, RectTransform>();
        private static string PrepText(string key, string fallback) => StoryText.Get("story.ui.prep." + key, fallback);

        internal void OpenBattlePreparation(BasePrepRoute route)
        {
            ClearBattlePreparation();
            try
            {
                battleDraft = new BaseBattlePreparation(PreparationState(route), route);
                prepTeam = battleDraft.PlayerId;
                ShowBattlePreparation();
            }
            catch (InvalidOperationException error) { owner.ReportMessage(error.Message); }
        }

        internal void ClearBattlePreparation()
        {
            battleDraft = null;
            prepTeam = prepFeedback = "";
            prepKind = null;
            prepRosterScroll = 0;
            prepRoster = null;
            prepGrid = null;
            prepInventory = prepProduction = false;
            armedUnit = null;
            prepSupplyFields.Clear();
            deployCards.Clear();
        }

        internal void BattlePreparationPanelClosed()
        {
            if (prepInventory && battleDraft != null)
            {
                prepInventory = false;
                RestorePreparationAfterInventory();
            }
            else ClearBattlePreparation();
        }

        internal bool HandleBattlePreparationShortcut(bool backpack)
        {
            if (battleDraft == null || !PanelOpen) return false;
            if (backpack && panelViewKey == PreparationView) OpenBattlePreparationInventory();
            else if (backpack && prepInventory) ClosePanel();
            return true;
        }

        internal void ReturnToBattleDeployment() => ShowBattleDeployment();

        internal void RefreshPreparedFailure()
        {
            if (battleDraft == null) return;
            string message = owner.Message;
            if (panelViewKey == SweepView) ShowBattlePreparation();
            else if (panelViewKey != DeploymentView) ShowBattleDeployment();
            prepFeedback = message;
            if (panelViewKey == DeploymentView) RefreshDeploymentFields();
            else RefreshPreparationFields();
        }

        private void OpenBattlePreparationInventory()
        {
            RememberPreparationScroll();
            prepInventory = true;
            OpenBackpack();
            prepInventory = activeObject == "backpack";
        }

        private void RestorePreparationAfterInventory()
        {
            var previous = battleDraft;
            try
            {
                var current = new BaseBattlePreparation(PreparationState(previous.Route), previous.Route);
                var oldUnits = previous.Units.ToDictionary(unit => unit.Id);
                var retained = new HashSet<string>();
                foreach (var unit in current.Units.Where(unit => unit.Kind == SoldierKind.Hero))
                {
                    unit.Selected = unit.Id == current.PlayerId || oldUnits.TryGetValue(unit.Id, out var old) && old.Selected;
                    if (!unit.Selected) continue;
                    retained.Add(unit.Id);
                    if (previous.Supplies.TryGetValue(unit.Id, out var supply)) Array.Copy(supply, current.Supplies[unit.Id], 2);
                    if (previous.Positions.TryGetValue(unit.Id, out var cell)) current.Place(unit.Id, cell);
                }
                foreach (var unit in current.Units.Where(unit => unit.Kind != SoldierKind.Hero))
                {
                    unit.Selected = oldUnits.TryGetValue(unit.Id, out var old) && old.Selected && old.Kind == unit.Kind &&
                        old.Health == unit.Health && old.MaxHealth == unit.MaxHealth && retained.Contains(old.TeamId);
                    if (!unit.Selected) continue;
                    retained.Add(unit.Id);
                    unit.TeamId = old.TeamId;
                    if (previous.Positions.TryGetValue(unit.Id, out var cell)) current.Place(unit.Id, cell);
                }
                if (previous.Units.Any(unit => unit.Selected && !retained.Contains(unit.Id)) || previous.Positions.Count != current.Positions.Count)
                    prepFeedback = PrepText("roster_changed", "人员或落点已变化，请检查编成。");
                battleDraft = current;
                ShowBattlePreparation();
            }
            catch (InvalidOperationException error) { ClearBattlePreparation(); owner.ReportMessage(error.Message); }
        }

        private BaseState PreparationState(BasePrepRoute route) => route == BasePrepRoute.Travel ? owner.DepartureState("world") : owner.State;

        private static string PrepTitle(BasePrepRoute route) => route switch
        {
            BasePrepRoute.Ruins => BaseScriptNames.Ruins,
            BasePrepRoute.Defense => PrepText("first_defense", "首次防守"),
            BasePrepRoute.Second => PrepText("second_defense", "第二波防守"),
            BasePrepRoute.BlackCrow => BaseScriptNames.Bandit,
            _ => PrepText("travel", "出征编成")
        };

        private void PrepButton(Transform view, string path, string label, Action click, bool enabled = true)
        {
            var button = Ui<Button>(view, path);
            if (label != null) Ui<Text>(button.transform, "Label").text = label;
            button.onClick.AddListener(() => { if (!owner.Busy) click(); });
            button.interactable = enabled;
        }

        private IEnumerable<BasePrepUnit> PrepLeaders => battleDraft.Units.Where(unit => unit.Kind == SoldierKind.Hero);
        private IEnumerable<BasePrepUnit> PrepSoldiers => battleDraft.Units.Where(unit => unit.Kind != SoldierKind.Hero);
        private IEnumerable<BasePrepUnit> SelectedPrepSoldiers => PrepSoldiers.Where(unit => unit.Selected);
        private static string Number(double value) => value.ToString("0.#");
        private static string PrepCount(IEnumerable<BasePrepUnit> units, SoldierKind kind) => units.Count(unit => unit.Kind == kind).ToString();
        private string PrepSummary(bool deployed)
        {
            var units = battleDraft.Units.Where(unit => unit.Selected && (!deployed || battleDraft.Positions.ContainsKey(unit.Id))).ToArray();
            return $"队长 {PrepCount(units, SoldierKind.Hero)} · 剑士 {PrepCount(units, SoldierKind.Melee)} · 弓兵 {PrepCount(units, SoldierKind.Ranged)} · 骑兵 {PrepCount(units, SoldierKind.Cavalry)}";
        }

        private void ShowBattlePreparation()
        {
            if (battleDraft == null) return;
            bool travel = battleDraft.Route == BasePrepRoute.Travel;
            var state = PreparationState(battleDraft.Route);
            var view = OpenScenePanel("BattlePreparation", PreparationView);
            activeObject = "battle-preparation";
            interactionObject = "";
            prepSupplyFields.Clear();
            prepRoster = Ui<ScrollRect>(view, "Candidates/List");
            Ui<Text>(view, "Title").text = PrepTitle(battleDraft.Route) + (travel ? "" : " · " + PrepText("preparation", "战前准备"));
            PrepButton(view, "Close", null, ClosePanel);
            PrepButton(view, "Reset", null, () => OpenBattlePreparation(battleDraft.Route));
            PrepButton(view, "Backpack", null, OpenBattlePreparationInventory);
            PrepButton(view, "Continue", travel ? PrepText("travel_confirm", "确认出征编成") : null, ContinueBattlePreparation);
            var priority = Ui<Button>(view, "Priority");
            priority.gameObject.SetActive(travel);
            if (travel) PrepButton(view, "Priority", prepProduction ? "生产优先" : "作战优先", () =>
            { prepProduction = !prepProduction; RerenderPreparation(); });
            bool ruins = battleDraft.Route == BasePrepRoute.Ruins;
            Ui<Button>(view, "Sweep").gameObject.SetActive(ruins && state.Demo.RuinsDay > 0);
            Ui<Button>(view, "Reward").gameObject.SetActive(ruins && !string.IsNullOrEmpty(state.Demo.RuinsRewards?.PendingBattleId));
            PrepButton(view, "Sweep", null, ShowPreparedSweep);
            PrepButton(view, "Reward", null, () => { ClearBattlePreparation(); OpenRuinsReward(); });

            var cards = Ui<RectTransform>(view, "Teams");
            foreach (var team in PrepLeaders)
            {
                var card = CloneSceneUi("BattlePreparationTeam", cards);
                BindInventoryPortrait(Ui<RectTransform>(card, "Portrait"), team.Leader);
                string marker = team.Selected ? "● " : "○ ";
                PrepButton(card, "Select", marker + team.Name, () => TogglePrepLeader(team), team.Id != battleDraft.PlayerId);
                Ui<Text>(card, "Health").text = $"生命 {Number(team.Health)} / {Number(team.MaxHealth)}";
                PrepButton(card, "Assign", prepTeam == team.Id ? PrepText("assigning", "正在分配") : PrepText("assign", "分配士兵"), () => SetPrepFilter(team.Id, prepKind), team.Selected);
                foreach (var kind in BattleSimulation.TroopKinds)
                {
                    string key = BaseCampaign.ToBase(kind).ToString();
                    int count = SelectedPrepSoldiers.Count(unit => unit.TeamId == team.Id && unit.Kind == kind);
                    Ui<Text>(card, key + "/Count").text = BattleSimulation.KindName(kind) + " · " + count;
                    PrepButton(card, key + "/Minus", null, () => AdjustPrepTroops(team.Id, kind, false), count > 0);
                    PrepButton(card, key + "/Plus", null, () => AdjustPrepTroops(team.Id, kind, true), team.Selected && SelectedPrepSoldiers.Count() < BaseMuster.BattleSoldierLimit && PrepSoldiers.Any(unit => unit.Kind == kind && !unit.Selected));
                }
                Ui<RectTransform>(card, "Supply").gameObject.SetActive(!travel);
                if (!travel) BindPrepSupply(card, "Supply/", team.Id, false, team.Selected);
            }

            PrepButton(view, "Candidates/All", prepKind == null ? "● 全部" : null, () => SetPrepFilter(prepTeam, null));
            foreach (var kind in BattleSimulation.TroopKinds)
            {
                string path = "Candidates/" + BaseCampaign.ToBase(kind);
                PrepButton(view, path, prepKind == kind ? "● " + BattleSimulation.KindName(kind) : null, () => SetPrepFilter(prepTeam, kind));
            }
            var activeTeam = PrepLeaders.FirstOrDefault(unit => unit.Id == prepTeam && unit.Selected);
            if (activeTeam == null) activeTeam = PrepLeaders.FirstOrDefault(unit => unit.Selected);
            prepTeam = activeTeam?.Id;
            Ui<Text>(view, "Candidates/Title").text = PrepText("roster", "候选名册") + (activeTeam == null ? "" : " · " + activeTeam.Name);
            var rosterNumbers = new Dictionary<SoldierKind, int>();
            foreach (var soldier in PrepSoldiers.Where(unit => !prepKind.HasValue || unit.Kind == prepKind.Value))
            {
                var card = CloneSceneUi("BattlePreparationSoldier", prepRoster.content);
                string assigned = soldier.Selected ? PrepLeaders.FirstOrDefault(unit => unit.Id == soldier.TeamId)?.Name ?? "" : PrepText("unselected", "待选");
                int number = rosterNumbers.GetValueOrDefault(soldier.Kind) + 1;
                rosterNumbers[soldier.Kind] = number;
                string label = $"{soldier.Name} #{number}   生命 {Number(soldier.Health)} / {Number(soldier.MaxHealth)}\n" +
                    $"攻 {Number(soldier.Stats.Damage)} · 防 {Number(soldier.Stats.Defense)}     {(soldier.Selected ? "● " : "○ ")}{assigned}";
                PrepButton(card, "", label, () => SelectPrepSoldier(soldier), activeTeam != null);
                Ui<Text>(card, "Label").alignment = TextAnchor.MiddleLeft;
                if (soldier.Selected) Ui<Image>(card, "").color = new Color32(217, 219, 174, 255);
                if (soldier.Selected && PrepLowHealth(state, soldier)) Ui<Text>(card, "Label").color = PrepInvalid;
            }
            RefreshPreparationFields();
            Canvas.ForceUpdateCanvases();
            prepRoster.content.anchoredPosition = new Vector2(0, Mathf.Clamp(prepRosterScroll, 0, Mathf.Max(0, prepRoster.content.rect.height - prepRoster.viewport.rect.height)));
        }

        private void RememberPreparationScroll()
        {
            if (prepRoster != null) prepRosterScroll = Mathf.Max(0, prepRoster.content.anchoredPosition.y);
        }

        private void RerenderPreparation() { RememberPreparationScroll(); ShowBattlePreparation(); }

        private void SetPrepFilter(string team, SoldierKind? kind)
        {
            prepTeam = team;
            prepKind = kind;
            prepRosterScroll = 0;
            ShowBattlePreparation();
        }

        private void TogglePrepLeader(BasePrepUnit leader)
        {
            if (leader.Id == battleDraft.PlayerId) return;
            leader.Selected = !leader.Selected;
            if (leader.Selected) prepTeam = leader.Id;
            else
            {
                foreach (var soldier in SelectedPrepSoldiers.Where(unit => unit.TeamId == leader.Id).ToArray()) soldier.Selected = false;
                Array.Clear(battleDraft.Supplies[leader.Id], 0, 2);
                if (prepTeam == leader.Id) prepTeam = battleDraft.PlayerId;
            }
            battleDraft.Prune();
            prepFeedback = "";
            RerenderPreparation();
        }

        private void AdjustPrepTroops(string team, SoldierKind kind, bool add)
        {
            var soldier = add ? PrepSoldiers.FirstOrDefault(unit => !unit.Selected && unit.Kind == kind)
                : SelectedPrepSoldiers.LastOrDefault(unit => unit.TeamId == team && unit.Kind == kind);
            if (soldier == null) return;
            if (add) soldier.TeamId = team;
            soldier.Selected = add;
            prepTeam = team;
            prepFeedback = "";
            battleDraft.Prune();
            RerenderPreparation();
        }

        private void SelectPrepSoldier(BasePrepUnit soldier)
        {
            if (!soldier.Selected && SelectedPrepSoldiers.Count() >= BaseMuster.BattleSoldierLimit)
            {
                prepFeedback = PrepText("limit", "出战士兵最多 24 人");
                RefreshPreparationFields();
                return;
            }
            soldier.Selected = !soldier.Selected || soldier.TeamId != prepTeam;
            if (soldier.TeamId != prepTeam) battleDraft.Recall(soldier.Id);
            soldier.TeamId = prepTeam;
            prepFeedback = "";
            battleDraft.Prune();
            RerenderPreparation();
        }

        private static bool PrepLowHealth(BaseState state, BasePrepUnit soldier) => !BaseDevelopment.WarningOff(state) && soldier.MaxHealth > 0 && soldier.Health / soldier.MaxHealth < BaseDevelopment.WarningThreshold(state);

        private void BindPrepSupply(Transform card, string prefix, string team, bool deployed, bool enabled = true)
        {
            for (int i = 0; i < 2; i++)
            {
                int grade = i;
                var field = Ui<InputField>(card, prefix + "Grade" + (i + 1) + "/Input");
                field.SetTextWithoutNotify(battleDraft.Supplies[team][grade].ToString(CultureInfo.InvariantCulture));
                field.interactable = enabled;
                prepSupplyFields.Add((team, grade, field, field.textComponent.color));
                field.onValueChanged.AddListener(value =>
                {
                    if (battleDraft == null) return;
                    battleDraft.Supplies[team][grade] = int.TryParse(value, out int n) ? n : 0;
                    prepFeedback = "";
                    if (deployed) RefreshDeploymentFields(); else RefreshPreparationFields();
                });
            }
        }

        private void RefreshPrepSupplyInk(BaseState state, bool deployed)
        {
            var teams = PrepLeaders.Where(unit => unit.Selected).Select(unit => unit.Id).ToArray();
            var soldiers = SelectedPrepSoldiers.Where(unit => !deployed || battleDraft.Positions.ContainsKey(unit.Id)).ToLookup(unit => unit.TeamId);
            var overstock = new bool[2];
            for (int grade = 0; grade < 2; grade++)
                overstock[grade] = teams.Sum(team => Math.Max(0, battleDraft.Supplies[team][grade])) > battleDraft.AvailableSupply(state, grade + 1);
            foreach (var binding in prepSupplyFields)
            {
                var supply = battleDraft.Supplies[binding.Team];
                int value = supply[binding.Grade];
                bool invalid = value < 0 || value > 0 && (overstock[binding.Grade] || supply[0] + supply[1] > soldiers[binding.Team].Count());
                binding.Field.textComponent.color = invalid ? PrepInvalid : binding.Ink;
            }
        }

        private string PrepSupplyStock(BaseState state)
        {
            var teams = PrepLeaders.Where(unit => unit.Selected).Select(unit => unit.Id).ToArray();
            int totalLow = teams.Sum(team => Math.Max(0, battleDraft.Supplies[team][0]));
            int totalHigh = teams.Sum(team => Math.Max(0, battleDraft.Supplies[team][1]));
            return $"军需分配／库存   一级 {totalLow} / {battleDraft.AvailableSupply(state, 1)}  ·  二级 {totalHigh} / {battleDraft.AvailableSupply(state, 2)}";
        }

        private void RefreshPreparationFields()
        {
            var state = PreparationState(battleDraft.Route);
            bool travel = battleDraft.Route == BasePrepRoute.Travel;
            var problem = battleDraft.Problem(state, false);
            var context = "";
            if (travel && problem == "")
            {
                try
                {
                    var plan = battleDraft.TravelPlan(state, prepProduction);
                    context = PrepText("travel_context", "抵达黑鸦寨后，可在随行人员中重新编成及部署。") +
                        $"\n预计日产变化   粮食 {(-plan.FoodLoss):+0;-0;0}  ·  军需 {(-plan.ProductLoss):+0;-0;0}";
                    if (prepProduction && plan.Production?.Allowed != true) problem = plan.Production?.Problem ?? PrepText("production", "当前编成无法保障生产");
                }
                catch (ArgumentException error) { problem = error.Message; }
                catch (InvalidOperationException error) { problem = error.Message; }
            }
            else if (!travel)
            {
                context = PrepSupplyStock(state) + "\n" + (battleDraft.Route switch
                {
                    BasePrepRoute.Ruins => $"侵蚀 {BaseRuinsRewards.Exposure(state):P0} / 秒 · 体力 {BaseRuinsRewards.Energy(state)} / {BaseRuinsRewards.Maximum(state)}" +
                        (state.Demo.RuinsDay == 0 ? " · 首通领奖免费" : " · 复打领奖消耗 " + BaseRuinsRewards.Cost(state)),
                    BasePrepRoute.Defense => PrepText("first_objective", "坚守到敌军撤离，或击败全部来敌。"),
                    BasePrepRoute.Second => PrepText("second_objective", "击败全部来敌。开战后无法撤退。"),
                    _ => PrepText("attack_objective", "使用已抵达的队伍攻占黑鸦寨。")
                });
                RefreshPrepSupplyInk(state, false);
            }
            var (ally, enemy) = battleDraft.Power(state, false);
            Ui<Text>(panel, "Ally/Value").text = (travel ? "我方出征" : "我方战力 " + Number(ally)) + "\n" + PrepSummary(false);
            Ui<Text>(panel, "Enemy/Value").text = travel ? PrepText("enemy_arrival", "敌方 · 抵达战场后查看") : enemy.HasValue
                ? "敌方参考战力 " + Number(enemy.Value) + "\n" + PrepText("enemy_roster_unknown", "敌军编成待侦察")
                : PrepText("enemy_unknown", "敌方 · 暂无已知情报");
            Ui<Text>(panel, "Context").text = context;
            Ui<Button>(panel, "Continue").interactable = problem == "";
            Ui<Button>(panel, "Sweep").interactable = problem == "";
            int warning = SelectedPrepSoldiers.Count(unit => PrepLowHealth(state, unit));
            var feedback = Ui<Text>(panel, "Feedback");
            feedback.text = problem != "" ? problem : !string.IsNullOrEmpty(prepFeedback) ? prepFeedback : warning > 0 ? $"生命预警 · {warning} 名所选士兵低于 {BaseDevelopment.WarningThreshold(state):P0}" : "";
            if (feedback.text != "") feedback.color = PrepInvalid;
        }

        private void ContinueBattlePreparation()
        {
            RememberPreparationScroll();
            if (battleDraft.Route != BasePrepRoute.Travel) { ShowBattleDeployment(); return; }
            try
            {
                var plan = battleDraft.TravelPlan(PreparationState(BasePrepRoute.Travel), prepProduction);
                if (prepProduction && plan.Production?.Allowed != true) return;
                owner.CompleteTravel("world", plan);
                if (owner.State.Campaign.Location != "world") { prepFeedback = owner.Message; RefreshPreparationFields(); }
            }
            catch (InvalidOperationException error) { prepFeedback = error.Message; RefreshPreparationFields(); }
            catch (ArgumentException error) { prepFeedback = error.Message; RefreshPreparationFields(); }
        }

        private void ShowBattleDeployment()
        {
            if (battleDraft?.Board == null) return;
            battleDraft.Prune();
            var view = OpenScenePanel("BattleDeployment", DeploymentView);
            activeObject = "battle-deployment";
            Ui<Text>(view, "Title").text = PrepTitle(battleDraft.Route) + " · " + PrepText("deployment", "部署");
            PrepButton(view, "Close", null, ClosePanel);
            PrepButton(view, "Back", null, ShowBattlePreparation);
            PrepButton(view, "Auto", null, () => { battleDraft.AutoPlace(); armedUnit = null; prepFeedback = ""; RefreshDeploymentFields(); });
            PrepButton(view, "Recall", null, () => { battleDraft.Positions.Clear(); armedUnit = NextDeploymentUnit(); prepFeedback = ""; RefreshDeploymentFields(); });
            PrepButton(view, "Start", null, () => owner.BeginPreparedBattle(battleDraft));
            var board = Ui<RectTransform>(view, "Board");
            var grid = new GameObject("战场格", typeof(RectTransform), typeof(CanvasRenderer), typeof(BattleDeploymentGrid));
            var rect = (RectTransform)grid.transform;
            rect.SetParent(board, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            prepGrid = grid.GetComponent<BattleDeploymentGrid>();
            prepGrid.Bind(battleDraft, SelectDeploymentCell);
            prepSupplyFields.Clear();
            deployCards.Clear();
            foreach (var team in PrepLeaders.Where(unit => unit.Selected))
            {
                var card = CloneSceneUi("BattleDeploymentTeam", Ui<RectTransform>(view, "Teams"));
                deployCards.Add(team.Id, card);
                PrepButton(card, "Captain", null, () =>
                {
                    if (battleDraft.Positions.ContainsKey(team.Id)) battleDraft.Recall(team.Id);
                    armedUnit = team;
                    prepFeedback = "";
                    RefreshDeploymentFields();
                });
                foreach (var kind in BattleSimulation.TroopKinds)
                    PrepButton(card, BaseCampaign.ToBase(kind).ToString(), null, () =>
                    {
                        armedUnit = NextDeploymentUnit(team.Id, kind);
                        prepFeedback = "";
                        RefreshDeploymentFields();
                    });
                BindPrepSupply(card, "", team.Id, true);
            }
            armedUnit = NextDeploymentUnit();
            prepFeedback = "";
            RefreshDeploymentFields();
        }

        private BasePrepUnit NextDeploymentUnit(string team = null, SoldierKind? kind = null)
        {
            var missing = battleDraft.Units.Where(unit => unit.Selected && !battleDraft.Positions.ContainsKey(unit.Id)).ToArray();
            if (team != null) return missing.FirstOrDefault(unit => unit.TeamId == team && (!kind.HasValue || unit.Kind == kind));
            foreach (var leader in PrepLeaders.Where(unit => unit.Selected))
            {
                if (!battleDraft.Positions.ContainsKey(leader.Id)) return leader;
                var soldier = missing.FirstOrDefault(unit => unit.TeamId == leader.Id);
                if (soldier != null) return soldier;
            }
            return null;
        }

        private void SelectDeploymentCell(GridPosition cell, bool recall, bool dragging)
        {
            if (battleDraft == null) return;
            var occupant = battleDraft.Positions.FirstOrDefault(pair => pair.Value == cell).Key;
            if (occupant != null)
            {
                if (dragging) return;
                battleDraft.Recall(occupant);
                armedUnit = battleDraft.Units.FirstOrDefault(unit => unit.Id == occupant);
                prepFeedback = "";
            }
            else if (!recall && armedUnit != null)
            {
                if (battleDraft.Place(armedUnit.Id, cell))
                {
                    var placed = armedUnit;
                    armedUnit = NextDeploymentUnit(placed.TeamId, placed.Kind) ?? NextDeploymentUnit(placed.TeamId) ?? NextDeploymentUnit();
                    prepFeedback = "";
                }
                else prepFeedback = PrepText("illegal_cell", "这里无法放置该单位，请选择己方部署区内的可用格。");
            }
            RefreshDeploymentFields();
        }

        private void RefreshDeploymentFields()
        {
            var state = owner.State;
            var problem = battleDraft.Problem(state, true);
            var context = Ui<Text>(panel, "Context");
            string help = PrepText("deployment_help", "点选或拖划浅色格部署。点击已部署单位可撤回。");
            context.text = (armedUnit == null ? "" : PrepText("placing", "放置中") + " · " + armedUnit.Name + "    |    ") + help + "    |    " + PrepSupplyStock(state);
            foreach (var pair in deployCards)
            {
                var team = PrepLeaders.Single(unit => unit.Id == pair.Key);
                var card = pair.Value;
                bool placed = battleDraft.Positions.ContainsKey(team.Id);
                Ui<Text>(card, "Captain/Label").text = (armedUnit == team ? "● " : "") + team.Name + (placed ? " · " + PrepText("recall_captain", "撤回分队") : " · " + PrepText("captain", "部署队长"));
                int totalPlaced = 0;
                foreach (var kind in BattleSimulation.TroopKinds)
                {
                    string key = BaseCampaign.ToBase(kind).ToString();
                    var group = SelectedPrepSoldiers.Where(unit => unit.TeamId == team.Id && unit.Kind == kind).ToArray();
                    int count = group.Count(unit => battleDraft.Positions.ContainsKey(unit.Id));
                    totalPlaced += count;
                    bool active = armedUnit?.TeamId == team.Id && armedUnit.Kind == kind;
                    Ui<Text>(card, key + "/Label").text = (active ? "● " : "") + BattleSimulation.KindName(kind) + " " + count + "/" + group.Length;
                    Ui<Button>(card, key).interactable = placed && count < group.Length;
                }
                Ui<Text>(card, "Placed").text = $"已布兵 {totalPlaced}";
            }
            RefreshPrepSupplyInk(state, true);
            prepGrid.SetVerticesDirty();
            Ui<Button>(panel, "Start").interactable = problem == "";
            var feedback = Ui<Text>(panel, "Feedback");
            feedback.text = !string.IsNullOrEmpty(prepFeedback) ? prepFeedback : problem != "" ? problem : PrepSummary(true);
            feedback.color = problem != "" || !string.IsNullOrEmpty(prepFeedback) ? PrepInvalid : DemoUiTheme.Current.Ink;
        }

        private void ShowPreparedSweep()
        {
            if (battleDraft == null) return;
            RememberPreparationScroll();
            var state = owner.State;
            BaseSweepPreview preview;
            try { preview = battleDraft.PreviewSweep(state); }
            catch (ArgumentException error) { prepFeedback = error.Message; RefreshPreparationFields(); return; }
            catch (InvalidOperationException error) { prepFeedback = error.Message; RefreshPreparationFields(); return; }
            BeginPanel(BaseScriptNames.Ruins + " · 扫荡确认", SweepView);
            string ratio = preview.EnemyPower > 0 ? (preview.AllyPower / preview.EnemyPower).ToString("0.00") : "—";
            Line($"我方战力 {preview.AllyPower:0.00} / 敌方 {preview.EnemyPower:0.00} ≈ {ratio} 倍 · 需严格超过 1.5 倍", 64, 21);
            Line($"体力 {BaseRuinsRewards.Energy(state)} / {BaseRuinsRewards.Maximum(state)} · 领奖消耗 {BaseRuinsRewards.Cost(state)}", 52, 18);
            if (preview.Problem != "") Line(preview.Problem, 66, 18);
            RowButton("返回备战", -205, 390, ShowBattlePreparation);
            RowButton("确认扫荡", 205, 390, () => owner.SweepPreparedRuins(battleDraft), preview.Problem == "");
            row += 60;
            EndPanel();
        }
    }
}

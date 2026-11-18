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
        private const string PreparationView = "battle-preparation", SweepView = "prepared-ruins-sweep";
        private static readonly Color PrepInvalid = new Color32(150, 35, 28, 255);
        private enum PrepSort { Role, Health, Status }
        private BaseBattlePreparation battleDraft;
        private string prepTeam, prepFeedback, prepDetail;
        private SoldierKind? prepKind;
        private PrepSort prepSort;
        private bool prepDescending;
        private float prepRosterScroll;
        private bool prepProduction;
        private ScrollRect prepRoster;
        private readonly Dictionary<string, (RectTransform Card, Color PortraitColor)> prepCards = new();
        private readonly Dictionary<string, (RectTransform Card, Color Background, Color Text)> prepRows = new();
        private readonly Dictionary<string, (Button Button, Color Normal)> prepFilters = new();
        private Dictionary<string, BasePersonnelUnit> prepRosterUnits;
        private (GameObject Node, bool Visible)[] deploymentHudVisibility;
        private static string PrepText(string key, string fallback) => StoryText.Get("story.ui.prep." + key, fallback);
        private bool PreparationHelpOpen => panelViewKey == PreparationView && panel != null && panel.Find("HelpOverlay").gameObject.activeSelf;

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
            bool wasDeploying = owner.DeploymentWorldActive;
            owner.EndDeploymentWorld();
            ShowExplorationHudAfterDeployment();
            if (wasDeploying) SetVisible(true);
            battleDraft = null;
            prepTeam = prepFeedback = prepDetail = "";
            prepKind = null;
            prepSort = PrepSort.Role;
            prepDescending = false;
            prepRosterScroll = 0;
            prepRoster = null;
            prepCards.Clear();
            prepRows.Clear();
            prepFilters.Clear();
            prepRosterUnits = null;
            prepProduction = false;
        }

        internal void BattlePreparationPanelClosed() => ClearBattlePreparation();

        internal bool HandleBattlePreparationShortcut(bool backpack) => battleDraft != null && PanelOpen;

        internal void ReturnToBattleDeployment() => ShowBattleDeployment();

        internal void RefreshPreparedFailure()
        {
            if (battleDraft == null) return;
            string message = owner.Message;
            if (owner.DeploymentWorldActive) { owner.ResumeDeploymentWorld(message); return; }
            if (panelViewKey == SweepView) ShowBattlePreparation();
            prepFeedback = message;
            if (panelViewKey == PreparationView) RefreshPreparationFields();
        }

        private BaseState PreparationState(BasePrepRoute route) => route == BasePrepRoute.Travel ? owner.DepartureState("world") : owner.State;

        private static string PrepTitle(BasePrepRoute route) => route switch
        {
            BasePrepRoute.Ruins => BaseScriptNames.Ruins,
            BasePrepRoute.Defense => PrepText("first_defense", "首次防守"),
            BasePrepRoute.Second => PrepText("second_defense", "第二波防守"),
            BasePrepRoute.BlackCrow => BaseScriptNames.Bandit,
            BasePrepRoute.Hideout => BaseBanditHideout.Title,
            _ => PrepText("travel", "出征编队")
        };

        private void PrepButton(Transform view, string path, string label, Action click)
        {
            var button = Ui<Button>(view, path);
            if (label != null) Ui<Text>(button.transform, "Label").text = label;
            button.onClick.AddListener(() => { if (!owner.Busy && !PreparationHelpOpen) click(); });
            button.interactable = true;
        }

        private IEnumerable<BasePrepUnit> PrepLeaders => battleDraft.Units.Where(unit => unit.Kind == SoldierKind.Hero);
        private IEnumerable<BasePrepUnit> PrepSoldiers => battleDraft.Units.Where(unit => unit.Kind != SoldierKind.Hero);
        private IEnumerable<BasePrepUnit> SelectedPrepSoldiers => PrepSoldiers.Where(unit => unit.Selected);
        private static string Number(double value) => value.ToString("0.#");
        private string PrepSummary()
        {
            var units = battleDraft.Units.Where(unit => unit.Selected).ToArray();
            return PrepCounts(kind => units.Count(unit => unit.Kind == kind));
        }

        private static string PrepCounts(Func<SoldierKind, int> count) =>
            $"队长 {count(SoldierKind.Hero)} · 剑士 {count(SoldierKind.Melee)} · 弓兵 {count(SoldierKind.Ranged)} · 骑兵 {count(SoldierKind.Cavalry)}";

        private static void PrepMeter(Transform view, string path, double fraction)
        {
            var fill = Ui<RectTransform>(view, path);
            var anchors = fill.anchorMax;
            anchors.x = Mathf.Clamp01((float)fraction);
            fill.anchorMax = anchors;
        }

        private void ShowBattlePreparation()
        {
            if (battleDraft == null) return;
            owner.EndDeploymentWorld();
            ShowExplorationHudAfterDeployment();
            SetVisible(true);
            bool travel = battleDraft.Route == BasePrepRoute.Travel;
            var state = PreparationState(battleDraft.Route);
            var view = OpenScenePanel("BattlePreparation", PreparationView);
            if (Application.platform == RuntimePlatform.Android) AdaptBattlePreparationForTouch(view);
            activeObject = "battle-preparation";
            interactionObject = "";
            prepRoster = Ui<ScrollRect>(view, "Candidates/List");
            Ui<Text>(view, "Title").text = PrepTitle(battleDraft.Route) + (travel ? "" : " · " + PrepText("preparation", "战前准备"));
            PrepButton(view, "Back", PrepText("back", "返回"), ClosePanel);
            PrepButton(view, "Help", null, ShowPreparationHelp);
            BindPreparationHelp(view, travel);
            PrepButton(view, "Close", null, ClosePanel);
            PrepButton(view, "Reset", null, () => OpenBattlePreparation(battleDraft.Route));
            PrepButton(view, "Continue", travel ? PrepText("travel_confirm", "确认出征编队") : PrepText("deployment_enter", "进入战场"), ContinueBattlePreparation);
            var priority = Ui<Button>(view, "Priority");
            priority.gameObject.SetActive(travel);
            if (travel) PrepButton(view, "Priority", null, () =>
            {
                prepProduction = !prepProduction;
                RefreshPreparationFields();
            });
            var route = battleDraft.Route;
            bool dungeon = BaseRuinsRewards.IsDungeon(route);
            Ui<Button>(view, "Sweep").gameObject.SetActive(dungeon && BaseRuinsRewards.Cleared(state, route));
            Ui<Button>(view, "Reward").gameObject.SetActive(dungeon && BaseRuinsRewards.PendingBattle(state, route) != "");
            PrepButton(view, "Sweep", null, ShowPreparedSweep);
            PrepButton(view, "Reward", null, () => { ClearBattlePreparation(); OpenRuinsReward(route); });
            var supply = view.Find("Supply");
            supply.gameObject.SetActive(!travel);
            if (!travel)
            {
                PrepButton(supply, "Grade1", null, () => ChoosePrepSupply(1));
                PrepButton(supply, "Grade2", null, () => ChoosePrepSupply(2));
            }

            var cards = Ui<RectTransform>(view, "Teams");
            var activeTeam = PrepLeaders.FirstOrDefault(unit => unit.Id == prepTeam) ?? PrepLeaders.FirstOrDefault(unit => unit.Selected);
            prepTeam = activeTeam?.Id;
            prepCards.Clear();
            foreach (var team in PrepLeaders)
            {
                var card = CloneSceneUi("BattlePreparationTeam", cards);
                if (Application.platform == RuntimePlatform.Android)
                    foreach (string kind in new[] { "Melee", "Ranged", "Cavalry" })
                    {
                        var group = card.Find(kind);
                        var minus = group.Find("Minus").GetComponent<Button>();
                        var plus = group.Find("Plus").GetComponent<Button>();
                        var input = (RectTransform)group.Find("Input");
                        ((RectTransform)minus.transform).sizeDelta = new Vector2(38, 49);
                        ((RectTransform)plus.transform).sizeDelta = new Vector2(38, 49);
                        ((RectTransform)plus.transform).anchoredPosition = new Vector2(84, -17);
                        input.sizeDelta = new Vector2(42, 49);
                        input.anchoredPosition = new Vector2(40, -17);
                        ResizeTouchButton(minus, 49, 22);
                        ResizeTouchButton(plus, 49, 22);
                    }
                var portrait = Ui<RectTransform>(card, "Portrait");
                BindInventoryPortrait(portrait, team.Leader);
                prepCards.Add(team.Id, (card, portrait.GetComponent<Image>().color));
                var rowSelect = card.gameObject.GetOrAdd<Button>();
                rowSelect.transition = Selectable.Transition.None;
                rowSelect.onClick.AddListener(() => SelectPreparationTeam(team));
                PrepButton(card, "Select", null, () => SelectPreparationTeam(team));
                Ui<Text>(card, "Health").text = $"生命 {Number(team.Health)} / {Number(team.MaxHealth)}";
                PrepMeter(card, "HealthMeter/Fill", team.MaxHealth > 0 ? team.Health / team.MaxHealth : 0);
                var membership = card.Find("Assign");
                membership.gameObject.SetActive(team.Id != battleDraft.PlayerId);
                PrepButton(card, "Assign", null, () => TogglePrepLeader(team));
                foreach (var kind in BattleSimulation.TroopKinds)
                    BindPrepCount(card, team, kind);
            }

            var candidates = view.Find("Candidates");
            prepFilters.Clear();
            BindPrepFilter(candidates, "All", null);
            foreach (var kind in BattleSimulation.TroopKinds)
                BindPrepFilter(candidates, BaseCampaign.ToBase(kind).ToString(), kind);
            foreach (PrepSort sort in Enum.GetValues(typeof(PrepSort)))
                PrepButton(candidates, "Sort" + sort, null, () => SetPrepSort(sort));
            PrepButton(candidates, "Detail/Back", null, () => { prepDetail = ""; RefreshPreparationRoster(); });
            prepRows.Clear();
            prepRosterUnits = BasePersonnel.Units(state).Where(unit => !unit.IsLeader).ToDictionary(unit => unit.Key);
            RefreshPreparationEdits();
            Canvas.ForceUpdateCanvases();
            prepRoster.content.anchoredPosition = new Vector2(0, Mathf.Clamp(prepRosterScroll, 0, Mathf.Max(0, prepRoster.content.rect.height - prepRoster.viewport.rect.height)));
        }

        private static void AdaptBattlePreparationForTouch(RectTransform view)
        {
            foreach (var path in new[] { "Back", "Help", "Close" })
            {
                var button = view.Find(path).GetComponent<Button>();
                var rect = (RectTransform)button.transform;
                rect.anchoredPosition = new Vector2(rect.anchoredPosition.x, -2);
                ResizeTouchButton(button, 42, 20);
            }
            foreach (var path in new[] { "Continue", "Reset", "Priority", "Sweep", "Reward" })
            {
                var button = view.Find(path).GetComponent<Button>();
                var rect = (RectTransform)button.transform;
                rect.anchoredPosition = new Vector2(rect.anchoredPosition.x, -569);
                ResizeTouchButton(button, 58, 22);
            }
            foreach (var path in new[] { "Supply/Grade1", "Supply/Grade2" })
                ResizeTouchButton(view.Find(path).GetComponent<Button>(), 50, 21);

            var candidates = view.Find("Candidates");
            foreach (var name in new[] { "All", "Melee", "Ranged", "Cavalry" })
                ResizeTouchButton(candidates.Find(name).GetComponent<Button>(), 40, 20);
            foreach (var name in new[] { "SortRole", "SortHealth", "SortStatus" })
            {
                var button = candidates.Find(name).GetComponent<Button>();
                var rect = (RectTransform)button.transform;
                rect.anchoredPosition = new Vector2(rect.anchoredPosition.x, -76);
                ResizeTouchButton(button, 36, 19);
            }
            foreach (var name in new[] { "List", "Detail", "Empty" })
            {
                var rect = (RectTransform)candidates.Find(name);
                rect.anchoredPosition = new Vector2(rect.anchoredPosition.x, rect.anchoredPosition.y - 17);
                if (name != "Empty")
                    rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, rect.rect.height - 17);
            }

            var feedbackRect = (RectTransform)view.Find("Feedback");
            feedbackRect.anchoredPosition = new Vector2(feedbackRect.anchoredPosition.x, -537);
            feedbackRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, 28);
        }

        private void BindPreparationHelp(Transform view, bool travel)
        {
            var help = view.Find("HelpOverlay");
            Ui<Text>(help, "Window/Title").text = PrepText("help_title", "备战说明");
            string roster = PrepText("help_roster", "选定队长后，可以修改各兵种人数，或在候选名册中点选具体士兵。");
            Ui<Text>(help, "Window/Body").text = roster + "\n\n" + (travel
                ? PrepText("travel_context", "抵达黑鸦寨后，可在随行人员中重新编队及部署。")
                : PrepText("help_supply", "每名士兵需要一份军需，所有士兵使用同一等级。仅队长出战时无需军需。") + "\n\n" +
                  PrepText("help_place", "敌方只显示已知情报。进入战场后先放置队长，再点按或拖划部署所属士兵。"));
            var close = Ui<Button>(help, "Window/Close");
            Ui<Text>(close.transform, "Label").text = PrepText("help_close", "返回备战");
            close.onClick.AddListener(() => ClosePreparationHelp());
            help.gameObject.SetActive(false);
        }

        private void ShowPreparationHelp()
        {
            var help = panel.Find("HelpOverlay");
            help.gameObject.SetActive(true);
            Ui<Button>(help, "Window/Close").Select();
        }

        private bool ClosePreparationHelp()
        {
            if (!PreparationHelpOpen) return false;
            panel.Find("HelpOverlay").gameObject.SetActive(false);
            Ui<Button>(panel, "Help").Select();
            return true;
        }

        private void RememberPreparationScroll()
        {
            if (prepRoster != null) prepRosterScroll = Mathf.Max(0, prepRoster.content.anchoredPosition.y);
        }

        private void ResetPreparationScroll()
        {
            prepRosterScroll = 0;
            prepRoster.StopMovement();
            prepRoster.content.anchoredPosition = Vector2.zero;
        }

        private void BindPrepFilter(Transform view, string path, SoldierKind? kind)
        {
            var button = Ui<Button>(view, path);
            PrepButton(view, path, null, () => SetPrepFilter(kind));
            prepFilters.Add(path, (button, button.targetGraphic.color));
        }

        private void SetPrepFilter(SoldierKind? kind)
        {
            if (prepKind == kind && string.IsNullOrEmpty(prepDetail)) return;
            prepKind = kind;
            prepDetail = "";
            ResetPreparationScroll();
            RefreshPreparationRoster();
        }

        private void SelectPreparationTeam(BasePrepUnit team)
        {
            if (owner.Busy || PreparationHelpOpen || prepTeam == team.Id) return;
            prepTeam = team.Id;
            prepDetail = "";
            RefreshPreparationTeams();
            RefreshPreparationRoster();
        }

        private void TogglePrepLeader(BasePrepUnit leader)
        {
            if (leader.Id == battleDraft.PlayerId) return;
            leader.Selected = !leader.Selected;
            if (leader.Selected) prepTeam = leader.Id;
            else
            {
                foreach (var soldier in SelectedPrepSoldiers.Where(unit => unit.TeamId == leader.Id).ToArray()) soldier.Selected = false;
                if (prepTeam == leader.Id) prepTeam = battleDraft.PlayerId;
            }
            battleDraft.Prune();
            prepFeedback = "";
            RefreshPreparationEdits();
        }

        private int PrepTroopCount(BasePrepUnit team, SoldierKind kind) =>
            SelectedPrepSoldiers.Count(unit => unit.TeamId == team.Id && unit.Kind == kind);

        private void BindPrepCount(Transform card, BasePrepUnit team, SoldierKind kind)
        {
            string key = BaseCampaign.ToBase(kind).ToString();
            var field = Ui<InputField>(card, key + "/Input");
            field.onEndEdit.AddListener(value =>
            {
                if (battleDraft == null || panelViewKey != PreparationView || !PanelOpen) return;
                if (!owner.Busy && !PreparationHelpOpen && int.TryParse(value, out int requested))
                    SetPrepTroopCount(team, kind, requested);
                field.SetTextWithoutNotify(PrepTroopCount(team, kind).ToString(CultureInfo.InvariantCulture));
            });
            PrepButton(card, key + "/Minus", null, () => SetPrepTroopCount(team, kind, PrepTroopCount(team, kind) - 1));
            PrepButton(card, key + "/Plus", null, () => SetPrepTroopCount(team, kind, PrepTroopCount(team, kind) + 1));
        }

        private void RefreshPreparationEdits()
        {
            RefreshPreparationTeams();
            RefreshPreparationRoster();
            RefreshPreparationFields();
        }

        private void RefreshPreparationTeams()
        {
            var selected = SelectedPrepSoldiers.ToArray();
            var availableKinds = PrepSoldiers.Where(unit => !unit.Selected).Select(unit => unit.Kind).ToHashSet();
            int slot = 0;
            foreach (var team in PrepLeaders)
            {
                var (card, portraitColor) = prepCards[team.Id];
                Ui<Image>(card, "Portrait").color = prepTeam == team.Id ? DemoUiTheme.Current.Gold : portraitColor;
                string marker = team.Selected ? (char)('A' + slot++) + " · " : "○ ";
                Ui<Text>(card, "Select/Label").text = marker + team.Name;
                Ui<Text>(card, "Assign/Label").text = team.Selected ? "移出出战" : "加入出战";
                foreach (var kind in BattleSimulation.TroopKinds)
                {
                    var group = card.Find(BaseCampaign.ToBase(kind).ToString());
                    int count = selected.Count(unit => unit.TeamId == team.Id && unit.Kind == kind);
                    var field = Ui<InputField>(group, "Input");
                    if (!field.isFocused) field.SetTextWithoutNotify(count.ToString(CultureInfo.InvariantCulture));
                    field.interactable = team.Selected;
                    Ui<Button>(group, "Minus").interactable = team.Selected && count > 0;
                    Ui<Button>(group, "Plus").interactable = team.Selected && selected.Length < BaseMuster.BattleSoldierLimit && availableKinds.Contains(kind);
                }
            }
        }

        private bool SetPrepTroopCount(BasePrepUnit team, SoldierKind kind, int requested)
        {
            if (battleDraft == null || !team.Selected || !battleDraft.Units.Contains(team)) return false;
            var selected = SelectedPrepSoldiers.Where(unit => unit.TeamId == team.Id && unit.Kind == kind).ToArray();
            var available = PrepSoldiers.Where(unit => !unit.Selected && unit.Kind == kind).ToArray();
            int room = BaseMuster.BattleSoldierLimit - SelectedPrepSoldiers.Count();
            int count = Mathf.Clamp(requested, 0, selected.Length + Math.Min(available.Length, Math.Max(0, room)));
            if (count == selected.Length)
            {
                SelectPreparationTeam(team);
                return true;
            }
            foreach (var soldier in selected.Skip(count)) soldier.Selected = false;
            foreach (var soldier in available.Take(count - selected.Length))
            {
                soldier.TeamId = team.Id;
                soldier.Selected = true;
            }
            prepTeam = team.Id;
            prepFeedback = "";
            battleDraft.Prune();
            RefreshPreparationEdits();
            return true;
        }

        private void SelectPrepSoldier(BasePrepUnit soldier)
        {
            if (!PrepLeaders.Any(leader => leader.Id == prepTeam && leader.Selected)) return;
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
            RefreshPreparationEdits();
        }

        private static bool PrepLowHealth(BaseState state, BasePrepUnit soldier) => !BaseDevelopment.WarningOff(state) && soldier.MaxHealth > 0 && soldier.Health / soldier.MaxHealth < BaseDevelopment.WarningThreshold(state);

        private void SetPrepSort(PrepSort sort)
        {
            prepDescending = prepSort == sort ? !prepDescending : sort != PrepSort.Role;
            prepSort = sort;
            prepDetail = "";
            ResetPreparationScroll();
            RefreshPreparationRoster();
        }

        private void RefreshPreparationRoster()
        {
            var view = panel.Find("Candidates");
            var activeTeam = PrepLeaders.FirstOrDefault(unit => unit.Id == prepTeam);
            Ui<Text>(view, "Title").text = PrepText("roster", "候选名册") + (activeTeam == null ? "" : " · " + activeTeam.Name) +
                $" · {PrepText("selected_count", "已选")} {SelectedPrepSoldiers.Count()}/{BaseMuster.BattleSoldierLimit}";
            var theme = DemoUiTheme.Current;
            string selectedFilter = prepKind.HasValue ? BaseCampaign.ToBase(prepKind.Value).ToString() : "All";
            foreach (var (path, (button, normal)) in prepFilters)
                button.targetGraphic.color = path == selectedFilter ? theme.Gold : normal;
            string arrow = prepDescending ? " ↓" : " ↑";
            Ui<Text>(view, "SortRole/Label").text = PrepText("sort_role", "兵种") + (prepSort == PrepSort.Role ? arrow : "");
            Ui<Text>(view, "SortHealth/Label").text = PrepText("sort_health", "生命") + (prepSort == PrepSort.Health ? arrow : "");
            Ui<Text>(view, "SortStatus/Label").text = PrepText("sort_status", "状态") + (prepSort == PrepSort.Status ? arrow : "");

            var detail = view.Find("Detail");
            BasePersonnelUnit detailUnit = null;
            bool showingDetail = !string.IsNullOrEmpty(prepDetail) && prepRosterUnits.TryGetValue(prepDetail, out detailUnit);
            detail.gameObject.SetActive(showingDetail);
            prepRoster.gameObject.SetActive(!showingDetail);
            var empty = Ui<Text>(view, "Empty").gameObject;
            if (showingDetail)
            {
                empty.SetActive(false);
                Ui<Text>(detail, "Title").text = detailUnit.Name;
                Ui<Text>(detail, "Body").text = $"{BattleSimulation.KindName(detailUnit.Kind)}  ·  生命 {Number(detailUnit.Health)} / {Number(detailUnit.MaxHealth)}\n" +
                    $"当前攻击 {Number(detailUnit.Current.Damage)}  ·  当前防御 {Number(detailUnit.Current.Defense)}\n" +
                    $"基础攻击 {Number(detailUnit.Basic.Damage)}  ·  基础防御 {Number(detailUnit.Basic.Defense)}\n\n" +
                    $"{detailUnit.Facility}  ·  {detailUnit.State}\n{detailUnit.Effects.Replace("／", "\n")}";
                return;
            }

            var candidates = PrepSoldiers.Where(soldier => !prepKind.HasValue || soldier.Kind == prepKind.Value).ToArray();
            empty.SetActive(candidates.Length == 0);
            var visibleIds = candidates.Select(soldier => soldier.Id).ToHashSet();
            foreach (var entry in prepRows)
                if (!visibleIds.Contains(entry.Key)) entry.Value.Card.gameObject.SetActive(false);
            IOrderedEnumerable<BasePrepUnit> sorted = prepSort switch
            {
                PrepSort.Health => candidates.OrderBy(unit => unit.MaxHealth <= 0 ? 0 : unit.Health / unit.MaxHealth),
                PrepSort.Status => candidates.OrderBy(unit => unit.Selected).ThenBy(unit => prepRosterUnits[unit.Id].State, StringComparer.CurrentCulture),
                _ => candidates.OrderBy(unit => unit.Kind)
            };
            int index = 0;
            foreach (var soldier in prepDescending ? sorted.Reverse() : sorted)
            {
                var entry = prepRosterUnits[soldier.Id];
                if (!prepRows.TryGetValue(soldier.Id, out var row))
                {
                    var card = AddPersonnelRow(prepRoster.content, entry, "", () =>
                    {
                        if (!PreparationHelpOpen) SelectPrepSoldier(soldier);
                    }, details: () =>
                    {
                        if (PreparationHelpOpen) return;
                        prepRoster.StopMovement();
                        prepDetail = soldier.Id;
                        RefreshPreparationRoster();
                    }, showStats: false);
                    row = (card, Ui<Image>(card, "").color, Ui<Text>(card, "Label").color);
                    prepRows.Add(soldier.Id, row);
                }

                if (row.Card.GetSiblingIndex() != index) row.Card.SetSiblingIndex(index);
                index++;
                row.Card.gameObject.SetActive(true);
                Ui<Button>(row.Card, "").interactable = activeTeam?.Selected == true;
                Ui<Image>(row.Card, "").color = soldier.Selected ? Color.Lerp(row.Background, theme.Gold, .38f) : row.Background;
                string assigned = soldier.Selected ? PrepLeaders.FirstOrDefault(team => team.Id == soldier.TeamId)?.Name ?? "" : PrepText("unselected", "待选");
                var label = Ui<Text>(row.Card, "Label");
                label.text = PersonnelRowLabel(entry, $"{(soldier.Selected ? "● " : "○ ")}{assigned} · {entry.State}", false, false);
                label.color = soldier.Selected && PrepLowHealth(owner.State, soldier) ? PrepInvalid : row.Text;
            }
        }

        private void ChoosePrepSupply(int grade)
        {
            battleDraft.SupplyGrade = grade;
            prepFeedback = "";
            RefreshPreparationFields();
        }

        private void RefreshPrepSupply(BaseState state)
        {
            var rule = state.Supply.Rules.FirstOrDefault(entry => entry.Product == "supply");
            int required = battleDraft.RequiredSupply(false);
            var view = panel.Find("Supply");
            Ui<Text>(view, "Demand").text = required == 0 ? PrepText("supply_none", "仅队长出战 · 所需军需 0") :
                PrepText("supply_required", "本次所需军需") + " " + required;
            for (int grade = 1; grade <= 2; grade++)
            {
                string name = grade == 1 ? PrepText("grade_1", "一级") : PrepText("grade_2", "二级");
                string path = "Grade" + grade;
                int stock = battleDraft.AvailableSupply(state, grade);
                bool chosen = required > 0 && battleDraft.SupplyGrade == grade;
                var stockLabel = Ui<Text>(view, "Stock" + grade);
                stockLabel.text = name + " " + PrepText("stock", "库存") + " " + stock;
                var button = Ui<Button>(view, path);
                button.interactable = required > 0;
                button.targetGraphic.color = chosen ? DemoUiTheme.Current.Gold : DemoUiTheme.Current.Button;
                double attack = (grade == 1 ? rule?.LowBonus : rule?.HighBonus) ?? 0;
                double defense = (grade == 1 ? rule?.LowDefense : rule?.HighDefense) ?? 0;
                string choice = grade == 1 ? PrepText("supply_use_1", "使用一级军需") : PrepText("supply_use_2", "使用二级军需");
                Ui<Text>(button.transform, "Label").text = (chosen ? "● " : "") + choice + $"\n攻 +{Number(attack)} / 防 +{Number(defense)}";
            }
        }

        private void RefreshPreparationFields()
        {
            var state = PreparationState(battleDraft.Route);
            bool travel = battleDraft.Route == BasePrepRoute.Travel;
            var problem = battleDraft.Problem(state, false);
            var context = "";
            if (travel) Ui<Text>(panel, "Priority/Label").text = prepProduction ? "生产优先" : "作战优先";
            if (travel && problem == "")
            {
                try
                {
                    var plan = battleDraft.TravelPlan(state, prepProduction);
                    context = PrepText("travel_context", "抵达黑鸦寨后，可在随行人员中重新编队及部署。") +
                        $"\n预计日产变化   粮食 {(-plan.FoodLoss):+0;-0;0}  ·  军需 {(-plan.ProductLoss):+0;-0;0}";
                    if (prepProduction && plan.Production?.Allowed != true) problem = plan.Production?.Problem ?? PrepText("production", "当前编队无法保障生产");
                }
                catch (ArgumentException error) { problem = error.Message; }
                catch (InvalidOperationException error) { problem = error.Message; }
            }
            else if (!travel)
            {
                context = battleDraft.Route switch
                {
                    BasePrepRoute.Ruins => $"侵蚀 {BaseRuinsRewards.Exposure(state):P0} / 秒 · " + DungeonEnergy(state, battleDraft.Route),
                    BasePrepRoute.Defense => PrepText("first_objective", "坚守到敌军撤离，或击败全部来敌。"),
                    BasePrepRoute.Second => PrepText("second_objective", "击败全部来敌。开战后无法撤退。"),
                    BasePrepRoute.Hideout => DungeonEnergy(state, battleDraft.Route),
                    _ => PrepText("attack_objective", "使用已抵达的队伍攻占黑鸦寨。")
                };
                RefreshPrepSupply(state);
            }
            var (ally, enemy) = battleDraft.Power(state, false);
            Ui<Text>(panel, "Ally/Value").text = (travel ? "我方出征" : "我方战力 " + Number(ally)) + "\n" + PrepSummary();
            double maxPower = Math.Max(1, Math.Max(ally, enemy ?? 0));
            PrepMeter(panel, "Ally/Meter/Fill", ally / maxPower);
            Ui<RectTransform>(panel, "Enemy/Meter").gameObject.SetActive(enemy.HasValue && !travel);
            Ui<Text>(panel, "Enemy/Value").text = travel ? PrepText("enemy_arrival", "敌方 · 抵达战场后查看") : enemy.HasValue
                ? "敌方参考战力 " + Number(enemy.Value) + "\n" + (battleDraft.Route == BasePrepRoute.Hideout
                    ? BaseBanditHideout.CountText(battleDraft.KnownEnemyCount() ?? 0, battleDraft.KnownEnemyCount(SoldierKind.Melee),
                        battleDraft.KnownEnemyCount(SoldierKind.Ranged), battleDraft.KnownEnemyCount(SoldierKind.Cavalry))
                    : PrepCounts(battleDraft.KnownEnemyCount))
                : PrepText("enemy_unknown", "敌方 · 暂无已知情报");
            if (enemy.HasValue) PrepMeter(panel, "Enemy/Meter/Fill", enemy.Value / maxPower);
            Ui<Text>(panel, "Context").text = context;
            Ui<Button>(panel, "Continue").interactable = problem == "";
            Ui<Button>(panel, "Sweep").interactable = problem == "";
            int warning = SelectedPrepSoldiers.Count(unit => PrepLowHealth(state, unit));
            var feedback = Ui<Text>(panel, "Feedback");
            feedback.text = problem != "" ? problem : !string.IsNullOrEmpty(prepFeedback) ? prepFeedback : warning > 0 ? $"生命预警 · {warning} 名所选士兵低于 {BaseDevelopment.WarningThreshold(state):P0}" : "";
            if (feedback.text != "") feedback.color = PrepInvalid;
            RefreshQuestCue();
        }

        private static string DungeonEnergy(BaseState state, BasePrepRoute route)
        {
            string energy = string.Format(PrepText("dungeon_energy", "体力 {0} / {1}"),
                BaseRuinsRewards.Energy(state), BaseRuinsRewards.Maximum(state));
            string reward = BaseRuinsRewards.Cleared(state, route)
                ? string.Format(PrepText("dungeon_repeat_reward", "复打领奖消耗 {0}"), BaseRuinsRewards.Cost(state))
                : PrepText("dungeon_first_reward", "首通领奖免费");
            return energy + " · " + reward;
        }

        private void ContinueBattlePreparation()
        {
            RememberPreparationScroll();
            string problem = battleDraft.Problem(PreparationState(battleDraft.Route), false);
            if (problem != "") { prepFeedback = problem; RefreshPreparationFields(); return; }
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
            try
            {
                owner.BeginDeploymentWorld(battleDraft, ShowBattlePreparation, ExitBattleDeployment);
                HideForBattleDeployment();
            }
            catch (InvalidOperationException error)
            {
                prepFeedback = error.Message;
                ShowBattlePreparation();
            }
        }

        internal void HideForBattleDeployment()
        {
            deploymentHudVisibility ??= new[] { summary.transform.parent, mapLabels, controls, inspection, trackedQuest?.transform.parent }
                .Where(node => node != null).Select(node => (node.gameObject, node.gameObject.activeSelf)).ToArray();
            foreach (var item in deploymentHudVisibility) item.Node.SetActive(false);
            ClearPanelSelection();
            modal.gameObject.SetActive(false);
            dialogueContinue = null;
            SetVisible(false);
        }

        private void ShowExplorationHudAfterDeployment()
        {
            if (deploymentHudVisibility == null) return;
            foreach (var item in deploymentHudVisibility) if (item.Node != null) item.Node.SetActive(item.Visible);
            deploymentHudVisibility = null;
        }

        private void ExitBattleDeployment()
        {
            SetVisible(true);
            ClosePanel();
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
            BeginPanel(BaseRuinsRewards.DungeonName(battleDraft.Route) + " · 扫荡确认", SweepView);
            string ratio = preview.EnemyPower > 0 ? (preview.AllyPower / preview.EnemyPower).ToString("0.00") : "—";
            Line($"我方战力 {preview.AllyPower:0.00} / 敌方 {preview.EnemyPower:0.00} ≈ {ratio} 倍 · 需严格超过 1.5 倍", 64, 21);
            Line($"体力 {BaseRuinsRewards.Energy(state)} / {BaseRuinsRewards.Maximum(state)} · 领奖消耗 {BaseRuinsRewards.Cost(state)}", 52, 18);
            int required = battleDraft.RequiredSupply(false);
            string grade = battleDraft.SupplyGrade == 1 ? PrepText("grade_1", "一级") : PrepText("grade_2", "二级");
            Line(required == 0 ? PrepText("supply_none", "仅队长出战 · 所需军需 0") :
                PrepText("supply_required", "本次所需军需") + $" {required} · {grade}", 42, 18);
            if (preview.Problem != "") Line(preview.Problem, 66, 18);
            RowButton("返回备战", -205, 390, ShowBattlePreparation);
            RowButton("确认扫荡", 205, 390, () => owner.SweepPreparedRuins(battleDraft), preview.Problem == "");
            row += 60;
            EndPanel();
        }
    }
}

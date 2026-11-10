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
            _ => PrepText("travel", "出征编队")
        };

        private void PrepButton(Transform view, string path, string label, Action click, bool enabled = true)
        {
            var button = Ui<Button>(view, path);
            if (label != null) Ui<Text>(button.transform, "Label").text = label;
            button.onClick.AddListener(() => { if (!owner.Busy && !PreparationHelpOpen) click(); });
            button.interactable = enabled;
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
            if (travel) PrepButton(view, "Priority", prepProduction ? "生产优先" : "作战优先", () =>
            { prepProduction = !prepProduction; RerenderPreparation(); });
            bool ruins = battleDraft.Route == BasePrepRoute.Ruins;
            Ui<Button>(view, "Sweep").gameObject.SetActive(ruins && state.Demo.RuinsDay > 0);
            Ui<Button>(view, "Reward").gameObject.SetActive(ruins && !string.IsNullOrEmpty(state.Demo.RuinsRewards?.PendingBattleId));
            PrepButton(view, "Sweep", null, ShowPreparedSweep);
            PrepButton(view, "Reward", null, () => { ClearBattlePreparation(); OpenRuinsReward(); });
            var supply = view.Find("Supply");
            supply.gameObject.SetActive(!travel);
            if (!travel)
            {
                PrepButton(supply, "Grade1", null, () => ChoosePrepSupply(1));
                PrepButton(supply, "Grade2", null, () => ChoosePrepSupply(2));
            }

            var cards = Ui<RectTransform>(view, "Teams");
            var activeTeam = PrepLeaders.FirstOrDefault(unit => unit.Id == prepTeam && unit.Selected) ?? PrepLeaders.FirstOrDefault(unit => unit.Selected);
            prepTeam = activeTeam?.Id;
            int slot = 0;
            foreach (var team in PrepLeaders)
            {
                var card = CloneSceneUi("BattlePreparationTeam", cards);
                BindInventoryPortrait(Ui<RectTransform>(card, "Portrait"), team.Leader);
                string marker = team.Selected ? (char)('A' + slot++) + " · " : "○ ";
                PrepButton(card, "Select", marker + team.Name, () => TogglePrepLeader(team), team.Id != battleDraft.PlayerId);
                Ui<Text>(card, "Health").text = $"生命 {Number(team.Health)} / {Number(team.MaxHealth)}";
                PrepMeter(card, "HealthMeter/Fill", team.MaxHealth > 0 ? team.Health / team.MaxHealth : 0);
                PrepButton(card, "Assign", prepTeam == team.Id ? PrepText("assigning", "正在分配") : PrepText("assign", "分配士兵"), () => SetPrepFilter(team.Id, prepKind), team.Selected);
                foreach (var kind in BattleSimulation.TroopKinds)
                    BindPrepCount(card, team, kind);
            }

            var candidates = view.Find("Candidates");
            PrepButton(candidates, "All", prepKind == null ? "● 全部" : null, () => SetPrepFilter(prepTeam, null));
            foreach (var kind in BattleSimulation.TroopKinds)
            {
                string path = BaseCampaign.ToBase(kind).ToString();
                PrepButton(candidates, path, prepKind == kind ? "● " + BattleSimulation.KindName(kind) : null, () => SetPrepFilter(prepTeam, kind));
            }
            BindPrepSort(candidates, PrepSort.Role, PrepText("sort_role", "兵种"));
            BindPrepSort(candidates, PrepSort.Health, PrepText("sort_health", "生命"));
            BindPrepSort(candidates, PrepSort.Status, PrepText("sort_status", "状态"));
            Ui<Text>(candidates, "Title").text = PrepText("roster", "候选名册") + (activeTeam == null ? "" : " · " + activeTeam.Name) +
                $" · {PrepText("selected_count", "已选")} {SelectedPrepSoldiers.Count()}/{BaseMuster.BattleSoldierLimit}";
            ShowPrepRoster(candidates, state);
            RefreshPreparationFields();
            Canvas.ForceUpdateCanvases();
            prepRoster.content.anchoredPosition = new Vector2(0, Mathf.Clamp(prepRosterScroll, 0, Mathf.Max(0, prepRoster.content.rect.height - prepRoster.viewport.rect.height)));
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

        private void RerenderPreparation() { RememberPreparationScroll(); ShowBattlePreparation(); }

        private void SetPrepFilter(string team, SoldierKind? kind)
        {
            prepTeam = team;
            prepKind = kind;
            prepDetail = "";
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
                if (prepTeam == leader.Id) prepTeam = battleDraft.PlayerId;
            }
            battleDraft.Prune();
            prepFeedback = "";
            RerenderPreparation();
        }

        private void BindPrepCount(Transform card, BasePrepUnit team, SoldierKind kind)
        {
            string key = BaseCampaign.ToBase(kind).ToString();
            int count = SelectedPrepSoldiers.Count(unit => unit.TeamId == team.Id && unit.Kind == kind);
            var field = Ui<InputField>(card, key + "/Input");
            field.SetTextWithoutNotify(count.ToString(CultureInfo.InvariantCulture));
            field.interactable = team.Selected;
            field.onEndEdit.AddListener(value =>
            {
                if (!owner.Busy && !PreparationHelpOpen && int.TryParse(value, out int requested) && SetPrepTroopCount(team, kind, requested)) return;
                field.SetTextWithoutNotify(count.ToString(CultureInfo.InvariantCulture));
            });
            PrepButton(card, key + "/Minus", null, () => SetPrepTroopCount(team, kind, count - 1), count > 0);
            PrepButton(card, key + "/Plus", null, () => SetPrepTroopCount(team, kind, count + 1),
                team.Selected && SelectedPrepSoldiers.Count() < BaseMuster.BattleSoldierLimit && PrepSoldiers.Any(unit => unit.Kind == kind && !unit.Selected));
        }

        private bool SetPrepTroopCount(BasePrepUnit team, SoldierKind kind, int requested)
        {
            if (battleDraft == null || !team.Selected || !battleDraft.Units.Contains(team)) return false;
            var selected = SelectedPrepSoldiers.Where(unit => unit.TeamId == team.Id && unit.Kind == kind).ToArray();
            var available = PrepSoldiers.Where(unit => !unit.Selected && unit.Kind == kind).ToArray();
            int room = BaseMuster.BattleSoldierLimit - SelectedPrepSoldiers.Count();
            int count = Mathf.Clamp(requested, 0, selected.Length + Math.Min(available.Length, Math.Max(0, room)));
            if (count == selected.Length) return false;
            foreach (var soldier in selected.Skip(count)) soldier.Selected = false;
            foreach (var soldier in available.Take(count - selected.Length))
            {
                soldier.TeamId = team.Id;
                soldier.Selected = true;
            }
            prepTeam = team.Id;
            prepFeedback = "";
            battleDraft.Prune();
            RerenderPreparation();
            return true;
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

        private void BindPrepSort(Transform view, PrepSort sort, string label)
        {
            if (prepSort == sort) label += prepDescending ? " ↓" : " ↑";
            PrepButton(view, "Sort" + sort, label, () =>
            {
                prepDescending = prepSort == sort ? !prepDescending : sort != PrepSort.Role;
                prepSort = sort;
                prepRosterScroll = 0;
                prepDetail = "";
                ShowBattlePreparation();
            });
        }

        private void ShowPrepRoster(Transform view, BaseState state)
        {
            var candidates = PrepSoldiers.Where(unit => !prepKind.HasValue || unit.Kind == prepKind.Value).ToArray();
            var roster = BasePersonnel.Units(state).Where(unit => !unit.IsLeader).ToDictionary(unit => unit.Key);
            var detail = view.Find("Detail");
            bool showingDetail = !string.IsNullOrEmpty(prepDetail) && roster.TryGetValue(prepDetail, out _);
            detail.gameObject.SetActive(showingDetail);
            prepRoster.gameObject.SetActive(!showingDetail);
            if (showingDetail)
            {
                var unit = roster[prepDetail];
                Ui<Text>(detail, "Title").text = unit.Name;
                Ui<Text>(detail, "Body").text = $"{BattleSimulation.KindName(unit.Kind)}  ·  生命 {Number(unit.Health)} / {Number(unit.MaxHealth)}\n" +
                    $"当前攻击 {Number(unit.Current.Damage)}  ·  当前防御 {Number(unit.Current.Defense)}\n" +
                    $"基础攻击 {Number(unit.Basic.Damage)}  ·  基础防御 {Number(unit.Basic.Defense)}\n\n" +
                    $"{unit.Facility}  ·  {unit.State}\n{unit.Effects.Replace("／", "\n")}";
                PrepButton(detail, "Back", null, () => { prepDetail = ""; ShowBattlePreparation(); });
                return;
            }

            Ui<Text>(view, "Empty").gameObject.SetActive(candidates.Length == 0);
            IOrderedEnumerable<BasePrepUnit> sorted = prepSort switch
            {
                PrepSort.Health => candidates.OrderBy(unit => unit.MaxHealth <= 0 ? 0 : unit.Health / unit.MaxHealth),
                PrepSort.Status => candidates.OrderBy(unit => unit.Selected).ThenBy(unit => roster[unit.Id].State, StringComparer.CurrentCulture),
                _ => candidates.OrderBy(unit => unit.Kind)
            };
            foreach (var soldier in prepDescending ? sorted.Reverse() : sorted)
            {
                var unit = roster[soldier.Id];
                var card = CloneSceneUi("BattlePreparationRosterRow", prepRoster.content);
                string assigned = soldier.Selected ? PrepLeaders.FirstOrDefault(team => team.Id == soldier.TeamId)?.Name ?? "" : PrepText("unselected", "待选");
                string label = $"{unit.Name} · {Number(unit.Health)} / {Number(unit.MaxHealth)}\n" +
                    $"{(soldier.Selected ? "● " : "○ ")}{assigned} · {unit.Facility} · {unit.State}";
                PrepButton(card, "", label, () => SelectPrepSoldier(soldier), prepTeam != null);
                PrepButton(card, "Info", null, () => { RememberPreparationScroll(); prepDetail = soldier.Id; ShowBattlePreparation(); });
                if (soldier.Selected)
                {
                    var background = Ui<Image>(card, "");
                    background.color = Color.Lerp(background.color, DemoUiTheme.Current.Gold, .38f);
                }
                if (soldier.Selected && PrepLowHealth(state, soldier)) Ui<Text>(card, "Label").color = PrepInvalid;
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
                    BasePrepRoute.Ruins => $"侵蚀 {BaseRuinsRewards.Exposure(state):P0} / 秒 · 体力 {BaseRuinsRewards.Energy(state)} / {BaseRuinsRewards.Maximum(state)}" +
                        (state.Demo.RuinsDay == 0 ? " · 首通领奖免费" : " · 复打领奖消耗 " + BaseRuinsRewards.Cost(state)),
                    BasePrepRoute.Defense => PrepText("first_objective", "坚守到敌军撤离，或击败全部来敌。"),
                    BasePrepRoute.Second => PrepText("second_objective", "击败全部来敌。开战后无法撤退。"),
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
                ? "敌方参考战力 " + Number(enemy.Value) + "\n" + PrepCounts(battleDraft.KnownEnemyCount)
                : PrepText("enemy_unknown", "敌方 · 暂无已知情报");
            if (enemy.HasValue) PrepMeter(panel, "Enemy/Meter/Fill", enemy.Value / maxPower);
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
            BeginPanel(BaseScriptNames.Ruins + " · 扫荡确认", SweepView);
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

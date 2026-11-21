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
        private BaseBattlePreparation battleDraft;
        private string prepTeam, prepFeedback, prepDetail;
        private SoldierRosterFilter prepFilter;
        private Button[] prepToolbar;
        private float prepRosterScroll;
        private bool prepProduction;
        private ScrollRect prepRoster;
        private readonly Dictionary<string, (RectTransform Card, Color PortraitColor)> prepCards = new();
        private readonly Dictionary<string, (RectTransform Card, Color Background)> prepRows = new();
        private Dictionary<string, BasePersonnelUnit> prepRosterUnits;
        private (GameObject Node, bool Visible)[] deploymentHudVisibility;
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
            bool wasDeploying = owner.DeploymentWorldActive;
            owner.EndDeploymentWorld();
            ShowExplorationHudAfterDeployment();
            if (wasDeploying) SetVisible(true);
            battleDraft = null;
            prepTeam = prepFeedback = prepDetail = "";
            prepFilter = new SoldierRosterFilter();
            prepRosterScroll = 0;
            prepRoster = null;
            prepCards.Clear();
            prepRows.Clear();
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
            button.onClick.AddListener(() => { if (!owner.Busy) click(); });
            button.interactable = true;
        }

        private IEnumerable<BasePrepUnit> PrepLeaders => battleDraft.Units.Where(unit => unit.Kind == SoldierKind.Hero);
        private IEnumerable<BasePrepUnit> PrepSoldiers => battleDraft.Units.Where(unit => unit.Kind != SoldierKind.Hero);
        private IEnumerable<BasePrepUnit> SelectedPrepSoldiers => PrepSoldiers.Where(unit => unit.Selected);
        private static string Number(double value) => value.ToString("0");
        private string PrepSummary()
        {
            var units = battleDraft.Units.Where(unit => unit.Selected).ToArray();
            return PrepCounts(kind => units.Count(unit => unit.Kind == kind));
        }

        private static string PrepCounts(Func<SoldierKind, int> count) =>
            $"队长 {count(SoldierKind.Hero)} · 剑士 {count(SoldierKind.Melee)} · 弓兵 {count(SoldierKind.Ranged)} · 骑兵 {count(SoldierKind.Cavalry)} · 法师 {count(SoldierKind.Mage)}";

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
            var troopKinds = BattleSimulation.TroopKinds.Select(BaseCampaign.ToBase).ToArray();
            LayoutPreparation(view);
            activeObject = "battle-preparation";
            interactionObject = "";
            prepRoster = Ui<ScrollRect>(view, "Candidates/List");
            Ui<Text>(view, "Title").text = travel ? owner.WorldMarchTitle : PrepTitle(battleDraft.Route) + " · " + PrepText("preparation", "战前准备");
            PrepButton(view, "Back", PrepText("back", "返回"), ClosePanel);
            PrepButton(view, "Close", null, ClosePanel);
            PrepButton(view, "Reset", null, () => OpenBattlePreparation(battleDraft.Route));
            PrepButton(view, "Continue", travel ? "出发" : PrepText("deployment_enter", "进入战场"), ContinueBattlePreparation);
            var priority = Ui<Button>(view, "Priority");
            priority.gameObject.SetActive(travel && !battleDraft.TravelingParty);
            if (travel) PrepButton(view, "Priority", null, () =>
            {
                prepProduction = !prepProduction;
                RefreshPreparationFields();
            });
            var route = battleDraft.Route;
            bool dungeon = BaseRuinsRewards.IsDungeon(route);
            Ui<Button>(view, "Sweep").gameObject.SetActive(dungeon && BaseRuinsRewards.Cleared(state, route));
            Ui<Button>(view, "Reward").gameObject.SetActive(dungeon && BaseRuinsRewards.PendingBattle(state, route) != "");
            PrepButton(view, "Sweep", "扫荡预览", ShowPreparedSweep);
            PrepButton(view, "Reward", null, () => { ClearBattlePreparation(); OpenRuinsReward(route); });
            var supply = view.Find("Supply");
            PrepButton(supply, "Grade1", null, () => ChoosePrepSupply(battleDraft.SupplyGrade == 1 ? 2 : 1));
            var cards = Ui<RectTransform>(view, "Teams");
            var activeTeam = PrepLeaders.FirstOrDefault(unit => unit.Id == prepTeam) ?? PrepLeaders.FirstOrDefault(unit => unit.Selected);
            prepTeam = activeTeam?.Id;
            prepCards.Clear();
            foreach (var team in PrepLeaders)
            {
                var card = CloneSceneUi("BattlePreparationTeam", cards);
                LayoutTeamCard(card);
                var portrait = Ui<RectTransform>(card, "Portrait");
                var figure = new GameObject("队长半身像", typeof(RectTransform), typeof(CanvasRenderer));
                var figureRect = (RectTransform)figure.transform;
                figureRect.SetParent(portrait, false);
                figureRect.anchorMin = Vector2.zero;
                figureRect.anchorMax = Vector2.one;
                figureRect.offsetMin = figureRect.offsetMax = Vector2.zero;
                var avatar = figure.AddComponent<BattleAvatarGraphic>();
                avatar.PlayerHero = team.Id == state.PlayerLeaderId;
                avatar.AppearanceId = avatar.PlayerHero ? BaseDemoIntro.PlayerArt(state) : BaseCommanderIdentity.Appearance(team.Leader);
                avatar.raycastTarget = false;
                prepCards.Add(team.Id, (card, portrait.GetComponent<Image>().color));
                var rowSelect = card.gameObject.GetOrAdd<Button>();
                rowSelect.transition = Selectable.Transition.None;
                rowSelect.onClick.AddListener(() => SelectPreparationTeam(team));
                PrepButton(card, "Select", null, () => SelectPreparationTeam(team));
                Ui<Text>(card, "Health").text = $"生命 {Number(team.Health)} / {Number(team.MaxHealth)}";
                PrepMeter(card, "HealthMeter/Fill", team.MaxHealth > 0 ? team.Health / team.MaxHealth : 0);
                foreach (var kind in troopKinds)
                    BindPrepCount(card, team, BaseCampaign.ToBattle(kind));
            }

            for (int slot = prepCards.Count; slot < BaseAdventure.Capacity; slot++)
            {
                var emptyCard = CloneSceneUi("BattlePreparationTeam", cards);
                emptyCard.name = "Vacant" + slot;
                LayoutTeamCard(emptyCard);
                foreach (Transform child in emptyCard) child.gameObject.SetActive(false);
            }
            var candidates = view.Find("Candidates");
            var names = new[] { "All", "Melee", "SortHealth", "SortStatus" };
            prepToolbar = names.Select(key => Ui<Button>(candidates, key)).ToArray();
            for (int i = 0; i < names.Length; i++)
            {
                int index = i;
                PrepButton(candidates, names[i], null, () =>
                {
                    prepFilter.Activate(index);
                    prepDetail = "";
                    RefreshPreparationRoster();
                });
            }
            PrepButton(candidates, "Detail/Back", null, () => { prepDetail = ""; RefreshPreparationRoster(); });
            prepRows.Clear();
            prepRosterUnits = BasePersonnel.Soldiers(state).ToDictionary(unit => unit.Key);
            RefreshPreparationEdits();
            PaintOtherWoodScreen(view);
            Canvas.ForceUpdateCanvases();
            prepRoster.content.anchoredPosition = new Vector2(0, Mathf.Clamp(prepRosterScroll, 0, Mathf.Max(0, prepRoster.content.rect.height - prepRoster.viewport.rect.height)));
        }

        private static void FitControl(Transform parent, string key, float x, float y, float w, float h)
        {
            var rect = (RectTransform)parent.Find(key);
            rect.anchoredPosition = new Vector2(x,-y);
            rect.sizeDelta = new Vector2(w,h);
        }
        private static void EnsureFourthColumn(Transform parent, string name, string source)
        {
            if (parent.Find(name) != null) return;
            var copy = UnityEngine.Object.Instantiate(parent.Find(source), parent, false);
            copy.name = name;
        }
        private static void LayoutTeamCard(Transform card)
        {
            EnsureFourthColumn(card, "Mage", "Ranged");
            foreach (var key in new[] { "Select", "Health", "HealthMeter" })
                ((RectTransform)card.Find(key)).SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, 142);
            int column = 0;
            foreach (var kind in BattleSimulation.TroopKinds)
            {
                string key = BaseCampaign.ToBase(kind).ToString();
                FitControl(card,key,221+column++*113,4,107,65);
                var group = card.Find(key);
                group.Find("Minus").gameObject.SetActive(false);
                group.Find("Plus").gameObject.SetActive(false);
                FitControl(group,"Input",3,10,98,48);
            }
        }
        private static void LayoutPreparation(Transform view)
        {
            EnsureFourthColumn(view,"MageHeading","RangedHeading");
            view.Find("MageHeading").GetComponent<Text>().text = "法师";
            FitControl(view,"TeamHeading",20,177,209,27);
            int column=0;
            foreach(var kind in BattleSimulation.TroopKinds)
                FitControl(view,BaseCampaign.ToBase(kind)+"Heading",235+column++*113,177,107,27);
            FitControl(view,"Ally",14,57,315,87);
            FitControl(view,"Versus",330,64,42,57);
            FitControl(view,"Enemy",372,57,322,87);
            foreach (var side in new[] {"Ally","Enemy"})
            {
                var box = view.Find(side);
                FitControl(box,"Value",9,3,300,57);
                FitControl(box,"Meter",9,66,297,10);
                var label = box.Find("Value").GetComponent<Text>();
                label.resizeTextMaxSize=label.fontSize; label.resizeTextForBestFit=true; label.resizeTextMinSize=13;
            }
            FitControl(view,"Context",18,143,676,31);
            FitControl(view,"Candidates",706,57,450,563);
            var roster = view.Find("Candidates");
            foreach (var key in new[] {"SortHeading","SortRole","Ranged","Cavalry"}) roster.Find(key).gameObject.SetActive(false);
            int slot=0;
            foreach (var key in new[] {"All","Melee","SortHealth","SortStatus"})
                FitControl(roster,key,8+slot++*109,34,104,43);
            foreach (var key in new[] {"List","Detail"}) FitControl(roster,key,8,83,434,472);
            FitControl(roster,"List/Viewport",0,0,434,472);
            FitControl(roster,"Empty",12,89,422,42);
            FitControl(view,"Feedback",14,510,676,43);
            FitControl(view,"Supply",14,565,237,56);
            var supply = view.Find("Supply");
            foreach (var key in new[] {"Heading","Demand","Stock1","Stock2","Grade2"}) supply.Find(key).gameObject.SetActive(false);
            FitControl(supply,"Grade1",0,0,237,56);
            FitControl(view,"Reset",258,565,170,56);
            FitControl(view,"Continue",435,565,259,56);
            FitControl(view,"Sweep",14,458,190,43);
            FitControl(view,"Priority",14,458,190,43);
            FitControl(view,"Reward",211,458,190,43);
        }

        private void RememberPreparationScroll()
        {
            if (prepRoster != null) prepRosterScroll = Mathf.Max(0, prepRoster.content.anchoredPosition.y);
        }

        private void SelectPreparationTeam(BasePrepUnit team)
        {
            if (owner.Busy || prepTeam == team.Id) return;
            prepTeam = team.Id;
            prepDetail = "";
            RefreshPreparationTeams();
            RefreshPreparationRoster();
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
                if (!owner.Busy && int.TryParse(value, out int requested))
                    SetPrepTroopCount(team, kind, requested);
                field.SetTextWithoutNotify(PrepTroopCount(team, kind).ToString(CultureInfo.InvariantCulture));
            });
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
            int slot = 0;
            foreach (var team in PrepLeaders)
            {
                var (card, portraitColor) = prepCards[team.Id];
                Ui<Image>(card, "Portrait").color = prepTeam == team.Id ? DemoUiTheme.Current.Gold : portraitColor;
                string marker = team.Selected ? (char)('A' + slot++) + " · " : "○ ";
                Ui<Text>(card, "Select/Label").text = marker + team.Name;
                foreach (var baseKind in BattleSimulation.TroopKinds.Select(BaseCampaign.ToBase))
                {
                    var kind = BaseCampaign.ToBattle(baseKind);
                    var group = card.Find(baseKind.ToString());
                    int count = selected.Count(unit => unit.TeamId == team.Id && unit.Kind == kind);
                    var field = Ui<InputField>(group, "Input");
                    if (!field.isFocused) field.SetTextWithoutNotify(count.ToString(CultureInfo.InvariantCulture));
                    field.interactable = team.Selected;
                }
            }
        }

        private bool SetPrepTroopCount(BasePrepUnit team, SoldierKind kind, int requested)
        {
            if (battleDraft == null || !battleDraft.SetTeamCount(team.Id,kind,requested)) return false;
            prepTeam = team.Id;
            prepFeedback = "";
            battleDraft.Prune();
            RefreshPreparationEdits();
            return true;
        }

        private void SelectPrepSoldier(BasePrepUnit soldier)
        {
            if (!battleDraft.ToggleSoldier(prepTeam,soldier))
            {
                prepFeedback = PrepText("limit", "请选择出战队长并检查编队余量");
                RefreshPreparationFields();
                return;
            }
            prepFeedback = "";
            battleDraft.Prune();
            RefreshPreparationEdits();
        }

        private static bool PrepLowHealth(BaseState state, BasePrepUnit soldier) => !BaseDevelopment.WarningOff(state) && soldier.MaxHealth > 0 && soldier.Health / soldier.MaxHealth < BaseDevelopment.WarningThreshold(state);

        private void RefreshPreparationRoster()
        {
            var view = panel.Find("Candidates");
            var activeTeam = PrepLeaders.FirstOrDefault(unit => unit.Id == prepTeam);
            Ui<Text>(view, "Title").text = PrepText("roster", "士兵名册") + (activeTeam == null ? "" : " · " + activeTeam.Name) +
                $" · {PrepText("selected_count", "已选")} {SelectedPrepSoldiers.Count()}/{BaseMuster.BattleSoldierLimit}";
            PaintRosterToolbar(prepToolbar,prepFilter);
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

            var eligible = PrepSoldiers.ToDictionary(u => u.Id);
            var candidates = prepFilter.Apply(prepRosterUnits.Values,u => eligible.ContainsKey(u.Key)).ToArray();
            empty.SetActive(candidates.Length == 0);
            var visibleIds = candidates.Select(unit => unit.Key).ToHashSet();
            foreach (var entry in prepRows)
                if (!visibleIds.Contains(entry.Key)) entry.Value.Card.gameObject.SetActive(false);
            int index = 0;
            var teamLabels = PrepLeaders.Where(team => team.Selected).Select((team, slot) => (team.Id, label: (char)('A' + slot) + "队"))
                .ToDictionary(team => team.Id, team => team.label);
            foreach (var entry in candidates)
            {
                bool available = eligible.TryGetValue(entry.Key,out var soldier);
                if (!prepRows.TryGetValue(entry.Key,out var row))
                {
                    var card = AddPersonnelRow(prepRoster.content, entry, "", () =>
                    {
                        if (available) SelectPrepSoldier(soldier);
                    }, details: () =>
                    {
                        prepDetail = entry.Key;
                        RefreshPreparationRoster();
                    });
                    row = (card, Ui<Image>(card, "").color);
                    prepRows.Add(entry.Key, row);
                }
                if (row.Card.GetSiblingIndex() != index) row.Card.SetSiblingIndex(index);
                index++;
                row.Card.gameObject.SetActive(true);
                Ui<Button>(row.Card, "").interactable = available && activeTeam?.Selected == true;
                TintRoster(row.Card,available,false,soldier?.Selected == true);
                string status = BasePersonnel.CompactPost(entry);
                if (soldier?.Selected == true && teamLabels.TryGetValue(soldier.TeamId,out var assigned)) status = assigned+" · "+status;
                UpdatePersonnelRow(row.Card,entry,status,showFacility:false);
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
            int grade = battleDraft.SupplyGrade;
            int required = battleDraft.Route == BasePrepRoute.Travel ? 0 : battleDraft.RequiredSupply(false);
            var button = Ui<Button>(panel,"Supply/Grade1");
            button.GetComponentInChildren<Text>().text = $"{grade}级军需 · 库存 {battleDraft.AvailableSupply(state,grade)} / 需求 {required}";
            button.targetGraphic.color = DemoUiTheme.Current.Button;
        }

        private void RefreshPreparationFields()
        {
            var state = PreparationState(battleDraft.Route);
            bool travel = battleDraft.Route == BasePrepRoute.Travel;
            var problem = battleDraft.Problem(state, false);
            var context = "";
            if (travel) Ui<Text>(panel, "Priority/Label").text = prepProduction ? "生产优先" : "作战优先";
            if (battleDraft.TravelingParty) context = "沿用随行队伍，可将士兵调给另一名队长。";
            else if (travel && problem == "")
            {
                try
                {
                    var plan = battleDraft.TravelPlan(state, prepProduction);
                    context = $"预计日产变化   粮食 {(-plan.FoodLoss):+0;-0;0}  ·  军需 {(-plan.ProductLoss):+0;-0;0}";
                    if (prepProduction && plan.Production?.Allowed != true) problem = plan.Production?.Problem ?? PrepText("production", "当前编队无法保障生产");
                }
                catch (ArgumentException error) { problem = error.Message; }
                catch (InvalidOperationException error) { problem = error.Message; }
            }
            else if (!travel)
            {
                context = battleDraft.Route switch
                {
                    BasePrepRoute.Ruins => $"侵蚀基准 {BaseRuinsRewards.Exposure(state):P0} / 秒 · " + DungeonEnergy(state, battleDraft.Route),
                    BasePrepRoute.Defense => PrepText("first_objective", "坚守到敌军撤离，或击败全部来敌。"),
                    BasePrepRoute.Second => PrepText("second_objective", "击败全部来敌。开战后无法撤退。"),
                    BasePrepRoute.Hideout => DungeonEnergy(state, battleDraft.Route),
                    _ => PrepText("attack_objective", "使用已抵达的队伍攻占黑鸦寨。")
                };
            }
            RefreshPrepSupply(state);
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
                : route==BasePrepRoute.Ruins ? "救出受困村民" : PrepText("dungeon_first_reward", "首通领奖免费");
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
                if (!owner.DepartWorldMarch(battleDraft, prepProduction))
                { prepFeedback = owner.Message; RefreshPreparationFields(); }
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
            BeginPanel(BaseRuinsRewards.DungeonName(battleDraft.Route) + " · 扫荡预览", SweepView);
            string ratio = preview.EnemyPower > 0 ? (preview.AllyPower / preview.EnemyPower).ToString("P0") : "—";
            Line($"我方战力 {preview.AllyPower:0} / 敌方 {preview.EnemyPower:0} · 战力比 {ratio} / 需超过 150%", 64, 21);
            Line($"体力 {BaseRuinsRewards.Energy(state)} / {BaseRuinsRewards.Maximum(state)} · 领奖消耗 {BaseRuinsRewards.Cost(state)}", 52, 18);
            int required = battleDraft.RequiredSupply(false);
            string grade = battleDraft.SupplyGrade == 1 ? PrepText("grade_1", "一级") : PrepText("grade_2", "二级");
            Line(required == 0 ? PrepText("supply_none", "仅队长出战 · 所需军需 0") :
                PrepText("supply_required", "本次所需军需") + $" {required} · {grade}", 42, 18);
            if (preview.Problem != "") Line(preview.Problem, 66, 18);
            RowButton("返回备战", -205, 390, ShowBattlePreparation);
            RowButton("开始扫荡", 205, 390, () => owner.SweepPreparedRuins(battleDraft), preview.Problem == "");
            row += 60;
            EndPanel();
        }
    }
}

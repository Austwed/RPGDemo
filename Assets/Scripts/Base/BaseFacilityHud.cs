using System;
using System.Collections.Generic;
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
        private ScrollRect facilityControls;
        private Vector2 facilityScrollOffsets;
        private SoldierKind? facilityRosterKind;
        private string facilityPersonnelDetail;
        private bool UseFacilityPanel => BaseBattleGateway.DemoMode && BaseDemo.Active(owner.State) &&
            owner.SceneUiTemplate("FacilityPanel") != null;
        internal bool ProductionFacilityOpen => PanelOpen &&
            (panelViewKey == "intro_farm" || panelViewKey == "intro_smith" ||
             panelViewKey?.StartsWith("farm:", StringComparison.Ordinal) == true ||
             panelViewKey?.StartsWith("workshop:", StringComparison.Ordinal) == true);

        private (RectTransform actions, RectTransform filters, RectTransform list) OpenFacilityPanel(string key, string title,
            string actionsTitle, string rosterTitle)
        {
            facilityScrollOffsets = panelViewKey == key && PanelOpen && facilityControls != null && panelScroll != null
                ? new Vector2(facilityControls.content.anchoredPosition.y, panelScroll.content.anchoredPosition.y) : Vector2.zero;
            var view = OpenScenePanel("FacilityPanel", key);
            Ui<Text>(view, "Title").text = title;
            Ui<Text>(view, "Left/Title").text = actionsTitle;
            Ui<Text>(view, "Right/Title").text = rosterTitle;
            Ui<Button>(view, "Close").onClick.AddListener(ClosePanel);
            panelFeedback = Ui<Text>(view, "Feedback");
            panelFeedback.gameObject.SetActive(false);
            facilityControls = Ui<ScrollRect>(view, "Left/List");
            panelScroll = Ui<ScrollRect>(view, "Right/List");
            return (facilityControls.content, Ui<RectTransform>(view, "Right/Filters"), panelScroll.content);
        }

        private void FinishFacilityPanel()
        {
            Canvas.ForceUpdateCanvases();
            Restore(facilityControls, facilityScrollOffsets.x);
            Restore(panelScroll, facilityScrollOffsets.y);

            static void Restore(ScrollRect scroll, float offset)
            {
                scroll.content.anchoredPosition = new Vector2(0, Mathf.Clamp(offset, 0,
                    Mathf.Max(0, scroll.content.rect.height - scroll.viewport.rect.height)));
            }
        }

        private Text FacilityText(Transform parent, string text, bool heading = false)
        {
            var label = CloneSceneUi(heading ? "FacilityHeading" : "FacilityText", parent).GetComponent<Text>();
            label.text = text;
            return label;
        }

        private static string FacilityCopy(string key, string fallback) => StoryText.Get("story.ui.facility." + key, fallback);

        private Button FacilityButton(Transform parent, string name, string title, Action action,
            bool available = true, bool selected = false)
        {
            var view = CloneSceneUi("FacilityAction", parent);
            view.name = name;
            var button = view.GetComponent<Button>();
            button.GetComponentInChildren<Text>().text = title;
            button.interactable = available;
            button.onClick.AddListener(() => { if (!owner.Busy) action(); });
            if (selected)
            {
                var image = view.GetComponent<Image>();
                image.color = DemoUiTheme.Current.Gold;
            }
            return button;
        }

        private void FacilityChange(Action<BaseState> change, string success, Action refresh)
        {
            if (owner.Change(change, success)) refresh();
            else panelFeedback?.gameObject.SetActive(true);
        }

        private void EnsureWorkerDraft()
        {
            string stamp = BaseMuster.Snapshot(owner.State);
            if (workerDraft != null && (workerMoves.Count > 0 || workerDraftStamp == stamp)) return;
            workerDraft = owner.State.Copy();
            workerDraftStamp = stamp;
            LockCurrentPosts(workerDraft);
            workerMoves.Clear();
        }

        private void ResetWorkerDraft()
        {
            workerDraft = null;
            workerMoves.Clear();
        }

        private void CommitWorkerDraft(Action refresh)
        {
            var moves = workerMoves.ToArray();
            bool saved = owner.Change(next =>
            {
                if (BaseMuster.Snapshot(next) != workerDraftStamp) throw new InvalidOperationException("人员已变化，请重新安排");
                LockCurrentPosts(next);
                foreach (var move in moves)
                {
                    if (!BaseEconomy.MoveWorkers(next, move.kind, move.from, move.to, 1, move.bucket))
                        throw new InvalidOperationException("方案已失效，请重新安排");
                    if (move.to == "farm" && BaseDemoFlow.Active(next) && !next.Demo.Flow.FarmOfferShown)
                    {
                        next.Demo.Flow.FarmOfferPending = true;
                        next.Demo.Flow.FarmX = next.PlayerX;
                        next.Demo.Flow.FarmY = next.PlayerY;
                    }
                }
            }, "已确认工人安排");
            if (saved)
                foreach (var move in moves) owner.EnqueueWorkerTransfer(move.kind, move.from, move.to, 1);
            ResetWorkerDraft();
            refresh();
            if (panelFeedback != null)
            {
                panelFeedback.text = owner.Message;
                panelFeedback.gameObject.SetActive(true);
            }
        }

        private sealed class ProductionFacilityView
        {
            public string FacilityId;
            public RectTransform Panel, Actions, List;
            public BaseState State;
            public bool HidePostLock, ShowingManagers;
            public Image WorkersTab, ManagersTab;
            public Color DefaultTabColor;
            public Action<BaseState, BaseDayPreview, int, bool> RefreshActions;
        }

        private ProductionFacilityView productionView;

        private void OpenProductionFacility()
        {
            string id = activeObject;
            bool managers = activePage == "administrators";
            var state = owner.State;
            EnsureWorkerDraft();
            bool pending = workerMoves.Count > 0;
            var draft = workerDraft;
            var facility = state.Muster.Facilities.Single(x => x.FacilityId == id);
            string key = id + ":" + activePage;
            string title = BaseEconomy.FacilityName(id) + " · " + facility.Level + "级";
            string rosterTitle = managers ? FacilityCopy("managers", "负责人名册") : FacilityCopy("workers", "工人名册");
            bool reuse = PanelOpen && productionView != null && productionView.Panel == panel && productionView.FacilityId == id;
            if (!reuse)
            {
                var (actions, filters, list) = OpenFacilityPanel(key, title, FacilityCopy("production", "生产与岗位"), rosterTitle);
                var workersTab = FacilityButton(filters, "Workers", "工人", () => ShowRoster("workers")).GetComponent<Image>();
                var managersTab = FacilityButton(filters, "Managers", "负责人", () => ShowRoster("administrators")).GetComponent<Image>();
                productionView = new ProductionFacilityView
                {
                    FacilityId = id, Panel = panel, Actions = actions, List = list,
                    WorkersTab = workersTab, ManagersTab = managersTab, DefaultTabColor = workersTab.color
                };
            }

            var view = productionView;
            panelViewKey = key;
            panelFeedback.gameObject.SetActive(false);
            Ui<Text>(panel, "Title").text = title;
            Ui<Text>(panel, "Right/Title").text = rosterTitle;
            view.WorkersTab.color = managers ? view.DefaultTabColor : DemoUiTheme.Current.Gold;
            view.ManagersTab.color = managers ? DemoUiTheme.Current.Gold : view.DefaultTabColor;

            bool hidePostLock = BaseDemo.ExperimentalManagementHidden(state);
            bool rebuildActions = view.State != state || view.HidePostLock != hidePostLock;
            float actionOffset = view.Actions.anchoredPosition.y;
            if (rebuildActions)
            {
                ClearPanelChildren(view.Actions);
                view.RefreshActions = BuildProductionActions(view.Actions, id, state, facility, hidePostLock);
                view.State = state;
                view.HidePostLock = hidePostLock;
            }
            int count = BaseEconomy.WorkersAt(draft, id);
            view.RefreshActions(draft, BaseEconomy.ProjectDay(draft), count, pending);

            float rosterOffset = view.ShowingManagers == managers ? view.List.anchoredPosition.y : 0;
            view.ShowingManagers = managers;
            ClearPanelChildren(view.List);
            if (managers) FillFacilityManagers(view.List, state, id, pending);
            else FillFacilityWorkers(view.List, draft, id, count < facility.Capacity);
            if (rebuildActions) RestoreProductionScroll(facilityControls, actionOffset);
            RestoreProductionScroll(panelScroll, rosterOffset);

            void ShowRoster(string page)
            {
                if (activePage == page) return;
                activePage = page;
                OpenFacility();
            }
        }

        private static void RestoreProductionScroll(ScrollRect scroll, float offset)
        {
            LayoutRebuilder.ForceRebuildLayoutImmediate(scroll.content);
            scroll.StopMovement();
            scroll.content.anchoredPosition = new Vector2(0, Mathf.Clamp(offset, 0,
                Mathf.Max(0, scroll.content.rect.height - scroll.viewport.rect.height)));
        }

        private Action<BaseState, BaseDayPreview, int, bool> BuildProductionActions(Transform actions, string id, BaseState state,
            BaseFacilityProtection facility, bool hidePostLock)
        {
            bool workshop = id == "workshop";
            var manager = state.Leaders.FirstOrDefault(x => x.FacilityId == id);
            var card = CloneSceneUi("FacilityManagerCard", actions);
            Ui<Text>(card, "Name").text = manager == null ? "负责人 · 未安排" : "负责人 · " + BasePersonnel.LeaderName(manager);
            Ui<Text>(card, "Effect").text = manager == null ? "从右侧名册安排负责人" : "产能 +" + BaseDevelopment.Bonus(manager, id).ToString("P0");
            if (manager != null) BindInventoryPortrait(Ui<RectTransform>(card, "Portrait"), manager);

            var staff = FacilityText(actions, "");
            var output = FacilityText(actions, "");
            var shortage = workshop ? FacilityText(actions, "原料不足 · 预计产量已按库存计算") : null;

            var commands = CloneSceneUi("FacilityActionRow", actions);
            var apply = FacilityButton(commands, "Apply", "确认方案", () => CommitWorkerDraft(OpenFacility));
            var applyImage = apply.GetComponent<Image>();
            var applyColor = applyImage.color;
            var cancel = FacilityButton(commands, "Cancel", "取消方案", () => { ResetWorkerDraft(); OpenFacility(); });
            var draftNotice = FacilityText(actions, FacilityCopy("draft", "岗位调配待确认"));

            var counters = new List<(BaseTroopKind Kind, Text Label, Button Minus, Button Plus, Button Unlock, bool CanUnlock)>();
            foreach (BaseTroopKind kind in Enum.GetValues(typeof(BaseTroopKind)))
            {
                var counter = FacilityCounter(actions, kind.ToString(), "",
                    () => Adjust(kind, id, "", OpenFacility), () => Adjust(kind, "", id, OpenFacility), false, false);
                var unlock = Ui<Button>(counter, "Unlock");
                bool canUnlock = false;
                if (hidePostLock) unlock.gameObject.SetActive(false);
                else
                {
                    canUnlock = state.Workers.Any(w => w.Kind == kind && w.PostLocked &&
                        (w.FacilityId == id || w.DesiredFacility == id));
                    unlock.onClick.AddListener(() => FacilityChange(next =>
                    {
                        foreach (var worker in next.Workers.Where(w => w.Kind == kind && (w.FacilityId == id || w.DesiredFacility == id)))
                            worker.PostLocked = false;
                    }, "已解锁工人，由策略安排", OpenFacility));
                }
                counters.Add((kind, Ui<Text>(counter, "Label"), Ui<Button>(counter, "Minus"), Ui<Button>(counter, "Plus"), unlock, canUnlock));
            }

            var draftLocks = new List<Button>();
            if (!hidePostLock && manager != null)
            {
                var managerLock = FacilityButton(actions, "ManagerLock", manager.PostLocked ? "解锁负责人岗位" : "锁定负责人岗位", () =>
                    FacilityChange(next =>
                    {
                        var leader = next.Leaders.Single(x => x.Id == manager.Id);
                        leader.PostLocked = !leader.PostLocked;
                        if (leader.PostLocked) leader.DesiredFacility = leader.FacilityId;
                    }, "已更新管理员岗位锁定", OpenFacility));
                if (BaseAdventure.Contains(state, manager.Id)) managerLock.interactable = false;
                else draftLocks.Add(managerLock);
            }
            if (workshop) BuildWorkshopControls(actions, state, facility, draftLocks);
            FacilityButton(actions, "Rules", "设施规则", () => FacilityRules(OpenFacility));

            return (draft, preview, count, pending) =>
            {
                int producing = BaseEconomy.ProductionWorkersAt(draft, id);
                staff.text = $"岗位 {producing}/{facility.Capacity} · 今日生产 {producing} 人";
                output.text = workshop
                    ? $"预计产出 {preview.ProductLevel}级{BaseEconomy.ProductName(draft.WorkshopProduct)} {preview.ProductsMade} 件 · 原料 {preview.MaterialsSpent}"
                    : $"预计产粮 {preview.FoodProduced} · 扣粮后库存 {preview.NextFood}";
                if (workshop) shortage.gameObject.SetActive(preview.ProductsMade < preview.WorkCapacity);
                apply.interactable = cancel.interactable = pending;
                applyImage.color = pending ? DemoUiTheme.Current.Gold : applyColor;
                draftNotice.gameObject.SetActive(pending);
                foreach (var (kind, label, minus, plus, unlock, canUnlock) in counters)
                {
                    int assigned = BaseEconomy.WorkersAt(draft, id, kind);
                    label.text = BaseEconomy.TroopName(kind) + "  " + assigned;
                    minus.interactable = assigned > 0;
                    plus.interactable = count < facility.Capacity && draft.Workers.Any(w => w.Kind == kind &&
                        w.FacilityId == "" && BaseDevelopment.Effective(draft, w));
                    unlock.interactable = !pending && canUnlock;
                }
                foreach (var control in draftLocks) control.interactable = !pending;
            };
        }

        private void BuildWorkshopControls(Transform actions, BaseState state, BaseFacilityProtection facility, List<Button> draftLocks)
        {
            FacilityText(actions, "生产配方", true);
            var grades = CloneSceneUi("FacilityActionRow", actions);
            for (int grade = 1; grade <= facility.MaxProductLevel; grade++)
            {
                int level = grade;
                draftLocks.Add(FacilityButton(grades, "Grade" + level, level == 1 ? "一级军需" : "二级军需", () =>
                    FacilityChange(next =>
                    {
                        if (!BaseConstruction.SelectLevel(next, level)) throw new InvalidOperationException("当前不能改配方");
                    }, "已选择生产等级", OpenFacility), selected: BaseConstruction.ProductLevel(state) == level));
            }
            var recipes = CloneSceneUi("FacilityActionRow", actions);
            foreach (string product in BaseEconomy.ProductsFor(state))
                draftLocks.Add(FacilityButton(recipes, product, BaseEconomy.ProductName(product) + " · 原料" + BaseConstruction.MaterialCost(state, product),
                    () => FacilityChange(next =>
                    {
                        if (!BaseEconomy.SelectProduct(next, product)) throw new InvalidOperationException("当前不能改配方");
                    }, "已选择生产", OpenFacility),
                    selected: state.WorkshopProduct == product));
        }

        private void FillFacilityWorkers(Transform list, BaseState draft, string id, bool room)
        {
            int civilians = BaseEconomy.CivilianWorkersAt(draft, id);
            if (civilians > 0) FacilityText(list, CivilianRosterLabel(civilians), true);
            var units = BasePersonnel.Units(draft).Where(u => !u.IsLeader && !u.Away &&
                (draft.Workers[u.BucketIndex].FacilityId == id || draft.Workers[u.BucketIndex].FacilityId == ""))
                .OrderByDescending(u => draft.Workers[u.BucketIndex].FacilityId == id).ThenBy(u => u.Kind).ToArray();
            if (units.Length == 0) FacilityText(list, "当前没有可调人员。");
            foreach (var unit in units)
            {
                var worker = draft.Workers[unit.BucketIndex];
                bool assigned = worker.FacilityId == id;
                string status = assigned ? RosterText("unassign", "点击调回闲置") : !unit.Effective ? unit.State :
                    room ? RosterText("assign", "点击安排上岗") : RosterText("full", "岗位已满");
                AddPersonnelRow(list, unit, status, () => Adjust(worker.Kind, worker.FacilityId, assigned ? "" : id, OpenFacility, unit.BucketIndex),
                    assigned || unit.Effective && room, assigned, showStats: false);
            }
        }

        private static string CivilianRosterLabel(int count) => $"村民 ×{count} · 补位";

        private void FillFacilityManagers(Transform list, BaseState state, string id, bool pending)
        {
            foreach (var unit in BasePersonnel.Units(state).Where(x => x.IsLeader))
            {
                var leader = state.Leaders.Single(x => x.Id == unit.LeaderId);
                bool assigned = leader.FacilityId == id;
                string target = assigned ? "" : id;
                bool available = !pending && BaseEconomy.CanAssignLeader(state, leader.Id, target);
                string label = available ? assigned ? RosterText("unassign_manager", "点击解除任职") : RosterText("assign_manager", "点击任命为管理员") : unit.State;
                AddPersonnelRow(list, unit, label, () => ConfirmManager(leader.Id, target, OpenFacility), available, assigned, showStats: false);
            }
        }

        private RectTransform FacilityCounter(Transform actions, string name, string title, Action lower, Action raise,
            bool canLower, bool canRaise)
        {
            var counter = CloneSceneUi("FacilityCounter", actions);
            counter.name = name;
            Ui<Text>(counter, "Label").text = title;
            var minus = Ui<Button>(counter, "Minus");
            var plus = Ui<Button>(counter, "Plus");
            minus.interactable = canLower;
            plus.interactable = canRaise;
            minus.onClick.AddListener(() => { if (!owner.Busy) lower(); });
            plus.onClick.AddListener(() => { if (!owner.Busy) raise(); });
            return counter;
        }

        private void OpenBarracksFacility(string[] rangeDraft = null)
        {
            var state = owner.State;
            var (actions, filters, list) = OpenFacilityPanel("barracks:facility",
                "兵营", FacilityCopy("barracks", "军队与编队"), "单位名册");
            var rosterHeading = Ui<Text>(panel, "Right/Title");
            var rosterScroll = panelScroll;
            var tally = BaseDevelopment.Tally(state);
            FacilityText(actions, $"士兵 {tally[0]} · 队长 {state.Leaders.Count} · 可主动出战 {tally[2]} · 重伤 {tally[3]}");
            var tabs = CloneSceneUi("FacilityActionRow", actions);
            FacilityButton(tabs, "Training", "等级训练", OpenTraining);
            FacilityButton(tabs, "Recovery", "伤情与岗位", () =>
            {
                BeginPanel("兵营 · 伤情与岗位", "barracks:recovery");
                RowButton("返回兵营", 0, 800, OpenBarracks); row += 60;
                Recovery();
                EndPanel();
            });
            if (state.Muster.Parties.Count > 0) FacilityButton(tabs, "Away", "外出队伍", () =>
            {
                BeginPanel("兵营 · 外出队伍", "barracks:away");
                RowButton("返回兵营", 0, 800, OpenBarracks); row += 60;
                OutsideParties(OpenBarracks);
                EndPanel();
            });
            if (state.Aftermath.Prisoners.Any(p => p.Status == "held")) FacilityButton(tabs, "Prisoners", "俘虏名册", OpenHeldPrisoners);

            BaseLeader Commander(BaseState current) => current.Leaders.FirstOrDefault(l => l.Id == compositionLeader)
                ?? current.Leaders.Single(l => l.Id == current.PlayerLeaderId);
            var commanderHeading = FacilityText(actions, "", true);
            FacilityText(actions, "从右侧队长名册切换。预设人数在备战时按实际兵力配置。");
            var counters = new Dictionary<BaseTroopKind, RectTransform>();
            var commanderRows = new Dictionary<string, (Image Background, Color DefaultColor)>();
            foreach (BaseTroopKind kind in Enum.GetValues(typeof(BaseTroopKind)))
            {
                var counter = FacilityCounter(actions, kind.ToString(), "", () => ChangeTroopCount(kind, -1), () => ChangeTroopCount(kind, 1), false, false);
                counters.Add(kind, counter);
                counter.Find("Unlock").gameObject.SetActive(false);
            }

            void RefreshCommander()
            {
                var current = owner.State;
                var leader = Commander(current);
                bool available = !leader.Away && !current.Campaign.HasActiveBattle;
                commanderHeading.text = BasePersonnel.LeaderName(leader) + " · 默认带兵";
                foreach (var (kind, counter) in counters)
                {
                    int count = leader.DefaultTroops.Count(kind);
                    Ui<Text>(counter, "Label").text = BaseEconomy.TroopName(kind) + "  " + count;
                    Ui<Button>(counter, "Minus").interactable = available && count > 0;
                    Ui<Button>(counter, "Plus").interactable = available && count < 30;
                }
                foreach (var (id, (background, defaultColor)) in commanderRows)
                    background.color = id == leader.Id ? Color.Lerp(defaultColor, DemoUiTheme.Current.Gold, .38f) : defaultColor;
            }

            void ChangeTroopCount(BaseTroopKind kind, int delta)
            {
                var leader = Commander(owner.State);
                int count = Mathf.Clamp(leader.DefaultTroops.Count(kind) + delta, 0, 30);
                SetFacilityCommanderTroops(leader.Id, kind, count, RefreshCommander);
            }

            FacilityText(actions, "默认策略与范围 · 驻守 / 威胁 / 进攻", true);
            var fields = new InputField[9];
            var strategies = new[] { SquadStrategy.Obey, SquadStrategy.Attack, SquadStrategy.Protect };
            string rosterStamp = null;
            for (int i = 0; i < 3; i++)
            {
                int slot = i;
                var config = CloneSceneUi("FacilityRangeRow", actions);
                config.name = "Slot" + (char)('A' + slot);
                Ui<Text>(config, "Label").text = (char)('A' + slot) + " 位";
                var toggle = Ui<Button>(config, "Strategy");
                void RefreshStrategy()
                {
                    var current = owner.State;
                    toggle.GetComponentInChildren<Text>().text = BattleSimulation.StrategyName(BaseSceneRules.Tier((SquadStrategy)current.Muster.Strategies[slot]));
                    toggle.interactable = !current.Campaign.HasActiveBattle;
                }
                RefreshStrategy();
                toggle.onClick.AddListener(() =>
                {
                    if (owner.Busy) return;
                    FacilityChange(next =>
                    {
                        if (next.Campaign.HasActiveBattle) throw new InvalidOperationException("战斗中不能修改默认策略");
                        var strategy = BaseSceneRules.Tier((SquadStrategy)next.Muster.Strategies[slot]);
                        next.Muster.Strategies[slot] = (int)strategies[(Array.IndexOf(strategies, strategy) + 1) % strategies.Length];
                    }, "已保存默认策略", () =>
                    {
                        RefreshStrategy();
                        rosterStamp = BaseMuster.Snapshot(owner.State);
                    });
                });
                var names = new[] { "Guard", "Threat", "Attack" };
                for (int j = 0; j < names.Length; j++)
                {
                    int index = slot * 3 + j;
                    fields[index] = Ui<InputField>(config, names[j]);
                    fields[index].SetTextWithoutNotify(rangeDraft?[index] ?? state.Muster.Ranges[index].ToString());
                    fields[index].interactable = !state.Campaign.HasActiveBattle;
                }
            }
            FacilityButton(actions, "SaveRanges", "保存位次范围", () => FacilityChange(next =>
            {
                var values = fields.Select(field => int.TryParse(field.text, out int value) ? value : 0).ToArray();
                BaseMuster.SetRanges(next, values);
            }, "已保存位次范围", () =>
            {
                for (int i = 0; i < fields.Length; i++)
                    fields[i].SetTextWithoutNotify(owner.State.Muster.Ranges[i].ToString());
                rosterStamp = BaseMuster.Snapshot(owner.State);
            }), !state.Campaign.HasActiveBattle);
            FacilityButton(actions, "Rules", "兵营规则", () => FacilityRules(OpenBarracks));

            float listOffset = 0;
            var choices = new (SoldierKind? Kind, Button Button, Color Color)[5];
            Button back = null;
            AddFilter(0, null, "All", "全部");
            int filterIndex = 1;
            foreach (var kind in new[] { SoldierKind.Hero, SoldierKind.Melee, SoldierKind.Ranged, SoldierKind.Cavalry })
                AddFilter(filterIndex++, kind, kind.ToString(), BattleSimulation.KindName(kind));
            back = FacilityButton(filters, "Back", "返回名册", () =>
            {
                facilityPersonnelDetail = null;
                RefreshRoster(listOffset);
            });
            RefreshRoster();
            RefreshCommander();
            FinishFacilityPanel();

            void AddFilter(int index, SoldierKind? kind, string name, string label)
            {
                var button = FacilityButton(filters, name, label, () =>
                {
                    if (facilityRosterKind == kind && facilityPersonnelDetail == null) return;
                    facilityRosterKind = kind;
                    facilityPersonnelDetail = null;
                    listOffset = 0;
                    RefreshRoster();
                });
                choices[index] = (kind, button, button.targetGraphic.color);
            }

            void RefreshRoster(float offset = 0)
            {
                var current = owner.State;
                var personnel = BasePersonnel.Units(current).ToArray();
                var detail = personnel.FirstOrDefault(u => u.Key == facilityPersonnelDetail);
                var leader = Commander(current);
                rosterStamp = BaseMuster.Snapshot(current);
                rosterHeading.text = detail == null ? "单位名册" : FacilityCopy("details", "单位详情");
                foreach (var (kind, button, color) in choices)
                {
                    button.gameObject.SetActive(detail == null);
                    button.targetGraphic.color = kind == facilityRosterKind ? DemoUiTheme.Current.Gold : color;
                }
                back.gameObject.SetActive(detail != null);
                commanderRows.Clear();
                for (int i = list.childCount - 1; i >= 0; i--)
                {
                    var child = list.GetChild(i);
                    child.gameObject.SetActive(false);
                    child.SetParent(null, false);
                    UiPrefabBinding.DestroyView(child.gameObject);
                }

                if (detail != null) FillBarracksDetail(list, detail, () =>
                {
                    var draft = fields.Select(field => field.text).ToArray();
                    OpenPersonnelDetail(detail.Key, rosterStamp, () => OpenBarracksFacility(draft), "返回兵营");
                });
                else
                {
                    var units = personnel.Where(u => facilityRosterKind == null || u.Kind == facilityRosterKind).ToArray();
                    foreach (var unit in units)
                    {
                        Action details = () =>
                        {
                            if (BaseMuster.Snapshot(owner.State) == rosterStamp)
                            {
                                listOffset = Mathf.Max(0, list.anchoredPosition.y);
                                facilityPersonnelDetail = unit.Key;
                            }
                            RefreshRoster();
                        };
                        Action select = unit.IsLeader ? () => { compositionLeader = unit.LeaderId; RefreshCommander(); } : details;
                        var card = AddPersonnelRow(list, unit, unit.State, select, useCurrentStats: true, details: unit.IsLeader ? details : null);
                        if (unit.IsLeader)
                        {
                            var background = Ui<Image>(card, "");
                            commanderRows.Add(unit.LeaderId, (background, background.color));
                            if (unit.LeaderId == leader.Id) background.color = Color.Lerp(background.color, DemoUiTheme.Current.Gold, .38f);
                        }
                    }
                    if (units.Length == 0) FacilityText(list, "暂无符合条件的人员。");
                }

                rosterScroll.StopMovement();
                if (offset > 0)
                {
                    LayoutRebuilder.ForceRebuildLayoutImmediate(list);
                    offset = Mathf.Clamp(offset, 0, Mathf.Max(0, list.rect.height - rosterScroll.viewport.rect.height));
                }
                list.anchoredPosition = new Vector2(0, offset);
            }
        }

        private void FillBarracksDetail(Transform list, BasePersonnelUnit unit, Action openComplete)
        {
            FacilityText(list, unit.Name + " · " + BattleSimulation.KindName(unit.Kind), true);
            FacilityText(list, $"生命 {unit.Health:0.#}/{unit.MaxHealth:0.#} · {unit.State}");
            FacilityText(list, $"当前攻击 {unit.Current.Damage:0.##} · 防御 {unit.Current.Defense:0.##}");
            FacilityText(list, $"基础攻击 {unit.Basic.Damage:0.##} · 防御 {unit.Basic.Defense:0.##}");
            FacilityText(list, unit.Facility + " · " + (unit.Locked ? "手动锁定" : "自动安排"));
            FacilityButton(list, "CompleteDetail", "完整属性", openComplete);
        }

        private void SetFacilityCommanderTroops(string leaderId, BaseTroopKind kind, int count, Action refresh) => FacilityChange(next =>
        {
            var leader = next.Leaders.Single(x => x.Id == leaderId);
            if (leader.Away || next.Campaign.HasActiveBattle) throw new InvalidOperationException("队长外出期间保留冻结编队，请返营后修改");
            leader.DefaultTroops.Set(kind, count);
        }, "已保存队长预设，未抽调人员", refresh);

        private void OpenHomeFacility()
        {
            activeObject = interactionObject = "home";
            var state = owner.State;
            int day = state.Day;
            var preview = BaseEconomy.ProjectDay(state);
            var raids = BaseCampaign.Due(state).ToArray();
            string restProblem = BaseDemoFlow.RestProblem(state, preview);
            var (actions, filters, list) = OpenFacilityPanel("facility:home", "营帐", "休息与管理", FacilityCopy("forecast", "日结预览"));
            FacilityText(filters, $"第{day}天 → 第{day + 1}天", true);
            FacilityButton(actions, "Rest", "休息至明天", () => owner.Rest(day), restProblem == "" || BaseDemoDefense.AnyDue(state), true);
            if (restProblem != "" && raids.Length == 0) FacilityText(actions, restProblem);
            if (BaseDemo.ExperimentalManagementHidden(state))
                FacilityButton(actions, "Government", "营地建设", OpenCampConstruction);
            else
                FacilityButton(actions, "Government", "营地管理", () => { activeObject = "government"; activePage = "workers"; OpenGovernment(); });
            if (BasePartyInventory.AtWarehouse(state)) FacilityButton(actions, "Warehouse", "仓库", OpenWarehouse);
            if (BaseDeputy.Unlocked(state)) FacilityButton(actions, "Deputy", "副手 · " + (BaseDeputy.Leader(state)?.Name ?? "未指定"), OpenDeputy);
            if (state.Leaders.Any(l => l.Id != state.PlayerLeaderId && l.Rpg != null && !l.Rpg.Story))
                FacilityButton(actions, "Dismiss", "请离基地", OpenDismiss);
            if (BaseRpg.Active(state))
            {
                var rules = state.Demo.Rpg;
                FacilityText(actions, $"营帐 {rules.CampLevel}级 · 队长名额 {BaseRpg.Population(state)}/{BaseRpg.Capacity(state)}", true);
                if (rules.CampLevel < rules.Capacity.Length)
                {
                    int index = rules.CampLevel - 1;
                    FacilityText(actions, $"升级所需 · 原料 {rules.CampMaterials[index]} · 钱币 {rules.CampCoins[index]} · 容量 {rules.Capacity[index + 1]}");
                    FacilityButton(actions, "Upgrade", "升级营帐", () => FacilityChange(next =>
                    {
                        if (!BaseRpg.UpgradeCamp(next)) throw new InvalidOperationException(BaseRpg.CampUpgradeProblem(next));
                    }, "营帐已升级", OpenHome), BaseRpg.CampUpgradeProblem(state) == "");
                }
            }
            FacilityText(list, $"预计产粮 +{preview.FoodProduced} · 明日口粮 {preview.FoodRequired} · 扣粮后库存 {preview.NextFood}");
            FacilityText(list, $"{preview.ProductLevel}级{BaseEconomy.ProductName(preview.Product)} +{preview.ProductsMade} · 原料消耗 {preview.MaterialsSpent}");
            FacilityText(list, $"今日生产效率 {preview.ProductionFactor:P0}" + (preview.NextMissing > 0 ? $" · 预计缺粮 {preview.NextMissing}" : ""));
            if (raids.Length > 0)
            {
                FacilityText(list, "待处理入侵", true);
                foreach (var raid in raids) FacilityText(list, raid.Title);
            }
            FinishFacilityPanel();
        }
    }
}

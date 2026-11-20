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
        private string tentLeaderId;
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

        private static void ShowFacilityPortrait(Transform column, string name, string description, string appearance, bool player = false)
        {
            Ui<Text>(column, "Name").text = name;
            Ui<Text>(column, "Effect").text = description;
            var portrait = Ui<RectTransform>(column, "Portrait");
            bool visible = player || !string.IsNullOrEmpty(appearance);
            portrait.gameObject.SetActive(visible);
            if (!visible) return;
            var avatar = portrait.gameObject.GetOrAdd<BattleAvatarGraphic>();
            avatar.PlayerHero = player;
            avatar.AppearanceId = appearance;
            avatar.raycastTarget = false;
        }

        private void ShowFacilityLeader(Transform column, BaseLeader leader, string description)
        {
            bool player = leader.Id == owner.State.PlayerLeaderId;
            ShowFacilityPortrait(column, BasePersonnel.LeaderName(leader), description,
                player ? BaseDemoIntro.PlayerArt(owner.State) : BaseCommanderIdentity.Appearance(leader), player);
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
            if (workerDraft != null && (workerMoves.Count > 0 || workerDraftSource == owner.State)) return;
            workerDraftSource = owner.State;
            workerDraftStamp = BaseMuster.Snapshot(owner.State);
            workerDraft = owner.State.Copy();
            LockCurrentPosts(workerDraft);
            workerMoves.Clear();
        }

        private void ResetWorkerDraft()
        {
            workerDraft = null;
            workerDraftSource = null;
            workerMoves.Clear();
        }

        private void CommitWorkerDraft(Action refresh)
        {
            var moves = workerMoves.ToArray();
            if (moves.Length == 0) return;
            bool saved = owner.Change(next =>
            {
                if (BaseMuster.Snapshot(next) != workerDraftStamp) throw new InvalidOperationException("人员已变化，请取消方案后重新安排");
                ApplyNamedTransfers(next, moves);
            }, "已确认工人安排");
            if (saved)
            {
                owner.OpeningAudio?.Play(VillageOpeningAudio.Cue.Assignment);
                foreach (var (_, move) in moves) owner.EnqueueWorkerTransfer(move.kind, move.from, move.to, 1);
                ResetWorkerDraft();
                refresh();
            }
            if (panelFeedback != null)
            {
                panelFeedback.text = owner.Message;
                panelFeedback.gameObject.SetActive(true);
            }
        }

        private static void ApplyNamedTransfers(BaseState next, KeyValuePair<int,(BaseTroopKind kind,string from,string to)>[] moves)
        {
            if (BaseDemo.Active(next)) BaseSettlements.SetPolicy(next,"manual");
            LockCurrentPosts(next);
            // 先空出旧岗位，满员时才能在同一份草案内换人。
            foreach (var (number, move) in moves)
                if (move.from != "" && !BaseEconomy.MoveWorker(next, number, move.from, ""))
                    throw new InvalidOperationException("人员已变化，请取消方案后重新安排");
            foreach (var (number, move) in moves)
                if (move.to != "" && !BaseEconomy.MoveWorker(next, number, "", move.to))
                    throw new InvalidOperationException("岗位已变化，请取消方案后重新安排");
            if (BaseDemoFlow.Active(next) && !next.Demo.Flow.FarmOfferShown && moves.Any(pair => pair.Value.to == "farm"))
            {
                next.Demo.Flow.FarmOfferPending = true;
                next.Demo.Flow.FarmX = next.PlayerX;
                next.Demo.Flow.FarmY = next.PlayerY;
            }
        }

        private sealed class ProductionFacilityView
        {
            public string FacilityId;
            public RectTransform Panel, Actions, List, Portrait;
            public BaseState State;
            public bool HidePostLock;
            public SoldierKind? Filter;
            public readonly List<(SoldierKind? kind, Button button, Color color)> Filters = new List<(SoldierKind?,Button,Color)>();
            public readonly Dictionary<int,RectTransform> Soldiers = new Dictionary<int,RectTransform>();
            public Color RosterColor;
            public Text Civilians, Empty;
            public Action<bool> UpdateManagerAvailability;
            public Action<BaseState, BaseDayPreview, int, bool> RefreshActions;
        }

        private ProductionFacilityView productionView;

        internal void OpenSettlementFacility(string facility)
        {
            if (!BaseSettlements.Available(owner.State,facility,true)) return;
            activeObject = interactionObject = facility;
            activePage = "workers";
            switch (facility)
            {
                case "home": OpenHome(); break;
                case "farm":
                case "workshop": OpenFacility(); break;
                case "barracks": OpenBarracksFacility(); break;
            }
        }

        private void OpenProductionFacility()
        {
            string id = activeObject;
            var state = owner.State;
            bool reuse = PanelOpen && productionView != null && productionView.Panel == panel && productionView.FacilityId == id;
            if (!reuse) ResetWorkerDraft();
            EnsureWorkerDraft();
            bool pending = workerMoves.Count > 0;
            var draft = workerDraft;
            var facility = state.Muster.Facilities.Single(x => x.FacilityId == id);
            string key = id + ":workers";
            string title = BaseEconomy.FacilityName(id) + " · " + facility.Level + "级";
            if (!reuse)
            {
                var (actions, filters, list) = OpenFacilityPanel(key, title, FacilityCopy("production", "生产与岗位"), FacilityCopy("workers", "士兵名册"));
                productionView = new ProductionFacilityView
                {
                    FacilityId = id, Panel = panel, Actions = actions, List = list, Portrait = Ui<RectTransform>(panel, "PortraitColumn")
                };
                var created = productionView;
                foreach (SoldierKind? kind in new SoldierKind?[] { null, SoldierKind.Melee, SoldierKind.Ranged, SoldierKind.Cavalry })
                {
                    var button = FacilityButton(filters, kind?.ToString() ?? "All", kind.HasValue ? BattleSimulation.KindName(kind.Value) : "全部", () =>
                    {
                        if (created.Filter == kind) return;
                        created.Filter = kind;
                        panelScroll.StopMovement();
                        created.List.anchoredPosition = Vector2.zero;
                        OpenProductionFacility();
                    });
                    created.Filters.Add((kind, button, button.targetGraphic.color));
                }
                created.Civilians = FacilityText(list, "", true);
                created.Empty = FacilityText(list, "");
            }

            var view = productionView;
            panelFeedback.gameObject.SetActive(false);
            Ui<Text>(panel, "Title").text = title;

            bool hidePostLock = BaseDemo.ExperimentalManagementHidden(state);
            bool rebuildActions = view.State != state || view.HidePostLock != hidePostLock;
            float actionOffset = view.Actions.anchoredPosition.y;
            if (rebuildActions)
            {
                ClearPanelChildren(view.Actions);
                var manager = BaseEconomy.Manager(state,id);
                view.UpdateManagerAvailability = BindFacilityManager(view.Portrait, state, id, manager);
                view.RefreshActions = BuildProductionActions(view.Actions, id, state, facility, manager, hidePostLock);
                view.State = state;
                view.HidePostLock = hidePostLock;
            }
            int count = BaseEconomy.WorkersAt(draft, id);
            view.UpdateManagerAvailability(pending);
            view.RefreshActions(draft, BaseEconomy.ProjectDay(draft), count, pending);

            FillFacilityWorkers(view, draft);
            if (rebuildActions) RestoreProductionScroll(facilityControls, actionOffset);
        }

        private Action<bool> BindFacilityManager(RectTransform column, BaseState state, string facility, BaseLeader manager)
        {
            foreach (var name in new[] { "Manage", "ManagerOptions" })
            {
                var old = column.Find(name);
                if (old == null) continue;
                old.gameObject.SetActive(false);
                old.SetParent(null);
                UiPrefabBinding.DestroyView(old.gameObject);
            }
            var incoming = state.Leaders.FirstOrDefault(l => l.Relocating && l.VillageResident != BaseSettlements.AtVillage(state) && l.ArrivalFacility == facility);
            if (incoming != null)
                ShowFacilityLeader(column,incoming,"调动中 · 次日到岗");
            else if (manager != null)
                ShowFacilityLeader(column, manager, "产能 +" + BaseDevelopment.Bonus(manager, facility).ToString("P0"));
            else ShowFacilityPortrait(column, "负责人 · 未安排", "", "");

            var candidates = manager == null
                ? state.Leaders.Where(leader => BaseEconomy.CanAssignLeader(state, leader.Id, facility)).ToArray()
                : Array.Empty<BaseLeader>();
            var permissions = manager == null ? candidates.Length > 0 : BaseEconomy.CanAssignLeader(state, manager.Id, "");
            var options = manager == null && permissions ? CloneSceneUi("FacilityPanel/Left/List", column) : null;
            if (options != null)
            {
                options.name = "ManagerOptions";
                var list = options.GetComponent<ScrollRect>().content;
                foreach (var leader in candidates)
                    FacilityButton(list, leader.Id, BasePersonnel.LeaderName(leader) + (!BaseAdventure.Contains(state,leader.Id) && leader.VillageResident != BaseSettlements.AtVillage(state) ? " · 异地 1天" : ""), () => ConfirmManager(leader.Id, facility, OpenFacility));
                options.gameObject.SetActive(false);
            }

            var change = FacilityButton(column, "Manage", manager == null ? "调整管理员" : "解除任职", () =>
            {
                if (manager != null) ConfirmManager(manager.Id, "", OpenFacility);
                else options.gameObject.SetActive(!options.gameObject.activeSelf);
            }, permissions);
            PlaceFacilityPortraitOptions(column, change, options);
            return pending =>
            {
                change.interactable = permissions && !pending;
                if (pending && options != null) options.gameObject.SetActive(false);
            };
        }

        private static void PlaceFacilityPortraitOptions(Transform column, Button toggle, RectTransform options)
        {
            var effect = Ui<RectTransform>(column, "Effect");
            var button = (RectTransform)toggle.transform;
            button.anchorMin = button.anchorMax = button.pivot = Vector2.up;
            button.anchoredPosition = new Vector2(effect.anchoredPosition.x, effect.anchoredPosition.y - effect.rect.height - 8);
            button.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, effect.rect.width);
            if (options == null) return;
            options.anchorMin = Vector2.zero;
            options.anchorMax = Vector2.one;
            options.offsetMin = new Vector2(effect.anchoredPosition.x, 8);
            options.offsetMax = new Vector2(-effect.anchoredPosition.x, button.anchoredPosition.y - button.rect.height - 6);
        }

        private static void RestoreProductionScroll(ScrollRect scroll, float offset)
        {
            LayoutRebuilder.ForceRebuildLayoutImmediate(scroll.content);
            scroll.StopMovement();
            scroll.content.anchoredPosition = new Vector2(0, Mathf.Clamp(offset, 0,
                Mathf.Max(0, scroll.content.rect.height - scroll.viewport.rect.height)));
        }

        private Action<BaseState, BaseDayPreview, int, bool> BuildProductionActions(Transform actions, string id, BaseState state,
            BaseFacilityProtection facility, BaseLeader manager, bool hidePostLock)
        {
            bool workshop = id == "workshop";

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
                    canUnlock = state.Workers.Any(w => BaseSettlements.Local(state,w) && w.Kind == kind && w.PostLocked &&
                        (w.FacilityId == id || w.DesiredFacility == id));
                    unlock.onClick.AddListener(() => FacilityChange(next =>
                    {
                        foreach (var worker in next.Workers.Where(w => BaseSettlements.Local(next,w) && w.Kind == kind && (w.FacilityId == id || w.DesiredFacility == id)))
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

            return (draft, preview, count, pending) =>
            {
                int producing = BaseEconomy.ProductionWorkersAt(draft, id);
                staff.text = $"岗位 {producing}/{facility.Capacity} · {BaseDevelopment.PolicyName(BaseDevelopment.EffectivePolicy(draft))}";
                string scope = BaseDemoFinale.HomeVolunteers(draft) ? "两地" : "";
                output.text = workshop
                    ? $"{scope}预计产出 {preview.ProductLevel}级{BaseEconomy.ProductName(draft.WorkshopProduct)} {preview.ProductsMade} 件 · 原料 {preview.MaterialsSpent}"
                    : $"{scope}预计产粮 {preview.FoodProduced} · 扣粮后库存 {preview.NextFood}";
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
                        BaseSettlements.Local(draft,w) && w.FacilityId == "" && BaseDevelopment.Effective(draft, w));
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

        private string WorkerRowStatus(BasePersonnelUnit unit)
        {
            if (workerMoves.TryGetValue(unit.SoldierNumber, out var move))
                return "待调 " + BaseEconomy.FacilityName(move.from) + "→" + BaseEconomy.FacilityName(move.to) + " · 可生产";
            if (unit.Relocating) return unit.State;
            if (unit.Remote) return unit.State;
            if (unit.Away) return "外出 · 无法生产";
            if (!unit.Effective) return unit.Health <= 0 ? "倒地 · 无法生产" : "重伤 · 无法生产";
            return unit.Facility + " · 今日可生产";
        }

        private void FillFacilityWorkers(ProductionFacilityView view, BaseState draft)
        {
            int civilians = BaseEconomy.CivilianWorkersAt(draft, view.FacilityId);
            view.Civilians.gameObject.SetActive(civilians > 0 && !view.Filter.HasValue);
            view.Civilians.text = CivilianRosterLabel(civilians);
            foreach (var (kind, button, color) in view.Filters)
                button.targetGraphic.color = kind == view.Filter ? DemoUiTheme.Current.Gold : color;
            var units = BasePersonnel.Soldiers(draft).ToArray();
            var activeNumbers = new HashSet<int>();
            bool anyVisible = false;
            foreach (var unit in units)
            {
                int number = unit.SoldierNumber;
                activeNumbers.Add(number);
                string status = WorkerRowStatus(unit);
                if (!view.Soldiers.TryGetValue(number, out var card))
                {
                    card = AddPersonnelRow(view.List, unit, status, () => ToggleWorker(number, view.FacilityId, OpenFacility), showFacility:false);
                    view.RosterColor = card.GetComponent<Image>().color;
                    card.name = "Soldier" + number;
                    view.Soldiers.Add(number, card);
                }
                else UpdatePersonnelRow(card, unit, status, showFacility:false);
                var select = card.GetComponent<Button>();
                select.interactable = unit.Effective && !unit.Remote && !unit.Relocating;
                select.targetGraphic.color = workerMoves.ContainsKey(number) ? Color.Lerp(view.RosterColor, DemoUiTheme.Current.Gold, .38f) : view.RosterColor;
                bool visible = !view.Filter.HasValue || view.Filter.Value == unit.Kind;
                card.gameObject.SetActive(visible);
                anyVisible |= visible;
            }
            foreach (int number in view.Soldiers.Keys.Where(n => !activeNumbers.Contains(n)).ToArray())
            {
                var card = view.Soldiers[number];
                view.Soldiers.Remove(number);
                card.gameObject.SetActive(false);
                UiPrefabBinding.DestroyView(card.gameObject);
            }
            view.Empty.gameObject.SetActive(!anyVisible);
            view.Empty.text = units.Length == 0 ? "尚未招募士兵。" : "没有该兵种的士兵。";
        }

        private static string CivilianRosterLabel(int count) => $"村民 ×{count} · 补位";

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
                "兵营", FacilityCopy("barracks", "军队与编队"), "士兵名册");
            var rosterHeading = Ui<Text>(panel, "Right/Title");
            var portrait = Ui<RectTransform>(panel, "PortraitColumn");
            var rosterScroll = panelScroll;
            var tally = BaseDevelopment.Tally(state);
            FacilityText(actions, $"士兵 {tally[0]} · 队长 {state.Leaders.Count} · 可主动出战 {tally[2]} · 重伤 {tally[3]}");
            var tabs = CloneSceneUi("FacilityActionRow", actions);
            if (state.Campaign.Location == "base")
                FacilityButton(actions, "LanDuel", "模拟对抗", () => OpenLanDuel());
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

            BaseLeader Commander(BaseState current) => current.Leaders.FirstOrDefault(l => l.Id == compositionLeader && BaseAdventure.Alive(l))
                ?? current.Leaders.Single(l => l.Id == current.PlayerLeaderId);
            var commanderHeading = FacilityText(actions, "", true);
            FacilityText(actions, "预设人数在备战时按实际兵力配置。");
            var counters = new Dictionary<BaseTroopKind, RectTransform>();
            foreach (BaseTroopKind kind in Enum.GetValues(typeof(BaseTroopKind)))
            {
                var counter = FacilityCounter(actions, kind.ToString(), "", () => ChangeTroopCount(kind, -1), () => ChangeTroopCount(kind, 1), false, false);
                counters.Add(kind, counter);
                counter.Find("Unlock").gameObject.SetActive(false);
            }

            var captains = state.Leaders.Where(BaseAdventure.Alive).ToArray();
            if (captains.Length > 1)
            {
                var options = CloneSceneUi("FacilityPanel/Left/List", portrait);
                options.name = "FormationCaptainOptions";
                options.gameObject.SetActive(false);
                var optionsList = options.GetComponent<ScrollRect>().content;
                foreach (var captain in captains)
                    FacilityButton(optionsList, captain.Id, BasePersonnel.LeaderName(captain), () =>
                    {
                        compositionLeader = captain.Id;
                        options.gameObject.SetActive(false);
                        RefreshCommander();
                    });
                var choose = FacilityButton(portrait, "FormationCaptain", "切换编队对象", () =>
                    options.gameObject.SetActive(!options.gameObject.activeSelf));
                PlaceFacilityPortraitOptions(portrait, choose, options);
            }

            void RefreshCommander()
            {
                var current = owner.State;
                var leader = Commander(current);
                bool available = !leader.Away && !current.Campaign.HasActiveBattle;
                ShowFacilityLeader(portrait, leader, "队长");
                commanderHeading.text = BasePersonnel.LeaderName(leader) + " · 默认带兵";
                foreach (var (kind, counter) in counters)
                {
                    int count = leader.DefaultTroops.Count(kind);
                    Ui<Text>(counter, "Label").text = BaseEconomy.TroopName(kind) + "  " + count;
                    Ui<Button>(counter, "Minus").interactable = available && count > 0;
                    Ui<Button>(counter, "Plus").interactable = available && count < 30;
                }
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

            float listOffset = 0;
            var choices = new (SoldierKind? Kind, Button Button, Color Color)[4];
            Button back = null;
            AddFilter(0, null, "All", "全部");
            int filterIndex = 1;
            foreach (var kind in new[] { SoldierKind.Melee, SoldierKind.Ranged, SoldierKind.Cavalry })
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
                var personnel = BasePersonnel.Soldiers(current).ToArray();
                var detail = personnel.FirstOrDefault(u => u.Key == facilityPersonnelDetail);
                rosterStamp = BaseMuster.Snapshot(current);
                rosterHeading.text = detail == null ? "士兵名册" : FacilityCopy("details", "单位详情");
                foreach (var (kind, button, color) in choices)
                {
                    button.gameObject.SetActive(detail == null);
                    button.targetGraphic.color = kind == facilityRosterKind ? DemoUiTheme.Current.Gold : color;
                }
                back.gameObject.SetActive(detail != null);
                ClearPanelChildren(list);

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
                        AddPersonnelRow(list, unit, unit.State, details);
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
            FacilityText(list, CampSight(unit.Current));
            FacilityText(list, $"基础攻击 {unit.Basic.Damage:0.##} · 防御 {unit.Basic.Defense:0.##}");
            FacilityText(list, unit.Facility + " · " + (unit.Locked ? "手动锁定" : "自动安排"));
            FacilityButton(list, "CompleteDetail", "完整属性", openComplete);
            var state = owner.State;
            if (BaseDemoFinale.Opened(state) && BaseDemoFinale.Later(state))
            {
                var worker = state.Workers[unit.BucketIndex];
                bool cancel = worker.Relocating;
                string reason = cancel ? "" : BaseSettlements.TransferProblem(state,worker);
                string label = cancel ? "取消调动" : "调往" + BaseSettlements.Name(!worker.VillageResident) + " · 1天停工 · 路粮 " + BaseEconomy.Ration(state,worker);
                FacilityButton(list,"Relocate",reason == "" ? label : reason, () =>
                    FacilityChange(next => BaseSettlements.Order(next,unit.SoldierNumber),"", () => OpenBarracksFacility()),reason == "");
            }
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
            var raids = BaseCampaign.Due(state).ToArray();
            var (actions, filters, list) = OpenFacilityPanel("facility:home", BaseSettlements.AtVillage(state) ? "家" : "营帐", "休息与管理", "队长名册");
            var portrait = Ui<RectTransform>(panel, "PortraitColumn");
            var cards = new Dictionary<string, (RectTransform Card, Color DefaultColor)>();
            var partyCapacity = FacilityText(filters, "", true);
            partyCapacity.name = "PartyCapacity";
            FacilityButton(actions, "Rest", "休息至明天", () => owner.Rest(day), selected: true);
            Action updatePolicy = null;
            var policies = CloneSceneUi("FacilityActionRow",actions);
            var policyButtons = new List<(string id,Button button,Color normal)>();
            foreach (var option in new[] { ("manual","手动"), ("food","粮田优先"), ("equipment","工坊优先") })
            {
                string id = option.Item1;
                var button = FacilityButton(policies,id,option.Item2,() =>
                {
                    if (BaseDevelopment.EffectivePolicy(owner.State) != id)
                        FacilityChange(next => BaseDevelopment.SetPolicy(next,id),"",() => updatePolicy?.Invoke());
                });
                policyButtons.Add((id,button,button.targetGraphic.color));
            }
            void RefreshPolicy()
            {
                foreach (var item in policyButtons)
                    item.button.targetGraphic.color = BaseDevelopment.EffectivePolicy(owner.State) == item.id ? DemoUiTheme.Current.Gold : item.normal;
                RefreshHome();
            }
            var restAdvice = FacilityText(actions, "");
            if (BasePartyInventory.AtWarehouse(state)) FacilityButton(actions, "Warehouse", "仓库", OpenWarehouse);
            if (BaseDeputy.Unlocked(state)) FacilityButton(actions, "Deputy", "副手 · " + (BaseDeputy.Leader(state)?.Name ?? "未指定"), OpenDeputy);

            FacilityText(actions, "队长管理", true);
            var roleActions = CloneSceneUi("FacilityActionRow", actions);
            FacilityButton(roleActions, "CaptainDetail", "完整属性", () =>
                OpenPersonnelDetail("leader:" + tentLeaderId, BaseMuster.Snapshot(owner.State), OpenHome, "返回营帐"));
            Button follow = null, dismiss = null;
            Text restriction = null, capacity = null, food = null, product = null, efficiency = null;
            follow = FacilityButton(roleActions, "CaptainFollow", "", ChangeFollowing);
            restriction = FacilityText(actions, "");
            dismiss = FacilityButton(actions, "CaptainDismiss", "永久请离基地", () => FacilityChange(next =>
            {
                if (!BaseRpg.Dismiss(next, tentLeaderId)) throw new InvalidOperationException(BaseRpg.DismissProblem(next, tentLeaderId));
            }, "已请离基地，物品已返仓", RefreshHome));
            FacilityButton(actions, "Training", "等级训练", OpenTraining);

            if (BaseRpg.Active(state) && !BaseSettlements.AtVillage(state))
            {
                var rules = state.Demo.Rpg;
                capacity = FacilityText(actions, "", true);
                if (rules.CampLevel < rules.Capacity.Length)
                {
                    int index = rules.CampLevel - 1;
                    FacilityText(actions, $"升级所需 · 原料 {rules.CampMaterials[index]} · 钱币 {rules.CampCoins[index]}");
                    double recovery = Math.Min(BaseDevelopment.CampRecoveryStep, 1 - BaseDevelopment.RecoveryRatio(state));
                    FacilityText(actions, $"升级后 · 容量 {rules.Capacity[index + 1]} · 每日恢复 +{recovery * 100:0.#} 个百分点");
                    FacilityButton(actions, "Upgrade", "升级营帐", () => FacilityChange(next =>
                    {
                        if (!BaseRpg.UpgradeCamp(next)) throw new InvalidOperationException(BaseRpg.CampUpgradeProblem(next));
                    }, "营帐已升级", OpenHome), BaseRpg.CampUpgradeProblem(state) == "");
                }
            }
            foreach (var captain in BasePersonnel.Units(state).Take(state.Leaders.Count))
            {
                var card = AddPersonnelRow(list, captain, captain.State, () =>
                {
                    tentLeaderId = captain.LeaderId;
                    RefreshSelection(owner.State);
                }, showFacility: false);
                cards.Add(captain.LeaderId, (card, Ui<Image>(card, "").color));
            }
            FacilityText(list, $"{FacilityCopy("forecast", "日结预览")} · 第{day}天 → 第{day + 1}天", true);
            food = FacilityText(list, "");
            product = FacilityText(list, "");
            efficiency = FacilityText(list, "");
            if (raids.Length > 0)
            {
                FacilityText(list, "待处理入侵", true);
                foreach (var raid in raids) FacilityText(list, raid.Title);
            }
            updatePolicy = RefreshPolicy;
            RefreshPolicy();
            FinishFacilityPanel();

            void RefreshSelection(BaseState current)
            {
                var selected = current.Leaders.FirstOrDefault(l => l.Id == tentLeaderId)
                    ?? current.Leaders.Single(l => l.Id == current.PlayerLeaderId);
                tentLeaderId = selected.Id;
                ShowFacilityLeader(portrait, selected, BasePersonnel.State(current, selected));
                bool player = selected.Id == current.PlayerLeaderId;
                bool joining = !BaseAdventure.Contains(current, selected.Id);
                string problem = player ? "" : BaseAdventure.ChangeProblem(current, selected.Id, joining);
                follow.gameObject.SetActive(!player);
                follow.GetComponentInChildren<Text>().text = joining ? "加入冒险小队" : "留在营地";
                follow.interactable = problem == "" || problem == BaseAdventure.FullPartyReply;
                restriction.text = problem;
                restriction.gameObject.SetActive(problem != "" && problem != BaseAdventure.FullPartyReply);
                dismiss.gameObject.SetActive(!player && selected.Rpg != null && !selected.Rpg.Story);
                dismiss.interactable = BaseRpg.DismissProblem(current, selected.Id) == "";
                foreach (var (id, (card, normal)) in cards)
                    Ui<Image>(card, "").color = id == selected.Id ? Color.Lerp(normal, DemoUiTheme.Current.Gold, .38f) : normal;
            }

            void ChangeFollowing()
            {
                string id = tentLeaderId;
                bool joining = !BaseAdventure.Contains(owner.State, id);
                if (joining && BaseAdventure.Full(owner.State)) { OpenFullPartyReply(id,OpenHome); return; }
                FacilityChange(next =>
                {
                    if (!BaseAdventure.SetMember(next, id, joining))
                        throw new InvalidOperationException(BaseAdventure.ChangeProblem(next, id, joining));
                }, joining ? "伙伴已随行，实际岗位已释放" : "伙伴已留在营地，可安排岗位", RefreshHome);
            }

            void RefreshHome()
            {
                var current = owner.State;
                partyCapacity.text = $"小队名额 {current.Adventure.Members.Count}/{BaseAdventure.Capacity}";
                if (capacity != null)
                {
                    int population = BaseRpg.Population(current);
                    capacity.text = $"营帐 {current.Demo.Rpg.CampLevel}级 · 队长 {population} · 招降空位 {BaseRpg.RecruitingSlots(current)}";
                }
                var roster = BasePersonnel.Units(current).Take(current.Leaders.Count).ToDictionary(unit => unit.LeaderId);
                foreach (var (id, (card, _)) in cards)
                {
                    bool present = roster.TryGetValue(id, out var unit);
                    card.gameObject.SetActive(present);
                    if (present) UpdatePersonnelRow(card, unit, unit.State, showFacility: false);
                }
                var preview = BaseEconomy.ProjectDay(current);
                food.text = $"预计产粮 +{preview.FoodProduced} · 明日口粮 {preview.FoodRequired} · 扣粮后库存 {preview.NextFood}";
                product.text = $"{preview.ProductLevel}级{BaseEconomy.ProductName(preview.Product)} +{preview.ProductsMade} · 原料消耗 {preview.MaterialsSpent}";
                efficiency.text = $"今日生产效率 {preview.ProductionFactor:P0}" + (preview.NextMissing > 0 ? $" · 预计缺粮 {preview.NextMissing}" : "");
                restAdvice.text = BaseDemoFlow.RestAdvice(current, preview);
                restAdvice.gameObject.SetActive(restAdvice.text != "" && raids.Length == 0);
                RefreshSelection(current);
            }
        }
    }
}

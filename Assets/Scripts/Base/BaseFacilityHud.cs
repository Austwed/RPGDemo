using System;
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

        private void FacilityText(Transform parent, string text, bool heading = false)
        {
            var label = CloneSceneUi(heading ? "FacilityHeading" : "FacilityText", parent).GetComponent<Text>();
            label.text = text;
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
                image.color = Color.Lerp(image.color, DemoUiTheme.Current.Gold, .6f);
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
            if (!saved && panelFeedback != null)
            {
                panelFeedback.text = owner.Message;
                panelFeedback.gameObject.SetActive(true);
            }
        }

        private void OpenProductionFacility()
        {
            string id = activeObject;
            bool workshop = id == "workshop", managers = activePage == "administrators";
            bool pending = workerMoves.Count > 0;
            var state = owner.State;
            EnsureWorkerDraft();
            var draft = workerDraft;
            var facility = state.Muster.Facilities.Single(x => x.FacilityId == id);
            var preview = BaseEconomy.Preview(draft);
            var (actions, filters, list) = OpenFacilityPanel(id + ":" + activePage,
                BaseEconomy.FacilityName(id) + " · " + facility.Level + "级", FacilityCopy("production", "生产与岗位"),
                managers ? FacilityCopy("managers", "负责人名册") : FacilityCopy("workers", "工人名册"));

            FacilityButton(filters, "Workers", "工人", () => { activePage = "workers"; OpenFacility(); }, selected: !managers);
            FacilityButton(filters, "Managers", "负责人", () => { activePage = "administrators"; OpenFacility(); }, selected: managers);
            var manager = state.Leaders.FirstOrDefault(x => x.FacilityId == id);
            var card = CloneSceneUi("FacilityManagerCard", actions);
            Ui<Text>(card, "Name").text = manager == null ? "负责人 · 未安排" : "负责人 · " + BasePersonnel.LeaderName(manager);
            Ui<Text>(card, "Effect").text = manager == null ? "从右侧名册安排负责人" : "产能 +" + BaseDevelopment.Bonus(manager, id).ToString("P0");
            if (manager != null) BindInventoryPortrait(Ui<RectTransform>(card, "Portrait"), manager);

            int count = BaseEconomy.WorkersAt(draft, id);
            FacilityText(actions, $"岗位 {count}/{facility.Capacity} · 今日生产 {BaseEconomy.EligibleWorkers(draft, id)} 人");
            FacilityText(actions, workshop
                ? $"预计产出 {preview.ProductLevel}级{BaseEconomy.ProductName(draft.WorkshopProduct)} {preview.ProductsMade} 件 · 原料 {preview.MaterialsSpent}"
                : $"预计产粮 {preview.FoodProduced} · 扣粮后库存 {preview.NextFood}");
            if (workshop && preview.ProductsMade < preview.WorkCapacity) FacilityText(actions, "原料不足 · 预计产量已按库存计算");

            var commands = CloneSceneUi("FacilityActionRow", actions);
            FacilityButton(commands, "Apply", "确认方案", () => CommitWorkerDraft(OpenFacility), pending, pending);
            FacilityButton(commands, "Cancel", "取消方案", () => { ResetWorkerDraft(); OpenFacility(); }, pending);
            if (pending) FacilityText(actions, FacilityCopy("draft", "岗位调配待确认"));

            var kinds = Enum.GetValues(typeof(BaseTroopKind)).Cast<BaseTroopKind>();
            foreach (var kind in kinds)
            {
                int assigned = BaseEconomy.WorkersAt(draft, id, kind);
                bool free = draft.Workers.Any(w => w.Kind == kind && w.FacilityId == "" && BaseDevelopment.Effective(draft, w));
                var counter = FacilityCounter(actions, kind.ToString(), BaseEconomy.TroopName(kind) + "  " + assigned,
                    () => Adjust(kind, id, "", OpenFacility), () => Adjust(kind, "", id, OpenFacility),
                    assigned > 0, free && count < facility.Capacity);
                var unlock = Ui<Button>(counter, "Unlock");
                unlock.interactable = !pending && state.Workers.Any(w => w.Kind == kind && w.PostLocked &&
                    (w.FacilityId == id || w.DesiredFacility == id));
                unlock.onClick.AddListener(() => FacilityChange(next =>
                {
                    foreach (var worker in next.Workers.Where(w => w.Kind == kind && (w.FacilityId == id || w.DesiredFacility == id)))
                        worker.PostLocked = false;
                }, "已解锁工人，由策略安排", OpenFacility));
            }

            if (manager != null)
                FacilityButton(actions, "ManagerLock", manager.PostLocked ? "解锁负责人岗位" : "锁定负责人岗位", () =>
                    FacilityChange(next =>
                    {
                        var leader = next.Leaders.Single(x => x.Id == manager.Id);
                        leader.PostLocked = !leader.PostLocked;
                        if (leader.PostLocked) leader.DesiredFacility = leader.FacilityId;
                    }, "已更新管理员岗位锁定", OpenFacility), !pending && !BaseAdventure.Contains(state, manager.Id));
            if (workshop) BuildWorkshopControls(actions, state, facility, pending);
            FacilityButton(actions, "Rules", "设施规则", () => FacilityRules(OpenFacility));
            if (managers) FillFacilityManagers(list, state, id, pending);
            else FillFacilityWorkers(list, draft, id, count < facility.Capacity);
            FinishFacilityPanel();
        }

        private void BuildWorkshopControls(Transform actions, BaseState state, BaseFacilityProtection facility, bool pending)
        {
            FacilityText(actions, "生产配方", true);
            var grades = CloneSceneUi("FacilityActionRow", actions);
            for (int grade = 1; grade <= facility.MaxProductLevel; grade++)
            {
                int level = grade;
                FacilityButton(grades, "Grade" + level, level == 1 ? "一级军需" : "二级军需", () =>
                    FacilityChange(next =>
                    {
                        if (!BaseConstruction.SelectLevel(next, level)) throw new InvalidOperationException("当前不能改配方");
                    }, "已选择生产等级", OpenFacility), !pending, BaseConstruction.ProductLevel(state) == level);
            }
            var recipes = CloneSceneUi("FacilityActionRow", actions);
            foreach (string product in BaseEconomy.ProductsFor(state))
                FacilityButton(recipes, product, BaseEconomy.ProductName(product) + " · 原料" + BaseConstruction.MaterialCost(state, product),
                    () => FacilityChange(next =>
                    {
                        if (!BaseEconomy.SelectProduct(next, product)) throw new InvalidOperationException("当前不能改配方");
                    }, "已选择生产", OpenFacility),
                    !pending, state.WorkshopProduct == product);
        }

        private void FillFacilityWorkers(Transform list, BaseState draft, string id, bool room)
        {
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

        private void OpenBarracksFacility()
        {
            var state = owner.State;
            var personnel = BasePersonnel.Units(state).ToArray();
            var detail = personnel.FirstOrDefault(u => u.Key == facilityPersonnelDetail);
            var (actions, filters, list) = OpenFacilityPanel(detail == null ? "barracks:" + facilityRosterKind : "barracks-detail:" + detail.Key,
                "兵营", FacilityCopy("barracks", "军队与编队"), detail == null ? "单位名册" : FacilityCopy("details", "单位详情"));
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

            var leader = state.Leaders.FirstOrDefault(l => l.Id == compositionLeader) ?? state.Leaders.Single(l => l.Id == state.PlayerLeaderId);
            FacilityText(actions, BasePersonnel.LeaderName(leader) + " · 默认带兵", true);
            FacilityText(actions, "从右侧队长名册切换。预设人数在备战时按实际兵力配置。");
            foreach (BaseTroopKind kind in Enum.GetValues(typeof(BaseTroopKind)))
            {
                int count = leader.DefaultTroops.Count(kind);
                bool available = !leader.Away && !state.Campaign.HasActiveBattle;
                var counter = FacilityCounter(actions, kind.ToString(), BaseEconomy.TroopName(kind) + "  " + count,
                    () => SetFacilityCommanderTroops(leader.Id, kind, count - 1), () => SetFacilityCommanderTroops(leader.Id, kind, count + 1),
                    available && count > 0, available && count < 30);
                counter.Find("Unlock").gameObject.SetActive(false);
            }

            FacilityText(actions, "默认策略与范围 · 驻守 / 威胁 / 进攻", true);
            var fields = new InputField[9];
            var strategies = new[] { SquadStrategy.Obey, SquadStrategy.Attack, SquadStrategy.Protect };
            for (int i = 0; i < 3; i++)
            {
                int slot = i;
                var config = CloneSceneUi("FacilityRangeRow", actions);
                config.name = "Slot" + (char)('A' + slot);
                Ui<Text>(config, "Label").text = (char)('A' + slot) + " 位";
                var strategy = BaseSceneRules.Tier((SquadStrategy)state.Muster.Strategies[slot]);
                var toggle = Ui<Button>(config, "Strategy");
                toggle.GetComponentInChildren<Text>().text = BattleSimulation.StrategyName(strategy);
                toggle.interactable = !state.Campaign.HasActiveBattle;
                toggle.onClick.AddListener(() =>
                {
                    if (owner.Busy) return;
                    FacilityChange(next =>
                    {
                        if (next.Campaign.HasActiveBattle) throw new InvalidOperationException("战斗中不能修改默认策略");
                        next.Muster.Strategies[slot] = (int)strategies[(Array.IndexOf(strategies, strategy) + 1) % strategies.Length];
                    }, "已保存默认策略", OpenBarracks);
                });
                var names = new[] { "Guard", "Threat", "Attack" };
                for (int j = 0; j < names.Length; j++)
                {
                    int index = slot * 3 + j;
                    fields[index] = Ui<InputField>(config, names[j]);
                    fields[index].SetTextWithoutNotify(state.Muster.Ranges[index].ToString());
                    fields[index].interactable = !state.Campaign.HasActiveBattle;
                }
            }
            FacilityButton(actions, "SaveRanges", "保存位次范围", () => FacilityChange(next =>
            {
                var values = fields.Select(field => int.TryParse(field.text, out int value) ? value : 0).ToArray();
                for (int slot = 0; slot < 3; slot++) BaseMuster.SetRanges(next, slot, values[slot * 3], values[slot * 3 + 1], values[slot * 3 + 2]);
            }, "已保存位次范围", OpenBarracks), !state.Campaign.HasActiveBattle);
            FacilityButton(actions, "Rules", "兵营规则", () => FacilityRules(OpenBarracks));

            if (detail != null)
            {
                FillBarracksDetail(filters, list, state, detail);
                FinishFacilityPanel();
                return;
            }
            FacilityButton(filters, "All", "全部", () => { facilityRosterKind = null; OpenBarracks(); }, selected: facilityRosterKind == null);
            foreach (var kind in new[] { SoldierKind.Hero, SoldierKind.Melee, SoldierKind.Ranged, SoldierKind.Cavalry })
                FacilityButton(filters, kind.ToString(), BattleSimulation.KindName(kind),
                    () => { facilityRosterKind = kind; OpenBarracks(); }, selected: facilityRosterKind == kind);
            string stamp = BaseMuster.Snapshot(state);
            foreach (var unit in personnel.Where(u => facilityRosterKind == null || u.Kind == facilityRosterKind))
            {
                Action details = () =>
                {
                    if (BaseMuster.Snapshot(owner.State) == stamp) facilityPersonnelDetail = unit.Key;
                    OpenBarracks();
                };
                Action select = unit.IsLeader ? () => { compositionLeader = unit.LeaderId; OpenBarracks(); } : details;
                AddPersonnelRow(list, unit, unit.State, select, selected: unit.LeaderId == leader.Id,
                    useCurrentStats: true, details: unit.IsLeader ? details : null);
            }
            if (list.childCount == 0) FacilityText(list, "暂无符合条件的人员。");
            FinishFacilityPanel();
        }

        private void FillBarracksDetail(Transform filters, Transform list, BaseState state, BasePersonnelUnit unit)
        {
            FacilityButton(filters, "Back", "返回名册", () => { facilityPersonnelDetail = null; OpenBarracks(); });
            FacilityText(list, unit.Name + " · " + BattleSimulation.KindName(unit.Kind), true);
            FacilityText(list, $"生命 {unit.Health:0.#}/{unit.MaxHealth:0.#} · {unit.State}");
            FacilityText(list, $"当前攻击 {unit.Current.Damage:0.##} · 防御 {unit.Current.Defense:0.##}");
            FacilityText(list, $"基础攻击 {unit.Basic.Damage:0.##} · 防御 {unit.Basic.Defense:0.##}");
            FacilityText(list, unit.Facility + " · " + (unit.Locked ? "手动锁定" : "自动安排"));
            string stamp = BaseMuster.Snapshot(state);
            FacilityButton(list, "CompleteDetail", "完整属性", () => OpenPersonnelDetail(unit.Key, stamp));
        }

        private void SetFacilityCommanderTroops(string leaderId, BaseTroopKind kind, int count) => FacilityChange(next =>
        {
            var leader = next.Leaders.Single(x => x.Id == leaderId);
            if (leader.Away || next.Campaign.HasActiveBattle) throw new InvalidOperationException("队长外出期间保留冻结编队，请返营后修改");
            leader.DefaultTroops.Set(kind, count);
        }, "已保存队长预设，未抽调人员", OpenBarracks);

        private void OpenHomeFacility()
        {
            activeObject = interactionObject = "home";
            var state = owner.State;
            int day = state.Day;
            var preview = BaseEconomy.Preview(state);
            var raids = BaseCampaign.Due(state).ToArray();
            string restProblem = BaseDemoFlow.RestProblem(state);
            var (actions, filters, list) = OpenFacilityPanel("facility:home", "营帐", "休息与管理", FacilityCopy("forecast", "日结预览"));
            FacilityText(filters, $"第{day}天 → 第{day + 1}天", true);
            FacilityButton(actions, "Rest", "休息至明天", () => owner.Rest(day), restProblem == "" || BaseDemoDefense.AnyDue(state), true);
            if (restProblem != "" && raids.Length == 0) FacilityText(actions, restProblem);
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

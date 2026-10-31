using BaseConquest.Presentation;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace BaseConquest.Battle
{
    // 原生触摸按钮负责 UI，单位选择／指令与回合推进仍由控制器和模拟器管理。
    public sealed class BattleHud
    {
        private readonly BattleMobileHud mobile;
        public BaseConquest.Base.BaseJoystick Joystick => mobile?.Joystick;
        public bool ReportVisible => mobile?.ReportVisible == true;
        public bool ContextMenuVisible => mobile != null ? mobile.ContextMenuVisible : menu != null && menu.gameObject.activeSelf;
        public void CloseReport() => mobile?.CloseReport();
        public void ShowRealtimeCandidates(Vector2 point,List<BattleSoldier> units,Action<BattleSoldier> choose)=>mobile?.ShowRealtimeCandidates(point,units,choose);
        public readonly GameObject Root;
        public readonly Canvas Canvas;
        private readonly BattleSceneController owner;
        private readonly Font font;
        private readonly RectTransform safe;
        private readonly CanvasScaler scaler;
        private readonly Button automatic, fast, skip, step, cancel, drawer, quick, sideSwitch, formation, retreat;
        private readonly RectTransform formationPanel;
        private readonly Text formationInfo;
        private readonly Text[] loadoutLabels = new Text[3];
        private readonly Text status, target, menuLabel;
        private readonly RectTransform minimap;
        private readonly RectTransform menu;
        private readonly List<RectTransform> hitAreas = new List<RectTransform>();
        private readonly HeroSlot[] heroes = new HeroSlot[3];
        private readonly ArmyColumn[] armies = new ArmyColumn[2];
        private readonly Color panel = new Color(0.055f, 0.08f, 0.12f, 0.93f);
        private readonly Color active = new Color(0.2f, 0.45f, 0.62f, 0.98f);
        private sealed class HeroSlot { public RectTransform Root; public Button Portrait, Skill, Ultimate; public Text Name, Health; public RectTransform Hp; public int Id = -1; }
        private sealed class ArmyColumn { public RectTransform Root; public Text Header; public readonly Button[] Cards = new Button[4]; public Button Fill, Withdraw, Command; }

        public BattleHud(BattleSceneController owner, Transform parent, Font font)
        {
            this.owner = owner; this.font = font;
            if (owner.PreviewSimulation.ModernRules) { mobile = new BattleMobileHud(owner, parent, font); Root = mobile.Root; Canvas = mobile.Canvas; return; }
            Root = UiPrefabBinding.Create("BattleClassic", "BattleHud", parent, true);
            Canvas = Root.GetComponent<Canvas>(); Canvas.renderMode = RenderMode.ScreenSpaceOverlay; Canvas.sortingOrder = 50;
            scaler = Root.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            safe = Rect("SafeArea", Root.transform, Vector2.zero, Vector2.zero, Vector2.zero); safe.anchorMin = Vector2.zero; safe.anchorMax = Vector2.one;
            minimap = Box("MinimapPlaceholder", safe, new Vector2(0, 1), new Vector2(12, -12), new Vector2(76, 52));
            Label("小地图\n占位", minimap, 12);
            drawer = MakeButton("测试", safe, new Vector2(0, 1), new Vector2(12, -70), new Vector2(76, 44), owner.ToggleDrawer);
            quick = MakeButton("快速选择", safe, new Vector2(0, 1), new Vector2(94, -70), new Vector2(88, 44), owner.ToggleQuickSelection);
            sideSwitch = MakeButton("切换阵营", safe, new Vector2(0, 1), new Vector2(94, -12), new Vector2(124, 48), owner.SwitchSide);
            formation = MakeButton("编队", safe, new Vector2(0, 1), new Vector2(224, -12), new Vector2(66, 48), owner.ToggleFormation);
            RectTransform objective = Box("ObjectivePlaceholder", safe, new Vector2(0.5f, 1), new Vector2(0, -12), new Vector2(230, 32)); target = Label("目标进度 · 占位", objective, 13);
            automatic = MakeButton("自动", safe, new Vector2(1, 1), new Vector2(-128, -12), new Vector2(52, 48), owner.ToggleAutomatic);
            fast = MakeButton("3 倍", safe, new Vector2(1, 1), new Vector2(-70, -12), new Vector2(52, 48), owner.ToggleFast);
            skip = MakeButton("跳过", safe, new Vector2(1, 1), new Vector2(-12, -12), new Vector2(52, 48), owner.SkipBattle);
            step = MakeButton("下一轮", safe, new Vector2(1, 0), new Vector2(-116, 12), new Vector2(86, 52), owner.AdvanceOne);
            cancel = MakeButton("取消下令", safe, new Vector2(1, 0), new Vector2(-12, 12), new Vector2(98, 52), owner.CancelAllOrders);
            retreat = MakeButton("全军撤退", safe, new Vector2(1, 0), new Vector2(-12, 70), new Vector2(98, 42), owner.RetreatAll);
            foreach (BattleSide side in Enum.GetValues(typeof(BattleSide)))
            {
                var column = new ArmyColumn(); armies[(int)side] = column;
                Vector2 anchor = new Vector2((int)side, 1);
                column.Root = Rect(side + "ArmyColumn", safe, anchor, new Vector2(side == BattleSide.A ? 12 : -12, -124), new Vector2(126, 374));
                column.Root.pivot = new Vector2((int)side, 1);
                var heading = Box("ArmyHeading", column.Root, new Vector2(0, 1), Vector2.zero, new Vector2(126, 26)); column.Header = Label("", heading, 12);
                SoldierKind[] kinds = { SoldierKind.Melee, SoldierKind.Ranged, SoldierKind.Cavalry, SoldierKind.Hero };
                for (int i = 0; i < kinds.Length; i++)
                {
                    SoldierKind kind = kinds[i];
                    var card = MakeButton("", column.Root, new Vector2(0, 1), new Vector2(0, -32 - i * 62), new Vector2(126, 56), () => { if (owner.ControlledSide == side) owner.ChooseDeployment(kind); });
                    card.name = "Deploy_" + side + "_" + kind; card.GetComponentInChildren<Text>().fontSize = 12; column.Cards[i] = card;
                }
                column.Fill = MakeButton("一键布兵", column.Root, new Vector2(0, 1), new Vector2(0, -282), new Vector2(126, 40), owner.AutoDeployControlled);
                column.Fill.name = "Fill_" + side;
                column.Withdraw = MakeButton("撤回所选", column.Root, new Vector2(0, 1), new Vector2(0, -330), new Vector2(126, 40), owner.WithdrawSelected); column.Withdraw.name = "Withdraw_" + side;
                column.Command = MakeButton("指挥", column.Root, new Vector2(0, 1), new Vector2(0, -330), new Vector2(126, 40), owner.EndDeployment); column.Command.name = "Command_" + side;
            }
            for (int i = 0; i < heroes.Length; i++)
            {
                int index = i; var slot = new HeroSlot(); heroes[i] = slot;
                slot.Root = Rect("HeroSlot" + i, safe, Vector2.zero, new Vector2(12 + i * 126, 12), new Vector2(118, 98));
                slot.Portrait = MakeButton("", slot.Root, Vector2.zero, Vector2.zero, new Vector2(60, 82), () => { if (owner.PreviewSimulation.Scenario.CommanderControl) owner.SelectCommanderSlot(index); else if (slot.Id >= 0) owner.ToggleHero(slot.Id); });
                slot.Name = Label("英雄 " + (i + 1), slot.Portrait.transform, 14); slot.Name.rectTransform.anchorMin = new Vector2(0, 0.6f); slot.Name.rectTransform.anchorMax = Vector2.one;
                slot.Health = Label("空位", slot.Portrait.transform, 12); slot.Health.rectTransform.anchorMin = new Vector2(0, 0.32f); slot.Health.rectTransform.anchorMax = new Vector2(1, 0.6f);
                var track = Rect("HpTrack", slot.Portrait.transform, Vector2.zero, new Vector2(4, 22), new Vector2(52, 4)); track.gameObject.GetOrAdd<Image>().color = Color.gray;
                slot.Hp = Rect("Hp", track, Vector2.zero, Vector2.zero, new Vector2(52, 4)); slot.Hp.gameObject.GetOrAdd<Image>().color = new Color(0.3f, 0.8f, 0.6f);
                Text energy = Label("充能占位", slot.Portrait.transform, 11); energy.rectTransform.anchorMin = Vector2.zero; energy.rectTransform.anchorMax = new Vector2(1, 0.22f);
                slot.Skill = MakeButton("战技", slot.Root, Vector2.zero, new Vector2(66, 50), new Vector2(48, 48), null);
                slot.Ultimate = MakeButton("绝招", slot.Root, Vector2.zero, new Vector2(66, 0), new Vector2(48, 48), null);
                slot.Skill.interactable = slot.Ultimate.interactable = false;
            }
            var statusArea = Rect("BattleStatus", safe, new Vector2(0.5f, 0), new Vector2(0, 116), new Vector2(540, 44));
            var statusBackground = statusArea.gameObject.GetOrAdd<Image>(); statusBackground.color = panel; statusBackground.raycastTarget = false;
            status = Label("", statusArea, 13); status.raycastTarget = false;
            menu = Box("LongPressCommands", safe, Vector2.zero, Vector2.zero, new Vector2(260, 94));
            menuLabel = Label("所选单位", menu, 13); menuLabel.rectTransform.anchorMin = new Vector2(0, 0.66f); menuLabel.rectTransform.anchorMax = Vector2.one;
            MakeButton("前往此处", menu, Vector2.zero, new Vector2(8, 8), new Vector2(118, 48), () => owner.ConfirmContext(false));
            MakeButton("原地驻守", menu, Vector2.zero, new Vector2(134, 8), new Vector2(118, 48), () => owner.ConfirmContext(true));
            menu.gameObject.SetActive(false);
            formationPanel = Box("FormationPanel", safe, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(690, 294));
            formationInfo = Label("", formationPanel, 14); formationInfo.rectTransform.anchorMin = new Vector2(0, 0.79f); formationInfo.rectTransform.anchorMax = Vector2.one;
            for (int i = 0; i < 3; i++)
            {
                int slot = i; float y = -64 - i * 62;
                var choose = MakeButton("队 " + (i + 1), formationPanel, new Vector2(0, 1), new Vector2(10, y), new Vector2(60, 48), () => owner.SelectCommanderSlot(slot)); choose.name = "FormationSelect_" + slot;
                var textBox = Rect("Allocation" + i, formationPanel, new Vector2(0, 1), new Vector2(74, y), new Vector2(208, 48));
                loadoutLabels[i] = Label("", textBox, 13);
                for (int k = 0; k < 3; k++)
                {
                    SoldierKind kind = BattleSimulation.TroopKinds[k];
                    var minus = MakeButton("−", formationPanel, new Vector2(0, 1), new Vector2(290 + k * 124, y), new Vector2(48, 48), () => owner.ChangeAllocation(slot, kind, -1)); minus.name = "Allocation_" + slot + "_" + kind + "_Minus";
                    var plus = MakeButton("+", formationPanel, new Vector2(0, 1), new Vector2(342 + k * 124, y), new Vector2(48, 48), () => owner.ChangeAllocation(slot, kind, 1)); plus.name = "Allocation_" + slot + "_" + kind + "_Plus";
                }
            }
            MakeButton("均分混编", formationPanel, Vector2.zero, new Vector2(10, 8), new Vector2(116, 42), owner.DistributeFormation);
            MakeButton("队长 −", formationPanel, Vector2.zero, new Vector2(134, 8), new Vector2(92, 42), () => owner.ChangeCommanderCount(-1));
            MakeButton("队长 +", formationPanel, Vector2.zero, new Vector2(234, 8), new Vector2(92, 42), () => owner.ChangeCommanderCount(1));
            MakeButton("完成编队", formationPanel, Vector2.zero, new Vector2(552, 8), new Vector2(128, 42), owner.ToggleFormation);
            formationPanel.gameObject.SetActive(false);
            if (Application.isPlaying && EventSystem.current == null)
            {
                var events = new GameObject("BattleEventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule)); events.transform.SetParent(Root.transform, false);
                events.GetComponent<InputSystemUIInputModule>().AssignDefaultActions();
            }
            UiPrefabBinding.Complete(Root);
            Refresh();
        }
        private RectTransform Rect(string name, Transform parent, Vector2 anchor, Vector2 position, Vector2 size)
        {
            var rect = UiPrefabBinding.Rect(name, parent);
            rect.anchorMin = rect.anchorMax = rect.pivot = anchor; rect.anchoredPosition = position; rect.sizeDelta = size; return rect;
        }
        private RectTransform Box(string name, Transform parent, Vector2 anchor, Vector2 position, Vector2 size)
        {
            var rect = Rect(name, parent, anchor, position, size); rect.gameObject.GetOrAdd<Image>().color = panel; hitAreas.Add(rect); return rect;
        }
        private Text Label(string text, Transform parent, int size)
        {
            var rect = Rect("Label", parent, Vector2.zero, Vector2.zero, Vector2.zero); rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero;
            Text label = rect.gameObject.GetOrAdd<Text>(); label.font = font; label.fontSize = size; label.color = Color.white; label.alignment = TextAnchor.MiddleCenter; label.text = text; label.raycastTarget = false; return label;
        }
        private Button MakeButton(string text, Transform parent, Vector2 anchor, Vector2 position, Vector2 size, Action action)
        {
            var rect = Box(text, parent, anchor, position, size); var button = rect.gameObject.GetOrAdd<Button>(); button.targetGraphic = rect.GetComponent<Image>();
            var colors = button.colors; colors.normalColor = Color.white; colors.highlightedColor = new Color(0.8f, 0.9f, 1); colors.disabledColor = new Color(0.5f, 0.5f, 0.5f, 1); button.colors = colors;
            Label(text, rect, 15); if (action != null) button.onClick.AddListener(() => action()); return button;
        }
        private static void ButtonText(Button button, string text) => button.GetComponentInChildren<Text>().text = text;
        public IEnumerable<int> PortraitSlotsBetween(Vector2 from, Vector2 to) => mobile != null ? mobile.PortraitSlotsBetween(from, to) : Enumerable.Empty<int>();
        public bool BlocksPointer(Vector2 point)
        {
            if (mobile != null) return mobile.BlocksPointer(point);
            return hitAreas.Any(r => r.gameObject.activeInHierarchy && RectTransformUtility.RectangleContainsScreenPoint(r, point, Canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : Canvas.worldCamera));
        }
        public void Refresh()
        {
            if (mobile != null) { mobile.Refresh(); return; }
            if (owner.PreviewSimulation == null) return;
            scaler.scaleFactor = owner.HudScale;
            Rect area = Screen.safeArea; if (area.width <= 0 || area.height <= 0) area = new Rect(0, 0, Screen.width, Screen.height);
            safe.anchorMin = new Vector2(area.xMin / Mathf.Max(1, Screen.width), area.yMin / Mathf.Max(1, Screen.height)); safe.anchorMax = new Vector2(area.xMax / Mathf.Max(1, Screen.width), area.yMax / Mathf.Max(1, Screen.height)); safe.offsetMin = safe.offsetMax = Vector2.zero;
            var battle = owner.PreviewSimulation; bool fighting = battle.Phase == BattlePhase.Fighting;
            automatic.interactable = fighting && !owner.Skipping; fast.interactable = !owner.Skipping;
            automatic.GetComponent<Image>().color = owner.Automatic ? active : panel; fast.GetComponent<Image>().color = owner.FastMode ? active : panel;
            skip.interactable = fighting; ButtonText(skip, owner.Skipping ? "停止" : "跳过");
            step.interactable = !owner.IsAnimating && !owner.Skipping; ButtonText(step, battle.Phase == BattlePhase.Deployment ? "开战" : battle.Phase == BattlePhase.Finished ? "重开" : "下一轮");
            cancel.interactable = fighting && !owner.IsAnimating && !owner.Skipping;
            retreat.gameObject.SetActive(fighting && battle.Scenario.CommanderControl); retreat.interactable = !owner.IsAnimating && !owner.Skipping;
            ButtonText(drawer, owner.DrawerVisible ? "收起测试" : "测试");
            formation.gameObject.SetActive(battle.Scenario.CommanderControl && !owner.DrawerVisible);
            formation.interactable = battle.Phase == BattlePhase.Deployment && !owner.IsAnimating && !owner.Skipping;
            formationPanel.gameObject.SetActive(owner.FormationVisible && battle.Phase == BattlePhase.Deployment);
            if (owner.FormationVisible)
            {
                var army = battle.Scenario.Army(owner.ControlledSide);
                formationInfo.text = BattleSimulation.SideName(owner.ControlledSide) + " · 每队最多6兵（测试） · 当前部署队 " + (owner.DeploymentCommanderSlot + 1) + "\n未分配 近战" + battle.Unassigned(owner.ControlledSide, SoldierKind.Melee) + " / 远程" + battle.Unassigned(owner.ControlledSide, SoldierKind.Ranged) + " / 骑兵" + battle.Unassigned(owner.ControlledSide, SoldierKind.Cavalry);
                for (int i = 0; i < 3; i++)
                {
                    var c = army.Commanders[i]; loadoutLabels[i].text = i >= army.HeroCount ? "未启用小队长" : "近战 " + c.Melee + "    远程 " + c.Ranged + "    骑兵 " + c.Cavalry + "\n总兵力 " + c.Total + " / 6";
                    foreach (var button in formationPanel.GetComponentsInChildren<Button>())
                        if (button.name.StartsWith("Allocation_" + i + "_") || button.name == "FormationSelect_" + i) button.interactable = i < army.HeroCount;
                }
                float logicalWidth = area.width / owner.HudScale; formationPanel.localScale = Vector3.one * Mathf.Min(1, (logicalWidth - 24) / 690);
                formationPanel.SetAsLastSibling();
            }
            quick.gameObject.SetActive(!owner.DrawerVisible); quick.interactable = fighting && !owner.Skipping;
            quick.GetComponent<Image>().color = owner.QuickSelection ? active : panel;
            drawer.GetComponent<RectTransform>().anchoredPosition = owner.DrawerVisible ? new Vector2(262, -12) : new Vector2(12, -70);
            sideSwitch.gameObject.SetActive(!owner.DrawerVisible); sideSwitch.interactable = !owner.IsAnimating && !owner.Skipping;
            ButtonText(sideSwitch, BattleSimulation.SideName(owner.ControlledSide) + "操作\n切换阵营·测试"); sideSwitch.GetComponentInChildren<Text>().fontSize = 12;
            minimap.gameObject.SetActive(!owner.DrawerVisible); target.transform.parent.gameObject.SetActive(!owner.DrawerVisible); status.transform.parent.gameObject.SetActive(!owner.DrawerVisible);
            BattleSoldier primary = owner.SelectedUnits.Count > 0 ? battle.Soldier(owner.SelectedUnits[0]) : null;
            string selected = primary == null ? "未选单位" : "已选 " + owner.SelectedUnits.Count + " 人 · #" + (primary.Id + 1) + (primary.IsTransit ? " 通行（禁攻）" : " 战斗") + (primary.FocusTargetId >= 0 ? " · 锁定 #" + (primary.FocusTargetId + 1) : "");
            if (battle.Scenario.CommanderControl) selected = primary == null ? "未选小队长" : "已选 " + owner.SelectedUnits.Count + " 队 · " + battle.Squads[primary.SquadId].Name + " · 在场" + battle.Squads[primary.SquadId].ActiveCount;
            string detail = primary?.Command == SquadCommand.Focus && owner.PreviewOrders.Current != null ? owner.PreviewOrders.Current.Status : owner.Feedback;
            status.text = BattleSimulation.SideName(owner.ControlledSide) + " · 第 " + battle.RoundNumber + " 轮 · " + (owner.Automatic ? "自动" : "暂停") + " · " + (owner.FastMode ? "3" : "1") + " 倍 · " + selected + "\n" + detail;
            if (owner.DeploymentArmed && battle.Phase != BattlePhase.Finished) status.text = BattleSimulation.SideName(owner.ControlledSide) + " · 部署" + BattleSimulation.KindName(owner.DeploymentKind) + " · 点部署格／拖动下兵\n" + owner.Feedback;
            if (battle.Phase == BattlePhase.Finished) status.text = "战斗结束 · " + BattleSceneController.OutcomeName(battle.Report.Outcome) + " · " + battle.RoundNumber + " 轮\n点右下重开返回准备";
            target.text = battle.Phase == BattlePhase.Finished ? BattleSceneController.OutcomeName(battle.Report.Outcome) : "目标进度 · 占位";
            foreach (BattleSide side in Enum.GetValues(typeof(BattleSide)))
            {
                var column = armies[(int)side]; bool controlled = side == owner.ControlledSide;
                column.Root.gameObject.SetActive(!owner.DrawerVisible);
                column.Header.text = BattleSimulation.SideName(side) + (controlled ? battle.Scenario.CommanderControl && battle.Phase == BattlePhase.Deployment ? " · 部署队" + (owner.DeploymentCommanderSlot + 1) : " · 场 / 损 / 备" : " · 已知信息");
                SoldierKind[] kinds = { SoldierKind.Melee, SoldierKind.Ranged, SoldierKind.Cavalry, SoldierKind.Hero };
                for (int i = 0; i < kinds.Length; i++)
                {
                    SoldierKind kind = kinds[i]; ArmyStatus info = battle.Vision.Status(owner.ControlledSide, side, kind); Button card = column.Cards[i];
                    ButtonText(card, BattleSimulation.KindName(kind) + "\n" + (info.Exact ? "场" + info.InField + " 亡" + info.Defeated + " 库" + info.Stock : "见" + info.InField + " 亡≥" + info.Defeated + " 库?\n累计目击 " + info.SeenTotal));
                    if (controlled && battle.Scenario.CommanderControl) ButtonText(card, card.GetComponentInChildren<Text>().text + "\n队" + (owner.DeploymentCommanderSlot + 1) + "余" + battle.CommanderRemaining(side, owner.DeploymentCommanderSlot, kind));
                    card.interactable = controlled && (!battle.Scenario.CommanderControl || battle.CommanderRemaining(side, owner.DeploymentCommanderSlot, kind) > 0 && (kind == SoldierKind.Hero || battle.Commander(side, owner.DeploymentCommanderSlot)?.IsActive == true)) && info.Stock > 0 && battle.Phase != BattlePhase.Finished && !owner.IsAnimating && !owner.Skipping;
                    card.GetComponent<Image>().color = controlled && owner.DeploymentArmed && kind == owner.DeploymentKind ? active : panel;
                }
                column.Fill.gameObject.SetActive(controlled && battle.Phase != BattlePhase.Finished);
                column.Fill.interactable = !owner.IsAnimating && !owner.Skipping && kinds.Any(k => battle.Remaining(side, k) > 0);
                column.Withdraw.gameObject.SetActive(controlled && battle.Phase == BattlePhase.Deployment && !owner.DeploymentArmed);
                column.Withdraw.interactable = owner.SelectedUnits.Count > 0;
                column.Command.gameObject.SetActive(controlled && owner.DeploymentArmed && battle.Phase != BattlePhase.Finished);
                column.Command.interactable = !owner.IsAnimating && !owner.Skipping;
            }
            var members = battle.Soldiers.Where(s => s.Side == owner.ControlledSide && s.Kind == SoldierKind.Hero).OrderBy(s => s.Id).Take(3).ToList();
            for (int i = 0; i < heroes.Length; i++)
            {
                var slot = heroes[i]; var hero = battle.Scenario.CommanderControl ? battle.Commander(owner.ControlledSide, i) : i < members.Count ? members[i] : null; slot.Id = hero?.Id ?? -1;
                slot.Root.gameObject.SetActive(!owner.DrawerVisible);
                slot.Portrait.interactable = !owner.IsAnimating && !owner.Skipping && (battle.Scenario.CommanderControl && battle.Phase == BattlePhase.Deployment ? i < battle.Scenario.Army(owner.ControlledSide).HeroCount : hero != null && hero.IsActive);
                slot.Name.text = hero == null ? "英雄 " + (i + 1) : "英 " + (i + 1) + (hero.IsTransit ? "\n通行" : hero.Escaped ? "\n撤离" : !hero.IsAlive ? "\n击倒" : "");
                if (battle.Scenario.CommanderControl) slot.Name.text = "队长" + (i + 1) + (hero == null ? "" : hero.Escaped ? "\n撤离" : !hero.IsAlive ? "\n失能" : hero.IsTransit ? "\n通行" : "");
                slot.Health.text = hero == null ? "待部署" : hero.Health.ToString("F0") + "/" + hero.Stats.Health.ToString("F0");
                slot.Hp.sizeDelta = new Vector2(hero == null ? 0 : 52 * Mathf.Clamp01(hero.Health / hero.Stats.Health), 4);
                slot.Portrait.GetComponent<Image>().color = hero != null && owner.SelectedUnits.Contains(hero.Id) || battle.Scenario.CommanderControl && battle.Phase == BattlePhase.Deployment && owner.DeploymentCommanderSlot == i ? active : panel;
            }
            // 狭窄预览窗口换行；横屏手机保留三组紧凑状态区与右下回合按钮。
            float width = area.width / owner.HudScale;
            status.rectTransform.parent.GetComponent<RectTransform>().sizeDelta = new Vector2(Mathf.Min(540, width - 24), 44);
            status.rectTransform.parent.GetComponent<RectTransform>().anchoredPosition = new Vector2(0, width < 610 ? 224 : 116);
            for (int i = 0; i < heroes.Length; i++) heroes[i].Root.anchoredPosition = new Vector2(12 + (width < 610 ? i % 2 : i) * 126, 12 + (width < 610 ? i / 2 : 0) * 104);
            target.rectTransform.parent.GetComponent<RectTransform>().sizeDelta = new Vector2(Mathf.Clamp(width - 290, 80, 230), 32);
        }
        public void ShowMenu(Vector2 screenPoint)
        {
            if (mobile != null) { mobile.ShowMenu(screenPoint); return; }
            Canvas.ForceUpdateCanvases();
            RectTransformUtility.ScreenPointToLocalPointInRectangle(safe, screenPoint, Canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : Canvas.worldCamera, out var local);
            Vector2 fromBottomLeft = local - safe.rect.min; menu.anchoredPosition = new Vector2(Mathf.Clamp(fromBottomLeft.x - 130, 4, Mathf.Max(4, safe.rect.width - 264)), Mathf.Clamp(fromBottomLeft.y + 18, 4, Mathf.Max(4, safe.rect.height - 98)));
            menuLabel.text = "已选 " + owner.SelectedUnits.Count + " 人"; menu.gameObject.SetActive(true);
        }
        public void CloseMenu() { if (mobile != null) { mobile.CloseMenu(); return; } if (menu != null) menu.gameObject.SetActive(false); }
        public void Dispose() { if (Root != null) { Root.SetActive(false); BattleBoardView.DestroyObject(Root); } }
    }
}

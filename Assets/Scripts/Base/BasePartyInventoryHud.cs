using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using BaseConquest.Battle;
using BaseConquest.Presentation;

namespace BaseConquest.Base
{
    public sealed partial class BaseHud
    {
        private const int WarehouseSlots = 24;
        private bool inventoryWarehouse;
        private readonly InventoryClickTracker inventoryClicks = new InventoryClickTracker();
        private string inventoryPerson = "", inventorySelected = "";
        private int inventoryQuantity = 1, warehousePage;
        private RectTransform inventorySelectedCell, inventoryChoice;
        private readonly Dictionary<string, string> inventoryFilters = new Dictionary<string, string>();
        private static readonly string[] InventoryCategories =
            { "all", "weapon", "armor", "accessory", "consumable", "quest", "material", "exclusive" };

        private string InventoryFilter(string holder) =>
            inventoryFilters.TryGetValue(holder, out var value) ? value : "all";

        public void OpenWarehouse()
        {
            if (BasePartyInventory.AtWarehouse(owner.State)) OpenInventory(true);
            else owner.ReportMessage("请接近基地仓库交互");
        }

        private void OpenInventory(bool warehouse)
        {
            var state = owner.State;
            if (BaseDemoIntro.Daily(state) && state.Demo.Intro.Step == 0) { OpenHeroChoice(); return; }
            if (state == null || !BasePartyInventory.Peaceful(state))
            {
                owner.ReportMessage("战斗中不能打开背包");
                return;
            }
            if (warehouse && BaseBattleLoot.Pending(state)) { OpenLoot(); return; }
            if (warehouse && !BasePartyInventory.AtWarehouse(state))
            {
                owner.ReportMessage("请接近基地仓库");
                return;
            }

            var people = BasePartyInventory.People(state, warehouse)
                .Where(person => person.PersonalEquipment?.Version == 1).ToArray();
            if (people.Length == 0) return;
            if (!people.Any(person => person.Id == inventoryPerson)) inventoryPerson = people[0].Id;
            string viewKey = warehouse ? "party-warehouse" : "party-inventory";
            if (!PanelOpen || panelViewKey != viewKey)
            {
                inventoryClicks.Reset();
                inventorySelected = "";
                inventoryQuantity = 1;
            }
            inventoryWarehouse = warehouse;
            inventorySelectedCell = null;
            questSwordTarget = null;
            inventoryChoice = null;
            OpenScenePanel(warehouse ? "PartyWarehouse" : "PartyInventory", viewKey);
            activeObject = warehouse ? "warehouse" : "backpack";
            interactionObject = warehouse ? BasePartyInventory.WarehouseObject(state).Id : "";
            root.GetComponent<Canvas>().sortingOrder = 100;
            InventoryButton(panel, "Close", null, ClosePanel);

            var entries = BasePartyInventory.Entries(state).ToArray();
            if (warehouse) BuildWarehouseInventory(people, entries);
            else
            {
                var cards = Ui<RectTransform>(panel, "Cards");
                var frame = Ui<RectTransform>(panel, "FrameWidths/" + Mathf.Clamp(people.Length, 1, 3));
                panel.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, frame.rect.width);
                foreach (var person in people)
                    BuildInventoryCard(cards, person, entries, inventorySelected, true, null);
            }
            if (inventorySelectedCell != null) ShowInventoryItemMenu();
            if (BaseDemoIntro.Daily(state)) state.Demo.Intro.BackpackOpened = true;
            RefreshSummary();
        }

        private void BuildWarehouseInventory(BaseLeader[] people, BaseInventoryEntry[] entries)
        {
            var tabs = Ui<RectTransform>(panel, "Tabs");
            BuildInventoryPersonTabs(tabs, people, inventoryPerson, person =>
            {
                inventoryClicks.Reset();
                inventoryPerson = person.Id;
                inventorySelected = "";
                OpenInventory(true);
            });
            var recipient = people.Single(person => person.Id == inventoryPerson);
            BuildInventoryCard(Ui<RectTransform>(panel, "Card"), recipient, entries, inventorySelected, true, null);

            InventoryButton(panel, "Filter", BaseBackpack.CategoryName(InventoryFilter("")) + " ▾", () => InventoryPopup(""));
            InventoryButton(panel, "Sort", null, () => InventoryChange(state => BasePartyInventory.Sort(state, ""), "已整理物品"));
            var stored = entries.Where(entry => !entry.Worn && entry.Holder == "").ToArray();
            int pages = Math.Max(1, (stored.Length + WarehouseSlots - 1) / WarehouseSlots);
            warehousePage = Mathf.Clamp(warehousePage, 0, pages - 1);
            var grid = Ui<RectTransform>(panel, "Grid");
            for (int index = 0; index < WarehouseSlots; index++)
            {
                int absolute = warehousePage * WarehouseSlots + index;
                var slot = CloneSceneUi("WarehouseSlot", grid).GetComponent<Button>();
                BindInventoryCell(slot, absolute < stored.Length ? stored[absolute] : null, "", absolute, "", inventorySelected, true, null);
            }
            InventoryButton(panel, "Prev", null, () => { inventoryClicks.Reset(); warehousePage--; OpenInventory(true); }, warehousePage > 0);
            InventoryButton(panel, "Next", null, () => { inventoryClicks.Reset(); warehousePage++; OpenInventory(true); }, warehousePage + 1 < pages);
            Ui<Text>(panel, "Page").text = (warehousePage + 1) + " / " + pages;
        }

        private void BuildInventoryPersonTabs(RectTransform parent, IEnumerable<BaseLeader> people, string selectedId,
            Action<BaseLeader> select)
        {
            foreach (var person in people)
            {
                var option = CloneSceneUi("InventoryOption", parent);
                InventoryButton(option, "", (person.Id == selectedId ? "● " : "") + person.Name,
                    () => { if (!owner.Busy) select(person); }, person.Id != selectedId);
            }
        }

        private RectTransform BuildTradeInventoryCard(RectTransform parent, BaseLeader person, string selectedItemId,
            Action<BaseInventoryEntry> select)
        {
            var entries = BasePartyInventory.Entries(owner.State).ToArray();
            return BuildInventoryCard(parent, person, entries, selectedItemId, false, select);
        }

        private RectTransform BuildInventoryCard(RectTransform parent, BaseLeader person, BaseInventoryEntry[] entries,
            string selectedItemId, bool editable, Action<BaseInventoryEntry> select)
        {
            var card = CloneSceneUi("PartyMemberCard", parent);
            card.name = "人物与背包_" + person.Id;
            var title = InventoryButton(card, "Name", BasePersonnel.LeaderName(person) + " · " + BasePersonalEquipment.ClassName(person),
                editable ? (Action)(() => { inventoryPerson = person.Id; inventorySelected = ""; OpenInventory(inventoryWarehouse); }) : null);
            if (editable) BindInventoryDrop(title.gameObject, null, person.Id, -2, "");
            Ui<Text>(card, "Vitals").text = "生命 " + person.Health.ToString("0.#") + " / " + person.MaxHealth.ToString("0.#");

            var held = entries.Where(entry => entry.Holder == person.Id).ToArray();
            Ui<Text>(card, "Capacity").text = "背包 " + held.Count(entry => !entry.Worn) + "/" + BasePartyInventory.Capacity;
            InventoryButton(card, "Filter", BaseBackpack.CategoryName(InventoryFilter(person.Id)) + " ▾",
                editable ? (Action)(() => InventoryPopup(person.Id)) : null).gameObject.SetActive(editable);
            InventoryButton(card, "Sort", null,
                editable ? (Action)(() => InventoryChange(state => BasePartyInventory.Sort(state, person.Id), "已整理物品")) : null)
                .gameObject.SetActive(editable);
            InventoryButton(card, "Details", null,
                editable ? (Action)(() =>
                {
                    inventoryPerson = person.Id;
                    inventorySelected = "";
                    OpenPersonnelDetail("leader:" + person.Id, BaseMuster.Snapshot(owner.State), () => OpenInventory(inventoryWarehouse));
                }) : null).gameObject.SetActive(editable);

            BindInventoryPortrait(Ui<RectTransform>(card, "Portrait"), person);
            bool extra = BasePersonalEquipment.SlotsFor(person).Count() >= 6;
            for (int index = 0; index < 6; index++)
            {
                string key = "EquipmentSlot_" + index;
                string slot = BasePersonalEquipment.Slots[index];
                if (index == 5 && !extra)
                {
                    InventoryButton(card, key, BasePersonalEquipment.SlotName(slot) + "\n锁定", null);
                    continue;
                }
                BindInventoryCell(Ui<Button>(card, key), held.SingleOrDefault(entry => entry.Slot == slot),
                    person.Id, -1, slot, selectedItemId, editable, select);
            }
            for (int index = 0; index < BasePartyInventory.Capacity; index++)
            {
                BindInventoryCell(Ui<Button>(card, "BagSlot_" + index),
                    held.SingleOrDefault(entry => !entry.Worn && entry.Cell == index),
                    person.Id, index, "", selectedItemId, editable, select);
            }
            return card;
        }

        private void BindInventoryPortrait(RectTransform portrait, BaseLeader person)
        {
            bool player = person.Id == owner.State.PlayerLeaderId;
            string appearance = player ? BaseDemoIntro.PlayerArt(owner.State) : BaseCommanderIdentity.Appearance(person);
            var image = new GameObject("全身像", typeof(RectTransform));
            var art = image.GetComponent<RectTransform>();
            art.SetParent(portrait, false);
            art.anchorMin = Vector2.zero;
            art.anchorMax = Vector2.one;
            art.offsetMin = art.offsetMax = Vector2.zero;
            var atlas = PixelDemoArt.Load("Characters", appearance);
            if (atlas != null)
            {
                var raw = image.AddComponent<RawImage>();
                raw.texture = atlas;
                raw.uvRect = PixelDemoArt.Frame(HeroFacing.South, HeroPose.Idle, 0);
                raw.raycastTarget = false;
                var aspect = image.AddComponent<AspectRatioFitter>();
                aspect.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
                aspect.aspectRatio = 1;
            }
            else
            {
                var fallback = image.GetOrAdd<BattleAvatarGraphic>();
                fallback.PlayerHero = player;
                fallback.AppearanceId = appearance;
                fallback.raycastTarget = false;
            }
        }

        private void BindInventoryCell(Button button, BaseInventoryEntry entry, string holder, int index, string slot,
            string selectedItemId, bool editable, Action<BaseInventoryEntry> select)
        {
            string filter = InventoryFilter(holder);
            bool visible = entry != null && (!editable || entry.Worn || filter == "all" || filter == entry.Category);
            string title = slot != ""
                ? BasePersonalEquipment.SlotName(slot) + "\n" + (visible ? InventoryShortName(entry) : "空")
                : visible ? InventoryShortName(entry) + (entry.Count > 1 ? " ×" + entry.Count : "")
                : entry == null ? "" : "·";
            var label = button.GetComponentInChildren<Text>(true);
            label.text = title;
            if (visible)
            {
                var icon = InventoryItemIcons.Load(InventoryIconKey(entry));
                if (icon != null)
                {
                    InventoryItemIcons.Draw(button.transform, icon, new Vector2(.5f, .5f), new Vector2(0, 6), 35);
                    var caption = label.rectTransform;
                    caption.anchorMin = Vector2.zero;
                    caption.anchorMax = Vector2.right;
                    caption.offsetMin = new Vector2(2, 1);
                    caption.offsetMax = new Vector2(-2, 15);
                    label.fontSize = Mathf.Min(label.fontSize, 12);
                    label.alignment = TextAnchor.MiddleCenter;
                    label.text = InventoryShortName(entry);
                    if (entry.Count > 1)
                    {
                        var quantity = Label(button.transform, "×" + entry.Count, new Vector2(-15, -9),
                            new Vector2(29, 15), 12, TextAnchor.MiddleRight, Vector2.one);
                        quantity.color = DemoUiTheme.Current.Panel;
                        var outline = quantity.gameObject.GetOrAdd<Outline>();
                        outline.effectColor = DemoUiTheme.Current.Line;
                    }
                }
            }
            button.interactable = editable || visible && select != null;
            if (editable) button.onClick.AddListener(() => SelectInventoryCell(visible ? entry : null, holder, slot));
            else if (visible && select != null) button.onClick.AddListener(() => select(entry));

            if (visible && entry.Id == selectedItemId)
            {
                var outline = button.gameObject.GetOrAdd<Outline>();
                outline.effectColor = DemoUiTheme.Current.Copper;
                outline.effectDistance = new Vector2(2, -2);
                if (editable) inventorySelectedCell = button.GetComponent<RectTransform>();
            }
            if (visible && editable && !entry.Worn && BaseDemoIntro.Daily(owner.State) &&
                entry.Id == owner.State.Demo.Intro.PracticeId) questSwordTarget = (RectTransform)button.transform;
            if (visible && entry.Gear != null && entry.Quality >= 4) label.color = DemoUiTheme.Current.Copper;
            if (editable) BindInventoryDrop(button.gameObject, visible ? entry : null, holder, index, slot, !visible && entry != null);
            else
            {
                var cell = button.gameObject.GetOrAdd<PartyInventoryCell>();
                cell.ItemId = visible ? entry.Id : null;
                cell.Holder = holder;
                cell.Cell = index;
                cell.Slot = slot;
            }
        }

        private string InventoryIconKey(BaseInventoryEntry entry) =>
            entry == null ? null : entry.Stack?.ItemId ??
            (entry.Id == owner.State.Demo?.Intro?.PracticeId ? "practice_sword" : entry.Gear?.Template);

        private static string InventoryShortName(BaseInventoryEntry entry)
        {
            string title = entry.Name;
            if (entry.Gear != null)
            {
                int qualityEnd = title.IndexOf(']');
                if (qualityEnd >= 0 && qualityEnd + 1 < title.Length) title = title.Substring(qualityEnd + 1).TrimStart();
            }
            return title.Length > 4 ? title.Substring(0, 4) : title;
        }

        private void SelectInventoryCell(BaseInventoryEntry entry, string holder, string slot)
        {
            bool twice = inventoryClicks.Click(entry?.Id, Time.unscaledTime);
            if (twice && inventoryWarehouse && slot == "")
            {
                InventoryChange(state => BasePartyInventory.QuickTransfer(state, entry.Id, inventoryPerson, true), "已完成仓库快速存取");
                return;
            }
            if (holder != "") inventoryPerson = holder;
            inventorySelected = entry == null || (!inventoryWarehouse && inventorySelected == entry.Id) ? "" : entry.Id;
            inventoryQuantity = entry?.Count ?? 1;
            OpenInventory(inventoryWarehouse);
        }

        private void BindInventoryDrop(GameObject obj, BaseInventoryEntry entry, string holder, int cell, string slot, bool hidden = false)
        {
            var drag = obj.GetOrAdd<PartyInventoryCell>();
            drag.ItemId = entry?.Id;
            drag.ItemName = entry?.Name;
            drag.Icon = InventoryItemIcons.Load(InventoryIconKey(entry));
            drag.Holder = holder;
            drag.Cell = cell;
            drag.Slot = slot;
            drag.Scope = panel;
            drag.BeginDrag = inventoryClicks.Reset;
            drag.Move = (id, target, index, gear) =>
            {
                if (hidden) { owner.ReportMessage("此格物品被分类隐藏，请先切换全部"); return; }
                InventoryChange(state => BasePartyInventory.Move(state, id, target,
                    index == -2 ? BasePartyInventory.FreeCell(state, target) : index, gear), "已移动物品");
            };
        }

        private static Button InventoryButton(RectTransform parent, string path, string text, Action action, bool enabled = true)
        {
            var button = Ui<Button>(parent, path);
            if (text != null) button.GetComponentInChildren<Text>(true).text = text;
            button.interactable = action != null && enabled;
            if (action != null) button.onClick.AddListener(() => action());
            return button;
        }

        private void InventoryChange(Func<BaseState, bool> action, string message)
        {
            inventoryClicks.Reset();
            owner.Change(state =>
            {
                if (!action(state)) throw new InvalidOperationException("位置、空格、职业或专属条件不允许此操作");
            }, message);
            OpenInventory(inventoryWarehouse);
        }

        private void ShowInventoryItemMenu()
        {
            var entry = BasePartyInventory.Find(owner.State, inventorySelected);
            if (entry == null) return;
            var popup = CloneSceneUi("InventoryItemMenu", panel);
            InventoryButton(popup, "Close", null, () => CloseInventoryItemMenu());
            BindInventoryDetails(popup, entry, owner.State.Leaders.Single(person => person.Id == inventoryPerson), inventoryWarehouse);
            Canvas.ForceUpdateCanvases();

            var selected = inventorySelectedCell;
            var corners = new Vector3[4];
            selected.GetWorldCorners(corners);
            var bottomLeft = (Vector2)panel.InverseTransformPoint(corners[0]);
            var topRight = (Vector2)panel.InverseTransformPoint(corners[2]);
            var cell = UnityEngine.Rect.MinMaxRect(bottomLeft.x, bottomLeft.y, topRight.x, topRight.y);
            var offset = popup.anchoredPosition;
            float minX = panel.rect.xMin - popup.rect.xMin;
            float maxX = panel.rect.xMax - popup.rect.xMax;
            float minY = panel.rect.yMin - popup.rect.yMin;
            float maxY = panel.rect.yMax - popup.rect.yMax;
            float x = cell.xMax - popup.rect.xMin + 8 + offset.x;
            float y = cell.center.y - popup.rect.center.y + offset.y;
            if (x > maxX)
            {
                x = cell.xMin - popup.rect.xMax - 8 + offset.x;
                if (x < minX)
                {
                    x = cell.center.x - popup.rect.center.x + offset.x;
                    y = cell.yMax - popup.rect.yMin + 8 + offset.y;
                    if (y > maxY) y = cell.yMin - popup.rect.yMax - 8 + offset.y;
                }
            }
            popup.localPosition = new Vector3(Mathf.Clamp(x, minX, maxX), Mathf.Clamp(y, minY, maxY), popup.localPosition.z);
        }

        internal bool CloseInventoryItemMenu()
        {
            if (!PanelOpen || (panelViewKey != "party-inventory" && panelViewKey != "party-warehouse")
                || panel.Find("InventoryItemMenu") == null) return false;
            if (inventoryChoice == null)
            {
                inventoryClicks.Reset();
                inventorySelected = "";
            }
            OpenInventory(inventoryWarehouse);
            return true;
        }

        private void BindInventoryDetails(RectTransform details, BaseInventoryEntry entry, BaseLeader recipient, bool warehouse)
        {
            details.gameObject.SetActive(entry != null);
            if (entry == null) return;
            inventoryQuantity = Mathf.Clamp(inventoryQuantity, 1, entry.Count);
            var title = Ui<Text>(details, "Title");
            title.text = entry.Name + (entry.Count > 1 ? " ×" + entry.Count : "");
            InventoryItemIcons.LeadLabel(title, InventoryItemIcons.Load(InventoryIconKey(entry)), 25);
            string quantity = entry.Count > 1 ? inventoryQuantity + " / " + entry.Count : entry.Count.ToString();
            Ui<Text>(details, "Meta").text = "数量 " + quantity + (entry.Gear != null
                ? " · " + BasePersonalEquipment.SlotName(BasePersonalEquipment.Definition(entry.Gear.Template).Slot) : "");
            string description=entry.Description;
            if(entry.Gear!=null)description=(string.IsNullOrEmpty(description)?"":description+"\n")+InventoryGearDescription(entry,recipient);
            Ui<Text>(details, "Description").text=description;

            string primary = null;
            Action act = null;
            bool allowed = true;
            if (entry.Gear != null && entry.Holder != "")
            {
                var definition = BasePersonalEquipment.Definition(entry.Gear.Template);
                primary = entry.Worn ? "卸下" : "穿戴";
                act = () => EquipInventoryEntry(entry, definition.Slot);
                var wearer = owner.State.Leaders.Single(person => person.Id == entry.Holder);
                allowed = entry.Worn || definition.Slot != "weapon" || definition.Weapon == wearer.PersonalEquipment.Weapon;
            }
            else if (entry.Stack != null && (entry.Stack.ItemId == BaseWine.Strong || entry.Stack.ItemId == BaseWine.Sake))
            {
                primary = entry.Count > 1 ? "使用／拆分" : "使用";
                act = () => OpenWine(entry.Id);
            }
            else if (entry.Count > 1)
            {
                primary = "拆分";
                act = () => InventoryChange(next => BasePartyInventory.Move(next, entry.Id, entry.Holder,
                    entry.Holder == "" ? -1 : BasePartyInventory.FreeCell(next, entry.Holder), "", inventoryQuantity), "已拆分物品");
                allowed = inventoryQuantity < entry.Count;
            }

            var primaryButton = InventoryButton(details, "Primary", primary, act, allowed);
            primaryButton.gameObject.SetActive(act != null);
            if (act != null && !entry.Worn && BaseDemoIntro.Daily(owner.State) && entry.Id == owner.State.Demo.Intro.PracticeId)
                questSwordTarget = (RectTransform)primaryButton.transform;
            InventoryButton(details, "Destroy", null, () => InventoryDestroyConfirm(entry))
                .gameObject.SetActive(!entry.Worn && !entry.Protected);
            InventoryButton(details, "QuantityDown", null, () => { inventoryQuantity--; OpenInventory(inventoryWarehouse); },
                inventoryQuantity > 1).gameObject.SetActive(entry.Count > 1);
            InventoryButton(details, "QuantityUp", null,
                () => { inventoryQuantity++; OpenInventory(inventoryWarehouse); }, inventoryQuantity < entry.Count)
                .gameObject.SetActive(entry.Count > 1);
            InventoryButton(details, "Store", entry.Holder == "" ? "取出给" + recipient.Name : "存入仓库", () =>
                InventoryChange(next => BasePartyInventory.Move(next, entry.Id, entry.Holder == "" ? inventoryPerson : "",
                    entry.Holder == "" ? BasePartyInventory.FreeCell(next, inventoryPerson) : -1, "", inventoryQuantity), "已完成仓库存取"))
                .gameObject.SetActive(warehouse && !entry.Worn);
            bool transferable = !entry.Worn && string.IsNullOrEmpty(entry.Bound)
                && BasePartyInventory.People(owner.State, warehouse).Any(person =>
                    person.Id != (entry.Holder == "" ? inventoryPerson : entry.Holder));
            InventoryButton(details, "Transfer", null, () => InventoryTransferMenu(entry))
                .gameObject.SetActive(transferable);
        }

        private void EquipInventoryEntry(BaseInventoryEntry entry, string itemSlot)
        {
            string slot = entry.Worn ? entry.Slot : itemSlot;
            if (slot == "accessory")
            {
                var wearer = owner.State.Leaders.Single(person => person.Id == entry.Holder);
                slot = BasePersonalEquipment.SlotsFor(wearer).FirstOrDefault(candidate =>
                    candidate.StartsWith("accessory") && !wearer.PersonalEquipment.Items.Any(item => item.Slot == candidate));
            }
            if (slot == null) { owner.ReportMessage("请拖动到要替换的饰品槽"); return; }
            InventoryChange(next => entry.Worn
                ? BasePersonalEquipment.Unequip(next, entry.Holder, slot)
                : BasePersonalEquipment.Equip(next, entry.Holder, entry.Id, slot), "已更新装备");
        }

        private string InventoryGearDescription(BaseInventoryEntry entry, BaseLeader selected)
        {
            var state = owner.State;
            if (BaseRpg.Active(state))
                return BaseRpg.GearDescription(state, entry.Gear) + BaseRpg.GearComparison(state, entry.Gear,
                    string.IsNullOrEmpty(entry.Holder) ? null : entry.Holder);

            var definition = BasePersonalEquipment.Definition(entry.Gear.Template);
            var person = state.Leaders.FirstOrDefault(candidate => candidate.Id == entry.Holder) ?? selected;
            bool weapon = definition.Slot == "weapon";
            int quality = entry.Quality;
            var damage = weapon ? BasePersonalEquipment.WeaponAttack(person, entry.Gear) : BasePersonalEquipment.Scale(definition.Damage, quality);
            return BaseBackpack.CategoryName(entry.Category)
                + (weapon ? " · " + BasePersonalEquipment.Profession(definition.Weapon) + "专用" : "")
                + (entry.Protected ? " · 剧情保护／主角专属" : "")
                + "\n攻击 " + damage.ToString("+0.##;-0.##;0")
                + "／防御 " + BasePersonalEquipment.Scale(definition.Defense, quality).ToString("+0.##;-0.##;0")
                + "／生命 " + BasePersonalEquipment.Scale(definition.Health, quality).ToString("+0.##;-0.##;0")
                + (weapon ? "／射程 " + BasePersonalEquipment.RealtimeRange(entry.Gear) + "细格"
                    + (definition.Weapon != person.PersonalEquipment.Weapon ? "\n非本职业武器：无法装备此武器" : "") : "");
        }

        private RectTransform OpenInventoryChoice(string title)
        {
            if (inventoryChoice != null)
            {
                inventoryChoice.gameObject.SetActive(false);
                UiPrefabBinding.DestroyView(inventoryChoice.gameObject);
            }
            inventoryChoice = CloneSceneUi("InventoryChoice", panel);
            Ui<Text>(inventoryChoice, "Title").text = title;
            return Ui<RectTransform>(inventoryChoice, "Actions");
        }

        private void AddInventoryChoice(RectTransform actions, string title, Action select)
        {
            InventoryButton(CloneSceneUi("InventoryOption", actions), "", title, select);
        }

        private void InventoryPopup(string holder)
        {
            inventoryClicks.Reset();
            var actions = OpenInventoryChoice("物品分类");
            foreach (string category in InventoryCategories)
                AddInventoryChoice(actions, BaseBackpack.CategoryName(category), () =>
                {
                    inventoryFilters[holder] = category;
                    inventorySelected = "";
                    OpenInventory(inventoryWarehouse);
                });
            AddInventoryChoice(actions, "取消", () => OpenInventory(inventoryWarehouse));
        }

        private void InventoryTransferMenu(BaseInventoryEntry entry)
        {
            var actions = OpenInventoryChoice("转交物品");
            foreach (var person in BasePartyInventory.People(owner.State, inventoryWarehouse).Where(candidate => candidate.Id != entry.Holder))
                AddInventoryChoice(actions, "转交给 " + person.Name, () => InventoryChange(state =>
                    BasePartyInventory.Move(state, entry.Id, person.Id, BasePartyInventory.FreeCell(state, person.Id), "", inventoryQuantity), "已转交物品"));
            AddInventoryChoice(actions, "取消", () => OpenInventory(inventoryWarehouse));
        }

        private void InventoryDestroyConfirm(BaseInventoryEntry entry)
        {
            var actions = OpenInventoryChoice("销毁 " + entry.Name + " ×" + inventoryQuantity + "？\n此操作不可恢复。");
            AddInventoryChoice(actions, "确认销毁", () => InventoryChange(state =>
                BasePartyInventory.Destroy(state, entry.Id, inventoryQuantity), "已销毁物品"));
            AddInventoryChoice(actions, "取消", () => OpenInventory(inventoryWarehouse));
        }
    }
}

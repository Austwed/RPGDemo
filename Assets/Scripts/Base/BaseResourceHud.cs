using System;
using System.Collections.Generic;
using System.Linq;
using BaseConquest.Presentation;
using BaseConquest.Story;
using UnityEngine;
using UnityEngine.UI;

namespace BaseConquest.Base
{
    public sealed partial class BaseHud
    {
        private enum ResourceKind { Calendar, Food, Supplies, Materials, Coins, Energy, Troops }

        private static readonly ResourceKind[] ResourceKinds = (ResourceKind[])Enum.GetValues(typeof(ResourceKind));
        private readonly Dictionary<ResourceKind, Button> resourceButtons = new Dictionary<ResourceKind, Button>();
        private RectTransform resourceStrip, resourcePopover;
        private ResourceKind currentResource;
        private int calendarQuarter;

        public bool ResourcePopoverOpen => resourcePopover != null && resourcePopover.gameObject.activeSelf;

        private void BindResourceStrip(RectTransform header)
        {
            if (!BaseBattleGateway.DemoMode || owner.SceneUiTemplate("ResourceStrip") == null) return;
            header.GetComponent<Image>().raycastTarget = false;
            resourceStrip = CloneSceneUi("ResourceStrip", header);
            foreach (var kind in ResourceKinds)
            {
                var button = Ui<Button>(resourceStrip, "Stats/" + kind);
                resourceButtons.Add(kind, button);
                button.onClick.AddListener(() => ToggleResourcePopover(kind));
            }
            summary.gameObject.SetActive(false);
        }

        private void RefreshResourceStrip(BaseState state)
        {
            resourceStrip.gameObject.SetActive(state != null);
            summary.gameObject.SetActive(state == null);
            if (state == null) return;

            Ui<Text>(resourceStrip, "Location").text = BaseCampaign.LocationName(state);
            bool prologue = BaseDemoPrologue.Village(state) && BaseDemoIntro.Active(state);
            foreach (var kind in ResourceKinds)
            {
                var button = resourceButtons[kind];
                button.gameObject.SetActive(!prologue || kind == ResourceKind.Calendar || kind == ResourceKind.Food || kind == ResourceKind.Supplies);
                button.interactable = !prologue && !PanelOpen && !owner.Busy && !state.Aftermath.GameOver;
            }
            if (prologue)
            {
                int production = BaseDemoIntro.FarmOutput(state), consumption = BaseDemoIntro.VillageFood(state);
                SetStat(ResourceKind.Calendar, "时间", BaseDemoIntro.Daily(state) ? "清晨巡村" : "风起村头", "");
                SetStat(ResourceKind.Food, "粮食", $"{state.Inventory.Food}（{production - consumption:+0;-0;0}）", $"日产 {production}／口粮 {consumption}");
                SetStat(ResourceKind.Supplies, "工坊", BaseDemoIntro.SmithWork(state).ToString(), "工点");
                return;
            }

            var delta = BaseDailyDelta.Read(state);
            int supply = state.Inventory.Equipment.Where(e => e.Product == "supply").Sum(e => e.Count);
            var workers = state.Workers;
            SetStat(ResourceKind.Calendar, "日期", $"第 {state.Day} 天", $"第 {BaseCalendar.QuarterNumber(state.Day)} 季度");
            SetStat(ResourceKind.Food, "粮食", state.Inventory.Food.ToString(), DailyAmount(delta.Food));
            SetStat(ResourceKind.Supplies, "军需", supply.ToString(), DailyAmount(delta.Supply));
            SetStat(ResourceKind.Materials, "原料", state.Inventory.Materials.ToString(), DailyAmount(delta.Materials));
            SetStat(ResourceKind.Coins, "钱币", state.Inventory.Coins.ToString(), DailyAmount(delta.Coins));
            SetStat(ResourceKind.Energy, "体力", BaseDemoRuins.Enabled(state) ? $"{BaseRuinsRewards.Energy(state)} / {BaseRuinsRewards.Maximum(state)}" : "—",
                BaseDemoRuins.Enabled(state) ? DailyAmount(ExpectedEnergy(state)) : "");
            SetStat(ResourceKind.Troops, "兵力", workers.Sum(w => w.Count).ToString(),
                $"剑 {workers.Where(w => w.Kind == BaseTroopKind.Melee).Sum(w => w.Count)} · 弓 {workers.Where(w => w.Kind == BaseTroopKind.Ranged).Sum(w => w.Count)} · 骑 {workers.Where(w => w.Kind == BaseTroopKind.Cavalry).Sum(w => w.Count)} · 法 {workers.Where(w => w.Kind == BaseTroopKind.Mage).Sum(w => w.Count)}");
        }

        private static int ExpectedEnergy(BaseState state) => Math.Min(BaseRuinsRewards.DailyRecovery,
            Math.Max(0, BaseRuinsRewards.Maximum(state) - BaseRuinsRewards.Energy(state)));

        private static string DailyAmount(int amount)
        {
            string color = amount > 0 ? "#FFCE94" : amount < 0 ? "#C6E1AA" : "#E2C69A";
            return "<color=" + color + ">" + amount.ToString("+0;-0;0") + " / 日</color>";
        }

        private void SetStat(ResourceKind kind, string title, string value, string detail)
        {
            var button = resourceButtons[kind];
            Ui<Text>(button.transform, "Title").text = title;
            Ui<Text>(button.transform, "Value").text = value;
            Ui<Text>(button.transform, "Detail").text = detail;
        }

        private void ToggleResourcePopover(ResourceKind kind)
        {
            if (PanelOpen || owner.State == null || owner.Busy) return;
            if (ResourcePopoverOpen && currentResource == kind) { CloseResourcePopover(); return; }
            currentResource = kind;
            if (kind == ResourceKind.Calendar) calendarQuarter = BaseCalendar.QuarterNumber(owner.State.Day);
            RenderResourcePopover();
        }

        internal void DismissResourcePopoverOutside(Transform hit)
        {
            if (!ResourcePopoverOpen) return;
            if (hit != null && (hit.IsChildOf(resourcePopover) || hit.IsChildOf(resourceStrip))) return;
            CloseResourcePopover();
        }

        public void CloseResourcePopover()
        {
            if (resourcePopover == null) return;
            resourcePopover.gameObject.SetActive(false);
            UiPrefabBinding.DestroyView(resourcePopover.gameObject);
            resourcePopover = null;
        }

        private void RenderResourcePopover()
        {
            CloseResourcePopover();
            var state = owner.State;
            var header = resourceStrip.parent;
            var layer = CloneSceneUi("ResourcePopover", header.parent);
            resourcePopover = layer;
            // Only the popup window participates in pointer hit testing.
            layer.SetAsLastSibling();
            header.SetAsLastSibling();
            var window = Ui<RectTransform>(layer, "Window");
            Ui<Button>(window, "Close").onClick.AddListener(CloseResourcePopover);
            var scroll = Ui<ScrollRect>(window, "Body");
            var content = scroll.content;
            var title = Ui<Text>(window, "Title");

            void Details(string caption, params string[] lines)
            {
                title.text = caption;
                foreach (var value in lines)
                {
                    var line = CloneSceneUi("ResourceDetailLine", content);
                    Ui<Text>(line, "").text = value;
                }
            }

            switch (currentResource)
            {
                case ResourceKind.Calendar:
                    Details(StoryText.Get("story.ui.resource.calendar.title", "日历 · 第 {quarter} 季度").Replace("{quarter}", calendarQuarter.ToString()),
                        RenderResourceCalendar(state, content));
                    break;
                case ResourceKind.Food: Details("粮食 · 粮田工作", BaseTopBar.Food(state)); break;
                case ResourceKind.Supplies: Details("军需 · 库存与工作", BaseTopBar.Supply(state)); break;
                case ResourceKind.Materials: Details("原料 · 消耗渠道", BaseTopBar.Materials(state)); break;
                case ResourceKind.Coins:
                    Details("钱币 · 今日变化", StoryText.Get("story.ui.resource.coins.details", "当前 {coins} 币\n预计净变化 {delta} / 日\n交易等即时变化不计入日结预计。")
                        .Replace("{coins}", state.Inventory.Coins.ToString()).Replace("{delta}", BaseDailyDelta.Read(state).Coins.ToString("+0;-0;0")).Split('\n'));
                    break;
                case ResourceKind.Energy:
                    if (!BaseDemoRuins.Enabled(state)) Details("体力", StoryText.Get("story.ui.resource.energy.disabled", "本局没有启用体力。"));
                    else Details("体力", StoryText.Get("story.ui.resource.energy.status", "当前 / 上限 {current} / {max}\n每日恢复最多 {recovery}，下次预计恢复 +{next}")
                        .Replace("{current}", BaseRuinsRewards.Energy(state).ToString()).Replace("{max}", BaseRuinsRewards.Maximum(state).ToString())
                        .Replace("{recovery}", BaseRuinsRewards.DailyRecovery.ToString()).Replace("{next}", ExpectedEnergy(state).ToString()).Split('\n'));
                    break;
                case ResourceKind.Troops: Details("兵力 · 基地总览", BaseTopBar.Troops(state)); break;
            }
            Canvas.ForceUpdateCanvases();
            float frameHeight = window.rect.height - scroll.viewport.rect.height;
            float preferredHeight = frameHeight + Mathf.Max(42, LayoutUtility.GetPreferredHeight(content));
            window.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, Mathf.Min(window.rect.height, preferredHeight));
            var anchor = (RectTransform)resourceButtons[currentResource].transform;
            var bottom = layer.InverseTransformPoint(anchor.TransformPoint(new Vector3(anchor.rect.center.x, anchor.rect.yMin, 0)));
            float left = Mathf.Clamp(bottom.x - window.rect.width / 2 + window.anchoredPosition.x,
                layer.rect.xMin + 8, layer.rect.xMax - window.rect.width - 8);
            // Preserve the authored vertical gap; anchor the window under whichever stat opened it.
            window.anchoredPosition = new Vector2(left - layer.rect.xMin, bottom.y - layer.rect.yMax + window.anchoredPosition.y);
        }

        private int calendarDetailDay;
        private string[] RenderResourceCalendar(BaseState state, Transform content)
        {
            var calendar = CloneSceneUi("ResourceCalendar", content);
            var previous = Ui<Button>(calendar, "Previous");
            var next = Ui<Button>(calendar, "Next");
            int limit = BaseCalendar.QuarterNumber(state.Day) + 1;
            previous.interactable = calendarQuarter > 1;
            next.interactable = calendarQuarter < limit;
            previous.onClick.AddListener(() => ChangeResourceQuarter(-1, limit));
            next.onClick.AddListener(() => ChangeResourceQuarter(1, limit));
            Ui<Text>(calendar, "Quarter").text = $"第 {calendarQuarter} 季度";

            var days = BaseCalendar.Quarter(state, calendarQuarter);
            var grid = Ui<RectTransform>(calendar, "Days");
            var theme = DemoUiTheme.Current;
            string today = StoryText.Get("story.ui.resource.calendar.today", "今日");
            string raid = StoryText.Get("story.ui.resource.calendar.raid", "军情");
            foreach (var day in days)
            {
                var cell = CloneSceneUi("ResourceCalendarDay", grid);
                var label = Ui<Text>(cell, "Label");
                label.text = day.Day + (day.Today ? "\n" + today : day.Raids.Count > 0 ? "\n" + raid : "");
                var button=cell.gameObject.GetOrAdd<Button>();
                button.targetGraphic=Ui<Image>(cell,"");
                button.targetGraphic.raycastTarget=true;
                button.onClick.AddListener(()=>{calendarDetailDay=day.Day;RenderResourcePopover();});
                if (day.Today || day.Raids.Count > 0)
                    Ui<Image>(cell, "").color = day.Today ? theme.Gold : Color.Lerp(theme.Panel, theme.Gold, .35f);
                if (day.Past && day.Raids.Count == 0) label.color = theme.Muted;
            }
            var selected=days.FirstOrDefault(d=>d.Day==calendarDetailDay);
            return selected!=null?new[]{BaseCalendar.DayDetails(state,selected)}:days.Where(d=>d.Raids.Count>0).Select(d=>$"第 {d.Day} 天 · {string.Join("；",d.Raids)}").ToArray();
        }

        private void ChangeResourceQuarter(int delta, int limit)
        {
            calendarQuarter = Mathf.Clamp(calendarQuarter + delta, 1, limit);
            RenderResourcePopover();
        }
    }
}

using System.Linq;
using BaseConquest.Presentation;
using UnityEngine;
using UnityEngine.UI;

namespace BaseConquest.Base
{
    // P33 顶栏明细、15 天日历与体力页。
    public sealed partial class BaseHud
    {
        public void OpenResources()
        {
            if (owner.State == null) return;
            var s = owner.State;
            interactionObject = "";
            BeginPanel("资源明细", "resources");
            RowButton("体力", -210, 400, OpenEnergy);
            RowButton("日历", 210, 400, OpenCalendar);
            row += 58;
            ResourceCard("粮食 " + s.Inventory.Food, BaseTopBar.Food(s), -1);
            ResourceCard("原料 " + s.Inventory.Materials, BaseTopBar.Materials(s), 1);
            row += 176;
            ResourceCard("军需 " + s.Inventory.Equipment.Where(e => e.Product == "supply").Sum(e => e.Count), BaseTopBar.Supply(s), -1);
            ResourceCard("兵力 " + s.Workers.Sum(w => w.Count), BaseTopBar.Troops(s), 1);
            row += 176;
            EndPanel();
        }

        private void ResourceCard(string title, string[] details, int column)
        {
            var theme = DemoUiTheme.Current;
            var card = Solid("ResourceCard", content, new Vector2(.5f, 1), new Vector2(216 * column, -row - 82), new Vector2(412, 164), theme.Inset);
            var border = card.gameObject.GetOrAdd<Outline>();
            border.effectColor = theme.Line;
            border.effectDistance = new Vector2(1, -1);
            var header = Solid("ResourceHeading", card, new Vector2(.5f, 1), new Vector2(0, -15), new Vector2(410, 28), theme.Wood);
            Label(header, title, Vector2.zero, new Vector2(384, 26), DemoUiMetrics.FontSubhead).color = theme.Panel;
            for (int i = 0; i < details.Length; i++)
                Label(card, details[i], new Vector2(0, -48 - i * 31), new Vector2(384, 30), DemoUiMetrics.FontBody, TextAnchor.MiddleLeft, new Vector2(.5f, 1));
        }

        public void OpenCalendar()
        {
            if (owner.State == null) return;
            var s = owner.State;
            interactionObject = "";
            BeginPanel("日历 · 第 " + s.Day + " 天", "calendar");
            RowButton("返回", -290, 270, OpenResources);
            RowButton("体力", 0, 270, OpenEnergy);
            RowButton("关闭", 290, 270, ClosePanel);
            row += 58;
            // 5 列 × 3 行真网格：格子自带底色，不靠空格对齐（比例字体下对不齐）。
            const float cellWidth = 168, cellHeight = 40, cellGap = 6;
            int index = 0;
            var theme = DemoUiTheme.Current;
            foreach (var day in BaseCalendar.Quarter(s))
            {
                string mark = day.Today ? "◆" : day.Past ? "·" : "○";
                string cell = mark + day.Day;
                if (day.Raids.Count > 0) cell += "!";
                int column = index % 5, line = index / 5;
                var fill = day.Today ? theme.Gold : day.Raids.Count > 0 ? Color.Lerp(theme.Panel, theme.Gold, .32f) : theme.Inset;
                var box = Solid("Day" + day.Day, content, new Vector2(0.5f, 1),
                    new Vector2((column - 2) * cellWidth, -row - cellHeight / 2 - line * (cellHeight + cellGap)),
                    new Vector2(cellWidth - cellGap, cellHeight), fill);
                var dayLabel = Label(box, cell, Vector2.zero, new Vector2(cellWidth - cellGap - 8, cellHeight), DemoUiMetrics.FontSubhead, TextAnchor.MiddleCenter);
                dayLabel.color = day.Past && day.Raids.Count == 0 ? theme.Muted : theme.Ink;
                if (day.Today) dayLabel.fontStyle = FontStyle.Bold;
                var selectDay=box.gameObject.GetOrAdd<Button>();
                selectDay.targetGraphic=box.GetComponent<Image>();
                selectDay.targetGraphic.raycastTarget=true;
                selectDay.onClick.AddListener(()=>{calendarDetailDay=day.Day;OpenCalendar();});
                index++;
            }
            row += 3 * (cellHeight + cellGap) + 6;
            Line("15 天一个季度 · 本季度 " + BaseCalendar.QuarterStart(s.Day) + "～" + BaseCalendar.QuarterEnd(s.Day) + " 天", 24, DemoUiMetrics.FontCaption);
            foreach (var day in BaseCalendar.Quarter(s).Where(d => d.Raids.Count > 0))
            {
                Line("第 " + day.Day + " 天 · " + string.Join("；", day.Raids), 22, DemoUiMetrics.FontBody);
            }
            var selected=BaseCalendar.Quarter(s).FirstOrDefault(day=>day.Day==calendarDetailDay);
            if(selected!=null)Line(BaseCalendar.DayDetails(s,selected),44,DemoUiMetrics.FontBody);
            EndPanel();
        }
        public void OpenEnergy()
        {
            if (owner.State == null) return;
            var s = owner.State;
            interactionObject = "";
            BeginPanel("体力", "energy");
            RowButton("返回", -290, 270, OpenCalendar);
            RowButton("关闭", 290, 270, ClosePanel);
            row += 58;
            if (!BaseDemoRuins.Enabled(s)) { Line("本局没有启用体力。", 24); EndPanel(); return; }
            Line("当前 " + BaseRuinsRewards.Energy(s) + " / 上限 " + BaseRuinsRewards.Maximum(s), 26, DemoUiMetrics.FontSubhead);
            Line("次日预计恢复 +" + ExpectedEnergy(s) + " 点 · 窝点挑战、古窟再战和扫荡各需 " + BaseRuinsRewards.EntryCost(s, BasePrepRoute.Hideout) + " 点", 24, DemoUiMetrics.FontBody);
            EndPanel();
        }
    }
}

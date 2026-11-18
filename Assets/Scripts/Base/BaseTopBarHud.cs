using System;
using System.Linq;
using BaseConquest.Presentation;
using UnityEngine;
using UnityEngine.UI;

namespace BaseConquest.Base
{
    // P33 顶栏明细、15 天日历与体力页。
    public sealed partial class BaseHud
    {
        private void OpenResourceInfo(Action back) => RuleInfo("资源与人员信息",
            "基地总兵力只统计己方士兵，包含驻外、在岗、闲置与重伤士兵。\n\n带正负号的数字是按当前岗位预计的一日净变化。粮食扣除全部人员口粮，原料扣除工坊耗料。军需显示两档总量，库存页可查看品质拆分。\n\n姓名为棕色表示通过劝降加入，只标记来源，工作、伤情、编队和作战规则相同。\n\n格子信息 3 秒自动收起，点击信息面板可立即收起。兵营完整单位详情保持打开。", back);

        public void OpenResources()
        {
            if (owner.State == null) return;
            var s = owner.State;
            interactionObject = "";
            BeginPanel("资源明细", "resources");
            RowButton("体力", -290, 270, OpenEnergy);
            RowButton("规则 i", 0, 270, () => RuleInfo("资源与人员信息", "括号内是按当前岗位预计的一日净变化：正值红色、负值绿色、零为中性色。粮食扣除全部人员口粮，原料扣除工坊耗料。军需只在战斗内加攻防。兵力四分类与编队同源：总数、可工作、可主动出战、重伤。", OpenResources));
            RowButton("日历", 290, 270, OpenCalendar);
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
                else if (day.Forecast) cell += "~";
                int column = index % 5, line = index / 5;
                var fill = day.Today ? theme.Gold : day.Raids.Count > 0 ? Color.Lerp(theme.Panel, theme.Gold, .32f) : theme.Inset;
                var box = Solid("Day" + day.Day, content, new Vector2(0.5f, 1),
                    new Vector2((column - 2) * cellWidth, -row - cellHeight / 2 - line * (cellHeight + cellGap)),
                    new Vector2(cellWidth - cellGap, cellHeight), fill);
                var dayLabel = Label(box, cell, Vector2.zero, new Vector2(cellWidth - cellGap - 8, cellHeight), DemoUiMetrics.FontSubhead, TextAnchor.MiddleCenter);
                dayLabel.color = day.Past && day.Raids.Count == 0 ? theme.Muted : theme.Ink;
                if (day.Today) dayLabel.fontStyle = FontStyle.Bold;
                index++;
            }
            row += 3 * (cellHeight + cellGap) + 6;
            Line("15 天一个季度 · 本季度 " + BaseCalendar.QuarterStart(s.Day) + "～" + BaseCalendar.QuarterEnd(s.Day) + " 天", 24, DemoUiMetrics.FontCaption);
            foreach (var day in BaseCalendar.Quarter(s).Where(d => d.Raids.Count > 0))
            {
                Line("第 " + day.Day + " 天 · " + string.Join("；", day.Raids), 22, DemoUiMetrics.FontBody);
            }
            Line("◆今天 · ·已过去 · ○未到 · !已知袭击 · ~模糊范围", 20, DemoUiMetrics.FontCaption);
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
            if (!BaseDemoRuins.Enabled(s)) { Line("本局没有启用体力（遗迹重战与扫荡消耗）。", 24); EndPanel(); return; }
            Line("当前 " + BaseRuinsRewards.Energy(s) + " / 上限 " + BaseRuinsRewards.Maximum(s), 26, DemoUiMetrics.FontSubhead);
            Line("每天恢复 " + BaseRuinsRewards.DailyRecovery + " 点，次日结算时补到上限为止；扫荡与重战各消耗 " + BaseRuinsRewards.Cost(s) + " 点。", 24, DemoUiMetrics.FontBody);
            EndPanel();
        }
    }
}

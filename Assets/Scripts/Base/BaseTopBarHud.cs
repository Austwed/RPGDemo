using System;
using System.Linq;
using BaseConquest.Presentation;
using UnityEngine;

namespace BaseConquest.Base
{
    // P33 顶栏明细、15 天日历与体力页。只提供入口与数据；版式和展开方式等原型定稿后替换。
    public sealed partial class BaseHud
    {
        public void OpenResources()
        {
            if (owner.State == null) return;
            var s = owner.State;
            interactionObject = "";
            BeginPanel("资源明细", "resources");
            RowButton("返回", -290, 270, ClosePanel);
            RowButton("规则 i", 0, 270, () => RuleInfo("资源与人员信息", "括号内是按当前岗位预计的一日净变化：正值红色、负值绿色、零为中性色。粮食扣除全部人员口粮，原料扣除工坊耗料，钱币扣除旅店寄存日费。军需只在战斗内加攻防。兵力四分类与编队同源：总数、可工作、可主动出战、重伤。", OpenResources));
            RowButton("日历", 290, 270, OpenCalendar);
            row += 58;
            Line("粮食 " + s.Inventory.Food, 26, DemoUiMetrics.FontSubhead);
            foreach (var text in BaseTopBar.Food(s)) Line(text, 20, DemoUiMetrics.FontBody);
            row += 2;
            Line("原料 " + s.Inventory.Materials, 26, DemoUiMetrics.FontSubhead);
            foreach (var text in BaseTopBar.Materials(s)) Line(text, 20, DemoUiMetrics.FontBody);
            row += 2;
            Line("军需 " + s.Inventory.Equipment.Where(e => e.Product == "supply").Sum(e => e.Count), 26, DemoUiMetrics.FontSubhead);
            foreach (var text in BaseTopBar.Supply(s)) Line(text, 20, DemoUiMetrics.FontBody);
            row += 2;
            Line("兵力 " + s.Workers.Sum(w => w.Count), 26, DemoUiMetrics.FontSubhead);
            foreach (var text in BaseTopBar.Troops(s)) Line(text, 20, DemoUiMetrics.FontBody);
            EndPanel();
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
            foreach (var day in BaseCalendar.Quarter(s))
            {
                string mark = day.Today ? "◆" : day.Past ? "·" : "○";
                string cell = mark + day.Day;
                if (day.Raids.Count > 0) cell += "!";
                else if (day.Forecast) cell += "~";
                int column = index % 5, line = index / 5;
                var box = Box("Day" + day.Day, content, new Vector2(0.5f, 1),
                    new Vector2((column - 2) * cellWidth, -row - cellHeight / 2 - line * (cellHeight + cellGap)),
                    new Vector2(cellWidth - cellGap, cellHeight), new Color(.16f, .15f, .14f, .55f));
                Label(box, cell, Vector2.zero, new Vector2(cellWidth - cellGap - 8, cellHeight), DemoUiMetrics.FontSubhead, TextAnchor.MiddleCenter);
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

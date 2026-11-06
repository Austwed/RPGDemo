using System;
using System.Linq;

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
            row += 64;
            Line("粮食 " + s.Inventory.Food, 46, 22);
            foreach (var text in BaseTopBar.Food(s)) Line(text, 40, 17);
            row += 6;
            Line("原料 " + s.Inventory.Materials, 46, 22);
            foreach (var text in BaseTopBar.Materials(s)) Line(text, 40, 17);
            row += 6;
            Line("军需 " + s.Inventory.Equipment.Where(e => e.Product == "supply").Sum(e => e.Count), 46, 22);
            foreach (var text in BaseTopBar.Supply(s)) Line(text, 40, 17);
            row += 6;
            Line("兵力 " + s.Workers.Sum(w => w.Count), 46, 22);
            foreach (var text in BaseTopBar.Troops(s)) Line(text, 40, 17);
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
            row += 64;
            Line("15 天一个季度 · 本季度 " + BaseCalendar.QuarterStart(s.Day) + "～" + BaseCalendar.QuarterEnd(s.Day) + " 天", 44, 20);
            int column = 0; string line = "";
            foreach (var day in BaseCalendar.Quarter(s))
            {
                string mark = day.Today ? "◆" : day.Past ? "·" : "○";
                string cell = mark + day.Day.ToString();
                if (day.Raids.Count > 0) cell += "!";
                else if(day.Forecast)cell+="~";
                line += cell.PadRight(6);
                if (++column % 5 == 0) { Line(line, 38, 18); line = ""; }
            }
            if (line != "") Line(line, 38, 18);
            row += 6;
            foreach (var day in BaseCalendar.Quarter(s).Where(d => d.Raids.Count > 0))
            {
                Line("第 " + day.Day + " 天 · " + string.Join("；", day.Raids), 40, 17);
            }
            Line("◆今天 · ·已过去 · ○未到 · !已知袭击 · ~模糊范围", 44, 17);
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
            row += 64;
            if (!BaseDemoRuins.Enabled(s)) { Line("本局没有启用体力（遗迹重战与扫荡消耗）。", 60); EndPanel(); return; }
            Line("当前 " + BaseRuinsRewards.Energy(s) + " / 上限 " + BaseRuinsRewards.Maximum(s), 50, 22);
            Line("每天恢复 " + BaseRuinsRewards.DailyRecovery + " 点，次日结算时补到上限为止；扫荡与重战各消耗 " + BaseRuinsRewards.Cost(s) + " 点。", 56, 17);
            Line("补充入口留在这里（道具与购买方式待设计），当前没有可用的补充手段。", 48, 17);
            EndPanel();
        }
    }
}

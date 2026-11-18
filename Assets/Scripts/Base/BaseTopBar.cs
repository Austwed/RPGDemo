using System;
using System.Collections.Generic;
using System.Linq;

namespace BaseConquest.Base
{
    // 15 天一个季度，只标出已发生或确切已知的袭击。
    public sealed class BaseCalendarDay
    {
        public int Day;
        public bool Today, Past, Forecast;
        public List<string> Raids = new List<string>();
        public string Label => "第 " + Day + " 天";
    }
    public static class BaseCalendar
    {
        public const int QuarterDays = 15;
        public static int QuarterNumber(int day) => (Math.Max(1, day) - 1) / QuarterDays + 1;
        public static int QuarterStart(int day) => ((Math.Max(1, day) - 1) / QuarterDays) * QuarterDays + 1;
        public static int QuarterEnd(int day) => QuarterStart(day) + QuarterDays - 1;
        public static List<BaseCalendarDay> Quarter(BaseState s) => Quarter(s, QuarterNumber(s?.Day ?? 1));
        public static List<BaseCalendarDay> Quarter(BaseState s, int quarter)
        {
            var days = new List<BaseCalendarDay>();
            if (s?.Campaign?.Events == null || quarter < 1 || quarter > QuarterNumber(s.Day) + 1) return days;
            int start = (quarter - 1) * QuarterDays + 1;
            for (int offset = 0; offset < QuarterDays; offset++)
            {
                int day = start + offset;
                var row = new BaseCalendarDay { Day = day, Today = day == s.Day, Past = day < s.Day };
                var intel=s.Demo?.Flow?.RaidIntel??new List<BaseRaidIntelSnapshot>();
                foreach(var snapshot in intel.Where(x=>x.Window==3&&x.HasRaid&&day>=x.Day+1&&day<=x.Day+3&&day>=s.Day))
                    if(!intel.Any(precise=>precise.Window==5&&precise.Day<=snapshot.Day&&precise.Day+5>=snapshot.Day+3))row.Forecast=true;
                foreach(var e in s.Campaign.Events.Where(item=>item.Kind=="defense"&&item.AvailableDay==day).OrderBy(item=>item.Title,StringComparer.Ordinal))
                {
                    bool occurred=day<=s.Day&&(e.Status=="pending"||s.Campaign.Reports.Any(r=>r.EventId==e.Id&&r.Day==day));
                    bool known=day>s.Day&&BaseRaidIntel.Known(s,e);
                    if(occurred||known)row.Raids.Add(e.Title);
                }
                days.Add(row);
            }
            return days;
        }
    }
    // 顶栏四项的下拉明细（P33）。只提供数据，展开方式与版式等原型定稿后替换。
    public static class BaseTopBar
    {
        public static string[] Food(BaseState s)
        {
            var preview = BaseEconomy.ProjectDay(s);
            int soldiers = s.Workers.Sum(w => w.Count);
            int leaders = s.Leaders.Count(l => BaseAdventure.Alive(l));
            var farm = s.Muster.Facilities.FirstOrDefault(f => f.FacilityId == "farm");
            return new[]
            {
                "粮田在岗 " + BaseEconomy.WorkersAt(s, "farm") + " / " + (farm != null ? farm.Capacity : 0) + " 人 · 预计产粮 " + preview.FoodProduced + " / 日（生产系数 " + (preview.ProductionFactor * 100).ToString("0") + "%）",
                "预计消耗 " + preview.FoodRequired + " / 日：士兵 " + soldiers + " 人／队长 " + leaders + " 人（骑兵与队长按 2 份）",
                "日净变化 " + (preview.FoodProduced - preview.FoodRequired).ToString("+0;-0;0") + " / 日 · 库存 " + s.Inventory.Food
            };
        }
        public static string[] Materials(BaseState s)
        {
            var preview = BaseEconomy.ProjectDay(s);
            return new[]
            {
                "工坊在岗 " + BaseEconomy.ProductionWorkersAt(s, "workshop") + " 人 · 当前产品 " + BaseEconomy.ProductName(s.WorkshopProduct) + "（" + BaseConstruction.ProductLevel(s) + " 级）",
                "预计消耗原料 " + preview.MaterialsSpent + "：可做成 " + preview.ProductsMade + " 件（每件 " + BaseConstruction.MaterialCost(s, s.WorkshopProduct) + "）",
                "库存 " + s.Inventory.Materials + (s.Inventory.Materials < preview.MaterialsSpent ? " · 原料不足，产能会打折" : "")
            };
        }
        public static string[] Supply(BaseState s)
        {
            var preview = BaseEconomy.ProjectDay(s);
            int low = s.Inventory.Equipment.Where(e => e.Product == "supply" && e.Level <= 1).Sum(e => e.Count);
            int high = s.Inventory.Equipment.Where(e => e.Product == "supply" && e.Level >= 2).Sum(e => e.Count);
            bool making = s.WorkshopProduct == "supply";
            var shop = s.Muster.Facilities.FirstOrDefault(f => f.FacilityId == "workshop");
            string output = making
                ? "工坊在岗 " + BaseEconomy.WorkersAt(s, "workshop") + " / " + (shop != null ? shop.Capacity : 0) + " 人 · 预计产量 " + preview.ProductsMade + " 件 / 日（" + preview.ProductLevel + " 级）"
                : "工坊当前生产 " + BaseEconomy.ProductName(s.WorkshopProduct) + "，没有在生产军需";
            if (making && s.Inventory.Materials < preview.MaterialsSpent) output += " · 原料不足，预计 0 / 日";
            return new[]
            {
                "库存 一级 " + low + " 件／二级 " + high + " 件",
                output,
                "军需只在战斗内加攻防，战后只扣磨损；跨阶段越级使用属于经营选择"
            };
        }
        // 兵力四分类与 P38 的 Tally 同源：总数／可工作／可主动出战／重伤，外加在岗与闲置。
        public static string[] Troops(BaseState s)
        {
            var rows = new List<string>();
            int working = 0, idle = 0;
            foreach (BaseTroopKind kind in Enum.GetValues(typeof(BaseTroopKind)))
            {
                var tally = BaseDevelopment.Tally(s, kind);
                int onDuty = s.Workers.Where(w => w.Kind == kind && BaseDevelopment.Effective(s, w) && w.FacilityId != "").Sum(w => w.Count);
                int free = s.Workers.Where(w => w.Kind == kind && BaseDevelopment.Effective(s, w) && w.FacilityId == "").Sum(w => w.Count);
                working += onDuty; idle += free;
                rows.Add(BaseEconomy.TroopName(kind) + "：总数 " + tally[0] + " · 可工作 " + tally[1] + " · 可主动出战 " + tally[2] + " · 重伤 " + tally[3] + "（在岗 " + onDuty + "／闲置 " + free + "）");
            }
            rows.Add("全军在岗 " + working + " 人／闲置 " + idle + " 人；外出与随行不计入在岗");
            return rows.ToArray();
        }
    }
}

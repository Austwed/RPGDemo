using System;
using System.Collections.Generic;
using System.Linq;

namespace BaseConquest.Base
{
    // 15 天一个季度，只标出已发生或确切已知的袭击。
    public sealed class BaseCalendarDay
    {
        public int Day;
        public bool Today, Past;
        public List<string> EventIds = new List<string>();
        public List<string> Raids = new List<string>();
        public string Label => "第 " + Day + " 天";
    }
    public static class BaseCalendar
    {
        public const int QuarterDays = 15;
        public static int QuarterNumber(int day) => (Math.Max(1, day) - 1) / QuarterDays + 1;
        public static int QuarterStart(int day) => ((Math.Max(1, day) - 1) / QuarterDays) * QuarterDays + 1;
        public static int QuarterEnd(int day) => QuarterStart(day) + QuarterDays - 1;
        public static string DayDetails(BaseState state,BaseCalendarDay day)
        {
            var entries=state.Demo?.Flow?.RaidIntel?.Where(x=>x.Window==10).OrderByDescending(x=>x.Day)
                .SelectMany(x=>x.Entries).Where(x=>x.Day==day.Day && !string.IsNullOrEmpty(x.Composition)).ToList();
            var lines=new List<string>();
            for(int i=0;i<day.EventIds.Count;i++)
            {
                var entry=entries?.FirstOrDefault(x=>x.EventId==day.EventIds[i]);
                var report=state.Campaign.Reports.LastOrDefault(r=>r.EventId==day.EventIds[i]&&r.Day==day.Day);
                string detail=report!=null?(report.Outcome=="SideAWin"?"守住":"失守")+
                    (string.IsNullOrEmpty(report.RaidNote)?"":" · "+report.RaidNote):entry?.Composition;
                lines.Add(day.Raids[i]+" · "+(string.IsNullOrEmpty(detail)?"没有更多情报":detail));
            }
            return $"第 {day.Day} 天 · "+(lines.Count==0?"没有更多情报":string.Join("；",lines));
        }
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
                foreach(var e in s.Campaign.Events.Where(item=>item.Kind=="defense").OrderBy(item=>item.Title,StringComparer.Ordinal))
                {
                    bool reported=day<=s.Day&&s.Campaign.Reports.Any(r=>r.EventId==e.Id&&r.Day==day);
                    bool scheduled=e.Status=="pending"&&e.AvailableDay==day&&BaseRaidIntel.Known(s,e);
                    if(reported||scheduled){row.Raids.Add(e.Title);row.EventIds.Add(e.Id);}
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
            var farm = s.Muster.Facilities.FirstOrDefault(f => f.FacilityId == "farm");
            string scope = BaseSettlements.Available(s, "farm", true) ? "两地" : "";
            return new[]
            {
                "粮田在岗 " + BaseEconomy.ProductionWorkersAt(s, "farm") + " / " + (farm != null ? farm.Capacity : 0) + " 人 · " + scope + "预计产粮 " + preview.FoodProduced + " / 日（生产系数 " + (preview.ProductionFactor * 100).ToString("0") + "%）",
                "口粮需求 " + preview.FoodRequired + " / 日",
                "日净变化 " + (preview.FoodProduced - preview.FoodRequired).ToString("+0;-0;0") + " / 日 · 库存 " + s.Inventory.Food
            };
        }
        public static string[] Materials(BaseState s)
        {
            var preview = BaseEconomy.ProjectDay(s);
            string scope = BaseSettlements.Available(s, "workshop", true) ? "两地" : "";
            return new[]
            {
                "工坊在岗 " + BaseEconomy.ProductionWorkersAt(s, "workshop") + " 人 · 当前产品 " + BaseEconomy.ProductName(s.WorkshopProduct) + "（" + BaseConstruction.ProductLevel(s) + " 级）",
                scope + "预计消耗原料 " + preview.MaterialsSpent + " · 可做成 " + preview.ProductsMade + " 件（每件 " + BaseConstruction.MaterialCost(s, s.WorkshopProduct) + "）",
                "库存 " + s.Inventory.Materials + (preview.ProductsMade < preview.WorkCapacity ? " · 原料不足" : "")
            };
        }
        public static string[] Supply(BaseState s)
        {
            var preview = BaseEconomy.ProjectDay(s);
            int low = s.Inventory.Equipment.Where(e => e.Product == "supply" && e.Level <= 1).Sum(e => e.Count);
            int high = s.Inventory.Equipment.Where(e => e.Product == "supply" && e.Level >= 2).Sum(e => e.Count);
            bool making = s.WorkshopProduct == "supply";
            var shop = s.Muster.Facilities.FirstOrDefault(f => f.FacilityId == "workshop");
            string scope = BaseSettlements.Available(s, "workshop", true) ? "两地" : "";
            string output = making
                ? "工坊在岗 " + BaseEconomy.ProductionWorkersAt(s, "workshop") + " / " + (shop != null ? shop.Capacity : 0) + " 人 · " + scope + "预计产量 " + preview.ProductsMade + " 件 / 日（" + preview.ProductLevel + " 级）"
                : "工坊当前生产 " + BaseEconomy.ProductName(s.WorkshopProduct);
            if (making && preview.ProductsMade < preview.WorkCapacity) output += " · 原料不足";
            return new[]
            {
                "库存 一级 " + low + " 件／二级 " + high + " 件",
                output
            };
        }
        // 兵力四分类与 P38 的 Tally 同源：总数／可工作／可主动出战／重伤，外加在岗与闲置。
        public static string[] Troops(BaseState s)
        {
            var rows = new List<string>();
            int working = 0, idle = 0;
            foreach (BaseTroopKind kind in BaseTown.TroopKinds(s))
            {
                var tally = BaseDevelopment.Tally(s, kind);
                int onDuty = s.Workers.Where(w => w.Kind == kind && BaseDevelopment.Effective(s, w) && w.FacilityId != "").Sum(w => w.Count);
                int free = s.Workers.Where(w => w.Kind == kind && BaseDevelopment.Effective(s, w) && w.FacilityId == "").Sum(w => w.Count);
                working += onDuty; idle += free;
                rows.Add(BaseEconomy.TroopName(kind) + "：总数 " + tally[0] + " · 可工作 " + tally[1] + " · 可主动出战 " + tally[2] + " · 重伤 " + tally[3] + "（在岗 " + onDuty + "／闲置 " + free + "）");
            }
            rows.Add("全军在岗 " + working + " 人／闲置 " + idle + " 人");
            return rows.ToArray();
        }
    }
}

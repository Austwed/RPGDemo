using System;
using System.Collections.Generic;
using System.Linq;

namespace BaseConquest.Base
{
    // 商队线路：领地内跑商（粮换钱），以及开发者可测试的黑鸦寨往返（取原料）。
    // 人员占用"商队"设施的岗位，与军营战斗编制分开计算；每日口粮按双倍计。
    [Serializable] public sealed class BaseCaravanState
    {
        public int Version, DepartDay, Cargo, TotalDelivered;
        public string Route = "", Phase = "", Report = "";
        public List<string> Log = new List<string>();
        public BaseCaravanState Copy() => new BaseCaravanState { Version = Version, DepartDay = DepartDay, Cargo = Cargo, TotalDelivered = TotalDelivered, Route = Route, Phase = Phase, Report = Report, Log = (Log ?? new List<string>()).ToList() };
    }
    public static class BaseCaravan
    {
        public const int Version = 1;
        // 占位：每名商队人员每日运力、本地跑商金币、黑鸦寨每日产料与驻军人数。正式数值待平衡（P100）。
        public const int LoadPerCrew = 3, LocalCoinsPerCrew = 4, SiteYieldPerDay = 4;
        public static readonly string[] Routes = { "local", "crow" };
        public static string RouteName(string id) => id == "local" ? "领地内跑商" : id == "crow" ? "黑鸦寨运输" : "未安排";
        public static void Normalize(BaseState s)
        {
            if (s == null) return;
            if (s.Caravan == null) s.Caravan = new BaseCaravanState { Version = Version };
            if (s.Caravan.Version == 0) s.Caravan.Version = Version;
            if (s.Caravan.Log == null) s.Caravan.Log = new List<string>();
            if (s.Caravan.Log.Count > 20) s.Caravan.Log.RemoveRange(0, s.Caravan.Log.Count - 20);
            if (!Routes.Contains(s.Caravan.Route)) s.Caravan.Route = "";
            if (s.Caravan.Phase != "" && s.Caravan.Phase != "out" && s.Caravan.Phase != "back") s.Caravan.Phase = "";
        }
        public static void Validate(BaseState s)
        {
            Normalize(s);
            var c = s.Caravan;
            if (c.Version != Version || c.DepartDay < 0 || c.DepartDay > s.Day || c.Cargo < 0 || c.Cargo > 100000 || c.TotalDelivered < 0 || (c.Phase != "" && c.Route == "")) throw new ArgumentException("商队事务状态非法");
            if (c.Phase == "" && (c.Cargo != 0 || c.DepartDay != 0)) throw new ArgumentException("商队驻留时不应带在途货物");
            if (c.Route == "crow" && !BaseFacilityLayout.Built(s, "caravan")) throw new ArgumentException("未建成商队设施不能跑运输线");
            if (c.Route == "crow" && BaseDemo.ExperimentalManagementHidden(s))
            {
                int received = Math.Min(c.Cargo, 100000000 - s.Inventory.Materials);
                s.Inventory.Materials += received;
                c.TotalDelivered += Math.Min(received, int.MaxValue - c.TotalDelivered);
                Stop(s, received > 0 ? "商队已回营，卸下原料 " + received : "商队已回营，人员回到营地空闲");
            }
        }
        internal static void RecallHiddenRoute(BaseState s)
        {
            if (s.Caravan?.Route == "crow" && BaseDemo.ExperimentalManagementHidden(s)) Validate(s);
        }
        public static int Crew(BaseState s) => BaseEconomy.WorkersAt(s, "caravan");
        public static int Capacity(BaseState s) => Crew(s) * LoadPerCrew;
        private static int FoodPerCrew(BaseState s, BaseWorkerBucket worker)
            => BaseEconomy.Ration(s, worker) * 2;
        public static int DailyFood(BaseState s) => s.Workers.Where(w => !w.Away && w.FacilityId == "caravan").Sum(w => w.Count * FoodPerCrew(s, w));
        // 己方据点只认归属，不写死 Demo 的据点 id（攻营后会被改名）。
        public static BaseEnemySite Site(BaseState s) => s?.Campaign?.EnemySites?.FirstOrDefault(site => site != null && site.Owner == "player");
        public static string Problem(BaseState s, string route)
        {
            if (route == "crow" && BaseDemo.ExperimentalManagementHidden(s)) return "该商队线路暂未开放";
            if (!BaseFacilityLayout.Built(s, "caravan")) return "商队设施尚未建成";
            if (s.Campaign.HasActiveBattle || BaseSceneCombat.Active(s)) return "战斗中不能安排商队";
            if (s.Caravan.Phase != "" || s.Caravan.Route != "") return "商队事务进行中，返回或召回后才能再安排";
            if (!Routes.Contains(route)) return "未知线路";
            if (Crew(s) <= 0) return "请先在商队安排至少 1 人";
            if (route == "crow" && Site(s) == null) return "尚未攻占" + BaseScriptNames.Bandit + "，只能先做领地内跑商";
            int reserve = DailyFood(s) * 3;
            if (s.Inventory.Food < reserve) return "往返口粮不足：需要 " + reserve + "，现有 " + s.Inventory.Food;
            return "";
        }
        public static bool Start(BaseState s, string route, out string message)
        {
            message = Problem(s, route);
            if (message != "") return false;
            var c = s.Caravan;
            c.Route = route; c.DepartDay = route == "crow" ? s.Day : 0;
            if (route == "crow") { c.Phase = "out"; c.Cargo = 0; message = "商队已出发前往" + BaseScriptNames.Bandit + "，第 2 天取货、第 3 天回营"; }
            else { c.Phase = ""; message = "商队开始在领地内跑商，按日结算金币"; }
            Record(s, message);
            return true;
        }
        public static void Stop(BaseState s, string reason)
        {
            if (s?.Caravan == null) return;
            var c = s.Caravan;
            if (c.Phase == "" && c.Route == "" && c.Cargo == 0) return;
            // 途中解散不返还货物：与"商队失败完全解散"一致，只是这批货运丢了。
            c.Route = ""; c.Phase = ""; c.DepartDay = 0; c.Cargo = 0;
            foreach (var worker in s.Workers.Where(w => w.FacilityId == "caravan")) { worker.FacilityId = ""; worker.DesiredFacility = ""; worker.PostLocked = false; }
            if (!string.IsNullOrEmpty(reason)) Record(s, reason);
        }
        private static void Record(BaseState s, string text)
        {
            s.Caravan.Report = text;
            s.Caravan.Log.Add("第" + s.Day + "天 · " + text);
            if (s.Caravan.Log.Count > 20) s.Caravan.Log.RemoveAt(0);
        }
        // 日结：本地跑商直接结算金币；黑鸦寨线按"出发日／抵达日／回营日"推进。
        public static void AdvanceDay(BaseState s)
        {
            RecallHiddenRoute(s);
            var c = s.Caravan;
            if (c.Phase == "" && c.Route == "") return;
            if (c.Route == "local")
            {
                int coins = Crew(s) * LocalCoinsPerCrew;
                s.Inventory.Coins = Math.Min(100000000, s.Inventory.Coins + coins);
                Record(s, "领地内跑商结算：金币 +" + coins);
                if (s.Inventory.Food <= 0 || s.TodayFoodMissing > 0) { Stop(s, "口粮不足，商队解散，人员已回到基地空闲"); }
                return;
            }
            if (c.Route != "crow" || c.Phase == "") return;
            var site = Site(s);
            if (site == null) { Stop(s, BaseScriptNames.Bandit + "不再是己方据点，运输事务终止"); return; }
            site.Stock = Math.Min(100000000, site.Stock + SiteYieldPerDay);
            if (c.Phase == "out" && s.Day >= c.DepartDay + 1)
            {
                int load = Math.Min(Math.Min(site.Stock, Capacity(s)), Math.Max(0, 100000000 - c.Cargo));
                c.Cargo += load; site.Stock -= load; c.Phase = "back";
                Record(s, "商队抵达" + BaseScriptNames.Bandit + "，装载原料 " + load + "／" + Capacity(s));
            }
            else if (c.Phase == "back" && s.Day >= c.DepartDay + 2)
            {
                s.Inventory.Materials = Math.Min(100000000, s.Inventory.Materials + c.Cargo);
                Record(s, "商队回营卸下原料 " + c.Cargo);
                c.TotalDelivered += c.Cargo; c.Cargo = 0; c.Phase = ""; c.Route = ""; c.DepartDay = 0;
            }
            if (s.TodayFoodMissing > 0) { Stop(s, "口粮不足，商队解散，人员已回到基地空闲"); BaseDevelopment.Allocate(s); }
        }
        public static string Status(BaseState s)
        {
            var c = s.Caravan;
            if (!BaseFacilityLayout.Built(s, "caravan")) return "未建成商队设施";
            if (c.Route == "local") return "领地内跑商 · " + Crew(s) + "人 · 每日金币 +" + Crew(s) * LocalCoinsPerCrew;
            if (BaseDemo.ExperimentalManagementHidden(s)) return "待命";
            if (c.Route == "crow")
                return c.Phase == "out" ? "前往" + BaseScriptNames.Bandit + " · 第" + (c.DepartDay + 1) + "天抵达"
                    : "返程 · 载原料 " + c.Cargo + "／" + Capacity(s) + " · 第" + (c.DepartDay + 2) + "天回营";
            var site = Site(s);
            return "待命 · 运力 " + Capacity(s) + (site == null ? " · " + BaseScriptNames.Bandit + "未占领" : " · 黑鸦寨库存 " + site.Stock);
        }
    }
}

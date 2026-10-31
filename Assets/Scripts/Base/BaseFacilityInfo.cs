using System;
using System.Linq;

namespace BaseConquest.Base
{
    public sealed class BaseCellSelection
    {
        public readonly int X, Y;
        public BaseCellSelection(int x, int y) { X = x; Y = y; }
        public BaseMapObject Facility => BaseMap.FacilityAtCell(X, Y);
    }
    public static class BaseFacilityPages
    {
        public static string[] ForObject(string id) => id == "government" ? new[] { "workers", "inventory", "administrators", "army", "development", "report" }
            : id == "farm" ? new[] { "workers", "administrators", "status", "upgrade" }
            : id == "workshop" ? new[] { "workers", "administrators", "status", "production", "upgrade" } : id == "barracks" ? new[] { "armyinfo", "composition", "roster", "recovery" } : new string[0];
        public static string PageName(string id) => id=="composition" ? "队长编组" : id == "report" ? "日结报告" : id == "prisoners" ? "俘虏" : id == "upgrade" ? "升级" : id == "inventory" ? "库存" : id == "administrators" ? "管理员" : id == "workers" ? "工人" : id == "production" ? "生产" : id == "army" ? "用兵" : id == "development" ? "发展策略" : id == "armyinfo" ? "军队总览" : id == "roster" ? "单位名册" : id == "recovery" ? "休养" : "当前状态";
    }
    public sealed class BaseFacilityInfo
    {
        public string FacilityId, Name, Administrator, Workers, Production, Status;
        public static BaseFacilityInfo Read(BaseState state, string id)
        {
            var obj = BaseSpatial.Objects(state).FirstOrDefault(o => o.Id == id && !o.Id.StartsWith("partner"));
            if (obj == null) throw new ArgumentException("未知设施");
            var info = new BaseFacilityInfo { FacilityId = id, Name = obj.Name, Administrator = "", Workers = "", Production = "", Status = "" };
            if (id.StartsWith("exit_")) { info.Production = id == "exit_west" ? "前往基地外围迎敌战场" : id == "exit_east" ? "前往城镇" : id == "exit_south" ? "前往世界地图并选择敌点进攻" : "北侧预留"; info.Status = "接近入口后交互"; return info; }
            if (id == "government") { info.Production = "统一管理设施管理员、工人和仓库"; info.Status = "接近管理所后交互，进入全局管理"; return info; }
            if (id == "home") { info.Production = "休息一天：结算生产，推进至下一天"; info.Status = "第" + state.Day + "天 · " + (state.TodayFoodMissing > 0 ? "今天缺粮" : "今天口粮充足"); return info; }
            if (id == "barracks") { info.Production = "全军 " + state.Workers.Sum(w => w.Count) + "兵／" + state.Leaders.Count + "队长；士兵休养 " + state.Workers.Where(w => w.Resting).Sum(w => w.Count) + "人"; info.Status = "查看名册与士兵休养；队长每日自然恢复"; return info; }
            if(id!="farm"&&id!="workshop"){info.Status=obj.Kind=="npc"?"人物 · 接近后交谈":obj.VisualOnly?"场景装饰":"接近后交互";return info;}
            var manager = BaseEconomy.Manager(state, id); var preview = BaseEconomy.Preview(state);
            info.Administrator = manager == null ? "无可生产的管理员" : BasePersonnel.LeaderName(manager) + "（产能+" + (BaseDevelopment.Bonus(manager, id) * 100).ToString("0.#") + "%）";
            info.Workers = BaseEconomy.WorkersAt(state, id) + "人（近战" + BaseEconomy.WorkersAt(state, id, BaseTroopKind.Melee) + "／弓兵" + BaseEconomy.WorkersAt(state, id, BaseTroopKind.Ranged) + "／骑兵" + BaseEconomy.WorkersAt(state, id, BaseTroopKind.Cavalry) + "）";
            info.Production = id == "farm" ? "今日预计产粮 " + preview.FoodProduced + "；累计进度 " + state.FarmFraction.ToString("0.##")
                : "今日预计" + preview.ProductLevel + "级" + BaseEconomy.ProductName(preview.Product) + " " + preview.ProductsMade + "件；消耗原料 " + preview.MaterialsSpent;
            info.Status = BaseEconomy.ProductionPaused(state,id) ? "开发者已暂停产出" : BaseEconomy.EligibleWorkers(state, id) == 0 ? "无人可生产" : id == "workshop" && preview.ProductsMade < preview.WorkCapacity ? "原料不足" : "正常生产";
            info.Status += " · 今日生产人数 " + BaseEconomy.EligibleWorkers(state, id) + "/" + state.Muster.Facilities.Single(f => f.FacilityId == id).Capacity + " · 缺粮产能系数 " + (preview.ProductionFactor * 100).ToString("0.#") + "%";
            return info;
        }
    }
}

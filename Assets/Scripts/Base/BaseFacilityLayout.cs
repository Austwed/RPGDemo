using System;
using System.Collections.Generic;
using System.Linq;
using BaseConquest.Battle;

namespace BaseConquest.Base
{
    // 基地设施的定义（P40，2026-11-06）：玩法 §11.1A 的放置／收存只需要“尺寸、是否必需、上限、费用”。
    // 尺寸沿用现有营地布局；办公与商队尚无正式美术，先按 2×2 占位，费用与岗位数为待平衡占位值。
    public sealed class BaseFacilityDef
    {
        public readonly string Id, Name, Asset;
        public readonly double Width, Height;
        public readonly bool Required;
        public readonly int MaxCount, Materials, Coins, Capacity, MinimumWorkers;
        public BaseFacilityDef(string id, string name, string asset, double width, double height, bool required, int maxCount, int materials, int coins, int capacity = 0, int minimumWorkers = 0)
        {
            Id = id; Name = name; Asset = asset; Width = width; Height = height; Required = required;
            MaxCount = maxCount; Materials = materials; Coins = coins; Capacity = capacity; MinimumWorkers = minimumWorkers;
        }
    }
    // 只记录“已收存”的设施。已放置的设施以营地布局里的对象为准，避免第二份坐标状态。
    [Serializable] public sealed class BaseLayoutState
    {
        public int Version, Revision;
        // 领主在首防后赠送办公设施与商队建设许可；许可到手后玩家回营地自行放置。
        public bool OfficeGift;
        // 收存办公设施时暂停的策略值：保留给界面显示，重新放置后须玩家确认才恢复。
        public string PausedPolicy = "";
        public List<string> Stored = new List<string>();
        public BaseLayoutState Copy() => new BaseLayoutState { Version = Version, Revision = Revision, OfficeGift = OfficeGift, PausedPolicy = PausedPolicy ?? "", Stored = (Stored ?? new List<string>()).ToList() };
    }
    public static class BaseFacilityLayout
    {
        public const int Version = 1;
        // 数值全部是让流程能跑通的占位：粮田／工坊容量沿用配表 07b，办公免费，商队材料费与岗位数待平衡（P100）。
        public static readonly BaseFacilityDef[] Catalog =
        {
            new BaseFacilityDef("home", "营帐", "home-small", 3, 2, true, 1, 0, 0),
            new BaseFacilityDef("farm", "粮田", "farm", 5, 3, false, 1, 0, 0, 6, 2),
            new BaseFacilityDef("workshop", "工坊", "workshop", 4, 3, false, 1, 0, 0, 6, 2),
            new BaseFacilityDef("barracks", "兵营", "barracks", 3.2, 2, true, 1, 0, 0),
            new BaseFacilityDef("office", "办公设施", "office", 2, 2, false, 1, 0, 0),
            new BaseFacilityDef("caravan", "商队", "caravan", 2, 2, false, 1, 12, 0, 3, 1)
        };
        public static BaseFacilityDef Find(string id) => Catalog.FirstOrDefault(def => def.Id == id);
        public static void Normalize(BaseState s)
        {
            if (s == null) return;
            if (s.Layout == null) s.Layout = new BaseLayoutState { Version = Version };
            if (s.Layout.Version == 0) s.Layout.Version = Version;
            if (s.Layout.Stored == null) s.Layout.Stored = new List<string>();
            // 商队是后加的设施：旧档与未建成的新档都要在军营里补一条岗位记录，收存后移除。
            BaseMuster.EnsureCaravan(s);
        }
        public static void Validate(BaseState s)
        {
            Normalize(s);
            var layout = BaseLayout(s);
            var state = s.Layout;
            if (state.Version != Version || state.Revision < 0 || state.Revision >= int.MaxValue || state.Stored.Count > Catalog.Length) throw new ArgumentException("设施布局版本或收存清单非法");
            if (state.Stored.Distinct().Count() != state.Stored.Count || state.Stored.Any(id => Find(id) == null || Find(id).Required)) throw new ArgumentException("收存设施非法");
            if (!string.IsNullOrEmpty(state.PausedPolicy) && !BaseDevelopment.Policies.Contains(state.PausedPolicy)) throw new ArgumentException("暂停策略值非法");
            // 剧情场景／返乡村庄会临时换掉营地布局；此时没有营地可校验，保留收存清单原样。
            // 序章村庄仍占用 base 区域，用村舍 id 识别，不对它校验营地必需设施。
            if (layout == null || layout.Objects.Any(obj => obj != null && obj.Id == "village_house")) return;
            foreach (var def in Catalog)
            {
                int placed = layout.Objects.Count(obj => obj != null && obj.Id == def.Id);
                if (placed > def.MaxCount) throw new ArgumentException("设施数量超过上限：" + def.Name);
                if (def.Required && placed != 1) throw new ArgumentException("必需设施必须留在地图：" + def.Name);
                if (def.Required && state.Stored.Contains(def.Id)) throw new ArgumentException("必需设施不能收存：" + def.Name);
            }
            if (layout.Objects.Any(obj => obj != null && obj.Id == "caravan" && obj.VisualOnly)) throw new ArgumentException("商队建成后不应是占位外观");
        }
        public static BaseMapLayout BaseLayout(BaseState s) => s?.Campaign?.Layouts?.FirstOrDefault(map => map.Region == "base");
        public static bool Built(BaseState s, string id) => BaseLayout(s)?.Objects.Any(obj => obj != null && obj.Id == id) == true;
        public static bool Stored(BaseState s, string id) => s?.Layout?.Stored?.Contains(id) == true;
        public static bool Owned(BaseState s, string id) => Built(s, id) || Stored(s, id);
        public static int Count(BaseState s, string id) => BaseLayout(s)?.Objects.Count(obj => obj != null && obj.Id == id) ?? 0;
        public static bool Editable(BaseState s) => s != null && !s.Aftermath.GameOver && !s.Campaign.HasActiveBattle && !s.Muster.Parties.Any(p => p.InCombat) && !BaseSceneCombat.Active(s) && s.Campaign.Location == "base";
        internal static bool CanOrder(BaseState s) => BaseDemoFlow.Active(s) && BaseDemoDefense.Delivered(s) &&
            !BaseBattleLoot.Pending(s) && !BaseDemoPrologue.Village(s);
        internal static IEnumerable<BaseFacilityDef> CampProjects(BaseState s) => Catalog.Where(def => !def.Required &&
            (def.Id != "workshop" || BaseDemoDefense.Repaired(s, def.Id)) &&
            (def.Id != "office" || !BaseDemo.Active(s)) &&
            (def.Id != "office" && def.Id != "caravan" || Owned(s, def.Id) || s.Layout.OfficeGift));
        public static bool OfficeBuilt(BaseState s) => Built(s, "office");
        private static bool Overlaps(BaseMapPlacement obj, double x, double y, double width, double height)
            => Math.Abs(obj.X - x) < (obj.Width + width) / 2 - 1e-6 && Math.Abs(obj.Y - y) < (obj.Height + height) / 2 - 1e-6;
        // 细格吸附：中心坐标落在 0.5 的整数倍上，占地也按 0.5 取整。
        private static bool Snapped(double value) => Math.Abs(value * 2 - Math.Round(value * 2)) < 1e-6;
        public static string Problem(BaseState s, string id, double x, double y)
        {
            var def = Find(id);
            if (def == null) return "未知设施";
            if (id == "office" && BaseDemo.Active(s)) return "发展策略在营帐调整";
            if (!Editable(s)) return "只能在营地和平状态下调整布局";
            // 办公与商队要在首防后从领主处领到建设许可，不能提前白拿。
            if ((id == "office" || id == "caravan") && !Owned(s, id) && s.Layout?.OfficeGift != true) return "尚未取得建设许可：首防结束后由领主赠送";
            if (!Snapped(x) || !Snapped(y)) return "位置需按半格吸附";
            var layout = BaseLayout(s);
            if (layout == null) return "缺少营地布局";
            var existing = layout.Objects.FirstOrDefault(obj => obj.Id == id);
            double halfW = (existing?.Object.BoundsWidth ?? def.Width) / 2, halfH = (existing?.Object.BoundsHeight ?? def.Height) / 2;
            if (x - halfW < -.001 || y - halfH < -.001 || x + halfW > layout.Columns + .001 || y + halfH > layout.Rows + .001) return "超出营地边界";
            if (!string.IsNullOrEmpty(existing?.Shape) &&
                (Math.Abs(x - halfW - Math.Round(x - halfW)) > 1e-6 || Math.Abs(y - halfH - Math.Round(y - halfH)) > 1e-6))
                return "位置需对齐地格";
            if (Math.Abs(x - layout.SpawnX) < halfW + .5 && Math.Abs(y - layout.SpawnY) < halfH + .5) return "不能压在营地出生点";
            if (Math.Abs(x - s.PlayerX) < halfW + .5 && Math.Abs(y - s.PlayerY) < halfH + .5) return "不能压在角色身上";
            var others = layout.Objects.Where(obj => obj != null && obj.Id != id && (obj.Blocks || !obj.VisualOnly)).ToArray();
            if (others.Any(obj => Overlaps(obj, x, y, halfW * 2, halfH * 2))) return "与其他设施或人物占地重叠";
            var board = BaseBattlefield.Board(s, "base");
            var cells = board.Positions().Where(p => Math.Abs(p.X + .5 - x) < halfW + .5 - 1e-6 && Math.Abs(p.Z + .5 - y) < halfH + .5 - 1e-6).Select(p => board.Cell(p)).ToArray();
            if (cells.Length == 0) return "脚下没有可用地块";
            if (cells.Any(cell => (!cell.Walkable && (cell.Terrain != TerrainKind.Building || cell.BuildingId != id)) || cell.Ramp != RampAxis.None)) return "需要平整可通行地块";
            if (cells.Select(cell => cell.Elevation).Distinct().Count() > 1) return "设施地基需同高";
            if (!Owned(s, id) && (s.Inventory.Materials < def.Materials || s.Inventory.Coins < def.Coins)) return "材料不足：需要原料" + def.Materials + "、钱币" + def.Coins;
            return "";
        }
        public static bool Place(BaseState s, string id, double x, double y, out string message)
        {
            message = Problem(s, id, x, y);
            if (message != "") return false;
            var def = Find(id);
            var layout = BaseLayout(s);
            bool owned = Owned(s, id);
            var obj = layout.Objects.FirstOrDefault(item => item != null && item.Id == id);
            if (obj == null)
            {
                obj = new BaseMapPlacement { Id = id, SourceObjectId = "camp-layout-" + id, AssetId = def.Asset, Kind = "facility", Blocks = true, Width = def.Width, Height = def.Height };
                layout.Objects.Add(obj);
            }
            // 未修复的工坊／兵营必须保持占位外观，否则会绕过修复流程。
            obj.Name = def.Name; obj.X = x; obj.Y = y;
            obj.VisualOnly = BaseDemo.Active(s) && (id == "workshop" || id == "barracks") && !BaseDemoDefense.Repaired(s, id);
            if (BaseDemo.Active(s)) RefreshFootprint(s, id, obj);
            s.Layout.Stored.Remove(id);
            if (!owned) { s.Inventory.Materials -= def.Materials; s.Inventory.Coins -= def.Coins; }
            s.Layout.Revision++;
            if (id == "caravan") BaseMuster.EnsureCaravan(s);
            BaseDevelopment.Allocate(s);
            message = def.Name + "已放置在（" + x.ToString("0.#") + "，" + y.ToString("0.#") + "）";
            return true;
        }
        public static bool Store(BaseState s, string id, out string message)
        {
            var def = Find(id);
            message = "";
            if (def == null) { message = "未知设施"; return false; }
            if (!Editable(s)) { message = "只能在营地和平状态下调整布局"; return false; }
            if (def.Required) { message = "必需设施不能收存"; return false; }
            if (BaseDemo.Active(s) && (id == "workshop" || id == "barracks") && !BaseDemoDefense.Repaired(s, id)) { message = "尚未修复的设施不能收存"; return false; }
            var layout = BaseLayout(s);
            var obj = layout?.Objects.FirstOrDefault(item => item != null && item.Id == id);
            if (obj == null) { message = "该设施不在营地上"; return false; }
            layout.Objects.Remove(obj);
            if (BaseDemo.Active(s)) RefreshFootprint(s, id, null);
            if (!s.Layout.Stored.Contains(id)) s.Layout.Stored.Add(id);
            s.Layout.Revision++;
            // 收存办公设施：暂停策略但保留设置，其他岗位保持现状（先落成手动意向再重排）。
            if (id == "office") BaseDevelopment.PausePolicy(s);
            if (id == "caravan") { BaseCaravan.Stop(s, "商队设施已收存，事务结束"); BaseMuster.RemoveCaravan(s); }
            foreach (var worker in s.Workers.Where(w => !w.VillageResident && w.FacilityId == id)) { worker.FacilityId = ""; worker.DesiredFacility = ""; worker.PostLocked = false; }
            foreach (var leader in s.Leaders.Where(l => !l.VillageResident && l.FacilityId == id)) { leader.FacilityId = ""; leader.DesiredFacility = ""; leader.PostLocked = false; }
            BaseDevelopment.Allocate(s);
            message = def.Name + "已收存";
            return true;
        }
        internal static void RefreshFootprint(BaseState state, string id, BaseMapPlacement placement, string region = "base")
        {
            var board = BaseBattlefield.Board(state, region);
            foreach (var cell in board.Cells.Where(c => c.Terrain == TerrainKind.Building && c.BuildingId == id))
            {
                cell.Terrain = TerrainKind.Ground;
                cell.BuildingId = "";
            }
            if (placement == null) return;
            foreach (var position in BaseObjectFootprint.Cells(placement.Object))
            {
                var cell = board.Cell(position);
                cell.Terrain = TerrainKind.Building;
                cell.BuildingId = id;
                cell.Zone = DeploymentZone.None;
            }
        }
        // 临时入口：按固定顺序找第一个合法位置。正式建设界面等原型定稿后再改成玩家拖放。
        public static bool AutoPlace(BaseState s, string id, out string message, out double x, out double y)
        {
            var layout = BaseLayout(s); x = 0; y = 0;
            if (id == "office" && BaseDemo.Active(s)) { message = "发展策略在营帐调整"; return false; }
            if (layout == null) { message = "缺少营地布局"; return false; }
            for (double row = 1; row < layout.Rows; row += .5)
                for (double column = 1; column < layout.Columns; column += .5)
                    if (Problem(s, id, column, row) == "") { x = column; y = row; return Place(s, id, x, y, out message); }
            message = "营地内没有足够空地放置" + (Find(id)?.Name ?? id);
            return false;
        }
        public static string Snapshot(BaseState s)
        {
            var layout = BaseLayout(s);
            return (s.Layout == null ? "layout:legacy" : "layout:" + s.Layout.Revision + ":" + string.Join(",", s.Layout.Stored)) + "|" +
                string.Join(",", (layout?.Objects ?? new List<BaseMapPlacement>()).Where(obj => Find(obj.Id) != null).Select(obj => obj.Id + ":" + obj.X.ToString("R") + ":" + obj.Y.ToString("R") + ":" + obj.VisualOnly));
        }
    }
}

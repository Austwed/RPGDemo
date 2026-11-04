using System;
using System.Linq;

namespace BaseConquest.Base
{
    // 领地地图面板（P32）：按场景列出口，点出口即传送到它旁边。
    // 版式等原型定稿后替换，这里只保证数据与入口可用。
    public sealed partial class BaseHud
    {
        public void OpenLandMap()
        {
            if (owner.State == null) return;
            interactionObject = "";
            var s = owner.State;
            var nodes = BaseLandMap.Nodes(s);
            BeginPanel("领地地图", "landmap");
            RowButton("返回", -290, 270, ClosePanel);
            RowButton("规则 i", 0, 270, () => RuleInfo("领地地图", "基地、外围、城镇、野外与遗迹内部同属这块领地，进出靠各自出入口。地图上的蓝点是地表出入口、黄点通往内嵌场景；点出口旁边的传送标记直接过去，不消耗日程。遗迹内部嵌在野外，得从野外的调查点进。世界旅行走基地南侧的斥候，不在本图范围。", OpenLandMap));
            RowButton("世界地图", 290, 270, () => { ClosePanel(); owner.Travel("world"); }, BaseLandMap.WorldEntry(s));
            row += 64;
            if (nodes.Count == 0) { Line("当前没有可显示的领地场景。", 80); EndPanel(); return; }
            var current = nodes.FirstOrDefault(node => node.Current);
            Line("当前：" + (current == null ? BaseCampaign.LocationName(s.Campaign.Location) : current.Title) + " · 主角 " + s.PlayerX.ToString("0.#") + "／" + s.PlayerY.ToString("0.#"), 48, 20);
            foreach (var node in nodes)
            {
                string state = node.Current ? "当前所在" : BaseLandMap.Problem(s, node.Key);
                Line((node.Inside ? "〔内嵌〕" : "") + node.Title + " · " + (state == "" ? "可传送" : state), 52, 20);
                if (node.Exits.Count == 0) Line("这里没有通往其他场景的出入口。", 40, 17);
                foreach (var exit in node.Exits)
                {
                    var captured = exit; var target = node.Key;
                    RowButton((captured.Inside ? "黄点 · " : "蓝点 · ") + captured.Label + " → " + captured.TargetTitle, 0, 700, () =>
                    {
                        if (!captured.Available) { owner.ReportMessage(captured.Problem); return; }
                        owner.MapTeleport(target, captured.ObjectId);
                    }, captured.Available);
                    row += 52;
                }
                row += 6;
            }
            Line("出口以实际交互位置为准；点击后落在该出入口旁边，不消耗日程。跨领地旅行仍走基地南侧斥候。", 56, 17);
            EndPanel();
        }
        // 场景里按 M 打开；没有地图可用时给一句原因，不静默失败。
        public void ToggleLandMap()
        {
            if (PanelOpen) { ClosePanel(); return; }
            if (owner.State == null) return;
            if (!BaseLandMap.Available(owner.State)) { owner.ReportMessage("当前阶段还不能查看领地地图"); return; }
            OpenLandMap();
        }
    }
}

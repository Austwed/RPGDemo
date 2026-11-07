using System;
using System.Collections.Generic;
using System.Linq;
using BaseConquest.Presentation;
using UnityEngine;
using UnityEngine.UI;

namespace BaseConquest.Base
{
    // 领地地图面板（P32）：一个场景一块，块内按出口列传送标记，点一下落到它旁边。
    // 字段口径见《基地征服RPG-UI实施说明书（Unity）》§3.2：五场景块、蓝黄点图例、当前位置 X/Y、出口旁传送。
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
            RowButton("规则 i", 0, 270, () => RuleInfo("领地地图", "基地、外围、城镇、野外与遗迹内部同属这块领地，进出靠各自出入口。地图上的蓝点是地表出入口、黄点通往内嵌场景；点出口旁的传送标记直接过去，不消耗日程。遗迹内部嵌在野外，得从野外的调查点进。世界旅行走基地南侧的斥候，不在本图范围。", OpenLandMap));
            RowButton("世界地图", 290, 270, () => { ClosePanel(); owner.Travel("world"); }, BaseLandMap.WorldEntry(s));
            row += 64;
            if (nodes.Count == 0) { Line("当前没有可显示的领地场景。", 60, DemoUiMetrics.FontBody); EndPanel(); return; }

            var current = nodes.FirstOrDefault(node => node.Current);
            Line("<color=#7FB6D9>●</color> 蓝点 ＝ 地表出入口　　<color=#E8C15A>●</color> 黄点 ＝ 通往内嵌场景（遗迹内部一类）", 24, DemoUiMetrics.FontBody);
            Line("当前：" + (current == null ? BaseCampaign.LocationName(s.Campaign.Location) : current.Title) + " · 主角 " + s.PlayerX.ToString("0.#") + "／" + s.PlayerY.ToString("0.#"), 28, DemoUiMetrics.FontSubhead);
            Line("点击出口旁的传送标记即落到该出入口旁，不消耗日程；跨领地旅行仍走基地南侧斥候。", 22, DemoUiMetrics.FontCaption);
            row += 8;
            foreach (var node in nodes) SceneBlock(s, node);
            EndPanel();
        }

        // 一个场景块：标题 + 状态 + 可用出口（两列）+ 不可用出口及原因（逐行）。
        // 出口只列「通往别的场景」的；指向自己所在场景的自环不占位置。
        private void SceneBlock(BaseState s, BaseLandMapNode node)
        {
            var open = node.Exits.Where(exit => exit.Available && exit.Problem != BaseLandMap.SamePlace).ToList();
            var closed = node.Exits.Where(exit => !exit.Available && exit.Problem != BaseLandMap.SamePlace).ToList();
            int rows = (open.Count + 1) / 2;
            float chipsHeight = rows > 0 ? 6 + rows * 44 + (rows - 1) * 8 : 0;
            float closedHeight = closed.Count > 0 ? 6 + closed.Count * 20 : 0;
            float emptyHeight = node.Exits.Count == 0 ? 6 + 20 : 0;
            float height = 12 + 26 + chipsHeight + closedHeight + emptyHeight + 14;
            var block = Box("SceneBlock", content, new Vector2(.5f, 1), new Vector2(0, -row - height / 2), new Vector2(850, height), new Color(.16f, .15f, .14f, .72f));
            var outline = block.gameObject.GetOrAdd<Outline>();
            outline.effectColor = new Color(.72f, .55f, .32f, node.Current ? .85f : .4f);
            outline.effectDistance = new Vector2(1, -1);

            Label(block, (node.Inside ? "〔内嵌〕" : "") + node.Title, new Vector2(276, -12), new Vector2(520, 26), DemoUiMetrics.FontSubhead, TextAnchor.MiddleLeft, new Vector2(0, 1));
            string problem = node.Current ? "当前所在" : BaseLandMap.Problem(s, node.Key);
            var status = Label(block, problem == "" ? "可传送" : problem, new Vector2(-164, -12), new Vector2(300, 26), DemoUiMetrics.FontBody, TextAnchor.MiddleRight, new Vector2(1, 1));
            status.color = node.Current ? DemoUiTheme.Current.Copper : problem == "" ? new Color(.55f, .80f, .60f) : DemoUiTheme.Current.Muted;

            for (int i = 0; i < open.Count; i++)
            {
                var exit = open[i];
                // 只有一个出口时铺满整行，避免大片空框。
                bool single = open.Count == 1;
                float x = single ? 425 : i % 2 == 0 ? 218.5f : 631.5f;
                float y = -(12 + 26 + 6 + (i / 2) * 52 + 22);
                var button = MakeButton(block, (exit.Inside ? "黄点 · " : "蓝点 · ") + exit.Label + " → " + exit.TargetTitle,
                    new Vector2(x, y), new Vector2(single ? 818 : 405, 44), () => owner.MapTeleport(node.Key, exit.ObjectId), new Vector2(0, 1));
                var text = button.GetComponentInChildren<Text>();
                if (text != null) { text.fontSize = DemoUiMetrics.FontBody; text.horizontalOverflow = HorizontalWrapMode.Overflow; }
            }

            float lineY = -(12 + 26 + chipsHeight + 6 + 10);
            if (node.Exits.Count == 0)
            {
                var empty = Label(block, "这里没有通往其他场景的出入口。", new Vector2(425, lineY), new Vector2(818, 20), DemoUiMetrics.FontCaption, TextAnchor.MiddleLeft, new Vector2(0, 1));
                empty.color = DemoUiTheme.Current.Muted;
            }
            foreach (var exit in closed)
            {
                var line = Label(block, "○ " + (exit.Inside ? "黄点 · " : "蓝点 · ") + exit.Label + " → " + exit.TargetTitle + " · " + exit.Problem,
                    new Vector2(425, lineY), new Vector2(818, 20), DemoUiMetrics.FontCaption, TextAnchor.MiddleLeft, new Vector2(0, 1));
                line.color = DemoUiTheme.Current.Muted;
                lineY -= 20;
            }
            row += height + 12;
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

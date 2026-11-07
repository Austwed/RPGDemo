using System;
using System.Collections.Generic;
using System.Linq;
using BaseConquest.Presentation;
using UnityEngine;
using UnityEngine.UI;

namespace BaseConquest.Base
{
    // 领地地图面板（P32）：照原型 `case 'map'` 做成一排场景块——
    // 区域名在上、节点框在下，出口按场景坐标换算成圆点贴在框里；遗迹内部嵌在野外块内。
    // 字段口径见《基地征服RPG-UI实施说明书（Unity）》§3.2。
    public sealed partial class BaseHud
    {
        // 原型：territory-node 130 高、territory-link 23 宽、块宽按剩余宽度均分。
        private const float MapTileHeight = 150, MapLinkWidth = 24;
        private static readonly Color MapBlue = new Color(.30f, .56f, .80f);
        private static readonly Color MapYellow = new Color(.89f, .71f, .24f);
        private static readonly Color MapOff = new Color(.42f, .42f, .40f);
        private static readonly Color MapRing = new Color(1f, 1f, 1f);

        public void OpenLandMap()
        {
            if (owner.State == null) return;
            interactionObject = "";
            var s = owner.State;
            var nodes = BaseLandMap.Nodes(s);
            BeginPanel("领地地图", "landmap");
            var info = MakeButton(content, "i", new Vector2(421, -17), new Vector2(34, 34), () => RuleInfo("领地地图", "基地、外围、城镇、野外与遗迹内部同属这块领地，进出靠各自出入口。地图上的蓝点是地表出入口、黄点通往内嵌场景；点出口旁的标记直接过去，不消耗日程。遗迹内部嵌在野外，得从野外的调查点进。世界旅行走基地南侧的斥候，不在本图范围。", OpenLandMap), new Vector2(.5f, 1));
            var infoLabel = info.GetComponentInChildren<Text>();
            if (infoLabel != null) infoLabel.fontSize = DemoUiMetrics.FontBody;

            var outside = nodes.Where(node => !node.Inside).ToList();
            if (outside.Count == 0)
            {
                Line("当前没有可显示的领地场景。", 40, DemoUiMetrics.FontBody);
                EndPanel();
                return;
            }

            float tileWidth = Mathf.Clamp((880 - MapLinkWidth * (outside.Count - 1)) / (float)outside.Count, 150, 240);
            float total = tileWidth * outside.Count + MapLinkWidth * (outside.Count - 1);
            float left = -total * .5f;
            float nameY = -68, tileCenterY = -(82 + MapTileHeight * .5f);
            for (int i = 0; i < outside.Count; i++)
            {
                float centerX = left + tileWidth * .5f + i * (tileWidth + MapLinkWidth);
                DrawMapTile(s, nodes, outside[i], centerX, nameY, tileCenterY, tileWidth);
                if (i < outside.Count - 1)
                {
                    // 场景之间的连接线：原型是 23×2 的短横线，中线对齐节点框。
                    var link = Solid("MapLink", content, new Vector2(.5f, 1), new Vector2(centerX + tileWidth * .5f + MapLinkWidth * .5f, tileCenterY), new Vector2(MapLinkWidth, 2), new Color(.553f, .663f, .557f));
                    link.gameObject.GetComponent<Image>().raycastTarget = false;
                }
            }

            float subY = tileCenterY - MapTileHeight * .5f - 16;
            var current = outside.FirstOrDefault(node => node.Current) ?? nodes.FirstOrDefault(node => node.Current);
            var pos = Label(content, "当前位置：" + (current == null ? BaseCampaign.LocationName(s.Campaign.Location) : current.Title) + " · X " + s.PlayerX.ToString("0.#") + "，Y " + s.PlayerY.ToString("0.#"), new Vector2(0, subY), new Vector2(850, 20), DemoUiMetrics.FontBody, TextAnchor.MiddleLeft, new Vector2(.5f, 1));
            pos.color = DemoUiTheme.Current.Ink;
            var legend = Label(content, "蓝点为地表出入口，黄点为遗迹内部进出位置；点击标记传送至入口附近，不消耗日程。", new Vector2(0, subY - 24), new Vector2(850, 20), DemoUiMetrics.FontCaption, TextAnchor.MiddleLeft, new Vector2(.5f, 1));
            legend.color = DemoUiTheme.Current.Muted;

            MakeButton(content, "世界地图 →", new Vector2(0, subY - 70), new Vector2(240, 42), () => { ClosePanel(); owner.Travel("world"); }, new Vector2(.5f, 1)).interactable = BaseLandMap.WorldEntry(s);
            EndPanel();
        }

        // 一个场景块：区域名在上，节点框内按场景坐标摆出口圆点与主角点；嵌在里面的小场景另画一个框。
        private void DrawMapTile(BaseState s, List<BaseLandMapNode> all, BaseLandMapNode node, float centerX, float nameY, float centerY, float width)
        {
            string problem = node.Current ? "" : BaseLandMap.Problem(s, node.Key);
            bool open = problem == "";
            var title = Label(content, ShortTitle(node.Title) + (node.Current ? " · 当前位置" : open ? "" : " · 未开放"), new Vector2(centerX, nameY), new Vector2(width, 20), DemoUiMetrics.FontBody, TextAnchor.MiddleCenter, new Vector2(.5f, 1));
            title.color = node.Current ? DemoUiTheme.Current.Copper : open ? DemoUiTheme.Current.Ink : DemoUiTheme.Current.Muted;
            title.fontStyle = FontStyle.Bold;

            var fill = node.Current ? new Color(.882f, .929f, .855f) : open ? new Color(.992f, .996f, .973f) : new Color(.902f, .902f, .882f);
            var tile = Solid("MapTile", content, new Vector2(.5f, 1), new Vector2(centerX, centerY), new Vector2(width, MapTileHeight), fill);
            var outline = tile.gameObject.GetOrAdd<Outline>();
            outline.effectColor = node.Current ? new Color(.259f, .420f, .314f, .95f) : new Color(.655f, .725f, .647f, open ? .95f : .55f);
            outline.effectDistance = node.Current ? new Vector2(2, -2) : new Vector2(1, -1);

            foreach (var exit in node.Exits)
            {
                if (exit.Problem == BaseLandMap.SamePlace) continue;
                Vector2 at = MapPoint(exit.X, exit.Y, node, width, MapTileHeight);
                DrawMapDot(tile, at, exit.Inside ? MapYellow : MapBlue, exit.Available, () => owner.MapTeleport(node.Key, exit.ObjectId));
            }
            if (node.Current) DrawPlayerDot(tile, MapPoint(s.PlayerX, s.PlayerY, node, width, MapTileHeight), 10);

            // 嵌在父场景里的小场景（遗迹内部）：原型的 nested-ruins 方块，位置固定在节点框右下。
            foreach (var child in all.Where(item => item.Inside && item.ParentKey == node.Key))
            {
                bool inside = child.Current;
                float boxWidth = width * .42f, boxHeight = MapTileHeight * .5f;
                Vector2 center = new Vector2(width * .22f, -MapTileHeight * .04f);
                var nested = Solid("NestedScene", tile, new Vector2(.5f, .5f), center, new Vector2(boxWidth, boxHeight), inside ? new Color(.973f, .918f, .776f) : new Color(.953f, .925f, .859f));
                var nestedOutline = nested.gameObject.GetOrAdd<Outline>();
                nestedOutline.effectColor = inside ? new Color(.690f, .502f, .243f, .95f) : new Color(.553f, .475f, .341f, .75f);
                nestedOutline.effectDistance = new Vector2(1, -1);
                var nestedLabel = Label(nested, "遗迹内部", new Vector2(0, -8), new Vector2(boxWidth, 18), DemoUiMetrics.FontCaption, TextAnchor.MiddleCenter, new Vector2(.5f, 1));
                nestedLabel.color = inside ? DemoUiTheme.Current.Copper : DemoUiTheme.Current.Muted;
                // 进入入口：父场景通往内嵌场景的出口已经画在父块上，这里只补内嵌场景自己的出口。
                foreach (var exit in child.Exits)
                {
                    if (exit.Problem == BaseLandMap.SamePlace) continue;
                    Vector2 at = MapPoint(exit.X, exit.Y, child, boxWidth, boxHeight);
                    DrawMapDot(nested, at, MapYellow, exit.Available, () => owner.MapTeleport(child.Key, exit.ObjectId));
                }
                if (inside)
                {
                    Label(nested, "当前位置", new Vector2(0, -26), new Vector2(boxWidth, 16), DemoUiMetrics.FontCaption, TextAnchor.MiddleCenter, new Vector2(.5f, 1)).color = DemoUiTheme.Current.Copper;
                    DrawPlayerDot(nested, MapPoint(s.PlayerX, s.PlayerY, child, boxWidth, boxHeight), 8);
                }
            }

            if (!open)
            {
                var reason = Label(tile, problem, new Vector2(0, 9), new Vector2(width - 18, 16), DemoUiMetrics.FontCaption, TextAnchor.MiddleCenter, new Vector2(.5f, 0));
                reason.color = DemoUiTheme.Current.Muted;
            }
        }

        // 场景坐标 → 节点框内坐标：X 按列数、Y 按行数换算，Y 轴与世界一致（北在上）。
        private static Vector2 MapPoint(double x, double y, BaseLandMapNode node, float width, float height)
        {
            float px = Mathf.Clamp01((float)(x / Mathf.Max(1, node.Columns)));
            float py = Mathf.Clamp01((float)(y / Mathf.Max(1, node.Rows)));
            return new Vector2((px - .5f) * width, (py - .5f) * height);
        }

        // 出口圆点：18 px 视觉 + 44 px 透明热区（§2.4 视觉与热区分离）。
        private void DrawMapDot(RectTransform parent, Vector2 at, Color color, bool enabled, Action action)
        {
            var hit = Box("MapDot", parent, new Vector2(.5f, .5f), at, new Vector2(DemoUiMetrics.HitPc, DemoUiMetrics.HitPc), new Color(0, 0, 0, 0));
            var button = hit.gameObject.GetOrAdd<Button>(); button.targetGraphic = hit.GetComponent<Image>(); button.transition = Selectable.Transition.None;
            button.interactable = enabled;
            button.onClick.AddListener(() => { if (!owner.Busy) action(); });
            var ring = Solid("Ring", hit, new Vector2(.5f, .5f), Vector2.zero, new Vector2(20, 20), MapRing);
            ring.gameObject.GetComponent<Image>().raycastTarget = false;
            var dot = Solid("Dot", ring, new Vector2(.5f, .5f), Vector2.zero, new Vector2(18, 18), enabled ? color : MapOff);
            dot.gameObject.GetComponent<Image>().raycastTarget = false;
        }

        private void DrawPlayerDot(RectTransform parent, Vector2 at, float size)
        {
            var ring = Solid("PlayerRing", parent, new Vector2(.5f, .5f), at, new Vector2(size + 5, size + 5), MapRing);
            ring.gameObject.GetComponent<Image>().raycastTarget = false;
            var dot = Solid("Player", ring, new Vector2(.5f, .5f), Vector2.zero, new Vector2(size, size), new Color(.70f, .50f, .20f));
            dot.gameObject.GetComponent<Image>().raycastTarget = false;
        }

        private static string ShortTitle(string title)
        {
            if (string.IsNullOrEmpty(title)) return "";
            int cut = title.IndexOfAny(new[] { '·', '：', ':' });
            return cut > 0 ? title.Substring(0, cut) : title;
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

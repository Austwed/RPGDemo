using System;
using System.Collections.Generic;
using System.Linq;

namespace BaseConquest.Base
{
    // 领地地图（P32，2026-11-04 裁决）：只描述「同一领地内有哪些场景、出口通向哪里、主角在哪」。
    // 坐标是示意位置（0~1），不是画布像素；实际出入口连线以场景内交互点为准。
    public sealed class BaseLandMapExit
    {
        public string ObjectId, Label, TargetKey, TargetTitle;
        public double X, Y;
        public bool Inside;      // 黄点：通往内嵌场景（遗迹内部一类）
        public bool Available;   // 现在点得动
        public string Problem;   // 点不动的原因
    }
    public sealed class BaseLandMapNode
    {
        public string Key, Region, Title, ParentKey;
        public double X, Y;
        public int Columns, Rows;      // 场景格数，出口圆点按它换算成百分比
        public bool Inside, Current;   // Inside＝嵌在父场景里的小场景
        public List<BaseLandMapExit> Exits = new List<BaseLandMapExit>();
    }
    public static class BaseLandMap
    {
        // 出口标记 → 目标场景角色。实例组合优先读 DesignLinks，这张表只服务旧存档的固定连接。
        private static readonly Dictionary<string, string> Legacy = new Dictionary<string, string>
        {
            {"exit_east","town"},{"travel_base","base"},{"exit_west","perimeter"},{"exit_south","world"},{"travel_world","world"},
            {"demo_defense_exit","perimeter"},{"demo_defense_return","base"},
            {"demo_ruins_exit","outpost"},{"demo_ruins_return","town"},{"demo_ruins_investigate","dungeon"},{"demo_dungeon_exit","outpost"},
            {"demo_march_exit","world"},{"demo_home_world","world"},{"demo_camp_world","world"},{"demo_camp_east","world"}
        };
        // 领地内的相对地理位置：基地居中偏西，外围在基地西侧，城镇在东，遗迹在城镇以东的野外，地下回廊嵌在野外内部。
        private static void Spot(string region, out double x, out double y)
        {
            switch (region)
            {
                case "base": x = .28; y = .62; break;
                case "perimeter": x = .07; y = .62; break;
                case "town": x = .62; y = .72; break;
                case "outpost": x = .86; y = .52; break;
                case "dungeon": x = .9; y = .2; break;
                case "world": x = .5; y = .1; break;
                default: x = .5; y = .45; break;
            }
        }
        public static bool Available(BaseState s) => s?.Campaign?.Layouts != null && s.Campaign.Layouts.Count > 0 && !BaseDemoPrologue.Evacuating(s) && !BaseDemoPrologue.Pursuit(s);
        public static string CurrentKey(BaseState s) => s == null ? "" : BaseSceneInstances.Active(s) ? s.Campaign.CurrentMapId : s.Campaign.Location;
        public static bool Active(BaseState s) => BaseSceneInstances.Active(s);
        private static string KeyOf(BaseState s, BaseMapLayout map) => BaseSceneInstances.Active(s) ? map.SourceMapId : map.Region;
        private static BaseMapLayout Scene(BaseState s, string key)
        {
            if (s?.Campaign?.Layouts == null || string.IsNullOrEmpty(key)) return null;
            if (BaseSceneInstances.Active(s)) return s.Campaign.Layouts.FirstOrDefault(map => map.SourceMapId == key);
            return s.Campaign.Layouts.FirstOrDefault(map => map.Region == key);
        }
        private static string TitleOf(BaseState s, string key)
        {
            var map = Scene(s, key); if (map == null) return key;
            var node = s.Campaign.Layouts.FirstOrDefault(candidate => KeyOf(s, candidate) == key);
            return string.IsNullOrEmpty(node?.Title) ? BaseCampaign.LocationName(map.Region) : node.Title;
        }
        // 同一领地：世界旅程开启后只看当前节点登记的场景，否则就是整套 Demo 领地。
        private static IEnumerable<BaseMapLayout> Domain(BaseState s)
        {
            var maps = s.Campaign.Layouts.Where(map => map != null && map.SourceMapId != BaseBanditHideout.MapId);
            if (BaseWorldJourney.Active(s) && s.Campaign.WorldJourney?.Nodes != null)
            {
                var node = s.Campaign.WorldJourney.Nodes.FirstOrDefault(item => item.Id == s.Campaign.WorldJourney.NodeId);
                if (node?.MapIds != null && node.MapIds.Count > 0) maps = maps.Where(map => node.MapIds.Contains(map.SourceMapId));
            }
            return maps;
        }
        public static List<BaseLandMapNode> Nodes(BaseState s)
        {
            var result = new List<BaseLandMapNode>();
            if (!Available(s)) return result;
            string current = CurrentKey(s);
            foreach (var map in Domain(s))
            {
                string key = KeyOf(s, map);
                if (string.IsNullOrEmpty(key)) continue;
                double x, y; Spot(map.Region, out x, out y);
                bool inside = Nested(map);
                string parent = string.IsNullOrEmpty(map.ParentMapId) ? map.Region == "dungeon" ? "outpost" : "" : BaseSceneInstances.Active(s) ? map.ParentMapId : s.Campaign.Layouts.FirstOrDefault(item => item.SourceMapId == map.ParentMapId)?.Region ?? map.ParentMapId;
                if (inside && !string.IsNullOrEmpty(parent))
                {
                    double px, py; Spot(Scene(s, parent)?.Region ?? "", out px, out py);
                    x = px + .18; y = py - .3;
                }
                var node = new BaseLandMapNode { Key = key, Region = map.Region, Title = string.IsNullOrEmpty(map.Title) ? BaseCampaign.LocationName(map.Region) : map.Title, ParentKey = parent, X = x, Y = y, Columns = Math.Max(1, map.Columns), Rows = Math.Max(1, map.Rows), Inside = inside, Current = key == current };
                foreach (var marker in map.Objects.Where(obj => !obj.VisualOnly && !obj.Blocks))
                {
                    string target = TargetOf(s, map, marker);
                    if (string.IsNullOrEmpty(target) || target == key) continue;
                    string problem = Problem(s, target);
                    node.Exits.Add(new BaseLandMapExit
                    {
                        ObjectId = marker.Id, Label = marker.Name, TargetKey = target, TargetTitle = TitleOf(s, target),
                        X = marker.X, Y = marker.Y, Inside = InsideTarget(s, target), Available = problem == "" || problem == SamePlace, Problem = problem
                    });
                }
                result.Add(node);
            }
            return result;
        }
        public const string SamePlace = "已经在这里";
        private static string TargetOf(BaseState s, BaseMapLayout map, BaseMapPlacement marker)
        {
            var link = s.Campaign.DesignLinks?.FirstOrDefault(item => item != null && item.SourceMapId == map.SourceMapId && item.SourceObjectId == marker.SourceObjectId);
            if (link != null) return BaseSceneInstances.Active(s) ? link.TargetMapId : Scene(s, link.TargetMapId)?.Region ?? link.TargetMapId;
            return Legacy.TryGetValue(marker.Id, out var legacy) ? legacy : "";
        }
        // 遗迹内部在原型里画成野外块内的一个小框：它没有 ParentMapId 也算内嵌。
        private static bool Nested(BaseMapLayout map) => map != null && (map.Region == "custom" || map.Region == "dungeon" || !string.IsNullOrEmpty(map.ParentMapId));
        private static bool InsideTarget(BaseState s, string key) => Nested(Scene(s, key));
        // 只判「这个场景现在开放没有」，不判「有没有站在出口边」——地图点击本身就是快捷传送。
        public static string Problem(BaseState s, string key)
        {
            if (s == null || string.IsNullOrEmpty(key)) return "领地内没有这个场景";
            if (s.Aftermath.GameOver) return "本局已经结束";
            if (s.Campaign.HasActiveBattle || BaseSceneCombat.Active(s) || s.Muster.Parties.Any(party => party.InCombat)) return "先结束当前战斗";
            if (BaseBattleLoot.Pending(s)) return "先处理战果";
            if (BaseDemoPrologue.Evacuating(s) || BaseDemoPrologue.Pursuit(s)) return "先完成序章撤离";
            if (s.Demo != null && s.Demo.Stage == BaseDemoStage.Waking) return "先在营帐了解处境";
            if (key == CurrentKey(s)) return SamePlace;
            var map = Scene(s, key);
            if (map == null) return "领地内没有这个场景";
            if (map.SourceMapId == BaseBanditHideout.MapId) return BaseBanditHideout.ApproachText;
            if (map.Region == "custom" || !string.IsNullOrEmpty(map.ParentMapId))
                return map.Region == "custom" && !BaseSceneInstances.Active(s) ? "附属场景只在场景实例组合里开放" : "";
            switch (map.Region)
            {
                case "base": case "town":
                    return BaseDemoPrologue.Village(s) ? "请先完成村庄撤离与追兵剧情" : "";
                case "perimeter":
                    if (!BaseDemoDefense.Enabled(s)) return "本局没有迎敌战场";
                    return BaseDemoDefense.Due(s) || BaseDemoDefense.Won(s) ? "" : "首次入侵到达后才开放迎战";
                case "outpost":
                    return "";
                case "dungeon":
                    return BaseSceneRules.Active(s) ? "" : "还没有发现遗迹内部";
                case "world":
                    return BaseDemoFinale.Opened(s) || BaseWorldJourney.Active(s) ? "" : "返乡路线尚未开放";
                default:
                    return "";
            }
        }
        // 快捷传送：切场景并落到该出入口旁边，不消耗日程。只走「已开放」判定，不看当前站在哪。
        public static bool Teleport(BaseState s, string key, string objectId = null)
        {
            if (s == null) return false;
            string problem = Problem(s, key);
            if (problem != "" && problem != SamePlace) return false;
            var map = Scene(s, key); if (map == null) return false;
            // 只在「离开基地」时记录营地位置；传到基地不该拿出发地坐标覆盖它。
            if (s.Campaign.Location == "base") { s.Campaign.BaseX = s.PlayerX; s.Campaign.BaseY = s.PlayerY; }
            if (BaseSceneInstances.Active(s)) s.Campaign.CurrentMapId = map.SourceMapId;
            s.Campaign.Location = map.Region;
            var marker = string.IsNullOrEmpty(objectId) ? null : map.Objects.FirstOrDefault(obj => obj.Id == objectId);
            s.PlayerX = marker?.X ?? map.SpawnX; s.PlayerY = marker?.Y ?? map.SpawnY;
            // 出口点本身常被交互体积占住，向场景中心让开半步；不可站立时退回出生点。
            if (marker != null)
            {
                double inwardX = s.PlayerX + Math.Sign(map.SpawnX - s.PlayerX) * .9, inwardY = s.PlayerY + Math.Sign(map.SpawnY - s.PlayerY) * .9;
                if (BaseSpatial.Walkable(s, inwardX, inwardY, BaseSceneInstances.Active(s) ? map.SourceMapId : map.Region)) { s.PlayerX = inwardX; s.PlayerY = inwardY; }
                else if (!BaseSpatial.Walkable(s, s.PlayerX, s.PlayerY, BaseSceneInstances.Active(s) ? map.SourceMapId : map.Region)) { s.PlayerX = map.SpawnX; s.PlayerY = map.SpawnY; }
            }
            BaseSpatial.RepairGridPositions(s);
            foreach (var leader in BaseAdventure.Members(s)) { leader.UnavailableDay = s.Day; leader.FacilityId = ""; }
            BaseAdventure.Arrive(s);
            return true;
        }
        // 基地南侧斥候：世界地图入口是否已开（只做判定，入口本身仍走场景交互）。
        public static bool WorldEntry(BaseState s) => BaseDemoFinale.Opened(s) || BaseWorldJourney.Active(s);
    }
}

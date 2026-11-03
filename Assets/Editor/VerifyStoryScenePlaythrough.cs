using System;
using System.IO;
using System.Linq;
using BaseConquest.Base;
using BaseConquest.Battle;
using UnityEditor;
using UnityEngine;

namespace BaseConquest.Editor
{
    // 编辑器→游戏的接缝验收：读 story-scene-package.json，按它开新局，
    // 逐张核对六场景是否真的换成了设计里的尺寸、出生点与对象。
    public static class VerifyStoryScenePlaythrough
    {
        public static void Batch()
        {
            try { Run(); Debug.Log("[VerifyStoryScenePlaythrough] PASS"); }
            catch (Exception error) { Debug.LogError("[VerifyStoryScenePlaythrough] FAIL " + error); EditorApplication.Exit(1); }
        }

        private static void Need(bool value, string message) { if (!value) throw new InvalidOperationException(message); }

        private static void Run()
        {
            string packageFile = Path.Combine(Application.streamingAssetsPath, "StoryScene", "story-scene-package.json");
            Need(File.Exists(packageFile), "缺少 story-scene-package.json：先用编辑器导出");
            var package = JsonUtility.FromJson<BaseStoryScenePackage>(File.ReadAllText(packageFile));
            Need(package != null, "剧情场景包为空");
            package.Validate();
            Debug.Log($"[Play] 读取设计 {package.Id?.Substring(0, 8)}，六场景：{string.Join("、", package.Maps.Select(map => map.Role))}");

            string json = File.ReadAllText(Path.Combine(Application.streamingAssetsPath, "BaseScenarios", BaseDemo.BalanceTemplate));
            var balance = JsonUtility.FromJson<BaseScenarioDocument>(json);
            var config = DemoBalanceRuntime.Load();
            var state = BaseDemo.CreateOpening(balance, json, true, false, true, config.Rules, config.WorkbookSha256, package).CreateState();
            Need(state.Demo?.StoryScene != null, "新开局没有带上剧情场景包");

            // 序章：白桦村按设计安装，剧情人物由序章状态接管（不落回地图对象）。
            var villageDesign = package.Map("village");
            var village = state.Campaign.Layouts.Single(map => map.Region == "base");
            Need(village.Columns == villageDesign.Layout.Columns && village.Rows == villageDesign.Layout.Rows, $"白桦村尺寸未按设计：{village.Columns}×{village.Rows}");
            Need(village.SpawnX == villageDesign.Layout.SpawnX && village.SpawnY == villageDesign.Layout.SpawnY, "白桦村出生点未按设计");
            Need(village.Objects.Any(obj => obj.Id == "village_house"), "白桦村缺少村舍");
            Debug.Log($"[Play] 序章白桦村：{village.Columns}×{village.Rows}，对象 {village.Objects.Count}，出生点 {village.SpawnX},{village.SpawnY}");

            // 醒来后重装营地及其余区域，等价于玩家点「起身」后进入新营地。
            state.Demo.Stage = BaseDemoStage.Waking;
            BaseDemo.InstallCampMap(state);

            Check(state, package, "camp", "base");
            Check(state, package, "town", "town");
            Check(state, package, "ruins", "outpost");
            Check(state, package, "field", "perimeter");

            var bandit = package.Map("bandit");
            Need(state.Demo.Finale?.CampBoard != null, "攻营战场快照为空");
            Need(state.Demo.Finale.CampBoard.Width == bandit.Board.Width && state.Demo.Finale.CampBoard.Height == bandit.Board.Height,
                $"黑鸦寨战场尺寸未按设计：{state.Demo.Finale.CampBoard.Width}×{state.Demo.Finale.CampBoard.Height} vs 设计 {bandit.Board.Width}×{bandit.Board.Height}");
            Debug.Log($"[Play] 黑鸦寨战场 {bandit.Board.Width}×{bandit.Board.Height} 来自设计");

            foreach (var role in new[] { "camp", "town", "ruins", "field" })
            {
                var design = package.Map(role);
                var layout = Layout(state, design.Layout.Region);
                Need(layout.SpawnX == design.Layout.SpawnX && layout.SpawnY == design.Layout.SpawnY, $"{role} 出生点未按设计");
                foreach (var required in design.Layout.Objects)
                    Need(layout.Objects.Any(obj => obj.Id == required.Id), $"{role} 缺少设计对象 {required.Id}");
            }

            var home = state.Demo.Finale.Village;
            Need(home != null && home.Objects.Any(obj => obj.Id == "village_house"), "返乡村庄缺少村舍");
            Debug.Log($"[Play] 返乡村庄对象 {home.Objects.Count}，供世界道路复用");

            // 接缝的终点：设计图要能真的开战，而不只是数据对得上。
            // 用「醒来」预设档做载体，避免在验证里伪造序章完成状态。
            var play = JsonUtility.FromJson<BaseState>(Resources.Load<TextAsset>("DemoStages/camp-male").text);
            play.Demo.StoryScene = package.Copy();
            BaseDemo.InstallCampMap(play);
            Need(BaseDemo.AcknowledgeWaking(play), "醒来推进失败");
            Need(play.Demo.Stage == BaseDemoStage.VisitLord, "醒来后未进入拜访领主阶段");
            At(play, "exit_east"); Need(BaseCampaign.Travel(play, "town"), "无法进入灰岩镇");
            At(play, "town_quest"); Need(BaseDemo.AcceptLordQuest(play), "无法接取遗迹调查");
            At(play, "demo_ruins_exit"); Need(BaseCampaign.Travel(play, "outpost"), "无法进入沉眠古窟");
            At(play, "demo_ruins_investigate");
            var session = BaseDemoRuins.Begin(play, "none");
            Need(session?.Battle != null, "遗迹战斗未生成");
            var ruinsBoard = play.Campaign.RegionMaps.Single(map => map.Id == "outpost").Board;
            Need(ruinsBoard.Width == package.Map("ruins").Board.Width && ruinsBoard.Height == package.Map("ruins").Board.Height, "遗迹战斗没有用设计地格");
            Debug.Log($"[Play] 遗迹战斗已用设计场地开战：{ruinsBoard.Width}×{ruinsBoard.Height}，敌军 {session.Battle.Soldiers.Count(unit => unit.Side != BattleSide.A)} 人");
        }

        private static void At(BaseState state, string id)
        {
            var obj = BaseSpatial.Objects(state).Single(item => item.Id == id);
            var point = obj.Blocks ? BaseStageWalk.Door(state, id) : new BasePoint(obj.X, obj.Y);
            state.PlayerX = point.X; state.PlayerY = point.Y; BaseAdventure.Arrive(state);
        }

        private static BaseMapLayout Layout(BaseState state, string region) =>
            state.Campaign.Layouts.Single(map => map.Region == region);

        private static void Check(BaseState state, BaseStoryScenePackage package, string role, string region)
        {
            var design = package.Map(role);
            var layout = Layout(state, region);
            var board = region == "perimeter" ? state.Campaign.PerimeterBoard : state.Campaign.RegionMaps.Single(map => map.Id == region).Board;
            Need(layout.Columns == design.Layout.Columns && layout.Rows == design.Layout.Rows,
                $"{role} 尺寸未按设计：{layout.Columns}×{layout.Rows} vs 设计 {design.Layout.Columns}×{design.Layout.Rows}");
            Need(board.Width == design.Board.Width && board.Height == design.Board.Height, $"{role} 地格尺寸未按设计");
            Debug.Log($"[Play] {role} → {region}：{layout.Columns}×{layout.Rows}，对象 {layout.Objects.Count}，出生点 {layout.SpawnX},{layout.SpawnY}");
        }
    }
}

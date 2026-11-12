using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using BaseConquest.Base;
using BaseConquest.Battle;
using UnityEditor;
using UnityEngine;

namespace BaseConquest.Editor
{
    // 从内置 Demo 的正式开局取地图底稿，独立地图编辑器用它创建新设计。
    // 输出在 Unity 工程外，不参与打包或运行，也不覆盖已保存的设计。
    public static class ExportStorySceneSeed
    {
        private const string Output = "独立编辑器/基线/demo-story-map-seed.json";

        [Serializable]
        private sealed class Seed
        {
            public int Version = 1;
            public List<BaseDemoDesignMap> Maps = new List<BaseDemoDesignMap>();
        }

        public static void Batch()
        {
            try { Run(); Debug.Log("[ExportStorySceneSeed] PASS"); }
            catch (Exception error) { Debug.LogError("[ExportStorySceneSeed] FAIL " + error); EditorApplication.Exit(1); }
        }

        [MenuItem("SLG/导出正式地图底稿")]
        public static void Run()
        {
            string json = File.ReadAllText(Path.Combine(Application.streamingAssetsPath, "BaseScenarios", BaseDemo.BalanceTemplate));
            var balance = JsonUtility.FromJson<BaseScenarioDocument>(json);
            var config = DemoBalanceRuntime.Load();
            var state = BaseDemo.CreateOpening(balance, json, true, true, true, config.Rules, config.WorkbookSha256, null).CreateState();

            var seed = new Seed();
            var village = Add(seed, state, "village", "base");
            village.SpawnX = state.PlayerX; village.SpawnY = state.PlayerY;
            // 这些锚点由序章引导或返乡事件按阶段生成，开局 Layout 本身没有。
            village.Objects.AddRange(new[] {
                new BaseMapPlacement { Id="demo_captain", Name="搭档 · 交谈", X=state.Demo.Intro.CaptainX, Y=state.Demo.Intro.CaptainY, Width=.5, Height=.5, Kind="npc", AssetId=BaseDemoIntro.CaptainArt(state), SourceObjectId="demo-npc:captain" },
                new BaseMapPlacement { Id="intro_villager", Name="阿禾 · 交谈", X=12, Y=10, Width=.5, Height=.5, Kind="npc", AssetId="ahe", SourceObjectId="demo-npc:intro-villager" },
                new BaseMapPlacement { Id="demo_home_world", Name="返回世界道路", X=23.3, Y=7, Width=1, Height=2 },
                new BaseMapPlacement { Id="demo_broken_sword", Name="断剑 · 调查", X=10, Y=7, Width=1, Height=1 },
                new BaseMapPlacement { Id="demo_survivor", Name="幸存村民", X=8.8, Y=7.5, Width=.5, Height=.5, Kind="npc", AssetId="ahe" }
            });

            state.Demo.Stage = BaseDemoStage.Waking;
            state.Demo.PrologueCompleted = true;
            state.Demo.SceneFollowers.Clear();
            BaseDemo.InstallCampMap(state);
            var camp = Add(seed, state, "camp", "base");
            camp.SpawnX = state.PlayerX; camp.SpawnY = state.PlayerY;
            Add(seed, state, "town", "town");
            Add(seed, state, "ruins", "outpost");
            Add(seed, state, "dungeon", "dungeon");
            Add(seed, state, "field", "perimeter");
            var bandit = state.Demo.ReleaseMaps.Single(m => m.Role == "bandit").Copy();
            bandit.Board = BaseDemoFinale.CreateCampBoard(state);
            seed.Maps.Add(bandit);

            foreach (var map in seed.Maps)
            {
                if (map.Layout.Columns != map.Board.Width || map.Layout.Rows != map.Board.Height ||
                    map.Layout.Objects.Select(o => o.Id).Distinct(StringComparer.Ordinal).Count() != map.Layout.Objects.Count)
                    throw new InvalidOperationException("底稿地图尺寸或物件 ID 非法：" + map.Role);
                Debug.Log($"[Seed] {map.Role}: {map.Layout.Title} {map.Board.Width}×{map.Board.Height}, {map.Layout.Objects.Count} objects");
            }

            var args = Environment.GetCommandLineArgs();
            int option = Array.IndexOf(args, "-storySeedOutput");
            if (option >= 0 && option + 1 >= args.Length) throw new ArgumentException("-storySeedOutput requires a path");
            var output = Path.GetFullPath(option < 0 ? Path.Combine(Application.dataPath, "..", "..", Output) : args[option + 1]);
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            var temporary = output + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporary, JsonUtility.ToJson(seed) + "\n", new UTF8Encoding(false));
                if (File.Exists(output)) File.Replace(temporary, output, null);
                else File.Move(temporary, output);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
            Debug.Log("[ExportStorySceneSeed] Wrote " + output);
        }

        private static BaseMapLayout Add(Seed seed, BaseState state, string role, string region)
        {
            var layout = state.Campaign.Layouts.Single(m => m.Region == region).Copy();
            seed.Maps.Add(new BaseDemoDesignMap { Role=role, Layout=layout, Board=BaseBattlefield.Board(state, region).Clone() });
            return layout;
        }
    }
}

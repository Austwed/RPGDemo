using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using BaseConquest.Base;
using BaseConquest.Battle;
using UnityEditor;
using UnityEngine;

namespace BaseConquest.Editor
{
    // 把当前内置 Demo 布局导出成剧情场景包，作为编辑器的起点与回归样本。
    // 只在编辑器里跑，不参与运行期；坐标来源就是游戏自己，避免两边各抄一份。
    public static class ExportStorySceneSeed
    {
        private const string Output = "Assets/StreamingAssets/BaseScenarios/story-scene-seed.json";

        public static void Batch()
        {
            try { Run(); Debug.Log("[ExportStorySceneSeed] PASS"); }
            catch (Exception error) { Debug.LogError("[ExportStorySceneSeed] FAIL " + error); EditorApplication.Exit(1); }
        }

        private static void Run()
        {
            string json = File.ReadAllText(Path.Combine(Application.streamingAssetsPath, "BaseScenarios", BaseDemo.BalanceTemplate));
            var balance = JsonUtility.FromJson<BaseScenarioDocument>(json);
            var config = DemoBalanceRuntime.Load();
            var state = BaseDemo.CreateOpening(balance, json, true, true, true, config.Rules, config.WorkbookSha256, null).CreateState();

            foreach (var map in state.Campaign.Layouts)
                Debug.Log($"[Seed] layout region={map.Region} source={map.SourceMapId} title={map.Title} objects={map.Objects.Count} size={map.Columns}x{map.Rows}");
            foreach (var map in state.Campaign.RegionMaps)
                Debug.Log($"[Seed] board id={map.Id} size={map.Board.Width}x{map.Board.Height}");
            Debug.Log($"[Seed] finale={state.Demo?.Finale != null} campBoard={state.Demo?.Finale?.CampBoard?.Width}x{state.Demo?.Finale?.CampBoard?.Height}");
        }
    }
}

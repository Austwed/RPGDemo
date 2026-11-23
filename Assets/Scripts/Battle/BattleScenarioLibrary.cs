using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace BaseConquest.Battle
{
    public sealed partial class BattleSceneController
    {
        [NonSerialized] private string scenarioFolder, scenarioSaveName = "自定义方案", scenarioMessage = "";
        private readonly List<string> scenarioFiles = new List<string>();
        [NonSerialized] private BattleScenarioDocument activeScenarioDocument;
        private string activeScenarioPath;
        private void TryLoadDefaultScenario()
        {
            if (!Application.isPlaying) return;
            string path = Path.Combine(ScenarioFolder, "01_基础对峙.json"); if (File.Exists(path)) LoadScenarioFile(path);
        }
        public string ScenarioFolder => string.IsNullOrWhiteSpace(scenarioFolder) ? Path.Combine(Application.streamingAssetsPath, "BattleScenarios") : scenarioFolder;
        public IReadOnlyList<string> ScenarioFiles => scenarioFiles;
        public void RefreshScenarioFiles()
        {
            scenarioFolder = ScenarioFolder; scenarioFiles.Clear();
            try
            {
                if (!Directory.Exists(scenarioFolder)) { scenarioMessage = "方案文件夹不存在"; return; }
                foreach (var file in Directory.GetFiles(scenarioFolder, "*.json").OrderBy(Path.GetFileName, StringComparer.Ordinal))
                {
                    try { var d = JsonUtility.FromJson<BattleScenarioDocument>(File.ReadAllText(file)); if (d?.Kind == "battle" && d.Scenario != null) scenarioFiles.Add(file); }
                    catch (Exception) { /* 单个错误文件不阻止目录中的其他方案显示。 */ }
                }
                scenarioMessage = "找到 " + scenarioFiles.Count + " 张对战方案；00_说明.json仅供编写参考";
            }
            catch (Exception e) { scenarioMessage = "读取目录失败：" + e.Message; }
        }
        public bool LoadScenarioFile(string path)
        {
            if (IsCampaignBattle) return false;
            if (animating || skipping) return false;
            try
            {
                var document = JsonUtility.FromJson<BattleScenarioDocument>(File.ReadAllText(path)); BattleScenarioDocument.Validate(document);
                LoadScenarioDocument(document); activeScenarioPath = path; scenarioSaveName = Path.GetFileNameWithoutExtension(path);
                scenarioMessage = "已读取：" + document.Title; feedback = scenarioMessage; hud?.Refresh(); return true;
            }
            catch (Exception e) { scenarioMessage = "未加载：" + e.Message; feedback = scenarioMessage; return false; }
        }
        private void LoadScenarioDocument(BattleScenarioDocument document)
        {
            var prepared = document.CreateSimulation();
            autoRun = skipping = animating = false; CloseContext(); activeScenarioDocument = document; suppressInitialDeploy = true;
            ReplaceSimulation(prepared.Scenario, prepared);
            RefreshUnits(); SyncViews(); hud?.Refresh();
        }
        public bool SaveScenarioFile(string name)
        {
            if (IsCampaignBattle) return false;
            try
            {
                string fileName = Path.GetFileName(name);
                if (string.IsNullOrWhiteSpace(fileName) || fileName != name || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) throw new ArgumentException("请使用不含路径的方案名称");
                if (!fileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) fileName += ".json";
                Directory.CreateDirectory(ScenarioFolder);
                var d = new BattleScenarioDocument { Title = Path.GetFileNameWithoutExtension(fileName), Description = activeScenarioDocument?.Description ?? "由测试面板保存的开局", TestNotes = activeScenarioDocument?.TestNotes ?? new string[0], DeploymentMode = "explicit", Scenario = battle.Scenario.Clone(), StartInCombat = activeScenarioDocument?.StartInCombat ?? false, InitialHealth = activeScenarioDocument?.InitialHealth ?? new List<OpeningHealth>(), InitialEffects = activeScenarioDocument?.InitialEffects ?? new List<OpeningEffects>() };
                d.Scenario.TestAutoDeployment = false; DeveloperTerrain.Restore(d.Scenario.Board);BattleScenarioDocument.Validate(d);
                string path = Path.Combine(ScenarioFolder, fileName); string temporary = path + ".tmp";
                File.WriteAllText(temporary, JsonUtility.ToJson(d, true));
                if (File.Exists(path)) File.Replace(temporary, path, null); else File.Move(temporary, path);
                activeScenarioDocument = d; activeScenarioPath = path; scenarioMessage = "已保存开局：" + fileName; RefreshScenarioFiles(); feedback = "已保存开局：" + fileName; return true;
            }
            catch (Exception e) { scenarioMessage = "保存失败：" + e.Message; feedback = scenarioMessage; return false; }
        }
        private void DrawScenarioPanel()
        {
            var previousColor = GUI.color; GUI.color = new Color(0.04f, 0.06f, 0.085f, 1); GUI.DrawTexture(new Rect(0, 0, PanelWidth, UiHeight), Texture2D.whiteTexture); GUI.color = previousColor;
            float width = PanelWidth - 48;
            var centeredButton = new GUIStyle(buttonStyle) { alignment = TextAnchor.MiddleCenter, wordWrap = true };
            bool ActionButton(string label) => GUILayout.Button(label, centeredButton, GUILayout.Width(width), GUILayout.Height(40));
            void Note(string label) => GUILayout.Label(label, smallStyle, GUILayout.Width(width));
            GUILayout.BeginArea(new Rect(12, 12, PanelWidth - 24, UiHeight - 24));
            panelScroll = GUILayout.BeginScrollView(panelScroll, false, true, GUILayout.Width(PanelWidth - 24)); GUILayout.BeginVertical(GUILayout.Width(width));
            GUILayout.Label("对战JSON方案", titleStyle, GUILayout.Width(width)); GUILayout.Label("当前：" + (activeScenarioDocument?.Title ?? "默认场景参数"), textStyle, GUILayout.Width(width));
            GUI.SetNextControlName("ScenarioFolder"); scenarioFolder = GUILayout.TextField(ScenarioFolder, fieldStyle, GUILayout.Width(width)); if (ActionButton("刷新方案列表")) RefreshScenarioFiles();
            GUI.enabled = !animating && !skipping;
            foreach (var file in scenarioFiles.ToArray()) if (ActionButton("读取 " + Path.GetFileNameWithoutExtension(file))) LoadScenarioFile(file);
            GUILayout.Space(8); GUI.SetNextControlName("ScenarioSaveName"); scenarioSaveName = GUILayout.TextField(scenarioSaveName, fieldStyle, GUILayout.Width(width)); if (ActionButton("保存当前开局JSON")) SaveScenarioFile(scenarioSaveName);
            if (ActionButton("重开当前测试方案")) RestartTestBattle(); GUI.enabled = true;
            bool fog = GUILayout.Toggle(battle.Scenario.FogEnabled, "启用战争迷雾", centeredButton, GUILayout.Width(width)); bool reveal = GUILayout.Toggle(battle.Scenario.ShowEnemiesThroughFog, "玩家透雾显示（地形与单位）", centeredButton, GUILayout.Width(width));
            if (fog != battle.Scenario.FogEnabled || reveal != battle.Scenario.ShowEnemiesThroughFog) ToggleTestFog(fog, reveal);
            if (ActionButton("切换操作阵营·测试")) SwitchSide();
            bool assist = GUILayout.Toggle(MapDisplayOptions.BattleAssist, "战斗辅助显示", centeredButton, GUILayout.Width(width));
            if (assist != MapDisplayOptions.BattleAssist)
            {
                MapDisplayOptions.BattleAssist = assist;
                boardView.SetGridOutlines(assist);
                SyncViews();
            }
            if (RuntimeDeveloperOptions.Available)
            {
                bool developer = GUILayout.Toggle(RuntimeDeveloperOptions.Enabled, "开发者模式", centeredButton, GUILayout.Width(width));
                if (developer != RuntimeDeveloperOptions.Enabled) RuntimeDeveloperOptions.Enabled = developer;
            }
            Note(scenarioMessage);
            if (activeScenarioDocument != null) { Note(activeScenarioDocument.Description); foreach (string note in activeScenarioDocument.TestNotes ?? new string[0]) Note("• " + note); }
            Note("修改地图／数值请编辑JSON后读取；战中保存的是初始站位。");
            numericFocus = GUI.GetNameOfFocusedControl() == "ScenarioFolder" || GUI.GetNameOfFocusedControl() == "ScenarioSaveName";
            GUILayout.EndVertical(); GUILayout.EndScrollView(); GUILayout.EndArea();
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using BaseConquest.Base;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using static BaseConquest.Editor.PlanningWorkbook;
using Grid = BaseConquest.Editor.PlanningWorkbook.Grid;

namespace BaseConquest.Editor
{
    public sealed class PlanningWorkbookWindow : EditorWindow
    {
        internal const string SourcePath = "Library/PlanningWorkbook/applied.json";
        internal const string DemonstrationPath = "Library/PlanningWorkbook/调参备战.json";
        private const string Snapshot = DemoBalanceImporter.OutputPath;
        private const string Seed = PlanningBalanceImporter.OutputPath;
        private static readonly string[] Stages = { "camp-male", "camp-female", "defense-male", "defense-female" };
        private static readonly Encoding Utf8 = new UTF8Encoding(false);
        private Preview preview;
        private string message;
        private bool failed;
        private Vector2 scroll;

        internal sealed class Applied
        {
            public string Full, Effective, Master;
            public List<Binding> Edits;
        }

        internal sealed class Preview
        {
            internal string Master, MasterHash, Input, InputHash, SeedHash, SnapshotHash, SourceHash;
            internal string[] Changes;
            internal DemoBalancePackage Package;
            internal Applied State;
            internal Dictionary<string, string> Outputs;
        }

        private static string CurrentHash(string path) => File.Exists(path) ? Hash(Bytes(path)) : null;
        private static Applied ReadApplied() => File.Exists(SourcePath)
            ? JsonConvert.DeserializeObject<Applied>(File.ReadAllText(SourcePath, Utf8)) : null;
        private static JObject RulesToken(RpgRules rules) => JObject.Parse(JsonUtility.ToJson(rules));
        private static string Fingerprint(RpgRules rules) => Digest(RulesToken(rules));

        internal static RpgRules Compile(Schema schema, Grid data)
        {
            var json = BuildRules(schema, data);
            var rules = JsonUtility.FromJson<RpgRules>(json.ToString(Formatting.None));
            if (rules == null) throw new InvalidDataException("无法生成完整运行规则");
            rules.Validate();
            return rules;
        }

        internal static Preview BuildPreview(string input, string masterFile = MasterPath)
        {
            var schema = Schema.Read();
            string inputPath = Path.GetFullPath(input);
            var inputBytes = Bytes(inputPath);
            string kind = GetKind(inputBytes, schema);
            bool trial = kind == TrialKind;
            string masterPath = Path.GetFullPath(trial ? masterFile : inputPath);
            var masterBytes = trial ? Bytes(masterPath) : inputBytes;
            var grid = ReadMaster(schema, masterBytes);
            var baseline = Compile(schema, grid);
            string full = Fingerprint(baseline);
            var actualBytes = Bytes(Snapshot);
            var current = JsonUtility.FromJson<DemoBalancePackage>(Utf8.GetString(actualBytes).TrimStart('\ufeff'));
            current.Validate();
            string currentDigest = Fingerprint(current.Rules);
            string sourceHash = CurrentHash(SourcePath);
            var edits = new List<Binding>();
            if (trial)
            {
                var applied = ReadApplied();
                if ((applied == null ? currentDigest : applied.Full) != full)
                    throw new InvalidOperationException("磁盘总表的有效配置尚未完整导入。请先导入系统数值配置表，再导入试玩调参表。");
                if (applied != null && (applied.Effective != currentDigest || current.WorkbookSha256 != currentDigest))
                    throw new InvalidOperationException("当前游戏快照已由其他操作更改。请重新导入系统数值配置表。");
                edits = ReadTrial(schema, inputBytes, grid);
                Apply(schema, grid, edits);
            }
            var rules = trial ? Compile(schema, grid) : baseline;
            var effective = Fingerprint(rules);
            var package = new DemoBalancePackage { Rules = rules, WorkbookSha256 = effective };
            package.Validate();
            var diffs = Diff(schema, FromRules(schema, RulesToken(current.Rules)), FromRules(schema, RulesToken(rules)));
            var outputs = Stages.Select(stage => "Assets/Resources/DemoStages/" + stage + ".json")
                .Concat(new[] { DemonstrationPath, SourcePath }).ToDictionary(path => path, CurrentHash);
            if (outputs[SourcePath] != sourceHash) throw new IOException("导入记录在预览期间变化，请重新预览");
            return new Preview
            {
                Master = masterPath, MasterHash = Hash(masterBytes), Input = inputPath, InputHash = Hash(inputBytes),
                SnapshotHash = Hash(actualBytes), SeedHash = CurrentHash(Seed), SourceHash = sourceHash,
                Package = package, State = new Applied { Master = masterPath, Full = full, Effective = effective, Edits = edits },
                Outputs = outputs, Changes = diffs.ToArray(),
            };
        }

        private static void CheckSources(Preview import)
        {
            if (CurrentHash(import.Input) != import.InputHash || CurrentHash(import.Master) != import.MasterHash ||
                CurrentHash(Seed) != import.SeedHash || CurrentHash(Snapshot) != import.SnapshotHash ||
                CurrentHash(SourcePath) != import.SourceHash)
                throw new IOException("表格、关卡基底或游戏快照在预览后有改动。请重新预览。");
        }

        internal static void Commit(Preview import)
        {
            CheckSources(import);
            var package = import.Package;
            string config = JsonUtility.ToJson(package, true);
            var roundtrip = JsonUtility.FromJson<DemoBalancePackage>(config);
            roundtrip.Validate();
            if (Fingerprint(roundtrip.Rules) != import.State.Effective) throw new InvalidDataException("运行快照回读值不一致");
            string seed = File.ReadAllText(Seed, Utf8).TrimStart('\ufeff');
            var stages = DemoStageSaveTools.Generate(seed, package.Rules, package.WorkbookSha256);
            var files = new Dictionary<string, string>();
            foreach (var id in Stages)
            {
                var state = JsonUtility.FromJson<BaseState>(stages[id]);
                BaseEconomy.Validate(state);
                if (state.Demo.ConfigHash != package.WorkbookSha256) throw new InvalidDataException("阶段档配置来源错误 " + id);
                files.Add("Assets/Resources/DemoStages/" + id + ".json", stages[id]);
            }
            files.Add(DemonstrationPath, stages["defense-male"]);
            files.Add(SourcePath, JsonConvert.SerializeObject(import.State, Formatting.Indented));
            var configBytes = Utf8.GetBytes(config);
            DemoBalanceImporter.CommitPreparedStages(Snapshot, Hash(configBytes), files, import.Outputs,
                () => { CheckSources(import); WriteChecked(Snapshot, configBytes, import.SnapshotHash); },
                ReplaceChecked);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        }

        private static void WriteChecked(string path, byte[] contents, string expected)
        {
            var full = Path.GetFullPath(path);
            Directory.CreateDirectory(Path.GetDirectoryName(full));
            string temp = full + "." + Guid.NewGuid().ToString("N") + ".pending";
            try
            {
                File.WriteAllBytes(temp, contents);
                ReplaceChecked(temp, full, expected);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }

        private static void ReplaceChecked(string temp, string path, string expected)
        {
            for (int attempt = 0; ; attempt++)
            {
                if (CurrentHash(path) != expected) throw new IOException(Path.GetFileName(path) + " 刚被修改，请重新预览");
                try
                {
                    if (expected == null) File.Move(temp, path);
                    else File.Replace(temp, path, null);
                    return;
                }
                catch (IOException) when (attempt < 4 && File.Exists(temp))
                {
                    // Office and filesystem watchers can briefly hold a sharing lock.
                    // Recheck the expected bytes before retrying the replacement.
                    System.Threading.Thread.Sleep(50 << attempt);
                }
            }
        }

        internal static int WriteBack(string masterFile)
        {
            var applied = ReadApplied();
            if (applied?.Edits?.Count is not > 0) return 0;
            if (!string.Equals(Path.GetFullPath(masterFile), applied.Master, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("当前试玩配置来自另一份总表");
            var actual = JsonUtility.FromJson<DemoBalancePackage>(File.ReadAllText(Snapshot));
            if (applied.Effective != Fingerprint(actual.Rules) || applied.Effective != actual.WorkbookSha256)
                throw new InvalidOperationException("当前游戏快照与试玩导入记录不同");
            var schema = Schema.Read();
            var original = Bytes(masterFile);
            var data = ReadMaster(schema, original);
            var updates = applied.Edits.Select(edit => (edit, record: data.Find(edit.Table, edit.Key)))
                .Where(item => item.record == null || !JToken.DeepEquals(item.record.Values[item.edit.Column], item.edit.Value)).ToArray();
            Apply(schema, data, applied.Edits);
            var future = Compile(schema, data);
            new DemoBalancePackage { Rules = future, WorkbookSha256 = Fingerprint(future) }.Validate();
            if (updates.Length == 0) return 0;
            var patched = PlanningWorkbookFile.Patch(original, updates.Select(item =>
                (item.record.Excel, schema.Find(item.edit.Table).Get(item.edit.Column).Label, item.edit.Value)));
            if (Fingerprint(Compile(schema, ReadMaster(schema, patched))) != Fingerprint(future))
                throw new InvalidDataException("总表回写校验失败");
            WriteChecked(masterFile, patched, Hash(original));
            return updates.Length;
        }

        internal static void RebuildTrial(string masterFile, string trialFile)
        {
            var schema = Schema.Read();
            var data = ReadMaster(schema, Bytes(masterFile));
            var rules = Compile(schema, data);
            new DemoBalancePackage { Rules = rules, WorkbookSha256 = Fingerprint(rules) }.Validate();
            var expected = CurrentHash(trialFile);
            var export = PlanningWorkbookFile.Create(schema, data, true);
            if (ReadTrial(schema, export, data).Count != 0) throw new InvalidDataException("试玩表导出回读失败");
            WriteChecked(trialFile, export, expected);
        }

        internal static BaseState OpenDemonstration()
        {
            var package = JsonUtility.FromJson<DemoBalancePackage>(File.ReadAllText(Snapshot));
            var state = JsonUtility.FromJson<BaseState>(File.ReadAllText(DemonstrationPath));
            if (state?.Demo?.ConfigHash != package.WorkbookSha256)
                throw new InvalidDataException("调参备战档与当前配置不同，请用导入入口重新生成。");
            BaseEconomy.Validate(state);
            return state;
        }

        [MenuItem("SLG/其他工具/导入/导入策划表")]
        public static void Open()
        {
            var window = GetWindow<PlanningWorkbookWindow>("导入策划表");
            window.minSize = new Vector2(600, 360);
            window.Show();
        }

        private void Try(Action action)
        {
            message = null;
            failed = false;
            try { action(); }
            catch (Exception error) { preview = null; message = error.Message; failed = true; }
        }

        private void OnGUI()
        {
            using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("选择工作簿"))
                    {
                        var path = EditorUtility.OpenFilePanel("选择系统数值配置表或试玩调参表", Path.GetDirectoryName(Path.GetFullPath(MasterPath)), "xlsx");
                        if (path.Length > 0) Try(() => preview = BuildPreview(path));
                    }
                    if (GUILayout.Button("写回总表") && EditorUtility.DisplayDialog("写回总表", "把已启用的试玩改动写入总表。", "写回", "取消"))
                        Try(() => { preview = null; message = "已写回 " + WriteBack(MasterPath) + " 项。"; });
                    if (GUILayout.Button("重新生成试玩调参表") && EditorUtility.DisplayDialog("重新生成试玩调参表", "根据当前总表重新生成正式试玩表，替换该文件的内容。", "生成", "取消"))
                        Try(() => { RebuildTrial(MasterPath, TrialPath); message = "已生成试玩调参表。"; });
                    if (GUILayout.Button("载入调参备战档")) Try(() => PlanningRangeDemonstration.Open(OpenDemonstration()));
                }
                if (message != null) EditorGUILayout.HelpBox(message, failed ? MessageType.Error : MessageType.Info);
                if (preview == null) return;
                EditorGUILayout.LabelField(Path.GetFileName(preview.Input), EditorStyles.boldLabel);
                if (preview.Changes.Length == 0) EditorGUILayout.LabelField("游戏配置值一致。导入将更新来源和阶段档。");
                scroll = EditorGUILayout.BeginScrollView(scroll);
                foreach (var line in preview.Changes) EditorGUILayout.LabelField(line, EditorStyles.wordWrappedLabel);
                EditorGUILayout.EndScrollView();
                if (GUILayout.Button("确认导入并重建阶段档"))
                    Try(() => { Commit(preview); preview = null; message = "导入完成。可载入本次生成的调参备战档。"; });
            }
        }
    }
}

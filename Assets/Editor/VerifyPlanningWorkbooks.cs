using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BaseConquest.Base;
using BaseConquest.Battle;
using BaseConquest.RealtimePrototype;
using Newtonsoft.Json.Linq;
using UnityEngine;
using static BaseConquest.Editor.PlanningWorkbook;
using static BaseConquest.Editor.PlanningWorkbookWindow;
using Grid = BaseConquest.Editor.PlanningWorkbook.Grid;

namespace BaseConquest.Editor
{
    public static class VerifyPlanningWorkbooks
    {
        private const string Snapshot = DemoBalanceImporter.OutputPath;

        public static void ExportFieldReference()
        {
            if (!Directory.Exists("../.isolated-workbook-verification"))
                throw new InvalidOperationException("仅在隔离工程生成表格副本");
            var schema = Schema.Read();
            var grid = ReadMaster(schema, Bytes(MasterPath));
            Need(ReadTrial(schema, Bytes(TrialPath), grid).Count == 0, "已有试调值需要保留");
            var master = PlanningWorkbookFile.Create(schema, grid, false);
            var trial = PlanningWorkbookFile.Create(schema, grid, true);
            var exported = ReadMaster(schema, master);
            Need(JsonUtility.ToJson(Compile(schema, grid)) == JsonUtility.ToJson(Compile(schema, exported)), "字段定义或列顺序改变了有效配置");
            Need(ReadTrial(schema, trial, exported).Count == 0, "试玩批注改变了导入值");
            var edited = Edit(trial, "cfg_try_units", "mage", "职业普攻距离（细格）", new JValue(8));
            var changes = ReadTrial(schema, edited, exported);
            Need(changes.Count == 1 && changes[0].Key == "mage" && changes[0].Column == "Range", "试玩射程未准确定位");
            Apply(schema, exported, changes);
            Need(Compile(schema, exported).Range[3] == 8, "试玩射程未被读回");
            File.WriteAllBytes("../field-reference-master.xlsx", master);
            File.WriteAllBytes("../field-reference-trial.xlsx", trial);
            Debug.Log("[VerifyPlanningWorkbooks] PASS field reference export, identical full config, one-cell range patch");
        }

        public static void VerifyExcelSaved()
        {
            const string sortedMaster = "../excel-sorted-master.xlsx";
            const string editedTrial = "../excel-edited-trial.xlsx";
            var a = BuildPreview(sortedMaster);
            Need(a.Changes.Length == 0, "Excel 排序、列宽或重新保存改变了总表的有效配置");
            var b = BuildPreview(editedTrial, sortedMaster);
            Need(b.Changes.Length == 1 && b.Changes[0].Contains("6 → 8") && b.Package.Rules.Range[3] == 8,
                "Excel 保存后的试玩表未识别法师射程改动");
            Debug.Log("[VerifyPlanningWorkbooks] PASS Excel-saved and sorted master; Excel-edited trial one-cell overlay");
        }

        private static void Need(bool condition, string explanation)
        {
            if (!condition) throw new InvalidOperationException(explanation);
        }

        private static void ExpectFailure(Action action, string message)
        {
            try { action(); }
            catch (Exception error) when (error.Message.Contains(message)) { return; }
            throw new InvalidOperationException("预期拦截 " + message);
        }

        public static void Run()
        {
            if (!Directory.Exists("../.isolated-workbook-verification"))
                throw new InvalidOperationException("这项回归会更新工作簿与运行快照，只允许在隔离副本执行");
            var schema = Schema.Read();
            var sourceBytes = Bytes(Snapshot);
            var original = JsonUtility.FromJson<DemoBalancePackage>(File.ReadAllText(Snapshot));
            var data = FromRules(schema, JObject.Parse(JsonUtility.ToJson(original.Rules)));
            const string output = "../策划/DEMO";
            Directory.CreateDirectory(output);
            var master = PlanningWorkbookFile.Create(schema, data, false);
            var trial = PlanningWorkbookFile.Create(schema, data, true);
            VerifyContracts(schema, data, master, trial, original);
            File.WriteAllBytes(MasterPath, master);
            File.WriteAllBytes(TrialPath, trial);
            string alphaFile = "Library/PlanningWorkbook/alpha.xlsx";
            string betaFile = "Library/PlanningWorkbook/beta.xlsx";
            string hold = "Library/PlanningWorkbook/original.json";
            Directory.CreateDirectory(Path.GetDirectoryName(hold));
            File.WriteAllBytes(hold, sourceBytes);
            var alpha = Edit(trial, "cfg_try_units", "mage", "职业普攻距离（细格）", new JValue(8));
            var beta = Edit(trial, "cfg_try_facility", "farm", "每人日产量", new JValue(5));
            File.WriteAllBytes(alphaFile, alpha);
            File.WriteAllBytes(betaFile, beta);

            Commit(BuildPreview(MasterPath));
            var old = OpenDemonstration();
            VerifyShot(old, 6, false);
            Commit(BuildPreview(alphaFile));
            VerifyShot(OpenDemonstration(), 8, true);
            VerifyShot(old, 6, false);
            var replacement = BuildPreview(betaFile);
            Need(replacement.Changes.Length == 2 && replacement.Changes.Any(e => e.Contains("8 → 6")) &&
                replacement.Changes.Any(e => e.Contains("4 → 5")), "更换试玩表的预览遗漏恢复或粮田产量");
            Commit(replacement);
            VerifyShot(OpenDemonstration(), 6, false);
            Need(OpenDemonstration().Rules.FarmPerWorker == 5, "粮田新数值未进入阶段档");
            Need(BaseEconomy.Preview(OpenDemonstration()).FoodProduced > BaseEconomy.Preview(old).FoodProduced,
                "同一备战编制的预计产粮没有随配置增加");

            var pending = Edit(master, "cfg_units", "soldier_warrior", "生命", new JValue(111));
            File.WriteAllBytes(MasterPath, pending);
            ExpectFailure(() => BuildPreview(alphaFile), "尚未完整导入");
            Commit(BuildPreview(MasterPath));
            var staleTrial = BuildPreview(alphaFile);
            Need(staleTrial.Package.Rules.Templates.Single(t => t.Id == "soldier_warrior").Health == 111,
                "旧试玩表覆盖总表中不相关的士兵生命");
            Commit(staleTrial);
            Need(WriteBack(MasterPath) == 1 && WriteBack(MasterPath) == 0, "试玩回写必须精确、可重复");
            var written = Compile(schema, ReadMaster(schema, Bytes(MasterPath)));
            Need(written.Range[3] == 8 && written.Templates.Single(t => t.Id == "soldier_warrior").Health == 111, "总表未正确回写");

            File.WriteAllBytes(MasterPath, Edit(pending, "cfg_profession_stats", "mage", "职业普攻距离（细格）", new JValue(7)));
            Commit(BuildPreview(MasterPath));
            var stable = Hash(Bytes(Snapshot));
            ExpectFailure(() => BuildPreview(alphaFile), "同字段冲突");
            Need(Hash(Bytes(Snapshot)) == stable, "冲突改动了游戏配置");

            var extended = ReadMaster(schema, master);
            var roster = extended.Tables["cfg_units"];
            var sample = roster.Single(t => t.Id == "soldier_mage");
            var added = new Record { Id = "verification_mage", Order = roster.Count };
            foreach (var item in sample.Values) added.Values[item.Key] = item.Value.DeepClone();
            added.Values["IsDefault"] = new JValue(false);
            added.Values["Name"] = new JValue("验证法师");
            roster.Add(added);
            File.WriteAllBytes(MasterPath, PlanningWorkbookFile.Create(schema, extended, false));
            Commit(BuildPreview(MasterPath));
            Need(OpenDemonstration().Demo.Rpg.Templates.Any(t => t.Id == added.Id), "追加模板未进入阶段档");
            Commit(BuildPreview(alphaFile));
            Need(OpenDemonstration().Demo.Rpg.Templates.Any(t => t.Id == added.Id), "局部导入抹掉总表新增模板");

            File.WriteAllBytes(MasterPath, master);
            Commit(BuildPreview(MasterPath));
            RebuildTrial(MasterPath, TrialPath);
            Debug.Log("[VerifyPlanningWorkbooks] PASS full parity, 6/8 at 7 fine cells, old-stage isolation, overlay replacement, stale master gate, writeback, conflict, added unit, restore");
        }

        private static void VerifyContracts(Schema schema, Grid data, byte[] master, byte[] trial, DemoBalancePackage original)
        {
            var rules = Compile(schema, ReadMaster(schema, master));
            string actual = JsonUtility.ToJson(rules);
            string expected = JsonUtility.ToJson(original.Rules);
            if (actual != expected)
            {
                var old = JObject.Parse(expected);
                var current = JObject.Parse(actual);
                var fields = old.Properties().Where(p => !JToken.DeepEquals(p.Value, current[p.Name]))
                    .Select(p => p.Name + " " + p.Value.ToString(Newtonsoft.Json.Formatting.None) + " → " +
                        current[p.Name]?.ToString(Newtonsoft.Json.Formatting.None));
                throw new InvalidOperationException("总表迁移出现差异\n" + string.Join("\n", fields));
            }
            Need(ReadTrial(schema, trial, data).Count == 0, "原始分表存在非预期改动");
            // A sort result can reorder authored rows without changing their stable runtime order.
            var reversed = ReadMaster(schema, master);
            reversed.Tables["cfg_units"].Reverse();
            var sortedAgain = Compile(schema, ReadMaster(schema, PlanningWorkbookFile.Create(schema, reversed, false)));
            Need(JsonUtility.ToJson(sortedAgain) == actual, "Excel 排序改变了游戏配置");
            var entries = reversed.Tables["cfg_units"];
            var a = entries.First();
            var b = entries.Last();
            (a.Order, b.Order) = (b.Order, a.Order);
            var reordered = Compile(schema, reversed);
            var changes = Diff(schema, data, FromRules(schema, JObject.Parse(JsonUtility.ToJson(reordered))));
            Need(changes.Count == 2 && changes.All(line => line.Contains(OrderColumn)), "互换运行顺序须预览两行");
        }

        private static void VerifyShot(BaseState stage, double expected, bool hit)
        {
            var run = new RealtimeCampaignBattle(BaseStandaloneBattle.CreateTuningBattle(stage));
            var caster = PlanningRangeDemonstration.Caster(run);
            var target = PlanningRangeDemonstration.Target(run);
            Need(Math.Abs(RealtimeBattlePrototype.BodyGap(caster, target) * PrototypeArena.Subdivision - 7) < 1e-6,
                "固定目标与法师并非相距 7 细格");
            Need(RealtimeCampaignBattle.RangeCells(run.Session, run.Battle.Soldier(caster.Id - 1)) == expected, "阶段档法师射程不正确");
            var before = target.Health;
            var shot = PlanningRangeDemonstration.Fire(run);
            Need(hit ? shot?.DamageType == "magic" && target.Health < before : shot == null && target.Health == before,
                "固定目标的法师攻击结果与预期不同");
        }

        private static byte[] Edit(byte[] source, string table, string rowId, string header, JToken value)
        {
            using var zip = Open(source);
            var row = new ProfessionWorkbook(zip).Rows(table, new[] { IdColumn, header }, true).Single(r => r.TextField(IdColumn) == rowId);
            return PlanningWorkbookFile.Patch(source, new List<(ProfessionWorkbook.Row, string, JToken)> { (row, header, value) });
        }
    }
}

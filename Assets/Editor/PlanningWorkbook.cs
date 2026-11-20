using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BaseConquest.Editor
{
    internal sealed class PlanningWorkbook
    {
        internal const string IdColumn = "记录 ID";
        internal const string OrderColumn = "运行顺序";
        internal const string LinkColumn = "字段关联";
        internal const string SchemaPath = "Assets/Editor/PlanningWorkbookSchema.json";
        internal const string MasterPath = "../策划/DEMO/基地征服-系统数值配置表.xlsx";
        internal const string TrialPath = "../策划/DEMO/基地征服-试玩调参表.xlsx";
        internal const string MasterKind = "base-conquest-master";
        internal const string TrialKind = "base-conquest-trial";
        internal const string MetaTable = "cfg_workbook";
        internal static string BaselineColumn(string label) => "导出原值 · " + label;

        internal sealed class Field
        {
            public string Id, Label, Type, Description;
            public Field() { }
            internal Field(string id, string label, string type) { Id = id; Label = label; Type = type; }
        }

        internal sealed class FixedRow
        {
            public string Id, Label, Description;
            public string[] Paths, Types;
        }

        internal sealed class Table
        {
            public string Id, Sheet, Path, Key, Filter;
            public string[] Natural;
            public Field[] Fields;
            public FixedRow[] Rows;
            public JObject Defaults;
            public Dictionary<string, JObject> ConstantsById;
            public Dictionary<string, string> Labels, Prices, SceneLabels;
            internal bool IsCollection => Path != null;
            internal Field Get(string id) => Fields.Single(f => f.Id == id);
        }

        internal sealed class Group
        {
            public string Name;
            public string[] Sheets;
        }

        internal sealed class Shortcut
        {
            public string Id, Sheet, Source, Type;
            public string[] Fields, FilterIds;
        }

        internal sealed class Schema
        {
            public int Version;
            [JsonProperty("fixed")] public JObject Constants;
            public Dictionary<string, string> Mirrors, GearSlots, SceneLabels;
            public Group[] Group;
            public Table[] Tables;
            public Shortcut[] Quick;
            public string[][] Classes;
            public string[] QuickSheets;
            internal Table Find(string id) => Tables.Single(t => t.Id == id);
            internal static Schema Read() => JsonConvert.DeserializeObject<Schema>(File.ReadAllText(SchemaPath));
        }

        internal sealed class Record
        {
            internal string Id, Label;
            internal int Order;
            internal ProfessionWorkbook.Row Excel;
            internal readonly Dictionary<string, JToken> Values = new Dictionary<string, JToken>(StringComparer.Ordinal);
            internal string String(string id) => Values.TryGetValue(id, out var value) ? value.Value<string>() : null;
        }

        internal sealed class Grid
        {
            internal readonly Dictionary<string, List<Record>> Tables = new Dictionary<string, List<Record>>(StringComparer.Ordinal);
            internal Record Find(string table, string key) => Tables[table].SingleOrDefault(r => r.Id == key);
        }

        internal sealed class Binding
        {
            public string Table, Key, Column;
            public JToken Before, Value;
        }

        internal sealed class QuickRow
        {
            internal string Key, Label;
            internal Binding[] Cells;
            internal string Signature => string.Join("|", Cells.Select(c => c.Table + "/" + c.Key + "/" + c.Column));
        }

        internal sealed class QuickTable
        {
            internal string Id, Sheet, Caption;
            internal Field[] Fields;
            internal List<QuickRow> Rows;
        }

        internal static byte[] Bytes(string path)
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            if (stream.Length > 16 * 1024 * 1024) throw new InvalidDataException("工作簿超过 16 MB");
            using var result = new MemoryStream();
            stream.CopyTo(result);
            return result.ToArray();
        }

        internal static ZipArchive Open(byte[] bytes) => new ZipArchive(new MemoryStream(bytes, false), ZipArchiveMode.Read);
        internal static JToken At(JToken root, string path)
        {
            foreach (string key in path.Split('/'))
                root = root is JArray array ? array[int.Parse(key, CultureInfo.InvariantCulture)] : root[key];
            return root;
        }

        private static void Put(JToken root, string path, JToken value)
        {
            int n = path.LastIndexOf('/');
            JToken parent = n < 0 ? root : At(root, path.Substring(0, n));
            string leaf = path.Substring(n + 1);
            if (parent is JArray array) array[int.Parse(leaf, CultureInfo.InvariantCulture)] = value.DeepClone();
            else parent[leaf] = value.DeepClone();
        }

        private static string Label(Table table, Record record)
        {
            if (table.Labels != null && table.Labels.TryGetValue(record.Id, out var text)) return text;
            if (table.SceneLabels != null && table.SceneLabels.TryGetValue(record.String("Scene"), out var scene))
            {
                var qualifier = record.String("Slot") ?? record.String("Profession");
                return scene + " · " + (qualifier ?? "全体");
            }
            return record.String("Name") ?? record.Id;
        }

        private static JToken ReadCell(ProfessionWorkbook.Row row, string label, string type)
        {
            if (!row.Values.ContainsKey(label))
            {
                if (type.EndsWith("?", StringComparison.Ordinal)) return JValue.CreateNull();
                throw row.Error(label, "必填字段为空");
            }
            switch (type.TrimEnd('?'))
            {
                case "s": return new JValue(row.TextField(label));
                case "b": return new JValue(row.Boolean(label));
                case "i": return new JValue(row.Int(label, int.MinValue, int.MaxValue));
                case "f": return new JValue((float)row.Number(label, -float.MaxValue, float.MaxValue));
                case "d": return new JValue(row.Number(label, -1e12, 1e12));
                default: throw new InvalidDataException("未知配置字段类型 " + type);
            }
        }

        internal static string RowType(FixedRow row, Field field, int index)
            => row?.Types == null ? field.Type : row.Types[index];

        internal static string GetKind(byte[] bytes, Schema schema)
        {
            using var zip = Open(bytes);
            var row = new ProfessionWorkbook(zip).Rows(MetaTable, new[] { "工作簿类型", "结构版本" }, true).Single();
            if (row.Int("结构版本", 1, int.MaxValue) != schema.Version)
                throw row.Error("结构版本", "当前编辑器不支持该版本");
            return row.OneOf("工作簿类型", MasterKind, TrialKind);
        }

        internal static Grid FromRules(Schema schema, JObject rules)
        {
            var data = new Grid();
            foreach (var table in schema.Tables)
            {
                var records = data.Tables[table.Id] = new List<Record>();
                if (table.IsCollection)
                {
                    int order = 0;
                    foreach (JObject item in (JArray)At(rules, table.Path))
                    {
                        int thisOrder = order++;
                        bool hasSlot = !string.IsNullOrEmpty(item.Value<string>("Slot"));
                        if (table.Filter == "slot" && !hasSlot || table.Filter == "group" && hasSlot) continue;
                        var record = new Record
                        {
                            Id = table.Key == null
                                ? string.Join("_", table.Natural.Select(f => item[f].ToString().Replace("-", "_")))
                                : item.Value<string>(table.Key),
                            Order = thisOrder,
                        };
                        if (records.Any(r => r.Id == record.Id)) record.Id += "_" + thisOrder;
                        foreach (var field in table.Fields)
                        {
                            var value = item[field.Id];
                            record.Values[field.Id] = value == null || value.Type == JTokenType.String && value.Value<string>() == ""
                                ? JValue.CreateNull() : value.DeepClone();
                        }
                        record.Label = Label(table, record);
                        records.Add(record);
                    }
                }
                else foreach (var row in table.Rows)
                {
                    var record = new Record { Id = row.Id, Label = row.Label };
                    for (int i = 0; i < table.Fields.Length; i++)
                        record.Values[table.Fields[i].Id] = row.Paths[i] == null ? JValue.CreateNull() : At(rules, row.Paths[i]).DeepClone();
                    records.Add(record);
                }
            }
            return data;
        }

        internal static Grid ReadMaster(Schema schema, byte[] bytes)
        {
            if (GetKind(bytes, schema) != MasterKind) throw new InvalidDataException("请选择系统数值配置表");
            using var zip = Open(bytes);
            var book = new ProfessionWorkbook(zip);
            var data = new Grid();
            foreach (var table in schema.Tables)
            {
                var keys = new HashSet<string>(StringComparer.Ordinal);
                var fixedRows = table.Rows?.ToDictionary(r => r.Id);
                var required = table.Fields.Select(f => f.Label).Append(IdColumn);
                if (table.IsCollection) required = required.Append(OrderColumn);
                var rows = data.Tables[table.Id] = new List<Record>();
                foreach (var row in book.Rows(table.Id, required, true))
                {
                    string id = row.TextField(IdColumn);
                    if (!keys.Add(id)) throw row.Error(IdColumn, "重复 ID " + id);
                    if (table.IsCollection && !Regex.IsMatch(id, "^[a-zA-Z0-9_][a-zA-Z0-9_-]*$"))
                        throw row.Error(IdColumn, "ID 只允许字母、数字、下划线及连字符");
                    FixedRow definition = null;
                    if (!table.IsCollection && !fixedRows.TryGetValue(id, out definition))
                        throw row.Error(IdColumn, "未知配置 " + id);
                    var record = new Record
                    {
                        Id = id,
                        Excel = row,
                        Order = table.IsCollection && row.Values.ContainsKey(OrderColumn) ? row.Int(OrderColumn, 0, int.MaxValue) : int.MaxValue,
                    };
                    for (int i = 0; i < table.Fields.Length; i++)
                    {
                        var f = table.Fields[i];
                        if (definition != null && definition.Paths[i] == null)
                        {
                            if (row.Values.ContainsKey(f.Label)) throw row.Error(f.Label, "此行无此配置");
                            record.Values[f.Id] = JValue.CreateNull();
                        }
                        else record.Values[f.Id] = ReadCell(row, f.Label, RowType(definition, f, i));
                    }
                    record.Label = definition?.Label ?? Label(table, record);
                    rows.Add(record);
                }
                if (fixedRows != null && keys.Count != fixedRows.Count)
                    throw new InvalidDataException(table.Sheet + " · " + table.Id + "：缺少 " + string.Join("、", fixedRows.Keys.Except(keys)));
            }
            return data;
        }

        internal static JObject BuildRules(Schema schema, Grid data)
        {
            var root = (JObject)schema.Constants.DeepClone();
            var arrays = new Dictionary<string, List<(int order, string id, JObject row)>>();
            foreach (var table in schema.Tables)
            {
                if (table.IsCollection)
                {
                    if (!arrays.TryGetValue(table.Path, out var rows))
                        arrays[table.Path] = rows = new List<(int, string, JObject)>();
                    foreach (var record in data.Tables[table.Id])
                    {
                        var item = (JObject)(table.Defaults?.DeepClone() ?? new JObject());
                        if (table.ConstantsById != null && table.ConstantsById.TryGetValue(record.Id, out var defaults))
                            item.Merge(defaults.DeepClone());
                        if (table.Key != null) item[table.Key] = record.Id;
                        foreach (var field in table.Fields)
                        {
                            var value = record.Values[field.Id];
                            item[field.Id] = value.Type == JTokenType.Null && field.Type == "s?" ? new JValue("") : value.DeepClone();
                        }
                        rows.Add((record.Order, record.Id, item));
                    }
                    continue;
                }
                var definitions = table.Rows.ToDictionary(row => row.Id);
                foreach (var record in data.Tables[table.Id])
                    for (int i = 0; i < table.Fields.Length; i++)
                    {
                        var path = definitions[record.Id].Paths[i];
                        if (path != null) Put(root, path, record.Values[table.Fields[i].Id]);
                    }
            }
            foreach (var pair in arrays)
                Put(root, pair.Key, new JArray(pair.Value.OrderBy(r => r.order).ThenBy(r => r.id, StringComparer.Ordinal).Select(r => r.row)));
            foreach (var pair in schema.Mirrors) Put(root, pair.Key, At(root, pair.Value));
            return root;
        }

        internal static JToken Sorted(JToken value)
        {
            if (value is JObject o)
                return new JObject(o.Properties().OrderBy(p => p.Name, StringComparer.Ordinal).Select(p => new JProperty(p.Name, Sorted(p.Value))));
            if (value is JArray a) return new JArray(a.Select(Sorted));
            return value.DeepClone();
        }

        internal static string Hash(byte[] bytes)
        {
            using var hash = System.Security.Cryptography.SHA256.Create();
            return BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
        }

        internal static string Digest(JObject rules) => Hash(Encoding.UTF8.GetBytes(Sorted(rules).ToString(Formatting.None)));

        private static Binding Bind(Grid master, string table, Record record, string field)
            => new Binding { Table = table, Key = record.Id, Column = field, Before = record.Values[field].DeepClone() };

        internal static List<QuickTable> Quick(Schema schema, Grid master)
        {
            var result = new List<QuickTable>();
            foreach (var quick in schema.Quick)
            {
                var rows = new List<QuickRow>();
                if (quick.Type == "unit")
                {
                    var stats = schema.Find("cfg_profession_stats");
                    var fields = new[]
                    {
                        new Field("Health","普通兵生命","d"),new Field("Attack","普通兵攻击","d"),
                        new Field("Defense","普通兵防御","d"),stats.Get("Range")
                    };
                    foreach (var entry in schema.Classes)
                    {
                        var unit = master.Tables["cfg_units"].Single(u => u.String("ProfessionId") == entry[0] &&
                            u.String("Identity") == "soldier" && u.Values["IsDefault"].Value<bool>());
                        var profession = master.Find(stats.Id, entry[0]);
                        var name = master.Find("cfg_profession", entry[0]).String("Name");
                        rows.Add(new QuickRow
                        {
                            Key = entry[0],
                            Label = name,
                            Cells = new[]
                            {
                                Bind(master,"cfg_units",unit,"Health"),Bind(master,"cfg_units",unit,"Attack"),
                                Bind(master,"cfg_units",unit,"Defense"),Bind(master,stats.Id,profession,"Range")
                            },
                        });
                    }
                    result.Add(new QuickTable { Id = quick.Id, Sheet = quick.Sheet, Fields = fields, Rows = rows });
                }
                else
                {
                    var table = schema.Find(quick.Source);
                    var fields = quick.Id == "cfg_try_material"
                        ? new[] { new Field("value", "每份一级军需原料（份）", "i") }
                        : quick.Fields.Select(table.Get).ToArray();
                    foreach (var record in master.Tables[table.Id].Where(r => quick.FilterIds == null || quick.FilterIds.Contains(r.Id)))
                    {
                        var label = record.Label;
                        if (quick.Id == "cfg_try_group")
                        {
                            var p = record.String("Profession");
                            var profession = p == null ? null : master.Find("cfg_profession", p)?.String("Name");
                            label = schema.SceneLabels[record.String("Scene")] + " · " + (profession ?? "全体");
                        }
                        rows.Add(new QuickRow { Key = record.Id, Label = label, Cells = fields.Select(f => Bind(master, table.Id, record, f.Id)).ToArray() });
                    }
                    var captions = new Dictionary<string, string>
                    {
                        {"cfg_try_value","共享基础价值 · 按槽位和阶段"},{"cfg_try_material","一级军需配方"},
                        {"cfg_try_supply","军需攻防"},{"cfg_try_exact","指定槽位配装 · 每槽一人"},{"cfg_try_group","按职业／全军配装"}
                    };
                    result.Add(new QuickTable { Id = quick.Id, Sheet = quick.Sheet, Fields = fields, Rows = rows, Caption = captions.TryGetValue(quick.Id,out var caption) ? caption : null });
                }
            }
            return result;
        }

        private static bool Same(JToken left, JToken right)
        {
            if (left == null || right == null) return left == right;
            if ((left.Type == JTokenType.Integer || left.Type == JTokenType.Float) &&
                (right.Type == JTokenType.Integer || right.Type == JTokenType.Float))
                return left.Value<double>() == right.Value<double>();
            return JToken.DeepEquals(left, right);
        }

        private static void CheckBase(Binding edit, Record record, string location)
        {
            if (record == null) throw new InvalidDataException(location + "：总表中已经没有这条记录");
            var current = record.Values[edit.Column];
            if (!Same(current, edit.Before) && !Same(current, edit.Value))
                throw new InvalidDataException(location + "：同字段冲突。导出 " + edit.Before + "，总表 " + current + "，试玩拟写 " + edit.Value);
        }

        internal static List<Binding> ReadTrial(Schema schema, byte[] bytes, Grid master)
        {
            if (GetKind(bytes, schema) != TrialKind) throw new InvalidDataException("请选择试玩调参表");
            using var zip = Open(bytes);
            var book = new ProfessionWorkbook(zip);
            var changes = new List<Binding>();
            foreach (var table in Quick(schema, master))
            {
                var projected = table.Rows.ToDictionary(r => r.Key);
                var seen = new HashSet<string>(StringComparer.Ordinal);
                var headers = table.Fields.SelectMany(f => new[] { f.Label, BaselineColumn(f.Label) }).Concat(new[] { IdColumn, LinkColumn });
                foreach (var row in book.Rows(table.Id, headers, true))
                {
                    string id = row.TextField(IdColumn);
                    if (!seen.Add(id)) throw row.Error(IdColumn, "重复 ID " + id);
                    for (int i = 0; i < table.Fields.Length; i++)
                    {
                        var field = table.Fields[i];
                        var edit = ReadCell(row, field.Label, field.Type);
                        var before = ReadCell(row, BaselineColumn(field.Label), field.Type);
                        if (Same(edit, before)) continue;
                        if (!projected.TryGetValue(id, out var reference) || reference.Signature != row.TextField(LinkColumn))
                            throw row.Error(field.Label, "关联已变化，请从总表重新生成分表");
                        var target = reference.Cells[i];
                        var change = new Binding { Table = target.Table, Key = target.Key, Column = target.Column, Before = before, Value = edit };
                        CheckBase(change, master.Find(change.Table, change.Key), row.Location + " · " + field.Label);
                        changes.Add(change);
                    }
                }
            }
            return changes;
        }

        internal static void Apply(Schema schema, Grid master, IEnumerable<Binding> changes)
        {
            foreach (var change in changes)
            {
                var record = master.Find(change.Table, change.Key);
                CheckBase(change, record, Describe(schema, master, change));
                record.Values[change.Column] = change.Value.DeepClone();
            }
        }

        internal static string Describe(Schema schema, Grid master, Binding change)
        {
            var table = schema.Find(change.Table);
            return table.Sheet + " · " + (master.Find(change.Table, change.Key)?.Label ?? change.Key) + " · " + table.Get(change.Column).Label;
        }

        internal static List<string> Diff(Schema schema, Grid current, Grid candidate)
        {
            var changes = new List<string>();
            foreach (var table in schema.Tables)
            {
                var left = current.Tables[table.Id].ToDictionary(r => r.Id);
                var right = candidate.Tables[table.Id].ToDictionary(r => r.Id);
                foreach (var id in left.Keys.Union(right.Keys))
                {
                    left.TryGetValue(id, out var before);
                    right.TryGetValue(id, out var after);
                    if (before == null || after == null)
                    {
                        changes.Add(table.Sheet + " · " + (before?.Label ?? after.Label) + (before == null ? " · 新增" : " · 移除"));
                        continue;
                    }
                    if (table.IsCollection && before.Order != after.Order)
                        changes.Add(table.Sheet + " · " + after.Label + " · " + OrderColumn + "  " + before.Order + " → " + after.Order);
                    foreach (var field in table.Fields)
                        if (!Same(before.Values[field.Id], after.Values[field.Id]))
                            changes.Add(table.Sheet + " · " + after.Label + " · " + field.Label + "  " +
                                before.Values[field.Id] + " → " + after.Values[field.Id]);
                }
            }
            return changes;
        }
    }
}

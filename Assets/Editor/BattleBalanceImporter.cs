using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using BaseConquest.Base;
using BaseConquest.Battle;
#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
#endif

namespace BaseConquest.Editor
{
    // 只依赖 .NET 的 OpenXML 读取；无需 Excel 进程、外部插件或运行时 XLSX 包。
    public static class BattleBalanceImporter
    {
        private static readonly XNamespace Ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        private static readonly XNamespace Rel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        public sealed class ImportedBalance
        {
            public BattleScenario Scenario;
            public float RoundSeconds;
            public BaseEconomyRules Rules;
        }
        private sealed class UnitRow
        {
            public int Id, Enabled;
            public SoldierKind Kind;
            public SoldierStats Stats;
        }

        public static ImportedBalance Read(string file, BattleScenario original)
        {
            using (var archive = ZipFile.OpenRead(file))
            {
                var unitData = Rows(archive, "单位配置", "unit_id", new[] { "unit_id", "code", "name", "kind", "enabled", "health", "damage", "attack_range", "move_distance", "crit_chance", "crit_multiplier", "source" });
                var armyData = Rows(archive, "编制配置", "entry_id", new[] { "entry_id", "side", "unit_id", "unit_count" });
                var parameters = Rows(archive, "战斗参数", "key", new[] { "key", "value", "type" });
                var imported = Parse(unitData, armyData, parameters, original);
                imported.Rules = ParseEconomyRules(Rows(archive, "经营规则", "key", new[] { "key", "value", "type", "unit", "description", "source" }));
                if (Document(archive, "xl/workbook.xml").Descendants(Ns + "sheet").Any(x => (string)x.Attribute("name") == "战技与效果"))
                    imported.Scenario.Effects = ParseEffects(Rows(archive, "战技与效果", "effect_id", new[] { "effect_id", "code", "name", "kind", "attack_multiplier", "speed_bonus", "heal_per_round", "duration", "cooldown", "opening_cooldown", "ignore_terrain", "disable_crit", "source" }));
                return imported;
            }
        }
        // 先建立并校验完整克隆；只有全部成功才由菜单写入正式资产。
        public static ImportedBalance Parse(List<Dictionary<string, string>> units, List<Dictionary<string, string>> armies, List<Dictionary<string, string>> parameters, BattleScenario original)
        {
            var scenario = (original ?? new BattleScenario()).Clone();
            var ids = new Dictionary<int, UnitRow>(); var codes = new HashSet<string>(StringComparer.Ordinal); var kinds = new HashSet<SoldierKind>();
            foreach (var row in units)
            {
                int id = Integer(row, "unit_id", 1, 999999), kind = Integer(row, "kind", 0, 3), enabled = Integer(row, "enabled", 0, 1);
                if (ids.ContainsKey(id) || !codes.Add(Text(row, "code")) || !kinds.Add((SoldierKind)kind)) throw new InvalidOperationException("单位主键、代号或兵种重复：" + id);
                Text(row, "name"); Text(row, "source");
                var stats = new SoldierStats { Health = Number(row, "health", float.Epsilon, float.MaxValue), Damage = Number(row, "damage", 0, float.MaxValue), Range = Integer(row, "attack_range", 1, 12), MoveDistance = Integer(row, "move_distance", 1, 12), CritChance = Number(row, "crit_chance", 0, 1), CritMultiplier = Number(row, "crit_multiplier", 1, 10) };
                if (float.IsInfinity(stats.ExpectedDamage)) throw new InvalidOperationException("暴击期望伤害溢出：" + id);
                ids.Add(id, new UnitRow { Id = id, Kind = (SoldierKind)kind, Enabled = enabled, Stats = stats });
            }
            if (kinds.Count != 4) throw new InvalidOperationException("当前需完整配置近战、远程、英雄和骑兵四种基础模板");
            var entryIds = new HashSet<int>(); var assignments = new HashSet<(int side, int id)>();
            foreach (var row in armies)
            {
                int entry = Integer(row, "entry_id", 1, 999999), side = Integer(row, "side", 0, 1), id = Integer(row, "unit_id", 1, 999999);
                if (!ids.TryGetValue(id, out UnitRow unit)) throw new InvalidOperationException("编制引用不存在的单位：" + id);
                if (!entryIds.Add(entry) || !assignments.Add((side, id))) throw new InvalidOperationException("编制主键或阵营单位重复：" + entry);
                int count = Integer(row, "unit_count", 0, unit.Kind == SoldierKind.Hero ? 3 : 30);
                if (unit.Enabled == 0 && count > 0) throw new InvalidOperationException("禁用单位仍有编制：" + id);
                ArmyDefinition army = scenario.Army((BattleSide)side); army.SetCount(unit.Kind, count);
                SoldierStats target = army.Stats(unit.Kind); target.Health = unit.Stats.Health; target.Damage = unit.Stats.Damage; target.Range = unit.Stats.Range; target.MoveDistance = unit.Stats.MoveDistance; target.CritChance = unit.Stats.CritChance; target.CritMultiplier = unit.Stats.CritMultiplier;
            }
            if (assignments.Count != 8) throw new InvalidOperationException("甲乙双方须各有四条基础兵种编制，可将人数设为0");
            var keyed = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
            foreach (var row in parameters) if (!keyed.TryAdd(Text(row, "key"), row)) throw new InvalidOperationException("战斗参数键重复");
            var expectedKeys = new[] { "max_rounds", "stalemate_rounds", "skill_range", "skill_radius", "skill_damage", "skill_cooldown", "random_seed", "round_seconds" };
            if (keyed.Count != expectedKeys.Length || expectedKeys.Any(key => !keyed.ContainsKey(key))) throw new InvalidOperationException("战斗参数键缺失或包含未接入键");
            foreach (string key in expectedKeys) if (Text(keyed[key], "type") != (key == "skill_damage" || key == "round_seconds" ? "float" : "int")) throw new InvalidOperationException("参数类型声明错误：" + key);
            scenario.MaxRounds = Integer(keyed["max_rounds"], "value", 1, int.MaxValue); scenario.StalemateRounds = Integer(keyed["stalemate_rounds"], "value", 1, int.MaxValue);
            scenario.SkillRange = Integer(keyed["skill_range"], "value", 1, int.MaxValue); scenario.SkillRadius = Integer(keyed["skill_radius"], "value", 0, int.MaxValue);
            scenario.SkillDamage = Number(keyed["skill_damage"], "value", 0, float.MaxValue); scenario.SkillCooldown = Integer(keyed["skill_cooldown"], "value", 1, int.MaxValue);
            scenario.RandomSeed = Integer(keyed["random_seed"], "value", int.MinValue, int.MaxValue);
            float seconds = Number(keyed["round_seconds"], "value", 0.1f, 1.5f);
            // 当前部署超过新人数时由运行时重建合法成员；保留原始地图和布兵记录，不在导入时删图。
            return new ImportedBalance { Scenario = scenario, RoundSeconds = seconds };
        }
        public static BattleEffectRules ParseEffects(List<Dictionary<string, string>> rows)
        {
            if (rows.Count != 5 || rows.Select(r => Text(r, "code")).Distinct().Count() != 5 || rows.Select(r => Integer(r, "effect_id", 1, 999999)).Distinct().Count() != 5) throw new InvalidOperationException("战技与效果须含五条唯一记录");
            var keyed = rows.ToDictionary(r => Text(r, "code"));
            if (new[] { "inspire", "march", "heal", "weakness", "archer_close" }.Any(k => !keyed.ContainsKey(k))) throw new InvalidOperationException("战技／效果代号不完整");
            var inspire = keyed["inspire"]; var march = keyed["march"]; var heal = keyed["heal"]; var weak = keyed["weakness"]; var close = keyed["archer_close"];
            foreach (var r in rows) { Text(r, "name"); Text(r, "source"); int kind = Integer(r, "kind", 0, 1); if (kind != (r == weak || r == close ? 1 : 0)) throw new InvalidOperationException("效果类型不匹配"); }
            int cooldown = Integer(inspire, "cooldown", 1, 1000);
            if (Integer(march, "cooldown", 1, 1000) != cooldown || Integer(heal, "cooldown", 1, 1000) != cooldown || Integer(march, "ignore_terrain", 0, 1) != 1 || Integer(weak, "disable_crit", 0, 1) != 1) throw new InvalidOperationException("三技能CD须一致，行军忽略地形、虚弱禁暴击");
            var rules = new BattleEffectRules { InspireMultiplier = Number(inspire, "attack_multiplier", 0, 10), InspireDuration = Integer(inspire, "duration", 1, 1000), MarchSpeedBonus = Integer(march, "speed_bonus", 1, 12), MarchDuration = Integer(march, "duration", 1, 1000), HealPerRound = Number(heal, "heal_per_round", 0, 100000), HealDuration = Integer(heal, "duration", 1, 1000), WeakMultiplier = Number(weak, "attack_multiplier", 0, 10), WeakDuration = Integer(weak, "duration", 1, 1000), RangedCloseMultiplier = Number(close, "attack_multiplier", 0, 10), Cooldown = cooldown, OpeningCooldowns = new[] { Integer(inspire, "opening_cooldown", 0, cooldown), Integer(march, "opening_cooldown", 0, cooldown), Integer(heal, "opening_cooldown", 0, cooldown) } };
            BattleEffectRules.Validate(rules); return rules;
        }

        /// <summary>
        /// 解析「经营规则」页。与战斗参数同范式：竖排键值表，10 个固定键，类型声明须与实际一致。
        /// 任一项缺失、重复或越界都拒绝整个导入，不留下部分配置。
        /// </summary>
        public static BaseEconomyRules ParseEconomyRules(List<Dictionary<string, string>> rows)
        {
            var keyed = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
            foreach (var row in rows) if (!keyed.TryAdd(Text(row, "key"), row)) throw new InvalidOperationException("经营规则键重复");
            var expected = new[] { "soldier_food", "cavalry_food", "leader_food", "farm_per_worker", "workshop_per_worker", "hunger_production_penalty", "hunger_attack_penalty", "sword_material", "bow_material", "armor_material" };
            if (keyed.Count != expected.Length || expected.Any(k => !keyed.ContainsKey(k))) throw new InvalidOperationException("经营规则键缺失或包含未接入键");
            foreach (string key in expected) Text(keyed[key], "description");
            var intKeys = new[] { "soldier_food", "cavalry_food", "leader_food", "sword_material", "bow_material", "armor_material" };
            var floatKeys = new[] { "farm_per_worker", "workshop_per_worker", "hunger_production_penalty", "hunger_attack_penalty" };
            foreach (string key in intKeys) if (Text(keyed[key], "type") != "int") throw new InvalidOperationException("经营规则类型声明错误：" + key);
            foreach (string key in floatKeys) if (Text(keyed[key], "type") != "float") throw new InvalidOperationException("经营规则类型声明错误：" + key);
            return new BaseEconomyRules
            {
                SoldierFood = Integer(keyed["soldier_food"], "value", 1, 100),
                CavalryFood = Integer(keyed["cavalry_food"], "value", 1, 100),
                LeaderFood = Integer(keyed["leader_food"], "value", 0, 100),
                FarmPerWorker = Number(keyed["farm_per_worker"], "value", float.Epsilon, 100f),
                WorkshopPerWorker = Number(keyed["workshop_per_worker"], "value", float.Epsilon, 100f),
                HungerProductionPenalty = Number(keyed["hunger_production_penalty"], "value", 0f, 0.999f),
                HungerAttackPenalty = Number(keyed["hunger_attack_penalty"], "value", 0f, 0.999f),
                SwordMaterial = Integer(keyed["sword_material"], "value", 1, 100),
                BowMaterial = Integer(keyed["bow_material"], "value", 1, 100),
                ArmorMaterial = Integer(keyed["armor_material"], "value", 1, 100)
            };
        }

        /// <summary>
        /// 把经营规则写入 Assets/Resources/BaseSettings.asset（缺失时创建）。
        /// 运行时由 BaseSettings.InstallDefaultRules 注入到 BaseScenarioDocument.DefaultRules。
        /// </summary>
#if UNITY_EDITOR
        private static void ApplyBaseRules(BaseEconomyRules rules)
        {
            const string path = "Assets/Resources/BaseSettings.asset";
            BaseSettings asset = AssetDatabase.LoadAssetAtPath<BaseSettings>(path);
            if (asset == null)
            {
                Directory.CreateDirectory("Assets/Resources");
                asset = ScriptableObject.CreateInstance<BaseSettings>();
                AssetDatabase.CreateAsset(asset, path);
            }
            Undo.RecordObject(asset, "导入经营规则");
            asset.Rules = rules.Copy();
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssetIfDirty(asset);
        }
#endif
        private static string Text(Dictionary<string, string> row, string key)
        {
            if (!row.TryGetValue(key, out string value) || string.IsNullOrWhiteSpace(value)) throw new InvalidOperationException("必填字段为空：" + key);
            return value.Trim();
        }
        private static int Integer(Dictionary<string, string> row, string key, int min, int max)
        {
            string text = Text(row, key);
            if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) || double.IsNaN(value) || double.IsInfinity(value) || value < min || value > max || Math.Truncate(value) != value) throw new InvalidOperationException("整数无效或越界：" + key + "=" + text);
            return (int)value;
        }
        private static float Number(Dictionary<string, string> row, string key, float min, float max)
        {
            string text = Text(row, key);
            if (!float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float value) || float.IsNaN(value) || float.IsInfinity(value) || value < min || value > max) throw new InvalidOperationException("数值无效或越界：" + key + "=" + text);
            return value;
        }
        private static XDocument Document(ZipArchive archive, string path)
        {
            ZipArchiveEntry entry = archive.GetEntry(path);
            if (entry == null) throw new InvalidOperationException("XLSX 缺少 " + path);
            using (Stream stream = entry.Open()) return XDocument.Load(stream);
        }
        public static List<Dictionary<string, string>> Rows(ZipArchive archive, string name, string primary, string[] required)
        {
            var workbook = Document(archive, "xl/workbook.xml");
            var sheet = workbook.Descendants(Ns + "sheet").FirstOrDefault(x => (string)x.Attribute("name") == name);
            if (sheet == null) throw new InvalidOperationException("缺少配置页：" + name);
            string relation = (string)sheet.Attribute(Rel + "id");
            var link = Document(archive, "xl/_rels/workbook.xml.rels").Root.Elements().FirstOrDefault(x => (string)x.Attribute("Id") == relation);
            if (link == null) throw new InvalidOperationException("配置页关联缺失：" + name);
            string target = (string)link.Attribute("Target"); string xmlPath = target.StartsWith("/") ? target.TrimStart('/') : "xl/" + target;
            string[] shared = archive.GetEntry("xl/sharedStrings.xml") == null ? Array.Empty<string>() : Document(archive, "xl/sharedStrings.xml").Descendants(Ns + "si").Select(x => string.Concat(x.Descendants(Ns + "t").Select(t => t.Value))).ToArray();
            var rows = Document(archive, xmlPath).Descendants(Ns + "row").ToList();
            var header = rows.FirstOrDefault(row => row.Elements(Ns + "c").Any(c => Cell(c, shared) == primary));
            if (header == null) throw new InvalidOperationException("缺少字段行：" + name);
            var fields = new Dictionary<string, string>();
            foreach (var c in header.Elements(Ns + "c")) { string key = Cell(c, shared); if (string.IsNullOrEmpty(key)) continue; if (fields.Values.Contains(key)) throw new InvalidOperationException("重复字段：" + key); fields.Add(Column(c), key); }
            if (required.Any(key => !fields.Values.Contains(key))) throw new InvalidOperationException("字段不完整：" + name);
            int headerNumber = (int)header.Attribute("r"); var result = new List<Dictionary<string, string>>();
            foreach (var row in rows.Where(row => (int)row.Attribute("r") > headerNumber))
            {
                var values = new Dictionary<string, string>();
                foreach (var c in row.Elements(Ns + "c"))
                {
                    if (!fields.TryGetValue(Column(c), out string key)) continue;
                    string value = Cell(c, shared); if (string.IsNullOrEmpty(value)) continue;
                    if (c.Element(Ns + "f") != null) throw new InvalidOperationException(name + "：配置输入须为数值或文本，公式请放数值分析页");
                    values[key] = value;
                }
                if (values.Count == 0) continue;
                if (required.Any(key => !values.ContainsKey(key))) throw new InvalidOperationException(name + " 第 " + row.Attribute("r") + " 行必填字段缺失");
                result.Add(values);
            }
            if (result.Count > 200) throw new InvalidOperationException(name + " 当前最多200条记录");
            return result;
        }
        // A workbook page may contain several tables.  Read only the declared
        // data rectangle and preserve cell addresses in diagnostics.
        public static Dictionary<string,string> Section(ZipArchive archive,string name,int firstRow,int lastRow,string columns)
        {
            if(firstRow<1||lastRow<firstRow||lastRow-firstRow>200||string.IsNullOrEmpty(columns))throw new ArgumentException("表格范围非法");
            var workbook=Document(archive,"xl/workbook.xml");
            var sheet=workbook.Descendants(Ns+"sheet").FirstOrDefault(x=>(string)x.Attribute("name")==name);
            if(sheet==null)throw new InvalidOperationException("缺少配置页："+name);
            string relation=(string)sheet.Attribute(Rel+"id");
            var link=Document(archive,"xl/_rels/workbook.xml.rels").Root.Elements().FirstOrDefault(x=>(string)x.Attribute("Id")==relation);
            if(link==null)throw new InvalidOperationException("配置页关联缺失："+name);
            string target=(string)link.Attribute("Target");string xmlPath=target.StartsWith("/")?target.TrimStart('/') : "xl/"+target;
            string[] shared=archive.GetEntry("xl/sharedStrings.xml")==null?Array.Empty<string>():Document(archive,"xl/sharedStrings.xml").Descendants(Ns+"si").Select(x=>string.Concat(x.Descendants(Ns+"t").Select(t=>t.Value))).ToArray();
            var result=new Dictionary<string,string>(StringComparer.Ordinal);
            foreach(var row in Document(archive,xmlPath).Descendants(Ns+"row"))
            {
                int n=(int)row.Attribute("r");if(n<firstRow||n>lastRow)continue;
                foreach(var cell in row.Elements(Ns+"c"))
                {
                    string address=(string)cell.Attribute("r"),column=Column(cell);
                    if(column.Length!=1||columns.IndexOf(column[0])<0)continue;
                    if(cell.Element(Ns+"f")!=null)throw new InvalidOperationException(name+"!"+address+"：配置输入不能使用公式");
                    string value=Cell(cell,shared);
                    if(!string.IsNullOrWhiteSpace(value)){result[address]=value.Trim();result["#type:"+address]=(string)cell.Attribute("t")??"n";}
                }
            }
            return result;
        }
        private static string Column(XElement c) => new string(((string)c.Attribute("r")).TakeWhile(char.IsLetter).ToArray());
        private static string Cell(XElement c, string[] shared)
        {
            string type = (string)c.Attribute("t"), value = (string)c.Element(Ns + "v") ?? "";
            if (type == "s") return shared[int.Parse(value, CultureInfo.InvariantCulture)];
            if (type == "inlineStr") return string.Concat(c.Descendants(Ns + "t").Select(x => x.Value));
            return value;
        }
    }

    // Early, deliberately limited parser for the two verified workbook tables.
    // It is not exposed as a complete Demo importer until all fields are wired.
    public static partial class DemoBalanceTableReader
    {
        private static string Need(Dictionary<string,string> cells,string sheet,string address)
        {if(!cells.TryGetValue(address,out var value)||string.IsNullOrWhiteSpace(value))throw new InvalidOperationException(sheet+"!"+address+"：必填值为空");return value;}
        private static double Number(Dictionary<string,string> cells,string sheet,string address,double min,double max)
        {
            var text=Need(cells,sheet,address);
            if(!cells.TryGetValue("#type:"+address,out var type)||type!="n")throw new InvalidOperationException(sheet+"!"+address+"：请输入Excel数值，不接受文本或布尔值");
            if(!double.TryParse(text,NumberStyles.Float,CultureInfo.InvariantCulture,out double n)||double.IsNaN(n)||double.IsInfinity(n)||n<min||n>max)throw new InvalidOperationException(sheet+"!"+address+"：数值无效或越界，允许 "+min+"～"+max);
            return n;
        }
        private static void Header(Dictionary<string,string> cells,string sheet,string address,string expected)
        {if(Need(cells,sheet,address)!=expected)throw new InvalidOperationException(sheet+"!"+address+"：字段应为 "+expected);}
        public static void ApplyIntelPrices(RpgRules rules,Dictionary<string,string> cells)
        {
            if(cells.Count==0)return;const string sheet="05c 装备价格";
            Header(cells,sheet,"A62","消息类型");Header(cells,sheet,"B62","价格");
            if(Need(cells,sheet,"A63")!="3日"||Need(cells,sheet,"A64")!="5日")throw new InvalidOperationException(sheet+"!A63:A64：消息类型须为3日、5日");
            double three=Number(cells,sheet,"B63",0,100000000),five=Number(cells,sheet,"B64",0,100000000);
            if(three!=Math.Truncate(three)||five!=Math.Truncate(five))throw new InvalidOperationException(sheet+"!B63:B64：消息价格须为整数");
            rules.ThreeDayIntelPrice=(int)three;rules.FiveDayIntelPrice=(int)five;rules.IntelPricingConfigured=true;
        }

        private static ZipArchive OpenWorkbook(string path)
        {
            byte[] bytes=File.ReadAllBytes(path);
            if(bytes.Length>16*1024*1024)throw new InvalidOperationException("配表超过16MB");
            var archive=new ZipArchive(new MemoryStream(bytes),ZipArchiveMode.Read);
            try
            {
                if(archive.Entries.Count>1024||archive.Entries.Sum(e=>e.Length)>64L*1024*1024)
                    throw new InvalidOperationException("XLSX解压体积或条目数越界");
                return archive;
            }
            catch { archive.Dispose(); throw; }
        }

        public static RpgRules ReadEquipmentTables(string path,RpgRules baseline)
        {
            var rules=baseline.Copy();
            using(var archive=OpenWorkbook(path))
            {
                ApplyPersonalEquipment(archive,rules);
                ApplyEquipmentPrices(archive,rules);
            }
            rules.Validate();
            return rules;
        }

        private static void ApplyPersonalEquipment(ZipArchive archive,RpgRules rules)
        {
            const string equipment="05a 个人装备";
            var eq=BattleBalanceImporter.Section(archive,equipment,6,19,"ABCDEFGHIJKLSW");
            var eqHeaders=new Dictionary<string,string>{{"A6","装备ID"},{"B6","名称"},{"C6","槽位"},{"D6","职业限制"},{"E6","攻击加值"},{"F6","生命加值"},{"G6","防御加值"},{"H6","暴击倍率加值"},{"I6","移速加值"},{"J6","命中加值"},{"K6","闪避加值"},{"L6","套装ID"},{"S6","启用"},{"W6","价值阶段"}};
            foreach(var pair in eqHeaders)Header(eq,equipment,pair.Key,pair.Value);
            var seen=new HashSet<string>();
            for(int row=7;row<=19;row++)
            {
                string id=Need(eq,equipment,"A"+row);var original=BasePersonalEquipment.Definition(id);var item=rules.Equipment.SingleOrDefault(x=>x.Id==id);
                if(original==null||item==null||!seen.Add(id))throw new InvalidOperationException(equipment+"!A"+row+"：装备ID不存在或重复："+id);
                string slot=original.Slot=="weapon"?"武器":original.Slot=="helmet"?"头盔":original.Slot=="armor"?"护甲":"饰品";
                string profession=original.Weapon=="sword"?"saber":original.Weapon=="bow"?"archer":original.Weapon=="spear"?"rider":"";
                string set=original.Set=="light"?"set_light":original.Set=="heavy"?"set_heavy":"";
                string Get(string col)=>eq.TryGetValue(col+row,out var value)?value:"";
                if(Need(eq,equipment,"B"+row)!=original.Name||Need(eq,equipment,"C"+row)!=slot||Get("D")!=profession||Get("L")!=set)throw new InvalidOperationException(equipment+" 第"+row+"行：名称、槽位、职业或套装与固定装备定义不符");
                if(Get("J")!=""||Get("K")!="")throw new InvalidOperationException(equipment+"!J"+row+"/K"+row+"：Demo装备不使用命中或闪避加值，请留空");
                if(Need(eq,equipment,"S"+row)!="1"||!eq.TryGetValue("#type:S"+row,out var enabledType)||enabledType!="b")throw new InvalidOperationException(equipment+"!S"+row+"：实际使用的装备须启用（布尔值）");
                item.Damage=(float)Number(eq,equipment,"E"+row,-10000,10000);item.Health=(float)Number(eq,equipment,"F"+row,0,100000);item.Defense=(float)Number(eq,equipment,"G"+row,0,100000);
                item.CritMultiplier=(float)Number(eq,equipment,"H"+row,0,5);item.Movement=(float)Number(eq,equipment,"I"+row,-3,3);
                double stage=Number(eq,equipment,"W"+row,0,4);if(stage!=Math.Truncate(stage))throw new InvalidOperationException(equipment+"!W"+row+"：价值阶段须为整数");item.Stage=(int)stage;
            }
        }

        private static void ApplyEquipmentPrices(ZipArchive archive,RpgRules rules)
        {
            const string prices="05c 装备价格";
            var values=BattleBalanceImporter.Section(archive,prices,26,31,"ABCDE");
            Header(values,prices,"A26","装备阶段");Header(values,prices,"B26","武器基础价值");Header(values,prices,"C26","头盔基础价值");Header(values,prices,"D26","护甲基础价值");Header(values,prices,"E26","饰品基础价值");
            for(int stage=0;stage<5;stage++)
            {
                int row=stage+27;
                Number(values,prices,"A"+row,stage,stage);
                var targets=new[]{rules.WeaponValues,rules.HelmetValues,rules.ArmorValues,rules.AccessoryValues};
                for(int col=0;col<targets.Length;col++)
                {
                    string address=((char)('B'+col))+row.ToString(CultureInfo.InvariantCulture);
                    double value=Number(values,prices,address,1,1000000);
                    if(value!=Math.Truncate(value))throw new InvalidOperationException(prices+"!"+address+"：基础价值须为整数");
                    targets[col][stage]=(int)value;
                }
            }
            var qualityValues=BattleBalanceImporter.Section(archive,prices,34,40,"AB");
            Header(qualityValues,prices,"A34","品质ID");Header(qualityValues,prices,"B34","价值倍率");var seen=new HashSet<string>();
            for(int row=35;row<=40;row++)
            {
                string id=Need(qualityValues,prices,"A"+row);
                int index=Array.IndexOf(new[]{"quality_0","quality_1","quality_2","quality_3","quality_4","quality_5"},id);
                if(index<0||!seen.Add(id))throw new InvalidOperationException(prices+"!A"+row+"：品质ID无效或重复");
                rules.QualityValue[index]=Number(qualityValues,prices,"B"+row,.0001,100);
            }
        }

        public static RpgRules ReadVerifiedTables(string path)
        {
            using(var archive=OpenWorkbook(path))
            {
                var rules=RpgRules.Defaults();
                const string professions="03a 职业规则",templates="03b 九类模板";
                var p=BattleBalanceImporter.Section(archive,professions,6,9,"ADEFGM");
                Header(p,professions,"A6","职业ID");Header(p,professions,"D6","基础命中率");Header(p,professions,"E6","基础闪避率");Header(p,professions,"F6","射程");Header(p,professions,"G6","基础移速");Header(p,professions,"M6","启用");
                string[] ids={"saber","archer","rider"};var seen=new HashSet<string>();
                for(int row=7;row<=9;row++)
                {
                    string id=Need(p,professions,"A"+row);int index=Array.IndexOf(ids,id);
                    if(index<0||!seen.Add(id))throw new InvalidOperationException(professions+"!A"+row+"：职业ID不存在或重复："+id);
                    if(Need(p,professions,"M"+row)!="1"||!p.TryGetValue("#type:M"+row,out var enabledType)||enabledType!="b")throw new InvalidOperationException(professions+"!M"+row+"：游戏必需职业须启用（布尔值）");
                    double hit=Number(p,professions,"D"+row,0,1)*10,evade=Number(p,professions,"E"+row,0,1)*10;
                    if(Math.Abs(hit-Math.Round(hit))>1e-8||Math.Abs(evade-Math.Round(evade))>1e-8)throw new InvalidOperationException(professions+"!D"+row+"/E"+row+"：命中和闪避必须换算为整数点数");
                    rules.Hit[index]=Math.Round(hit);rules.Evasion[index]=Math.Round(evade);
                    rules.Range[index]=Number(p,professions,"F"+row,.01,24);rules.Speed[index]=Number(p,professions,"G"+row,.01,12);
                }
                const string quality="05b 品质与套装";
                var qualityRows=BattleBalanceImporter.Section(archive,quality,6,12,"ABDEH");
                Header(qualityRows,quality,"A6","品质ID");Header(qualityRows,quality,"B6","索引");Header(qualityRows,quality,"D6","正属性倍率");Header(qualityRows,quality,"E6","弓射程加值");Header(qualityRows,quality,"H6","启用");
                seen.Clear();
                for(int row=7;row<=12;row++)
                {
                    string id=Need(qualityRows,quality,"A"+row);int index=Array.IndexOf(new[]{"quality_0","quality_1","quality_2","quality_3","quality_4","quality_5"},id);
                    if(index<0||!seen.Add(id)||Number(qualityRows,quality,"B"+row,0,5)!=index)throw new InvalidOperationException(quality+"!A"+row+"/B"+row+"：品质ID或索引无效或重复");
                    if(Need(qualityRows,quality,"H"+row)!="1"||!qualityRows.TryGetValue("#type:H"+row,out var enabledType)||enabledType!="b")throw new InvalidOperationException(quality+"!H"+row+"：品质定义须启用（布尔值）");
                    rules.EquipmentQuality[index]=Number(qualityRows,quality,"D"+row,.01,100);
                    double range=Number(qualityRows,quality,"E"+row,0,24);
                    if(range!=Math.Truncate(range)||rules.Range[1]+range>24)throw new InvalidOperationException(quality+"!E"+row+"：弓射程加值须为整数，且与职业射程合计不超过24细格");
                    rules.BowQualityRange[index]=(int)range;
                }
                var forge=BattleBalanceImporter.Section(archive,quality,34,38,"ABCF");
                Header(forge,quality,"A34","投入锻造石数");Header(forge,quality,"B34","升档率");Header(forge,quality,"C34","降档率");Header(forge,quality,"F34","启用");
                rules.ReforgeUp=new int[4];rules.ReforgeDown=new int[4];int[] anchors={1,3,5,10};
                for(int row=35;row<=38;row++)
                {
                    if(Need(forge,quality,"F"+row)!="1"||!forge.TryGetValue("#type:F"+row,out var forgeEnabled)||forgeEnabled!="b")throw new InvalidOperationException(quality+"!F"+row+"：重铸锚点须启用（布尔值）");
                    int i=row-35;if(Number(forge,quality,"A"+row,1,10)!=anchors[i])throw new InvalidOperationException(quality+"!A"+row+"：锻造石锚点须为1、3、5、10");
                    double up=Number(forge,quality,"B"+row,0,1)*10000,down=Number(forge,quality,"C"+row,0,1)*10000;
                    if(Math.Abs(up-Math.Round(up))>1e-7||Math.Abs(down-Math.Round(down))>1e-7||up+down>10000+1e-7)throw new InvalidOperationException(quality+"!B"+row+"/C"+row+"：概率须为0.01个百分点的整数倍且合计不超过100%");
                    rules.ReforgeUp[i]=(int)Math.Round(up);rules.ReforgeDown[i]=(int)Math.Round(down);
                }
                var t=BattleBalanceImporter.Section(archive,templates,6,15,"ADEFGP");
                Header(t,templates,"A6","模板ID");Header(t,templates,"D6","职业ID");Header(t,templates,"E6","1级生命");Header(t,templates,"F6","1级攻击");Header(t,templates,"G6","1级防御");Header(t,templates,"P6","启用");
                seen.Clear();
                for(int row=7;row<=15;row++)
                {
                    string id=Need(t,templates,"A"+row),profession=Need(t,templates,"D"+row);int index=Array.IndexOf(ids,profession);
                    if(index<0||!id.EndsWith("_"+profession,StringComparison.Ordinal)||!seen.Add(id))throw new InvalidOperationException(templates+"!A"+row+"：模板ID或职业引用无效或重复："+id);
                    if(Need(t,templates,"P"+row)!="1"||!t.TryGetValue("#type:P"+row,out var enabledType)||enabledType!="b")throw new InvalidOperationException(templates+"!P"+row+"：游戏必需模板须启用（布尔值）");
                    string runtime=id.Substring(0,id.Length-profession.Length)+new[]{"warrior","archer","knight"}[index];
                    var item=rules.Templates.SingleOrDefault(x=>x.Id==runtime);
                    if(item==null)throw new InvalidOperationException(templates+"!A"+row+"：未支持的模板ID："+id);
                    item.Health=Number(t,templates,"E"+row,1,1000000);item.Attack=Number(t,templates,"F"+row,0,1000000);item.Defense=Number(t,templates,"G"+row,0,1000000);
                    if(runtime=="soldier_warrior"&&item.Attack<.001)throw new InvalidOperationException(templates+"!F"+row+"：剑士是战力公式的参照，攻击须至少为0.001；其他模板可为0");
                }
                ApplyPersonalEquipment(archive,rules);
                const string economy="07a 经营参数";
                var eco=BattleBalanceImporter.Section(archive,economy,6,15,"ACDG");
                Header(eco,economy,"A6","参数ID");Header(eco,economy,"C6","值");Header(eco,economy,"D6","类型");Header(eco,economy,"G6","启用");
                var opening=new BaseEconomyRules();double recovery=0;
                var economyKeys=new Dictionary<string,(string type,Action<double> apply,double min,double max)>{
                    {"food_saber",("INT",x=>opening.SoldierFood=(int)x,1,100)},
                    {"food_archer",("INT",x=>rules.ArcherFood=(int)x,1,100)},
                    {"food_rider",("INT",x=>opening.CavalryFood=(int)x,1,100)},
                    {"food_captain",("INT",x=>{rules.LeaderFood=(int)x;opening.LeaderFood=(int)x;},0,100)},
                    {"farm_output_base",("NUM",x=>opening.FarmPerWorker=x,.0001,100)},
                    {"workshop_workpoint_base",("NUM",x=>opening.WorkshopPerWorker=x,.0001,100)},
                    {"hunger_production_penalty_max",("RATE",x=>opening.HungerProductionPenalty=x,0,.999999)},
                    {"hunger_attack_penalty_max",("RATE",x=>opening.HungerAttackPenalty=x,0,.999999)},
                    {"recovery_per_day",("NUM",x=>recovery=x,.0001,10000)}
                };
                seen.Clear();
                for(int row=7;row<=15;row++)
                {
                    string id=Need(eco,economy,"A"+row);
                    if(!economyKeys.TryGetValue(id,out var item)||!seen.Add(id))throw new InvalidOperationException(economy+"!A"+row+"：参数ID不存在或重复："+id);
                    if(Need(eco,economy,"D"+row)!=item.type)throw new InvalidOperationException(economy+"!D"+row+"：参数类型应为"+item.type);
                    if(Need(eco,economy,"G"+row)!="1"||!eco.TryGetValue("#type:G"+row,out var enabledType)||enabledType!="b")throw new InvalidOperationException(economy+"!G"+row+"：实际使用的参数须启用（布尔值）");
                    double value=Number(eco,economy,"C"+row,item.min,item.max);
                    if(item.type=="INT"&&value!=Math.Truncate(value))throw new InvalidOperationException(economy+"!C"+row+"：参数须为整数");item.apply(value);
                }
                rules.OpeningEconomy=opening;rules.OpeningEconomyVersion=1;rules.OpeningRecoveryPerDay=recovery;
                const string facilitySheet="07b 设施与营帐";
                var facilities=BattleBalanceImporter.Section(archive,facilitySheet,6,9,"ADEHK");
                Header(facilities,facilitySheet,"A6","设施ID");Header(facilities,facilitySheet,"D6","工人容量");Header(facilities,facilitySheet,"E6","最低保留意向");Header(facilities,facilitySheet,"H6","初始可用");Header(facilities,facilitySheet,"K6","启用");
                rules.OpeningFacilityCapacity=new int[2];rules.OpeningFacilityMinimum=new int[2];seen.Clear();
                for(int row=8;row<=9;row++)
                {
                    string id=Need(facilities,facilitySheet,"A"+row);int index=Array.IndexOf(new[]{"farm","workshop"},id);
                    if(index<0||!seen.Add(id))throw new InvalidOperationException(facilitySheet+"!A"+row+"：设施ID无效或重复");
                    double capacity=Number(facilities,facilitySheet,"D"+row,1,10000),minimum=Number(facilities,facilitySheet,"E"+row,0,capacity);
                    if(capacity!=Math.Truncate(capacity)||minimum!=Math.Truncate(minimum))throw new InvalidOperationException(facilitySheet+" 第"+row+"行：岗位容量与最低保留须为整数");
                    if(Need(facilities,facilitySheet,"K"+row)!="1"||!facilities.TryGetValue("#type:K"+row,out var enabledType)||enabledType!="b")throw new InvalidOperationException(facilitySheet+"!K"+row+"：Demo设施须启用（布尔值）");
                    if(Need(facilities,facilitySheet,"H"+row)!=(index==0?"1":"0")||!facilities.TryGetValue("#type:H"+row,out var availableType)||availableType!="b")throw new InvalidOperationException(facilitySheet+"!H"+row+"：初始可用性由剧情固定（粮田可用，工坊待修复）");
                    rules.OpeningFacilityCapacity[index]=(int)capacity;rules.OpeningFacilityMinimum[index]=(int)minimum;
                }
                const string growth="04a 队长成长与训练";
                var gr=BattleBalanceImporter.Section(archive,growth,6,14,"ABCDEG");
                Header(gr,growth,"A6","成长组ID");Header(gr,growth,"B6","等级");Header(gr,growth,"C6","生命倍率");Header(gr,growth,"D6","攻击倍率");Header(gr,growth,"E6","防御倍率");Header(gr,growth,"G6","启用");
                rules.AttributeGrowthVersion=1;rules.AttackGrowth=rules.Growth.ToArray();rules.DefenseGrowth=rules.Growth.ToArray();
                seen.Clear();
                for(int row=7;row<=14;row++)
                {
                    if(Need(gr,growth,"A"+row)!="growth_shared")throw new InvalidOperationException(growth+"!A"+row+"：未知成长组");
                    double level=Number(gr,growth,"B"+row,2,9);if(level!=Math.Truncate(level)||!seen.Add(level.ToString(CultureInfo.InvariantCulture)))throw new InvalidOperationException(growth+"!B"+row+"：等级须为唯一整数");
                    double hp=Number(gr,growth,"C"+row,.01,100),attack=Number(gr,growth,"D"+row,.01,100),defense=Number(gr,growth,"E"+row,.01,100);
                    if(Need(gr,growth,"G"+row)!="1"||!gr.TryGetValue("#type:G"+row,out var enabledType)||enabledType!="b")throw new InvalidOperationException(growth+"!G"+row+"：当前成长必须启用");
                    rules.Growth[(int)level-1]=hp;rules.AttackGrowth[(int)level-1]=attack;rules.DefenseGrowth[(int)level-1]=defense;
                }
                var training=BattleBalanceImporter.Section(archive,growth,19,43,"ABCDEFGHJ");
                var trainingHeaders=new Dictionary<string,string>{{"A19","成长组ID"},{"B19","目标等级"},{"C19","耗时日"},{"D19","职业ID"},{"E19","兵书ID"},{"F19","兵书数量"},{"G19","额外钱币"},{"H19","取消退还比例"},{"J19","启用"}};
                foreach(var pair in trainingHeaders)Header(training,growth,pair.Key,pair.Value);
                seen.Clear();
                for(int row=20;row<=43;row++)
                {
                    string profession=Need(training,growth,"D"+row);int index=Array.IndexOf(ids,profession);
                    if(Need(training,growth,"A"+row)!="growth_shared"||index<0)throw new InvalidOperationException(growth+" 第"+row+"行：成长组或职业无效");
                    double target=Number(training,growth,"B"+row,2,9);if(target!=Math.Truncate(target)||!seen.Add(profession+":"+target))throw new InvalidOperationException(growth+"!B"+row+"：职业与等级重复或等级非整数");
                    var kind=index==0?SoldierKind.Melee:index==1?SoldierKind.Ranged:SoldierKind.Cavalry;
                    if(Need(training,growth,"E"+row)!=BaseTraining.Book(kind,(int)target))throw new InvalidOperationException(growth+"!E"+row+"：兵书与职业或等级不符");
                    double days=Number(training,growth,"C"+row,1,365),books=Number(training,growth,"F"+row,1,1000);
                    if(days!=Math.Truncate(days)||books!=Math.Truncate(books))throw new InvalidOperationException(growth+" 第"+row+"行：训练天数与兵书数量须为整数");
                    if(Number(training,growth,"G"+row,0,0)!=0||Number(training,growth,"H"+row,0,0)!=0)throw new InvalidOperationException(growth+" 第"+row+"行：当前训练无额外金币且取消不退款");
                    if(Need(training,growth,"J"+row)!="1"||!training.TryGetValue("#type:J"+row,out var enabledType)||enabledType!="b")throw new InvalidOperationException(growth+"!J"+row+"：当前训练规则必须启用");
                    int entry=index*8+(int)target-2;rules.TrainingDays[entry]=(int)days;rules.TrainingBooks[entry]=(int)books;
                }
                const string recruiting="03c 招募与招降";
                var rec=BattleBalanceImporter.Section(archive,recruiting,6,9,"ABCDHJ");
                Header(rec,recruiting,"A6","招募规则ID");Header(rec,recruiting,"B6","兵种模板ID");Header(rec,recruiting,"C6","基准B");Header(rec,recruiting,"D6","基础单价");Header(rec,recruiting,"H6","当天不生产");Header(rec,recruiting,"J6","启用");
                seen.Clear();
                for(int row=7;row<=9;row++)
                {
                    string id=Need(rec,recruiting,"A"+row);int index=Array.IndexOf(ids,id.StartsWith("recruit_")?id.Substring(8):"");
                    if(index<0||!seen.Add(id)||Need(rec,recruiting,"B"+row)!="soldier_"+ids[index])throw new InvalidOperationException(recruiting+"!A"+row+"/B"+row+"：招募ID或模板引用无效");
                    double baseline=Number(rec,recruiting,"C"+row,1,10000),price=Number(rec,recruiting,"D"+row,1,1000000);
                    if(baseline!=Math.Truncate(baseline)||price!=Math.Truncate(price))throw new InvalidOperationException(recruiting+" 第"+row+"行：兵力基准及单价须为整数");
                    if(Need(rec,recruiting,"H"+row)!="0"||!rec.TryGetValue("#type:H"+row,out var canWorkType)||canWorkType!="b")throw new InvalidOperationException(recruiting+"!H"+row+"：新兵当天可工作，应填写布尔否");
                    if(Need(rec,recruiting,"J"+row)!="1"||!rec.TryGetValue("#type:J"+row,out var enabledType)||enabledType!="b")throw new InvalidOperationException(recruiting+"!J"+row+"：当前招募须启用");
                    rules.TroopBaseline[index]=(int)baseline;rules.RecruitBasePrices[index]=(int)price;
                }
                var recruitFactors=BattleBalanceImporter.Section(archive,recruiting,20,22,"ACD");
                Header(recruitFactors,recruiting,"A20","参数ID");Header(recruitFactors,recruiting,"C20","数值");Header(recruitFactors,recruiting,"D20","单位");
                seen.Clear();
                for(int row=21;row<=22;row++)
                {
                    string id=Need(recruitFactors,recruiting,"A"+row);
                    if(!seen.Add(id))throw new InvalidOperationException(recruiting+"!A"+row+"：参数ID重复");
                    if(Need(recruitFactors,recruiting,"D"+row)!="倍率")throw new InvalidOperationException(recruiting+"!D"+row+"：单位应为倍率");
                    if(id=="recruit_supply_factor")rules.RecruitSupplyFactor=Number(recruitFactors,recruiting,"C"+row,0,1);
                    else if(id=="recruit_price_sensitivity")rules.RecruitPriceSensitivity=Number(recruitFactors,recruiting,"C"+row,0,10);
                    else throw new InvalidOperationException(recruiting+"!A"+row+"：参数ID无效");
                }
                const string supplies="06b 军需与配方";
                var sup=BattleBalanceImporter.Section(archive,supplies,6,8,"ABDEH");
                Header(sup,supplies,"A6","物品ID");Header(sup,supplies,"B6","等级");Header(sup,supplies,"D6","攻击加值");Header(sup,supplies,"E6","防御加值");Header(sup,supplies,"H6","启用");
                for(int level=1;level<=2;level++)
                {
                    int row=level+6;if(Need(sup,supplies,"A"+row)!="supply"+level||Number(sup,supplies,"B"+row,level,level)!=level)throw new InvalidOperationException(supplies+" 第"+row+"行：应为通用军需"+level+"级");
                    double atk=Number(sup,supplies,"D"+row,0,1000),def=Number(sup,supplies,"E"+row,0,1000);
                    if(Need(sup,supplies,"H"+row)!="1"||!sup.TryGetValue("#type:H"+row,out var enabledType)||enabledType!="b")throw new InvalidOperationException(supplies+"!H"+row+"：军需必须启用");
                    if(level==1){rules.SupplyAttackLow=atk;rules.SupplyDefenseLow=def;}else{rules.SupplyAttackHigh=atk;rules.SupplyDefenseHigh=def;}
                }
                var recipe=BattleBalanceImporter.Section(archive,supplies,17,19,"ABCDE");
                Header(recipe,supplies,"A17","配方ID");Header(recipe,supplies,"B17","产物ID");Header(recipe,supplies,"C17","产物数量");Header(recipe,supplies,"D17","工作量");Header(recipe,supplies,"E17","所需工坊等级");
                var material=BattleBalanceImporter.Section(archive,supplies,28,30,"ABC");
                Header(material,supplies,"A28","配方ID");Header(material,supplies,"B28","原料ID");Header(material,supplies,"C28","数量");
                int unitMaterial=0;
                for(int level=1;level<=2;level++)
                {
                    int rr=level+17,mr=level+28;string id="recipe_supply"+level;
                    if(Need(recipe,supplies,"A"+rr)!=id||Need(recipe,supplies,"B"+rr)!="supply"+level||Number(recipe,supplies,"C"+rr,1,1)!=1||Number(recipe,supplies,"D"+rr,1,99)<1||Number(recipe,supplies,"E"+rr,1,2)!=level)throw new InvalidOperationException(supplies+" 第"+rr+"行：产量、工作量或所需工坊等级不符合生产规则");
                    if(Need(material,supplies,"A"+mr)!=id||Need(material,supplies,"B"+mr)!="materials")throw new InvalidOperationException(supplies+" 第"+mr+"行：配方材料引用无效");
                    double amount=Number(material,supplies,"C"+mr,1,100);if(amount!=Math.Truncate(amount))throw new InvalidOperationException(supplies+"!C"+mr+"：原料数量须为整数");
                    if(level==1)unitMaterial=(int)amount;else if(amount<unitMaterial*2)throw new InvalidOperationException(supplies+"!C"+mr+"：二级军需原料数不得少于一级的两倍");
                }
                rules.SupplyMaterial=unitMaterial;
                const string objectSheet="02c 地形与物件";
                var objects=BattleBalanceImporter.Section(archive,objectSheet,15,17,"ACDEFGHIK");
                var objectHeaders=new Dictionary<string,string>{{"A15","物件规则ID"},{"C15","耐久参考倍率"},{"D15","倒下长度"},{"E15","滚动距离"},{"F15","滚动速度"},{"G15","伤害参考倍率"},{"H15","参考模板ID"},{"I15","暴击开关"},{"K15","启用"}};
                foreach(var pair in objectHeaders)Header(objects,objectSheet,pair.Key,pair.Value);
                if(Need(objects,objectSheet,"A16")!="prop_tree"||Need(objects,objectSheet,"A17")!="prop_boulder")throw new InvalidOperationException(objectSheet+"!A16/A17：物件ID不匹配");
                foreach(int row in new[]{16,17})
                {
                    if(Need(objects,objectSheet,"H"+row)!="soldier_saber")throw new InvalidOperationException(objectSheet+"!H"+row+"：当前参考剑士士兵模板");
                    if(Need(objects,objectSheet,"I"+row)!="0"||!objects.TryGetValue("#type:I"+row,out var critType)||critType!="b")throw new InvalidOperationException(objectSheet+"!I"+row+"：物件不掷暴击");
                    if(Need(objects,objectSheet,"K"+row)!="1"||!objects.TryGetValue("#type:K"+row,out var enabledType)||enabledType!="b")throw new InvalidOperationException(objectSheet+"!K"+row+"：当前物件规则须启用");
                }
                double treeLength=Number(objects,objectSheet,"D16",1,8),rockDistance=Number(objects,objectSheet,"E17",1,16);
                if(treeLength!=Math.Truncate(treeLength)||rockDistance!=Math.Truncate(rockDistance))throw new InvalidOperationException(objectSheet+"：倒下长度与滚动距离须为整数");
                var tactical=new BattleTacticalRules {TreeHits=Number(objects,objectSheet,"C16",.01,20),TreeLength=(int)treeLength,RockDistance=(int)rockDistance,RockSpeed=Number(objects,objectSheet,"F17",.01,32),RockDamage=Number(objects,objectSheet,"G17",.01,100)};tactical.Validate();
                const string ruins="09d 奖励与遗迹";
                var reward=BattleBalanceImporter.Section(archive,ruins,39,47,"ACD");
                Header(reward,ruins,"A39","参数ID");Header(reward,ruins,"C39","数值");Header(reward,ruins,"D39","类型");
                var rewardState=new BaseRuinsRewardState();
                var rewardKeys=new Dictionary<string,(string type,Action<double> apply,double min,double max)>{
                    {"max_energy",("INT",x=>rewardState.MaximumEnergy=(int)x,1,10000)},
                    {"reward_cost",("INT",x=>rewardState.RewardCost=(int)x,1,10000)},
                    {"exposure_fraction",("RATE",x=>rewardState.ExposureFraction=x,.000001,1)},
                    {"first_coins",("INT",x=>rewardState.FirstCoins=(int)x,0,1000000)},
                    {"first_food",("INT",x=>rewardState.FirstFood=(int)x,0,1000000)},
                    {"first_materials",("INT",x=>rewardState.FirstMaterials=(int)x,0,1000000)},
                    {"repeat_coins",("INT",x=>rewardState.Coins=(int)x,0,1000000)},
                    {"stone_count",("INT",x=>rewardState.ItemCount=(int)x,1,99)}
                };
                seen.Clear();var rewardRows=new Dictionary<string,int>();
                for(int row=40;row<=47;row++)
                {
                    string id=Need(reward,ruins,"A"+row);
                    if(!rewardKeys.TryGetValue(id,out var item)||!seen.Add(id))throw new InvalidOperationException(ruins+"!A"+row+"：参数ID不存在或重复："+id);
                    rewardRows[id]=row;
                    if(Need(reward,ruins,"D"+row)!=item.type)throw new InvalidOperationException(ruins+"!D"+row+"：参数类型应为"+item.type);
                    double value=Number(reward,ruins,"C"+row,item.min,item.max);
                    if(item.type=="INT"&&value!=Math.Truncate(value))throw new InvalidOperationException(ruins+"!C"+row+"：参数须为整数");item.apply(value);
                }
                if(rewardState.RewardCost>rewardState.MaximumEnergy)throw new InvalidOperationException(ruins+"!C"+rewardRows["reward_cost"]+"：领奖消耗不能超过体力上限");
                rewardState.Energy=rewardState.MaximumEnergy;BaseRuinsRewards.ValidateDefaults(rewardState);
                var lord=BattleBalanceImporter.Section(archive,ruins,52,55,"ACD");
                Header(lord,ruins,"A52","参数ID");Header(lord,ruins,"C52","数值");Header(lord,ruins,"D52","类型");
                var lordReward=new BaseInventory();
                var lordKeys=new Dictionary<string,Action<int>>{
                    {"lord_food",v=>lordReward.Food=v},{"lord_materials",v=>lordReward.Materials=v},{"lord_coins",v=>lordReward.Coins=v}};
                seen.Clear();
                for(int row=53;row<=55;row++)
                {
                    string id=Need(lord,ruins,"A"+row);
                    if(!lordKeys.TryGetValue(id,out var assign)||!seen.Add(id))throw new InvalidOperationException(ruins+"!A"+row+"：参数ID不存在或重复："+id);
                    if(Need(lord,ruins,"D"+row)!="INT")throw new InvalidOperationException(ruins+"!D"+row+"：参数类型应为INT");
                    double value=Number(lord,ruins,"C"+row,0,1000000);
                    if(value!=Math.Truncate(value))throw new InvalidOperationException(ruins+"!C"+row+"：参数须为整数");
                    assign((int)value);
                }
                rules.OpeningLordReward=lordReward;
                var battleRewards=BattleBalanceImporter.Section(archive,ruins,16,16,"ACDE");
                Header(battleRewards,ruins,"A16","奖励组ID");Header(battleRewards,ruins,"C16","对象类型");Header(battleRewards,ruins,"D16","对象ID");Header(battleRewards,ruins,"E16","数量");
                rules.OpeningDefenseReward=new BaseInventory();rules.OpeningDefenseLoss=new BaseInventory();rules.OpeningCampReward=new BaseInventory();
                var defenseWin=BattleBalanceImporter.Section(archive,ruins,21,23,"ACDE");var campWin=BattleBalanceImporter.Section(archive,ruins,25,27,"ACDE");var defenseLoss=BattleBalanceImporter.Section(archive,ruins,32,34,"ACDE");
                foreach(var entry in new (int row,string group,string item,BaseInventory target)[]{(21,"reward_defense_win","food",rules.OpeningDefenseReward),(22,"reward_defense_win","materials",rules.OpeningDefenseReward),(23,"reward_defense_win","coins",rules.OpeningDefenseReward),(25,"reward_camp_win","food",rules.OpeningCampReward),(26,"reward_camp_win","materials",rules.OpeningCampReward),(27,"reward_camp_win","coins",rules.OpeningCampReward),(32,"loot_per_enemy","food",rules.OpeningDefenseLoss),(33,"loot_per_enemy","materials",rules.OpeningDefenseLoss),(34,"loot_per_enemy","coins",rules.OpeningDefenseLoss)})
                {
                    int row=entry.row;battleRewards=row<=23?defenseWin:row<=27?campWin:defenseLoss;if(Need(battleRewards,ruins,"A"+row)!=entry.group||Need(battleRewards,ruins,"C"+row)!="资源"||Need(battleRewards,ruins,"D"+row)!=entry.item)throw new InvalidOperationException(ruins+" 第"+row+"行：战斗奖惩的组别或资源ID不匹配");
                    double value=Number(battleRewards,ruins,"E"+row,0,1000000);if(value!=Math.Truncate(value))throw new InvalidOperationException(ruins+"!E"+row+"：战斗奖惩数量须为整数");
                    if(entry.item=="food")entry.target.Food=(int)value;else if(entry.item=="materials")entry.target.Materials=(int)value;else entry.target.Coins=(int)value;
                }
                rules.OpeningBattleRewardVersion=1;
                rules.OpeningTactical=tactical;rules.OpeningRewards=rewardState;rules.OpeningAuxVersion=1;
                const string openingSheet="09b 开局配置";
                var openingItems=BattleBalanceImporter.Section(archive,openingSheet,6,9,"ABCE");
                Header(openingItems,openingSheet,"A6","开局ID");Header(openingItems,openingSheet,"B6","资源ID");Header(openingItems,openingSheet,"C6","数量");Header(openingItems,openingSheet,"E6","启用");
                var initial=new BaseInventory();seen.Clear();
                for(int row=7;row<=9;row++)
                {
                    if(Need(openingItems,openingSheet,"A"+row)!="demo_start")throw new InvalidOperationException(openingSheet+"!A"+row+"：未知开局ID");
                    if(Need(openingItems,openingSheet,"E"+row)!="1"||!openingItems.TryGetValue("#type:E"+row,out var enabledType)||enabledType!="b")throw new InvalidOperationException(openingSheet+"!E"+row+"：开局资源须启用（布尔值）；不需要时请将数量设为0");
                    string id=Need(openingItems,openingSheet,"B"+row);double value=Number(openingItems,openingSheet,"C"+row,0,100000000);
                    if(!seen.Add(id)||value!=Math.Truncate(value))throw new InvalidOperationException(openingSheet+" 第"+row+"行：资源ID重复或数量非整数");
                    if(id=="food")initial.Food=(int)value;else if(id=="materials")initial.Materials=(int)value;else if(id=="coins")initial.Coins=(int)value;else throw new InvalidOperationException(openingSheet+"!B"+row+"：未知资源ID");
                }
                var initialSupply=BattleBalanceImporter.Section(archive,openingSheet,22,24,"ABCE");
                Header(initialSupply,openingSheet,"A22","开局ID");Header(initialSupply,openingSheet,"B22","军需ID");Header(initialSupply,openingSheet,"C22","数量");Header(initialSupply,openingSheet,"E22","启用");seen.Clear();
                for(int row=23;row<=24;row++)
                {
                    if(Need(initialSupply,openingSheet,"A"+row)!="demo_start")throw new InvalidOperationException(openingSheet+"!A"+row+"：未知开局ID");
                    if(Need(initialSupply,openingSheet,"E"+row)!="1"||!initialSupply.TryGetValue("#type:E"+row,out var enabledType)||enabledType!="b")throw new InvalidOperationException(openingSheet+"!E"+row+"：开局军需须启用（布尔值）；不需要时请将数量设为0");
                    string id=Need(initialSupply,openingSheet,"B"+row);int level=id=="supply1"?1:id=="supply2"?2:0;double value=Number(initialSupply,openingSheet,"C"+row,0,100000000);
                    if(level==0||!seen.Add(id)||value!=Math.Truncate(value))throw new InvalidOperationException(openingSheet+" 第"+row+"行：军需ID重复、无效或数量非整数");
                    initial.Equipment.Add(new BaseEquipmentStock {Product="supply",Level=level,Count=(int)value});
                }
                rules.OpeningInventory=initial;
                var secondRaid=BattleBalanceImporter.Section(archive,openingSheet,27,27,"ABCE");
                if(Need(secondRaid,openingSheet,"A27")!="second_raid"||Need(secondRaid,openingSheet,"B27")!="countdown_days"||Need(secondRaid,openingSheet,"E27")!="1"||!secondRaid.TryGetValue("#type:E27",out var secondEnabled)||secondEnabled!="b")throw new InvalidOperationException(openingSheet+" 第27行：第二波倒计时配置身份或启用标记非法");
                double raidDays=Number(secondRaid,openingSheet,"C27",1,365);if(raidDays!=Math.Truncate(raidDays))throw new InvalidOperationException(openingSheet+"!C27：倒计时必须为整数日");rules.SecondRaidDays=(int)raidDays;
                const string skills="10c 效果定义与明细",traits="11b 特性数值与互斥";
                var sk=BattleBalanceImporter.Section(archive,skills,6,12,"ABCDEFGHIJ");
                var skillHeaders=new[]{"技能ID","名称","职业索引","效果数值","持续秒","冷却秒","半径细格","移速加成比例","开场冷却秒","效果数值单位"};
                for(int i=0;i<skillHeaders.Length;i++)Header(sk,skills,((char)('A'+i))+"6",skillHeaders[i]);
                var units=new Dictionary<string,string>{{"inspire","攻击加成比例"},{"march","细格/秒"},{"heal","生命/秒"},{"sweep","攻击倍率"},{"charge","下一击倍率"},{"rapid_fire","攻速加成比例"}};
                seen.Clear();
                for(int row=7;row<=12;row++)
                {
                    string id=Need(sk,skills,"A"+row);var skill=rules.Skills.SingleOrDefault(x=>x.Id==id);
                    if(skill==null||!seen.Add(id))throw new InvalidOperationException(skills+"!A"+row+"：技能ID不存在或重复："+id);
                    if(Need(sk,skills,"J"+row)!=units[id])throw new InvalidOperationException(skills+"!J"+row+"：技能单位不匹配");
                    double profession=Number(sk,skills,"C"+row,-1,2);
                    if(profession!=skill.Profession)throw new InvalidOperationException(skills+"!C"+row+"：该技能的职业限定由规则定义");
                    skill.Name=Need(sk,skills,"B"+row);skill.Amount=Number(sk,skills,"D"+row,.0001,100000);
                    skill.Duration=Number(sk,skills,"E"+row,0,600);skill.Cooldown=Number(sk,skills,"F"+row,.01,3600);
                    skill.Radius=Number(sk,skills,"G"+row,0,50);skill.SpeedBonus=Number(sk,skills,"H"+row,0,10);skill.Opening=Number(sk,skills,"I"+row,0,3600);
                    if(id=="sweep"&&skill.Radius<=0)throw new InvalidOperationException(skills+"!G"+row+"：横扫半径须大于0");
                    if(id=="heal"&&skill.Duration<1)throw new InvalidOperationException(skills+"!E"+row+"：治疗每秒生效一次，持续时间须至少1秒");
                    if(id=="sweep"&&skill.Duration!=0)throw new InvalidOperationException(skills+"!E"+row+"：横扫立即结算，持续时间须为0");
                    if(id!="sweep"&&id!="heal"&&skill.Duration<=0)throw new InvalidOperationException(skills+"!E"+row+"：持续技能时间须大于0");
                    if(id!="sweep"&&skill.Radius!=0)throw new InvalidOperationException(skills+"!G"+row+"：该技能不使用半径，请填写0");
                    if(id!="charge"&&skill.SpeedBonus!=0)throw new InvalidOperationException(skills+"!H"+row+"：该技能不使用此移速加成，请填写0");
                }
                var tr=BattleBalanceImporter.Section(archive,traits,6,27,"ABCDEFGH");
                var traitHeaders=new[]{"特性ID","名称","最小值","最大值","步长","抽取权重","互斥组ID","单位"};
                for(int i=0;i<traitHeaders.Length;i++)Header(tr,traits,((char)('A'+i))+"6",traitHeaders[i]);
                seen.Clear();var groups=new HashSet<string>(rules.Traits.Select(x=>x.Conflict));
                for(int row=7;row<=27;row++)
                {
                    string id=Need(tr,traits,"A"+row);var trait=rules.Traits.SingleOrDefault(x=>x.Id==id);
                    if(trait==null||!seen.Add(id))throw new InvalidOperationException(traits+"!A"+row+"：特性ID不存在或重复："+id);
                    string unit=id=="advantage"||id=="disadvantage"?"层":id=="taunt"||id=="inconspicuous"||id=="drunkard"?"倍率":"百分点";
                    if(Need(tr,traits,"H"+row)!=unit)throw new InvalidOperationException(traits+"!H"+row+"：特性单位应为"+unit);
                    string group=Need(tr,traits,"G"+row);if(!groups.Contains(group))throw new InvalidOperationException(traits+"!G"+row+"：未知互斥组 "+group);
                    trait.Name=Need(tr,traits,"B"+row);trait.Min=Number(tr,traits,"C"+row,.01,unit=="倍率"?10:unit=="层"?10:99);
                    trait.Max=Number(tr,traits,"D"+row,trait.Min,unit=="倍率"?10:unit=="层"?10:99);
                    trait.Step=Number(tr,traits,"E"+row,.01,100);trait.Weight=Number(tr,traits,"F"+row,.0001,10000);trait.Conflict=group;
                    if(Math.Abs((trait.Max-trait.Min)/trait.Step-Math.Round((trait.Max-trait.Min)/trait.Step))>1e-6)throw new InvalidOperationException(traits+"!E"+row+"：范围与步长不整除");
                    if(unit=="层"&&(trait.Min!=Math.Truncate(trait.Min)||trait.Max!=Math.Truncate(trait.Max)||trait.Step!=Math.Truncate(trait.Step)))throw new InvalidOperationException(traits+" 第"+row+"行：层数必须为整数");
                }
                const string storySheet="09a 预设人物与初装";
                var story=BattleBalanceImporter.Section(archive,storySheet,29,32,"ABCDE");
                var storyHeaders=new[]{"人物ID","主动技能ID","特性ID","特性数值","启用"};
                for(int i=0;i<storyHeaders.Length;i++)Header(story,storySheet,((char)('A'+i))+"29",storyHeaders[i]);
                rules.StoryPresets=new List<RpgStoryPreset>();seen.Clear();
                for(int row=30;row<=32;row++)
                {
                    string id=Need(story,storySheet,"A"+row);
                    if(!new[]{"player","partner_farm","partner_craft"}.Contains(id)||!seen.Add(id))throw new InvalidOperationException(storySheet+"!A"+row+"：人物ID不存在或重复");
                    if(Need(story,storySheet,"E"+row)!="1"||!story.TryGetValue("#type:E"+row,out var enabledType)||enabledType!="b")throw new InvalidOperationException(storySheet+"!E"+row+"：固定人物配置须启用（布尔值）");
                    var preset=new RpgStoryPreset{Id=id,SkillId=Need(story,storySheet,"B"+row),TraitId=story.TryGetValue("C"+row,out var traitId)?traitId:"",TraitValue=Number(story,storySheet,"D"+row,0,100)};
                    int profession=id=="partner_farm"?1:id=="partner_craft"?2:0;
                    var character=BaseRpg.Character(id,profession,id!="partner_farm",true,rules);character.SkillId=preset.SkillId;
                    if(preset.TraitId!="")character.Traits.Add(new RpgTrait{Id=preset.TraitId,Value=preset.TraitValue});
                    else if(preset.TraitValue!=0)throw new InvalidOperationException(storySheet+"!D"+row+"：未配置特性时数值须为0");
                    try{BaseRpg.ValidateCharacter(character,rules);}catch(ArgumentException e){throw new InvalidOperationException(storySheet+" 第"+row+"行：技能或特性引用、职业或数值不合法",e);}
                    rules.StoryPresets.Add(preset);
                }
                var extra=BattleBalanceImporter.Section(archive,storySheet,35,41,"ABCD");
                var extraHeaders=new[]{"人物ID","槽位","特性ID","特性数值"};
                for(int i=0;i<extraHeaders.Length;i++)Header(extra,storySheet,((char)('A'+i))+"35",extraHeaders[i]);
                seen.Clear();
                for(int row=36;row<=41;row++)
                {
                    string id=Need(extra,storySheet,"A"+row);double slot=Number(extra,storySheet,"B"+row,2,3);
                    var preset=rules.StoryPresets.SingleOrDefault(p=>p.Id==id);
                    if(preset==null||slot!=Math.Truncate(slot)||!seen.Add(id+":"+slot))throw new InvalidOperationException(storySheet+" 第"+row+"行：人物或特性槽位无效、重复");
                    string trait=extra.TryGetValue("C"+row,out var value)?value:"";double amount=Number(extra,storySheet,"D"+row,0,100);
                    if(trait==""){if(amount!=0)throw new InvalidOperationException(storySheet+"!D"+row+"：未配置特性时数值须为0");continue;}
                    preset.ExtraTraits.Add(new RpgTrait{Id=trait,Value=amount});
                    int profession=id=="partner_farm"?1:id=="partner_craft"?2:0;
                    var character=BaseRpg.Character(id,profession,id!="partner_farm",true,rules);character.SkillId=preset.SkillId;
                    if(preset.TraitId!="")character.Traits.Add(new RpgTrait{Id=preset.TraitId,Value=preset.TraitValue});
                    character.Traits.AddRange(preset.ExtraTraits.Select(t=>t.Copy()));
                    try{BaseRpg.ValidateCharacter(character,rules);}catch(ArgumentException e){throw new InvalidOperationException(storySheet+" 第"+row+"行：特性不存在、数值越界、重复或互斥",e);}
                }
                const string qualityPool="08a 随机池";
                var pool=BattleBalanceImporter.Section(archive,qualityPool,13,19,"ABCDH");
                Header(pool,qualityPool,"A13","池ID");Header(pool,qualityPool,"B13","序号");Header(pool,qualityPool,"C13","对象ID");Header(pool,qualityPool,"D13","权重");Header(pool,qualityPool,"H13","启用");
                seen.Clear();double totalQuality=0;
                for(int row=14;row<=19;row++)
                {
                    string id=Need(pool,qualityPool,"C"+row);
                    int index=Array.IndexOf(new[]{"quality_0","quality_1","quality_2","quality_3","quality_4","quality_5"},id);
                    if(Need(pool,qualityPool,"A"+row)!="pool_quality"||index<0||!seen.Add(id))throw new InvalidOperationException(qualityPool+"!A"+row+"/C"+row+"：品质池或品质ID无效、重复");
                    if(Number(pool,qualityPool,"B"+row,row-13,row-13)!=row-13)throw new InvalidOperationException(qualityPool+"!B"+row+"：品质池序号不正确");
                    if(Need(pool,qualityPool,"H"+row)!="1"||!pool.TryGetValue("#type:H"+row,out var enabledType)||enabledType!="b")throw new InvalidOperationException(qualityPool+"!H"+row+"：品质池条目须启用（布尔值）；不掉落时权重填0");
                    double weight=Number(pool,qualityPool,"D"+row,0,100);rules.DropQuality[index]=weight/100;totalQuality+=weight;
                }
                if(Math.Abs(totalQuality-100)>1e-8)throw new InvalidOperationException(qualityPool+"!D14:D19：品质概率合计必须为100");
                ApplyEquipmentPrices(archive,rules);
                const string prices="05c 装备价格";
                var goods=BattleBalanceImporter.Section(archive,prices,43,51,"ABC");
                Header(goods,prices,"A43","商品价值ID");Header(goods,prices,"B43","基础价值");Header(goods,prices,"C43","单位");
                var goodsKeys=new Dictionary<string,(string unit,Action<int> apply)>{
                    {"food",("钱币/份",x=>rules.FoodValue=x)},{"materials",("钱币/份",x=>rules.MaterialValue=x)},
                    {"book",("钱币/本",x=>rules.BookValue=x)},{"advanced_book",("钱币/本",x=>rules.AdvancedBookValue=x)},
                    {"forging_stone",("钱币/个",x=>rules.StoneValue=x)},{"wine",("钱币/份",x=>rules.WineValue=x)},
                    {"other",("钱币/份",x=>rules.OtherValue=x)},{"supply_work",("钱币/工作点",x=>rules.SupplyWorkValue=x)}
                };
                seen.Clear();
                for(int row=44;row<=51;row++)
                {
                    string id=Need(goods,prices,"A"+row);
                    if(!goodsKeys.TryGetValue(id,out var entry)||!seen.Add(id))throw new InvalidOperationException(prices+"!A"+row+"：商品价值ID无效或重复");
                    if(Need(goods,prices,"C"+row)!=entry.unit)throw new InvalidOperationException(prices+"!C"+row+"：单位不匹配");
                    double value=Number(goods,prices,"B"+row,1,1000000);
                    if(value!=Math.Truncate(value))throw new InvalidOperationException(prices+"!B"+row+"：基础价值须为整数");
                    entry.apply((int)value);
                }
                var stock=BattleBalanceImporter.Section(archive,prices,54,59,"AB");
                Header(stock,prices,"A54","商品ID");Header(stock,prices,"B54","每日供应量");
                rules.OpeningTownStock=new int[4];seen.Clear();
                for(int row=55;row<=59;row++)
                {
                    string id=Need(stock,prices,"A"+row);int index=Array.IndexOf(new[]{"food","materials","supply1","supply2","forging_stone"},id);
                    if(index<0||!seen.Add(id))throw new InvalidOperationException(prices+"!A"+row+"：商品ID无效或重复");
                    double value=Number(stock,prices,"B"+row,1,index==4?99:100000);
                    if(value!=Math.Truncate(value))throw new InvalidOperationException(prices+"!B"+row+"：每日供应量须为整数");
                    if(index==4)rules.ForgeDailyStock=(int)value;else rules.OpeningTownStock[index]=(int)value;
                }
                // Optional until the two formal prices are decided; incomplete configuration must fail import.
                ApplyIntelPrices(rules,BattleBalanceImporter.Section(archive,prices,62,64,"AB"));
                var camp=BattleBalanceImporter.Section(archive,economy,25,28,"ABCD");
                Header(camp,economy,"A25","营帐等级");Header(camp,economy,"B25","队长容量");Header(camp,economy,"C25","升级原料");Header(camp,economy,"D25","升级钱币");
                for(int level=1;level<=3;level++)
                {
                    int row=level+25;Number(camp,economy,"A"+row,level,level);
                    double capacity=Number(camp,economy,"B"+row,level==1?3:1,10000),materialsCost=Number(camp,economy,"C"+row,0,1000000),coinCost=Number(camp,economy,"D"+row,0,1000000);
                    if(capacity!=Math.Truncate(capacity)||materialsCost!=Math.Truncate(materialsCost)||coinCost!=Math.Truncate(coinCost))throw new InvalidOperationException(economy+" 第"+row+"行：容量和升级费用须为整数");
                    if(level==1&&(materialsCost!=0||coinCost!=0))throw new InvalidOperationException(economy+" 第26行：初始营帐不需要升级费用");
                    if(level>1){rules.CampMaterials[level-2]=(int)materialsCost;rules.CampCoins[level-2]=(int)coinCost;}
                    rules.Capacity[level-1]=(int)capacity;
                    if(level>1&&rules.Capacity[level-1]<rules.Capacity[level-2])throw new InvalidOperationException(economy+"!B"+row+"：升级后容量不能减少");
                }
                var repairs=BattleBalanceImporter.Section(archive,economy,35,37,"ABC");
                Header(repairs,economy,"A35","设施修复参数ID");Header(repairs,economy,"B35","值");Header(repairs,economy,"C35","单位");
                var repairKeys=new Dictionary<string,(string unit,Action<int> apply)>{{"repair_materials",("原料/设施",x=>rules.FacilityRepairMaterials=x)},{"repair_coins",("钱币/设施",x=>rules.FacilityRepairCoins=x)}};
                seen.Clear();
                for(int row=36;row<=37;row++)
                {
                    string id=Need(repairs,economy,"A"+row);
                    if(!repairKeys.TryGetValue(id,out var entry)||!seen.Add(id))throw new InvalidOperationException(economy+"!A"+row+"：修复参数ID无效或重复");
                    if(Need(repairs,economy,"C"+row)!=entry.unit)throw new InvalidOperationException(economy+"!C"+row+"：单位不匹配");
                    double value=Number(repairs,economy,"B"+row,0,1000000);if(value!=Math.Truncate(value))throw new InvalidOperationException(economy+"!B"+row+"：修复费用须为整数");entry.apply((int)value);
                }
                const string formula="02a 全局参数与试算";
                var trade=BattleBalanceImporter.Section(archive,formula,21,22,"AC");
                if(Need(trade,formula,"A21")!="goods_buy_multiplier"||Need(trade,formula,"A22")!="goods_sell_multiplier")throw new InvalidOperationException(formula+"!A21/A22：交易参数ID不匹配");
                rules.BuyFactor=Number(trade,formula,"C21",.0001,100);rules.SellFactor=Number(trade,formula,"C22",0,100);
                if(rules.BuyFactor<=rules.SellFactor)throw new InvalidOperationException(formula+"!C21/C22：购买倍率须高于出售倍率");
                var armor=BattleBalanceImporter.Section(archive,formula,34,35,"AC");
                if(Need(armor,formula,"A34")!="armor_k"||Need(armor,formula,"A35")!="armor_mitigation_cap")throw new InvalidOperationException(formula+"!A34/A35：护甲参数ID不匹配");
                rules.ArmorK=Number(armor,formula,"C34",.0001,100000);rules.ArmorCap=Number(armor,formula,"C35",0,.999999);
                var wear=BattleBalanceImporter.Section(archive,formula,46,47,"ACD");
                if(Need(wear,formula,"A46")!="supply_returned_wear"||Need(wear,formula,"A47")!="supply_lost_wear")throw new InvalidOperationException(formula+"!A46/A47：军需磨损参数ID不匹配");
                if(Need(wear,formula,"D46")!="RATE"||Need(wear,formula,"D47")!="RATE")throw new InvalidOperationException(formula+"!D46/D47：军需磨损类型须为RATE");
                rules.OpeningSupplyWearVersion=1;rules.OpeningReturnedWear=Number(wear,formula,"C46",0,1);rules.OpeningLostWear=Number(wear,formula,"C47",0,1);
                var config=BattleBalanceImporter.Section(archive,formula,75,94,"ACD");
                Header(config,formula,"A75","参数ID");Header(config,formula,"C75","参数值");Header(config,formula,"D75","单位");
                var keys=new Dictionary<string,(string unit,Action<double> apply,double min,double max)>{
                    {"critical_multiplier",("倍",x=>rules.CriticalMultiplier=x,1,10)},
                    {"base_interval",("秒",x=>rules.BaseInterval=x,.2,60)},
                    {"minimum_interval",("秒",x=>rules.MinimumInterval=x,.2,60)},
                    {"arrow_speed",("细格/秒",x=>rules.ArrowSpeed=x,.01,1000)},
                    {"height_advantage",("细格高度",x=>rules.HeightAdvantage=x,.01,100)},
                    {"heal_threshold",("比例",x=>rules.HealThreshold=x,.01,1)},
                    {"weapon_attack_scale",("倍率",x=>rules.WeaponAttackScale=x,0,10)},
                    {"equipment_attack_scale",("倍率",x=>rules.AttackEquipmentScale=x,0,10)},
                    {"equipment_health_scale",("倍率",x=>rules.HealthEquipmentScale=x,0,10)},
                    {"equipment_defense_scale",("倍率",x=>rules.DefenseEquipmentScale=x,0,10)},
                    {"awakened_chance",("概率",x=>rules.AwakenedChance=x,0,1)},
                    {"reserved_jump_skill",("停用",x=>{},0,0)},
                    {"reserved_skill_weight",("停用",x=>{},0,0)},
                    {"enemy_leader_death",("概率",x=>rules.EnemyLeaderDeath=x,0,1)},
                    {"sweep_loss_parity",("比例",x=>rules.SweepLossAtParity=x,0,1)},
                    {"sweep_seconds_parity",("秒",x=>rules.SweepSecondsAtParity=x,.01,90)},
                    {"sweep_min_seconds",("秒",x=>rules.SweepMinimumSeconds=x,.01,90)},
                    {"soldier_death_base",("概率",x=>rules.SoldierDeathBase=x,0,1)},
                    {"soldier_death_sensitivity",("倍率",x=>rules.SoldierDeathSensitivity=x,0,10)}
                };
                seen.Clear();
                for(int row=76;row<=94;row++)
                {
                    string id=Need(config,formula,"A"+row);
                    if(!keys.TryGetValue(id,out var item)||!seen.Add(id))throw new InvalidOperationException(formula+"!A"+row+"：参数ID不存在或重复："+id);
                    if(Need(config,formula,"D"+row)!=item.unit)throw new InvalidOperationException(formula+"!D"+row+"：参数单位应为"+item.unit);
                    item.apply(Number(config,formula,"C"+row,item.min,item.max));
                }
                var hideout = BattleBalanceImporter.Section(archive, formula, 135, 135, "ACD");
                if (hideout.ContainsKey("A135"))
                {
                    if (Need(hideout, formula, "A135") != "bandit_hideout_enemy_count" || Need(hideout, formula, "D135") != "人")
                        throw new InvalidOperationException(formula + "!A135:D135：土匪窝点参数或单位非法");
                    double count = Number(hideout, formula, "C135", 1, Math.Min(BaseBanditHideout.EnemyCountCap, rules.TroopBaseline.Sum()));
                    if (count != Math.Truncate(count)) throw new InvalidOperationException(formula + "!C135：土匪窝点人数须为整数");
                    rules.HideoutEnemyCount = (int)count;
                }
                const string encounterSheet="09c 战斗与编制";
                var ruinsController=BattleBalanceImporter.Section(archive,encounterSheet,17,17,"ACEH");
                if(Need(ruinsController,encounterSheet,"A17")!="encounter_ruins"||Need(ruinsController,encounterSheet,"H17")!="controlled_source")
                    throw new InvalidOperationException(encounterSheet+" 第17行须为古窟首通控制来源");
                var controllerTemplate=Need(ruinsController,encounterSheet,"C17");
                if(!new[]{"captain_normal_saber","captain_awakened_saber"}.Contains(controllerTemplate)||Number(ruinsController,encounterSheet,"E17",1,1)!=1)
                    throw new InvalidOperationException(encounterSheet+"!C17:E17：古窟首通须使用一级剑士队长模板");
                rules.RuinsFirstControllerTemplate=RpgRules.Template(controllerTemplate=="captain_awakened_saber"?"captain_awakened":"captain_normal",0);
                if(rules.BaseInterval<rules.MinimumInterval)throw new InvalidOperationException(formula+"：基础前摇不能低于最小间隔");
                double maxAttackSpeed=rules.Traits.Single(t=>t.Id=="frenzy").Max/100+rules.Skills.Single(s=>s.Id=="rapid_fire").Amount;
                if(rules.BaseInterval/(1+maxAttackSpeed)<rules.MinimumInterval)throw new InvalidOperationException("10c 效果定义与明细 / 11b 特性数值与互斥 / 02a 全局参数与试算：连射与乱武的最大组合使攻击间隔低于下限");
                if(rules.SweepSecondsAtParity<rules.SweepMinimumSeconds)throw new InvalidOperationException(formula+"：扫荡基础时间不能低于最小时间");
                try{rules.Validate();}catch(ArgumentException error){throw new InvalidOperationException(error.Message,error);}return rules;
            }
        }
    }
}

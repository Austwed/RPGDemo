using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using Newtonsoft.Json.Linq;
using static BaseConquest.Editor.PlanningWorkbook;

namespace BaseConquest.Editor
{
    internal static class PlanningWorkbookFile
    {
        private static readonly XNamespace Main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        private static readonly XNamespace Rel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        private static readonly XNamespace Package = "http://schemas.openxmlformats.org/package/2006/relationships";
        private static readonly XNamespace Content = "http://schemas.openxmlformats.org/package/2006/content-types";
        private const string Reference = "字段定义";
        private const string ReferenceTable = "planning_fields";
        private const string Price = "基准购入价（币）";
        private const string PriceMeaning = "一般品质的基准购入价，按槽位基础价值和倍率计算。此列为公式结果。";
        private const string StyleXml = @"<styleSheet xmlns=""http://schemas.openxmlformats.org/spreadsheetml/2006/main"">
<numFmts count=""1""><numFmt numFmtId=""164"" formatCode=""0.00##""/></numFmts>
<fonts count=""3""><font><sz val=""11""/><name val=""等线""/></font><font><b/><sz val=""11""/><name val=""等线""/></font><font><color rgb=""FF626262""/><sz val=""11""/><name val=""等线""/></font></fonts>
<fills count=""3""><fill><patternFill patternType=""none""/></fill><fill><patternFill patternType=""gray125""/></fill><fill><patternFill patternType=""solid""><fgColor rgb=""FFF2F2F2""/><bgColor indexed=""64""/></patternFill></fill></fills>
<borders count=""2""><border><left/><right/><top/><bottom/><diagonal/></border><border><left/><right/><top/><bottom style=""thin""><color rgb=""FFD1D1D1""/></bottom><diagonal/></border></borders>
<cellStyleXfs count=""1""><xf numFmtId=""0"" fontId=""0"" fillId=""0"" borderId=""0""/></cellStyleXfs>
<cellXfs count=""7""><xf numFmtId=""0"" fontId=""0"" fillId=""0"" borderId=""0"" xfId=""0""/><xf numFmtId=""0"" fontId=""1"" fillId=""2"" borderId=""1"" xfId=""0"" applyFont=""1"" applyFill=""1"" applyBorder=""1""/><xf numFmtId=""0"" fontId=""2"" fillId=""0"" borderId=""0"" xfId=""0"" applyFont=""1""/><xf numFmtId=""0"" fontId=""1"" fillId=""0"" borderId=""0"" xfId=""0"" applyFont=""1""/><xf numFmtId=""164"" fontId=""0"" fillId=""0"" borderId=""0"" xfId=""0"" applyNumberFormat=""1""/><xf numFmtId=""10"" fontId=""0"" fillId=""0"" borderId=""0"" xfId=""0"" applyNumberFormat=""1""/><xf numFmtId=""0"" fontId=""0"" fillId=""0"" borderId=""0"" xfId=""0"" applyAlignment=""1""><alignment vertical=""center"" wrapText=""1""/></xf></cellXfs>
<cellStyles count=""1""><cellStyle name=""Normal"" xfId=""0"" builtinId=""0""/></cellStyles>
</styleSheet>";

        private sealed class Sheet
        {
            internal string Name;
            internal readonly SortedDictionary<int, SortedDictionary<int, XElement>> Cells = new SortedDictionary<int, SortedDictionary<int, XElement>>();
            internal readonly Dictionary<int, double> Widths = new Dictionary<int, double>();
            internal readonly HashSet<int> Hidden = new HashSet<int>();
            internal readonly List<(string name, string range, string[] headers)> Tables = new List<(string, string, string[])>();
            internal readonly List<XElement> Links = new List<XElement>();
            internal readonly List<(int row, int col, string text)> Notes = new List<(int, int, string)>();
            internal int Next = 1;
            internal void Add(int row, int col, JToken value, int style = 0, string formula = null)
            {
                if (!Cells.TryGetValue(row, out var line)) Cells[row] = line = new SortedDictionary<int, XElement>();
                line[col] = Cell(Address(row, col), value, style, formula);
            }
            internal void Width(int col, double width)
            {
                if (!Widths.TryGetValue(col, out var previous) || previous < width) Widths[col] = width;
            }
            internal int AddTable(string id, string title, string[] headers, List<JToken[]> rows, int visible, int left = 1)
            {
                if (title != null)
                {
                    Add(Next++, left, new JValue(title), 3);
                }
                int start = Next;
                for (int i = 0; i < headers.Length; i++)
                {
                    Add(start, i + left, new JValue(headers[i]), 1);
                    if (i >= visible) Hidden.Add(i + left);
                    else Width(i + left, Math.Max(i == 0 ? 21 : 11, WidthOf(headers[i]) + 4));
                }
                for (int n = 0; n < rows.Count; n++)
                {
                    var values = rows[n];
                    for (int i = 0; i < values.Length; i++)
                    {
                        int style = 0;
                        if ((id == "cfg_gear" || id == "cfg_try_gear") &&
                            i < visible && headers[i] != "价值阶段" && values[i]?.Type == JTokenType.Float)
                            style = 4;
                        if (id == "cfg_quality" && headers[i] == "掉落概率") style = 5;
                        if (id == ReferenceTable) style = 6;
                        Add(start + n + 1, i + left, values[i], style);
                        if (i < visible && values[i]?.Type == JTokenType.String)
                            Width(i + left, Math.Min(42, WidthOf(values[i].Value<string>()) + 2));
                    }
                }
                int end = start + Math.Max(1, rows.Count);
                Tables.Add((id, Address(start, left) + ":" + Address(end, headers.Length + left - 1), headers));
                Next = end + 3;
                return start;
            }
        }

        private static int WidthOf(string text) => text.Sum(c => c > 255 ? 2 : 1);
        private static string Column(int col)
        {
            string result = "";
            while (col > 0) { col--; result = (char)('A' + col % 26) + result; col /= 26; }
            return result;
        }
        private static string Address(int row, int col) => Column(col) + row;
        private static XElement Cell(string address, JToken value, int style = 0, string formula = null)
        {
            var cell = new XElement(Main + "c", new XAttribute("r", address));
            if (style != 0) cell.Add(new XAttribute("s", style));
            if (formula != null) cell.Add(new XElement(Main + "f", formula));
            if (value == null || value.Type == JTokenType.Null) return cell;
            switch (value.Type)
            {
                case JTokenType.String:
                    cell.Add(new XAttribute("t", "inlineStr"),
                        new XElement(Main + "is", new XElement(Main + "t", new XAttribute(XNamespace.Xml + "space", "preserve"), value.Value<string>())));
                    break;
                case JTokenType.Boolean:
                    cell.Add(new XAttribute("t", "b"), new XElement(Main + "v", value.Value<bool>() ? "1" : "0"));
                    break;
                default:
                    cell.Add(new XElement(Main + "v", Convert.ToString(((JValue)value).Value, CultureInfo.InvariantCulture)));
                    break;
            }
            return cell;
        }

        internal static byte[] Create(Schema schema, Grid grid, bool trial)
        {
            var names = trial ? schema.QuickSheets : new[] { "目录" }.Concat(schema.Group.SelectMany(g => g.Sheets)).Append(Reference).ToArray();
            var sheets = names.ToDictionary(n => n, n => new Sheet { Name = n });
            var prices = new List<(Sheet sheet, int row, int column, int stageColumn, string slot, JToken stage)>();
            if (trial)
            {
                var quick = Quick(schema, grid);
                var pageWidths = quick.GroupBy(t => t.Sheet).ToDictionary(g => g.Key, g => g.Max(t => 1 + t.Fields.Length + (t.Id == "cfg_try_gear" ? 1 : 0)));
                foreach (var table in quick)
                {
                    var sheet = sheets[table.Sheet];
                    bool gear = table.Id == "cfg_try_gear";
                    var headers = new[] { "名称" }.Concat(table.Fields.Select(f => f.Label))
                        .Concat(gear ? new[] { Price } : Array.Empty<string>()).Concat(new[] { IdColumn, LinkColumn })
                        .Concat(table.Fields.Select(f => BaselineColumn(f.Label))).ToArray();
                    var rows = table.Rows.Select(r => new JToken[] { new JValue(r.Label) }
                        .Concat(r.Cells.Select(c => c.Before)).Concat(gear ? new[] { JValue.CreateNull() } : Array.Empty<JValue>())
                        .Concat(new[] { new JValue(r.Key), new JValue(r.Signature) }).Concat(r.Cells.Select(c => c.Before)).ToArray()).ToList();
                    int visible = 1 + table.Fields.Length + (gear ? 1 : 0);
                    int left = 1 + pageWidths[table.Sheet] - visible;
                    int start = sheet.AddTable(table.Id, table.Caption, headers, rows, visible, left);
                    for (int i = 0; i < visible; i++)
                    {
                        var note = TrialNote(table.Id, headers[i]);
                        if (note != null) sheet.Notes.Add((start, left + i, note));
                    }
                    if (gear) for (int i = 0; i < table.Rows.Count; i++)
                    {
                        var row = table.Rows[i];
                        if (schema.GearSlots.TryGetValue(row.Key, out var slot))
                            prices.Add((sheet, start + 1 + i, 8, 7, slot, row.Cells[5].Before));
                    }
                }
            }
            else
            {
                var toc = sheets["目录"];
                toc.Width(1, 24); toc.Width(2, 24);
                toc.Add(1, 1, new JValue("系统数值配置"), 3);
                int groupRow = 3;
                foreach (var group in schema.Group)
                {
                    toc.Add(groupRow, 1, new JValue(group.Name), 3);
                    foreach (string name in group.Sheets)
                    {
                        toc.Add(groupRow, 2, new JValue(name), 2);
                        toc.Links.Add(new XElement(Main + "hyperlink", new XAttribute("ref", "B" + groupRow++),
                            new XAttribute("location", "'" + name.Replace("'", "''") + "'!A1")));
                    }
                }
                toc.Add(groupRow, 2, new JValue(Reference), 2);
                toc.Links.Add(new XElement(Main + "hyperlink", new XAttribute("ref", "B" + groupRow),
                    new XAttribute("location", "'" + Reference + "'!A1")));
                AddReference(sheets[Reference], schema);
                foreach (var table in schema.Tables)
                {
                    var sheet = sheets[table.Sheet];
                    bool label = table.Fields[0].Id != "Name";
                    bool gear = table.Id == "cfg_gear";
                    bool idFirst = table.IsCollection || table.Id == "cfg_profession_stats" || table.Id == "cfg_gear_value" || table.Id == "cfg_quality";
                    var headers = (label ? new[] { "名称" } : Array.Empty<string>()).Concat(table.Fields.Select(f => f.Label))
                        .Concat(gear ? new[] { Price } : Array.Empty<string>())
                        .Concat(idFirst ? Array.Empty<string>() : new[] { IdColumn })
                        .Concat(table.IsCollection ? new[] { OrderColumn } : Array.Empty<string>()).ToArray();
                    if (idFirst) headers = headers.Prepend(IdColumn).ToArray();
                    var records = grid.Tables[table.Id];
                    var rows = records.Select(r => (idFirst ? new JToken[] { new JValue(r.Id) } : Array.Empty<JToken>())
                        .Concat(label ? new JToken[] { new JValue(r.Label) } : Array.Empty<JToken>())
                        .Concat(table.Fields.Select(f => r.Values[f.Id])).Concat(gear ? new[] { JValue.CreateNull() } : Array.Empty<JValue>())
                        .Concat(idFirst ? Array.Empty<JValue>() : new[] { new JValue(r.Id) })
                        .Concat(table.IsCollection ? new[] { new JValue(r.Order) } : Array.Empty<JValue>()).ToArray()).ToList();
                    int start = sheet.AddTable(table.Id, null, headers, rows, headers.Length - (table.IsCollection ? 1 : 0));
                    if (gear) for (int i = 0; i < records.Count; i++)
                        if (schema.GearSlots.TryGetValue(records[i].Id, out var slot))
                            prices.Add((sheet, start + 1 + i, Array.IndexOf(headers, Price) + 1,
                                Array.IndexOf(headers, table.Get("Stage").Label) + 1, slot, records[i].Values["Stage"]));
                }
            }
            foreach (var sheet in sheets.Values)
                sheet.Hidden.ExceptWith(sheet.Widths.Keys);
            foreach (var price in prices)
            {
                string slot = schema.Find("cfg_gear_value").Get(price.slot).Label;
                double basis = grid.Find("cfg_gear_value", price.stage.ToString()).Values[price.slot].Value<double>();
                double quality = grid.Find("cfg_quality", "normal").Values["value"].Value<double>();
                double buy = grid.Find("cfg_trade", "BuyFactor").Values["value"].Value<double>();
                string value = "INDEX(cfg_" + (trial ? "try_value" : "gear_value") + "[" + slot + "],MATCH(" +
                    Address(price.row, price.stageColumn) + "&\"\",cfg_" + (trial ? "try_value" : "gear_value") + "[" + IdColumn + "],0))";
                string factor = trial ? Number(quality) + "*" + Number(buy) :
                    "INDEX(cfg_quality[价值倍率],MATCH(\"normal\",cfg_quality[" + IdColumn + "],0))*INDEX(cfg_trade[数值],MATCH(\"BuyFactor\",cfg_trade[" + IdColumn + "],0))";
                price.sheet.Add(price.row, price.column, new JValue(Math.Max(1, Math.Ceiling(basis * quality * buy))), 2,
                    "MAX(1,ROUNDUP(" + value + "*" + factor + ",0))");
            }
            var first = sheets[names[0]];
            first.Add(1, 24, new JValue("工作簿类型")); first.Add(1, 25, new JValue("结构版本"));
            first.Add(2, 24, new JValue(trial ? TrialKind : MasterKind)); first.Add(2, 25, new JValue(schema.Version));
            first.Hidden.UnionWith(new[] { 24, 25 });
            return PackageWorkbook(names.Select(n => sheets[n]).ToArray());
        }

        private static string TrialNote(string table, string heading)
        {
            switch (table, heading)
            {
                case ("cfg_try_units", "职业普攻距离（细格）"): return "该职业各身份共用。可填小数，0 < 值 ≤ 24；弓兵叠加品质射程后也不得超过 24。";
                case ("cfg_try_gear", "价值阶段"): return "填整数 0～4，和装备槽位一起决定该装备所用基础价值。";
                case ("cfg_try_gear", Price): return PriceMeaning;
                case ("cfg_try_value", "名称"): return "同一槽位、同一阶段的装备共用基础价值。各价格列填整数钱币。";
                case ("cfg_try_facility", "每人日产量"): return "可填小数，0 < 值 ≤ 100。粮田以粮计，工坊以工作点计。";
                case ("cfg_try_material", "每份一级军需原料（份）"): return "一级军需的单份原料消耗，填正整数，也参与军需计价。";
                case ("cfg_try_exact", "军需档"): return "此表按指定部署槽位分配，每槽一人。填 0 无军需、1 一级、2 二级。";
                case ("cfg_try_group", "军需档"): return "按该行人数分配。填 0 无军需、1 一级、2 二级。";
                case ("cfg_try_group", "配装人数"): return "填整数 1～999。先发指定槽位，再从本职业或全军余下单位中分配。";
                default: return null;
            }
        }

        private static string DisplayType(string type)
        {
            string label = type.TrimEnd('?') switch
            {
                "s" => "string", "b" => "bool", "i" => "int", "f" => "float", "d" => "double",
                _ => throw new InvalidDataException("未知配置字段类型 " + type),
            };
            return type.EndsWith("?", StringComparison.Ordinal) ? label + "（可空）" : label;
        }

        private static void AddReference(Sheet sheet, Schema schema)
        {
            var rows = new List<JToken[]>();
            foreach (var table in schema.Tables)
            {
                void Add(string label, string key, string type, string meaning)
                {
                    if (string.IsNullOrWhiteSpace(meaning)) throw new InvalidDataException("缺少字段说明 " + table.Id + "/" + key);
                    rows.Add(new[] { table.Sheet, table.Id, label, key, type, meaning }.Select(text => (JToken)new JValue(text)).ToArray());
                }
                Add(IdColumn, table.Key ?? "", "string", table.IsCollection
                    ? "配置区内唯一。关联或回写使用该 ID 定位同一条记录。"
                    : "配置区内唯一的参数或行标识，按既有 ID 读取。");
                for (int i = 0; i < table.Fields.Length; i++)
                {
                    var field = table.Fields[i];
                    if (table.Rows != null && table.Fields.Length == 1 && field.Id == "value")
                    {
                        foreach (var row in table.Rows)
                            Add(row.Label, row.Paths[i]?.Replace("/", ".") ?? row.Id, DisplayType(RowType(row, field, i)), row.Description);
                    }
                    else Add(field.Label, field.Id, DisplayType(field.Type), field.Description);
                }
                if (table.IsCollection) Add(OrderColumn, "", "int（可空）", "同一区的运行顺序。新增行可留空，排在有明确顺序的记录之后。");
                if (table.Id == "cfg_gear") Add(Price, "", "公式", PriceMeaning);
            }
            sheet.AddTable(ReferenceTable, null, new[] { "页签", "配置区", "字段", "字段键", "值类型", "含义与填写规则" }, rows, 6);
            sheet.Width(5, 18);
            sheet.Width(6, 63);
        }

        private static string Number(double value) => value.ToString("R", CultureInfo.InvariantCulture);
        private static XElement Relation(string id, string type, string target) =>
            new XElement(Package + "Relationship", new XAttribute("Id", id), new XAttribute("Type", Rel.NamespaceName + "/" + type), new XAttribute("Target", target));
        private static void Xml(ZipArchive zip, string path, XElement data)
        {
            using var stream = zip.CreateEntry(path, CompressionLevel.Optimal).Open();
            data.Save(stream, SaveOptions.DisableFormatting);
        }

        private static void WriteNotes(ZipArchive zip, Sheet sheet, int number, XElement xml, XElement links, XElement redirects)
        {
            string notes = "comments" + number + ".xml";
            string drawing = "notes" + number + ".vml";
            Xml(zip, "xl/" + notes, new XElement(Main + "comments",
                new XElement(Main + "authors", new XElement(Main + "author", "配置说明")),
                new XElement(Main + "commentList", sheet.Notes.Select(note => new XElement(Main + "comment",
                    new XAttribute("ref", Address(note.row, note.col)), new XAttribute("authorId", 0),
                    new XElement(Main + "text", new XElement(Main + "t", note.text)))))));
            redirects.Add(new XElement(Content + "Override", new XAttribute("PartName", "/xl/" + notes),
                new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.comments+xml")));
            links.Add(Relation("rIdNotes", "comments", "../" + notes),
                Relation("rIdNoteDrawing", "vmlDrawing", "../drawings/" + drawing));
            xml.Add(new XElement(Main + "legacyDrawing", new XAttribute(Rel + "id", "rIdNoteDrawing")));

            XNamespace vml = "urn:schemas-microsoft-com:vml";
            XNamespace office = "urn:schemas-microsoft-com:office:office";
            XNamespace excel = "urn:schemas-microsoft-com:office:excel";
            var shapes = new XElement("xml", new XAttribute(XNamespace.Xmlns + "v", vml),
                new XAttribute(XNamespace.Xmlns + "o", office), new XAttribute(XNamespace.Xmlns + "x", excel),
                new XElement(office + "shapelayout", new XAttribute(vml + "ext", "edit"),
                    new XElement(office + "idmap", new XAttribute(vml + "ext", "edit"), new XAttribute("data", "1"))),
                new XElement(vml + "shapetype", new XAttribute("id", "_x0000_t202"), new XAttribute("coordsize", "21600,21600"),
                    new XAttribute(office + "spt", "202"), new XAttribute("path", "m,l,21600r21600,l21600,xe"),
                    new XElement(vml + "stroke", new XAttribute("joinstyle", "miter")),
                    new XElement(vml + "path", new XAttribute("gradientshapeok", "t"), new XAttribute(office + "connecttype", "rect"))));
            int shapeId = 1024;
            foreach (var note in sheet.Notes)
            {
                var anchor = string.Join(", ", Math.Max(0, note.col - 3), 12, note.row, 4,
                    Math.Max(1, note.col - 1), 32, note.row + 4, 4);
                shapes.Add(new XElement(vml + "shape", new XAttribute("id", "_x0000_s" + ++shapeId), new XAttribute("type", "#_x0000_t202"),
                    new XAttribute("style", "position:absolute;width:195pt;height:63pt;z-index:1;visibility:hidden"),
                    new XAttribute("fillcolor", "#ffffe1"), new XAttribute(office + "insetmode", "auto"),
                    new XElement(vml + "fill", new XAttribute("color2", "#ffffe1")),
                    new XElement(vml + "textbox", new XElement("div", new XAttribute("style", "text-align:left"))),
                    new XElement(excel + "ClientData", new XAttribute("ObjectType", "Note"),
                        new XElement(excel + "MoveWithCells"), new XElement(excel + "Anchor", anchor),
                        new XElement(excel + "AutoFill", "False"), new XElement(excel + "Row", note.row - 1), new XElement(excel + "Column", note.col - 1))));
            }
            Xml(zip, "xl/drawings/" + drawing, shapes);
        }

        private static byte[] PackageWorkbook(Sheet[] sheets)
        {
            using var output = new MemoryStream();
            using (var zip = new ZipArchive(output, ZipArchiveMode.Create, true))
            {
                var redirects = new XElement(Content + "Types",
                    new XElement(Content + "Default", new XAttribute("Extension", "rels"), new XAttribute("ContentType", "application/vnd.openxmlformats-package.relationships+xml")),
                    new XElement(Content + "Default", new XAttribute("Extension", "xml"), new XAttribute("ContentType", "application/xml")));
                if (sheets.Any(s => s.Notes.Count > 0))
                    redirects.Add(new XElement(Content + "Default", new XAttribute("Extension", "vml"),
                        new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.vmlDrawing")));
                void Register(string path, string kind, XElement xml)
                {
                    Xml(zip, path, xml);
                    redirects.Add(new XElement(Content + "Override", new XAttribute("PartName", "/" + path),
                        new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml." + kind + "+xml")));
                }
                Xml(zip, "_rels/.rels", new XElement(Package + "Relationships", Relation("rId1", "officeDocument", "xl/workbook.xml")));
                var tabs = new XElement(Main + "sheets");
                var bookRels = new XElement(Package + "Relationships", Relation("rIdStyles", "styles", "styles.xml"));
                int tableId = 0;
                for (int n = 0; n < sheets.Length; n++)
                {
                    var sheet = sheets[n];
                    string file = "sheet" + (n + 1) + ".xml", id = "rId" + (n + 1);
                    tabs.Add(new XElement(Main + "sheet", new XAttribute("name", sheet.Name), new XAttribute("sheetId", n + 1), new XAttribute(Rel + "id", id)));
                    bookRels.Add(Relation(id, "worksheet", "worksheets/" + file));
                    var view = new XElement(Main + "sheetView", new XAttribute("workbookViewId", "0"), new XAttribute("zoomScale", "100"));
                    if (n == 0) view.Add(new XAttribute("tabSelected", "1"));
                    if (sheet.Tables.Count != 0)
                    {
                        view.Add(new XElement(Main + "pane", new XAttribute("xSplit", 1), new XAttribute("ySplit", 1),
                            new XAttribute("topLeftCell", "B2"), new XAttribute("activePane", "bottomRight"), new XAttribute("state", "frozen")));
                        view.Add(new XElement(Main + "selection", new XAttribute("pane", "topRight"), new XAttribute("activeCell", "B1"), new XAttribute("sqref", "B1")),
                            new XElement(Main + "selection", new XAttribute("pane", "bottomLeft"), new XAttribute("activeCell", "A2"), new XAttribute("sqref", "A2")),
                            new XElement(Main + "selection", new XAttribute("pane", "bottomRight"), new XAttribute("activeCell", "A1"), new XAttribute("sqref", "A1")));
                    }
                    else view.Add(new XElement(Main + "selection", new XAttribute("activeCell", "A1"), new XAttribute("sqref", "A1")));
                    var xml = new XElement(Main + "worksheet", new XAttribute(XNamespace.Xmlns + "r", Rel),
                        new XElement(Main + "sheetViews", view),
                        new XElement(Main + "sheetFormatPr", new XAttribute("defaultRowHeight", 16), new XAttribute("outlineLevelCol", 1)),
                        new XElement(Main + "cols", sheet.Widths.Keys.Concat(sheet.Hidden).Distinct().OrderBy(x => x).Select(c =>
                            new XElement(Main + "col", new XAttribute("min", c), new XAttribute("max", c),
                                new XAttribute("width", sheet.Widths.TryGetValue(c, out var w) ? w : 20), new XAttribute("customWidth", 1),
                                sheet.Hidden.Contains(c) ? new[] { new XAttribute("hidden", 1), new XAttribute("outlineLevel", 1) } : null))),
                        new XElement(Main + "sheetData", sheet.Cells.Select(line =>
                            new XElement(Main + "row", new XAttribute("r", line.Key),
                                sheet.Name == Reference && line.Key > 1
                                    ? new[] { new XAttribute("ht", 32), new XAttribute("customHeight", 1) } : null, line.Value.Values))));
                    if (sheet.Links.Count > 0) xml.Add(new XElement(Main + "hyperlinks", sheet.Links));
                    var links = new XElement(Package + "Relationships");
                    if (sheet.Notes.Count > 0) WriteNotes(zip, sheet, n + 1, xml, links, redirects);
                    if (sheet.Tables.Count > 0)
                    {
                        var parts = new XElement(Main + "tableParts", new XAttribute("count", sheet.Tables.Count));
                        foreach (var table in sheet.Tables)
                        {
                            tableId++;
                            string tablePath = "tables/table" + tableId + ".xml", tableRel = "rId" + tableId;
                            Register("xl/" + tablePath, "table", new XElement(Main + "table",
                                new XAttribute("id", tableId), new XAttribute("name", table.name), new XAttribute("displayName", table.name),
                                new XAttribute("ref", table.range), new XAttribute("totalsRowShown", 0),
                                new XElement(Main + "autoFilter", new XAttribute("ref", table.range)),
                                new XElement(Main + "tableColumns", new XAttribute("count", table.headers.Length),
                                    table.headers.Select((header, i) => new XElement(Main + "tableColumn", new XAttribute("id", i + 1), new XAttribute("name", header)))),
                                new XElement(Main + "tableStyleInfo", new XAttribute("showFirstColumn", 0), new XAttribute("showLastColumn", 0),
                                    new XAttribute("showRowStripes", 0), new XAttribute("showColumnStripes", 0))));
                            parts.Add(new XElement(Main + "tablePart", new XAttribute(Rel + "id", tableRel)));
                            links.Add(Relation(tableRel, "table", "../" + tablePath));
                        }
                        xml.Add(parts);
                    }
                    if (links.HasElements) Xml(zip, "xl/worksheets/_rels/" + file + ".rels", links);
                    Register("xl/worksheets/" + file, "worksheet", xml);
                }
                Register("xl/workbook.xml", "sheet.main", new XElement(Main + "workbook", new XAttribute(XNamespace.Xmlns + "r", Rel),
                    new XElement(Main + "bookViews", new XElement(Main + "workbookView", new XAttribute("activeTab", 0))),
                    tabs, new XElement(Main + "definedNames", new XElement(Main + "definedName", new XAttribute("name", MetaTable),
                        "'" + sheets[0].Name + "'!$X$1:$Y$2")), new XElement(Main + "calcPr", new XAttribute("fullCalcOnLoad", 1))));
                Register("xl/styles.xml", "styles", XElement.Parse(StyleXml));
                Xml(zip, "xl/_rels/workbook.xml.rels", bookRels);
                Xml(zip, "[Content_Types].xml", redirects);
            }
            return output.ToArray();
        }

        internal static byte[] Patch(byte[] original, IEnumerable<(ProfessionWorkbook.Row row, string label, JToken value)> changes)
        {
            var byPart = changes.GroupBy(e => e.row.Part).ToDictionary(g => g.Key);
            using var input = Open(original);
            using var output = new MemoryStream();
            using (var zip = new ZipArchive(output, ZipArchiveMode.Create, true))
            {
                foreach (var entry in input.Entries)
                {
                    if (!byPart.TryGetValue(entry.FullName, out var entries))
                    {
                        using var source = entry.Open();
                        using var destination = zip.CreateEntry(entry.FullName, CompressionLevel.Optimal).Open();
                        source.CopyTo(destination);
                        continue;
                    }
                    var xml = ProfessionWorkbook.Xml(input, entry.FullName);
                    foreach (var change in entries)
                    {
                        string address = change.row.Addresses[change.label];
                        var cell = xml.Descendants(Main + "c").Single(c => (string)c.Attribute("r") == address);
                        int style = (int?)cell.Attribute("s") ?? 0;
                        cell.ReplaceWith(Cell(address, change.value, style));
                    }
                    Xml(zip, entry.FullName, xml.Root);
                }
            }
            return output.ToArray();
        }
    }
}

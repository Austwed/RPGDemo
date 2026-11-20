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
        private const string Price = "基准购入价（币）";
        private const string StyleXml = @"<styleSheet xmlns=""http://schemas.openxmlformats.org/spreadsheetml/2006/main"">
<fonts count=""3""><font><sz val=""11""/><name val=""等线""/></font><font><b/><sz val=""11""/><name val=""等线""/></font><font><color rgb=""FF626262""/><sz val=""11""/><name val=""等线""/></font></fonts>
<fills count=""3""><fill><patternFill patternType=""none""/></fill><fill><patternFill patternType=""gray125""/></fill><fill><patternFill patternType=""solid""><fgColor rgb=""FFF2F2F2""/><bgColor indexed=""64""/></patternFill></fill></fills>
<borders count=""2""><border><left/><right/><top/><bottom/><diagonal/></border><border><left/><right/><top/><bottom style=""thin""><color rgb=""FFD1D1D1""/></bottom><diagonal/></border></borders>
<cellStyleXfs count=""1""><xf numFmtId=""0"" fontId=""0"" fillId=""0"" borderId=""0""/></cellStyleXfs>
<cellXfs count=""4""><xf numFmtId=""0"" fontId=""0"" fillId=""0"" borderId=""0"" xfId=""0""/><xf numFmtId=""0"" fontId=""1"" fillId=""2"" borderId=""1"" xfId=""0"" applyFont=""1"" applyFill=""1"" applyBorder=""1""/><xf numFmtId=""0"" fontId=""2"" fillId=""0"" borderId=""0"" xfId=""0"" applyFont=""1""/><xf numFmtId=""0"" fontId=""1"" fillId=""0"" borderId=""0"" xfId=""0"" applyFont=""1""/></cellXfs>
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
                        Add(start + n + 1, i + left, values[i]);
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
            var names = trial ? schema.QuickSheets : new[] { "目录" }.Concat(schema.Group.SelectMany(g => g.Sheets)).ToArray();
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
                    int start = sheet.AddTable(table.Id, table.Caption, headers, rows, visible, 1 + pageWidths[table.Sheet] - visible);
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
                foreach (var table in schema.Tables)
                {
                    var sheet = sheets[table.Sheet];
                    bool label = table.Fields[0].Id != "Name";
                    bool gear = table.Id == "cfg_gear";
                    var headers = (label ? new[] { "名称" } : Array.Empty<string>()).Concat(table.Fields.Select(f => f.Label))
                        .Concat(gear ? new[] { Price } : Array.Empty<string>()).Concat(new[] { IdColumn })
                        .Concat(table.IsCollection ? new[] { OrderColumn } : Array.Empty<string>()).ToArray();
                    var records = grid.Tables[table.Id];
                    var rows = records.Select(r => (label ? new JToken[] { new JValue(r.Label) } : Array.Empty<JToken>())
                        .Concat(table.Fields.Select(f => r.Values[f.Id])).Concat(gear ? new[] { JValue.CreateNull() } : Array.Empty<JValue>())
                        .Concat(new[] { new JValue(r.Id) }).Concat(table.IsCollection ? new[] { new JValue(r.Order) } : Array.Empty<JValue>()).ToArray()).ToList();
                    int visible = table.Fields.Length + (label ? 1 : 0) + (gear ? 1 : 0);
                    int start = sheet.AddTable(table.Id, null, headers, rows, visible + 1);
                    if (gear) for (int i = 0; i < records.Count; i++)
                        if (schema.GearSlots.TryGetValue(records[i].Id, out var slot))
                            prices.Add((sheet, start + 1 + i, visible, visible - 1, slot, records[i].Values["Stage"]));
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

        private static string Number(double value) => value.ToString("R", CultureInfo.InvariantCulture);
        private static XElement Relation(string id, string type, string target) =>
            new XElement(Package + "Relationship", new XAttribute("Id", id), new XAttribute("Type", Rel.NamespaceName + "/" + type), new XAttribute("Target", target));
        private static void Xml(ZipArchive zip, string path, XElement data)
        {
            using var stream = zip.CreateEntry(path, CompressionLevel.Optimal).Open();
            data.Save(stream, SaveOptions.DisableFormatting);
        }
        private static byte[] PackageWorkbook(Sheet[] sheets)
        {
            using var output = new MemoryStream();
            using (var zip = new ZipArchive(output, ZipArchiveMode.Create, true))
            {
                var redirects = new XElement(Content + "Types",
                    new XElement(Content + "Default", new XAttribute("Extension", "rels"), new XAttribute("ContentType", "application/vnd.openxmlformats-package.relationships+xml")),
                    new XElement(Content + "Default", new XAttribute("Extension", "xml"), new XAttribute("ContentType", "application/xml")));
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
                            new XElement(Main + "row", new XAttribute("r", line.Key), line.Value.Values))));
                    if (sheet.Links.Count > 0) xml.Add(new XElement(Main + "hyperlinks", sheet.Links));
                    if (sheet.Tables.Count > 0)
                    {
                        var parts = new XElement(Main + "tableParts", new XAttribute("count", sheet.Tables.Count));
                        var links = new XElement(Package + "Relationships");
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
                        Xml(zip, "xl/worksheets/_rels/" + file + ".rels", links);
                    }
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

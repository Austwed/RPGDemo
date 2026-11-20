using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace BaseConquest.Editor
{
    // Read Excel tables by name (rather than by sheet position), including files saved by Excel/WPS/LibreOffice.
    internal sealed class ProfessionWorkbook
    {
        internal sealed class Row
        {
            internal string Location;
            internal Dictionary<string,string> Values=new Dictionary<string,string>(StringComparer.Ordinal);
            internal Dictionary<string,string> Types=new Dictionary<string,string>(StringComparer.Ordinal);
            internal InvalidDataException Error(string field,string problem)=>new InvalidDataException(Location+" · "+field+"："+problem);
            internal string Get(string field)
            {
                if(!Values.TryGetValue(field,out var value)||string.IsNullOrWhiteSpace(value))throw Error(field,"必填字段为空");
                return value;
            }
            internal string TextField(string field)
            {
                string value=Get(field);
                if(Types[field]!="s"&&Types[field]!="str"&&Types[field]!="inlineStr")throw Error(field,"须为文本");
                return value;
            }
            internal string Key(string field)
            {
                var value=TextField(field);
                if(!Regex.IsMatch(value,@"^[a-z][a-z0-9_]*$"))throw Error(field,"ID 格式非法："+value);
                return value;
            }
            internal double Number(string field,double min,double max)
            {
                if(!Types.TryGetValue(field,out var kind)||kind!="n"||!double.TryParse(Get(field),NumberStyles.Float,CultureInfo.InvariantCulture,out var number)||
                    double.IsNaN(number)||double.IsInfinity(number)||number<min||number>max)
                    throw Error(field,"须为 "+min+"～"+max+" 范围内的数值");
                return number;
            }
            internal int Int(string field,int min,int max)
            {
                double number=Number(field,min,max);
                if(number!=Math.Truncate(number))throw Error(field,"须为整数");
                return (int)number;
            }
            internal bool Boolean(string field)
            {
                string v=Get(field);
                if(Types[field]=="b"&&(v=="0"||v=="1"))return v=="1";
                throw Error(field,"须使用 Excel 布尔值 TRUE 或 FALSE");
            }
            internal string OneOf(string field,params string[] choices)
            {
                var value=TextField(field);
                if(!choices.Contains(value))throw Error(field,"无效引用 "+value+"；可用 "+string.Join(" / ",choices));
                return value;
            }
        }
        private static readonly XNamespace Main="http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        private static readonly XNamespace Rel="http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        private readonly Dictionary<string,(XDocument sheet,string title,string range)> regions=
            new Dictionary<string,(XDocument,string,string)>(StringComparer.Ordinal);
        private readonly string[] shared;
        internal static XDocument Xml(ZipArchive zip,string path)
        {
            var entry=zip.GetEntry(path)??throw new InvalidDataException("XLSX 缺少 "+path);
            using(var file=entry.Open())return XDocument.Load(file);
        }
        private static string Resolve(string part,string target)
        {
            var absolute=new Uri(new Uri("http://xlsx/"+part),target).AbsolutePath;
            return Uri.UnescapeDataString(absolute.TrimStart('/'));
        }
        internal static Dictionary<string,string> Relations(ZipArchive zip,string path)
        {
            string directory=path.Substring(0,path.LastIndexOf('/')+1);
            string rel=directory+"_rels/"+path.Substring(directory.Length)+".rels";
            if(zip.GetEntry(rel)==null)return new Dictionary<string,string>();
            return Xml(zip,rel).Root.Elements().Where(e=>(string)e.Attribute("TargetMode")!="External")
                .ToDictionary(e=>(string)e.Attribute("Id"),e=>Resolve(path,(string)e.Attribute("Target")));
        }
        internal ProfessionWorkbook(ZipArchive zip)
        {
            if(zip.Entries.Count>1024||zip.Entries.Sum(e=>e.Length)>64L*1024*1024)
                throw new InvalidDataException("XLSX解压体积或条目数越界");
            shared=zip.GetEntry("xl/sharedStrings.xml")==null?Array.Empty<string>():Xml(zip,"xl/sharedStrings.xml")
                .Descendants(Main+"si").Select(si=>string.Concat(si.Descendants(Main+"t").Select(t=>t.Value))).ToArray();
            var book=Xml(zip,"xl/workbook.xml");
            var links=Relations(zip,"xl/workbook.xml");
            var sheets=new Dictionary<string,XDocument>(StringComparer.Ordinal);
            foreach(var tab in book.Descendants(Main+"sheet"))
            {
                string title=(string)tab.Attribute("name"),part=links[(string)tab.Attribute(Rel+"id")];
                var sheet=Xml(zip,part);
                sheets.Add(title,sheet);
                var tables=Relations(zip,part);
                foreach(var tag in sheet.Descendants(Main+"tablePart"))
                {
                    var table=Xml(zip,tables[(string)tag.Attribute(Rel+"id")]).Root;
                    string name=(string)table.Attribute("displayName")??(string)table.Attribute("name");
                    if(name.StartsWith("cfg_",StringComparison.Ordinal))
                        Add(name,sheet,title,(string)table.Attribute("ref"));
                }
            }
            foreach(var def in book.Descendants(Main+"definedName"))
            {
                var name=(string)def.Attribute("name");
                if(!name.StartsWith("cfg_",StringComparison.Ordinal)||regions.ContainsKey(name))continue;
                int split=def.Value.LastIndexOf('!');
                if(split<0)throw new InvalidDataException(name+"：须引用连续的数据区");
                var title=def.Value.Substring(0,split).Trim('\'').Replace("''","'");
                if(!sheets.TryGetValue(title,out var sheet))throw new InvalidDataException(name+"：工作表不存在");
                Add(name,sheet,title,def.Value.Substring(split+1));
            }
        }
        private void Add(string name,XDocument sheet,string title,string range)
        {
            if(regions.ContainsKey(name))throw new InvalidDataException(name+"：区域重名");
            regions.Add(name,(sheet,title,range.Replace("$","")));
        }
        private string Text(XElement c)
        {
            string type=(string)c.Attribute("t"),value=(string)c.Element(Main+"v")??"";
            if(type=="s")return shared[int.Parse(value,CultureInfo.InvariantCulture)].Trim();
            if(type=="inlineStr")return string.Concat(c.Descendants(Main+"t").Select(t=>t.Value)).Trim();
            return value.Trim();
        }
        private static int Column(string address)
        {
            int index=0;
            foreach(char ch in address.TakeWhile(c=>c>='A'&&c<='Z'))index=checked(index*26+ch-'A'+1);
            return index;
        }
        internal List<Row> Rows(string name,string fields)
        {
            if(!regions.TryGetValue(name,out var region))throw new InvalidDataException("缺少配置区 "+name);
            var bounds=Regex.Match(region.range,@"^([A-Z]+)([1-9][0-9]*):([A-Z]+)([1-9][0-9]*)$");
            if(!bounds.Success)throw new InvalidDataException(name+"：配置区须为单个矩形");
            int left=Column(bounds.Groups[1].Value),right=Column(bounds.Groups[3].Value);
            int top=int.Parse(bounds.Groups[2].Value),bottom=int.Parse(bounds.Groups[4].Value);
            var rows=region.sheet.Descendants(Main+"row").ToDictionary(r=>(int)r.Attribute("r"));
            var headers=new Dictionary<int,string>();
            if(!rows.TryGetValue(top,out var header))throw new InvalidDataException(name+"：缺少表头");
            foreach(var cell in header.Elements(Main+"c"))
            {
                int col=Column((string)cell.Attribute("r"));if(col<left||col>right)continue;
                var key=Text(cell);if(key.Length==0)continue;
                if(headers.ContainsValue(key))throw new InvalidDataException(name+" · "+key+"：列名重复");
                headers[col]=key;
            }
            foreach(var f in fields.Split(' '))if(!headers.ContainsValue(f))throw new InvalidDataException(name+" · "+f+"：缺少字段列");
            var result=new List<Row>();
            foreach(var entry in rows.Where(pair=>pair.Key>top&&pair.Key<=bottom).OrderBy(pair=>pair.Key))
            {
                int n=entry.Key;
                var cells=entry.Value;
                var row=new Row{Location=name+"（"+region.title+" 第"+n+"行）"};
                foreach(var c in cells.Elements(Main+"c"))
                {
                    if(!headers.TryGetValue(Column((string)c.Attribute("r")),out var key))continue;
                    if(c.Element(Main+"f")!=null)throw row.Error(key,"配置输入须为直接值");
                    string val=Text(c);if(val.Length==0)continue;
                    row.Values[key]=val;row.Types[key]=(string)c.Attribute("t")??"n";
                }
                if(row.Values.Count>0)result.Add(row);
            }
            return result;
        }
    }
}

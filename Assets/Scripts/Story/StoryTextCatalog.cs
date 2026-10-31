using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace BaseConquest.Story
{
    [Serializable] public sealed class StoryTextEntry
    {
        public string key="",group="",text="",notes="";
        public StoryTextEntry Copy()=>(StoryTextEntry)MemberwiseClone();
    }
    [Serializable] public sealed class StoryTextDocument
    {
        public int version=1;
        public List<StoryTextEntry> entries=new List<StoryTextEntry>();
        public StoryTextDocument Copy()=>new StoryTextDocument {version=version,entries=entries.Select(e=>e.Copy()).ToList()};
        public void Validate()
        {
            if(version!=1||entries==null||entries.Count==0||entries.Count>10000)throw new ArgumentException("文本目录版本或条数非法");
            if(entries.Any(e=>e==null||!Regex.IsMatch(e.key??"",@"^story\.[a-z0-9_.-]+$")||e.key.Length>160||string.IsNullOrWhiteSpace(e.group)||e.group.Length>100||string.IsNullOrWhiteSpace(e.text)||e.text.Length>20000||e.notes==null||e.notes.Length>2000))throw new ArgumentException("文本键、分组、正文或备注非法");
            if(entries.Select(e=>e.key).Distinct(StringComparer.Ordinal).Count()!=entries.Count)throw new ArgumentException("文本键重复");
        }
    }
    // Only presentation text lives here. No BaseState reference, transaction or save operation.
    public static class StoryText
    {
        private static StoryTextDocument current=new StoryTextDocument();
        public static int Revision {get;private set;}
        public static StoryTextDocument Snapshot()=>current.Copy();
        public static void Replace(StoryTextDocument document){document.Validate();current=document.Copy();Revision++;}
        public static string Get(string key,string fallback)=>current.entries.FirstOrDefault(e=>e.key==key)?.text??fallback??key;
        public static IEnumerable<StoryTextEntry> Find(string query,string group)=>current.entries.Where(e=>(string.IsNullOrEmpty(group)||e.group==group)&&(string.IsNullOrWhiteSpace(query)||(e.key+"\n"+e.text+"\n"+e.notes).IndexOf(query,StringComparison.OrdinalIgnoreCase)>=0)).Select(e=>e.Copy());
    }
    public static class StoryTextCsv
    {
        public static string Export(StoryTextDocument document)
        {
            document.Validate();var result=new StringBuilder("key,group,text,notes\r\n");
            string Quote(string value)=>"\""+value.Replace("\"","\"\"")+"\"";
            foreach(var e in document.entries)result.AppendLine(string.Join(",",new[]{e.key,e.group,e.text,e.notes}.Select(Quote)));
            return result.ToString();
        }
        public static StoryTextDocument Import(string csv)
        {
            if(csv==null)throw new ArgumentException("CSV为空");
            csv=csv.TrimStart('\ufeff');var rows=new List<string[]>();var row=new List<string>();var field=new StringBuilder();bool quoted=false,closed=false;
            for(int i=0;i<csv.Length;i++)
            {
                char c=csv[i];
                if(quoted){if(c=='"'){if(i+1<csv.Length&&csv[i+1]=='"'){field.Append('"');i++;}else{quoted=false;closed=true;}}else field.Append(c);continue;}
                if(c==','||c=='\r'||c=='\n')
                {
                    row.Add(field.ToString());field.Clear();closed=false;
                    if(c!=','){if(c=='\r'&&i+1<csv.Length&&csv[i+1]=='\n')i++;if(row.Any(v=>v!=""))rows.Add(row.ToArray());row.Clear();}continue;
                }
                if(closed)throw new ArgumentException("CSV引号后出现非分隔字符");
                if(c=='"'){if(field.Length!=0)throw new ArgumentException("CSV引号位置非法");quoted=true;}else field.Append(c);
            }
            if(quoted)throw new ArgumentException("CSV引号未闭合");
            if(field.Length>0||row.Count>0||closed){row.Add(field.ToString());rows.Add(row.ToArray());}
            if(rows.Count<2||!rows[0].SequenceEqual(new[]{"key","group","text","notes"}))throw new ArgumentException("CSV表头必须为key,group,text,notes");
            var document=new StoryTextDocument();foreach(var values in rows.Skip(1)){if(values.Length!=4)throw new ArgumentException("CSV每条记录必须4列");document.entries.Add(new StoryTextEntry {key=values[0],group=values[1],text=values[2],notes=values[3]});}
            document.Validate();return document;
        }
    }
}

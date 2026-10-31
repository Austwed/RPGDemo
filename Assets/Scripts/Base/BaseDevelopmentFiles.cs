using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
namespace BaseConquest.Base
{
    [Serializable] public sealed class BaseTemplateEntry { public string File, Title; }
    [Serializable] public sealed class BaseTemplateList { public BaseTemplateEntry[] Entries; }
    public static class BaseDevelopmentFiles
    {
        public static BaseTemplateEntry[] Templates(string folder,Func<string,string> title,BaseTemplateEntry[] fallback)
        {
            int Number(string file)=>int.TryParse(Path.GetFileName(file).Split('_')[0],out int n)?n:-1;
            if(!Directory.Exists(folder))return fallback??Array.Empty<BaseTemplateEntry>();
            return Directory.GetFiles(folder,"*.json").Where(f=>Number(f)>0).OrderByDescending(Number).ThenBy(f=>f,StringComparer.Ordinal)
                .Select(f=>new BaseTemplateEntry {File=Path.GetFileName(f),Title=title(System.IO.File.ReadAllText(f))}).ToArray();
        }
        public static void ClearEditorDevelopment(string baseFolder)
        {
            string root=Path.GetFullPath(baseFolder),target=Path.GetFullPath(Path.Combine(root,"EditorDevelopment"));
            if(!target.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)||Path.GetFileName(target)!="EditorDevelopment")throw new IOException("拒绝清理非开发测试目录");
            if(!Directory.Exists(target))return;
            var pending=new Stack<string>();var directories=new List<string>();var files=new List<string>();pending.Push(target);
            // Validate the complete tree before removing anything; never follow directory links.
            while(pending.Count>0)
            {
                var path=pending.Pop();if((System.IO.File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0)throw new IOException("开发测试目录含链接，已停止清理");
                if(Directory.Exists(path)){directories.Add(path);foreach(var entry in Directory.GetFileSystemEntries(path))pending.Push(entry);}
                else files.Add(path);
            }
            foreach(var file in files)System.IO.File.Delete(file);
            foreach(var dir in directories.OrderByDescending(d=>d.Length))if(dir!=target)Directory.Delete(dir);
        }
    }
}

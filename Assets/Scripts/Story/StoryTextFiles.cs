using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace BaseConquest.Story
{
    public static class StoryTextFiles
    {
        private const int InstalledRevision=3;
        public static string SourcePath=>Path.Combine(Application.streamingAssetsPath,"Text/story-text.json");
        public static string EditablePath=>Application.isEditor ? SourcePath : Path.Combine(ExchangeDirectory,"story-text-v"+InstalledRevision+".json");
        public static string ExchangeDirectory=>Application.isEditor ? Path.GetFullPath(Path.Combine(Application.dataPath,"../Logs/StoryTextExports")) : Path.Combine(BaseConquest.Base.BaseBattleGateway.PlayerDataDirectory,"StoryText");
        public static string CsvPath=>Path.Combine(ExchangeDirectory,"story-text.csv");
        public static string ExportPath=>Path.Combine(ExchangeDirectory,"story-text.export.json");
        public static string LastError {get;private set;}="";
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)] private static void Initialize()
        {
            if(Application.isEditor||File.Exists(EditablePath)||!SourcePath.Contains("://")){TryReload();return;}
            var host=new GameObject("StoryTextLoader");UnityEngine.Object.DontDestroyOnLoad(host);host.AddComponent<StoryTextLoader>().StartCoroutine(LoadPackaged(host));
        }
        private static IEnumerator LoadPackaged(GameObject host)
        {
            using(var request=UnityWebRequest.Get(SourcePath))
            {
                yield return request.SendWebRequest();
                try{if(request.result!=UnityWebRequest.Result.Success)throw new IOException(request.error);InstallPackaged(Parse(request.downloadHandler.text));}
                catch(Exception e){LastError=e.Message;Debug.LogWarning("剧情文本读取失败，使用原文回落："+LastError);}
            }
            UnityEngine.Object.Destroy(host);
        }
        public static StoryTextDocument Parse(string json){var document=JsonUtility.FromJson<StoryTextDocument>(json);if(document==null)throw new ArgumentException("文本JSON为空");document.Validate();return document;}
        public static StoryTextDocument UpgradeInstalled(StoryTextDocument packaged,StoryTextDocument installed)
        {
            var result=packaged.Copy();
            if(installed==null)return result;
            var old=installed.entries.ToDictionary(entry=>entry.key,StringComparer.Ordinal);
            foreach(var entry in result.entries)
                if(old.TryGetValue(entry.key,out var previous)&&previous.revision>=entry.revision)
                    entry.text=previous.text;
            return result;
        }
        private static void InstallPackaged(StoryTextDocument packaged)
        {
            StoryTextDocument installed=null;
            foreach (string name in new[] { "story-text-v2.json", "story-text.json" })
            {
                string oldPath = Path.Combine(ExchangeDirectory, name);
                if (!File.Exists(oldPath)) continue;
                try { installed = Parse(File.ReadAllText(oldPath, Encoding.UTF8)); break; }
                catch(Exception e){Debug.LogWarning("旧剧情文本副本无法读取，已使用新版正文："+e.Message);}
            }
            var upgraded=UpgradeInstalled(packaged,installed);
            ApplyLoaded(upgraded);
            Write(EditablePath,JsonUtility.ToJson(upgraded,true));
        }
        private static void ApplyLoaded(StoryTextDocument document)
        {
            StoryText.Replace(document);
            LastError="";
        }
        // Optional paths let isolated Editor checks exercise persistence without touching authored text or player data.
        public static void Reload(string path=null)
        {
            if(path!=null||Application.isEditor||File.Exists(EditablePath))
                ApplyLoaded(Parse(File.ReadAllText(path??EditablePath,Encoding.UTF8)));
            else InstallPackaged(Parse(File.ReadAllText(SourcePath,Encoding.UTF8)));
        }
        public static bool TryReload(){try{Reload();return true;}catch(Exception e){LastError=e.Message;Debug.LogWarning("剧情文本读取失败，保留当前目录／原文回落："+LastError);return false;}}
        private static void Write(string path,string text)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));string temp=path+".tmp";
            File.WriteAllText(temp,text,new UTF8Encoding(false));if(File.Exists(path))File.Replace(temp,path,null);else File.Move(temp,path);
        }
        public static void Save(StoryTextDocument document,string path=null){document.Validate();Write(path??EditablePath,JsonUtility.ToJson(document,true));StoryText.Replace(document);LastError="";}
        public static void ExportJson(string path=null)=>Write(path??ExportPath,JsonUtility.ToJson(StoryText.Snapshot(),true));
        public static void ExportCsv(string path=null)=>Write(path??CsvPath,"\ufeff"+StoryTextCsv.Export(StoryText.Snapshot()));
        public static StoryTextDocument ReadCsv(string path=null)=>StoryTextCsv.Import(File.ReadAllText(path??CsvPath,Encoding.UTF8));
    }
}

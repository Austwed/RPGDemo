using System;
using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace BaseConquest.Story
{
    public static class StoryTextFiles
    {
        public static string SourcePath=>Path.Combine(Application.streamingAssetsPath,"Text/story-text.json");
        public static string EditablePath=>Application.isEditor ? SourcePath : Path.Combine(Application.persistentDataPath,"StoryText/story-text.json");
        public static string ExchangeDirectory=>Application.isEditor ? Path.GetFullPath(Path.Combine(Application.dataPath,"../Logs/StoryTextExports")) : Path.Combine(Application.persistentDataPath,"StoryText");
        public static string CsvPath=>Path.Combine(ExchangeDirectory,"story-text.csv");
        public static string ExportPath=>Path.Combine(ExchangeDirectory,"story-text.export.json");
        public static string LastError {get;private set;}="";
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)] private static void Initialize()
        {
            if(File.Exists(EditablePath)||!SourcePath.Contains("://")){TryReload();return;}
            var host=new GameObject("StoryTextLoader");UnityEngine.Object.DontDestroyOnLoad(host);host.AddComponent<StoryTextLoader>().StartCoroutine(LoadPackaged(host));
        }
        private static IEnumerator LoadPackaged(GameObject host)
        {
            using(var request=UnityWebRequest.Get(SourcePath))
            {
                yield return request.SendWebRequest();
                try{if(request.result!=UnityWebRequest.Result.Success)throw new IOException(request.error);ApplyLoaded(Parse(request.downloadHandler.text));}
                catch(Exception e){LastError=e.Message;Debug.LogWarning("剧情文本读取失败，使用原文回落："+LastError);}
            }
            UnityEngine.Object.Destroy(host);
        }
        public static StoryTextDocument Parse(string json){var document=JsonUtility.FromJson<StoryTextDocument>(json);if(document==null)throw new ArgumentException("文本JSON为空");document.Validate();return document;}
        private static void ApplyLoaded(StoryTextDocument document)
        {
            StoryText.Replace(document);
            // Android's packaged StreamingAssets use a URL, so subsequent editor reloads need a real writable file.
            if(!Application.isEditor&&!File.Exists(EditablePath))Save(document);
            LastError="";
        }
        // Optional paths let isolated Editor checks exercise persistence without touching authored text or player data.
        public static void Reload(string path=null){ApplyLoaded(Parse(File.ReadAllText(path??(File.Exists(EditablePath)?EditablePath:SourcePath),Encoding.UTF8)));}
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

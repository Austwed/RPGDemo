using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using BaseConquest.Base;
using BaseConquest.Battle;
using BaseConquest.Presentation;
using UnityEditor;
using UnityEngine;

namespace BaseConquest.Editor
{
    public static class VerifyDemoResources
    {
        // Dynamic families: character atlases and optional motions; UI prefabs and portrait fallbacks.
        // BaseSettings is explicitly optional (BaseSettings.Load constructs defaults).
        private static readonly HashSet<string> Optional=new HashSet<string>{"BaseSettings"};
        private static readonly string[] Cast={"player","female","ahe","azhu","galan","chief","batu","huoya","saber","archer","rider","baron","chunshen","horn","laozhou","merchant","villager","chenan","wilderness"};
        public static void Run()
        {
            var references=new SortedDictionary<string,HashSet<string>>(StringComparer.Ordinal);
            var missing=new SortedSet<string>(StringComparer.Ordinal);
            void Add(string path,string source,bool required=true)
            {
                if(!references.TryGetValue(path,out var origins))references[path]=origins=new HashSet<string>();origins.Add(source);
                if(Resources.Load(path)==null&&required&&!Optional.Contains(path))missing.Add(path+" ← "+source);
            }
            foreach(string file in Directory.GetFiles("Assets/Scripts","*.cs",SearchOption.AllDirectories))
            {
                string code=File.ReadAllText(file);
                code=Regex.Replace(code,@"/\*[\s\S]*?\*/|//[^\r\n]*","");
                foreach(Match match in Regex.Matches(code,@"Resources\.Load(?:<[^>]+>)?\(\s*""([^""\r\n]+)""\s*\)"))Add(match.Groups[1].Value,file);
                foreach(Match match in Regex.Matches(code,@"PixelDemoArt\.Load\(\s*""([^""]+)""\s*,\s*""([^""]+)""\s*\)"))
                    Add("PixelDemo/"+match.Groups[1].Value+"/"+PixelDemoArt.TroopId(match.Groups[2].Value)+"-v1",file);
            }
            foreach(string id in Cast)
            {
                string atlas=PixelDemoArt.AtlasId(id);Add("PixelDemo/Characters/"+atlas+"-v1","动态角色 "+id);
                foreach(string motion in new[]{"idle","walk","attack","down"}){string path="PixelDemo/Characters/"+atlas+"-"+motion+"-v2";if(Resources.Load<Texture2D>(path)!=null)Add(path,"可选动作 "+id,false);}
            }
            Add("PixelDemo/Characters/ancient-saber-v1","古裔剑士四向图");
            foreach (string file in Directory.GetFiles("Assets/Resources/PixelDemo/DialoguePortraits", "*-v1.png"))
                Add("PixelDemo/DialoguePortraits/" + Path.GetFileNameWithoutExtension(file), "共用半身像");
            foreach(string id in new[]{"player","female","ahe","azhu","galan","chief","batu","huoya","saber","archer","rider","villager"})
            {
                string path="PixelDemo/Characters/"+PixelDemoArt.AtlasId(id)+"-down-v2";
                Add(path,"战斗倒地 "+id);
                var down=Resources.Load<Texture2D>(path);
                if(down!=null&&(down.height%4!=0||down.width!=(down.height/4)*8))
                    throw new InvalidOperationException("倒地图集规格错误："+path);
            }
            // Literal IDs passed indirectly through local variables are evidence of a possible reference.
            var sourceText=string.Join("\n",Directory.GetFiles("Assets/Scripts","*.cs",SearchOption.AllDirectories).Select(File.ReadAllText));
            foreach(string guid in AssetDatabase.FindAssets("t:Texture2D",new[]{"Assets/Resources/PixelDemo"}))
            {
                string path=AssetDatabase.GUIDToAssetPath(guid),name=Path.GetFileNameWithoutExtension(path);
                string id=name.EndsWith("-v1",StringComparison.Ordinal)?name.Substring(0,name.Length-3):name;
                if(sourceText.Contains("\""+id+"\""))Add(Path.ChangeExtension(path.Substring("Assets/Resources/".Length),null).Replace('\\','/'),"源码间接字面 ID",false);
            }
            foreach(string direction in new[]{"north","south","east","west"})Add("PixelDemo/Terrain/stairs-"+direction+"-v1","四向坡道动态路径");
            foreach(string id in DemoUiIcons.Keys)Add("PixelDemo/Icons/icon-"+id+"-v1","UI 图标映射");
            foreach(string id in new[]{"world-background","world-home","world-camp","world-enemy-camp"})Add("PixelDemo/World/"+id+"-v1","世界地图映射");
            foreach(string id in new[]{"ahe","azhu","chenan","wilderness"})
            {var art=CommanderArt.For(id);if(art!=null)Add("Characters/"+art.TextureName,"旧版角色回退 "+id,false);}
            var catalog=Resources.Load<BuildingAppearanceCatalog>("BuildingAppearances");
            var dataFiles=Directory.GetFiles("Assets/Resources/DemoStages","*.json",SearchOption.AllDirectories).Concat(Directory.GetFiles("Assets/StreamingAssets/BaseScenarios","*.json",SearchOption.AllDirectories));
            foreach(string file in dataFiles)
            {
                string data=File.ReadAllText(file);
                foreach(Match m in Regex.Matches(data,@"""BackgroundResource""\s*:\s*""([^""]+)"""))Add(m.Groups[1].Value,file);
                foreach(Match m in Regex.Matches(data,@"""AssetId""\s*:\s*""([^""]+)"""))
                {
                    string id=m.Groups[1].Value;string prop=id=="demo-long-sword"?"longsword":id=="demo-broken-sword"?"broken-sword":id=="demo-debris"?"ruined-wall":id;
                    var texture=Resources.Load<Texture2D>("Environment/"+id)??PixelDemoArt.Load("Characters",id)??PixelDemoArt.Load("Props",prop)??catalog?.Find(id)?.Texture;
                    if(texture==null){missing.Add("AssetId:"+id+" ← "+file);continue;}
                    string asset=AssetDatabase.GetAssetPath(texture).Replace('\\','/');int index=asset.IndexOf("/Resources/",StringComparison.Ordinal);
                    if(index<0){missing.Add("AssetId 非 Resources:"+id+" ← "+file);continue;}
                    Add(Path.ChangeExtension(asset.Substring(index+"/Resources/".Length),null),file);
                }
            }
            var report=new StringBuilder("# Demo 资源引用检查\n\n## 引用\n\n");
            foreach(var entry in references)report.AppendLine("- `"+entry.Key+"`："+string.Join("、",entry.Value.OrderBy(x=>x,StringComparer.Ordinal)));
            report.AppendLine("\n## 缺失\n");foreach(string path in missing)report.AppendLine("- "+path);if(missing.Count==0)report.AppendLine("- 无");
            report.AppendLine("\n## 未引用贴图（仅报告）\n");
            foreach(string guid in AssetDatabase.FindAssets("t:Texture2D",new[]{"Assets/Resources/PixelDemo"}))
            {string path=AssetDatabase.GUIDToAssetPath(guid);string resource=Path.ChangeExtension(path.Substring("Assets/Resources/".Length),null).Replace('\\','/');if(!references.ContainsKey(resource))report.AppendLine("- `"+resource+"`");}
            report.AppendLine("\n## NPC 专属图状态（仅报告）\n");
            report.AppendLine("- chenan：斥候陈安本人图");
            foreach(string id in new[]{"guard","survivor","villager-girl","rescued-villager"})
            {string resolved=BaseDemoNpcs.ResolveNpcArt(id);report.AppendLine("- "+id+"："+(resolved==id?"专属图已接线":"待补专属图（已接线，当前回退 "+resolved+"）"));}
            Directory.CreateDirectory("Logs");File.WriteAllText("Logs/verify-demo-resources.md",report.ToString(),Encoding.UTF8);
            if(missing.Count>0)throw new InvalidOperationException("资源检查缺失 "+missing.Count+" 项；见 Logs/verify-demo-resources.md");
            Debug.Log("[VerifyDemoResources] PASS");
        }
    }
}

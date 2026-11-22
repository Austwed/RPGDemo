using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using BaseConquest.Base;
#if UNITY_EDITOR
using UnityEngine;
#endif

namespace BaseConquest.Editor
{
    public sealed class PlanningBalanceImport
    {
        public BaseScenarioDocument Document;
        public string WorkbookPath, WorkbookHash, SeedPath, SeedHash, Preview;
    }

    public sealed class DemoBalanceImport
    {
        public DemoBalancePackage Package;
        public string WorkbookPath,WorkbookHash,SeedPath,SeedHash,OutputPath,OutputHash,Preview;
        public string ConfigurationHash,SerializedSnapshot;
        public string EffectiveHash=>ConfigurationHash??WorkbookHash;
        public Dictionary<string,string> StageHashes;
    }

    public static class DemoBalanceImporter
    {
        public const string WorkbookPath="../归档/文档/2026-11-20_旧数值配表/DEMO数值配置表.xlsx";
        public const string OutputPath="Assets/Resources/DemoBalance/DemoBalance.json";
        public static void Write(DemoBalanceImport import,Func<DemoBalancePackage,string> serialize,Func<string,DemoBalancePackage> deserialize,Func<string,BaseScenarioDocument> deserializeSeed)
        {
            if(import==null||import.Package==null)throw new InvalidOperationException("请先校验配表");
            if(import.Package.WorkbookSha256!=import.EffectiveHash)throw new InvalidOperationException("配表来源与预览快照不一致，请重新校验");
            if(PlanningBalanceImporter.Hash(File.ReadAllBytes(import.WorkbookPath))!=import.WorkbookHash||PlanningBalanceImporter.Hash(File.ReadAllBytes(import.SeedPath))!=import.SeedHash)throw new InvalidOperationException("配表或开局基底在预览后已修改，请重新校验");
            string current=File.Exists(import.OutputPath)?PlanningBalanceImporter.Hash(File.ReadAllBytes(import.OutputPath)):null;
            if(current!=import.OutputHash)throw new InvalidOperationException("目标配置在预览后已改变，请重新校验");
            import.Package.Validate();string expectedJson=serialize(import.Package);string json=import.SerializedSnapshot??expectedJson;var roundtrip=deserialize(json.TrimStart('\ufeff'));roundtrip?.Validate();
            if(roundtrip==null||serialize(roundtrip)!=expectedJson)throw new InvalidOperationException("导出后的快照不完整，原配置保留");
            string seedJson=File.ReadAllText(import.SeedPath,Encoding.UTF8).TrimStart('\ufeff');
            BaseDemo.CreateOpening(deserializeSeed(seedJson),seedJson,true,true,true,roundtrip.Rules).CreateState();
            Directory.CreateDirectory(Path.GetDirectoryName(import.OutputPath));string temp=import.OutputPath+"."+Guid.NewGuid().ToString("N")+".pending";
            try
            {
                File.WriteAllText(temp,json,new UTF8Encoding(false));
                if(PlanningBalanceImporter.Hash(File.ReadAllBytes(import.WorkbookPath))!=import.WorkbookHash||PlanningBalanceImporter.Hash(File.ReadAllBytes(import.SeedPath))!=import.SeedHash)throw new InvalidOperationException("配表或开局基底在写入前已改变，请重新校验");
                current=File.Exists(import.OutputPath)?PlanningBalanceImporter.Hash(File.ReadAllBytes(import.OutputPath)):null;
                if(current!=import.OutputHash)throw new InvalidOperationException("目标配置在写入前已改变，请重新校验");
                if(File.Exists(import.OutputPath))File.Replace(temp,import.OutputPath,null);else File.Move(temp,import.OutputPath);
            }
            finally{if(File.Exists(temp))File.Delete(temp);}
        }
        internal static void ReplaceStageIfUnchanged(string temp,string destination,string expectedHash)
        {
            string current=File.Exists(destination)?PlanningBalanceImporter.Hash(File.ReadAllBytes(destination)):null;
            if(current!=expectedHash)throw new InvalidOperationException("阶段档在写入前已改变，请重新校验："+Path.GetFileName(destination));
            if(current==null)File.Move(temp,destination);else File.Replace(temp,destination,null);
        }
        internal static void CommitPreparedStages(string config,string configHash,Dictionary<string,string> contents,Dictionary<string,string> expected,Action writeConfig,Action<string,string,string> replace)
        {
            var originals=new Dictionary<string,byte[]>();var committed=new Dictionary<string,string>();var temporary=new List<string>();
            originals[config]=File.Exists(config)?File.ReadAllBytes(config):null;
            foreach(var path in contents.Keys)originals[path]=File.Exists(path)?File.ReadAllBytes(path):null;
            try
            {
                foreach(var pair in contents){string temp=pair.Key+"."+Guid.NewGuid().ToString("N")+".pending";Directory.CreateDirectory(Path.GetDirectoryName(temp));File.WriteAllText(temp,pair.Value,new UTF8Encoding(false));temporary.Add(temp);}
                foreach(var path in contents.Keys){string current=File.Exists(path)?PlanningBalanceImporter.Hash(File.ReadAllBytes(path)):null;if(current!=expected[path])throw new InvalidOperationException("阶段档在生成期间已改变，请重新校验："+Path.GetFileName(path));}
                writeConfig();committed[config]=configHash;
                int index=0;foreach(var pair in contents){string temp=temporary[index++];try{replace(temp,pair.Key,expected[pair.Key]);}catch(Exception error){throw new IOException("更新阶段档 "+Path.GetFileName(pair.Key)+" 失败："+error.Message,error);}committed[pair.Key]=PlanningBalanceImporter.Hash(Encoding.UTF8.GetBytes(pair.Value));}
            }
            catch(Exception error)
            {
                var rollbackErrors=new List<string>();
                foreach(var pair in committed)
                {
                    try
                    {
                        string current=File.Exists(pair.Key)?PlanningBalanceImporter.Hash(File.ReadAllBytes(pair.Key)):null;
                        if(current!=pair.Value)throw new InvalidOperationException("文件在导入过程中被其他操作改变，未覆盖该外部修改");
                        var original=originals[pair.Key];
                        if(original==null){if(File.Exists(pair.Key))File.Delete(pair.Key);}
                        else{string temp=pair.Key+"."+Guid.NewGuid().ToString("N")+".rollback";try{File.WriteAllBytes(temp,original);if(File.Exists(pair.Key))File.Replace(temp,pair.Key,null);else File.Move(temp,pair.Key);}finally{if(File.Exists(temp))File.Delete(temp);}}
                    }
                    catch(Exception rollback){rollbackErrors.Add(pair.Key+"："+rollback.Message);}
                }
                if(rollbackErrors.Count>0)throw new InvalidOperationException("导入失败且部分文件回滚失败："+string.Join("；",rollbackErrors),error);
                throw;
            }
            finally{foreach(string temp in temporary)if(File.Exists(temp))File.Delete(temp);}
        }
#if UNITY_EDITOR
        // Build all four test starts before touching the active snapshot; if a
        // later file replacement fails, restore every original byte sequence.
        public static void WriteWithStages(DemoBalanceImport import)
        {
            if(import==null||Path.GetFullPath(import.OutputPath)!=Path.GetFullPath(OutputPath)||import.StageHashes?.Count!=4)throw new InvalidOperationException("请先校验正式Demo配表与阶段档");
            string seedJson=File.ReadAllText(import.SeedPath,Encoding.UTF8).TrimStart('\ufeff');
            Dictionary<string,string> stages;
            try { stages=DemoStageSaveTools.Generate(seedJson,import.Package.Rules,import.EffectiveHash); }
            catch(Exception error) { throw new InvalidOperationException("固定阶段档无法按当前配置生成："+error.Message+"。请检查开局资源、兵源和经营参数；原配置与阶段档未更改。",error); }
            if(stages.Count!=4||import.StageHashes.Keys.Any(k=>!stages.ContainsKey(k)))throw new InvalidOperationException("阶段档生成不完整");
            var paths=import.StageHashes.Keys.ToDictionary(k=>k,k=>Path.GetFullPath("Assets/Resources/DemoStages/"+k+".json"));
            foreach(var pair in paths)
            {
                string old=File.Exists(pair.Value)?PlanningBalanceImporter.Hash(File.ReadAllBytes(pair.Value)):null;
                if(old!=import.StageHashes[pair.Key])throw new InvalidOperationException("阶段档在预览后已改变，请重新校验："+pair.Key);
                var state=JsonUtility.FromJson<BaseState>(stages[pair.Key]);BaseEconomy.Validate(state);
                if(state.Demo.ConfigHash!=import.EffectiveHash)throw new InvalidOperationException("阶段档数值来源不一致："+pair.Key);
            }
            var contents=paths.ToDictionary(p=>p.Value,p=>stages[p.Key]);
            var expected=paths.ToDictionary(p=>p.Value,p=>import.StageHashes[p.Key]);
            string configHash=PlanningBalanceImporter.Hash(Encoding.UTF8.GetBytes(import.SerializedSnapshot??JsonUtility.ToJson(import.Package,true)));
            CommitPreparedStages(import.OutputPath,configHash,contents,expected,
                ()=>Write(import,p=>JsonUtility.ToJson(p,true),JsonUtility.FromJson<DemoBalancePackage>,JsonUtility.FromJson<BaseScenarioDocument>),ReplaceStageIfUnchanged);
        }
#endif
    }

    public static class PlanningBalanceImporter
    {
        public const string OutputPath="Assets/StreamingAssets/BaseScenarios/35_配表驱动验证.json";
        public static string Hash(byte[] bytes){using(var algorithm=SHA256.Create())return BitConverter.ToString(algorithm.ComputeHash(bytes)).Replace("-","").ToLowerInvariant();}
    }
}

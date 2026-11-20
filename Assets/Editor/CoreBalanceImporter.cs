using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using BaseConquest.Base;
#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
#endif

namespace BaseConquest.Editor
{
    [Serializable] public sealed class CoreBalanceSource
    {
        public int Version=1;
        public string Kind="SixSheetPatch",CoreWorkbook,CoreWorkbookSha256,BaselineSnapshotSha256,BaselineConfigurationHash,EffectiveConfigurationHash;
    }

    public sealed class CoreBalanceImport
    {
        public DemoBalanceImport Import;
        public DemoBalancePackage BaselinePackage;
        public CoreBalanceSource Source;
        public string BaselineJson,SourcePath,SourceHash,Preview;
    }

    public static class CoreBalanceImporter
    {
        public const string WorkbookPath="../归档/文档/2026-11-20_旧数值配表/DEMO核心数值表.xlsx";
        private const string EquipmentPatch = "EquipmentPatch";
        private const string CorePatch = "SixSheetPatch";
        private static string SourcePath(string output,bool equipmentOnly)
            =>Path.ChangeExtension(output,equipmentOnly?"equipment-source.json":"core-source.json");
        private static string EffectiveHash(string workbookHash,string baselineHash,bool equipmentOnly)
            =>PlanningBalanceImporter.Hash(Encoding.UTF8.GetBytes((equipmentOnly?"EquipmentWorkbook/v1\n":"CoreWorkbook/v1\n")+workbookHash+"\n"+baselineHash));
        private static RpgRules ReadTables(string workbook,RpgRules baseline,bool equipmentOnly)
            =>equipmentOnly?DemoBalanceTableReader.ReadEquipmentTables(workbook,baseline):DemoBalanceTableReader.ReadCoreTables(workbook,baseline);

        private sealed class Difference
        {
            public string Path,Pointer,Sheet;
            public object Before,After;
        }

        private static Dictionary<string,string> Allowed(RpgRules before,bool equipmentOnly)
        {
            var allowed=new Dictionary<string,string>(StringComparer.Ordinal);
            void Fields(string sheet,string prefix,params string[] names){foreach(string name in names)allowed.Add(prefix+"/"+name,sheet);}
            void Array(string sheet,string name,int count){for(int i=0;i<count;i++)allowed.Add("/"+name+"/"+i,sheet);}
            if(equipmentOnly)
            {
                for(int i=0;i<before.Equipment.Count;i++)
                    Fields("05a","/Equipment/"+i,"Damage","Health","Defense","CritMultiplier","Movement","Stage");
                foreach(string name in new[]{"WeaponValues","HelmetValues","ArmorValues","AccessoryValues"})
                    Array("05c",name,5);
                Array("05c","QualityValue",6);
                return allowed;
            }
            Fields("02a","","BuyFactor","SellFactor","ArmorK","ArmorCap","OpeningSupplyWearVersion","OpeningReturnedWear","OpeningLostWear","CriticalMultiplier","BaseInterval","MinimumInterval","ArrowSpeed","HeightAdvantage","HealThreshold","WeaponAttackScale","AttackEquipmentScale","HealthEquipmentScale","DefenseEquipmentScale","AwakenedChance","EnemyLeaderDeath","SweepLossAtParity","SweepSecondsAtParity","SweepMinimumSeconds","SoldierDeathBase","SoldierDeathSensitivity","HideoutEnemyCount");
            var ids=new HashSet<string>(from category in new[]{"soldier","captain_normal","captain_awakened"} from profession in Enumerable.Range(0,3) select RpgRules.Template(category,profession));
            for(int i=0;i<before.Templates.Count;i++)if(ids.Contains(before.Templates[i].Id))Fields("03b","/Templates/"+i,"Health","Attack","Defense","SightRadius");
            Array("03c","TroopBaseline",3);Array("03c","RecruitBasePrices",3);
            Fields("03c","","RecruitSupplyFactor","RecruitPriceSensitivity");
            Fields("06b","","SupplyAttackLow","SupplyAttackHigh","SupplyDefenseLow","SupplyDefenseHigh","SupplyMaterial");
            Fields("07a","","LeaderFood","ArcherFood","OpeningRecoveryPerDay","FacilityRepairMaterials","FacilityRepairCoins");
            Fields("07a","/OpeningEconomy","SoldierFood","CavalryFood","LeaderFood","FarmPerWorker","WorkshopPerWorker","HungerProductionPenalty","HungerAttackPenalty");
            Array("07a","Capacity",3);Array("07a","CampMaterials",2);Array("07a","CampCoins",2);
            Fields("09d","/OpeningRewards","MaximumEnergy","RewardCost","ExposureFraction","FirstCoins","FirstFood","FirstMaterials","Coins","ItemCount","Energy");
            foreach(string name in new[]{"OpeningLordReward","OpeningDefenseReward","OpeningCampReward","OpeningDefenseLoss"})Fields("09d","/"+name,"Food","Materials","Coins");
            Fields("09d","","OpeningBattleRewardVersion");
            return allowed;
        }

        private static string EscapePointer(string value)=>value.Replace("~","~0").Replace("/","~1");
        private static void Compare(object before,object after,string path,string pointer,List<Difference> differences)
        {
            if(ReferenceEquals(before,after))return;
            if(before==null||after==null||before.GetType()!=after.GetType())
            {differences.Add(new Difference{Path=path,Pointer=pointer,Before=before,After=after});return;}
            var type=before.GetType();
            if(type.IsPrimitive||type.IsEnum||type==typeof(string)||type==typeof(decimal))
            {if(!before.Equals(after))differences.Add(new Difference{Path=path,Pointer=pointer,Before=before,After=after});return;}
            if(before is IList left&&after is IList right)
            {
                if(left.Count!=right.Count){differences.Add(new Difference{Path=path+".Count",Pointer=pointer,Before=left.Count,After=right.Count});return;}
                for(int i=0;i<left.Count;i++)Compare(left[i],right[i],path+"["+i+"]",pointer+"/"+i,differences);
                return;
            }
            foreach(var field in type.GetFields(BindingFlags.Instance|BindingFlags.Public).Where(f=>!f.IsDefined(typeof(NonSerializedAttribute),false)).OrderBy(f=>f.Name,StringComparer.Ordinal))
                Compare(field.GetValue(before),field.GetValue(after),path.Length==0?field.Name:path+"."+field.Name,pointer+"/"+EscapePointer(field.Name),differences);
        }

        private static List<Difference> CheckedDifferences(RpgRules before,RpgRules after,bool equipmentOnly)
        {
            if(before==null||after==null)throw new InvalidOperationException("局部导入差异检查缺少规则");
            var allowed=Allowed(before,equipmentOnly);var differences=new List<Difference>();Compare(before,after,"","",differences);
            foreach(var change in differences)
            {
                if(!allowed.TryGetValue(change.Pointer,out change.Sheet))throw new InvalidOperationException("局部导入越界："+change.Path+" 不属于本次可更新字段");
            }
            return differences;
        }
        private static string Display(object value)=>value==null?"null":Convert.ToString(value,CultureInfo.InvariantCulture);
        public static string[] Changes(RpgRules before,RpgRules after,bool equipmentOnly=false)=>CheckedDifferences(before,after,equipmentOnly).Select(c=>c.Sheet+" · "+c.Path+"："+Display(c.Before)+" → "+Display(c.After)).ToArray();

        public static string PatchJson(string baselineJson,DemoBalancePackage before,DemoBalancePackage after,Func<DemoBalancePackage,string> serialize,Func<string,DemoBalancePackage> deserialize,bool equipmentOnly=false)
        {
            if(before==null||after==null||before.Version!=after.Version)throw new InvalidOperationException("局部导入不能更改快照版本");
            var decoded=deserialize(baselineJson.TrimStart('\ufeff'));
            if(decoded==null||serialize(decoded)!=serialize(before))throw new InvalidOperationException("局部导入基底与原JSON不一致");
            var differences=CheckedDifferences(before.Rules,after.Rules,equipmentOnly);after.Validate();
            var original=CoreJsonSpans.Parse(baselineJson);string generatedJson=serialize(after);var generated=CoreJsonSpans.Parse(generatedJson);
            var pointers=differences.Select(c=>"/Rules"+c.Pointer).ToList();
            if(before.WorkbookSha256!=after.WorkbookSha256)pointers.Add("/WorkbookSha256");
            var edits=new List<(CoreJsonSpans.Span span,string value)>();
            foreach(string pointer in pointers)
            {
                if(!generated.TryGetValue(pointer,out var newValue))throw new InvalidOperationException("导入结果缺少字段："+pointer);
                if(!original.TryGetValue(pointer,out var oldValue))
                {
                    // Older snapshots use the battlefield default and omit this optional field.
                    if(pointer.StartsWith("/Rules/Templates/",StringComparison.Ordinal) && pointer.EndsWith("/SightRadius",StringComparison.Ordinal) &&
                        newValue.Kind=='n' && original.TryGetValue(pointer.Substring(0,pointer.LastIndexOf('/')),out var template) && template.Kind=='{')
                    {
                        edits.Add((new CoreJsonSpans.Span{Start=template.Start+template.Length-1,Length=0},
                            ",\"SightRadius\":"+generatedJson.Substring(newValue.Start,newValue.Length)));
                        continue;
                    }
                    throw new InvalidOperationException("基底JSON缺少待更新字段："+pointer);
                }
                if(pointer!="/WorkbookSha256"&&(oldValue.Kind!='n'||newValue.Kind!='n'))throw new InvalidOperationException("局部导入只允许替换数值字段："+pointer);
                edits.Add((oldValue,generatedJson.Substring(newValue.Start,newValue.Length)));
            }
            var result=new StringBuilder(baselineJson);
            foreach(var edit in edits.OrderByDescending(e=>e.span.Start))result.Remove(edit.span.Start,edit.span.Length).Insert(edit.span.Start,edit.value);
            string json=result.ToString();var roundtrip=deserialize(json.TrimStart('\ufeff'));roundtrip?.Validate();
            if(roundtrip==null||serialize(roundtrip)!=generatedJson)throw new InvalidOperationException("局部导入JSON回读不一致，原配置保留");
            return json;
        }

        public static CoreBalanceImport Read(string workbookPath,string seedPath,string outputPath,Func<string,DemoBalancePackage> deserializePackage,Func<string,BaseScenarioDocument> deserializeSeed,bool equipmentOnly=false)
        {
            string workbook=Path.GetFullPath(workbookPath),seed=Path.GetFullPath(seedPath),output=Path.GetFullPath(outputPath);
            if(!File.Exists(output))throw new InvalidOperationException("请先提供当前有效的Demo数值快照");
            byte[] workbookBytes=File.ReadAllBytes(workbook),baselineBytes=File.ReadAllBytes(output),seedBytes=File.ReadAllBytes(seed);
            string workbookHash=PlanningBalanceImporter.Hash(workbookBytes),baselineHash=PlanningBalanceImporter.Hash(baselineBytes),seedHash=PlanningBalanceImporter.Hash(seedBytes);
            string baselineJson=Encoding.UTF8.GetString(baselineBytes),seedJson=Encoding.UTF8.GetString(seedBytes).TrimStart('\ufeff');
            var baseline=deserializePackage(baselineJson.TrimStart('\ufeff'));if(baseline==null)throw new InvalidOperationException("当前Demo数值快照为空");baseline.Validate();
            var rules=ReadTables(workbook,baseline.Rules,equipmentOnly);
            if(PlanningBalanceImporter.Hash(File.ReadAllBytes(workbook))!=workbookHash||PlanningBalanceImporter.Hash(File.ReadAllBytes(output))!=baselineHash||PlanningBalanceImporter.Hash(File.ReadAllBytes(seed))!=seedHash)
                throw new InvalidOperationException("配表、基底快照或开局基底在读取期间发生变化，请重新校验");
            string effectiveHash=EffectiveHash(workbookHash,baselineHash,equipmentOnly);
            var package=new DemoBalancePackage{Version=baseline.Version,WorkbookSha256=effectiveHash,Rules=rules};package.Validate();
            BaseDemo.CreateOpening(deserializeSeed(seedJson),seedJson,true,true,true,rules).CreateState();
            string sourcePath=SourcePath(output,equipmentOnly);
            string[] stages={"camp-male","camp-female","defense-male","defense-female"};
            var hashes=stages.ToDictionary(n=>n,n=>{string p=Path.GetFullPath("Assets/Resources/DemoStages/"+n+".json");return File.Exists(p)?PlanningBalanceImporter.Hash(File.ReadAllBytes(p)):null;});
            var source=new CoreBalanceSource{Kind=equipmentOnly?EquipmentPatch:CorePatch,CoreWorkbook=Path.GetFileName(workbook),CoreWorkbookSha256=workbookHash,BaselineSnapshotSha256=baselineHash,BaselineConfigurationHash=baseline.WorkbookSha256,EffectiveConfigurationHash=effectiveHash};
            var changes=Changes(baseline.Rules,rules,equipmentOnly);
            string title=equipmentOnly?"个人装备与价格":"核心六页表";
            var lines=new List<string>{title+"导入："+workbook,"基底快照："+output,"配表 SHA-256："+workbookHash,"基底文件 SHA-256："+baselineHash,"基底来源标识："+baseline.WorkbookSha256,"组合来源标识："+effectiveHash,"来源记录："+sourcePath,"数值字段变更："+changes.Length+"项"};
            lines.AddRange(changes);if(changes.Length==0)lines.Add("已接入数值与当前运行快照一致。");
            lines.Add("其余规则字段保持原值；JSON中的未知字段和未改内容保留原文。");
            lines.Add(equipmentOnly?"本次读取 05a 的装备数值与价值阶段，05c 的槽位基础价与品质价值倍率。"
                :"02a试算和未接入区域仅作参考；06b工作量及二级原料数只校验，招降、奖品池、遗迹每日体力恢复未新增接入。");
            lines.Add("确认后更新数值快照、来源记录及四份阶段档；当前玩家存档不被改写。");
            lines.Add("来源记录中的组合标识须与当前快照一致；全量导入后标识不一致的记录只作历史参考。");
            string preview=string.Join("\n",lines);
            return new CoreBalanceImport{BaselinePackage=baseline,BaselineJson=baselineJson,Source=source,SourcePath=sourcePath,SourceHash=File.Exists(sourcePath)?PlanningBalanceImporter.Hash(File.ReadAllBytes(sourcePath)):null,Preview=preview,
                Import=new DemoBalanceImport{Package=package,WorkbookPath=workbook,WorkbookHash=workbookHash,ConfigurationHash=effectiveHash,SeedPath=seed,SeedHash=seedHash,OutputPath=output,OutputHash=baselineHash,StageHashes=hashes,Preview=preview}};
        }

#if UNITY_EDITOR
        public static void WriteWithStages(CoreBalanceImport core)
        {
            var import=core?.Import;
            bool equipmentOnly=core?.Source?.Kind==EquipmentPatch;
            if(import==null||Path.GetFullPath(import.OutputPath)!=Path.GetFullPath(DemoBalanceImporter.OutputPath)||import.StageHashes?.Count!=4)throw new InvalidOperationException("请先校验配表与当前快照");
            string[] stageNames={"camp-male","camp-female","defense-male","defense-female"};
            if(stageNames.Any(n=>!import.StageHashes.ContainsKey(n))||Path.GetFullPath(core.SourcePath)!=SourcePath(Path.GetFullPath(import.OutputPath),equipmentOnly))throw new InvalidOperationException("关联输出路径与预览不一致");
            string expectedSource=EffectiveHash(import.WorkbookHash,import.OutputHash,equipmentOnly);
            if(core.Source==null||core.Source.Version!=1||core.Source.Kind!=(equipmentOnly?EquipmentPatch:CorePatch)||core.Source.CoreWorkbook!=Path.GetFileName(import.WorkbookPath)||import.Package.WorkbookSha256!=expectedSource||import.ConfigurationHash!=expectedSource||core.Source.EffectiveConfigurationHash!=expectedSource||core.Source.CoreWorkbookSha256!=import.WorkbookHash||core.Source.BaselineSnapshotSha256!=import.OutputHash||core.Source.BaselineConfigurationHash!=core.BaselinePackage.WorkbookSha256||PlanningBalanceImporter.Hash(Encoding.UTF8.GetBytes(core.BaselineJson))!=import.OutputHash)
                throw new InvalidOperationException("局部导入来源记录与基底不一致，请重新校验");
            var expectedRules=ReadTables(import.WorkbookPath,core.BaselinePackage.Rules,equipmentOnly);
            if(JsonUtility.ToJson(expectedRules)!=JsonUtility.ToJson(import.Package.Rules))throw new InvalidOperationException("待写规则与预览不一致，请重新校验");
            string seedJson=File.ReadAllText(import.SeedPath,Encoding.UTF8).TrimStart('\ufeff');
            string Serialize(DemoBalancePackage p)=>PatchJson(core.BaselineJson,core.BaselinePackage,p,x=>JsonUtility.ToJson(x,true),JsonUtility.FromJson<DemoBalancePackage>,equipmentOnly);
            string configJson=Serialize(import.Package);
            Dictionary<string,string> stages;
            try{stages=DemoStageSaveTools.Generate(seedJson,import.Package.Rules,import.ConfigurationHash);}
            catch(Exception error){throw new InvalidOperationException("四份阶段档无法按当前参数生成："+error.Message+"。原配置与阶段档保留。",error);}
            if(stages.Count!=4||import.StageHashes.Keys.Any(k=>!stages.ContainsKey(k)))throw new InvalidOperationException("阶段档生成不完整");
            var contents=new Dictionary<string,string>();var expected=new Dictionary<string,string>();
            foreach(var pair in import.StageHashes)
            {
                string path=Path.GetFullPath("Assets/Resources/DemoStages/"+pair.Key+".json");
                var state=JsonUtility.FromJson<BaseState>(stages[pair.Key]);BaseEconomy.Validate(state);
                if(state.Demo.ConfigHash!=import.ConfigurationHash)throw new InvalidOperationException("阶段档组合来源不一致："+pair.Key);
                contents.Add(path,stages[pair.Key]);expected.Add(path,pair.Value);
            }
            contents.Add(core.SourcePath,JsonUtility.ToJson(core.Source,true));expected.Add(core.SourcePath,core.SourceHash);
            DemoBalanceImporter.CommitPreparedStages(import.OutputPath,PlanningBalanceImporter.Hash(Encoding.UTF8.GetBytes(configJson)),contents,expected,
                ()=>DemoBalanceImporter.Write(import,Serialize,JsonUtility.FromJson<DemoBalancePackage>,JsonUtility.FromJson<BaseScenarioDocument>),DemoBalanceImporter.ReplaceStageIfUnchanged);
        }
#endif
    }

#if UNITY_EDITOR
    public sealed class CoreBalanceWindow:EditorWindow
    {
        private string workbookPath,error="";
        private CoreBalanceImport imported;
        private Vector2 scroll;
        [SerializeField] private bool equipmentOnly;
        public static void Open()=>Open(false);

        public static void OpenEquipment()=>Open(true);

        private static void Open(bool equipmentOnly)
        {
            var window=GetWindow<CoreBalanceWindow>(equipmentOnly?"Demo个人装备与价格":"Demo核心六页表");
            window.equipmentOnly=equipmentOnly;
            window.workbookPath=Path.GetFullPath(equipmentOnly?DemoBalanceImporter.WorkbookPath:CoreBalanceImporter.WorkbookPath);
            window.imported=null;
            window.error="";
            window.minSize=new Vector2(680,480);
            window.Show();
        }
        private void OnGUI()
        {
            string scope=equipmentOnly?"05a 装备属性与价值阶段、05c 装备基础价与品质倍率":"六页已接入字段";
            EditorGUILayout.HelpBox("以当前Demo数值快照为基底，可更新"+scope+"。确认后更新快照、来源记录与四份阶段档。",MessageType.Info);
            using(new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode||EditorApplication.isCompiling||EditorApplication.isUpdating))
            {
                using(new EditorGUILayout.HorizontalScope())
                {
                    string next=EditorGUILayout.TextField(equipmentOnly?"Demo数值配置表":"独立六页表",workbookPath);
                    if(next!=workbookPath){workbookPath=next;imported=null;}
                    if(GUILayout.Button("选择",GUILayout.Width(55)))
                    {
                        string selected=EditorUtility.OpenFilePanel(equipmentOnly?"选择Demo数值配置表":"选择Demo核心六页表",Path.GetDirectoryName(workbookPath),"xlsx");
                        if(selected!=""){workbookPath=selected;imported=null;}
                    }
                }
                if(GUILayout.Button("校验并预览差异",GUILayout.Height(28)))
                {
                    try{imported=CoreBalanceImporter.Read(workbookPath,PlanningBalanceImporter.OutputPath,DemoBalanceImporter.OutputPath,JsonUtility.FromJson<DemoBalancePackage>,JsonUtility.FromJson<BaseScenarioDocument>,equipmentOnly);error="";}
                    catch(Exception exception){imported=null;error=exception.Message;}
                }
                if(error!="")EditorGUILayout.HelpBox(error,MessageType.Error);
                scroll=EditorGUILayout.BeginScrollView(scroll);
                if(imported!=null)EditorGUILayout.LabelField(imported.Preview,EditorStyles.wordWrappedLabel);
                EditorGUILayout.EndScrollView();
                using(new EditorGUI.DisabledScope(imported==null))if(GUILayout.Button("确认应用并重建阶段档",GUILayout.Height(30)))
                {
                    try
                    {
                        if(Path.GetFullPath(workbookPath)!=imported.Import.WorkbookPath)throw new InvalidOperationException("配表路径已改变，请重新校验");
                        CoreBalanceImporter.WriteWithStages(imported);
                        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);imported=null;error="";
                        Debug.Log("数值变更已应用，来源记录与四份阶段档已一并更新。");
                    }
                    catch(Exception exception){error=exception.Message;}
                }
            }
        }
    }
#endif
}

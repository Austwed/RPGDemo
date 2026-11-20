using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using BaseConquest.Base;
using BaseConquest.Battle;
#if UNITY_EDITOR
using UnityEditor;
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
        public const string WorkbookPath="../策划/DEMO/DEMO数值配置表.xlsx";
        public const string OutputPath="Assets/Resources/DemoBalance/DemoBalance.json";
        public static DemoBalanceImport Read(string workbookPath,string seedPath,string outputPath,Func<string,BaseScenarioDocument> deserializeSeed)
        {
            string workbook=Path.GetFullPath(workbookPath),seed=Path.GetFullPath(seedPath),output=Path.GetFullPath(outputPath);
            var bytes=File.ReadAllBytes(workbook);string hash=PlanningBalanceImporter.Hash(bytes);
            var rules=DemoBalanceTableReader.ReadVerifiedTables(workbook);
            if(PlanningBalanceImporter.Hash(File.ReadAllBytes(workbook))!=hash)throw new InvalidOperationException("配表在读取期间发生变化，请重新校验");
            var seedBytes=File.ReadAllBytes(seed);var seedDocument=deserializeSeed(Encoding.UTF8.GetString(seedBytes).TrimStart('\ufeff'));
            var package=new DemoBalancePackage {WorkbookSha256=hash,Rules=rules};package.Validate();
            var previewState=BaseDemo.CreateOpening(seedDocument,Encoding.UTF8.GetString(seedBytes).TrimStart('\ufeff'),true,true,true,rules).CreateState();
            var lines=new List<string>{"来源 SHA-256："+hash,"输出："+output,"新局读取独立快照；已经创建的存档不被改写。","战斗基础："};
            lines.AddRange(rules.Templates.Select(t=>t.Id+"：生命 "+t.Health+"，攻击 "+t.Attack+"，防御 "+t.Defense+"，基础视野 "+(t.SightRadius>0?t.SightRadius+"格":"随战场")));
            lines.Add("队长成长倍率（生命 / 攻击 / 防御，均以1级裸装为基准）：");
            lines.AddRange(Enumerable.Range(1,8).Select(i=>(i+1)+"级："+rules.Growth[i].ToString("R",CultureInfo.InvariantCulture)+" / "+rules.AttackGrowth[i].ToString("R",CultureInfo.InvariantCulture)+" / "+rules.DefenseGrowth[i].ToString("R",CultureInfo.InvariantCulture)));
            var initial=rules.OpeningInventory;
            lines.Add("首日扣粮前库存：粮食 "+initial.Food+"，原料 "+initial.Materials+"，钱币 "+initial.Coins+"，通用军需 "+string.Join(" / ",initial.Equipment.OrderBy(e=>e.Level).Select(e=>e.Count))+"；新局按实际口粮扣除。");
            lines.Add("设施岗位：粮田 "+rules.OpeningFacilityCapacity[0]+"（最低保留 "+rules.OpeningFacilityMinimum[0]+"），工坊修复后 "+rules.OpeningFacilityCapacity[1]+"（最低保留 "+rules.OpeningFacilityMinimum[1]+"）。");
            lines.Add("交易摊位每日供应：粮食 "+rules.OpeningTownStock[0]+"、原料 "+rules.OpeningTownStock[1]+"、一级军需 "+rules.OpeningTownStock[2]+"、二级军需 "+rules.OpeningTownStock[3]+"、锻造石 "+rules.ForgeDailyStock+"。");
            lines.Add("军需磨损：安全返回 "+rules.OpeningReturnedWear.ToString("0.##%",CultureInfo.InvariantCulture)+"，损失 "+rules.OpeningLostWear.ToString("0.##%",CultureInfo.InvariantCulture)+"；按配额和战果结算，小数跨战累计。");
            string Resources(BaseInventory v)=>"粮 "+v.Food+" / 原料 "+v.Materials+" / 钱 "+v.Coins;
            lines.Add("战斗资源（基础值）：首防胜利 "+Resources(rules.OpeningDefenseReward)+"；攻营胜利 "+Resources(rules.OpeningCampReward)+"。胜利奖受难度倍率影响。");
            lines.Add("掠夺单价（每名敌军）：口粮 "+rules.OpeningDefenseLoss.Food+" 天（按各兵种日耗折算）／原料 "+rules.OpeningDefenseLoss.Materials+"／钱币 "+rules.OpeningDefenseLoss.Coins
                +"；战损≥30% 轻掠（三分之一），战损≥50% 或战败重掠，自动迎战至少轻掠，实扣不超过库存。");
            lines.Add("重铸升／降概率（1、3、5、10石）："+string.Join("；",Enumerable.Range(0,4).Select(i=>rules.ReforgeUp[i]/100.0+"% / "+rules.ReforgeDown[i]/100.0+"%"))+"，中间数量线性插值。 ");
            var previewPeople=BaseAdventure.Members(previewState).ToArray();
            foreach(var person in previewPeople)person.Rpg?.Traits.Clear();
            var previewPlayer=previewPeople.First(l=>l.Id==previewState.PlayerLeaderId);
            previewPlayer.Rpg.Traits.Add(new RpgTrait {Id="merchant",Value=rules.Traits.Single(t=>t.Id=="merchant").Max});
            var tradePrices=previewState.Town.Goods.Select(g=>new {Name=g.Id=="food"?"粮食":g.Id=="materials"?"原料":g.Id=="supply1"?"一级军需":"二级军需",Buy=BaseTown.TradePrice(previewState,g.Id,true),Sell=BaseTown.TradePrice(previewState,g.Id,false)});
            var gearPrices=previewState.PersonalEquipment.Offers.Select(o=>new {Name=BasePersonalEquipment.Definition(o.Template).Name,Buy=BasePersonalEquipment.BuyPrice(previewState,o.Template),Sell=BaseRpg.Price(previewState,BaseRpg.Value(previewState,o.Template),false)});
            var stonePrice=new {Name="锻造石",Buy=BaseEquipmentCrafting.BuyPrice(previewState),Sell=BaseRpg.Price(previewState,BaseRpg.Value(previewState,BaseEquipmentCrafting.Stone),false)};
            var arbitrage=tradePrices.Concat(gearPrices).Concat(new[]{stonePrice}).FirstOrDefault(g=>g.Sell>g.Buy);
            if(arbitrage!=null)lines.Add("价格提示：商人特性达到配置上限且无坏脾气抵消时，"+arbitrage.Name+"买价 "+arbitrage.Buy+"、卖价 "+arbitrage.Sell+"，可能通过买卖获利；请结合供货与价格设置确认。此提示不阻止导入。");
            var suppliesRisk=BaseProgressSupplies.Items.Select(id=>new {Name=previewState.Backpack.Catalog.Single(d=>d.Id==id).Name,Buy=BaseProgressSupplies.Price(previewState,id),Sell=BaseRpg.Price(previewState,BaseRpg.Value(previewState,id),false)}).FirstOrDefault(g=>g.Sell>g.Buy);
            if(suppliesRisk!=null)lines.Add("价格提示：商人特性达到配置上限且无坏脾气抵消时，"+suppliesRisk.Name+"买价 "+suppliesRisk.Buy+"、卖价 "+suppliesRisk.Sell+"，可能通过买卖获利；请结合供货与价格设置确认。此提示不阻止导入。");
            lines.Add("固定人物技能与特性：");
            foreach(var preset in rules.StoryPresets)
            {
                string skill=rules.Skills.Single(x=>x.Id==preset.SkillId).Name;
                var traits=new List<RpgTrait>();
                if(!string.IsNullOrEmpty(preset.TraitId))traits.Add(new RpgTrait{Id=preset.TraitId,Value=preset.TraitValue});
                if(preset.ExtraTraits!=null)traits.AddRange(preset.ExtraTraits);
                string trait=traits.Count==0?"无":string.Join("、",traits.Select(t=>rules.Traits.Single(x=>x.Id==t.Id).Name+"（"+t.Id+" = "+t.Value.ToString("0.##",CultureInfo.InvariantCulture)+"）"));
                lines.Add(preset.Id+"："+skill+"（"+preset.SkillId+"）；特性 "+trait);
            }
            lines.Add("生命／攻击／防御9级成长倍率、三职业训练成本、13件装备及品质属性倍率、六技能、21特性、经营口粮／产量、军需、招募基准、领主／遗迹奖励及树石参数已校验。");
            lines.Add("品质掉落："+string.Join(" / ",rules.DropQuality.Select(x=>(x*100).ToString("0.##",CultureInfo.InvariantCulture)+"%"))+"；粮食基础价值 "+rules.FoodValue+"，营帐容量 "+string.Join(" / ",rules.Capacity)+"。");
            lines.Add("固定剧情、地图坐标、D10算法和装备槽位由代码／地图资产维护；其余未列入的表格区域暂不驱动游戏。");
            lines.Add("改动表格后必须重新校验；四个固定阶段档按当前数值实际完成招募、建设和日结。若无法到达首防，将显示失败原因并保留原配置与阶段档。");
            lines.Add("这是30页全量导入；确认生成会按原表重建完整规则，覆盖之前通过独立六页表调整的数值。");
            string[] stageNames={"camp-male","camp-female","defense-male","defense-female"};
            var stageHashes=stageNames.ToDictionary(n=>n,n=>{string p=Path.GetFullPath("Assets/Resources/DemoStages/"+n+".json");return File.Exists(p)?PlanningBalanceImporter.Hash(File.ReadAllBytes(p)):null;});
            return new DemoBalanceImport {Package=package,WorkbookPath=workbook,WorkbookHash=hash,SeedPath=seed,SeedHash=PlanningBalanceImporter.Hash(seedBytes),OutputPath=output,OutputHash=File.Exists(output)?PlanningBalanceImporter.Hash(File.ReadAllBytes(output)):null,Preview=string.Join("\n",lines),StageHashes=stageHashes};
        }
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

#if UNITY_EDITOR
    public sealed class DemoBalanceWindow:EditorWindow
    {
        private bool professions;private string workbookPath,error="";private DemoBalanceImport imported;private Vector2 scroll;
        [MenuItem("SLG/其他工具/导入/Demo数值表（校验与预览）")]
        public static void Open(){var w=GetWindow<DemoBalanceWindow>("Demo数值表");w.professions=false;w.imported=null;w.workbookPath=Path.GetFullPath(DemoBalanceImporter.WorkbookPath);w.minSize=new Vector2(600,420);w.Show();}
        [MenuItem("SLG/其他工具/导入/Demo职业扩展表（完整职业配置）")]
        public static void OpenProfessions()
        {
            var window=GetWindow<DemoBalanceWindow>("Demo数值表");
            window.professions=true;window.imported=null;window.error="";
            window.workbookPath=Path.GetFullPath(ProfessionBalanceImporter.WorkbookPath);
            window.minSize=new Vector2(600,420);window.Show();
        }
        [MenuItem("SLG/其他工具/生成/Demo数值快照与阶段档")]
        public static void GenerateDefault()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode||EditorApplication.isCompiling)throw new InvalidOperationException("请保持停止Play并等待编译");
            const string log="Logs/Demo/balance-import.log";Directory.CreateDirectory("Logs/Demo");File.WriteAllText(log,"RUNNING "+DateTime.UtcNow.ToString("o"));
            try
            {
                var result=DemoBalanceImporter.Read(DemoBalanceImporter.WorkbookPath,PlanningBalanceImporter.OutputPath,DemoBalanceImporter.OutputPath,JsonUtility.FromJson<BaseScenarioDocument>);
                DemoBalanceImporter.WriteWithStages(result);AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                File.WriteAllText(log,"PASS "+result.WorkbookHash+"\n"+result.Preview);
            }
            catch(Exception error){File.WriteAllText(log,"FAIL "+error);throw;}
        }
        private void OnGUI()
        {
            EditorGUILayout.HelpBox("校验后生成独立Demo数值快照并重建四份阶段测试档。变更供新局使用；已有存档不被改写。",MessageType.Info);
            using(new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode||EditorApplication.isCompiling||EditorApplication.isUpdating))
            {
                using(new EditorGUILayout.HorizontalScope())
                {
                    string next=EditorGUILayout.TextField("Demo数值表",workbookPath);if(next!=workbookPath){workbookPath=next;imported=null;}
                    if(GUILayout.Button("选择",GUILayout.Width(55))){string selected=EditorUtility.OpenFilePanel("选择Demo数值表",Path.GetDirectoryName(workbookPath),"xlsx");if(selected!=""){workbookPath=selected;imported=null;}}
                }
                if(GUILayout.Button("校验并预览",GUILayout.Height(28)))try{imported=professions ? ProfessionBalanceImporter.Read(workbookPath,PlanningBalanceImporter.OutputPath,DemoBalanceImporter.OutputPath) : DemoBalanceImporter.Read(workbookPath,PlanningBalanceImporter.OutputPath,DemoBalanceImporter.OutputPath,JsonUtility.FromJson<BaseScenarioDocument>);error="";}catch(Exception exception){imported=null;error=exception.Message;}
                if(error!="")EditorGUILayout.HelpBox(error,MessageType.Error);
                scroll=EditorGUILayout.BeginScrollView(scroll);if(imported!=null)EditorGUILayout.LabelField(imported.Preview,EditorStyles.wordWrappedLabel);EditorGUILayout.EndScrollView();
                using(new EditorGUI.DisabledScope(imported==null))if(GUILayout.Button("确认生成Demo数值快照",GUILayout.Height(30)))
                {
                    try
                    {
                        if(Path.GetFullPath(workbookPath)!=imported.WorkbookPath)throw new InvalidOperationException("配表路径已改变，请重新校验");
                        DemoBalanceImporter.WriteWithStages(imported);
                        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);imported=null;error="";Debug.Log("Demo数值快照及四份阶段档已生成，新局会使用新值。");
                    }
                    catch(Exception exception){error=exception.Message;}
                }
            }
        }
    }

#endif
}

#if UNITY_EDITOR
using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using BaseConquest.Base;
using BaseConquest.Battle;
using UnityEditor;
using UnityEngine;

namespace BaseConquest.Editor
{
    public static class VerifyProfessionWorkbook
    {
        private static readonly XNamespace Main="http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        private static readonly XNamespace Rel="http://schemas.openxmlformats.org/officeDocument/2006/relationships";

        private static void Check(bool value,string failure)
        {
            if(!value)throw new InvalidOperationException("[P52Workbook] "+failure);
        }
        private static void Save(ZipArchive archive,string path,XDocument document)
        {
            archive.GetEntry(path).Delete();
            using(var stream=archive.CreateEntry(path).Open())document.Save(stream);
        }

        // Mutate a copy of the real named Excel table in memory, including column order and absolute cell addresses.
        private static byte[] EditTable(byte[] source,string tableName,Action<XDocument,XDocument> edit)
        {
            using var result=new MemoryStream();
            result.Write(source,0,source.Length);
            using(var zip=new ZipArchive(result,ZipArchiveMode.Update,true))Apply(zip);
            return result.ToArray();

            void Apply(ZipArchive zip)
            {
                var book=ProfessionWorkbook.Xml(zip,"xl/workbook.xml");
                var links=ProfessionWorkbook.Relations(zip,"xl/workbook.xml");
                foreach(var tab in book.Descendants(Main+"sheet"))
                {
                    var sheetPath=links[(string)tab.Attribute(Rel+"id")];
                    var sheet=ProfessionWorkbook.Xml(zip,sheetPath);
                    var tables=ProfessionWorkbook.Relations(zip,sheetPath);
                    foreach(var part in sheet.Descendants(Main+"tablePart"))
                    {
                        string tablePath=tables[(string)part.Attribute(Rel+"id")];
                        var table=ProfessionWorkbook.Xml(zip,tablePath);
                        if((string)table.Root.Attribute("displayName")!=tableName)continue;
                        edit(table,sheet);
                        Save(zip,tablePath,table);Save(zip,sheetPath,sheet);
                        return;
                    }
                }
                throw new InvalidDataException("验证样本缺少配置表："+tableName);
            }
        }

        private static int Column(string cell)
        {
            int result=0;
            foreach(char letter in cell.TakeWhile(char.IsLetter))result=result*26+letter-'A'+1;
            return result;
        }

        private static byte[] WithExtraTemplate(byte[] source,string id="watch_mage",string profession="mage",int defense=1)
        {
            return EditTable(source,"cfg_unit_template",(table,sheet)=>
            {
                string oldRange=(string)table.Root.Attribute("ref");
                int n=int.Parse(new string(oldRange.Split(':')[1].SkipWhile(char.IsLetter).ToArray()))+1;
                string newRange=oldRange.Split(':')[0]+":"+new string(oldRange.Split(':')[1].TakeWhile(char.IsLetter).ToArray())+n;
                table.Root.SetAttributeValue("ref",newRange);
                table.Root.Element(Main+"autoFilter")?.SetAttributeValue("ref",newRange);
                var editable=new[]{id,"验证队长",profession,"captain_normal","0","321","12",defense.ToString(),"4"};
                var row=new XElement(Main+"row",new XAttribute("r",n));
                // Honor the authored field order when a designer reorders table columns.
                string[] fields={"template_id","name","class_id","identity","is_default","hp","atk","def","sight"};
                var headings=table.Root.Descendants(Main+"tableColumn").Select(x=>(string)x.Attribute("name")).ToArray();
                int firstColumn=Column(oldRange);
                for(int col=0;col<headings.Length;col++)
                {
                    int i=Array.IndexOf(fields,headings[col]);if(i<0)continue;
                    int k=firstColumn+col;string letters="";
                    while(k>0){k--;letters=(char)('A'+k%26)+letters;k/=26;}
                    var cell=new XElement(Main+"c",new XAttribute("r",letters+n));
                    if(i<4){cell.SetAttributeValue("t","inlineStr");cell.Add(new XElement(Main+"is",new XElement(Main+"t",editable[i])));}
                    else{if(i==4)cell.SetAttributeValue("t","b");cell.Add(new XElement(Main+"v",editable[i]));}
                    row.Add(cell);
                }
                var data=sheet.Descendants(Main+"sheetData").Single();
                var oldRow=data.Elements(Main+"row").FirstOrDefault(x=>(int)x.Attribute("r")==n);
                if(oldRow==null)data.Add(row);else oldRow.ReplaceWith(row);
            });
        }

        private static byte[] WithEncounter(byte[] source,string scene,string slot)
        {
            return EditTable(source,"cfg_encounter_unit",(table,sheet)=>
            {
                string headerAddress=((string)table.Root.Attribute("ref")).Split(':')[0];
                int header=int.Parse(new string(headerAddress.SkipWhile(char.IsLetter).ToArray()));
                var rows=sheet.Descendants(Main+"sheetData").Single().Elements(Main+"row");
                var cells=rows.Single(r=>(int)r.Attribute("r")==header+1).Elements(Main+"c")
                    .ToDictionary(c=>Column((string)c.Attribute("r")));
                int firstColumn=Column(headerAddress);
                var headings=table.Root.Descendants(Main+"tableColumn").Select(x=>(string)x.Attribute("name")).ToArray();
                foreach(var field in new[]{("scene_id",scene),("slot",slot)})
                {
                    var cell=cells[firstColumn+Array.IndexOf(headings,field.Item1)];
                    cell.SetAttributeValue("t","inlineStr");
                    cell.ReplaceNodes(new XElement(Main+"is",new XElement(Main+"t",field.Item2)));
                }
            });
        }
        private static void ExpectInvalid(byte[] sample,RpgRules baseline,string field,string table="cfg_unit_template")
        {
            try{ProfessionBalanceImporter.ReadRules(sample,baseline);}
            catch(InvalidDataException error)
            {
                Check(error.Message.Contains(table)&&error.Message.Contains(field),"坏行未定位至该表及字段："+error.Message);
                return;
            }
            throw new InvalidOperationException("坏行通过校验："+field);
        }
        private static void VerifyPreservedFields(string source,string result)
        {
            var original=CoreJsonSpans.Parse(source);
            var actual=CoreJsonSpans.Parse(result);
            var owned=new[]{"Professions","EncounterUnits","TemplateDefenseCaps","MageFood","ArcherFood","OpeningEconomy/SoldierFood","OpeningEconomy/CavalryFood","Hit","Evasion","Range","Speed",
                "TroopBaseline","RecruitBasePrices","TrainingDays","TrainingBooks","Templates","Skills","Equipment","ArmorK","ArmorCap","RuinsFirstControllerTemplate"};
            foreach(string key in original.Keys.Concat(actual.Keys).Distinct())
            {
                if(key=="/WorkbookSha256"||owned.Any(field=>key=="/Rules/"+field||key.StartsWith("/Rules/"+field+"/",StringComparison.Ordinal)))continue;
                Check(original.TryGetValue(key,out var left)&&actual.ContainsKey(key),"非职业字段增减："+key);
                if(left.Kind=='{'||left.Kind=='[')continue;
                var right=actual[key];
                Check(source.Substring(left.Start,left.Length)==result.Substring(right.Start,right.Length),"非职业字段被改写："+key);
            }
        }
        private static void VerifyDefenseMapping(byte[] workbook,RpgRules baseline)
        {
            const string variant="blackcrow_watch_mage";
            const string slot="ranged-2";
            var rules=ProfessionBalanceImporter.ReadRules(WithEncounter(workbook,"defense",slot),baseline);
            var template=rules.Templates.Single(t=>t.Id==variant);
            string file=Path.Combine(Application.streamingAssetsPath,"BaseScenarios",BaseDemo.BalanceTemplate);
            var state=DemoStageSaveTools.Defense(File.ReadAllText(file),"male",rules);
            var actual=state.Demo.Encounters.Defense.Units.Single(u=>u.TemplateId==variant);
            var shipped=JsonUtility.FromJson<BaseState>(File.ReadAllText("Assets/Resources/DemoStages/defense-male.json"));
            var source=shipped.Demo.Encounters.Defense.Units.Single(u=>u.Id==slot);
            Check(actual.Kind==SoldierKind.Mage&&actual.Cell==source.Cell&&actual.CommanderSlot==source.CommanderSlot,
                "首防换模板改动了原岗位站位或所属队长");
            Check(state.Demo.Encounters.Ruins.Melee==2&&state.Demo.Encounters.Ruins.Commanders.Single().Profession==SoldierKind.Mage,
                "首防换模板影响了古窟固定编制");
            Check(shipped.Demo.Encounters.Defense.Mage==0&&shipped.Demo.Encounters.Defense.Units.All(u=>string.IsNullOrEmpty(u.TemplateId)),
                "测试映射意外写入默认首防编制");
            var fight=BaseDemoDefense.Begin(state,"none");
            BattleScenePlacement.Start(state,fight);
            var live=new RealtimeCampaignBattle(fight);BattleScenePlacement.Snap(state,live);BaseDemoDefense.Configure(live);
            int index=fight.Opening.Enemies.FindIndex(e=>e.Rpg?.TemplateId==variant);
            var mage=index<0?null:fight.UnitForEnemy(index);
            Check(mage!=null&&mage.Kind==SoldierKind.Mage&&live.Battle.UnitName(mage).Contains(template.Name)&&
                mage.Stats.Health==template.Health&&mage.Stats.Damage==template.Attack&&mage.Stats.Defense==template.Defense,
                "实际首防没有从配置生成指定普通兵及属性");
            ExpectInvalid(WithEncounter(workbook,"defense","ranged-3"),baseline,"slot","cfg_encounter_unit");
            ExpectInvalid(WithEncounter(workbook,"defense","commander-1"),baseline,"slot","cfg_encounter_unit");
            ExpectInvalid(WithEncounter(workbook,"ruins","melee-1"),baseline,"scene_id","cfg_encounter_unit");
            rules.EncounterUnits.Single(e=>e.Scene=="defense"&&e.Slot==slot).Slot="ranged-3";
            bool rejected=false;
            try{rules.Validate();}catch(ArgumentException){rejected=true;}
            Check(rejected,"运行快照接受了首防不存在的普通兵槽");
            Debug.Log("[P52Encounters] PASS · defense/ranged-2 按配置生成 "+template.Name+
                "，保持站位与队属；拒绝别关槽、剧情队长、古窟和损坏的快照引用。");
        }

        private static void VerifyBlackCrow(RpgRules patched)
        {
            const string id="blackcrow_watch_mage";
            var entry=patched.Templates.Single(t=>t.Id==id);
            Check(entry.Name=="黑鸦寨法师"&&entry.ProfessionId=="mage"&&entry.Identity=="soldier"&&!entry.IsDefault,"旧六页导入丢失黑鸦寨普通兵模板");
            var state=JsonUtility.FromJson<BaseState>(File.ReadAllText("Assets/Resources/DemoStages/defense-male.json"));
            BaseEconomy.Validate(state);
            var level=state.Demo.Rpg;
            Check(level.DefaultTemplateId("soldier",3)=="soldier_mage"&&
                state.Town.Recruits.All(offer=>offer.Kind!=BaseTroopKind.Mage),"城镇募兵使用了关卡变体");
            Check(state.Demo.Encounters.Camp.Units.Single(u=>u.TemplateId==id).Kind==SoldierKind.Mage,"阶段档缺少关卡模板引用");
            VerifyChenanAndRuins.ReachBlackCrowFromFirstDefense(state);
            var fight=BaseDemoFinale.Begin(state,"none");
            BattleScenePlacement.Start(state,fight);
            var run=new RealtimeCampaignBattle(fight);BattleScenePlacement.Snap(state,run);BaseDemoFinale.Configure(run);
            int enemyIndex=fight.Opening.Enemies.FindIndex(enemy=>enemy.Rpg?.TemplateId==id);
            Check(enemyIndex>=0,"黑鸦寨编制没有按关卡引用生成法师");
            var enemy=fight.Opening.Enemies[enemyIndex];
            var unit=fight.UnitForEnemy(enemyIndex);
            Check(enemy.Name==entry.Name&&unit!=null&&unit.Kind==SoldierKind.Mage&&unit.Rpg?.TemplateId==id&&
                run.Battle.UnitName(unit).Contains(entry.Name)&&unit.Stats.Health==entry.Health&&unit.Stats.Damage==entry.Attack&&unit.Stats.Defense==entry.Defense,
                "黑鸦寨战内名称、普通兵身份或实装数值与表不一致");
            Debug.Log("[P52BlackCrow] PASS · "+id+" / "+run.Battle.UnitName(unit)+" / HP "+unit.Stats.Health+" / 攻 "+unit.Stats.Damage+" / 防 "+unit.Stats.Defense);
        }
        [MenuItem("SLG/其他工具/验证/P52 职业配表与阶段档")]
        public static void RunAndGenerate()
        {
            string output=DemoBalanceImporter.OutputPath;
            var preview=ProfessionBalanceImporter.Read(ProfessionBalanceImporter.WorkbookPath,PlanningBalanceImporter.OutputPath,output);
            VerifyPreservedFields(File.ReadAllText(output),preview.SerializedSnapshot);
            byte[] input=File.ReadAllBytes(preview.WorkbookPath);
            var rules=preview.Package.Rules;
            Check(rules.Professions.Count==4&&rules.Templates.Count(t=>t.IsDefault)==12&&rules.TroopBaseline.SequenceEqual(new[]{4,4,4,4}),"职业、默认模板或兵源基准不符");
            Check(rules.TrainingDays.Length==32&&rules.TrainingBooks.Length==32&&rules.RuinsFirstControllerTemplate=="captain_awakened_mage","训练或古窟职业不符");
            Check(rules.Equipment.Single(item=>item.Id=="basic_staff").Damage==2&&rules.Professions[3].TrainingBasic=="training_mage_basic","法师物品关联不符");
            var additional=ProfessionBalanceImporter.ReadRules(WithExtraTemplate(input),rules);
            var character=BaseRpg.Character("sample",3,false,false,additional);
            Check(character.TemplateId!= "watch_mage","扩展模板意外替换默认模板");
            character.TemplateId="watch_mage";BaseRpg.ValidateCharacter(character,additional);
            Check(additional.Bare(character,1).Health==321&&additional.Templates.Count==rules.Templates.Count+1,"自定义同职模板未按 ID 解析");
            ExpectInvalid(WithExtraTemplate(input,"soldier_mage"),rules,"template_id");
            ExpectInvalid(WithExtraTemplate(input,profession:"absent_class"),rules,"class_id");
            ExpectInvalid(WithExtraTemplate(input,defense:999999),rules,"def");
            var old=DemoBalanceTableReader.ReadVerifiedTables(DemoBalanceImporter.WorkbookPath);
            Check(old.ProfessionCount==3&&old.Templates.Count==9,"旧完整导入改变原三职业");
            Check(old.Range.SequenceEqual(new double[]{1,12,2}) && rules.Range.SequenceEqual(new double[]{1,12,2,12}),
                "完整配表与职业扩展表的细格攻击距离不一致");
            var original=JsonUtility.ToJson(additional);
            var patched=DemoBalanceTableReader.ReadCoreTables(CoreBalanceImporter.WorkbookPath,additional);
            Check(patched.MatchesTemplate("watch_mage",3)&&patched.TroopBaseline[3]==4&&patched.Templates.Count==additional.Templates.Count,"六页局部导入丢失扩展字段");
            var equipment=DemoBalanceTableReader.ReadEquipmentTables(DemoBalanceImporter.WorkbookPath,additional);
            CoreBalanceImporter.Changes(additional,equipment,true);
            Check(equipment.Equipment.Any(item=>item.Id=="basic_staff")&&JsonUtility.ToJson(additional)==original,"局部导入改写了基底或丢失法杖");
            Check(preview.OutputHash==PlanningBalanceImporter.Hash(File.ReadAllBytes(output)),"预览写入运行快照");
            DemoBalanceImporter.WriteWithStages(preview);
            var package=JsonUtility.FromJson<DemoBalancePackage>(File.ReadAllText(output));package.Validate();
            var again=ProfessionBalanceImporter.Read(preview.WorkbookPath,preview.SeedPath,output);
            Check(again.EffectiveHash==package.WorkbookSha256&&again.EffectiveHash==preview.EffectiveHash,"连续导入的来源哈希不稳定");
            foreach(var id in preview.StageHashes.Keys)
            {
                var state=JsonUtility.FromJson<BaseState>(File.ReadAllText("Assets/Resources/DemoStages/"+id+".json"));
                BaseEconomy.Validate(state);
                Check(state.Demo.ConfigHash==package.WorkbookSha256&&state.Demo.Rpg.ProfessionCount==4,"阶段档回读职业或来源错误："+id);
            }
            VerifyDefenseMapping(input,rules);
            VerifyBlackCrow(patched);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            Debug.Log("[P52Workbook] PASS · 4职业 / 12默认模板+黑鸦寨普通法师 / B16 / 同职模板 / 3个坏例 / 旧完整与局部导入 / 快照与4阶段档 / 重导幂等");
        }
    }
}
#endif

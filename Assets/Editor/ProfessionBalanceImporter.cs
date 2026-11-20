using System;
using System.IO;
using System.IO.Compression;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using BaseConquest.Base;
#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
#endif

namespace BaseConquest.Editor
{
    public static class ProfessionBalanceImporter
    {
        public const string WorkbookPath="../归档/文档/2026-11-20_旧数值配表/DEMO职业扩展表.xlsx";
        private static readonly string[] Classes={"warrior","archer","knight","mage"};
        private static readonly string[] Identities={"soldier","captain_normal","captain_awakened"};
        private static readonly string[] Weapons={"sword","bow","spear","staff"};
        private static string BookStem(string profession,string weapon)=>profession=="mage"?"mage":weapon;

        private static Dictionary<string,ProfessionWorkbook.Row> Unique(ProfessionWorkbook data,string table,string key,string fields)
        {
            var dict=new Dictionary<string,ProfessionWorkbook.Row>(StringComparer.Ordinal);
            foreach(var row in data.Rows(table,fields))
            {
                var id=row.Key(key);
                if(dict.ContainsKey(id))throw row.Error(key,"重复 ID："+id);
                dict.Add(id,row);
            }
            return dict;
        }
        private static void Required(Dictionary<string,ProfessionWorkbook.Row> rows,string key,string region)
        { if(!rows.ContainsKey(key))throw new InvalidDataException(region+"：缺少 "+key); }

        public static RpgRules ReadRules(byte[] bytes,RpgRules baseline)
        {
            if(bytes.Length>16*1024*1024)throw new InvalidDataException("配表超过16MB");
            using var stream=new MemoryStream(bytes,false);
            using var archive=new ZipArchive(stream,ZipArchiveMode.Read);
            var data=new ProfessionWorkbook(archive);
            var rules=baseline.Copy();
            var classes=Unique(data,"cfg_class","class_id","class_id name attack_mode damage_type weapon basic_book advanced_book hit evasion attack_range move_speed daily_food");
            var recruit=Unique(data,"cfg_recruit","class_id","class_id baseline base_price");
            var caps=Unique(data,"cfg_unit_identity","identity","identity max_base_def");
            var templates=Unique(data,"cfg_unit_template","template_id","template_id name class_id identity is_default hp atk def sight");
            var damage=Unique(data,"cfg_skill_damage","skill_id","skill_id damage_type");
            var tuning=Unique(data,"cfg_battle_param","param_id","param_id value");
            var training=data.Rows("cfg_training","class_id target_level days book_id books");
            foreach(var row in classes.Values)row.OneOf("class_id",Classes);
            foreach(var row in recruit.Values)row.OneOf("class_id",Classes);
            foreach(var row in caps.Values)row.OneOf("identity",Identities);
            foreach(var row in damage.Values)row.OneOf("skill_id",rules.Skills.Select(s=>s.Id).ToArray());
            foreach(var row in tuning.Values)row.OneOf("param_id","armor_k","armor_cap");
            rules.Professions=new List<RpgProfessionDefinition>();
            rules.Hit=new double[Classes.Length];rules.Evasion=new double[Classes.Length];
            rules.Range=new double[Classes.Length];rules.Speed=new double[Classes.Length];
            rules.TroopBaseline=new int[Classes.Length];rules.RecruitBasePrices=new int[Classes.Length];
            rules.TrainingDays=new int[Classes.Length*8];rules.TrainingBooks=new int[Classes.Length*8];
            rules.TemplateDefenseCaps=new double[Identities.Length];
            for(int i=0;i<Classes.Length;i++)
            {
                string id=Classes[i];Required(classes,id,"cfg_class · class_id");Required(recruit,id,"cfg_recruit · class_id");
                var row=classes[id];
                var weapon=row.OneOf("weapon",Weapons[i]);
                var profession=new RpgProfessionDefinition{Id=id,Name=row.TextField("name"),Weapon=weapon,
                    AttackMode=row.OneOf("attack_mode","melee","ranged"),DamageType=row.OneOf("damage_type","physical","magic")};
                string stem=BookStem(id,weapon);
                profession.TrainingBasic=row.OneOf("basic_book","training_"+stem+"_basic");
                profession.TrainingAdvanced=row.OneOf("advanced_book","training_"+stem+"_advanced");
                rules.Professions.Add(profession);
                rules.Hit[i]=row.Int("hit",0,10);rules.Evasion[i]=row.Int("evasion",0,10);
                rules.Range[i]=row.Number("attack_range",0.01,24);
                rules.Speed[i]=row.Number("move_speed",0.01,24);
                int food=row.Int("daily_food",1,100);
                if(i==0)rules.OpeningEconomy.SoldierFood=food;
                else if(i==1)rules.ArcherFood=food;
                else if(i==2)rules.OpeningEconomy.CavalryFood=food;
                else rules.MageFood=food;
                rules.TroopBaseline[i]=recruit[id].Int("baseline",1,10000);
                rules.RecruitBasePrices[i]=recruit[id].Int("base_price",1,1000000);
            }
            for(int i=0;i<Identities.Length;i++)
            {
                Required(caps,Identities[i],"cfg_unit_identity · identity");
                rules.TemplateDefenseCaps[i]=caps[Identities[i]].Number("max_base_def",0,1000000);
            }
            rules.Templates=new List<RpgTemplate>();
            var defaults=new HashSet<string>();
            foreach(var entry in templates)
            {
                var r=entry.Value;
                string profession=r.OneOf("class_id",Classes),identity=r.OneOf("identity",Identities);
                var t=new RpgTemplate{Id=entry.Key,Name=r.TextField("name"),ProfessionId=profession,Identity=identity,IsDefault=r.Boolean("is_default"),
                    Health=r.Number("hp",1,1000000),Attack=r.Number("atk",0,1000000),
                    Defense=r.Number("def",0,rules.TemplateDefenseCaps[Array.IndexOf(Identities,identity)]),SightRadius=r.Int("sight",0,32)};
                if(t.IsDefault)
                {
                    string primary=identity+"_"+profession;
                    if(t.Id!=primary||!defaults.Add(primary))throw r.Error("template_id","该职业和身份须有唯一的默认模板 "+primary);
                }
                if(t.Id=="soldier_warrior"&&t.Attack<.001)throw r.Error("atk","战力参照必须大于零");
                rules.Templates.Add(t);
            }
            foreach(string identity in Identities)foreach(string profession in Classes)
                if(!defaults.Contains(identity+"_"+profession))throw new InvalidDataException("cfg_unit_template · is_default：缺少 "+identity+"_"+profession);
            rules.EncounterUnits=new List<RpgEncounterUnit>();
            var encounterSlots=new HashSet<string>();
            foreach(var row in data.Rows("cfg_encounter_unit","scene_id slot template_id"))
            {
                string scene=row.Key("scene_id");
                if(!BaseDemoIntro.HasEditableEncounter(scene))throw row.Error("scene_id","该关卡未开放普通兵编制："+scene);
                string slot=row.TextField("slot");
                if(!BaseDemoIntro.HasEditableSoldierSlot(scene,slot))throw row.Error("slot","该关卡没有该普通兵位置："+scene+"/"+slot);
                if(!encounterSlots.Add(scene+"/"+slot))throw row.Error("slot","关卡位置重复："+slot);
                string template=row.Key("template_id");
                if(!rules.Templates.Any(t=>t.Id==template&&t.Identity=="soldier"))throw row.Error("template_id","须引用已登记的普通兵模板："+template);
                rules.EncounterUnits.Add(new RpgEncounterUnit{Scene=scene,Slot=slot,TemplateId=template});
            }
            if(data.HasTable("cfg_enemy_supply"))
            {
                rules.EnemySupplies=new List<RpgEnemySupply>();
                foreach(var row in data.Rows("cfg_enemy_supply","scene_id slot class_id count supply_tier"))
                    rules.EnemySupplies.Add(new RpgEnemySupply {Scene=row.Key("scene_id"),Slot=row.Values.TryGetValue("slot",out var slot)?slot:"",
                        Profession=row.Values.TryGetValue("class_id",out var profession)?profession:"",Count=row.Int("count",1,999),Tier=row.Int("supply_tier",0,2)});
            }
            foreach(var row in training)
            {
                var id=row.OneOf("class_id",Classes);int cls=Array.IndexOf(Classes,id),level=row.Int("target_level",2,9),n=cls*8+level-2;
                if(rules.TrainingDays[n]!=0)throw row.Error("target_level","复合主键重复："+id+" / "+level);
                string book=level%3==0?rules.Professions[cls].TrainingAdvanced:rules.Professions[cls].TrainingBasic;
                row.OneOf("book_id",book);rules.TrainingDays[n]=row.Int("days",1,365);rules.TrainingBooks[n]=row.Int("books",1,999);
            }
            for(int i=0;i<rules.TrainingDays.Length;i++)
                if(rules.TrainingDays[i]==0)throw new InvalidDataException("cfg_training · target_level：缺少 "+Classes[i/8]+" / "+(2+i%8));
            foreach(var skill in rules.Skills)
            {
                Required(damage,skill.Id,"cfg_skill_damage · skill_id");
                skill.DamageType=skill.Id=="sweep"||skill.Id=="charge"
                    ? damage[skill.Id].OneOf("damage_type","physical","magic") : damage[skill.Id].OneOf("damage_type","none");
            }
            foreach(var key in new[]{"armor_k","armor_cap"})Required(tuning,key,"cfg_battle_param · param_id");
            rules.ArmorK=tuning["armor_k"].Number("value",0.000001,1000000);
            rules.ArmorCap=tuning["armor_cap"].Number("value",0,0.99999999);

            rules.RuinsFirstControllerTemplate="captain_awakened_mage";
            var weapons=Unique(data,"cfg_weapon","item_id","item_id damage");
            foreach(var row in weapons.Values)row.OneOf("item_id","basic_staff");
            Required(weapons,"basic_staff","cfg_weapon · item_id");
            var staff=rules.Equipment.SingleOrDefault(item=>item.Id=="basic_staff");
            if(staff==null)
            {
                staff=rules.Equipment.Single(item=>item.Id=="basic_bow").Copy();
                staff.Id="basic_staff";
                rules.Equipment.Add(staff);
            }
            staff.Damage=(float)weapons[staff.Id].Number("damage",-10000,10000);
            rules.Validate();
            return rules;
        }

#if UNITY_EDITOR
        // Replace only fields owned by these tables. Unknown and legacy fields in the active snapshot survive byte-for-byte.
        private static string MergeSnapshot(string original,DemoBalancePackage package)
        {
            var before=CoreJsonSpans.Parse(original);
            var serialized=JsonUtility.ToJson(package,true);
            var after=CoreJsonSpans.Parse(serialized);
            var paths=new[]{"Professions","EncounterUnits","EnemySupplies","TemplateDefenseCaps","MageFood","ArcherFood","OpeningEconomy/SoldierFood","OpeningEconomy/CavalryFood",
                "Hit","Evasion","Range","Speed","TroopBaseline","RecruitBasePrices","TrainingDays","TrainingBooks","Templates","Skills","Equipment","ArmorK","ArmorCap","RuinsFirstControllerTemplate"};
            var edits=new List<(int position,int length,string text)>();
            var additions=new StringBuilder();
            foreach(string path in new[]{"/WorkbookSha256"}.Concat(paths.Select(name=>"/Rules/"+name)))
            {
                var span=after[path];string value=serialized.Substring(span.Start,span.Length);
                if(before.TryGetValue(path,out var prior))edits.Add((prior.Start,prior.Length,value));
                else
                {
                    string name=path.Substring("/Rules/".Length);
                    if(name.Contains("/"))throw new InvalidDataException("原快照缺少配置："+path);
                    additions.Append(",\n        \"").Append(name).Append("\": ").Append(value);
                }
            }
            if(additions.Length>0){var rules=before["/Rules"];edits.Add((rules.Start+rules.Length-1,0,additions.ToString()));}
            var text=new StringBuilder(original);
            foreach(var edit in edits.OrderByDescending(x=>x.position))text.Remove(edit.position,edit.length).Insert(edit.position,edit.text);
            return text.ToString();
        }
        public static DemoBalanceImport Read(string workbook,string seed,string output)
        {
            workbook=Path.GetFullPath(workbook);seed=Path.GetFullPath(seed);output=Path.GetFullPath(output);
            byte[] data=File.ReadAllBytes(workbook),source=File.ReadAllBytes(output),seedBytes=File.ReadAllBytes(seed);
            var baselineJson=Encoding.UTF8.GetString(source).TrimStart('\ufeff');
            var baseline=JsonUtility.FromJson<DemoBalancePackage>(baselineJson);
            baseline.Validate();
            var rules=ReadRules(data,baseline.Rules);
            string hash=PlanningBalanceImporter.Hash(data);
            string effective=PlanningBalanceImporter.Hash(Encoding.UTF8.GetBytes("P52/1\n"+hash+"\n"+JsonUtility.ToJson(rules)));
            var package=new DemoBalancePackage {WorkbookSha256=effective,Rules=rules};package.Validate();
            var imports=new DemoBalanceImport{Package=package,WorkbookPath=workbook,WorkbookHash=hash,ConfigurationHash=effective,
                SeedPath=seed,SeedHash=PlanningBalanceImporter.Hash(seedBytes),OutputPath=output,OutputHash=PlanningBalanceImporter.Hash(source),
                SerializedSnapshot=MergeSnapshot(baselineJson,package),StageHashes=new Dictionary<string,string>()};
            foreach(var key in new[]{"camp-male","camp-female","defense-male","defense-female"})
            {
                string p=Path.GetFullPath("Assets/Resources/DemoStages/"+key+".json");
                imports.StageHashes.Add(key,File.Exists(p)?PlanningBalanceImporter.Hash(File.ReadAllBytes(p)):null);
            }
            imports.Preview="变更来源："+workbook+"\n职业 "+rules.Professions.Count+" · 模板 "+rules.Templates.Count+" · 兵源基准 "+rules.TroopBaseline.Sum()+"\n"+
                string.Join("\n",rules.Templates.Select(t=>t.Id+"（"+t.Name+"）  生命 "+t.Health+" / 攻 "+t.Attack+" / 防 "+t.Defense+" / 视野 "+t.SightRadius))+
                "\n关卡逐兵引用 "+string.Join(" / ",rules.EncounterUnits.Select(e=>e.Scene+"·"+e.Slot+" → "+e.TemplateId))+"\n训练 "+rules.TrainingDays.Length+" 项 · 技能伤害 "+rules.Skills.Count+" 项 · 物理防御上限 "+string.Join(" / ",rules.TemplateDefenseCaps)+
                "\n职业扩展区覆盖对应字段，其余规则沿用当前运行快照。确认后同步四个阶段档。";
            return imports;
        }
        public static void Generate()
        {
            var result=Read(WorkbookPath,PlanningBalanceImporter.OutputPath,DemoBalanceImporter.OutputPath);
            DemoBalanceImporter.WriteWithStages(result);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            Debug.Log("[P52Import] PASS  "+result.Preview);
        }
#endif
    }
}

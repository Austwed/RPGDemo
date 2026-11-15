using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using BaseConquest.Base;

namespace BaseConquest.Editor
{
    public static partial class DemoBalanceTableReader
    {
        internal static readonly string[] CoreSheetNames={"02a 全局参数与试算","03b 九类模板","03c 招募与招降","06b 军需与配方","07a 经营参数","09d 奖励与遗迹"};

        public static RpgRules ReadCoreTables(string path,RpgRules baseline)
        {
            if(baseline==null)throw new InvalidOperationException("六页导入需要当前有效的Demo数值快照");
            baseline.Validate();
            if(baseline.OpeningEconomyVersion!=1||baseline.OpeningAuxVersion!=1||baseline.OpeningBattleRewardVersion!=1||baseline.OpeningSupplyWearVersion!=1||baseline.OpeningLordReward==null)
                throw new InvalidOperationException("当前快照缺少已接入的经营、磨损或奖励规则，不能局部覆盖");
            byte[] bytes=File.ReadAllBytes(path);
            if(bytes.Length>16*1024*1024)throw new InvalidOperationException("配表超过16MB");
            var rules=baseline.Copy();
            using(var archive=new ZipArchive(new MemoryStream(bytes),ZipArchiveMode.Read))
            {
                if(archive.Entries.Count>1024||archive.Entries.Sum(e=>e.Length)>64L*1024*1024)throw new InvalidOperationException("XLSX解压体积或条目数越界");
                var workbook=archive.GetEntry("xl/workbook.xml");
                if(workbook==null)throw new InvalidOperationException("XLSX缺少xl/workbook.xml");
                XDocument document;using(var stream=workbook.Open())document=XDocument.Load(stream);
                XNamespace ns="http://schemas.openxmlformats.org/spreadsheetml/2006/main";
                var names=document.Descendants(ns+"sheet").Select(e=>(string)e.Attribute("name")).ToArray();
                if(names.Length!=CoreSheetNames.Length||names.Distinct(StringComparer.Ordinal).Count()!=CoreSheetNames.Length||CoreSheetNames.Any(n=>!names.Contains(n)))
                    throw new InvalidOperationException("独立表必须且只能包含六页："+string.Join("、",CoreSheetNames));
                ApplyCoreTemplates(archive,rules);
                ApplyCoreRecruitment(archive,rules);
                ApplyCoreSupplies(archive,rules);
                ApplyCoreEconomy(archive,rules);
                ApplyCoreRewards(archive,rules);
                ApplyCoreGlobal(archive,rules);
            }
            if(rules.BaseInterval<rules.MinimumInterval)throw new InvalidOperationException("02a 全局参数与试算：基础前摇不能低于最小间隔");
            double maxAttackSpeed=rules.Traits.Single(t=>t.Id=="frenzy").Max/100+rules.Skills.Single(s=>s.Id=="rapid_fire").Amount;
            if(rules.BaseInterval/(1+maxAttackSpeed)<rules.MinimumInterval)throw new InvalidOperationException("当前连射与乱武规则和02a攻击间隔下限冲突，请调整六页表中的间隔");
            if(rules.SweepSecondsAtParity<rules.SweepMinimumSeconds)throw new InvalidOperationException("02a 全局参数与试算：扫荡基础时间不能低于最小时间");
            try{rules.Validate();}catch(ArgumentException error){throw new InvalidOperationException(error.Message,error);}
            CoreBalanceImporter.Changes(baseline,rules);
            return rules;
        }

        private static void ApplyCoreTemplates(ZipArchive archive,RpgRules rules)
        {
            const string templates="03b 九类模板";
            string[] ids={"saber","archer","rider"};var seen=new HashSet<string>();
            var t=BattleBalanceImporter.Section(archive,templates,6,15,"ADEFGP");
            Header(t,templates,"A6","模板ID");Header(t,templates,"D6","职业ID");Header(t,templates,"E6","1级生命");Header(t,templates,"F6","1级攻击");Header(t,templates,"G6","1级防御");Header(t,templates,"P6","启用");
            seen.Clear();
            for(int row=7;row<=15;row++)
            {
                string id=Need(t,templates,"A"+row),profession=Need(t,templates,"D"+row);int index=Array.IndexOf(ids,profession);
                if(index<0||!id.EndsWith("_"+profession,StringComparison.Ordinal)||!seen.Add(id))throw new InvalidOperationException(templates+"!A"+row+"：模板ID或职业引用无效或重复："+id);
                if(Need(t,templates,"P"+row)!="1"||!t.TryGetValue("#type:P"+row,out var enabledType)||enabledType!="b")throw new InvalidOperationException(templates+"!P"+row+"：游戏必需模板须启用（布尔值）");
                string runtime=id.Substring(0,id.Length-profession.Length)+new[]{"warrior","archer","knight"}[index];
                var item=rules.Templates.SingleOrDefault(x=>x.Id==runtime);
                if(item==null)throw new InvalidOperationException(templates+"!A"+row+"：未支持的模板ID："+id);
                item.Health=Number(t,templates,"E"+row,1,1000000);item.Attack=Number(t,templates,"F"+row,0,1000000);item.Defense=Number(t,templates,"G"+row,0,1000000);
                if(runtime=="soldier_warrior"&&item.Attack<.001)throw new InvalidOperationException(templates+"!F"+row+"：剑士是战力公式的参照，攻击须至少为0.001；其他模板可为0");
            }
        }

        private static void ApplyCoreRecruitment(ZipArchive archive,RpgRules rules)
        {
            string[] ids={"saber","archer","rider"};var seen=new HashSet<string>();
            const string recruiting="03c 招募与招降";
            var rec=BattleBalanceImporter.Section(archive,recruiting,6,9,"ABCDHJ");
            Header(rec,recruiting,"A6","招募规则ID");Header(rec,recruiting,"B6","兵种模板ID");Header(rec,recruiting,"C6","基准B");Header(rec,recruiting,"D6","基础单价");Header(rec,recruiting,"H6","当天不生产");Header(rec,recruiting,"J6","启用");
            seen.Clear();
            for(int row=7;row<=9;row++)
            {
                string id=Need(rec,recruiting,"A"+row);int index=Array.IndexOf(ids,id.StartsWith("recruit_")?id.Substring(8):"");
                if(index<0||!seen.Add(id)||Need(rec,recruiting,"B"+row)!="soldier_"+ids[index])throw new InvalidOperationException(recruiting+"!A"+row+"/B"+row+"：招募ID或模板引用无效");
                double baseline=Number(rec,recruiting,"C"+row,1,10000),price=Number(rec,recruiting,"D"+row,1,1000000);
                if(baseline!=Math.Truncate(baseline)||price!=Math.Truncate(price))throw new InvalidOperationException(recruiting+" 第"+row+"行：兵力基准及单价须为整数");
                if(Need(rec,recruiting,"H"+row)!="0"||!rec.TryGetValue("#type:H"+row,out var canWorkType)||canWorkType!="b")throw new InvalidOperationException(recruiting+"!H"+row+"：新兵当天可工作，应填写布尔否");
                if(Need(rec,recruiting,"J"+row)!="1"||!rec.TryGetValue("#type:J"+row,out var enabledType)||enabledType!="b")throw new InvalidOperationException(recruiting+"!J"+row+"：当前招募须启用");
                rules.TroopBaseline[index]=(int)baseline;rules.RecruitBasePrices[index]=(int)price;
            }
            var recruitFactors=BattleBalanceImporter.Section(archive,recruiting,20,22,"ACD");
            Header(recruitFactors,recruiting,"A20","参数ID");Header(recruitFactors,recruiting,"C20","数值");Header(recruitFactors,recruiting,"D20","单位");
            seen.Clear();
            for(int row=21;row<=22;row++)
            {
                string id=Need(recruitFactors,recruiting,"A"+row);
                if(!seen.Add(id))throw new InvalidOperationException(recruiting+"!A"+row+"：参数ID重复");
                if(Need(recruitFactors,recruiting,"D"+row)!="倍率")throw new InvalidOperationException(recruiting+"!D"+row+"：单位应为倍率");
                if(id=="recruit_supply_factor")rules.RecruitSupplyFactor=Number(recruitFactors,recruiting,"C"+row,0,1);
                else if(id=="recruit_price_sensitivity")rules.RecruitPriceSensitivity=Number(recruitFactors,recruiting,"C"+row,0,10);
                else throw new InvalidOperationException(recruiting+"!A"+row+"：参数ID无效");
            }
        }

        private static void ApplyCoreSupplies(ZipArchive archive,RpgRules rules)
        {
            const string supplies="06b 军需与配方";
            var sup=BattleBalanceImporter.Section(archive,supplies,6,8,"ABDEH");
            Header(sup,supplies,"A6","物品ID");Header(sup,supplies,"B6","等级");Header(sup,supplies,"D6","攻击加值");Header(sup,supplies,"E6","防御加值");Header(sup,supplies,"H6","启用");
            for(int level=1;level<=2;level++)
            {
                int row=level+6;if(Need(sup,supplies,"A"+row)!="supply"+level||Number(sup,supplies,"B"+row,level,level)!=level)throw new InvalidOperationException(supplies+" 第"+row+"行：应为通用军需"+level+"级");
                double atk=Number(sup,supplies,"D"+row,0,1000),def=Number(sup,supplies,"E"+row,0,1000);
                if(Need(sup,supplies,"H"+row)!="1"||!sup.TryGetValue("#type:H"+row,out var enabledType)||enabledType!="b")throw new InvalidOperationException(supplies+"!H"+row+"：军需必须启用");
                if(level==1){rules.SupplyAttackLow=atk;rules.SupplyDefenseLow=def;}else{rules.SupplyAttackHigh=atk;rules.SupplyDefenseHigh=def;}
            }
            var recipe=BattleBalanceImporter.Section(archive,supplies,17,19,"ABCDE");
            Header(recipe,supplies,"A17","配方ID");Header(recipe,supplies,"B17","产物ID");Header(recipe,supplies,"C17","产物数量");Header(recipe,supplies,"D17","工作量");Header(recipe,supplies,"E17","所需工坊等级");
            var material=BattleBalanceImporter.Section(archive,supplies,28,30,"ABC");
            Header(material,supplies,"A28","配方ID");Header(material,supplies,"B28","原料ID");Header(material,supplies,"C28","数量");
            int unitMaterial=0;
            for(int level=1;level<=2;level++)
            {
                int rr=level+17,mr=level+28;string id="recipe_supply"+level;
                if(Need(recipe,supplies,"A"+rr)!=id||Need(recipe,supplies,"B"+rr)!="supply"+level||Number(recipe,supplies,"C"+rr,1,1)!=1||Number(recipe,supplies,"D"+rr,1,99)<1||Number(recipe,supplies,"E"+rr,1,2)!=level)throw new InvalidOperationException(supplies+" 第"+rr+"行：产量、工作量或所需工坊等级不符合生产规则");
                if(Need(material,supplies,"A"+mr)!=id||Need(material,supplies,"B"+mr)!="materials")throw new InvalidOperationException(supplies+" 第"+mr+"行：配方材料引用无效");
                double amount=Number(material,supplies,"C"+mr,1,100);if(amount!=Math.Truncate(amount))throw new InvalidOperationException(supplies+"!C"+mr+"：原料数量须为整数");
                if(level==1)unitMaterial=(int)amount;else if(amount<unitMaterial*2)throw new InvalidOperationException(supplies+"!C"+mr+"：二级军需原料数不得少于一级的两倍");
            }
            rules.SupplyMaterial=unitMaterial;
        }

        private static void ApplyCoreEconomy(ZipArchive archive,RpgRules rules)
        {
            var seen=new HashSet<string>();
            const string economy="07a 经营参数";
            var eco=BattleBalanceImporter.Section(archive,economy,6,15,"ACDG");
            Header(eco,economy,"A6","参数ID");Header(eco,economy,"C6","值");Header(eco,economy,"D6","类型");Header(eco,economy,"G6","启用");
            var opening=rules.OpeningEconomy.Copy();double recovery=0;
            var economyKeys=new Dictionary<string,(string type,Action<double> apply,double min,double max)>{
                {"food_saber",("INT",x=>opening.SoldierFood=(int)x,1,100)},
                {"food_archer",("INT",x=>rules.ArcherFood=(int)x,1,100)},
                {"food_rider",("INT",x=>opening.CavalryFood=(int)x,1,100)},
                {"food_captain",("INT",x=>{rules.LeaderFood=(int)x;opening.LeaderFood=(int)x;},1,100)},
                {"farm_output_base",("NUM",x=>opening.FarmPerWorker=x,.0001,100)},
                {"workshop_workpoint_base",("NUM",x=>opening.WorkshopPerWorker=x,.0001,100)},
                {"hunger_production_penalty_max",("RATE",x=>opening.HungerProductionPenalty=x,0,.999999)},
                {"hunger_attack_penalty_max",("RATE",x=>opening.HungerAttackPenalty=x,0,.999999)},
                {"recovery_per_day",("NUM",x=>recovery=x,.0001,10000)}
            };
            seen.Clear();
            for(int row=7;row<=15;row++)
            {
                string id=Need(eco,economy,"A"+row);
                if(!economyKeys.TryGetValue(id,out var item)||!seen.Add(id))throw new InvalidOperationException(economy+"!A"+row+"：参数ID不存在或重复："+id);
                if(Need(eco,economy,"D"+row)!=item.type)throw new InvalidOperationException(economy+"!D"+row+"：参数类型应为"+item.type);
                if(Need(eco,economy,"G"+row)!="1"||!eco.TryGetValue("#type:G"+row,out var enabledType)||enabledType!="b")throw new InvalidOperationException(economy+"!G"+row+"：实际使用的参数须启用（布尔值）");
                double value=Number(eco,economy,"C"+row,item.min,item.max);
                if(item.type=="INT"&&value!=Math.Truncate(value))throw new InvalidOperationException(economy+"!C"+row+"：参数须为整数");item.apply(value);
            }
            rules.OpeningEconomy=opening;rules.OpeningEconomyVersion=1;rules.OpeningRecoveryPerDay=recovery;
            var camp=BattleBalanceImporter.Section(archive,economy,25,28,"ABCD");
            Header(camp,economy,"A25","营帐等级");Header(camp,economy,"B25","队长容量");Header(camp,economy,"C25","升级原料");Header(camp,economy,"D25","升级钱币");
            for(int level=1;level<=3;level++)
            {
                int row=level+25;Number(camp,economy,"A"+row,level,level);
                double capacity=Number(camp,economy,"B"+row,level==1?3:1,10000),materialsCost=Number(camp,economy,"C"+row,0,1000000),coinCost=Number(camp,economy,"D"+row,0,1000000);
                if(capacity!=Math.Truncate(capacity)||materialsCost!=Math.Truncate(materialsCost)||coinCost!=Math.Truncate(coinCost))throw new InvalidOperationException(economy+" 第"+row+"行：容量和升级费用须为整数");
                if(level==1&&(materialsCost!=0||coinCost!=0))throw new InvalidOperationException(economy+" 第26行：初始营帐不需要升级费用");
                if(level>1){rules.CampMaterials[level-2]=(int)materialsCost;rules.CampCoins[level-2]=(int)coinCost;}
                rules.Capacity[level-1]=(int)capacity;
                if(level>1&&rules.Capacity[level-1]<rules.Capacity[level-2])throw new InvalidOperationException(economy+"!B"+row+"：升级后容量不能减少");
            }
            var repairs=BattleBalanceImporter.Section(archive,economy,35,37,"ABC");
            Header(repairs,economy,"A35","设施修复参数ID");Header(repairs,economy,"B35","值");Header(repairs,economy,"C35","单位");
            var repairKeys=new Dictionary<string,(string unit,Action<int> apply)>{{"repair_materials",("原料/设施",x=>rules.FacilityRepairMaterials=x)},{"repair_coins",("钱币/设施",x=>rules.FacilityRepairCoins=x)}};
            seen.Clear();
            for(int row=36;row<=37;row++)
            {
                string id=Need(repairs,economy,"A"+row);
                if(!repairKeys.TryGetValue(id,out var entry)||!seen.Add(id))throw new InvalidOperationException(economy+"!A"+row+"：修复参数ID无效或重复");
                if(Need(repairs,economy,"C"+row)!=entry.unit)throw new InvalidOperationException(economy+"!C"+row+"：单位不匹配");
                double value=Number(repairs,economy,"B"+row,0,1000000);if(value!=Math.Truncate(value))throw new InvalidOperationException(economy+"!B"+row+"：修复费用须为整数");entry.apply((int)value);
            }
        }

        private static void ApplyCoreRewards(ZipArchive archive,RpgRules rules)
        {
            var seen=new HashSet<string>();
            const string ruins="09d 奖励与遗迹";
            var reward=BattleBalanceImporter.Section(archive,ruins,39,47,"ACD");
            Header(reward,ruins,"A39","参数ID");Header(reward,ruins,"C39","数值");Header(reward,ruins,"D39","类型");
            var rewardState=rules.OpeningRewards.Copy();
            var rewardKeys=new Dictionary<string,(string type,Action<double> apply,double min,double max)>{
                {"max_energy",("INT",x=>rewardState.MaximumEnergy=(int)x,1,10000)},
                {"reward_cost",("INT",x=>rewardState.RewardCost=(int)x,1,10000)},
                {"exposure_fraction",("RATE",x=>rewardState.ExposureFraction=x,.000001,1)},
                {"first_coins",("INT",x=>rewardState.FirstCoins=(int)x,0,1000000)},
                {"first_food",("INT",x=>rewardState.FirstFood=(int)x,0,1000000)},
                {"first_materials",("INT",x=>rewardState.FirstMaterials=(int)x,0,1000000)},
                {"repeat_coins",("INT",x=>rewardState.Coins=(int)x,0,1000000)},
                {"stone_count",("INT",x=>rewardState.ItemCount=(int)x,1,99)}
            };
            seen.Clear();var rewardRows=new Dictionary<string,int>();
            for(int row=40;row<=47;row++)
            {
                string id=Need(reward,ruins,"A"+row);
                if(!rewardKeys.TryGetValue(id,out var item)||!seen.Add(id))throw new InvalidOperationException(ruins+"!A"+row+"：参数ID不存在或重复："+id);
                rewardRows[id]=row;
                if(Need(reward,ruins,"D"+row)!=item.type)throw new InvalidOperationException(ruins+"!D"+row+"：参数类型应为"+item.type);
                double value=Number(reward,ruins,"C"+row,item.min,item.max);
                if(item.type=="INT"&&value!=Math.Truncate(value))throw new InvalidOperationException(ruins+"!C"+row+"：参数须为整数");item.apply(value);
            }
            if(rewardState.RewardCost>rewardState.MaximumEnergy)throw new InvalidOperationException(ruins+"!C"+rewardRows["reward_cost"]+"：领奖消耗不能超过体力上限");
            rewardState.Energy=rewardState.MaximumEnergy;BaseRuinsRewards.ValidateDefaults(rewardState);
            var lord=BattleBalanceImporter.Section(archive,ruins,52,55,"ACD");
            Header(lord,ruins,"A52","参数ID");Header(lord,ruins,"C52","数值");Header(lord,ruins,"D52","类型");
            var lordReward=rules.OpeningLordReward.Copy();
            var lordKeys=new Dictionary<string,Action<int>>{
                {"lord_food",v=>lordReward.Food=v},{"lord_materials",v=>lordReward.Materials=v},{"lord_coins",v=>lordReward.Coins=v}};
            seen.Clear();
            for(int row=53;row<=55;row++)
            {
                string id=Need(lord,ruins,"A"+row);
                if(!lordKeys.TryGetValue(id,out var assign)||!seen.Add(id))throw new InvalidOperationException(ruins+"!A"+row+"：参数ID不存在或重复："+id);
                if(Need(lord,ruins,"D"+row)!="INT")throw new InvalidOperationException(ruins+"!D"+row+"：参数类型应为INT");
                double value=Number(lord,ruins,"C"+row,0,1000000);
                if(value!=Math.Truncate(value))throw new InvalidOperationException(ruins+"!C"+row+"：参数须为整数");
                assign((int)value);
            }
            rules.OpeningLordReward=lordReward;
            var battleRewards=BattleBalanceImporter.Section(archive,ruins,16,16,"ACDE");
            Header(battleRewards,ruins,"A16","奖励组ID");Header(battleRewards,ruins,"C16","对象类型");Header(battleRewards,ruins,"D16","对象ID");Header(battleRewards,ruins,"E16","数量");
            // Preserve every non-resource field in these shared inventory objects.
            var defenseWin=BattleBalanceImporter.Section(archive,ruins,21,23,"ACDE");var campWin=BattleBalanceImporter.Section(archive,ruins,25,27,"ACDE");var defenseLoss=BattleBalanceImporter.Section(archive,ruins,32,34,"ACDE");
            foreach(var entry in new (int row,string group,string item,BaseInventory target)[]{(21,"reward_defense_win","food",rules.OpeningDefenseReward),(22,"reward_defense_win","materials",rules.OpeningDefenseReward),(23,"reward_defense_win","coins",rules.OpeningDefenseReward),(25,"reward_camp_win","food",rules.OpeningCampReward),(26,"reward_camp_win","materials",rules.OpeningCampReward),(27,"reward_camp_win","coins",rules.OpeningCampReward),(32,"loot_per_enemy","food",rules.OpeningDefenseLoss),(33,"loot_per_enemy","materials",rules.OpeningDefenseLoss),(34,"loot_per_enemy","coins",rules.OpeningDefenseLoss)})
            {
                int row=entry.row;battleRewards=row<=23?defenseWin:row<=27?campWin:defenseLoss;if(Need(battleRewards,ruins,"A"+row)!=entry.group||Need(battleRewards,ruins,"C"+row)!="资源"||Need(battleRewards,ruins,"D"+row)!=entry.item)throw new InvalidOperationException(ruins+" 第"+row+"行：战斗奖惩的组别或资源ID不匹配");
                double value=Number(battleRewards,ruins,"E"+row,0,1000000);if(value!=Math.Truncate(value))throw new InvalidOperationException(ruins+"!E"+row+"：战斗奖惩数量须为整数");
                if(entry.item=="food")entry.target.Food=(int)value;else if(entry.item=="materials")entry.target.Materials=(int)value;else entry.target.Coins=(int)value;
            }
            rules.OpeningBattleRewardVersion=1;
            rules.OpeningRewards=rewardState;
        }

        private static void ApplyCoreGlobal(ZipArchive archive,RpgRules rules)
        {
            var seen=new HashSet<string>();
            const string formula="02a 全局参数与试算";
            var trade=BattleBalanceImporter.Section(archive,formula,21,22,"AC");
            if(Need(trade,formula,"A21")!="goods_buy_multiplier"||Need(trade,formula,"A22")!="goods_sell_multiplier")throw new InvalidOperationException(formula+"!A21/A22：交易参数ID不匹配");
            rules.BuyFactor=Number(trade,formula,"C21",.0001,100);rules.SellFactor=Number(trade,formula,"C22",0,100);
            if(rules.BuyFactor<=rules.SellFactor)throw new InvalidOperationException(formula+"!C21/C22：购买倍率须高于出售倍率");
            var armor=BattleBalanceImporter.Section(archive,formula,34,35,"AC");
            if(Need(armor,formula,"A34")!="armor_k"||Need(armor,formula,"A35")!="armor_mitigation_cap")throw new InvalidOperationException(formula+"!A34/A35：护甲参数ID不匹配");
            rules.ArmorK=Number(armor,formula,"C34",.0001,100000);rules.ArmorCap=Number(armor,formula,"C35",0,.999999);
            var wear=BattleBalanceImporter.Section(archive,formula,46,47,"ACD");
            if(Need(wear,formula,"A46")!="supply_returned_wear"||Need(wear,formula,"A47")!="supply_lost_wear")throw new InvalidOperationException(formula+"!A46/A47：军需磨损参数ID不匹配");
            if(Need(wear,formula,"D46")!="RATE"||Need(wear,formula,"D47")!="RATE")throw new InvalidOperationException(formula+"!D46/D47：军需磨损类型须为RATE");
            rules.OpeningSupplyWearVersion=1;rules.OpeningReturnedWear=Number(wear,formula,"C46",0,1);rules.OpeningLostWear=Number(wear,formula,"C47",0,1);
            var config=BattleBalanceImporter.Section(archive,formula,75,94,"ACD");
            Header(config,formula,"A75","参数ID");Header(config,formula,"C75","参数值");Header(config,formula,"D75","单位");
            var keys=new Dictionary<string,(string unit,Action<double> apply,double min,double max)>{
                {"critical_multiplier",("倍",x=>rules.CriticalMultiplier=x,1,10)},
                {"base_interval",("秒",x=>rules.BaseInterval=x,.2,60)},
                {"minimum_interval",("秒",x=>rules.MinimumInterval=x,.2,60)},
                {"arrow_speed",("细格/秒",x=>rules.ArrowSpeed=x,.01,1000)},
                {"height_advantage",("细格高度",x=>rules.HeightAdvantage=x,.01,100)},
                {"heal_threshold",("比例",x=>rules.HealThreshold=x,.01,1)},
                {"weapon_attack_scale",("倍率",x=>rules.WeaponAttackScale=x,0,10)},
                {"equipment_attack_scale",("倍率",x=>rules.AttackEquipmentScale=x,0,10)},
                {"equipment_health_scale",("倍率",x=>rules.HealthEquipmentScale=x,0,10)},
                {"equipment_defense_scale",("倍率",x=>rules.DefenseEquipmentScale=x,0,10)},
                {"awakened_chance",("概率",x=>rules.AwakenedChance=x,0,1)},
                {"reserved_jump_skill",("停用",x=>{},0,0)},
                {"reserved_skill_weight",("停用",x=>{},0,0)},
                {"enemy_leader_death",("概率",x=>rules.EnemyLeaderDeath=x,0,1)},
                {"sweep_loss_parity",("比例",x=>rules.SweepLossAtParity=x,0,1)},
                {"sweep_seconds_parity",("秒",x=>rules.SweepSecondsAtParity=x,.01,90)},
                {"sweep_min_seconds",("秒",x=>rules.SweepMinimumSeconds=x,.01,90)},
                {"soldier_death_base",("概率",x=>rules.SoldierDeathBase=x,0,1)},
                {"soldier_death_sensitivity",("倍率",x=>rules.SoldierDeathSensitivity=x,0,10)}
            };
            seen.Clear();
            for(int row=76;row<=94;row++)
            {
                string id=Need(config,formula,"A"+row);
                if(!keys.TryGetValue(id,out var item)||!seen.Add(id))throw new InvalidOperationException(formula+"!A"+row+"：参数ID不存在或重复："+id);
                if(Need(config,formula,"D"+row)!=item.unit)throw new InvalidOperationException(formula+"!D"+row+"：参数单位应为"+item.unit);
                item.apply(Number(config,formula,"C"+row,item.min,item.max));
            }
            var hideout = BattleBalanceImporter.Section(archive, formula, 135, 135, "ACD");
            if (hideout.ContainsKey("A135"))
            {
                if (Need(hideout, formula, "A135") != "bandit_hideout_enemy_count" || Need(hideout, formula, "D135") != "人")
                    throw new InvalidOperationException(formula + "!A135:D135：土匪窝点参数或单位非法");
                double count = Number(hideout, formula, "C135", 1, Math.Min(BaseBanditHideout.EnemyCountCap, rules.TroopBaseline.Sum()));
                if (count != Math.Truncate(count)) throw new InvalidOperationException(formula + "!C135：土匪窝点人数须为整数");
                rules.HideoutEnemyCount = (int)count;
            }
        }
    }
}

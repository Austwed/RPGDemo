using System;
using System.Collections.Generic;
using System.Linq;
using BaseConquest.Battle;

namespace BaseConquest.Base
{
    [Serializable] public sealed class DemoBalancePackage
    {
        public int Version=1;public string WorkbookSha256;public RpgRules Rules;
        public void Validate()
        {
            if(Version!=1||WorkbookSha256==null||WorkbookSha256.Length!=64||WorkbookSha256.Any(c=>!"0123456789abcdef".Contains(c))||Rules==null||Rules.OpeningEconomyVersion!=1||Rules.OpeningAuxVersion!=1||Rules.Equipment?.Count!=13)throw new ArgumentException("Demo配表快照缺少版本、来源或完整规则");
            Rules.Validate();
            var expected=Enumerable.Range(0,9).Select(i=>RpgRules.Template(i<3?"soldier":i<6?"captain_normal":"captain_awakened",i%3));
            if(!expected.OrderBy(x=>x).SequenceEqual(Rules.Templates.Select(t=>t.Id).OrderBy(x=>x)))throw new ArgumentException("Demo配表快照的模板ID不完整");
            var defaults=RpgRules.Defaults();
            if(!defaults.Skills.Select(s=>s.Id).OrderBy(x=>x).SequenceEqual(Rules.Skills.Select(s=>s.Id).OrderBy(x=>x))||!defaults.Traits.Select(t=>t.Id).OrderBy(x=>x).SequenceEqual(Rules.Traits.Select(t=>t.Id).OrderBy(x=>x)))throw new ArgumentException("Demo配表快照的技能或特性ID不完整");
        }
    }
    [Serializable] public sealed class RpgTemplate
    {
        public string Id;public double Health,Attack,Defense;
        public RpgTemplate Copy()=>(RpgTemplate)MemberwiseClone();
    }
    [Serializable] public sealed class RpgSkillDefinition
    {
        public string Id,Name;public int Profession=-1;public double Amount,Duration,Cooldown,Radius,SpeedBonus,Opening=3;
        public RpgSkillDefinition Copy()=>(RpgSkillDefinition)MemberwiseClone();
    }
    [Serializable] public sealed class RpgTraitDefinition
    {
        public string Id,Name,Conflict;public double Min,Max,Step=1,Weight=1;
        public RpgTraitDefinition Copy()=>(RpgTraitDefinition)MemberwiseClone();
    }
    [Serializable] public sealed class RpgTrait
    {public string Id;public double Value;public RpgTrait Copy()=>(RpgTrait)MemberwiseClone();}
    [Serializable] public sealed class RpgStoryPreset
    {
        public string Id,SkillId,TraitId;public double TraitValue;
        public List<RpgTrait> ExtraTraits=new List<RpgTrait>();
        public RpgStoryPreset Copy(){var c=(RpgStoryPreset)MemberwiseClone();c.ExtraTraits=ExtraTraits?.Select(t=>t?.Copy()).ToList();return c;}
    }
    [Serializable] public sealed class RpgEquipmentDefinition
    {
        public string Id;public float Damage,Defense,Health,CritMultiplier,Movement;public int Stage;
        public RpgEquipmentDefinition Copy()=>(RpgEquipmentDefinition)MemberwiseClone();
    }
    [Serializable] public sealed class RpgCharacter
    {
        public int Version;
        public string Id,TemplateId,SkillId,HomeBaseId="base";public int Profession,Level=1;
        public bool Story,AutoSkill=true;public List<RpgTrait> Traits=new List<RpgTrait>();
        [NonSerialized] public RpgRules Rules;
        public double Trait(string id)=>Traits.Where(t=>t.Id==id).Sum(t=>t.Value);
        public RpgCharacter Copy(){if(Version==0)return null;var c=(RpgCharacter)MemberwiseClone();c.Traits=Traits.Select(t=>t.Copy()).ToList();return c;}
    }
    [Serializable] public sealed class RpgRules
    {
        public int GenerationSeed, SceneRulesVersion;
        public int Version,CampLevel=1,InnDailyFee=2,LeaderFood=2,ArcherFood=1,SupplyMaterial=2,SupplyWorkValue=5,FacilityRepairMaterials=10,FacilityRepairCoins=20;
        public double AwakenedChance=.2;
        public double EnemyLeaderDeath=.5;
        public double SoldierDeathBase=.5,SoldierDeathSensitivity=1;
        public double RecruitSupplyFactor=.5,RecruitPriceSensitivity=1;
        public double SweepLossAtParity=.25,SweepSecondsAtParity=10,SweepMinimumSeconds=2;
        public int FoodValue=1,MaterialValue=3,BookValue=15,AdvancedBookValue=45,StoneValue=6,WineValue=10,OtherValue=10;
        public int[] WeaponValues={20,60,180,540,1620},HelmetValues={12,36,108,324,972},ArmorValues={18,54,162,486,1458},AccessoryValues={40,120,360,1080,3240};
        public double[] DropQuality={.05,.1,.5,.2,.1,.05};
        public double[] EquipmentQuality={.5,.75,1,1.25,1.5,2};
        public int[] BowQualityRange={0,0,0,0,1,2};
        public double SupplyAttackLow=10,SupplyAttackHigh=16,SupplyDefenseLow=2,SupplyDefenseHigh=4;
        public double ArmorK=8,ArmorCap=.8,CriticalMultiplier=1.5,BaseInterval=1,MinimumInterval=.2,ArrowSpeed=14,HeightAdvantage=2,HealThreshold=.7;
        public double AttackEquipmentScale=.12,HealthEquipmentScale=.1,DefenseEquipmentScale=.2,BuyFactor=1.3,SellFactor=.7;
        public double WeaponAttackScale=.12;
        public float EquipmentScale(float value,int quality)=>(float)(value>=0?value*EquipmentQuality[quality]:value/EquipmentQuality[quality]);
        public double EquipmentAttack(BasePersonalDefinition definition,int quality)=>EquipmentScale(definition.Damage,quality)*(definition.Slot=="weapon"?WeaponAttackScale:AttackEquipmentScale);
        public double[] Hit={10,7,8},Evasion={1,0,3};
        public int[] Capacity={3,5,8},CampMaterials={20,40},CampCoins={60,150};
        public int[] TroopBaseline={10,8,4},RecruitBasePrices={30,40,60};
        public int[] TrainingDays=DefaultTraining(true),TrainingBooks=DefaultTraining(false);
        public double[] Range={2,12,3},Speed={4,3,6},Growth={1,1.18,1.5,1.85,2.25,3,3.7,4.5,6},QualityValue={.5,.75,1,1.3,1.8,2.5};
        public int AttributeGrowthVersion;public double[] AttackGrowth,DefenseGrowth;
        public List<RpgTemplate> Templates=new List<RpgTemplate>();public List<RpgSkillDefinition> Skills=new List<RpgSkillDefinition>();public List<RpgTraitDefinition> Traits=new List<RpgTraitDefinition>();
        public List<RpgEquipmentDefinition> Equipment=new List<RpgEquipmentDefinition>();
        public List<RpgStoryPreset> StoryPresets;
        private static int[] DefaultTraining(bool days)=>Enumerable.Range(0,24).Select(i=>days?BaseTraining.Days(true,i%8+2):BaseTraining.Books(true,i%8+2)).ToArray();
        public int TrainingCost(bool days,int profession,int target)
        {if(profession<0||profession>2||target<2||target>9)throw new ArgumentOutOfRangeException();return (days?TrainingDays:TrainingBooks)[profession*8+target-2];}
        // Optional opening-only data. Running games consume their own copied
        // BaseState rules and recovery state, never a mutable global asset.
        public BaseEconomyRules OpeningEconomy;
        public int OpeningEconomyVersion;public double OpeningRecoveryPerDay;
        public int OpeningAuxVersion;public BattleTacticalRules OpeningTactical;public BaseRuinsRewardState OpeningRewards;
        public int OpeningSupplyWearVersion;public double OpeningReturnedWear,OpeningLostWear;
        public BaseInventory OpeningLordReward;
        public int OpeningBattleRewardVersion;public BaseInventory OpeningDefenseReward,OpeningCampReward;
        // 掠夺单价（2026-11-04 起含义变了）：不是整包损失的粮草件数，而是「每名来袭敌军带走多少」。
        // Food 是口粮天数（按各兵种日耗折算）、Materials 与 Coins 是每人的件数；口径见 BaseRaidLoot。
        public BaseInventory OpeningDefenseLoss;
        public BaseInventory OpeningInventory;
        public int SecondRaidDays=5;
        public int[] OpeningFacilityCapacity,OpeningFacilityMinimum;
        public int[] OpeningTownStock;
        public int[] ReforgeUp,ReforgeDown;
        public int ForgeDailyStock;
        public BasePersonalDefinition EquipmentDefinition(string id)
        {
            var original=BasePersonalEquipment.Definition(id);if(original==null)return null;
            var configured=Equipment?.SingleOrDefault(x=>x.Id==id);if(configured==null)return original;
            var d=original.Copy();d.Damage=configured.Damage;d.Defense=configured.Defense;d.Health=configured.Health;d.CritMultiplier=configured.CritMultiplier;d.Movement=configured.Movement;d.Stage=configured.Stage;return d;
        }
        public RpgRules Copy()
        {
            if(Version==0)return null;var c=(RpgRules)MemberwiseClone();c.Hit=Hit.ToArray();c.Evasion=Evasion.ToArray();c.Capacity=Capacity.ToArray();c.CampMaterials=CampMaterials.ToArray();c.CampCoins=CampCoins.ToArray();c.Range=Range.ToArray();c.Speed=Speed.ToArray();c.Growth=Growth.ToArray();c.QualityValue=QualityValue.ToArray();
            c.WeaponValues=WeaponValues.ToArray();c.HelmetValues=HelmetValues.ToArray();c.ArmorValues=ArmorValues.ToArray();c.AccessoryValues=AccessoryValues.ToArray();c.DropQuality=DropQuality.ToArray();c.EquipmentQuality=EquipmentQuality.ToArray();c.BowQualityRange=BowQualityRange.ToArray();c.TroopBaseline=TroopBaseline.ToArray();c.RecruitBasePrices=RecruitBasePrices.ToArray();c.TrainingDays=TrainingDays.ToArray();c.TrainingBooks=TrainingBooks.ToArray();c.AttackGrowth=AttackGrowth?.ToArray();c.DefenseGrowth=DefenseGrowth?.ToArray();
            c.Templates=Templates.Select(x=>x.Copy()).ToList();c.Skills=Skills.Select(x=>x.Copy()).ToList();c.Traits=Traits.Select(x=>x.Copy()).ToList();c.Equipment=Equipment?.Select(x=>x.Copy()).ToList();c.OpeningEconomy=OpeningEconomy?.Copy();c.OpeningTactical=OpeningTactical?.Copy();c.OpeningRewards=OpeningRewards?.Copy();c.OpeningLordReward=OpeningLordReward?.Copy();c.OpeningDefenseReward=OpeningBattleRewardVersion==1?OpeningDefenseReward?.Copy():null;c.OpeningDefenseLoss=OpeningBattleRewardVersion==1?OpeningDefenseLoss?.Copy():null;c.OpeningCampReward=OpeningBattleRewardVersion==1?OpeningCampReward?.Copy():null;c.OpeningInventory=OpeningInventory?.Copy();c.OpeningFacilityCapacity=OpeningFacilityCapacity?.ToArray();c.OpeningFacilityMinimum=OpeningFacilityMinimum?.ToArray();c.OpeningTownStock=OpeningTownStock?.ToArray();c.ReforgeUp=ReforgeUp?.ToArray();c.ReforgeDown=ReforgeDown?.ToArray();c.StoryPresets=StoryPresets?.Select(x=>x.Copy()).ToList();return c;
        }
        public static string Template(string category,int profession)=>category+"_"+new[]{"warrior","archer","knight"}[profession];
        public static RpgRules Defaults()
        {
            var r=new RpgRules {Version=1};r.Equipment=BasePersonalEquipment.DefaultDefinitions().Select(d=>new RpgEquipmentDefinition {Id=d.Id,Damage=d.Damage,Defense=d.Defense,Health=d.Health,CritMultiplier=d.CritMultiplier,Movement=d.Movement,Stage=d.Stage}).ToList();double[,] stats={{100,12,4},{75,14,2},{100,10,3},{300,24,8},{225,28,4},{300,20,6},{600,36,12},{450,42,6},{600,30,9}};
            for(int i=0;i<9;i++)r.Templates.Add(new RpgTemplate{Id=Template(i<3?"soldier":i<6?"captain_normal":"captain_awakened",i%3),Health=stats[i,0],Attack=stats[i,1],Defense=stats[i,2]});
            void Skill(string id,string name,double amount,double duration,double cd,int profession=-1)=>r.Skills.Add(new RpgSkillDefinition{Id=id,Name=name,Amount=amount,Duration=duration,Cooldown=cd,Profession=profession});
            Skill("inspire","鼓舞",.3,4,15);Skill("march","行军",2,3,15);Skill("heal","治愈",30,3,15);Skill("sweep","横扫",1.2,0,10,0);Skill("charge","突进",1.5,5,15,2);Skill("rapid_fire","连射",.5,5,15,1);
            r.Skills.Single(s=>s.Id=="sweep").Radius=4;r.Skills.Single(s=>s.Id=="charge").SpeedBonus=.5;
            void Trait(string id,string name,double min,double max,string conflict,double weight=1)=>r.Traits.Add(new RpgTraitDefinition{Id=id,Name=name,Min=min,Max=max,Conflict=conflict,Weight=weight});
            Trait("diligent","勤劳",1,10,"work");Trait("workaholic","卷王",11,20,"work",.25);
            Trait("farming","种植专精",1,15,"farm");Trait("farming_master","神农",16,25,"farm",.25);
            Trait("forging","锻造专精",1,15,"forge");Trait("forging_master","神匠",16,25,"forge",.25);
            Trait("frenzy","乱武",1,25,"frenzy");Trait("tough","坚韧",1,25,"damage");Trait("advantage","优势在我",1,1,"dice",.1);
            Trait("taunt","嘲讽",2,2,"taunt");Trait("merchant","商人",1,10,"trade");Trait("hasty","急性子",1,15,"speed");Trait("appraiser","鉴宝",1,5,"loot");
            Trait("bad_temper","坏脾气",1,10,"trade");Trait("disadvantage","我不行了",1,1,"dice");Trait("fragile","脆骨",1,25,"damage");Trait("slow","慢性子",1,15,"speed");
            Trait("drunkard","酒鬼",2,2,"wine");Trait("merciful","仁慈",1,10,"death");Trait("cruel","凶残",1,10,"death");Trait("inconspicuous","小透明",.5,.5,"taunt");
            return r;
        }
        public void Validate()
        {
            if(OpeningSupplyWearVersion<0||OpeningSupplyWearVersion>1||OpeningSupplyWearVersion==1&&(!Finite(OpeningReturnedWear)||OpeningReturnedWear<0||OpeningReturnedWear>1||!Finite(OpeningLostWear)||OpeningLostWear<0||OpeningLostWear>1))throw new ArgumentException("Demo军需磨损比例非法");
            if(EquipmentQuality?.Length!=6||EquipmentQuality.Any(v=>!Finite(v)||v<=0||v>100))throw new ArgumentException("装备品质属性倍率非法");
            if(BowQualityRange?.Length!=6||BowQualityRange.Any(v=>v<0||v>24)||Range?.Length!=3||Range[1]+BowQualityRange.Max()>24)throw new ArgumentException("弓品质加值与职业射程超过24细格");
            if(OpeningLordReward!=null&&new[]{OpeningLordReward.Food,OpeningLordReward.Materials,OpeningLordReward.Coins}.Any(v=>v<0||v>1000000))throw new ArgumentException("领主任务奖励非法");
            if(OpeningBattleRewardVersion<0||OpeningBattleRewardVersion>1||OpeningBattleRewardVersion==1&&new[]{OpeningDefenseReward,OpeningDefenseLoss,OpeningCampReward}.Any(v=>v==null||new[]{v.Food,v.Materials,v.Coins}.Any(n=>n<0||n>1000000)))throw new ArgumentException("Demo战斗奖励或首防损失非法");
            if(SecondRaidDays<1||SecondRaidDays>365)throw new ArgumentException("第二波倒计时须为1～365日");
            if(OpeningInventory!=null&&(new[]{OpeningInventory.Food,OpeningInventory.Materials,OpeningInventory.Coins}.Any(v=>v<0||v>100000000)||OpeningInventory.Equipment?.Count!=2||OpeningInventory.Equipment.Any(e=>e==null||e.Product!="supply"||e.Level<1||e.Level>2||e.Count<0||e.Count>100000000)||OpeningInventory.Equipment.Select(e=>e.Level).Distinct().Count()!=2))throw new ArgumentException("Demo开局资源或通用军需非法");
            if((OpeningFacilityCapacity==null)!=(OpeningFacilityMinimum==null)||OpeningFacilityCapacity!=null&&(OpeningFacilityCapacity.Length!=2||OpeningFacilityMinimum.Length!=2||Enumerable.Range(0,2).Any(i=>OpeningFacilityCapacity[i]<1||OpeningFacilityCapacity[i]>10000||OpeningFacilityMinimum[i]<0||OpeningFacilityMinimum[i]>OpeningFacilityCapacity[i])))throw new ArgumentException("Demo设施岗位容量或最低保留人数非法");
            if(ForgeDailyStock<0||ForgeDailyStock>99)throw new ArgumentException("锻造石每日供货非法");
            if((ReforgeUp==null)!=(ReforgeDown==null)||ReforgeUp!=null&&(ReforgeUp.Length!=4||ReforgeDown.Length!=4||Enumerable.Range(0,4).Any(i=>ReforgeUp[i]<0||ReforgeDown[i]<0||ReforgeUp[i]>10000||ReforgeDown[i]>10000||ReforgeUp[i]+ReforgeDown[i]>10000)))throw new ArgumentException("重铸概率非法");
            if(OpeningTownStock!=null&&(OpeningTownStock.Length!=4||OpeningTownStock.Any(n=>n<1||n>100000)))throw new ArgumentException("Demo城镇每日供货量非法");
            if(OpeningAuxVersion<0||OpeningAuxVersion>1)throw new ArgumentException("Demo物件／奖励版本非法");
            if(OpeningAuxVersion==1){if(OpeningTactical==null)throw new ArgumentException("Demo物件规则缺失");OpeningTactical.Validate();BaseRuinsRewards.ValidateDefaults(OpeningRewards);}
            if(TroopBaseline?.Length!=3||RecruitBasePrices?.Length!=3||TrainingDays?.Length!=24||TrainingBooks?.Length!=24||TroopBaseline.Any(v=>v<1||v>10000)||RecruitBasePrices.Any(v=>v<1||v>1000000)||TrainingDays.Any(v=>v<1||v>365)||TrainingBooks.Any(v=>v<1||v>1000))throw new ArgumentException("兵力基准、招募价格或训练成本非法");
            if(OpeningEconomyVersion<0||OpeningEconomyVersion>1||OpeningEconomyVersion==0&&OpeningRecoveryPerDay!=0||OpeningEconomyVersion==1&&(OpeningEconomy==null||!Finite(OpeningRecoveryPerDay)||OpeningRecoveryPerDay<=0||OpeningRecoveryPerDay>1||new[]{OpeningEconomy.SoldierFood,OpeningEconomy.CavalryFood,OpeningEconomy.LeaderFood,OpeningEconomy.SwordMaterial,OpeningEconomy.BowMaterial,OpeningEconomy.ArmorMaterial}.Any(x=>x<1||x>100)||new[]{OpeningEconomy.FarmPerWorker,OpeningEconomy.WorkshopPerWorker}.Any(x=>!Finite(x)||x<=0||x>100)||new[]{OpeningEconomy.HungerProductionPenalty,OpeningEconomy.HungerAttackPenalty}.Any(x=>!Finite(x)||x<0||x>=1)))throw new ArgumentException("Demo开局经营规则非法");
            // Demo 15 snapshots predate configurable equipment. Their empty list
            // keeps the old definitions until the new save version is enabled.
            if(Equipment?.Count>0)
            {
                var expected=BasePersonalEquipment.DefaultDefinitions().Select(d=>d.Id).ToArray();
                if(Equipment.Count!=expected.Length||Equipment.Any(d=>d==null||string.IsNullOrEmpty(d.Id)||!expected.Contains(d.Id)||d.Stage<0||d.Stage>4||new[]{d.Damage,d.Defense,d.Health,d.CritMultiplier,d.Movement}.Any(v=>float.IsNaN(v)||float.IsInfinity(v)||Math.Abs(v)>100000)||d.CritMultiplier<0)||Equipment.Select(d=>d.Id).Distinct().Count()!=expected.Length)throw new ArgumentException("RPG装备快照不完整或数值非法");
            }
            if(SceneRulesVersion<0||SceneRulesVersion>1)throw new ArgumentException("场景规则版本非法");
            if(!Finite(EnemyLeaderDeath)||EnemyLeaderDeath<0||EnemyLeaderDeath>1)throw new ArgumentException("敌方队长即死率非法");
            if(!Finite(SoldierDeathBase)||SoldierDeathBase<0||SoldierDeathBase>1||!Finite(SoldierDeathSensitivity)||SoldierDeathSensitivity<0||SoldierDeathSensitivity>10)throw new ArgumentException("普通兵即死率参数非法");
            if(!Finite(RecruitSupplyFactor)||RecruitSupplyFactor<0||RecruitSupplyFactor>1||!Finite(RecruitPriceSensitivity)||RecruitPriceSensitivity<0||RecruitPriceSensitivity>10)throw new ArgumentException("募兵公式参数非法");
            if(new[]{SupplyAttackLow,SupplyAttackHigh,SupplyDefenseLow,SupplyDefenseHigh}.Any(x=>!Finite(x)||x<0||x>1000))throw new ArgumentException("军需攻击或防御加值须为0～1000");
            if(!Finite(SweepLossAtParity)||SweepLossAtParity<0||SweepLossAtParity>1||!Finite(SweepSecondsAtParity)||!Finite(SweepMinimumSeconds)||SweepMinimumSeconds<=0||SweepSecondsAtParity<SweepMinimumSeconds||SweepSecondsAtParity>90)throw new ArgumentException("扫荡默认参数非法");
            if(new[]{AwakenedChance}.Any(x=>!Finite(x)||x<0||x>1)||SupplyWorkValue<1)throw new ArgumentException("生成权重或军需工作点价值非法");
            if(Hit==null||Evasion==null||Capacity?.Length!=3||CampMaterials?.Length!=2||CampCoins?.Length!=2||Range?.Length!=3||Speed?.Length!=3||Growth?.Length!=9||QualityValue?.Length!=6||DropQuality?.Length!=6||Templates==null||Skills==null||Traits==null||new[]{WeaponValues,HelmetValues,ArmorValues,AccessoryValues}.Any(a=>a?.Length!=5))throw new ArgumentException("RPG配置集合缺失");
            if(AttributeGrowthVersion<0||AttributeGrowthVersion>1||AttributeGrowthVersion==1&&(AttackGrowth?.Length!=9||DefenseGrowth?.Length!=9||AttackGrowth.Concat(DefenseGrowth).Any(x=>!Finite(x)||x<=0||x>100)||AttackGrowth[0]!=1||DefenseGrowth[0]!=1))throw new ArgumentException("队长攻击／防御成长倍率非法");
            if(Capacity.Any(x=>x<1||x>10000)||Capacity[1]<Capacity[0]||Capacity[2]<Capacity[1]||CampMaterials.Concat(CampCoins).Any(x=>x<0||x>1000000)||new[]{WeaponValues,HelmetValues,ArmorValues,AccessoryValues}.Any(a=>a.Any(x=>x<1||x>1000000))||SupplyWorkValue>1000000)throw new ArgumentException("营帐容量、升级费用或商品价值非法");
            if(new[]{ArmorK,ArmorCap,CriticalMultiplier,BaseInterval,MinimumInterval,ArrowSpeed,HeightAdvantage,WeaponAttackScale,AttackEquipmentScale,HealthEquipmentScale,DefenseEquipmentScale,BuyFactor,SellFactor,SupplyAttackLow,SupplyAttackHigh,SupplyDefenseLow,SupplyDefenseHigh,HealThreshold}.Any(x=>!Finite(x)||x<0)||HealThreshold<=0||HealThreshold>1||ArrowSpeed<=0||BuyFactor<=SellFactor||SupplyMaterial<1||LeaderFood<1||Range.Concat(Speed).Concat(Growth).Concat(QualityValue).Any(x=>!Finite(x)||x<=0)||DropQuality.Any(x=>!Finite(x)||x<0)||Math.Abs(DropQuality.Sum()-1)>1e-8||Templates.Any(x=>x==null)||Skills.Any(x=>x==null||string.IsNullOrEmpty(x.Id)||!Finite(x.Cooldown)||x.Cooldown<=0||!Finite(x.Duration)||x.Duration<0||!Finite(x.Amount)||x.Amount<=0||!Finite(x.Radius)||x.Radius<0||!Finite(x.SpeedBonus)||x.SpeedBonus<0||!Finite(x.Opening)||x.Opening<0)||Skills.Select(x=>x.Id).Distinct().Count()!=6||Traits.Any(x=>x==null||!Finite(x.Min)||!Finite(x.Max)||!Finite(x.Step)||!Finite(x.Weight))||Traits.Select(x=>x.Id).Distinct().Count()!=21)throw new ArgumentException("RPG参数非法");
            if(new[]{FoodValue,MaterialValue,BookValue,AdvancedBookValue,StoneValue,WineValue,OtherValue,SupplyMaterial,LeaderFood,ArcherFood}.Any(v=>v<1||v>1000000)||new[]{FacilityRepairMaterials,FacilityRepairCoins}.Any(v=>v<0||v>1000000))throw new ArgumentException("经济默认值非法");
            if(Version!=1||CampLevel<1||CampLevel>3||InnDailyFee<1||InnDailyFee>1000||Hit.Length!=3||Evasion.Length!=3||Hit.Concat(Evasion).Any(x=>!Finite(x)||x<0||x>10||x!=Math.Truncate(x))||Templates.Count!=9||Templates.Select(t=>t.Id).Distinct().Count()!=9||Templates.Any(t=>!Finite(t.Health)||t.Health<1||!Finite(t.Attack)||t.Attack<0||!Finite(t.Defense)||t.Defense<0)||Skills.Count!=6||Traits.Count!=21||ArmorK<=0||ArmorCap<0||ArmorCap>=1||BaseInterval<MinimumInterval||MinimumInterval<=0||CriticalMultiplier<1||Traits.Any(t=>t.Min>t.Max||t.Step<=0||t.Weight<=0||Math.Abs((t.Max-t.Min)/t.Step-Math.Round((t.Max-t.Min)/t.Step))>1e-6))throw new ArgumentException("RPG规则／九类基准／技能特性配置非法");
            // Validate derived prices before a snapshot can create a town or overflow a transaction.
            double supplyUnit=2d*SupplyMaterial*MaterialValue+SupplyWorkValue;
            foreach(int level in new[]{1,2})
            {
                double buy=Math.Ceiling(supplyUnit*level*level*BuyFactor),sell=Math.Floor(supplyUnit*level*level*SellFactor);
                if(!Finite(buy)||!Finite(sell)||buy<1||buy>1000000||sell<1||sell>=buy)throw new ArgumentException("06b 军需与配方 / 05c 装备价格 / 02a 全局参数与试算：军需基础价值和交易倍率组合导致城镇报价非法（买价1～1000000，卖价至少1且小于买价）");
            }
            double maxGear=new[]{WeaponValues,HelmetValues,ArmorValues,AccessoryValues}.Max(a=>a.Max())*(double)QualityValue.Max();
            double maxGoods=Math.Max(supplyUnit*4,new[]{FoodValue,MaterialValue,BookValue,AdvancedBookValue,StoneValue,WineValue,OtherValue}.Max());
            double maxModifier=1+Math.Max(0,Math.Max(Traits.SingleOrDefault(t=>t.Id=="merchant")?.Max??0,Traits.SingleOrDefault(t=>t.Id=="bad_temper")?.Max??0))/100;
            if(!Finite(maxGear)||!Finite(maxGoods)||Math.Max(maxGear,maxGoods)*Math.Max(BuyFactor,SellFactor)*maxModifier>int.MaxValue-1d)throw new ArgumentException("05c 装备价格 / 02a 全局参数与试算 / 11b 特性数值与互斥：商品价值、品质与交易倍率组合导致报价溢出");
            if(Skills.Any(s=>s.Id=="heal"&&s.Duration<1||s.Id=="sweep"&&s.Duration!=0||s.Id!="heal"&&s.Id!="sweep"&&s.Duration<=0))throw new ArgumentException("技能持续时间与结算方式不符：治疗至少1秒，横扫为0，其他技能大于0");
            if(StoryPresets!=null)
            {
                string[] ids={"player","partner_farm","partner_craft"};
                if(StoryPresets.Count!=3||StoryPresets.Any(p=>p==null||!ids.Contains(p.Id))||StoryPresets.Select(p=>p.Id).Distinct().Count()!=3)throw new ArgumentException("固定人物配置不完整");
                foreach(var preset in StoryPresets)
                {
                    int profession=preset.Id=="partner_farm"?1:preset.Id=="partner_craft"?2:0;
                    var character=BaseRpg.Character(preset.Id,profession,preset.Id!="partner_farm",true,this);
                    character.SkillId=preset.SkillId;
                    if(!string.IsNullOrEmpty(preset.TraitId))character.Traits.Add(new RpgTrait{Id=preset.TraitId,Value=preset.TraitValue});
                    else if(preset.TraitValue!=0)throw new ArgumentException("固定人物 "+preset.Id+" 未设置特性却填写了数值");
                    if(preset.ExtraTraits?.Count>2||preset.ExtraTraits?.Any(t=>t==null)==true)throw new ArgumentException("固定人物 "+preset.Id+" 的额外特性非法");
                    if(preset.ExtraTraits!=null)character.Traits.AddRange(preset.ExtraTraits.Select(t=>t.Copy()));
                    try{BaseRpg.ValidateCharacter(character,this);}catch(ArgumentException e){throw new ArgumentException("固定人物 "+preset.Id+" 的技能或特性配置非法："+e.Message,e);}
                }
            }
            if(Templates.SingleOrDefault(t=>t.Id=="soldier_warrior")?.Attack<.001)throw new ArgumentException("剑士战力参照攻击须至少为0.001");
            var frenzy=Traits.SingleOrDefault(t=>t.Id=="frenzy");var rapid=Skills.SingleOrDefault(s=>s.Id=="rapid_fire");
            if(frenzy==null||rapid==null||BaseInterval/(1+frenzy.Max/100+rapid.Amount)<MinimumInterval)throw new ArgumentException("连射与乱武的最大组合使攻击间隔低于下限");
            foreach(var template in Templates)
            {
                int levels=template.Id.StartsWith("soldier_")?1:9;
                for(int level=1;level<=levels;level++)
                {
                    double healthGrowth=levels==1?1:Growth[level-1],attackGrowth=levels==1?1:AttributeGrowthVersion==1?AttackGrowth[level-1]:Growth[level-1];
                    double health=template.Health*healthGrowth,attack=template.Attack*attackGrowth;
                    if(health<1||health>100000||attack>1000000)
                        throw new ArgumentException("03b 九类模板 / 04a 队长成长与训练："+template.Id+" 等级"+level+"的裸装生命须为1～100000、攻击不得超过1000000");
                }
            }
        }
        private static bool Finite(double x)=>!double.IsNaN(x)&&!double.IsInfinity(x);
        public SoldierStats Bare(RpgCharacter character,int level)
        {
            var t=Templates.Single(x=>x.Id==character.TemplateId);int p=character.Profession;
            if(p<0||p>2||level<1||level>9||!character.TemplateId.EndsWith(new[]{"warrior","archer","knight"}[p],StringComparison.Ordinal))throw new ArgumentException("人物模板／职业／等级不一致");
            bool soldier=character.TemplateId.StartsWith("soldier_");double healthGrowth=soldier?1:Growth[level-1],attackGrowth=soldier?1:AttributeGrowthVersion==1?AttackGrowth[level-1]:Growth[level-1],defenseGrowth=soldier?1:AttributeGrowthVersion==1?DefenseGrowth[level-1]:Growth[level-1];
            var kind=p==1?SoldierKind.Ranged:p==2?SoldierKind.Cavalry:SoldierKind.Melee;
            return new SoldierStats{Health=(float)(t.Health*healthGrowth),Damage=(float)(t.Attack*attackGrowth),Defense=(float)(t.Defense*defenseGrowth),Level=level,CritChance=0,CritMultiplier=(float)CriticalMultiplier,Range=p==1?6:p==2?2:1,MoveDistance=p==2?3:2,HasProfessionKind=true,ProfessionKind=kind,HasCombatKind=true,CombatKind=kind};
        }
    }
    public static partial class BaseRpg
    {
        public static int Profession(SoldierKind kind)=>kind==SoldierKind.Melee?0:kind==SoldierKind.Ranged?1:kind==SoldierKind.Cavalry?2:throw new ArgumentException("队长须指定实际职业");
        public static bool Active(BaseState s)=>s?.Demo?.Rpg?.Version==1;
        public static RpgCharacter Character(string id,int profession,bool awakened,bool story,RpgRules rules,int level=1)
        {return new RpgCharacter{Version=1,Id=id,Profession=profession,Level=level,Story=story,TemplateId=RpgRules.Template(awakened?"captain_awakened":"captain_normal",profession),SkillId=profession==0?"sweep":profession==1?"rapid_fire":"charge",Rules=rules};}
        public static void Bind(BaseState s)
        {
            if(!Active(s))return;var rules=s.Demo.Rpg;rules.Validate();
            foreach(var site in s.Campaign.EnemySites)PrepareEnemies(s,site.Garrison,site.AttackEventId);
            foreach(var mission in s.Campaign.Events)PrepareEnemies(s,mission.EnemyArmy,mission.Id);
            foreach(var leader in s.Leaders.Concat(s.Demo.Candidates??new List<BaseLeader>()).Concat(s.Demo.SceneFollowers??new List<BaseLeader>()))if(leader.Rpg?.Version==1)ValidateCharacter(leader.Rpg,rules);
            foreach(var enemy in s.Campaign.EnemySites.SelectMany(site=>site.Garrison).Concat(s.Campaign.Events.SelectMany(e=>e.EnemyArmy)))if(enemy.Rpg?.Version==1)ValidateCharacter(enemy.Rpg,rules);
            if(s.Demo.Intro?.Captain?.Rpg?.Version==1)ValidateCharacter(s.Demo.Intro.Captain.Rpg,rules);
            foreach(var prisoner in s.Aftermath.Prisoners)if(prisoner.Rpg?.Version==1)ValidateCharacter(prisoner.Rpg,rules);
        }
        public static void ValidateCharacter(RpgCharacter c,RpgRules r)
        {
            if(c==null||c.Version!=1||string.IsNullOrEmpty(c.Id)||c.Profession<0||c.Profession>2||c.Level<1||c.Level>9||!r.Templates.Any(t=>t.Id==c.TemplateId)||!c.TemplateId.EndsWith(new[]{"warrior","archer","knight"}[c.Profession],StringComparison.Ordinal)||c.Traits==null||c.Traits.Count>3||c.Traits.Any(t=>t==null||!r.Traits.Any(d=>d.Id==t.Id&&t.Value>=d.Min&&t.Value<=d.Max&&Math.Abs((t.Value-d.Min)/d.Step-Math.Round((t.Value-d.Min)/d.Step))<1e-6))||c.Traits.Select(t=>t.Id).Distinct().Count()!=c.Traits.Count||c.Traits.Select(t=>r.Traits.Single(d=>d.Id==t.Id).Conflict).Distinct().Count()!=c.Traits.Count)throw new ArgumentException("人物模板／特性引用非法");
            if(!c.TemplateId.StartsWith("soldier_")&&!r.Skills.Any(d=>d.Id==c.SkillId&&(d.Profession<0||d.Profession==c.Profession)))throw new ArgumentException("人物技能不属于职业合法池");
            c.Rules=r;
        }
        public static void Initialize(BaseState s,RpgRules openingRules=null)
        {
            // Freeze the supplied values in this game; editing the source later must
            // never mutate a running game's rules.
            var r=openingRules==null?RpgRules.Defaults():openingRules.Copy();
            if(r==null)throw new ArgumentException("开局RPG规则快照缺版本");
            r.Validate();s.Demo.Rpg=r;
            if(r.ForgeDailyStock>0)s.PersonalEquipment.StonesRemaining=r.ForgeDailyStock;
            foreach(var l in s.Leaders.Concat(s.Demo.Candidates))
            {
                int p=l.Id=="partner_farm"?1:l.Id=="partner_craft"?2:0;
                l.PreferredStrategy=l.Id==s.PlayerLeaderId?SquadStrategy.Obey:SquadStrategy.Attack;
                l.Rpg=Character(l.Id,p,l.IsAwakened,l.Id==s.PlayerLeaderId||l.Id=="partner_farm"||l.Id=="partner_craft",r);
                var preset=r.StoryPresets?.SingleOrDefault(x=>x.Id==(l.Id==s.PlayerLeaderId?"player":l.Id));
                l.Rpg.SkillId=preset?.SkillId??(l.Id==s.PlayerLeaderId?"inspire":p==1?"rapid_fire":"charge");
                if(preset!=null&&!string.IsNullOrEmpty(preset.TraitId))l.Rpg.Traits.Add(new RpgTrait{Id=preset.TraitId,Value=preset.TraitValue});
                else if(preset==null&&p!=0)l.Rpg.Traits.Add(new RpgTrait{Id=p==1?"farming":"forging",Value=10});
                if(preset?.ExtraTraits!=null)l.Rpg.Traits.AddRange(preset.ExtraTraits.Select(t=>t.Copy()));
                try { ValidateCharacter(l.Rpg,r); }
                catch(ArgumentException error) { throw new ArgumentException("固定人物 "+l.Id+" 的技能或特性与当前配表不一致："+error.Message,error); }
                l.PersonalEquipment.BaseStats=r.Bare(l.Rpg,1);l.Health=l.MaxHealth=BasePersonalEquipment.Stats(l).Health;
            }
            foreach(var worker in s.Workers){var c=new RpgCharacter{TemplateId=RpgRules.Template("soldier",(int)worker.Kind),Profession=(int)worker.Kind};worker.CombatStats=r.Bare(c,1);worker.HasCombatProfile=true;worker.Health=worker.MaxHealth=worker.CombatStats.Health;}
            foreach(var site in s.Campaign.EnemySites)PrepareEnemies(s,site.Garrison,site.AttackEventId);
            foreach(var mission in s.Campaign.Events)PrepareEnemies(s,mission.EnemyArmy,mission.Id);
            InitializeSupply(s);
            foreach(var offer in s.Town.Recruits){var c=new RpgCharacter{TemplateId=RpgRules.Template("soldier",(int)offer.Kind),Profession=(int)offer.Kind};offer.Stats=r.Bare(c,1);}
        }
        public static SoldierStats Stats(BaseLeader leader)
        {
            var c=leader.Rpg;var r=c.Rules??throw new ArgumentException("人物缺RPG规则快照");c.Level=leader.Training?.Level??c.Level;var stats=r.Bare(c,c.Level);
            foreach(var item in leader.PersonalEquipment?.Items??new List<BasePersonalItem>())
            {
                var d=BasePersonalEquipment.Definition(r,item.Template);stats.Health+=(float)(r.EquipmentScale(d.Health,item.Quality)*r.HealthEquipmentScale);stats.Damage+=(float)r.EquipmentAttack(d,item.Quality);stats.Defense+=(float)(r.EquipmentScale(d.Defense,item.Quality)*r.DefenseEquipmentScale);stats.CritMultiplier+=r.EquipmentScale(d.CritMultiplier,item.Quality);
            }
            string set=BasePersonalEquipment.ArmorSet(leader);
            if(set=="light")stats.Damage+=(float)(2*r.AttackEquipmentScale);
            if(set=="heavy"){stats.Health+=(float)(10*r.HealthEquipmentScale);stats.Defense+=(float)r.DefenseEquipmentScale;}
            if(set!=""&&leader.PersonalEquipment.Items.Any(i=>i.Slot=="weapon"&&i.Template.StartsWith("novice_"))){stats.Damage+=(float)(2*r.AttackEquipmentScale);stats.Health+=(float)(10*r.HealthEquipmentScale);}
            return stats;
        }
        public static float Power(BattleSoldier unit,RpgRules rules)
        {
            var reference=rules.Templates.Single(t=>t.Id=="soldier_warrior");var stats=unit.Stats;int p=Profession(unit.ProfessionKind);double outgoing=0,incoming=0;
            for(int die=1;die<=10;die++)
            {
                var attack=new RpgAttackRoll((int)rules.Hit[p],(int)rules.Evasion[0],0,0,die);var defense=new RpgAttackRoll((int)rules.Hit[0],(int)rules.Evasion[p],0,0,die);
                if(attack.Hit)outgoing+=RpgCombatRules.Damage(stats.Damage,reference.Defense,rules.ArmorK*stats.Level,rules.ArmorCap,attack.Critical?stats.CritMultiplier:1)/10;
                if(defense.Hit)incoming+=RpgCombatRules.Damage(reference.Attack,stats.Defense,rules.ArmorK,rules.ArmorCap,defense.Critical?rules.CriticalMultiplier:1)/10;
            }
            double received=1+((unit.Rpg?.Trait("fragile")??0)-(unit.Rpg?.Trait("tough")??0))/100;
            return (float)Math.Sqrt(stats.Health*Math.Max(0,outgoing)*(1+(unit.Rpg?.Trait("frenzy")??0)/100)/(reference.Health*Math.Max(.000001,incoming*received)));
        }
        private static void InitializeSupply(BaseState s)
        {
            var r=s.Demo.Rpg;
            var old=s.Inventory.Equipment;
            s.Inventory.Equipment=new[]{1,2}.Select(level=>new BaseEquipmentStock {Product="supply",Level=level,Count=old.Where(e=>e.Level==level).Select(e=>e.Count).DefaultIfEmpty(0).Max()}).ToList();
            s.WorkshopProduct="supply";
            s.WorkshopProgress=new List<BaseProductProgress>{new BaseProductProgress {Product="supply"}};
            s.Construction.AdvancedProgress=new List<BaseProductProgress>{new BaseProductProgress {Product="supply"}};
            s.Supply=new BaseSupplyState {Version=1,ReturnedWear=r.OpeningSupplyWearVersion==1?r.OpeningReturnedWear:s.Supply.ReturnedWear,LostWear=r.OpeningSupplyWearVersion==1?r.OpeningLostWear:s.Supply.LostWear,
                Rules=new List<BaseSupplyRule>{new BaseSupplyRule {Product="supply",LowBonus=r.SupplyAttackLow,HighBonus=r.SupplyAttackHigh,LowDefense=r.SupplyDefenseLow,HighDefense=r.SupplyDefenseHigh}},
                Wear=new[]{1,2}.Select(level=>new BaseEquipmentWear {Product="supply",Level=level}).ToList()};
            s.Town.Goods.RemoveAll(g=>g.Id!="food"&&g.Id!="materials");
            if(r.OpeningTownStock!=null)
            {
                string[] ids={"food","materials"};
                for(int i=0;i<ids.Length;i++){var good=s.Town.Goods.Single(g=>g.Id==ids[i]);good.DailyStock=good.Remaining=r.OpeningTownStock[i];}
            }
            foreach(int level in new[]{1,2})
            {
                // 2026-11-04：二级军需含额外制作难度（需要更稀缺的原料与工时），价格按等级平方计，供给量减半，逼玩家算性价比。
                double value=(2d*r.SupplyMaterial*r.MaterialValue+r.SupplyWorkValue)*level*level;
                int stock=level==1?(r.OpeningTownStock?[2]??6):Math.Max(1,(r.OpeningTownStock?[3]??2)/2);
                s.Town.Goods.Add(new BaseTownGood {Id="supply"+level,BuyPrice=(int)Math.Ceiling(value*r.BuyFactor),SellPrice=(int)Math.Floor(value*r.SellFactor),DailyStock=stock,Remaining=stock});
            }
        }
    }
}

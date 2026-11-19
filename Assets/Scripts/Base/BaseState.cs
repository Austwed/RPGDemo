using System;
using System.Collections.Generic;
using System.Linq;

namespace BaseConquest.Base
{
    public enum BaseTroopKind { Melee, Ranged, Cavalry }
    [Serializable] public sealed class BaseLeader
    {
        public string Id, Name, FacilityId = "";
        public string AppearanceId = "";
        public BaseConquest.Battle.SquadStrategy PreferredStrategy=BaseConquest.Battle.SquadStrategy.Attack;
        public string AcquisitionSource = "";
        public int AcquisitionDay;
        public RpgCharacter Rpg;
        public BaseCommanderTroops DefaultTroops;
        public BasePersonalLoadout PersonalEquipment;
        public BaseTrainingProgress Training=new BaseTrainingProgress();
        public BaseWineStatus Wine=new BaseWineStatus();
        public string Fate = "active";
        public string PartyId = "";
        public string DesiredFacility = "";
        public bool PostLocked, Resting, HasAffinity;
        public bool IsAwakened, IsAlien;
        public double Health, MaxHealth, FarmBonus, WorkshopBonus;
        public bool HasCombatProfile;
        public BaseConquest.Battle.SoldierStats CombatStats;
        public bool Away;
        public bool HasMapPosition;
        public string MapRegion = "base";
        public string MapId;
        public bool Recruited, HasHomePosition;
        public bool WaitingAtRecruitment, CampPostSet;
        public double HomeX, HomeY;
        public double MapX, MapY;
        public int UnavailableDay;
        public double ProductionBonus = 0.2;
        public BaseLeader Copy() { var x = (BaseLeader)MemberwiseClone(); x.CombatStats = CombatStats?.Clone(); x.DefaultTroops=DefaultTroops?.Copy(); x.PersonalEquipment=PersonalEquipment?.Copy();x.Training=Training?.Copy();x.Wine=Wine?.Copy();x.Rpg=Rpg?.Copy(); return x; }
    }
    [Serializable] public sealed class BaseWorkerBucket
    {
        public BaseTroopKind Kind;
        public string FacilityId = "";
        public string PartyId = "", SourceId = "";
        public string HeldBy = "", HeldSourceId = "";
        public int PartyOrder;
        public string DesiredFacility = "";
        public bool PostLocked, Resting;
        public bool Recruited;
        public double Health, MaxHealth, FarmEfficiency = 1, WorkshopEfficiency = 1;
        public bool HasCombatProfile;
        public BaseConquest.Battle.SoldierStats CombatStats;
        public bool Away;
        public int Count, UnavailableDay;
        public List<int> SoldierNumbers;

        private BaseWorkerBucket CopyWith(List<int> numbers, int count)
        {
            var copy = (BaseWorkerBucket)MemberwiseClone();
            copy.CombatStats = CombatStats?.Clone();
            copy.SoldierNumbers = numbers;
            copy.Count = count;
            return copy;
        }
        public BaseWorkerBucket Copy() => CopyWith(SoldierNumbers?.ToList(), Count);
        public BaseWorkerBucket Slice(int ordinal, int count) => CopyWith(SoldierNumbers.GetRange(ordinal, count), count);
        internal BaseWorkerBucket Take(int ordinal, int count)
        {
            var moved = Slice(ordinal, count);
            SoldierNumbers.RemoveRange(ordinal, count);
            Count -= count;
            return moved;
        }
        internal BaseWorkerBucket TakeNumbers(List<int> numbers)
        {
            var selected = numbers.ToHashSet();
            if (selected.Count != numbers.Count || SoldierNumbers.Count(selected.Contains) != numbers.Count)
                throw new InvalidOperationException("出征士兵与所选名单不一致");
            SoldierNumbers.RemoveAll(selected.Contains);
            Count = SoldierNumbers.Count;
            return CopyWith(numbers, numbers.Count);
        }
    }
    [Serializable] public sealed class BaseEquipmentStock
    {
        public string Product;
        public int Level, Count;
        public BaseEquipmentStock Copy() => (BaseEquipmentStock)MemberwiseClone();
    }
    [Serializable] public sealed class BaseProductProgress
    {
        public string Product;
        public double Fraction;
        public BaseProductProgress Copy() => (BaseProductProgress)MemberwiseClone();
    }
    [Serializable] public sealed class BaseInventory
    {
        public int Food, Materials, Coins;
        public List<BaseEquipmentStock> Equipment = new List<BaseEquipmentStock>();
        public BaseInventory Copy() => new BaseInventory { Food = Food, Materials = Materials, Coins = Coins, Equipment = Equipment.Select(x => x.Copy()).ToList() };
    }
    [Serializable] public sealed class BaseEconomyRules
    {
        public int SoldierFood = 1, CavalryFood = 2, LeaderFood;
        public double FarmPerWorker = 3, WorkshopPerWorker = 1, HungerProductionPenalty = 0.5, HungerAttackPenalty = 0.2;
        public int SwordMaterial = 2, BowMaterial = 2, ArmorMaterial = 3;
        public BaseEconomyRules Copy() => (BaseEconomyRules)MemberwiseClone();
        public int MaterialCost(string product) => product == "sword" ? SwordMaterial : product == "bow" ? BowMaterial : ArmorMaterial;
    }
    [Serializable] public sealed class BaseDayReport
    {
        public int CompletedDay, NewDay, FoodBefore, FoodProduced, MaterialsSpent, ProductsMade, FoodRequired, FoodConsumed, FoodMissing, FoodAfter;
        public string Product;
        public int ProductLevel;
        public double ProductionFactor;
        public int PeopleRecovered;
        public BaseDayReport Copy() => (BaseDayReport)MemberwiseClone();
    }
    [Serializable] public sealed class BaseState
    {
        public int Version = 1, Day = 1, FoodPaidDay, LastCompletedDay;
        public int TodayFoodRequired, TodayFoodConsumed, TodayFoodMissing;
        public string PlayerLeaderId = "player", TemplateTitle = "", WorkshopProduct = "sword";
        public double PlayerX = 12, PlayerY = 6.5, FarmFraction;
        public BaseInventory Inventory = new BaseInventory();
        public BaseEconomyRules Rules = new BaseEconomyRules();
        public List<BaseLeader> Leaders = new List<BaseLeader>();
        public List<BaseWorkerBucket> Workers = new List<BaseWorkerBucket>();
        public int LastSoldierNumber;
        internal void EnsureSoldierNumbers(bool validate = false)
        {
            if (LastSoldierNumber < 0) throw new ArgumentException("士兵编号计数非法");
            var used = validate ? new HashSet<int>() : null;
            int previousLast = LastSoldierNumber;
            // Old saves and new hires enter in roster order. Retired numbers stay reserved by the counter.
            foreach (var worker in Workers)
            {
                if (worker.SoldierNumbers?.Count > 0)
                {
                    if (worker.SoldierNumbers.Count != worker.Count || validate && worker.SoldierNumbers.Any(n => n <= 0 || n > previousLast || !used.Add(n)))
                        throw new ArgumentException("士兵编号与名册不一致");
                    continue;
                }
                if (worker.Count < 0 || worker.Count > 10000 || (long)LastSoldierNumber + worker.Count > int.MaxValue)
                    throw new ArgumentException("士兵编号或人数越界");
                worker.SoldierNumbers ??= new List<int>(worker.Count);
                for (int i = 0; i < worker.Count; i++) worker.SoldierNumbers.Add(++LastSoldierNumber);
            }
        }
        public List<BaseProductProgress> WorkshopProgress = new List<BaseProductProgress>();
        public BaseMusterState Muster;
        public BaseDevelopmentState Development;
        public BaseSupplyState Supply;
        public BaseCampaignState Campaign;
        public BaseAftermath Aftermath;
        public BaseTownState Town;
        public BaseInnState Inn=new BaseInnState();
        public BaseBackpackState Backpack;
        public BaseConstructionState Construction;
        // 设施布局与商队（P40／P41）：已放置设施仍以营地布局对象为准，这里只存收存清单与商队事务。
        public BaseLayoutState Layout;
        public BaseCaravanState Caravan;
        public BaseQuestState Quests;
        public BaseAdventureState Adventure;
        public BasePersonalEquipmentState PersonalEquipment;
        public BaseDemoProgress Demo;
        public BaseAchievementState Achievements;
        public int InjuryDialogueDay;
        public List<string> InjuryDialogueIds = new List<string>();
        public string SceneBattleId="";
        // Unity按值序列化自定义类；用显式标记区分无报告，避免依赖null的回读行为。
        public bool HasReport;
        public BaseDayReport LastReport = new BaseDayReport();
        public BaseState Copy() => new BaseState {
            Version = Version, Day = Day, FoodPaidDay = FoodPaidDay, LastCompletedDay = LastCompletedDay,
            TodayFoodRequired = TodayFoodRequired, TodayFoodConsumed = TodayFoodConsumed, TodayFoodMissing = TodayFoodMissing,
            PlayerLeaderId = PlayerLeaderId, TemplateTitle = TemplateTitle, WorkshopProduct = WorkshopProduct,
            InjuryDialogueDay = InjuryDialogueDay, InjuryDialogueIds = InjuryDialogueIds?.ToList(),
            PlayerX = PlayerX, PlayerY = PlayerY, FarmFraction = FarmFraction, SceneBattleId=SceneBattleId,
            Inventory = Inventory.Copy(), Rules = Rules.Copy(), Leaders = Leaders.Select(x => x.Copy()).ToList(), LastSoldierNumber = LastSoldierNumber,
            Workers = Workers.Select(x => x.Copy()).ToList(), WorkshopProgress = WorkshopProgress.Select(x => x.Copy()).ToList(), HasReport = HasReport, LastReport = LastReport?.Copy() ?? new BaseDayReport(), Muster = Muster?.Copy(), Development = Development?.Copy(), Supply = Supply?.Copy(), Campaign = Campaign?.Copy(), Aftermath = Aftermath?.Copy(), Town = Town?.Copy(), Inn=Inn?.Copy(), Backpack=Backpack?.Copy(), Construction=Construction?.Copy(), Layout=Layout?.Copy(), Caravan=Caravan?.Copy(), Quests=Quests?.Copy(), Adventure=Adventure?.Copy(), PersonalEquipment=PersonalEquipment?.Copy(), Demo=Demo?.Copy(),Achievements=Achievements?.Copy()
        };
    }
    [Serializable] public sealed class BaseScenarioDocument
    {
        public int SchemaVersion = 1;
        public string Kind = "base", Title, Description;
        public string[] TestNotes;
        public BaseState Opening;
        public bool UseOpeningRules;
        /// <summary>
        /// 全局默认经营规则。由 BaseSettings 资产在运行时注入（见 BaseSettings.InstallDefaultRules）。
        /// 为 null 时保持模板自带 Opening.Rules，旧模板与离线检查脚本的行为不变。
        /// </summary>
        public static BaseEconomyRules DefaultRules;
        public BaseState CreateState()
        {
            if (SchemaVersion != 1 || Kind != "base" || string.IsNullOrWhiteSpace(Title)) throw new ArgumentException("需要SchemaVersion=1、Kind=base与Title");
            // 兼容旧模板的BaseSettings默认值；显式独立规则的导入开局不覆盖。
            // 旧存档继续使用自身保存的Rules，重导不会迁移进度。
            if (!UseOpeningRules && DefaultRules != null) Opening.Rules = DefaultRules.Copy();
            BaseEconomy.Validate(Opening, false);
            if (Opening.Day != 1 || Opening.FoodPaidDay != 0 || Opening.LastCompletedDay != 0) throw new ArgumentException("据点模板必须从未扣粮的第1天开始；进度请使用存档");
            var state = Opening.Copy(); state.TemplateTitle = Title;
            if(BaseRpg.Active(state)&&state.Demo.Rpg.GenerationSeed==0)state.Demo.Rpg.GenerationSeed=(int)(BitConverter.ToUInt32(Guid.NewGuid().ToByteArray(),0)%int.MaxValue)+1;
            state.Campaign.ContentInitialized = true;
            BaseEconomy.PayMorningFood(state);
            BaseDevelopment.Allocate(state);
            return state;
        }
    }
}

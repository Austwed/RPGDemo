using System;
using System.Linq;

namespace BaseConquest.Base
{
    public sealed class BaseDayPreview
    {
        public int Day, FoodProduced, ProductsMade, MaterialsSpent, FoodRequired, NextFood, NextMissing, WorkCapacity;
        public string Product;
        public int ProductLevel;
        public double ProductionFactor, FarmFraction, WorkshopFraction;
    }
    public static class BaseEconomy
    {
        public static readonly string[] Products = { "sword", "bow", "armor" };
        public static string[] ProductsFor(BaseState s)=>BaseRpg.Active(s)?new[]{"supply"}:Products;
        public static readonly string[] Facilities = { "", "farm", "workshop", "caravan" };
        public static string ProductName(string id) => id == "supply" ? "军需" : id == "sword" ? "剑" : id == "bow" ? "弓" : "盔甲";
        public static string FacilityName(string id) => id == "farm" ? "粮田" : id == "workshop" ? "工坊" : id == "caravan" ? "商队" : id == "office" ? "办公设施" : "闲置";
        public static string TroopName(BaseTroopKind kind) => kind == BaseTroopKind.Melee ? "剑士" : kind == BaseTroopKind.Ranged ? "弓兵" : "骑兵";
        private static bool Finite(double x) => !double.IsNaN(x) && !double.IsInfinity(x);
        private static bool Fraction(double x) => Finite(x) && x >= 0 && x < 1;
        public static void Validate(BaseState s, bool initialized = true)
        {
            if (s == null || s.Version != 1 || s.Day < 1 || s.Day > 1000000 || s.LastCompletedDay != s.Day - 1 || s.FoodPaidDay != (initialized ? s.Day : 0)) throw new ArgumentException("存档版本或日期／日初／日末状态非法");
            s.InjuryDialogueIds ??= new System.Collections.Generic.List<string>();
            if(s.InjuryDialogueDay<0||s.InjuryDialogueDay>s.Day||s.InjuryDialogueIds.Count>32
                ||s.InjuryDialogueDay==0&&s.InjuryDialogueIds.Count>0
                ||s.InjuryDialogueIds.Any(string.IsNullOrWhiteSpace)||s.InjuryDialogueIds.Distinct().Count()!=s.InjuryDialogueIds.Count)
                throw new ArgumentException("带伤对白记录非法");
            if (s.Inventory == null || s.Rules == null || s.Leaders == null || s.Workers == null || s.WorkshopProgress == null || s.Inventory.Equipment == null) throw new ArgumentException("据点数据缺少库存、人员或生产规则");
            if (new[] { s.Inventory.Food, s.Inventory.Materials, s.Inventory.Coins }.Any(x => x < 0 || x > 100000000)) throw new ArgumentException("资源数量越界");
            if (!Fraction(s.FarmFraction) || !ProductsFor(s).Contains(s.WorkshopProduct) || s.WorkshopProgress.Count != ProductsFor(s).Length || s.WorkshopProgress.Any(x => x == null || !ProductsFor(s).Contains(x.Product) || !Fraction(x.Fraction)) || s.WorkshopProgress.Select(x => x.Product).Distinct().Count() != ProductsFor(s).Length) throw new ArgumentException("生产进度或配方非法");
            BaseCampaign.Normalize(s);
            BaseFacilityLayout.Normalize(s);
            BaseRpg.Bind(s);
            BaseBanditHideout.Install(s);
            BaseSceneRules.Validate(s);BaseInn.ValidateRetired(s);
            if (s.Campaign.Location == "base" && (s.Development == null || s.Development.Version == 0)) BaseMap.RelocateLegacyBarracks(ref s.PlayerX, ref s.PlayerY);
            BaseSceneRules.RepairEntrancePositions(s);
            if (!Finite(s.PlayerX) || !Finite(s.PlayerY) || !BaseSpatial.Geometry(s,s.PlayerX,s.PlayerY)) throw new ArgumentException("角色位置不可通行");
            if(s.Leaders.Any(l=>l.HasHomePosition&&(!Finite(l.HomeX)||!Finite(l.HomeY)||!BaseSpatial.Geometry(s,l.HomeX,l.HomeY,"base"))))throw new ArgumentException("伙伴待命位置非法");
            var r = s.Rules;
            if (r.LeaderFood < 0 || r.LeaderFood > 100 || new[] { r.SoldierFood, r.CavalryFood, r.SwordMaterial, r.BowMaterial, r.ArmorMaterial }.Any(x => x < 1 || x > 100) || new[] { r.FarmPerWorker, r.WorkshopPerWorker }.Any(x => !Finite(x) || x <= 0 || x > 100) || new[] { r.HungerProductionPenalty, r.HungerAttackPenalty }.Any(x => !Finite(x) || x < 0 || x >= 1)) throw new ArgumentException("经济规则越界");
            if (s.Leaders.Count < 1 || s.Leaders.Count > 100 || s.Leaders.Any(x => x == null || string.IsNullOrWhiteSpace(x.Id) || string.IsNullOrWhiteSpace(x.Name) || !Facilities.Contains(x.FacilityId) || x.UnavailableDay < 0 || x.UnavailableDay > s.Day || !Finite(x.ProductionBonus) || x.ProductionBonus < 0 || x.ProductionBonus > 1) || s.Leaders.Select(x => x.Id).Distinct().Count() != s.Leaders.Count || !s.Leaders.Any(x => x.Id == s.PlayerLeaderId)) throw new ArgumentException("人物身份／任职非法");
            if(s.Leaders.Any(l=>l.HasMapPosition&&(!BaseSceneInstances.LocationValid(s,BaseAdventure.Region(l))||!Finite(l.MapX)||!Finite(l.MapY)||!BaseSpatial.Geometry(s,l.MapX,l.MapY,BaseAdventure.MapKey(s,l))))) throw new ArgumentException("伙伴存档坐标不可通行");
            if (s.Leaders.Where(x => x.FacilityId != "").GroupBy(x => x.FacilityId).Any(g => g.Count() > 1)) throw new ArgumentException("同一设施只能有一名负责人");
            if (s.Workers.Any(x => x == null || !Enum.IsDefined(typeof(BaseTroopKind), x.Kind) || !Facilities.Contains(x.FacilityId) || x.Count < 0 || x.Count > 10000 || x.UnavailableDay < 0 || x.UnavailableDay > s.Day) || s.Workers.Sum(x => (long)x.Count) > 10000) throw new ArgumentException("兵力数量或生产资格非法");
            if (s.Inventory.Equipment.Count != ProductsFor(s).Length*2 || s.Inventory.Equipment.Any(x => x == null || !ProductsFor(s).Contains(x.Product) || x.Level < 1 || x.Level > 2 || x.Count < 0 || x.Count > 100000000) || s.Inventory.Equipment.Select(x => x.Product + x.Level).Distinct().Count() != ProductsFor(s).Length*2) throw new ArgumentException("装备库存非法");
            if (s.TodayFoodRequired < 0 || s.TodayFoodConsumed < 0 || s.TodayFoodMissing < 0 || s.TodayFoodConsumed + (long)s.TodayFoodMissing != s.TodayFoodRequired || !initialized && s.TodayFoodRequired != 0) throw new ArgumentException("当天口粮记录非法");
            if (s.HasReport && (s.LastReport == null || s.LastReport.CompletedDay != s.LastCompletedDay || s.LastReport.NewDay != s.Day)) throw new ArgumentException("日结报告日期非法");
            if (initialized && s.HasReport != (s.Day > 1)) throw new ArgumentException("日结报告状态非法");
            if (s.HasReport)
            {
                var report = s.LastReport;
                if (new[] { report.FoodBefore, report.FoodProduced, report.MaterialsSpent, report.ProductsMade, report.FoodRequired, report.FoodConsumed, report.FoodMissing, report.FoodAfter }.Any(x => x < 0) || !ProductsFor(s).Contains(report.Product) || report.ProductLevel<0 || report.ProductLevel>2 || !Finite(report.ProductionFactor) || report.ProductionFactor < 0 || report.ProductionFactor > 1 || report.FoodBefore + (long)report.FoodProduced - report.FoodConsumed != report.FoodAfter || report.FoodConsumed + (long)report.FoodMissing != report.FoodRequired) throw new ArgumentException("日结报告收支非法");
            }
            BaseAftermath.Normalize(s);BaseAftermath.Validate(s);BaseAftermath.MarkKnownRecruits(s); BaseMuster.Validate(s); BaseDevelopment.Normalize(s); BaseAdventure.Normalize(s); BaseAdventure.Validate(s); BaseDevelopment.Validate(s); BaseSupply.Validate(s); BaseCampaign.Validate(s); BaseTown.Validate(s); BaseBackpack.Validate(s); BaseConstruction.Validate(s); BaseFacilityLayout.Validate(s); BaseCaravan.Validate(s); BaseQuests.Validate(s); BaseDeputy.Ensure(s); BaseDeputy.Validate(s); BaseSpatial.RepairGridPositions(s);
            BaseMuster.NormalizeCompositions(s);
            BasePersonalEquipment.Validate(s);
            BasePartyInventory.Validate(s);
            BaseTraining.Normalize(s);BaseTraining.Validate(s);
            BaseBattleLoot.Validate(s);
            BaseDemo.Validate(s);BaseAchievements.Validate(s);
            BaseSceneCombat.Validate(s);
        }
        public static int FoodRequired(BaseState s) => checked(s.Workers.Sum(x => x.Count * (x.FacilityId == "caravan" ? 2 : 1) * (x.Kind == BaseTroopKind.Cavalry ? s.Rules.CavalryFood : x.Kind == BaseTroopKind.Ranged && BaseRpg.Active(s) ? s.Demo.Rpg.ArcherFood : s.Rules.SoldierFood)) + s.Leaders.Count(x => x.Health > 0) * (BaseRpg.Active(s)?s.Demo.Rpg.LeaderFood:s.Rules.LeaderFood));
        public static double Hunger(BaseState s) => s.TodayFoodRequired == 0 ? 0 : (double)s.TodayFoodMissing / s.TodayFoodRequired;
        public static double ProductionFactor(BaseState s) => 1 - s.Rules.HungerProductionPenalty * Hunger(s);
        public static double AttackFactor(BaseState s) => 1 - s.Rules.HungerAttackPenalty * Hunger(s);
        public static int WorkersAt(BaseState s, string facility, BaseTroopKind? kind = null) => s.Workers.Where(x => !x.Away && x.FacilityId == facility && (facility == "" || BaseDevelopment.Effective(s, x)) && (!kind.HasValue || x.Kind == kind)).Sum(x => x.Count);
        public static int EligibleWorkers(BaseState s, string facility) => s.Workers.Where(x => BaseDevelopment.Effective(s, x) && x.FacilityId == facility).Sum(x => x.Count);
        public static int CivilianWorkersAt(BaseState s, string facility)
        {
            const int availableCivilians = 2;
            if (!BaseDemo.Active(s) || (facility != "farm" && facility != "workshop") ||
                !BaseDemo.FacilityAvailable(s, "farm")) return 0;

            int farming = Math.Min(availableCivilians, Vacancies("farm"));
            if (facility == "farm") return farming;
            if (farming == availableCivilians || !BaseDemo.FacilityAvailable(s, "workshop")) return 0;
            return Math.Min(availableCivilians - farming, Vacancies("workshop"));

            int Vacancies(string id)
            {
                var post = s.Muster.Facilities.FirstOrDefault(f => f.FacilityId == id);
                return post == null ? 0 : Math.Max(0, post.Capacity - WorkersAt(s, id));
            }
        }
        public static int ProductionWorkersAt(BaseState s, string facility)
            => EligibleWorkers(s, facility) + CivilianWorkersAt(s, facility);
        public static BaseLeader Manager(BaseState s, string facility) => s.Leaders.FirstOrDefault(x => BaseDevelopment.Effective(s, x) && x.FacilityId == facility);
        private static double Bonus(BaseState s, string facility) { var chief = Manager(s, facility); return chief == null ? 1 : 1 + BaseDevelopment.Bonus(chief, facility); }
        public static bool ProductionPaused(BaseState s,string facility) => s.Muster.Facilities.Any(f=>f.FacilityId==facility&&f.DeveloperProductionPaused);
        private static double Work(BaseState s, string facility, double soldierFactor)
        {
            if (ProductionPaused(s, facility) || !BaseDemo.FacilityAvailable(s, facility)) return 0;
            double soldierWork = s.Workers.Where(w => BaseDevelopment.Effective(s, w) && w.FacilityId == facility)
                .Sum(w => w.Count * BaseDevelopment.Efficiency(w, facility));
            return soldierWork * soldierFactor + CivilianWorkersAt(s, facility) * .5;
        }
        public static bool PayMorningFood(BaseState s)
        {
            if (s.FoodPaidDay == s.Day) return false;
            if (s.FoodPaidDay != s.Day - 1) throw new InvalidOperationException("口粮日期不连续");
            s.TodayFoodRequired = FoodRequired(s); s.TodayFoodConsumed = Math.Min(s.Inventory.Food, s.TodayFoodRequired);
            s.TodayFoodMissing = s.TodayFoodRequired - s.TodayFoodConsumed;
            s.Inventory.Food -= s.TodayFoodConsumed; s.FoodPaidDay = s.Day;
            return true;
        }
        public static BaseDayPreview Preview(BaseState s)
        {
            Validate(s);
            double factor = ProductionFactor(s);
            double food = s.FarmFraction + Work(s, "farm", factor) * s.Rules.FarmPerWorker * Bonus(s, "farm");
            double work = BaseConstruction.Progress(s,s.WorkshopProduct).Fraction + Work(s, "workshop", factor) * s.Rules.WorkshopPerWorker * Bonus(s, "workshop") / BaseConstruction.ProductLevel(s);
            int foodWhole = (int)Math.Floor(food + 1e-9), workWhole = (int)Math.Floor(work + 1e-9);
            int made = Math.Min(workWhole, s.Inventory.Materials / BaseConstruction.MaterialCost(s,s.WorkshopProduct)), need = FoodRequired(s);
            int nextFood = checked(s.Inventory.Food + foodWhole);
            return new BaseDayPreview { Day = s.Day, Product = s.WorkshopProduct, ProductLevel=BaseConstruction.ProductLevel(s), ProductionFactor = factor,
                FoodProduced = foodWhole, FarmFraction = Math.Max(0, food - foodWhole), WorkCapacity = workWhole,
                ProductsMade = made, MaterialsSpent = made * BaseConstruction.MaterialCost(s,s.WorkshopProduct), WorkshopFraction = Math.Max(0, work - workWhole),
                FoodRequired = need, NextFood = Math.Max(0, nextFood - need), NextMissing = Math.Max(0, need - nextFood) };
        }
        // expectedDay来自打开休息预览时的日期；同一确认重复到达不能结算第二次。
        public static bool Rest(BaseState s, int expectedDay)
        {
            if(BaseDemoFinale.Rebuilding(s))throw new InvalidOperationException("请先完成返乡重建");
            if(BaseSceneCombat.Active(s)||BaseDemoPrologue.Village(s))throw new InvalidOperationException("当前撤离／交战中不能推进经营日");
            if(s.Aftermath?.GameOver==true)return false;
            if (s.Day != expectedDay || s.LastCompletedDay >= expectedDay) return false;
            if (s.Day >= 1000000) throw new InvalidOperationException("已达日期上限");
            if (s.Muster.Parties.Any(party => party.InCombat)) throw new InvalidOperationException("战斗中不能推进经营日");
            BaseCampaign.Normalize(s);
            if (BaseCampaign.Due(s).Any()) throw new InvalidOperationException("需先处理本日入侵，再结算生产");
            string lesson=BaseDemoFlow.RestProblem(s);if(lesson!="")throw new InvalidOperationException(lesson);
            if(BaseDemoFlow.Recruited(s)&&s.Day>=2)s.Demo.Flow.RestLessonDone=true;
            bool camp=BaseInteraction.At(s,"base","home");CompleteProductionDay(s,true);if(camp)BaseAchievements.Unlock(s,BaseAchievements.Dawn);return true;
        }
        // Economic projection and real rest share production, recovery and morning-food order.
        internal static void CompleteProductionDay(BaseState s,bool advanceThreats)
        {
            if(advanceThreats&&BaseDemoFinale.Rebuilding(s))throw new InvalidOperationException("返乡重建期间不能推进日程");
            if(s.Day>=1000000)throw new InvalidOperationException("已达日期上限");
            BaseDevelopment.Allocate(s);
            var p = Preview(s);
            var report = new BaseDayReport { CompletedDay = s.Day, NewDay = s.Day + 1, FoodBefore = s.Inventory.Food,
                FoodProduced = p.FoodProduced, Product = p.Product, ProductLevel=p.ProductLevel, ProductsMade = p.ProductsMade, MaterialsSpent = p.MaterialsSpent, ProductionFactor = p.ProductionFactor };
            int food = checked(s.Inventory.Food + p.FoodProduced);
            var stock = s.Inventory.Equipment.Single(x => x.Product == p.Product && x.Level == p.ProductLevel);
            int equipment = checked(stock.Count + p.ProductsMade);
            if (food > 100000000 || equipment > 100000000) throw new InvalidOperationException("库存已达上限");
            s.Inventory.Food = food; stock.Count = equipment; s.Inventory.Materials -= p.MaterialsSpent;
            s.FarmFraction = p.FarmFraction; BaseConstruction.Progress(s,p.Product).Fraction = p.WorkshopFraction;
            report.PeopleRecovered = BaseDevelopment.Recover(s);
            s.LastCompletedDay = s.Day; s.Day++;
            BaseTown.AdvanceDay(s);
            BaseRuinsRewards.AdvanceDay(s);
            BaseTraining.AdvanceDay(s);
            if(advanceThreats)BaseThreats.AdvanceDay(s);
            else if(s.Campaign.ThreatVersion==1)s.Campaign.ThreatDay=s.Day; // Projection freezes external armies, not the calendar invariant.
            // 主线走完后的周期流寇：与 BaseThreats 的据点派兵互不干扰，只在 Demo 且 Stage=Finished 时生效。
            if(advanceThreats)BaseDemoRaids.AdvanceDay(s);
            BaseDevelopment.Allocate(s);
            PayMorningFood(s);
            // 商队按新一天结算：本地跑商进金币，黑鸦寨线推进"出发／抵达／回营"；口粮不足则解散。
            BaseCaravan.AdvanceDay(s);
            report.FoodRequired = s.TodayFoodRequired; report.FoodConsumed = s.TodayFoodConsumed; report.FoodMissing = s.TodayFoodMissing; report.FoodAfter = s.Inventory.Food;
            s.LastReport = report; s.HasReport = true;
            if(advanceThreats){BaseDemoDefense.ProductionFinished(s,report);if(report.FoodProduced>0)BaseAchievements.Unlock(s,BaseAchievements.Harvest);}
        }
        public static bool AssignLeader(BaseState s, string leaderId, string facility)
        {
            if(!CanAssignLeader(s,leaderId,facility))return false;
            var leader=s.Leaders.Single(x=>x.Id==leaderId);
            if(facility!=""&&BaseAdventure.Contains(s,leaderId)&&!BaseAdventure.SetMember(s,leaderId,false))return false;
            leader.FacilityId = leader.DesiredFacility = facility; leader.PostLocked = true; return true;
        }
        public static bool CanAssignLeader(BaseState s,string leaderId,string facility)
        {
            if(!Facilities.Contains(facility)||!BaseDemo.FacilityAvailable(s,facility))return false;
            var l=s.Leaders.FirstOrDefault(x=>x.Id==leaderId);
            if(l==null||l.Away)return false;
            if(facility=="")return true;
            if(s.Leaders.Any(x=>x.Id!=leaderId&&x.FacilityId==facility))return false;
            if(BaseAdventure.Contains(s,leaderId))return BaseAdventure.ChangeProblem(s,leaderId,false)==""&&BaseAdventure.Alive(l)&&l.Health>0&&!BaseTraining.Busy(l.Training);
            return BaseDevelopment.CanManage(s,l);
        }
        public static bool MoveWorkers(BaseState s, BaseTroopKind kind, string from, string to, int count)
            => MoveWorkers(s, kind, from, to, count, null);

        internal static bool MoveWorkers(BaseState s, BaseTroopKind kind, string from, string to, int count, int? bucket)
        {
            if (count <= 0 || from == to || !Facilities.Contains(from) || !Facilities.Contains(to)||!BaseDemo.FacilityAvailable(s,to)) return false;
            if (bucket.HasValue && (bucket.Value < 0 || bucket.Value >= s.Workers.Count)) return false;
            var sources = s.Workers.Where((x, index) => (!bucket.HasValue || bucket.Value == index) &&
                !x.Away && x.FacilityId == from && x.Kind == kind && (to == "" || BaseDevelopment.Effective(s, x))).ToArray();
            if (sources.Sum(x => x.Count) < count || to != "" && WorkersAt(s, to) + count > s.Muster.Facilities.Single(f => f.FacilityId == to).Capacity) return false;
            int left = count;
            // 资格随数量批次转移，换岗不能洗掉当天停工状态。
            foreach (var source in sources)
            {
                int n = Math.Min(left, source.Count); if (n == 0) continue;
                var target = source.Copy(); target.FacilityId = target.DesiredFacility = to; target.PostLocked = true; target.Count = n; s.Workers.Add(target);
                source.Count -= n; left -= n; if (left == 0) break;
            }
            BaseDevelopment.Merge(s); return true;
        }
        public static bool SelectProduct(BaseState s, string product) { if (!ProductsFor(s).Contains(product)) return false; s.WorkshopProduct = product; return true; }
    }
}

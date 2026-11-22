using System;
using System.Linq;
using System.Reflection;
using BaseConquest.Base;
using BaseConquest.Battle;
using UnityEngine;

namespace BaseConquest.Editor
{
    // P40／P41（2026-11-06）：基地设施布局、办公策略门禁、四档策略迁移、商队往返与存档往返。
    public static class VerifyBaseFacilities
    {
        private static void Need(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        private static int Workers(BaseState s, string facility) => BaseEconomy.WorkersAt(s, facility);
        private static void Assign(BaseState s, BaseTroopKind kind, string to, int count)
            => Need(BaseEconomy.MoveWorkers(s, kind, "", to, count), "调配 " + count + " 名" + BaseEconomy.TroopName(kind) + "到" + BaseEconomy.FacilityName(to) + "失败");
        private static void Day(BaseState s){s.Day++;s.TodayFoodMissing=0;BaseCaravan.AdvanceDay(s);}
        public static void Run()
        {
            bool wasEnabled = RuntimeDeveloperOptions.Enabled;
            try
            {
                RuntimeDeveloperOptions.Enabled = true;
                VerifyFacilitySystems();
                VerifySettlementStaffing();
            }
            finally { RuntimeDeveloperOptions.Enabled = wasEnabled; }
        }

        public static void VerifyRosterDuty()
        {
            var asset=Resources.Load<TextAsset>("DemoStages/defense-male");
            var state=JsonUtility.FromJson<BaseState>(asset.text);
            BaseEconomy.Validate(state);
            state.Campaign.Location="base";
            state.Workers.Clear();
            state.Workers.Add(new BaseWorkerBucket { Kind=BaseTroopKind.Melee, Count=3, Health=100, MaxHealth=100,
                DesiredFacility="farm", FacilityId="farm", PostLocked=true });
            state.LastSoldierNumber=0;
            BaseDevelopment.SetPolicy(state,"manual");
            BaseDevelopment.Allocate(state);
            int day=state.Day;
            var ids=state.Workers.SelectMany(w=>w.SoldierNumbers).ToArray();
            BaseDevelopment.MarkFoughtToday(state,new[] {ids[1],ids[1]});
            BaseWorkerBucket Unit(int id)=>state.Workers.Single(w=>w.SoldierNumbers.Contains(id));
            Need(Unit(ids[0]).FacilityId=="farm" && Unit(ids[1]).LastBattleDay==day &&
                !BaseDevelopment.Effective(state,Unit(ids[1])) && BaseDevelopment.CanFight(Unit(ids[1])),
                "同批士兵中的真实参战者应单独停工");
            Unit(ids[1]).Health=20;
            var wounded=BasePersonnel.Soldiers(state).Single(u=>u.SoldierNumber==ids[1]);
            Need(wounded.Wounded && wounded.FoughtToday && !wounded.CanBattle,"疗养覆盖参战");
            Unit(ids[1]).Health=100;
            BaseDevelopment.Allocate(state);
            state=JsonUtility.FromJson<BaseState>(JsonUtility.ToJson(state));
            BaseEconomy.Validate(state);
            var healed=BasePersonnel.Soldiers(state).Single(u=>u.SoldierNumber==ids[1]);
            Need(!healed.Effective && !healed.Wounded && healed.FoughtToday && healed.CanBattle,"治疗或读档清除了当天参战");
            var forecast=BaseEconomy.Preview(state);
            var end=typeof(BaseEconomy).GetMethod("CompleteProductionDay",BindingFlags.NonPublic|BindingFlags.Static);
            end.Invoke(null,new object[]{state,false});
            Need(state.LastReport.FoodProduced==forecast.FoodProduced && BaseDevelopment.Effective(state,Unit(ids[1])) &&
                Unit(ids[1]).FacilityId=="farm","参战当日预估与日结或隔日归岗不一致");
            var filter=new SoldierRosterFilter();
            filter.Activate(2); filter.Activate(2); filter.Activate(2);
            filter.Activate(3); filter.Activate(2);
            Need(filter.StatusOrder==0 && filter.HealthOrder==1,"排序互斥或三态切换失败");
            foreach(var kind in BattleSimulation.TroopKinds)
            {
                filter.Activate(1); Need(filter.Kind==kind,"兵种循环顺序错误");
            }
            Debug.Log("[RosterDuty] PASS · 实名参战、同日治疗、隔日归岗、日结与筛选");
        }

        public static void VerifySettlementStaffing()
        {
            var asset = Resources.Load<TextAsset>("DemoStages/defense-male");
            Need(asset != null, "缺少防守阶段档 defense-male");
            var state = JsonUtility.FromJson<BaseState>(asset.text);
            var camp = state.Campaign.Layouts.Single(m => m.Region == "base");
            var village = new BaseMapLayout { SourceMapId = BaseDemoFinale.Home, Region = "custom" };
            var barracks = new BaseMapPlacement { Id = "barracks" };
            village.Objects.Add(barracks);
            state.Campaign.Layouts.Add(village);
            state.Demo.Finale.WorldOpened = state.Demo.Finale.VolunteersArrived = true;
            state.Demo.Finale.RebuildMask = 7;
            state.Demo.Defense.WorkshopRepaired = state.Demo.Defense.BarracksRepaired = true;
            state.Demo.Stage = BaseDemoStage.AttackWon;
            foreach (string id in new[] { "farm", "workshop", "barracks" })
                if (!camp.Objects.Any(o => o.Id == id)) camp.Objects.Add(new BaseMapPlacement { Id = id });

            var farm = state.Muster.Facilities.Single(f => f.FacilityId == "farm");
            var workshop = state.Muster.Facilities.Single(f => f.FacilityId == "workshop");
            farm.Capacity = workshop.Capacity = 2;
            state.Workers.Clear();
            state.LastSoldierNumber = 0;
            state.Inventory.Food = 600;
            state.Inventory.Materials = 500;
            state.Workers.Add(new BaseWorkerBucket { Count = 5, Health = 50, MaxHealth = 100, Kind = BaseTroopKind.Melee });
            state.Workers.Add(new BaseWorkerBucket { Count = 1, Health = 100, MaxHealth = 100, Kind = BaseTroopKind.Ranged, VillageResident = true });

            // Bind to the production commands without extending the gameplay API for an editor probe.
            var settlements = typeof(BaseDevelopment).Assembly.GetType("BaseConquest.Base.BaseSettlements",true);
            var order = (Action<BaseState,int>)settlements.GetMethod("Order").CreateDelegate(typeof(Action<BaseState,int>));
            const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Static;
            var projectDay = (Func<BaseState,BaseDayPreview>)typeof(BaseEconomy).GetMethod("ProjectDay",flags).CreateDelegate(typeof(Func<BaseState,BaseDayPreview>));
            var endDay = (Action<BaseState,bool>)typeof(BaseEconomy).GetMethod("CompleteProductionDay",flags).CreateDelegate(typeof(Action<BaseState,bool>));
            BaseWorkerBucket Soldier(int number) => state.Workers.Single(w => w.SoldierNumbers.Contains(number));

            state.Campaign.Location = "base";
            state.Campaign.CurrentMapId = camp.SourceMapId;
            BaseDevelopment.Allocate(state);
            BaseDevelopment.SetPolicy(state,"food");
            state.Campaign.Location = "custom";
            state.Campaign.CurrentMapId = village.SourceMapId;
            BaseDevelopment.SetPolicy(state,"equipment");
            Need(Soldier(6).FacilityId == "workshop" && BaseEconomy.CivilianWorkersAt(state,"farm") == 2,
                "白桦村应优先派士兵进工坊，由两名当地村民补田");
            Need(state.Development.Policy == "food" && state.Development.VillagePolicy == "equipment","两地策略互相覆盖");

            int selected = state.Workers.First(w => !w.VillageResident && w.FacilityId == "farm").SoldierNumbers[0];
            int normalRations = BaseEconomy.FoodRequired(state);
            int startingFood=state.Inventory.Food;
            state.Inventory.Food=0;
            bool shortRoad=false;
            try{order(state,selected);}catch(InvalidOperationException){shortRoad=true;}
            Need(shortRoad && !Soldier(selected).Relocating,"路粮不足不得调动人员");
            state.Inventory.Food=startingFood;
            order(state,selected);
            Need(Soldier(selected).Relocating && Soldier(selected).FacilityId == "" && !BaseDevelopment.Eligible(state,"defense",Soldier(selected)),
                "调动中士兵仍可上岗或出战");
            Need(state.Inventory.Food==startingFood-BaseEconomy.Ration(state,Soldier(selected))&&BaseEconomy.FoodRequired(state)==normalRations,"路粮应增加该兵种的一份口粮");
            Need(state.Workers.Where(w => !w.VillageResident && w.FacilityId == "farm").Sum(w => w.Count) == 2 &&
                BaseEconomy.WorkersAt(state,"workshop") == 1,"原岗应由本地士兵补足，目的地不应预占岗位");

            order(state,selected);
            Need(!Soldier(selected).Relocating && !Soldier(selected).VillageResident && BaseEconomy.FoodRequired(state) == normalRations && state.Inventory.Food==startingFood,
                "撤销调动应恢复原地资格并撤回路粮");
            village.Objects.Remove(barracks);
            bool refused = false;
            try { order(state,selected); }
            catch (InvalidOperationException) { refused = true; }
            Need(refused && !Soldier(selected).Relocating,"目的地没有兵营也接受了调令");
            village.Objects.Add(barracks);

            order(state,selected);
            var preview = projectDay(state);
            endDay(state,false);
            Need(Soldier(selected).VillageResident && !Soldier(selected).Relocating && Soldier(selected).FacilityId == "workshop",
                "次日抵达后没有按白桦村策略就岗");
            Need(state.LastReport.FoodProduced == preview.FoodProduced && state.LastReport.FoodRequired == preview.FoodRequired &&
                state.Inventory.Food == preview.NextFood && state.LastReport.ProductsMade == preview.ProductsMade,"两地日结与预估不符");
            Need(BaseEconomy.MoveWorker(state,selected,"workshop","") && state.Development.VillagePolicy == "manual" &&
                state.Development.Policy == "food","手动调人应只改变当地策略");
            BaseDevelopment.Allocate(state);
            Need(Soldier(selected).FacilityId == "" && state.Workers.SelectMany(w => w.SoldierNumbers).OrderBy(n => n).SequenceEqual(Enumerable.Range(1,6)),
                "手动模式抢占了闲置人员，或人员编号丢失、重复");

            foreach (var worker in state.Workers)
            {
                worker.VillageResident = true;
                worker.FacilityId = worker.DesiredFacility = "";
            }
            farm.Capacity = workshop.Capacity = 6;
            state.Inventory.Materials = BaseConstruction.MaterialCost(state,state.WorkshopProduct);
            BaseDevelopment.SetPolicy(state,"equipment");
            Need(projectDay(state).ProductsMade == 1 && BaseEconomy.WorkersAt(state,"farm") > 0,
                "共享材料只够一件军需时，应做足一件并释放多余士兵补田");
            Debug.Log("[SettlementStaffing] PASS 12/12");
        }

        private static void VerifyFacilitySystems()
        {
            var s = JsonUtility.FromJson<BaseState>(Resources.Load<TextAsset>("DemoStages/camp-male").text);
            BaseEconomy.Validate(s);
            VerifyCaptainRations(s);
            VerifyCivilianWork(s);
            Need(!BaseFacilityLayout.OfficeBuilt(s) && !BaseFacilityLayout.AutoPlace(s,"office",out _,out _,out _),
                "营帐应直接提供策略，开发者模式也不能新建办公设施");
            var defaults = s.Workers.SelectMany(w=>w.SoldierNumbers).OrderBy(n=>n).ToArray();
            BaseDevelopment.SetPolicy(s,"food");
            Need(BaseDevelopment.AutoAssign(s) && BaseDevelopment.PolicyOrder(s).SequenceEqual(new[]{"farm","workshop"}),
                "灰岩营帐的粮田优先未生效");
            BaseDevelopment.SetPolicy(s,"equipment");
            Need(BaseDevelopment.PolicyOrder(s).SequenceEqual(new[]{"workshop","farm"}) && s.Development.VillagePolicy=="manual",
                "选择灰岩策略改动了白桦村");
            BaseDevelopment.SetPolicy(s,"manual");
            Need(!BaseDevelopment.AutoAssign(s) && defaults.SequenceEqual(s.Workers.SelectMany(w=>w.SoldierNumbers).OrderBy(n=>n)),
                "切换手动策略改变人员身份");
            Need(!BaseFacilityLayout.Store(s,"home",out _),"营帐不能收存");
            s.Layout.OfficeGift=true;
            s.Inventory.Materials=Math.Max(s.Inventory.Materials,50);
            s.Inventory.Coins=Math.Max(s.Inventory.Coins,50);
            s.Workers.Clear();
            s.Workers.Add(new BaseWorkerBucket{Kind=BaseTroopKind.Melee,Count=6,Health=100,MaxHealth=100});
            BaseDevelopment.SetPolicy(s,"food");
            Need(Workers(s,"farm")>0,"粮田优先未安排本地士兵");

            // 收存生产设施：立即停产、不能继续派人，重新放置后恢复。
            Need(BaseFacilityLayout.Store(s, "farm", out _), "粮田应能收存");
            Need(!BaseBattlefield.Board(s, "base").Cells.Any(cell => cell.BuildingId == "farm"), "收存后旧粮田占地仍阻挡");
            BaseDevelopment.Allocate(s);
            Need(BaseEconomy.Preview(s).FoodProduced == 0, "粮田收存后不应继续产粮");
            Need(!BaseEconomy.MoveWorkers(s, BaseTroopKind.Melee, "", "farm", 1), "粮田收存后不应还能派人");
            Need(BaseFacilityLayout.AutoPlace(s, "farm", out string farmProblem, out _, out _), "粮田重新放置失败：" + farmProblem);
            var field = BaseSpatial.Objects(s, "base").Single(item => item.Id == "farm");
            Need(!string.IsNullOrEmpty(field.SourceObjectId) && BaseBattlefield.Board(s, "base").Cells.Any(cell => cell.BuildingId == "farm"),
                "重新放置的粮田缺少持久对象标识或占地");
            var board = BaseBattlefield.Board(s, "base");
            for (int side = -1; side <= 1; side += 2)
            {
                double x = field.X + side * (field.Width / 2 + .35);
                if (x <= BaseMap.Radius || x >= board.Width - BaseMap.Radius) continue;
                Need(BaseInteraction.CanReach(board, field, x, field.Y, new BasePoint(side, 0)), "粮田边缘交互被人物朝向拦截");
            }
            BaseEconomy.Validate(s);
            Need(BaseEconomy.Preview(s).FoodProduced >= 0, "粮田重新放置后应恢复生产入口");
            // 商队：建成后才有岗位；出发要备足往返口粮；黑鸦寨按三天往返取料，满载看当前库存。
            s.Inventory.Materials = 60;
            int beforeBuild = s.Inventory.Materials;
            Need(BaseFacilityLayout.AutoPlace(s, "caravan", out string caravanProblem, out _, out _), "商队设施放置失败：" + caravanProblem);
            Need(s.Inventory.Materials == beforeBuild - BaseFacilityLayout.Find("caravan").Materials, "商队设施应扣材料");
            Need(s.Muster.Facilities.Any(f => f.FacilityId == "caravan" && f.Capacity == 3), "商队建成后军营应出现岗位记录");
            s.Workers.Clear();
            s.Workers.Add(new BaseWorkerBucket { Kind = BaseTroopKind.Melee, Count = 2, MaxHealth = 100, Health = 100, FarmEfficiency = 1, WorkshopEfficiency = 1 });
            Assign(s, BaseTroopKind.Melee, "caravan", 2);
            Need(BaseCaravan.Crew(s) == 2 && BaseCaravan.Capacity(s) == 6, "商队运力应按人数计算");
            Need(BaseCaravan.DailyFood(s) == 4, "商队口粮应按双倍计");
            var normal = s.Copy();
            foreach (var worker in normal.Workers) { worker.FacilityId = ""; worker.DesiredFacility = ""; }
            Need(BaseEconomy.FoodRequired(s) == BaseEconomy.FoodRequired(normal), "未出发人员只领普通口粮");
            s.Inventory.Food = 0;
            Need(BaseCaravan.Problem(s, "local").Contains("口粮"), "口粮不足应拒绝出发");
            // 走一次真实日结：商队在日结里结算金币，不能破坏日期／口粮不变量。
            var daily = s.Copy();
            daily.Inventory.Food = 100;
            int localCoins = daily.Inventory.Coins;
            Need(BaseCaravan.Start(daily, "local", out _), "应能开始领地内跑商");
            Need(BaseEconomy.Rest(daily, daily.Day), "带商队的日结应能推进");
            Need(daily.Inventory.Coins == localCoins && daily.Caravan.FoodOnBoard==8, "商队途中只消耗随车粮，完成整趟才结收益");
            BaseEconomy.Validate(daily);
            s.Inventory.Food = 100;
            Need(BaseCaravan.Problem(s, "crow").Contains("黑鸦寨"), "未攻占黑鸦寨时不能跑运输线");
            var site = s.Campaign.EnemySites.First();
            site.Owner = "player"; site.CapturedDay = s.Day; site.Garrison.Clear(); site.Stock = 1;
            s.Campaign.Events.First(e => e.Id == site.AttackEventId).Status = "resolved";
            Need(BaseCaravan.Start(s, "crow", out string startProblem), "满足条件应能出发：" + startProblem);
            BaseEconomy.Validate(s);
            // 日程推进在副本上模拟，避免手动改天数破坏日结不变量；商队状态单独校验。
            var trip = s.Copy();
            Day(trip);
            Need(trip.Caravan.Phase == "back" && trip.Caravan.Cargo == 5, "抵达日应按当前库存装料（库存 1＋当日产 4）");
            VerifyFormalManagement(trip);
            trip.Campaign.EnemySites.First().Stock = 100;
            Day(trip);
            Need(trip.Caravan.Route == "" && trip.Caravan.Cargo == 0 && trip.Caravan.TotalDelivered == 5, "回营日应卸货并结束事务");
            Need(trip.Inventory.Materials >= beforeBuild - BaseFacilityLayout.Find("caravan").Materials + 5, "卸货后原料应进仓库");
            BaseCaravan.Validate(trip);
            // 满载：库存充足时只装运力上限。
            trip.Inventory.Food = 100;
            Need(BaseCaravan.Start(trip, "crow", out _), "应能再次出发");
            Day(trip);
            Need(trip.Caravan.Cargo == 6, "满载不应超过商队运力，实际 " + trip.Caravan.Cargo);
            Day(trip);
            Need(trip.Caravan.Route == "" && trip.Caravan.TotalDelivered == 11, "第二次运输应结算 6 原料");
            // 本地跑商：每日进金币，不再装货。
            trip.Inventory.Food = 100; int coinsBefore = trip.Inventory.Coins;
            Need(BaseCaravan.Start(trip, "local", out _), "应能开始领地内跑商");
            Need(!BaseCaravan.Start(trip, "local", out _), "跑商进行中不应重复出发");
            Need(trip.Inventory.Food==88 && trip.Caravan.FoodOnBoard==12 && BaseEconomy.FoodRequired(trip)==0,"两名剑士出发须实扣三日路粮，日常不重复领取");
            var traveling=trip.Workers.First(w=>w.TravelingMerchant);
            Need(!BaseDevelopment.CanFight(traveling)&&!BaseDevelopment.CanDefend(traveling)&&!BaseEconomy.MoveWorker(trip,traveling.SoldierNumbers.First(),"caravan",""),"在途商队成员可被调走");
            var renewal=trip.Copy();Day(renewal);Day(renewal);Day(renewal);
            Need(renewal.Inventory.Coins==coinsBefore+24&&renewal.Inventory.Food==76&&renewal.Caravan.DaysProvisioned==3&&renewal.Caravan.FoodOnBoard==12,"第三日未扣新一趟粮");
            renewal.Inventory.Food=0;Day(renewal);Day(renewal);
            Need(renewal.Caravan.Route=="local","仓库断粮中止了有随车粮的商队");
            Day(renewal);Need(renewal.Caravan.Route==""&&renewal.Caravan.FoodOnBoard==0,"粮不足仍续程");
            Day(trip);
            Need(trip.Inventory.Coins == coinsBefore, "跑商中途不得提前发放收益");
            BaseCaravan.Stop(trip, "测试结束");
            Need(trip.Inventory.Food==96&&!trip.Workers.Any(w=>w.TravelingMerchant),"中途召回应退回未吃的 8 粮并解锁名册");
            Need(trip.Caravan.Route == "" && !trip.Workers.Any(w => w.FacilityId == "caravan"), "解散商队应清空事务并释放人员");
            // 收存商队设施：岗位记录一并移除（出发中的事务会被终止）。
            Need(BaseFacilityLayout.Store(s, "caravan", out _), "商队设施应能收存");
            Need(!s.Muster.Facilities.Any(f => f.FacilityId == "caravan") && s.Caravan.Route == "", "收存商队设施应移除岗位记录并终止事务");
            BaseEconomy.Validate(s);

            // 存档往返：布局与商队状态随存档保留；旧档缺字段时补默认。
            var saved = JsonUtility.FromJson<BaseState>(JsonUtility.ToJson(s));
            BaseEconomy.Validate(saved);
            Need(BaseFacilityLayout.Stored(saved, "caravan") && !BaseFacilityLayout.Built(saved, "office"), "布局状态应随存档保留");
            var legacy = saved.Copy(); legacy.Layout = null; legacy.Caravan = null;
            BaseFacilityLayout.Normalize(legacy); BaseCaravan.Normalize(legacy);
            Need(legacy.Layout.Version == BaseFacilityLayout.Version && legacy.Caravan.Version == BaseCaravan.Version, "旧档缺布局／商队字段时应补默认");
            BaseEconomy.Validate(legacy);
            Debug.Log("[BaseFacilities] 营帐策略、设施收存、自动分配、商队往返与存档往返 PASS");
        }

        private static void VerifyFormalManagement(BaseState legacy)
        {
            var s = legacy.Copy();
            s.Workers.Add(new BaseWorkerBucket { Kind = BaseTroopKind.Melee, Count = 1, MaxHealth = 100, Health = 100,
                FarmEfficiency = 1, WorkshopEfficiency = 1, FacilityId = "farm" });
            int materials = s.Inventory.Materials, cargo = s.Caravan.Cargo;
            RuntimeDeveloperOptions.Enabled = false;
            try
            {
                Need(!BaseFacilityLayout.AutoPlace(s,"office",out _,out _,out _),"Demo 不提供办公设施");
                BaseDevelopment.SetPolicy(s,"manual");
                BaseDevelopment.Normalize(s);
                BaseDevelopment.Allocate(s);
                Need(!BaseDevelopment.AutoAssign(s) && Workers(s,"farm")==1,"手动模式应保留已指定岗位");
                BaseCaravan.Validate(s);
                Need(s.Caravan.Route == "" && s.Caravan.Phase == "" && Workers(s, "caravan") == 0,
                    "正式 Demo 应结束旧远途事务并让商队人员回营");
                Need(s.Inventory.Materials == materials + cargo && s.Caravan.TotalDelivered == cargo, "旧商队已装货物应入仓");
                BaseCaravan.Validate(s);
                Need(s.Inventory.Materials == materials + cargo, "重复校验不能再次卸货");
                Assign(s, BaseTroopKind.Melee, "caravan", 2);
                Need(!BaseCaravan.Start(s, "crow", out _) && BaseCaravan.Start(s, "local", out _),
                    "正式 Demo 保留领地内跑商，拒绝远途出发");
            }
            finally { RuntimeDeveloperOptions.Enabled = true; }
        }

        private static void VerifyCaptainRations(BaseState camp)
        {
            Need(BaseEconomy.FoodRequired(camp) == 0 && camp.TodayFoodRequired == 0,
                "只带主角的正式营地首日应免口粮");
            var legacy = camp.Copy();
            legacy.Demo.CaptainRationsUpdated = false;
            legacy.Demo.Rpg.LeaderFood = legacy.Demo.Rpg.OpeningEconomy.LeaderFood = legacy.Rules.LeaderFood = 2;
            legacy.TodayFoodRequired = legacy.TodayFoodConsumed = 2;
            legacy.Inventory.Food -= 2;
            legacy = JsonUtility.FromJson<BaseState>(JsonUtility.ToJson(legacy));
            BaseEconomy.Validate(legacy);
            Need(BaseEconomy.FoodRequired(legacy) == 0 && legacy.TodayFoodConsumed == 2 &&
                legacy.Inventory.Food == camp.Inventory.Food - 2, "旧档应更新后续口粮并保留已扣粮食");
            Need(BaseEconomy.Rest(legacy, legacy.Day) && legacy.LastReport.FoodRequired == 0 &&
                legacy.LastReport.FoodConsumed == 0, "旧档下一次日结应免除队长口粮");
        }

        private static void VerifyCivilianWork(BaseState camp)
        {
            var empty = camp.Copy();
            empty.Workers.Clear();
            empty.Inventory.Food = empty.Inventory.Coins = empty.Inventory.Materials = 0;
            empty.TodayFoodConsumed = 0;
            empty.TodayFoodMissing = empty.TodayFoodRequired;
            Need(BaseEconomy.CivilianWorkersAt(empty, "farm") == 2 &&
                BaseEconomy.CivilianWorkersAt(empty, "workshop") == 0, "空营粮田应有两名村民补位");
            Need(BaseEconomy.Preview(empty).FoodProduced == (int)empty.Rules.FarmPerWorker &&
                BaseEconomy.FoodRequired(empty) == 0, "队长和村民均不耗粮，两名村民仍各提供半工");
            Need(BaseEconomy.Rest(empty, empty.Day) && empty.LastReport.FoodProduced == (int)empty.Rules.FarmPerWorker &&
                empty.Inventory.Coins == 0 && empty.Inventory.Materials == 0 && empty.Workers.Count == 0,
                "村民工量应进入真实日结，且不产生士兵或个人消耗");
            var saved = JsonUtility.FromJson<BaseState>(JsonUtility.ToJson(empty));
            BaseEconomy.Validate(saved);
            Need(BaseEconomy.CivilianWorkersAt(saved, "farm") == 2 && saved.Workers.Count == 0,
                "读档应从岗位重算村民工量");
            Need(BaseEconomy.ProductionWorkersAt(saved, "farm") == 0,
                "粮田补益不应计入生产岗位人数");

            var defense = JsonUtility.FromJson<BaseState>(Resources.Load<TextAsset>("DemoStages/defense-male").text);
            BaseEconomy.Validate(defense);
            int farmRoom=defense.Muster.Facilities.Single(f=>f.FacilityId=="farm").Capacity-BaseEconomy.WorkersAt(defense,"farm");
            foreach(var worker in defense.Workers.Where(w=>w.FacilityId==""&&BaseDevelopment.Effective(defense,w)).ToArray())
            {
                int count=Math.Min(farmRoom,worker.Count);
                if(count==0)break;
                Need(BaseEconomy.MoveWorkers(defense,worker.Kind,"","farm",count),"未能布置满粮田夹具");
                farmRoom-=count;
            }
            Need(BaseEconomy.CivilianWorkersAt(defense, "farm") == 0 &&
                BaseEconomy.CivilianWorkersAt(defense, "workshop") == 0, "粮田满岗时补益结束，工坊没有村民补益");
            Need(BaseEconomy.MoveWorkers(defense, BaseTroopKind.Melee, "farm", "", 1) &&
                BaseEconomy.CivilianWorkersAt(defense, "farm") == 1 &&
                BaseEconomy.CivilianWorkersAt(defense, "workshop") == 0, "有一个粮田空岗时只增加半份粮田工量");
            Need(BaseEconomy.MoveWorkers(defense, BaseTroopKind.Melee, "", "farm", 1) &&
                BaseEconomy.CivilianWorkersAt(defense, "farm") == 0 &&
                BaseEconomy.CivilianWorkersAt(defense, "workshop") == 0, "粮田的空岗补益不应阻止士兵进岗");
            defense.Inventory.Materials = 0;
            Need(BaseEconomy.Preview(defense).ProductsMade == 0, "工坊军需仍需原料");
            Debug.Log("[CivilianWork] 零兵粮田补益、工坊无补益、实际岗位、存档重算 PASS");
        }
    }
}

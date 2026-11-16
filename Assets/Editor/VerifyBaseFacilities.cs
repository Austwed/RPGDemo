using System;
using System.Linq;
using BaseConquest.Base;
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
            var s = JsonUtility.FromJson<BaseState>(Resources.Load<TextAsset>("DemoStages/camp-male").text);
            BaseEconomy.Validate(s);
            VerifyCaptainRations(s);
            VerifyCivilianWork(s);
            Need(!BaseFacilityLayout.OfficeBuilt(s), "阶段档不应预先拥有办公设施");
            Need(BaseDevelopment.EffectivePolicy(s) == "manual", "办公设施未建成时应按手动执行");
            bool rejected = false;
            try { BaseDevelopment.SetPolicy(s, "auto"); } catch (InvalidOperationException) { rejected = true; }
            Need(rejected, "办公设施未建成时不允许切换到自动策略");
            Need(!s.Workers.Any(w => w.FacilityId == "caravan"), "未建成商队时不应有人占商队岗位");

            // 旧策略迁移：均衡→自动分配、自定优先→手动分配。
            var migrated = s.Copy(); migrated.Development.Version = 2; migrated.Development.Policy = "balanced";
            BaseDevelopment.Normalize(migrated);
            Need(migrated.Development.Version == 3 && migrated.Development.Policy == "auto", "均衡应迁移为自动分配");
            migrated.Development.Version = 2; migrated.Development.Policy = "custom";
            BaseDevelopment.Normalize(migrated);
            Need(migrated.Development.Policy == "manual", "自定优先应迁移为手动分配");
            migrated.Development.Version = 2; migrated.Development.Policy = "food";
            BaseDevelopment.Normalize(migrated);
            Need(migrated.Development.Policy == "food", "粮食优先应保留原值");

            // 办公设施：免费放置、可收存、收存后保留策略设置但按手动执行。
            s.Inventory.Materials = Math.Max(s.Inventory.Materials, 50); s.Inventory.Coins = Math.Max(s.Inventory.Coins, 50);
            int materials = s.Inventory.Materials, coins = s.Inventory.Coins;
            Need(BaseFacilityLayout.Problem(s, "office", 2, 2).Contains("建设许可"), "未领取许可时不能放置办公设施");
            s.Layout.OfficeGift = true;
            Need(BaseFacilityLayout.AutoPlace(s, "office", out string officeProblem, out _, out _), "办公设施放置失败：" + officeProblem);
            Need(BaseFacilityLayout.OfficeBuilt(s), "办公设施应已放置");
            Need(s.Inventory.Materials == materials && s.Inventory.Coins == coins, "赠送的办公设施不应扣材料或钱币");
            BaseEconomy.Validate(s);
            BaseDevelopment.SetPolicy(s, "food");
            Need(s.Development.Policy == "food" && BaseDevelopment.AutoAssign(s), "办公设施建成后应能选择粮食优先");
            Need(BaseFacilityLayout.Store(s, "office", out _) && !BaseFacilityLayout.OfficeBuilt(s), "办公设施应能收存");
            Need(s.Development.Policy == "manual" && s.Layout.PausedPolicy == "food", "收存办公设施应暂停策略并保留设置");
            Need(BaseDevelopment.EffectivePolicy(s) == "manual" && !BaseDevelopment.AutoAssign(s), "收存办公设施后应按手动执行");
            Need(!BaseFacilityLayout.Store(s, "home", out string homeProblem) && homeProblem == "必需设施不能收存", "营帐属于必需设施，不能收存");
            Need(BaseFacilityLayout.AutoPlace(s, "office", out _, out _, out _) && s.Inventory.Materials == materials, "重新放置已拥有的办公设施不应再次扣费");
            Need(s.Development.Policy == "manual" && s.Layout.PausedPolicy == "food", "重新放置办公设施不应自动恢复策略");

            // 自动分配：有缺口时先排粮田；工坊要留够三天原料；商队吃双份只在有盈余时排人。
            s.Workers.Clear();
            s.Workers.Add(new BaseWorkerBucket { Kind = BaseTroopKind.Melee, Count = 6, MaxHealth = 100, Health = 100, FarmEfficiency = 1, WorkshopEfficiency = 1 });
            BaseDevelopment.SetPolicy(s, "auto");
            Need(s.Layout.PausedPolicy == "", "确认策略后应清掉暂停值");
            BaseDevelopment.Allocate(s);
            Need(Workers(s, "farm") > 0, "自动分配应先保证粮食生产");
            Need(Workers(s, "farm") + Workers(s, "workshop") + Workers(s, "caravan") <= 6, "自动分配不能超过可用人数");
            s.Inventory.Materials = 0;
            BaseDevelopment.Allocate(s);
            Need(Workers(s, "workshop") == 0, "原料不够三天时不应继续往工坊排人");
            Need(BaseDevelopment.PolicyOrder(s).SequenceEqual(new[] { "farm", "workshop", "caravan" }), "粮食优先的顺序应为粮田→工坊→商队");
            BaseDevelopment.SetPolicy(s, "equipment");
            Need(BaseDevelopment.PolicyOrder(s).SequenceEqual(new[] { "workshop", "farm", "caravan" }), "军需优先的顺序应为工坊→粮田→商队");

            // 收存生产设施：立即停产、不能继续派人，重新放置后恢复。
            Need(BaseFacilityLayout.Store(s, "farm", out _), "粮田应能收存");
            BaseDevelopment.Allocate(s);
            Need(BaseEconomy.Preview(s).FoodProduced == 0, "粮田收存后不应继续产粮");
            Need(!BaseEconomy.MoveWorkers(s, BaseTroopKind.Melee, "", "farm", 1), "粮田收存后不应还能派人");
            Need(BaseFacilityLayout.AutoPlace(s, "farm", out string farmProblem, out _, out _), "粮田重新放置失败：" + farmProblem);
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
            Need(BaseEconomy.FoodRequired(s) - BaseEconomy.FoodRequired(normal) == 2, "商队人员应比在岗多耗一份口粮");
            s.Inventory.Food = 0;
            Need(BaseCaravan.Problem(s, "local").Contains("口粮"), "口粮不足应拒绝出发");
            // 走一次真实日结：商队在日结里结算金币，不能破坏日期／口粮不变量。
            var daily = s.Copy();
            daily.Inventory.Food = 100;
            int localCoins = daily.Inventory.Coins;
            Need(BaseCaravan.Start(daily, "local", out _), "应能开始领地内跑商");
            Need(BaseEconomy.Rest(daily, daily.Day), "带商队的日结应能推进");
            Need(daily.Inventory.Coins == localCoins + 8, "日结后本地跑商应结算金币");
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
            Day(trip);
            Need(trip.Inventory.Coins == coinsBefore + 8, "本地跑商应按 4 金币／人结算");
            BaseCaravan.Stop(trip, "测试结束");
            Need(trip.Caravan.Route == "" && !trip.Workers.Any(w => w.FacilityId == "caravan"), "解散商队应清空事务并释放人员");
            // 收存商队设施：岗位记录一并移除（出发中的事务会被终止）。
            Need(BaseFacilityLayout.Store(s, "caravan", out _), "商队设施应能收存");
            Need(!s.Muster.Facilities.Any(f => f.FacilityId == "caravan") && s.Caravan.Route == "", "收存商队设施应移除岗位记录并终止事务");
            BaseEconomy.Validate(s);

            // 存档往返：布局与商队状态随存档保留；旧档缺字段时补默认。
            var saved = JsonUtility.FromJson<BaseState>(JsonUtility.ToJson(s));
            BaseEconomy.Validate(saved);
            Need(BaseFacilityLayout.Stored(saved, "caravan") && BaseFacilityLayout.Built(saved, "office"), "布局状态应随存档保留");
            var legacy = saved.Copy(); legacy.Layout = null; legacy.Caravan = null;
            BaseFacilityLayout.Normalize(legacy); BaseCaravan.Normalize(legacy);
            Need(legacy.Layout.Version == BaseFacilityLayout.Version && legacy.Caravan.Version == BaseCaravan.Version, "旧档缺布局／商队字段时应补默认");
            BaseEconomy.Validate(legacy);
            Debug.Log("[BaseFacilities] 办公门禁、策略迁移、设施收存、自动分配、商队往返与存档往返 PASS");
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
            var travel = new BaseWorkerTravel();
            var tent = BaseStageWalk.Door(camp, "home");
            var farm = BaseStageWalk.Door(camp, "farm");
            travel.EnqueueCivilians(camp, "home", "farm", 1);
            travel.EnqueueCivilians(camp, "farm", "home", 1);
            travel.Tick(camp, 0);
            var outbound = travel.Active.Single();
            Need(outbound.AppearanceId == "villager" && outbound.Position.Distance(tent) < .01 &&
                outbound.Route.Last().Distance(farm) < .08, "村民应沿营地通路从营帐走到粮田");
            travel.Tick(camp, .4);
            var returning = travel.Active.Single(journey => journey != outbound);
            Need(returning.AppearanceId == "villager" && returning.Route.Last().Distance(tent) < .08,
                "调回村民应走回营帐");

            var defense = JsonUtility.FromJson<BaseState>(Resources.Load<TextAsset>("DemoStages/defense-male").text);
            BaseEconomy.Validate(defense);
            Need(BaseEconomy.CivilianWorkersAt(defense, "farm") == 0 &&
                BaseEconomy.CivilianWorkersAt(defense, "workshop") == 2, "粮田已被士兵填满时村民应补到工坊");
            Need(BaseEconomy.MoveWorkers(defense, BaseTroopKind.Melee, "farm", "", 1) &&
                BaseEconomy.CivilianWorkersAt(defense, "farm") == 1 &&
                BaseEconomy.CivilianWorkersAt(defense, "workshop") == 1, "两处同时有空岗时应先补足粮田");
            Need(BaseEconomy.MoveWorkers(defense, BaseTroopKind.Melee, "", "farm", 1) &&
                BaseEconomy.CivilianWorkersAt(defense, "farm") == 0 &&
                BaseEconomy.CivilianWorkersAt(defense, "workshop") == 2, "士兵应能调入被村民补位的岗位");
            defense.Inventory.Materials = 0;
            Need(BaseEconomy.Preview(defense).ProductsMade == 0, "工坊有村民时仍须原料才能生产军需");
            Debug.Log("[CivilianWork] 零兵零库存日结、派工优先、工坊原料、存档重算、营帐到岗路线 PASS");
        }
    }
}

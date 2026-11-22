using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BaseConquest.Base;
using BaseConquest.Battle;
using UnityEngine;

namespace BaseConquest.Editor
{
    public static class VerifyCampDialogueAndRoster
    {
        public static void Run()
        {
            var facing = BaseInteraction.Facing;
            try
            {
                var state = OpenCamp();
                VerifyFarmOffer(state.Copy());
                VerifyDuelAndRoster(state.Copy());
                VerifyConstructionGate(state.Copy());
                Debug.Log("[VerifyCampDialogueAndRoster] PASS farm offer proves reach after committing worker plan; Azhu win, loss, and abandonment; leaving frees a slot and preserves location; Old Zhou permit gates");
            }
            finally { BaseInteraction.Facing = facing; }
        }

        private static BaseState OpenCamp()
        {
            string raw = File.ReadAllText(Path.Combine(Application.streamingAssetsPath, "BaseScenarios", BaseDemo.BalanceTemplate));
            var state = DemoStageSaveTools.Camp(raw, "male", DemoBalanceRuntime.Load().Rules);
            Approach(state, "home");
            Need(BaseDemo.AcknowledgeWaking(state), "无法完成营地苏醒");
            VerifyBanditHideout.AddSoldiers(state, 2, 1, 0);
            BaseDevelopment.SetPolicy(state, "manual");
            BaseDevelopment.Allocate(state);
            Need(state.Workers.Sum(worker => worker.SoldierNumbers.Count) == 3, "测试兵没有完成编号和岗位初始化");
            return state;
        }

        private static void VerifyFarmOffer(BaseState state)
        {
            Approach(state, "farm");
            var worker = state.Workers.FirstOrDefault(w => !w.Away && w.FacilityId != "farm");
            if (worker == null)
            {
                var assigned = state.Workers.First(w => !w.Away && w.FacilityId == "farm");
                int assignedNumber = assigned.SoldierNumbers.First();
                Need(BaseEconomy.MoveWorker(state, assignedNumber, "farm", ""), "无法准备空闲士兵");
                worker = state.Workers.Single(w => w.SoldierNumbers.Contains(assignedNumber));
            }

            int number = worker.SoldierNumbers.First();
            int before = BaseEconomy.WorkersAt(state, "farm");
            var move = new KeyValuePair<int, (BaseTroopKind kind, string from, string to)>(
                number, (worker.Kind, worker.FacilityId, "farm"));
            Call<object>(typeof(BaseHud), "ApplyNamedTransfers", state, new[] { move });
            Need(BaseEconomy.WorkersAt(state, "farm") == before + 1 && state.Demo.Flow.FarmOfferPending,
                "没有通过正式派工路径移动士兵并登记阿禾引导");

            var flow = state.Demo.Flow;
            flow.FarmWalking = true;
            flow.FarmNpcX = flow.FarmX;
            flow.FarmNpcY = flow.FarmY;
            var marker = BaseSpatial.Objects(state).Single(o => o.Id == "demo_ahe");
            var candidates = new[]
            {
                new BasePoint(marker.X + .8, marker.Y), new BasePoint(marker.X - .8, marker.Y),
                new BasePoint(marker.X, marker.Y + .8), new BasePoint(marker.X, marker.Y - .8)
            };
            var board = BaseBattlefield.Board(state);
            var point = candidates.First(p => BaseInteraction.WithinReach(board, marker, p.X, p.Y));
            state.PlayerX = point.X;
            state.PlayerY = point.Y;
            BaseInteraction.Walk(new BasePoint(point.X - marker.X, point.Y - marker.Y));
            Need(!Call<bool>(typeof(BaseDemoFlow), "TryShowFarmOffer", state, false) &&
                flow.FarmOfferPending && !flow.FarmOfferShown && BaseEconomy.WorkersAt(state, "farm") == before + 1,
                "背对阿禾时应保留待触发状态和已确认的派工");
            BaseInteraction.Walk(new BasePoint(marker.X - point.X, marker.Y - point.Y));
            Need(Call<bool>(typeof(BaseDemoFlow), "TryShowFarmOffer", state, false) &&
                !flow.FarmOfferPending && flow.FarmOfferShown && state.Demo.MetCompanions.Contains("partner_farm"),
                "可交谈时没有正确消费引导");
            Need(!Call<bool>(typeof(BaseDemoFlow), "TryShowFarmOffer", state, false), "同一次派工重复播阿禾引导");
        }

        private static void VerifyDuelAndRoster(BaseState state)
        {
            Approach(state, "exit_east");
            Need(BaseDemoRuins.Travel(state, "town"), "未到灰岩镇");
            Approach(state, "demo_azhu");
            BaseLocalDuel.Create(state);
            var abandoned = state.Copy();
            Need(!Call<bool>(typeof(BaseLocalDuel), "RecordResult", abandoned, BattleOutcome.None) &&
                !abandoned.Demo.Flow.AzhuDuelCompleted, "放弃切磋提前结识阿铸");
            BaseLocalDuel.Create(abandoned);
            var won = state.Copy();
            Need(Call<bool>(typeof(BaseLocalDuel), "RecordResult", won, BattleOutcome.SideAWin) &&
                won.Demo.Flow.AzhuDuelWon && won.Demo.Flow.AzhuDuelCompleted, "胜利没有保留真实胜负");
            Need(Call<bool>(typeof(BaseLocalDuel), "RecordResult", state, BattleOutcome.SideBWin) &&
                !state.Demo.Flow.AzhuDuelWon && state.Demo.Flow.AzhuDuelCompleted &&
                state.Demo.MetCompanions.Contains("partner_craft"), "落败没有结识阿铸或被误记为胜利");
            var legacy = abandoned.Copy();
            legacy.Demo.Flow.AzhuDuelWon = true;
            Need(Call<bool>(typeof(BaseLocalDuel), "Completed", legacy), "旧切磋胜利档丢失完成资格");
            Need(BaseDemoRuins.Join(state, "partner_craft") && BaseAdventure.Following(state, "partner_craft"),
                "自然落败后未开放真实招募");
            Approach(state, "travel_base");
            Need(BaseDemoRuins.Travel(state, "base"), "未从灰岩镇返回营地");

            var azhu = state.Leaders.Single(person => person.Id == "partner_craft");
            var third = azhu.Copy();
            third.Id = "fixture_captain";
            third.PersonalEquipment.Items.Clear();
            third.Health = third.MaxHealth = BasePersonalEquipment.Stats(third).Health;
            if (third.Rpg != null) third.Rpg.Id = third.Id;
            state.Leaders.Add(third);
            Need(BaseAdventure.SetMember(state, third.Id, true) && BaseAdventure.Full(state), "未准备满员测试小队");

            var at = new BasePoint(azhu.MapX, azhu.MapY);
            int day = state.Day, food = state.Inventory.Food;
            var before = WorkerPosts(state);
            Need(Call<bool>(typeof(BaseAdventure), "LeaveHere", state, azhu.Id) && !BaseAdventure.Full(state) &&
                !BaseAdventure.Contains(state, azhu.Id) && BaseAdventure.WaitingHere(state, azhu) &&
                new BasePoint(azhu.MapX, azhu.MapY).Distance(at) < 1e-6,
                "原地离队没有释放名额或移动了阿铸");
            Need(state.Day == day && state.Inventory.Food == food && state.Muster.Parties.Count == 0 &&
                WorkerPosts(state).SequenceEqual(before), "本地离队改变了日期、路粮或普通兵岗位");

            Approach(state, "demo_ahe");
            Need(BaseDemoRuins.Join(state, "partner_farm") && BaseAdventure.Following(state, "partner_farm"),
                "阿禾未使用刚释放的随行名额");
            Approach(state, "exit_east");
            Need(BaseDemoRuins.Travel(state, "town") && !azhu.WaitingAtRecruitment &&
                azhu.MapRegion == "base" && !BaseAdventure.Contains(state, azhu.Id) &&
                state.Muster.Parties.Count == 0 && WorkerPosts(state).SequenceEqual(before),
                "切图后原地离队者没有安排回营，或影响普通兵");

            Approach(state, "demo_ruins_exit");
            Need(BaseDemoRuins.Travel(state, "outpost") && state.Muster.Parties.Count == 0 &&
                WorkerPosts(state).SequenceEqual(before), "领地内进入古窟不应抽调普通兵");
            var elsewhere = state.Workers.First(worker => !worker.Away);
            elsewhere.VillageResident = true;
            Approach(state, "demo_ruins_investigate");
            var prep = new BaseBattlePreparation(state, BasePrepRoute.Ruins);
            Need(prep.Units.Any(unit => unit.SoldierNumber > 0) &&
                prep.Units.All(unit => !elsewhere.SoldierNumbers.Contains(unit.SoldierNumber)),
                "当地备战带入了异地普通兵");
        }

        private static void VerifyConstructionGate(BaseState state)
        {
            state.Demo.Stage = BaseDemoStage.RuinsDelivered;
            Need(!Call<bool>(typeof(BaseFacilityLayout), "CanEnterCampConstruction", state),
                "古窟交付后的原许可错误开放老周建设");
            state.Demo.Stage = BaseDemoStage.DefenseWon;
            Need(!Call<bool>(typeof(BaseFacilityLayout), "CanEnterCampConstruction", state),
                "首防完成但没领新许可就开放了建设");
            state.Layout.OfficeGift = true;
            Need(Call<bool>(typeof(BaseFacilityLayout), "CanEnterCampConstruction", state) &&
                Call<bool>(typeof(BaseFacilityLayout), "NeedsZhouConstructionNotice", state),
                "领许可后老周建设入口和首句未就绪");
            state.Demo.Flow.ZhouConstructionNoticeSeen = true;
            Need(Call<bool>(typeof(BaseFacilityLayout), "CanEnterCampConstruction", state) &&
                !Call<bool>(typeof(BaseFacilityLayout), "NeedsZhouConstructionNotice", state),
                "读过首句不能再次进入建设，或会重复首句");

            VerifyConstructionDraft(state);
        }

        private static void VerifyConstructionDraft(BaseState state)
        {
            var draftType = typeof(BaseFacilityLayout).Assembly.GetType("BaseConquest.Base.SettlementLayoutDraft");
            var constructor = draftType.GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic,
                null, new[] { typeof(BaseState), typeof(bool) }, null);
            var move = draftType.GetMethod("Move", BindingFlags.Instance | BindingFlags.NonPublic);
            var layoutProblem = draftType.GetMethod("LayoutProblem", BindingFlags.Instance | BindingFlags.NonPublic);
            var commit = draftType.GetMethod("Commit", BindingFlags.Instance | BindingFlags.NonPublic);

            string original = BaseFacilityLayout.Snapshot(state);
            int materials = state.Inventory.Materials, coins = state.Inventory.Coins;
            var farm = BaseFacilityLayout.BaseLayout(state).Objects.Single(item => item.Id == "farm");
            double dx = string.IsNullOrEmpty(farm.Shape) ? .5 : farm.Object.BoundsWidth / 2 % 1;
            double dy = string.IsNullOrEmpty(farm.Shape) ? .5 : farm.Object.BoundsHeight / 2 % 1;
            object[] args = null;
            foreach (var cell in BaseBattlefield.Board(state).Positions())
            {
                double x = cell.X + dx, y = cell.Z + dy;
                if (x == farm.X && y == farm.Y || BaseFacilityLayout.Problem(state, "farm", x, y) != "") continue;
                var trial = constructor.Invoke(new object[] { state, false });
                var position = new object[] { "farm", x, y };
                move.Invoke(trial, position);
                if ((string)layoutProblem.Invoke(trial, new object[] { state }) != "") continue;
                args = position;
                break;
            }
            Need(args != null && BaseFacilityLayout.Snapshot(state) == original &&
                state.Inventory.Materials == materials && state.Inventory.Coins == coins, "丢弃建设预览改变了地图或扣费");

            var draft = constructor.Invoke(new object[] { state, false });
            move.Invoke(draft, args);
            state.Demo.Flow.CampConstructionOrdered = true;
            commit.Invoke(draft, new object[] { state });
            var placed = BaseFacilityLayout.BaseLayout(state).Objects.Single(item => item.Id == "farm");
            Need(placed.X == (double)args[1] && placed.Y == (double)args[2] && !state.Demo.Flow.CampConstructionOrdered &&
                state.Inventory.Materials == materials && state.Inventory.Coins == coins, "正式确认未搬移粮田或错误扣费");
        }

        private static string[] WorkerPosts(BaseState s) => s.Workers.SelectMany(worker =>
            worker.SoldierNumbers.Select(number => $"{number}/{worker.FacilityId}/{worker.Away}")).OrderBy(item => item).ToArray();

        private static void Approach(BaseState state, string id)
        {
            var marker = BaseSpatial.Objects(state).Single(o => o.Id == id);
            var point = marker.Blocks ? BaseStageWalk.Door(state, id) : new BasePoint(marker.X, marker.Y);
            state.PlayerX = point.X;
            state.PlayerY = point.Y;
            BaseInteraction.Walk(new BasePoint(marker.X - point.X, marker.Y - point.Y));
        }

        private static T Call<T>(Type type, string method, params object[] args) =>
            (T)type.GetMethod(method, BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, args);

        private static void Need(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}

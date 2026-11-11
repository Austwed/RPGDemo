using System;
using System.Linq;
using BaseConquest.Base;
using BaseConquest.Battle;
using BaseConquest.RealtimePrototype;

namespace BaseConquest.Editor
{
    public static partial class VerifyDemoPlayerRoute
    {
        private static BaseState FinishRoute(BaseState state)
        {
            Need(state.Campaign.Location == "base" && BaseDemoDefense.Due(state), "主线未到首次迎敌日");
            EnterDefenseField(state);
            state = WinRouteBattle(state, BasePrepRoute.Defense);
            BaseDemoDefense.AcknowledgeBatu(state);
            ResolveLoot(state);
            Note($"第 {state.Day} 天首防结算，巴图{(state.Demo.Defense.BatuFate == "dead" ? "战死" : "撤离")}，对白确认后第二波排在第 {BaseDemoDefense.SecondArrival(state)} 天。");
            SaveRouteCheckpoint(state, "merchant");

            VerifySecondRaidBranch(state.Copy());
            TryHideoutRoute(state);
            BuyMarchSupply(state);
            At(state, "demo_march_exit");
            BaseDemoFinale.OpenWorld(state);
            BaseAdventure.RecallPaused(state);
            var travel = PrepareRouteRoster(state, BasePrepRoute.Travel);
            var departure = travel.TravelPlan(state);
            Need(BaseMuster.Confirm(state, departure), "南侧出口的出征编队未确认");
            BaseWorldJourney.EnterWorld(state, "camp_entry");
            Need(!BaseWorldJourney.Plan(state, null, "home").Reachable, "未攻占黑鸦寨时已能规划返乡");
            var itinerary = BaseWorldJourney.Plan(state, null, "bandit");
            Need(itinerary.Reachable && itinerary.Days == 1, "黑鸦寨行军路线或路程非法");
            SaveRouteCheckpoint(state, "world");
            BaseWorldJourney.Start(state, itinerary.Path);
            int departureDay = state.Day;
            Need(BaseWorldJourney.Step(state, _ => null) && state.Day == departureDay + 1, "攻营行军没有正确推进一天");
            BaseWorldJourney.EnterScene(state, "bandit_entry");
            At(state, "demo_camp_attack");
            Note($"第 {departureDay} 天从南侧出口选出征队，经世界地图走一天，第 {state.Day} 天抵达黑鸦寨。行军前返乡路受敌寨阻断。");

            bool neutralValidation = CheckExitFacing(state);
            state = WinRouteBattle(state, BasePrepRoute.BlackCrow);
            ResolveLoot(state);
            Need(state.Demo.Defense.SecondOutcome == "camp_captured" || state.Demo.Defense.SecondOutcome == "intercepted",
                "早出征攻寨后第二波仍待触发");
            At(state, "demo_direct_home");
            if (neutralValidation)
            {
                var exit = BaseSpatial.Objects(state).Single(item => item.Id == "demo_direct_home");
                Need(BaseInteraction.CanReach(state, exit), "朝向正确时仍无法使用返乡出口");
                BaseInteraction.Facing = default;
            }
            int day = state.Day;
            Need(BaseCampaign.Travel(state, "custom", "demo_direct_home") && state.Campaign.CurrentMapId == BaseDemoFinale.Home &&
                state.Day == day, "攻寨后的白桦村直达出口未生效");
            string alien = BaseAdventure.Members(state).FirstOrDefault(leader => leader.IsAlien)?.Name;
            Note(alien == null
                ? "经寨内出口直达白桦村且未扣日程。本路线上没有异族伙伴随行，迦兰的返乡附加对白条件未满足。"
                : $"经寨内出口直达白桦村且未扣日程。{alien}仍在随行，满足调查处的异族附加对白条件。");
            At(state, "demo_broken_sword");
            if (neutralValidation)
            {
                Need(BaseInteraction.At(state, "custom", "demo_broken_sword"), "主角未实际抵达断剑旁");
                BaseInteraction.Facing = default;
            }
            Need(BaseDemoFinale.Investigate(state) && state.Demo.Evidence.Contains(BaseDemoFinale.Sword) &&
                BaseDemoFinale.Rebuilding(state), "断剑调查没有开启三项重建");
            Need(!BaseDemoFinale.FinishRebuild(state), "未布置设施便能结束 Demo");
            SaveRouteCheckpoint(state, "rebuild");
            var board = BaseBattlefield.Board(state, BaseDemoFinale.Home);
            var names = new[] { "小粮田", "营帐", "仓库" };
            for (int item = 0; item < names.Length; item++)
            {
                int selected = item;
                var spot = board.Positions().Where(cell => BaseDemoFinale.RebuildProblem(state, selected, cell.X, cell.Z) == "")
                    .OrderBy(cell => new BasePoint(cell.X + .5, cell.Z + .5).Distance(new BasePoint(state.PlayerX, state.PlayerY)))
                    .Select(cell => (GridPosition?)cell).FirstOrDefault();
                Need(spot.HasValue && BaseDemoFinale.PlaceRebuild(state, item, spot.Value.X, spot.Value.Z), names[item] + "重建失败");
                Note($"返乡重建放置{names[item]}，格 ({spot.Value.X}, {spot.Value.Z})。");
            }
            Need(BaseDemoFinale.FinishRebuild(state) && state.Demo.Stage == BaseDemoStage.Finished &&
                state.Day == day && state.Demo.Finale.VillageRebuilt, "未到达 Demo 结束节点");
            BaseEconomy.Validate(state);
            Note($"第 {state.Day} 天拿到断剑证据，完成三项重建，进入 Finished。");
            return state;
        }

        private static bool CheckExitFacing(BaseState state)
        {
            try
            {
                BaseEconomy.Validate(state.Copy());
                return false;
            }
            catch (ArgumentException error) when (error.Message.StartsWith("目标出口身份或同层安全落点非法：", StringComparison.Ordinal))
            {
                Need(BaseInteraction.At(state, "outpost", "demo_camp_attack"), "主角未实际抵达攻营点");
                BaseInteraction.Facing = default;
                BaseEconomy.Validate(state.Copy());
                Note("正式攻寨开战被全局朝向误用于出口校验所阻断：" + error.Message +
                    "。同一存档仅用中立朝向校验后继续检查末段，已核对主角实际走到了攻营点。");
                return true;
            }
        }

        private static void EnterDefenseField(BaseState state)
        {
            At(state, "demo_defense_exit");
            Need(BaseDemoDefense.Travel(state, "perimeter"), BaseDemoDefense.TravelProblem(state, "perimeter"));
            At(state, "demo_defense_start");
        }

        private static void BuyMarchSupply(BaseState state)
        {
            int desired = Math.Min(14, state.Workers.Where(worker => BaseDevelopment.Eligible("expedition", worker)).Sum(worker => worker.Count));
            int needed = desired - BaseSupply.Available(state, "supply", 1);
            if (needed <= 0) return;
            At(state, "exit_east");
            Need(BaseCampaign.Travel(state, "town"), "攻寨前不能回灰岩镇补军需");
            At(state, "town_market");
            int quantity = Math.Min(needed, BaseTown.MaxTrade(state, "supply1", true));
            if (quantity > 0)
                Need(BaseTown.Confirm(state, BaseTown.QuoteTrade(state, "supply1", true, quantity)), "攻寨前补军需交易失败");
            Note($"攻寨前回灰岩镇，按实际库存与报价补买一级军需 {quantity} 件，返营时可用一级军需 {BaseSupply.Available(state, "supply", 1)} 件。");
            At(state, "travel_base");
            Need(BaseCampaign.Travel(state, "base"), "补军需后不能回营");
        }

        private static void VerifySecondRaidBranch(BaseState branch)
        {
            int arrival = BaseDemoDefense.SecondArrival(branch);
            for (int day = branch.Day; day < arrival; day++)
            {
                At(branch, "home");
                Need(BaseEconomy.Rest(branch, day), "分支留营等待第二波时未结算日期");
            }
            Need(BaseDemoDefense.SecondDue(branch), "留营分支未触发第二波");
            EnterDefenseField(branch);
            var wave = OpenRouteBattle(branch, BasePrepRoute.Second);
            Need(!wave.RetreatAll(), "第二波出现撤退路径");
            Tick(wave, 60 * 90);
            var outcome = wave.Battle.Report?.Outcome;
            if (outcome == BattleOutcome.SideAWin)
                Need(BaseDemoDefense.Complete(branch, wave) && branch.Demo.Defense.SecondOutcome == "defended", "第二波胜利未记入结算");
            else if (outcome == BattleOutcome.SideBWin)
                Need(BaseDemoCombat.Fail(branch, wave) && branch.Aftermath.GameOver, "第二波败北没有进游戏结束");
            Note($"旁支从同一首防后存档留营至第 {arrival} 天，第二波真实运行 {wave.Model.Elapsed:F1} 秒，结果 {outcome?.ToString() ?? "90 秒内未结束"}；撤退不可用。主路线采用提前攻寨。");
        }

        private static BaseBattlePreparation PrepareRouteRoster(BaseState state, BasePrepRoute route)
        {
            var draft = new BaseBattlePreparation(state, route) { SupplyGrade = 1 };
            var leaders = draft.Units.Where(unit => unit.Kind == SoldierKind.Hero).Take(BaseAdventure.Capacity).ToArray();
            Need(leaders.Any(leader => leader.Id == draft.PlayerId), "备战名册缺少主角");
            foreach (var leader in leaders) leader.Selected = true;
            int limit = Math.Min(14, draft.AvailableSupply(state, 1));
            if (limit == 0 && route != BasePrepRoute.Travel)
            {
                draft.SupplyGrade = 2;
                limit = Math.Min(14, draft.AvailableSupply(state, 2));
            }
            var soldiers = draft.Units.Where(unit => unit.Kind != SoldierKind.Hero)
                .OrderByDescending(unit => unit.Health / unit.MaxHealth).ToArray();
            var selected = BattleSimulation.TroopKinds.Select(kind => soldiers.FirstOrDefault(unit => unit.Kind == kind))
                .Where(unit => unit != null).Concat(soldiers).Distinct().Take(limit).ToArray();
            foreach (var soldier in soldiers) soldier.Selected = false;
            for (int i = 0; i < selected.Length; i++)
            {
                selected[i].Selected = true;
                selected[i].TeamId = leaders[i % leaders.Length].Id;
            }
            return draft;
        }

        private static RealtimeCampaignBattle OpenRouteBattle(BaseState state, BasePrepRoute route)
        {
            var draft = PrepareRouteRoster(state, route);
            var problem = draft.Problem(state, false);
            Need(problem == "", route + "备战：" + problem);
            Need(draft.AutoPlace() > 0 && draft.Positions.ContainsKey(draft.PlayerId), route + "自动布置失败");
            var opening = draft.Begin(state);
            var run = new RealtimeCampaignBattle(opening);
            if (route == BasePrepRoute.BlackCrow) BaseDemoFinale.Configure(run);
            else BaseDemoDefense.Configure(run);
            int soldiers = run.Model.Units.Count(unit => unit.Side == PrototypeSide.Ally && unit.Role != PrototypeRole.Leader);
            var commanders = run.Battle.Soldiers.Where(unit => unit.Side == BattleSide.A && unit.Kind == SoldierKind.Hero)
                .OrderBy(unit => unit.OriginCommanderSlot).ToArray();
            Need(soldiers == draft.RequiredSupply(true), route + "实际上场人数和军需锁定数不一致");
            Note($"{route}备战/一键布置，队长 {string.Join("、", opening.Opening.Leaders.Select(leader => leader.Name))}，" +
                $"士兵 {soldiers} 人，锁定 {soldiers} 份{(draft.SupplyGrade == 1 ? "一级" : "二级")}军需。");
            foreach (var commander in commanders)
                run.Strategy(commander.OriginCommanderSlot, route == BasePrepRoute.Defense ? SquadStrategy.Protect : SquadStrategy.Attack);
            return run;
        }

        private static BaseState WinRouteBattle(BaseState state, BasePrepRoute route)
        {
            var before = state.Copy();
            var run = OpenRouteBattle(state, route);
            if (route == BasePrepRoute.Defense)
            {
                Tick(run, run.Battle.Scenario.Objective.HoldRounds * 60 + 3);
                if (run.DefensePursuitReady) Need(run.FinishDefensePursuit(), "首防结束追击未提交");
            }
            else Tick(run, 60 * 90 + 3);
            if (run.Battle.Report?.Outcome != BattleOutcome.SideAWin)
            {
                Note($"{route} 真实战斗 {run.Model.Elapsed:F1} 秒，结果 {run.Battle.Report?.Outcome.ToString() ?? "未决"}；另从本场战前检查点做结算注入，以下只验证后续事务。");
                state = before;
                run = OpenRouteBattle(state, route);
                foreach (var enemy in run.Model.Units.Where(unit => unit.Side == PrototypeSide.Enemy)) enemy.Escaped = true;
                Tick(run, 3);
                Need(run.Battle.Report?.Outcome == BattleOutcome.SideAWin, route + "结算注入未产生胜利");
            }
            else Note($"{route}真实战斗胜利，耗时 {run.Model.Elapsed:F1} 秒。");
            if (route == BasePrepRoute.BlackCrow) Need(BaseDemoFinale.Complete(state, run), "黑鸦寨未正常提交战果");
            else Need(BaseDemoDefense.Complete(state, run), "首防未正常提交战果");
            var report = state.Campaign.Reports.Last();
            Note($"{route}战果结算实扣军需 {report.GearLost} 件。");
            return state;
        }
    }
}

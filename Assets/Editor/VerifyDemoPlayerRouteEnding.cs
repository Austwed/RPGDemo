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
            SaveRouteCheckpoint(state, "home-field");
            if(state.Demo.StoryScene?.Version==2)CheckAuthoredBoard(state,state.Demo.StoryScene,"field");
            state = WinRouteBattle(state, BasePrepRoute.Defense);
            BaseDemoDefense.AcknowledgeBatu(state);
            ResolveLoot(state);
            Note($"第 {state.Day} 天首防结算，巴图{(state.Demo.Defense.BatuFate == "dead" ? "战死" : "撤离")}，对白确认后第二波排在第 {BaseDemoDefense.SecondArrival(state)} 天。");
            if(state.Demo.StoryScene?.Version!=2) VerifyDaySixDirectAttack(state.Copy());
            var merchantVisit=state.Copy();
            if(merchantVisit.Day<7) Rest(merchantVisit);
            Need(BaseDemoDefense.MerchantReminderDue(merchantVisit),"第七日营地没有老莫的消息");
            SaveRouteCheckpoint(merchantVisit, "merchant");

            VerifySecondRaidBranch(state.Copy(), false);
            VerifySecondRaidBranch(state.Copy(), true);
            TryHideoutRoute(state);
            Rest(state);
            BuyMarchSupply(state);
            At(state, "demo_march_exit");
            BaseDemoFinale.OpenWorld(state);
            Need(state.Campaign.WorldJourney.Nodes.Single(node => node.Id == "camp").Title == BaseScriptNames.Camp,
                "世界地图营地节点未使用灰岩营地");
            BaseAdventure.RecallPaused(state);
            var travel = PrepareRouteRoster(state, BasePrepRoute.Travel);
            Need(!BaseWorldJourney.Plan(state, null, "home","camp_entry").Reachable, "未攻占黑鸦寨时已能规划返乡");
            var itinerary = BaseWorldJourney.Plan(state, null, "bandit","camp_entry");
            Need(itinerary.Reachable && itinerary.Days == 1 && state.Campaign.Location=="base" &&
                !state.Muster.Parties.Any(p=>p.Purpose=="expedition"), "查看目的地或备战已经提交行程");
            // Old saves can be on the world map with a party already committed but no route.
            var staged=state.Copy();
            Need(BaseMuster.Confirm(staged,travel.TravelPlan(staged)), "旧版出征队生成失败");
            BaseWorldJourney.EnterWorld(staged,"camp_entry");
            SaveRouteCheckpoint(staged, "world");
            BaseWorldJourney.Depart(state,"camp_entry",itinerary.Path,travel,false);
            Need(!BaseWorldJourney.RequiredBattles(state).Any(), "平日行军试图读取已结束战斗的资源");
            int departureDay = state.Day;
            Need(BaseWorldJourney.Step(state, _ => null) && state.Day == departureDay + 1, "攻营行军没有正确推进一天");
            BaseWorldJourney.EnterScene(state, "bandit_entry");
            if(state.Demo.StoryScene?.Version==2)CheckAuthoredBoard(state,state.Demo.StoryScene,"bandit");
            var returning=state.Copy();
            At(returning,"demo_camp_world");
            var sameParty=returning.Muster.Parties.Single(p=>p.Purpose=="expedition");
            var regroup=new BaseBattlePreparation(returning,BasePrepRoute.Travel);
            Need(regroup.TravelingParty, "在寨外再次出发误用了营地人员");
            var member=regroup.Units.FirstOrDefault(u=>u.Kind!=SoldierKind.Hero);
            var other=regroup.Units.FirstOrDefault(u=>u.Kind==SoldierKind.Hero&&u.Id!=member?.TeamId);
            if(member!=null&&other!=null)member.TeamId=other.Id;
            var back=BaseWorldJourney.Plan(returning,null,"camp","bandit_entry");
            BaseWorldJourney.Depart(returning,"bandit_entry",back.Path,regroup,false);
            Need(returning.Muster.Parties.Single().Id==sameParty.Id && (member==null||other==null||
                sameParty.CommanderTroops.Single(row=>row.LeaderId==other.Id).SoldierNumbers.Contains(member.SoldierNumber)),
                "从寨外调整编制替换了出征队或丢失士兵编号");
            Need(BaseWorldJourney.Step(returning,_=>null),"返营路线未结算");
            BaseWorldJourney.EnterScene(returning,"camp_entry");
            Need(returning.Campaign.Location=="base"&&returning.Muster.Parties.Count==0&&returning.Day==state.Day+1,"已出征的部队未正常返营");
            At(state, "demo_camp_attack");
            Note($"第 {departureDay} 天从南侧出口查看世界地图，选黑鸦寨、整备后出发，第 {state.Day} 天抵达。行军前返乡路受敌寨阻断。");

            VerifyExitFacings(state, "黑鸦寨战前");
            state = WinRouteBattle(state, BasePrepRoute.BlackCrow);
            ResolveLoot(state);
            Need(state.Demo.Defense.SecondOutcome == "camp_captured" || state.Demo.Defense.SecondOutcome == "intercepted",
                "早出征攻寨后第二波仍待触发");
            Need(!BaseDemoFinale.OccupiedBySecondDefense(state) && !BaseDemoFinale.SecondRaidReportPending(state) &&
                !BaseSpatial.Objects(state).Any(obj => obj.Id == BaseDemoFinale.CampSuppliesMarker), "主动攻寨被识别为第二波自动接管");
            int day = state.Day;
            foreach(var gate in new[]{("demo_camp_world","bandit_entry"),("demo_camp_east","bandit_east_entry")})
            {
                var leaving=state.Copy();
                At(leaving,gate.Item1);BaseWorldJourney.EnterWorld(leaving,gate.Item2);
                Need(BaseWorldJourney.OnWorld(leaving)&&leaving.Day==day,"寨门未返回世界地图："+gate.Item1);
            }
            At(state,"demo_camp_east");BaseWorldJourney.EnterWorld(state,"bandit_east_entry");
            BaseWorldJourney.Start(state,new[]{"bandit","home"});Need(BaseWorldJourney.Step(state,_=>null),"攻寨后未能向白桦村行军");
            var beforeHome = state.Copy();
            var homeExpedition = beforeHome.Muster.Parties.Single(p => p.Purpose == "expedition" &&
                p.Leaders.Any(leader => leader.LeaderId == beforeHome.PlayerLeaderId));
            BaseWorldJourney.EnterScene(state,"home_entry");
            VerifySettlementTravel.VerifyArrival(state, homeExpedition, beforeHome);
            Need(state.Day==day+1 && state.Campaign.CurrentMapId==BaseDemoFinale.Home && state.PlayerX<BaseSpatial.Objects(state).Single(o=>o.Id=="demo_home_world").X,"返乡日数或东口入村朝向错误");
            var bridge=state.Copy();At(bridge,"village_gate_out");
            Need(BaseCampaign.Travel(bridge,"custom","village_gate_out")&&bridge.Campaign.CurrentMapId==BaseDemoFinale.HomeField,"村西未通往独立西桥头");
            VerifyChenanAndRuins.VerifyOptionalSword(bridge);
            At(bridge,"village_gate_in");Need(BaseCampaign.Travel(bridge,"custom","village_gate_in"),"西桥头不能回村");
            if(state.Demo.StoryScene?.Version==2)CheckAuthoredBoard(state,state.Demo.StoryScene,"village");
            Need(!state.Demo.Flow.HomeEnterSeen,"首次入村前已写入已读标志");
            SaveRouteCheckpoint(state,"home-arrival");
            Need(!BaseSpatial.Objects(state).Any(o=>o.Id=="demo_survivor") && !BaseDemoFinale.FinishAtBench(state,"village_bench:seat:left"),"入村仍有旧指路人或提前触发了结局");
            VerifyExitFacings(state,"返乡修理");
            state.Demo.Flow.HomeEnterSeen=true; // The rebuild fixture begins after the separate arrival dialogue.
            SaveRouteCheckpoint(state,"rebuild");
            VerifyChenanAndRuins.RepairReturnedVillage(state);
            Need(!state.Demo.Evidence.Contains(BaseDemoFinale.Sword),"主路线跳过调查却自动拾取断剑");
            Note($"第 {state.Day} 天从东口回到白桦村，跳过断剑，修好家、粮田与工坊，接应村民，坐上长椅完成结局。");
            return state;
        }

        private static void VerifyDaySixDirectAttack(BaseState state)
        {
            Need(state.Day==6 && !state.Demo.Defense.MerchantDone && !BaseDemoDefense.MerchantReminderDue(state),
                "第六日直攻分支缺少首防军情或已播放提醒");
            SaveRouteCheckpoint(state, "day6-direct-after-defense");
            BuyMarchSupply(state);
            At(state,"demo_march_exit");
            BaseDemoFinale.OpenWorld(state);
            BaseAdventure.RecallPaused(state);
            var formation=PrepareRouteRoster(state,BasePrepRoute.Travel);
            var outbound=BaseWorldJourney.Plan(state,null,"bandit","camp_entry");
            Need(outbound.Reachable && outbound.Days==1 && state.Day==6,"第六日不能直接备战出征");
            BaseWorldJourney.Depart(state,"camp_entry",outbound.Path,formation,false);
            Need(BaseWorldJourney.Step(state,_=>null) && state.Day==7 && !BaseDemoDefense.MerchantReminderDue(state),
                "行军途中第七日被商人打断");
            BaseWorldJourney.EnterScene(state,"bandit_entry");
            At(state,"demo_camp_attack");
            SaveRouteCheckpoint(state,"day7-direct-before-attack");
            WinRouteBattle(state,BasePrepRoute.BlackCrow);
            ResolveLoot(state);
            Need(state.Demo.Stage==BaseDemoStage.AttackWon && !state.Demo.Defense.MerchantDone,"直攻战果错误");
            At(state,"demo_camp_world");
            BaseWorldJourney.EnterWorld(state,"bandit_entry");
            var home=BaseWorldJourney.Plan(state,null,"camp","bandit_entry");
            Need(home.Reachable && home.Days==1,"攻寨后不能直接返营");
            BaseWorldJourney.Start(state,home.Path);
            Need(BaseWorldJourney.Step(state,_=>null),"直攻后返营未完成");
            BaseWorldJourney.EnterScene(state,"camp_entry");
            Need(state.Campaign.Location=="base" && !state.Demo.Defense.MerchantDone &&
                !BaseDemoDefense.MerchantReminderDue(state),"攻寨凯旋后仍触发老莫提醒");
            SaveRouteCheckpoint(state,"day8-direct-returned");
            Note($"直攻旁支：第 6 天处理首防军情、整备出发，第 7 天行军途中无老莫提醒，实战攻占黑鸦寨，第 {state.Day} 天回营仍未触发。");
        }

        private static void VerifyExitFacings(BaseState state, string checkpoint)
        {
            var current = BaseInteraction.Facing;
            try
            {
                var store = BaseSaveStore.InMemory(checkpoint);
                foreach (var facing in new[] { new BasePoint(1, 0), new BasePoint(-1, 0), new BasePoint(0, 1), new BasePoint(0, -1) })
                {
                    BaseInteraction.Facing = facing;
                    store.RestoreSession(state.Copy());
                    var restored = store.Load();
                    Need(restored.Day == state.Day && restored.Campaign.CurrentMapId == state.Campaign.CurrentMapId,
                        checkpoint + "读档改变了日期或场景");
                }
            }
            finally
            {
                BaseInteraction.Facing = current;
            }
            Note(checkpoint + "存档按四个朝向分别恢复成功。");
        }

        private static void EnterDefenseField(BaseState state)
        {
            At(state, "demo_defense_exit");
            bool arrived = BaseDemoFinale.Opened(state)
                ? BaseCampaign.Travel(state, "perimeter", "demo_defense_exit")
                : BaseDemoDefense.Travel(state, "perimeter");
            Need(arrived, "未能从营地前往外围迎战");
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

        private static void VerifySecondRaidBranch(BaseState branch, bool worldOpened)
        {
            if (worldOpened)
            {
                At(branch, "demo_march_exit");
                BaseDemoFinale.OpenWorld(branch);
            }
            int arrival = BaseDemoDefense.SecondArrival(branch);
            for (int day = branch.Day; day < arrival; day++)
            {
                At(branch, "home");
                Need(BaseEconomy.Rest(branch, day), "分支留营等待第二波时未结算日期");
            }
            Need(BaseDemoDefense.SecondDue(branch), "留营分支未触发第二波");
            EnterDefenseField(branch);
            var priorSite = branch.Campaign.EnemySites.Single().Copy();
            var wave = OpenRouteBattle(branch, BasePrepRoute.Second);
            Need(!wave.RetreatAll(), "第二波出现撤退路径");
            Tick(wave, 60 * 90);
            var outcome = wave.Battle.Report?.Outcome;
            if (outcome == BattleOutcome.SideAWin)
            {
                Need(BaseDemoDefense.Complete(branch, wave) && branch.Demo.Defense.SecondOutcome == "defended", "第二波胜利未记入结算");
                VerifySecondRaidOccupation(branch, priorSite, worldOpened);
            }
            else if (outcome == BattleOutcome.SideBWin)
                Need(BaseDemoCombat.Fail(branch, wave) && branch.Aftermath.GameOver, "第二波败北没有进游戏结束");
            Note($"旁支在世界地图{(worldOpened ? "已" : "未")}开放时留营至第 {arrival} 天，第二波真实运行 {wave.Model.Elapsed:F1} 秒，结果 {outcome?.ToString() ?? "90 秒内未结束"}；撤退不可用。");
        }

        private static void VerifySecondRaidOccupation(BaseState branch, BaseEnemySite priorSite, bool worldOpened)
        {
            var occupied = BaseThreats.Site(branch, priorSite.Id);
            Need(BaseDemoFinale.OccupiedBySecondDefense(branch) && occupied.Owner == "player" &&
                occupied.CapturedDay == branch.Day && occupied.Garrison.Count == 0 &&
                branch.Campaign.Events.Single(item => item.Id == occupied.AttackEventId).Status == "resolved",
                "第二波胜利没有清空守军并占领黑鸦寨");
            if (worldOpened)
            {
                ResolveLoot(branch);
                Need(BaseWorldJourney.Plan(branch, null, "home").Reachable &&
                    BaseSpatial.Objects(branch, BaseDemoFinale.Camp).Any(obj => obj.Id == BaseDemoFinale.CampSuppliesMarker),
                    "世界地图已开放时胜利未更新返乡路与寨内搜查点");
                return;
            }

            var legacy = branch.Copy();
            legacy.Demo.Stage = BaseDemoStage.DefenseWon;
            legacy.Demo.Finale.AttackDay = 0;
            legacy.Demo.Finale.CampTakenByDefense = false;
            legacy.Demo.Evidence.Remove(BaseDemoFinale.AttackProof);
            legacy.Campaign.EnemySites[0] = priorSite;
            legacy.Campaign.Events.Single(item => item.Id == priorSite.AttackEventId).Status = "pending";
            BaseEconomy.Validate(legacy);
            Need(BaseDemoFinale.SecondRaidReportPending(legacy) && legacy.Campaign.EnemySites.Single().Owner == "player",
                "旧第二波胜利存档未补齐黑鸦寨占领与陈安报告");

            // Defense completion returns the party to the camp before its report and loot are shown.
            branch.Campaign.Location = "base";
            branch.Campaign.CurrentMapId = BaseSpatial.Layout(branch, "base").SourceMapId ?? "";
            var landing = BaseSpatial.Nearest(branch, new BasePoint(1.6, 6.5));
            branch.PlayerX = landing.X;
            branch.PlayerY = landing.Y;
            BaseAdventure.Arrive(branch);
            SaveRouteCheckpoint(branch, "second-occupation");
            BaseDemoFinale.AcknowledgeSecondRaidReport(branch);
            ResolveLoot(branch);
            At(branch, "demo_march_exit");
            BaseDemoFinale.OpenWorld(branch);
            BaseAdventure.RecallPaused(branch);
            var travel = PrepareRouteRoster(branch, BasePrepRoute.Travel);
            Need(BaseMuster.Confirm(branch, travel.TravelPlan(branch)), "第二波胜利后的出征编队未确认");
            BaseWorldJourney.EnterWorld(branch, "camp_entry");
            var direct = branch.Copy();
            var homePlan = BaseWorldJourney.Plan(direct, null, "home");
            Need(homePlan.Reachable && homePlan.Days == 2, "第二波胜利后白桦村行程未解锁");
            BaseWorldJourney.Start(direct, homePlan.Path);
            Need(BaseWorldJourney.Step(direct, _ => null) && BaseWorldJourney.Marching(direct) &&
                BaseWorldJourney.Step(direct, _ => null) && !BaseWorldJourney.Marching(direct),
                "第二波胜利后的返乡行军不能连续通过黑鸦寨");
            BaseWorldJourney.EnterScene(direct, "home_entry");
            Need(direct.Campaign.CurrentMapId == BaseDemoFinale.Home && !direct.Campaign.HasActiveBattle &&
                !direct.Demo.Finale.CampSuppliesTaken, "直接返乡触发了黑鸦寨战斗或搜查");

            var campPlan = BaseWorldJourney.Plan(branch, null, "bandit");
            BaseWorldJourney.Start(branch, campPlan.Path);
            Need(BaseWorldJourney.Step(branch, _ => null), "占领后不能行军到黑鸦寨");
            BaseWorldJourney.EnterScene(branch, "bandit_entry");
            Need(!branch.Demo.Finale.CampSuppliesTaken, "抵达空寨不应自动搜查物资");
            // Depending on the imported entry, the player can land within reach of the search marker.
            var search = BaseSpatial.Objects(branch).Single(obj => obj.Id == BaseDemoFinale.CampSuppliesMarker);
            var far = BaseBattlefield.Board(branch).Positions()
                .Select(p => new BasePoint(p.X + .5, p.Z + .5))
                .First(p => search.Distance(p.X, p.Y) > BaseInteraction.Distance + 1 && BaseSpatial.Walkable(branch, p.X, p.Y));
            branch.PlayerX = far.X;
            branch.PlayerY = far.Y;
            Need(!BaseDemoFinale.ClaimCampSupplies(branch), "远离搜查点仍可领取物资");
            At(branch, BaseDemoFinale.CampSuppliesMarker);
            SaveRouteCheckpoint(branch, "camp-remnants");
            var stock = branch.Inventory.Copy();
            Need(BaseDemoFinale.ClaimCampSupplies(branch) &&
                (branch.Inventory.Food > stock.Food || branch.Inventory.Materials > stock.Materials || branch.Inventory.Coins > stock.Coins),
                "进入占领的黑鸦寨未能领取余下物资");
            var saves = BaseSaveStore.InMemory("second-camp-supplies");
            saves.RestoreSession(branch.Copy());
            var restored = saves.Load();
            Need(restored.Demo.Finale.CampSuppliesTaken && !BaseDemoFinale.ClaimCampSupplies(restored),
                "读档后重复领到了黑鸦寨物资");
            At(restored,"demo_camp_east");BaseWorldJourney.EnterWorld(restored,"bandit_east_entry");
            Need(BaseWorldJourney.OnWorld(restored)&&BaseWorldJourney.Plan(restored,null,"home").Days==1,"搜查后不能从寨东口走一天返乡");
            Note("第二波胜利自动占领黑鸦寨，验证直接行军返乡、进寨领取物资、读档防重领及寨内出口。");
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
            var run = OpenRouteBattle(state, route);
            if (route == BasePrepRoute.Defense)
            {
                Tick(run, run.Battle.Scenario.Objective.HoldRounds * 60 + 3);
                if (run.DefensePursuitReady) Need(run.FinishDefensePursuit(), "首防结束追击未提交");
            }
            else FightBlackCrow(run);
            Need(run.Battle.Report?.Outcome == BattleOutcome.SideAWin,
                $"{route}真实战斗 {run.Model.Elapsed:F1} 秒，结果 {run.Battle.Report?.Outcome.ToString() ?? "未决"}");
            Note($"{route}真实战斗胜利，耗时 {run.Model.Elapsed:F1} 秒。");
            if (route == BasePrepRoute.BlackCrow) Need(BaseDemoFinale.Complete(state, run), "黑鸦寨未正常提交战果");
            else Need(BaseDemoDefense.Complete(state, run), "首防未正常提交战果");
            var report = state.Campaign.Reports.Last();
            Note($"{route}战果结算实扣军需 {report.GearLost} 件。");
            return state;
        }

        private static void FightBlackCrow(RealtimeCampaignBattle run)
        {
            run.Model.SetPaused(false);
            for (int frame = 0; frame < 60 * 90 + 3 && run.Battle.Phase == BattlePhase.Fighting; frame++)
            {
                if (frame >= 60 * 12 && frame % (60 * 3) == 0)
                {
                    var target = run.Battle.Soldiers.Where(unit => unit.Side == BattleSide.B &&
                            unit.IsActive && run.Battle.PlayerCanSee(BattleSide.A, unit))
                        .OrderByDescending(unit => unit.Kind == SoldierKind.Hero).ThenBy(unit => unit.Health).FirstOrDefault();
                    var leaders = run.Model.Units.Where(unit => unit.Alive && unit.Side == PrototypeSide.Ally &&
                        unit.Role == PrototypeRole.Leader).ToArray();
                    if (target != null)
                    {
                        var actor = run.Model.Units.Single(unit => unit.Id == target.Id + 1);
                        var waiting = leaders.Where(unit => !unit.ManualOrder || unit.TargetId != actor.Id)
                            .Select(unit => unit.Id - 1).ToArray();
                        if (waiting.Length > 0) run.Issue(waiting, PrototypeOrder.Focus, actor.Position, target.Id, squadFocus: true);
                    }
                    else if (leaders.Length > 0 && frame % (60 * 9) == 0)
                    {
                        var board = run.Battle.Board;
                        int row = (frame / (60 * 9)) % 2 == 0 ? board.Height / 3 : 2 * board.Height / 3;
                        var scout = board.Positions().Where(cell => board.Walkable(cell))
                            .OrderBy(cell => Math.Abs(cell.X - 3 * board.Width / 4) + Math.Abs(cell.Z - row)).First();
                        run.Issue(leaders.Select(unit => unit.Id - 1), PrototypeOrder.Move, new PrototypePoint(scout.X + .5, scout.Z + .5));
                    }
                }
                run.Tick(1d / 60);
            }
            if (run.Battle.Report?.Outcome != BattleOutcome.SideAWin)
                Note("黑鸦寨末段仍在场：" + string.Join("；", run.Model.Units.Where(unit => unit.Alive)
                    .Select(unit => $"{unit.Side}/{unit.Role}#{unit.Id} ({unit.Position.X:F1},{unit.Position.Z:F1})")));
        }
    }
}

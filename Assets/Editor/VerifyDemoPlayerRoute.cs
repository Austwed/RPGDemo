using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BaseConquest.Base;
using BaseConquest.Battle;
using BaseConquest.RealtimePrototype;
using UnityEditor;
using UnityEngine;

namespace BaseConquest.Editor
{
    public static partial class VerifyDemoPlayerRoute
    {
        private static readonly List<string> Report = new List<string>();

        public static void RunAndExit()
        {
            const string difficultyKey = "BaseConquest.GlobalDifficulty";
            bool hadDifficulty = PlayerPrefs.HasKey(difficultyKey);
            string prior = PlayerPrefs.GetString(difficultyKey);
            var priorFacing = BaseInteraction.Facing;
            int exit = 0;
            Report.Clear();
            try
            {
                PlayerPrefs.SetString(difficultyKey, "normal");
                var config = DemoBalanceRuntime.Load();
                string raw = File.ReadAllText(Path.Combine(Application.streamingAssetsPath, "BaseScenarios", BaseDemo.BalanceTemplate));
                var state = OpenRoute(raw, config.Rules, config.WorkbookSha256);
                PrepareFirstDefense(state);
                state = FinishRoute(state);
                Need(state.Demo.Stage == BaseDemoStage.Finished, "路线未走到 Demo 结束");
                Note("PASS：同一份进度从白桦村新开局走到返乡重建结束。");
            }
            catch (Exception error)
            {
                exit = 2;
                Note("未走完：" + error.Message);
                Debug.LogException(error);
            }
            finally
            {
                if (hadDifficulty) PlayerPrefs.SetString(difficultyKey, prior);
                else PlayerPrefs.DeleteKey(difficultyKey);
                BaseInteraction.Facing = priorFacing;
                Directory.CreateDirectory("Logs");
                File.WriteAllLines("Logs/demo-player-route.txt", Report);
            }
            EditorApplication.Exit(exit);
        }

        private static void PrepareFirstDefense(BaseState state)
        {
            Travel(state, "exit_east", "town");
            At(state, "town_quest");
            Need(BaseDemo.AcceptLordQuest(state), "未能接取领主委托");
            At(state, "demo_azhu");
            Need(BaseDemoNpcs.InviteAzhu(state) && !BaseDemoNpcs.CanInviteAzhu(state) &&
                !state.Demo.MetCompanions.Contains("partner_craft"), "拒绝阿铸后未保持当前进度");
            Note("灰岩镇：接受领主委托。触发阿铸叫停后离开，没有进入比武或招募。");
            BuyAndReforge(state);

            Travel(state, "demo_ruins_exit", "outpost");
            At(state, "demo_ruins_investigate");
            var solo = new BaseBattlePreparation(state, BasePrepRoute.Ruins);
            solo.AutoPlace();
            var lost = new RealtimeCampaignBattle(solo.Begin(state));
            BaseDemoRuins.Configure(lost);
            ApproachController(lost, false, 3600, () => BaseDemoRuins.SoloRetreatGuide(lost));
            bool reachedHalf = BaseDemoRuins.SoloRetreatGuide(lost);
            if (!reachedHalf)
            {
                Need(lost.Battle.Phase == BattlePhase.Fighting && lost.Model.Player.Alive, "独闯未在半血停住");
                lost.Model.Player.Health = lost.Model.Player.MaxHealth * .5;
            }
            Need(BaseDemoRuins.SoloRetreatGuide(lost) && BaseDemoCombat.Fail(state, lost), "独闯半血撤退未结算");
            BaseSceneRules.LeaveDungeon(state);
            Need(BaseSceneRules.Entrance(state) && state.Demo.RuinsDay == 0 && !state.Campaign.HasActiveBattle, "撤退没有回到遗迹入口");
            Note("沉眠古窟：主角独自布置进场，" + (reachedHalf ? "实战降至半血" : "用受控半血输入") + "触发撤退，回入口后可重整队伍。");

            Travel(state, "demo_ruins_return", "town");
            Travel(state, "travel_base", "base");
            At(state, "home");
            Need(BaseRpg.UpgradeCamp(state), "无法升级营帐为伙伴腾出名额");
            Rest(state);
            At(state, "demo_ahe");
            Need(BaseDemoRuins.Meet(state, "partner_farm") && BaseDemoRuins.Join(state, "partner_farm"), "新营地无法邀请阿禾");
            Travel(state, "exit_east", "town");
            RecruitAzhu(state);
            Travel(state, "demo_ruins_exit", "outpost");
            At(state, "demo_ruins_investigate");
            var draft = new BaseBattlePreparation(state, BasePrepRoute.Ruins);
            draft.AutoPlace();
            var rescue = new RealtimeCampaignBattle(draft.Begin(state));
            BaseDemoRuins.Configure(rescue);
            ApproachController(rescue, true, 5400);
            Need(BaseDemoRuins.Complete(state, rescue), "古窟重试没有击败控制来源：" + rescue.Battle.Report?.Outcome);
            string controller = state.Demo.Flow.FirstControllerId;
            Need(BaseDemoStories.AcceptController(state, controller), "古窟结算无法接纳迦兰");
            ResolveLoot(state);
            At(state, "demo_rescued_1");
            Need(BaseSceneRules.TalkRescued(state), "无法与获救村民交谈");
            for (int i = 0; i < 900 && state.Demo.Rescue.Stage == 2; i++) BaseSceneRules.TickRescued(state, .05);
            Need(state.Demo.Rescue.Stage == 3, "获救村民未返回城镇");
            Note("古窟重试：与阿禾、阿铸实际打赢，领取首通奖励、接纳迦兰，与获救村民道别。");

            Travel(state, "demo_ruins_return", "town");
            At(state, "town_quest");
            Need(BaseDemoRuins.Deliver(state), "无法交付古窟证据并获得募兵许可");
            bool intel = false;
            int hired = 0;
            while (!BaseDemoDefense.Due(state))
            {
                At(state, "town_recruit");
                foreach (var kind in new[] { BaseTroopKind.Melee, BaseTroopKind.Ranged, BaseTroopKind.Cavalry })
                {
                    // Avoid locking in all three troop types during the opening rest lesson.
                    if (state.Day == 2 && kind == BaseTroopKind.Cavalry) continue;
                    int wanted = kind == BaseTroopKind.Cavalry ? 3 : 4;
                    while (state.Workers.Where(worker => worker.Kind == kind).Sum(worker => worker.Count) < wanted &&
                        BaseTown.MaxHire(state, kind) > 0)
                    {
                        Need(BaseTown.Confirm(state, BaseTown.QuoteHire(state, kind, 1)), "逐人招募失败：" + kind);
                        hired++;
                    }
                }
                if (state.Day == 2) CheckEarlyRestAfterBuyingFood(state.Copy());
                At(state, "travel_base");
                if (!intel && state.Demo.Flow.FirstIntelEligible)
                {
                    Need(BaseRaidIntel.OfferFirst(state) && BaseRaidIntel.ClaimFirst(state), "首防免费情报未触发");
                    intel = true;
                    Note($"第{state.Day}天 首次购兵后从老莫领取免费消息，确认第{state.Demo.Flow.FreeFirstRaidDay}天来袭。");
                }
                Need(BaseCampaign.Travel(state, "base"), "募兵后无法返营");
                RepairAndAssign(state);
                if (hired >= 11) break;
                Rest(state);
                if (BaseDemoDefense.Due(state)) break;
                Travel(state, "exit_east", "town");
            }
            Need(intel && hired >= 10, "首防前未购足至少10名士兵");
            for (int attempt = 0; attempt < 12 && !BaseDemoDefense.Due(state); attempt++) Rest(state);
            Need(BaseDemoDefense.Due(state), "来袭没有到期");
            Note($"第{state.Day}天 首防到达。已逐次购兵{hired}人，粮田{BaseEconomy.WorkersAt(state, "farm")}人，工坊{BaseEconomy.WorkersAt(state, "workshop")}人，一级军需{BaseSupply.Available(state, "supply", 1)}件。");
        }

        private static void BuyAndReforge(BaseState state)
        {
            At(state, "town_market");
            int bought = BaseEquipmentCrafting.MaxBuy(state);
            Need(bought >= 10 && BaseEquipmentCrafting.Buy(state, 10, state.Backpack.Revision), "老莫处无法买十颗锻造石");
            At(state, "town_blacksmith");
            var sword = BasePartyInventory.Entries(state).First(entry =>
                entry.Holder == state.PlayerLeaderId && entry.Slot == "weapon" && BaseEquipmentCrafting.CanSelectReforge(state, entry));
            string starting = BasePersonalEquipment.Qualities[sword.Quality];
            var qualities = new List<string> { starting };
            var leader = state.Leaders.Single(person => person.Id == state.PlayerLeaderId);
            var before = BasePersonalEquipment.Stats(leader);
            foreach (int stones in new[] { 3, 1, 1, 5 })
            {
                var quote = BaseEquipmentCrafting.Quote(state, sword.Id, stones);
                Need(quote != null && BaseEquipmentCrafting.Confirm(state, quote), "巴隆处无法重铸短剑");
                qualities.Add(BasePersonalEquipment.Qualities[BasePartyInventory.Find(state, sword.Id).Quality]);
            }
            var after = BasePersonalEquipment.Stats(leader);
            Note($"老莫购入10颗锻造石。巴隆实际重铸{sword.Name}：{string.Join(" → ", qualities)}，耗石3／1／1／5；攻击{before.Damage}→{after.Damage}，防御{before.Defense}→{after.Defense}，生命{before.Health}→{after.Health}。");
        }

        private static void RecruitAzhu(BaseState state)
        {
            At(state, "demo_azhu");
            var duel = new RealtimeCampaignBattle(BaseLocalDuel.Create(state, false));
            var opponent = duel.Model.Units.Single(unit => unit.Side == PrototypeSide.Enemy);
            duel.OutcomeResolver = run => run.Model.Player.Health <= run.Model.Player.MaxHealth * .5 ? BattleOutcome.SideBWin
                : opponent.Health <= opponent.MaxHealth * .5 ? BattleOutcome.SideAWin : BattleOutcome.None;
            duel.Strategy(0, SquadStrategy.Attack);
            Tick(duel, 5400);
            bool liveWin = duel.Battle.Report?.Outcome == BattleOutcome.SideAWin;
            if (!liveWin)
            {
                duel = new RealtimeCampaignBattle(BaseLocalDuel.Create(state, false));
                var sample = duel.Model.Units.Single(unit => unit.Side == PrototypeSide.Enemy);
                duel.OutcomeResolver = _ => sample.Health <= sample.MaxHealth * .5 ? BattleOutcome.SideAWin : BattleOutcome.None;
                sample.Health = sample.MaxHealth * .5;
                Tick(duel, 4);
            }
            Need(duel.Battle.Report?.Outcome == BattleOutcome.SideAWin && BaseDemoRuins.Meet(state, "partner_craft"), "阿铸比武没有开放邀请");
            state.Demo.Flow.AzhuDuelWon = true;
            Need(BaseDemoRuins.Join(state, "partner_craft") && BaseDemoStories.Gift(state), "阿铸加入或铁匠赠礼失败");
            ResolveLoot(state);
            Note("回镇主动找阿铸比武，" + (liveWin ? "实战" : "受控半血样本") + "胜利后邀请同行，领取巴隆赠礼。");
        }

        private static void ApproachController(RealtimeCampaignBattle run, bool attack, int frames, Func<bool> stop = null)
        {
            var enemy = run.Battle.Commander(BattleSide.B, 0);
            var actor = run.Model.Units.Single(unit => unit.Id == enemy.Id + 1);
            run.Model.SetPaused(false);
            for (int frame = 0; frame < frames && run.Battle.Phase == BattlePhase.Fighting && stop?.Invoke() != true; frame++)
            {
                if (frame % 30 == 0)
                {
                    bool focus = attack && run.Battle.PlayerCanSee(BattleSide.A, enemy);
                    foreach (var captain in run.Model.Units.Where(unit => unit.Alive && unit.Side == PrototypeSide.Ally && unit.Role == PrototypeRole.Leader))
                    {
                        if (focus && captain.TargetId == actor.Id && captain.ManualOrder) continue;
                        run.Issue(new[] { captain.Id - 1 }, focus ? PrototypeOrder.Focus : PrototypeOrder.Move,
                            actor.Position, focus ? actor.Id - 1 : -1, true);
                    }
                }
                run.Tick(1d / 60);
            }
        }

        private static void RepairAndAssign(BaseState state)
        {
            foreach (string facility in new[] { "workshop", "barracks" })
            {
                if (BaseDemoDefense.Repaired(state, facility)) continue;
                At(state, "demo_fix_" + facility);
                Need(BaseDemoDefense.Repair(state, facility), "无法修复 " + facility);
            }
            if (BaseAdventure.Contains(state, "partner_farm"))
            {
                At(state, "farm");
                Need(BaseEconomy.AssignLeader(state, "partner_farm", "farm"), "阿禾无法担任粮田负责人");
                string galan = "partner_recruit:" + state.Demo.Flow.FirstControllerId;
                Need(BaseAdventure.SetMember(state, galan, true), "迦兰无法加入随行");
            }
            if (BaseEconomy.EligibleWorkers(state, "workshop") == 0)
            {
                At(state, "workshop");
                var worker = state.Workers.FirstOrDefault(person => person.FacilityId == "" && BaseDevelopment.Effective(state, person));
                if (worker != null)
                    Need(BaseEconomy.MoveWorkers(state, worker.Kind, "", "workshop", 1), "必须为休息教学保留一名工坊工人");
            }
            foreach (string facility in new[] { "farm", "workshop" })
            {
                At(state, facility);
                int target = state.Muster.Facilities.Single(site => site.FacilityId == facility).Capacity;
                while (BaseEconomy.WorkersAt(state, facility) < target)
                {
                    var worker = state.Workers.FirstOrDefault(person => person.FacilityId == "" && BaseDevelopment.Effective(state, person));
                    if (worker == null) break;
                    Need(BaseEconomy.MoveWorkers(state, worker.Kind, "", facility, 1), "无法逐人安排 " + facility);
                }
            }
            BaseEconomy.Validate(state);
        }

        private static void CheckEarlyRestAfterBuyingFood(BaseState branch)
        {
            while (branch.Workers.Where(person => person.Kind == BaseTroopKind.Cavalry).Sum(person => person.Count) < 3 &&
                BaseTown.MaxHire(branch, BaseTroopKind.Cavalry) > 0)
                Need(BaseTown.Confirm(branch, BaseTown.QuoteHire(branch, BaseTroopKind.Cavalry, 1)), "测试分支无法补骑兵");
            At(branch, "town_market");
            int amount = Math.Min(20, BaseTown.MaxTrade(branch, "food", true));
            if (amount > 0) Need(BaseTown.Confirm(branch, BaseTown.QuoteTrade(branch, "food", true, amount)), "测试分支无法购粮");
            Travel(branch, "travel_base", "base");
            RepairAndAssign(branch);
            At(branch, "home");
            var preview = BaseEconomy.Preview(branch);
            string lesson = BaseDemoFlow.RestProblem(branch);
            if (lesson != "")
            {
                try { BaseEconomy.Rest(branch, branch.Day); }
                catch (InvalidOperationException error) when (error.Message == lesson)
                {
                    Note($"第2天旁支：当天买齐三职业、工坊已排人、又买{amount}份粮，库存{branch.Inventory.Food}。" +
                        $"预计产粮{preview.FoodProduced}、口粮{preview.FoodRequired}，仍被休息教学拦下。主线延到第3天购骑兵。");
                }
            }
        }

        private static void TryHideoutRoute(BaseState state)
        {
            Travel(state, "exit_east", "town");
            At(state, "town_market");
            int stock = BaseSupply.Available(state, "supply", 1);
            int purchase = Math.Min(Math.Max(0, 10 - stock), BaseTown.MaxTrade(state, "supply1", true));
            if (purchase > 0) Need(BaseTown.Confirm(state, BaseTown.QuoteTrade(state, "supply1", true, purchase)), "窝点补充军需失败");
            Travel(state, "demo_ruins_exit", "outpost");
            At(state, BaseBanditHideout.EntryId);
            var draft = new BaseBattlePreparation(state, BasePrepRoute.Hideout, 71101) { SupplyGrade = 1 };
            var eligible = draft.Units.Where(unit => unit.Kind != SoldierKind.Hero).ToArray();
            int count = Math.Min(10, Math.Min(eligible.Length, BaseSupply.Available(state, "supply", 1)));
            foreach (var soldier in eligible) soldier.Selected = false;
            foreach (var soldier in eligible.OrderByDescending(unit => unit.Health / unit.MaxHealth).Take(count))
            {
                soldier.Selected = true;
                soldier.TeamId = draft.PlayerId;
            }
            draft.AutoPlace();
            var hideout = new RealtimeCampaignBattle(draft.Begin(state));
            Tick(hideout, 10800);
            bool won = hideout.Battle.Report?.Outcome == BattleOutcome.SideAWin;
            if (!won && hideout.Battle.Phase == BattlePhase.Fighting) Need(hideout.RetreatAll(), "窝点超时后无法撤退");
            Need(BaseBanditHideout.Complete(state, hideout), "窝点战果无法结算");
            ResolveLoot(state, won);
            Note($"绕墙进入土匪窝点：实放{draft.RequiredSupply(true)}名士兵，敌军{draft.KnownEnemyCount()}名，自动战斗{hideout.Battle.Report?.Outcome}。");
            Travel(state, "demo_ruins_return", "town");
            Travel(state, "travel_base", "base");
        }

        private static void Rest(BaseState state)
        {
            At(state, "home");
            int day = state.Day;
            Need(BaseEconomy.Rest(state, day), "营帐休息失败：" + BaseDemoFlow.RestProblem(state));
            Note($"第{day}天 日结产粮{state.LastReport.FoodProduced}，产军需{state.LastReport.ProductsMade}，次日粮食{state.Inventory.Food}。");
        }

        private static void Travel(BaseState state, string marker, string destination)
        {
            At(state, marker);
            Need(BaseCampaign.Travel(state, destination), "出口无法通往 " + destination + "：" + BaseDemoRuins.TravelProblem(state, destination));
        }

        private static void At(BaseState state, string id)
        {
            var marker = BaseSpatial.Objects(state).SingleOrDefault(item => item.Id == id);
            Need(marker != null, "场景缺少 " + id);
            var start = BaseSpatial.Nearest(state, new BasePoint(state.PlayerX, state.PlayerY));
            state.PlayerX = start.X;
            state.PlayerY = start.Y;
            var goal = marker.Blocks ? BaseStageWalk.Door(state, id) : BaseSpatial.Nearest(state, new BasePoint(marker.X, marker.Y));
            var route = BaseStageWalk.Route(state, start, goal);
            foreach (var waypoint in route)
            {
                for (int step = 0; step < 500; step++)
                {
                    var delta = new BasePoint(waypoint.X - state.PlayerX, waypoint.Y - state.PlayerY);
                    double distance = new BasePoint(0, 0).Distance(delta);
                    if (distance < .025) break;
                    double fraction = Math.Min(1, .075 / distance);
                    var previous = new BasePoint(state.PlayerX, state.PlayerY);
                    BaseSpatial.Move(state, ref state.PlayerX, ref state.PlayerY, delta.X * fraction, delta.Y * fraction);
                    Need(previous.Distance(new BasePoint(state.PlayerX, state.PlayerY)) > .0001, "步行卡在 " + id + " 前");
                }
            }
            Need(new BasePoint(state.PlayerX, state.PlayerY).Distance(goal) < .08, "步行未能到达 " + id);
            BaseInteraction.Walk(new BasePoint(marker.X - state.PlayerX, marker.Y - state.PlayerY));
        }

        private static void Tick(RealtimeCampaignBattle run, int maxTicks = 5400)
        {
            run.Model.SetPaused(false);
            for (int step = 0; step < maxTicks && run.Battle.Phase == BattlePhase.Fighting; step++) run.Tick(1d / 60);
        }

        private static void ResolveLoot(BaseState state, bool recruit = false)
        {
            foreach (var prisoner in state.Aftermath.Prisoners.Where(person => person.Status == "held").ToArray())
                Need(recruit && prisoner.Kind != SoldierKind.Hero && BaseAftermath.Decide(state, prisoner.Id, "recruit") ||
                    BaseAftermath.Decide(state, prisoner.Id, "release"), "无法处置俘虏");
            foreach (var loot in state.Aftermath.Loot.Where(item => !item.Closed).ToArray())
            {
                BaseBattleLoot.CollectAll(state, loot.BattleId);
                foreach (var item in loot.Items.Where(item => item.Status == "pending").ToArray())
                    Need(BaseBattleLoot.Resolve(state, loot.BattleId, item.Id, state.PlayerLeaderId, true), "无法清理满包的待领战果");
                Need(BaseBattleLoot.Finish(state, loot.BattleId), "战果没有关闭");
            }
        }

        private static void Need(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        private static void Note(string message)
        {
            Report.Add(message);
            Debug.Log("[DemoPlayerRoute] " + message);
        }

        private static void SaveRouteCheckpoint(BaseState state, string name)
        {
            const string folder = "Logs/demo-route-checkpoints";
            BaseEconomy.Validate(state);
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, name + ".json"), JsonUtility.ToJson(state));
        }
    }
}

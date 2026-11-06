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
    // 副手探针（P42～P44）：解锁、指定、剧情防守不可委托、副手编成不含玩家队伍、
    // 后台真实模拟与同规则结算、权限只放查看与策略、战斗不耗日、存档往返。
    //
    // 用「首防已胜 + 世界道路已开」的状态，因为副手正是在讨伐黑鸦寨出发时解锁，
    // 而 Demo 在世界开放前不允许出现剧情以外的战斗。
    public static class VerifyDeputy
    {
        // 命令行入口：退出码 0 才算过。
        public static void RunAndExit()
        {
            try { Run(); EditorApplication.Exit(0); }
            catch (Exception error) { Debug.LogError("[Deputy] FAIL：" + error); EditorApplication.Exit(1); }
        }

        public static void Run()
        {
            var s = WorldOpened();
            Need(BaseDemoFinale.Opened(s), "探针需要世界道路已开放的状态");

            // 1) 解锁前：没有副手，也不能指定。
            Need(!BaseDeputy.Unlocked(s), "旧档默认不应有副手");
            Need(BaseDeputy.Problem(s).Contains("黑鸦寨"), "未解锁时应说明要讨伐黑鸦寨才解锁");
            Need(Rejects(() => BaseDeputy.Set(s, "partner_farm")), "未解锁时不能指定副手");

            // 2) 解锁挂在「首次出发讨伐黑鸦寨」上：出发前没有，Start 之后才有。
            var march = s.Copy();
            var plan = BaseMuster.PreviewFormation(march, "expedition", "combat");
            Need(BaseMuster.Confirm(march, plan), "出征编队应能确认");
            At(march, "demo_march_exit");
            BaseWorldJourney.EnterWorld(march, "camp_entry");
            Need(!BaseDeputy.Unlocked(march), "进入世界地图但未出发时仍不应解锁");
            BaseWorldJourney.Start(march, new[] { "camp", "bandit" });
            Need(BaseDeputy.Unlocked(march) && march.Demo.Deputy.UnlockedDay == march.Day, "首次出发讨伐黑鸦寨应解锁副手");
            BaseEconomy.Validate(march);
            Debug.Log("[Deputy] 出发讨伐时解锁 PASS");

            // 3) 指定：主角、随行成员都不能兼任，留守角色可以。
            BaseDeputy.Unlock(s);
            Need(BaseDeputy.Unlocked(s) && s.Demo.Deputy.UnlockedDay == s.Day, "解锁应记录当天日期");
            Need(Rejects(() => BaseDeputy.Set(s, s.PlayerLeaderId)), "主角不能当副手");
            Need(BaseDeputy.ChooseProblem(s, "partner_farm") != "", "随行成员不能兼任副手");
            s.Adventure.Members.Remove("partner_farm");
            BaseDeputy.Set(s, "partner_farm");
            Need(BaseDeputy.Leader(s)?.Id == "partner_farm" && BaseDeputy.Available(s), "留守角色应能担任副手");
            BaseEconomy.Validate(s);
            var saved = JsonUtility.FromJson<BaseState>(JsonUtility.ToJson(s));
            Need(saved.Demo.Deputy.Unlocked && saved.Demo.Deputy.LeaderId == "partner_farm", "副手应随存档保留");
            BaseEconomy.Validate(saved);
            Debug.Log("[Deputy] 解锁与指定 PASS");

            // 4) 剧情防守不可委托。
            Need(BaseDeputyBattle.Story(s, BaseDemoDefense.EventId) && BaseDeputyBattle.Story(s, BaseDemoDefense.SecondEventId), "首防与第二波应视为剧情防守");
            Need(!BaseDeputyBattle.Delegable(s, BaseDemoDefense.EventId), "剧情防守不应可委托");
            Need(BaseDeputyBattle.Problem(s, BaseDemoDefense.EventId).Contains("剧情"), "剧情防守应给出明确原因");
            Debug.Log("[Deputy] 剧情防守拒绝 PASS");

            // 5) 普通袭击：可以委托，编成不含玩家队伍。
            string eventId = AddRaid(s);
            BaseEconomy.Validate(s);
            Need(string.IsNullOrEmpty(BaseDeputyBattle.Problem(s, eventId)), "普通袭击应可交给副手：" + BaseDeputyBattle.Problem(s, eventId));
            var selection = BaseDeputyBattle.Selection(s);
            Need(selection.Candidates.Count >= 1 && selection.Candidates[0] == "partner_farm", "副手应排在 A 位");
            Need(selection.Candidates.All(id => id != s.PlayerLeaderId), "副手编成不能包含主角");
            Need(selection.Candidates.All(id => !s.Adventure.Members.Contains(id)), "副手编成不能包含随行队伍");
            Need(selection.Purpose == "defense" && selection.LeaderCount == selection.Candidates.Count, "副手编成应沿用迎战用途与人数");
            Debug.Log("[Deputy] 副手编成 A 位＝" + string.Join("、", selection.Candidates) + " PASS");

            // 6) 后台真实模拟：同规则结算、不耗日、不搬玩家。
            int day = s.Day, food = s.Inventory.Food;
            string location = s.Campaign.Location;
            double px = s.PlayerX, py = s.PlayerY;
            BaseDeputyBattle.Resolve(s, eventId, Document(s));
            BaseEconomy.Validate(s);
            Need(s.Day == day, "副手战不能消耗游戏日");
            Need(s.Campaign.Location == location && Math.Abs(s.PlayerX - px) < 1e-9 && Math.Abs(s.PlayerY - py) < 1e-9, "副手战不能把玩家搬走");
            Need(s.Campaign.Events.Single(e => e.Id == eventId).Status == "resolved", "副手战应把袭击结算掉");
            var report = s.Campaign.Reports.LastOrDefault(r => r.EventId == eventId);
            Need(report != null && report.Deputy && report.Title.Contains("副手迎战"), "战报应标记副手迎战");
            Need(report.Deployed > 0 && report.EnemyDeployed > 0, "副手战应记录双方实际参战人数");
            Need(!s.Workers.Any(w => w.Away), "副手战结束后队伍应返营");
            Need(s.Inventory.Food <= food, "副手战不应凭空增加口粮");
            Debug.Log("[Deputy] 后台模拟：部署 " + report.Deployed + "／敌军 " + report.EnemyDeployed + "，第 " + s.Day + " 天未变 PASS");

            // 7) 观战路径：同一场战斗，权限只放查看与策略；玩家队伍不进编成。
            var watch = saved.Copy();
            string watchId = AddRaid(watch);
            BaseEconomy.Validate(watch);
            var watchPlan = BaseDeputyBattle.Preview(watch);
            var opening = BaseCampaign.Begin(watch, watchId, Document(watch), "full", watchPlan, true);
            opening.Deputy = true;
            var session = new BaseCampaignSession(opening);
            Need(session.Battle.Control.View && session.Battle.Control.Strategy, "副手观战应保留查看与策略");
            Need(!session.Battle.Control.Order && !session.Battle.Control.Skill && !session.Battle.Control.Range, "副手观战不能移动／集火／放技能／改范围");
            Need(session.Opening.Deputy && session.Opening.Leaders.All(l => l.Id != watch.PlayerLeaderId), "观战编成不能带主角");
            session.Battle.AutoDeploy(BattleSide.A); session.Bind();
            Need(session.Battle.Start(out string reason), "副手观战应能开战：" + reason);
            Debug.Log("[Deputy] 观战权限：查看／策略开，移动／技能／范围关 PASS");

            // 8) 真实模型可跑完（不靠估算）：沿真实战斗打到结束再结算。
            var run = new RealtimeCampaignBattle(session);
            run.Model.SetPaused(false);
            int bound = (session.Opening.Scenario.MaxRounds + 1) * 8;
            for (int frame = 0; frame < bound && session.Battle.Phase == BattlePhase.Fighting; frame++) run.Tick(.25);
            Need(session.Battle.Phase == BattlePhase.Finished, "副手观战应在时限内由真实模型打完");
            Need(BaseCampaign.Settle(watch, session), "观战战果应走同一套结算");
            Need(watch.Campaign.Reports.Any(r => r.EventId == watchId && r.Deputy), "观战战报也要标记副手");
            BaseEconomy.Validate(watch);
            Debug.Log("[Deputy] 观战跑完并同规则结算 PASS");

            // 9) 过夜无人接手：也跑真实模型，不再用战力估算兜底。
            var overnight = saved.Copy();
            string nightId = AddRaid(overnight);
            BaseEconomy.Validate(overnight);
            int beforeDay = overnight.Day;
            Need(BaseCampaign.Rest(overnight, beforeDay, _ => Document(overnight), true), "过夜应能结算本日");
            Need(overnight.Day == beforeDay + 1, "过夜应推进一天");
            Need(overnight.Campaign.Events.Single(e => e.Id == nightId).Status == "resolved", "过夜碰到袭击应打真实战斗并结算");
            var nightReport = overnight.Campaign.Reports.LastOrDefault(r => r.EventId == nightId);
            Need(nightReport != null && !nightReport.Deputy, "过夜迎战不是副手战，战报不该标副手");
            BaseEconomy.Validate(overnight);
            Debug.Log("[Deputy] 过夜袭击走真实模型并结算 PASS");

            // 10) 不能中途改成玩家指挥：副手战编成里没有主角位，换人也只能换留守角色。
            Need(Rejects(() => BaseDeputy.Set(watch, watch.PlayerLeaderId)), "副手战不能把主角改成副手");
            Debug.Log("[Deputy] PASS：解锁、指定、剧情例外、编成、后台模拟、观战权限与不耗日");
        }

        // 首防胜利 → 领主交付 → 世界道路开放，玩家回到营地。
        private static BaseState WorldOpened()
        {
            string path = Path.Combine(Application.streamingAssetsPath, "BaseScenarios", BaseDemo.BalanceTemplate);
            var s = DemoStageSaveTools.Defense(File.ReadAllText(path), "male", DemoBalanceRuntime.Load().Rules);
            var defense = BaseDemoDefense.Begin(s, "none");
            BattleScenePlacement.Start(s, defense);
            var first = new RealtimeCampaignBattle(defense);
            BattleScenePlacement.Snap(s, first);
            BaseDemoDefense.Configure(first);
            foreach (var enemy in first.Model.Units.Where(u => u.Side == PrototypeSide.Enemy)) enemy.Escaped = true;
            first.Model.SetPaused(false); first.Tick(.02);
            Need(BaseDemoDefense.Complete(s, first), "首防结算");
            ClearLoot(s);
            BaseDemoDefense.AcknowledgeBatu(s);
            s.Campaign.Location = "base"; At(s, "demo_march_exit");
            BaseDemoFinale.OpenWorld(s);
            BaseEconomy.Validate(s);
            return s;
        }

        private static void ClearLoot(BaseState s)
        {
            foreach (var prisoner in s.Aftermath.Prisoners.Where(p => p.Status == "held").ToArray())
                Need(BaseAftermath.Decide(s, prisoner.Id, "release"), "释放俘虏");
            foreach (var manifest in s.Aftermath.Loot.Where(x => !x.Closed).ToArray())
            {
                foreach (var item in manifest.Items.Where(i => i.Status == "pending").ToArray())
                    Need(BaseBattleLoot.Resolve(s, manifest.BattleId, item.Id, s.PlayerLeaderId, true), "领取战果");
                Need(BaseBattleLoot.Finish(s, manifest.BattleId), "结束战果");
            }
        }

        private static void At(BaseState state, string id)
        {
            var obj = BaseSpatial.Objects(state).Single(o => o.Id == id);
            var point = obj.Blocks ? BaseStageWalk.Door(state, id) : new BasePoint(obj.X, obj.Y);
            state.PlayerX = point.X; state.PlayerY = point.Y; BaseAdventure.Arrive(state);
        }

        private static string AddRaid(BaseState s)
        {
            string id = "probe:deputy:raid";
            int n = 1;
            while (s.Campaign.Events.Any(e => e.Id == id)) id = "probe:deputy:raid:" + (++n);
            s.Campaign.Events.Add(new BaseCampaignEvent
            {
                Id = id, Title = "探针来袭部队", Kind = "defense", ScenarioFile = "23_据点迎战.json", Status = "pending",
                DepartedDay = s.Day, AvailableDay = s.Day, DeadlineDay = s.Day, HoldRounds = 12,
                FoodReward = 0, MaterialReward = 0, CoinReward = 0, FoodRaid = 1, MaterialRaid = 0, CoinRaid = 2
            });
            return id;
        }

        // 探针用小型敌军：副手战要跑到结束，敌人太多会让等待时间不可控。
        private static BattleScenarioDocument Document(BaseState s)
        {
            var army = (s.Campaign.EnemySites?.FirstOrDefault()?.Capacity ?? new ArmyDefinition()).Clone();
            army.HeroCount = 1; army.CaptainSlot = 0;
            army.MeleeCount = 1; army.RangedCount = 0; army.CavalryCount = 0;
            army.Commanders = new List<CommanderLoadout> { new CommanderLoadout { Melee = 1 } };
            army.CommanderStats = new List<SoldierStats>();
            army.CommanderNames = new List<string>();
            army.CommanderAppearances = new List<string>();
            army.CommanderRecruited = new List<bool>();
            return new BattleScenarioDocument
            {
                Scenario = new BattleScenario
                {
                    Board = BaseBattlefield.Board(s, "perimeter").Clone(),
                    A = army.Clone(), B = army.Clone(), MaxRounds = 60
                }
            };
        }

        private static bool Rejects(Action action)
        {
            try { action(); return false; }
            catch (InvalidOperationException) { return true; }
            catch (ArgumentException) { return true; }
        }

        private static void Need(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}

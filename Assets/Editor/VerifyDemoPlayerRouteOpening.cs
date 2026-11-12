using System;
using System.Linq;
using BaseConquest.Base;
using BaseConquest.Battle;
using BaseConquest.RealtimePrototype;
using UnityEngine;

namespace BaseConquest.Editor
{
    public static partial class VerifyDemoPlayerRoute
    {
        private static BaseState OpenRoute(string raw, RpgRules rules, string configHash,string sex="female",bool saveFixture=true,BaseStoryScenePackage story=null)
        {
            var source = JsonUtility.FromJson<BaseScenarioDocument>(raw);
            var opening = BaseDemo.CreateOpening(source, raw, true, true, true, rules, configHash,story);
            opening.Opening.Demo.Rpg.GenerationSeed = DemoStageSaveTools.StageGenerationSeed;
            var state = opening.CreateState();
            if(story!=null)CheckAuthoredBoard(state,story,"village");
            Need(BaseQuestLog.Cue(state).Control == "hero", "开场任务未指向选角");
            Need(BaseDemoIntro.Choose(state,sex) && BaseDemoIntro.PlayerArt(state)==(sex=="male"?"player":"female"), "序章无法选择主角："+sex);
            Need(BaseQuestLog.Cue(state).ObjectId == "demo_captain", "选角后任务未指向搭档");
            WalkToOpeningObject(state, "demo_captain");
            Need(BaseDemoIntro.Meet(state) && state.Adventure.Members.Contains("demo_captain"), "与搭档交谈未加入随行");
            WalkToOpeningObject(state, "intro_villager");
            Need(state.Demo.Intro.Walked > 0 && BaseDemoIntro.Patrol(state), "散步后无法与阿禾交谈");
            Need(BaseQuestLog.Cue(state).ObjectId == "intro_farm", "派工目标未指向粮田");
            WalkToOpeningObject(state, "intro_farm");
            Need(BaseDemoIntro.AssignFarm(state, 3) && state.Demo.Intro.Step == 9, "未达到四人便完成粮田任务");
            Need(BaseDemoIntro.AssignFarm(state, 4) && state.Demo.Intro.Step == 10, "确认四人后未自动推进粮田任务");
            WalkToOpeningObject(state, "demo_captain");
            Need(BaseDemoIntro.ReceiveSword(state), "搭档未交付序章短剑");
            Need(BaseQuestLog.Cue(state).Control == "sword", "领取武器后任务未指向背包");
            // Opening the backpack UI records this flag before the equip transaction.
            state.Demo.Intro.BackpackOpened = true;
            Need(BasePersonalEquipment.Equip(state, state.PlayerLeaderId, state.Demo.Intro.PracticeId, "weapon"),
                "序章短剑无法穿戴");
            BaseDemoIntro.CheckEquipment(state);
            Need(state.Demo.Intro.Step == 4, "穿戴后未开放演练");
            var villageBoard = BaseBattlefield.Board(state);
            var villageMemory = BaseSceneRules.Memory(state);
            Need(villageMemory.MapId == "demo_prologue" && villageMemory.Width == villageBoard.Width &&
                villageMemory.Height == villageBoard.Height && state.Campaign.RegionMaps.All(map => map.Id != villageMemory.MapId),
                "序章迷雾记忆须使用村庄尺寸，且不产生同名区域地图");
            BaseEconomy.Validate(state);
            Note("第1天 白桦村："+(sex=="female"?"女主角":"男主角")+"与搭档结伴，步行问候阿禾，粮田2→4，接过并穿戴序章短剑。");

            if(saveFixture)SaveRouteCheckpoint(state, "drill");
            PlayOpeningPractice(state);
            Note("序章演练：显式选人／暂停、实际下达移动与驻守、施放技能、查看增益，等待木桩示范和自动攻击，主动攻击搭档完成演练。");

            for (int frame = 0; frame < 1200 && !state.Demo.Intro.ReporterArrived; frame++)
                BaseDemoIntro.AdvanceReporter(state, .05);
            Need(state.Demo.Intro.ReporterArrived, "报信人无法走到序章会合点");
            WalkToOpeningObject(state, "prologue_report");
            Need(BaseDemoIntro.Depart(state), "无法向搭档告别");
            for (int frame = 0; frame < 1600 && !BaseDemoPrologue.Evacuating(state); frame++)
            {
                BaseDemoIntro.AdvanceReporterExit(state, .05);
                BaseDemoIntro.AdvanceDeparture(state, .05);
            }
            Need(BaseDemoPrologue.Evacuating(state) && BaseDemoPrologue.Followers(state).Count() == 3, "民兵离开后未推进三人撤离");
            Need(BasePartyInventory.Find(state, state.Demo.Intro.PracticeId) == null, "序章练习装备在撤离后仍留在背包");
            WalkToOpeningObject(state, "prologue_trigger");
            Need(BaseDemoPrologue.BeginPursuit(state), "抵达东侧撤离点未触发追兵");
            CheckPursuitDefeat(state.Copy());
            PlayOpeningPursuit(state);
            Need(BaseDemoPrologue.Recover(state), "重伤后无法进入断流滩新营地");
            if(story!=null)CheckAuthoredBoard(state,story,"camp");
            else At(state, "home");
            Need(BaseDemo.AcknowledgeWaking(state), "营帐醒来对白无法推进拜访领主");
            Need(BaseQuestLog.Cue(state).ObjectId == "exit_east" && !BaseQuestLog.ToggleTrack(state, "demo:ruins_trial"), "主线出口目标或未开放任务的追踪不正确");
            Need(BaseQuestLog.ToggleTrack(state, "demo:partner_farm") && BaseQuestLog.Cue(state).ObjectId == "demo_ahe", "手动追踪未指向阿禾");
            Need(BaseQuestLog.ToggleTrack(state, "demo:partner_farm") && BaseQuestLog.Cue(state).ObjectId == "exit_east", "取消追踪没有恢复主线");
            BaseEconomy.Validate(state);
            Note("追兵：核对伽兰战斗形象、村民撤离与追兵胜负两条路线。均接重伤剧情，可进入断流滩新营地。");
            return state;
        }

        private static void WalkToOpeningObject(BaseState state, string id)
        {
            var target = BaseSpatial.Objects(state).FirstOrDefault(item => item.Id == id);
            if (target == null && id == "demo_captain")
            {
                var captain = state.Leaders.Single(person => person.Id == id);
                target = new BaseMapObject(id, captain.Name, captain.MapX, captain.MapY, .45, .45, false);
            }
            Need(target != null, "序章缺少交互对象 " + id);
            var goal = target.Blocks ? BaseStageWalk.Door(state, id) : BaseSpatial.Nearest(state, new BasePoint(target.X, target.Y));
            var start = new BasePoint(state.PlayerX, state.PlayerY);
            var route = BaseStageWalk.Route(state, start, goal);
            Need(route.Count > 0 || start.Distance(goal) < .08, "序章无法步行到 " + id);
            foreach (var next in route)
            {
                for (int step = 0; step < 240; step++)
                {
                    double dx = next.X - state.PlayerX, dy = next.Y - state.PlayerY;
                    double distance = Math.Sqrt(dx * dx + dy * dy);
                    if (distance < .03) break;
                    double factor = Math.Min(1, .08 / distance);
                    var previous = new BasePoint(state.PlayerX, state.PlayerY);
                    BaseSpatial.Move(state, ref state.PlayerX, ref state.PlayerY, dx * factor, dy * factor);
                    var current = new BasePoint(state.PlayerX, state.PlayerY);
                    BaseDemoIntro.Walk(state, previous.Distance(current));
                    Need(previous.Distance(current) > .0001, "步行卡在序章对象 " + id + " 前");
                }
            }
            Need(new BasePoint(state.PlayerX, state.PlayerY).Distance(goal) < .08, "步行未抵达序章对象 " + id);
            BaseInteraction.Walk(new BasePoint(target.X - state.PlayerX, target.Y - state.PlayerY));
        }

        private static void PlayOpeningPractice(BaseState state)
        {
            var intro = state.Demo.Intro;
            var explorationStart = new BasePoint(state.PlayerX, state.PlayerY);
            var practice = new RealtimeCampaignBattle(BaseDemoIntro.Practice(state));
            state.SceneBattleId = BaseDemoIntro.BattleId;
            BattleScenePlacement.Snap(state, practice);
            Need(state.PlayerX == explorationStart.X && state.PlayerY == explorationStart.Y,
                "序章演练的战术站位不应覆盖开练前的村庄坐标");
            BaseDemoIntro.Configure(practice, intro);
            var actor = practice.Model.Player;
            int[] selected = { actor.Id - 1 };
            void NextAfter(int action)
            {
                Need(BaseDemoIntro.NextExercise(intro) == action, "演练引导步骤不一致 " + action);
                practice.TutorialAction?.Invoke(action);
                BaseDemoIntro.AdvanceTutorialDelay(intro, 1);
            }
            void AdvanceUntil(Func<bool> finished, int limit, string reason)
            {
                for (int step = 0; step < limit && !finished(); step++) practice.Tick(.05);
                Need(finished(), reason);
            }

            Need(intro.DrillFlags == 0, "演练预选错误地记成玩家选人");
            NextAfter(BaseDemoIntro.Select);
            NextAfter(BaseDemoIntro.Pause);
            practice.Model.SetPaused(false);
            var arena = practice.Model.Arena;
            var near = arena.Distances(actor.Cell, true).Select((cost, cell) => (cost, cell))
                .First(option => option.cost >= 1 && option.cost <= 1.5 && arena.CanStand(arena.Center(option.cell)));
            Need(practice.Issue(selected, PrototypeOrder.Move, arena.Center(near.cell), commanderOnly: true), "演练移动下令失败");
            AdvanceUntil(() => (intro.DrillFlags & BaseDemoIntro.Move) != 0, 240, "演练位移未计入");
            BaseDemoIntro.AdvanceTutorialDelay(intro, 1);
            Need(practice.Issue(selected, PrototypeOrder.Guard, actor.Position, commanderOnly: true) &&
                (intro.DrillFlags & BaseDemoIntro.Guard) != 0, "演练驻守下令失败");
            BaseDemoIntro.AdvanceTutorialDelay(intro, 1);
            Need(practice.CastSkill(BattleSide.A, 0) && (intro.DrillFlags & BaseDemoIntro.Skill) != 0, "演练鼓舞施放失败");
            BaseDemoIntro.AdvanceTutorialDelay(intro, 1);
            NextAfter(BaseDemoIntro.Inspect);
            BaseDemoIntro.StartDummyDemo(practice, intro);
            AdvanceUntil(() => intro.DummyPhase >= 2, 480, "搭档攻击木桩示范未完成");
            Need(practice.Strategy(0, SquadStrategy.Attack), "演练无法切进攻");
            AdvanceUntil(() => intro.DummyPhase >= 4, 900, "进攻策略未自动砍中木桩三次");
            BaseDemoIntro.AdvanceTutorialDelay(intro, 1);
            var opponent = practice.Model.Units.Single(unit => unit.Side == PrototypeSide.Enemy);
            Need(practice.Issue(selected, PrototypeOrder.Focus, opponent.Position, opponent.Id - 1, commanderOnly: true), "演练无法指向搭档");
            AdvanceUntil(() => practice.Battle.Phase == BattlePhase.Finished, 1200, "演练最后一击未结算");
            Need(practice.Battle.Report.Outcome == BattleOutcome.SideAWin && BaseDemoIntro.FinishPractice(state, true), "无法交出演练战果");
            BaseEconomy.Validate(state);
            BaseSceneRules.RememberStrategy(state, practice.Session);
        }

        private static void PlayOpeningPursuit(BaseState state)
        {
            var pursuit = new RealtimeCampaignBattle(BaseDemoPrologue.Encounter(state));
            var movement = new BaseCompanionMovement();
            movement.Synchronize(state, true);
            BaseDemoPrologue.Configure(pursuit, () => BaseDemoPrologue.Safe(state));
            Need(pursuit.Strategy(0, SquadStrategy.Protect), "追兵战无法驻守护送");
            pursuit.Model.SetPaused(false);
            for (int step = 0; step < 450 && !BaseDemoPrologue.Safe(state); step++)
            {
                pursuit.Tick(.05);
                movement.Update(state, .05, sceneActorsOnly: true);
                BaseDemoPrologue.CaptureDepartures(state, movement);
            }
            Need(BaseDemoPrologue.Safe(state) && pursuit.Battle.Phase == BattlePhase.Fighting, "村民未能走到东侧撤离点");
            Need(pursuit.Strategy(0,SquadStrategy.Attack),"追兵战无法下达进攻");
            for(int step=0;step<1800&&pursuit.Battle.Phase==BattlePhase.Fighting;step++)pursuit.Tick(.05);
            Need(pursuit.Battle.Report?.Outcome == BattleOutcome.SideAWin && BaseDemoPrologue.Resolve(state, pursuit),
                "追兵胜利条件未触发重伤");
            BaseSceneRules.RememberStrategy(state, pursuit.Session);
        }

        private static void CheckPursuitDefeat(BaseState state)
        {
            var encounter = BaseDemoPrologue.Encounter(state);
            Need(encounter.Battle.Soldiers.Where(unit => unit.Side == BattleSide.B).All(unit => unit.AppearanceId == "galan"), "追兵仍使用占位形象");
            var pursuit = new RealtimeCampaignBattle(encounter);
            BaseDemoPrologue.Configure(pursuit, () => BaseDemoPrologue.Safe(state));
            Need(!BaseDemoPrologue.Safe(state), "早败回归必须先保留仍在撤离的村民");
            pursuit.Model.Player.Health = 0;
            pursuit.Model.SetPaused(false);
            pursuit.Tick(.05);
            Need(pursuit.Battle.Report?.Outcome == BattleOutcome.SideBWin && BaseDemoPrologue.Resolve(state, pursuit), "主角提前倒下未接重伤剧情");
            Need(state.Demo.Stage == BaseDemoStage.PrologueWounded && BaseDemoPrologue.Safe(state) && !BaseDemoPrologue.Resolve(state, pursuit),
                "追兵败局未保留村民离场或战果重复提交");
            BaseEconomy.Validate(state);
            Need(BaseDemoPrologue.Recover(state) && state.Demo.Stage == BaseDemoStage.Waking, "追兵败局未进入断流滩");
        }
    }
}

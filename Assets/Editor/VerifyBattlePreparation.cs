using System;
using System.Linq;
using System.Reflection;
using BaseConquest.Base;
using BaseConquest.Battle;
using BaseConquest.RealtimePrototype;
using UnityEngine;

namespace BaseConquest.Editor
{
    public static class VerifyBattlePreparation
    {
        public static void Run()
        {
            VerifyNamedWorker();
            VerifySavedPreferences();
            VerifyRuins();
            VerifyDefenseAndAttack();
            Debug.Log("[VerifyBattlePreparation] PASS");
        }

        public static void VerifyRationsAndPrisoners()
        {
            var opening=Load("defense");
            var rules=opening.Demo.Rpg;
            Need(rules.TroopBaseline.Sum()==16&&rules.SoldierDeathSensitivity==1.5,"正式数值快照不匹配");
            double P(int n,double modifier=0)=>BaseDemoCombat.TroopDeathChance(16,n,rules,modifier);
            Need(P(8)==0&&P(16)==.5&&P(32)==1&&P(40,-.1)==1&&Math.Abs(P(32,-.1)-.9)<1e-9&&P(2,.1)==0,"P47 特性与截断顺序");
            var state=opening.Copy();
            state.Workers.Clear();
            state.Workers.Add(new BaseWorkerBucket{Kind=BaseTroopKind.Cavalry,Count=1,Health=100,MaxHealth=100});
            state.Workers.Add(new BaseWorkerBucket{Kind=BaseTroopKind.Melee,Count=1,Health=100,MaxHealth=100});
            BaseDevelopment.Allocate(state);
            state.Inventory.Food=1;
            state.Day++;
            var saved=JsonUtility.FromJson<BaseState>(JsonUtility.ToJson(state));
            var lost=new System.Collections.Generic.List<int>();
            Need(BaseEconomy.PayMorningFood(state,lost)&&lost.Count==1&&state.Workers.Sum(w=>w.Count)==1&&state.TodayFoodRequired==2&&state.TodayFoodConsumed==1,"四职业应各吃一份，仅一个未领粮者离队");
            Need(!BaseEconomy.PayMorningFood(state)&&BaseEconomy.AttackFactor(state)==1&&BaseEconomy.ProductionFactor(state)==1,"重复扣粮或饥饿减益");
            var copyLost=new System.Collections.Generic.List<int>();BaseEconomy.PayMorningFood(saved,copyLost);
            Need(lost.SequenceEqual(copyLost),"重复读档分粮结果变化");

            state=opening.Copy();
            var captive=new BasePrisoner{Id="p47-held",BattleId="p47-old",Name="剑士",Kind=SoldierKind.Melee,
                Stats=new SoldierStats{Health=100},Health=30};
            state.Aftermath.Prisoners.Add(captive);
            var ledger=new BaseLootManifest{BattleId=captive.BattleId,Title="旧战果",Closed=true};
            state.Aftermath.Loot.Add(ledger);
            BaseEconomy.Validate(state);
            Need(BaseBattleLoot.Pending(state)&&BaseBattleLoot.ShouldPresent(state)&&!BaseBattleLoot.CanFinish(state,ledger),"旧档已关清单掩盖待处置俘虏");
            Need(BaseAftermath.Decide(state,captive.Id,"release")&&BaseBattleLoot.CanFinish(state,ledger),"释放俘虏后仍无法离场");

            state=opening.Copy();At(state,"demo_defense_start");Stock(state,1).Count=24;
            var draft=new BaseBattlePreparation(state,BasePrepRoute.Defense);
            draft.AutoPlace();var run=Start(state,draft);
            var targets=run.Battle.Soldiers.Where(u=>u.Side==BattleSide.B&&u.Kind!=SoldierKind.Hero).Take(2).ToArray();
            var test=targets[0];var earlier=targets[1];
            run.Session.Opening.PlayerTroopCounts=new[]{16,0,0,0};
            double before=BaseDemoCombat.DeathChance(run.Session,test);
            test.KnockedDown=test.DeathResolved=true;earlier.KnockedDown=true;
            Need(BaseDemoCombat.DeathChance(run.Session,test)==before,"当前单位或未判定单位计入 N");
            earlier.DeathResolved=true;Need(BaseDemoCombat.DeathChance(run.Session,test)>before,"已判定倒地存活未计入 N");
            earlier.InstantDead=true;Need(BaseDemoCombat.DeathChance(run.Session,test)==before,"永久阵亡计入敌方存活数");
            Debug.Log("[P47] PASS casualty curve, survivors, legacy captives, whole rations and stable reload");
        }

        private static void VerifyNamedWorker()
        {
            var state = Load("defense");
            foreach (var batch in state.Workers) batch.SoldierNumbers = null;
            state.LastSoldierNumber = 0;
            BaseEconomy.Validate(state);
            var original = state.Workers.SelectMany(w => w.SoldierNumbers).ToArray();
            Need(original.SequenceEqual(Enumerable.Range(1, original.Length)), "旧档首次编号没有沿用原名册顺序");
            if (!state.Workers.Any(w => !w.Away && w.FacilityId=="farm" && w.Count>=2))
                Need(BaseEconomy.MoveWorkers(state,BaseTroopKind.Melee,"","farm",2),"无法准备两名粮田工人夹具");
            var worker = state.Workers.First(w => !w.Away && w.FacilityId == "farm" && w.Count >= 2);
            int number = worker.SoldierNumbers[1], neighbor = worker.SoldierNumbers[0];
            Need(BaseEconomy.MoveWorker(state, number, "farm", ""), "点名调离失败");
            Need(state.Workers.Single(w => w.SoldierNumbers.Contains(number)).FacilityId == "" &&
                state.Workers.Single(w => w.SoldierNumbers.Contains(neighbor)).FacilityId == "farm", "同批的另一名士兵被调离");
            Need(BaseEconomy.MoveWorker(state, number, "", "farm"), "原岗合批失败");
            BaseDevelopment.Allocate(state);
            var pick = BaseMuster.SoldierRows(state, worker.Kind).Single(r => r.Pick.SoldierNumber == number).Pick;
            var preset = state.Muster.Presets.Single(p => p.Purpose == "expedition").Copy();
            preset.SelectLeaders(new[] { state.PlayerLeaderId });
            foreach (BaseTroopKind kind in Enum.GetValues(typeof(BaseTroopKind))) preset.SetCount(kind, kind == worker.Kind ? 1 : 0);
            var plan = BaseMuster.Preview(state, "expedition", true, preset, new[] { pick });
            Need(BaseMuster.Confirm(state, plan) && state.Workers.Where(w => w.Away).SelectMany(w => w.SoldierNumbers).SequenceEqual(new[] { number }), "出征没有带走点中的士兵");
            state = JsonUtility.FromJson<BaseState>(JsonUtility.ToJson(state));
            BaseEconomy.Validate(state);
            Need(BaseMuster.Return(state, plan.Id) && state.Workers.All(w => !w.Away), "读档归队失败");
            var roster = BasePersonnel.Soldiers(state).ToList();
            Need(roster.Select(u => u.SoldierNumber).SequenceEqual(original) &&
                roster.Single(u => u.SoldierNumber == number).Key == "soldier:" + number, "调岗、出征或读档后人员编号变化");
            Debug.Log("[VerifyBattlePreparation] 固定编号、逐人调岗、出征与读档归队 PASS");
        }

        private static void VerifySavedPreferences()
        {
            var s=Load("defense");
            s.Muster.SlotTroops = new [] {new BaseCommanderTroops {Melee=1},new BaseCommanderTroops(),new BaseCommanderTroops()}.ToList();
            var soldiers=BasePersonnel.Soldiers(s,SoldierKind.Melee).Where(u => u.Health > 0).ToArray();
            int healthiest=soldiers.OrderByDescending(u=>u.Health/u.MaxHealth).ThenBy(u=>u.SoldierNumber).First().SoldierNumber;
            var player=s.Leaders.Single(l=>l.Id==s.PlayerLeaderId);
            player.PreferredSoldiers.Add(soldiers.OrderByDescending(u=>u.SoldierNumber).First().SoldierNumber);
            var before=JsonUtility.ToJson(s.Muster.SlotTroops[0]);
            var workerState=s.Workers.Select(w=>(w.FacilityId,w.LastBattleDay)).ToArray();
            s=JsonUtility.FromJson<BaseState>(JsonUtility.ToJson(s));
            BaseEconomy.Validate(s);
            var draft=new BaseBattlePreparation(s,BasePrepRoute.Defense);
            var selected=draft.Units.Single(u=>u.Selected && u.Kind==SoldierKind.Melee);
            Need(selected.SoldierNumber==healthiest && selected.TeamId==draft.PlayerId &&
                workerState.SequenceEqual(s.Workers.Select(w=>(w.FacilityId,w.LastBattleDay))),"预设未按健康自动填充，或提前占用人员");
            draft.SetTeamCount(draft.PlayerId,SoldierKind.Melee,0);
            Need(!selected.Selected && before==JsonUtility.ToJson(s.Muster.SlotTroops[0]), "手动调整本场编队覆写了兵营");
            var replacement=new BaseBattlePreparation(s,BasePrepRoute.Defense);
            Need(replacement.Units.Any(u=>u.SoldierNumber==healthiest && u.Selected),"重新备战未按原兵营预设填充");
            Debug.Log("[SavedPreferences] PASS");
        }

        private static BaseState Load(string id)
        {
            var asset = Resources.Load<TextAsset>("DemoStages/" + id + "-male");
            var state = JsonUtility.FromJson<BaseState>(asset.text);
            BaseEconomy.Validate(state);
            return state;
        }

        private static void At(BaseState state, string id)
        {
            var item = BaseSpatial.Objects(state).Single(o => o.Id == id);
            var point = item.Blocks ? BaseStageWalk.Door(state, id) : new BasePoint(item.X, item.Y);
            state.PlayerX = point.X;
            state.PlayerY = point.Y;
            BaseAdventure.Arrive(state);
        }

        private static void Need(bool ok, string message)
        {
            if (!ok) throw new InvalidOperationException(message);
        }

        private static BaseState ReadLegacyRoster(BaseState state)
        {
            var legacy = state.Copy();
            legacy.LastSoldierNumber = 0;
            foreach (var worker in legacy.Workers) worker.SoldierNumbers = null;
            foreach (var party in legacy.Muster.Parties)
                if (party.CommanderTroops != null)
                    foreach (var team in party.CommanderTroops) team.SoldierNumbers = null;
            if (legacy.Campaign.HasActiveBattle)
                foreach (var troop in legacy.Campaign.ActiveBattle.Troops) troop.SoldierNumbers = null;
            legacy = JsonUtility.FromJson<BaseState>(JsonUtility.ToJson(legacy));
            BaseEconomy.Validate(legacy);
            return legacy;
        }

        private static void ClearLoot(BaseState state)
        {
            foreach (var prisoner in state.Aftermath.Prisoners.Where(p => p.Status == "held").ToArray())
                Need(BaseAftermath.Decide(state, prisoner.Id, "release"), "战果俘虏");
            foreach (var manifest in state.Aftermath.Loot.Where(l => !l.Closed).ToArray())
            {
                foreach (var item in manifest.Items.Where(i => i.Status == "pending").ToArray())
                    Need(BaseBattleLoot.Resolve(state, manifest.BattleId, item.Id, state.PlayerLeaderId, true), "领取战果");
                Need(BaseBattleLoot.Finish(state, manifest.BattleId), "关闭战果");
            }
        }

        private static RealtimeCampaignBattle Start(BaseState state, BaseBattlePreparation draft)
        {
            var cell = draft.Positions[draft.PlayerId];
            Need(draft.Problem(state, true) == "", draft.Problem(state, true));
            var preview = draft.Power(state, true);
            Need(preview.ally > 0 && preview.enemy > 0, "战斗备战缺少敌我参考战力：" + draft.Route);
            var session = draft.Begin(state);
            var battle = new RealtimeCampaignBattle(session);
            var expected = draft.Units.Where(u => u.SoldierNumber > 0 && draft.Positions.ContainsKey(u.Id))
                .ToDictionary(u => u.SoldierNumber, u => u.TeamId);
            var opening = session.Opening;
            var enemies = opening.Enemies.Where(e => !e.Dead && !e.KnockedDown && e.Health > 0).ToArray();
            Need(draft.KnownEnemyCount() == enemies.Length && BattleSimulation.TroopKinds.Prepend(SoldierKind.Hero)
                .All(kind => draft.KnownEnemyCount(kind) == enemies.Count(e => e.Kind == kind)),
                "敌军编成与备战情报不一致：" + draft.Route);
            Need(opening.Troops.Count == expected.Count && opening.Troops.Select((troop, i) =>
                troop.SoldierNumbers.Count == 1 && expected.TryGetValue(troop.SoldierNumbers[0], out var team) &&
                opening.Leaders[opening.TroopSlots[i]].Id == team).All(matches => matches), "实际出战士兵编号或分队与落位不一致");
            for (int i = 0; i < opening.Troops.Count; i++)
            {
                int number = opening.Troops[i].SoldierNumbers[0];
                var actor = session.UnitForTroop(i);
                Need(actor?.SoldierNumber == number && actor.OriginCommanderSlot == opening.TroopSlots[i] &&
                    actor.Cell.Equals(draft.Positions["soldier:" + number]), "战场实体与所点士兵的编号、队长或部署格不一致");
            }
            Need(battle.Battle.Phase == BattlePhase.Fighting && battle.Model.Player != null, "草案未开战");
            Need(battle.Model.Units.Count(u => u.Side == PrototypeSide.Ally) == draft.Positions.Count, "未落位人员进了战场");
            Need((int)battle.Model.Player.Position.X == cell.X && (int)battle.Model.Player.Position.Z == cell.Z, "未使用主角落点");
            foreach (var rider in battle.Model.Units.Where(u => u.Side == PrototypeSide.Ally && u.Mounted))
                Need(rider.FootprintLength == 1 && battle.Model.Footprint(rider, rider.Cell, rider.Facing).All(n =>
                    battle.Battle.Board.Cell(new GridPosition(n % battle.Model.Arena.FineColumns / 2,
                        n / battle.Model.Arena.FineColumns / 2)).Zone == DeploymentZone.A), "骑兵占格跨出部署区");
            Need(draft.Problem(state, true) != "" && state.Supply.Quotas.Count == 1, "重复提交未拦截");
            return battle;
        }

        private static void VerifyRuins()
        {
            var state = Load("camp");
            At(state, "home");
            Need(BaseDemo.AcknowledgeWaking(state), "醒来");
            At(state, "exit_east");
            Need(BaseCampaign.Travel(state, "town"), "进入灰岩镇");
            At(state, "town_quest");
            Need(BaseDemo.AcceptLordQuest(state), "接取委托");
            At(state, "demo_ruins_exit");
            Need(BaseCampaign.Travel(state, "outpost"), "前往沉眠古窟");
            At(state, "demo_ruins_investigate");
            if (state.Demo.RuinsRewards == null) BaseRuinsRewards.Enable(state);
            state.Demo.RuinsRewards.Energy = 0;
            var draft = new BaseBattlePreparation(state, BasePrepRoute.Ruins);
            Need(draft.KnownEnemyPower(state) > 0 && draft.KnownEnemyCount(SoldierKind.Hero) == 1 &&
                draft.KnownEnemyCount(SoldierKind.Melee) == 2 && draft.KnownEnemyCount() == 3,
                "古窟首战应显示队长和两名受控制的剑士");
            Need(draft.AutoPlace() > 0, "古窟没有可用部署区");
            var battle = Start(state, draft);
            Need(BaseRuinsRewards.Energy(state) == 0, "古窟剧情首战应免体力");
            Need(battle.Battle.CommanderDisplayName(BattleSide.B, 0) == "迦兰" &&
                battle.Battle.Commander(BattleSide.B, 0).AppearanceId == "galan", "首战迦兰名称或外观被替换");
            BaseDemoRuins.Configure(battle);
            var chief = battle.Model.Units.Single(u => u.Side == PrototypeSide.Enemy && u.Role == PrototypeRole.Leader);
            chief.Health = 1;
            chief.KnockedDown = true;
            var pawn = battle.Battle.Soldier(chief.Id - 1);
            pawn.Health = 1;
            pawn.KnockedDown = pawn.DeathResolved = true;
            battle.Model.SetPaused(false);
            battle.Tick(.02);
            VerifyChenanAndRuins.WalkOutOfRuins(battle);
            Need(BaseDemoRuins.Complete(state, battle), "古窟首通");
            ClearLoot(state);
            At(state, "demo_ruins_investigate");
            var repeat = new BaseBattlePreparation(state, BasePrepRoute.Ruins);
            state.Demo.RuinsRewards.Energy = 19;
            Need(repeat.Problem(state, false).Contains("体力不足") && repeat.PreviewSweep(state).Problem.Contains("体力不足") &&
                !state.Campaign.HasActiveBattle && BaseRuinsRewards.Energy(state) == 19, "古窟低体力校验或预览修改了存档");
            state.Demo.RuinsRewards.Energy = 20;
            var sweep = repeat.PreviewSweep(state);
            Need(sweep.EnemyPower > 0 && (sweep.Problem == "") == (sweep.AllyPower > sweep.EnemyPower * 1.5), "扫荡门槛与正式战力不一致");
            VerifyPreparedSweepSupply(state.Copy());
            var nextCaptainState = state.Copy();
            repeat.AutoPlace();
            var repeatBattle = Start(state, repeat);
            Need(!repeatBattle.Session.Opening.RuinsFirstClear && BaseRuinsRewards.Energy(state) == 0,
                "古窟重战误作首通或未扣除体力");
            var enemyCaptain = repeatBattle.Session.Opening.Enemies.Single(unit => unit.Kind == SoldierKind.Hero);
            Need(enemyCaptain.Name == "异族队长1" && enemyCaptain.Appearance == "galan" &&
                repeatBattle.Battle.UnitName(repeatBattle.Battle.Commander(BattleSide.B, 0)) == "敌方异族队长1" &&
                repeatBattle.Battle.Commander(BattleSide.B, 0).AppearanceId == "galan" &&
                repeatBattle.Session.Opening.Enemies.Count(unit => unit.Kind == SoldierKind.Mage) == 2,
                "古窟再战队长名称、外观或普通法师编制错误");
            repeatBattle.SaveCheckpoint(state);
            var checkpoint = state.Campaign.ActiveBattle;
            var legacyCaptain = checkpoint.Enemies.Single(unit => unit.Kind == SoldierKind.Hero);
            legacyCaptain.Name = "古窟术士";
            legacyCaptain.Appearance = "";
            var legacyMissionCaptain = state.Campaign.Events.Single(e => e.Id == BaseDemoRuins.EventId).EnemyArmy
                .Single(unit => unit.Kind == SoldierKind.Hero);
            legacyMissionCaptain.Name = legacyCaptain.Name;
            legacyMissionCaptain.Appearance = legacyCaptain.Appearance;
            checkpoint.Scenario.B.CommanderNames[0] = checkpoint.Checkpoint.Scenario.B.CommanderNames[0] = "古窟术士";
            checkpoint.Checkpoint.Units.Single(unit => unit.Side == BattleSide.B && unit.Kind == SoldierKind.Hero).AppearanceId = "";
            var restored = new BaseCampaignSession(JsonUtility.FromJson<BaseCampaignBattle>(JsonUtility.ToJson(checkpoint)));
            Need(restored.Battle.CommanderDisplayName(BattleSide.B, 0) == "异族队长1" &&
                restored.Battle.Commander(BattleSide.B, 0).AppearanceId == "galan", "读回旧战中档没有恢复编号或外观");
            VerifyRuinsRematch(state, new RealtimeCampaignBattle(restored));
            nextCaptainState.Demo.RuinsRewards.EnemyGeneration = state.Demo.RuinsRewards.EnemyGeneration;
            var nextDraft = new BaseBattlePreparation(nextCaptainState, BasePrepRoute.Ruins);
            nextDraft.AutoPlace();
            var nextBattle = Start(nextCaptainState, nextDraft);
            Need(nextBattle.Battle.CommanderDisplayName(BattleSide.B, 0) == "异族队长2", "替补队长未沿用下一编号");
            BaseDemoRuins.Configure(nextBattle);
            var nextCaptain = nextBattle.Battle.Commander(BattleSide.B, 0);
            var nextActor = nextBattle.Model.Units.Single(unit => unit.Id == nextCaptain.Id + 1);
            double deathChance = BaseDemoCombat.DeathChance(nextBattle.Session, nextCaptain);
            nextBattle.Battle.Scenario.RandomSeed = Enumerable.Range(1, 10000)
                .First(seed => BattleSimulation.StableRoll(seed, nextCaptain.Id, 11) < deathChance);
            nextBattle.Model.SetPaused(false);
            nextBattle.Model.ApplyResolvedHit(nextBattle.Model.Player, nextActor, nextActor.Health);
            Need(nextCaptain.InstantDead && !nextCaptain.KnockedDown && !nextBattle.RuinsExitOpen,
                "再战异族队长必须按普通即死率判定，不能固定存活");
            Debug.Log("[VerifyBattlePreparation] 古窟首通、重打、扫荡门槛 PASS");
        }

        private static void VerifyRuinsRematch(BaseState state, RealtimeCampaignBattle run)
        {
            run.EnableBattlefieldExits();
            BaseDemoRuins.Configure(run);
            var captain = run.Battle.Commander(BattleSide.B, 0);
            var mages = run.Battle.Soldiers.Where(unit => unit.Side == BattleSide.B && unit.Kind == SoldierKind.Mage).ToArray();
            double captainDeath = BaseDemoCombat.DeathChance(run.Session, captain);
            double mageDeath = BaseDemoCombat.DeathChance(run.Session, mages[0]);
            double captureRate = run.Session.Opening.Difficulty.DropRate;
            int seed = Enumerable.Range(1, 10000).First(i => BattleSimulation.StableRoll(i, captain.Id, 11) >= captainDeath &&
                BattleSimulation.StableRoll(i, mages[0].Id, 11) >= mageDeath &&
                BattleSimulation.StableRoll(i, mages[0].Id, 23) < captureRate &&
                BattleSimulation.StableRoll(i, mages[1].Id, 23) >= captureRate);
            run.Battle.Scenario.RandomSeed = seed;
            run.Session.Opening.Scenario.RandomSeed = seed;
            run.Model.SetPaused(false);
            Strike(captain);
            run.Tick(.02);
            Need(captain.KnockedDown && mages.All(unit => unit.IsActive && !unit.Rescued) &&
                run.Model.LeaderlessEnemyAssault && run.Battle.Phase == BattlePhase.Fighting &&
                !run.RuinsExitOpen && !run.VictoryExitOpen && !run.AwaitingRescueDialogue,
                "队长倒地后法师应继续交战，不能触发救援或胜利");
            Strike(mages[0]);
            run.Tick(.02);
            Need(mages[1].IsActive && !run.VictoryExitOpen && !BaseDemoRuins.Complete(state, run),
                "尚有一名法师时不应判胜或结算");
            Strike(mages[1]);
            run.Tick(.02);
            Need(!run.RuinsExitOpen && (run.VictoryExitOpen || run.Battle.Report?.Outcome == BattleOutcome.SideAWin),
                "歼灭后没有进入普通胜利离场");
            if (run.Battle.Phase == BattlePhase.Fighting)
            {
                Need(run.OrderBattlefieldExit(null), "歼灭后无法下令前往出口");
                for (int tick = 0; tick < 3000 && run.Battle.Phase == BattlePhase.Fighting; tick++) run.Tick(.025);
            }
            string battleId = run.Session.Opening.Id;
            string plotCaptain = state.Demo.Flow.FirstControllerId;
            Need(run.Battle.Report?.Outcome == BattleOutcome.SideAWin && BaseDemoRuins.Complete(state, run), "古窟再战未提交胜利");
            var prisoners = state.Aftermath.Prisoners.Where(unit => unit.BattleId == battleId && unit.Status == "held").ToArray();
            var capturedCaptain = prisoners.Single(unit => unit.Kind == SoldierKind.Hero);
            var capturedMage = prisoners.Single(unit => unit.Kind == SoldierKind.Mage);
            Need(prisoners.Length == 2 && capturedCaptain.Name.Contains("异族队长1") && capturedCaptain.IsAlien &&
                capturedCaptain.AppearanceId == "galan" && capturedMage.Id == battleId + ":" + mages[0].Id,
                "普通俘虏概率或敌方队长身份未保留");
            Need(BaseAftermath.Decide(state, capturedMage.Id, "recruit") && BaseAftermath.Decide(state, capturedCaptain.Id, "recruit") &&
                state.Workers.Any(unit => unit.Kind == BaseTroopKind.Mage && unit.Recruited) &&
                state.Leaders.Any(unit => unit.Id == "partner_recruit:" + capturedCaptain.Id && unit.IsAlien) &&
                state.Demo.Flow.FirstControllerId == plotCaptain, "法师或异族队长无法按普通俘虏规则招降");
            Debug.Log("[RuinsRematch] PASS 歼灭、出口、法师概率捕获和队长招降");

            void Strike(BattleSoldier unit)
            {
                var actor = run.Model.Units.Single(candidate => candidate.Id == unit.Id + 1);
                run.Model.ApplyResolvedHit(run.Model.Player, actor, actor.Health);
            }
        }

        private static BaseEquipmentStock Stock(BaseState state, int grade) => state.Inventory.Equipment.Single(e => e.Product == "supply" && e.Level == grade);

        private static void VerifyPreparedSweepSupply(BaseState state)
        {
            var stats = new SoldierStats { Health = 1000, Damage = 1000, Defense = 100 };
            state.Workers.Add(new BaseWorkerBucket { Kind = BaseTroopKind.Melee, Count = 2, Health = stats.Health,
                MaxHealth = stats.Health, HasCombatProfile = true, CombatStats = stats });
            Stock(state, 2).Count = 2;
            var draft = new BaseBattlePreparation(state, BasePrepRoute.Ruins) { SupplyGrade = 2 };
            foreach (var soldier in draft.Units.Where(u => u.Kind != SoldierKind.Hero))
            {
                soldier.Selected = true;
                soldier.TeamId = draft.PlayerId;
            }
            string before = JsonUtility.ToJson(state);
            var estimate = draft.PreviewSweep(state);
            Need(estimate.Problem == "", "扫荡军需夹具无法扫荡：" + estimate.Problem);
            Need(before == JsonUtility.ToJson(state), "扫荡预览修改军需库存");
            draft.Sweep(state);
            Need(Stock(state, 2).Count == 0 && state.Campaign.Reports.Last().GearLost == 2 && state.Supply.Quotas.Count == 0,
                "扫荡未按实际编队人数全额扣军需");
            Need(BaseRuinsRewards.Energy(state) == 0 && BaseRuinsRewards.Claim(state) &&
                BaseRuinsRewards.Energy(state) == 0 && !BaseRuinsRewards.Claim(state), "古窟扫荡或领奖体力结算错误");
        }

        public static void VerifyBattlefieldExits()
        {
            (BaseState state, RealtimeCampaignBattle run) Defense()
            {
                var state = Load("defense");
                Stock(state, 1).Count = 24;
                At(state, "demo_defense_start");
                var draft = new BaseBattlePreparation(state, BasePrepRoute.Defense) { SupplyGrade = 1 };
                draft.AutoPlace();
                var run = Start(state, draft);
                run.EnableBattlefieldExits();
                run.OutcomeResolver = _ => BattleOutcome.None;
                run.Model.CanTarget = (_, _) => false;
                run.Model.CanMove = unit => unit.Side == PrototypeSide.Ally;
                Need(run.HasBattlefieldExits, "首防未使用正式出口");
                return (state, run);
            }

            var (withdrawing, retreat) = Defense();
            var commanders = retreat.Model.Units.Where(u => u.Side == PrototypeSide.Ally && u.Role == PrototypeRole.Leader).ToArray();
            var first = commanders.First(unit => retreat.Model.Units.Any(ally => ally.Side == unit.Side &&
                ally.Role != PrototypeRole.Leader && ally.Squad == unit.Squad));
            Need(commanders.Length >= 2, "撤退夹具需要两个出战小队");
            var hisTroops = retreat.Model.Units.Where(u => u.Side == PrototypeSide.Ally &&
                u.Role != PrototypeRole.Leader && u.Squad == first.Squad).ToArray();
            var otherSquad = retreat.Model.Units.Where(u => u.Side == PrototypeSide.Ally && u.Squad != first.Squad).ToArray();
            Need(retreat.OrderBattlefieldExit(new[] { first.Id - 1 }), "选中的队长未收到出口指令");
            Need(hisTroops.All(u => u.Order != PrototypeOrder.Retreat && !u.Escaped) && retreat.Battle.Report == null,
                "队长尚未离场时，所属士兵已撤退，或产生了战报");
            AdvanceUntil(retreat, () => first.Escaped && hisTroops.All(u => u.Escaped));
            Need(otherSquad.All(u => !u.Escaped && u.Order != PrototypeOrder.Retreat) && retreat.Battle.Report == null,
                "撤退指令影响了未选中的小队或提前结算");
            Need(retreat.OrderBattlefieldExit(null), "剩余小队未收到出口指令");
            AdvanceUntil(retreat, () => retreat.Battle.Phase == BattlePhase.Finished);
            Need(retreat.Battle.Report?.Outcome == BattleOutcome.Retreat && otherSquad.All(u => u.Escaped),
                "结算未保留实际离场小队");
            Need(BaseDemoCombat.Fail(withdrawing, retreat), "实际离场后的首防败退不可结算");

            var (winning, defense) = Defense();
            defense.SaveCheckpoint(winning);
            var legacy = JsonUtility.FromJson<BaseState>(JsonUtility.ToJson(winning));
            legacy.Campaign.ActiveBattle.RealtimeState.BattlefieldVictoryPending = true;
            var restored = new RealtimeCampaignBattle(new BaseCampaignSession(legacy.Campaign.ActiveBattle));
            restored.EnableBattlefieldExits();
            Need(!restored.VictoryExitOpen && restored.Battle.Report?.Outcome == BattleOutcome.SideAWin &&
                BaseDemoDefense.Complete(legacy, restored), "首防旧版出口检查点未恢复为可结算的战报");

            defense.OutcomeResolver = _ => BattleOutcome.SideAWin;
            defense.Model.SetPaused(false);
            defense.Tick(1d / 60);
            Need(!defense.VictoryExitOpen && defense.Battle.Report?.Outcome == BattleOutcome.SideAWin &&
                BaseDemoDefense.Complete(winning, defense), "首防达到胜利条件后应在当前战场结算");
            Debug.Log("[VerifyBattlefieldExits] PASS partial squad withdrawal and immediate defense settlement");

            static void AdvanceUntil(RealtimeCampaignBattle run, Func<bool> done)
            {
                run.Model.SetPaused(false);
                for (int t = 0; t < 800 && !done() && run.Battle.Phase == BattlePhase.Fighting; t++) run.Tick(.1);
                Need(done(), "等待单位实际抵达出口超时");
            }
        }

        public static void VerifyMeleePursuit()
        {
            var arena = new PrototypeArena(PrototypeLayout.Open);
            var model = new RealtimeBattlePrototype(arena)
            {
                CommanderDoctrine = true,
                AutonomousOutcome = false
            };
            var attacker = model.AddUnit(PrototypeSide.Ally, PrototypeRole.Leader, new PrototypePoint(3.25, 4.25));
            var target = model.AddUnit(PrototypeSide.Enemy, PrototypeRole.Leader, new PrototypePoint(3.75, 4.25));
            attacker.Strategy = PrototypeStrategy.Obey;
            attacker.AttackRange = .5;
            attacker.Speed = 2;
            target.Speed = 2;
            int releases = 0;
            bool sometimesMoving = false;
            model.WindupDuration = _ => 1;
            model.AttackTriggered = unit =>
            {
                if (unit != attacker) return;
                Need(unit.WindupTarget == target.Id && model.InAttackRadius(unit, target),
                    "近战在攻击半径外或对更换后的目标释放");
                releases++;
            };
            var destination = new PrototypePoint(3.75, 11.25);
            Need(model.IssueOrder(0, attacker.Id, PrototypeOrder.Focus, target.Position, target.Id), "追击指令失败");
            model.MarkDirectOrder(new[] { 0 }, PrototypeOrder.Focus, target.Id, false);
            Need(model.IssueOrder(0, target.Id, PrototypeOrder.Move, destination, side: PrototypeSide.Enemy), "撤退目标的移动指令失败");
            model.MarkDirectOrder(new[] { 0 }, PrototypeOrder.Move, -1, false, PrototypeSide.Enemy);
            model.SetPaused(false);
            for (int frame = 0; frame < 150 && releases == 0; frame++)
            {
                model.Tick(1d / 60);
                sometimesMoving |= attacker.Moving && attacker.HitAt >= 0 && !attacker.HitResolved;
            }
            Need(sometimesMoving && releases == 1, "贴着同一后退目标，未成功在持续移动中完成前摇");
            var previousReleases = releases;
            model.HeightShotClear = (_, _) => false;
            for (int frame = 0; frame < 90; frame++) model.Tick(1d / 60);
            Need(releases == previousReleases, "目标被遮挡后仍可释放近战攻击");
            Debug.Log("[VerifyMeleePursuit] PASS moving windup, radius and obstruction");
        }

        public static void VerifyDefenseWorkshopLoop()
        {
            // 使用随包首防存档及正式的备战、结算和生产入口。分别覆盖主动撤退、击退和超时撤军。
            foreach(string ending in new[]{"退兵","击退","坚守"})
            {
                var state=Load("defense");
                state.Inventory.Coins=0;
                Stock(state,1).Count=24;
                At(state,"demo_defense_start");
                var draft=new BaseBattlePreparation(state,BasePrepRoute.Defense);
                var reservists=draft.Units.Where(u=>u.SoldierNumber>0).Take(3).ToArray();
                foreach(var unit in reservists)unit.Selected=false;
                draft.AutoPlace();
                var run=Start(state,draft);
                BaseDemoDefense.Configure(run);
                var enemies=run.Model.Units.Where(u=>u.Side==PrototypeSide.Enemy).ToArray();
                int originalRaw=state.Inventory.Materials;
                if(ending=="退兵")
                {
                    int expected=BaseRaidLoot.Charge(state,enemies.Length,run.Battle.Scenario.Objective.HoldRounds);
                    Need(BaseDemoCombat.Fail(state,run),"首防撤退结算失败");
                    Need(expected>0&&state.Inventory.Coins==-expected,"撤退预估与修缮实扣不一致");
                }
                else
                {
                    if(ending=="击退")
                    {
                        foreach(var enemy in enemies.Take((enemies.Length+1)/2))
                        {
                            enemy.KnockedDown=true;enemy.Health=1;
                            var soldier=run.Battle.Soldier(enemy.Id-1);
                            soldier.KnockedDown=true;soldier.Health=1;soldier.DeathResolved=true;
                        }
                    }
                    else
                    {
                        foreach(var actor in run.Model.Units)
                            run.Battle.Soldier(actor.Id-1).DeveloperInvulnerable=true;
                    }
                    run.Model.SetPaused(false);
                    for(int i=0;i<400 && !run.DefensePursuitReady;i++)run.Tick(.1);
                    Need(run.DefensePursuitReady,ending+"未触发敌军撤离");
                    run.Tick(.02);
                    Need(enemies.Where(e=>e.Alive).All(e=>e.Order==PrototypeOrder.Retreat),"存活敌军没有收到撤离命令");
                    int recovered=BaseRaidLoot.Recovered(run.DefenseDown);
                    Need(run.FinishDefensePursuit()&&BaseDemoDefense.Complete(state,run),"守胜结算失败");
                    var report=state.Campaign.Reports.Last();
                    Need(report.DropMaterials==recovered && state.Inventory.Materials==originalRaw+recovered+report.MaterialChange,"回收与固定赏金未分别结算");
                    Need(!BaseDemoDefense.Complete(state,run),"守胜可重复结算");
                    ClearLoot(state);
                    BaseDemoDefense.AcknowledgeBatu(state);
                }
                state.Campaign.Location="base";
                Need(BaseEconomy.SelectProduct(state,"supply"),"回营无法选择军需生产");
                var production=typeof(BaseEconomy).GetMethod("CompleteProductionDay",BindingFlags.NonPublic|BindingFlags.Static);
                foreach(var unit in reservists)
                {
                    var worker=state.Workers.Single(w=>w.SoldierNumbers.Contains(unit.SoldierNumber));
                    if(worker.FacilityId!="workshop")
                        Need(BaseEconomy.MoveWorker(state,unit.SoldierNumber,worker.FacilityId,"workshop"),"未能将留营士兵编入工坊");
                }
                var stock=Stock(state,BaseConstruction.ProductLevel(state));
                int startCount=stock.Count;
                var preview=BaseEconomy.Preview(state);
                production.Invoke(null,new object[]{state,false});
                Need(preview.ProductsMade>0 && stock.Count==startCount+preview.ProductsMade,"工坊库存与日结预估不一致");
                BaseEconomy.Validate(state);
                Debug.Log($"[DefenseWorkshop] {ending} · 原料 {originalRaw} → {state.Inventory.Materials}，修缮后余额 {state.Inventory.Coins}，生产 {preview.ProductsMade}");
            }
            VerifyChenanAndRuins.VerifyTopBar();
            Debug.Log("[DefenseWorkshop] PASS");
        }

        private static void VerifyDefenseAndAttack()
        {
            var state = Load("defense");
            At(state, "demo_defense_start");
            Stock(state, 1).Count = Stock(state, 2).Count = 24;
            var before = JsonUtility.ToJson(state);
            var draft = new BaseBattlePreparation(state, BasePrepRoute.Defense);
            var soldier = draft.Units.First(u => u.Selected && u.Kind != SoldierKind.Hero && u.TeamId == draft.PlayerId);
            var cell = draft.Board.Positions().First(p => draft.CanPlace(draft.PlayerId, p));
            Need(!draft.Place(soldier.Id, cell), "队长未落位时布置士兵");
            Need(draft.AutoPlace() > 0, "首防不能自动部署");
            Need(!draft.Place(soldier.Id, draft.Positions[draft.PlayerId]), "接受重叠部署");
            Need(draft.SupplyGrade == 1 && draft.SupplyProblem(state, false) == "" && draft.SupplyProblem(state, true) == "", "默认一级军需未生效");
            draft.SupplyGrade = 1;
            var limited = state.Copy();
            Stock(limited, 1).Count = draft.RequiredSupply(true) - 1;
            Need(draft.SupplyProblem(limited, false) != "" && draft.SupplyProblem(limited, true) != "", "军需不足时未拦截");
            draft.Recall(soldier.Id);
            int deployed = draft.RequiredSupply(true);
            Need(deployed < draft.RequiredSupply(false) && draft.SupplyProblem(limited, true) == "", "军需未按实际落位校验");
            Need(before == JsonUtility.ToJson(state), "预览、落位或撤回修改了真实状态");
            var retreatState = state.Copy();
            var retreatDraft = new BaseBattlePreparation(retreatState, BasePrepRoute.Defense) { SupplyGrade = 2 };
            retreatDraft.AutoPlace();
            var retreat = Start(retreatState, retreatDraft);
            int woundedNumber = 0;
            double baseMax = 0;
            if (retreatState.Demo.Rpg.RatioSupply)
            {
                var opening = retreat.Session.Opening;
                var soldierIndex = Enumerable.Range(0, opening.Troops.Count).First(i => retreat.Session.UnitForTroop(i) != null);
                var source = opening.Troops[soldierIndex];
                var supplied = retreat.Session.UnitForTroop(soldierIndex);
                bool Near(double a, double b) => Math.Abs(a - b) < .001;
                Need(Near(supplied.Stats.Health, source.MaxHealth * 1.3) && Near(supplied.Health, source.Health * 1.3) &&
                    Near(supplied.Stats.Damage, source.CombatStats.Damage * 1.3 * supplied.ProvisionAttackFactor) &&
                    Near(supplied.Stats.Defense, source.CombatStats.Defense * 1.3), "二级军需攻血防增幅错误");
                var main = retreat.Battle.Soldiers.Single(u => u.Side == BattleSide.A && u.Kind == SoldierKind.Hero && u.OriginCommanderSlot == 0);
                Need(Near(main.Stats.Health, opening.Leaders[0].MaxHealth) && main.ProvisionHealthBonus == 0,
                    "队长错误获得普通兵军需");
                var restoredTier2 = JsonUtility.FromJson<BaseState>(JsonUtility.ToJson(retreatState));
                BaseEconomy.Validate(restoredTier2);
                var restoredBattle = new BaseCampaignSession(restoredTier2.Campaign.ActiveBattle);
                Need(Near(restoredBattle.UnitForTroop(soldierIndex).Health, supplied.Health), "二级军需生命读档后变化");
                woundedNumber = supplied.SoldierNumber;
                baseMax = source.MaxHealth;
                supplied.Health = supplied.Stats.Health * .5f;
                retreat.Model.Units.Single(u => u.Id == supplied.Id + 1).Health = supplied.Health;
            }
            Need(BaseDemoCombat.Fail(retreatState, retreat) && Stock(retreatState, 2).Count == 24 - retreatDraft.RequiredSupply(true) &&
                Stock(retreatState, 1).Count == 24 && retreatState.Supply.Quotas.Count == 0, "正式撤退军需未全扣或错扣等级");
            if (woundedNumber > 0)
            {
                var returned = retreatState.Workers.Single(w => w.SoldierNumbers.Contains(woundedNumber));
                Need(returned.MaxHealth == baseMax && returned.Health == CombatNumbers.RemainingHealth(baseMax * .5),
                    "归队后未按原上限保留伤情比例");
            }

            var wear = state.Supply.Wear.Select(entry => entry.Fraction).ToArray();
            var battle = Start(state, draft);
            var tier = state.Supply.Rules.Single(r => r.Product == "supply");
            var member = battle.Battle.Soldiers.First(u => u.Side == BattleSide.A && u.Kind != SoldierKind.Hero && u.OriginCommanderSlot == 0);
            Need(Math.Abs(member.ProvisionWeaponBonus - tier.LowBonus) < .0001 && Math.Abs(member.ProvisionArmorBonus - tier.LowDefense) < .0001,
                "所选军需等级未作用于实际上场士兵");
            Need(Stock(state, 1).Count == 24 && BaseSupply.Available(state, "supply", 1) == 24 - deployed, "确认开战应只锁定实际用量");
            var restored = JsonUtility.FromJson<BaseState>(JsonUtility.ToJson(state));
            BaseEconomy.Validate(restored);
            var resumed = new RealtimeCampaignBattle(new BaseCampaignSession(restored.Campaign.ActiveBattle));
            var legacyBattle = ReadLegacyRoster(state);
            var legacyOpening = new BaseCampaignSession(legacyBattle.Campaign.ActiveBattle).Opening;
            var legacyAway = legacyBattle.Workers.Where(w => w.PartyId == legacyOpening.PartyId).ToArray();
            var legacyNumbers = legacyOpening.Troops.Select(troop => troop.SoldierNumbers.Single()).ToArray();
            Need(legacyNumbers.Distinct().Count() == deployed && legacyOpening.Troops.All(troop =>
                legacyAway.Any(worker => worker.SourceId == troop.SourceId && worker.SoldierNumbers.Contains(troop.SoldierNumbers[0]))),
                "旧战中存档的参战人员未对回真实出征来源");
            Need(resumed.Model.Player.Position.Equals(battle.Model.Player.Position) &&
                resumed.Session.Opening.SupplySnapshot.FullConsumptionGrade == 1 &&
                resumed.Session.Opening.SupplySnapshot.Equipment.Single(entry => entry.Level == 1).Count == deployed, "开战检查点丢失军需或落点");
            BaseDemoDefense.Configure(battle);
            Win(state, battle, () => BaseDemoDefense.Complete(state, battle));
            Need(Stock(state, 1).Count == 24 - deployed && Stock(state, 2).Count == 24 && state.Supply.Quotas.Count == 0 &&
                state.Campaign.Reports.Last().GearLost == deployed && state.Supply.Wear.Select(entry => entry.Fraction).SequenceEqual(wear),
                "正式胜利未按实际上场全扣或改写旧磨损余数");
            BaseDemoDefense.AcknowledgeBatu(state);

            var second = state.Copy();
            var advance = typeof(BaseEconomy).GetMethod("CompleteProductionDay", BindingFlags.Static | BindingFlags.NonPublic);
            while (second.Day < BaseDemoDefense.SecondArrival(second)) advance.Invoke(null, new object[] { second, false });
            second.Campaign.Location = "perimeter";
            At(second, "demo_defense_start");
            var secondDraft = new BaseBattlePreparation(second, BasePrepRoute.Second) { SupplyGrade = 2 };
            secondDraft.AutoPlace();
            int secondStock = Stock(second, 2).Count;
            var wave = Start(second, secondDraft);
            BaseDemoDefense.Configure(wave);
            Need(!wave.RetreatAll(), "第二波可撤退");
            wave.OutcomeResolver = _ => BattleOutcome.SideBWin;
            wave.Model.SetPaused(false);
            wave.Tick(.02);
            Need(BaseDemoCombat.Fail(second, wave) && Stock(second, 2).Count == secondStock - secondDraft.RequiredSupply(true), "正式败北军需未全扣");

            state.Campaign.Location = "base";
            At(state, "demo_march_exit");
            BaseDemoFinale.OpenWorld(state);
            var travel = new BaseBattlePreparation(state, BasePrepRoute.Travel);
            var teamIds = travel.Units.Where(unit => unit.Kind == SoldierKind.Hero && unit.Selected).Select(unit => unit.TeamId).ToArray();
            var pair = travel.Units.Where(unit => unit.SoldierNumber > 0 && unit.Selected)
                .GroupBy(unit => unit.Kind).First(group => group.Count() >= 2).Take(2).ToArray();
            pair[0].TeamId = teamIds[1];
            pair[1].TeamId = teamIds[0];
            var departingTeams = travel.Units.Where(unit => unit.SoldierNumber > 0 && unit.Selected)
                .ToDictionary(unit => unit.SoldierNumber, unit => unit.TeamId);
            Need(BaseMuster.Confirm(state, travel.TravelPlan(state)), "黑鸦寨行军编队失败");
            At(state, "demo_march_exit");
            BaseWorldJourney.EnterWorld(state, "camp_entry");
            var tooPoor=state.Copy();tooPoor.Inventory.Food=0;bool denied=false;
            try{BaseWorldJourney.Start(tooPoor,new[]{"camp","bandit"});}catch(InvalidOperationException){denied=true;}
            Need(denied&&!BaseWorldJourney.Marching(tooPoor)&&tooPoor.Inventory.Food==0,"路粮不足仍能出发");
            int foodBeforeMarch=state.Inventory.Food;
            BaseWorldJourney.Start(state, new[] { "camp", "bandit" });
            Need(foodBeforeMarch-state.Inventory.Food==departingTeams.Count&&state.Campaign.WorldJourney.ReservedRoadFood==departingTeams.Count,"大地图一日路应预扣每兵一份额外粮");
            var savedMarch=JsonUtility.FromJson<BaseState>(JsonUtility.ToJson(state));
            BaseEconomy.Validate(savedMarch);
            Need(savedMarch.Campaign.WorldJourney.ReservedRoadFood==departingTeams.Count,"行军读档丢失预扣路粮");
            var legacyTravel = ReadLegacyRoster(savedMarch);
            var legacyDraft = new BaseBattlePreparation(legacyTravel, BasePrepRoute.Travel);
            var legacyParty = legacyTravel.Muster.Parties.Single(p => p.Purpose == "expedition");
            Need(legacyParty.CommanderTroops.All(team => BattleSimulation.TroopKinds.All(kind =>
                legacyDraft.Units.Count(unit => unit.Selected && unit.Kind == kind && unit.TeamId == team.LeaderId) ==
                team.Actual.Count(BaseCampaign.ToBase(kind)))), "旧行军存档没有按原有兵种人数恢复出征分队");
            var marchingDraft = new BaseBattlePreparation(savedMarch, BasePrepRoute.Travel);
            Need(marchingDraft.Units.Where(unit => unit.Selected && unit.SoldierNumber > 0).All(unit =>
                departingTeams.TryGetValue(unit.SoldierNumber, out var captain) && captain == unit.TeamId),
                "行军读档后士兵编号或队长归属变化");
            Need(BaseWorldJourney.Step(state, _ => null), "行军失败");
            BaseWorldJourney.EnterScene(state, "bandit_entry");
            At(state, "demo_camp_attack");
            state = JsonUtility.FromJson<BaseState>(JsonUtility.ToJson(state));
            BaseEconomy.Validate(state);
            var attack = new BaseBattlePreparation(state, BasePrepRoute.BlackCrow);
            var arrived = state.Workers.Where(w => !w.Away && w.StationId == "bandit")
                .SelectMany(w => w.SoldierNumbers).ToHashSet();
            Need(arrived.SetEquals(departingTeams.Keys) && !state.Muster.Parties.Any(p => p.Purpose == "expedition") &&
                attack.Units.Where(unit => unit.SoldierNumber > 0).All(unit => arrived.Contains(unit.SoldierNumber)),
                "抵达寨外围后应按实际兵员驻扎，攻寨不能调用别处士兵");
            var reserve = arrived.Count;
            Need(reserve > 0, "攻营夹具没有驻军");
            VerifySubsetWound(state.Copy());
            Need(attack.Place(attack.PlayerId, attack.Board.Positions().First(p => attack.CanPlace(attack.PlayerId, p))), "无法仅部署主角");
            Need(attack.RequiredSupply(true) == 0 && attack.SupplyProblem(state, true) == "", "只有队长上场时被要求配军需");
            var encounter = Start(state, attack);
            Need(encounter.Session.Opening.Troops.Count == 0 && state.Workers.Where(w => !w.Away && w.StationId == "bandit").Sum(w => w.Count) == reserve,
                "未参战驻军被锁进战场");
            BaseDemoFinale.Configure(encounter);
            Win(state, encounter, () => BaseDemoFinale.Complete(state, encounter));
            Need(state.Workers.Where(w => !w.Away && w.StationId == "bandit").Sum(w => w.Count) == reserve && state.Campaign.Reports.Last().GearLost == 0,
                "仅队长参战时误扣军需或改动了场外随行士兵");
            Debug.Log("[VerifyBattlePreparation] 首防、第二波、行军、黑鸦寨、军需与存读 PASS");
        }

        private static void VerifySubsetWound(BaseState state)
        {
            var source = state.Workers.Where(w => !w.Away && w.StationId == "bandit" && w.Health >= w.MaxHealth * BaseDevelopment.DutyThreshold)
                .OrderByDescending(w => w.Count).First();
            int chosen = source.SoldierNumbers.Last();
            var originalHealth = state.Workers.SelectMany(w => w.SoldierNumbers.Select(n => (Number: n, w.Health))).ToDictionary(p => p.Number, p => p.Health);
            var draft = new BaseBattlePreparation(state, BasePrepRoute.BlackCrow) { SupplyGrade = 1 };
            foreach (var soldier in draft.Units.Where(u => u.Kind != SoldierKind.Hero))
            {
                soldier.Selected = soldier.SoldierNumber == chosen;
                soldier.TeamId = draft.PlayerId;
            }
            draft.AutoPlace();
            var battle = Start(state, draft);
            Need(battle.Session.Opening.Troops.Single().SoldierNumbers.Single() == chosen, "攻营快照未带走点中编号");
            var restored = JsonUtility.FromJson<BaseState>(JsonUtility.ToJson(state));
            BaseEconomy.Validate(restored);
            Need(restored.Campaign.ActiveBattle.Troops.Single().SoldierNumbers.Single() == chosen, "战斗读档丢失实际参战编号");
            var resumed = new RealtimeCampaignBattle(new BaseCampaignSession(restored.Campaign.ActiveBattle));
            var named = resumed.Session.UnitForTroop(0);
            Need(named.SoldierNumber == chosen && resumed.Battle.UnitName(named).Contains("#" + chosen), "战场显示的士兵编号与名册不同");
            BaseDemoFinale.Configure(resumed);
            resumed.EnableBattlefieldExits();
            var actor = resumed.Model.Units.Single(u => u.Side == PrototypeSide.Ally && u.Role != PrototypeRole.Leader);
            actor.Health = 7;
            Win(restored, resumed, () => BaseDemoFinale.Complete(restored, resumed));
            var after = BasePersonnel.Soldiers(restored).ToDictionary(u => u.SoldierNumber);
            Need(after.Count == originalHealth.Count && originalHealth.All(p => after[p.Key].Health == (p.Key == chosen ? 7 : p.Value)),
                "攻营战损写到未上场士兵或改变了编号");
            Need(!resumed.VictoryExitOpen, "攻寨胜利后应直接显示战报");

            At(restored, "demo_camp_world");
            var march = new BaseBattlePreparation(restored, BasePrepRoute.Travel, draftTravelTroops: false);
            var wounded = march.Units.Single(unit => unit.SoldierNumber == chosen);
            Need(!wounded.Selected && march.ToggleSoldier(march.PlayerId, wounded), "黑鸦寨伤兵不可选中行军");
            var back = new[] { "bandit", "camp" };
            int food = restored.Inventory.Food;
            BaseWorldJourney.Depart(restored, "bandit_entry", back, march);
            Need(restored.Inventory.Food == food - restored.Rules.SoldierFood &&
                restored.Workers.Single(w => w.SoldierNumbers.Contains(chosen)).Away, "伤兵未按真实编号出发或预扣路粮");
            restored = JsonUtility.FromJson<BaseState>(JsonUtility.ToJson(restored));
            Need(BaseWorldJourney.Step(restored, _ => null), "带伤兵返营的行军无法推进");
            BaseWorldJourney.EnterScene(restored, "camp_entry");
            var returned = restored.Workers.Single(w => w.SoldierNumbers.Contains(chosen));
            Need(!returned.Away && returned.StationId == "camp" &&
                (!BaseDevelopment.HeavilyWounded(returned) || !BaseDevelopment.CanDefend(returned)), "伤兵返营后驻地或防守资格错误");
            Debug.Log("[VerifyWoundedMarch] PASS Black Crow settlement, march, reload and return");
        }

        private static void Win(BaseState state, RealtimeCampaignBattle battle, Func<bool> complete)
        {
            foreach (var enemy in battle.Model.Units.Where(u => u.Side == PrototypeSide.Enemy)) enemy.Escaped = true;
            battle.Model.SetPaused(false);
            battle.Tick(.02);
            Need(complete(), "战果结算");
            ClearLoot(state);
        }
    }
}

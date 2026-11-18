using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BaseConquest.Base;
using BaseConquest.Battle;
using BaseConquest.RealtimePrototype;
using BaseConquest.Story;
using UnityEditor;
using UnityEngine;

namespace BaseConquest.Editor
{
    public static partial class VerifyDemoPlayerRoute
    {
        private static readonly List<string> Report = new List<string>();

        public static void RunAndExit()=>RunRoute(null);
        public static void RunCustomMapAndExit()
        {
            var arguments=System.Environment.GetCommandLineArgs();
            int index=Array.IndexOf(arguments,"-storyTestPackage");
            if(index<0||index+1>=arguments.Length){Debug.LogError("Missing -storyTestPackage");EditorApplication.Exit(2);return;}
            RunRoute(arguments[index+1]);
        }

        private static void RunRoute(string packagePath)
        {
            const string difficultyKey = "BaseConquest.GlobalDifficulty";
            bool hadDifficulty = PlayerPrefs.HasKey(difficultyKey);
            string prior = PlayerPrefs.GetString(difficultyKey);
            var priorFacing = BaseInteraction.Facing;
            int exit = 0;
            Report.Clear();
            try
            {
                VerifyInstalledStoryText();
                PlayerPrefs.SetString(difficultyKey, "normal");
                var config = DemoBalanceRuntime.Load();
                string raw = File.ReadAllText(Path.Combine(Application.streamingAssetsPath, "BaseScenarios", BaseDemo.BalanceTemplate));
                var parsedRules=DemoBalanceTableReader.ReadVerifiedTables(DemoBalanceImporter.WorkbookPath);
                Need(parsedRules.RuinsFirstControllerTemplate==config.Rules.RuinsFirstControllerTemplate,"古窟首通配表与运行包不一致");
                BaseStoryScenePackage story=null;
                if(packagePath!=null)
                {
                    story=JsonUtility.FromJson<BaseStoryScenePackage>(File.ReadAllText(packagePath));
                    story.ValidateForPlay();
                    Note("自定义地图包："+story.Id);
                }
                OpenRoute(raw, config.Rules, config.WorkbookSha256,"male",false,story);
                var state = OpenRoute(raw, config.Rules, config.WorkbookSha256,saveFixture:story==null,story:story);
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
                File.WriteAllLines(packagePath==null?"Logs/demo-player-route.txt":"Logs/custom-map-player-route.txt", Report);
            }
            EditorApplication.Exit(exit);
        }

        private static void VerifyInstalledStoryText()
        {
            var packaged=StoryTextFiles.Parse(File.ReadAllText(StoryTextFiles.SourcePath));
            var installed=packaged.Copy();
            var obsolete=installed.entries.Single(entry=>entry.key=="story.scene.prologue.pursuit_stab");
            obsolete.text="旧版雾中演出";
            obsolete.revision=0;
            const string unchanged="story.scene.prologue.drill_pause";
            installed.entries.Single(entry=>entry.key==unchanged).text="本机自改的操作提示";
            const string added="story.item.practice_sword.description";
            installed.entries.RemoveAll(entry=>entry.key==added);
            var upgraded=StoryTextFiles.UpgradeInstalled(packaged,installed);
            upgraded.Validate();
            Need(upgraded.entries.Single(entry=>entry.key==obsolete.key).text.Contains("古裔追兵") &&
                upgraded.entries.Single(entry=>entry.key==unchanged).text=="本机自改的操作提示" &&
                upgraded.entries.Single(entry=>entry.key==added).text.Contains("青绳"),
                "旧安装副本覆盖了新版台词，或未保留未改键的玩家文本");
            Note("发行文本：旧安装副本迁移覆盖过时台词，保留未改键的自定义内容，补入新物品说明。");
        }

        private static void PrepareFirstDefense(BaseState state)
        {
            Travel(state, "exit_east", "town");
            var earlyRescue=state.Copy();
            Travel(earlyRescue,"demo_ruins_exit","outpost");
            At(earlyRescue,"demo_ruins_investigate");
            VerifyRuinsSoloVictory(earlyRescue);
            At(state, "town_quest");
            Need(BaseDemo.AcceptLordQuest(state), "未能接取领主委托");
            At(state, "demo_azhu");
            Need(BaseDemoNpcs.InviteAzhu(state) && !BaseDemoNpcs.CanInviteAzhu(state) &&
                !state.Demo.MetCompanions.Contains("partner_craft"), "拒绝阿铸后未保持当前进度");
            Note("灰岩镇：接受领主委托。触发阿铸叫停后离开，没有进入比武或招募。");
            BuyStones(state);

            Travel(state, "demo_ruins_exit", "outpost");
            At(state, "demo_ruins_investigate");
            var beforeSolo=state.Copy();
            var solo = new BaseBattlePreparation(state, BasePrepRoute.Ruins);
            solo.AutoPlace();
            var lost = new RealtimeCampaignBattle(solo.Begin(state));
            if(state.Demo.StoryScene?.Version==2)CheckAuthoredBoard(state,state.Demo.StoryScene,"dungeon",lost.Battle.Board);
            var inBattle=state.Copy();
            BaseDemoRuins.Configure(lost);
            ApproachController(lost, true, 5400, () => BaseDemoRuins.SoloRetreatGuide(lost));
            Need(BaseDemoRuins.SoloRetreatGuide(lost), "初装主角独闯未打到半血撤退："+lost.Battle.Report?.Outcome+"，主角 "+lost.Model.Player.Health+"，敌队长 "+lost.Battle.Commander(BattleSide.B,0).Health);
            Need(BaseDemoRuins.SoloRetreatGuide(lost) && BaseDemoCombat.Fail(state, lost), "独闯半血撤退未结算");
            BaseSceneRules.LeaveDungeon(state);
            Need(BaseSceneRules.Entrance(state) && state.Demo.RuinsDay == 0 && !state.Campaign.HasActiveBattle, "撤退没有回到遗迹入口");
            VerifyRuinsAutosaves(beforeSolo,inBattle,state);
            Note("沉眠古窟：初装主角独自布置，实战降至半血触发撤退，回入口后可重整队伍。");

            Travel(state, "demo_ruins_return", "town");
            Travel(state, "travel_base", "base");
            At(state, "home");
            Need(BaseRpg.UpgradeCamp(state), "无法升级营帐为伙伴腾出名额");
            Rest(state);
            At(state, "demo_ahe");
            Need(BaseDemoRuins.Meet(state, "partner_farm") && BaseDemoRuins.Join(state, "partner_farm"), "新营地无法邀请阿禾");
            Travel(state, "exit_east", "town");
            RecruitAzhu(state);
            ReforgeGift(state);
            Travel(state, "demo_ruins_exit", "outpost");
            At(state, "demo_ruins_investigate");
            var twoCaptains=state.Copy();
            twoCaptains.Adventure.Members.Remove("partner_farm");
            var withAzhu=new BaseBattlePreparation(twoCaptains,BasePrepRoute.Ruins);
            withAzhu.AutoPlace();
            var two=new RealtimeCampaignBattle(withAzhu.Begin(twoCaptains));
            BaseDemoRuins.Configure(two);
            ApproachController(two,true,5400);
            Need(two.Session.Opening.Leaders.Count==2&&BaseDemoRuins.Complete(twoCaptains,two),"主角和阿铸未正常完成古窟首通");
            Note("沉眠古窟旁支：仅主角与阿铸出战，正常击败控制来源。");
            var draft = new BaseBattlePreparation(state, BasePrepRoute.Ruins);
            draft.AutoPlace();
            var rescue = new RealtimeCampaignBattle(draft.Begin(state));
            BaseDemoRuins.Configure(rescue);
            // 12 rendered frames/s at 3× advances a quarter second of battle per frame.
            ApproachController(rescue, true, 360, frameSeconds:.25);
            Need(BaseDemoRuins.Complete(state, rescue), "古窟重试没有击败控制来源：" + rescue.Battle.Report?.Outcome);
            Need(!BaseDemoRuins.Complete(state,rescue),"古窟战果能重复提交");
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
                    int wanted = kind == BaseTroopKind.Cavalry ? 3 : 4;
                    while (state.Workers.Where(worker => worker.Kind == kind).Sum(worker => worker.Count) < wanted &&
                        BaseTown.MaxHire(state, kind) > 0)
                    {
                        Need(BaseTown.Confirm(state, BaseTown.QuoteHire(state, kind, 1)), "逐人招募失败：" + kind);
                        hired++;
                    }
                }
                if (state.Day == 2) CheckEarlyRestAfterBuyingFood(state.Copy());
                if (!intel && state.Demo.Flow.FirstIntelEligible)
                {
                    At(state, "town_peddler");
                    Need(BaseRaidIntel.OfferFirst(state) && BaseRaidIntel.CanClaimFirst(state), "首防免费情报未触发");
                    SaveRouteCheckpoint(state, "first-intel");
                    var raid = state.Campaign.Events.Single(item => item.Id == BaseDemoDefense.EventId);
                    Need(!BaseRaidIntel.OfferFirst(state) && BaseRaidIntel.CanClaimFirst(state.Copy()) &&
                        BaseRaidIntel.ClaimFirst(state) && BaseRaidIntel.Known(state, raid) &&
                        state.Demo.Flow.FreeFirstRaidDay == raid.AvailableDay && !BaseRaidIntel.CanClaimFirst(state),
                        "首防免费情报在离开后丢失、重复发放或日期不一致");
                    intel = true;
                    Note($"第{state.Day}天 老莫的免费消息先离开后补领，日历确认第{state.Demo.Flow.FreeFirstRaidDay}天来袭。");
                }
                At(state, "travel_base");
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

        private static void VerifyRuinsSoloVictory(BaseState grown)
        {
            var hero=grown.Leaders.Single(l=>l.Id==grown.PlayerLeaderId);
            hero.Training.Origin=hero.PersonalEquipment.BaseStats.Clone();
            hero.Training.Target=2;hero.Training.EndDay=grown.Day;
            BaseTraining.AdvanceDay(grown);
            for(int day=0;day<10&&hero.Health<hero.MaxHealth;day++)BaseDevelopment.Recover(grown);
            var draft=new BaseBattlePreparation(grown,BasePrepRoute.Ruins);
            draft.AutoPlace();
            var encounter=draft.Begin(grown);
            const string difficultyKey="BaseConquest.GlobalDifficulty";
            var previous=PlayerPrefs.GetString(difficultyKey,"normal");
            try
            {
                PlayerPrefs.SetString(difficultyKey,"easy");
                var run=new RealtimeCampaignBattle(encounter);
                BaseDemoRuins.Configure(run);
                ApproachController(run,true,5400,()=>BaseDemoRuins.SoloRetreatGuide(run));
                Need(run.Session.Opening.Leaders.Count==1&&BaseDemoRuins.Complete(grown,run),"成长主角合法独胜失败："+run.Battle.Report?.Outcome);
            }
            finally{PlayerPrefs.SetString(difficultyKey,previous);}
            Need(grown.Demo.Stage==BaseDemoStage.RuinsCleared&&grown.Demo.AcceptedDay==0,
                "先救人分支在交付前提前接取了委托");
            if (grown.Demo.StoryScene?.Version != 2) VerifyFullCampInvitation(grown.Copy());
            ResolveLoot(grown);
            Travel(grown,"demo_ruins_return","town");
            At(grown,"town_quest");
            Need(BaseDemoRuins.Deliver(grown)&&grown.Demo.AcceptedDay==grown.Day&&BaseDemoDefense.Delivered(grown)&&
                !BaseDemoRuins.Deliver(grown),"先救人交差未能补记委托，或可重复发放许可");
            Note("沉眠古窟旁支：未领委托，简单难度二级主角独自实战取胜。返镇交差时补记委托，许可仅发一次。");
        }

        private static void VerifyFullCampInvitation(BaseState state)
        {
            Need(BaseDemoStories.AcceptController(state, state.Demo.Flow.FirstControllerId), "满队样本无法接纳迦兰");
            ResolveLoot(state);
            Travel(state, "demo_ruins_return", "town");
            RecruitAzhu(state);
            Travel(state, "travel_base", "base");
            At(state, "demo_ahe");
            var following = state.Adventure.Members.ToArray();
            Need(following.Length == BaseAdventure.Capacity && !BaseRpg.HasRoom(state), "满队样本应同时用完营帐和随行名额");
            SaveRouteCheckpoint(state, "full-camp-before-ahe");
            Need(BaseDemoRuins.Meet(state, "partner_farm") && BaseDemoRuins.Join(state, "partner_farm") &&
                state.Leaders.Any(leader => leader.Id == "partner_farm" && leader.MapRegion == "base") &&
                state.Adventure.Members.SequenceEqual(following), "满队邀请阿禾没有入营，或挤走了现有队友");
            BaseEconomy.Validate(state);
            At(state, "farm");
            Need(BaseEconomy.AssignLeader(state, "partner_farm", "farm") && state.Adventure.Members.SequenceEqual(following),
                "入营的阿禾不能担任粮田负责人，或影响了随行队伍");
            Note("满队邀请旁支：主角、迦兰、阿铸已随行且营帐满额。接受邀请后阿禾留营，随后可直接任职粮田。");
        }

        private static void VerifyRuinsAutosaves(BaseState entrance,BaseState active,BaseState retreated)
        {
            string folder=Path.Combine("Logs","polish-saves",Guid.NewGuid().ToString("N"));
            var catalog=new BaseSaveCatalog(folder,d=>JsonUtility.ToJson(d),JsonUtility.FromJson<BaseSaveDocument>,true);
            var session=new BasePlaySession(catalog,Path.Combine(folder,"memory"),false);
            var previouslySaved=entrance.Copy();previouslySaved.PlayerX-=.1;
            session.Start(previouslySaved);
            session.Store.Save(active,beforeChange:entrance);
            var entry=catalog.Entries().Single(e=>e.Name=="确认开战前");
            var disk=catalog.LoadSlot(entry.Id);
            Need(!BaseSceneCombat.Active(disk)&&disk.Campaign.CurrentMapId==entrance.Campaign.CurrentMapId&&
                disk.PlayerX==entrance.PlayerX&&disk.PlayerY==entrance.PlayerY,"战前自动档未使用确认时的地图和落点");
            session.Store.Save(retreated);
            var landed=catalog.Entries().Single(e=>e.Name.StartsWith("进入",StringComparison.Ordinal));
            disk=catalog.LoadSlot(landed.Id);
            Need(BaseSceneRules.Entrance(disk)&&disk.PlayerX==retreated.PlayerX&&disk.PlayerY==retreated.PlayerY,
                "返回探索自动档未保存实际落点");
            Need(!new BasePlaySession(catalog,Path.Combine(folder,"title"),false).Store.Exists&&catalog.Entries().Count==3,
                "空会话改写或自动恢复了磁盘进度");
            string name=entrance.Demo.Intro.PlayerName;
            Need(catalog.Entries().All(item=>item.PlayerName==name&&Path.GetFileName(catalog.Store(item.Id).Path).StartsWith(name+"-",StringComparison.Ordinal)),
                "自动档文件名或列表未使用本局姓名");
            catalog.SaveSlot("slot1",entrance,"手动档");
            var other=entrance.Copy();
            other.Demo.Intro.PlayerName="远山";
            other.Leaders.Single(leader=>leader.Id==other.PlayerLeaderId).Name="远山";
            catalog.SaveSlot("slot1",other,"覆盖手动档");
            Need(Path.GetFileName(catalog.Store("slot1").Path)=="远山-slot1.json" &&
                catalog.LoadSlot("slot1").Demo.Intro.PlayerName=="远山" &&
                catalog.LoadSlot("slot1",true).Demo.Intro.PlayerName==name,
                "覆盖不同姓名时未保留原手动档备份");
            Note("存档：确认开战前保留实际探索落点，半血撤退后保存入口；空会话保留磁盘档。");
            Note("存档：自动档及手动档使用姓名前缀，跨姓名覆盖保留原槽备份。");
        }

        private static void BuyStones(BaseState state)
        {
            At(state, "town_market");
            int bought = BaseEquipmentCrafting.MaxBuy(state);
            Need(bought >= 10 && BaseEquipmentCrafting.Buy(state, 10, state.Backpack.Revision), "老莫处无法买十颗锻造石");
            Need(!BasePersonalEquipment.CanBuy(state, "basic_sword", state.PlayerLeaderId), "老莫仍能出售装备");
            At(state, "town_blacksmith");
            var heirloom = BasePartyInventory.Find(state, state.Demo.Intro.PracticeId);
            Need(heirloom?.Gear?.Template == "novice_sword" && BaseEquipmentCrafting.Quote(state, heirloom.Id, 1) != null &&
                BasePartyInventory.AvailableMaterial(state, BaseEquipmentCrafting.Stone) >= 10, "搭档旧制短剑未保留，或巴隆无法重铸");
            Note("老莫处实付购入 10 颗锻造石。巴隆可为搭档旧制短剑报价，保留石头用于后续重铸。");
        }

        private static void ReforgeGift(BaseState state)
        {
            At(state, "town_blacksmith");
            var sword = BasePartyInventory.Entries(state).First(entry => entry.Gear?.Template == "novice_sword" && entry.Id != state.Demo.Intro.PracticeId);
            Need(BasePersonalEquipment.Equip(state, state.PlayerLeaderId, sword.Id, "weapon"), "无法装备巴隆赠送的新手短剑");
            string starting = BasePersonalEquipment.Qualities[sword.Quality];
            var qualities = new List<string> { starting };
            var leader = state.Leaders.Single(person => person.Id == state.PlayerLeaderId);
            var before = BasePersonalEquipment.Stats(leader);
            foreach (int stones in new[] { 3, 1, 1, 5 })
            {
                var quote = BaseEquipmentCrafting.Quote(state, sword.Id, stones);
                if (quote == null && BasePartyInventory.Find(state, sword.Id).Quality == 5) break;
                Need(quote != null && BaseEquipmentCrafting.Confirm(state, quote), "巴隆处无法重铸新手短剑");
                qualities.Add(BasePersonalEquipment.Qualities[BasePartyInventory.Find(state, sword.Id).Quality]);
            }
            var after = BasePersonalEquipment.Stats(leader);
            Need(qualities.Count > 1, "没有实际确认重铸");
            Note($"巴隆实际重铸{sword.Name}：{string.Join(" → ", qualities)}。攻击 {before.Damage}→{after.Damage}，暴击伤害 {before.CritMultiplier:P1}→{after.CritMultiplier:P1}。");
        }

        private static void RecruitAzhu(BaseState state)
        {
            At(state, "demo_azhu");
            var duel = new RealtimeCampaignBattle(BaseLocalDuel.Create(state));
            var opponent = duel.Model.Units.Single(unit => unit.Side == PrototypeSide.Enemy);
            duel.OutcomeResolver = run => run.Model.Player.Health <= run.Model.Player.MaxHealth * .5 ? BattleOutcome.SideBWin
                : opponent.Health <= opponent.MaxHealth * .5 ? BattleOutcome.SideAWin : BattleOutcome.None;
            duel.Strategy(0, SquadStrategy.Attack);
            Tick(duel, 5400);
            bool liveWin = duel.Battle.Report?.Outcome == BattleOutcome.SideAWin;
            if (!liveWin)
            {
                duel = new RealtimeCampaignBattle(BaseLocalDuel.Create(state));
                var sample = duel.Model.Units.Single(unit => unit.Side == PrototypeSide.Enemy);
                duel.OutcomeResolver = _ => sample.Health <= sample.MaxHealth * .5 ? BattleOutcome.SideAWin : BattleOutcome.None;
                sample.Health = sample.MaxHealth * .5;
                Tick(duel, 4);
            }
            Need(duel.Battle.Report?.Outcome == BattleOutcome.SideAWin && BaseDemoRuins.Meet(state, "partner_craft"), "阿铸比武没有开放邀请");
            state.Demo.Flow.AzhuDuelWon = true;
            try
            {
                BaseLocalDuel.Create(state);
                throw new InvalidOperationException("首次切磋完成后仍能重复开战");
            }
            catch (InvalidOperationException error) when (error.Message == "阿铸的切磋已经完成") { }
            Need(BaseDemoRuins.Join(state, "partner_craft") && BaseDemoStories.Gift(state), "阿铸加入或铁匠赠礼失败");
            ResolveLoot(state);
            Note("回镇主动找阿铸比武，" + (liveWin ? "实战" : "受控半血样本") + "胜利后邀请同行，领取巴隆赠礼。");
        }

        private static void ApproachController(RealtimeCampaignBattle run, bool attack, int frames, Func<bool> stop = null,double frameSeconds=1d/60)
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
                run.Tick(frameSeconds);
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
            Travel(branch, "travel_base", "base");
            RepairAndAssign(branch);
            Need(BaseDemoFlow.Recruited(branch), "第2天未买齐三种职业，休息提示尚未生效");
            var unstaffed = branch.Copy();
            At(unstaffed, "workshop");
            foreach (var group in unstaffed.Workers.Where(person => person.FacilityId == "workshop").GroupBy(person => person.Kind).ToArray())
                Need(BaseEconomy.MoveWorkers(unstaffed, group.Key, "workshop", "", group.Sum(person => person.Count)), "无法清空工坊岗位样本");
            At(unstaffed, "home");
            int unstaffedDay = unstaffed.Day;
            Need(BaseDemoFlow.RestAdvice(unstaffed).Contains("工坊") && BaseEconomy.Rest(unstaffed, unstaffedDay) &&
                unstaffed.Day == unstaffedDay + 1 && unstaffed.LastReport.ProductsMade == 0, "工坊无人时未正确提示并完成零产出日结");
            branch.Inventory.Food = 0;
            At(branch, "farm");
            while (BaseEconomy.Preview(branch).NextMissing == 0)
            {
                var worker = branch.Workers.FirstOrDefault(person => person.FacilityId == "farm" && BaseDevelopment.Effective(branch, person));
                Need(worker != null && BaseEconomy.MoveWorkers(branch, worker.Kind, "farm", "", 1), "未构造出缺粮样本");
            }
            At(branch, "home");
            int gap = BaseEconomy.Preview(branch).NextMissing;
            int day = branch.Day;
            Need(BaseDemoFlow.RestAdvice(branch).Contains($"缺粮 {gap} 份"), "休息未显示准确缺口");
            var shortage = branch.Copy();
            Need(BaseEconomy.Rest(shortage, day) && shortage.Day == day + 1 && shortage.LastReport.FoodMissing == gap,
                "缺粮时未按真实日结产生对应缺口");
            Travel(branch, "exit_east", "town");
            At(branch, "town_market");
            Need(BaseTown.Confirm(branch, BaseTown.QuoteTrade(branch, "food", true, gap)), "缺粮测试无法买到恰好够用的粮");
            Travel(branch, "travel_base", "base");
            At(branch, "home");
            var preview = BaseEconomy.Preview(branch);
            Need(preview.NextMissing == 0 && preview.NextFood == 0 && BaseDemoFlow.RestAdvice(branch) == "" &&
                BaseEconomy.Rest(branch, day) && branch.Day == day + 1, "买到恰好够用的粮后仍无法日结");
            Note($"第{day}天旁支：工坊缺人时完成零产出日结，短缺 {gap} 份时提示且照常日结，实付补足后结余为 0。");
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
            Need(BaseEconomy.Rest(state, day), "营帐休息失败");
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
            if(state.Demo.StoryScene?.Version==2)return;
            const string folder = "Logs/demo-route-checkpoints";
            BaseEconomy.Validate(state);
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, name + ".json"), JsonUtility.ToJson(state));
        }

        private static void CheckAuthoredBoard(BaseState state,BaseStoryScenePackage story,string role,BattleBoard current=null)
        {
            current??=BaseBattlefield.Board(state);
            var authored=story.Map(role).Board;
            Need(current.Width==authored.Width&&current.Height==authored.Height&&
                current.Cells.Select(c=>(c.Surface,c.Elevation,c.Ramp)).SequenceEqual(authored.Cells.Select(c=>(c.Surface,c.Elevation,c.Ramp))),
                "实际载入的地图未保留编辑地表和高差："+role);
        }
    }
}

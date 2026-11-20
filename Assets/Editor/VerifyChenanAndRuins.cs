using System;
using System.Linq;
using System.Reflection;
using BaseConquest.Base;
using BaseConquest.Battle;
using BaseConquest.Presentation;
using BaseConquest.RealtimePrototype;
using UnityEngine;

namespace BaseConquest.Editor
{
    public static class VerifyChenanAndRuins
    {
        private static void Need(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
        private static void ClearRebuildTestLoot(BaseState s)
        {
            foreach(var p in s.Aftermath.Prisoners.Where(p=>p.Status=="held").ToArray())Need(BaseAftermath.Decide(s,p.Id,"release"),"释放俘虏");
            foreach(var manifest in s.Aftermath.Loot.Where(x=>!x.Closed).ToArray())
            {
                foreach(var item in manifest.Items.Where(i=>i.Status=="pending").ToArray())Need(BaseBattleLoot.Resolve(s,manifest.BattleId,item.Id,s.PlayerLeaderId,true),"领取战果");
                Need(BaseBattleLoot.Finish(s,manifest.BattleId),"结束战果");
            }
        }
        public static void VerifyHomeReconstruction()
        {
            string path=System.IO.Path.Combine(Application.streamingAssetsPath,"BaseScenarios",BaseDemo.BalanceTemplate);
            var s=DemoStageSaveTools.Defense(System.IO.File.ReadAllText(path),"male",DemoBalanceRuntime.Load().Rules);
            var defense=BaseDemoDefense.Begin(s,"none");BattleScenePlacement.Start(s,defense);var first=new RealtimeCampaignBattle(defense);BattleScenePlacement.Snap(s,first);BaseDemoDefense.Configure(first);
            foreach(var enemy in first.Model.Units.Where(u=>u.Side==PrototypeSide.Enemy))enemy.Escaped=true;
            first.Model.SetPaused(false);first.Tick(.02);Need(BaseDemoDefense.Complete(s,first),"首防结算");ClearRebuildTestLoot(s);BaseDemoDefense.AcknowledgeBatu(s);
            s.Campaign.Location="base";At(s,"demo_march_exit");BaseDemoFinale.OpenWorld(s);
            var plan=BaseMuster.PreviewFormation(s,"expedition","combat");Need(BaseMuster.Confirm(s,plan),"出征编队");At(s,"demo_march_exit");BaseWorldJourney.EnterWorld(s,"camp_entry");
            BaseWorldJourney.Start(s,new[]{"camp","bandit"});Need(BaseWorldJourney.Step(s,_=>null),"前往敌营");BaseWorldJourney.EnterScene(s,"bandit_entry");At(s,"demo_camp_attack");
            var attack=BaseDemoFinale.Begin(s,"none");BattleScenePlacement.Start(s,attack);var battle=new RealtimeCampaignBattle(attack);BattleScenePlacement.Snap(s,battle);BaseDemoFinale.Configure(battle);
            foreach(var enemy in battle.Model.Units.Where(u=>u.Side==PrototypeSide.Enemy))enemy.Escaped=true;
            battle.Model.SetPaused(false);battle.Tick(.02);Need(BaseDemoFinale.Complete(s,battle),"攻营结算");ClearRebuildTestLoot(s);
            At(s,"demo_camp_world");BaseWorldJourney.EnterWorld(s,"bandit_entry");
            BaseWorldJourney.Start(s,new[]{"bandit","home"});Need(BaseWorldJourney.Step(s,_=>null),"前往白桦村");
            BaseWorldJourney.EnterScene(s,"home_entry");
            Need(s.Campaign.CurrentMapId==BaseDemoFinale.HomeField,"返乡须先到白桦村外");
            VerifyOptionalSword(s);
            At(s,"village_gate_in");
            Need(BaseCampaign.Travel(s,"custom","village_gate_in"),"不能经西桥头入村");
            RepairReturnedVillage(s);
            Debug.Log("[HomeReconstruction] PASS");
        }

        internal static void VerifyOptionalSword(BaseState bridge)
        {
            var s=bridge.Copy();At(s,"demo_broken_sword");
            var full=s.Copy();
            string filler=full.Backpack.Catalog.First(i=>i.Category=="material"&&i.MaxStack>0&&string.IsNullOrEmpty(i.ExclusiveLeaderId)).Id;
            int room=BaseBackpack.Room(full,filler,full.PlayerLeaderId);
            if(room>0)BaseBackpack.Receive(full,filler,room,full.PlayerLeaderId);
            for(int i=0;i<2;i++)
            {
                bool blocked=false;
                try{BaseDemoFinale.Investigate(full);}catch(BagFullException){blocked=true;}
                Need(blocked&&!full.Demo.Evidence.Contains(BaseDemoFinale.Sword)&&BaseSpatial.Objects(full).Any(o=>o.Id=="demo_broken_sword"),"满包重复拾取应保留地面断剑和剧情状态");
            }
            Need(BaseDemoFinale.Investigate(s)&&BaseBackpack.Count(s,BaseDemoFinale.BrokenSwordItem)==1,"断剑未进入背包");
            Need(!BaseSpatial.Objects(s).Any(o=>o.Id=="demo_broken_sword")&&!BaseDemoFinale.Investigate(s),"重新拾取了地面断剑");
            BaseEconomy.Validate(s);
        }

        internal static void RepairReturnedVillage(BaseState s)
        {
            Need(BaseDemoFinale.Rebuilding(s)&&!BaseDemoFinale.FinishRebuild(s),"不能从村庄开始修理");
            int day=s.Day,materials=s.Inventory.Materials,coins=s.Inventory.Coins;
            bool foundSword=s.Demo.Evidence.Contains(BaseDemoFinale.Sword);
            At(s,"home");
            Need(!BaseDemoFinale.RepairHome(s,"home")&&BaseDemoFinale.BeginHouseTalk(s),"修家时漏掉了喽啰");
            var paused=JsonUtility.FromJson<BaseState>(JsonUtility.ToJson(s));BaseEconomy.Validate(paused);
            Need(paused.Demo.Finale.HouseDialogueStep==1&&BaseDemoFinale.HearHouseConfession(paused),"家门口的问话不能读档续接");
            Need(BaseDemoFinale.HearHouseConfession(s)&&!BaseDemoFinale.HearHouseConfession(s),"喽啰供述状态不合法");
            foreach(string id in new[]{"home","farm","workshop"})
            {
                At(s,id);
                var poor=s.Copy();poor.Inventory.Materials=0;
                Need(!BaseDemoFinale.RepairHome(poor,id)&&!BaseDemoFinale.HomeRepaired(poor,id),"材料不足也能修理 "+id);
                Need(BaseDemoFinale.RepairHome(s,id)&&BaseDemoFinale.HomeRepaired(s,id)&&!BaseDemoFinale.RepairHome(s,id),"修理或防重复扣款失败 "+id);
                BaseEconomy.Validate(s);
            }
            Need(s.Day==day&&s.Inventory.Materials==materials-3*BaseDemoDefense.RepairMaterialCost(s)&&s.Inventory.Coins==coins-3*BaseDemoDefense.RepairCoinCost(s),"三处修理耗日或费用错误");
            Need(!BaseDemoFinale.FinishRebuild(s)&&!BaseDemoFinale.WelcomeVolunteers(s),"村民未到麦田便可结局");
            var residents=BaseSceneInstances.Layout(s,BaseDemoFinale.Home).Objects;
            for(int i=0;i<2;i++)
            {
                var npc=residents.Single(o=>o.Id==(i==0?BaseDemoFinale.Builder:BaseDemoFinale.Helper));
                var from=new BasePoint(npc.X,npc.Y);var to=BaseDemoFinale.VolunteerPost(s,i);
                Need(from.Distance(to)>1&&BaseStageWalk.Route(s,from,to).Count>0,"村口到麦田没有可行路线");
                BaseStageWalk.Advance(s,ref npc.X,ref npc.Y,to,.3);
                var saved=JsonUtility.FromJson<BaseState>(JsonUtility.ToJson(s));
                BaseEconomy.Validate(saved);
                var savedNpc=BaseSceneInstances.Layout(saved,BaseDemoFinale.Home).Objects.Single(o=>o.Id==npc.Id);
                Need(savedNpc.X==npc.X&&savedNpc.Y==npc.Y&&!BaseDemoFinale.HomeVolunteers(saved),"村民走到一半读档丢失位置");
                Need(BaseStageWalk.Advance(saved,ref savedNpc.X,ref savedNpc.Y,to,120),"村民读档后不能走到麦田");
                Need(BaseStageWalk.Advance(s,ref npc.X,ref npc.Y,to,120),"村民不能沿路线走到麦田");
            }
            Need(BaseDemoFinale.WelcomeVolunteers(s),"村民到麦田后未解锁对话");At(s,BaseDemoFinale.Builder);
            Need(BaseDemoFinale.FinishRebuild(s)&&!BaseDemoFinale.FinishRebuild(s)&&s.Demo.Stage==BaseDemoStage.Finished&&s.Demo.Finale.VillageRebuilt,"村民对话不能一次性收尾");
            Need(s.Demo.Evidence.Contains(BaseDemoFinale.Sword)==foundSword,"修理擅自更改断剑的收集状态");
            BaseEconomy.Validate(s);
        }
        public static void VerifyIntelPricingWorkbook()
        {
            var rules=DemoBalanceTableReader.ReadVerifiedTables(System.IO.Path.GetFullPath("../策划/DEMO数值配置表.xlsx"));
            Need(!rules.IntelPricingConfigured||rules.ThreeDayIntelPrice>=0&&rules.FiveDayIntelPrice>=0,"消息价格配置非法");
            Debug.Log("[IntelPricingWorkbook] PASS configured="+rules.IntelPricingConfigured);
        }
        public static void VerifyIntelScheduleFlow()
        {
            string path=System.IO.Path.Combine(Application.streamingAssetsPath,"BaseScenarios",BaseDemo.BalanceTemplate);
            var state=DemoStageSaveTools.Defense(System.IO.File.ReadAllText(path),"male",DemoBalanceRuntime.Load().Rules);
            Need(state.Demo.Defense.ScheduledOnDelivery&&state.Demo.Defense.ArrivalDay==Math.Max(6,state.Demo.DeliveredDay+3),"完整流程的首防日期应由交付日确定");
            state.Demo.Rpg.IntelPricingConfigured=true;state.Demo.Rpg.ThreeDayIntelPrice=1;state.Demo.Rpg.FiveDayIntelPrice=2;
            BaseRaidIntel.Buy(state,3);BaseEconomy.Validate(state);
            var restored=JsonUtility.FromJson<BaseState>(JsonUtility.ToJson(state));BaseEconomy.Validate(restored);
            Need(restored.Demo.Flow.RaidIntel.Count==1,"真实流程消息须能通过完整存档校验");Debug.Log("[IntelScheduleFlow] PASS");
        }
        public static void RunAll()
        {
            Run();VerifyNpcFallback.Run();VerifyDemoResources.Run();VerifyPortraits.Run();VerifyDemoCheckpoint.VerifyCampHandoff();
            VerifyBattleAppearances();VerifyRuinsEntranceCollision();VerifyRuinsFirstClear();VerifyInjuredLines();VerifyTemporaryBattleDialogue();VerifyHeightSight();VerifyExplorationInteraction();VerifyLandMap();VerifyTopBar();VerifyQuestTrack();VerifyInjuryTiers();VerifyStrategyTiers();VerifyBaseFacilities.Run();VerifyDeputy.Run();
            Debug.Log("[VerifyChenanAndRuins] regression PASS");
        }
        // 带伤应答对每位 NPC 按游戏日记录，保留读档当天的播放次数。
        public static void VerifyInjuredLines()
        {
            var s=JsonUtility.FromJson<BaseState>(Resources.Load<TextAsset>("DemoStages/camp-male").text);
            var p=s.Leaders.Single(l=>l.Id==s.PlayerLeaderId);
            Need(p.MaxHealth>0,"阶段档主角最大生命为 0，无法验证带伤阈值");
            p.Health=p.MaxHealth*.59;
            Need(BaseHud.Wounded(s),"生命低于 60% 应判为带伤");
            foreach(var id in new[]{"INJ_TOWN_GUARD","INJ_TOWN_MO","INJ_TOWN_XIAOYUN","INJ_TOWN_BALONG","INJ_TOWN_LEIEN","INJ_CAMP_ZHOU","INJ_CAMP_CHUN","INJ_CAMP_AHE","INJ_CAMP_AZHU","INJ_CAMP_JIALAN"})
                Need(BaseHud.InjuredText(s,id)!="",id+" 缺带伤应答文案");
            Need(BaseHud.InjuredText(s,"INJ_TOWN_MO")!=BaseHud.InjuredText(s,"INJ_TOWN_GUARD"),"不同 NPC 的带伤应答不应重复");
            Need(BaseHud.RecordInjuryDialogue(s,"INJ_TOWN_MO"),"当天第一次交谈应触发");
            Need(!BaseHud.RecordInjuryDialogue(s.Copy(),"INJ_TOWN_MO"),"状态复制后同一 NPC 当天不得重复触发");
            var restored=JsonUtility.FromJson<BaseState>(JsonUtility.ToJson(s));
            BaseEconomy.Validate(restored);
            Need(!BaseHud.RecordInjuryDialogue(restored,"INJ_TOWN_MO"),"读档后同一天同一 NPC 不应再次触发");
            Need(BaseHud.RecordInjuryDialogue(restored,"INJ_TOWN_BALONG"),"另一名 NPC 的当天触发独立记录");
            restored.Day++;
            Need(BaseHud.RecordInjuryDialogue(restored,"INJ_TOWN_MO")&&restored.InjuryDialogueIds.Count==1,"下一日同一 NPC 可重新触发");
            p.Health=p.MaxHealth*.6;
            Need(!BaseHud.Wounded(s)&&!BaseHud.RecordInjuryDialogue(s,"INJ_TOWN_BALONG"),"生命正好 60% 不应算带伤");
            p.Health=p.MaxHealth;
            Need(!BaseHud.Wounded(s)&&BaseHud.InjuredText(s,"INJ_CAMP_ZHOU")=="","满血不应出带伤应答");
            p.Health=p.MaxHealth*.4;p.Fate="dead";
            Need(!BaseHud.Wounded(s),"阵亡角色不参与带伤应答");
            p.Fate="active";
            Debug.Log("[Injured] PASS：阈值、每人每日、存档往返、过日重置");
        }
        public static void VerifyTemporaryBattleDialogue()
        {
            const BindingFlags Instance = BindingFlags.Instance | BindingFlags.NonPublic;
            var dispatch=typeof(BattleSceneController).GetMethod("ContinueFinishedSceneDialogue",Instance);
            var simulation=typeof(BattleSceneController).GetField("battle",Instance);
            var retryAt=typeof(BattleSceneController).GetField("campaignRetryAt",Instance);
            var finish=typeof(BattleSimulation).GetMethod("FinishRealtime",Instance);
            Check(BaseDemoIntro.BattleId,BattleOutcome.SideAWin,true);
            Check(BaseDemoIntro.BattleId,BattleOutcome.Retreat,true);
            Check(BaseDemoPrologue.BattleId,BattleOutcome.SideAWin,true);
            Check(BaseDemoPrologue.BattleId,BattleOutcome.SideBWin,true);
            Check(BaseDemoRuins.EventId,BattleOutcome.SideAWin,false);
            Debug.Log("[TemporaryBattleDialogue] PASS：演练与追兵成功／失败回调各一次，正式古窟不自动结算");

            void Check(string id,BattleOutcome outcome,bool expected)
            {
                var host=new GameObject("TemporaryBattleDialogueCheck");
                try
                {
                    var controller=host.AddComponent<BattleSceneController>();
                    var opening = new BaseCampaignBattle { Id=id, EventId=id, Scenario=new BattleScenario() };
                    opening.Leaders.Add(new BaseLeader { Name="演练者", AppearanceId="female" });
                    var session = new BaseCampaignSession(opening);
                    int calls=0;
                    controller.ConfigureSceneEncounter(session,null,null,(_,abandon,mainMenu)=>
                    {
                        Need(!abandon&&!mainMenu&&session.Battle.Report.Outcome==outcome,"回调须保留战斗结果与离场方式");
                        calls++;
                        return true;
                    },"");
                    simulation.SetValue(controller,session.Battle);
                    bool Dispatch()=>(bool)dispatch.Invoke(controller,null);
                    Need(!Dispatch()&&calls==0,"部署阶段不得自动切入对白");
                    finish.Invoke(session.Battle,new object[]{outcome});
                    controller.ExternalDialogue=true;
                    Need(!Dispatch()&&calls==0,"进行中的对白结束前不得切入结果");
                    controller.ExternalDialogue=false;
                    Need(Dispatch()==expected&&calls==(expected?1:0),"结束时必须按战斗类型派发");
                    retryAt.SetValue(controller,-1f);
                    Need(!Dispatch()&&calls==(expected?1:0),"重复帧不得再提交同一战斗回调");
                }
                finally { UnityEngine.Object.DestroyImmediate(host); }
            }
        }
        // P24（2026-11-04 定）：高地换算以现行 BattleHeightSight 为准，02／05 的数字必须跟它一致。
        // ArcherBonus 返回的是细格；粗格加成 = 返回值 ÷ Subdivision(2)。
        public static void VerifyHeightSight()
        {
            var board=new BattleBoard{Width=8,Height=8};board.EnsureValid();
            board.Cells[3*8+3].Elevation=2;
            Need(BattleHeightSight.Radius(board,new GridPosition(3,3))==14,"高地视野应为 10+2×2=14");
            Need(Math.Abs(BattleHeightSight.ArcherBonus(board,3.5,3.5,3.5,3.5))<1e-9,"平地不应有弓兵射程加成");
            Need(Math.Abs(BattleHeightSight.ArcherBonus(board,3.5,3.5,5.5,5.5)-4)<1e-9,"高差 2 级应 +4 细格（合 +2 粗格）");
            board.Cells[5*8+5].Elevation=3;
            Need(Math.Abs(BattleHeightSight.ArcherBonus(board,3.5,3.5,5.5,5.5))<1e-9,"从低处打高处不应有加成");
            Need(Math.Abs(BattleHeightSight.ArcherBonus(board,5.5,5.5,1.5,1.5)-6)<1e-9,"高差 3 级应封顶 +6 细格（合 +3 粗格）");
            Debug.Log("[Height] 视野 14（高度2）、平射 0、高差2 +4细格、低打高 0、高差3 封顶 +6细格");
        }

        public static void VerifyExplorationInteraction()
        {
            var state = new BaseState { Campaign = new BaseCampaignState() };
            state.Leaders.Add(new BaseLeader { Id = "partner_craft" });
            var companion = new BaseMapObject("partner_craft", "阿铸", 6.5, 6.5, .45, .45);
            var north = new BasePoint(0, 1);
            var south = new BasePoint(0, -1);
            Need(!BaseInteraction.CanReach(state, companion, 6.5, 7.25, north), "身后的伙伴不应成为附近目标");
            Need(!BaseInteraction.CanReach(state, companion, 6.5, 6.65, north), "贴身背后的伙伴不应绕过朝向");
            Need(BaseInteraction.CanReach(state, companion, 6.5, 7.25, south), "原地转向伙伴后应可交互");
            Need(!BaseInteraction.CanReach(state, companion, 6.5, 8.2, south), "伙伴应保持原有交互半径");
            foreach (int side in new[] {-1, 1})
            {
                double x = companion.X + side;
                Need(BaseInteraction.CanReach(state, companion, x, companion.Y, north), "正左右边界应可与伙伴交谈");
                Need(!BaseInteraction.CanReach(state, companion, x, companion.Y+.01, north), "半圆边界稍后的伙伴不应出现交互");
                Need(BaseInteraction.CanReach(state, companion, x, companion.Y-.01, north), "半圆边界稍前的伙伴应可交互");
            }

            var board = BattleBoard.CreateDefault(12, 12);
            var lord = new BaseMapObject("town_quest", "领主", 6.5, 6.5, .5, .5, false, kind: "npc");
            board.Cell(new GridPosition(6, 3)).Elevation = 1;
            var stairs = board.Cell(new GridPosition(6, 4));
            stairs.Elevation = 2;
            stairs.Ramp = RampAxis.NorthSouth;
            for (int y = 5; y <= 9; y++) board.Cell(new GridPosition(6, y)).Elevation = 2;
            Need(BaseInteraction.CanReach(board, lord, 6.5, 8.5, south), "高台上应可在扩大的范围内与领主交谈");
            Need(BaseInteraction.CanReach(board, lord, 6.5, 4.75, north), "面向领主走到相连楼梯顶部时应可交谈");
            Need(!BaseInteraction.CanReach(board, lord, 6.5, 4.25, north), "楼梯下半段与领主仍隔层");
            Need(!BaseInteraction.CanReach(board, lord, 7.4, 6.5, new BasePoint(-1, 0)), "高台外即使很近也不能隔层交谈");
            Need(!BaseInteraction.CanReach(board, lord, 6.5, 6.8, north), "普通人物贴身在背后仍不可交谈");
            Need(BaseInteraction.CanReach(board, lord, 6.8, 6.5, north), "人物在正侧方可交谈");
            Debug.Log("[ExplorationInteraction] 正面半圆含左右边界、贴身背后人物、领主高台距离与楼梯判定 PASS");
        }
        // P38（2026-11-04 最新裁决，取代伤员三档的 50%／20%）：
        // 士兵生命 <30% 不能工作也不能主动出战；≥30% 两者都可以。遇袭守家例外：还能行动就能迎战。
        // 队长不受阈值限制；自动选兵按生命比例从高到低；30%~100% 生命预警线默认 50%，拖到 30% 即关闭。
        public static void VerifyInjuryTiers()
        {
            var s=JsonUtility.FromJson<BaseState>(Resources.Load<TextAsset>("DemoStages/camp-male").text);
            BaseDevelopment.Normalize(s);
            s.Workers.Clear();
            s.Workers.Add(new BaseWorkerBucket{Kind=BaseTroopKind.Melee,Count=1,MaxHealth=100,Health=100,FarmEfficiency=1,WorkshopEfficiency=1});
            var w=s.Workers[0];
            w.Health=31;Need(BaseDevelopment.Effective(s,w)&&BaseDevelopment.CanFight(w),"生命 31% 应可工可战");
            w.Health=30;Need(BaseDevelopment.Effective(s,w)&&BaseDevelopment.CanFight(w),"生命正好 30% 应可工可战（阈值不带等号）");
            w.Health=29;Need(!BaseDevelopment.Effective(s,w)&&!BaseDevelopment.CanFight(w),"生命 29% 应强制空闲");
            Need(BaseDevelopment.CanDefend(w),"遇袭守家例外：29% 仍应能迎战");
            w.Health=1;Need(!BaseDevelopment.Effective(s,w)&&!BaseDevelopment.CanFight(w)&&BaseDevelopment.CanDefend(w),"1 血士兵应只能守家");
            BaseDevelopment.Normalize(s);
            Need(w.Resting,"重伤士兵应带闲置（Resting）标记");
            w.Health=0;Need(!BaseDevelopment.CanDefend(w),"生命归零不参战");
            // 当天返岗：UnavailableDay 生产禁令已摘除，返营／招降当天即可排班。
            w.Health=100;w.Resting=false;w.UnavailableDay=s.Day;
            Need(BaseDevelopment.Effective(s,w),"当天返营／招降的士兵应可立即排班（生产禁令已摘除）");
            w.UnavailableDay=0;
            // 队长豁免：不受任何血量阈值限制（夹具：临时清掉随行/外出状态再断言）。
            var l=s.Leaders.First(x=>BaseAdventure.Alive(x)&&x.Health>0&&!BaseTraining.Busy(x.Training));
            bool wasMember=s.Adventure.Members.Remove(l.Id),wasAway=l.Away;l.Away=false;
            Need(BaseDevelopment.CanManage(s,l),"夹具队长应满足管理条件");
            Need(BaseDevelopment.CanFight(l)&&BaseDevelopment.Effective(s,l),"满血队长应可战可工");
            l.Health=l.MaxHealth*.1;
            Need(BaseDevelopment.CanFight(l)&&BaseDevelopment.Effective(s,l),"队长应不受血量阈值限制");
            l.Health=l.MaxHealth;l.Away=wasAway;if(wasMember)s.Adventure.Members.Add(l.Id);
            // 恢复：营内全员每日自动进行；跨过 30% 就解除闲置、恢复出战资格。
            w.Health=10;BaseDevelopment.Normalize(s);Need(w.Resting,"10% 士兵应处于重伤闲置");
            BaseDevelopment.Recover(s);
            double expected=Math.Min(w.MaxHealth,10+w.MaxHealth*BaseDevelopment.RecoveryRatio(s));
            Need(Math.Abs(w.Health-expected)<1e-6,"全员自动恢复量不符：10 → "+w.Health.ToString("0.##")+"（应 "+expected.ToString("0.##")+"）");
            Need(!w.Resting,"恢复越过 30% 应解除重伤闲置标记");
            Need(BaseDevelopment.Effective(s,w)&&BaseDevelopment.CanFight(w),"恢复越过 30% 应可工可战");
            w.Health=25;BaseDevelopment.Recover(s);
            Need(BaseDevelopment.CanFight(w),"25% 未休养士兵过夜恢复后应可重新出战");
            // 生命预警：默认 50%，范围 30%~100%，拖到 30% 即关闭。
            Need(Math.Abs(BaseDevelopment.WarningThreshold(s)-.5)<1e-9,"默认预警线应为 50%");
            w.Health=45;Need(BaseDevelopment.BelowWarning(s,w),"45% 应低于默认预警线");
            w.Health=60;Need(!BaseDevelopment.BelowWarning(s,w),"60% 不应触发预警");
            BaseDevelopment.SetWarningThreshold(s,.3);
            Need(BaseDevelopment.WarningOff(s),"预警线拖到 30% 应视为关闭");
            w.Health=45;Need(!BaseDevelopment.BelowWarning(s,w),"预警关闭后不再提示");
            BaseDevelopment.SetWarningThreshold(s,.2);
            Need(Math.Abs(BaseDevelopment.WarningThreshold(s)-.3)<1e-9,"预警线下限锁在 30%");
            BaseDevelopment.SetWarningThreshold(s,.5);
            // 自动选兵：生命比例高的先上；守家迎战放宽到还能行动的伤兵。
            s.Workers.Clear();
            s.Workers.Add(new BaseWorkerBucket{Kind=BaseTroopKind.Melee,Count=1,MaxHealth=100,Health=80,FarmEfficiency=1,WorkshopEfficiency=1});
            s.Workers.Add(new BaseWorkerBucket{Kind=BaseTroopKind.Melee,Count=1,MaxHealth=100,Health=45,FarmEfficiency=1,WorkshopEfficiency=1});
            s.Workers.Add(new BaseWorkerBucket{Kind=BaseTroopKind.Melee,Count=1,MaxHealth=100,Health=10,FarmEfficiency=1,WorkshopEfficiency=1});
            var preset=s.Muster.Presets.Single(p=>p.Purpose=="expedition").Copy();
            preset.SelectLeaders(new[]{s.PlayerLeaderId});
            preset.SetCount(BaseTroopKind.Melee,2);preset.SetCount(BaseTroopKind.Ranged,0);preset.SetCount(BaseTroopKind.Cavalry,0);
            var plan=BaseMuster.Preview(s,"expedition",true,preset);
            Need(plan.Takes.Count==2&&plan.Takes[0].BucketIndex==0&&plan.Takes[1].BucketIndex==1,
                "出征选兵应按生命比例从高到低取（80%→45%），实际 "+string.Join(",",plan.Takes.Select(t=>t.BucketIndex+":"+t.Count)));
            var large=s.Copy();large.Workers.Clear();large.Workers.Add(new BaseWorkerBucket{Kind=BaseTroopKind.Melee,Count=30,MaxHealth=100,Health=100,FarmEfficiency=1,WorkshopEfficiency=1});
            var largePreset=preset.Copy();largePreset.Melee=30;var limited=BaseMuster.Preview(large,"expedition",true,largePreset);
            Need(limited.Total==24&&limited.MissingMelee==6,"编队预览须限制全军24人并报告缺编");
            Need(BaseMuster.Confirm(large,limited),"24人编队应能确认");
            var extra=BaseMuster.PreviewAppend(large,limited.Id,1,0,0,true);
            Need(extra.Total==0&&!BaseMuster.ConfirmAppend(large,extra),"满编队伍不能通过追加超过24人");
            preset.SetCount(BaseTroopKind.Melee,3);preset.Purpose="defense";
            var defend=BaseMuster.Preview(s,"defense",true,preset);
            Need(defend.Takes.Sum(t=>t.Count)==3,"守家迎战应放宽到还能行动的伤兵（含 10%），实际 "+defend.Takes.Sum(t=>t.Count));
            Need(plan.Takes.Sum(t=>t.Count)==2,"出征不应把 10% 伤兵编进队");
            // 生产岗位和四分类：重伤不占岗位，手动调入健康士兵。
            s.Workers.Clear();
            s.Workers.Add(new BaseWorkerBucket{Kind=BaseTroopKind.Melee,Count=2,MaxHealth=100,Health=100,FarmEfficiency=1,WorkshopEfficiency=1});
            s.Workers.Add(new BaseWorkerBucket{Kind=BaseTroopKind.Melee,Count=1,MaxHealth=100,Health=25,FarmEfficiency=1,WorkshopEfficiency=1});
            BaseDevelopment.SetPolicy(s,"manual");
            Need(BaseEconomy.MoveWorkers(s,BaseTroopKind.Melee,"","farm",2),"健康士兵能调入粮田");
            Need(s.Workers.Where(x=>!x.Away&&x.Health<30).All(x=>x.FacilityId==""),"重伤士兵不应占生产岗位");
            Need(s.Workers.Where(x=>!x.Away&&x.Health>=30&&x.FacilityId=="farm").Sum(x=>x.Count)==2,"粮田应保留两名手动调入的健康士兵");
            var tally=BaseDevelopment.Tally(s);
            Need(tally[0]==3&&tally[1]==2&&tally[2]==2&&tally[3]==1,
                "兵力四分类不符：总数"+tally[0]+"／可工作"+tally[1]+"／可出战"+tally[2]+"／重伤"+tally[3]);
            // 手动休养入口已关闭（恢复全员自动，Resting 由血量派生）。
            Need(!BaseDevelopment.RestWorkers(s,0,1,true),"手动休养入口应一律拒绝");
            Debug.Log("[InjuryTiers] 30% 不带等号、守家例外、队长豁免、当天返岗、全员自动恢复、预警开关、生命降序选兵、补位与四分类 PASS");
        }
        // P32（2026-11-04 裁决）：领地地图只列同一领地场景与出入口；点击传送落到出口旁、不耗日程。
        public static void VerifyLandMap()
        {
            var s=JsonUtility.FromJson<BaseState>(Resources.Load<TextAsset>("DemoStages/camp-male").text);
            BaseDevelopment.Normalize(s);
            // camp 测试档停在「醒来」阶段，先推到遗迹任务已接——城镇、野外都开放，返乡还没开。
            s.Demo.Stage=BaseDemoStage.RuinsAssigned;
            var nodes=BaseLandMap.Nodes(s);
            Need(nodes.Count>=2,"领地地图应至少列出两个场景，实际 "+nodes.Count);
            Need(nodes.Count(node=>node.Current)==1,"应恰有一个当前场景标记");
            var camp=nodes.FirstOrDefault(node=>node.Key=="base");
            Need(camp!=null&&camp.Current,"营地节点应存在并标记为当前");
            var east=camp.Exits.FirstOrDefault(exit=>exit.ObjectId=="exit_east");
            Need(east!=null&&east.TargetKey=="town","营地东侧出口应通向城镇");
            Need(!east.Inside,"地表出口应算蓝点（非内嵌）");
            var town=nodes.FirstOrDefault(node=>node.Key=="town");
            Need(town!=null&&town.Exits.Any(exit=>exit.ObjectId=="travel_base"&&exit.TargetKey=="base"),"城镇西侧出口应通回基地");
            Need(BaseLandMap.Problem(s,"town")=="","地图传送不应要求先站在出口边");
            int day=s.Day;
            Need(BaseLandMap.Teleport(s,"town","travel_base"),"应能传送到城镇西侧出口");
            Need(s.Campaign.Location=="town","传送后应身处城镇");
            Need(s.Day==day,"传送不消耗日程");
            Need(Math.Abs(s.PlayerX-2)<1.7&&Math.Abs(s.PlayerY-6.5)<1.7,"应落在 travel_base 附近，实际 "+s.PlayerX.ToString("0.##")+","+s.PlayerY.ToString("0.##"));
            Need(BaseLandMap.Problem(s,"base")=="","城镇应能传回基地");
            Need(BaseLandMap.Teleport(s,"base","exit_east"),"应能传回营地");
            Need(s.Campaign.Location=="base"&&Math.Abs(s.PlayerX-23.3)<2.2&&Math.Abs(s.PlayerY-6.5)<2.2,"传回营地应落在东侧出口旁，实际 "+s.PlayerX.ToString("0.##")+","+s.PlayerY.ToString("0.##"));
            Need(BaseLandMap.Problem(s,"world")!="","未开返乡时世界地图应不可传送");
            Need(!BaseLandMap.Teleport(s,"world"),"未开放场景应拒绝传送");
            s.Campaign.HasActiveBattle=true;
            Need(BaseLandMap.Problem(s,"base")!="","战斗进行中应拒绝传送");
            Debug.Log("[LandMap] 场景与出口映射、免站出口传送、不耗日程、未开放与战斗中拒绝 PASS");
        }
        // P33（2026-11-04 裁决）：15 天季度日历、顶栏四项明细、体力按天部分恢复与补充入口。
        public static void VerifyTopBar()
        {
            var s=JsonUtility.FromJson<BaseState>(Resources.Load<TextAsset>("DemoStages/camp-male").text);
            BaseDevelopment.Normalize(s);
            var schedule=s.Copy();schedule.Demo.Stage=BaseDemoStage.RuinsDelivered;schedule.Demo.DeliveredDay=schedule.Day;
            schedule.Campaign.Events.RemoveAll(e=>e.Id==BaseDemoDefense.EventId);
            BaseDemoDefense.ScheduleOnDelivery(schedule);
            var firstRaid=schedule.Campaign.Events.Single(e=>e.Id==BaseDemoDefense.EventId);
            Need(firstRaid.AvailableDay==Math.Max(6,schedule.Day+3),"首防须在交付日确定且保留三天准备");
            Need(!BaseRaidIntel.CanClaimFirst(schedule),"首次提示前不应出现免费领取入口");
            schedule.Demo.Flow.FirstIntelEligible=true;Need(BaseRaidIntel.OfferFirst(schedule)&&!BaseRaidIntel.OfferFirst(schedule),"免费消息引导只能提供一次");
            var dismissed=JsonUtility.FromJson<BaseState>(JsonUtility.ToJson(schedule));
            Need(BaseRaidIntel.CanClaimFirst(dismissed)&&!BaseCalendar.Quarter(dismissed).Single(day=>day.Day==firstRaid.AvailableDay).Raids.Contains(firstRaid.Title),
                "离开首防提示后存读档，须保留领取入口且继续隐藏日期");
            var resolved=dismissed.Copy();resolved.Campaign.Events.Single(e=>e.Id==BaseDemoDefense.EventId).Status="resolved";
            Need(!BaseRaidIntel.CanClaimFirst(resolved)&&!BaseRaidIntel.ClaimFirst(resolved),"首防已处理时不应再开放领取");
            int freeIntelCoins=dismissed.Inventory.Coins;
            Need(BaseRaidIntel.ClaimFirst(dismissed)&&!BaseRaidIntel.CanClaimFirst(dismissed)&&!BaseRaidIntel.ClaimFirst(dismissed)&&
                dismissed.Demo.Flow.FreeFirstRaidDay==firstRaid.AvailableDay&&dismissed.Inventory.Coins==freeIntelCoins&&
                BaseCalendar.Quarter(dismissed).Single(day=>day.Day==firstRaid.AvailableDay).Raids.Contains(firstRaid.Title),
                "免费领取须记录实际首防日、同步日历、不扣钱且只能领一次");
            schedule=dismissed;
            schedule.Day=3;BaseDemoDefense.ProductionFinished(schedule,new BaseDayReport{CompletedDay=2,Product=schedule.WorkshopProduct});
            Need(schedule.Campaign.Events.Count(e=>e.Id==BaseDemoDefense.EventId)==1&&schedule.Demo.Defense.ArrivalDay==6,"后续日结不得重复排期或改变首防日期");
            var late=s.Copy();late.Day=7;late.Demo.Stage=BaseDemoStage.RuinsDelivered;late.Demo.DeliveredDay=7;late.Campaign.Events.RemoveAll(e=>e.Id==BaseDemoDefense.EventId);BaseDemoDefense.ScheduleOnDelivery(late);
            Need(late.Demo.Defense.ArrivalDay==10,"晚交付须从交付当天起保留三天");
            var limit=s.Copy();limit.Day=999998;limit.Demo.Stage=BaseDemoStage.RuinsCleared;int foodBefore=limit.Inventory.Food,coinBefore=limit.Inventory.Coins;
            Need(!BaseDemoRuins.Deliver(limit)&&limit.Demo.Stage==BaseDemoStage.RuinsCleared&&limit.Inventory.Food==foodBefore&&limit.Inventory.Coins==coinBefore,"日期上限不能留下半完成交付或奖励");
            var intel=s.Copy();intel.Campaign.Events.RemoveAll(e=>e.Kind=="defense");intel.Demo.Flow.RaidIntel=null;intel.Demo.Rpg.IntelPricingConfigured=true;intel.Demo.Rpg.ThreeDayIntelPrice=2;intel.Demo.Rpg.FiveDayIntelPrice=3;
            var unpriced=intel.Copy();unpriced.Demo.Rpg.IntelPricingConfigured=false;int initialCoins=unpriced.Inventory.Coins;bool unpricedRejected=false;try{BaseRaidIntel.Buy(unpriced,3);}catch(InvalidOperationException){unpricedRejected=true;}
            Need(unpricedRejected&&unpriced.Inventory.Coins==initialCoins&&unpriced.Demo.Flow.RaidIntel==null,"未配置价格时不得扣款或修改消息状态");
            var fields=new System.Collections.Generic.Dictionary<string,string>{{"A62","消息类型"},{"B62","价格"},{"A63","3日"},{"B63","2"},{"#type:B63","n"},{"A64","5日"},{"B64","3"},{"#type:B64","n"}};
            var priceRules=unpriced.Demo.Rpg.Copy();DemoBalanceTableReader.ApplyIntelPrices(priceRules,fields);Need(priceRules.IntelPricingConfigured&&priceRules.ThreeDayIntelPrice==2&&priceRules.FiveDayIntelPrice==3,"配表应独立导入两种价格");
            fields.Remove("B64");bool partialRejected=false;try{DemoBalanceTableReader.ApplyIntelPrices(unpriced.Demo.Rpg,fields);}catch(InvalidOperationException){partialRejected=true;}Need(partialRejected&&!unpriced.Demo.Rpg.IntelPricingConfigured,"配表价格不完整时不得启用");
            Need(BaseRaidIntel.FillMissingPrices(unpriced,priceRules)&&unpriced.Demo.Rpg.ThreeDayIntelPrice==2,"旧档缺价格时应补入新配置");priceRules.ThreeDayIntelPrice=9;Need(!BaseRaidIntel.FillMissingPrices(unpriced,priceRules)&&unpriced.Demo.Rpg.ThreeDayIntelPrice==2,"已配置的旧档价格不能被后续配表覆盖");
            int coins=intel.Inventory.Coins;var raid=new BaseCampaignEvent{Id="intel-test-a",Title="袭击A",Kind="defense",Status="pending",AvailableDay=intel.Day+2};intel.Campaign.Events.Add(raid);
            Need(!BaseCalendar.Quarter(intel).Single(d=>d.Day==raid.AvailableDay).Raids.Any(),"未购买消息不能显示未来袭击");
            var cheap=BaseRaidIntel.Buy(intel,3);Need(cheap.HasRaid&&intel.Inventory.Coins==coins-2,"三日消息应扣款并冻结范围");
            Need(BaseCalendar.Quarter(intel).Single(d=>d.Day==raid.AvailableDay).Forecast,"三日消息应标记模糊范围");
            raid.Status="resolved";Need(BaseCalendar.Quarter(intel).Single(d=>d.Day==raid.AvailableDay).Forecast,"取消袭击不能提前撤除模糊范围");raid.Status="pending";
            intel.Campaign.Events.Add(new BaseCampaignEvent{Id="intel-test-b",Title="袭击B",Kind="defense",Status="pending",AvailableDay=intel.Day+4});
            Need(ReferenceEquals(cheap,BaseRaidIntel.Buy(intel,3))&&intel.Inventory.Coins==coins-2&&cheap.Entries.Count==1,"同日复习不得刷新或扣款");
            var precise=BaseRaidIntel.Buy(intel,5);Need(precise.Entries.Count==2&&intel.Inventory.Coins==coins-5,"五日消息应独立定价并取得新快照");
            Need(BaseCalendar.Quarter(intel).Single(d=>d.Day==raid.AvailableDay).Raids.Contains("袭击A")&&!BaseCalendar.Quarter(intel).Single(d=>d.Day==raid.AvailableDay).Forecast,"准确消息覆盖范围后应显示日期");
            raid.Status="resolved";Need(!BaseCalendar.Quarter(intel).Single(d=>d.Day==raid.AvailableDay).Raids.Contains("袭击A"),"被取消的未来袭击不得继续显示确切日期");
            var intelCopy=intel.Copy();Need(intelCopy.Demo.Flow.RaidIntel.Count==2&&!ReferenceEquals(intelCopy.Demo.Flow.RaidIntel[0],cheap),"消息快照复制应独立");
            var reloaded=JsonUtility.FromJson<BaseState>(JsonUtility.ToJson(intel));Need(reloaded.Demo.Flow.RaidIntel.Count==2&&reloaded.Demo.Flow.RaidIntel[1].Entries.Count==2,"消息快照须支持存档往返");
            // 日历：15 天一个季度，起点随天数滚动，今天只标一个。
            Need(BaseCalendar.QuarterStart(1)==1&&BaseCalendar.QuarterStart(15)==1&&BaseCalendar.QuarterStart(16)==16,"季度起点应为 1／16／31…");
            Need(BaseCalendar.QuarterEnd(1)==15&&BaseCalendar.QuarterEnd(16)==30,"季度终点应为 15／30…");
            var quarter=BaseCalendar.Quarter(s);
            Need(quarter.Count==BaseCalendar.QuarterDays,"日历应固定 15 格，实际 "+quarter.Count);
            Need(quarter.Count(day=>day.Today)==1&&quarter.First(day=>day.Today).Day==s.Day,"今天应只标一格且与天数一致");
            var boundary = intel.Copy(); boundary.Day = 15;
            boundary.Campaign.Events.Clear(); boundary.Demo.Flow.RaidIntel.Clear();
            boundary.Campaign.Events.Add(new BaseCampaignEvent { Id = "quarter-raid", Title = "跨季度袭击", Kind = "defense", Status = "pending", AvailableDay = 16 });
            var nextQuarter = BaseCalendar.Quarter(boundary, 2);
            Need(nextQuarter.Count == 15 && nextQuarter.First().Day == 16 && nextQuarter.Last().Day == 30 && nextQuarter.All(day => !day.Today), "下季度应显示第 16 到 30 天，不应重复今日");
            Need(nextQuarter.All(day => day.Raids.Count == 0), "翻页不能泄露未知袭击日期");
            BaseRaidIntel.Buy(boundary, 5);
            Need(BaseCalendar.Quarter(boundary, 2).First().Raids.Contains("跨季度袭击"), "准确情报应标在对应季度");
            Need(BaseCalendar.Quarter(boundary, 0).Count == 0 && BaseCalendar.Quarter(boundary, 3).Count == 0, "翻页应限制在首季度与下季度之间");
            boundary.Day = 16;
            Need(BaseCalendar.Quarter(boundary, 1).All(day => day.Past) && BaseCalendar.Quarter(boundary).First().Today, "进入下一季度后应仍能浏览过去的季度");
            // 顶栏四项明细：粮食／原料／军需／兵力都不能是空壳。
            Need(BaseTopBar.Food(s).Length==3&&BaseTopBar.Food(s)[0].Contains("预计产粮"),"粮食明细应包含岗位与预计产量");
            Need(BaseTopBar.Materials(s)[0].Contains("工坊在岗"),"原料明细应说明工坊在岗");
            Need(BaseTopBar.Supply(s).Length==3&&BaseTopBar.Supply(s)[0].Contains("一级"),"军需明细应分档");
            var troops=BaseTopBar.Troops(s);
            Need(troops.Length==4&&troops[0].Contains("可主动出战")&&troops[0].Contains("重伤"),"兵力明细应含四分类");
            // 体力：按天部分恢复，跨天累加、封顶，补充入口拒绝溢出。
            // 用副本推进天数，避免破坏「醒来日期与阶段一致」的存档校验。
            var t=s.Copy();t.Demo.RuinsVersion=1;t.Demo.RuinsRewards=null;BaseRuinsRewards.Enable(t);
            var r=t.Demo.RuinsRewards;
            r.Energy=20;r.EnergyDay=t.Day;t.Day+=1;BaseRuinsRewards.AdvanceDay(t);
            Need(r.Energy==20+BaseRuinsRewards.DailyRecovery&&r.EnergyDay==t.Day,"跨一天应恢复 "+BaseRuinsRewards.DailyRecovery+" 点，实际 "+r.Energy);
            t.Day+=3;BaseRuinsRewards.AdvanceDay(t);
            Need(r.Energy==r.MaximumEnergy,"跨多天应补到上限而不是溢出，实际 "+r.Energy);
            Need(!BaseRuinsRewards.AddEnergy(t,1),"满体力时补充入口应拒绝");
            r.Energy=100;Need(BaseRuinsRewards.AddEnergy(t,20)&&r.Energy==120,"补充 20 点应到上限");
            Need(!BaseRuinsRewards.AddEnergy(t,1),"超出上限的补充应拒绝");
            Debug.Log("[TopBar] 15 天季度、四项明细、体力按天部分恢复与补充边界 PASS");
        }
        // P34（2026-11-04 裁决）：任务分主线／支线，选择进常驻栏，再选取消，可折叠。
        public static void VerifyQuestTrack()
        {
            var s=JsonUtility.FromJson<BaseState>(Resources.Load<TextAsset>("DemoStages/camp-male").text);
            BaseDevelopment.Normalize(s);BaseQuests.Normalize(s);
            Need(BaseQuestLog.Entries(s).All(entry=>entry.Main),"醒来时不应显示未解锁支线");
            s.Demo.Stage=BaseDemoStage.VisitLord;s.Demo.RuinsVersion=1;
            var entries=BaseQuestLog.Entries(s);
            Need(entries.Any(entry=>entry.Main)&&entries.Any(entry=>!entry.Main),"任务应区分主线与支线");
            Need(entries.All(entry=>!string.IsNullOrEmpty(entry.Id)&&!string.IsNullOrEmpty(entry.Title)&&!string.IsNullOrEmpty(entry.Goal)),"任务条目应有 id／标题／目标");
            Need(entries.Select(entry=>entry.Id).Distinct().Count()==entries.Count,"任务 id 不能重复");
            var main=entries.First(entry=>entry.Main);var target=entries.First(entry=>!entry.Main&&entry.Id=="demo:partner_craft");
            Need(BaseQuestLog.Cue(s).Id==main.Id&&!BaseQuestLog.ToggleTrack(s,main.Id)&&!BaseQuestLog.ToggleTrack(s,"demo:ruins_trial"),"默认追踪主线；不可手动追踪主线或未解锁任务");
            Need(BaseQuestLog.ToggleTrack(s,target.Id)&&BaseQuestLog.Tracked(s)?.Id==target.Id,"可用支线应可追踪");
            Need(BaseQuestLog.Cue(s).Id==target.Id&&BaseQuestLog.Cue(s).Instruction==target.Goal&&BaseQuestLog.Cue(s).ObjectId=="exit_east","任务册、提示和异地图出口应使用同一目标");
            Need(BaseQuestLog.ToggleTrack(s,target.Id)&&BaseQuestLog.TrackedId(s)==""&&BaseQuestLog.Cue(s).Id==main.Id,"取消支线后应恢复主线");
            BaseQuestLog.ToggleTrack(s,target.Id);
            s.Leaders.Add(new BaseLeader{Id="partner_craft"});
            Need(BaseQuestLog.Tracked(s)==null&&BaseQuestLog.Cue(s).Id==main.Id&&!BaseQuestLog.ToggleTrack(s,target.Id),"支线完成后应恢复主线，且不再可追踪");
            s.Quests.TrackedId=null;BaseQuests.Normalize(s);
            Need(s.Quests.TrackedId=="","旧档缺失的追踪字段应补空串");
            Debug.Log("[QuestTrack] 主线默认、支线解锁与选择、出口箭头、取消与完成回主线、旧档补字段 PASS");
        }
        // P36（2026-11-06 裁决）：三档＝手动／进攻／驻守；逃跑退出 UI 但保留枚举位，旧档按驻守读。
        // 权限守卫落在 BattleSimulation 命令入口：远程只放策略与查看，移动／集火／技能／改范围一律拒绝。
        public static void VerifyStrategyTiers()
        {
            Need(BattleSimulation.StrategyName(SquadStrategy.Obey)=="手动","听令应显示为手动");
            Need(BattleSimulation.StrategyName(SquadStrategy.Attack)=="进攻","Attack 应显示为进攻");
            Need(BattleSimulation.StrategyName(SquadStrategy.Protect)=="驻守","Protect 应显示为驻守");
            Need(BaseSceneRules.Tier(SquadStrategy.Flee)==SquadStrategy.Protect,"旧档的逃跑应迁移为驻守");
            Need(BaseSceneRules.Tier(SquadStrategy.Obey)==SquadStrategy.Obey&&BaseSceneRules.Tier(SquadStrategy.Attack)==SquadStrategy.Attack,"其余档位不应被迁移改写");

            var local=BattleControlPolicy.Local;
            Need(local.Allows("view")&&local.Allows("strategy")&&local.Allows("order")&&local.Allows("skill")&&local.Allows("range"),"本地策略应五项全开");
            var remote=BattleControlPolicy.RemoteDefense;
            Need(remote.Allows("view")&&remote.Allows("strategy"),"远程应保留查看与改策略");
            Need(!remote.Allows("order")&&!remote.Allows("skill")&&!remote.Allows("range"),"远程应禁止移动／集火／技能／改范围");

            // 证红：同一入口在远程权限下必须拒绝，切回本地必须放行。
            var scenario=new BattleScenario{CommanderControl=true};
            scenario.A.HeroCount=1;scenario.A.Commanders=new System.Collections.Generic.List<CommanderLoadout>{new CommanderLoadout()};
            var sim=new BattleSimulation(scenario);
            sim.Control=BattleControlPolicy.Local;
            Need(sim.SetCommanderRanges(BattleSide.A,0,3,6,9),"本地应允许调整范围");
            Need(sim.SetCommanderStrategy(BattleSide.A,0,SquadStrategy.Protect),"本地应允许切换策略");
            sim.Control=BattleControlPolicy.RemoteDefense;
            Need(!sim.SetCommanderRanges(BattleSide.A,0,3,6,10),"远程改范围必须在入口被拒");
            Need(sim.SetCommanderStrategy(BattleSide.A,0,SquadStrategy.Attack),"远程仍应允许切换策略");
            var state=JsonUtility.FromJson<BaseState>(Resources.Load<TextAsset>("DemoStages/camp-male").text);
            state.Muster.Strategies=null;state.Muster.Ranges=null;BaseEconomy.Validate(state);
            Need(state.Muster.Strategies.Count==3&&state.Muster.Ranges.SequenceEqual(new[]{3,6,9,3,6,9,3,6,9}),"旧档应初始化三个位次和默认半径");
            state.Muster.Strategies[0]=(int)SquadStrategy.Protect;state.Muster.Strategies[1]=(int)SquadStrategy.Obey;
            state.Muster.Ranges=new System.Collections.Generic.List<int>{2,5,8};BaseEconomy.Validate(state);
            Need(state.Muster.Ranges.SequenceEqual(new[]{2,5,8,2,5,8,2,5,8}),"旧档共用半径须迁移到三个位次");
            BaseMuster.SetRanges(state,1,3,7,10);
            Need(state.Muster.Ranges.SequenceEqual(new[]{2,5,8,3,7,10,2,5,8}),"只应修改选中的位次");
            bool rejected=false;try{BaseMuster.SetRanges(state,1,7,3,10);}catch(ArgumentException){rejected=true;}
            Need(rejected&&state.Muster.Ranges[3]==3,"非法范围不得修改已保存配置");
            var copy=state.Copy();copy.Muster.Strategies[0]=(int)SquadStrategy.Attack;copy.Muster.Ranges[0]=1;
            Need(state.Muster.Strategies[0]==(int)SquadStrategy.Protect&&state.Muster.Ranges[0]==2,"复制存档不能共享配置列表");
            var configured=new BattleScenario{CommanderControl=true};configured.A.HeroCount=1;configured.A.Commanders.Add(new CommanderLoadout());
            BaseSceneRules.Configure(state,configured,new[]{state.Leaders[0]});
            Need(configured.A.Commanders[0].Strategy==SquadStrategy.Protect&&configured.A.Commanders[0].GuardRange==2&&configured.A.Commanders[0].AlertRange==5&&configured.A.Commanders[0].AttackRange==8,"战前应读取共用位次与半径");
            Need(state.Muster.Strategies[1]==(int)SquadStrategy.Obey,"空位配置应保留");
            var loaded=JsonUtility.FromJson<BaseState>(JsonUtility.ToJson(state));BaseEconomy.Validate(loaded);
            Need(loaded.Muster.Strategies.SequenceEqual(state.Muster.Strategies)&&loaded.Muster.Ranges.SequenceEqual(state.Muster.Ranges),"策略和半径应随存档往返");
            var casualties=new BattleSimulation(new BattleScenario{CampaignRules=true,InstantDeathEnabled=true,LeaderInstantDeath=1});
            var ally=new BattleSoldier{Id=100,Side=BattleSide.A,Kind=SoldierKind.Hero,Health=0};var enemy=new BattleSoldier{Id=101,Side=BattleSide.B,Kind=SoldierKind.Hero,Health=0};
            casualties.Soldiers.Add(ally);casualties.Soldiers.Add(enemy);casualties.ResolveCasualties();
            Need(!ally.InstantDead&&enemy.InstantDead,"己方队长即死须关闭且不影响敌方队长");
            Debug.Log("[StrategyTiers] 三档文案、逃跑迁移、远程权限守卫（证红＋放行）PASS");
        }
        private static void At(BaseState state,string id)
        {
            var obj=BaseSpatial.Objects(state).Single(o=>o.Id==id);var p=obj.Blocks?BaseStageWalk.Door(state,id):new BasePoint(obj.X,obj.Y);
            state.PlayerX=p.X;state.PlayerY=p.Y;BaseInteraction.Walk(new BasePoint(obj.X-p.X,obj.Y-p.Y));BaseAdventure.Arrive(state);
        }
        // P5/P6（2026-11-02）：序章队长外观、七个场景的队长外观白名单、wilderness 旧档回退、
        // 村民攻击图与 NPC 行为入口。一律按「行为定义动作、贴图只是表现层」口径断言。
        private static readonly string[] CommanderAppearanceWhitelist={"azhu","player","female","galan","chief","batu","huoya"};
        public static void VerifyBattleAppearances()
        {
            var male=Resources.Load<TextAsset>("DemoStages/camp-male").text;
            var female=Resources.Load<TextAsset>("DemoStages/camp-female").text;
            // ① 序章队长：兜底态（Intro 缺失）与正常路径都用异性主角搭档，不再借斥候 chenan。
            foreach(string json in new[]{male,female})
            {
                string expected=BaseDemoIntro.CaptainArt(JsonUtility.FromJson<BaseState>(json));
                Need(expected=="female"||expected=="player","序章搭档外观不是异性主角："+expected);
                var fallback=JsonUtility.FromJson<BaseState>(json);
                fallback.Demo.Intro=null;fallback.Demo.Stage=BaseDemoStage.PrologueBriefing;
                // 阶段档营地布局不含 prologue_report（序章村庄运行时才生成），测试里补一个。
                BaseSpatial.Layout(fallback,"base").Objects.Add(new BaseMapPlacement{Id="prologue_report",Name="村民与队长 · 来袭消息",X=9,Y=8.3,Width=1,Height=1});
                var captain=BaseDemoNpcs.Additional(fallback).SingleOrDefault(o=>o.Id=="demo_captain");
                Need(captain!=null,"序章队长兜底对象未生成");
                // 兜底态 Intro 为空，CaptainArt 固定回退 female；按该状态自身的取值断言（W10 原文口径）。
                Need(captain.AssetId==BaseDemoIntro.CaptainArt(fallback)&&captain.AssetId!="chenan","序章队长兜底仍借陈安或外观不符："+captain.AssetId);
                Need(captain.Name==BaseDemoIntro.PartnerName(fallback)+" · 交谈","序章队长兜底名字未跟随搭档名");
                var normal=JsonUtility.FromJson<BaseState>(json);
                normal.Demo.Stage=BaseDemoStage.PrologueBriefing;normal.Demo.Intro.Step=1;
                var partner=BaseDemoIntro.Additional(normal).SingleOrDefault(o=>o.Id=="demo_captain");
                Need(partner!=null&&partner.AssetId==expected&&partner.AssetId!="chenan","序章队长正常路径外观不符");
            }
            // ② 五场战斗＋比试＋演练：场景 id 齐备、外观目标全是真实角色图，不含随机敌营别名与 saber 兜底。
            var scenes=new[]{BaseDemoPrologue.BattleId,BaseDemoRuins.EventId,BaseDemoDefense.EventId,BaseDemoDefense.SecondEventId,BaseDemoFinale.Camp,BaseLocalDuel.Id,BaseDemoIntro.BattleId};
            Need(scenes.Distinct().Count()==scenes.Length&&scenes.All(id=>!string.IsNullOrEmpty(id)),"Demo 场景 id 重复或为空");
            Need(CommanderAppearanceWhitelist.All(PixelDemoArt.IsCast),"战斗队长外观白名单含非角色图");
            Need(scenes.All(id=>!id.Contains("wilderness")&&!id.Contains("enemy-captain")),"场景 id 仍含随机敌营别名");
            // ③ wilderness 只在旧档回退中出现；新场景队长外观空列表不生成兜底形象。
            var scenario=new BattleScenario{CampaignRules=true};scenario.B.CommanderAppearances.Clear();
            Need(new BattleSimulation(scenario).CommanderAppearance(BattleSide.B,0)=="","空队长外观列表生成了兜底形象");
            Need(PixelDemoArt.AtlasId("wilderness")=="enemy-captain"&&!PixelDemoArt.IsCast("wilderness"),"wilderness 旧档回退口径变化");
            // ④ 行为／表现解耦：村民贴图带攻击图是「能力」，NPC 身份不变且不进战斗编组。
            Need(PixelDemoArt.HasAttack("villager")&&PixelDemoArt.HasWalk("villager"),"村民攻击图或补岗行走图缺失");
            Need(BaseDemoNpcs.IsMotionNpc("villager")&&BaseDemoNpcs.MotionClass("villager").StartsWith("交互 NPC"),"村民 NPC 分类丢失");
            Need(BaseDemoNpcs.MotionClass("villager").Contains("攻击"),"村民攻击能力被类别门控关掉了");
            var camp=JsonUtility.FromJson<BaseState>(male);
            Need(BaseDemoNpcs.MotionChecks(camp,true).All(o=>o.Kind=="npc"),"检查台对象混入了战斗编组");
            Debug.Log("[VerifyBattleAppearances] 序章搭档、7 场景外观白名单、wilderness 旧档回退、村民攻击图与 NPC 行为入口 PASS");
        }
        public static void VerifyRuinsEntranceCollision()
        {
            var state = JsonUtility.FromJson<BaseState>(Resources.Load<TextAsset>("DemoStages/camp-male").text);
            At(state,"home");
            Need(BaseDemo.AcknowledgeWaking(state),"无法推进苏醒");
            At(state,"demo_ahe");
            Need(BaseDemoRuins.Meet(state,"partner_farm") && BaseDemoRuins.Join(state,"partner_farm"),"无法加入随行测试伙伴");
            At(state,"exit_east");
            Need(BaseCampaign.Travel(state,"town"),"无法进入城镇");
            At(state,"demo_ruins_exit");
            Need(BaseCampaign.Travel(state,"outpost") && BaseSceneRules.Entrance(state),"无法进入正式遗迹入口地图");
            var portal = BaseSpatial.Objects(state).Single(o => o.Id == "demo_ruins_investigate");
            var savedFacing = BaseInteraction.Facing;
            try
            {
                var center = WalkNorth(0);
                Need(center.Y > portal.Y + .10 && center.Y <= portal.Y + .241,"中线应停在门槛前");
                var pillar = WalkNorth(.8);
                Need(pillar.Y > portal.Y - .15 && pillar.Y <= portal.Y - .029,"石柱应在门槛前挡住角色");
                Need(WalkNorth(1.55).Y > portal.Y + 1.4,"入口侧面应可绕行");
                double backX = portal.X - .65, backY = portal.Y + 1.55;
                BaseSpatial.Move(state,ref backX,ref backY,0,-2);
                Need(backY >= portal.Y + 1.329 && backY < portal.Y + 1.46,"从入口背面也不能穿墙");

                var legacy = state.Copy();
                legacy.PlayerX = portal.X;
                legacy.PlayerY = portal.Y + .65;
                var companion = legacy.Leaders.Single(l => l.Id == "partner_farm");
                companion.MapX = portal.X - .4;
                companion.MapY = portal.Y + .65;
                legacy = JsonUtility.FromJson<BaseState>(JsonUtility.ToJson(legacy));
                BaseEconomy.Validate(legacy);
                companion = legacy.Leaders.Single(l => l.Id == "partner_farm");
                Need(BaseSpatial.Walkable(legacy,legacy.PlayerX,legacy.PlayerY) && BaseSpatial.Walkable(legacy,companion.MapX,companion.MapY),"旧档角色或伙伴仍陷在门洞内");

                state.PlayerX = center.X;
                state.PlayerY = center.Y;
                BaseInteraction.Walk(new BasePoint(0,1));
                Need(BaseInteraction.CanReach(state,portal),"从南侧抵达门槛后应能交互");
                BaseSceneRules.EnterDungeon(state);
                Need(BaseSceneRules.Dungeon(state),"入口交互应切入地下回廊");
                var missingPortal = state.Copy();
                missingPortal.Campaign.Layouts.Single(map => map.Objects.Any(o => o.AssetId == "dungeon-entrance"))
                    .Objects.RemoveAll(o => o.AssetId == "dungeon-entrance");
                BaseSceneRules.LeaveDungeon(missingPortal);
                Need(BaseSceneRules.Entrance(missingPortal) && BaseSpatial.Walkable(missingPortal,missingPortal.PlayerX,missingPortal.PlayerY),
                    "缺少门洞标记的旧地图仍应允许离开回廊");
                BaseSceneRules.LeaveDungeon(state);
                Need(BaseSceneRules.Entrance(state) && BaseSpatial.Walkable(state,state.PlayerX,state.PlayerY) &&
                    state.PlayerY < portal.Y + .24,"离开回廊应落在门槛前的可行走地面");
            }
            finally { BaseInteraction.Facing = savedFacing; }
            Debug.Log("[RuinsEntranceCollision] PASS 门槛、石柱、侧绕、旧档坐标、进出回廊");

            BasePoint WalkNorth(double offset)
            {
                double x = portal.X + offset, y = portal.Y - .55;
                BaseSpatial.Move(state,ref x,ref y,0,2);
                return new BasePoint(x,y);
            }
        }

        internal static void WalkOutOfRuins(RealtimeCampaignBattle run, bool useCompanion = false)
        {
            Need(run.RuinsExitOpen && run.Battle.Phase == BattlePhase.Fighting, "救人后须留在战场等待撤离");
            run.ContinueRuinsExit();
            var captain = run.Model.Units.Where(u => u.Alive && u.Side == BaseConquest.RealtimePrototype.PrototypeSide.Ally &&
                u.Role == BaseConquest.RealtimePrototype.PrototypeRole.Leader).OrderByDescending(u => u.Player != useCompanion).First();
            var exit = run.Battle.ExitCell(BattleSide.A);
            var target = new BaseConquest.RealtimePrototype.PrototypePoint(exit.X+.5,exit.Z+.5);
            var health=run.Model.Units.Select(u=>u.Health).ToArray();
            run.Model.SetPaused(false);
            Need(run.OrderRuinsExit(target,new[]{captain.Id-1}), "点击实际撤离点未下令");
            for (int i=0;i<480 && run.Battle.Phase==BattlePhase.Fighting;i++)run.Tick(.25);
            Need(run.Battle.Report?.Outcome==BattleOutcome.SideAWin && (int)captain.Position.X==exit.X &&
                (int)captain.Position.Z==exit.Z,"队长未能走出遗迹 "+captain.Position.X+","+captain.Position.Z+" → "+exit);
            Need(run.Model.Units.Select(u=>u.Health).SequenceEqual(health),"撤离期间仍在结算伤害");
        }

        // 遗迹首通的现场解控、撤离和入口处置。
        public static void VerifyRuinsFirstClear()
        {
            var state=JsonUtility.FromJson<BaseState>(Resources.Load<TextAsset>("DemoStages/camp-male").text);
            state.Demo.Rpg.GenerationSeed=44;At(state,"home");Need(BaseDemo.AcknowledgeWaking(state),"无法推进苏醒");
            At(state,"exit_east");Need(BaseCampaign.Travel(state,"town"),"无法进入城镇");
            At(state,"demo_ruins_exit");Need(BaseCampaign.Travel(state,"outpost"),"无法进入遗迹入口");At(state,"demo_ruins_investigate");
            Need(state.Demo.RuinsDay==0,"首通夹具 RuinsDay 非 0");
            var session=BaseDemoRuins.Begin(state,"none");
            Need(session.Opening.RuinsFirstClear,"RuinsDay==0 未标记首通");
            var hero=session.Battle.Soldiers.Single(u=>u.Side==BattleSide.B&&u.Kind==SoldierKind.Hero);
            Need(hero.AppearanceId=="galan","首通遗迹控制者外观错误："+hero.AppearanceId);
            var run=new RealtimeCampaignBattle(session);BaseDemoRuins.Configure(run);
            var exit=run.Battle.ExitCell(BattleSide.A);
            Need(run.Model.PlaceNearest(run.Model.Player,new BaseConquest.RealtimePrototype.PrototypePoint(exit.X+.25,exit.Z+.25))&&
                (int)run.Model.Player.Position.X==exit.X&&(int)run.Model.Player.Position.Z==exit.Z,"未能布置守在出口的队长");
            var enemy=run.Model.Units.Single(u=>u.Id==hero.Id+1);enemy.Health=1;enemy.KnockedDown=true;hero.Health=1;hero.KnockedDown=true;hero.DeathResolved=true;
            run.Model.SetPaused(false);run.Tick(.02);
            Need(run.AwaitingRescueDialogue && !BaseDemoRuins.Complete(state,run),"首胜在对白和离场前提交");
            run.ContinueRuinsExit();run.Model.SetPaused(false);run.Tick(.1);
            Need(run.Battle.Phase==BattlePhase.Fighting,"对白结束时在出口待命的队长触发了自动离场");
            WalkOutOfRuins(run);
            Need(BaseDemoRuins.Complete(state,run),"遗迹首通结算失败");
            var prisoner=state.Aftermath.Prisoners.Single(p=>p.BattleId==session.Opening.Id&&p.Kind==SoldierKind.Hero);
            Need(!string.IsNullOrEmpty(state.Demo.Flow.FirstControllerId)&&state.Demo.Flow.FirstControllerId==prisoner.Id,"首通未写入 FirstControllerId");
            Need(state.Aftermath.Loot.All(m=>m.BattleId!=session.Opening.Id)&&BaseRuinsRewards.PendingBattle(state)=="","首胜意外生成奖励");
            var later=JsonUtility.FromJson<BaseState>(JsonUtility.ToJson(state));BaseEconomy.Validate(later);
            Need(BaseDemoNpcs.Additional(later).Any(o=>o.Id==BaseDemoStories.GalanMarker)&&prisoner.Status==BaseDemoStories.WaitingAtEntrance,"入口读档丢失迦兰");
            var legacy=later.Copy();legacy.Aftermath.Prisoners.Single(p=>p.Id==prisoner.Id).Status="held";
            legacy.Aftermath.Loot.Add(new BaseLootManifest{BattleId=prisoner.BattleId,Title="遗迹旧战果",ReceiptCoins=15});
            int coins=legacy.Inventory.Coins;BaseEconomy.Validate(legacy);
            Need(BaseDemoStories.ControllerDialogue(legacy,prisoner.Id)&&!BaseBattleLoot.ShouldPresent(legacy)&&legacy.Inventory.Coins==coins,
                "旧档仍弹首战奖励或未保留迦兰");
            var board=BaseBattlefield.Board(later);var objects=BaseSpatial.Objects(later);
            var from=new BasePoint(later.PlayerX,later.PlayerY);
            bool CanWalk(double x,double y)=>BaseSpatial.Geometry(objects,x,y,board.Width,board.Height)&&BaseSpatial.TerrainWalkable(board,x,y,objects);
            foreach(var id in new[]{"demo_rescued_1","demo_rescued_2",BaseDemoStories.GalanMarker})
            {
                var person=objects.Single(o=>o.Id==id);var at=new BasePoint(person.X,person.Y);
                Need(CanWalk(at.X,at.Y)&&BaseMap.FindRoute(from,at,null,CanWalk,false,board.Width,board.Height).Count>0&&
                    BaseInteraction.WithinReach(board,person,at.X,at.Y),"出洞后不能走到人物旁交谈："+id);
            }
            Need(BaseSceneRules.TalkRescued(later,true),"撤离后无法继续洞外对白");
            BaseSceneRules.TickRescued(later,60);
            Need(BaseDemoNpcs.Additional(later).Any(o=>o.Id==BaseDemoStories.GalanMarker),"镇民离去时带走了迦兰");
            Need(!BaseAftermath.Decide(later,prisoner.Id,"release")&&!BaseAftermath.Decide(later,prisoner.Id,"execute"),"普通处置能移走剧情人物");
            Need(BaseDemoStories.ExecuteController(later,prisoner.Id)&&BaseDemoNpcs.Additional(later).All(o=>o.Id!=BaseDemoStories.GalanMarker)&&!BaseDemoStories.AcceptController(later,prisoner.Id),"处决后仍可接纳");
            var executed = JsonUtility.FromJson<BaseState>(JsonUtility.ToJson(later));
            BaseEconomy.Validate(executed);
            Need(executed.Aftermath.Prisoners.Single(p => p.Id == prisoner.Id).Status == "executed" &&
                !BaseDemoStories.ExecuteController(executed, prisoner.Id) &&
                BaseDemoNpcs.Additional(executed).All(person => person.Id != BaseDemoStories.GalanMarker),
                "处斩后读取存档复活了迦兰，或可重复处置");
            VerifyFullPartyGalan(state.Copy(),prisoner.Id);
            var full = state.Copy();
            full.Demo.Rpg.Capacity[full.Demo.Rpg.CampLevel - 1] = BaseRpg.Population(full);
            Need(!BaseDemoStories.AcceptController(full, prisoner.Id) && !full.Demo.Flow.ControllerAccepted &&
                BaseDemoNpcs.Additional(full).Any(person => person.Id == BaseDemoStories.GalanMarker),
                "营帐已满时接纳消耗了入口人物或写入了接纳标记");
            Need(BaseDemoStories.ControllerDialogue(state,prisoner.Id),"首通控制者专属剧情未生效");
            Need(!BaseAftermath.Decide(state,prisoner.Id,"recruit"),"首通控制者在接纳前不应直接招降");
            Need(BaseDemoStories.AcceptController(state,prisoner.Id),"首通控制者接纳失败");
            Need(state.Demo.Flow.ControllerAccepted&&state.Demo.Flow.ControllerPledged,"首通接纳标记未落地");
            Need(!BaseDemoStories.AcceptController(state,prisoner.Id) && !BaseDemoStories.ExecuteController(state,prisoner.Id) &&
                BaseDemoNpcs.Additional(state).All(person => person.Id != BaseDemoStories.GalanMarker),"接纳后仍有第二个可处置的迦兰");
            var recruited=state.Leaders.Single(l=>l.Id=="partner_recruit:"+prisoner.Id);
            Need(BasePartnerDialogue.Prefix(state,recruited)=="story.partner_galan","首通迦兰未使用专属对白");
            Debug.Log("[VerifyRuinsFirstClear] 首通标记、控制者外观、接纳前置、ControllerAccepted/Pledged 与专属对白 PASS");
        }

        private static void VerifyFullPartyGalan(BaseState state,string captive)
        {
            void Go(BaseState current,string exit,string destination)
            {
                At(current,exit);
                Need(BaseCampaign.Travel(current,destination),"招募验收无法通行："+exit);
            }
            Go(state,"demo_ruins_return","town");
            At(state,"demo_azhu");
            Need(BaseDemoRuins.Meet(state,"partner_craft") && BaseDemoRuins.Join(state,"partner_craft"),"准备满队时无法邀请阿铸");
            Go(state,"travel_base","base"); At(state,"demo_ahe");
            Need(BaseDemoRuins.Join(state,"partner_farm") && BaseAdventure.Full(state),"准备满队时无法邀请阿禾");
            Go(state,"exit_east","town"); Go(state,"demo_ruins_exit","outpost");
            var marker = BaseDemoNpcs.Additional(state).Single(o=>o.Id==BaseDemoStories.GalanMarker);
            Need(BaseDemoStories.AcceptController(state,captive),"满队无法接纳迦兰");
            state = JsonUtility.FromJson<BaseState>(JsonUtility.ToJson(state)); BaseEconomy.Validate(state);
            string id = "partner_recruit:"+captive;
            var galan = state.Leaders.Single(l=>l.Id==id);
            var movement = new BaseCompanionMovement(); movement.Synchronize(state,true);
            var atEntrance = movement.Agents.Single(a=>a.Id==id);
            Need(atEntrance.Visible && atEntrance.Position.Distance(new BasePoint(marker.X,marker.Y)) < .01 &&
                BaseAdventure.WaitingHere(state,galan) && !BaseAdventure.InviteWaiting(state,galan) &&
                BaseDemoNpcs.Additional(state).All(o=>o.Id!=BaseDemoStories.GalanMarker),"满队接纳后迦兰没留在原坐标或有重影");

            var returning = state.Copy();
            Need(BaseAdventure.SendToCamp(returning,id) && !returning.Leaders.Single(l=>l.Id==id).WaitingAtRecruitment,
                "选择回营没有送走迦兰");
            var leaving = state.Copy();
            Go(leaving,"demo_ruins_return","town"); Go(leaving,"travel_base","base");
            var atCamp = leaving.Leaders.Single(l=>l.Id==id);
            var barracks = BaseSpatial.Layout(leaving,"base").Objects.Single(o=>o.Id=="barracks");
            var chat = new BasePartnerDialogue();
            Need(!atCamp.WaitingAtRecruitment && atCamp.MapRegion=="base" && atCamp.HomeX>barracks.X+barracks.Width*.5 &&
                atCamp.HomeY>barracks.Y && BaseSpatial.Walkable(leaving,atCamp.HomeX,atCamp.HomeY,"base") &&
                BasePartnerDialogue.Resolve(atCamp,chat.Chat(leaving,atCamp))=="村民们都怕我，我躲远点。",
                "离场自动回营、兵营东侧台阶驻点或专属闲聊不符");
            atCamp.CampPostSet=false; atCamp.HomeX=atCamp.MapX=BasePersonnel.BarracksDoor(leaving).X;
            var oldSaveMovement = new BaseCompanionMovement(); oldSaveMovement.Synchronize(leaving,true);
            Need(oldSaveMovement.Agents.Single(a=>a.Id==id).Visible && atCamp.HomeX>barracks.X+barracks.Width*.5,
                "旧存档中的迦兰未迁至新驻点");

            Need(BaseAdventure.SendToCamp(state,"partner_craft") && BaseAdventure.InviteWaiting(state,galan) &&
                BaseAdventure.Contains(state,id) && !galan.WaitingAtRecruitment && !BaseAdventure.Contains(state,"partner_craft") &&
                !BaseAdventure.InviteWaiting(state,galan),"送队友回营后未能当场换人，或重复入队");
            movement.Synchronize(state);
            Need(movement.Agents.Single(a=>a.Id==id).Following && !movement.Agents.Single(a=>a.Id=="partner_craft").Visible,
                "换人后原队友仍在洞口，或迦兰没有开始跟随");
            BaseEconomy.Validate(state);

            Need(BaseAdventure.SendToCamp(leaving,"partner_farm"),"准备普通队长招募时无法腾出随行位");
            var other = leaving.Aftermath.Prisoners.Single(p=>p.Id==captive).Copy();
            other.Id="test:camp-officer"; other.Status="held"; other.Name="俘虏队长";
            leaving.Aftermath.Prisoners.Add(other);
            Need(BaseAftermath.Decide(leaving,other.Id,"recruit"),"普通队长招降失败");
            var officer = leaving.Leaders.Single(l=>l.Id=="partner_recruit:"+other.Id);
            var tent = BaseSpatial.WorkPost(leaving,"home");
            oldSaveMovement.Synchronize(leaving);
            var agent = oldSaveMovement.Agents.Single(a=>a.Id==officer.Id);
            Need(!agent.Visible && agent.Position.Distance(tent)<.01 && !BaseAdventure.Contains(leaving,officer.Id),"普通队长没有留在营帐内");
            At(leaving,"farm"); Need(BaseEconomy.AssignLeader(leaving,officer.Id,"farm"),"普通队长不能任职");
            oldSaveMovement.Synchronize(leaving);
            Need(agent.Visible && agent.Position.Distance(tent)<.01 && agent.Destination.Distance(BaseSpatial.WorkPost(leaving,"farm"))<.01,
                "任职后没有从营帐朝岗位出发");
            Go(leaving,"exit_east","town"); Go(leaving,"travel_base","base");
            oldSaveMovement.Synchronize(leaving,true);
            for (int frame=0; frame<600 && !agent.Arrived; frame++) oldSaveMovement.Update(leaving,.05,false);
            Need(agent.Visible && agent.Arrived && agent.Position.Distance(BaseSpatial.WorkPost(leaving,"farm"))<.05,
                "受任队长实际寻路没有抵达粮田岗位");
            BaseEconomy.Validate(leaving);
        }
        public static void Run()
        {
            var state=JsonUtility.FromJson<BaseState>(Resources.Load<TextAsset>("DemoStages/camp-male").text);
            var scout = BaseSpatial.Objects(state, "base").Single(obj => obj.Id == BaseDemoNpcs.CampScout);
            Need(scout.Name=="陈安 · 交谈"&&scout.AssetId=="chenan"&&!PixelDemoArt.IsCast("chenan"),"斥候名称、外观或分类错误");
            var gate = BaseSpatial.Objects(state, "base").Single(obj => obj.Id == "demo_march_exit");
            Need(gate.AssetId == "road-sign-down" && gate.VisualOnly && gate.Id != scout.Id, "未解锁的南口仍与陈安共用交互");
            foreach (string direction in new[] { "left", "right", "down" })
                Need(PixelDemoArt.Load("Props", "road-sign-" + direction) != null, "缺少路牌 " + direction);
            var defended = state.Copy();
            defended.Demo.Stage = BaseDemoStage.DefenseWon;
            var afterScout = BaseSpatial.Objects(defended, "base").Single(obj => obj.Id == BaseDemoNpcs.CampScout);
            Need((int)afterScout.X == 10 && (int)afterScout.Y == 4, "首防后陈安没有驻在 10,4");
            var ids=new[]{"baron","chunshen","horn","laozhou","merchant","villager","chenan","guard","survivor","villager-girl","rescued-villager"};
            // 行为／表现解耦（2026-11-02 定）：能力由贴图有无动作图集决定，不再按类别硬关。
            // 这里只验证「分类口径」仍正确（这些 id 属于交互 NPC），不断言它们「没有攻击能力」——
            // villager 恰好带 villager-attack-v2，作士兵参战时能播攻击，这是正确行为而非缺陷。
            foreach(string id in ids)Need(BaseDemoNpcs.IsMotionNpc(id),"NPC 分类丢失："+id);
            var shader=Shader.Find("Sprites/Default")??Shader.Find("Unlit/Texture");Need(shader!=null,"缺少测试材质");
            var go=new GameObject("C9-test");var material=new Material(shader);
            try
            {
                foreach(string id in new[]{"chenan","villager"})
                {
                    var sheet=new PixelCharacterSheet(go.transform,material,PixelDemoArt.Load("Characters",id),null,id,false,false);
                    var attack=typeof(PixelCharacterSheet).GetField("attackAtlas",BindingFlags.NonPublic|BindingFlags.Instance);
                    var walk=typeof(PixelCharacterSheet).GetField("walkAtlas",BindingFlags.NonPublic|BindingFlags.Instance);
                    Need(attack.GetValue(sheet)==null&&walk.GetValue(sheet)==null,"静态 NPC 加载了战斗或行走图："+id);sheet.Dispose();
                }
            }
            finally{UnityEngine.Object.DestroyImmediate(go);UnityEngine.Object.DestroyImmediate(material);}
            var doc=BaseDemoRuins.Document(state);doc.Scenario.B.CommanderAppearances=new System.Collections.Generic.List<string>{"chenan"};
            bool rejected=false;try{BattleScenarioDocument.Validate(doc);}catch(ArgumentException){rejected=true;}Need(rejected,"场景队长校验未拒绝陈安外观");
            foreach(int seed in new[]{11,22,33})
            {
                state=JsonUtility.FromJson<BaseState>(Resources.Load<TextAsset>("DemoStages/camp-male").text);
                state.Demo.Rpg.GenerationSeed=seed;At(state,"home");Need(BaseDemo.AcknowledgeWaking(state),"无法推进苏醒");
                At(state,"exit_east");Need(BaseCampaign.Travel(state,"town"),"无法进入城镇");
                At(state,"demo_ruins_exit");Need(BaseCampaign.Travel(state,"outpost"),"无法进入遗迹入口");At(state,"demo_ruins_investigate");
                string first=state.Demo.Flow.FirstControllerId;bool accepted=state.Demo.Flow.ControllerAccepted,pledged=state.Demo.Flow.ControllerPledged;
                var session=BaseDemoRuins.Begin(state,"none");
                // 以非首次结算分支检查常规产出，不改阶段档或首次剧情分支。
                session.Opening.RuinsFirstClear=false;
                var hero=session.Battle.Soldiers.Single(u=>u.Side==BattleSide.B&&u.Kind==SoldierKind.Hero);
                Need(hero.AppearanceId=="galan"&&hero.Rpg!=null,"遗迹控制者外观或生成数据错误："+seed);
                Need(session.Battle.Soldiers.Where(u=>u.Kind==SoldierKind.Hero).All(u=>!string.IsNullOrEmpty(u.AppearanceId)&&u.AppearanceId!="wilderness"),"遗迹队长外观缺失");
                var run=new RealtimeCampaignBattle(session);BaseDemoRuins.Configure(run);
                var enemy=run.Model.Units.Single(u=>u.Id==hero.Id+1);enemy.Health=1;enemy.KnockedDown=true;hero.Health=1;hero.KnockedDown=true;hero.DeathResolved=true;
                run.Model.SetPaused(false);run.Tick(.02);WalkOutOfRuins(run);Need(BaseDemoRuins.Complete(state,run),"遗迹结算失败："+seed);
                var prisoner=state.Aftermath.Prisoners.Single(p=>p.BattleId==session.Opening.Id&&p.Kind==SoldierKind.Hero);
                Need(prisoner.AppearanceId=="galan"&&BaseAftermath.Decide(state,prisoner.Id,"recruit"),"遗迹俘虏外观或招降失败："+seed);
                var recruited=state.Leaders.Single(l=>l.Id=="partner_recruit:"+prisoner.Id);
                Need(BasePartnerDialogue.Prefix(state,recruited)=="story.partner_default","重打招降的同外观队长未使用通用对白");
                Need(state.Demo.Flow.FirstControllerId==first&&state.Demo.Flow.ControllerAccepted==accepted&&state.Demo.Flow.ControllerPledged==pledged,"重刷招降改动了首次控制者剧情标记");
            }
            Debug.Log("[VerifyChenanAndRuins] NPC, scene validation and three ruins setups PASS");
        }
    }
}

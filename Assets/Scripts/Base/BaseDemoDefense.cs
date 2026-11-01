using System;
using System.Collections.Generic;
using System.Linq;
using BaseConquest.Battle;
using BaseConquest.RealtimePrototype;

namespace BaseConquest.Base
{
    [Serializable] public sealed class BaseDemoDefenseProgress
    {
        public int Version, GearDay, GearCount, WarningDay, ArrivalDay, VictoryDay, RetryDay, BatuStoryDay;
        public bool WorkshopRepaired, BarracksRepaired;
        public string GearProduct="", BatuFate="", SecondOutcome="";
        public bool MerchantDone,FirstBriefingSeen,SecondBriefingSeen,BattleIntelSeen;public double MerchantX,MerchantY;
        public List<BaseEnemyMember> MainForce=new List<BaseEnemyMember>();
        public BaseFacilityProtection Workshop;
        public ArmyDefinition Army;
        public BaseCampaignEvent Mission;
        public BaseDemoDefenseProgress Copy()=>new BaseDemoDefenseProgress {Version=Version,GearDay=GearDay,GearCount=GearCount,WarningDay=WarningDay,ArrivalDay=ArrivalDay,VictoryDay=VictoryDay,RetryDay=RetryDay,BatuStoryDay=BatuStoryDay,WorkshopRepaired=WorkshopRepaired,BarracksRepaired=BarracksRepaired,GearProduct=GearProduct,BatuFate=BatuFate,SecondOutcome=SecondOutcome,MerchantDone=MerchantDone,FirstBriefingSeen=FirstBriefingSeen,SecondBriefingSeen=SecondBriefingSeen,BattleIntelSeen=BattleIntelSeen,MerchantX=MerchantX,MerchantY=MerchantY,MainForce=MainForce?.Select(unit=>unit.Copy()).ToList(),Workshop=Workshop?.Copy(),Army=Army?.Clone(),Mission=Mission?.Copy()};
    }
    public static class BaseDemoDefense
    {
        public const string EventId="demo:defense:first", SecondEventId="demo:defense:second", Proof="demo:defense:won";
        public const int RepairMaterials=10, RepairCoins=20;
        public static int RepairMaterialCost(BaseState state)=>BaseRpg.Active(state)?state.Demo.Rpg.FacilityRepairMaterials:RepairMaterials;
        public static int RepairCoinCost(BaseState state)=>BaseRpg.Active(state)?state.Demo.Rpg.FacilityRepairCoins:RepairCoins;
        public static bool Enabled(BaseState state)=>BaseDemo.Active(state)&&state.Demo.Defense?.Version==1;
        public static bool Later(BaseState state)=>state.Demo.Stage==BaseDemoStage.DefenseWarned||state.Demo.Stage==BaseDemoStage.DefenseWon||BaseDemoFinale.Later(state);
        public static bool Delivered(BaseState state)=>state.Demo.Stage==BaseDemoStage.RuinsDelivered||Later(state);
        public static bool Won(BaseState state)=>Enabled(state)&&(state.Demo.Stage==BaseDemoStage.DefenseWon||BaseDemoFinale.Later(state));
        public static int NextArrival(BaseState state)=>Math.Max(state.Demo.Defense.ArrivalDay,state.Demo.Defense.RetryDay);
        public static bool Due(BaseState state)=>Enabled(state)&&state.Demo.Stage==BaseDemoStage.DefenseWarned&&state.Day>=NextArrival(state)&&state.Campaign.Events.Any(mission=>mission.Id==EventId&&mission.Status=="pending");
        public static bool SecondDue(BaseState state)=>Won(state)&&state.Campaign.Events.Any(mission=>mission.Id==SecondEventId&&mission.Status=="pending"&&mission.AvailableDay<=state.Day);
        public static bool AnyDue(BaseState state)=>Due(state)||SecondDue(state);
        public static int SecondArrival(BaseState state)=>state.Campaign.Events.FirstOrDefault(mission=>mission.Id==SecondEventId&&mission.Status=="pending")?.AvailableDay??0;
        public static void AcknowledgeBatu(BaseState state)
        {
            if(!Won(state)||state.Demo.Defense.BatuStoryDay>0)throw new InvalidOperationException("首防对白已经确认");
            int arrival=checked(state.Day+(state.Demo.Rpg?.SecondRaidDays??5));if(arrival>1000000)throw new InvalidOperationException("日期已达上限");
            var preset=state.Demo.Encounters?.Camp;if(preset==null||preset.Commanders.Count!=3)throw new InvalidOperationException("缺少三名队长的敌军主力配置，请重建阶段档");
            var site=new BaseEnemySite {Id=BaseDemoFinale.Camp};BaseThreats.InstallPreset(site,preset);
            if(state.Demo.Defense.BatuFate=="dead")site.Garrison.RemoveAll(unit=>unit.Kind==SoldierKind.Hero&&unit.PresetSlot==1);
            foreach(var unit in site.Garrison){unit.CommanderSlot=unit.Kind==SoldierKind.Hero?unit.PresetSlot:unit.Deployment.CommanderSlot;if(unit.Kind==SoldierKind.Hero)unit.Name=unit.PresetSlot==0?"大当家":unit.PresetSlot==1?"巴图":"豁牙";}
            BaseRpg.PrepareEnemies(state,site.Garrison,SecondEventId);
            state.Demo.Defense.MainForce=site.Garrison.Select(unit=>unit.Copy()).ToList();state.Demo.Defense.BatuStoryDay=state.Day;
            var mission=state.Demo.Defense.Mission.Copy();mission.Id=SecondEventId;mission.Title="黑鸦寨第二波入侵";mission.SourceSiteId="";mission.DepartedDay=state.Day;mission.AvailableDay=mission.DeadlineDay=arrival;mission.EnemyArmy=site.Garrison.Select(unit=>unit.Copy()).ToList();mission.EnemyRecoveryDay=0;mission.Status="pending";
            state.Campaign.Events.Add(mission);
            var entry=BaseSpatial.Nearest(state,new BasePoint(2,6));state.Demo.Defense.MerchantX=entry.X;state.Demo.Defense.MerchantY=entry.Y;
            state.Campaign.Layouts.Single(map=>map.Region=="base").Objects.Add(new BaseMapPlacement {Id="demo_merchant",Name="行商",Kind="npc",AssetId="merchant",X=entry.X,Y=entry.Y,Width=.5,Height=.5,VisualOnly=true});
        }
        public static bool CancelSecond(BaseState state,string reason)
        {
            var mission=state.Campaign.Events.FirstOrDefault(item=>item.Id==SecondEventId&&item.Status=="pending");if(mission==null)return false;
            mission.Status="resolved";state.Demo.Defense.SecondOutcome=reason;return true;
        }
        public static bool Repaired(BaseState state,string id)=>Enabled(state)&&(id=="workshop"?state.Demo.Defense.WorkshopRepaired:id=="barracks"&&state.Demo.Defense.BarracksRepaired);
        public static bool BattleActive(BaseState state)=>AnyDue(state)&&state.Campaign.Location=="perimeter"&&state.Campaign.HasActiveBattle&&new[]{EventId,SecondEventId}.Contains(state.Campaign.ActiveBattle.EventId);
        public static bool Encounter(BaseCampaignSession session)=>session!=null&&new[]{EventId,SecondEventId}.Contains(session.Opening.EventId);
        public static void PrepareOpening(BaseState state)
        {
            var source=state.Campaign.EnemySites[0];var army=source.Capacity.Clone();army.HeroCount=1;army.CaptainSlot=0;
            army.MeleeCount=source.RaidMelee;army.RangedCount=source.RaidRanged;army.CavalryCount=source.RaidCavalry;
            army.CommanderStats=army.CommanderStats.Take(1).Select(stats=>stats.Clone()).ToList();army.CommanderNames=new List<string>{"来袭队长"};
            army.Commanders=new List<CommanderLoadout>{new CommanderLoadout {Melee=army.MeleeCount,Ranged=army.RangedCount,Cavalry=army.CavalryCount}};
            var mission=state.Campaign.Events.First(item=>item.Kind=="defense").Copy();mission.Id=EventId;mission.Title="首次入侵 · 守住营地外围";mission.SourceSiteId="";mission.EnemyArmy.Clear();mission.Kind="defense";mission.Status="pending";
            state.Demo.Defense=new BaseDemoDefenseProgress {Version=1,Workshop=state.Muster.Facilities.Single(facility=>facility.FacilityId=="workshop").Copy(),Army=army,Mission=mission};
        }
        public static void InstallMap(BaseState state)
        {
            foreach(var position in state.Campaign.PerimeterBoard.Positions())
            {var cell=state.Campaign.PerimeterBoard.Cell(position);cell.Zone=!cell.Walkable?DeploymentZone.None:position.X>=15&&position.X<=17?DeploymentZone.A:position.X>=2&&position.X<=4?DeploymentZone.B:DeploymentZone.None;}
            var camp=state.Campaign.Layouts.Single(map=>map.Region=="base");
            camp.Objects.Add(new BaseMapPlacement {Id="demo_fix_workshop",Name="工坊修复 · 需建设许可",X=19,Y=1.3,Width=1.2,Height=.6,VisualOnly=true});
            camp.Objects.Add(new BaseMapPlacement {Id="demo_fix_barracks",Name="兵营修复 · 需建设许可",X=12,Y=8.8,Width=1.2,Height=.6,VisualOnly=true});
            camp.Objects.Add(new BaseMapPlacement {Id="demo_defense_exit",Name="← 迎敌战场 · 尚无入侵",X=.7,Y=3.5,Width=1.2,Height=2,VisualOnly=true});
            state.Campaign.Layouts.Add(new BaseMapLayout {Region="perimeter",Title="营地外围 · 首次防守",SpawnX=22,SpawnY=6.5,Objects=new List<BaseMapPlacement> {
                new BaseMapPlacement {Id="demo_defense_return",Name="返回营地 →",X=23.3,Y=6.5,Width=1.2,Height=2},
                new BaseMapPlacement {Id="demo_defense_start",Name="来袭部队 · 确认迎战",X=21,Y=6.5,Width=1,Height=1}
            }});
        }
        public static void PermitGranted(BaseState state)
        {
            if(!Enabled(state))return;
            foreach(string id in new[]{"workshop","barracks"})
            {var marker=state.Campaign.Layouts.Single(map=>map.Region=="base").Objects.Single(obj=>obj.Id=="demo_fix_"+id);marker.VisualOnly=false;marker.Name=id=="workshop"?"修复工坊":"修复兵营";}
        }
        public static string RepairProblem(BaseState state,string id)
        {
            if(!Enabled(state)||!new[]{"workshop","barracks"}.Contains(id))return "此旧Demo没有设施／防守快照，请新开Demo";
            if(!Delivered(state)||!state.Demo.Evidence.Contains(BaseDemoRuins.Permit))return id=="workshop"?"坍塌的旧工坊。未获灰岩领主许可，私自开炉锻造军械将被视作谋逆。":"荒废的校场围栏。需先前往灰岩镇高台，向霍恩爵士取得驻军与建设手令。";
            if(Repaired(state,id))return "已经修复，不会再次扣款";
            if(!BaseInteraction.At(state,"base","demo_fix_"+id)||BaseBattleLoot.Pending(state)||state.Muster.Parties.Count>0)return "请结束外出／战果，到建筑前修复点同层一格内确认";
            int materials=RepairMaterialCost(state),coins=RepairCoinCost(state);
            return state.Inventory.Materials<materials||state.Inventory.Coins<coins?"修复需要原料"+materials+"、钱币"+coins:"";
        }
        public static bool Repair(BaseState state,string id)
        {
            if(RepairProblem(state,id)!="")return false;
            state.Inventory.Materials-=RepairMaterialCost(state);state.Inventory.Coins-=RepairCoinCost(state);
            if(id=="workshop")
            {
                state.Demo.Defense.WorkshopRepaired=true;var current=state.Muster.Facilities.Single(facility=>facility.FacilityId==id);var saved=state.Demo.Defense.Workshop;
                current.Capacity=saved.Capacity;current.MinimumWorkers=saved.MinimumWorkers;
            }
            else state.Demo.Defense.BarracksRepaired=true;
            var layout=state.Campaign.Layouts.Single(map=>map.Region=="base");layout.Objects.RemoveAll(obj=>obj.Id=="demo_fix_"+id);var building=layout.Objects.Single(obj=>obj.Id==id);building.VisualOnly=false;building.Name=id=="workshop"?"工坊":"兵营";
            BaseDevelopment.Allocate(state);BaseAchievements.Unlock(state,BaseAchievements.Startup);return true;
        }
        public static void ProductionFinished(BaseState state,BaseDayReport report)
        {
            if(!Enabled(state))return;
            var progress=state.Demo.Defense;
            if(state.Demo.Stage==BaseDemoStage.RuinsDelivered&&progress.WorkshopRepaired&&progress.BarracksRepaired&&(!BaseDemoFlow.Active(state)||BaseDemoFlow.Recruited(state))&&report.ProductsMade>0&&progress.GearDay==0)
            {
                if(state.Day>=1000000)throw new InvalidOperationException("日期已达上限，不能安排次日入侵");
                progress.GearDay=report.CompletedDay;progress.GearProduct=report.Product;progress.GearCount=report.ProductsMade;progress.WarningDay=state.Day;progress.ArrivalDay=state.Day+1;
                var mission=progress.Mission.Copy();mission.DepartedDay=state.Day;mission.AvailableDay=mission.DeadlineDay=progress.ArrivalDay;state.Campaign.Events.Add(mission);state.Demo.Stage=BaseDemoStage.DefenseWarned;
            }
            if(state.Demo.Stage==BaseDemoStage.DefenseWarned)
            {var marker=state.Campaign.Layouts.Single(map=>map.Region=="base").Objects.Single(obj=>obj.Id=="demo_defense_exit");marker.VisualOnly=!Due(state);marker.Name=Due(state)?"首次入侵已到达 · 前往迎战":"入侵明日抵达 · 先整备";}
            else if(SecondDue(state)){var marker=state.Campaign.Layouts.Single(map=>map.Region=="base").Objects.Single(obj=>obj.Id=="demo_defense_exit");marker.VisualOnly=false;marker.Name="第二波入侵已到达 · 前往迎战";}
        }
        public static string Objective(BaseState state)
        {
            if(!Enabled(state))return "此旧Demo没有修复／防守快照；请新开Demo体验新增流程";
            if(SecondDue(state))return "第二波入侵已到达：前往营地外围迎战";
            if(Won(state)){int arrival=SecondArrival(state);return (arrival>0?"距黑鸦寨来袭 "+Math.Max(0,arrival-state.Day)+" 天 · ":state.Demo.Defense.SecondOutcome=="damaged"?"营防失守！匪众趁乱掠走部分物资，残敌将于明日卷土重来。 · ":"")+(BaseDemoFinale.Enabled(state)?BaseDemoFinale.Objective(state):"黑鸦寨匪众退去，营地守住了。他们没有伤筋动骨，这只是第一回——处理战果并返营补损");}
            if(state.Demo.Stage==BaseDemoStage.DefenseWarned)return Due(state)?"黑鸦寨匪众已杀至营门外，此刻歇息无异引颈受戮，即刻前往迎敌战场！":"黑鸦寨探子嗅到了工坊开炉的动静，大批匪众"+(NextArrival(state)-state.Day)+"天后兵临断流滩！";
            if(BaseDemoFlow.Active(state)&&!BaseDemoFlow.Recruited(state))return "招募建设人手："+BaseDemoFlow.RecruitProgress(state);
            if(!state.Demo.Defense.WorkshopRepaired||!state.Demo.Defense.BarracksRepaired)return "建设许可已取得：修复工坊与兵营";
            return "安排工坊岗位，过日完成首次军需生产";
        }
        public static string TravelProblem(BaseState state,string target)
        {
            if(BaseSceneCombat.Active(state)||BaseBattleLoot.Pending(state)||state.Aftermath.GameOver)return "先完成当前战斗或战果处理";
            if(target=="perimeter")return (Due(state)||Won(state))&&BaseInteraction.At(state,"base","demo_defense_exit")?"":"从营地西侧出口前往；首次入侵到达后才开放迎战";
            if(target=="base"&&BaseInteraction.At(state,"perimeter","demo_defense_return"))return "";
            return "请先完成本次主动迎战，可返回营地调整人员／军需；不能从其他区域遥控";
        }
        public static bool Travel(BaseState state,string target)
        {
            if(TravelProblem(state,target)!="")return false;
            if(state.Campaign.Location=="base"){state.Campaign.BaseX=state.PlayerX;state.Campaign.BaseY=state.PlayerY;}
            state.Campaign.Location=target;var landing=BaseSpatial.Layout(state);state.PlayerX=target=="perimeter"&&BaseDemoRelease.Enabled(state)?landing.SpawnX:target=="perimeter"?22:1.6;state.PlayerY=target=="perimeter"&&BaseDemoRelease.Enabled(state)?landing.SpawnY:6.5;BaseAdventure.Arrive(state);return true;
        }
        public static BaseCampaignSession Begin(BaseState state,string mode,BaseMusterPlan plan=null)
        {
            if(!AnyDue(state)||!BaseInteraction.At(state,"perimeter","demo_defense_start"))throw new InvalidOperationException("请在入侵抵达后到外围迎战点确认");
            plan=plan??BaseAdventure.Preview(state,"defense",true);
            if(plan.Purpose!="defense"||!plan.Leaders.Contains(state.PlayerLeaderId))throw new InvalidOperationException("首次防守需主角在场，训练中请先取消训练或重新选队");
            bool second=SecondDue(state);string eventId=second?SecondEventId:EventId;
            var army=state.Demo.Defense.Army.Clone();if(!second){BaseDemoEncounterConfig.ConfigureArmy(state,eventId,army);army.CommanderNames=new List<string>{"巴图"};}var own=army.Clone();own.HeroCount=1;own.CaptainSlot=0;own.CommanderStats.Clear();own.CommanderNames.Clear();own.Commanders=new List<CommanderLoadout>{new CommanderLoadout()};
            var document=new BattleScenarioDocument {Title="Demo首次防守",Scenario=new BattleScenario {Board=BaseBattlefield.Board(state).Clone(),A=own,B=army,MaxRounds=90,Objective=new ObjectiveConfig {Title="守住营地外围"}}};
            return new BaseCampaignSession(BaseCampaign.Begin(state,eventId,document,mode,plan,true));
        }
        public static void Configure(RealtimeCampaignBattle run)
        {
            if(run.Session.Opening.EventId==SecondEventId)
            {run.OutcomeResolver=current=>BaseDemoCombat.Defeated(current)?BattleOutcome.SideBWin:!current.Model.Units.Any(unit=>unit.Side==PrototypeSide.Enemy&&unit.Alive)||current.Model.Elapsed+1e-8>=current.Battle.Scenario.Objective.HoldRounds?BattleOutcome.SideAWin:BattleOutcome.None;return;}
            run.OutcomeResolver=current=>
            {
                if(BaseDemoCombat.Defeated(current))return BattleOutcome.SideBWin;
                var enemies=current.Model.Units.Where(unit=>unit.Side==PrototypeSide.Enemy&&unit.Alive).ToArray();
                if(enemies.Length==0)return BattleOutcome.SideAWin;
                if(current.Model.Elapsed+1e-8>=current.Battle.Scenario.Objective.HoldRounds)
                {
                    var exit=current.Battle.ExitCell(BattleSide.B);var point=new PrototypePoint(exit.X+.25,exit.Z+.25);
                    foreach(var enemy in enemies.Where(unit=>unit.Order!=PrototypeOrder.Retreat))
                    {enemy.Order=PrototypeOrder.Retreat;enemy.Anchor=enemy.Goal=point;enemy.TargetId=-1;enemy.NextThinkAt=0;}
                }
                return BattleOutcome.None;
            };
        }
        public static bool Complete(BaseState state,RealtimeCampaignBattle run)
        {
            if(!BattleActive(state)||!Encounter(run.Session)||state.Campaign.ActiveBattle.Id!=run.Session.Opening.Id||run.Battle.Phase!=BattlePhase.Finished||run.Battle.Report.Outcome!=BattleOutcome.SideAWin)return false;
            bool second=run.Session.Opening.EventId==SecondEventId;
            run.SaveCheckpoint(state);if(!BaseCampaign.Settle(state,run.Session))return false;
            if(second){state.Demo.Defense.SecondOutcome="defended";BaseEconomy.Validate(state);return true;}
            var batu=run.Battle.Commander(BattleSide.B,0);
            state.Demo.Stage=BaseDemoStage.DefenseWon;state.Demo.Defense.VictoryDay=state.Day;state.Demo.Defense.BatuFate=batu!=null&&batu.InstantDead?"dead":"escaped";state.Demo.Evidence.Add(Proof);
            foreach(var manifest in state.Aftermath.Loot.Where(item=>item.BattleId==run.Session.Opening.Id))if(BaseBattleLoot.CanFinish(state,manifest)&&!state.Aftermath.Prisoners.Any(p=>p.BattleId==manifest.BattleId&&p.Status=="held"))BaseBattleLoot.Finish(state,manifest.BattleId);
            var marker=state.Campaign.Layouts.Single(map=>map.Region=="base").Objects.Single(obj=>obj.Id=="demo_defense_exit");marker.VisualOnly=false;marker.Name="← 迎敌战场 · 已守住";
            if(BaseDemoFinale.Enabled(state)){var exit=state.Campaign.Layouts.Single(map=>map.Region=="base").Objects.Single(obj=>obj.Id=="demo_march_exit");exit.VisualOnly=false;exit.Name="世界道路 · 攻营与返乡";}
            BaseEconomy.Validate(state);return true;
        }
        public static void Validate(BaseState state)
        {
            var progress=state.Demo.Defense;
            if(progress==null){if(Later(state))throw new ArgumentException("防守阶段缺少记录");return;}
            if(progress.Version==0)
            {if(progress.WorkshopRepaired||progress.BarracksRepaired||progress.GearDay!=0||progress.GearCount!=0||progress.WarningDay!=0||progress.ArrivalDay!=0||progress.VictoryDay!=0||!string.IsNullOrEmpty(progress.GearProduct)||Later(state))throw new ArgumentException("设施／防守记录缺版本");return;}
            if(progress.Version!=1||progress.Workshop==null||progress.Workshop.FacilityId!="workshop"||progress.Workshop.Level!=1||progress.Workshop.MaxProductLevel!=1||progress.Workshop.Capacity<1||progress.Workshop.Capacity>10000||progress.Workshop.MinimumWorkers<0||progress.Workshop.MinimumWorkers>progress.Workshop.Capacity||progress.Army==null||!BaseAftermath.ValidStats(progress.Army.Hero)||!BaseAftermath.ValidStats(progress.Army.Melee)||progress.Mission==null||progress.Mission.Id!=EventId||progress.Mission.Kind!="defense"||progress.Mission.HoldRounds<1||progress.Mission.HoldRounds>90)throw new ArgumentException("防守快照版本、设施或敌军配置非法");
            if((progress.WorkshopRepaired||progress.BarracksRepaired)&&!Delivered(state))throw new ArgumentException("没有交付许可不能修复设施");
            var workshop=state.Muster.Facilities.Single(facility=>facility.FacilityId=="workshop");
            if(workshop.Capacity!=(progress.WorkshopRepaired?progress.Workshop.Capacity:0)||workshop.MinimumWorkers!=(progress.WorkshopRepaired?progress.Workshop.MinimumWorkers:0)||workshop.Level!=1||workshop.MaxProductLevel!=1)throw new ArgumentException("工坊实际能力与修复状态不符");
            var camp=state.Campaign.Layouts.Single(map=>map.Region=="base");
            if(!BaseDemoPrologue.Village(state))foreach(string id in new[]{"workshop","barracks"})
            {
                bool repaired=Repaired(state,id);var building=camp.Objects.Single(obj=>obj.Id==id);var marker=camp.Objects.SingleOrDefault(obj=>obj.Id=="demo_fix_"+id);
                if(building.VisualOnly==repaired||(repaired?marker!=null:marker==null||marker.VisualOnly==Delivered(state)))throw new ArgumentException("建筑／修复点外观或交互状态不一致");
            }
            if(Later(state))
            {
                if(!progress.WorkshopRepaired||!progress.BarracksRepaired||progress.GearDay<state.Demo.DeliveredDay||progress.WarningDay!=progress.GearDay+1||progress.WarningDay>state.Day||progress.ArrivalDay!=progress.WarningDay+1||progress.ArrivalDay>1000000||!BaseEconomy.ProductsFor(state).Contains(progress.GearProduct)||progress.GearCount<1||!state.Campaign.Events.Any(mission=>mission.Id==EventId&&mission.AvailableDay==NextArrival(state)&&mission.DeadlineDay==NextArrival(state)))throw new ArgumentException("首件军需／预告／入侵日程不一致");
            }
            else if(progress.GearDay!=0||progress.GearCount!=0||progress.WarningDay!=0||progress.ArrivalDay!=0||progress.VictoryDay!=0||!string.IsNullOrEmpty(progress.GearProduct)||state.Campaign.Events.Any(mission=>mission.Id==EventId))throw new ArgumentException("未完成整备不得提前生成教学入侵");
            if(Won(state)?progress.VictoryDay<progress.ArrivalDay||progress.VictoryDay>state.Day||!state.Campaign.Reports.Any(report=>report.EventId==EventId&&!report.Passive&&report.Outcome=="SideAWin"):progress.VictoryDay!=0)throw new ArgumentException("首次防守胜利缺真实主动战果");
            if(Won(state)&&!new[]{"dead","escaped"}.Contains(progress.BatuFate)||!Won(state)&&!string.IsNullOrEmpty(progress.BatuFate))throw new ArgumentException("巴图命运与首防结果不一致");
            var second=state.Campaign.Events.Where(mission=>mission.Id==SecondEventId).ToArray();
            if(progress.BatuStoryDay==0)
            {if(second.Length!=0||(progress.MainForce?.Count??0)>0||!string.IsNullOrEmpty(progress.SecondOutcome)||progress.MerchantDone||progress.SecondBriefingSeen)throw new ArgumentException("首防对白前不应生成第二波");}
            else if(progress.BatuStoryDay<progress.VictoryDay||progress.BatuStoryDay>state.Day||second.Length!=1||progress.MainForce==null||progress.MainForce.Count==0||second[0].AvailableDay!=progress.BatuStoryDay+(state.Demo.Rpg?.SecondRaidDays??5)||!new[]{"", "intercepted", "camp_captured", "defended", "damaged"}.Contains(progress.SecondOutcome??"")||!string.IsNullOrEmpty(progress.SecondOutcome)&&second[0].Status!="resolved")throw new ArgumentException("第二波日期、主力或结局非法");
            if(state.Campaign.Location=="perimeter"&&!Due(state)&&!Won(state))throw new ArgumentException("未到达的教学入侵不能提前进入战场");
        }
    }
}

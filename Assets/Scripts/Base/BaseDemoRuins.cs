using System;
using System.Collections.Generic;
using System.Linq;
using BaseConquest.Battle;
using BaseConquest.RealtimePrototype;

namespace BaseConquest.Base
{
    public static class BaseDemoRuins
    {
        public const string EventId="demo:ruins:rescue", Proof="demo:ruins:rescued", Permit="demo:lord:building-permit";
        public static bool Enabled(BaseState state)=>BaseDemo.Active(state)&&state.Demo.RuinsVersion==1;
        public static bool Assigned(BaseState state)=>state.Demo.Stage==BaseDemoStage.RuinsAssigned||state.Demo.Stage==BaseDemoStage.RuinsCleared||BaseDemoDefense.Delivered(state);
        public static bool InRuins(BaseState state)=>BaseSceneRules.Active(state)?BaseSceneRules.Dungeon(state):BaseSceneInstances.Active(state)?state.Campaign.CurrentMapId=="demo_ruins":state.Campaign.Location=="outpost";
        public static bool CanExplore(BaseState state)=>Enabled(state)&&!BaseDemoPrologue.Village(state)&&state.Demo.Stage!=BaseDemoStage.Waking;
        public static bool BattleActive(BaseState state)=>CanExplore(state)&&InRuins(state)&&state.Campaign.HasActiveBattle&&state.Campaign.ActiveBattle.EventId==EventId;
        public static bool Encounter(BaseCampaignSession session)=>session?.Opening.EventId==EventId;
        public static string Objective(BaseState state)=>!Enabled(state)?"旧Demo不含"+BaseScriptNames.Ruins+"快照；继续原阶段，重新开始可体验新增内容":state.Demo.Stage==BaseDemoStage.RuinsCleared?"当前目标：带救助证据返回领主处交差":BaseDemoDefense.Delivered(state)?BaseDemoDefense.Objective(state):"当前目标：可邀请阿禾／阿铸，经"+BaseScriptNames.Town+"东侧进入"+BaseScriptNames.Ruins+"调查";
        public static void PrepareOpening(BaseState state)
        {
            var progress=state.Demo;progress.RuinsVersion=1;
            progress.Candidates=state.Leaders.Where(person=>person.Id=="partner_farm"||person.Id=="partner_craft").Select(person=>person.Copy()).ToList();
            if(progress.Candidates.Count!=2||state.Campaign.EnemySites.Count==0)throw new ArgumentException("35号缺少伙伴或敌军属性快照");
            progress.RuinsArmy=state.Campaign.EnemySites[0].Capacity.Clone();
            var reward=state.Campaign.Events.First(mission=>mission.Kind=="attack");
            progress.RuinsReward=new BaseInventory {Food=reward.FoodReward,Materials=reward.MaterialReward,Coins=reward.CoinReward};
        }
        public static void InstallMap(BaseState state)
        {
            var town=state.Campaign.Layouts.Single(map=>map.Region=="town");
            town.Objects.Add(new BaseMapPlacement {Id="demo_ruins_exit",Name=BaseScriptNames.Ruins+"调查 →",X=23.2,Y=6.5,Width=1.2,Height=2});
            if(!state.Leaders.Any(person=>person.Id=="partner_farm"))state.Campaign.Layouts.Single(map=>map.Region=="base").Objects.Add(new BaseMapPlacement {Id="demo_ahe",Name="阿禾 · 可选伙伴",X=8,Y=7,Width=.5,Height=.5});
            if(!state.Leaders.Any(person=>person.Id=="partner_craft"))town.Objects.Add(new BaseMapPlacement {Id="demo_azhu",Name="阿铸 · 询问短剑",X=9,Y=8,Width=.5,Height=.5});
            state.Campaign.Layouts.Add(new BaseMapLayout {Region="outpost",Title="野外"+BaseScriptNames.Ruins+" · 失踪者",SpawnX=2.5,SpawnY=7.5,Objects=new List<BaseMapPlacement> {
                new BaseMapPlacement {Id="demo_ruins_return",Name="← 返回"+BaseScriptNames.Town,X=.7,Y=7.5,Width=1,Height=2},
                new BaseMapPlacement {Id="demo_ruins_investigate",Name="异常者与控制来源 · 调查",X=12,Y=7,Width=1,Height=1},
                new BaseMapPlacement {Id="ruins_wall",Name=BaseScriptNames.Ruins+"残墙",X=12,Y=11.5,Width=6,Height=1,Blocks=true,VisualOnly=true,SourceObjectId="demo:ruins:wall",Kind="obstacle"}
            }});
            var board=BattleBoard.CreateDefault(24,14);
            foreach(var position in board.Positions()) {var cell=board.Cell(position);cell.Terrain=TerrainKind.Ground;cell.Zone=position.X>=4&&position.X<=6?DeploymentZone.A:position.X>=17&&position.X<=19?DeploymentZone.B:DeploymentZone.None;}
            state.Campaign.RegionMaps.RemoveAll(map=>map.Id=="outpost");state.Campaign.RegionMaps.Add(new BaseRegionMap {Id="outpost",Board=board});
            if(!state.Campaign.Events.Any(mission=>mission.Id==EventId))state.Campaign.Events.Add(new BaseCampaignEvent {Id=EventId,Title=BaseScriptNames.Ruins+" · 制服受控者并击败控制来源",Kind="attack",ScenarioFile="24_据点进攻.json",FoodReward=0,MaterialReward=0,CoinReward=0,ItemRewards=new List<BaseLootReward>()});
        }
        public static string Marker(string id)=>id=="partner_farm"?"demo_ahe":"demo_azhu";
        public static string Region(string id)=>id=="partner_farm"?"base":"town";
        public static bool Meet(BaseState state,string id)
        {
            if(!Enabled(state)||BaseDemoPrologue.Village(state)||state.Demo.Stage==BaseDemoStage.Waking||!state.Demo.Candidates.Any(person=>person.Id==id)||!(id=="partner_craft"?BaseDemoNpcs.NearAzhu(state):BaseInteraction.At(state,Region(id),Marker(id))))return false;
            if(!state.Demo.MetCompanions.Contains(id))state.Demo.MetCompanions.Add(id);return true;
        }
        public static bool Join(BaseState state,string id)
        {
            if(state.Adventure.Pauses.Count>0)return false;
            if(!Enabled(state)||!state.Demo.MetCompanions.Contains(id)||!Meet(state,id)||state.Leaders.Any(person=>person.Id==id)||!BaseRpg.HasRoom(state)||state.Muster.Parties.Count>0)return false;
            bool follow=state.Adventure.Members.Count<BaseAdventure.Capacity;
            var person=state.Demo.Candidates.Single(candidate=>candidate.Id==id).Copy();person.FacilityId=person.DesiredFacility=person.PartyId="";person.PostLocked=person.Away=person.Resting=false;
            person.AcquisitionSource=id=="partner_farm"?"剧情结识 · 新营地邀请":"剧情结识 · "+BaseScriptNames.Town+"邀请";person.AcquisitionDay=state.Day;
            person.HasHomePosition=true;person.HomeX=id=="partner_farm"?8:10;person.HomeY=7;person.HasMapPosition=true;person.MapRegion=state.Campaign.Location;
            person.MapId=BaseSceneInstances.Active(state)?state.Campaign.CurrentMapId:null;
            var point=BaseSpatial.Nearest(state,new BasePoint(state.PlayerX,state.PlayerY-.8));person.MapX=point.X;person.MapY=point.Y;person.UnavailableDay=state.Day;
            if(!follow){var home=BaseSpatial.Layout(state,"base");var landing=BaseSpatial.Nearest(state,new BasePoint(home.SpawnX,home.SpawnY),"base");person.MapRegion="base";person.MapId=home.SourceMapId;person.MapX=landing.X;person.MapY=landing.Y;}
            state.Leaders.Add(person);if(follow)state.Adventure.Members.Add(id);state.Campaign.Layouts.Single(map=>map.Region==Region(id)).Objects.RemoveAll(obj=>obj.Id==Marker(id));BaseDevelopment.Allocate(state);BaseAchievements.PartyChanged(state);return true;
        }
        public static string TravelProblem(BaseState state,string target)
        {
            if(BaseDemoDefense.Enabled(state)&&(target=="perimeter"||state.Campaign.Location=="perimeter"||state.Demo.Stage==BaseDemoStage.DefenseWarned&&!BaseTown.DemoRecruitment(state)))return BaseDemoDefense.TravelProblem(state,target);
            if(state.Demo.Stage==BaseDemoStage.DefenseWarned&&target=="outpost")return "先处理营地入侵，可往返"+BaseScriptNames.Town+"招兵整备";
            if(BaseDemoPrologue.Village(state)||state.Demo.Stage==BaseDemoStage.Waking)return "请先完成当前序章／醒来目标";
            if(BaseSceneCombat.Active(state)||BaseBattleLoot.Pending(state)||state.Aftermath.GameOver)return "请先处理当前战斗／战果";
            bool at(string region,string id)=>BaseInteraction.At(state,region,id);
            if(target=="outpost")return at("town","demo_ruins_exit")?"":"从"+BaseScriptNames.Town+"东侧"+BaseScriptNames.Ruins+"出口进入，无需先接取委托";
            if(target=="town"&&(at("base","exit_east")||at("outpost","demo_ruins_return")))return "";
            if(target=="base"&&at("town","travel_base"))return "";
            return "请到对应出口同层一格以内通行";
        }
        public static bool Travel(BaseState state,string target)
        {
            if(BaseDemoDefense.Enabled(state)&&(target=="perimeter"||state.Campaign.Location=="perimeter"||state.Demo.Stage==BaseDemoStage.DefenseWarned&&!BaseTown.DemoRecruitment(state)))return BaseDemoDefense.Travel(state,target);
            if(!Enabled(state)||TravelProblem(state,target)!="")return false;
            if(state.Campaign.Location=="base"){state.Campaign.BaseX=state.PlayerX;state.Campaign.BaseY=state.PlayerY;}
            string from=state.Campaign.Location;state.Campaign.Location=target;var layout=BaseSpatial.Layout(state);
            state.PlayerX=target=="base"?22.4:target=="town"?(from=="outpost"?22.4:2):layout.SpawnX;state.PlayerY=target=="outpost"?layout.SpawnY:6.5;BaseAdventure.Arrive(state);return true;
        }
        public static BattleScenarioDocument Document(BaseState state)
        {
            if(!Enabled(state))throw new InvalidOperationException("此旧Demo没有"+BaseScriptNames.Ruins+"属性快照，请新开Demo");
            var army=state.Demo.RuinsArmy.Clone();var controller=army.Hero.Clone();controller.Health*=1.5f;controller.Damage*=1.2f;
            army.HeroCount=1;army.MeleeCount=2;army.RangedCount=army.CavalryCount=0;
            army.CommanderStats=new List<SoldierStats>{controller};army.CommanderNames=new List<string>{BaseScriptNames.Galan};army.Commanders=new List<CommanderLoadout>{new CommanderLoadout {Melee=2}};
            BaseDemoEncounterConfig.ConfigureArmy(state,EventId,army);
            var own=army.Clone();own.MeleeCount=2;own.RangedCount=own.CavalryCount=0;own.HeroCount=1;own.CaptainSlot=0;own.CommanderStats.Clear();own.Commanders=new List<CommanderLoadout>{new CommanderLoadout {Melee=2}};own.CommanderNames.Clear();
            foreach(var kind in BattleSimulation.TroopKinds){var source=state.Workers.FirstOrDefault(worker=>BaseCampaign.ToBattle(worker.Kind)==kind&&worker.HasCombatProfile);if(source!=null){if(kind==SoldierKind.Melee)own.Melee=source.CombatStats.Clone();else if(kind==SoldierKind.Ranged)own.Ranged=source.CombatStats.Clone();else own.Cavalry=source.CombatStats.Clone();}}
            return new BattleScenarioDocument {Title="Demo"+BaseScriptNames.Ruins+"救助",Scenario=new BattleScenario {Board=BaseSceneCombat.Project(state),A=own,B=army,MaxRounds=90,FogEnabled=false,Objective=new ObjectiveConfig {Title="击败控制来源，救助失踪者"}}};
        }
        public static BaseCampaignSession Begin(BaseState state,string mode="full")
            => Begin(state, mode, null, null);

        internal static BaseCampaignSession Begin(BaseState state, string mode, BaseMusterPlan plan, BasePreparedRoster roster)
        {
            if(BaseSceneRules.Entrance(state))BaseSceneRules.EnterDungeon(state);
            if(!CanExplore(state)||!InRuins(state)||!BaseInteraction.CanReach(state,BaseSpatial.Objects(state).SingleOrDefault(o=>o.Id=="demo_ruins_investigate")))throw new InvalidOperationException("请在"+BaseScriptNames.Ruins+"调查点旁确认交战，无需先接取委托");
            var mission=state.Campaign.Events.Single(e=>e.Id==EventId);
            if(state.Demo.RuinsRewards==null)BaseRuinsRewards.Enable(state);
            state.Demo.RuinsRewards.PendingBattleId="";
            state.Demo.RuinsRewards.PendingFirstClear=false;
            var document=Document(state);plan??=BaseAdventure.Preview(state,"expedition",true);
            var previous=mission.EnemyArmy;var rules=state.Demo.RuinsRewards;
            if(previous.Any(e=>e.Kind==SoldierKind.Hero&&(e.Dead||e.KnockedDown)))rules.EnemyGeneration++;
            var fresh=BaseDemoCombat.CreateEnemies(EventId,document.Scenario.B,BaseDemoEncounterConfig.Preset(state,EventId));
            for(int i=0;i<fresh.Count;i++)
            {
                var old=previous.ElementAtOrDefault(i);
                if(old!=null&&!old.Dead&&!old.KnockedDown)fresh[i]=old.Copy();
                else fresh[i].Id=EventId+":spawn:"+rules.EnemyGeneration+":unit:"+i;
                fresh[i].Dead=fresh[i].KnockedDown=false;fresh[i].Health=fresh[i].Stats.Health;
            }
            mission.Status="pending";mission.EnemyRecoveryDay=0;mission.EnemyArmy=fresh;
            var active=BaseCampaign.Begin(state,EventId,document,mode,plan,true,roster);active.Difficulty.DropRate=0;active.RuinsExposureFraction=state.Demo.RuinsRewards.ExposureFraction;
            active.RuinsFirstClear=state.Demo.RuinsDay==0;
            var session=new BaseCampaignSession(active);
            if (!active.ManualDeployment) session.Battle.AutoDeploy(BattleSide.A);
            if (roster != null && session.Battle.Soldiers.Count(u => u.Side == BattleSide.A) != roster.Units.Length)
                throw new InvalidOperationException("所选编队无法完整入场");
            session.Bind();
            if(!session.Battle.Start(out string problem))throw new InvalidOperationException(problem);return session;
        }
        public static IEnumerable<BattleSoldier> Civilians(BaseCampaignSession session)=>session.Opening.Enemies.Select((enemy,index)=>new {enemy,index}).Where(x=>x.enemy.IsControlled).Select(x=>session.UnitForEnemy(x.index));
        public static bool SoloRetreatGuide(RealtimeCampaignBattle run)
        {
            if(run==null||!Encounter(run.Session)||!run.Session.Opening.RuinsFirstClear||run.Battle.Phase!=BattlePhase.Fighting)return false;
            var allies=run.Battle.Soldiers.Where(u=>u.Side==BattleSide.A).ToArray();
            var player=run.Model.Player;
            return allies.Length==1&&allies[0].Kind==SoldierKind.Hero&&player!=null&&player.Alive&&player.Health<=player.MaxHealth*.5;
        }
        public static bool ControllerDefeated(RealtimeCampaignBattle run)
        {var unit=run.Battle.Commander(BattleSide.B,0);return unit!=null&&!unit.Escaped&&(unit.KnockedDown||unit.InstantDead||unit.Health<=0);}
        public static void Configure(RealtimeCampaignBattle run)
        {
            foreach(var leader in run.Battle.Soldiers.Where(unit=>unit.Side==BattleSide.B&&unit.Kind==SoldierKind.Hero))leader.ProtectFromInstantDeath=run.Session.Opening.RuinsFirstClear;
            foreach(var victim in Civilians(run.Session)){victim.ProtectFromInstantDeath=true;victim.AppearanceId="villager";}
            run.Battle.RuinsCivilianIds=new HashSet<int>(Civilians(run.Session).Select(v=>v.Id));
            var controller=run.Battle.Commander(BattleSide.B,0);if(controller!=null)controller.AppearanceId="galan";
            run.EnableRuinsExposure(run.Session.Opening.RuinsExposureFraction>0?run.Session.Opening.RuinsExposureFraction:.03);
            run.ImmediateVictory=()=>ControllerDefeated(run);
            run.OutcomeResolver=current=>ControllerDefeated(current)?BattleOutcome.SideAWin:BaseDemoCombat.Defeated(current)?BattleOutcome.SideBWin:current.Model.Elapsed>=90?BattleOutcome.RoundLimit:BattleOutcome.None;
        }
        public static bool Complete(BaseState state,RealtimeCampaignBattle run,bool allowMercyAchievement=true)
        {
            if(!BattleActive(state)||!Encounter(run.Session)||state.Campaign.ActiveBattle.Id!=run.Session.Opening.Id||run.Battle.Phase!=BattlePhase.Finished||run.Battle.Report.Outcome!=BattleOutcome.SideAWin||!ControllerDefeated(run))return false;
            foreach(var victim in Civilians(run.Session)){victim.Rescued=true;victim.ProtectFromInstantDeath=true;victim.InstantDead=false;}
            run.SaveCheckpoint(state);
            if(!BaseCampaign.Settle(state,run.Session))return false;
            if(run.Session.Opening.RuinsFirstClear&&BaseDemoFlow.Active(state))state.Demo.Flow.FirstControllerId=state.Aftermath.Prisoners.FirstOrDefault(p=>p.BattleId==run.Session.Opening.Id&&p.Kind==SoldierKind.Hero)?.Id??"";
            BaseAchievements.Unlock(state,BaseAchievements.Ruins);if(allowMercyAchievement&&!run.Battle.RuinsCivilianDamaged)BaseAchievements.Unlock(state,BaseAchievements.Mercy);
            if(state.Demo.RuinsDay==0){state.Demo.RuinsDay=state.Day;state.Demo.Evidence.Add(Proof);if(BaseSceneRules.Active(state)&&state.Demo.Rescue.Stage==0)state.Demo.Rescue.Stage=1;
            state.Demo.Stage=BaseDemoStage.RuinsCleared;}
            if(state.Demo.RuinsRewards==null)BaseRuinsRewards.Enable(state);state.Demo.RuinsRewards.PendingBattleId=run.Session.Opening.Id;
            state.Demo.RuinsRewards.PendingFirstClear=run.Session.Opening.RuinsFirstClear;
            if(run.Session.Opening.RuinsFirstClear&&!BaseRuinsRewards.Claim(state))throw new InvalidOperationException(BaseRuinsRewards.ClaimProblem(state));
            foreach(var manifest in state.Aftermath.Loot.Where(item=>item.BattleId==run.Session.Opening.Id))if(BaseBattleLoot.CanFinish(state,manifest)&&!state.Aftermath.Prisoners.Any(p=>p.BattleId==manifest.BattleId&&p.Status=="held"))BaseBattleLoot.Finish(state,manifest.BattleId);
            BaseSceneRules.LeaveDungeon(state);BaseEconomy.Validate(state);return true;
        }
        public static bool Deliver(BaseState state)
        {
            if(!Enabled(state)||state.Demo.Stage!=BaseDemoStage.RuinsCleared||state.Day>1000000-BaseDemoDefense.WarningLead||!BaseInteraction.At(state,"town","town_quest"))return false;
            if(state.Demo.AcceptedDay==0&&!BaseDemo.AcceptLordQuest(state))return false;
            var reward=state.Demo.RuinsReward;if((long)state.Inventory.Food+reward.Food>100000000||(long)state.Inventory.Materials+reward.Materials>100000000||(long)state.Inventory.Coins+reward.Coins>100000000)return false;
            state.Inventory.Food+=reward.Food;state.Inventory.Materials+=reward.Materials;state.Inventory.Coins+=reward.Coins;
            state.Demo.DeliveredDay=state.Day;state.Demo.Stage=BaseDemoStage.RuinsDelivered;state.Demo.Evidence.Add(Permit);BaseDemoDefense.PermitGranted(state);BaseDemoDefense.ScheduleOnDelivery(state);return true;
        }
        public static void Validate(BaseState state)
        {
            var progress=state.Demo;
            if(progress.RuinsVersion==0){if(progress.RuinsDay!=0||progress.DeliveredDay!=0||progress.Candidates?.Count>0||progress.MetCompanions?.Count>0||progress.Stage==BaseDemoStage.RuinsCleared||progress.Stage==BaseDemoStage.RuinsDelivered)throw new ArgumentException(BaseScriptNames.Ruins+"内容缺版本");return;}
            if(progress.RuinsVersion!=1||progress.Candidates==null||progress.Candidates.Count!=2||!progress.Candidates.Select(person=>person?.Id).OrderBy(id=>id).SequenceEqual(new[]{"partner_craft","partner_farm"})||progress.Candidates.Any(person=>person.Health<=0||person.PersonalEquipment?.Version!=1||!BaseAftermath.ValidStats(BasePersonalEquipment.Stats(person)))||progress.MetCompanions==null||progress.MetCompanions.Distinct().Count()!=progress.MetCompanions.Count||progress.MetCompanions.Any(id=>!progress.Candidates.Any(person=>person.Id==id))||progress.RuinsArmy==null||!BaseAftermath.ValidStats(progress.RuinsArmy.Hero)||!BaseAftermath.ValidStats(progress.RuinsArmy.Melee)||progress.RuinsReward==null||new[]{progress.RuinsReward.Food,progress.RuinsReward.Materials,progress.RuinsReward.Coins}.Any(value=>value<0||value>1000000))throw new ArgumentException(BaseScriptNames.Ruins+"版本、伙伴、属性或奖赏非法");
            bool delivered=BaseDemoDefense.Delivered(state),cleared=progress.Stage==BaseDemoStage.RuinsCleared||delivered;
            var expected=progress.AcceptedDay>0?new List<string>{BaseDemo.Assignment}:new List<string>();if(cleared)expected.Add(Proof);if(delivered)expected.Add(Permit);if(BaseDemoDefense.Won(state))expected.Add(BaseDemoDefense.Proof);if(BaseDemoFinale.Later(state))expected.Add(BaseDemoFinale.AttackProof);if(progress.Stage==BaseDemoStage.Finished||BaseDemoFinale.Rebuilding(state))expected.Add(BaseDemoFinale.Sword);
            if(!progress.Evidence.SequenceEqual(expected)||(cleared?(progress.RuinsDay<progress.WakeDay||progress.RuinsDay>state.Day||!state.Campaign.Reports.Any(report=>report.EventId==EventId&&report.Outcome=="SideAWin")):progress.RuinsDay!=0))throw new ArgumentException(BaseScriptNames.Ruins+"证据与真实战果不一致");
            if(delivered?(progress.DeliveredDay<progress.RuinsDay||progress.DeliveredDay>state.Day):progress.DeliveredDay!=0)throw new ArgumentException(BaseScriptNames.Ruins+"许可与交付日期不一致");
            if(state.Leaders.Any(person=>(person.Id=="partner_farm"||person.Id=="partner_craft")&&!progress.MetCompanions.Contains(person.Id))||InRuins(state)&&!CanExplore(state)||!BaseDemoFinale.Opened(state)&&state.Campaign.HasActiveBattle&&!BattleActive(state)&&!BaseDemoDefense.BattleActive(state)&&!BaseDemoFinale.BattleActive(state)&&!BaseBanditHideout.BattleActive(state))throw new ArgumentException("伙伴或剧情战阶段非法");
        }
    }
}

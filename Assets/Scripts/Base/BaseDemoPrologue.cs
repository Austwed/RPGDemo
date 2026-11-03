using System;
using System.Collections.Generic;
using System.Linq;
using BaseConquest.Battle;
using BaseConquest.RealtimePrototype;

namespace BaseConquest.Base
{
    public static class BaseDemoPrologue
    {
        public const string BattleId="demo:prologue:pursuit";
        public const double WoundedFraction=.2;
        public static bool Village(BaseState state)=>BaseDemo.Active(state)&&state.Demo.Stage>=BaseDemoStage.PrologueBriefing&&state.Demo.Stage<=BaseDemoStage.PrologueWounded;
        public static IEnumerable<BaseLeader> Followers(BaseState state)=>Village(state)?state.Demo.SceneFollowers??Enumerable.Empty<BaseLeader>():Enumerable.Empty<BaseLeader>();
        public static bool Evacuating(BaseState state)=>state?.Demo?.Stage==BaseDemoStage.PrologueEscape;
        public static bool Pursuit(BaseState state)=>state?.Demo?.Stage==BaseDemoStage.ProloguePursuit;
        public static bool Safe(BaseState state)=>Followers(state).Count()==(BaseDemoIntro.Active(state)?3:2)&&Followers(state).All(person=>person.Fate=="escaped");
        public static BasePoint Exit(BaseState state,string id)
        {
            if(state?.Demo?.StoryScene==null)return new BasePoint(23.4,id=="prologue_ahe"?8.5:id.EndsWith("1",StringComparison.Ordinal)?6.5:7.5);
            var marker=BaseSpatial.Layout(state)?.Objects.SingleOrDefault(obj=>obj.Id=="prologue_trigger");
            if(marker==null)throw new InvalidOperationException("剧情场景缺少序章撤离点");
            var board=BaseBattlefield.Board(state);double offset=id=="prologue_ahe"?1:id.EndsWith("1",StringComparison.Ordinal)?-1:0;
            return new BasePoint(Math.Max(.35,Math.Min(board.Width-.35,marker.X)),Math.Max(.35,Math.Min(board.Height-.35,marker.Y+offset)));
        }
        public static void PrepareOpening(BaseState state)
        {
            state.Demo.PrologueVersion=1;state.Demo.Stage=BaseDemoStage.PrologueBriefing;
            var village=CreateVillage();
            state.Campaign.Layouts.RemoveAll(map=>map.Region=="base");state.Campaign.Layouts.Add(village);
            state.Campaign.RegionMaps.RemoveAll(map=>map.Id=="base");state.Campaign.RegionMaps.Add(new BaseRegionMap {Id="base",Board=BattleBoard.CreateDefault(24,14)});
            state.PlayerX=state.Campaign.BaseX=village.SpawnX;state.PlayerY=state.Campaign.BaseY=village.SpawnY;
            foreach(var cell in BaseBattlefield.Board(state).Cells){cell.Terrain=TerrainKind.Ground;cell.Zone=DeploymentZone.None;}
        }
        public static BaseMapLayout CreateVillage(bool tutorial=false)=>tutorial?new BaseMapLayout {Region="base",Title=BaseScriptNames.Village+" · 撤离",SpawnX=9,SpawnY=7,Objects=new List<BaseMapPlacement> {
                new BaseMapPlacement {Id="village_house",Name="村舍",SourceObjectId="prologue:house",AssetId="home",X=6,Y=11,Width=4,Height=3,Blocks=true,VisualOnly=true},
                new BaseMapPlacement {Id="intro_farm",Name="粮田 · 调动工人",AssetId="farm-small",X=10.8,Y=4.1,Width=3,Height=1.6,Blocks=true,Kind="facility"},
                new BaseMapPlacement {Id="intro_smith",Name="小铁匠铺",AssetId="workshop-small",X=15.8,Y=10.5,Width=2.4,Height=1.4,Blocks=true,Kind="facility"},
                new BaseMapPlacement {Id=BaseDemoIntro.DummyId,Name="练习木桩",AssetId="stump",SourceObjectId="intro:dummy",Kind="pixel-prop",X=13.5,Y=8.5,Width=.8,Height=.8,Blocks=true},
                new BaseMapPlacement {Id="village_bench",Name="长椅",AssetId="bench",Kind="decoration",DefinitionId="env_bench",SourceObjectId="intro:bench",X=9,Y=9.8,Width=1.1,Height=.4,Blocks=true,VisualOnly=true},
                new BaseMapPlacement {Id="prologue_report",Name="村民与队长 · 来袭消息",X=9,Y=8.3,Width=1,Height=1},
                new BaseMapPlacement {Id="prologue_trigger",Name="向东撤离 →",X=22,Y=7,Width=2,Height=3,VisualOnly=true}
            }}:new BaseMapLayout {Region="base",Title=BaseScriptNames.Village+" · 撤离",SpawnX=9,SpawnY=7,Objects=new List<BaseMapPlacement>{
                new BaseMapPlacement{Id="village_house",Name="村舍",AssetId="home",X=6,Y=11,Width=4,Height=3,Blocks=true,VisualOnly=true},
                new BaseMapPlacement{Id="village_field",Name="粮田",AssetId="farm",X=5,Y=3,Width=5,Height=3,Blocks=true,VisualOnly=true},
                new BaseMapPlacement{Id="prologue_report",Name="村民与队长 · 来袭消息",X=9,Y=8.3,Width=1,Height=1},
                new BaseMapPlacement{Id="prologue_trigger",Name="向东撤离 →",X=22,Y=7,Width=2,Height=3,VisualOnly=true}}};
        private static List<BaseLeader> Villagers(double x)=>new List<BaseLeader> {
            new BaseLeader {Id="demo_villager_1",Name="逃难村民",AppearanceId="villager",Health=100,MaxHealth=100,HasMapPosition=true,MapX=x,MapY=8,MapRegion="base"},
            new BaseLeader {Id="demo_villager_2",Name="逃难村民",AppearanceId="villager",Health=100,MaxHealth=100,HasMapPosition=true,MapX=x+.8,MapY=8.6,MapRegion="base"}
        };
        public static void SeedVillagers(BaseState s)
        {
            s.Demo.SceneFollowers=Villagers(3);var second=s.Demo.SceneFollowers[1];second.MapX=18;second.MapY=10;
            foreach(var person in s.Demo.SceneFollowers){person.Name="村民";person.HasHomePosition=true;person.HomeX=person.MapX;person.HomeY=person.MapY;}
        }
        public static void GatherVillagers(BaseState s)
        {foreach(var person in s.Demo.SceneFollowers)person.Name="逃难村民";AddAhe(s);}
        public static bool FollowingPlayer(BaseState s)=>!BaseDemoIntro.Active(s)||s.Demo.Stage!=BaseDemoStage.PrologueBriefing||s.Demo.Intro.ReporterArrived;
        public static BasePoint GatherCenter(BaseState s)
        {var i=s.Demo.Intro;return BaseDemoIntro.Active(s)&&s.Demo.Stage==BaseDemoStage.PrologueBriefing&&i?.ReporterArrived==true&&!i.ReporterLeaving?new BasePoint(i.ReporterX,i.ReporterY):new BasePoint(s.PlayerX,s.PlayerY);}
        private static void AddAhe(BaseState s)
        {if(BaseDemoIntro.Active(s)&&!s.Demo.SceneFollowers.Any(p=>p.Id=="prologue_ahe"))s.Demo.SceneFollowers.Add(new BaseLeader{Id="prologue_ahe",Name="阿禾 · 带领村民",AppearanceId="ahe",Health=100,MaxHealth=100,HasMapPosition=true,MapX=12,MapY=10,MapRegion="base"});}
        public static bool BeginEscape(BaseState state)
        {
            if(!Village(state)||state.Demo.Stage!=BaseDemoStage.PrologueBriefing)return false;
            if(BaseDemoIntro.Active(state)){if(state.Demo.Intro.Step<8)return false;}
            else if(!BaseInteraction.At(state,"base","prologue_report")&&!BaseInteraction.At(state,"base","demo_captain"))return false;
            BaseDemoIntro.RemovePractice(state);
            state.Demo.Stage=BaseDemoStage.PrologueEscape;if(!BaseDemoIntro.Active(state))state.Demo.SceneFollowers=Villagers(state.PlayerX-.8);
            state.Campaign.Layouts.Single(map=>map.Region=="base").Objects.RemoveAll(item=>item.Id=="prologue_report");return true;
        }
        public static bool AtTrigger(BaseState state)
        {
            if(!Evacuating(state))return false;
            if(state.Demo.StoryScene==null)return state.PlayerX>=21&&state.PlayerX<=23.4&&state.PlayerY>=5&&state.PlayerY<=9;
            var marker=BaseSpatial.Layout(state)?.Objects.SingleOrDefault(obj=>obj.Id=="prologue_trigger");
            return marker!=null&&marker.Object.Contains(state.PlayerX,state.PlayerY);
        }
        public static bool BeginPursuit(BaseState state)
        {
            if(!AtTrigger(state)||BaseSceneCombat.Active(state))return false;
            state.Demo.Stage=BaseDemoStage.ProloguePursuit;state.SceneBattleId=BattleId;return true;
        }
        public static void CaptureDepartures(BaseState state,BaseCompanionMovement movement)
        {
            if(!Pursuit(state))return;
            movement.Capture(state);
            foreach(var person in Followers(state).Where(person=>person.Fate!="escaped"&&(state.Demo.StoryScene==null?person.MapX>=23.2:new BasePoint(person.MapX,person.MapY).Distance(Exit(state,person.Id))<=.3)))
            {person.Fate="escaped";var agent=movement.Agents.FirstOrDefault(item=>item.Id==person.Id);if(agent!=null)agent.Visible=false;}
        }
        public static BaseCampaignSession Encounter(BaseState state)
        {
            if(!Pursuit(state)||state.SceneBattleId!=BattleId)throw new InvalidOperationException("尚未触发追兵剧情");
            var player=state.Leaders.Single(person=>person.Id==state.PlayerLeaderId).Copy();
            var stats=BaseCommanderIdentity.Stats(player,new ArmyDefinition().Hero);var elite=stats.Clone();elite.Health*=1.2f;elite.Damage*=.35f;elite.Defense*=.7f;elite.CritChance=0;elite.HasProfessionKind=elite.HasCombatKind=true;elite.ProfessionKind=elite.CombatKind=SoldierKind.Melee;
            var scenario=new BattleScenario {Board=BaseSceneCombat.Project(state),CampaignRules=true,FogEnabled=false,PlayerHeroAppearance=true,PlayerLeaderSlot=0,CustomExits=true,ExitA=new GridPosition(23,6),ExitB=new GridPosition(0,6),MaxRounds=90,InstantDeathEnabled=false,
                A=new ArmyDefinition {HeroCount=1,MeleeCount=0,RangedCount=0,CavalryCount=0,Hero=stats},
                B=new ArmyDefinition {HeroCount=2,MeleeCount=0,RangedCount=0,CavalryCount=0,Hero=elite,CommanderNames=new List<string>{"受伤精英追兵","受伤精英追兵"}},
                Objective=new ObjectiveConfig {Title="掩护撤离 · 追兵交战"},Placements=new List<UnitPlacement>()};
            var playerCell=new GridPosition((int)state.PlayerX,(int)state.PlayerY);
            var firstEnemy=new GridPosition(Math.Max(1,playerCell.X-6),5);var secondEnemy=new GridPosition(Math.Max(1,playerCell.X-7),8);
            foreach(var cell in new[]{playerCell,firstEnemy,secondEnemy})if(!scenario.Board.Walkable(cell))throw new InvalidOperationException("追兵或主角起点被阻挡");
            scenario.Board.Cell(playerCell).Zone=DeploymentZone.A;
            scenario.Board.Cell(firstEnemy).Zone=scenario.Board.Cell(secondEnemy).Zone=DeploymentZone.B;
            scenario.Placements.Add(new UnitPlacement {Side=BattleSide.A,Kind=SoldierKind.Hero,CommanderSlot=0,Cell=playerCell});
            scenario.Placements.Add(new UnitPlacement {Side=BattleSide.B,Kind=SoldierKind.Hero,CommanderSlot=0,Cell=firstEnemy});
            scenario.Placements.Add(new UnitPlacement {Side=BattleSide.B,Kind=SoldierKind.Hero,CommanderSlot=1,Cell=secondEnemy});
            var enemies=BaseDemoCombat.CreateEnemies(BattleId,scenario.B);BaseRpg.PrepareEnemies(state,enemies,BattleId);
            BaseSceneRules.Configure(state,scenario,new[]{player});
            var session=new BaseCampaignSession(new BaseCampaignBattle {Rpg=state.Demo?.Rpg?.Copy(),Enemies=enemies,Id=BattleId,EventId=BattleId,MapRegion=state.Campaign.Location,StartedDay=state.Day,Scenario=scenario,RealtimeRules=true,RealtimeRulesVersion=1,Leaders=new List<BaseLeader>{player}});
            session.Bind();foreach(var unit in session.Battle.Soldiers.Where(unit=>unit.Side==BattleSide.B))unit.Health=unit.Stats.Health*.5f;
            if(!session.Battle.Start(out string problem))throw new InvalidOperationException(problem);return session;
        }
        public static void Configure(RealtimeCampaignBattle run,Func<bool> villagersSafe)
        {
            run.DamageLimit=(source,target,damage)=>target.Side==PrototypeSide.Enemy&&run.Model.Units.Any(other=>other.Side==PrototypeSide.Enemy&&other.Id!=target.Id&&other.Health<=0) ? Math.Min(damage,Math.Max(0,target.Health-target.MaxHealth*WoundedFraction)) : damage;
            run.OutcomeResolver=current=>
            {
                var enemies=current.Model.Units.Where(unit=>unit.Side==PrototypeSide.Enemy).ToArray();
                if(villagersSafe()&&enemies.Count(unit=>unit.Health<=0)==1&&enemies.Any(unit=>unit.Health>0&&unit.Health<=unit.MaxHealth*WoundedFraction+.001))return BattleOutcome.SideAWin;
                return current.Model.Player.Health<=0||current.Model.Player.Escaped||current.Model.Elapsed>=90?BattleOutcome.SideBWin:BattleOutcome.None;
            };
        }
        public static bool Resolve(BaseState state,RealtimeCampaignBattle run,bool abandon=false)
        {
            if(!Pursuit(state)||state.SceneBattleId!=BattleId)return false;
            bool complete=!abandon&&run?.Battle.Phase==BattlePhase.Finished&&run.Battle.Report.Outcome==BattleOutcome.SideAWin&&Safe(state);
            if(!abandon&&run?.Battle.Phase!=BattlePhase.Finished)return false;
            if(complete)
            {
                var enemies=run.Model.Units.Where(unit=>unit.Side==PrototypeSide.Enemy).ToArray();
                if(enemies.Count(unit=>unit.Health<=0)!=1||!enemies.Any(unit=>unit.Health>0&&unit.Health<=unit.MaxHealth*WoundedFraction+.001))return false;
                state.Demo.Stage=BaseDemoStage.PrologueWounded;state.Demo.PrologueCompleted=true;
            }
            else
            {
                state.Demo.Stage=BaseDemoStage.PrologueEscape;state.Demo.PrologueAttempts++;
                state.PlayerX=20;state.PlayerY=7;state.Demo.SceneFollowers=Villagers(18.8);AddAhe(state);
            }
            state.SceneBattleId="";return true;
        }
        public static bool Recover(BaseState state)
        {
            if(!Village(state)||state.Demo.Stage!=BaseDemoStage.PrologueWounded||!state.Demo.PrologueCompleted)return false;
            state.Demo.Stage=BaseDemoStage.Waking;state.Demo.SceneFollowers.Clear();BaseDemo.InstallCampMap(state);return true;
        }
        public static void Validate(BaseState state)
        {
            var progress=state.Demo;
            if(progress.PrologueVersion==0)
            {if(Village(state)||progress.PrologueCompleted||progress.PrologueAttempts!=0||progress.SceneFollowers?.Count>0)throw new ArgumentException("序章记录缺版本");return;}
            if(progress.PrologueVersion!=1||progress.PrologueAttempts<0||progress.PrologueAttempts>100000||progress.SceneFollowers==null||progress.SceneFollowers.Count!=(progress.Stage==BaseDemoStage.PrologueEscape||Pursuit(state)||progress.Stage==BaseDemoStage.PrologueWounded?(BaseDemoIntro.Active(state)?3:2):progress.Stage==BaseDemoStage.PrologueBriefing&&BaseDemoIntro.Active(state)?(progress.Intro.ReporterArrived?3:2):0))throw new ArgumentException("序章版本、尝试次数或村民记录非法");
            if(Pursuit(state)!=(state.SceneBattleId==BattleId)||progress.PrologueCompleted!=(progress.Stage==BaseDemoStage.PrologueWounded||!Village(state)))throw new ArgumentException("序章战斗、完成与阶段状态不一致");
            if(progress.SceneFollowers.Select(person=>person?.Id).Distinct().Count()!=progress.SceneFollowers.Count||progress.SceneFollowers.Any(person=>person==null||!new[]{"demo_villager_1","demo_villager_2","prologue_ahe"}.Contains(person.Id)||person.Fate!="active"&&person.Fate!="escaped"||!BaseSpatial.Walkable(state,person.MapX,person.MapY)))throw new ArgumentException("序章村民身份或坐标非法");
            if(progress.Stage==BaseDemoStage.PrologueWounded&&!Safe(state))throw new ArgumentException("村民未离开，不能提交序章完成");
        }
    }
}

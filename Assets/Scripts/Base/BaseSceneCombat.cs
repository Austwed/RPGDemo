using System;
using System.Linq;
using System.Collections.Generic;
using BaseConquest.RealtimePrototype;
using BaseConquest.Battle;

namespace BaseConquest.Base
{
    public static class BaseSceneCombat
    {
        public static bool Active(BaseState state)=>state?.Campaign?.HasActiveBattle==true||!string.IsNullOrEmpty(state?.SceneBattleId);
        internal static bool BlocksSight(BaseMapObject item)=>item.Blocks&&!item.Id.StartsWith("tactic:");
        public static BattleBoard Project(BaseState state,string map=null)=>Project(BaseBattlefield.Board(state,map),BaseSpatial.Objects(state,map).Where(BlocksSight).ToArray(),BaseDemo.Active(state));
        internal static BattleBoard Project(BattleBoard source,IReadOnlyList<BaseMapObject> objects,bool demo)
        {
            var board=source.Clone();
            if(demo)foreach(var cell in board.Cells)cell.FixedCover=false;
            foreach(var position in board.Positions())
            {
                var placement=objects.FirstOrDefault(item=>item.Parts.Any(part=>part.Contains(position.X+.5,position.Z+.5)));
                if(placement==null)continue;
                var cell=board.Cell(position);cell.Terrain=placement.Id=="farm"||placement.AssetId?.StartsWith("farm")==true ? TerrainKind.Water : TerrainKind.Wall;cell.Zone=DeploymentZone.None;
                var decoration=BaseEnvironment.Decorative(placement)?BaseEnvironment.Find(placement.AssetId):null;if(decoration!=null){cell.HasSightHeight=true;cell.SightHeight=decoration.Sight;}
            }
            return board;
        }
        public static BattleBoard PersistedBoard(BaseState state,BattleBoard board,string key)
        {
            var source=BaseBattlefield.Board(state,key);var objects=BaseSpatial.Objects(state,key).Where(o=>o.Blocks&&!o.Id.StartsWith("tactic:")).ToArray();
            foreach(var p in board.Positions())if(objects.Any(o=>o.Parts.Any(part=>part.Contains(p.X+.5,p.Z+.5))))
            {var cell=board.Cell(p);var original=source.Cell(p);cell.Terrain=original.Terrain;cell.BuildingId=original.BuildingId;cell.Zone=original.Zone;cell.HasSightHeight=original.HasSightHeight;cell.SightHeight=original.SightHeight;}
            return board;
        }
        public static void Validate(BaseState state)
        {
            if(string.IsNullOrEmpty(state.SceneBattleId))return;
            if(state.SceneBattleId.Length>100||state.SceneBattleId.Any(character=>!char.IsLetterOrDigit(character)&&!":-_".Contains(character))||state.Campaign.HasActiveBattle)throw new ArgumentException("同图遭遇身份非法或与另一场战斗冲突");
        }
    }
}

namespace BaseConquest.Base
{
    public static class BaseDemoCombat
    {
        // Temporary Demo defaults; author balance data can replace these three baselines.
        public static int Baseline(SoldierKind kind,RpgRules rules=null)=>rules==null?(kind==SoldierKind.Melee?10:kind==SoldierKind.Ranged?8:4):rules.TroopBaseline[BaseRpg.Profession(kind)];
        // 2026-11-04 起普通兵即死率改用三兵种合计口径：编队不再改变死亡概率，只改变战术。
        public static int BaselineTotal(RpgRules rules=null)=>rules==null?22:rules.TroopBaseline.Sum();
        public static double Probability(int baseline,int count,RpgRules rules=null) =>
            Math.Max(0,Math.Min(1,RawProbability(baseline,count,rules)));

        public static double RawProbability(int baseline,int count,RpgRules rules=null)
        {
            if(baseline<0||count<0)throw new ArgumentOutOfRangeException();
            double center=rules?.SoldierDeathBase??.5,sensitivity=rules?.SoldierDeathSensitivity??1;
            return baseline+count==0?center:center-sensitivity*(double)(baseline-count)/(baseline+count);
        }
        public static double TroopDeathChance(int baseline,int count,RpgRules rules,double lethalModifier) =>
            Math.Max(0,Math.Min(1,RawProbability(baseline,count,rules)+lethalModifier));
        public static double DeathChance(BaseCampaignSession session,BattleSoldier unit)
        {
            if(unit.Kind==SoldierKind.Hero)
            {
                if(unit.Side==BattleSide.A)return 0;
                if(session.Opening.Rpg!=null&&unit.Side==BattleSide.B)
                {
                    if(session.Opening.EventId==BaseDemoDefense.EventId)return 1;
                    if(session.Opening.EventId==BaseDemoRuins.EventId&&session.Opening.RuinsFirstClear)return 0;
                    if(BaseDemoFinale.Encounter(session))return 1;
                    return Math.Max(0,Math.Min(1,session.Opening.Rpg.EnemyLeaderDeath+BaseRpg.LethalModifier(session,unit)));
                }
                return Math.Max(0,Math.Min(1,session.Opening.Difficulty.LeaderDeath+BaseRpg.LethalModifier(session,unit)));
            }
            var counts=session.Opening.PlayerTroopCounts;
            if(counts==null||counts.Length!=(session.Opening.Rpg?.HasProfessionCatalog==true ? 4 : 3)||counts.Any(count=>count<0))throw new InvalidOperationException("Demo兵力基准快照非法");
            // 每次判定只使用此前已完成的生死结果，当前伤员尚未计入。
            // 队长不计入 N，敌方队长倒地也不计入。
            int count=counts.Sum()-session.Battle.Soldiers.Count(other=>other.Side==BattleSide.A&&other.Kind!=SoldierKind.Hero&&other.InstantDead&&!other.DeveloperSpawned)
                +session.Battle.Soldiers.Count(other=>other!=unit&&other.Side==BattleSide.B&&other.Kind!=SoldierKind.Hero&&other.DeathResolved&&other.KnockedDown&&!other.InstantDead&&!other.Rescued&&!session.Battle.RuinsCivilianIds.Contains(other.Id)&&!other.Escaped&&!other.DeveloperSpawned);
            return TroopDeathChance(BaselineTotal(session.Opening.Rpg),Math.Max(0,count),session.Opening.Rpg,BaseRpg.LethalModifier(session,unit));
        }
        public static List<BaseEnemyMember> CreateEnemies(string eventId,ArmyDefinition army,BaseEnemyPreset preset=null)
        {
            var result=new List<BaseEnemyMember>();
            foreach(var kind in BattleSimulation.TroopKinds.Prepend(SoldierKind.Hero))
                for(int n=0;n<army.Count(kind);n++)
                {
                    var stats=(kind==SoldierKind.Hero?army.CommanderProfile(n):army.Stats(kind)).Clone();
                    var deployment=preset?.Units.Where(unit=>unit.Kind==kind).ElementAt(n).Copy();
                    result.Add(new BaseEnemyMember {Deployment=deployment,PresetSlot=kind==SoldierKind.Hero?n:-1,Id=eventId+":unit:"+result.Count,Kind=kind,CommanderSlot=kind==SoldierKind.Hero?n:n%army.HeroCount,
                        IsAlien=eventId==BaseDemoRuins.EventId&&kind==SoldierKind.Hero&&n==0,IsControlled=eventId==BaseDemoRuins.EventId&&kind==SoldierKind.Melee,
                        Name=kind==SoldierKind.Hero&&n<army.CommanderNames.Count?army.CommanderNames[n]:eventId==BaseDemoRuins.EventId&&kind==SoldierKind.Melee?"受控村民"+(n+1):BattleSimulation.KindName(kind),Stats=stats,Health=stats.Health,
                        // 队长立绘：随机入侵用已有队长形象；其余战斗留空走通用队长图。
                        Appearance=kind==SoldierKind.Hero&&n<army.CommanderAppearances.Count?army.CommanderAppearances[n]??"":"",
                        Equipment=kind==SoldierKind.Hero?new BasePersonalLoadout {Version=1,Weapon="sword",InitialWeapon="sword",BaseStats=stats.Clone()}:null});
                }
            return result;
        }
        public static void PersistEnemies(BaseState state,BaseCampaignEvent mission,BaseCampaignSession session,BaseCampaignReport report,bool win)
        {
            var remaining=new List<BaseEnemyMember>();
            for(int n=0;n<session.Opening.Enemies.Count;n++)
            {
                var original=session.Opening.Enemies[n];var unit=session.UnitForEnemy(n);var member=original.Copy();
                if(unit!=null)
                {
                    if(!original.Dead)report.EnemyDeployed++;
                    member.Health=(float)BaseSupply.UnboostedHealth(unit, original.Stats.Health);member.Dead=unit.InstantDead;member.KnockedDown=unit.KnockedDown;
                    if(member.Dead&&!original.Dead)report.EnemyLost++;
                    if(state.Aftermath.Prisoners.Any(prisoner=>prisoner.Id==session.Opening.Id+":"+unit.Id))report.EnemyCaptured++;
                }
                remaining.Add(member);
            }
            mission.EnemyArmy=remaining;mission.EnemyRecoveryDay=state.Day;
            if(!string.IsNullOrEmpty(mission.SourceSiteId))
            {
                var site=BaseThreats.Site(state,mission.SourceSiteId);report.SourceSiteId=site.Id;site.Garrison.Clear();
                if(win){site.Owner="player";site.CapturedDay=state.Day;}else site.Garrison.AddRange(remaining.Select(member=>member.Copy()));
            }
            report.EnemyReturned=win?0:remaining.Count(member=>!member.Dead);
        }
        public static void RecoverEnemies(BaseState state)
        {
            foreach(var mission in state.Campaign.Events.Where(item=>item.Status=="pending"&&item.EnemyArmy.Count>0&&item.EnemyRecoveryDay>0&&item.EnemyRecoveryDay<state.Day))
            {
                foreach(var member in mission.EnemyArmy.Where(member=>!member.Dead))
                {
                    member.Health=member.Health<member.Stats.Health*.5f?Math.Max(1,member.Stats.Health*.5f):member.Stats.Health;
                    member.KnockedDown=false;
                }
                mission.EnemyRecoveryDay=state.Day;
                if(!string.IsNullOrEmpty(mission.SourceSiteId))
                {var site=BaseThreats.Site(state,mission.SourceSiteId);site.Garrison=mission.EnemyArmy.Select(member=>member.Copy()).ToList();}
            }
        }
        public static bool Defeated(RealtimeCampaignBattle run)=>!run.Model.Units.Any(unit=>unit.Side==PrototypeSide.Ally&&unit.Alive);
        public static bool Fail(BaseState state,RealtimeCampaignBattle run)
        {
            if(run==null||!run.Session.Opening.DemoCombat||!state.Campaign.HasActiveBattle||state.Campaign.ActiveBattle.Id!=run.Session.Opening.Id)return false;
            if (run.Battle.Phase == BattlePhase.Fighting &&
                (run.HasBattlefieldExits || !run.RetreatAll())) return false;
            if(run.Battle.Report?.Outcome==BattleOutcome.SideAWin)return false;
            run.SaveCheckpoint(state);if(!BaseCampaign.Settle(state,run.Session))return false;
            if (run.Session.Opening.EventId == BaseDemoRuins.EventId && run.RuinsHalfHealthRetreat)
                state.Demo.RuinsHalfHealthRetreatUsed = true;
            if(run.Session.Opening.EventId==BaseDemoDefense.SecondEventId){state.Demo.Defense.SecondOutcome="failed";state.Aftermath.GameOver=true;state.Aftermath.Reason=BaseDemoDefense.SecondGameOverReason;}
            if(run.Session.Opening.EventId==BaseDemoDefense.EventId)
            {
                var mission=state.Campaign.Events.Single(item=>item.Id==BaseDemoDefense.EventId);
                if(state.Day>=1000000)throw new InvalidOperationException("日期已达上限，无法安排残军再次来袭");
                state.Demo.Defense.RetryDay=state.Day+1;mission.AvailableDay=mission.DeadlineDay=state.Day+1;mission.Status="pending";
            }
            foreach(var manifest in state.Aftermath.Loot.Where(item=>item.BattleId==run.Session.Opening.Id&&!item.Closed))
                if(BaseBattleLoot.CanFinish(state,manifest))BaseBattleLoot.Finish(state,manifest.BattleId);
            return true;
        }
        public static void RetreatPosition(BaseState state,bool defense)
        {
            if(defense){state.Campaign.Location="base";state.PlayerX=state.Campaign.BaseX;state.PlayerY=state.Campaign.BaseY;}
            else if(!BaseWorldJourney.Active(state)){var map=BaseSpatial.Layout(state);state.PlayerX=map.SpawnX;state.PlayerY=map.SpawnY;}
            BaseSpatial.RepairGridPositions(state);BaseAdventure.Arrive(state);
        }
    }
    [Serializable] public sealed class BaseDemoEncounterConfig
    {
        public int Version;
        public BaseEnemyPreset Ruins,Defense,Camp;
        public BaseDemoEncounterConfig Copy()=>new BaseDemoEncounterConfig{Version=Version,Ruins=Ruins?.Copy(),Defense=Defense?.Copy(),Camp=Camp?.Copy()};
        public void Validate()
        {
            if(Version==0)
            {if((Ruins?.Version??0)!=0||(Defense?.Version??0)!=0||(Camp?.Version??0)!=0)throw new ArgumentException("Demo编排缺版本");return;}
            if((Version!=1&&Version!=2)||Ruins?.Version!=2||Defense?.Version!=2||Version==1&&(Camp?.Version??0)!=0||Version==2&&Camp?.Version!=2)throw new ArgumentException("Demo编排版本与两场／三场逐兵预设不匹配");
            Ruins.Validate();Defense.Validate();if(Version==2)Camp.Validate();
            if(Ruins.Commanders.Count!=1||(Ruins.Commanders[0].Profession!=SoldierKind.Melee&&Ruins.Commanders[0].Profession!=SoldierKind.Mage)||Ruins.Commanders[0].Level!=1||Ruins.Melee!=2||Ruins.Ranged!=0||Ruins.Cavalry!=0||Ruins.Mage!=0)throw new ArgumentException("遗迹固定一名异族队长和两名受控近战村民，只编辑站位");
        }
        public static BaseEnemyPreset Preset(BaseState state,string eventId)
        {
            var encounters = state.Demo?.Encounters;
            if (encounters == null || encounters.Version == 0) return null;
            if (eventId != BaseDemoRuins.EventId) return eventId == BaseDemoDefense.EventId ? encounters.Defense : null;
            if (!BaseDemoRuins.RepeatMages(state)) return encounters.Ruins;
            // Keep the authored positions and command slots for the repeat encounter.
            var repeat = encounters.Ruins.Copy();
            repeat.Mage = repeat.Melee;
            repeat.Melee = 0;
            int number = 0;
            foreach (var unit in repeat.Units.Where(u => u.Kind == SoldierKind.Melee))
            {
                unit.Kind = SoldierKind.Mage;
                unit.AuthoredSlot ??= unit.Id;
                unit.Id = "mage-" + (++number);
            }
            return repeat;
        }
        public static void ConfigureArmy(BaseState state,string eventId,ArmyDefinition army)
        {
            var preset=Preset(state,eventId);if(preset==null)return;
            if(eventId==BaseDemoDefense.EventId)
            {
                preset.Apply(new BaseEnemySite{Capacity=army});
                army.CommanderNames=Enumerable.Range(0,army.HeroCount).Select(index=>"来袭队长"+(index+1)).ToList();
            }
            else {army.MeleeCount=preset.Melee;army.RangedCount=preset.Ranged;army.CavalryCount=preset.Cavalry;army.MageCount=preset.Mage;}
            army.Commanders=Enumerable.Range(0,army.HeroCount).Select(_=>new CommanderLoadout()).ToList();
            foreach(var unit in preset.Units.Where(unit=>unit.Kind!=SoldierKind.Hero))
            {var command=army.Commanders[unit.CommanderSlot];command.Set(unit.Kind,command.Count(unit.Kind)+1);}
        }
        public static void ValidateRoster(BaseState state,BaseCampaignEvent mission)
        {
            var preset=Preset(state,mission.Id);if(preset==null||mission.EnemyArmy.Count==0)return;
            if (mission.Id == BaseDemoRuins.EventId && mission.EnemyArmy.Any(u=>u.IsControlled))
                preset = state.Demo.Encounters.Ruins;
            if(mission.EnemyArmy.Count!=preset.Units.Count||mission.EnemyArmy.Any(unit=>!unit.HasDeployment||!preset.Units.Any(source=>source.Id==unit.Deployment.Id&&source.Kind==unit.Kind&&source.Cell==unit.Deployment.Cell&&source.CommanderSlot==unit.Deployment.CommanderSlot))||mission.EnemyArmy.Select(unit=>unit.Deployment.Id).Distinct().Count()!=mission.EnemyArmy.Count)throw new ArgumentException("Demo残军的逐兵身份或站位与本局配置不一致");
        }
    }

}

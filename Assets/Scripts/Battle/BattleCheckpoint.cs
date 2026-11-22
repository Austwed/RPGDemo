using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
namespace BaseConquest.Battle
{
    [Serializable] public sealed class BattleSquadScalar
    {
        public int Id,CommanderSlot,CommanderId,SkillReadyRound,SkillChargeRounds,LastSkillCastRound,MergedInto,ActualGuardRange,ActualAlertRange,ActualAttackRange;
        public string Name;
        public BattleSide Side;
        public SoldierKind Kind;
        public bool SkillQueued,GuardAssigned,GuardArrived,GuardAlert;
        public GridPosition GuardCenter,GuardRequestedCell;
        public SquadCommand IssuedCommand;
    }
    [Serializable] public sealed class BattleSquadCheckpoint
    {
        public BattleSquadScalar Scalar;
        public List<int> Threats, Targets, Supply;
        public List<GridPosition> Cells;
        private static BattleSquadScalar Scalars(BattleSquad q)
        {
            var x=new BattleSquadScalar(); foreach(var f in typeof(BattleSquadScalar).GetFields(BindingFlags.Public|BindingFlags.Instance)) f.SetValue(x,typeof(BattleSquad).GetField(f.Name).GetValue(q));return x;
        }
        public static BattleSquadCheckpoint Capture(BattleSquad q) => new BattleSquadCheckpoint {Scalar=Scalars(q),Threats=q.StrategyThreats.ToList(),Targets=q.GuardTargets.ToList(),Supply=q.SupplySlots.ToList(),Cells=q.GuardCells.ToList()};
        public BattleSquad Restore() {var q=new BattleSquad();foreach(var f in typeof(BattleSquadScalar).GetFields(BindingFlags.Public|BindingFlags.Instance))typeof(BattleSquad).GetField(f.Name).SetValue(q,f.GetValue(Scalar));q.StrategyThreats.UnionWith(Threats);q.GuardTargets.UnionWith(Targets);q.SupplySlots.UnionWith(Supply);q.GuardCells.UnionWith(Cells);return q;}
        public BattleSquadCheckpoint Copy()=>Capture(Restore());
    }
    [Serializable] public sealed class BattleVisionCheckpoint
    {
        public bool[] VisibleA,VisibleB,ExploredA,ExploredB;
        public int[] SeenA,SeenB;
        public List<int> IdsA,IdsB,DeadA,DeadB;
        public List<SoldierKind> KindsA,KindsB;
        public BattleVisionCheckpoint Copy()=>new BattleVisionCheckpoint {VisibleA=VisibleA.ToArray(),VisibleB=VisibleB.ToArray(),ExploredA=ExploredA.ToArray(),ExploredB=ExploredB.ToArray(),SeenA=SeenA.ToArray(),SeenB=SeenB.ToArray(),IdsA=IdsA.ToList(),IdsB=IdsB.ToList(),DeadA=DeadA.ToList(),DeadB=DeadB.ToList(),KindsA=KindsA.ToList(),KindsB=KindsB.ToList()};
    }
    public sealed partial class BattleVision
    {
        public BattleVisionCheckpoint Capture()=>new BattleVisionCheckpoint {VisibleA=visible[0].ToArray(),VisibleB=visible[1].ToArray(),ExploredA=explored[0].ToArray(),ExploredB=explored[1].ToArray(),SeenA=lastSeen[0].ToArray(),SeenB=lastSeen[1].ToArray(),IdsA=sightings[0].Keys.ToList(),IdsB=sightings[1].Keys.ToList(),KindsA=sightings[0].Values.ToList(),KindsB=sightings[1].Values.ToList(),DeadA=confirmedDeaths[0].ToList(),DeadB=confirmedDeaths[1].ToList()};
        public void Restore(BattleVisionCheckpoint c)
        {
            int size=battle.Board.Width*battle.Board.Height;
            if(c==null||new[] {c.VisibleA,c.VisibleB,c.ExploredA,c.ExploredB}.Any(x=>x==null||x.Length!=size)||c.SeenA?.Length!=size||c.SeenB?.Length!=size||c.IdsA==null||c.IdsB==null||c.KindsA==null||c.KindsB==null||c.IdsA.Count!=c.KindsA.Count||c.IdsB.Count!=c.KindsB.Count||c.DeadA==null||c.DeadB==null)throw new ArgumentException("战斗视野快照非法");
            Array.Copy(c.VisibleA,visible[0],size);Array.Copy(c.VisibleB,visible[1],size);Array.Copy(c.ExploredA,explored[0],size);Array.Copy(c.ExploredB,explored[1],size);Array.Copy(c.SeenA,lastSeen[0],size);Array.Copy(c.SeenB,lastSeen[1],size);
            for(int i=0;i<2;i++){sightings[i].Clear();var ids=i==0?c.IdsA:c.IdsB;var kinds=i==0?c.KindsA:c.KindsB;for(int j=0;j<ids.Count;j++)sightings[i].Add(ids[j],kinds[j]);confirmedDeaths[i].Clear();confirmedDeaths[i].UnionWith(i==0?c.DeadA:c.DeadB);}Revision++;
        }
    }
    [Serializable] public sealed class BattleCheckpoint
    {
        public int Version=1,Round,Commands,Abilities,NextId,IdleRounds;
        public string Summary;
        public bool Victory,HasReport;
        public BattlePhase Phase;
        public BattleOutcome Outcome;
        public BattleScenario Scenario;
        public List<BattleSoldier> Units=new List<BattleSoldier>();
        public List<BattleSquadCheckpoint> Squads=new List<BattleSquadCheckpoint>();
        public List<BattleEvent> Events=new List<BattleEvent>();
        public BattleVisionCheckpoint Vision;
        public int[] Captains,Cooldowns,QueuedHeroes;
        public bool[] SkillArmed;
        public GridPosition[] SkillCells;
        public BattleCheckpoint Copy()=>new BattleCheckpoint {Version=Version,Round=Round,Commands=Commands,Abilities=Abilities,NextId=NextId,IdleRounds=IdleRounds,Summary=Summary,Victory=Victory,HasReport=HasReport,Phase=Phase,Outcome=Outcome,Scenario=Scenario.Clone(),Units=Units.Select(x=>x.Copy()).ToList(),Squads=Squads.Select(x=>x.Copy()).ToList(),Events=Events.Select(x=>x.Copy()).ToList(),Vision=Vision.Copy(),Captains=Captains.ToArray(),Cooldowns=Cooldowns.ToArray(),QueuedHeroes=QueuedHeroes.ToArray(),SkillArmed=SkillArmed.ToArray(),SkillCells=SkillCells.ToArray()};
    }
    public sealed partial class BattleSimulation
    {
        public BattleCheckpoint Capture()=>new BattleCheckpoint {Round=RoundNumber,Commands=CommandsIssued,Abilities=AbilitiesUsed,NextId=nextId,IdleRounds=idleRounds,Summary=LastRoundSummary,Victory=VictoryDeclared,HasReport=Report!=null,Outcome=Report?.Outcome??BattleOutcome.None,Phase=Phase,Scenario=Scenario.Clone(),Units=Soldiers.Select(x=>x.Copy()).ToList(),Squads=Squads.Select(BattleSquadCheckpoint.Capture).ToList(),Events=Events.Select(x=>x.Copy()).ToList(),Vision=Vision.Capture(),Captains=captainSlots.ToArray(),Cooldowns=skillCooldowns.ToArray(),QueuedHeroes=queuedHeroes.ToArray(),SkillArmed=queuedSkills.Select(x=>x.HasValue).ToArray(),SkillCells=queuedSkills.Select(x=>x??new GridPosition()).ToArray()};
        public static BattleSimulation Restore(BattleCheckpoint c,bool configuredWine=false)
        {
            if(c==null||c.Version!=1||c.Round<0||c.Round>10000||!Enum.IsDefined(typeof(BattlePhase),c.Phase)||!Enum.IsDefined(typeof(BattleOutcome),c.Outcome)||c.Units==null||c.Units.Count>186||c.Squads==null||c.Squads.Count!=6||c.Events==null||c.Events.Count>160||c.Captains?.Length!=2||c.Cooldowns?.Length!=2||c.QueuedHeroes?.Length!=2||c.SkillArmed?.Length!=2||c.SkillCells?.Length!=2)throw new ArgumentException("战斗检查点版本／集合非法");
            BattleScenarioDocument.Validate(new BattleScenarioDocument {Scenario=c.Scenario});
            if(c.Units.Any(u=>u!=null&&(float.IsNaN(u.DrinkMultiplier)||float.IsInfinity(u.DrinkMultiplier)||(configuredWine?u.DrinkMultiplier<1||u.DrinkMultiplier>2.00001f:u.DrinkMultiplier!=1&&u.DrinkMultiplier!=1.05f&&u.DrinkMultiplier!=1.10f))))throw new ArgumentException("战斗酒类倍率非法");
            var b=new BattleSimulation(c.Scenario);
            if(c.Units.Any(u=>u==null||u.Stats==null||!u.Stats.EquipmentStatsValid||u.Id<0||u.DeveloperSpawned&&u.Kind==SoldierKind.Hero||!b.Board.Contains(u.Cell)||float.IsNaN(u.Health)||u.Health<0||u.Health>u.Stats.Health||u.KnockedDown&&(u.Health!=1||u.InstantDead||!u.DeathResolved)||!Enum.IsDefined(typeof(SoldierKind),u.Kind)||!Enum.IsDefined(typeof(BattleSide),u.Side)||float.IsNaN(u.ProvisionHealthBonus)||u.ProvisionHealthBonus<0||u.ProvisionHealthBonus>1||u.HasProvisionEffects&&(new[]{u.ProvisionAttackFactor,u.ProvisionWeaponBonus,u.ProvisionArmorBonus}.Any(x=>float.IsNaN(x)||float.IsInfinity(x)||x<0)||u.ProvisionAttackFactor>1))||c.Units.Select(u=>u.Id).Distinct().Count()!=c.Units.Count||c.Squads.Any(q=>q?.Scalar==null||q.Threats==null||q.Targets==null||q.Supply==null||q.Cells==null)||c.Squads.Select(q=>q.Scalar.Id).Distinct().Count()!=6||c.NextId<=c.Units.Select(u=>u.Id).DefaultIfEmpty(-1).Max())throw new ArgumentException("战斗检查点单位非法");
            if(b.Scenario.BanditHideout)
            {
                int squadId=b.SquadId(BattleSide.B,SoldierKind.Melee);
                var enemySquad=c.Squads[squadId].Scalar;
                if(c.Captains[(int)BattleSide.B]!=-1||enemySquad.Id!=squadId||enemySquad.Side!=BattleSide.B||enemySquad.CommanderSlot!=-1||
                    c.Squads.Any(q=>q.Scalar.Side==BattleSide.B&&(q.Scalar.CommanderId!=-1||q.Supply.Count!=0))||
                    c.Units.Any(u=>u.Side==BattleSide.B&&(u.Kind==SoldierKind.Hero||u.OriginCommanderSlot!=-1||u.SquadId!=squadId))||
                    c.Phase!=BattlePhase.Deployment&&TroopKinds.Any(kind=>c.Units.Count(u=>u.Side==BattleSide.B&&!u.DeveloperSpawned&&u.Kind==kind)!=b.Scenario.B.Count(kind)))
                    throw new ArgumentException("窝点敌军检查点与独立士兵编队不一致");
            }
            b.Soldiers.Clear();b.Soldiers.AddRange(c.Units.Select(u=>u.Copy()));b.Squads.Clear();b.Squads.AddRange(c.Squads.Select(q=>q.Restore()));
            foreach(var unit in b.Soldiers){var q=b.Squads.SingleOrDefault(q=>q.Id==unit.SquadId);if(q==null)throw new ArgumentException("单位队伍引用不存在");q.Soldiers.Add(unit);}
            b.RoundNumber=c.Round;b.CommandsIssued=c.Commands;b.AbilitiesUsed=c.Abilities;b.nextId=c.NextId;b.idleRounds=c.IdleRounds;b.Phase=c.Phase;b.VictoryDeclared=c.Victory;
            b.LastRoundSummary=c.Summary??"已恢复第"+c.Round+"轮";
            Array.Copy(c.Captains,b.captainSlots,2);Array.Copy(c.Cooldowns,b.skillCooldowns,2);Array.Copy(c.QueuedHeroes,b.queuedHeroes,2);for(int i=0;i<2;i++)b.queuedSkills[i]=c.SkillArmed[i]?(GridPosition?)c.SkillCells[i]:null;
            b.Events.Clear();b.Events.AddRange(c.Events.Select(x=>x.Copy()));b.Vision.Restore(c.Vision);
            if(c.HasReport){b.Report=new BattleReport {Outcome=c.Outcome};b.RefreshBattleReport();} b.InvalidateUndo();return b;
        }
    }
}

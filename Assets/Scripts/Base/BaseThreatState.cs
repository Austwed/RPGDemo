using System;
using System.Collections.Generic;
using System.Linq;
using BaseConquest.Battle;
namespace BaseConquest.Base
{
    [Serializable] public sealed class BaseEnemyCommanderPreset
    {
        public SoldierKind Profession;
        public int Level=1;
        public string TemplateId="",SkillId="";
        public bool FixedTraits;
        public List<RpgTrait> Traits=new List<RpgTrait>();
        public BaseEnemyCommanderPreset Copy(){var c=(BaseEnemyCommanderPreset)MemberwiseClone();c.Traits=Traits?.Select(t=>t.Copy()).ToList();return c;}
    }
    [Serializable] public sealed class BaseEnemyDeployment
    {
        public string Id;
        public SoldierKind Kind;
        public int CommanderSlot;
        public GridPosition Cell;
        public BaseEnemyDeployment Copy() => (BaseEnemyDeployment)MemberwiseClone();
    }
    [Serializable] public sealed class BaseEnemyPreset
    {
        public int Version;
        public List<BaseEnemyCommanderPreset> Commanders=new List<BaseEnemyCommanderPreset>();
        public int Melee,Ranged,Cavalry;
        public List<BaseEnemyDeployment> Units=new List<BaseEnemyDeployment>();
        public BaseEnemyPreset Copy()=>new BaseEnemyPreset {Version=Version,Commanders=Commanders?.Select(commander=>commander?.Copy()).ToList(),Melee=Melee,Ranged=Ranged,Cavalry=Cavalry,Units=Units?.Select(unit=>unit?.Copy()).ToList()};
        public void Validate()
        {
            if(Version==0&&(Units?.Count??0)==0&&(Commanders?.Count??0)==0&&Melee==0&&Ranged==0&&Cavalry==0)return;
            if((Version!=1&&Version!=2)||Version==1&&(Units?.Count??0)!=0||Commanders==null||Commanders.Count<1||Commanders.Count>3||Commanders.Any(commander=>commander==null||!BattleSimulation.TroopKinds.Contains(commander.Profession)||commander.Level<1||commander.Level>9)||new[]{Melee,Ranged,Cavalry}.Any(count=>count<0||count>30))throw new ArgumentException("敌营编制须为1～3名合法职业队长、1～9级及每兵种0～30人");
            if(Version==2)
            {
                var expected=new Dictionary<string,Tuple<SoldierKind,int>>();
                foreach(var kind in new[]{SoldierKind.Hero,SoldierKind.Melee,SoldierKind.Ranged,SoldierKind.Cavalry})
                {
                    string tag=kind==SoldierKind.Hero?"commander":kind.ToString().ToLowerInvariant();
                    int count=kind==SoldierKind.Hero?Commanders.Count:kind==SoldierKind.Melee?Melee:kind==SoldierKind.Ranged?Ranged:Cavalry;
                    for(int index=0;index<count;index++)expected.Add(tag+"-"+(index+1),Tuple.Create(kind,index));
                }
                if(Units==null||Units.Count!=expected.Count||Units.Any(unit=>unit==null||unit.Id==null||!expected.ContainsKey(unit.Id)||expected[unit.Id].Item1!=unit.Kind||unit.CommanderSlot<0||unit.CommanderSlot>=Commanders.Count||unit.Kind==SoldierKind.Hero&&unit.CommanderSlot!=expected[unit.Id].Item2||unit.Cell.X<0||unit.Cell.X>=32||unit.Cell.Z<0||unit.Cell.Z>=24)||Units.Select(unit=>unit.Id).Distinct().Count()!=Units.Count||Units.Select(unit=>unit.Cell).Distinct().Count()!=Units.Count)throw new ArgumentException("逐兵编排身份、归属、数量或格坐标非法");
            }
        }
        public void ValidateBoard(BattleBoard board)
        {
            Validate();
            if(Version==2&&Units.Any(unit=>!board.Walkable(unit.Cell)||board.Cell(unit.Cell).Zone!=DeploymentZone.B))throw new ArgumentException("敌营逐兵站位须为无阻挡B部署格");
        }
        public void Apply(BaseEnemySite site)
        {
            Validate();if(Version==0)return;var capacity=site.Capacity;
            capacity.HeroCount=Commanders.Count;capacity.MeleeCount=Melee;capacity.RangedCount=Ranged;capacity.CavalryCount=Cavalry;
            capacity.CommanderStats=Commanders.Select(commander=>{
                var stats=BaseTraining.Growth(capacity.Hero,commander.Level);var profession=capacity.Stats(commander.Profession);
                stats.HasCombatKind=stats.HasProfessionKind=true;stats.CombatKind=stats.ProfessionKind=commander.Profession;stats.Range=profession.Range;stats.MoveDistance=profession.MoveDistance;return stats;
            }).ToList();
        }
    }
    [Serializable] public sealed class BaseEnemyMember
    {
        public string Id, Name;
        public RpgCharacter Rpg;
        public SoldierKind Kind;
        public int CommanderSlot;
        public int PresetSlot=-1;
        public BaseEnemyDeployment Deployment;
        public bool HasDeployment => !string.IsNullOrEmpty(Deployment?.Id);
        public float Health;
        public bool KnockedDown,Dead;
        public bool IsAlien,IsAwakened,IsControlled;
        public SoldierStats Stats = new SoldierStats();
        public BasePersonalLoadout Equipment;
        public BaseEnemyMember Copy() => new BaseEnemyMember { Rpg=Rpg?.Copy(),Id=Id, Name=Name, Kind=Kind, CommanderSlot=CommanderSlot, PresetSlot=PresetSlot, Deployment=Deployment?.Copy(), Health=Health, KnockedDown=KnockedDown,Dead=Dead,IsAlien=IsAlien,IsAwakened=IsAwakened,IsControlled=IsControlled, Stats=Stats.Clone(),Equipment=Equipment?.Copy() };
    }
    [Serializable] public sealed class BaseEnemySite
    {
        public string Id="outpost", Name="荒野敌军据点", Owner="enemy", AttackEventId="outpost_demo_001";
        public int CapturedDay, NextDispatchDay, LastReinforcementDay, WaveNumber, NextUnitNumber=1;
        public int DispatchInterval=4, TravelDays=2, ReinforcementInterval=2, RecoveryPerDay=20;
        public int RaidMelee=3, RaidRanged=1, RaidCavalry;
        public ArmyDefinition Capacity = new ArmyDefinition { HeroCount=2, MeleeCount=6, RangedCount=3, CavalryCount=1, Capacity=90 };
        public List<BaseEnemyMember> Garrison = new List<BaseEnemyMember>();
        public BaseEnemySite Copy() => new BaseEnemySite { Id=Id, Name=Name, Owner=Owner, AttackEventId=AttackEventId, CapturedDay=CapturedDay,
            NextDispatchDay=NextDispatchDay, LastReinforcementDay=LastReinforcementDay, WaveNumber=WaveNumber, NextUnitNumber=NextUnitNumber,
            DispatchInterval=DispatchInterval, TravelDays=TravelDays, ReinforcementInterval=ReinforcementInterval, RecoveryPerDay=RecoveryPerDay,
            RaidMelee=RaidMelee, RaidRanged=RaidRanged, RaidCavalry=RaidCavalry, Capacity=Capacity.Clone(), Garrison=Garrison.Select(u=>u.Copy()).ToList() };
    }
    [Serializable] public sealed class BaseEnemyBinding
    {
        public int SoldierId, MemberIndex;
        public BaseEnemyBinding Copy() => (BaseEnemyBinding)MemberwiseClone();
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using BaseConquest.Base;
using BaseConquest.RealtimePrototype;
namespace BaseConquest.Battle
{
    [Serializable] public sealed class RpgAttackPacket
    {
        public int Id,SourceId,TargetId,First,Second,Selected,Level,NetAdvantage,HitPoints,EvasionPoints;
        public string ObjectId="";
        public bool Hit,Critical,Settled;
        public double Attack,CriticalMultiplier,ArriveAt;
        public PrototypePoint Origin,Destination;
        public RpgAttackPacket Copy()=>(RpgAttackPacket)MemberwiseClone();
    }
    public sealed partial class RealtimeCampaignBattle
    {
        private readonly List<RpgAttackPacket> rpgAttacks=new List<RpgAttackPacket>();
        private int rpgAttackSerial;
        private RpgRules Rpg=>Session.Opening.Rpg;
        private string CombatDifficulty => hideoutDifficulty ?? Session.Opening.Difficulty.Preset;
        private double DifficultyIncoming(BattleSide side)=>!Session.Opening.DemoCombat||side!=BattleSide.A?1:CombatDifficulty=="easy"?.5:CombatDifficulty=="hard"?2:1;
        private RpgCharacter Profile(PrototypeUnit actor)=>Battle.Soldier(actor.Id-1).Rpg?.Version==1?Battle.Soldier(actor.Id-1).Rpg:null;
        private void InitializeRpgCombat()
        {
            if(Rpg==null)return;
            if(Session.Opening.HasRealtimeState)
            {
                rpgAttackSerial=Session.Opening.RealtimeState.RpgAttackSerial;
                if(Session.Opening.RealtimeState.RpgAttacks==null||Session.Opening.RealtimeState.RpgAttacks.Any(a=>a==null))throw new ArgumentException("缺少RPG攻击记录");
                rpgAttacks.AddRange(Session.Opening.RealtimeState.RpgAttacks.Select(a=>a.Copy()));
                ValidateRpgPackets();
            }
            Model.WindupDuration=actor=>RpgCombatRules.Interval(Rpg.BaseInterval,(Profile(actor)?.Trait("frenzy")??0)/100+(RpgAffected(actor,"rapid_fire")?Rpg.Skills.Single(s=>s.Id=="rapid_fire").Amount:0),Rpg.MinimumInterval);
            Model.AttackTriggered=actor=>
            {
                var target=Model.Units.First(u=>u.Id==actor.WindupTarget);LaunchRpgAttack(actor,target,1);
            };
            var previous=Model.AfterStep;Model.AfterStep=()=>{previous?.Invoke();SettleRpgAttacks();};
        }
        private void ValidateRpgPackets()
        {
            if(rpgAttackSerial<0||rpgAttacks.Count>100000||rpgAttacks.Select(a=>a.Id).Distinct().Count()!=rpgAttacks.Count)throw new ArgumentException("RPG攻击序列非法");
            foreach(var a in rpgAttacks)
            {
                if(a==null||a.Id<1||a.Id>rpgAttackSerial||!Model.Units.Any(u=>u.Id==a.SourceId)||!new[]{a.Attack,a.CriticalMultiplier,a.ArriveAt,a.Origin.X,a.Origin.Z,a.Destination.X,a.Destination.Z}.All(PrototypeArena.Finite)||a.Attack<0||a.CriticalMultiplier<1||a.CriticalMultiplier>10||a.ArriveAt<0||a.Level<1||a.Level>9)throw new ArgumentException("RPG攻击快照非法");
                if(!string.IsNullOrEmpty(a.ObjectId)){if(!a.Settled||!a.Hit||a.Critical||a.First!=0||a.Second!=0||!Session.Opening.TacticalObjects.Any(o=>o.Id==a.ObjectId))throw new ArgumentException("物件攻击记录非法");continue;}
                if(!Model.Units.Any(u=>u.Id==a.TargetId&&u.Side!=Model.Units.First(source=>source.Id==a.SourceId).Side)||Math.Abs(a.NetAdvantage)>1)throw new ArgumentException("原目标或优势记录非法");
                var roll=new RpgAttackRoll(a.HitPoints,a.EvasionPoints,a.NetAdvantage>0?1:0,a.NetAdvantage<0?1:0,a.First,a.Second);
                if(roll.Selected!=a.Selected||roll.Hit!=a.Hit||roll.Critical!=a.Critical)throw new ArgumentException("骰子与已锁定结果不一致");
            }
        }
        private RpgAttackRoll RollRpg(PrototypeUnit source,PrototypeUnit target,int id)
        {
            int advantage=(int)(Profile(source)?.Trait("advantage")??0),disadvantage=(int)(Profile(source)?.Trait("disadvantage")??0);
            if(!source.Ranged&&target.Ranged)advantage++;
            double height=(Battle.Board.SurfaceLevel(source.Position.X,source.Position.Z)-Battle.Board.SurfaceLevel(target.Position.X,target.Position.Z))*PrototypeArena.Subdivision;
            if(source.Ranged&&height>=Rpg.HeightAdvantage)advantage++;
            if(source.Ranged&&height<=-Rpg.HeightAdvantage)disadvantage++;
            var s=Battle.Soldier(source.Id-1);var t=Battle.Soldier(target.Id-1);
            if(s.ProfessionKind==SoldierKind.Cavalry&&Battle.Board.Cell(new GridPosition((int)source.Position.X,(int)source.Position.Z)).Terrain==TerrainKind.Swamp)disadvantage++;
            int first=1+(int)(BattleSimulation.StableRoll(Battle.Scenario.RandomSeed,id,401)*10);
            int second=advantage==disadvantage?0:1+(int)(BattleSimulation.StableRoll(Battle.Scenario.RandomSeed,id,402)*10);
            return new RpgAttackRoll((int)Rpg.Hit[BaseRpg.Profession(s.ProfessionKind)],(int)Rpg.Evasion[BaseRpg.Profession(t.ProfessionKind)],advantage,disadvantage,first,second);
        }
        private void LaunchRpgAttack(PrototypeUnit source,PrototypeUnit target,double multiplier)
        {
            Model.NotifyAttack(source,target);int id=++rpgAttackSerial;var roll=RollRpg(source,target,id);var soldier=Battle.Soldier(source.Id-1);
            var packet=new RpgAttackPacket {Id=id,SourceId=source.Id,TargetId=target.Id,First=roll.First,Second=roll.Second,Selected=roll.Selected,Hit=roll.Hit,Critical=roll.Critical,NetAdvantage=roll.NetAdvantage,HitPoints=(int)Rpg.Hit[BaseRpg.Profession(soldier.ProfessionKind)],EvasionPoints=(int)Rpg.Evasion[BaseRpg.Profession(Battle.Soldier(target.Id-1).ProfessionKind)],
                Attack=Model.DamageFor(source)*multiplier*ConsumeCharge(source),Level=soldier.Stats.Level,CriticalMultiplier=soldier.Stats.CritMultiplier,Origin=source.Position,Destination=target.BodyCenter,
                ArriveAt=Model.Elapsed+(source.Ranged?(target.BodyCenter-source.Position).Length/(Rpg.ArrowSpeed/PrototypeArena.Subdivision):0)};
            rpgAttacks.Add(packet);
            if(source.Ranged)Model.AddVisualArrow(source,packet.Destination,Rpg.ArrowSpeed/PrototypeArena.Subdivision);
            SettleRpgAttacks();
        }
        private double RpgDamage(double raw,int level,double critical,BattleSoldier attacker,BattleSoldier defender)
        {
            double received=1+((defender.Rpg?.Trait("fragile")??0)-(defender.Rpg?.Trait("tough")??0))/100;
            double damage=RpgCombatRules.Damage(raw,defender.Stats.Defense,Rpg.ArmorK*level,Rpg.ArmorCap,critical,received);
            if(Session.Opening.DemoCombat)
            {
                string preset=CombatDifficulty;
                if(preset=="easy")damage*=attacker.Side==BattleSide.A?2:1;
                damage*=DifficultyIncoming(defender.Side);
            }
            return damage;
        }
        private void SettleRpgAttacks()
        {
            if(Battle.Phase!=BattlePhase.Fighting)return;
            foreach(var packet in rpgAttacks.Where(a=>!a.Settled&&a.ArriveAt<=Model.Elapsed+1e-9).OrderBy(a=>a.ArriveAt).ThenBy(a=>a.Id))
            {
                if(Battle.Phase!=BattlePhase.Fighting)break;
                packet.Settled=true;var target=Model.Units.First(u=>u.Id==packet.TargetId);if(!target.Alive)continue;
                if(!packet.Hit){Battle.RecordRpgMiss(packet.SourceId-1,packet.TargetId-1,packet.Selected);continue;}
                var source=Model.Units.First(u=>u.Id==packet.SourceId);var defender=Battle.Soldier(target.Id-1);
                Battle.Soldier(source.Id-1).LastAttackCritical=packet.Critical;
                double damage=defender.DeveloperInvulnerable?0:RpgDamage(packet.Attack,packet.Level,packet.Critical?packet.CriticalMultiplier:1,Battle.Soldier(source.Id-1),defender);
                Model.ApplyResolvedHit(source,target,DamageLimit?.Invoke(source,target,damage)??damage);
            }
        }
        private void SaveRpgCombat(RealtimeBattleState state)
        {state.RpgAttackSerial=rpgAttackSerial;state.RpgAttacks=rpgAttacks.Select(a=>a.Copy()).ToList();}
        public IReadOnlyList<RpgAttackPacket> RpgAttackHistory=>rpgAttacks;
        public double RecordRpgObjectAttack(PrototypeUnit source,string objectId,PrototypePoint destination)
        {
            double attack=Model.DamageFor(source)*ConsumeCharge(source);
            rpgAttacks.Add(new RpgAttackPacket {Id=++rpgAttackSerial,SourceId=source.Id,TargetId=-1,ObjectId=objectId,Hit=true,Settled=true,Attack=attack,CriticalMultiplier=Battle.Soldier(source.Id-1).Stats.CritMultiplier,Level=Battle.Soldier(source.Id-1).Stats.Level,Origin=source.Position,Destination=destination,ArriveAt=Model.Elapsed});
            return attack;
        }
    }
}

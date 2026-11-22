using System;
using System.Collections.Generic;
using System.Linq;
using BaseConquest.Base;
using BaseConquest.RealtimePrototype;
namespace BaseConquest.Battle
{
    [Flags]
    public enum RpgShotContext
    {
        None = 0,
        MovingCavalry = 1,
        MeleePressure = 2
    }

    [Serializable] public sealed class RpgAttackPacket
    {
        public int Id,SourceId,TargetId,First,Second,Selected,Level,NetAdvantage,HitPoints,EvasionPoints;
        public string ObjectId="",DamageType;
        public bool Hit,Critical,Settled,ImpactApplied;
        public RpgShotContext Context;
        public float DamageApplied, HealthAfter;
        public bool MageSplashEligible, HasImpactPosition;
        public int SplashTargetsHit;
        public float SplashDamageApplied;
        public double Attack,CriticalMultiplier,LaunchedAt,ArriveAt;
        public PrototypePoint Origin,Destination,ImpactPosition;
        public RpgAttackPacket Copy()=>(RpgAttackPacket)MemberwiseClone();
    }
    public sealed partial class RealtimeCampaignBattle
    {
        private readonly List<RpgAttackPacket> rpgAttacks=new List<RpgAttackPacket>();
        private int rpgAttackSerial;
        internal bool MageSplashEnabled { get; set; } = true;
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
                if(a==null||a.Id<1||a.Id>rpgAttackSerial||!Model.Units.Any(u=>u.Id==a.SourceId)||!new[]{a.Attack,a.CriticalMultiplier,a.LaunchedAt,a.ArriveAt,a.DamageApplied,a.HealthAfter,a.Origin.X,a.Origin.Z,a.Destination.X,a.Destination.Z}.All(PrototypeArena.Finite)||a.Attack<0||a.CriticalMultiplier<1||a.CriticalMultiplier>10||a.LaunchedAt<0||a.ArriveAt<a.LaunchedAt||a.Level<1||a.Level>9)throw new ArgumentException("RPG攻击快照非法");
                if(a.DamageApplied<0||a.HealthAfter<0||(a.Context & ~(RpgShotContext.MovingCavalry|RpgShotContext.MeleePressure))!=0)throw new ArgumentException("攻击结算快照非法");
                if(a.ImpactApplied&&(!a.Settled||!a.Hit||!string.IsNullOrEmpty(a.ObjectId)))throw new ArgumentException("攻击包的命中表现与结算不符");
                if(a.HasImpactPosition&&(!a.ImpactApplied||a.DamageType!="magic"||Model.Arena.CellAt(a.ImpactPosition)<0))
                    throw new ArgumentException("法术命中位置非法");
                if(!PrototypeArena.Finite(a.SplashDamageApplied) || a.SplashDamageApplied<0 || a.SplashTargetsHit<0 || a.SplashTargetsHit>8 ||
                    (a.SplashDamageApplied>0)!=(a.SplashTargetsHit>0) || a.SplashTargetsHit>0&&(!a.ImpactApplied||!a.MageSplashEligible))
                    throw new ArgumentException("法师溅射结算快照非法");
                if(Rpg.HasProfessionCatalog&&a.DamageType!="physical"&&a.DamageType!="magic")throw new ArgumentException("攻击包伤害类型非法");
                if(!string.IsNullOrEmpty(a.ObjectId)){if(!a.Settled||!a.Hit||a.Critical||a.First!=0||a.Second!=0||!Session.Opening.TacticalObjects.Any(o=>o.Id==a.ObjectId))throw new ArgumentException("物件攻击记录非法");continue;}
                if(!Model.Units.Any(u=>u.Id==a.TargetId&&u.Side!=Model.Units.First(source=>source.Id==a.SourceId).Side)||Math.Abs(a.NetAdvantage)>1)throw new ArgumentException("原目标或优势记录非法");
                var roll=new RpgAttackRoll(a.HitPoints,a.EvasionPoints,a.NetAdvantage>0?1:0,a.NetAdvantage<0?1:0,a.First,a.Second);
                if(roll.Selected!=a.Selected||roll.Hit!=a.Hit||roll.Critical!=a.Critical)throw new ArgumentException("骰子与已锁定结果不一致");
            }
        }
        // Both tests sample the world at release, before the attack packet enters flight.
        private RpgShotContext ShotContext(PrototypeUnit source, PrototypeUnit target)
        {
            if (!source.Ranged) return RpgShotContext.None;
            var context = target.Moving && Battle.Soldier(target.Id - 1).ProfessionKind == SoldierKind.Cavalry
                ? RpgShotContext.MovingCavalry : RpgShotContext.None;
            foreach (var enemy in Model.Units)
            {
                if (!enemy.Alive || enemy.Side == source.Side || enemy.Ranged || enemy.Order == PrototypeOrder.Retreat) continue;
                if (!Model.InAttackRadius(enemy, source)) continue;
                if (!(Model.CanTarget?.Invoke(enemy, source) ?? true) || !Model.ClearAttack(enemy.Position, source.BodyCenter)) continue;
                return context | RpgShotContext.MeleePressure;
            }
            return context;
        }
        private RpgAttackRoll RollRpg(PrototypeUnit source,PrototypeUnit target,int id,RpgShotContext context, bool physicalArrow)
        {
            int advantage=(int)(Profile(source)?.Trait("advantage")??0),disadvantage=(int)(Profile(source)?.Trait("disadvantage")??0);
            if(!source.Ranged&&target.Ranged)advantage++;
            double height=(Battle.Board.SurfaceLevel(source.Position.X,source.Position.Z)-Battle.Board.SurfaceLevel(target.Position.X,target.Position.Z))*PrototypeArena.Subdivision;
            if(source.Ranged&&height>=Rpg.HeightAdvantage)advantage++;
            if(source.Ranged&&height<=-Rpg.HeightAdvantage)disadvantage++;
            var s=Battle.Soldier(source.Id-1);var t=Battle.Soldier(target.Id-1);
            if(s.ProfessionKind==SoldierKind.Cavalry&&Battle.Board.Cell(new GridPosition((int)source.Position.X,(int)source.Position.Z)).Terrain==TerrainKind.Swamp)disadvantage++;
            if (physicalArrow)
            {
                if ((context & RpgShotContext.MovingCavalry) != 0) disadvantage++;
                if ((context & RpgShotContext.MeleePressure) != 0) disadvantage++;
            }
            int first=1+(int)(BattleSimulation.StableRoll(Battle.Scenario.RandomSeed,id,401)*10);
            int second=advantage==disadvantage?0:1+(int)(BattleSimulation.StableRoll(Battle.Scenario.RandomSeed,id,402)*10);
            return new RpgAttackRoll((int)Rpg.Hit[BaseRpg.Profession(s.ProfessionKind)],(int)Rpg.Evasion[BaseRpg.Profession(t.ProfessionKind)],advantage,disadvantage,first,second);
        }
        private void LaunchRpgAttack(PrototypeUnit source,PrototypeUnit target,double multiplier,string skillType=null)
        {
            if(source.Side==PrototypeSide.Enemy && DefensePursuitReady)return;
            Model.NotifyAttack(source,target);int id=++rpgAttackSerial;var soldier=Battle.Soldier(source.Id-1);
            double charge=ConsumeCharge(source,out var chargeType);
            string damageType=Rpg.HasProfessionCatalog?(skillType??chargeType??Rpg.AttackDamageType(BaseRpg.Profession(soldier.ProfessionKind))):"legacy";
            var context = ShotContext(source,target);
            bool physicalArrow = soldier.ProfessionKind == SoldierKind.Ranged && source.Ranged && damageType != "magic";
            var roll = RollRpg(source,target,id,context,physicalArrow);
            var packet=new RpgAttackPacket {Id=id,SourceId=source.Id,TargetId=target.Id,Context=context,First=roll.First,Second=roll.Second,Selected=roll.Selected,Hit=roll.Hit,Critical=roll.Critical,NetAdvantage=roll.NetAdvantage,HitPoints=(int)Rpg.Hit[BaseRpg.Profession(soldier.ProfessionKind)],EvasionPoints=(int)Rpg.Evasion[BaseRpg.Profession(Battle.Soldier(target.Id-1).ProfessionKind)],
                DamageType=damageType,LaunchedAt=Model.Elapsed,Attack=Model.DamageFor(source)*multiplier*charge,Level=soldier.Stats.Level,CriticalMultiplier=soldier.Stats.CritMultiplier,Origin=source.Position,Destination=target.BodyCenter,
                ArriveAt=Model.Elapsed+(source.Ranged?(target.BodyCenter-source.Position).Length/(Rpg.ArrowSpeed/PrototypeArena.Subdivision):0)};
            packet.MageSplashEligible=MageSplashEnabled&&soldier.ProfessionKind==SoldierKind.Mage&&damageType=="magic"&&skillType==null&&chargeType==null;
            rpgAttacks.Add(packet);
            if(source.Ranged&&damageType!="magic")Model.AddVisualArrow(source,packet.Destination,Rpg.ArrowSpeed/PrototypeArena.Subdivision);
            SettleRpgAttacks();
        }
        private double RpgDamage(string damageType,double raw,int level,double critical,BattleSoldier attacker,BattleSoldier defender)
        {
            double received=1+((defender.Rpg?.Trait("fragile")??0)-(defender.Rpg?.Trait("tough")??0))/100;
            double difficulty=DifficultyIncoming(defender.Side);
            if(Session.Opening.DemoCombat&&CombatDifficulty=="easy"&&attacker.Side==BattleSide.A)difficulty*=2;
            return RpgCombatRules.Damage(damageType??"legacy",raw,defender.Stats.Defense,Rpg.ArmorK*level,Rpg.ArmorCap,critical,received,difficulty);
        }
        private void SettleRpgAttacks()
        {
            if(Battle.Phase!=BattlePhase.Fighting)return;
            foreach(var packet in rpgAttacks.Where(a=>!a.Settled&&a.ArriveAt<=Model.Elapsed+1e-9).OrderBy(a=>a.ArriveAt).ThenBy(a=>a.Id))
            {
                if(Battle.Phase!=BattlePhase.Fighting)break;
                packet.Settled=true;var target=Model.Units.First(u=>u.Id==packet.TargetId);
                packet.HealthAfter=(float)target.Health;
                if(VictoryExitOpen || RuinsExitOpen || !target.Alive || target.Side==PrototypeSide.Ally && DefensePursuitReady)continue;
                if(!packet.Hit){Battle.RecordRpgMiss(packet.SourceId-1,packet.TargetId-1,packet.Selected);continue;}
                var source=Model.Units.First(u=>u.Id==packet.SourceId);var defender=Battle.Soldier(target.Id-1);
                Battle.Soldier(source.Id-1).LastAttackCritical=packet.Critical;
                double damage=defender.DeveloperInvulnerable?0:RpgDamage(packet.DamageType,packet.Attack,packet.Level,packet.Critical?packet.CriticalMultiplier:1,Battle.Soldier(source.Id-1),defender);
                double primaryDamage=DamageLimit?.Invoke(source,target,damage)??damage;
                bool splash=packet.MageSplashEligible&&primaryDamage>0;
                if(splash)damageBatch++;
                try
                {
                    if(packet.DamageType=="magic")
                    {
                        packet.ImpactPosition=target.Position;
                        packet.HasImpactPosition=true;
                    }
                    double before=target.Health;
                    Model.ApplyResolvedHit(source,target,primaryDamage);
                    packet.HealthAfter=(float)target.Health;
                    packet.DamageApplied=(float)Math.Max(0,before-target.Health);
                    packet.ImpactApplied=true;
                    if(splash)ApplyMageSplash(packet,source,target,primaryDamage/8);
                }
                finally {if(splash)damageBatch--;}
                if(splash)ResolveImmediateOutcome();
            }
        }
        private void ApplyMageSplash(RpgAttackPacket packet,PrototypeUnit source,PrototypeUnit target,double damagePerCell)
        {
            var impact=packet.ImpactPosition;
            var impactTile=new GridPosition((int)impact.X,(int)impact.Z);
            var arena=Model.Arena;
            int center=arena.CellAt(impact);
            if(center<0)return;
            int centerX=center%arena.FineColumns,centerZ=center/arena.FineColumns;
            var usedCells=new HashSet<int>();
            var attacker=Battle.Soldier(source.Id-1);
            attacker.LastAttackCritical=false;
            try
            {
                foreach(var victim in Model.Units.OrderBy(u=>u.Id))
                {
                    if(!victim.Alive||victim.Side==source.Side||victim==target)continue;
                    int cell=arena.CellAt(victim.Position);
                    if(cell<0||cell==center||Math.Abs(cell%arena.FineColumns-centerX)>1||Math.Abs(cell/arena.FineColumns-centerZ)>1||usedCells.Contains(cell))continue;
                    var victimTile=new GridPosition((int)victim.Position.X,(int)victim.Position.Z);
                    if(!Battle.Board.HasLineOfSight(impactTile,victimTile)||!Model.ClearAttack(impact,victim.Position))continue;
                    usedCells.Add(cell);
                    if(Battle.Soldier(victim.Id-1).DeveloperInvulnerable)continue;
                    double amount=Math.Min(damagePerCell,DamageLimit?.Invoke(source,victim,damagePerCell)??damagePerCell);
                    if(amount<=0)continue;
                    double before=victim.Health;
                    Model.ApplyResolvedHit(source,victim,amount);
                    float lost=(float)Math.Max(0,before-victim.Health);
                    if(lost<=0)continue;
                    packet.SplashTargetsHit++;
                    packet.SplashDamageApplied+=lost;
                }
            }
            finally {attacker.LastAttackCritical=packet.Critical;}
        }
        private void SaveRpgCombat(RealtimeBattleState state)
        {state.RpgAttackSerial=rpgAttackSerial;state.RpgAttacks=rpgAttacks.Select(a=>a.Copy()).ToList();}
        public IReadOnlyList<RpgAttackPacket> RpgAttackHistory=>rpgAttacks;
        public double RecordRpgObjectAttack(PrototypeUnit source,string objectId,PrototypePoint destination)
        {
            double attack=Model.DamageFor(source)*ConsumeCharge(source,out var chargeType);
            string damageType=chargeType??Rpg.AttackDamageType(BaseRpg.Profession(Battle.Soldier(source.Id-1).ProfessionKind));
            rpgAttacks.Add(new RpgAttackPacket {Id=++rpgAttackSerial,SourceId=source.Id,TargetId=-1,ObjectId=objectId,DamageType=damageType,LaunchedAt=Model.Elapsed,Hit=true,Settled=true,Attack=attack,CriticalMultiplier=Battle.Soldier(source.Id-1).Stats.CritMultiplier,Level=Battle.Soldier(source.Id-1).Stats.Level,Origin=source.Position,Destination=destination,ArriveAt=Model.Elapsed});
            return attack;
        }
    }
}

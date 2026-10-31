using System;
using System.Linq;
using BaseConquest.Base;
using BaseConquest.RealtimePrototype;
namespace BaseConquest.Battle
{
    public sealed partial class RealtimeCampaignBattle
    {
        private PrototypeUnit SkillActor(RealtimeCommanderSkill skill)=>Model.Units.FirstOrDefault(u=>(int)u.Side==(int)skill.Side&&u.Squad==skill.Slot&&u.Role==PrototypeRole.Leader);
        private bool RpgAffected(PrototypeUnit actor,string id)=>skills.Any(s=>s.Id==id&&s.ActiveUntil>Model.Elapsed+1e-9&&s.Recipients.Contains(actor.Id));
        private double RpgGearMovement(PrototypeUnit actor)
        {
            if(actor.Role!=PrototypeRole.Leader)return 0;
            var gear=actor.Side==PrototypeSide.Ally?Session.Opening.Leaders[actor.Squad].PersonalEquipment:Session.Opening.Enemies.FirstOrDefault(e=>e.Kind==SoldierKind.Hero&&e.CommanderSlot==actor.Squad)?.Equipment;
            return gear?.Items.Sum(i=>Rpg.EquipmentScale(BasePersonalEquipment.Definition(Rpg,i.Template).Movement,i.Quality))??0;
        }
        private void InitializeRpgSkills()
        {
            var saved=Session.Opening.HasRealtimeState?Session.Opening.RealtimeState.Skills:null;
            foreach(var actor in Model.Units.Where(u=>u.Role==PrototypeRole.Leader))
            {
                var profile=Profile(actor);if(profile==null)continue;
                var definition=Rpg.Skills.Single(s=>s.Id==profile.SkillId);
                var prior=saved?.SingleOrDefault(s=>(int)s.Side==(int)actor.Side&&s.Slot==actor.Squad);
                if(prior!=null&&(prior.Id!=profile.SkillId||prior.ReadyAt<0||prior.ActiveUntil<0||prior.Recipients.Any(id=>!Model.Units.Any(u=>u.Id==id&&u.Side==actor.Side))))throw new ArgumentException("人物技能检查点不一致");
                skills.Add(prior?.Copy()??new RealtimeCommanderSkill {Side=(BattleSide)(int)actor.Side,Slot=actor.Squad,Id=profile.SkillId,Auto=actor.Side==PrototypeSide.Enemy||profile.AutoSkill,ReadyAt=definition.Opening});
            }
            foreach(var skill in skills.Where(s=>s.Side==BattleSide.B))skill.Auto=true;
            Model.AttackPower=actor=>actor.Damage*(1+(RpgAffected(actor,"inspire")?Rpg.Skills.Single(s=>s.Id=="inspire").Amount:0));
            Model.MovementSpeed=actor=>(actor.MoveSpeed+RpgGearMovement(actor)/PrototypeArena.Subdivision+(RpgAffected(actor,"march")?Rpg.Skills.Single(s=>s.Id=="march").Amount/PrototypeArena.Subdivision:0))*(1+((Profile(actor)?.Trait("hasty")??0)-(Profile(actor)?.Trait("slow")??0))/100+(RpgAffected(actor,"charge")?Rpg.Skills.Single(s=>s.Id=="charge").SpeedBonus:0));
            Model.MovementCost=(actor,cell)=>RpgAffected(actor,"march")?1:Model.Arena.TravelCost?.Invoke(cell)??1;
            Model.AfterStep=TickRpgSkills;
            Model.TargetWeight=(actor,target)=>!actor.Ranged||!LegalSkillTarget(actor,target)?1:(Profile(target)?.Trait("taunt")??0)>0?Profile(target).Trait("taunt"):(Profile(target)?.Trait("inconspicuous")??0)>0?Profile(target).Trait("inconspicuous"):1;
        }
        private bool CanCastRpgSkill(BattleSide side,int slot)
        {
            var skill=Skill(side,slot);var actor=skill==null?null:SkillActor(skill);
            return Battle.Phase==BattlePhase.Fighting&&actor?.Alive==true&&actor.Order!=PrototypeOrder.Retreat&&skill.ReadyAt<=Model.Elapsed+1e-9&&skill.ActiveUntil<=Model.Elapsed+1e-9;
        }
        private bool LegalSkillTarget(PrototypeUnit actor,PrototypeUnit target,bool range=true)=>target?.Alive==true&&target.Side!=actor.Side&&(Model.CanTarget?.Invoke(actor,target)??true)&&(!range||RealtimeBattlePrototype.BodyGap(actor,target)<=Model.ReachFor(actor,target))&&Model.ClearAttack(actor.Position,target.BodyCenter);
        private bool CastRpgSkill(BattleSide side,int slot)
        {
            if(!CanCastRpgSkill(side,slot))return false;
            var skill=Skill(side,slot);
            var actor=SkillActor(skill);var d=Rpg.Skills.Single(s=>s.Id==skill.Id);
            if(skill.Id=="sweep"&&!Model.Units.Any(u=>LegalSkillTarget(actor,u,false)&&(u.BodyCenter-actor.BodyCenter).Length<=d.Radius/PrototypeArena.Subdivision))return false;
            skill.Recipients=Model.Units.Where(u=>u.Alive&&u.Side==actor.Side&&u.Squad==slot&&(skill.Id=="inspire"||skill.Id=="march"||skill.Id=="heal"||u.Id==actor.Id)).Select(u=>u.Id).ToList();
            skill.ReadyAt=Model.Elapsed+d.Cooldown;skill.ActiveUntil=Model.Elapsed+d.Duration;skill.NextHealAt=skill.Id=="heal"?Model.Elapsed+1:0;
            if(skill.Id=="charge"){skill.Charged=true;skill.ReadyAt=skill.ActiveUntil+d.Cooldown;}
            if(skill.Id=="sweep")
            {
                damageBatch++;
                try{foreach(var target in Model.Units.Where(u=>LegalSkillTarget(actor,u,false)&&(u.BodyCenter-actor.BodyCenter).Length<=d.Radius/PrototypeArena.Subdivision).ToArray())LaunchRpgAttack(actor,target,d.Amount);}
                finally{damageBatch--;}
            }
            Battle.RecordRpgSkill(actor.Id-1,d.Name,skill.Recipients.Count);ResolveImmediateVictory();return true;
        }
        private double ConsumeCharge(PrototypeUnit actor)
        {
            var skill=skills.FirstOrDefault(s=>s.Id=="charge"&&s.Charged&&s.ActiveUntil>Model.Elapsed&&SkillActor(s)?.Id==actor.Id);
            if(skill==null)return 1;skill.Charged=false;skill.ActiveUntil=Model.Elapsed;skill.ReadyAt=Model.Elapsed+Rpg.Skills.Single(s=>s.Id=="charge").Cooldown;return Rpg.Skills.Single(s=>s.Id=="charge").Amount;
        }
        private void TickRpgSkills()
        {
            foreach(var s in skills)
            {
                if(Battle.Phase!=BattlePhase.Fighting)return;
                if(s.Id=="heal")while(s.NextHealAt>0&&s.NextHealAt<=s.ActiveUntil+1e-9&&s.NextHealAt<=Model.Elapsed+1e-9)
                {
                    foreach(int id in s.Recipients)
                    {
                        var u=Model.Units.First(a=>a.Id==id);if(!u.Alive)continue;float amount=(float)Math.Min(Rpg.Skills.Single(d=>d.Id=="heal").Amount,u.MaxHealth-u.Health);
                        if(amount>0){u.Health+=amount;Battle.RecordRealtimeHealing(SkillActor(s).Id-1,id-1,amount,(float)u.Health);}
                    }
                    s.NextHealAt+=1;
                }
                var actor=SkillActor(s);if(actor?.Alive!=true||!s.Auto||actor.ManualOrder||actor.HitAt>=0&&!actor.HitResolved||!CanCastRpgSkill(s.Side,s.Slot))continue;
                var squad=Model.Units.Where(u=>u.Alive&&u.Side==actor.Side&&u.Squad==actor.Squad).ToArray();
                bool attack=squad.Any(u=>Model.Units.Any(t=>LegalSkillTarget(u,t)));
                bool trigger=s.Id=="inspire"?attack:s.Id=="march"?squad.Any(u=>u.Moving):s.Id=="heal"?squad.Any(u=>u.Health<u.MaxHealth*Rpg.HealThreshold):s.Id=="charge"?actor.MoveGoal>=0&&Model.Units.Any(t=>t.Id==actor.TargetId&&LegalSkillTarget(actor,t,false)):attack;
                if(trigger)CastRpgSkill(s.Side,s.Slot);
            }
            foreach(var actor in Model.Units)
            {
                var u=Battle.Soldier(actor.Id-1);int now=(int)Math.Floor(Model.Elapsed)+1;
                int Until(string id)=>(int)Math.Ceiling(skills.Where(s=>s.Id==id&&s.ActiveUntil>Model.Elapsed&&s.Recipients.Contains(actor.Id)).Select(s=>s.ActiveUntil).DefaultIfEmpty(0).Max());
                u.InspiredUntil=Until("inspire");u.MarchUntil=Until("march");u.RegenUntil=Until("heal");
            }
        }
        public bool AutoSkill(BattleSide side,int slot)=>Skill(side,slot)?.Auto??false;
        public string SkillId(BattleSide side,int slot)=>Rpg==null?null:Skill(side,slot)?.Id;
        public string SkillName(BattleSide side,int slot)=>Rpg==null?BattleSimulation.CommanderSkillName(slot):Rpg.Skills.FirstOrDefault(d=>d.Id==SkillId(side,slot))?.Name??"无技能";
        public bool SetAutoSkill(int slot,bool enabled)
        {var skill=Skill(BattleSide.A,slot);if(Rpg==null||skill==null)return false;skill.Auto=enabled;Profile(SkillActor(skill)).AutoSkill=enabled;Session.Opening.Leaders[slot].Rpg.AutoSkill=enabled;return true;}
        private string RpgSkillDescription(BattleSide side,int slot)
        {
            var skill=Skill(side,slot);if(skill==null)return "无主动技能";var d=Rpg.Skills.Single(s=>s.Id==skill.Id);
            string effect=d.Id=="inspire"?"本队攻击 +"+d.Amount.ToString("0.##%"):d.Id=="march"?"本队移速 +"+d.Amount+"细格／秒，免地形减速":d.Id=="heal"?"本队每秒恢复"+d.Amount+"生命，共"+Math.Floor(d.Duration)+"次":d.Id=="sweep"?"半径"+d.Radius+"细格，"+d.Amount+"倍攻击，逐目标D10":d.Id=="charge"?"移速 +"+d.SpeedBonus.ToString("0.##%")+"，下一击"+d.Amount+"倍":d.Id=="rapid_fire"?"自身攻速 +"+d.Amount.ToString("0.##%"):"效果 "+d.Amount;
            return d.Name+"："+effect+(d.Duration>0?"；持续"+d.Duration+"秒":"")+"；冷却"+d.Cooldown+"秒；"+("自动施放"+(skill.Auto?"开启":"关闭"))+(skill.ActiveUntil>Model.Elapsed?"\n效果剩余 "+(skill.ActiveUntil-Model.Elapsed).ToString("0.0")+"秒":"");
        }
    }
    public sealed partial class BattleSimulation
    {
        internal void RecordRpgMiss(int source,int target,int die)
        {AddEvent(new BattleEvent {SourceId=source,TargetId=target,Missed=true,Message=UnitName(Soldier(source))+"攻击"+UnitName(Soldier(target))+"：D10="+die+"，失手",SeenByA=Vision.CanSee(BattleSide.A,Soldier(source))||Vision.CanSee(BattleSide.A,Soldier(target)),SeenByB=Vision.CanSee(BattleSide.B,Soldier(source))||Vision.CanSee(BattleSide.B,Soldier(target))});}
        internal void RecordRpgSkill(int source,string name,int recipients)
        {AbilitiesUsed++;CommandsIssued++;Notice(UnitName(Soldier(source))+"施放"+name+"，影响本队"+recipients+"人",Soldier(source).Side,source);}
    }
}

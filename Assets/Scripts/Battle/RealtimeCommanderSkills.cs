using System;
using BaseConquest.Base;
using System.Collections.Generic;
using System.Linq;
using BaseConquest.RealtimePrototype;

namespace BaseConquest.Battle
{
    [Serializable] public sealed class RealtimeCommanderSkill
    {
        public BattleSide Side;
        public int Slot;
        public string Id;public bool Auto=true,Charged;
        public double ReadyAt,ActiveUntil,NextHealAt;
        public List<int> Recipients=new List<int>();
        public RealtimeCommanderSkill Copy()=>new RealtimeCommanderSkill {Id=Id,Auto=Auto,Charged=Charged,Side=Side,Slot=Slot,ReadyAt=ReadyAt,ActiveUntil=ActiveUntil,NextHealAt=NextHealAt,Recipients=Recipients.ToList()};
    }
    public sealed partial class RealtimeCampaignBattle
    {
        private readonly List<RealtimeCommanderSkill> skills=new List<RealtimeCommanderSkill>();
        private BattleEffectRules Effects=>Battle.Scenario.Effects;
        private RealtimeCommanderSkill Skill(BattleSide side,int slot)=>skills.FirstOrDefault(s=>s.Side==side&&s.Slot==slot);
        private bool Affected(PrototypeUnit actor,int slot)=>skills.Any(s=>s.Slot==slot&&s.ActiveUntil>Model.Elapsed+1e-9&&s.Recipients.Contains(actor.Id));
        private void InitializeSkills()
        {
            if(Rpg!=null){InitializeRpgSkills();return;}
            BattleEffectRules.Validate(Effects);
            var saved=Session.Opening.HasRealtimeState?Session.Opening.RealtimeState.Skills:null;
            if(saved?.Count>0)
            {
                if(saved.Count>6||saved.Any(s=>s==null||!Enum.IsDefined(typeof(BattleSide),s.Side)||s.Slot<0||s.Slot>2||Battle.Commander(s.Side,s.Slot)==null||
                    new[]{s.ReadyAt,s.ActiveUntil,s.NextHealAt}.Any(t=>!PrototypeArena.Finite(t)||t<0)||s.ReadyAt>Model.Elapsed+Effects.Cooldown+1e-6||s.ActiveUntil>Model.Elapsed+Math.Max(Effects.InspireDuration,Math.Max(Effects.MarchDuration,Effects.HealDuration))+1e-6||
                    s.Recipients==null||s.Recipients.Count>Model.Units.Count||s.Recipients.Distinct().Count()!=s.Recipients.Count||s.Recipients.Any(id=>!Model.Units.Any(u=>u.Id==id&&(int)u.Side==(int)s.Side&&u.Squad==s.Slot)))||saved.Select(s=>s.Side+":"+s.Slot).Distinct().Count()!=saved.Count)
                    throw new ArgumentException("即时技能检查点的身份、时间或受益队伍非法");
                skills.AddRange(saved.Select(s=>s.Copy()));
            }
            foreach(BattleSide side in Enum.GetValues(typeof(BattleSide)))for(int slot=0;slot<3;slot++)
                if(Battle.Commander(side,slot)!=null&&Skill(side,slot)==null)skills.Add(new RealtimeCommanderSkill {Side=side,Slot=slot,ReadyAt=Effects.OpeningCooldowns[slot]});
            Model.AttackPower=actor=>actor.Damage*(Affected(actor,0)?Effects.InspireMultiplier:1);
            Model.MovementSpeed=actor=>actor.MoveSpeed+(Affected(actor,1)?Effects.MarchSpeedBonus:0);
            Model.MovementCost=(actor,cell)=>Affected(actor,1)?1:Model.Arena.TravelCost?.Invoke(cell)??1;
            Model.AfterStep=TickSkills;
            UpdateSkillVisuals();
        }
        public double SkillCooldown(BattleSide side,int slot)=>Math.Max(0,(Skill(side,slot)?.ReadyAt??0)-Model.Elapsed);
        public float SkillCharge(BattleSide side,int slot)=>Skill(side,slot)==null?0:(float)Math.Max(0,1-SkillCooldown(side,slot)/(Rpg==null?Effects.Cooldown:Rpg.Skills.Single(d=>d.Id==Skill(side,slot).Id).Cooldown));
        public bool CanCastSkill(BattleSide side,int slot)
        {
            if(Rpg!=null)return CanCastRpgSkill(side,slot);
            var hero=Battle.Commander(side,slot);var actor=Model.Units.FirstOrDefault(u=>u.Id==hero?.Id+1);
            return Battle.Phase==BattlePhase.Fighting&&actor?.Alive==true&&actor.Order!=PrototypeOrder.Retreat&&Skill(side,slot)!=null&&SkillCooldown(side,slot)<1e-7;
        }
        public bool CastSkill(BattleSide side,int slot)
        {
            if(Rpg!=null)return CastRpgSkill(side,slot);
            if(!CanCastSkill(side,slot))return false;
            var skill=Skill(side,slot);var hero=Battle.Commander(side,slot);
            skill.ReadyAt=Model.Elapsed+Effects.Cooldown;skill.ActiveUntil=Model.Elapsed+(slot==0?Effects.InspireDuration:slot==1?Effects.MarchDuration:Effects.HealDuration);skill.NextHealAt=Model.Elapsed+1;
            skill.Recipients=Model.Units.Where(a=>a.Alive&&(int)a.Side==(int)side&&a.Squad==slot).Select(a=>a.Id).ToList();
            foreach(int id in skill.Recipients){var unit=Battle.Soldier(id-1);if(slot==2)unit.HealGainSerial++;else unit.BuffGainSerial++;}
            Battle.RecordRealtimeSkill(hero.Id,slot,skill.Recipients.Count);UpdateSkillVisuals();return true;
        }
        private void TickSkills()
        {
            foreach(var skill in skills.Where(s=>s.Slot==2))
                while(skill.NextHealAt>0&&skill.NextHealAt<=skill.ActiveUntil+1e-8&&skill.NextHealAt<=Model.Elapsed+1e-8)
                {
                    foreach(int id in skill.Recipients)
                    {
                        var actor=Model.Units.First(u=>u.Id==id);if(!actor.Alive)continue;
                        float amount=(float)Math.Min(Effects.HealPerRound,actor.MaxHealth-actor.Health);if(amount<=0)continue;
                        actor.Health+=amount;Battle.RecordRealtimeHealing(Battle.Commander(skill.Side,skill.Slot).Id,id-1,amount,(float)actor.Health);
                    }
                    skill.NextHealAt+=1;
                }
            UpdateSkillVisuals();
        }
        private void UpdateSkillVisuals()
        {
            foreach(var actor in Model.Units)
            {
                var unit=Battle.Soldier(actor.Id-1);int activeRound=(int)Math.Floor(Model.Elapsed+1e-8)+1;
                unit.InspiredUntil=Affected(actor,0)?activeRound:0;unit.MarchUntil=Affected(actor,1)?activeRound:0;unit.RegenUntil=Affected(actor,2)?activeRound:0;
                if(unit.MarchUntil>0)unit.WeakTerrainUntil=0;
            }
        }
        public string SkillDescription(int slot)=>SkillDescription(BattleSide.A,slot);
        public string SkillDescription(BattleSide side,int slot)=>Rpg!=null?RpgSkillDescription(side,slot):
            $"{BattleSimulation.CommanderSkillName(slot)} · {Battle.CommanderSkillEffect(slot, realtime:true)} · 冷却 {Effects.Cooldown} 秒，暂停不计时";
    }
    public sealed partial class BattleSimulation
    {
        internal void RecordRealtimeSkill(int source,int slot,int recipients)
        {
            AbilitiesUsed++;
            CommandsIssued++;
            var caster = Soldier(source);
            Notice($"{UnitName(caster)}施放{CommanderSkillName(slot)}，影响 {recipients} 人 · {CommanderSkillEffect(slot, realtime:true)}", caster.Side, source);
        }
        internal void RecordRealtimeHealing(int source,int target,float amount,float health)
        {Soldier(target).Health=health;Soldier(source).HealingDone+=amount;Notice(UnitName(Soldier(source))+"的治愈为"+UnitName(Soldier(target))+"恢复"+amount.ToString("0")+"生命",Soldier(target).Side,target,source);}
    }
}

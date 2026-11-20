using System;
using System.Collections.Generic;
using System.Linq;
using BaseConquest.Battle;
namespace BaseConquest.Base
{
    public static partial class BaseRpg
    {
        public static void ValidatePreset(BaseEnemyPreset preset,RpgRules rules,bool story=false)
        {
            if(preset==null||preset.Version==0)return;if(rules?.Version!=1)rules=null;preset.Validate();
            foreach(var unit in (preset.Units??new List<BaseEnemyDeployment>()).Where(u=>!string.IsNullOrEmpty(u.TemplateId)))
                if(rules==null||story||unit.Kind==SoldierKind.Hero||rules.TemplateIdentity(unit.TemplateId)!="soldier"||!rules.MatchesTemplate(unit.TemplateId,Profession(unit.Kind)))
                    throw new ArgumentException("逐兵预设须引用同职业普通兵模板："+unit.TemplateId);
            foreach(var authored in preset.Commanders)
            {
                bool custom=!string.IsNullOrEmpty(authored.TemplateId)||!string.IsNullOrEmpty(authored.SkillId)||authored.FixedTraits||authored.Traits?.Count>0;
                if(custom&&(rules==null||story))throw new ArgumentException("此运行包或剧情角色不接受RPG覆盖引用");
                if(rules==null)continue;
                var c=Character("authored",Profession(authored.Profession),false,story,rules,authored.Level);
                if(!string.IsNullOrEmpty(authored.TemplateId)){if(rules.TemplateIdentity(authored.TemplateId) is not ("captain_normal" or "captain_awakened"))throw new ArgumentException("队长须引用队长模板");c.TemplateId=authored.TemplateId;}
                if(!string.IsNullOrEmpty(authored.SkillId))c.SkillId=authored.SkillId;
                if(authored.FixedTraits)c.Traits=authored.Traits?.Select(t=>t?.Copy()).ToList();
                else if(authored.Traits?.Count>0)throw new ArgumentException("特性列表须明确使用固定特性");
                ValidateCharacter(c,rules);
            }
        }
        public static RpgCharacter Generate(string id,int profession,int level,RpgRules rules)
        {
            var random=new Random(unchecked(BaseAftermath.StableIdentity(id)^rules.GenerationSeed));
            var c=Character(id,profession,random.NextDouble()<rules.AwakenedChance,false,rules,level);
            void Pick()
            {
                var options=rules.Traits.Where(t=>!c.Traits.Any(chosen=>rules.Traits.Single(d=>d.Id==chosen.Id).Conflict==t.Conflict)).ToArray();
                if(options.Length==0)return;
                double at=random.NextDouble()*options.Sum(t=>t.Weight);var selected=options.Last();
                foreach(var item in options){at-=item.Weight;if(at<0){selected=item;break;}}
                c.Traits.Add(new RpgTrait {Id=selected.Id,Value=selected.Min+random.Next((int)Math.Round((selected.Max-selected.Min)/selected.Step)+1)*selected.Step});
            }
            int count=random.Next(4);for(int n=0;n<count;n++)Pick();
            var skills=rules.Skills.Where(s=>s.Profession<0||s.Profession==profession).ToArray();double cursor=random.NextDouble()*skills.Length;
            foreach(var skill in skills){cursor-=1;if(cursor<0){c.SkillId=skill.Id;break;}}
            return c;
        }
        public static void PrepareEnemies(BaseState state,IEnumerable<BaseEnemyMember> enemies,string eventId,bool applyWaveTemplates=true)
        {
            if(!Active(state))return;var r=state.Demo.Rpg;
            var members=enemies as List<BaseEnemyMember>??enemies.ToList();
            foreach(var e in members)
            {
                if(applyWaveTemplates && eventId==BaseDemoDefense.SecondEventId && e.Kind!=SoldierKind.Hero)
                    ApplyWaveTemplate(e,r);
                bool firstController=eventId==BaseDemoRuins.EventId&&state.Demo.RuinsDay==0&&e.Kind==SoldierKind.Hero&&e.CommanderSlot==0;
                string firstTemplate=firstController?(string.IsNullOrEmpty(r.RuinsFirstControllerTemplate)?RpgRules.Template("captain_awakened",0):r.RuinsFirstControllerTemplate):null;
                if(e.Rpg?.Version==1&&(!firstController||e.Rpg.TemplateId==firstTemplate)){e.Rpg.Rules=r;continue;}
                int p=Profession(e.Stats.HasProfessionKind?e.Stats.ProfessionKind:e.Stats.HasCombatKind?e.Stats.CombatKind:e.Kind==SoldierKind.Hero?SoldierKind.Melee:e.Kind);
                if(firstController&&r.HasProfessionCatalog)p=r.Professions.FindIndex(def=>def.Id==r.Templates.Single(t=>t.Id==firstTemplate).ProfessionId);
                bool story=eventId==BaseDemoPrologue.BattleId;
                e.Rpg=e.Kind==SoldierKind.Hero?(story?Character(e.Id,p,e.IsAlien||e.IsAwakened,true,r,e.Stats.Level):Generate(e.Id,p,e.Stats.Level,r)):
                    new RpgCharacter {Version=1,Id=e.Id,Profession=p,TemplateId=r.DefaultTemplateId("soldier",p),Rules=r};
                if(e.Kind!=SoldierKind.Hero&&!string.IsNullOrEmpty(e.Deployment?.TemplateId))
                {
                    var template=r.Templates.Single(t=>t.Id==e.Deployment.TemplateId&&t.Identity=="soldier"&&r.MatchesTemplate(t.Id,p));
                    e.Rpg.TemplateId=template.Id;
                    e.Name=template.Name;
                }
                var preset=(eventId==BaseDemoDefense.SecondEventId?state.Demo.Encounters?.Camp:null)??BaseDemoEncounterConfig.Preset(state,eventId)??state.Campaign.Layouts.FirstOrDefault(m=>m.Enemy!=null&&state.Campaign.EnemySites.Any(site=>site.AttackEventId==eventId&&site.Id==m.SourceMapId))?.Enemy;
                var authored=e.Kind==SoldierKind.Hero&&preset?.Commanders.Count>e.CommanderSlot?preset.Commanders[e.CommanderSlot]:null;
                if(eventId==BaseDemoRuins.EventId&&e.CommanderSlot==0)authored=null;
                if(authored!=null)
                {
                    if(!string.IsNullOrEmpty(authored.TemplateId)){if(r.TemplateIdentity(authored.TemplateId) is not ("captain_normal" or "captain_awakened"))throw new ArgumentException("队长须引用队长模板");e.Rpg.TemplateId=authored.TemplateId;}
                    if(!string.IsNullOrEmpty(authored.SkillId))e.Rpg.SkillId=authored.SkillId;
                    if(authored.FixedTraits)e.Rpg.Traits=authored.Traits.Select(t=>t.Copy()).ToList();
                    ValidateCharacter(e.Rpg,r);
                }
                if (eventId == BaseDemoRuins.EventId && state.Demo.RuinsDay > 0 && e.Kind == SoldierKind.Hero && e.Rpg.SkillId == "heal")
                    e.Rpg.SkillId = "march";
                if(firstController)
                {
                    e.IsAwakened=r.TemplateIdentity(firstTemplate)=="captain_awakened";
                    e.Rpg.TemplateId=firstTemplate;
                    e.Rpg.SkillId="inspire";
                    e.Rpg.Traits.Clear();
                    ValidateCharacter(e.Rpg,r);
                }
                double fraction=e.Stats.Health>0?e.Health/e.Stats.Health:0;
                e.Stats=r.Bare(e.Rpg,e.Rpg.Level);e.Health=e.Dead?0:e.KnockedDown?1:(float)(fraction*e.Stats.Health);
                if(e.Equipment!=null){e.Equipment.Weapon=e.Equipment.InitialWeapon=Weapon(p);e.Equipment.BaseStats=e.Stats.Clone();}
            }
            BaseEnemySupply.Assign(r,BaseEnemySupply.Scene(state,eventId),members);
        }
        // The attacking column can contain veterans in slots occupied by ordinary soldiers back in camp.
        // The wave owns a copy of the garrison. Casualties and remaining HP stay with the same member ID.
        private static void ApplyWaveTemplate(BaseEnemyMember member,RpgRules rules)
        {
            if(!member.HasDeployment)return;
            string slot=string.IsNullOrEmpty(member.Deployment.AuthoredSlot)?member.Deployment.Id:member.Deployment.AuthoredSlot;
            var row=rules.EncounterUnits?.Find(refUnit=>refUnit.Scene=="second" && refUnit.Slot==slot);
            if(row==null || member.Rpg?.TemplateId==row.TemplateId)return;
            var template=rules.Templates.Single(t=>t.Id==row.TemplateId && t.Identity=="soldier");
            if(!rules.MatchesTemplate(template.Id,Profession(member.Kind)))
                throw new ArgumentException("第二波精锐须与原出征士兵同职业："+slot);
            member.Deployment.TemplateId=template.Id;
            member.Rpg=null;
        }

        public static void BindBattle(BaseCampaignSession session)
        {
            var r=session.Opening.Rpg;if(r==null)return;
            foreach(var u in session.Battle.Soldiers)
            {
                if(u.DeveloperSpawned)continue;
                if(u.Rpg?.Version!=1)
                {
                    if(u.Side==BattleSide.A&&u.Kind==SoldierKind.Hero)u.Rpg=session.Opening.Leaders[u.OriginCommanderSlot].Rpg?.Copy();
                    else if(u.Side==BattleSide.B)
                    {
                        int i=session.Opening.Enemies.FindIndex(e=>session.UnitForEnemy(session.Opening.Enemies.IndexOf(e))?.Id==u.Id);
                        if(i>=0)u.Rpg=session.Opening.Enemies[i].Rpg?.Copy();
                    }
                    else u.Rpg=new RpgCharacter {Version=1,Id=session.Opening.Id+":soldier:"+u.Id,Profession=Profession(u.Kind),TemplateId=r.DefaultTemplateId("soldier",Profession(u.Kind))};
                }
                if(u.Rpg?.Version==1){u.Rpg.Rules=r;u.BattlePower=Power(u,r);}
            }
        }
    }
}

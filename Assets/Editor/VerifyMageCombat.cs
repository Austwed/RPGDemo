using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BaseConquest.Base;
using BaseConquest.Battle;
using BaseConquest.RealtimePrototype;
using UnityEngine;

namespace BaseConquest.Editor
{
    public static class VerifyMageCombat
    {
        private static void Need(bool condition,string why)
        {
            if(!condition)throw new InvalidOperationException("[P52 combat] "+why);
        }

        public static void Run()
        {
            var rules=DemoBalanceRuntime.Load().Rules;
            Need(rules.ProfessionCount==4&&rules.AttackDamageType(3)=="magic"&&rules.IsRanged(3),"法师职业");
            foreach(int p in new[]{0,1,2})Need(rules.AttackDamageType(p)=="physical","剑弓骑物理伤害");
            Need(RpgCombatRules.Damage("physical",12,4,8,.8,1)==8,"12攻4防");
            Need(RpgCombatRules.Damage("physical",12,12,8,.8,1)==1,"12攻12防");
            Need(Math.Abs(RpgCombatRules.Damage("magic",12,12,8,.8,1)-6.24)<1e-8,"法术减伤");
            Need(RpgCombatRules.Damage("physical",12,4,8,.8,1.5,.5,2)==12,"暴击与倍率");
            Need(RpgCombatRules.Damage("physical",12,30,8,.8,1,.5,.5)==1,"最终保底");
            Need(!new RpgAttackRoll(10,0,0,0,1).Hit,"自然一失手");

            var stats=new SoldierStats{Health=100,Damage=12,Defense=12,Level=1,CritMultiplier=1};
            var defender=new BattleSoldier{Kind=SoldierKind.Melee,Health=100,Stats=stats.Clone()};
            double Output(SoldierKind kind)
            {
                var source=new BattleSoldier{Kind=kind,Health=100,Stats=stats.Clone()};
                var altered=rules.Copy();
                altered.Hit[BaseRpg.Profession(kind)]=10;
                altered.Evasion[0]=0;
                return BasePower.Output(new List<BattleSoldier>{source},new List<BattleSoldier>{defender},altered);
            }
            Need(Output(SoldierKind.Mage)>Output(SoldierKind.Ranged)*4,"实际靶甲进入战力和扫荡预估");
            defender.DeveloperInvulnerable=true;
            Need(Output(SoldierKind.Mage)==0,"保护状态不可受伤");

            string raw=File.ReadAllText(Path.Combine(Application.streamingAssetsPath,"BaseScenarios",BaseDemo.BalanceTemplate));
            var state=DemoStageSaveTools.OpenFirstRuins(raw,"male",rules);
            var session=BaseDemoRuins.Begin(state,"none");
            var run=new RealtimeCampaignBattle(session);
            BaseDemoRuins.Configure(run);
            var galan=run.Battle.Commander(BattleSide.B,0);
            Need(galan.ProfessionKind==SoldierKind.Mage&&galan.Rpg.TemplateId=="captain_awakened_mage"&&galan.Rpg.SkillId=="inspire","首见伽兰身份技能");
            var caster=run.Model.Units.Single(a=>a.Id==galan.Id+1);
            Need(caster.Ranged&&caster.FootprintLength==1,"远程法师占地");
            run.Model.SetPaused(false);
            RpgAttackPacket pending=null;
            for(int tick=0;tick<5400&&run.Battle.Phase==BattlePhase.Fighting&&pending==null;tick++)
            {
                if(tick%30==0)
                    foreach(var ally in run.Model.Units.Where(a=>a.Alive&&a.Side==PrototypeSide.Ally&&a.Role==PrototypeRole.Leader))
                        run.Issue(new[]{ally.Id-1},run.Battle.PlayerCanSee(BattleSide.A,galan)?PrototypeOrder.Focus:PrototypeOrder.Move,
                            caster.Position,run.Battle.PlayerCanSee(BattleSide.A,galan)?galan.Id:-1,true);
                run.Tick(1d/60);
                pending=run.RpgAttackHistory.FirstOrDefault(a=>a.SourceId==caster.Id&&a.DamageType=="magic"&&a.ArriveAt>a.LaunchedAt&&!a.Settled);
            }
            Need(pending!=null,"伽兰实际出手且法球存在飞行时间");
            run.SaveCheckpoint(state);
            var checkpoint=JsonUtility.FromJson<BaseCampaignBattle>(JsonUtility.ToJson(state.Campaign.ActiveBattle));
            var resumed=new RealtimeCampaignBattle(new BaseCampaignSession(checkpoint));
            var saved=resumed.RpgAttackHistory.Single(a=>a.Id==pending.Id);
            Need(saved.DamageType=="magic"&&saved.LaunchedAt==pending.LaunchedAt&&saved.ArriveAt==pending.ArriveAt&&saved.Hit==pending.Hit,"战斗存读档保留攻击类型、时间与掷骰");

            DemoStageSaveTools.CompleteFirstRuins(state,run);
            var cleared=state.Copy();
            BaseEconomy.Validate(cleared);
            var captive=cleared.Aftermath.Prisoners.Single(p=>p.Id==cleared.Demo.Flow.FirstControllerId);
            Need(captive.IsAlien&&captive.IsAwakened&&captive.Rpg.Profession==3,"首通后的古窟俘虏");
            Need(BaseDemoStories.AcceptController(cleared,captive.Id),"招降伽兰");
            var loaded=JsonUtility.FromJson<BaseState>(JsonUtility.ToJson(cleared));BaseEconomy.Validate(loaded);
            var leader=loaded.Leaders.Single(l=>l.Id=="partner_recruit:"+captive.Id);
            Need(leader.PersonalEquipment.Weapon=="staff"&&leader.Rpg.Profession==3&&BaseRpg.Stats(leader).CombatKind==SoldierKind.Mage,"招降、读档、空手施法");
            Need(BasePersonalEquipment.Compatible(leader,new BasePersonalItem{Template="basic_staff"},"weapon")
                &&!BasePersonalEquipment.Compatible(leader,new BasePersonalItem{Template="basic_bow"},"weapon"),"职业武器限制");
            var trainee=new BaseLeader{Rpg=BaseRpg.Character("trial",3,false,false,rules),
                PersonalEquipment=new BasePersonalLoadout{Version=1,Weapon="staff",InitialWeapon="staff"}};
            var before=BaseRpg.Stats(trainee);
            trainee.PersonalEquipment.Items.Add(new BasePersonalItem{Template="basic_staff",Slot="weapon",Quality=2});
            var withStaff=BaseRpg.Stats(trainee);
            Need(Math.Abs(withStaff.Damage-before.Damage-2)<.0001&&withStaff.CombatKind==before.CombatKind,
                "普通法师穿戴一般品质木法杖增加2点有效攻击");
            Need(BasePersonalEquipment.ShopFor(loaded).Any(d=>d.Id=="basic_staff")&&BaseTraining.Book(SoldierKind.Mage,2)=="training_mage_basic"
                &&BaseTraining.Book(SoldierKind.Mage,3)=="training_mage_advanced","铁匠法杖与两级兵书");
            void At(BaseState s,string objectId)
            {
                var obj=BaseSpatial.Objects(s).Single(o=>o.Id==objectId);
                var point=obj.Blocks?BaseStageWalk.Door(s,objectId):new BasePoint(obj.X,obj.Y);
                s.PlayerX=point.X;s.PlayerY=point.Y;BaseAdventure.Arrive(s);
            }
            At(loaded,"demo_ruins_return");Need(BaseCampaign.Travel(loaded,"town"),"离开遗迹入口");
            At(loaded,"town_quest");Need(BaseDemoRuins.Deliver(loaded),"回镇交付遗迹");
            At(loaded,"travel_base");Need(BaseCampaign.Travel(loaded,"base"),"返回营地接人");
            At(loaded,"home");
            Need(BaseEconomy.Rest(loaded,loaded.Day) && leader.Health>1,"招募后在营地过日恢复生命");
            if(!BaseAdventure.Contains(loaded,leader.Id))
            {
                if(BaseAdventure.Full(loaded))
                    Need(BaseAdventure.SetMember(loaded,loaded.Adventure.Members.Last(),false),"给伽兰腾出随行位置");
                Need(BaseAdventure.SetMember(loaded,leader.Id,true),"伽兰随行："+BaseAdventure.ChangeProblem(loaded,leader.Id,true));
            }
            At(loaded,"exit_east");Need(BaseCampaign.Travel(loaded,"town"),"随行队伍进镇");
            At(loaded,"demo_ruins_exit");Need(BaseCampaign.Travel(loaded,"outpost"),"前往遗迹");
            At(loaded,"demo_ruins_investigate");
            VerifyDemoPlayerRoute.VerifyRuinsSweepSeed(loaded.Copy());
            var rematch=new BaseBattlePreparation(loaded,BasePrepRoute.Ruins);
            Need(rematch.AutoPlace()>0&&rematch.Problem(loaded,true)=="","重返遗迹部署："+rematch.Problem(loaded,true));
            var allied=new RealtimeCampaignBattle(rematch.Begin(loaded));BaseDemoRuins.Configure(allied);
            var newCaster=allied.Battle.Soldiers.Single(u=>u.Side==BattleSide.A&&u.Kind==SoldierKind.Hero&&u.Rpg?.Id==leader.Rpg.Id);
            Need(newCaster.ProfessionKind==SoldierKind.Mage&&newCaster.AppearanceId=="galan","我方出征身份");
            var actor=allied.Model.Units.Single(u=>u.Id==newCaster.Id+1);
            allied.Model.SetPaused(false);
            for(int tick=0;tick<5400&&allied.Battle.Phase==BattlePhase.Fighting&&!allied.RpgAttackHistory.Any(p=>p.SourceId==actor.Id&&p.ImpactApplied);tick++)
            {
                if(tick%30==0)
                {
                    var target=allied.Model.Units.Where(u=>u.Side!=actor.Side&&u.Alive).OrderBy(u=>(u.Position-actor.Position).Length).FirstOrDefault();
                    if(target!=null && (!actor.ManualOrder || actor.TargetId!=target.Id))
                    {
                        bool visible=allied.Battle.PlayerCanSee(BattleSide.A,allied.Battle.Soldier(target.Id-1));
                        allied.Issue(new[]{newCaster.Id},visible?PrototypeOrder.Focus:PrototypeOrder.Move,target.Position,visible?target.Id-1:-1,true);
                    }
                }
                allied.Tick(1d/60);
            }
            Need(allied.RpgAttackHistory.Any(p=>p.SourceId==actor.Id&&p.DamageType=="magic"&&p.ImpactApplied),
                $"读档后伽兰出战命中：生命 {actor.Health}/{actor.MaxHealth}，战局 {allied.Battle.Phase}，{allied.Model.Elapsed:0.0}s，距离 {actor.Reach}，位置 {actor.Position.X:0.0},{actor.Position.Z:0.0}，目标 {actor.TargetId}，出手 {allied.RpgAttackHistory.Count(p=>p.SourceId==actor.Id)}");
            string controller=state.Demo.Flow.FirstControllerId;
            Need(BaseDemoStories.AcceptController(state,controller),"招降初次通关的伽兰后回营训练");
            At(state,"demo_ruins_return");Need(BaseCampaign.Travel(state,"town"),"训练前返回城镇");
            At(state,"travel_base");Need(BaseCampaign.Travel(state,"base"),"训练前回营");
            At(state,"home");
            var student=state.Leaders.Single(l=>l.Id=="partner_recruit:"+controller);
            foreach(int targetLevel in new[]{2,3})
            {
                string expectedBook=targetLevel==2?"training_mage_basic":"training_mage_advanced";
                int quantity=BaseTraining.Books(student,targetLevel), days=BaseTraining.Days(student,targetLevel), start=state.Day;
                BaseBackpack.Receive(state,expectedBook,quantity,state.PlayerLeaderId);
                int stock=BasePartyInventory.AvailableMaterial(state,expectedBook);
                Need(BaseTraining.StartLeader(state,student.Id,state.Backpack.Revision),"开始训练："+BaseTraining.LeaderProblem(state,student));
                Need(student.Training.Target==targetLevel&&student.Training.EndDay==start+days&&
                    BasePartyInventory.AvailableMaterial(state,expectedBook)==stock-quantity,"开始时扣除对应法师兵书");
                for(int n=0;n<days;n++)
                {
                    Need(student.Training.Level==targetLevel-1&&BaseEconomy.Rest(state,state.Day),"过日结算训练");
                }
                Need(!BaseTraining.Busy(student.Training)&&student.Training.Level==targetLevel&&state.Day==start+days&&
                    student.MaxHealth==BaseRpg.Stats(student).Health,"到期升级和生命上限");
                Debug.Log($"[P52 training] {targetLevel-1}→{targetLevel}: {expectedBook} ×{quantity}, day {start}→{state.Day}, maxHP {student.MaxHealth}");
            }
            Debug.Log("[P52 combat] PASS: 双通道伤害、估算、古窟施法、在途弹道存读档、招降装备和兵书、我方再次出战命中");
        }
    }
}

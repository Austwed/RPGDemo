using System;
using System.Collections.Generic;
using System.Linq;
using BaseConquest.Battle;

namespace BaseConquest.Base
{
    public sealed class BaseSweepLoss
    {
        public int UnitId;
        public string Name;
        public bool Leader, Dead;
        public double Before, After;
    }
    public sealed class BaseSweepPreview
    {
        public string Problem="";
        public double AllyPower, EnemyPower, Injury, Exposure;
        public int Seconds;
        public List<BaseSweepLoss> Losses=new List<BaseSweepLoss>();
        public int Deaths=>Losses.Count(l=>l.Dead);
    }
    public static class BaseRuinsSweep
    {
        public static string Problem(BaseState s)
        {
            if(!BaseDemoRuins.Enabled(s)||s.Demo.RuinsDay==0)return "首次通关遗迹后才能扫荡";
            if(s.Aftermath.GameOver||BaseSceneCombat.Active(s))return "请先结束当前战斗";
            if(!(BaseSceneRules.Entrance(s)||BaseDemoRuins.InRuins(s))||!BaseInteraction.CanReach(s,BaseSpatial.Objects(s).SingleOrDefault(o=>o.Id=="demo_ruins_investigate")))return "请靠近遗迹入口或调查点";
            if(!string.IsNullOrEmpty(s.Demo.RuinsRewards?.PendingBattleId))return "请先领取或放弃上次副本奖励";
            if(BaseBattleLoot.Pending(s))return "请先处理上次战果";
            return "";
        }
        public static BaseSweepPreview Preview(BaseState s,string mode="full")
        {
            var problem=Problem(s);if(problem!="")return new BaseSweepPreview {Problem=problem};
            try{return Estimate(s,BaseDemoRuins.Begin(s.Copy(),mode));}
            catch(Exception e){return new BaseSweepPreview {Problem=e.Message};}
        }
        private static BaseSweepPreview Estimate(BaseState state,BaseCampaignSession session)
        {
            var rules=session.Opening.Rpg??throw new InvalidOperationException("扫荡需要RPG战力规则");
            var allies=session.Battle.Soldiers.Where(u=>u.Side==BattleSide.A&&u.IsAlive).OrderBy(u=>u.Id).ToArray();
            double Power(BattleSoldier u)=>BaseRpg.Power(u,rules)*Math.Max(0,Math.Min(1,u.Health/u.Stats.Health));
            var p=new BaseSweepPreview {AllyPower=allies.Sum(Power),EnemyPower=session.Battle.Soldiers.Where(u=>u.Side==BattleSide.B&&u.IsAlive).Sum(Power)};
            if(p.AllyPower<=0||p.AllyPower+1e-8<p.EnemyPower){p.Problem="我方当前战力不足，请提升装备、补充军需或调整队伍";return p;}
            double ratio=p.EnemyPower/p.AllyPower;
            p.Injury=rules.SweepLossAtParity*ratio*ratio;
            p.Seconds=(int)Math.Ceiling(Math.Max(rules.SweepMinimumSeconds,rules.SweepSecondsAtParity*ratio));
            p.Exposure=session.Opening.RuinsExposureFraction*p.Seconds;
            var troops=allies.Where(u=>u.Kind!=SoldierKind.Hero).OrderBy(u=>u.Health/u.Stats.Health).ThenBy(u=>u.Id).ToArray();
            int lost=(int)Math.Floor(troops.Length*Math.Min(1,p.Injury+p.Exposure)+1e-8);
            var dead=new HashSet<int>(troops.Take(lost).Select(u=>u.Id));
            foreach(var unit in allies)
            {
                bool leader=unit.Kind==SoldierKind.Hero;
                var person=leader?session.Opening.Leaders[unit.OriginCommanderSlot]:null;
                double fraction=p.Injury+(leader&&BaseRuinsRewards.Immune(state,person)?0:p.Exposure);
                p.Losses.Add(new BaseSweepLoss {UnitId=unit.Id,Name=leader?person.Name:BaseEconomy.TroopName(BaseCampaign.ToBase(unit.Kind)),Leader=leader,Dead=dead.Contains(unit.Id),Before=unit.Health,After=dead.Contains(unit.Id)?0:Math.Max(1,unit.Health-unit.Stats.Health*fraction)});
            }
            return p;
        }
        public static BaseSweepPreview Resolve(BaseState s,string mode="full")
        {
            var preview=Preview(s,mode);if(preview.Problem!="")throw new InvalidOperationException(preview.Problem);
            var session=BaseDemoRuins.Begin(s,mode);var result=Estimate(s,session);
            if(result.Problem!="")throw new InvalidOperationException(result.Problem);
            var run=new RealtimeCampaignBattle(session);BaseDemoRuins.Configure(run);
            foreach(var loss in result.Losses)
            {
                var soldier=run.Battle.Soldier(loss.UnitId);var actor=run.Model.Units.Single(u=>u.Id==soldier.Id+1);
                actor.Health=loss.After;actor.KnockedDown=false;
                soldier.Health=(float)loss.After;soldier.InstantDead=loss.Dead;soldier.DeathResolved=loss.Dead;
            }
            var controller=run.Battle.Commander(BattleSide.B,0);
            run.Model.Units.Single(u=>u.Id==controller.Id+1).Health=0;
            var clock=run.Model.Capture();clock.Seconds=result.Seconds;run.Model.Restore(clock,RealtimePrototype.RealtimeBattlePrototype.RpgAttackDamageLimit);
            run.RefreshDeveloperState();run.Battle.FinishRealtime(BattleOutcome.SideAWin);
            if(!BaseDemoRuins.Complete(s,run,false))throw new InvalidOperationException("扫荡战果未能提交");
            var report=s.Campaign.Reports.Single(r=>r.BattleId==session.Opening.Id);report.Passive=true;report.Title="遗迹扫荡 · 战力估算";
            BaseEconomy.Validate(s);return result;
        }
    }
}

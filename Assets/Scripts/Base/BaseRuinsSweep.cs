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
        public static BaseSweepPreview Preview(BaseState s,string mode="full",BaseBattlePreparation preparation=null)
        {
            var problem=Problem(s);if(problem!="")return new BaseSweepPreview {Problem=problem};
            try{return Estimate(s,Begin(s.Copy(),mode,preparation));}
            catch(Exception e){return new BaseSweepPreview {Problem=e.Message};}
        }
        private static BaseSweepPreview Estimate(BaseState state,BaseCampaignSession session)
        {
            var rules=session.Opening.Rpg??throw new InvalidOperationException("扫荡需要RPG战力规则");
            var allies=session.Battle.Soldiers.Where(u=>u.Side==BattleSide.A&&u.IsAlive).ToList();
            var enemies=session.Battle.Soldiers.Where(u=>u.Side==BattleSide.B&&u.IsAlive).ToList();
            // 战力与伤损统一走 BasePower 的双方相关推演；门槛走 BaseAutoResolve 的扫荡专属战力比（严格 >1.5 倍）。
            var engagement=BasePower.Engage(allies,enemies,rules);
            var p=new BaseSweepPreview {AllyPower=engagement.AllyPower,EnemyPower=engagement.EnemyPower};
            var problem=BaseAutoResolve.Problem(state,BaseDemoRuins.EventId,engagement,BaseAutoResolve.SweepAdvantage);
            if(!string.IsNullOrEmpty(problem)){p.Problem=problem;return p;}
            p.Injury=Math.Max(0,Math.Min(1,1-engagement.AllySurvival));
            p.Seconds=(int)Math.Ceiling(Math.Max(rules.SweepMinimumSeconds,engagement.Seconds));
            p.Exposure=session.Opening.RuinsExposureFraction*p.Seconds;
            p.Losses=BaseAutoResolve.Losses(session,engagement,p.Exposure);
            return p;
        }
        private static BaseCampaignSession Begin(BaseState state, string mode, BaseBattlePreparation preparation)
        {
            if (preparation == null) return BaseDemoRuins.Begin(state, mode);
            if (preparation.Route != BasePrepRoute.Ruins) throw new InvalidOperationException("只有古窟备战可扫荡");
            return BaseDemoRuins.Begin(state, mode, preparation.Plan(state, false), preparation.Roster(state, false));
        }

        public static BaseSweepPreview Resolve(BaseState s,string mode="full",BaseBattlePreparation preparation=null)
        {
            var preview=Preview(s,mode,preparation);if(preview.Problem!="")throw new InvalidOperationException(preview.Problem);
            var session=Begin(s,mode,preparation);var result=Estimate(s,session);
            if(result.Problem!="")throw new InvalidOperationException(result.Problem);
            var run=new RealtimeCampaignBattle(session);BaseDemoRuins.Configure(run);
            BaseAutoResolve.Apply(run,result.Losses,result.Seconds);
            if(!BaseDemoRuins.Complete(s,run,false))throw new InvalidOperationException("扫荡战果未能提交");
            var report=s.Campaign.Reports.Single(r=>r.BattleId==session.Opening.Id);report.Passive=true;report.Title="遗迹扫荡 · 战力估算";
            BaseEconomy.Validate(s);return result;
        }
    }
}

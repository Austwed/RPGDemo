using System;
using System.Collections.Generic;
using System.Linq;
using BaseConquest.Battle;
using BaseConquest.RealtimePrototype;

namespace BaseConquest.Base
{
    // 「跳过战斗」的白名单与结算器。
    //
    // 口径：说哪些战斗可以跳过，不说哪些不允许跳过——默认一律不允许。
    // 允许跳过已真实通关的副本：沉眠古窟和土匪窝点。
    // 袭击防守（首防、第二波、随机入侵、异地入侵）不再允许战力估算，全部走真实战斗；
    // 过夜无人接手时也是真实模型跑完再结算。剧情战斗同样不在白名单。
    // 战力推演不胜或僵持超时的一律不结算，回退为真实作战——跳过永远只是「能跳过时才跳」。
    public static class BaseAutoResolve
    {
        public static bool Skippable(BaseState s, string eventId)
        {
            if (s?.Campaign == null || string.IsNullOrEmpty(eventId)) return false;
            // 是否已首通由 BaseRuinsSweep.Problem 另行判定。
            return eventId == BaseDemoRuins.EventId || eventId == BaseBanditHideout.EventId;
        }

        // 遗迹扫荡的专属门槛（2026-11-05 裁决，玩法案 §15.1）：我方战力必须**严格大于**敌方参考战力的 1.5 倍。
        // 只属于扫荡；袭击防守已无估算通道，所以这里不再需要另一套门槛。
        public const double SweepAdvantage = 1.5;

        public static string Problem(BaseState s, string eventId, BaseEngagement e, double minAdvantage = 0)
        {
            if (!Skippable(s, eventId)) return "这一战必须亲自指挥";
            if (e == null) return "无法推算战果";
            if (!string.IsNullOrEmpty(e.Problem)) return e.Problem;
            if (minAdvantage > 0)
            {
                // 达到战力比即视为可跳：不再叠加「推算能取胜」「僵持超时」两道旧门槛，
                // 否则 1.5 倍以上仍可能被拒，玩家看到的数与判定口径就不一致了。
                if (!(e.Advantage > minAdvantage))
                    return "我方战力需严格超过敌方 " + minAdvantage.ToString("P0") + "（当前 "
                        + e.AllyPower.ToString("0") + " / " + e.EnemyPower.ToString("0")
                        + " ≈ " + e.Advantage.ToString("P0") + "）";
                return "";
            }
            if (!e.AllyWins) return "推算显示无法取胜，请亲自指挥";
            if (e.TimedOut) return "推算显示僵持不下，请亲自指挥";
            return "";
        }

        public static BaseEngagement Evaluate(BaseCampaignSession session)
        {
            var rules = session?.Opening?.Rpg;
            if (rules == null) return null;
            var allies = session.Battle.Soldiers.Where(u => u.Side == BattleSide.A && u.IsAlive).ToList();
            var enemies = session.Battle.Soldiers.Where(u => u.Side == BattleSide.B && u.IsAlive).ToList();
            var objective = session.Battle.Scenario?.Objective;
            double hold = objective != null && objective.Kind == ObjectiveKinds.Hold ? objective.HoldRounds : 0;
            return BasePower.Engage(allies, enemies, rules, hold,session.Opening.DemoCombat?session.Opening.Difficulty.Preset:"normal");
        }

        // 真实战斗的伤亡结构（132 场真值拟合，见 05 数值案 §13）：
        // 总血量损失里只有一部分变成阵亡，其余是幸存者掉血。旧实现把 injury 同时当死亡比例和幸存者掉血比例，
        // 总损失被放大成 injury + injury×(1−injury)，11 人队损失 40% 会被结算成死 4 个（真实约 2 个）。
        //   阵亡比例 d = DeathCurve × injury²；幸存者血量比 h = (1−injury)/(1−d)，保证总血量守恒。
        public const double DeathCurve = .88;
        // 把「剩余血量比」摊到每个单位：普通兵按阵亡比例阵亡，队长只掉血、最低保留 1 点生命。
        public static List<BaseSweepLoss> Losses(BaseCampaignSession session, BaseEngagement e, double exposure = 0, double severity = 1)
        {
            var losses = new List<BaseSweepLoss>();
            var allies = session.Battle.Soldiers.Where(u => u.Side == BattleSide.A && u.IsAlive)
                .OrderBy(u => u.Id).ToArray();
            double injury = Math.Max(0, Math.Min(1, (1 - e.AllySurvival) * Math.Max(1, severity) + Math.Max(0, exposure)));
            double deathRate = Math.Min(1, DeathCurve * injury * injury);
            double survivorRatio = injury >= 1 ? 0 : Math.Max(0, Math.Min(1, (1 - injury) / Math.Max(1e-6, 1 - deathRate)));
            var troops = allies.Where(u => u.Kind != SoldierKind.Hero)
                .OrderBy(u => u.Health / Math.Max(1e-9, u.Stats.Health)).ThenBy(u => u.Id).ToArray();
            // 队长在 Demo 里只倒地、不真死，所以死亡全部由普通兵承担。
            // keep 是「按幸存者血量比折算的满血当量」：目标剩余总血量 (1−injury)×total 除以 h 得到。
            double troopHealth = troops.Sum(u => Math.Max(0, u.Stats.Health));
            double leaderHealth = allies.Where(u => u.Kind == SoldierKind.Hero).Sum(u => Math.Max(0, u.Stats.Health));
            double total = troopHealth + leaderHealth;
            double keep = total <= 0 || survivorRatio <= 0 ? 0 : Math.Min(total, (1 - injury) * total / survivorRatio);
            double troopKeep = Math.Max(0, keep - leaderHealth);
            double deathShare = troopHealth <= 0 ? 0 : Math.Min(1, Math.Max(0, 1 - troopKeep / troopHealth));
            int lost = (int)Math.Floor(troops.Length * deathShare + 1e-8);
            var dead = new HashSet<int>(troops.Take(lost).Select(u => u.Id));
            foreach (var unit in allies)
            {
                bool leader = unit.Kind == SoldierKind.Hero;
                var person = leader && unit.OriginCommanderSlot >= 0
                    && unit.OriginCommanderSlot < session.Opening.Leaders.Count
                    ? session.Opening.Leaders[unit.OriginCommanderSlot] : null;
                losses.Add(new BaseSweepLoss
                {
                    UnitId = unit.Id,
                    Name = leader ? (person?.Name ?? "队长") : BaseEconomy.TroopName(BaseCampaign.ToBase(unit.Kind)),
                    Leader = leader,
                    Dead = dead.Contains(unit.Id),
                    Before = unit.Health,
                    After = dead.Contains(unit.Id) ? 0
                        : Math.Max(1, Math.Min(unit.Health, unit.Stats.Health * survivorRatio))
                });
            }
            return losses;
        }

        // 把推算结果写进一场已开战的战斗并宣告我方获胜；真实作战的战果提交由调用方负责。
        public static void Apply(RealtimeCampaignBattle run, List<BaseSweepLoss> losses, double seconds)
        {
            foreach (var loss in losses)
            {
                var soldier = run.Battle.Soldier(loss.UnitId);
                var actor = run.Model.Units.Single(u => u.Id == soldier.Id + 1);
                actor.Health = loss.After;
                actor.KnockedDown = false;
                soldier.Health = (float)loss.After;
                soldier.InstantDead = loss.Dead;
                soldier.DeathResolved = loss.Dead;
            }
            if (run.Battle.Scenario.BanditHideout || BaseDemoRuins.Rematch(run.Session))
            {
                foreach (var unit in run.Battle.Soldiers.Where(u => u.Side == BattleSide.B))
                    run.Model.Units.Single(actor => actor.Id == unit.Id + 1).Health = 0;
            }
            else
            {
                var controller = run.Battle.Commander(BattleSide.B, 0);
                if (controller != null) run.Model.Units.Single(u => u.Id == controller.Id + 1).Health = 0;
            }
            var clock = run.Model.Capture();
            clock.Seconds = Math.Max(0, seconds);
            run.Model.Restore(clock, RealtimePrototype.RealtimeBattlePrototype.RpgAttackDamageLimit);
            run.RefreshDeveloperState();
            run.Battle.FinishRealtime(BattleOutcome.SideAWin);
        }

    }
}

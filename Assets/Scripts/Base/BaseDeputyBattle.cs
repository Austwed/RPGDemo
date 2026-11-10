using System;
using System.Collections.Generic;
using System.Linq;
using BaseConquest.Battle;
using BaseConquest.RealtimePrototype;

namespace BaseConquest.Base
{
    // 副手迎战（P43，2026-11-06 裁决）
    //
    // 与玩家亲自迎战是同一条真实战斗链：同一套战斗规则、同一套结算、同一份士兵库存，
    // 区别只有三点——① 主角与随行队伍不参战；② 权限只放查看与策略；③ 可以由系统后台完整模拟。
    // 这里不再使用任何战力估算：跑不完就整场不结算，保留原状态让玩家改期亲自打。
    public static class BaseDeputyBattle
    {
        // 剧情防守：首防与第二波带剧情分支，必须玩家亲自指挥。
        public static bool Story(BaseState s, string eventId) =>
            eventId == BaseDemoDefense.EventId || eventId == BaseDemoDefense.SecondEventId;

        public static BaseCampaignEvent Event(BaseState s, string eventId) =>
            s?.Campaign?.Events?.FirstOrDefault(e => e.Id == eventId);

        public static bool Delegable(BaseState s, string eventId)
        {
            var e = Event(s, eventId);
            return BaseDemo.Active(s) && e != null && e.Kind == "defense" && e.Status == "pending" && !Story(s, eventId);
        }

        public static string Problem(BaseState s, string eventId)
        {
            if (!BaseDemo.Active(s)) return "本局没有副手";
            var e = Event(s, eventId);
            if (e == null || e.Kind != "defense") return "这不是袭击防守";
            if (Story(s, eventId)) return "首防与第二波是剧情防守，必须玩家亲自指挥";
            if (e.Status != "pending") return "这次袭击已经处理";
            if (s.Campaign.HasActiveBattle) return "已有战斗在进行";
            if (e.AvailableDay > s.Day) return "袭击还没抵达";
            string deputy = BaseDeputy.Problem(s);
            if (deputy != "") return deputy;
            if (s.Muster.Parties.Any(p => p.Purpose == "defense")) return "已经有迎战队伍在编，请先返营解散再交给副手";
            if (s.Aftermath.GameOver) return "游戏已结束";
            return "";
        }

        // 副手战的编队：副手排 A 位，再按原迎战名单补齐留守队长；主角与随行成员一律不进。
        // 士兵仍从同一份库存按「可守家」条件抽取，并受 24 人上限约束。
        public static BaseArmyPreset Selection(BaseState s)
        {
            var deputy = BaseDeputy.Leader(s) ?? throw new InvalidOperationException("还没有指定副手");
            var preset = s.Muster.Presets.Single(p => p.Purpose == "defense").Copy();
            int size = Math.Max(1, Math.Min(3, preset.LeaderCount));
            var blocked = new HashSet<string>(s.Adventure?.Members ?? new List<string>()) { s.PlayerLeaderId };
            var candidates = new List<string> { deputy.Id };
            foreach (string id in preset.Candidates)
            {
                if (candidates.Count >= size) break;
                if (id == deputy.Id || blocked.Contains(id) || candidates.Contains(id)) continue;
                if (s.Leaders.Any(l => l.Id == id && BaseDevelopment.CanFight(l))) candidates.Add(id);
            }
            // 原名单不足（例如刚换过人）：按生命比例补本地守军，优先级与自动选兵一致。
            foreach (var leader in BaseDeputy.Candidates(s)
                .OrderByDescending(l => l.MaxHealth > 0 ? l.Health / l.MaxHealth : 0)
                .ThenBy(l => l.Id, StringComparer.Ordinal))
            {
                if (candidates.Count >= size) break;
                if (candidates.Contains(leader.Id) || !BaseDevelopment.CanFight(leader)) continue;
                candidates.Add(leader.Id);
            }
            preset.Candidates = candidates.Take(3).ToList();
            preset.LeaderCount = Math.Max(1, Math.Min(size, preset.Candidates.Count));
            return preset;
        }

        public static BaseMusterPlan Preview(BaseState s)
        {
            var selection = Selection(s);
            var plan = BaseMuster.Preview(s, "defense", false, selection);
            if (plan.Leaders.Count == 0) throw new InvalidOperationException("没有可带队的留守队长");
            return plan;
        }

        // 后台完整模拟：创建真实战斗 → 跑到结束 → 同一套结算。任何一步失败都抛异常，由 Change 回滚整场。
        public static void Resolve(BaseState s, string eventId, BattleScenarioDocument document)
        {
            string problem = Problem(s, eventId);
            if (problem != "") throw new InvalidOperationException(problem);
            var selection = Selection(s);
            var plan = BaseMuster.Preview(s, "defense", false, selection);
            if (plan.Leaders.Count == 0) throw new InvalidOperationException("没有可带队的留守队长");
            var opening = BaseCampaign.Begin(s, eventId, document, "full", plan, true);
            opening.Deputy = true;
            var session = new BaseCampaignSession(opening);
            session.Battle.Control = BattleControlPolicy.RemoteDefense;
            session.Battle.AutoDeploy(BattleSide.A); session.Bind();
            if (!session.Battle.Start(out string reason)) throw new InvalidOperationException("副手部署不能开始：" + reason);
            var run = new RealtimeCampaignBattle(session);
            run.Model.SetPaused(false);
            int bound = (opening.Scenario.MaxRounds + 1) * 8;
            for (int frame = 0; frame < bound && session.Battle.Phase == BattlePhase.Fighting; frame++) run.Tick(.25);
            if (session.Battle.Phase != BattlePhase.Finished)
                throw new InvalidOperationException("副手战未能在时限内结束，本场没有结算；原状态与日程保持不变");
            if (!BaseCampaign.Settle(s, session)) throw new InvalidOperationException("副手战结算失败");
            if (!s.Campaign.Reports.Any(r => r.BattleId == opening.Id && r.Deputy))
                throw new InvalidOperationException("副手战战报未标记副手");
        }
    }
}

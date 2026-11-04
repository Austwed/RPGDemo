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
    // 白名单目前只有两类：
    //   ① 沉眠古窟扫荡（需已真实通关一次）
    //   ② 基地自动迎战（普通入侵防守战）
    // 剧情战斗（序章、遗迹首次救援、黑鸦寨两波、攻营、据点进攻、返乡）都不在白名单。
    // 战力推演不胜或僵持超时的一律不结算，回退为真实作战——跳过永远只是「能跳过时才跳」。
    public static class BaseAutoResolve
    {
        public static bool Skippable(BaseState s, string eventId)
        {
            if (s?.Campaign == null || string.IsNullOrEmpty(eventId)) return false;
            // 扫荡：沉眠古窟。是否已首通由 BaseRuinsSweep.Problem 另行判定。
            if (eventId == BaseDemoRuins.EventId) return true;
            // 黑鸦寨两波入侵是带剧情对白的防守战，必须亲自指挥。
            if (eventId == BaseDemoDefense.EventId || eventId == BaseDemoDefense.SecondEventId) return false;
            // 基地自动迎战：普通入侵防守战。
            var mission = s.Campaign.Events.FirstOrDefault(x => x.Id == eventId);
            return mission != null && mission.Kind == "defense";
        }

        public static string Problem(BaseState s, string eventId, BaseEngagement e)
        {
            if (!Skippable(s, eventId)) return "这一战必须亲自指挥";
            if (e == null) return "无法推算战果";
            if (!string.IsNullOrEmpty(e.Problem)) return e.Problem;
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
            return BasePower.Engage(allies, enemies, rules, hold);
        }

        // 真实战斗的伤亡结构（132 场真值拟合，见 05 数值案 §13）：
        // 总血量损失里只有一部分变成阵亡，其余是幸存者掉血。旧实现把 injury 同时当死亡比例和幸存者掉血比例，
        // 总损失被放大成 injury + injury×(1−injury)，11 人队损失 40% 会被结算成死 4 个（真实约 2 个）。
        //   阵亡比例 d = DeathCurve × injury²；幸存者血量比 h = (1−injury)/(1−d)，保证总血量守恒。
        public const double DeathCurve = .88;
        // 自动迎战的惩罚倍率：它是兜底手段，不是替代方案，代价要明显高于亲自上战场。
        public const double AutoDefenseSeverity = 1.35;

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

        // 自动迎战的经济代价：来不及把物资撤进营帐，按库存固定比例被掠走。
        // 这不是「输了的罚款」——打赢也照扣，正是因为仓促应战才被抢。
        public const double AutoFoodLoss = .20, AutoMaterialLoss = .15, AutoCoinLoss = .15;

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
            // 敌方指挥被击溃，作为「敌方溃败」的结算依据。
            var controller = run.Battle.Commander(BattleSide.B, 0);
            if (controller != null) run.Model.Units.Single(u => u.Id == controller.Id + 1).Health = 0;
            var clock = run.Model.Capture();
            clock.Seconds = Math.Max(0, seconds);
            run.Model.Restore(clock, RealtimePrototype.RealtimeBattlePrototype.RpgAttackDamageLimit);
            run.RefreshDeveloperState();
            run.Battle.FinishRealtime(BattleOutcome.SideAWin);
        }

        // 过夜／行军时的自动迎战：能跳过就跳过，不能跳过返回 false 让调用方照旧跑真实作战。
        public static bool TryResolve(BaseState s, BaseCampaignSession session, string eventId)
        {
            if (session == null) return false;
            var e = Evaluate(session);
            if (!string.IsNullOrEmpty(Problem(s, eventId, e))) return false;
            var run = new RealtimeCampaignBattle(session);
            // 自动迎战的伤亡按同一套拆分，但总损失放大到 1.35 倍：它是兜底，不是替代亲自指挥。
            var losses = Losses(session, e, 0, AutoDefenseSeverity);
            Apply(run, losses, e.Seconds);
            return true;
        }

        // 自动迎战的基地损失：结算完成后调用，按库存比例掠走基础资源并记进战报净变化。
        public static void ApplyRaidCost(BaseState s, BaseCampaignReport report)
        {
            if (s == null || report == null) return;
            int food = (int)Math.Floor(Math.Max(0, s.Inventory.Food) * AutoFoodLoss);
            int materials = (int)Math.Floor(Math.Max(0, s.Inventory.Materials) * AutoMaterialLoss);
            int coins = (int)Math.Floor(Math.Max(0, s.Inventory.Coins) * AutoCoinLoss);
            if (food + materials + coins <= 0) return;
            s.Inventory.Food -= food;
            s.Inventory.Materials -= materials;
            s.Inventory.Coins -= coins;
            report.FoodChange -= food;
            report.MaterialChange -= materials;
            report.CoinChange -= coins;
            report.RaidNote = "自动迎战 · 仓促应战未及坚壁，被掠走 粮 " + food + "／原料 " + materials + "／钱币 " + coins
                + "（固定 " + (int)(AutoFoodLoss * 100) + "／" + (int)(AutoMaterialLoss * 100) + "／" + (int)(AutoCoinLoss * 100) + "% 库存）";
        }
    }
}

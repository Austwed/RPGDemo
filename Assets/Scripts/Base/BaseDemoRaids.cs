using System;
using System.Collections.Generic;
using System.Linq;
using BaseConquest.Battle;

namespace BaseConquest.Base
{
    [Serializable]
    public sealed class BaseDemoRaidState
    {
        public int Version, Waves, NextDay;
        public BaseDemoRaidState Copy() => (BaseDemoRaidState)MemberwiseClone();
    }

    // 主线之后的周期性随机入侵。
    //
    // 定位：Demo 主线走完（调查断剑、Stage=Finished）以后，外部世界不会因为剧情结束而安静下来。
    // 隔几天来一波流寇，玩家要么亲自迎战，要么让过夜时的自动迎战兜底——后者正是为「多基地分身乏术」准备的。
    //
    // 战力构成（2026-11-04 暂定骨架，具体配比待再议）：
    //   总量 = 合计基准 × (1 + 12%×已来波次，上限 2.2 倍) × 随机抖动(±15%)
    //   三兵种等概率掷点，指挥者固定 1 名。想换成「按玩家兵力镜像」或「固定配比」只需改 Allocate。
    public static class BaseDemoRaids
    {
        public const int Version = 1;
        public const int FirstDelay = 4;          // 主线结束后第几天来第一波
        public const int MinGap = 4, MaxGap = 8;  // 之后每波间隔
        public const double GrowthPerWave = .12;  // 每波总量 +12%
        public const double MaxScale = 2.2;       // 成长上限，防止无限滚雪球
        public const double Jitter = .15;         // 总量随机浮动
        public const string IdPrefix = "demo:raid:";
        public const string Scenario = "23_据点迎战.json";

        public static bool Enabled(BaseState s) => BaseDemo.Active(s);
        // 只有主线真正走完才开：Finished 是「调查断剑」那一步，此前仍按剧本推进，不掺随机事件。
        public static bool Unlocked(BaseState s) => Enabled(s) && s.Demo.Stage == BaseDemoStage.Finished;

        public static void PrepareOpening(BaseState s)
        {
            if (BaseDemo.Active(s)) s.Demo.Raid = new BaseDemoRaidState { Version = Version };
        }

        // 旧档没有这一段：第一次用到时补一份，不要求重开存档。
        public static BaseDemoRaidState Ensure(BaseState s)
        {
            if (!BaseDemo.Active(s)) return null;
            if (s.Demo.Raid == null || s.Demo.Raid.Version != Version) s.Demo.Raid = new BaseDemoRaidState { Version = Version };
            return s.Demo.Raid;
        }

        public static void Validate(BaseState s)
        {
            if (!BaseDemo.Active(s)) return;
            var raid = s.Demo.Raid;
            // Unity 的 JsonUtility 对缺失的可序列化字段会造一个默认实例（不是 null），所以旧档读进来是 Version=0 的空壳。
            // 那是「还没见过随机入侵」的正常状态，由 Ensure 在首次推进时补，不当作数据损坏。
            if (raid == null || raid.Version != Version) return;
            if (raid.Waves < 0 || raid.Waves > 100000) throw new ArgumentException("Demo随机入侵波次非法");
            if (raid.NextDay < 0 || raid.NextDay > 1000000) throw new ArgumentException("Demo随机入侵日期非法");
            var pending = s.Campaign.Events.Where(e => e.Id.StartsWith(IdPrefix, StringComparison.Ordinal) && e.Status == "pending").ToArray();
            if (pending.Length > 1) throw new ArgumentException("Demo随机入侵同时存在多波未处理");
            if (pending.Any(e => e.Kind != "defense" || e.EnemyArmy.Count == 0 || e.AvailableDay != e.DeadlineDay)) throw new ArgumentException("Demo随机入侵事件结构非法");
        }

        // 商人给的线索：说一个区间而不是确切日期，保留「去打探」的价值。
        public static string Rumor(BaseState s)
        {
            if (!Unlocked(s)) return "";
            var raid = Ensure(s);
            if (raid.NextDay <= 0) return "山外的流寇最近老实得很，暂时听不到动静。";
            int from = Math.Max(s.Day, raid.NextDay - 1), to = raid.NextDay + 1;
            if (to < s.Day) return "有一伙流寇已经在路上，就这两天的事。";
            return "山里的流寇又囤粮了，我瞧第 " + from + " 到第 " + to + " 天之间会来碰运气。";
        }

        public static void AdvanceDay(BaseState s)
        {
            if (!Unlocked(s)) return;
            var raid = Ensure(s);
            if (raid.NextDay <= 0) { raid.NextDay = Math.Min(1000000, s.Day + FirstDelay); return; }
            if (s.Day < raid.NextDay) return;
            // 上一波还没处理完就顺延到明天，避免同一天堆两波。
            if (s.Campaign.Events.Any(e => e.Id.StartsWith(IdPrefix, StringComparison.Ordinal) && e.Status == "pending")) return;
            Spawn(s);
        }

        private static void Spawn(BaseState s)
        {
            var raid = Ensure(s);
            var source = s.Campaign.EnemySites?.FirstOrDefault();
            if (source?.Capacity == null) return;
            int wave = raid.Waves + 1;
            uint seed = unchecked((uint)((s.Demo.Rpg?.GenerationSeed ?? 7919) * 397 ^ s.Day * 104729 ^ wave * 2654435761u));
            int total = TroopTotal(s, raid.Waves, seed);

            var army = source.Capacity.Clone();
            army.HeroCount = 1;
            army.MeleeCount = army.RangedCount = army.CavalryCount = 0;
            army.CommanderStats = army.CommanderStats.Take(1).Select(stats => stats.Clone()).ToList();
            army.CommanderNames = new List<string> { "流寇头目" };
            for (int i = 0; i < total; i++)
            {
                double roll = Unit(seed, (uint)(2 + i));
                if (roll < .40) army.MeleeCount++;
                else if (roll < .75) army.RangedCount++;
                else army.CavalryCount++;
            }
            army.Commanders = new List<CommanderLoadout> { new CommanderLoadout { Melee = army.MeleeCount, Ranged = army.RangedCount, Cavalry = army.CavalryCount } };

            string id = IdPrefix + wave;
            while (s.Campaign.Events.Any(e => e.Id == id)) id += "_";
            var enemies = BaseDemoCombat.CreateEnemies(id, army);
            if (enemies.Count == 0) return;
            s.Campaign.Events.Add(new BaseCampaignEvent
            {
                Id = id,
                Title = "流寇劫掠 · 第" + wave + "波",
                Kind = "defense",
                SourceSiteId = "",
                ScenarioFile = Scenario,
                Status = "pending",
                DepartedDay = s.Day,
                AvailableDay = s.Day,
                DeadlineDay = s.Day,
                HoldRounds = 30,
                FoodReward = 12, MaterialReward = 8, CoinReward = 15,
                FoodRaid = 15, MaterialRaid = 8, CoinRaid = 10,
                EnemyArmy = enemies
            });
            raid.Waves = wave;
            raid.NextDay = Math.Min(1000000, s.Day + MinGap + (int)Math.Floor(Unit(seed, 99) * (MaxGap - MinGap + 1)));
        }

        private static int TroopTotal(BaseState s, int waves, uint seed)
        {
            int baseline = Math.Max(3, BaseDemoCombat.BaselineTotal(s.Demo.Rpg));
            double scale = Math.Min(MaxScale, 1 + GrowthPerWave * waves);
            double jitter = 1 - Jitter + 2 * Jitter * Unit(seed, 1);
            return Math.Max(3, Math.Min(90, (int)Math.Round(baseline * scale * jitter)));
        }

        private static double Unit(uint seed, uint salt)
        {
            unchecked
            {
                uint x = seed ^ (salt * 2654435761u);
                x ^= x >> 15; x *= 2246822519u;
                x ^= x >> 13; x *= 3266489917u;
                x ^= x >> 16;
                return x / 4294967296.0;
            }
        }
    }
}

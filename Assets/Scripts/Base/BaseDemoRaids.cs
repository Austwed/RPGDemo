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
        // 下一波的配型（Patterns 里的 Id）。排期时就掷好，商人才能说清「来的是马匪还是弓匪」。
        public string NextPattern = "";
        public BaseDemoRaidState Copy() => (BaseDemoRaidState)MemberwiseClone();
    }

    // 主线之后的周期性随机入侵。
    //
    // 定位：Demo 主线走完（调查断剑、Stage=Finished）以后，外部世界不会因为剧情结束而安静下来。
    // 隔几天来一波流寇，玩家要么亲自迎战，要么让过夜时的自动迎战兜底——后者正是为「多基地分身乏术」准备的。
    //
    // 兵力总量 = 合计基准 × (1 + 12%×已来波次，上限 2.2 倍) × 随机抖动(±15%)，指挥者固定 1 名。
    //
    // 配型（2026-11-04 用户裁决：Demo 先用「已有队长立绘 + 随机／特色兵力」几套，之后按这张表继续加行）：
    //   立绘复用已有队长美术（PixelDemoArt 的 cast id），名字用「匪」系称呼——
    //   主线后大当家／巴图可能已经死了，这里只是同一套立绘，不指认剧情人物本人。
    //   特色波不追求彼此等强：马匪冲阵、弓匪压阵、刀匪夜袭本来手感就该不一样。
    //   想加新配型：在 Patterns 里加一行，写明权重、立绘、指挥职业和三兵种权重即可，其它代码不用动。
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

        private sealed class RaidPattern
        {
            public string Id, Title, Clue, Commander, Appearance;
            public SoldierKind CommandKind;
            public double Melee, Ranged, Cavalry;   // 三兵种权重，1 表示整波单一兵种
            public int Weight;                      // 抽取权重
        }

        private static readonly RaidPattern[] Patterns =
        {
            new RaidPattern { Id = "marauder", Title = "流寇劫掠", Clue = "流寇", Commander = "流寇头目", Appearance = "*", CommandKind = SoldierKind.Melee,
                Melee = .40, Ranged = .35, Cavalry = .25, Weight = 55 },
            new RaidPattern { Id = "riders", Title = "马匪冲阵", Clue = "马匪", Commander = "马匪首领", Appearance = "chief", CommandKind = SoldierKind.Cavalry,
                Cavalry = 1, Weight = 20 },
            new RaidPattern { Id = "archers", Title = "弓匪压阵", Clue = "弓匪", Commander = "弓匪头目", Appearance = "batu", CommandKind = SoldierKind.Ranged,
                Ranged = 1, Weight = 15 },
            new RaidPattern { Id = "blades", Title = "刀匪夜袭", Clue = "刀匪", Commander = "刀匪头目", Appearance = "huoya", CommandKind = SoldierKind.Melee,
                Melee = 1, Weight = 10 }
        };

        // 杂牌流寇的指挥立绘在步兵／弓兵两种里挑，避免马匪首领带一群步兵。
        private static readonly string[] MarauderLooks = { "huoya", "batu" };

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

        // 商人给的线索：说一个区间而不是确切日期，保留「去打探」的价值；顺带说清来的是哪一路。
        public static string Rumor(BaseState s)
        {
            if (!Unlocked(s)) return "";
            var raid = Ensure(s);
            if (raid.NextDay <= 0) return "山外的流寇最近老实得很，暂时听不到动静。";
            var pattern = Pattern(raid.NextPattern);
            string who = pattern == null ? "一伙流寇" : "一伙" + pattern.Clue;
            int from = Math.Max(s.Day, raid.NextDay - 1), to = raid.NextDay + 1;
            if (to < s.Day) return "有一伙流寇已经在路上，就这两天的事。";
            return "山里的" + who + "又囤粮了，我瞧第 " + from + " 到第 " + to + " 天之间会来碰运气。";
        }

        public static void AdvanceDay(BaseState s)
        {
            if (!Unlocked(s)) return;
            var raid = Ensure(s);
            if (raid.NextDay <= 0)
            {
                raid.NextDay = Math.Min(1000000, s.Day + FirstDelay);
                raid.NextPattern = Pick(Seed(s, raid.Waves + 1), 77).Id;
                return;
            }
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
            uint seed = Seed(s, wave);
            var pattern = Pattern(raid.NextPattern) ?? Pick(seed, 77);
            string look = pattern.Appearance == "*" ? MarauderLooks[(int)(Unit(seed, 78) * MarauderLooks.Length) % MarauderLooks.Length] : pattern.Appearance;
            int total = TroopTotal(s, raid.Waves, seed);
            Allocate(pattern, total, seed, out int melee, out int ranged, out int cavalry);

            var army = source.Capacity.Clone();
            army.HeroCount = 1; army.CaptainSlot = 0;
            army.MeleeCount = melee; army.RangedCount = ranged; army.CavalryCount = cavalry;
            army.CommanderStats = army.CommanderStats.Take(1).Select(stats => stats.Clone()).ToList();
            if (army.CommanderStats.Count == 0) army.CommanderStats.Add(new SoldierStats());
            // 指挥者的职业决定他吃什么口粮、拿什么武器；立绘与职业必须对上（骑马的就是骑兵）。
            // HasProfessionKind 也要显式写：PrepareEnemies 优先读职业，只写 CombatKind 会被据点自带的旧职业盖掉。
            var commander = army.CommanderStats[0];
            var commandKind = KindOf(look, pattern.CommandKind);
            commander.HasCombatKind = true; commander.CombatKind = commandKind;
            commander.HasProfessionKind = true; commander.ProfessionKind = commandKind;
            army.CommanderNames = new List<string> { pattern.Commander };
            army.CommanderAppearances = new List<string> { look };
            army.Commanders = new List<CommanderLoadout> { new CommanderLoadout { Melee = melee, Ranged = ranged, Cavalry = cavalry } };

            string id = IdPrefix + wave;
            while (s.Campaign.Events.Any(e => e.Id == id)) id += "_";
            var enemies = BaseDemoCombat.CreateEnemies(id, army);
            if (enemies.Count == 0) return;
            var mission = new BaseCampaignEvent
            {
                Id = id,
                Title = pattern.Title + " · 第" + wave + "波",
                Kind = "defense",
                SourceSiteId = "",
                ScenarioFile = Scenario,
                Status = "pending",
                DepartedDay = s.Day,
                AvailableDay = s.Day,
                DeadlineDay = s.Day,
                HoldRounds = 30,
                FoodReward = 12, MaterialReward = 8, CoinReward = 15,
                EnemyArmy = enemies
            };
            // 掠夺单价跟首防同一套（09d「按人头掠夺」三行），不在这里另写一份数字。
            BaseRaidLoot.ApplyRule(s, mission);
            s.Campaign.Events.Add(mission);
            raid.Waves = wave;
            raid.NextDay = Math.Min(1000000, s.Day + MinGap + (int)Math.Floor(Unit(seed, 99) * (MaxGap - MinGap + 1)));
            raid.NextPattern = Pick(Seed(s, wave + 1), 77).Id;
        }

        // 立绘自带职业：骑马的是骑兵、背弓的是弓兵（与 UnitNaming.CastCombatKind 同一口径）。
        private static SoldierKind KindOf(string look, SoldierKind fallback) =>
            look == "chief" ? SoldierKind.Cavalry : look == "batu" ? SoldierKind.Ranged : look == "huoya" ? SoldierKind.Melee : fallback;

        private static void Allocate(RaidPattern pattern, int total, uint seed, out int melee, out int ranged, out int cavalry)
        {
            melee = ranged = cavalry = 0;
            if (pattern.Cavalry >= 1) { cavalry = total; return; }
            if (pattern.Ranged >= 1) { ranged = total; return; }
            if (pattern.Melee >= 1) { melee = total; return; }
            double sum = pattern.Melee + pattern.Ranged + pattern.Cavalry;
            if (sum <= 0) { melee = total; return; }
            for (int i = 0; i < total; i++)
            {
                double roll = Unit(seed, (uint)(2 + i)) * sum;
                if (roll < pattern.Melee) melee++;
                else if (roll < pattern.Melee + pattern.Ranged) ranged++;
                else cavalry++;
            }
        }

        private static RaidPattern Pattern(string id) =>
            string.IsNullOrEmpty(id) ? null : Patterns.FirstOrDefault(p => p.Id == id);

        private static RaidPattern Pick(uint seed, uint salt)
        {
            int total = Patterns.Sum(p => p.Weight);
            int roll = (int)(Unit(seed, salt) * total);
            foreach (var pattern in Patterns)
            {
                if (roll < pattern.Weight) return pattern;
                roll -= pattern.Weight;
            }
            return Patterns[0];
        }

        private static uint Seed(BaseState s, int wave) =>
            unchecked((uint)((s.Demo.Rpg?.GenerationSeed ?? 7919) * 397 ^ s.Day * 104729 ^ wave * 2654435761u));

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

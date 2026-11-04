using System;
using System.IO;
using System.Linq;
using BaseConquest.Battle;
using UnityEditor;
using UnityEngine;
using BaseConquest.Base;

namespace BaseConquest.Editor
{
    // 随机入侵探针：确认「主线前不来、主线后按间隔来、波次递增、结构合法、商人有线索」。
    // 只读，不写存档；改 BaseDemoRaids 后必须重跑，退出码非 0 视为破坏规则。
    public static class VerifyRandomRaids
    {
        private const string StagePath = "Assets/Resources/DemoStages/defense-male.json";
        private static int failures;

        public static void Run()
        {
            try
            {
                var template = Load();
                var rules = template.Demo?.Rpg ?? throw new InvalidOperationException("阶段档缺 Demo RPG 规则");
                int baseline = BaseDemoCombat.BaselineTotal(rules);
                Debug.Log("[Raid] 合计基准 " + baseline + "，首波延迟 " + BaseDemoRaids.FirstDelay
                    + " 天，间隔 " + BaseDemoRaids.MinGap + "～" + BaseDemoRaids.MaxGap + " 天");

                // 1) 主线未走完：即使连推 30 天也不该冒出流寇。
                var locked = Fresh(template, BaseDemoStage.AttackWon);
                for (int day = 10; day < 40; day++) { locked.Day = day; BaseDemoRaids.AdvanceDay(locked); }
                Check(!locked.Campaign.Events.Any(IsRaid), "主线未完成时不应生成随机入侵");

                // 2) 主线完成：第 FirstDelay 天后首波，之后按间隔来，波次与总量递增。
                var s = Fresh(template, BaseDemoStage.Finished);
                s.Day = 10;
                BaseDemoRaids.AdvanceDay(s);
                Check(s.Demo.Raid != null && s.Demo.Raid.NextDay == 10 + BaseDemoRaids.FirstDelay, "首波日期应为主线完成后第 " + BaseDemoRaids.FirstDelay + " 天");
                Debug.Log("[Raid] 商人线索（未到首波）：" + BaseDemoRaids.Rumor(s));

                var spawned = new System.Collections.Generic.List<BaseCampaignEvent>();
                var lastWave = 0;
                for (int day = 11; day <= 90 && spawned.Count < 8; day++)
                {
                    s.Day = day;
                    int before = s.Campaign.Events.Count(e => IsRaid(e) && e.Status == "pending");
                    BaseDemoRaids.AdvanceDay(s);
                    var fresh = s.Campaign.Events.Where(e => IsRaid(e) && e.Status == "pending").ToArray();
                    if (fresh.Length == before) continue;
                    var raid = fresh.Single();
                    int gap = lastWave == 0 ? raid.AvailableDay - (10 + BaseDemoRaids.FirstDelay) : raid.AvailableDay - lastWave;
                    lastWave = raid.AvailableDay;
                    spawned.Add(raid);
                    int troops = raid.EnemyArmy.Count(u => u.Kind != SoldierKind.Hero);
                    int heroes = raid.EnemyArmy.Count(u => u.Kind == SoldierKind.Hero);
                    var kinds = string.Join("/", new[] { SoldierKind.Melee, SoldierKind.Ranged, SoldierKind.Cavalry }
                        .Select(k => raid.EnemyArmy.Count(u => u.Kind == k)));
                    Debug.Log("[Raid] 第" + raid.AvailableDay + "天 · " + raid.Title + " · 间隔 " + gap + " 天 · 兵 " + troops + "（" + kinds + "）· 队长 " + heroes);
                    Check(heroes == 1, "随机入侵应有且只有 1 名指挥者");
                    Check(troops >= 3, "随机入侵兵力不应少于 3");
                    Check(raid.AvailableDay == raid.DeadlineDay, "随机入侵应在到达当天处理");
                    Check(raid.Kind == "defense" && raid.ScenarioFile == BaseDemoRaids.Scenario, "随机入侵应走防守战场");
                    BaseDemoRaids.Validate(s);
                    // 打完这一波，才能测下一波间隔。
                    raid.Status = "resolved";
                }
                Check(spawned.Count >= 5, "90 天内应至少刷出 5 波");
                for (int i = 1; i < spawned.Count; i++)
                {
                    int gap = spawned[i].AvailableDay - spawned[i - 1].AvailableDay;
                    Check(gap >= BaseDemoRaids.MinGap && gap <= BaseDemoRaids.MaxGap, "波次间隔应在 " + BaseDemoRaids.MinGap + "～" + BaseDemoRaids.MaxGap + " 天：实测 " + gap);
                }
                Check(spawned.Last().EnemyArmy.Count(u => u.Kind != SoldierKind.Hero) > spawned.First().EnemyArmy.Count(u => u.Kind != SoldierKind.Hero), "后续波次总量应高于首波");

                // 3) 商人线索：应当指向下一波前后各一天。
                var rumor = BaseDemoRaids.Rumor(s);
                Debug.Log("[Raid] 商人线索（第" + s.Day + "天，下一波第" + s.Demo.Raid.NextDay + "天）：" + rumor);
                Check(rumor.Contains("第") && rumor.Contains("天"), "主线后商人应给出日期线索");

                // 4) 旧档没有 Raid 字段时也能懒初始化，不要求重开存档。
                var legacy = template.Copy();
                legacy.Demo.Stage = BaseDemoStage.Finished;
                legacy.Demo.Raid = null;
                legacy.Campaign.Events.RemoveAll(IsRaid);
                legacy.Day = 30;
                BaseDemoRaids.AdvanceDay(legacy);
                Check(legacy.Demo.Raid != null && legacy.Demo.Raid.Version == BaseDemoRaids.Version, "旧档应在首次推进时补出随机入侵状态");
                BaseDemoRaids.Validate(legacy);

                Debug.Log(failures == 0 ? "[Raid] PASS：主线后周期入侵、波次递增、商人有线索" : "[Raid] FAIL：" + failures + " 项");
                EditorApplication.Exit(failures == 0 ? 0 : 1);
            }
            catch (Exception error)
            {
                Debug.LogError("[Raid] 探针异常：" + error);
                EditorApplication.Exit(1);
            }
        }

        private static bool IsRaid(BaseCampaignEvent e) => e.Id.StartsWith(BaseDemoRaids.IdPrefix, StringComparison.Ordinal);

        private static BaseState Fresh(BaseState template, BaseDemoStage stage)
        {
            var s = template.Copy();
            s.Demo.Stage = stage;
            s.Demo.Raid = null;
            s.Campaign.Events.RemoveAll(IsRaid);
            return s;
        }

        private static void Check(bool ok, string message)
        {
            if (ok) return;
            failures++;
            Debug.LogError("[Raid] 断言失败：" + message);
        }

        private static BaseState Load()
        {
            string path = Path.Combine(Environment.CurrentDirectory, StagePath);
            if (!File.Exists(path)) throw new FileNotFoundException("阶段档不存在：" + path);
            var state = JsonUtility.FromJson<BaseState>(File.ReadAllText(path));
            if (state == null) throw new InvalidOperationException("阶段档解析失败");
            return state;
        }
    }
}

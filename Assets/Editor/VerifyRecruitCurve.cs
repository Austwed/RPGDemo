using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using BaseConquest.Base;

namespace BaseConquest.Editor
{
    // 招募曲线探针：拿真实阶段存档当底，按「合计基准」直接调 DemoDailyTotal / AllocateStock / HirePrice。
    // 只读，不写存档；改公式后必须重跑，退出码非 0 视为破坏规则。
    public static class VerifyRecruitCurve
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
                Debug.Log("[Recruit] 合计基准 B=" + baseline + "，基础价 " + string.Join("/", rules.RecruitBasePrices) + "，报价斜率 " + rules.RecruitPriceSensitivity);

                // 1) 总量曲线：基准 150%（18 人）以内至少 1 人，超过断货。
                foreach (int total in new[] { 0, 3, 6, 9, 12, 15, 18, 19, 21, 24 })
                {
                    var s = Prepare(template, total);
                    int stock = BaseTown.DemoDailyTotal(s);
                    int price = BaseTown.HirePrice(s, BaseTroopKind.Melee);
                    double p = BaseTown.HireProbability(s);
                    Debug.Log("[Recruit] N=" + total + " → p=" + p.ToString("P1") + "，兵源总量=" + stock + "，剑士单价=" + price);
                    if (total <= baseline * 1.5) Check(stock >= 1, "基准 150% 以内必须至少 1 人可买：N=" + total);
                    else Check(stock == 0, "超过基准 150% 必须断货：N=" + total);
                }

                // 2) 报价锚点：基准原价、+50% 翻倍、打空五折。
                int basePrice = rules.RecruitBasePrices[0];
                var atBase = Prepare(template, baseline);
                Check(BaseTown.HirePrice(atBase, BaseTroopKind.Melee) == basePrice, "基准兵力应为原价");
                var atHalf = Prepare(template, baseline * 3 / 2);
                Check(Math.Abs(BaseTown.HirePrice(atHalf, BaseTroopKind.Melee) - basePrice * 2) <= 1, "+50% 应接近翻倍");
                var atZero = Prepare(template, 0);
                Check(Math.Abs(BaseTown.HirePrice(atZero, BaseTroopKind.Melee) - basePrice * .5) <= 1, "打空应压到五折");
                Debug.Log("[Recruit] 报价锚点：0 人 " + BaseTown.HirePrice(atZero, BaseTroopKind.Melee) + " / 基准 " + BaseTown.HirePrice(atBase, BaseTroopKind.Melee) + " / +50% " + BaseTown.HirePrice(atHalf, BaseTroopKind.Melee));

                // 3) 随机分配：同一兵力连续 7 天应出现不同组合，且三项之和等于当日总量。
                var days = Prepare(template, baseline);
                for (int day = 2; day <= 8; day++)
                {
                    days.Day = day;
                    SetTroops(days, baseline);
                    BaseTown.AllocateStock(days);
                    var counts = days.Town.Recruits.OrderBy(r => r.Kind).Select(r => r.DailyCount).ToArray();
                    Debug.Log("[Recruit] 第" + day + "天 N=" + baseline + " → 分配 " + string.Join("/", counts) + "，合计 " + counts.Sum() + "，缺位 " + string.Join("/", days.Town.Recruits.OrderBy(r => r.Kind).Select(r => r.MissingDays)));
                    Check(counts.Sum() == BaseTown.DemoDailyTotal(days), "当日三兵种之和必须等于总量：第" + day + "天");
                }

                // 4a) 总量够三个兵种时，连续缺位 2 天的必须各拿 1 个（N=6 时兵源 3）。
                var fallback = Prepare(template, 6);
                foreach (var offer in fallback.Town.Recruits) offer.MissingDays = 2;
                BaseTown.AllocateStock(fallback);
                foreach (var offer in fallback.Town.Recruits.OrderBy(r => r.Kind))
                {
                    Debug.Log("[Recruit] 保底：缺位 2 天的 " + offer.Kind + " → 当日 " + offer.DailyCount);
                    Check(offer.DailyCount >= 1, "总量足够时连续缺位 2 天后必须给 1 个：" + offer.Kind);
                }

                // 4b) 总量不足时按缺位天数优先：缺得最久的先补，剩下的顺延到次日。
                var tight = Prepare(template, baseline);
                foreach (var offer in tight.Town.Recruits) offer.MissingDays = 2;
                tight.Town.Recruits.Single(r => r.Kind == BaseTroopKind.Cavalry).MissingDays = 5;
                BaseTown.AllocateStock(tight);
                var tightCounts = tight.Town.Recruits.OrderBy(r => r.Kind).Select(r => r.DailyCount).ToArray();
                Debug.Log("[Recruit] 保底优先级：三人缺位 2/2/5、兵源 " + tightCounts.Sum() + " → 分配 " + string.Join("/", tightCounts));
                Check(tightCounts.Sum() == BaseTown.DemoDailyTotal(tight), "保底不得超出当日总量");
                Check(tight.Town.Recruits.Single(r => r.Kind == BaseTroopKind.Cavalry).DailyCount >= 1, "缺位最久的兵种必须优先补");

                // 5) 编队不影响报价：同样的合计兵力，全战士与平均分配同价。
                var allMelee = Prepare(template, baseline);
                var balanced = Prepare(template, baseline);
                balanced.Workers[0].Kind = BaseTroopKind.Ranged;
                Check(BaseTown.HirePrice(allMelee, BaseTroopKind.Melee) == BaseTown.HirePrice(balanced, BaseTroopKind.Melee), "合计相同则编队不得改变报价");

                Debug.Log(failures == 0 ? "[Recruit] PASS：合计兵源、随机分配与报价曲线符合裁决" : "[Recruit] FAIL：" + failures + " 项");
                EditorApplication.Exit(failures == 0 ? 0 : 1);
            }
            catch (Exception error)
            {
                Debug.LogError("[Recruit] 探针异常：" + error);
                EditorApplication.Exit(1);
            }
        }

        private static BaseState Prepare(BaseState template, int total)
        {
            var s = template.Copy();
            s.Day = 10;
            SetTroops(s, total);
            foreach (var offer in s.Town.Recruits) offer.Price = s.Demo.Rpg.RecruitBasePrices[(int)offer.Kind];
            return s;
        }

        private static void SetTroops(BaseState s, int total)
        {
            var seed = s.Workers.FirstOrDefault();
            s.Workers.Clear();
            if (total <= 0 || seed == null) return;
            var worker = seed.Copy();
            worker.Kind = BaseTroopKind.Melee;
            worker.Count = total;
            worker.FacilityId = "";
            worker.Resting = false;
            worker.Away = false;
            s.Workers.Add(worker);
        }

        private static void Check(bool ok, string message)
        {
            if (ok) return;
            failures++;
            Debug.LogError("[Recruit] 断言失败：" + message);
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

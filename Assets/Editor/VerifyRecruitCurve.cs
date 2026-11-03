using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using BaseConquest.Base;

namespace BaseConquest.Editor
{
    // 招募曲线探针：直接调 BaseTown.DemoDailyCount / HirePrice，用真实阶段存档当底，
    // 只改“剑士人数”这一个量，打印 0～7 人的兵源与报价，验证「150% 以内至少 1 人、6 人翻倍、最低五折」。
    public static class VerifyRecruitCurve
    {
        private const string StagePath = "Assets/Resources/DemoStages/defense-male.json";
        private static int failures;

        public static void Run()
        {
            try
            {
                string path = Path.Combine(Environment.CurrentDirectory, StagePath);
                if (!File.Exists(path)) throw new FileNotFoundException("阶段存档不存在：" + path);
                var state = JsonUtility.FromJson<BaseState>(File.ReadAllText(path));
                if (state == null) throw new InvalidOperationException("阶段存档解析失败");

                int baseline = BaseDemoCombat.Baseline(BaseTown.ToBattle(BaseTroopKind.Melee), state.Demo.Rpg);
                int basePrice = state.Town.Recruits.Single(r => r.Kind == BaseTroopKind.Melee).Price;
                Debug.Log("[Recruit] 基准B=" + baseline + "，剑士基准原价=" + basePrice);

                int[] stocks = new int[8], prices = new int[8];
                for (int n = 0; n <= 7; n++)
                {
                    SetMelee(state, n);
                    stocks[n] = BaseTown.DemoDailyCount(state, BaseTroopKind.Melee);
                    prices[n] = BaseTown.HirePrice(state, BaseTroopKind.Melee);
                    Debug.Log("[Recruit] 剑士 " + n + " 人 → 次日兵源 " + stocks[n] + "，单价 " + prices[n]);
                }

                // 用户口径：不超过基准 150%（4 人基准＝6 人）当天至少 1 人可买，超过则断货。
                for (int n = 0; n <= baseline * 3 / 2; n++) Check(stocks[n] >= 1, n + " 人在基准150%以内，兵源不该断");
                Check(stocks[baseline * 3 / 2 + 1] == 0, "超过基准150%应断货");
                // 6 人＝+50%，价格应翻倍；0 人＝-100%，打到五折下限。
                Check(prices[baseline * 3 / 2] == basePrice * 2, "+50% 时价格应翻倍");
                Check(prices[0] == (int)Math.Ceiling(basePrice * 0.5), "兵力打空时价格应压到五折下限");
                Check(prices[baseline] == basePrice, "正好等于基准时应是原价");
                Check(prices[0] == prices[baseline / 2], "跌破五折后不再继续降");

                Debug.Log(failures == 0 ? "[Recruit] PASS：兵源与报价符合规则" : "[Recruit] FAIL：" + failures + " 项");
                EditorApplication.Exit(failures == 0 ? 0 : 1);
            }
            catch (Exception error)
            {
                Debug.LogError("[Recruit] 探针异常：" + error);
                EditorApplication.Exit(1);
            }
        }

        private static void SetMelee(BaseState state, int count)
        {
            var melee = state.Workers.Where(w => w.Kind == BaseTroopKind.Melee).ToList();
            if (melee.Count == 0) throw new InvalidOperationException("阶段存档没有剑士岗位");
            foreach (var bucket in melee) bucket.Count = 0;
            melee[0].Count = count;
        }

        private static void Check(bool ok, string message)
        {
            if (ok) return;
            failures++;
            Debug.LogError("[Recruit] 断言失败：" + message);
        }
    }
}

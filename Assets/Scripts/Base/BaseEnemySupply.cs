using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BaseConquest.Battle;

namespace BaseConquest.Base
{
    public static class BaseEnemySupply
    {
        static readonly string[] Scenes = { "hideout", "defense", "bandit", "second", "ruins", "ruins_repeat" };
        static readonly string[] Classes = { "warrior", "archer", "knight", "mage" };

        public static string Scene(BaseState state, string eventId)
        {
            if (eventId == BaseBanditHideout.EventId) return "hideout";
            if (eventId == BaseDemoDefense.EventId) return "defense";
            if (eventId == BaseDemoDefense.SecondEventId) return "second";
            if (eventId == BaseDemoRuins.EventId) return state.Demo.RuinsDay > 0 ? "ruins_repeat" : "ruins";
            return !string.IsNullOrEmpty(eventId) && state.Campaign.EnemySites.Any(s =>
                s.Id == BaseDemoFinale.Camp && s.AttackEventId == eventId) ? "bandit" : null;
        }

        static bool KnownSlot(string scene, string slot)
        {
            if (scene == "hideout") return slot.StartsWith("unit-", StringComparison.Ordinal)
                && int.TryParse(slot.Substring(5), out int n) && n >= 1 && n <= BaseBanditHideout.EnemyCountCap
                && slot == "unit-" + n;
            if (scene == "ruins" || scene == "ruins_repeat") return slot == "melee-1" || slot == "melee-2";
            return BaseDemoIntro.HasEditableSoldierSlot(scene == "second" ? "bandit" : scene, slot);
        }

        public static void Validate(RpgRules rules)
        {
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var row in rules.EnemySupplies ?? Enumerable.Empty<RpgEnemySupply>())
            {
                if (row == null || !Scenes.Contains(row.Scene) || row.Tier < 0 || row.Tier > 2 || row.Count < 1 || row.Count > 999)
                    throw new ArgumentException("敌军军需行的关卡、数量或等级无效");
                bool exact = !string.IsNullOrEmpty(row.Slot);
                if (exact ? !KnownSlot(row.Scene, row.Slot) || row.Count != 1 || !string.IsNullOrEmpty(row.Profession)
                          : !string.IsNullOrEmpty(row.Profession) && !Classes.Contains(row.Profession))
                    throw new ArgumentException("敌军军需须填写有效槽位或兵种：" + row.Scene + "/" + row.Slot);
                if (!keys.Add(row.Scene + "/" + (exact ? row.Slot : row.Profession + "/" + row.Tier)))
                    throw new ArgumentException("敌军军需配置重复：" + row.Scene);
            }
        }

        static string Slot(BaseEnemyMember member)
        {
            if (member.HasDeployment) return string.IsNullOrEmpty(member.Deployment.AuthoredSlot)
                ? member.Deployment.Id : member.Deployment.AuthoredSlot;
            int i = member.Id.LastIndexOf(":unit:", StringComparison.Ordinal);
            return i >= 0 && int.TryParse(member.Id.Substring(i + 6), NumberStyles.None, CultureInfo.InvariantCulture, out int n)
                ? "unit-" + (n + 1) : member.Id;
        }

        public static void Assign(RpgRules rules, string scene, List<BaseEnemyMember> members)
        {
            if (string.IsNullOrEmpty(scene) || rules?.EnemySupplies == null || members == null) return;
            var soldiers = members.Where(m => m.Kind != SoldierKind.Hero).OrderBy(m => m.Id, StringComparer.Ordinal).ToList();
            if (soldiers.All(m => m.SupplyScene == scene)) return;
            var rows = rules.EnemySupplies.Where(r => r.Scene == scene).ToList();
            var reserved = new HashSet<BaseEnemyMember>();
            foreach (var member in soldiers.Where(m => m.SupplyScene != scene)) member.SupplyTier = 0;
            foreach (var row in rows.Where(r => !string.IsNullOrEmpty(r.Slot)))
            {
                var member = soldiers.FirstOrDefault(m => Slot(m) == row.Slot);
                if (member == null) continue;
                if (member.SupplyScene != scene) member.SupplyTier = row.Tier;
                reserved.Add(member);
            }
            foreach (var row in rows.Where(r => string.IsNullOrEmpty(r.Slot))
                .OrderBy(r => string.IsNullOrEmpty(r.Profession)).ThenByDescending(r => r.Tier))
            {
                var eligible = soldiers.Where(m => !reserved.Contains(m) &&
                    (string.IsNullOrEmpty(row.Profession) || Classes[BaseRpg.Profession(m.Kind)] == row.Profession)).ToList();
                var previous = eligible.Where(m => m.SupplyScene == scene && m.SupplyTier == row.Tier)
                    .Take(row.Count).ToArray();
                foreach (var member in previous) reserved.Add(member);
                int remaining = row.Count - previous.Length;
                foreach (var member in eligible.Where(m => m.SupplyScene != scene).Take(Math.Max(0, remaining)))
                {
                    member.SupplyTier = row.Tier;
                    reserved.Add(member);
                }
            }
            foreach (var member in soldiers) member.SupplyScene = scene;
        }
    }
}

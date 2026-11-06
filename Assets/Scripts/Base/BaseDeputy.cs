using System;
using System.Collections.Generic;
using System.Linq;

namespace BaseConquest.Base
{
    // 副手存档（P42，2026-11-06 裁决）
    //
    // 只存「是否已解锁、谁在当、哪天解锁」。副手本人仍是一个普通 BaseLeader，
    // 不复制属性、不新增第三份人员状态；受伤但存活不撤职，阵亡或离队才需要换人。
    [Serializable] public sealed class BaseDeputyState
    {
        public int Version;
        public bool Unlocked;
        public string LeaderId = "";
        public int UnlockedDay;
        public BaseDeputyState Copy() => (BaseDeputyState)MemberwiseClone();
    }

    // 副手：玩家离营时留守领地的已加入角色。
    //
    // 口径（玩法案 §11.0B／§11.0C）：
    //   ① 首次出发讨伐黑鸦寨时解锁设立，此前没有副手；
    //   ② 主角与随行队伍不能当副手，副手必须是留守人员，可兼任设施负责人；
    //   ③ 首防与第二波是剧情防守，不委托副手（由 BaseDeputyBattle 判定）。
    public static class BaseDeputy
    {
        public const int Version = 1;

        public static bool Enabled(BaseState s) => BaseDemo.Active(s);
        public static BaseDeputyState State(BaseState s) => s?.Demo?.Deputy;

        public static void PrepareOpening(BaseState s)
        {
            if (Enabled(s)) s.Demo.Deputy = new BaseDeputyState { Version = Version };
        }

        // 旧档没有这一段：第一次用到时补一份，不要求重开存档。
        public static BaseDeputyState Ensure(BaseState s)
        {
            if (!Enabled(s)) return null;
            if (s.Demo.Deputy == null || s.Demo.Deputy.Version != Version)
                s.Demo.Deputy = new BaseDeputyState { Version = Version };
            return s.Demo.Deputy;
        }

        public static bool Unlocked(BaseState s) => Enabled(s) && s.Demo.Deputy != null && s.Demo.Deputy.Unlocked;

        // 首次出发讨伐黑鸦寨时调用；重复调用无副作用。
        public static void Unlock(BaseState s)
        {
            var d = Ensure(s);
            if (d == null || d.Unlocked) return;
            d.Unlocked = true;
            d.UnlockedDay = s.Day;
        }

        public static BaseLeader Leader(BaseState s)
        {
            if (!Unlocked(s)) return null;
            string id = s.Demo.Deputy.LeaderId;
            return string.IsNullOrEmpty(id) ? null : s.Leaders.FirstOrDefault(l => l.Id == id);
        }

        public static bool Available(BaseState s) => Problem(s) == "";

        // 为什么现在没有可用副手；空串表示可用。
        public static string Problem(BaseState s)
        {
            if (!Enabled(s)) return "本局没有副手";
            if (!Unlocked(s)) return "讨伐黑鸦寨出发前还没有副手";
            var deputy = Leader(s);
            if (deputy == null) return "还没有指定副手";
            if (!BaseAdventure.Alive(deputy)) return BasePersonnel.LeaderName(deputy) + "已不在队中，请重新指定副手";
            if (deputy.Health <= 0) return BasePersonnel.LeaderName(deputy) + "当前倒地，不能主持防守";
            if (deputy.Away) return BasePersonnel.LeaderName(deputy) + "正在外出的队伍里，不能主持防守";
            if (BaseAdventure.Contains(s, deputy.Id)) return BasePersonnel.LeaderName(deputy) + "随主角行动，不能主持防守";
            return "";
        }

        // 可选副手：留在领地的已加入角色（不含主角、随行队伍与外出人员）。
        public static IEnumerable<BaseLeader> Candidates(BaseState s)
        {
            if (!Enabled(s)) yield break;
            foreach (var leader in s.Leaders)
            {
                if (leader.Id == s.PlayerLeaderId) continue;
                if (!BaseAdventure.Alive(leader) || leader.Health <= 0) continue;
                if (leader.Away || BaseAdventure.Contains(s, leader.Id)) continue;
                yield return leader;
            }
        }

        public static string ChooseProblem(BaseState s, string id)
        {
            if (!Unlocked(s)) return "讨伐黑鸦寨出发前还没有副手";
            if (string.IsNullOrEmpty(id)) return "请选择一名留守角色";
            var leader = s.Leaders.FirstOrDefault(l => l.Id == id);
            if (leader == null) return "该角色不在队中";
            if (leader.Id == s.PlayerLeaderId) return "主角不能当自己的副手";
            if (!BaseAdventure.Alive(leader) || leader.Health <= 0) return "该角色当前无法主持领地";
            if (leader.Away) return "该角色正在外出的队伍里";
            if (BaseAdventure.Contains(s, leader.Id)) return "随主角行动的成员不能兼任副手";
            return "";
        }

        // 指定／更换副手。允许重设同一人（幂等）。
        public static void Set(BaseState s, string id)
        {
            string problem = ChooseProblem(s, id);
            if (problem != "") throw new InvalidOperationException(problem);
            Ensure(s).LeaderId = id;
        }

        public static void Clear(BaseState s)
        {
            if (Unlocked(s)) s.Demo.Deputy.LeaderId = "";
        }

        // 副手可以兼任设施负责人；离开岗位时由常规分配逻辑处理，这里不改岗位。
        public static void Validate(BaseState s)
        {
            var d = State(s);
            if (d == null)
            {
                if (Enabled(s)) throw new ArgumentException("缺少副手记录");
                return;
            }
            if (d.Version != Version) throw new ArgumentException("副手数据版本非法");
            if (d.UnlockedDay < 0 || d.UnlockedDay > s.Day) throw new ArgumentException("副手解锁日期非法");
            if (!d.Unlocked && (d.UnlockedDay != 0 || !string.IsNullOrEmpty(d.LeaderId))) throw new ArgumentException("未解锁副手不能有解锁日或人选");
            if (string.IsNullOrEmpty(d.LeaderId)) return;
            if (!d.Unlocked) throw new ArgumentException("副手人选必须先解锁");
            var leader = s.Leaders.FirstOrDefault(l => l.Id == d.LeaderId);
            if (leader == null) throw new ArgumentException("副手不在队伍中");
            if (leader.Id == s.PlayerLeaderId) throw new ArgumentException("主角不能兼任副手");
        }
    }
}

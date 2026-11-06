using System;
using System.Linq;

namespace BaseConquest.Base
{
    public sealed partial class BaseHud
    {
        // 副手面板（P42）：只做选择与状态说明，正式版式等原型定稿。
        private void OpenDeputy()
        {
            var s = owner.State;
            if (s == null) return;
            BeginPanel("副手 · 留守主持");
            RowButton("返回", -290, 270, OpenHome);
            RowButton("规则 i", 0, 270, () => RuleInfo("副手规则",
                "副手是主角离营时留在领地的已加入角色，可兼任设施负责人。\n\n"
                + "· 首次出发讨伐黑鸦寨时解锁设立，此前没有副手。\n"
                + "· 袭击可由玩家亲自迎战，也可交给副手；副手战用同一套真实战斗与结算规则，但主角与随行队伍不参战，只使用领地内可用的士兵。\n"
                + "· 首防和第二波是剧情防守，不能委托。\n"
                + "· 副手受伤但存活不撤职，阵亡或离队才需要更换。玩家可观看副手战、暂停、调速和调整策略。", OpenDeputy));
            row += 62;

            var deputy = BaseDeputy.Leader(s);
            Line("当前副手：" + (deputy == null ? "未指定" : BasePersonnel.LeaderName(deputy) + " · " + BasePersonnel.State(s, deputy)), 54, 22);
            string problem = BaseDeputy.Problem(s);
            Line(problem == "" ? "副手可以主持领地防守。" : problem, 58, 18);
            Line("主角和随行队伍不能兼任副手；副手可以同时管理设施。", 52, 18);

            var candidates = BaseDeputy.Candidates(s).ToArray();
            if (candidates.Length == 0) Line("当前没有可留守的已加入角色：先让随行成员留在营地，或招募新伙伴。", 62, 18);
            foreach (var leader in candidates)
            {
                string id = leader.Id;
                bool current = deputy != null && deputy.Id == id;
                RowButton((current ? "● " : "　 ") + BasePersonnel.LeaderName(leader) + " · " + BasePersonnel.State(s, leader), 0, 800, () =>
                {
                    owner.Change(next => BaseDeputy.Set(next, id), BasePersonnel.LeaderName(leader) + "已担任副手");
                    OpenDeputy();
                });
                row += 58;
            }
            if (deputy != null)
            {
                RowButton("撤销副手（暂不设）", 0, 800, () => { owner.Change(next => BaseDeputy.Clear(next), "已撤销副手，离营前请重新指定"); OpenDeputy(); });
                row += 58;
            }
            EndPanel();
        }
    }
}

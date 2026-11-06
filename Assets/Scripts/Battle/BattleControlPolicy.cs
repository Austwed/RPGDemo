using System;

namespace BaseConquest.Battle
{
    // 战斗控制权限（2026-11-06 P36）。
    //
    // 用途：远程防守只允许「查看＋改策略」，不能下达即时命令。守卫必须放在 BattleSimulation 的
    // 命令入口上，而不是只隐藏按钮——键盘、鼠标、触控以及后续的远程通道共用同一道闸。
    // 默认 Local 全开，所以现有玩法行为不变。
    [Serializable]
    public sealed class BattleControlPolicy
    {
        public bool View = true;      // 打开详情、暂停、调速
        public bool Strategy = true;  // 改队长战术策略
        public bool Order = true;     // 移动、集火、驻守、撤退
        public bool Skill = true;     // 释放战技
        public bool Range = true;     // 调整驻守／预警／追击／率领范围

        public static BattleControlPolicy Local => new BattleControlPolicy();
        public static BattleControlPolicy RemoteDefense => new BattleControlPolicy { Order = false, Skill = false, Range = false };

        // 统一成字符串判据，便于后续远程通道复用同一套检查。
        public bool Allows(string action)
        {
            if (action == "view") return View;
            if (action == "strategy") return Strategy;
            if (action == "order") return Order;
            if (action == "skill") return Skill;
            if (action == "range") return Range;
            return false;
        }
        public BattleControlPolicy Copy() => (BattleControlPolicy)MemberwiseClone();
    }
}

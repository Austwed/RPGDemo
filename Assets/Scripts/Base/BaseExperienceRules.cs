using System;
using System.Collections.Generic;
using System.Linq;
namespace BaseConquest.Base
{
    public static class BaseDepartureAdvice
    {
        public static BaseLeader[] Candidates(BaseState s,string location,bool disabled)
            =>disabled||s==null||(location!="world"&&location!="outpost")||s.Adventure.Members.Count>=BaseAdventure.Capacity
                ? Array.Empty<BaseLeader>() : s.Leaders.Where(l=>BaseAdventure.ChangeProblem(s,l.Id,true)==""&&BaseDevelopment.CanFight(l)&&BaseAdventure.Region(l)=="base").ToArray();
    }
    public static class BaseExperienceRules
    {
        public static IReadOnlyList<(string Title, string Text)> Help { get; } = new (string, string)[]
        {
            ("游戏操作",
                "WASD、方向键或摇杆移动，靠近后按 E 或点交互。B 打开小队背包，N 打开任务，M 查看领地地图。顶栏各资源可点开查看明细。\n\n" +
                "领地地图的蓝点是地表出入口，黄点是遗迹出入口。点击已开放的标记可前往入口附近，不结日。\n\n" +
                "点选物品可查看当前操作，拖放可转交、换装或移动。仓库页双击未穿戴物品可整格存取。"),
            ("经营与日结",
                "粮田产粮，工坊消耗原料生产军需。每处设施可任命一名管理员。村民补空岗，每人贡献半份工作量。\n\n" +
                "在营、随行和外出的士兵按兵种及状态计入口粮，队长不计入口粮。口粮需求见顶栏粮食明细。缺粮降低生产和攻击。\n\n" +
                "主动休息和耗日行军推进日期、产出、恢复与新日口粮。驻地伤兵与存活的受伤队长会恢复生命。入侵抵达后先迎敌再结日。"),
            ("人员与行军",
                "常驻小队由主角和最多两位伙伴组成。队长的编队预设记录带兵偏好，可按人数调整或实名点选。棕色姓名表示招降人员。\n\n" +
                "留守人员继续按各自据点分工。同行者到达灰岩营地或白桦村后在当地驻扎，经黑鸦寨中转时仍留在出征队。\n\n" +
                "生产优先保留所需留营人手并估算三天经营，作战优先可抽调岗位。生命低于 30% 的士兵不能工作或主动出战，仍可守家。\n\n" +
                "可在营帐请离非剧情队长，其装备和物品会退回基地仓库。"),
            ("备战、军需与工事",
                "先选择参战队长和士兵，再到部署区落位。先放队长，再放其所属士兵。主角亲自迎战时必须落位。\n\n" +
                "每场选一档军需。实际部署的普通兵每人一件，队长使用个人装备。开战时锁定军需，战斗结算时扣除实际部署的用量。\n\n" +
                "迎敌前可以在合适的平地布置陷阱或沙袋。陷阱接敌触发，沙袋拖慢通过的单位。"),
            ("战斗与战果",
                "可暂停下令。选中队长后，单击敌人命令队长攻击，双击命令小队集火。直接命令优先于队长策略。命中掷骰结果为 10 时暴击，为 1 时失手。\n\n" +
                "首防坚守到时或击倒半数来敌后可追击撤军，第二波须击败全部来敌。战斗和结算保留当天，战利品与俘虏按战果清单处理。\n\n" +
                "古裔与异能者免受古窟侵蚀。副本首通奖励免体力，重战或扫荡后领取重复奖励时扣体力。\n\n" +
                "简单难度提高我方造成的伤害、降低我方受到的伤害。困难难度提高我方受到的伤害。"),
            ("装备、训练与交易",
                "个人装备可换装比较，仓库与人物背包各有使用范围。交易、修复或训练前查看界面的报价、材料和资格。\n\n" +
                "训练最高到 9 级，升入 3、6、9 级时用高阶兵书。开始训练扣书，取消后已扣的兵书不会返还。\n\n" +
                "铁匠处可投入锻造石重铸，品质可能上升、保持或下降，传奇装备已达最高品质。"),
        };
    }
}

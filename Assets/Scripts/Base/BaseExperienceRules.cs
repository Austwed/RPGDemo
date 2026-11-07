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
        public const string Construction="固定设施通过剧情开放升级或功能解锁，需要营造入门条件、营造图册1本、原料10和钱币20；每次实际确认后消耗，道具用完须从剧情再取得。测试首次接入默认1本图册，开发者模式可追加。\n\n粮田升级：岗位＋2。工坊升级：岗位＋2，并开放二级剑／弓／甲；二级每件消耗一级两倍原料和两倍工作量，按等级与配方分别保存小数进度。当前首批升级至2级。固定设施不能拆除。\n\n迎敌战场接近固定工事解锁后，预设两格变成永久掩体，格上单位受到的远程伤害×0.75，双方均可利用；基地瞭望塔原有守方信息优势保持。解锁同时开放普通路障自由建造。\n\n在固定工事处点击“开启建造模式”，左侧选择木路障或拆除工具，再连续点击／拖划格子；每个木路障原料3、耐久60，阻挡移动与射击。和平时可拆除普通路障，无材料返还；不能覆盖设施、人物、入口、部署区、掩体或临时地形，每区域最多50处。固定设施没有拆除功能。\n\n敌军有路可绕；进攻单位在破坏路线更合适或无路可走时攻击敌对路障，不攻击己方路障，不越过队长进攻范围。耐久归零恢复原空地；受损和摧毁写回同一地图及存档。建造与升级仅在和平状态开放，不推进日期或重置当天生产资格。";
        public const string Home="先处理到期入侵与实际战损，再补位、生产与恢复，推进日期并扣新日口粮。待处理入侵时，预览是战前估算；主角未被救出则结束游戏，不继续生产。缺料未完成的整数工作点不积攒。";
        public const string Personnel="每处设施一名管理员，一人只能负责一处；工人位有限。随行者不兼任管理员。手动调配锁定岗位意向，解锁后由发展策略安排。全员每天自动恢复生命，满血停止。士兵生命低于 30% 不能工作也不能主动出战；遇袭守家例外，还能行动就能迎战。队长不受血量阈值限制。";
        public const string Storage="全体己方人员均消耗口粮，包含在岗、闲置和驻外人员。量产军需存入统一仓库供普通兵使用；个人装备与道具走人物背包和地图仓库。基地仓库不限格数，每页24格；人物背包20格（5列×4行）。仓库页双击未穿戴物品可整格互转，拖放可指定位置，仍受空格、专属归属及和平状态限制。";
        public const string Production="粮田和工坊在休息日结时生产。管理员效率与当天有效工人共同决定产能；缺粮影响生产，缺料不透支。工坊各等级／配方分别保留小数进度，日末只扣完成产品的原料；二级每件使用两倍工作量和原料。";
        public const string Operations="WASD／方向键或摇杆移动\nE：接近后交互\nB：小队背包／关闭\nN：任务\n点击物品：选中\n拖放：移动、转交或换装\n仓库页双击：整格存取\n家：预览并休息一天\n左上入侵信息：敌情\n菜单：设置、帮助、开发档";
        public const string Rules="日结：入侵处理后生产、恢复，再扣新日口粮。\n救援：倒地角色须实际救出；主角未救出将结束游戏。\n出征：生产优先保留三天经营条件；作战优先尽量满足编制。\n训练：最高9级，3／6／9级突破；训练停工，取消不退兵书。\n重铸：投入锻造石，品质升／留／降；传奇不可重铸。\n烈酒当日有效；清酒留到下一场，强覆盖弱须确认。\n占领据点停止后续出兵，在途敌军仍会抵达。";
        public const string Terms="常驻小队：主角与最多两名伙伴。\n编制：队长期望带兵组成，不提前占兵。\n军需：出战时实际分配并锁定的量产装备。\n生产资格：当天可在日结参与生产。\n缺编：实际可带兵低于期望编制。\n突破：升入3／6／9级前使用高阶兵书。\n遗失物：败战未带回人物的物品，可反击追回。\n品质：破损至传奇的六档装备等级。";
    }
}

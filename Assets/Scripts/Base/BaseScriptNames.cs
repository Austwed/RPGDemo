namespace BaseConquest.Base
{
    // 剧本专名与人物名的唯一来源（P3 标识迁移，依据《DEMO剧情文案落地规格》2.1／2.2／2.3）。
    // 规则：禁止在别处硬写人名与地名，一律从这里取；改名字只改本文件。
    // 美术未到位前，Portrait() 一律映射到现有可用头像资源；新美术到位后只改 Portrait() 一处。
    public static class BaseScriptNames
    {
        // ===== 地名（规格 2.2）=====
        public const string Village="白桦村";       // 旧泛称：故乡／村子
        public const string Camp="断流滩";          // 旧泛称：营地
        public const string Town="灰岩镇";          // 旧泛称：城镇
        public const string Ruins="沉眠古窟";       // 旧泛称：遗迹
        public const string Bandit="黑鸦寨";        // 旧泛称：匪寨／阻路土匪营地／敌营

        // ===== 主角与搭档（规格 2.1 方案 C）=====
        // art 取 player（男）／female（女），与 BaseDemoIntro.PlayerArt/CaptainArt 一致。
        public static string HeroName(string art)=>art=="female"?"秦昭":"林烬";
        public static string HeroName(BaseState s)=>HeroName(BaseDemoIntro.PlayerArt(s));
        // 搭档＝未选的那一位，取其 art 后同样走 HeroName。
        public static string PartnerName(BaseState s)=>HeroName(BaseDemoIntro.CaptainArt(s));
        // 旧档兼容：旧档把搭档名写死为「搭档」，读档时按所选性别回填本名，不回写存档字段。
        public const string LegacyPartner="搭档";

        // ===== 人物名（规格 2.2／2.3，头像列的 key 见 Portrait）=====
        public const string Lord="霍恩爵士";        // 城镇领主，旧称「领主」
        public const string Galan="迦兰";           // 遗迹控制者，旧称「异族队长」
        public const string Batu="巴图";            // 首防敌方队长
        public const string Chief="大当家";         // 黑鸦寨之主
        public const string Huoya="豁牙";           // 大当家副手
        public const string LaoZhou="老周";
        public const string ChunShen="春婶";
        public const string Blacksmith="老铁匠巴隆"; // 阿铸之父
        public const string Recruiter="募兵官雷恩";
        public const string InnKeeper="旅店麦格";
        public const string Merchant="行商";
        public const string Scout="营地斥候";
        public const string Guard="镇卫兵";            // 城镇闲聊
        public const string Peddler="行商老莫";        // 商铺街闲聊
        public const string Girl="镇民小芸";           // 公园角喂鸟的镇民

        // ===== 头像占位（规格 2.3）=====
        // 现有真实资源（Assets/Resources/PixelDemo/Characters，2026-11-02 复核）。全项目只有三类单位：
        // ① 三职业角色（四向+移动+攻击）：player／female／ahe／mounted-azhu／galan／chief／batu／huoya；
        // ② 三职业士兵（四向+移动+攻击，不分敌我阵营）：saber／archer／rider（图集文件沿用旧批次名 infantry／cavalry，由 PixelDemoArt.TroopId 归一）；
        // ③ 交互 NPC（只有四向，移动按作用分配）：laozhou／chunshen／merchant／villager／horn／chenan。
        // enemy-captain 不是兵种，是野外随机战队长的兜底外观（wilderness → enemy-captain）。
        // 其余缺图的角色先用气质相近的图占位，新美术到位后只改这里的返回值即可全局生效。
        public static string Portrait(string role)
        {
            switch(role)
            {
                case "hero":return "player";
                case "heroine":return "female";
                case "laozhou":return "laozhou";     // 已有专属图
                case "chunshen":return "chunshen";   // 已有专属图
                case "merchant":case "peddler":return "merchant";
                case "lord":return "horn";
                case "blacksmith":return "chenan";   // TODO 美术：baron
                case "galan":return "galan";
                case "batu":return "batu";
                case "azhu":return "azhu";           // 骑乘阿铸的头像（portrait-azhu-v1，已换新骑乘版）
                case "chief":return "chief";
                case "huoya":return "huoya";
                default:return "villager";           // 春婶／募兵官／旅店／镇民 兜底
            }
        }
    }
}

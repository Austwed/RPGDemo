using UnityEngine;

namespace BaseConquest.Presentation
{
    /// <summary>
    /// UI 规格常量，唯一口径见《基地征服RPG-UI实施说明书（Unity）》§2.4／§4。
    /// 坐标按 1280×720 逻辑画布，1 单位＝1 逻辑 px；改这里等于改全工程。
    /// </summary>
    public static class DemoUiMetrics
    {
        // 栅格与间距（§2.4）
        public const float Grid = 8;
        public const float MarginPage = 32, MarginSection = 24, MarginInline = 16;
        public const float GapCard = 16, GapInline = 12;

        // 字阶 5 档（§4.2）；行高 1.4，字重 400／600
        public const int FontCaption = 12, FontBody = 14, FontSubhead = 18, FontPanelTitle = 24, FontHero = 40;
        public const float LineHeight = 1.4f;

        // 圆角 3 档（源图像素值，写进 Sprite 的九宫格边界，不是运行时数值）
        public const int RadiusSmall = 4, RadiusCard = 8, RadiusPanel = 12;

        // 点击区下限（§2.4）：视觉尺寸与热区可分离，小图标配透明扩热区
        public const float HitPc = 44, HitTouch = 64;

        // 顶栏／底栏，沿用 ExplorationHudLayout，勿改
        public const float HeaderHeight = 112, FooterHeight = 64;

        public static bool IsFontStep(int size)
            => size == FontCaption || size == FontBody || size == FontSubhead || size == FontPanelTitle || size == FontHero;
    }
}

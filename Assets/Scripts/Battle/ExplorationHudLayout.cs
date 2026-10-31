using System;
namespace BaseConquest.Battle
{
    // 与探索Canvas的Expand缩放保持一致；地图相机和文字层共用边界。
    public static class ExplorationHudLayout
    {
        public const float HeaderHeight = 112, FooterHeight = 64;
        public static float Scale(float screenWidth, float screenHeight) => Math.Max(.01f, Math.Min(screenWidth / 1280f, screenHeight / 720f));
        public static float Bottom(float screenWidth, float screenHeight) => (FooterHeight + 6) * Scale(screenWidth, screenHeight) / Math.Max(1, screenHeight);
        public static float Top(float screenWidth, float screenHeight) => 1 - (HeaderHeight + 12) * Scale(screenWidth, screenHeight) / Math.Max(1, screenHeight);
    }
}

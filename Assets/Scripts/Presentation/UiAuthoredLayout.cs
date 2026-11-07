using System.Collections.Generic;

namespace BaseConquest.Presentation
{
    /// <summary>
    /// 表现权总开关。Enabled = true 时，试点页面的 prefab 是版式与配色的唯一来源：
    /// C# 只负责节点命名（BindingName）、文本值、可见性、交互状态与事件回调，不再决定位置、尺寸、颜色、字体。
    ///
    /// 回滚：把 Enabled 改成 false 并重新编译，即完整回到 C# 硬编码版式的旧路径（不需要删 prefab）。
    /// 试点范围由 Owns 限定，未列入的页面一律走旧路径，避免一次改动波及全工程。
    /// </summary>
    public static class UiAuthoredLayout
    {
        public static bool Enabled = true;

        public const string Folder = "Presentation/AuthoredUI/";

        private static readonly HashSet<string> Pages = new HashSet<string> { "Page_party_inventory" };
        private static readonly HashSet<string> SharedCards = new HashSet<string> { "PartyMemberCard" };

        /// <summary>打开试点页面的构建期间为 true，决定共享人物卡是否也交给 prefab 主导。</summary>
        public static bool PilotActive;

        public static bool Owns(string key)
            => Enabled && !string.IsNullOrEmpty(key)
               && (Pages.Contains(key) || (PilotActive && SharedCards.Contains(key)));
    }
}

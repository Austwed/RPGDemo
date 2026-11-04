namespace BaseConquest.Base
{
    public sealed partial class BaseHud
    {
        // P34：任务列表只做展示与选择追踪；任务内容来自 BaseQuestLog，列表页与常驻栏同源。
        private void OpenDemoQuestList()
        {
            var s = owner.State;
            activeObject = "quests"; interactionObject = "";
            BeginPanel("任务列表 · 主线与支线", "quests");
            string section = "";
            foreach (var entry in BaseQuestLog.Entries(s))
            {
                string head = entry.Main ? "主线任务" : "支线任务";
                if (head != section) { section = head; Line(head, 50, 25); }
                Line(entry.Title + "  【" + entry.Status + "】", 48, 23);
                Line(entry.Goal + "\n奖励：" + entry.Reward, 88, 20);
                if (!entry.Trackable) { Line("已完成，不再追踪。", 34, 17); continue; }
                string id = entry.Id; bool tracked = BaseQuestLog.TrackedId(s) == id;
                RowButton(tracked ? "● 取消追踪 · " + entry.Title : "○ 追踪 · " + entry.Title, 0, 800, () =>
                {
                    if (!owner.Change(next => { if (!BaseQuestLog.ToggleTrack(next, id)) throw new System.InvalidOperationException("任务状态已变化"); }, tracked ? "已取消追踪" : "已显示在常驻任务栏")) return;
                    OpenDemoQuestList();
                });
                row += 54;
            }
            RowButton("关闭任务列表", 0, 800, ClosePanel); row += 60; EndPanel();
        }
        // 常驻任务栏：没追踪时整条隐藏；折叠只留标题，点一下展开目标。
        private void RefreshTrackedQuest()
        {
            if (trackedQuest == null) return;
            var s = owner.State;
            var entry = s == null || PanelOpen ? null : BaseQuestLog.Tracked(s);
            trackedQuest.transform.parent.gameObject.SetActive(entry != null);
            if (entry == null) return;
            bool collapsed = BaseQuestLog.Collapsed(s);
            trackedQuest.text = (collapsed ? "任务 ▸ " : "任务 ▾ ") + entry.Title + (collapsed ? "" : "\n" + entry.Goal);
        }
        private void ToggleTrackedQuestBar()
        {
            var s = owner.State;
            if (s == null || BaseQuestLog.Tracked(s) == null) return;
            owner.Change(next => BaseQuestLog.SetCollapsed(next, !BaseQuestLog.Collapsed(next)), "");
        }
    }
}

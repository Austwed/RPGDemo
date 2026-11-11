using System;
using System.Collections.Generic;
using System.Linq;
using BaseConquest.Presentation;
using UnityEngine;
using UnityEngine.UI;

namespace BaseConquest.Base
{
    public sealed partial class BaseHud
    {
        // 主菜单任务页按主线和支线列出任务日志。
        private string questTab = "tasks";

        private void OpenDemoQuestPanel()
        {
            var s = owner.State;
            activeObject = "quests"; interactionObject = "";
            BeginPanel("任务与成就", "quests");
            QuestTab("tasks", "任务", 0);
            QuestTab("achievement", "成就", 1);
            row += 48;
            if (questTab == "achievement") { AchievementBody(s); EndPanel(); return; }
            var entries = BaseQuestLog.Entries(s);
            QuestSection("主线", entries.Where(entry => entry.Main).ToList());
            QuestSection("支线", entries.Where(entry => !entry.Main).ToList());
            EndPanel();
        }

        // 选中的页签用原型的暖金色，动态任务仍由同一份任务日志提供。
        private void QuestTab(string key, string name, int index)
        {
            bool active = questTab == key;
            var button = MakeButton(content, name, new Vector2(-330 + index * 96, -18), new Vector2(88, 34), () => { questTab = key; OpenDemoQuestPanel(); }, new Vector2(.5f, 1));
            var label = button.GetComponentInChildren<Text>();
            if (label != null) { label.fontSize = DemoUiMetrics.FontBody; label.fontStyle = active ? FontStyle.Bold : FontStyle.Normal; label.color = active ? DemoUiTheme.Current.Ink : DemoUiTheme.Current.Muted; }
            if (active) button.GetComponent<Image>().color = DemoUiTheme.Current.Gold;
        }

        // 一个分组：小标题 + 若干任务行；空分组给一句说明，不留空档。
        private void QuestSection(string title, List<BaseQuestEntry> entries)
        {
            var head = Label(content, title, new Vector2(0, -row - 9), new Vector2(850, 18), DemoUiMetrics.FontCaption, TextAnchor.MiddleLeft, new Vector2(.5f, 1));
            head.color = DemoUiTheme.Current.Muted;
            row += 26;
            if (entries.Count == 0)
            {
                var empty = Label(content, "当前没有" + title + "任务。", new Vector2(0, -row - 9), new Vector2(850, 18), DemoUiMetrics.FontCaption, TextAnchor.MiddleLeft, new Vector2(.5f, 1));
                empty.color = DemoUiTheme.Current.Muted;
                row += 28;
                return;
            }
            foreach (var entry in entries) QuestRow(entry);
            row += 8;
        }

        private void QuestRow(BaseQuestEntry entry)
        {
            var theme = DemoUiTheme.Current;
            var bar = Solid("QuestRow", content, new Vector2(.5f, 1), new Vector2(0, -row - 29), new Vector2(850, 58), theme.Inset);
            var outline = bar.gameObject.GetOrAdd<Outline>();
            outline.effectColor = theme.Line;
            outline.effectDistance = new Vector2(1, -1);
            var name = Label(bar, entry.Title, new Vector2(292, -8), new Vector2(560, 20), DemoUiMetrics.FontBody, TextAnchor.MiddleLeft, new Vector2(0, 1));
            name.color = entry.Done ? theme.Muted : theme.Ink;
            name.fontStyle = FontStyle.Bold;
            var goal = Label(bar, entry.Goal, new Vector2(292, -30), new Vector2(560, 18), DemoUiMetrics.FontCaption, TextAnchor.MiddleLeft, new Vector2(0, 1));
            goal.color = theme.Muted;
            var state = Label(bar, entry.Status, new Vector2(-132, -20), new Vector2(240, 20), DemoUiMetrics.FontCaption, TextAnchor.MiddleRight, new Vector2(1, 1));
            state.color = entry.Done ? theme.Muted : theme.Copper;
            row += 66;
        }

        // 常驻任务栏：没追踪时整条隐藏；折叠只留标题。
        private void RefreshTrackedQuest()
        {
            if (trackedQuest == null) return;
            var s = owner.State;
            var entry = s == null || PanelOpen ? null : BaseQuestLog.Tracked(s);
            trackedQuest.transform.parent.gameObject.SetActive(entry != null);
            if (entry == null) return;
            bool collapsed = BaseQuestLog.Collapsed(s);
            string prefix = collapsed ? "任务 ▸ " : "任务 ▾ ";
            trackedQuest.text = prefix + entry.Title + (collapsed ? "" : "\n" + entry.Goal);
        }
        private void ToggleTrackedQuestBar()
        {
            var s = owner.State;
            if (s == null || BaseQuestLog.Tracked(s) == null) return;
            owner.Change(next => BaseQuestLog.SetCollapsed(next, !BaseQuestLog.Collapsed(next)), "");
        }
    }
}

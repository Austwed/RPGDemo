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
        // P34：任务列表只做展示与选择追踪；任务内容来自 BaseQuestLog，列表页与常驻栏同源。
        // 版式照原型 `case 'quests'`：面板头两个页签「任务／成就」，任务按主线／支线分组，每条一行。
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
            QuestSection(s, "主线", entries.Where(entry => entry.Main).ToList());
            QuestSection(s, "支线", entries.Where(entry => !entry.Main).ToList());
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
        private void QuestSection(BaseState s, string title, List<BaseQuestEntry> entries)
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
            foreach (var entry in entries) QuestRow(s, entry);
            row += 8;
        }

        // 一条任务：左侧标题＋目标，右侧追踪态；整行可点，已完成的行不再可点。
        private void QuestRow(BaseState s, BaseQuestEntry entry)
        {
            bool tracked = BaseQuestLog.TrackedId(s) == entry.Id;
            bool clickable = entry.Trackable;
            var theme = DemoUiTheme.Current;
            var bar = Solid("QuestRow", content, new Vector2(.5f, 1), new Vector2(0, -row - 29), new Vector2(850, 58), tracked ? Color.Lerp(theme.Panel, theme.Gold, .36f) : theme.Inset);
            var outline = bar.gameObject.GetOrAdd<Outline>();
            outline.effectColor = tracked ? theme.Copper : theme.Line;
            outline.effectDistance = new Vector2(1, -1);
            if (clickable)
            {
                var button = bar.gameObject.GetOrAdd<Button>(); button.targetGraphic = bar.GetComponent<Image>(); button.transition = Selectable.Transition.None;
                button.onClick.AddListener(() =>
                {
                    if (!owner.Change(next => { if (!BaseQuestLog.ToggleTrack(next, entry.Id)) throw new InvalidOperationException("任务状态已变化"); }, tracked ? "已取消追踪" : "已显示在常驻任务栏")) return;
                    OpenDemoQuestPanel();
                });
            }
            var name = Label(bar, entry.Title, new Vector2(312, -8), new Vector2(600, 20), DemoUiMetrics.FontBody, TextAnchor.MiddleLeft, new Vector2(0, 1));
            name.color = clickable ? DemoUiTheme.Current.Ink : DemoUiTheme.Current.Muted;
            name.fontStyle = FontStyle.Bold;
            var goal = Label(bar, entry.Goal, new Vector2(322, -30), new Vector2(620, 18), DemoUiMetrics.FontCaption, TextAnchor.MiddleLeft, new Vector2(0, 1));
            goal.color = DemoUiTheme.Current.Muted;
            var state = Label(bar, tracked ? "取消追踪" : entry.Done ? "已完成" : "追踪", new Vector2(-72, -20), new Vector2(120, 20), DemoUiMetrics.FontCaption, TextAnchor.MiddleRight, new Vector2(1, 1));
            state.color = tracked ? theme.Ink : entry.Done ? theme.Muted : theme.Copper;
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
            string prefix = trackedQuestArrow != null ? "任务 · " : collapsed ? "任务 ▸ " : "任务 ▾ ";
            trackedQuest.text = prefix + entry.Title + (collapsed ? "" : "\n" + entry.Goal);
            if (trackedQuestArrow != null) trackedQuestArrow.text = collapsed ? "＋" : "−";
        }
        private void ToggleTrackedQuestBar()
        {
            var s = owner.State;
            if (s == null || BaseQuestLog.Tracked(s) == null) return;
            owner.Change(next => BaseQuestLog.SetCollapsed(next, !BaseQuestLog.Collapsed(next)), "");
        }
    }
}

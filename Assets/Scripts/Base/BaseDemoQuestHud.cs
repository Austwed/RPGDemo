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

        // 页签：原型是面板头里的两个小胶囊，选中填色。这里用字重＋铜色下划线表达，不额外造色块。
        private void QuestTab(string key, string name, int index)
        {
            bool active = questTab == key;
            var button = MakeButton(content, name, new Vector2(-330 + index * 96, -18), new Vector2(88, 34), () => { questTab = key; OpenDemoQuestPanel(); }, new Vector2(.5f, 1));
            var label = button.GetComponentInChildren<Text>();
            if (label != null) { label.fontSize = DemoUiMetrics.FontBody; label.fontStyle = active ? FontStyle.Bold : FontStyle.Normal; label.color = active ? DemoUiTheme.Current.Ink : DemoUiTheme.Current.Muted; }
            if (active) Solid("TabUnderline", button.transform, new Vector2(.5f, 0), new Vector2(0, 2), new Vector2(72, 2), DemoUiTheme.Current.Copper);
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
            var bar = Box("QuestRow", content, new Vector2(.5f, 1), new Vector2(0, -row - 29), new Vector2(850, 58), tracked ? new Color(.22f, .26f, .19f, .78f) : new Color(.16f, .16f, .14f, .72f));
            var outline = bar.gameObject.GetOrAdd<Outline>();
            outline.effectColor = tracked ? new Color(.55f, .72f, .50f, .85f) : new Color(.72f, .55f, .32f, .30f);
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
            state.color = tracked ? new Color(.60f, .80f, .58f) : entry.Done ? DemoUiTheme.Current.Muted : DemoUiTheme.Current.Copper;
            row += 66;
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

using System;
using System.Linq;
using BaseConquest.Presentation;
using UnityEngine;
using UnityEngine.UI;

namespace BaseConquest.Base
{
    public sealed partial class BaseHud
    {
        // P34：任务列表只做展示与选择追踪；任务内容来自 BaseQuestLog，列表页与常驻栏同源。
        // 字段口径见《基地征服RPG-UI实施说明书（Unity）》§3.2：主线／支线 tab、追踪／取消追踪、成就三类、任务五态。
        private string questTab = "main";

        private void OpenDemoQuestPanel()
        {
            var s = owner.State;
            activeObject = "quests"; interactionObject = "";
            BeginPanel("任务与成就", "quests");
            QuestTab("main", "主线", 0);
            QuestTab("side", "支线", 1);
            QuestTab("achievement", "成就", 2);
            row += 56;
            if (questTab == "achievement") { AchievementBody(s); EndPanel(); return; }
            bool main = questTab == "main";
            var entries = BaseQuestLog.Entries(s).Where(entry => entry.Main == main).ToList();
            if (entries.Count == 0)
            {
                string empty = main ? "当前没有主线任务。" : "当前没有支线任务。";
                Line(empty, 40, DemoUiMetrics.FontBody);
            }
            foreach (var entry in entries) QuestCard(s, entry);
            EndPanel();
        }

        private void QuestTab(string key, string name, int index)
        {
            RowButton((questTab == key ? "● " : "") + name, (index - 1) * 200, 180, () => { questTab = key; OpenDemoQuestPanel(); });
        }

        // 一张任务卡：标题 + 状态（按五态配色）+ 目标 + 奖励 + 追踪按钮。已完成的不再给追踪入口。
        private void QuestCard(BaseState s, BaseQuestEntry entry)
        {
            bool trackable = entry.Trackable;
            float height = trackable ? 142 : 96;
            var card = Box("QuestCard", content, new Vector2(.5f, 1), new Vector2(0, -row - height / 2), new Vector2(850, height), new Color(.16f, .15f, .14f, .72f));
            var outline = card.gameObject.GetOrAdd<Outline>();
            outline.effectColor = new Color(.72f, .55f, .32f, .35f);
            outline.effectDistance = new Vector2(1, -1);
            Box("Accent", card, new Vector2(0, 1), new Vector2(4, -height / 2), new Vector2(4, height - 16), PhaseColor(entry.Phase, .79f));

            Label(card, entry.Title, new Vector2(278, -10), new Vector2(520, 26), DemoUiMetrics.FontSubhead, TextAnchor.MiddleLeft, new Vector2(0, 1));
            var status = Label(card, entry.Status, new Vector2(-154, -10), new Vector2(280, 26), DemoUiMetrics.FontBody, TextAnchor.MiddleRight, new Vector2(1, 1));
            status.color = PhaseColor(entry.Phase);
            Label(card, entry.Goal, new Vector2(425, -38), new Vector2(814, 20), DemoUiMetrics.FontBody, TextAnchor.MiddleLeft, new Vector2(0, 1));
            var reward = Label(card, "奖励：" + entry.Reward, new Vector2(425, -60), new Vector2(814, 18), DemoUiMetrics.FontCaption, TextAnchor.MiddleLeft, new Vector2(0, 1));
            reward.color = DemoUiTheme.Current.Muted;
            if (!trackable) { row += height + 10; return; }

            bool tracked = BaseQuestLog.TrackedId(s) == entry.Id;
            MakeButton(card, tracked ? "● 取消追踪" : "○ 追踪", new Vector2(128, -height + 40), new Vector2(220, 36), () =>
            {
                if (!owner.Change(next => { if (!BaseQuestLog.ToggleTrack(next, entry.Id)) throw new InvalidOperationException("任务状态已变化"); }, tracked ? "已取消追踪" : "已显示在常驻任务栏")) return;
                OpenDemoQuestPanel();
            }, new Vector2(0, 1));
            row += height + 10;
        }

        // 五态配色：可接取／进行中／可交付／已完成／可重复。状态文字始终写全，颜色只是辅助编码。
        private static Color PhaseColor(QuestPhase phase, float alpha = 1f)
        {
            switch (phase)
            {
                case QuestPhase.Active: return new Color(.80f, .67f, .38f, alpha);
                case QuestPhase.Ready: return new Color(.56f, .81f, .61f, alpha);
                case QuestPhase.Done: return new Color(.58f, .62f, .60f, alpha);
                case QuestPhase.Repeatable: return new Color(.52f, .72f, .88f, alpha);
                default: return new Color(.76f, .79f, .74f, alpha);
            }
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

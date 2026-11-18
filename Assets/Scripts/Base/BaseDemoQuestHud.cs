using System;
using System.Collections.Generic;
using System.Linq;
using BaseConquest.Battle;
using BaseConquest.Presentation;
using UnityEngine;
using UnityEngine.UI;

namespace BaseConquest.Base
{
    public sealed partial class BaseHud
    {
        // 主菜单任务页按主线和支线列出任务日志。
        private string questTab = "tasks";
        private RectTransform questBar;
        private Text questInstruction;
        private GuidanceArrow questArrow;
        private Button questBackpackButton;
        private RectTransform questSwordTarget, questFarmTarget;

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
            var name = Label(bar, entry.Title, new Vector2(295, -13), new Vector2(566, 22), DemoUiMetrics.FontBody, TextAnchor.MiddleLeft, new Vector2(0, 1));
            name.color = entry.Done ? theme.Muted : theme.Ink;
            name.fontStyle = FontStyle.Bold;
            var goal = Label(bar, entry.Goal, new Vector2(295, -34), new Vector2(566, 32), DemoUiMetrics.FontCaption, TextAnchor.MiddleLeft, new Vector2(0, 1));
            goal.color = theme.Muted;
            var state = Label(bar, entry.Status, new Vector2(-138, -16), new Vector2(252, 20), DemoUiMetrics.FontCaption, TextAnchor.MiddleRight, new Vector2(1, 1));
            state.color = entry.Done ? theme.Muted : theme.Copper;
            var cue = BaseQuestLog.Cue(owner.State);
            if (!entry.Main && entry.Trackable)
            {
                bool tracking = cue.Id == entry.Id;
                MakeButton(bar, tracking ? "取消追踪" : "追踪", new Vector2(-70, -39), new Vector2(111, 29), () =>
                {
                    if (owner.Change(next => BaseQuestLog.ToggleTrack(next, entry.Id), "")) OpenDemoQuestPanel();
                }, new Vector2(1, 1));
            }
            row += 66;
        }

        private void RefreshQuestCue()
        {
            if (!BaseBattleGateway.DemoMode) return;
            float headerHeight = ((RectTransform)summary.transform.parent).rect.height;
            if (questInstruction == null)
            {
                var frame = (RectTransform)(root.Find("游戏显示区域 · 16比9") ?? root);
                var theme = DemoUiTheme.Current;
                questBar = Solid("CurrentQuestBar", frame, Vector2.up, Vector2.zero,
                    new Vector2(256, 34), new Color(theme.Panel.r, theme.Panel.g, theme.Panel.b, .94f));
                questBar.pivot = Vector2.up;
                var background = questBar.GetComponent<Image>();
                background.raycastTarget = false;
                var border = questBar.gameObject.AddComponent<Outline>();
                border.effectColor = theme.Line;
                border.effectDistance = new Vector2(1, -1);
                questInstruction = Label(questBar, "", Vector2.zero, Vector2.zero, 19);
                questInstruction.name = "CurrentQuestInstruction";
                questInstruction.color = theme.Ink;
                questInstruction.fontStyle = FontStyle.Bold;
                var labelRect = questInstruction.rectTransform;
                labelRect.anchorMin = Vector2.zero;
                labelRect.anchorMax = Vector2.one;
                labelRect.offsetMin = new Vector2(12, 4);
                labelRect.offsetMax = new Vector2(-12, -4);
                questInstruction.GetComponent<Shadow>().enabled = false;
                questArrow = GuidanceArrow.Create(frame, "CurrentQuestArrow", 36);
            }

            var s = owner.State;
            var cue = BaseQuestLog.Cue(s);
            bool show = !BaseBattleGateway.MainMenu && !owner.DeploymentWorldActive && cue.Id != null;
            bool hero = show && cue.Control == "hero" && heroChoiceLayer != null && heroChoiceLayer.gameObject.activeInHierarchy;
            bool showInstruction = show && !hero && !PanelOpen && !owner.ConstructionMode && !owner.HomeRebuildOverview;
            questBar.gameObject.SetActive(showInstruction);
            if (showInstruction)
            {
                var barPosition = new Vector2(20, -headerHeight - 12);
                if (questBar.anchoredPosition != barPosition)
                    questBar.anchoredPosition = barPosition;
                if (questInstruction.text != cue.Instruction)
                {
                    questInstruction.text = cue.Instruction;
                    questBar.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal,
                        Mathf.Clamp(questInstruction.preferredWidth + 24, 148, 500));
                    questBar.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical,
                        Mathf.Max(34, questInstruction.preferredHeight + 8));
                }
            }
            if (!show || heroNameLayer != null && heroNameLayer.gameObject.activeInHierarchy)
            { questArrow.Hide(); return; }
            RectTransform target = null;
            if (hero)
            {
                if (heroChoiceConfirm != null && heroChoiceConfirm.interactable) target = (RectTransform)heroChoiceConfirm.transform;
                else if (heroChoiceTargets != null)
                {
                    // Both characters are valid at this stage: point between them until one is selected.
                    var left = (RectTransform)heroChoiceTargets[0].transform;
                    var right = (RectTransform)heroChoiceTargets[1].transform;
                    var center = (left.TransformPoint(left.rect.center) + right.TransformPoint(right.rect.center)) * .5f;
                    var screen = RectTransformUtility.WorldToScreenPoint(null, center);
                    questArrow.PointToMap(screen - Vector2.up * 55, screen + Vector2.up * 55);
                    return;
                }
            }
            else if (PanelOpen)
            {
                if (DialogueOpen && dialogueQuestTarget != null && dialogueQuestTarget.gameObject.activeInHierarchy)
                    target = (RectTransform)dialogueQuestTarget.transform;
                else if (cue.Control == "sword" && panelViewKey == "party-inventory") target = questSwordTarget;
                else if (cue.Control == "farm" && (panelViewKey == "intro_farm" || panelViewKey == "intro_smith")) target = questFarmTarget;
                else if (panelViewKey == PreparationView && !PreparationHelpOpen && cue.ObjectId == "demo_defense_start")
                    target = panel.Find("Continue") as RectTransform;
            }
            else if (cue.Control == "sword") target = (RectTransform)questBackpackButton.transform;
            else if (cue.Control == "rebuild")
            {
                if (owner.HomeRebuildOverview) target = (RectTransform)rebuildFinish.transform;
                else if (!owner.ConstructionMode) target = (RectTransform)rebuildResume.transform;
                else if (rebuildConfirm.IsInteractable()) target = (RectTransform)rebuildConfirm.transform;
                else if (rebuildPlace.IsInteractable()) target = (RectTransform)rebuildPlace.transform;
                else if (owner.RebuildProject < 0)
                {
                    int mask = s.Demo.Finale.RebuildMask;
                    for (int i = 0; i < rebuildProjects.Length; i++)
                        if ((mask & (1 << i)) == 0) { target = (RectTransform)rebuildProjects[i].transform; break; }
                }
            }
            else if (cue.ObjectId != null)
            {
                if (nearbyId == cue.ObjectId && interact.IsInteractable() && interact.gameObject.activeInHierarchy)
                    target = (RectTransform)interact.transform;
                else
                {
                    var item = owner.PresentedObject(cue.ObjectId) ?? owner.DisplayObjects.FirstOrDefault(entry => entry.Id == cue.ObjectId);
                    if (item != null && owner.WorldCamera != null)
                    {
                        var camera = owner.WorldCamera;
                        var feet = owner.WorldPoint(item.X, item.Y);
                        var head = owner.TryCharacterTop(item.Id, out var actualTop) ? actualTop :
                            feet + camera.transform.up * .8f;
                        var bottom = camera.WorldToScreenPoint(feet);
                        var top = camera.WorldToScreenPoint(head);
                        if (bottom.z > 0)
                        {
                            questArrow.PointToMap(bottom, top, headerHeight, aboveLabel: true);
                            return;
                        }
                    }
                }
            }
            if (target != null && (!target.TryGetComponent<Button>(out var button) || button.IsInteractable()))
                questArrow.PointTo(target);
            else questArrow.Hide();
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

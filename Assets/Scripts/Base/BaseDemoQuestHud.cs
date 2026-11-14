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
        private Text questInstruction, questArrow;
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
            if (questInstruction == null)
            {
                Transform frame = root.Find("游戏显示区域 · 16比9") ?? root;
                questInstruction = Label(frame, "", new Vector2(288, -ExplorationHudLayout.HeaderHeight - 25),
                    new Vector2(540, 40), 20, TextAnchor.MiddleLeft, new Vector2(0, 1));
                questInstruction.name = "CurrentQuestInstruction";
                questInstruction.color = DemoUiTheme.Current.Panel;
                questInstruction.fontStyle = FontStyle.Bold;
                questArrow = Label(frame, "▼", Vector2.zero, new Vector2(34, 34), 28, TextAnchor.MiddleCenter);
                questArrow.name = "CurrentQuestArrow";
                questArrow.color = DemoUiTheme.Current.Gold;
                questArrow.fontStyle = FontStyle.Bold;
            }

            var s = owner.State;
            var cue = BaseQuestLog.Cue(s);
            bool show = !BaseBattleGateway.MainMenu && !owner.DeploymentWorldActive && cue.Id != null;
            bool hero = show && cue.Control == "hero" && heroChoiceLayer != null && heroChoiceLayer.gameObject.activeInHierarchy;
            bool showInstruction = show && !hero && !PanelOpen && !owner.ConstructionMode && !owner.HomeRebuildOverview;
            questInstruction.gameObject.SetActive(showInstruction);
            if (showInstruction && questInstruction.text != cue.Instruction) questInstruction.text = cue.Instruction;
            questArrow.gameObject.SetActive(false);
            if (!show || heroNameLayer != null && heroNameLayer.gameObject.activeInHierarchy) return;
            var arrowFrame = (RectTransform)questArrow.rectTransform.parent;
            RectTransform target = null;
            Vector2? point = null;
            bool mapTarget = false;
            if (hero)
            {
                if (heroChoiceConfirm != null && heroChoiceConfirm.interactable) target = (RectTransform)heroChoiceConfirm.transform;
                else if (heroChoiceTargets != null)
                {
                    // Both characters are valid at this stage: point between them until one is selected.
                    var left = (RectTransform)heroChoiceTargets[0].transform;
                    var right = (RectTransform)heroChoiceTargets[1].transform;
                    point = arrowFrame.InverseTransformPoint((left.TransformPoint(new Vector3(0, left.rect.yMax)) +
                        right.TransformPoint(new Vector3(0, right.rect.yMax))) / 2f);
                }
            }
            else if (PanelOpen)
            {
                if (DialogueOpen && dialogueQuestTarget != null && dialogueQuestTarget.gameObject.activeInHierarchy)
                    target = (RectTransform)dialogueQuestTarget.transform;
                else if (cue.Control == "sword" && panelViewKey == "party-inventory") target = questSwordTarget;
                else if (cue.Control == "farm" && (panelViewKey == "intro_farm" || panelViewKey == "intro_smith")) target = questFarmTarget;
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
                        var screen = camera.WorldToScreenPoint(owner.WorldPoint(item.X, item.Y) +
                            camera.transform.up * (item.Kind == "npc" || cue.ObjectId == "demo_captain" ? 2.05f : .8f));
                        if (screen.z > 0 && RectTransformUtility.ScreenPointToLocalPointInRectangle(arrowFrame, screen, null, out var local))
                        { point = local; mapTarget = true; }
                    }
                }
            }
            if (target != null && target.gameObject.activeInHierarchy)
                point = arrowFrame.InverseTransformPoint(target.TransformPoint(new Vector3(0, target.rect.yMax)));
            if (!point.HasValue) return;
            var rect = arrowFrame.rect;
            var desired = point.Value + new Vector2(0, 17 + Mathf.Sin(Time.unscaledTime * 5) * 3);
            var clipped = new Vector2(Mathf.Clamp(desired.x, rect.xMin + 23, rect.xMax - 23),
                Mathf.Clamp(desired.y, rect.yMin + 23, rect.yMax - (mapTarget ? ExplorationHudLayout.HeaderHeight + 23 : 20)));
            var offset = desired - clipped;
            questArrow.rectTransform.anchoredPosition = clipped;
            questArrow.rectTransform.localRotation = mapTarget && offset.sqrMagnitude > 9
                ? Quaternion.Euler(0, 0, Mathf.Atan2(offset.y, offset.x) * Mathf.Rad2Deg + 90) : Quaternion.identity;
            questArrow.transform.SetAsLastSibling();
            questArrow.gameObject.SetActive(true);
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

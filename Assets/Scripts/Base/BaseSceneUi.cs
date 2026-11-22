using System;
using BaseConquest.Presentation;
using BaseConquest.Story;
using UnityEngine;
using UnityEngine.UI;

namespace BaseConquest.Base
{
    public sealed partial class BaseSceneController
    {
        internal Transform SceneUiSource => transform.Find("UI 布局源");

        internal GameObject SceneUiTemplate(string name)
        {
            var source = SceneUiSource;
            if (source == null) return null;
            var template = source.Find(name);
            return template == null ? null : template.gameObject;
        }
    }

    public sealed partial class BaseHud
    {
        private static T Ui<T>(Transform parent, string path) where T : Component
        {
            var node = string.IsNullOrEmpty(path) ? parent : parent.Find(path);
            var component = node == null ? null : node.GetComponent<T>();
            if (component == null) throw new InvalidOperationException($"UI 布局缺少 {parent.name}/{path} ({typeof(T).Name})");
            return component;
        }

        private RectTransform CloneSceneUi(string key, Transform parent)
        {
            var template = owner.SceneUiTemplate(key);
            if (template == null) throw new InvalidOperationException("Base_Stage2 的 UI 布局源缺少 " + key);
            var view = UnityEngine.Object.Instantiate(template, parent, false);
            view.name = key;
            foreach (var label in view.GetComponentsInChildren<Text>(true))
                if (label.font == null || (label.font.hideFlags & HideFlags.DontSave) != 0) label.font = font;
            foreach (var button in view.GetComponentsInChildren<Button>(true))
                DemoUiTheme.Current.ApplyButtonFeedback(button);
            view.SetActive(true);
            return view.GetComponent<RectTransform>();
        }

        private RectTransform OpenScenePanel(string key, string viewKey)
        {
            ResetScenePanel(viewKey);
            return panel = CloneSceneUi(key, modal);
        }

        private static void ClearPanelChildren(Transform node)
        {
            for (int index = node.childCount - 1; index >= 0; index--)
            {
                var child = node.GetChild(index);
                child.gameObject.SetActive(false);
                child.SetParent(null, false);
                UiPrefabBinding.DestroyView(child.gameObject);
            }
        }

        private void ResetScenePanel(string viewKey)
        {
            if (villageTitle != null)
            {
                titleActions.SetActive(false);
                if (!BaseBattleGateway.MainMenu)
                {
                    villageTitle.gameObject.SetActive(false);
                    UiPrefabBinding.DestroyView(villageTitle.gameObject);
                    villageTitle = null;
                    titleActions = null;
                }
            }
            CloseResourcePopover();
            if (panel != null)
            {
                ClearPanelSelection();
                panel.gameObject.SetActive(false);
                UiPrefabBinding.DestroyView(panel.gameObject);
            }
            panelViewKey = viewKey;
            panelScroll = null;
            panelFeedback = null;
            content = null;
            dialogueContinue = skipNarration = null;
            modal.GetComponent<Image>().color = DemoUiTheme.Current.Shade;
            modal.gameObject.SetActive(true);
            questBar?.gameObject.SetActive(false);
            questArrow?.Hide();
            Joystick.ResetInput();
            Joystick.gameObject.SetActive(false);
        }

        private void OpenSceneCampMenu()
        {
            var view = OpenScenePanel("CampMenu", "camp-menu");
            const float width = 600, height = 416;
            view.sizeDelta = new Vector2(width, height);
            view.anchoredPosition = new Vector2(-width / 2 - 15, view.anchoredPosition.y);
            Place("TitleRail", 0, height / 2 - 28, width, 55);
            Place("Title", -width / 2 + 140, height / 2 - 28, 240, 39);
            Place("Close", width / 2 - 32, height / 2 - 28, 42, 36);
            Place("Actions", 0, -5, width - 36, 302);
            Place("SaveNotice", 0, -height / 2 + 24, width - 36, 30);
            var rows = Ui<ScrollRect>(view, "Actions").content;
            var columns = UiPrefabBinding.Rect("Columns", rows).gameObject.AddComponent<HorizontalLayoutGroup>();
            columns.spacing = 12;
            columns.childAlignment = TextAnchor.UpperLeft;
            columns.childControlWidth = columns.childControlHeight = true;
            columns.childForceExpandWidth = true;
            columns.childForceExpandHeight = false;
            var gameplay = Column("Gameplay");
            var system = Column("System");
            Ui<Button>(view, "Close").onClick.AddListener(ClosePanel);
            var saveNotice = Ui<Text>(view, "SaveNotice");
            saveNotice.gameObject.SetActive(Application.isPlaying && BaseBattleGateway.Session().HasUnsavedChanges);

            RegisterCampMenuActions((title, action, enabled) => Add(gameplay, title, action, enabled),
                (title, action, enabled) => Add(system, title, action, enabled));

            void Place(string name, float x, float y, float w, float h)
            {
                var rect = Ui<RectTransform>(view, name);
                rect.anchoredPosition = new Vector2(x, y);
                rect.sizeDelta = new Vector2(w, h);
            }

            RectTransform Column(string name)
            {
                var column = UiPrefabBinding.Rect(name, columns.transform);
                var layout = column.gameObject.AddComponent<VerticalLayoutGroup>();
                layout.spacing = 6;
                layout.childControlWidth = true;
                layout.childForceExpandWidth = true;
                layout.childControlHeight = false;
                layout.childForceExpandHeight = false;
                return column;
            }

            void Add(RectTransform column, string title, Action action, bool enabled)
            {
                var button = CloneSceneUi("CampMenuAction", column).GetComponent<Button>();
                button.GetComponentInChildren<Text>(true).text = title;
                button.interactable = enabled;
                button.onClick.AddListener(() => { if (!owner.Busy) action(); });
            }
            RefreshSummary();
        }

        private void RegisterCampMenuActions(Action<string, Action, bool> gameplay, Action<string, Action, bool> system)
        {
            var state = owner.State;
            gameplay("领地地图 · M", OpenLandMap, BaseLandMap.Available(state));
            gameplay(StoryText.Get("story.ui.menu.tasks", "任务 · N"), () => OpenQuests(), true);
            gameplay("日结报告", OpenReport, true);
            if (BaseAchievements.Active(state)) gameplay("成就 · " + state.Achievements.Records.Count + "/9", OpenAchievements, true);
            if (state.Campaign.HasActiveBattle) gameplay("恢复当前战斗", owner.ResumeCampaign, true);
            if (BaseConquest.Battle.RuntimeDeveloperOptions.Enabled)
            {
                if (BaseBattleGateway.DemoMode && !BaseBattleGateway.CustomMapTestMode) gameplay("阶段测试档", OpenDemoStages, true);
                if (BaseDemo.Active(state)) gameplay("独立即时战斗测试", OpenStandaloneBattleTest, true);
            }

            system("手动保存", () => OpenSlots(true), !state.Campaign.HasActiveBattle);
            system("读取存档", () => OpenSlots(false), true);
            system("选项", OpenOptions, true);
            system("帮助", OpenHelp, true);
            system("回主菜单", owner.ToMainMenu, true);
            if (BaseBattleGateway.DemoMode) system("退出游戏", () => BaseBattleGateway.ExitDemoSession(true), true);
        }
    }
}

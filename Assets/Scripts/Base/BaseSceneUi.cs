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
            view.SetActive(true);
            return view.GetComponent<RectTransform>();
        }

        private RectTransform OpenScenePanel(string key, string viewKey)
        {
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
            dialogueContinue = null;
            modal.GetComponent<Image>().color = DemoUiTheme.Current.Shade;
            modal.gameObject.SetActive(true);
            Joystick.ResetInput();
            Joystick.gameObject.SetActive(false);
            return panel = CloneSceneUi(key, modal);
        }

        private void OpenSceneCampMenu()
        {
            var view = OpenScenePanel("CampMenu", "camp-menu");
            var state = owner.State;
            var rows = Ui<ScrollRect>(view, "Actions").content;
            Ui<Button>(view, "Close").onClick.AddListener(ClosePanel);
            var saveNotice = Ui<Text>(view, "SaveNotice");
            saveNotice.gameObject.SetActive(Application.isPlaying && BaseBattleGateway.Session().HasUnsavedChanges);

            void Add(string title, Action action, bool enabled = true)
            {
                var button = CloneSceneUi("CampMenuAction", rows).GetComponent<Button>();
                button.GetComponentInChildren<Text>(true).text = title;
                button.interactable = enabled;
                button.onClick.AddListener(() => { if (!owner.Busy) action(); });
            }

            Add("领地地图 · M", OpenLandMap, BaseLandMap.Available(state));
            Add(StoryText.Get("story.ui.menu.tasks", "任务 · N"), () => OpenQuests());
            Add("日结报告", OpenReport);
            if (BaseAchievements.Active(state)) Add("成就 · " + state.Achievements.Records.Count + "/9", OpenAchievements);
            if (state.Campaign.HasActiveBattle) Add("恢复当前战斗", owner.ResumeCampaign);
            else if (!owner.AtBase)
                Add(BaseDesignRoutes.Active(state) ? "经绑定出口返回基地" : "返回基地", () => owner.Travel("base"),
                    !BaseDesignRoutes.Active(state) || BaseDesignRoutes.Reachable(state, "base") != null);
            if (BaseDemoFlow.Active(state) && !BaseDemoPrologue.Village(state) && state.Campaign.Location == "perimeter" && !BaseSceneCombat.Active(state))
                Add("战前布置工事", owner.ArmConstruction);
            Add("手动保存", () => OpenSlots(true), !state.Campaign.HasActiveBattle);
            Add("读取存档", () => OpenSlots(false));
            Add("选项", OpenOptions);
            Add("帮助", OpenHelp);
            Add("回主菜单", owner.ToMainMenu);
            if(BaseBattleGateway.DemoMode)Add("退出游戏",()=>BaseBattleGateway.ExitDemoSession(true));
            if (BaseConquest.Battle.RuntimeDeveloperOptions.Enabled)
            {
                Add("阶段测试档", OpenDemoStages);
                if (BaseDemo.Active(state)) Add("独立即时战斗测试", OpenStandaloneBattleTest);
            }
            RefreshSummary();
        }
    }
}

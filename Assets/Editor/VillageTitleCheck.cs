using System;
using System.IO;
using System.Linq;
using BaseConquest.Base;
using BaseConquest.Presentation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace BaseConquest.Editor
{
    public static class VillageTitleCheck
    {
        private const string Output = "Logs/wood-ui";
        private static double deadline;
        private static int step;
        private static bool checking;
        private static Camera titleCamera;
        private static BaseState titleState;
        private static Vector3 titlePosition;
        private static float titleZoom;

        [MenuItem("Jobs/DEMO/预览动态主菜单", false, 14)]
        public static void Preview()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            DemoStartup.Apply();
            SessionState.SetBool(DemoLauncher.PreviewTitleKey, true);
            EditorApplication.EnterPlaymode();
        }

        // Standalone batch runner, with graphics; do not pass -quit.
        public static void Run()
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("请使用独立检查进程。");
            Directory.CreateDirectory(Output);
            Environment.SetEnvironmentVariable("BASE_CONQUEST_UI_CHECK_SAVES",
                Path.GetFullPath(Path.Combine(Output, "check-saves", Guid.NewGuid().ToString("N"))));
            EditorSceneManager.OpenScene(DemoStartup.Launcher);
            checking = true;
            step = 0;
            deadline = EditorApplication.timeSinceStartup + 240;
            EditorApplication.update += Tick;
            Preview();
        }

        private static void Tick()
        {
            if (!checking) return;
            try
            {
                if (EditorApplication.timeSinceStartup > deadline) throw new TimeoutException("步骤 " + step);
                if (!EditorApplication.isPlaying) return;
                var owner = Object.FindAnyObjectByType<BaseSceneController>();
                if (owner?.Hud == null || owner.Busy) return;
                var title = Object.FindAnyObjectByType<VillageTitleLoop>();
                Button Btn(string path) => title.transform.Find("Actions/" + path).GetComponent<Button>();
                void PressText(string caption)
                {
                    var button = owner.Hud.VisualRoot.GetComponentsInChildren<Button>().Single(b =>
                        b.GetComponentInChildren<Text>()?.text == caption && b.interactable);
                    button.onClick.Invoke();
                }

                switch (step)
                {
                    case 0:
                        if (title == null) return;
                        Require(BaseBattleGateway.MainMenu && BaseDemoIntro.Daily(owner.State) &&
                            owner.State.Demo.Intro.Step == 0 && !Btn("Continue").interactable,
                            "空档应显示白桦村序章并禁用继续");
                        Require(!title.transform.Find("Village").gameObject.activeSelf, "标题下应显示实际地图");
                        owner.Save();
                        Require(!owner.SaveStore.Exists, "停留标题不得创建会话存档");
                        UiSkinScreenshot.Capture("village-title-1280x720");
                        UiSkinScreenshot.Capture("village-title-960x432", 960, 432);
                        Btn("Options").onClick.Invoke();
                        Require(!title.transform.Find("Actions").gameObject.activeSelf, "子页遮挡主按键");
                        PressText("声音设置");
                        PressText("返回选项");
                        PressText("返回");
                        Require(Btn("Load").gameObject.activeInHierarchy, "设置返回标题");
                        Btn("Load").onClick.Invoke();
                        Require(!owner.Hud.VisualRoot.GetComponentsInChildren<Button>().Any(b =>
                            b.GetComponentInChildren<Text>()?.text == "返回"), "读档页只保留关闭");
                        PressText("关闭");
                        Require(Btn("New").gameObject.activeInHierarchy, "读档返回标题");
                        titleCamera = owner.WorldCamera;
                        titleState = owner.State;
                        titlePosition = titleCamera.transform.position;
                        titleZoom = titleCamera.orthographicSize;
                        Btn("New").onClick.Invoke();
                        step++;
                        break;
                    case 1:
                        if (owner.State == null || BaseBattleGateway.MainMenu) return;
                        Require(BaseDemoIntro.Daily(owner.State) && owner.State.Demo.Intro.Step == 0, "新游戏进入白桦村选人");
                        Require(owner.WorldCamera == titleCamera && owner.State == titleState &&
                            Vector3.Distance(titleCamera.transform.position, titlePosition) < .001f &&
                            Mathf.Abs(titleCamera.orthographicSize - titleZoom) < .001f,
                            "开始新游戏须保持同一份地图状态和镜头");
                        Require(title == null && owner.Hud.VisualRoot.GetComponentsInChildren<Button>().Any(b => b.name == "HeroChoice_male"),
                            "标题应切换为人物选角");
                        Require(!BaseBattleGateway.Catalog(false).Entries().Any(e => e.Error == null),
                            "确认姓名前不得写入新游戏存档");
                        UiSkinScreenshot.Capture("village-hero-choice-1280x720");
                        var seeded = owner.State.Copy();
                        Require(BaseDemoIntro.Choose(seeded, "female", "菜单验收"), "预备近期存档");
                        BaseBattleGateway.Catalog().SaveSlot("slot1", seeded, "标题继续检查");
                        owner.ToMainMenu();
                        step++;
                        break;
                    case 2:
                        if (title == null || !BaseBattleGateway.MainMenu) return;
                        Require(Btn("Continue").interactable, "磁盘存档应启用继续");
                        Btn("Continue").onClick.Invoke();
                        step++;
                        break;
                    case 3:
                        if (owner.State?.Demo?.Intro?.PlayerName != "菜单验收") return;
                        Require(!BaseBattleGateway.MainMenu && title == null, "继续应恢复磁盘进度并移除标题演出");
                        Finish(0, "PASS");
                        break;
                }
            }
            catch (Exception error) { Finish(1, error.ToString()); }
        }

        private static void Require(bool pass, string message)
        {
            if (!pass) throw new InvalidOperationException(message);
        }

        private static void Finish(int code, string result)
        {
            checking = false;
            EditorApplication.update -= Tick;
            File.WriteAllText(Path.Combine(Output, "village-title-result.txt"), result);
            if (code == 0) Debug.Log("[VillageTitle] " + result);
            else Debug.LogError("[VillageTitle] " + result);
            EditorApplication.Exit(code);
        }
    }
}

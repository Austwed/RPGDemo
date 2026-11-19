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
                        Require(BaseBattleGateway.MainMenu && owner.State == null && !Btn("Continue").interactable,
                            "空档应进入标题并禁用继续");
                        CheckScene(title);
                        title.Sample(3.5f);
                        UiSkinScreenshot.Capture("village-title-1280x720");
                        UiSkinScreenshot.Capture("village-title-960x432", 960, 432);
                        for (int i = 0; i < 96; i++)
                        {
                            title.Sample(i / 8f);
                            UiSkinScreenshot.Capture("village-title-frame-" + i.ToString("D2"), 640, 360);
                        }
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
                        Btn("New").onClick.Invoke();
                        step++;
                        break;
                    case 1:
                        if (owner.State == null || BaseBattleGateway.MainMenu) return;
                        Require(BaseDemoIntro.Daily(owner.State) && owner.State.Demo.Intro.Step == 0, "新游戏进入白桦村选人");
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

        private static void CheckScene(VillageTitleLoop title)
        {
            var village = title.transform.Find("Village");
            var hero = village.Find("SeatedHero").GetComponent<RawImage>();
            var partner = village.Find("SeatedCompanion").GetComponent<RawImage>();
            Require(hero.texture.name == "player-sit-v2" && partner.texture.name == "female-sit-v2" &&
                hero.uvRect.x == .75f && partner.uvRect.x == .75f, "长椅上两人使用坐姿末帧");
            var walker = village.Find("MarketWalker").GetComponent<RawImage>();
            var rabbit = village.Find("MeadowRabbit").GetComponent<RawImage>();
            title.Sample(11);
            var pause = walker.rectTransform.anchoredPosition;
            title.Sample(12);
            Require(walker.rectTransform.anchoredPosition == pause && walker.texture.name.EndsWith("-v1"),
                "村民须在路端停下并换回站姿");
            title.Sample(3);
            Require(walker.rectTransform.anchoredPosition != pause && walker.texture.name.EndsWith("-walk-v2"),
                "村民沿路线行走");
            var position = rabbit.rectTransform.anchoredPosition;
            title.Sample(9);
            Require(!rabbit.enabled, "兔子入洞后短暂停留");
            title.Sample(15);
            Require(rabbit.enabled && Vector2.Distance(position, rabbit.rectTransform.anchoredPosition) < .01f,
                "兔子回到下一轮相同位置");
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

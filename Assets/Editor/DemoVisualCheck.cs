using System;
using System.IO;
using System.Linq;
using BaseConquest.Base;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BaseConquest.Editor
{
    // 批处理画面自检：-batchmode（带图形，不加 -nographics）+ -executeMethod BaseConquest.Editor.DemoVisualCheck.CaptureTown
    // 流程：进 Play → 读阶段档 camp-male → 走去 exit_east 进镇 → 截图镇中心与西侧 → 退出。
    // 产物：Temp/visual-check/*.png 与 result.log；退出码 0 成功、2 失败（Unity -quit 时经 Environment.Exit 透传）。
    public static class DemoVisualCheck
    {
        // 不能用 Temp：Unity 退出时会清空。Logs 是持久目录且本就被 git 忽略。
        private static readonly string OutDir = Path.Combine("Logs", "visual-check");
        private static double deadline;
        private static double stageAt;
        private static float frameDeadline;
        private static string stage = "boot";
        private static BaseSceneController controller;
        private static bool failed;
        private static double nextLog;

        private static bool campMode;

        public static void CaptureCamp()
        {
            // 营地四向楼梯与检查台的专用自检：不进镇，载入 camp 阶段档后直接在营地截三张。
            campMode = true;
            CaptureTown();
        }

        public static void CaptureTown()
        {
            Directory.CreateDirectory(OutDir);
            // 批处理不恢复上次打开的场景，活动场景为空会导致 playmode 里什么都没有，先显式打开启动场景。
            UnityEditor.SceneManagement.EditorSceneManager.OpenScene(DemoStartup.Launcher);
            deadline = EditorApplication.timeSinceStartup + 480;
            EditorApplication.update += Tick;
            EditorApplication.EnterPlaymode();
        }

        private static void Tick()
        {
            if (failed || EditorApplication.timeSinceStartup > deadline) { Finish(2, "超时或前置失败：" + stage); return; }
            if (!EditorApplication.isPlaying)
            {
                if (EditorApplication.timeSinceStartup > nextLog) { nextLog = EditorApplication.timeSinceStartup + 15; Debug.Log("[VisualCheck] 等待进入 playmode"); }
                return;
            }
            switch (stage)
            {
                case "boot":
                    controller = Object.FindAnyObjectByType<BaseSceneController>();
                    if (controller == null)
                    {
                        if (EditorApplication.timeSinceStartup > nextLog)
                        {
                            nextLog = EditorApplication.timeSinceStartup + 15;
                            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
                            Debug.Log("[VisualCheck] boot 无 controller，场景=" + scene.name + " 根对象=" + string.Join("|", scene.GetRootGameObjects().Select(g => g.name).Take(16)));
                        }
                        return;
                    }
                    controller.LoadDemoStage("camp", "male");
                    stage = "loading";
                    stageAt = EditorApplication.timeSinceStartup;
                    break;
                case "loading":
                    controller = Object.FindAnyObjectByType<BaseSceneController>();
                    if (controller?.State == null) return;
                    if (EditorApplication.timeSinceStartup - stageAt > 120) { Finish(2, "阶段档载入超时"); return; }
                    if (controller.State.Campaign.Location != "base") return;
                    // camp 阶段档还处于"营地苏醒"阶段，先在营帐旁完成醒来目标（DemoStageSaveTools 同款顺序），再瞬移到东侧出口。
                    controller.Change(s =>
                    {
                        if (s.Demo.Stage == BaseDemoStage.Waking)
                        {
                            var home = BaseSpatial.Objects(s).Single(o => o.Id == "home");
                            s.PlayerX = home.X; s.PlayerY = home.Y;
                            BaseAdventure.Arrive(s);
                            if (!BaseDemo.AcknowledgeWaking(s)) throw new InvalidOperationException("醒来目标未完成");
                        }
                        var exit = BaseSpatial.Objects(s).Single(o => o.Id == "exit_east");
                        s.PlayerX = exit.X; s.PlayerY = exit.Y;
                        BaseAdventure.Arrive(s);
                    }, "视觉自检：就位");
                    stage = "arrive";
                    break;
                case "arrive":
                    controller = Object.FindAnyObjectByType<BaseSceneController>();
                    if (controller?.State == null) return;
                    if (controller.State.Campaign.Location != "base") return;
                    if (campMode) { stage = "camp-stairs-south"; frameDeadline = Time.frameCount + 45; break; }
                    bool moved = controller.Change(s =>
                    {
                        var exit = BaseSpatial.Objects(s).Single(o => o.Id == "exit_east");
                        s.PlayerX = exit.X; s.PlayerY = exit.Y;
                        BaseAdventure.Arrive(s);
                        if (!BaseCampaign.Travel(s, "town")) throw new InvalidOperationException("前往城镇失败");
                    }, "视觉自检：前往城镇");
                    if (!moved) { Finish(2, "前往城镇被拒：" + controller.Message); return; }
                    stage = "settle";
                    frameDeadline = Time.frameCount + 90;
                    break;
                case "settle":
                    controller = Object.FindAnyObjectByType<BaseSceneController>();
                    if (Time.frameCount < frameDeadline) return;
                    if (controller?.State == null || controller.State.Campaign.Location != "town") { Finish(2, "未到达城镇"); return; }
                    var objects = controller.MapObjects;
                    foreach (var id in new[] { "town_guard", "town_peddler", "town_girl" })
                        if (!objects.Any(o => o.Id == id)) { Finish(2, "城镇缺少对象：" + id); return; }
                    Debug.Log("[VisualCheck] 城镇对象齐全：" + string.Join(",", objects.Where(o => o.Id.StartsWith("town_")).Select(o => o.Id)));
                    Capture("town-center");
                    stage = "west";
                    break;
                case "west":
                    if (!controller.Change(s =>
                    {
                        var peddler = BaseSpatial.Objects(s).Single(o => o.Id == "town_peddler");
                        s.PlayerX = peddler.X + 2; s.PlayerY = peddler.Y;
                        BaseAdventure.Arrive(s);
                    }, "视觉自检：走向行商")) { Finish(2, "移动被拒：" + controller.Message); return; }
                    stage = "west-settle";
                    frameDeadline = Time.frameCount + 45;
                    break;
                case "west-settle":
                    if (Time.frameCount < frameDeadline) return;
                    Capture("town-west");
                    stage = "stairs";
                    break;
                case "stairs":
                    // 城镇的坡道格坐标是运行时算出来的，别写死：直接从版图上找第一个 Ramp 格再定位。
                    if (!controller.Change(s =>
                    {
                        var board = s.Campaign.RegionMaps.Single(m => m.Id == "town").Board;
                        var ramp = board.Positions().First(p => board.Cell(p).Ramp != BaseConquest.Battle.RampAxis.None);
                        s.PlayerX = ramp.X + .5; s.PlayerY = ramp.Z + .5;
                        BaseAdventure.Arrive(s);
                        Debug.Log("[VisualCheck] 坡道格=" + ramp.X + "," + ramp.Z);
                    }, "视觉自检：走向坡道")) { Finish(2, "移动被拒：" + controller.Message); return; }
                    stage = "stairs-settle";
                    frameDeadline = Time.frameCount + 45;
                    break;
                case "stairs-settle":
                    if (Time.frameCount < frameDeadline) return;
                    Capture("town-stairs");
                    stage = "done";
                    // 截图是异步写盘，多等几十帧再退出进程。
                    frameDeadline = Time.frameCount + 60;
                    break;
                case "camp-stairs-south":
                    // 营地南楼梯 (11,8)(12,8)：主角站到楼梯南侧正对台面。
                    if (!controller.Change(s => { s.PlayerX = 11.5; s.PlayerY = 6.6; BaseAdventure.Arrive(s); }, "视觉自检：走向营地南楼梯")) { Finish(2, "移动被拒：" + controller.Message); return; }
                    stage = "camp-stairs-south-settle";
                    frameDeadline = Time.frameCount + 45;
                    break;
                case "camp-stairs-south-settle":
                    if (Time.frameCount < frameDeadline) return;
                    Capture("camp-stairs-south");
                    stage = "camp-stairs-west";
                    frameDeadline = Time.frameCount + 10;
                    break;
                case "camp-stairs-west":
                    // 西楼梯 (9,10)：主角站到楼梯南侧一格朝北看（粮田占了 z9.5~12.5 的西侧，不能贴太近）。
                    if (!controller.Change(s => { s.PlayerX = 9; s.PlayerY = 8.4; BaseAdventure.Arrive(s); }, "视觉自检：走向营地西楼梯")) { Finish(2, "移动被拒：" + controller.Message); return; }
                    stage = "camp-stairs-west-settle";
                    frameDeadline = Time.frameCount + 45;
                    break;
                case "camp-stairs-west-settle":
                    if (Time.frameCount < frameDeadline) return;
                    Capture("camp-stairs-west");
                    stage = "camp-motion";
                    frameDeadline = Time.frameCount + 10;
                    break;
                case "camp-motion":
                    // 检查台三排（士兵／角色／NPC）在营地南半区。
                    if (!controller.Change(s => { s.PlayerX = 6; s.PlayerY = 3; BaseAdventure.Arrive(s); }, "视觉自检：走向检查台")) { Finish(2, "移动被拒：" + controller.Message); return; }
                    stage = "camp-motion-settle";
                    frameDeadline = Time.frameCount + 45;
                    break;
                case "camp-motion-settle":
                    if (Time.frameCount < frameDeadline) return;
                    Capture("camp-motion-checks");
                    stage = "done";
                    // 截图是异步写盘，多等几十帧再退出进程。
                    frameDeadline = Time.frameCount + 60;
                    break;
                case "done":
                    if (Time.frameCount < frameDeadline) return;
                    Finish(0, "OK");
                    break;
            }
        }

        private static void Capture(string name)
        {
            // batchmode 无 GameView，ScreenCapture 静默失败；改为手动让相机渲染到 RenderTexture 后读像素，同步写盘。
            var cam = controller?.WorldCamera != null ? controller.WorldCamera : Camera.main != null ? Camera.main : Object.FindAnyObjectByType<Camera>();
            if (cam == null) { Debug.LogError("[VisualCheck] 找不到相机"); return; }
            const int w = 1280, h = 720;
            var rt = new RenderTexture(w, h, 24);
            var prevTarget = cam.targetTexture;
            var prevActive = RenderTexture.active;
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();
            RenderTexture.active = prevActive;
            cam.targetTexture = prevTarget;
            rt.Release();
            string path = Path.GetFullPath(Path.Combine(OutDir, name + ".png"));
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            Debug.Log("[VisualCheck] 截图 " + path + " 相机=" + cam.name);
        }

        private static void Finish(int code, string note)
        {
            EditorApplication.update -= Tick;
            File.WriteAllText(Path.Combine(OutDir, "result.log"), "exit=" + code + " " + note + "\n");
            if (code != 0) Debug.LogError("[VisualCheck] " + note); else Debug.Log("[VisualCheck] " + note);
            failed = code != 0;
            EditorApplication.isPlaying = false;
            EditorApplication.Exit(code);
        }
    }
}

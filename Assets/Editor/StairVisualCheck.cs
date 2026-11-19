using System;
using System.IO;
using System.Linq;
using System.Reflection;
using BaseConquest.Base;
using BaseConquest.Presentation;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BaseConquest.Editor
{
    // 批处理楼梯实景截图：-batchmode（带图形，不加 -nographics / -quit）
    // -executeMethod BaseConquest.Editor.StairVisualCheck.CaptureStairs
    // 故乡：新营地南梯 (11,8)(12,8)、东梯 (15,10)，默认开启 Cainos stair-64-v1 试点。
    // 遗迹：地牢 (24,12) H0->H1 与 (25,12) H1->H2 东西向坡道，使用 dungeon-stair。
    // 白桦村剧情图当前没有坡道格，故故乡实景取营地高差（故乡地面试点实际生效处）。
    public static class StairVisualCheck
    {
        private static readonly string OutDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            "designer图片", "楼梯现状");
        private static double deadline;
        private static double stageAt;
        private static float frameDeadline;
        private static string stage = "boot";
        private static BaseSceneController controller;
        private static bool failed;
        private static double nextLog;
        private static float midView = 8f;

        public static void CaptureStairs()
        {
            Directory.CreateDirectory(OutDir);
            HomeTerrainTrial.Enabled = true;
            UnityEditor.SceneManagement.EditorSceneManager.OpenScene(DemoStartup.Launcher);
            deadline = EditorApplication.timeSinceStartup + 600;
            EditorApplication.update += Tick;
            EditorApplication.EnterPlaymode();
        }

        private static void Tick()
        {
            try { TickBody(); }
            catch (Exception e) { if (!failed) Finish(2, "未处理异常 @" + stage + ": " + e.Message); }
        }

        private static void TickBody()
        {
            if (failed || EditorApplication.timeSinceStartup > deadline) { Finish(2, "超时或前置失败：" + stage); return; }
            if (!EditorApplication.isPlaying)
            {
                if (EditorApplication.timeSinceStartup > nextLog) { nextLog = EditorApplication.timeSinceStartup + 15; Debug.Log("[StairCheck] 等待进入 playmode"); }
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
                            Debug.Log("[StairCheck] boot 无 controller，场景=" + scene.name);
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
                    if (!controller.Change(s =>
                    {
                        if (s.Demo.Stage == BaseDemoStage.Waking && !BaseDemo.AcknowledgeWaking(s))
                            throw new InvalidOperationException("醒来目标未完成");
                    }, "楼梯自检：醒来")) { Finish(2, "醒来被拒：" + controller.Message); return; }
                    HomeTerrainTrial.Enabled = true;
                    stage = "home-south-near";
                    break;
                case "home-south-near":
                    if (!Move(11.5, 6.55, "南梯近景")) return;
                    stage = "home-south-near-settle";
                    frameDeadline = Time.frameCount + 45;
                    break;
                case "home-south-near-settle":
                    if (Time.frameCount < frameDeadline) return;
                    RememberView();
                    Capture("故乡-营地南梯-近景", 3.6f);
                    stage = "home-south-on";
                    break;
                case "home-south-on":
                    if (!Move(11.5, 8.45, "站上南梯")) return;
                    stage = "home-south-on-settle";
                    frameDeadline = Time.frameCount + 45;
                    break;
                case "home-south-on-settle":
                    if (Time.frameCount < frameDeadline) return;
                    Capture("故乡-营地南梯-坡上", midView);
                    stage = "home-east-near";
                    break;
                case "home-east-near":
                    if (!Move(16.7, 10.5, "东梯近景")) return;
                    stage = "home-east-near-settle";
                    frameDeadline = Time.frameCount + 45;
                    break;
                case "home-east-near-settle":
                    if (Time.frameCount < frameDeadline) return;
                    Capture("故乡-营地东梯-近景", 3.6f);
                    stage = "home-east-on";
                    break;
                case "home-east-on":
                    if (!Move(15.5, 10.5, "站上东梯")) return;
                    stage = "home-east-on-settle";
                    frameDeadline = Time.frameCount + 45;
                    break;
                case "home-east-on-settle":
                    if (Time.frameCount < frameDeadline) return;
                    Capture("故乡-营地东梯-坡上", midView);
                    stage = "to-exit";
                    break;
                case "to-exit":
                    if (!GoTo("exit_east", "东侧出口")) return;
                    stage = "to-town";
                    break;
                case "to-town":
                    if (!TravelTo("town", "前往城镇")) return;
                    stage = "at-town";
                    frameDeadline = Time.frameCount + 50;
                    break;
                case "at-town":
                    if (Time.frameCount < frameDeadline) return;
                    controller = Object.FindAnyObjectByType<BaseSceneController>();
                    if (controller?.State == null || controller.State.Campaign.Location != "town") { Finish(2, "未到达城镇"); return; }
                    if (!GoTo("demo_ruins_exit", "城镇遗迹出口")) return;
                    stage = "to-outpost";
                    break;
                case "to-outpost":
                    if (!TravelTo("outpost", "前往遗迹入口")) return;
                    stage = "at-outpost";
                    frameDeadline = Time.frameCount + 50;
                    break;
                case "at-outpost":
                    if (Time.frameCount < frameDeadline) return;
                    controller = Object.FindAnyObjectByType<BaseSceneController>();
                    if (controller?.State == null || controller.State.Campaign.Location != "outpost") { Finish(2, "未到达遗迹入口"); return; }
                    SanitizeMemories(controller.State);
                    if (!controller.Change(s =>
                    {
                        SanitizeMemories(s);
                        var portal = BaseSpatial.Objects(s).Single(o => o.Id == "demo_ruins_investigate");
                        s.PlayerX = portal.X; s.PlayerY = portal.Y;
                        BaseAdventure.Arrive(s);
                        BaseSceneRules.EnterDungeon(s);
                        SanitizeMemories(s);
                    }, "楼梯自检：进入地牢")) { Finish(2, "进入地牢被拒：" + controller.Message); return; }
                    RefreshView();
                    stage = "dungeon-arrive";
                    frameDeadline = Time.frameCount + 55;
                    break;
                case "dungeon-arrive":
                    if (Time.frameCount < frameDeadline) return;
                    controller = Object.FindAnyObjectByType<BaseSceneController>();
                    if (controller?.State == null || !BaseSceneRules.Dungeon(controller.State)) { Finish(2, "未进入地牢，location=" + controller?.State?.Campaign.Location); return; }
                    if (!Move(22.6, 12.5, "地牢坡道近景位")) return;
                    stage = "dungeon-near-settle";
                    frameDeadline = Time.frameCount + 45;
                    break;
                case "dungeon-near-settle":
                    if (Time.frameCount < frameDeadline) return;
                    controller = Object.FindAnyObjectByType<BaseSceneController>();
                    if (controller?.State == null || !BaseSceneRules.Dungeon(controller.State)) { Finish(2, "未进入地牢，location=" + controller?.State?.Campaign.Location); return; }
                    Capture("遗迹-地牢坡道-近景", 4.2f);
                    stage = "dungeon-on";
                    break;
                case "dungeon-on":
                    if (!Move(24.5, 12.5, "站上地牢第一级坡")) return;
                    stage = "dungeon-on-settle";
                    frameDeadline = Time.frameCount + 45;
                    break;
                case "dungeon-on-settle":
                    if (Time.frameCount < frameDeadline) return;
                    Capture("遗迹-地牢坡道-坡上", midView);
                    stage = "dungeon-side";
                    break;
                case "dungeon-side":
                    if (!Move(24.5, 11.15, "地牢坡道南侧")) return;
                    stage = "dungeon-side-settle";
                    frameDeadline = Time.frameCount + 45;
                    break;
                case "dungeon-side-settle":
                    if (Time.frameCount < frameDeadline) return;
                    Capture("遗迹-地牢坡道-侧面", 4.0f);
                    stage = "dungeon-top";
                    break;
                case "dungeon-top":
                    if (!Move(26.6, 12.5, "地牢高台顶端")) return;
                    stage = "dungeon-top-settle";
                    frameDeadline = Time.frameCount + 45;
                    break;
                case "dungeon-top-settle":
                    if (Time.frameCount < frameDeadline) return;
                    Capture("遗迹-地牢坡道-顶端", midView);
                    stage = "done";
                    frameDeadline = Time.frameCount + 60;
                    break;
                case "done":
                    if (Time.frameCount < frameDeadline) return;
                    Finish(0, "OK files=" + string.Join(",", Directory.GetFiles(OutDir, "*.png").Select(Path.GetFileName)));
                    break;
            }
        }

        private static bool GoTo(string id, string why)
        {
            SanitizeMemories(controller.State);
            if (!controller.Change(s =>
            {
                SanitizeMemories(s);
                var obj = BaseSpatial.Objects(s).Single(o => o.Id == id);
                s.PlayerX = obj.X; s.PlayerY = obj.Y;
                BaseAdventure.Arrive(s);
            }, "楼梯自检：" + why)) { Finish(2, "移动被拒（" + why + "）：" + controller.Message); return false; }
            return true;
        }

        private static void RefreshView()
        {
            var method = typeof(BaseSceneController).GetMethod("RefreshDemoRegion", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (method == null) throw new InvalidOperationException("找不到 RefreshDemoRegion");
            method.Invoke(controller, null);
        }

        private static bool TravelTo(string location, string why)
        {
            SanitizeMemories(controller.State);
            controller.TravelDemo(location);
            if (controller.State == null || controller.State.Campaign.Location != location)
            {
                Finish(2, why + "失败：" + controller.Message + " location=" + controller.State?.Campaign.Location);
                return false;
            }
            return true;
        }

        private static bool Move(double x, double y, string why)
        {
            SanitizeMemories(controller.State);
            if (!controller.Change(s =>
            {
                SanitizeMemories(s);
                s.PlayerX = x;
                s.PlayerY = y;
                BaseAdventure.Arrive(s);
            }, "楼梯自检：" + why)) { Finish(2, "移动被拒（" + why + "）：" + controller.Message); return false; }
            return true;
        }

        private static void SanitizeMemories(BaseState s)
        {
            // 探索视野会把 Known 写成与当前棋盘宽度不一致的数组；空列表对 Validate 永远合法。
            if (s?.Demo != null) s.Demo.TerrainMemories = new System.Collections.Generic.List<BaseTerrainMemory>();
        }

        private static void RememberView()
        {
            var cam = FindCamera();
            if (cam != null && cam.orthographic) midView = Mathf.Clamp(cam.orthographicSize, 5f, 16f);
        }

        private static UnityEngine.Camera FindCamera()
        {
            return controller?.WorldCamera != null ? controller.WorldCamera : UnityEngine.Camera.main != null ? UnityEngine.Camera.main : Object.FindAnyObjectByType<UnityEngine.Camera>();
        }

        private static void Capture(string name, float ortho)
        {
            var cam = FindCamera();
            if (cam == null) { Debug.LogError("[StairCheck] 找不到相机"); return; }
            float previous = cam.orthographicSize;
            if (cam.orthographic) cam.orthographicSize = ortho;
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
            if (cam.orthographic) cam.orthographicSize = previous;
            rt.Release();
            string path = Path.GetFullPath(Path.Combine(OutDir, name + ".png"));
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            Debug.Log("[StairCheck] 截图 " + path + " 相机=" + cam.name + " ortho=" + ortho);
        }

        private static void Finish(int code, string note)
        {
            EditorApplication.update -= Tick;
            try { File.WriteAllText(Path.Combine(OutDir, "result-stairs.log"), "exit=" + code + " " + note + Environment.NewLine); }
            catch (Exception e) { Debug.LogWarning("[StairCheck] 无法写 result-stairs.log: " + e.Message); }
            if (code != 0) Debug.LogError("[StairCheck] " + note); else Debug.Log("[StairCheck] " + note);
            failed = code != 0;
            EditorApplication.isPlaying = false;
            EditorApplication.Exit(code);
        }
    }
}

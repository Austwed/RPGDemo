using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BaseConquest.Base;
using BaseConquest.Presentation;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace BaseConquest.Editor
{
    // P8（W8）C4～C6 离屏截图验收：
    //   Unity.exe -batchmode -projectPath RPGDemo -executeMethod BaseConquest.Editor.UiSkinScreenshot.Run
    // 不加 -nographics、不加 -quit。使用本次检查专用进度验证正式 Demo 流程。
    // 产物：Logs/wood-ui/。
    // 世界地图不在此脚本内：Demo 阶段档的 WorldJourney.Nodes／Entries 为空且全流程走领地内直达，
    // 游戏内没有可进入的世界地图画面，见 LOG 记录；C6 以 VerifyWorldArt 的静态断言为准。
    // 截图沿用 UiInventoryCapture 的双遍合成：世界相机渲一张，屏幕空间 Overlay 的 Canvas 再单独渲到透明底按 alpha 叠回。
    public static class UiSkinScreenshot
    {
        private static readonly string OutDir = Path.Combine("Logs", "wood-ui");
        private static double deadline, stageAt, nextLog;
        private static int frameDeadline;
        private static string stage = "boot";
        private static BaseSceneController controller;
        private static bool failed;
        private static readonly List<string> Log = new List<string>();

        private static BaseSceneController C => Object.FindAnyObjectByType<BaseSceneController>();
        private static BaseHud H => C?.Hud;

        public static void Run()
        {
            Directory.CreateDirectory(OutDir);
            if (!Application.isBatchMode) throw new InvalidOperationException("界面检查须在独立后台编辑器运行");
            Environment.SetEnvironmentVariable("BASE_CONQUEST_UI_CHECK_SAVES",
                Path.GetFullPath(Path.Combine(OutDir, "check-saves", Guid.NewGuid().ToString("N"))));
            UnityEditor.SceneManagement.EditorSceneManager.OpenScene(DemoStartup.Launcher);
            deadline = EditorApplication.timeSinceStartup + 480;
            EditorApplication.update += Tick;
            EditorApplication.EnterPlaymode();
        }

        private static void Tick()
        {
            try { TickStage(); }
            catch (Exception error) { Finish(2, stage + "：" + error); }
        }

        private static void TickStage()
        {
            if (failed || EditorApplication.timeSinceStartup > deadline) { Finish(2, "超时或前置失败：" + stage); return; }
            if (!EditorApplication.isPlaying) { Note("等待进入 playmode"); return; }
            switch (stage)
            {
                case "boot":
                    controller = C;
                    if (controller?.Message?.StartsWith("开局失败", StringComparison.Ordinal) == true)
                        throw new InvalidOperationException(controller.Message);
                    if (controller?.State == null || Object.FindAnyObjectByType<SceneLoadingCover>() != null)
                    { Note("等待序章进度"); return; }
                    if (!BaseDemoPrologue.Village(controller.State)) throw new InvalidOperationException("新档未进入白桦村");
                    CaptureViewports("ui-skin-prologue");
                    controller.LoadDemoStage("camp", "female");
                    stage = "loading"; stageAt = EditorApplication.timeSinceStartup;
                    break;
                case "loading":
                    controller = C;
                    if (controller?.State == null) { if (EditorApplication.timeSinceStartup - stageAt > 150) Finish(2, "阶段档载入超时"); return; }
                    if (controller.State.Campaign.Location != "base") return;
                    H?.ClosePanel();
                    stage = "wake"; frameDeadline = Time.frameCount + 5;
                    break;
                case "wake":
                    // camp 阶段档停在 Waking：Demo 里「先了解处境」之前不许出营地，所以先完成醒来目标。
                    if (Time.frameCount < frameDeadline) return;
                    if (controller.State.Demo.Stage == BaseDemoStage.Waking)
                    {
                        if (!controller.Change(s =>
                        {
                            if (s.Demo.Stage != BaseDemoStage.Waking) return;
                            var home = BaseSpatial.Objects(s).Single(o => o.Id == "home");
                            // 营帐是带地基的设施，直接写中心点会落进不可通行格；走门前格。
                            var spot = home.Blocks ? BaseStageWalk.Door(s, home.Id) : new BasePoint(home.X, home.Y);
                            s.PlayerX = spot.X; s.PlayerY = spot.Y; BaseAdventure.Arrive(s);
                            if (!BaseDemo.AcknowledgeWaking(s)) throw new InvalidOperationException("醒来目标未完成");
                        }, "截图：完成醒来")) { Finish(2, "醒来失败：" + controller.Message); return; }
                    }
                    stage = "camp-shot"; frameDeadline = Time.frameCount + 45;
                    break;
                case "camp-shot":
                    if (Time.frameCount < frameDeadline) return;
                    CaptureViewports("ui-skin-camp-hud");
                    H.OpenMenu();
                    stage = "camp-menu-shot"; frameDeadline = Time.frameCount + 20;
                    break;
                case "camp-menu-shot":
                    if (Time.frameCount < frameDeadline) return;
                    RequirePanel("CampMenu");
                    Capture("ui-skin-camp-menu");
                    H.OpenResources();
                    stage = "resources-shot"; frameDeadline = Time.frameCount + 20;
                    break;
                case "resources-shot":
                    if (Time.frameCount < frameDeadline) return;
                    Capture("ui-skin-resources");
                    H.OpenCalendar();
                    stage = "calendar-shot"; frameDeadline = Time.frameCount + 20;
                    break;
                case "calendar-shot":
                    if (Time.frameCount < frameDeadline) return;
                    Capture("ui-skin-calendar");
                    H.OpenLandMap();
                    stage = "landmap-shot"; frameDeadline = Time.frameCount + 20;
                    break;
                case "landmap-shot":
                    if (Time.frameCount < frameDeadline) return;
                    Capture("ui-skin-landmap");
                    H.OpenQuests();
                    stage = "quests-shot"; frameDeadline = Time.frameCount + 20;
                    break;
                case "quests-shot":
                    if (Time.frameCount < frameDeadline) return;
                    Capture("ui-skin-quests");
                    H.OpenQuests("achievement");
                    stage = "achievements-shot"; frameDeadline = Time.frameCount + 20;
                    break;
                case "achievements-shot":
                    if (Time.frameCount < frameDeadline) return;
                    Capture("ui-skin-achievements");
                    H.ClosePanel();
                    H.OpenBackpack();
                    stage = "backpack-shot"; frameDeadline = Time.frameCount + 20;
                    break;
                case "backpack-shot":
                    if (Time.frameCount < frameDeadline) return;
                    RequirePanel("PartyInventory");
                    CaptureViewports("ui-skin-backpack");
                    VerifyAuthoredLayout();
                    // 选中一件物品，验证说明区已改为点选后才出现的小弹框。
                    var firstItem = BasePartyInventory.Entries(controller.State).FirstOrDefault(e => e != null && !string.IsNullOrEmpty(e.Holder) && !e.Worn);
                    if (firstItem != null)
                    {
                        var hudType = H.GetType();
                        hudType.GetField("inventorySelected", BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(H, firstItem.Id);
                        hudType.GetMethod("OpenInventory", BindingFlags.Instance | BindingFlags.NonPublic)?.Invoke(H, new object[] { false });
                        stage = "backpack-menu-shot"; frameDeadline = Time.frameCount + 20;
                        break;
                    }
                    OpenWarehouseShot();
                    break;
                case "backpack-menu-shot":
                    if (Time.frameCount < frameDeadline) return;
                    Capture("ui-skin-backpack-menu");
                    OpenWarehouseShot();
                    break;
                case "warehouse-shot":
                    if (Time.frameCount < frameDeadline) return;
                    RequirePanel("PartyWarehouse");
                    Capture("ui-skin-warehouse");
                    H.ClosePanel();
                    BaseBattleGateway.MainMenu = true;
                    try { H.OpenMenu(); } finally { BaseBattleGateway.MainMenu = false; }
                    stage = "menu-shot"; frameDeadline = Time.frameCount + 40;
                    break;
                case "menu-shot":
                    if (Time.frameCount < frameDeadline) return;
                    Capture("ui-skin-mainmenu");
                    H.ClosePanel();
                    if (!controller.Change(s =>
                    {
                        var exit = BaseSpatial.Objects(s).Single(o => o.Id == "exit_east");
                        s.PlayerX = exit.X; s.PlayerY = exit.Y; BaseAdventure.Arrive(s);
                        if (!BaseCampaign.Travel(s, "town")) throw new InvalidOperationException("前往城镇失败");
                    }, "截图：前往城镇")) { Finish(2, "前往城镇被拒：" + controller.Message); return; }
                    stage = "to-town"; frameDeadline = Time.frameCount + 90; stageAt = EditorApplication.timeSinceStartup;
                    break;
                case "to-town":
                    controller = C;
                    if (controller?.State == null) return;
                    if (controller.State.Campaign.Location != "town")
                    {
                        if (EditorApplication.timeSinceStartup - stageAt > 60) Finish(2, "未到达城镇");
                        return;
                    }
                    if (Time.frameCount < frameDeadline) return;
                    Approach("town_quest");
                    stage = "town-arrive"; frameDeadline = Time.frameCount + 30;
                    break;
                case "town-arrive":
                    if (Time.frameCount < frameDeadline) return;
                    H.OpenObject("town_quest");
                    RequirePanel("NpcDialogue");
                    if (!H.DialogueExitLocked) throw new InvalidOperationException("首次领主对白没有锁定退出");
                    H.ClosePanel();
                    if (!H.DialogueOpen) throw new InvalidOperationException("中途关闭了首次领主对白");
                    FinishDialogue();
                    if (H.DialogueExitLocked) throw new InvalidOperationException("领主叙述完成后仍锁定退出");
                    stage = "dialogue-shot"; frameDeadline = Time.frameCount + 45;
                    break;
                case "dialogue-shot":
                    if (Time.frameCount < frameDeadline) return;
                    RequirePanel("NpcDialogue");
                    CaptureViewports("ui-skin-dialogue");
                    H.ClosePanel();
                    Approach("town_peddler");
                    H.OpenObject("town_peddler");
                    FinishDialogue();
                    Click("看看货品");
                    RequirePanel("TownTrade");
                    stage = "trade-shot"; frameDeadline = Time.frameCount + 30;
                    break;
                case "trade-shot":
                    if (Time.frameCount < frameDeadline) return;
                    CaptureViewports("ui-skin-trade");
                    H.ClosePanel();
                    controller.LoadDemoStage("defense", "female");
                    stage = "defense-ready"; stageAt = EditorApplication.timeSinceStartup;
                    break;
                case "defense-ready":
                    controller = C;
                    if (controller?.State == null || controller.State.Demo.Stage != BaseDemoStage.DefenseWarned
                        || Object.FindAnyObjectByType<SceneLoadingCover>() != null) return;
                    H.OpenBackpack();
                    if (RequirePanel("PartyInventory").Find("Cards").childCount != 3)
                        throw new InvalidOperationException("整备阶段背包应同屏显示三名队员");
                    stage = "defense-inventory-shot"; frameDeadline = Time.frameCount + 5;
                    break;
                case "defense-inventory-shot":
                    if (Time.frameCount < frameDeadline) return;
                    CaptureViewports("ui-skin-backpack-three");
                    H.ClosePanel();
                    Approach("demo_defense_start");
                    controller.BeginDemoDefense("none");
                    FinishDialogue();
                    Click("拔剑迎战");
                    stage = "battle"; frameDeadline = Time.frameCount + 60; stageAt = EditorApplication.timeSinceStartup;
                    break;
                case "battle":
                    if (Field(controller, "combatController") == null)
                    {
                        if (EditorApplication.timeSinceStartup - stageAt > 30) Finish(2, "首防未开战：" + controller.Message);
                        frameDeadline = Time.frameCount + 20;
                        return;
                    }
                    if (Time.frameCount < frameDeadline) return;
                    CaptureViewports("ui-icons-battle-hud");
                    stage = "done"; frameDeadline = Time.frameCount + 60;
                    break;
                case "done":
                    if (Time.frameCount < frameDeadline) return;
                    Finish(0, "OK");
                    break;
            }
        }

        private static void VerifyAuthoredLayout()
        {
            var source = (RectTransform)controller.transform.Find("UI 布局源/PartyMemberCard/BagSlot_0");
            var image = source.GetComponent<Image>();
            var originalPosition = source.anchoredPosition;
            var originalColor = image.color;
            try
            {
                source.anchoredPosition += new Vector2(7, -3);
                image.color = new Color32(190, 141, 84, 255);
                H.ClosePanel();
                H.OpenBackpack();
                var card = RequirePanel("PartyInventory").Find("Cards").GetChild(0);
                var live = (RectTransform)card.Find("BagSlot_0");
                if (Vector2.Distance(source.anchoredPosition, live.anchoredPosition) > .01f
                    || Vector4.Distance(image.color, live.GetComponent<Image>().color) > .005f)
                    throw new InvalidOperationException("背包格运行时覆盖了场景位置或配色");
                Log.Add("[PASS] 场景编辑的背包格位置与配色在重新打开时生效");
            }
            finally { source.anchoredPosition = originalPosition; image.color = originalColor; }
        }

        private static void OpenWarehouseShot()
        {
            H.ClosePanel();
            Approach("home");
            H.OpenObject("home");
            Click("仓库");
            stage = "warehouse-shot"; frameDeadline = Time.frameCount + 20;
        }

        private static Transform RequirePanel(string name)
        {
            var value = Field(H, "panel") as RectTransform;
            if (!H.PanelOpen || value == null || value.name != name)
                throw new InvalidOperationException("未进入 " + name + "：" + controller.Message);
            return value;
        }

        private static void Approach(string id)
        {
            if (!controller.Change(s =>
            {
                var target = BaseSpatial.Objects(s).Single(o => o.Id == id);
                var point = target.Blocks ? BaseStageWalk.Door(s, id) : new BasePoint(target.X, target.Y);
                s.PlayerX = point.X; s.PlayerY = point.Y;
                BaseInteraction.Walk(new BasePoint(target.X - point.X, target.Y - point.Y));
                BaseAdventure.Arrive(s);
            }, "界面检查")) throw new InvalidOperationException(controller.Message);
        }

        private static void FinishDialogue()
        {
            RequirePanel("NpcDialogue");
            for (int i = 0; i < 80 && H.ContinueDialogue(); i++) { }
        }

        private static void Click(string label)
        {
            var button = ((RectTransform)Field(H, "panel")).GetComponentsInChildren<Button>()
                .FirstOrDefault(b => b.interactable && b.GetComponentInChildren<Text>()?.text == label);
            if (button == null) throw new InvalidOperationException("找不到可操作的「" + label + "」");
            button.onClick.Invoke();
        }
        private static object Field(object target, string name)
            => target?.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(target);

        private static void Note(string message)
        {
            if (EditorApplication.timeSinceStartup < nextLog) return;
            nextLog = EditorApplication.timeSinceStartup + 15;
            Debug.Log("[UiSkin] " + message + "（阶段 " + stage + "）");
        }

        private static void CaptureViewports(string name)
        {
            Capture(name);
            Capture(name + "-1080p", 1920, 1080);
            Capture(name + "-narrow", 1024, 640);
        }

        private static void Capture(string name, int w = 1280, int h = 720)
        {
            var cams = Camera.allCameras.Where(c => c.enabled && c.targetTexture == null).OrderBy(c => c.depth).ToList();
            if (cams.Count == 0 && controller?.WorldCamera != null) cams.Add(controller.WorldCamera);
            if (cams.Count == 0) { Log.Add("[失败] " + name + "：找不到相机"); failed = true; return; }
            var fit = GameFrameLayout.Fit(w, h, 0, 0, w, h);
            var area = new Rect((float)fit.X, (float)fit.Y, (float)fit.Width, (float)fit.Height);
            foreach (var frame in Object.FindObjectsByType<GameFrameCanvas>()) frame.ApplyLayout(area, w, h);
            var rects = cams.ToDictionary(c => c, c => c.rect);
            var targets = cams.ToDictionary(c => c, c => c.targetTexture);
            var rt = new RenderTexture(w, h, 24); rt.Create();
            var uiRt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32); uiRt.Create();
            var prev = RenderTexture.active;
            foreach (var cam in cams) { cam.rect = new Rect(0, 0, 1, 1); cam.targetTexture = rt; }
            foreach (var cam in cams) cam.Render();
            var tex = Read(rt, w, h);
            var top = cams.LastOrDefault();
            var canvases = Object.FindObjectsByType<Canvas>().Where(c => c.isRootCanvas && c.isActiveAndEnabled && c.renderMode == RenderMode.ScreenSpaceOverlay).OrderBy(c => c.sortingOrder).ToArray();
            var layers = new Dictionary<GameObject, int>();
            if (top != null && canvases.Length > 0)
            {
                int mask = top.cullingMask; var clear = top.clearFlags; var bg = top.backgroundColor;
                foreach (var canvas in canvases)
                {
                    foreach (var t in canvas.GetComponentsInChildren<Transform>(true)) { layers[t.gameObject] = t.gameObject.layer; t.gameObject.layer = 31; }
                    canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = top; canvas.planeDistance = Mathf.Min(top.farClipPlane * .5f, top.nearClipPlane + 1f);
                }
                Canvas.ForceUpdateCanvases();
                top.targetTexture = uiRt; top.cullingMask = 1 << 31; top.clearFlags = CameraClearFlags.SolidColor; top.backgroundColor = new Color(0, 0, 0, 0); top.Render();
                top.cullingMask = mask; top.clearFlags = clear; top.backgroundColor = bg;
                foreach (var canvas in canvases) { canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.worldCamera = null; }
                foreach (var pair in layers) if (pair.Key != null) pair.Key.layer = pair.Value;
                var ui = Read(uiRt, w, h);
                var a = tex.GetPixels32(); var b = ui.GetPixels32();
                for (int i = 0; i < a.Length; i++)
                {
                    int al = b[i].a; if (al == 0) continue;
                    a[i] = new Color32((byte)Mathf.Min(255, b[i].r + a[i].r * (255 - al) / 255), (byte)Mathf.Min(255, b[i].g + a[i].g * (255 - al) / 255), (byte)Mathf.Min(255, b[i].b + a[i].b * (255 - al) / 255), 255);
                }
                tex.SetPixels32(a); tex.Apply(); Object.DestroyImmediate(ui);
            }
            foreach (var cam in cams) { cam.targetTexture = targets[cam]; cam.rect = rects[cam]; }
            foreach (var frame in Object.FindObjectsByType<GameFrameCanvas>()) frame.Apply();
            RenderTexture.active = prev; rt.Release(); uiRt.Release();
            File.WriteAllBytes(Path.Combine(OutDir, name + ".png"), tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            Log.Add("## " + name + " ｜ 地点：" + controller?.State?.Campaign.Location + " ｜ 阶段：" + controller?.State?.Demo?.Stage
                + " ｜ 页面：" + (H != null && H.PanelOpen ? "有模态页" : "无模态页") + " ｜ Canvas=" + canvases.Length);
            Debug.Log("[UiSkin] 截图 " + name);
        }

        private static Texture2D Read(RenderTexture rt, int w, int h)
        {
            RenderTexture.active = rt; var tex = new Texture2D(w, h, TextureFormat.RGBA32, false); tex.ReadPixels(new Rect(0, 0, w, h), 0, 0); tex.Apply(); return tex;
        }

        private static void Finish(int code, string message)
        {
            EditorApplication.update -= Tick;
            Log.Insert(0, "exit=" + code + " " + message + " ｜ " + DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
            File.WriteAllText(Path.Combine(OutDir, "ui-skin-report.txt"), string.Join("\n", Log));
            if (code == 0) Debug.Log("[UiSkin] " + message); else Debug.LogError("[UiSkin] " + message);
            failed = code != 0;
            EditorApplication.isPlaying = false;
            EditorApplication.Exit(code);
        }
    }
}

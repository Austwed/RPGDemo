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
    // 不加 -nographics、不加 -quit。流程：读 camp 阶段档 → 营地 HUD → 主菜单 → 进镇与镇民小芸对白 → 独立战斗 HUD。
    // 产物：Logs/ui-skin-*.png 与 Logs/ui-skin-report.txt。
    // 世界地图不在此脚本内：Demo 阶段档的 WorldJourney.Nodes／Entries 为空且全流程走领地内直达，
    // 游戏内没有可进入的世界地图画面，见 LOG 记录；C6 以 VerifyWorldArt 的静态断言为准。
    // 截图沿用 UiInventoryCapture 的双遍合成：世界相机渲一张，屏幕空间 Overlay 的 Canvas 再单独渲到透明底按 alpha 叠回。
    public static class UiSkinScreenshot
    {
        private const string OutDir = "Logs";
        private static double deadline, stageAt, nextLog;
        private static int frameDeadline;
        private static string stage = "boot";
        private static BaseSceneController controller;
        private static bool failed;
        private static readonly List<string> Log = new List<string>();
        private static PropertyInfo devProperty; private static object devOriginal;

        private static BaseSceneController C => Object.FindAnyObjectByType<BaseSceneController>();
        private static BaseHud H => C?.Hud;

        public static void Run()
        {
            Directory.CreateDirectory(OutDir);
            foreach (var old in Directory.GetFiles(OutDir, "ui-skin-*.png")) File.Delete(old);
            UnityEditor.SceneManagement.EditorSceneManager.OpenScene(DemoStartup.Launcher);
            deadline = EditorApplication.timeSinceStartup + 480;
            EditorApplication.update += Tick;
            EditorApplication.EnterPlaymode();
        }

        private static void Tick()
        {
            if (failed || EditorApplication.timeSinceStartup > deadline) { Finish(2, "超时或前置失败：" + stage); return; }
            if (!EditorApplication.isPlaying) { Note("等待进入 playmode"); return; }
            switch (stage)
            {
                case "boot":
                    controller = C;
                    if (controller == null) { Note("等待场景控制器"); return; }
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
                    Capture("ui-skin-camp-hud");
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
                    if (!H.PanelOpen) Log.Add("[说明] 队伍背包未打开：" + controller.Message);
                    Capture("ui-skin-backpack");
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
                    H.ClosePanel();
                    BaseBattleGateway.MainMenu = true;
                    try { H.OpenMenu(); } finally { BaseBattleGateway.MainMenu = false; }
                    stage = "menu-shot"; frameDeadline = Time.frameCount + 40;
                    break;
                case "backpack-menu-shot":
                    if (Time.frameCount < frameDeadline) return;
                    Capture("ui-skin-backpack-menu");
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
                    if (!controller.Change(s =>
                    {
                        var girl = BaseSpatial.Objects(s).Single(o => o.Id == "town_girl");
                        s.PlayerX = girl.X; s.PlayerY = girl.Y + 1; BaseAdventure.Arrive(s);
                    }, "截图：走向镇民小芸")) { Finish(2, "移动被拒：" + controller.Message); return; }
                    stage = "town-arrive"; frameDeadline = Time.frameCount + 30;
                    break;
                case "town-arrive":
                    if (Time.frameCount < frameDeadline) return;
                    H.OpenObject("town_girl");
                    stage = "dialogue-shot"; frameDeadline = Time.frameCount + 45;
                    break;
                case "dialogue-shot":
                    if (Time.frameCount < frameDeadline) return;
                    if (!H.PanelOpen) Log.Add("[说明] 小芸对白未打开：" + controller.Message);
                    Capture("ui-skin-dialogue");
                    H.ClosePanel();
                    if (!StartBattle()) { Log.Add("[说明] 独立战斗入口不存在，战斗 HUD 跳过"); Finish(0, "四张，战斗 HUD 缺失"); return; }
                    stage = "battle"; frameDeadline = Time.frameCount + 60; stageAt = EditorApplication.timeSinceStartup;
                    break;
                case "battle":
                    if (Field(controller, "combatController") == null)
                    {
                        if (EditorApplication.timeSinceStartup - stageAt > 90) Finish(2, "独立战斗未进入");
                        frameDeadline = Time.frameCount + 20;
                        return;
                    }
                    if (Time.frameCount < frameDeadline) return;
                    Capture("ui-icons-battle-hud");
                    stage = "done"; frameDeadline = Time.frameCount + 60;
                    break;
                case "done":
                    if (Time.frameCount < frameDeadline) return;
                    Finish(0, "OK");
                    break;
            }
        }

        // 表现权探针：prefab 里故意给第 0 格留了 3px 哨兵；C# 硬编码是 x=-132。
        // 运行时若读到 prefab 的值、且不等于硬编码值，才证明版式权真的交给了 prefab。
        private static void VerifyAuthoredLayout()
        {
            if(!UiAuthoredLayout.Enabled){Log.Add("[说明] 表现权总开关关闭，探针跳过");return;}
            var prefab=Resources.Load<GameObject>(UiAuthoredLayout.Folder+"PartyMemberCard");
            if(prefab==null){Log.Add("[说明] 尚无 AuthoredUI/PartyMemberCard.prefab，探针跳过");return;}
            // 只比对名字唯一的绑定节点。模板里有一批同名的 Text 样例节点（样式原型），
            // 运行时按当次数据行复用，逐一对位没有意义，跳过它们。
            var authored=prefab.GetComponentsInChildren<UiVisualNode>(true)
                .Where(n=>!string.IsNullOrEmpty(n.BindingName))
                .GroupBy(n=>n.BindingName).Where(g=>g.Count()==1).Select(g=>g.First()).ToArray();
            var live=Object.FindObjectsByType<UiVisualNode>(FindObjectsSortMode.None)
                .Where(n=>n.gameObject.activeInHierarchy&&!string.IsNullOrEmpty(n.BindingName))
                .GroupBy(n=>n.BindingName).ToDictionary(g=>g.Key,g=>g.First());
            // 全节点比对：美术可以随时拖任何一格，运行时都必须与 prefab 一致；
            // 第 0 格的位置／颜色哨兵只是额外的「已知坏样本」，被改动也不该让探针误报。
            int matched=0,mismatched=0;string first="";
            foreach(var n in authored)
            {
                if(!live.TryGetValue(n.BindingName,out var r))continue;
                matched++;
                var a=(RectTransform)n.transform;var b=(RectTransform)r.transform;
                var ia=n.GetComponent<Image>();var ib=r.GetComponent<Image>();
                bool ok=Vector2.Distance(a.anchoredPosition,b.anchoredPosition)<.01f
                     && Vector2.Distance(a.sizeDelta,b.sizeDelta)<.01f
                     && (ia==null||ib==null||Vector4.Distance(ia.color,ib.color)<.005f);
                if(!ok&&mismatched++==0)
                    first=n.BindingName+"：prefab "+a.anchoredPosition+"/"+a.sizeDelta+"／颜色 "+(ia!=null?ia.color.ToString():"无")
                        +" vs 运行时 "+b.anchoredPosition+"/"+b.sizeDelta+"／颜色 "+(ib!=null?ib.color.ToString():"无");
            }
            var slot0=authored.FirstOrDefault(n=>n.BindingName=="BagSlot_0");
            bool sentinel=slot0!=null&&Mathf.Abs(((RectTransform)slot0.transform).anchoredPosition.x+132f)>1f;
            if(matched>=10&&mismatched==0)
                Log.Add("[PASS] 表现权由 prefab 主导："+matched+" 个绑定节点的位置／尺寸／配色与 prefab 一致"
                    +(sentinel?"；位置哨兵仍在（C# 硬编码 -132 未生效）":"；哨兵已被改动，改由全节点比对守护"));
            else{Log.Add("[失败] 表现权未交给 prefab：匹配 "+matched+" 个、不一致 "+mismatched+" 个"+(first!=""?"，首例 "+first:""));failed=true;}
        }

        private static bool StartBattle()
        {
            var dev = FindType("BaseConquest.Battle.RuntimeDeveloperOptions");
            var prop = dev?.GetProperty("Enabled", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (prop != null) { devProperty = prop; devOriginal = prop.GetValue(null); prop.SetValue(null, true); }
            var mi = controller.GetType().GetMethod("StartStandaloneBattle", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            var preset = FindType("BaseConquest.Base.BaseStandaloneBattlePreset");
            if (mi == null || preset == null) return false;
            mi.Invoke(controller, new object[] { Enum.Parse(preset, "Mixed") });
            return true;
        }

        private static Type FindType(string fullName) => AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(fullName)).FirstOrDefault(t => t != null);
        private static object Field(object target, string name)
            => target?.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(target);

        private static void Note(string message)
        {
            if (EditorApplication.timeSinceStartup < nextLog) return;
            nextLog = EditorApplication.timeSinceStartup + 15;
            Debug.Log("[UiSkin] " + message + "（阶段 " + stage + "）");
        }

        private static void Capture(string name)
        {
            const int w = 1280, h = 720;
            var cams = Camera.allCameras.Where(c => c.enabled && c.targetTexture == null).OrderBy(c => c.depth).ToList();
            if (cams.Count == 0 && controller?.WorldCamera != null) cams.Add(controller.WorldCamera);
            if (cams.Count == 0) { Log.Add("[失败] " + name + "：找不到相机"); failed = true; return; }
            foreach (var frame in Object.FindObjectsByType<BaseConquest.Presentation.GameFrameCanvas>(FindObjectsSortMode.None)) frame.ApplyLayout(new Rect(0, 0, w, h), w, h);
            var rects = cams.ToDictionary(c => c, c => c.rect);
            var targets = cams.ToDictionary(c => c, c => c.targetTexture);
            var rt = new RenderTexture(w, h, 24); rt.Create();
            var uiRt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32); uiRt.Create();
            var prev = RenderTexture.active;
            foreach (var cam in cams) { cam.rect = new Rect(0, 0, 1, 1); cam.targetTexture = rt; }
            foreach (var cam in cams) cam.Render();
            var tex = Read(rt, w, h);
            var top = cams.LastOrDefault();
            var canvases = Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None).Where(c => c.isRootCanvas && c.isActiveAndEnabled && c.renderMode == RenderMode.ScreenSpaceOverlay).OrderBy(c => c.sortingOrder).ToArray();
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
            if (devProperty != null && devOriginal != null) { try { devProperty.SetValue(null, devOriginal); } catch (Exception e) { Debug.LogError("[UiSkin] 开发者模式还原失败：" + e.Message); } devProperty = null; }
            failed = code != 0;
            EditorApplication.isPlaying = false;
            EditorApplication.Exit(code);
        }
    }
}

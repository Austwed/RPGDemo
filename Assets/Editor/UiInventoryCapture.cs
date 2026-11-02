using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BaseConquest.Base;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace BaseConquest.Editor
{
    // UI 界面盘点截图（agent3，2026-11-02）：-batchmode（带图形，不加 -nographics、不加 -quit）
    //   Unity.exe -batchmode -projectPath RPGDemo -executeMethod BaseConquest.Editor.UiInventoryCapture.Run
    // 读阶段档 → 依次打开主菜单、营地顶栏、各设施／NPC 交互页、背包、任务、城镇、迎战、战斗 HUD、选角 → 截图并导出每张画面上可见的按钮。
    // 世界相机先渲染一张图；屏幕空间 Overlay 的 Canvas 再临时改成 Camera 模式、单独渲染到透明底，按 alpha 叠回世界画面，结束后还原。
    // 产物：Logs/ui-capture/*.png 与 report.txt；环境变量 UI_CAPTURE_HERO=male|female（默认 female）。
    // 存档：开跑前备份 persistentDataPath/BaseConquest，结束时还原（见 BackupSaves／RestoreSaves）；测试留存（阶段测试档、独立战斗测试）都用反射调用，删掉后本工具照样编译。
    // 阶段档迎战目前进不了战斗（「首防对白前不应生成第二波」），战斗 HUD 退回用独立战斗测试截；独立战斗测试被删后 63～66 只截到当时画面并在 report 里注明。
    public static class UiInventoryCapture
    {
        private static readonly string OutDir = Path.Combine("Logs", "ui-capture");
        private static bool NoBattle;
        private sealed class Step { public string Name; public Action Do; public Func<bool> Until; public int Wait = 20; public bool Shot = true; public double Timeout = 90; }
        private static readonly List<Step> Steps = new List<Step>();
        private static readonly List<string> Report = new List<string>();
        private static int index, shots, failures;
        private static Step current;
        private static bool started;
        private static double stepAt, deadline;
        private static int frameAt;
        private static string hero = "female";

        private static BaseSceneController C => Object.FindAnyObjectByType<BaseSceneController>();
        private static BaseHud H => C?.Hud;

        public static void Run()
        {
            Directory.CreateDirectory(OutDir);
            foreach (var old in Directory.GetFiles(OutDir, "*.png")) File.Delete(old);
            NoBattle = false;
            BackupSaves();
            hero = Environment.GetEnvironmentVariable("UI_CAPTURE_HERO") == "male" ? "male" : "female";
            Build();
            UnityEditor.SceneManagement.EditorSceneManager.OpenScene(DemoStartup.Launcher);
            deadline = EditorApplication.timeSinceStartup + 900;
            EditorApplication.update += Tick;
            EditorApplication.EnterPlaymode();
        }

        private static void Add(string name, Action act, int wait = 20, bool shot = true, Func<bool> until = null, double timeout = 90)
            => Steps.Add(new Step { Name = name, Do = act, Wait = wait, Shot = shot, Until = until, Timeout = timeout });
        private static void Insert(List<Step> more) => Steps.InsertRange(index, more);

        private static object Call(object target, string method, params object[] args)
        {
            var mi = target.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .FirstOrDefault(m => m.Name == method && m.GetParameters().Length >= args.Length && m.GetParameters().Skip(args.Length).All(p => p.IsOptional));
            if (mi == null) throw new MissingMethodException(target.GetType().Name + "." + method);
            var full = args.Concat(mi.GetParameters().Skip(args.Length).Select(p => p.DefaultValue)).ToArray();
            return mi.Invoke(target, full);
        }
        // 测试留存（阶段测试档、独立战斗测试）都用反射调用：C10 删掉它们以后本工具仍能编译，缺的步骤只记一条说明。
        private static bool TryCall(object target, string method, params object[] args)
        {
            if (target == null || !target.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Any(m => m.Name == method)) return false;
            Call(target, method, args); return true;
        }
        private static Type FindType(string fullName) => AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(fullName)).FirstOrDefault(t => t != null);
        private static object Field(object target, string name)
            => target?.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(target);
        private static string Click(params string[] prefixes)
        {
            var buttons = Object.FindObjectsByType<Button>(FindObjectsSortMode.None).Where(b => b.isActiveAndEnabled && b.interactable).ToArray();
            foreach (var prefix in prefixes)
                foreach (var b in buttons)
                {
                    var t = b.GetComponentInChildren<Text>(); if (t == null || !t.text.StartsWith(prefix, StringComparison.Ordinal)) continue;
                    b.onClick.Invoke(); return t.text;
                }
            return null;
        }
        private static void Close() { if (H != null && H.PanelOpen) H.ClosePanel(); }
        private static bool Move(BaseMapObject o)
        {
            return C.Change(s =>
            {
                var p = o.Blocks ? BaseStageWalk.Door(s, o.Id) : new BasePoint(o.X, o.Y);
                s.PlayerX = p.X; s.PlayerY = p.Y; BaseAdventure.Arrive(s);
            }, "界面盘点：走到 " + o.Name);
        }

        private static void Build()
        {
            Add("boot", null, 30, false, () => C != null, 180);
            Add("load-camp", () => C.LoadDemoStage("camp", hero), 30, false);
            Add("camp-ready", null, 60, false, () => C?.State != null && C.State.Campaign.Location == "base", 180);
            Add("01_营地_苏醒剧情", () => { Close(); H.OpenDemoStory(); });
            Add("02_营地_主界面顶栏", () => { Close(); }, 30);
            Add("03_主菜单", () => { BaseBattleGateway.MainMenu = true; try { H.OpenMenu(); } finally { BaseBattleGateway.MainMenu = false; } });
            Add("04_据点菜单", () => { Close(); H.OpenMenu(); });
            Add("05_选项", () => H.OpenOptions());
            Add("06_读取存档", () => H.OpenSlots(false));
            Add("07_手动保存", () => H.OpenSlots(true));
            Add("08_帮助", () => Call(H, "OpenHelp"));
            Add("09_成就", () => H.OpenAchievements());
            Add("10_难度", () => Call(H, "OpenDifficulty"));
            Add("11_阶段测试档_测试留存", () => { if (!TryCall(H, "OpenDemoStages")) Report.Add("[说明] 阶段测试档入口已不存在（C10 清理后属正常）"); });
            Add("12_当前剧情目标", () => H.OpenDemoStory());
            Add("13_小队背包", () => { Close(); H.ToggleBackpack(); });
            Add("14_任务", () => { Close(); H.ToggleQuests(); });
            Add("15_敌情与据点", () => { Close(); Call(H, "OpenThreatOverview"); });
            Add("16_资源说明i", () => { Close(); Call(H, "RuleInfo", "资源与人员信息", "（截图用）", (Action)Close); });
            Add("wake", () =>
            {
                Close();
                if (!C.Change(s =>
                {
                    if (s.Demo.Stage != BaseDemoStage.Waking) return;
                    var home = BaseStageWalk.Door(s, "home");
                    s.PlayerX = home.X; s.PlayerY = home.Y; BaseAdventure.Arrive(s);
                    if (!BaseDemo.AcknowledgeWaking(s)) throw new InvalidOperationException("醒来目标未完成");
                }, "界面盘点：醒来")) throw new InvalidOperationException(C.Message);
            }, 30, false);
            Add("17_营帐", () => { Close(); H.OpenHome(); });
            Add("camp-objects", () => { Close(); Insert(ObjectSteps("18_营地")); }, 5, false);
            Add("to-town", () =>
            {
                Close();
                if (!C.Change(s =>
                {
                    var exit = BaseSpatial.Objects(s).Single(o => o.Id == "exit_east");
                    s.PlayerX = exit.X; s.PlayerY = exit.Y; BaseAdventure.Arrive(s);
                    if (!BaseCampaign.Travel(s, "town")) throw new InvalidOperationException("前往城镇失败");
                }, "界面盘点：前往城镇")) throw new InvalidOperationException(C.Message);
            }, 60, false);
            Add("town-ready", null, 60, false, () => C?.State != null && C.State.Campaign.Location == "town", 120);
            Add("30_城镇_主界面", () => Close(), 30);
            Add("town-objects", () => Insert(ObjectSteps("31_城镇")), 5, false);
            Add("load-defense", () => { Close(); C.LoadDemoStage("defense", hero); }, 30, false);
            Add("defense-ready", null, 60, false, () => C?.State != null && C.State.Demo.Stage == BaseDemoStage.DefenseWarned, 180);
            Add("50_迎战_主界面", () => Close(), 30);
            Add("51_迎战_来袭部队确认迎战", () => { Close(); var o = C.MapObjects.First(x => x.Id == "demo_defense_start"); if (!Move(o)) throw new InvalidOperationException(C.Message); H.OpenObject(o.Id); });
                        for (int n = 1; n <= 6; n++)
            {
                int step = n;
                Add("62_迎战流程_" + step, () =>
                {
                    if (Field(C, "combatController") != null) throw new InvalidOperationException("跳过：已进入战斗");
                    string clicked = Click("拔剑迎战", "确认迎战", "确认队伍", "作战优先", "准备迎战", "继续");
                    if (clicked == null) throw new InvalidOperationException("没有可点的迎战按钮；" + C.Message);
                    Report.Add("[点击] " + clicked);
                }, 40);
            }
            Add("standalone-fallback", () =>
            {
                if (Field(C, "combatController") != null) return;
                Report.Add("[说明] 阶段档迎战没能进入战斗（见上一条提示），改用开发者「独立即时战斗测试」截取同一套战斗 HUD（BattleMobileHud）");
                Close();
                var dev = FindType("BaseConquest.Battle.RuntimeDeveloperOptions"); var preset = FindType("BaseConquest.Base.BaseStandaloneBattlePreset");
                if (dev == null || preset == null) { Report.Add("[说明] 独立战斗测试已不存在，战斗 HUD 截图跳过"); NoBattle = true; return; }
                dev.GetProperty("Enabled", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)?.SetValue(null, true);
                if (!TryCall(C, "StartStandaloneBattle", Enum.Parse(preset, "Mixed"))) { Report.Add("[说明] 独立战斗测试入口不存在，战斗 HUD 截图跳过"); NoBattle = true; }
            }, 90, false);
            Add("63_战斗_HUD", null, 60, true, () => NoBattle || Field(C, "combatController") != null, 60);
            Add("64_战斗_队长详情", () => { if (!NoBattle) Call(Field(C, "combatController"), "ClickCommanderPortrait", 0); }, 30);
            Add("65_战斗_菜单", () => { if (NoBattle) return; Call(Field(C, "combatController"), "CloseCommanderDetails"); Call(Field(C, "combatController"), "OpenCampaignMenu"); }, 30);
            Add("66_战斗_背包", () => { if (!NoBattle) Call(Field(C, "combatController"), "OpenBattleBackpack"); }, 30);
            Add("new-game", () => { C.NewTemplate(BaseDemo.Profile); }, 60, false);
            Add("prologue-ready", null, 60, false, () => C?.State?.Demo?.Intro != null && C.State.Demo.Intro.Step == 0, 180);
            Add("70_序章_选择主角", () => { Close(); Call(H, "OpenHeroChoice"); }, 40);
        }

        private static List<Step> ObjectSteps(string prefix)
        {
            var list = new List<Step>();
            var c = C; if (c?.State == null) return list;
            string location = c.State.Campaign.Location;
            var objects = c.MapObjects.Where(o => !o.VisualOnly && !BaseEnvironment.Decorative(o) && !o.Id.StartsWith("motion-check:") && !o.Id.Contains("exit") && !o.Id.Contains("march") && !o.Id.Contains("return") && !o.Id.StartsWith("travel"))
                .GroupBy(o => o.Id).Select(g => g.First()).ToArray();
            Report.Add("[" + prefix + "] 可交互对象 " + objects.Length + "：" + string.Join("、", objects.Select(o => o.Id + "＝" + o.Name)));
            int n = 0;
            foreach (var o in objects)
            {
                var obj = o; n++;
                list.Add(new Step { Name = prefix + "_" + n.ToString("00") + "_" + Safe(obj.Name), Wait = 25, Do = () =>
                {
                    if (C.State.Campaign.Location != location) throw new InvalidOperationException("跳过：地点已变化");
                    Close();
                    if (!Move(obj)) throw new InvalidOperationException("移动被拒：" + C.Message);
                    H.OpenObject(obj.Id);
                }});
            }
            return list;
        }
        private static string Safe(string s) => new string((s ?? "").Where(ch => !Path.GetInvalidFileNameChars().Contains(ch) && ch != ' ' && ch != '·').ToArray());

        private static void Tick()
        {
            if (EditorApplication.timeSinceStartup > deadline) { Finish(2, "总超时，停在 " + current?.Name); return; }
            if (!EditorApplication.isPlaying) return;
            if (current == null)
            {
                if (index >= Steps.Count) { if (frameAt == 0) frameAt = Time.frameCount + 60; if (Time.frameCount >= frameAt) Finish(failures == 0 ? 0 : 1, "完成"); return; }
                current = Steps[index++]; started = false; stepAt = EditorApplication.timeSinceStartup;
            }
            if (!started)
            {
                if (current.Until != null && !current.Until())
                {
                    if (EditorApplication.timeSinceStartup - stepAt > current.Timeout) { Fail(current.Name, "等待条件超时"); current = null; }
                    return;
                }
                try { current.Do?.Invoke(); }
                catch (Exception e) { Fail(current.Name, (e is TargetInvocationException t ? t.InnerException : e)?.Message); current = null; return; }
                started = true; frameAt = Time.frameCount + current.Wait; return;
            }
            if (Time.frameCount < frameAt) return;
            if (current.Shot) { try { Capture(current.Name); } catch (Exception e) { Fail(current.Name, "截图失败 " + e.Message); } }
            current = null; frameAt = 0;
        }
        private static void Fail(string step, string why) { if (why == null || !why.StartsWith("跳过")) failures++; Report.Add("[失败] " + step + "：" + why); Debug.LogWarning("[UiCapture] " + step + " 失败：" + why); }

        private static void Capture(string name)
        {
            const int w = 1280, h = 720;
            var cams = Camera.allCameras.Where(c => c.enabled && c.targetTexture == null).OrderBy(c => c.depth).ToList();
            if (cams.Count == 0 && C?.WorldCamera != null) cams.Add(C.WorldCamera);
            // 1280×720 当作整块屏幕：相机视口铺满，16:9 显示区域按 1:1 摆放（下一帧 LateUpdate 会按真实屏幕还原）。
            foreach (var frame in Object.FindObjectsByType<BaseConquest.Presentation.GameFrameCanvas>(FindObjectsSortMode.None)) frame.ApplyLayout(new Rect(0, 0, w, h), w, h);
            var rects = cams.ToDictionary(c => c, c => c.rect);
            var targets = cams.ToDictionary(c => c, c => c.targetTexture);
            var rt = new RenderTexture(w, h, 24); rt.Create();
            var uiRt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32); uiRt.Create();
            var prev = RenderTexture.active;
            foreach (var cam in cams) { cam.rect = new Rect(0, 0, 1, 1); cam.targetTexture = rt; }
            foreach (var cam in cams) cam.Render();
            var tex = Read(rt, w, h);
            // 第二遍只画 UI 到透明底：Overlay Canvas 临时挂到最上层相机（Camera 模式、专用层），再按 alpha 叠回世界画面。
            // URP 类管线的基础相机总会清屏，所以不能直接画在同一张图上。
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
            Debug.Log("[UiCapture] " + name + " 相机=" + string.Join(",", cams.Select(c => c.name + "/" + c.clearFlags)) + " Canvas=" + canvases.Length);
            Object.DestroyImmediate(tex);
            shots++;
            var panel = Field(H, "panel") as RectTransform;
            string title = panel != null && panel.gameObject.activeInHierarchy ? panel.name : "（无模态页）";
            var buttons = canvases.SelectMany(c => c.GetComponentsInChildren<Button>(false)).Where(b => b.gameObject.activeInHierarchy)
                .Select(b => { var t = b.GetComponentInChildren<Text>(); string label = t != null ? t.text.Replace("\n", "／").Trim() : ""; if (label == "") label = "〔" + b.name + "〕"; return label + (b.interactable ? "" : "（禁用）"); });
            Report.Add("## " + name + "\n页面：" + title + " ｜ 地点：" + C?.State?.Campaign.Location + " ｜ 阶段：" + C?.State?.Demo?.Stage + "\n按钮：" + string.Join(" ｜ ", buttons));
        }

        private static Texture2D Read(RenderTexture rt, int w, int h)
        {
            RenderTexture.active = rt; var tex = new Texture2D(w, h, TextureFormat.RGBA32, false); tex.ReadPixels(new Rect(0, 0, w, h), 0, 0); tex.Apply(); return tex;
        }

        private static void Finish(int code, string note)
        {
            EditorApplication.update -= Tick;
            Report.Insert(0, "exit=" + code + " " + note + " 截图=" + shots + " 失败=" + failures + " 主角=" + hero + " 时间=" + DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
            File.WriteAllText(Path.Combine(OutDir, "report.txt"), string.Join("\n\n", Report));
            Debug.Log("[UiCapture] " + note + " 截图=" + shots + " 失败=" + failures);
            EditorApplication.isPlaying = false;
            RestoreSaves();
            EditorApplication.quitting += RestoreSaves;
            EditorApplication.Exit(code);
        }

        // 跑截图会经由 LoadDemoStage 等写入本机 Demo 存档。开跑前把 persistentDataPath/BaseConquest 整个备份到 Logs/ui-capture/save-backup，
        // 结束时用备份覆盖回去；本次运行新产生的存档文件移到 Logs/ui-capture/run-saves（不删除），保证用户原有存档不变。
        private static string SaveRoot => Path.Combine(Application.persistentDataPath, "BaseConquest");
        private static string BackupRoot => Path.Combine(OutDir, "save-backup");
        private static bool restored;
        private static void BackupSaves()
        {
            restored = false;
            if (Directory.Exists(BackupRoot)) Directory.Move(BackupRoot, BackupRoot + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"));
            Directory.CreateDirectory(BackupRoot);
            if (Directory.Exists(SaveRoot)) CopyTree(SaveRoot, BackupRoot);
            Report.Add("存档备份：" + SaveRoot + " → " + BackupRoot);
        }
        private static void RestoreSaves()
        {
            if (restored) return; restored = true;
            try
            {
                if (Directory.Exists(SaveRoot))
                    foreach (var file in Directory.GetFiles(SaveRoot, "*", SearchOption.AllDirectories))
                    {
                        var rel = file.Substring(SaveRoot.Length).TrimStart('/', '\\');
                        if (File.Exists(Path.Combine(BackupRoot, rel))) continue;
                        var moved = Path.Combine(OutDir, "run-saves", rel); Directory.CreateDirectory(Path.GetDirectoryName(moved));
                        if (File.Exists(moved)) File.Delete(moved);
                        File.Move(file, moved);
                    }
                CopyTree(BackupRoot, SaveRoot);
                Debug.Log("[UiCapture] 存档已还原：" + SaveRoot);
            }
            catch (Exception e) { Debug.LogError("[UiCapture] 存档还原失败，请手动从 " + BackupRoot + " 复制回 " + SaveRoot + "：" + e.Message); }
        }
        private static void CopyTree(string from, string to)
        {
            foreach (var file in Directory.GetFiles(from, "*", SearchOption.AllDirectories))
            {
                var target = Path.Combine(to, file.Substring(from.Length).TrimStart('/', '\\'));
                Directory.CreateDirectory(Path.GetDirectoryName(target)); File.Copy(file, target, true);
            }
        }
    }
}

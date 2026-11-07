using System;
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
    /// <summary>
    /// 把「当前运行态」的背包界面烘焙成美术可直接编辑的 prefab。
    /// 初次生成：Unity.exe -batchmode -projectPath RPGDemo -executeMethod BaseConquest.Editor.UiAuthoredLayoutBake.Run -logFile Logs/ui-bake.log
    /// 之后在 Unity 里 Play → 打开背包 → 菜单 Tools/UI/烘焙当前背包为可编辑 Prefab 可随时重新烘焙。
    ///
    /// 产物（Resources 下，运行时优先级最高）：
    ///   Assets/Resources/Presentation/AuthoredUI/Page_party_inventory.prefab
    ///   Assets/Resources/Presentation/AuthoredUI/PartyMemberCard.prefab
    /// 已存在时初次生成命令会直接跳过，绝不覆盖美术在 prefab 里做过的调整；需要强制重建请手动删除这两个文件。
    /// </summary>
    public static class UiAuthoredLayoutBake
    {
        private const string Folder = "Assets/Resources/Presentation/AuthoredUI";
        private const string PanelKey = "Page_party_inventory";
        private const string CardKey = "PartyMemberCard";
        private static double deadline, stageAt;
        private static int frameDeadline;
        private static string stage = "boot";
        private static BaseSceneController controller;

        public static void Run()
        {
            if (File.Exists(Path.Combine(Folder, PanelKey + ".prefab")) && File.Exists(Path.Combine(Folder, CardKey + ".prefab")))
            {
                Debug.Log("[UiBake] 可编辑 prefab 已存在，跳过；不覆盖已有调整。需要重建请先删除这两个文件。");
                EditorApplication.Exit(0);
                return;
            }
            UnityEditor.SceneManagement.EditorSceneManager.OpenScene(DemoStartup.Launcher);
            deadline = EditorApplication.timeSinceStartup + 300;
            EditorApplication.update += Tick;
            EditorApplication.EnterPlaymode();
        }

        private static void Tick()
        {
            if (EditorApplication.timeSinceStartup > deadline) { Stop(2, "超时：" + stage); return; }
            if (!EditorApplication.isPlaying) return;
            // 烘焙必须捕获「今天这一版」的实际版式，所以这一轮强制走 C# 主导的旧路径。
            UiAuthoredLayout.Enabled = false;
            // 阶段档会重建场景，控制器每帧重取，避免拿到已销毁的旧 HUD。
            controller = Object.FindAnyObjectByType<BaseSceneController>() ?? controller;
            switch (stage)
            {
                case "boot":
                    if (controller == null) return;
                    controller.LoadDemoStage("camp", "female");
                    stage = "loading"; stageAt = EditorApplication.timeSinceStartup;
                    break;
                case "loading":
                    // 阶段档会重建场景，boot 阶段缓存的控制器会失效，必须每帧重取（与 UiSkinScreenshot 同）。
                    controller = Object.FindAnyObjectByType<BaseSceneController>();
                    if (controller?.State == null)
                    {
                        if (EditorApplication.timeSinceStartup - stageAt > 150) Stop(2, "阶段档载入超时");
                        return;
                    }
                    if (controller.State.Campaign.Location != "base") return;
                    controller.Hud?.ClosePanel();
                    stage = "wake"; frameDeadline = Time.frameCount + 5;
                    break;
                case "wake":
                    if (Time.frameCount < frameDeadline) return;
                    if (controller.State.Demo.Stage == BaseDemoStage.Waking)
                    {
                        if (!controller.Change(s =>
                        {
                            if (s.Demo.Stage != BaseDemoStage.Waking) return;
                            var home = BaseSpatial.Objects(s).Single(o => o.Id == "home");
                            var spot = home.Blocks ? BaseStageWalk.Door(s, home.Id) : new BasePoint(home.X, home.Y);
                            s.PlayerX = spot.X; s.PlayerY = spot.Y; BaseAdventure.Arrive(s);
                            if (!BaseDemo.AcknowledgeWaking(s)) throw new InvalidOperationException("醒来目标未完成");
                        }, "烘焙：完成醒来")) { Stop(2, "醒来失败：" + controller.Message); return; }
                    }
                    controller.Hud.OpenBackpack();
                    stage = "bake"; frameDeadline = Time.frameCount + 30;
                    break;
                case "bake":
                    if (Time.frameCount < frameDeadline) return;
                    try { Bake(); Stop(0, "OK"); }
                    catch (Exception e) { Debug.LogException(e); Stop(2, e.Message); }
                    break;
            }
        }

        private static void Stop(int code, string message)
        {
            EditorApplication.update -= Tick;
            if (code == 0) Debug.Log("[UiBake] " + message); else Debug.LogError("[UiBake] " + message);
            EditorApplication.isPlaying = false;
            EditorApplication.Exit(code);
        }

        [MenuItem("Tools/UI/烘焙当前背包为可编辑 Prefab")]
        public static void BakeCurrent()
        {
            if (!EditorApplication.isPlaying) { Debug.LogError("[UiBake] 请先进入 Play 并打开小队背包"); return; }
            var c = Object.FindAnyObjectByType<BaseSceneController>();
            if (c?.Hud == null) { Debug.LogError("[UiBake] 找不到运行中的 HUD"); return; }
            c.Hud.OpenBackpack();
            try { Bake(); Debug.Log("[UiBake] 已重新烘焙到 " + Folder); }
            catch (Exception e) { Debug.LogException(e); }
        }

        private static void Bake()
        {
            var c = Object.FindAnyObjectByType<BaseSceneController>();
            var panel = (RectTransform)c.Hud.GetType().GetField("panel", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(c.Hud);
            if (panel == null || !panel.gameObject.activeInHierarchy) throw new InvalidOperationException("背包面板未打开");
            var card = panel.GetComponentsInChildren<UiPrefabBinding>(true).FirstOrDefault(b => b.ResourceKey == CardKey);
            if (card == null) throw new InvalidOperationException("面板里没有 PartyMemberCard 实例");

            if (!AssetDatabase.IsValidFolder(Folder))
            {
                if (!AssetDatabase.IsValidFolder("Assets/Resources")) AssetDatabase.CreateFolder("Assets", "Resources");
                if (!AssetDatabase.IsValidFolder("Assets/Resources/Presentation")) AssetDatabase.CreateFolder("Assets/Resources", "Presentation");
                AssetDatabase.CreateFolder("Assets/Resources/Presentation", "AuthoredUI");
            }

            // 人物卡是运行时按人数新建的独立 prefab，不能把当次的人物实例烘进面板外壳里。
            Save(Object.Instantiate(panel.gameObject), PanelKey, removeNestedCards: true, beforeSave: null);
            Save(Object.Instantiate(card.gameObject), CardKey, removeNestedCards: false, beforeSave: copy =>
            {
                // 哨兵：把第 0 格在 prefab 里故意挪 3px。运行时若发现它没被 C# 的硬编码坐标盖回去，
                // 就证明版式权确实在 prefab 手里（UiSkinScreenshot 的表现权探针会断言这一点）。
                var sentinel = copy.GetComponentsInChildren<UiVisualNode>(true).FirstOrDefault(n => n.BindingName == "BagSlot_0");
                if (sentinel == null) throw new InvalidOperationException("人物卡里没有 BagSlot_0 节点");
                // 位置哨兵：prefab 故意挪 3px；颜色哨兵：红通道故意减 0.04。
                // 两者都必须在运行时原样出现，才能证明版式与配色都不再被 C# 回写。
                ((RectTransform)sentinel.transform).anchoredPosition += new Vector2(3, 0);
                var image = sentinel.GetComponent<Image>();
                if (image != null) { var c = image.color; c.r = Mathf.Clamp01(c.r - .04f); image.color = c; }
            });

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[UiBake] 已生成 " + PanelKey + ".prefab / " + CardKey + ".prefab；第 0 格含 3px 版式哨兵。");
        }

        private static void Save(GameObject source, string key, bool removeNestedCards, Action<GameObject> beforeSave)
        {
            var copy = source;
            copy.name = key;
            if (removeNestedCards)
                foreach (var nested in copy.GetComponentsInChildren<UiPrefabBinding>(true).Where(b => b != copy.GetComponent<UiPrefabBinding>() && b.ResourceKey == CardKey).ToArray())
                    Object.DestroyImmediate(nested.gameObject);

            foreach (var t in copy.GetComponentsInChildren<Transform>(true))
            {
                var node = t.GetComponent<UiVisualNode>();
                if (node != null)
                {
                    node.UseAuthoredLayout = true;   // prefab 是唯一版式源
                    node.OverrideStaticText = false; // 文字内容永远由代码填
                    node.Decoration = false;
                    if (!string.IsNullOrEmpty(node.BindingName)) t.gameObject.name = node.BindingName;
                }
                foreach (var component in t.GetComponents<Component>())
                {
                    if (component == null) continue;
                    if (component is UiDynamicContent || component.GetType().Name == "PartyInventoryCell")
                        Object.DestroyImmediate(component, true);
                }
                foreach (var button in t.GetComponents<Button>()) button.onClick.RemoveAllListeners();
            }
            var binding = copy.GetComponent<UiPrefabBinding>() ?? copy.AddComponent<UiPrefabBinding>();
            binding.ResourceKey = key;
            var revision = copy.GetComponent<DemoUiRevision>() ?? copy.AddComponent<DemoUiRevision>();
            revision.Version = 14; revision.UseThemeColors = true;

            beforeSave?.Invoke(copy);
            string path = Path.Combine(Folder, key + ".prefab").Replace('\\', '/');
            PrefabUtility.SaveAsPrefabAsset(copy, path);
            Object.DestroyImmediate(copy);
        }
    }
}

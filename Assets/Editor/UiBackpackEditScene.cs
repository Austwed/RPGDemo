using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace BaseConquest.Editor
{
    /// <summary>
    /// 背包 UI 编辑现场：把 AuthoredUI 的两份 prefab 摆进一个 1280x720 的 Canvas，
    /// 打开场景就能在 Hierarchy / Scene 里看到整机（面板 + 3 张人物卡 + 顶栏／背景占位）并直接拖版式。
    ///
    /// 用法：
    ///   1) 菜单 Tools/UI/生成背包 UI 编辑场景 —— 生成（已存在则只定位，不覆盖）。双击打开场景。
    ///   2) 在场景里拖节点调位置、尺寸、颜色、字体。
    ///   3) 菜单 Tools/UI/把编辑场景改动应用回 Prefab —— 写回 prefab 资产，运行时直接生效。
    ///
    /// 两份 prefab 实例分别挂在 PanelInstances / CardInstances 两个容器下，彼此独立，
    /// 所以 Apply 不会把预览用的人物卡塞进面板 prefab。Apply 前会还原「预览用的根节点坐标」
    /// 与所有 Text.text（文案归代码），只把表现字段写回。
    /// </summary>
    public static class UiBackpackEditScene
    {
        public const string ScenePath = "Assets/Scenes/UI_背包编辑.unity";
        private const string Folder = "Assets/Resources/Presentation/AuthoredUI";
        private const string PanelKey = "Page_party_inventory";
        private const string CardKey = "PartyMemberCard";
        private const string PanelHost = "PanelInstances";
        private const string CardHost = "CardInstances";
        private const string FrameName = "游戏显示区域 · 16比9";
        private const float CardStep = 390f;
        private const int CardCount = 3;

        [MenuItem("Tools/UI/生成背包 UI 编辑场景")]
        public static void Generate()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            { Debug.LogError("[UiEdit] 请先退出 Play 模式再生成编辑场景"); return; }
            var existing = AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath);
            if (existing != null)
            {
                EditorGUIUtility.PingObject(existing);
                Debug.Log("[UiEdit] 场景已存在：" + ScenePath + "。双击打开即可；需要重建请先删除该文件。");
                return;
            }
            var panel = Load(PanelKey);
            var card = Load(CardKey);
            if (panel == null || card == null) { Debug.LogError("[UiEdit] 缺少 AuthoredUI prefab，请先烘焙背包界面"); return; }

            // Additive 新建、存盘后立刻关闭，不切换／不卸载用户当前打开的场景。
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            try
            {
                // Additive 新建的场景即活动场景，new GameObject 与 InstantiatePrefab 都会落在它里面。
                var canvasGo = new GameObject("Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
                canvasGo.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
                var scaler = canvasGo.GetComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1280, 720);
                scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
                scaler.matchWidthOrHeight = .5f;

                var frame = NewRect(FrameName, canvasGo.transform, Vector2.zero, new Vector2(1280, 720));
                Fill(frame, "背景占位 · 游戏画面", new Color(.10f, .18f, .15f, 1f));
                var header = NewRect("资源顶栏（运行时生成 · 占位）", frame, new Vector2(0, 304), new Vector2(1280, 112));
                Fill(header, "顶栏底色", new Color(.9686f, .9647f, .9255f, .96f));
                Notice(header, "资源顶栏 · 运行时由 BaseTopBar 生成，此处仅占位", 18);
                var shade = NewRect("模态遮罩", frame, Vector2.zero, new Vector2(1280, 720));
                Fill(shade, "遮罩", new Color(.02f, .06f, .07f, .62f));

                var panelHost = NewRect(PanelHost, frame, Vector2.zero, new Vector2(1220, 680));
                var panelInstance = (GameObject)PrefabUtility.InstantiatePrefab(panel, scene);
                panelInstance.transform.SetParent(panelHost, false);
                ResetRoot(panelInstance);
                // 面板宽度是运行时按人数算的；预览里统一撑到 1220 与 3 张卡片对齐。
                // 这属于预览用的根节点位姿，Apply 时会还原，不会写回 prefab 资产。
                var panelRect = panelInstance.GetComponent<RectTransform>();
                if (panelRect != null) panelRect.sizeDelta = new Vector2(1220, 680);

                // 人物卡预览叠在面板之上：坐标与运行时 PartyViewport 一致（viewport 中心 y=-65）。
                var cardHost = NewRect(CardHost, frame, new Vector2(0, -65), new Vector2(1220, 540));
                for (int n = 0; n < CardCount; n++)
                {
                    var c = (GameObject)PrefabUtility.InstantiatePrefab(card, scene);
                    c.transform.SetParent(cardHost, false);
                    ResetRoot(c);
                    var r = c.GetComponent<RectTransform>();
                    if (r != null) r.anchoredPosition = new Vector2((n - (CardCount - 1) * .5f) * CardStep, 0);
                }

                var tip = NewRect("编辑说明", frame, new Vector2(0, -332), new Vector2(1200, 36));
                Notice(tip, "拖节点调版式 → Tools/UI/把编辑场景改动应用回 Prefab → 运行时直接生效", 16);

                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene, ScenePath);
            }
            finally
            {
                if (scene.IsValid() && scene.isLoaded) EditorSceneManager.CloseScene(scene, true);
            }
            AssetDatabase.Refresh();
            EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath));
            bool dirty = false;
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).isDirty) dirty = true;
            if (dirty)
                Debug.LogWarning("[UiEdit] 已生成 " + ScenePath + "；当前有场景存在未保存修改，未自动切换，请双击打开。");
            else
            {
                EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
                Debug.Log("[UiEdit] 已生成并打开 " + ScenePath + "：Hierarchy 里就是完整背包，Game 视图看效果。");
            }
        }

        [MenuItem("Tools/UI/把编辑场景改动应用回 Prefab")]
        public static void Apply()
        {
            var scene = SceneManager.GetActiveScene();
            if (scene.path != ScenePath) { Debug.LogError("[UiEdit] 请先打开 " + ScenePath + " 再应用改动"); return; }
            Transform panelHost = null, cardHost = null;
            foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsSortMode.None))
            {
                if (t.name == PanelHost) panelHost = t;
                else if (t.name == CardHost) cardHost = t;
            }
            if (panelHost == null || cardHost == null) { Debug.LogError("[UiEdit] 场景里找不到 PanelInstances / CardInstances 容器"); return; }

            int applied = 0;
            // 三张人物卡是同一份 prefab 的三个实例：只应用第一个，避免后面的覆盖前面的版式。
            var appliedSources = new System.Collections.Generic.HashSet<Object>();
            foreach (var host in new[] { panelHost, cardHost })
                foreach (Transform child in host)
                {
                    var instance = child.gameObject;
                    if (!PrefabUtility.IsPartOfPrefabInstance(instance)) continue;
                    var source = PrefabUtility.GetCorrespondingObjectFromSource(instance) as GameObject;
                    if (source == null) continue;

                    var r = instance.GetComponent<RectTransform>();
                    Vector2 previewPos = r != null ? r.anchoredPosition : Vector2.zero;
                    Vector2 previewSize = r != null ? r.sizeDelta : Vector2.zero;
                    if (appliedSources.Add(source))
                    {
                        RestoreNonPresentation(instance, source, r);
                        PrefabUtility.ApplyPrefabInstance(instance, InteractionMode.AutomatedAction);
                        applied++;
                    }
                    else
                    {
                        // 同一份 prefab 的其余预览副本：以第一个为准，清掉误改的 override 保持三张一致。
                        PrefabUtility.RevertPrefabInstance(instance, InteractionMode.AutomatedAction);
                    }
                    // Apply/Revert 后把预览坐标写回去（只留在场景里，不进 prefab 资产）。
                    if (r != null) { r.anchoredPosition = previewPos; r.sizeDelta = previewSize; }
                }
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Debug.Log("[UiEdit] 已把 " + applied + " 份 prefab 的改动应用回资产；其余预览副本已同步。根节点预览坐标与 Text.text 未写回。");
        }

        private static GameObject Load(string key)
            => AssetDatabase.LoadAssetAtPath<GameObject>(Path.Combine(Folder, key + ".prefab").Replace('\\', '/'));

        // 只还原「不属于表现」的两类东西：预览用的根节点位姿、以及所有运行时文案。
        private static void RestoreNonPresentation(GameObject instance, GameObject source, RectTransform r)
        {
            var sr = source.GetComponent<RectTransform>();
            if (r != null && sr != null)
            {
                r.anchorMin = sr.anchorMin; r.anchorMax = sr.anchorMax; r.pivot = sr.pivot;
                r.anchoredPosition = sr.anchoredPosition; r.sizeDelta = sr.sizeDelta; r.localScale = sr.localScale;
            }
            var instTexts = instance.GetComponentsInChildren<Text>(true);
            var srcTexts = source.GetComponentsInChildren<Text>(true);
            // 只有层级完全一致时才能按索引还原文案；用户在预览里增删过 Text 节点就跳过，避免错位串字。
            if (instTexts.Length == srcTexts.Length)
                for (int i = 0; i < instTexts.Length; i++) instTexts[i].text = srcTexts[i].text;
        }

        private static void ResetRoot(GameObject go)
        {
            var r = go.GetComponent<RectTransform>();
            if (r == null) return;
            r.anchorMin = r.anchorMax = r.pivot = new Vector2(.5f, .5f);
            r.anchoredPosition = Vector2.zero;
            r.localScale = Vector3.one;
        }

        private static RectTransform NewRect(string name, Transform parent, Vector2 pos, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var r = go.GetComponent<RectTransform>();
            r.SetParent(parent, false);
            r.anchorMin = r.anchorMax = r.pivot = new Vector2(.5f, .5f);
            r.anchoredPosition = pos; r.sizeDelta = size;
            return r;
        }

        private static void Fill(RectTransform parent, string name, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            var r = go.GetComponent<RectTransform>();
            r.SetParent(parent, false);
            r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one;
            r.offsetMin = r.offsetMax = Vector2.zero;
            var image = go.GetComponent<Image>();
            image.color = color; image.raycastTarget = false;
        }

        private static void Notice(RectTransform parent, string text, int size)
        {
            var go = new GameObject("说明文字", typeof(RectTransform), typeof(Text));
            var r = go.GetComponent<RectTransform>();
            r.SetParent(parent, false);
            r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one;
            r.offsetMin = r.offsetMax = Vector2.zero;
            var label = go.GetComponent<Text>();
            label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            label.text = text; label.fontSize = size; label.alignment = TextAnchor.MiddleCenter;
            label.color = new Color(.15f, .23f, .20f, 1f); label.raycastTarget = false;
        }
    }
}

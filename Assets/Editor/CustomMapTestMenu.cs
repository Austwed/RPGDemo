using System;
using BaseConquest.Base;
using UnityEditor;

namespace BaseConquest.Editor
{
    public static class CustomMapTestMenu
    {
        private const string MenuPath = "SLG/地图编辑/自定义地图测试新档";
        private const string ImportMenu = "SLG/地图编辑/从 JSON 打开地图测试";

        [MenuItem(ImportMenu)]
        private static void OpenFile()
        {
            string path = EditorUtility.OpenFilePanel("打开地图编辑器导出的 JSON", "", "json");
            if (string.IsNullOrEmpty(path)) return;
            try { Start(path); }
            catch (Exception error) { EditorUtility.DisplayDialog("无法打开地图", error.Message, "关闭"); }
        }

        [MenuItem(MenuPath)]
        private static void Open()
        {
            try { Start(); }
            catch (Exception error) { EditorUtility.DisplayDialog("无法开始自定义地图测试", error.Message, "确定"); }
        }

        [MenuItem(MenuPath, true)]
        [MenuItem(ImportMenu, true)]
        private static bool CanStart() => !EditorApplication.isPlayingOrWillChangePlaymode
            && !EditorApplication.isCompiling && !EditorApplication.isUpdating;

        public static void Start(string path = null)
        {
            if (!CanStart()) throw new InvalidOperationException("请退出 Play 并等 Unity 完成导入，再开始地图测试。");
            var package = BaseSceneController.ReadCustomMapTestPackage(path);
            DemoStartup.Apply();
            SessionState.SetString(DemoLauncher.CustomMapTestFileKey, path ?? "");
            SessionState.SetString(DemoLauncher.CustomMapTestLaunchKey, package.Id);
            try { EditorApplication.EnterPlaymode(); }
            catch
            {
                SessionState.EraseString(DemoLauncher.CustomMapTestLaunchKey);
                SessionState.EraseString(DemoLauncher.CustomMapTestFileKey);
                throw;
            }
        }

        [InitializeOnLoadMethod]
        private static void ClearCanceledLaunchOnReturn()
        {
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.EnteredEditMode)
                {
                    SessionState.EraseString(DemoLauncher.CustomMapTestLaunchKey);
                    SessionState.EraseString(DemoLauncher.CustomMapTestFileKey);
                }
            };
        }
    }
}

using System;
using BaseConquest.Base;
using UnityEditor;

namespace BaseConquest.Editor
{
    public static class CustomMapTestMenu
    {
        private const string MenuPath = "SLG/地图编辑/自定义地图测试新档";

        [MenuItem(MenuPath)]
        private static void Open()
        {
            try { Start(); }
            catch (Exception error) { EditorUtility.DisplayDialog("无法开始自定义地图测试", error.Message, "确定"); }
        }

        [MenuItem(MenuPath, true)]
        private static bool CanStart() => !EditorApplication.isPlayingOrWillChangePlaymode
            && !EditorApplication.isCompiling && !EditorApplication.isUpdating;

        public static void Start()
        {
            if (!CanStart()) throw new InvalidOperationException("请退出 Play 并等 Unity 完成导入，再开始地图测试。");
            var package = BaseSceneController.ReadCustomMapTestPackage();
            DemoStartup.Apply();
            SessionState.SetString(DemoLauncher.CustomMapTestLaunchKey, package.Id);
            try { EditorApplication.EnterPlaymode(); }
            catch
            {
                SessionState.EraseString(DemoLauncher.CustomMapTestLaunchKey);
                throw;
            }
        }

        [InitializeOnLoadMethod]
        private static void ClearCanceledLaunchOnReturn()
        {
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.EnteredEditMode)
                    SessionState.EraseString(DemoLauncher.CustomMapTestLaunchKey);
            };
        }
    }
}

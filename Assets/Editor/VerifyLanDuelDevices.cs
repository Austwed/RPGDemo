using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace BaseConquest.Editor
{
    public static class VerifyLanDuelDevices
    {
        public static void Run()
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("须从独立测试编辑器启动");
            string role = Environment.GetEnvironmentVariable("DEMO_DUEL_ROLE");
            if (role != "host" && role != "guest") throw new InvalidOperationException("指定 host 或 guest");
            string isolated = Path.GetFullPath(Path.Combine("Logs","lan-duel-saves",Guid.NewGuid().ToString("N")));
            Environment.SetEnvironmentVariable("BASE_CONQUEST_UI_CHECK_SAVES",isolated);
            EditorSceneManager.OpenScene(DemoStartup.Launcher);
            EditorApplication.EnterPlaymode();
        }
    }
}

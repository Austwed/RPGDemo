using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;

namespace BaseConquest.Editor
{
    [InitializeOnLoad] public static class DemoStartup
    {
        public const string Launcher="Assets/Scenes/Demo_Launcher.unity";
        static DemoStartup(){EditorApplication.delayCall+=ApplyWhenReady;}
        private static void ApplyWhenReady()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode)return;
            if(EditorApplication.isCompiling||EditorApplication.isUpdating){EditorApplication.delayCall+=ApplyWhenReady;return;}
            Apply();
        }
        public static void Apply()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("请停止Play后设置默认入口");
            var launcher=AssetDatabase.LoadAssetAtPath<SceneAsset>(Launcher);
            if(launcher==null)throw new InvalidOperationException("Demo启动场景不存在");
            // Play from any open scene enters Demo without opening or saving the user's scene.
            EditorSceneManager.playModeStartScene=launcher;
            var scenes=EditorBuildSettings.scenes;
            if(scenes.Length==0||scenes[0].path!=Launcher||!scenes[0].enabled)
                EditorBuildSettings.scenes=new[]{new EditorBuildSettingsScene(Launcher,true)}.Concat(scenes.Where(s=>s.path!=Launcher)).ToArray();
            // Unity 6 can keep an active profile list separate from the project's global scene list.
            var global=EditorBuildSettings.globalScenes;
            if(global.Length==0||global[0].path!=Launcher||!global[0].enabled)
                EditorBuildSettings.globalScenes=new[]{new EditorBuildSettingsScene(Launcher,true)}.Concat(global.Where(s=>s.path!=Launcher)).ToArray();
        }
    }
}

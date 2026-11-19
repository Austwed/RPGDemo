using UnityEngine;
using UnityEngine.SceneManagement;
namespace BaseConquest.Base
{
    [DefaultExecutionOrder(-900)] public sealed class DemoLauncher:MonoBehaviour
    {
        public static void PrepareSession()
        {
#if UNITY_EDITOR
            string test=UnityEditor.SessionState.GetString(CustomMapTestLaunchKey,"");
            if(test.Length>0)
            {
                UnityEditor.SessionState.EraseString(CustomMapTestLaunchKey);
                BaseBattleGateway.UseCustomMapTest(test);return;
            }
#endif
            BaseBattleGateway.UseDemo();
#if UNITY_EDITOR
            BaseBattleGateway.MainMenu=UnityEditor.SessionState.GetBool(PreviewTitleKey,false);
            UnityEditor.SessionState.EraseBool(PreviewTitleKey);
#else
            BaseBattleGateway.MainMenu=true;
#endif
        }
#if UNITY_EDITOR
        public const string PreviewTitleKey="BaseConquest.PreviewVillageTitle";
        public const string CustomMapTestLaunchKey="BaseConquest.CustomMapTest.PendingPackage";
        public const string CustomMapTestFileKey="BaseConquest.CustomMapTest.File";
#endif
        private void Awake()
        {
            Application.targetFrameRate=60;QualitySettings.vSyncCount=1;
            if(!Application.isEditor&&!Application.isMobilePlatform)
                Screen.SetResolution(1280,720,FullScreenMode.Windowed);
            PrepareSession();SceneLoadingCover.LoadExploration();
        }
    }
}

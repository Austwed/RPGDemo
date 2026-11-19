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
            BaseBattleGateway.UseDemo();BaseBattleGateway.MainMenu=false;
        }
#if UNITY_EDITOR
        public const string CustomMapTestLaunchKey="BaseConquest.CustomMapTest.PendingPackage";
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

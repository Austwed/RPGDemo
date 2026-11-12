using UnityEngine;
using UnityEngine.SceneManagement;
namespace BaseConquest.Base
{
    [DefaultExecutionOrder(-900)] public sealed class DemoLauncher:MonoBehaviour
    {
        public static void PrepareSession()
        {BaseBattleGateway.UseDemo();BaseBattleGateway.MainMenu=false;}
        private void Awake()
        {
            Application.targetFrameRate=60;QualitySettings.vSyncCount=1;
            if(!Application.isEditor&&!Application.isMobilePlatform)
                Screen.SetResolution(1280,720,FullScreenMode.Windowed);
            PrepareSession();SceneManager.LoadScene(BaseBattleGateway.ExplorationScene);
        }
    }
}

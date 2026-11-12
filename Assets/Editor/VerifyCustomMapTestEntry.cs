using System;
using System.IO;
using System.Linq;
using BaseConquest.Base;
using UnityEditor;
using UnityEngine;

namespace BaseConquest.Editor
{
    public static class VerifyCustomMapTestEntry
    {
        private static byte[] officialBytes;
        private static string officialPath,packageId,testRoot;
        private static double deadline;
        private static bool opened,returning;

        public static void Run()
        {
            try
            {
                testRoot=Environment.GetEnvironmentVariable("BASE_CONQUEST_UI_CHECK_SAVES");
                if(!Application.isBatchMode||string.IsNullOrEmpty(testRoot)||Directory.Exists(testRoot))
                    throw new InvalidOperationException("请为此回归设置一个尚不存在的 BASE_CONQUEST_UI_CHECK_SAVES 目录。");
                var package=BaseSceneController.ReadCustomMapTestPackage();
                packageId=package.Id;
                var rules=DemoBalanceRuntime.Load();
                string raw=File.ReadAllText(Path.Combine(Application.streamingAssetsPath,"BaseScenarios",BaseDemo.BalanceTemplate));
                var source=JsonUtility.FromJson<BaseScenarioDocument>(raw);
                BaseBattleGateway.UseDemo();
                var catalog=BaseBattleGateway.Catalog(false);
                var official=BaseDemo.CreateOpening(source,raw,true,true,true,rules.Rules,rules.WorkbookSha256).CreateState();
                catalog.SaveSlot("slot1",official,"正式对照档");
                officialPath=catalog.Store("slot1").Path;
                officialBytes=File.ReadAllBytes(officialPath);
                var custom=BaseDemo.CreateOpening(source,raw,true,true,true,rules.Rules,rules.WorkbookSha256,package).CreateState();
                if(!BaseDemoIntro.Choose(custom,"male"))throw new InvalidOperationException("不能准备已有地图测试档");
                BaseBattleGateway.UseCustomMapTest(packageId);
                BaseBattleGateway.Catalog(false).SaveSlot("slot1",custom,"已有地图测试档");
                deadline=EditorApplication.timeSinceStartup+65;
                EditorApplication.update+=Tick;
                CustomMapTestMenu.Start();
            }
            catch(Exception error){Finish(error);}
        }

        private static void Tick()
        {
            try
            {
                if(EditorApplication.timeSinceStartup>deadline)throw new TimeoutException("自定义地图测试入口未完成："+(returning?"回正式标题":"打开测试新档"));
                if(!EditorApplication.isPlaying)return;
                var scene=UnityEngine.Object.FindAnyObjectByType<BaseSceneController>();
                if(scene?.Hud==null)return;
                if(returning)
                {
                    if(!BaseBattleGateway.MainMenu||BaseBattleGateway.CustomMapTestMode)return;
                    if(!File.ReadAllBytes(officialPath).SequenceEqual(officialBytes)||
                        BaseBattleGateway.Catalog(false).LoadSlot("slot1").Demo.StoryScene?.Valid==true)
                        throw new InvalidOperationException("正式存档被地图测试改写");
                    Finish(null);
                }
                else if(!opened&&scene.State!=null)
                {
                    opened=true;
                    if(!BaseBattleGateway.DemoReleaseMode||!BaseBattleGateway.CustomMapTestMode||
                        scene.State.Demo.StoryScene?.Id!=packageId||scene.State.Demo.Intro.Step!=0)
                        throw new InvalidOperationException("测试入口没有用指定地图创建新档");
                    var catalog=BaseBattleGateway.Catalog(false);
                    if(Path.GetFullPath(BaseBattleGateway.SaveDirectory)!=Path.GetFullPath(Path.Combine(testRoot,"custom-map-test",packageId))||
                        catalog.LoadSlot("slot1").Demo.Intro.Step!=1||
                        !catalog.Entries().Any(e=>BaseSaveCatalog.AutoSlots.Contains(e.Id)&&e.Error==null))
                        throw new InvalidOperationException("地图测试自动档未隔离，或覆盖了已有测试手动档");
                    returning=true;
                    scene.ToMainMenu();
                }
            }
            catch(Exception error){Finish(error);}
        }

        private static void Finish(Exception error)
        {
            EditorApplication.update-=Tick;
            if(error==null)Debug.Log("[VerifyCustomMapTestEntry] PASS: imported Play session, fresh selection, separate autosave, unchanged official slot and title");
            else Debug.LogException(error);
            EditorApplication.Exit(error==null?0:2);
        }
    }
}

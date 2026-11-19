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
        private static string officialPath,packageId,testRoot,capturePath;
        private static BaseMapPlacement expectedProp;
        private static double deadline;
        private static bool opened,returning;

        public static void Run()
        {
            try
            {
                testRoot=Environment.GetEnvironmentVariable("BASE_CONQUEST_UI_CHECK_SAVES");
                if(!Application.isBatchMode||string.IsNullOrEmpty(testRoot)||Directory.Exists(testRoot))
                    throw new InvalidOperationException("请为此回归设置一个尚不存在的 BASE_CONQUEST_UI_CHECK_SAVES 目录。");
                var args=Environment.GetCommandLineArgs();
                int fileIndex=Array.IndexOf(args,"-storyTestPackage"), propIndex=Array.IndexOf(args,"-storyTestObject");
                string path=fileIndex<0?null:args[fileIndex+1];
                int captureIndex=Array.IndexOf(args,"-storyTestCapture");
                capturePath=captureIndex<0?null:Path.GetFullPath(args[captureIndex+1]);
                var package=BaseSceneController.ReadCustomMapTestPackage(path);
                expectedProp=propIndex<0?null:package.Map("village").Layout.Objects.Single(o=>o.Id==args[propIndex+1]);
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
                if(!BaseDemoIntro.Choose(custom,"male","地图测试"))throw new InvalidOperationException("不能准备已有地图测试档");
                BaseBattleGateway.UseCustomMapTest(packageId);
                BaseBattleGateway.Catalog(false).SaveSlot("slot1",custom,"已有地图测试档");
                deadline=EditorApplication.timeSinceStartup+65;
                EditorApplication.update+=Tick;
                CustomMapTestMenu.Start(path);
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
                    if(capturePath!=null&&UnityEngine.Object.FindAnyObjectByType<SceneLoadingCover>()!=null)return;
                    opened=true;
                    if(!BaseBattleGateway.DemoReleaseMode||!BaseBattleGateway.CustomMapTestMode||
                        scene.State.Demo.StoryScene?.Id!=packageId||scene.State.Demo.Intro.Step!=0)
                        throw new InvalidOperationException("测试入口没有用指定地图创建新档");
                    if(expectedProp!=null)
                    {
                        var prop=BaseSpatial.Layout(scene.State).Objects.Single(o=>o.Id==expectedProp.Id);
                        var view=scene.GetComponentsInChildren<BaseConquest.Presentation.AuthoredMapView>(true)
                            .Single(v=>v.name==prop.Id+" · "+prop.Name);
                        var point=scene.WorldPoint(expectedProp.X,expectedProp.Y);
                        if(prop.X!=expectedProp.X||prop.Y!=expectedProp.Y||
                            Math.Abs(point.x-view.transform.position.x)>.001||Math.Abs(point.z-view.transform.position.z)>.001||
                            view.GetComponentInChildren<Renderer>()==null)
                            throw new InvalidOperationException("导出的摆件与游戏场景实物位置不一致："+prop.Name);
                        Debug.Log($"[VerifyCustomMapTestEntry] {packageId} {prop.Name} ({prop.X}, {prop.Y}) rendered");
                    }
                    if(capturePath!=null)
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(capturePath));
                        UiSkinScreenshot.Capture(Path.ChangeExtension(capturePath,null));
                    }
                    var catalog=BaseBattleGateway.Catalog(false);
                    if(Path.GetFullPath(BaseBattleGateway.SaveDirectory)!=Path.GetFullPath(Path.Combine(testRoot,"custom-map-test",packageId))||
                        catalog.LoadSlot("slot1").Demo.Intro.PlayerName!="地图测试"||
                        catalog.Entries().Any(e=>BaseSaveCatalog.AutoSlots.Contains(e.Id)&&e.CanLoad))
                        throw new InvalidOperationException("开局选角前的测试存档不正确");
                    scene.SelectDemoHero("female","布景演示");
                    var automatic=catalog.Entries().SingleOrDefault(e=>BaseSaveCatalog.AutoSlots.Contains(e.Id)&&e.CanLoad);
                    if(automatic?.PlayerName!="布景演示"||catalog.LoadSlot(automatic.Id).Demo.StoryScene?.Id!=packageId||
                        catalog.LoadSlot("slot1").Demo.Intro.PlayerName!="地图测试")
                        throw new InvalidOperationException("取名后未建立独立的地图测试自动档，或覆盖了已有手动档");
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

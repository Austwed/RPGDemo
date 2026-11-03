using System;
using System.Collections.Generic;
using System.Linq;
using BaseConquest.Base;
using BaseConquest.Battle;
using UnityEditor;
using UnityEngine;

namespace BaseConquest.Editor
{
    public static class VerifyStoryScenePackage
    {
        private static void Need(bool value,string message){if(!value)throw new InvalidOperationException(message);}
        private static void Reject(Action action,string message){try{action();}catch(ArgumentException){return;}throw new InvalidOperationException(message);}
        private static BaseStoryScenePackage Fixture()
        {
            var regions=new[]{("village","base"),("camp","base"),("town","town"),("ruins","outpost"),("field","perimeter"),("bandit","outpost")};
            var required=new[]{
                ("village","village_house","facility"),("village","intro_farm","facility"),("village","demo_captain","npc"),("village","intro_villager","npc"),("village","prologue_report","interaction"),("village","prologue_trigger","interaction"),("village","demo_home_world","interaction"),("village","demo_broken_sword","interaction"),
                ("camp","home","facility"),("camp","farm","facility"),("camp","workshop","facility"),("camp","barracks","facility"),("camp","exit_east","interaction"),("camp","demo_defense_exit","interaction"),("camp","demo_march_exit","interaction"),
                ("town","town_quest","npc"),("town","town_recruit","facility"),("town","travel_base","interaction"),("town","demo_ruins_exit","interaction"),
                ("ruins","demo_ruins_return","interaction"),("ruins","demo_ruins_investigate","interaction"),("field","demo_defense_return","interaction"),("field","demo_defense_start","interaction"),("bandit","demo_camp_world","interaction"),("bandit","demo_camp_attack","interaction")};
            var maps=new List<BaseDemoDesignMap>();
            foreach(var region in regions)
            {
                int size=region.Item1=="field"?10:64;var objects=new List<BaseMapPlacement>();int n=0;
                foreach(var item in required.Where(item=>item.Item1==region.Item1))
                {
                    string source=region.Item1+"-"+item.Item2;
                    objects.Add(new BaseMapPlacement {Id=item.Item2,Name=item.Item2,SourceObjectId=source,DefinitionId="story_"+region.Item1+"_"+item.Item2,Kind=item.Item3,AssetId="",X=1+n++,Y=2,Width=.5,Height=.5,Blocks=item.Item3=="facility"});
                }
                maps.Add(new BaseDemoDesignMap {Role=region.Item1,Layout=new BaseMapLayout {Region=region.Item2,Title=region.Item1,SourceMapId="story-"+region.Item1,Columns=size,Rows=size,SpawnX=.5,SpawnY=.5,Objects=objects},Board=BattleBoard.CreateDefault(size,size)});
            }
            return new BaseStoryScenePackage {Format="rpg-story-scene-package",Version=1,Id=new string('a',64),Maps=maps};
        }
        public static void Run()
        {
            var package=Fixture();package.Validate();
            var village=package.Maps.Single(map=>map.Role=="village");var captain=village.Layout.Objects.Single(obj=>obj.Id=="demo_captain");
            village.Layout.Objects.Remove(captain);Reject(package.Validate,"缺失主线人物未拒绝");village.Layout.Objects.Add(captain);
            var town=package.Maps.Single(map=>map.Role=="town");var foreign=captain.Copy();foreign.SourceObjectId="foreign-captain";town.Layout.Objects.Add(foreign);Reject(package.Validate,"其他场景放置异性主角未拒绝");town.Layout.Objects.Remove(foreign);
            var shell=captain.Copy();shell.SourceObjectId="captain-copy";shell.Id="story-shell:captain-copy";shell.VisualOnly=true;village.Layout.Objects.Add(shell);package.Validate();
            var progress=new BaseDemoProgress {Version=1,StoryScene=package};var copy=progress.Copy();
            copy.StoryScene.Maps[0].Layout.Title="changed";Need(package.Maps[0].Layout.Title!="changed","状态副本共享场景布局");
            Need(copy.StoryScene.Maps[0].Board.Width==64&&copy.StoryScene.Maps[0].Board.Cells.Length==4096,"状态副本截断场景地图");
            var restored=JsonUtility.FromJson<BaseDemoProgress>(JsonUtility.ToJson(progress));restored.StoryScene.Validate();
            Need(restored.StoryScene.Maps.Single(map=>map.Role=="field").Board.Width==10,"序列化丢失独立尺寸");
            Debug.Log("VerifyStoryScenePackage PASS");
        }
        public static void Batch(){try{Run();EditorApplication.Exit(0);}catch(Exception error){Debug.LogException(error);EditorApplication.Exit(1);}}
    }
}

using System;
using System.IO;
using System.Linq;
using System.Reflection;
using BaseConquest.Base;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace BaseConquest.Editor
{
    public static class VerifyFullBagSwordDialogue
    {
        private const BindingFlags Members=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;

        public static void Run()
        {
            const string stagePath="Assets/Scenes/Base_Stage2.unity";
            var stage=SceneManager.GetSceneByPath(stagePath);
            bool opened=!stage.IsValid()||!stage.isLoaded;
            if(opened)stage=EditorSceneManager.OpenScene(stagePath,OpenSceneMode.Additive);
            var root=new GameObject("西桥头 · 满包拾取验证");
            var previousFacing=BaseInteraction.Facing;
            try
            {
                var state=JsonUtility.FromJson<BaseState>(File.ReadAllText("Logs/demo-route-checkpoints/home-arrival.json"));
                state.Campaign.CurrentMapId=BaseDemoFinale.HomeField;
                var field=BaseSceneInstances.Layout(state,BaseDemoFinale.HomeField);
                state.Campaign.Location=field.Region;
                var sword=field.Objects.Single(o=>o.Id=="demo_broken_sword");
                state.PlayerX=sword.X;state.PlayerY=sword.Y;
                BaseAdventure.Arrive(state);
                BaseBackpack.Normalize(state);
                int room=BaseBackpack.Room(state,"forging_stone",state.PlayerLeaderId);
                if(room>0)BaseBackpack.Receive(state,"forging_stone",room,state.PlayerLeaderId);
                Need(BaseBackpack.Room(state,BaseDemoFinale.BrokenSwordItem,state.PlayerLeaderId)==0,"背包未填满");

                var source=stage.GetRootGameObjects().SelectMany(o=>o.GetComponentsInChildren<BaseSceneController>(true))
                    .Select(owner=>owner.transform.Find("UI 布局源/NpcDialogue")).Single(t=>t!=null);
                var library=new GameObject("UI 布局源").transform;
                library.SetParent(root.transform,false);
                UnityEngine.Object.Instantiate(source,library,false).name="NpcDialogue";
                library.gameObject.SetActive(false);
                var owner=root.AddComponent<BaseSceneController>();
                typeof(BaseSceneController).GetProperty("State",Members).SetValue(owner,state);
                typeof(BaseSceneController).GetProperty("Companions",Members).SetValue(owner,new BaseCompanionMovement());
                typeof(BaseSceneController).GetMethod("BuildWorld",Members).Invoke(owner,null);
                var hud=new BaseHud(owner,root.transform,Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"));
                typeof(BaseSceneController).GetProperty("Hud",Members).SetValue(owner,hud);
                var events=new GameObject("UiPointer",typeof(EventSystem)).GetComponent<EventSystem>();
                events.transform.SetParent(root.transform,false);
                EventSystem.current=events;
                hud.OpenObject(sword.Id);
                Need(hud.DialogueOpen,"触碰断剑后没有打开剧情界面");
                Click(FindButton(root,"跳过对话"),events);
                var take=FindButton(root,"这是搭档的剑……收起来吧。");
                var leave=FindButton(root,"先去村庄看看吧，或许呢？");
                var initialMessage=owner.Message;
                var before=JsonUtility.ToJson(state.Backpack);
                Transform notice=null;
                for(int i=0;i<4;i++)
                {
                    Click(take,events);
                    var found=root.GetComponentsInChildren<RectTransform>().Where(t=>t.name=="BagFullNotice").ToArray();
                    Need(found.Length==1&&(notice==null||notice==found[0]),"软提示叠加或丢失");
                    notice=found[0];
                    Need(notice.GetComponentInChildren<Text>().text=="背包已满"&&
                        notice.GetComponentsInChildren<Graphic>().All(g=>!g.raycastTarget),"提示文案或点击穿透错误");
                    Need(hud.DialogueOpen&&take==FindButton(root,"这是搭档的剑……收起来吧。")&&leave.isActiveAndEnabled,"点击后重建或关闭了选择");
                    Need(owner.State==state&&owner.Message==initialMessage&&JsonUtility.ToJson(state.Backpack)==before&&
                        field.Objects.Contains(sword)&&!state.Demo.Evidence.Contains(BaseDemoFinale.Sword),"满包点击改变了进度、物品或普通提示");
                    Need(root.GetComponentsInChildren<Text>().Count(t=>t.text=="背包已满")==1,"出现了重复提示文字");
                }
                Click(leave,events);
                Need(!hud.DialogueOpen&&!hud.PanelOpen&&field.Objects.Contains(sword),"第二项没有退出或误领了断剑");
                Debug.Log("[VerifyFullBagSwordDialogue] PASS four pickup clicks, same choices and sword, single nonblocking notice, exit by second reply");
            }
            finally
            {
                BaseInteraction.Facing=previousFacing;
                UnityEngine.Object.DestroyImmediate(root);
                if(opened)EditorSceneManager.CloseScene(stage,true);
            }
        }

        private static Button FindButton(GameObject root,string caption)=>root.GetComponentsInChildren<Button>()
            .Single(b=>b.IsInteractable()&&b.GetComponentInChildren<Text>()?.text==caption);
        private static void Click(Button button,EventSystem events)
        {
            Canvas.ForceUpdateCanvases();
            var rect=(RectTransform)button.transform;
            var center=RectTransformUtility.WorldToScreenPoint(null,rect.TransformPoint(rect.rect.center));
            var pointer=new PointerEventData(events){button=PointerEventData.InputButton.Left,pointerId=2,position=center};
            Need(rect.rect.width>0&&rect.rect.height>0&&button.targetGraphic.Raycast(center,null),"按钮无法接收点击");
            ExecuteEvents.Execute(button.gameObject,pointer,ExecuteEvents.pointerDownHandler);
            ExecuteEvents.Execute(button.gameObject,pointer,ExecuteEvents.pointerUpHandler);
            ExecuteEvents.Execute(button.gameObject,pointer,ExecuteEvents.pointerClickHandler);
        }
        private static void Need(bool condition,string reason)
        {if(!condition)throw new InvalidOperationException(reason);}
    }
}

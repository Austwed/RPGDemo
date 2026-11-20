using System;
using System.IO;
using System.Linq;
using System.Reflection;
using BaseConquest.Base;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BaseConquest.Editor
{
    public static class VerifyVillageLayoutInput
    {
        private const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        public static void Run()
        {
            // The player-route verifier produces this visit through the normal travel and combat flow.
            var state=JsonUtility.FromJson<BaseState>(File.ReadAllText("Logs/demo-route-checkpoints/home-arrival.json"));
            var map=BaseSceneInstances.Layout(state,BaseDemoFinale.Home);
            state.Demo.Stage=BaseDemoStage.Finished;
            state.Demo.Finale.RebuildMask=7;
            state.Demo.Finale.VolunteersArrived=state.Demo.Finale.VillageRebuilt=true;
            state.Inventory.Materials=100;
            state.Inventory.Coins=100;
            var root=new GameObject("Village layout input fixture");
            try
            {
                var owner=root.AddComponent<BaseSceneController>();
                void Set(string property,object value)=>typeof(BaseSceneController).GetProperty(property,Members).SetValue(owner,value);
                object Call(string method, params object[] args)=>typeof(BaseSceneController).GetMethod(method,Members).Invoke(owner,args);
                Set("State",state);
                Set("Companions",new BaseCompanionMovement());
                Call("BuildWorld");
                var hud=new BaseHud(owner,root.transform,Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"));
                Set("Hud",hud);
                Call("StartSettlementPlacement",true);
                Canvas.ForceUpdateCanvases();
                var toolbar=root.GetComponentsInChildren<RectTransform>().Single(t=>t.name=="CampConstructionPalette");
                var camera=owner.WorldCamera;
                Need(toolbar.rect.height<=60 && toolbar.GetComponent<Image>().color.a<1,"建设工具栏尺寸或透明度");
                var names=toolbar.GetComponentsInChildren<Button>().Select(b=>b.name).ToArray();
                Need(new[]{"Project-farm","Project-workshop","Project-barracks","ConfirmLayout","ExitLayout"}.All(names.Contains),"村内工具项");
                var button=toolbar.Find("Project-barracks").gameObject;
                var confirm=toolbar.Find("ConfirmLayout").GetComponent<Button>();
                var es=new GameObject("Touch event system",typeof(EventSystem)).GetComponent<EventSystem>();
                es.transform.SetParent(root.transform,false);
                EventSystem.current=es;
                var p=new PointerEventData(es){pointerId=3,button=PointerEventData.InputButton.Left};
                var bounds=(RectTransform)button.transform;
                Vector2 origin=RectTransformUtility.WorldToScreenPoint(null,bounds.TransformPoint(bounds.rect.center));
                p.position=origin;
                // Headless EditMode does not assign UI draw depths. Check the button's real
                // geometry and Graphic filters before dispatching pointer input to its handlers.
                var graphic=button.GetComponent<Image>();
                Need(graphic.raycastTarget&&graphic.Raycast(origin,null)&&RectTransformUtility.RectangleContainsScreenPoint(bounds,origin),
                    "兵营按钮的实际位置或射线过滤不可交互");
                var draft=typeof(BaseSceneController).GetProperty("CampLayoutDraft",Members).GetValue(owner);
                var type=draft.GetType();
                BaseMapPlacement Placed()=> (BaseMapPlacement)type.GetMethod("Placed",Members).Invoke(draft,new object[]{"barracks"});
                void Drag(Vector2 destination)
                {
                    p.position=origin;
                    ExecuteEvents.Execute(button,p,ExecuteEvents.beginDragHandler);
                    p.position=destination;
                    ExecuteEvents.Execute(button,p,ExecuteEvents.dragHandler);
                    ExecuteEvents.Execute(button,p,ExecuteEvents.endDragHandler);
                }
                Vector2 Project(double x,double y)=>camera.WorldToScreenPoint(owner.WorldPoint(x,y));
                var farm=map.Objects.Single(o=>o.Id=="farm");
                var view=typeof(BaseSceneController).GetField("regionView",Members).GetValue(owner);
                view.GetType().GetMethod("Follow").Invoke(view,new object[]{owner.WorldPoint(farm.X,farm.Y)});
                Drag(Project(farm.X,farm.Y));
                Need(Placed()!=null&&!confirm.interactable,"临时重叠应可预览且不可提交");
                Need(map.Objects.All(o=>o.Id!="barracks")&&state.Inventory.Materials==100,"预览污染了场景或库存");
                bool legal=false;
                for(int y=2;y<map.Rows-1&&!legal;y++)
                    for(int x=2;x<map.Columns-1&&!legal;x++)
                    {
                        var screen=Project(x,y);
                        if(screen.x<20||screen.y<90||screen.x>Screen.width-20||screen.y>Screen.height-110)continue;
                        Drag(screen);
                        legal=confirm.interactable;
                    }
                Need(legal,"有效位置不能提交");
                var placed=Placed().Copy();
                var snapshot=state.Copy();
                type.GetMethod("Commit",Members).Invoke(draft,new object[]{snapshot});
                var delivered=BaseSceneInstances.Layout(snapshot,BaseDemoFinale.Home).Objects.Single(o=>o.Id=="barracks");
                Need(delivered.X==placed.X&&delivered.Y==placed.Y,"提交位置不一致");
                Need(snapshot.Inventory.Materials==100-BaseDemoDefense.RepairMaterialCost(snapshot)&&snapshot.Inventory.Coins==100-BaseDemoDefense.RepairCoinCost(snapshot),"兵营费用未一次扣除");
                Need(state.Inventory.Materials==100&&state.Inventory.Coins==100,"实时草案污染了原库存");
                Debug.Log("[VerifyVillageLayoutInput] PASS actual village HUD, touch hit, conflict, free drop and atomic settlement");
            }
            finally {UnityEngine.Object.DestroyImmediate(root);}
        }
        private static void Need(bool result,string detail)
        {if(!result)throw new InvalidOperationException(detail);}
    }
}

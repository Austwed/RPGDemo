using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using BaseConquest.Presentation;
using UnityEngine;
using UnityEngine.UI;

namespace BaseConquest.Base
{
    public sealed partial class BaseHud
    {
        internal void UpdateJourneyGuidance() => RefreshQuestCue();
        internal void OpenWorldEntries(List<BaseWorldEntry> entries)
        {
            BeginPanel("选择入口");
            foreach(var entry in entries)
            {
                var layout=BaseSceneInstances.Layout(owner.State,entry.MapId);
                var marker=layout.Objects.First(item=>item.SourceObjectId==entry.ObjectId);
                RowButton(layout.Title+" · "+marker.Name,0,720,()=>owner.EnterWorldScene(entry.Id));
                row+=54;
            }
            EndPanel();
        }
        internal RectTransform JourneySurface()
        {
            var header = (RectTransform)summary.transform.parent;
            var surface = new GameObject("WorldJourney", typeof(RectTransform)).GetComponent<RectTransform>();
            surface.SetParent(header.parent, false);
            surface.SetSiblingIndex(modal.GetSiblingIndex());
            surface.anchorMin = Vector2.zero;
            surface.anchorMax = Vector2.one;
            surface.offsetMin = Vector2.zero;
            surface.offsetMax = new Vector2(0, header.anchoredPosition.y + header.rect.yMin);
            // Keep header menus and their popovers above the map, as on any other scene.
            header.SetSiblingIndex(surface.GetSiblingIndex());
            return surface;
        }
    }
    public sealed class BaseWorldJourneyHud : MonoBehaviour
    {
        public static Sprite BackgroundSprite(string path)=>Resources.Load<Sprite>(string.IsNullOrEmpty(path)?"PixelDemo/World/world-background-v1":path);
        public static Sprite NodeSprite(BaseWorldNode node)
        {
            string name=node?.Id=="camp"?"world-camp":node?.Id=="home"?"world-home":node?.Id=="bandit"?"world-enemy-camp":null;
            return name==null?null:Resources.Load<Sprite>("PixelDemo/World/"+name+"-v1");
        }
        private BaseSceneController owner;
        private Font font;
        private RectTransform root,map,panel,actual,preview,roadsLayer;
        private Text questLine;
        private GuidanceArrow questMarker;
        private Button enterAction, marchAction, returnAction;
        private Text place, travelInfo, conditions, enterLabel;
        private string departureEntry;
        private RectTransform artwork;
        private string Origin => departureEntry == null ? owner.State.Campaign.WorldJourney.NodeId :
            owner.State.Campaign.WorldJourney.Entries.First(entry => entry.Id == departureEntry).NodeId;
        private BaseWorldPlan plan;
        private string selected,entryId;
        private (BaseState State,bool Marching,bool Busy,string Message) lastState;
        private bool lastCueVisible;
        private Coroutine animation;
        private Vector2 mapSize;
        private readonly DemoUiTheme theme=DemoUiTheme.Current;
        public int PreviewRevision {get;private set;}
        public void Initialize(BaseSceneController controller,Font textFont,string entry)
        {
            owner=controller;font=textFont;departureEntry=entry;
            root=(RectTransform)transform;
            map=Box("WorldMap",root,theme.Wood);
            map.offsetMin=new Vector2(0,94);
            map.gameObject.AddComponent<RectMask2D>();
            var background=BackgroundSprite(owner.State.Campaign.WorldJourney.BackgroundResource);
            if(background!=null)
            {
                artwork=Box("MapArt",map,Color.white);
                var image=artwork.GetComponent<Image>();image.sprite=background;image.raycastTarget=false;
                var fit=artwork.gameObject.AddComponent<AspectRatioFitter>();
                fit.aspectRatio=background.rect.width/background.rect.height;
                fit.aspectMode=AspectRatioFitter.AspectMode.EnvelopeParent;
            }
            panel=Box("JourneyPanel",root,theme.Wood);
            panel.anchorMax=new Vector2(1,0);panel.offsetMax=new Vector2(0,94);
            place=Text(panel,"",new Vector2(22,-7),new Vector2(330,34),22);
            place.fontStyle=FontStyle.Bold;
            travelInfo=Text(panel,"",new Vector2(22,-45),new Vector2(550,36),18);
            conditions=Text(panel,"",new Vector2(385,-9),new Vector2(440,38),17);
            var actions=Layer("JourneyActions",panel);
            actions.anchorMin=actions.anchorMax=new Vector2(1,.5f);actions.pivot=new Vector2(1,.5f);
            actions.sizeDelta=new Vector2(408,46);actions.anchoredPosition=new Vector2(-18,0);
            returnAction=Button(actions,"返回",owner.LeaveJourneyMap,Vector2.zero,new Vector2(105,46));
            enterAction=Button(actions,"进入",ChooseEntry,new Vector2(114,0),new Vector2(135,46));
            enterLabel=enterAction.GetComponentInChildren<Text>();
            marchAction=Button(actions,"整备出征",()=>{if(BaseWorldJourney.Marching(owner.State))owner.ResumeWorldMarch();else owner.PrepareWorldMarch(plan?.Path);},new Vector2(258,0),new Vector2(150,46));
            roadsLayer=Layer("Roads",map);
            foreach(var node in owner.State.Campaign.WorldJourney.Nodes)
            {
                var button=Button(map,NodeLabel(node),()=>Select(node.Id),Vector2.zero,new Vector2(132,48));button.name="Node_"+node.Id;button.GetComponent<RectTransform>().anchorMin=button.GetComponent<RectTransform>().anchorMax=Vector2.zero;
                var icon=NodeSprite(node);if(icon!=null){var r=new GameObject("NodeIcon",typeof(RectTransform),typeof(Image)).GetComponent<RectTransform>();r.SetParent(button.transform,false);r.anchorMin=r.anchorMax=new Vector2(0,.5f);r.anchoredPosition=new Vector2(18,0);r.sizeDelta=new Vector2(32,32);var image=r.GetComponent<Image>();image.sprite=icon;image.preserveAspect=true;image.raycastTarget=false;var label=button.GetComponentInChildren<Text>();label.rectTransform.offsetMin+=new Vector2(32,0);}
            }
            actual=Marker("ActualParty",map,"队伍",theme.Wood);preview=Marker("PlanParty",map,"计划",theme.Gold);preview.gameObject.SetActive(false);
            questLine=Text(panel,"",new Vector2(385,-45),new Vector2(400,36),17);
            questMarker=GuidanceArrow.Create(root,"JourneyDestinationArrow",32);
            selected=owner.State.Campaign.WorldJourney.Route.LastOrDefault()??Origin;Canvas.ForceUpdateCanvases();PositionMap();actual.anchoredPosition=ActualPoint();RefreshPanel();RefreshQuestCue(owner.State.Campaign.WorldJourney);
        }
        private string NodeLabel(BaseWorldNode node)
        {
            if(!BaseDemoFinale.Opened(owner.State))return node.Title;
            switch(node.Id)
            {
                case "bandit":return BaseScriptNames.Bandit;
                case "home":return BaseScriptNames.Village;
                default:return node.Title;
            }
        }
        private void Update()
        {
            if(owner==null||owner.State==null)return;
            bool visible=!owner.Hud.PanelOpen && !owner.Hud.ResourcePopoverOpen && !owner.State.Aftermath.GameOver;
            if(mapSize!=map.rect.size)PositionMap();
            actual.anchoredPosition=Vector2.MoveTowards(actual.anchoredPosition,ActualPoint(),Time.unscaledDeltaTime*700);
            var key=(owner.State,owner.WorldMarchRunning,owner.Busy,owner.Message);
            bool changed=key!=lastState;
            if(changed)
            {
                lastState=key;
                if(!BaseWorldJourney.Marching(owner.State)&&plan!=null&&plan.Path[0]!=Origin)
                {plan=null;selected=Origin;preview.gameObject.SetActive(false);}
                RefreshPanel();DrawRoads();
            }
            if(changed||visible!=lastCueVisible)
            {
                lastCueVisible=visible;
                if(visible)RefreshQuestCue(owner.State.Campaign.WorldJourney);else questMarker.Hide();
            }
        }
        private void RefreshQuestCue(BaseWorldJourneyState world)
        {
            var cue=BaseQuestLog.Cue(owner.State);
            var targetEntry=world.Entries.FirstOrDefault(entry=>entry.Id==cue.ObjectId);
            if(targetEntry==null && departureEntry!=null && BaseDemoFinale.Opened(owner.State))
                targetEntry=world.Entries.FirstOrDefault(entry=>entry.Id==(BaseDemoFinale.Later(owner.State)?"home_entry":"bandit_entry"));
            questLine.gameObject.SetActive(targetEntry!=null);
            if(targetEntry!=null)questLine.text="目的地提示 · "+owner.WorldNodeTitle(targetEntry.NodeId);
            if(targetEntry==null||owner.Busy||owner.WorldMarchRunning||selected==targetEntry.NodeId)
                questMarker.Hide();
            else questMarker.PointTo((RectTransform)map.Find("Node_"+targetEntry.NodeId));
        }
        private RectTransform Layer(string name,Transform parent)
        {
            var rect=new GameObject(name,typeof(RectTransform)).GetComponent<RectTransform>();rect.SetParent(parent,false);rect.anchorMin=Vector2.zero;rect.anchorMax=Vector2.one;rect.offsetMin=rect.offsetMax=Vector2.zero;return rect;
        }
        private RectTransform Box(string name,Transform parent,Color color)
        {var rect=Layer(name,parent);rect.gameObject.AddComponent<Image>().color=color;return rect;}
        private Text Text(Transform parent,string value,Vector2 position,Vector2 size,int fontSize=18)
        {
            var rect=new GameObject("Text",typeof(RectTransform)).GetComponent<RectTransform>();rect.SetParent(parent,false);rect.anchorMin=rect.anchorMax=new Vector2(0,1);rect.pivot=new Vector2(0,1);rect.anchoredPosition=position;rect.sizeDelta=size;
            var text=rect.gameObject.AddComponent<Text>();text.font=font;text.fontSize=fontSize;text.color=theme.Panel;text.text=value;text.raycastTarget=false;text.supportRichText=false;return text;
        }
        private Button Button(Transform parent,string label,Action action,Vector2 position,Vector2 size)
        {
            var rect=Box(label,parent,theme.Line);rect.anchorMin=rect.anchorMax=new Vector2(0,1);rect.pivot=new Vector2(0,1);rect.anchoredPosition=position;rect.sizeDelta=size;
            var button=rect.gameObject.AddComponent<Button>();button.targetGraphic=rect.GetComponent<Image>();button.onClick.AddListener(()=>{if(!owner.Busy)action();});var labelText=Text(rect,label,new Vector2(6,-4),size-new Vector2(12,8));labelText.alignment=TextAnchor.MiddleCenter;return button;
        }
        private RectTransform Marker(string name,Transform parent,string label,Color color)
        {
            var rect=Box(name,parent,color);rect.anchorMin=rect.anchorMax=Vector2.zero;rect.pivot=new Vector2(.5f,0);rect.sizeDelta=new Vector2(58,30);rect.GetComponent<Image>().raycastTarget=false;var text=Text(rect,label,new Vector2(3,-2),new Vector2(52,26),16);text.alignment=TextAnchor.MiddleCenter;if(name=="PlanParty")text.color=theme.Wood;return rect;
        }
        private Vector2 Point(string id)
        {var node=owner.State.Campaign.WorldJourney.Nodes.Single(item=>item.Id==id);return new Vector2(map.rect.width/2+(artwork??map).rect.width*((float)node.X-.5f),map.rect.height/2+(artwork??map).rect.height*((float)node.Y-.5f));}
        private Vector2 ActualPoint()
        {
            var world=owner.State.Campaign.WorldJourney;var point=Point(Origin);
            if(world.Route.Count>0){var road=BaseWorldJourney.Road(world,world.NodeId,world.Route[world.RoadIndex+1]);if(road.Days>0)point=Vector2.Lerp(point,Point(world.Route[world.RoadIndex+1]),(float)world.RoadDay/road.Days);}
            return point+new Vector2(0,38);
        }
        private void PositionMap()
        {
            mapSize=map.rect.size;foreach(var node in owner.State.Campaign.WorldJourney.Nodes){var rect=map.Find("Node_"+node.Id).GetComponent<RectTransform>();rect.pivot=Vector2.one*.5f;rect.anchoredPosition=Point(node.Id);}DrawRoads();
        }
        private void Line(Vector2 from,Vector2 to,Color color,float width)
        {
            var rect=Box("Road",roadsLayer,color);rect.anchorMin=rect.anchorMax=Vector2.zero;rect.pivot=Vector2.one*.5f;rect.anchoredPosition=(from+to)*.5f;rect.sizeDelta=new Vector2(Vector2.Distance(from,to),width);rect.localRotation=Quaternion.Euler(0,0,Mathf.Atan2(to.y-from.y,to.x-from.x)*Mathf.Rad2Deg);rect.GetComponent<Image>().raycastTarget=false;
        }
        private void DrawRoads()
        {
            Clear(roadsLayer);
            var world=owner.State.Campaign.WorldJourney;foreach(var road in world.Roads)Line(Point(road.From),Point(road.To),BaseWorldJourney.Passable(owner.State,road)?new Color(.58f,.55f,.38f):new Color(.53f,.29f,.25f),3);
            var route=BaseWorldJourney.Marching(owner.State)?world.Route:plan?.Path;
            if(route==null)return;
            for(int index=1;index<route.Count;index++){var from=Point(route[index-1]);var to=Point(route[index]);Line(from,to,theme.Gold,4);var arrow=Text(roadsLayer,"›",Vector2.zero,new Vector2(24,24),24).rectTransform;arrow.anchorMin=arrow.anchorMax=Vector2.zero;arrow.pivot=Vector2.one*.5f;arrow.anchoredPosition=Vector2.Lerp(from,to,.7f);arrow.localRotation=Quaternion.Euler(0,0,Mathf.Atan2(to.y-from.y,to.x-from.x)*Mathf.Rad2Deg);}
        }
        public void Select(string id)
        {
            if(owner.Busy||BaseWorldJourney.Marching(owner.State))return;
            var next=BaseWorldJourney.Plan(owner.State,plan?.Path,id,departureEntry);selected=id;entryId=null;
            if(next.Changed){plan=next;PreviewRevision++;if(animation!=null)StopCoroutine(animation);animation=StartCoroutine(Preview(next.Preview));DrawRoads();}
            RefreshPanel();
            RefreshQuestCue(owner.State.Campaign.WorldJourney);
        }
        private IEnumerator Preview(List<string> path)
        {
            preview.gameObject.SetActive(true);if(path.Count==0)yield break;preview.anchoredPosition=Point(path[0])+new Vector2(0,72);
            foreach(string id in path.Skip(1)){var from=preview.anchoredPosition;var to=Point(id)+new Vector2(0,72);for(float elapsed=0;elapsed<.28f;elapsed+=Time.unscaledDeltaTime){preview.anchoredPosition=Vector2.Lerp(from,to,elapsed/.28f);yield return null;}preview.anchoredPosition=to;}
            if(plan?.Path.Count==1)preview.gameObject.SetActive(false);
        }
        private void RefreshPanel()
        {
            var state=owner.State;
            var world=state.Campaign.WorldJourney;
            bool marching=BaseWorldJourney.Marching(state);
            if(marching)selected=world.Route.Last();
            var route=marching?world.Route:plan?.Path;
            bool atOrigin=selected==Origin;
            bool reachable=atOrigin||(route?.Count>1&&route.Last()==selected);
            int days=marching?world.Route.Skip(world.RoadIndex+1).Select((id,index)=>BaseWorldJourney.Road(world,world.Route[world.RoadIndex+index],id).Days).Sum()-world.RoadDay:plan?.Days??0;
            place.text=owner.WorldNodeTitle(selected)+(atOrigin?" · 当前位置":"");
            travelInfo.text=!reachable?(BaseDemoFinale.Opened(state)&&selected=="home"?"通往白桦村的道路被黑鸦寨封锁。":"道路尚未通行。"):
                marching?$"行军中 · 剩余 {days} 天":atOrigin?"选择地图上的目的地":string.Join(" → ",route.Select(owner.WorldNodeTitle))+$"   ·   {days} 天";
            var raid=state.Campaign.Events.FirstOrDefault(e=>e.Id==BaseDemoDefense.SecondEventId&&e.Status=="pending");
            conditions.text=raid!=null&&BaseRaidIntel.Known(state,raid)?$"第二波来袭还剩 {Math.Max(0,raid.AvailableDay-state.Day)} 天":"";
            bool rebuilding=BaseDemoFinale.Rebuilding(state);
            if(rebuilding)conditions.text="先完成白桦村重建";
            if(marching && !owner.WorldMarchRunning && (owner.Message.Contains("失败") || owner.Message.Contains("未保存")))conditions.text=owner.Message;
            var entries=world.Entries.Where(entry=>entry.NodeId==Origin&&world.Discovered.Contains(entry.Id)).ToArray();
            if(!entries.Any(entry=>entry.Id==entryId))entryId=entries.FirstOrDefault()?.Id;
            enterAction.gameObject.SetActive(atOrigin && departureEntry==null && !marching && entryId!=null);
            if(entryId!=null)enterLabel.text=entries.Length>1?"选择入口":"进入";
            marchAction.GetComponentInChildren<Text>().text=marching?(owner.WorldMarchRunning?"行军中":"继续行军"):"整备出征";
            marchAction.interactable=!owner.WorldMarchRunning&&!owner.Busy&&!rebuilding&&(marching||!atOrigin&&reachable);
            returnAction.interactable=!owner.Busy&&!owner.WorldMarchRunning&&(!marching||world.RoadDay==0&&entries.Length>0);
            foreach(var node in world.Nodes)
            {
                var button=map.Find("Node_"+node.Id).GetComponent<Button>();
                button.interactable=!marching&&!owner.Busy;
                button.image.color=node.Id==selected?theme.Wood:theme.Line;
            }
            StyleAction(enterAction,theme.Button);StyleAction(marchAction,theme.Gold);StyleAction(returnAction,theme.Button);
        }
        private void ChooseEntry()
        {
            var world=owner.State.Campaign.WorldJourney;
            var entries=world.Entries.Where(entry=>entry.NodeId==Origin&&world.Discovered.Contains(entry.Id)).ToList();
            if(entries.Count==1)owner.EnterWorldScene(entries[0].Id);
            else owner.Hud.OpenWorldEntries(entries);
        }
        private void StyleAction(Button button,Color ready)
        {
            button.image.color=button.interactable?ready:theme.Line;
            button.GetComponentInChildren<Text>().color=button.interactable?theme.Ink:theme.Button;
        }
        private static void Clear(Transform parent)
        {foreach(var child in parent.Cast<Transform>().ToArray()){child.gameObject.SetActive(false);if(Application.isPlaying)Destroy(child.gameObject);else DestroyImmediate(child.gameObject);}}
    }
}

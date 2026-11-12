using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using BaseConquest.Presentation;
using UnityEngine;
using UnityEngine.UI;

namespace BaseConquest.Base
{
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
        private Text questLine, questMarker;
        private Button enterAction, marchAction;
        private BaseWorldPlan plan;
        private string selected,entryId,lastState;
        private Coroutine animation;
        private Vector2 mapSize;
        private readonly DemoUiTheme theme=DemoUiTheme.Current;
        public int PreviewRevision {get;private set;}
        public void Initialize(BaseSceneController controller,Font textFont)
        {
            owner=controller;font=textFont;
            var canvas=gameObject.AddComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceOverlay;canvas.sortingOrder=5;
            gameObject.AddComponent<GraphicRaycaster>();gameObject.AddComponent<CanvasScaler>();
            root=gameObject.GetComponent<RectTransform>();
            map=Box("WorldMap",root,new Color(.19f,.23f,.16f));map.offsetMax=new Vector2(-290,0);
            string resource=owner.State.Campaign.WorldJourney.BackgroundResource;
            var background=BackgroundSprite(resource);
            if(background!=null)
            {
                var image=Box("MapArt",map,Color.white).GetComponent<Image>();
                image.sprite=background;image.preserveAspect=true;image.raycastTarget=false;
            }
            panel=Box("JourneyPanel",root,theme.Wood);panel.anchorMin=new Vector2(1,0);panel.offsetMin=new Vector2(-290,0);
            roadsLayer=Layer("Roads",map);
            foreach(var node in owner.State.Campaign.WorldJourney.Nodes)
            {
                var button=Button(map,NodeLabel(node),()=>Select(node.Id),Vector2.zero,new Vector2(132,48));button.name="Node_"+node.Id;button.GetComponent<RectTransform>().anchorMin=button.GetComponent<RectTransform>().anchorMax=Vector2.zero;
                var icon=NodeSprite(node);if(icon!=null){var r=new GameObject("NodeIcon",typeof(RectTransform),typeof(Image)).GetComponent<RectTransform>();r.SetParent(button.transform,false);r.anchorMin=r.anchorMax=new Vector2(0,.5f);r.anchoredPosition=new Vector2(18,0);r.sizeDelta=new Vector2(32,32);var image=r.GetComponent<Image>();image.sprite=icon;image.preserveAspect=true;image.raycastTarget=false;var label=button.GetComponentInChildren<Text>();label.rectTransform.offsetMin+=new Vector2(32,0);}
            }
            actual=Marker("ActualParty",map,"队伍",theme.Wood);preview=Marker("PlanParty",map,"计划",theme.Gold);preview.gameObject.SetActive(false);
            questLine=Text(map,"",new Vector2(22,-14),new Vector2(520,52),20);questLine.fontStyle=FontStyle.Bold;
            questMarker=Text(root,"▼",Vector2.zero,new Vector2(36,38),26);questMarker.alignment=TextAnchor.MiddleCenter;questMarker.color=theme.Gold;
            gameObject.AddComponent<GameFrameCanvas>().Apply();
            selected=owner.State.Campaign.WorldJourney.NodeId;Canvas.ForceUpdateCanvases();PositionMap();actual.anchoredPosition=ActualPoint();RefreshPanel();
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
            bool visible=BaseWorldJourney.OnWorld(owner.State)&&!owner.State.Aftermath.GameOver&&!BaseBattleLoot.ShouldPresent(owner.State)&&!owner.Hud.PanelOpen;
            map.gameObject.SetActive(visible);panel.gameObject.SetActive(visible);owner.Hud.SetVisible(!visible);
            if(!visible){questMarker.gameObject.SetActive(false);if(BaseBattleLoot.ShouldPresent(owner.State)&&!owner.Hud.PanelOpen)owner.Hud.OpenLoot();else if(owner.State.Aftermath.GameOver&&!owner.Hud.PanelOpen)owner.Hud.OpenMenu();return;}
            if(mapSize!=map.rect.size)PositionMap();
            actual.anchoredPosition=Vector2.MoveTowards(actual.anchoredPosition,ActualPoint(),Time.unscaledDeltaTime*700);
            var world=owner.State.Campaign.WorldJourney;string key=owner.State.Day+"/"+world.NodeId+"/"+world.RoadIndex+"/"+world.RoadDay+"/"+world.Route.Count+"/"+owner.WorldMarchRunning+"/"+owner.Busy;
            if(key!=lastState){lastState=key;if(!BaseWorldJourney.Marching(owner.State)&&owner.WorldMarchRunning==false&&plan?.Path.FirstOrDefault()!=world.NodeId){plan=null;selected=world.NodeId;preview.gameObject.SetActive(false);}RefreshPanel();DrawRoads();}
            RefreshQuestCue(world);
        }
        private void RefreshQuestCue(BaseWorldJourneyState world)
        {
            var cue=BaseQuestLog.Cue(owner.State);
            var targetEntry=world.Entries.FirstOrDefault(entry=>entry.Id==cue.ObjectId);
            questMarker.gameObject.SetActive(false);
            questLine.gameObject.SetActive(targetEntry!=null);
            if(targetEntry==null)return;
            if(questLine.text!=cue.Instruction)questLine.text=cue.Instruction;
            if(owner.Busy||owner.WorldMarchRunning)return;
            RectTransform target;
            if(BaseWorldJourney.Marching(owner.State))target=marchAction.IsInteractable()?(RectTransform)marchAction.transform:null;
            else if(selected==targetEntry.NodeId&&enterAction.IsInteractable())target=(RectTransform)enterAction.transform;
            else if(selected==targetEntry.NodeId&&marchAction.IsInteractable())target=(RectTransform)marchAction.transform;
            else target=map.Find("Node_"+targetEntry.NodeId) as RectTransform;
            if(target==null)return;
            var frame=(RectTransform)questMarker.rectTransform.parent;
            Vector3 point=frame.InverseTransformPoint(target.TransformPoint(new Vector3(target.rect.center.x,target.rect.yMax)));
            questMarker.rectTransform.anchoredPosition=new Vector2(point.x-frame.rect.xMin-18,point.y-frame.rect.yMax+32);
            questMarker.transform.SetAsLastSibling();questMarker.gameObject.SetActive(true);
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
            var button=rect.gameObject.AddComponent<Button>();button.onClick.AddListener(()=>{if(!owner.Busy)action();});var labelText=Text(rect,label,new Vector2(6,-4),size-new Vector2(12,8));labelText.alignment=TextAnchor.MiddleCenter;return button;
        }
        private RectTransform Marker(string name,Transform parent,string label,Color color)
        {
            var rect=Box(name,parent,color);rect.anchorMin=rect.anchorMax=Vector2.zero;rect.pivot=new Vector2(.5f,0);rect.sizeDelta=new Vector2(58,30);rect.GetComponent<Image>().raycastTarget=false;var text=Text(rect,label,new Vector2(3,-2),new Vector2(52,26),16);text.alignment=TextAnchor.MiddleCenter;if(name=="PlanParty")text.color=theme.Wood;return rect;
        }
        private Vector2 Point(string id)
        {var node=owner.State.Campaign.WorldJourney.Nodes.Single(item=>item.Id==id);return new Vector2(map.rect.width*(float)node.X,map.rect.height*(float)node.Y);}
        private Vector2 ActualPoint()
        {
            var world=owner.State.Campaign.WorldJourney;var point=Point(world.NodeId);
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
            var next=BaseWorldJourney.Plan(owner.State,plan?.Path,id);selected=id;entryId=null;
            if(next.Changed){plan=next;PreviewRevision++;if(animation!=null)StopCoroutine(animation);animation=StartCoroutine(Preview(next.Preview));DrawRoads();}
            RefreshPanel();
        }
        private IEnumerator Preview(List<string> path)
        {
            preview.gameObject.SetActive(true);if(path.Count==0)yield break;preview.anchoredPosition=Point(path[0])+new Vector2(0,72);
            foreach(string id in path.Skip(1)){var from=preview.anchoredPosition;var to=Point(id)+new Vector2(0,72);for(float elapsed=0;elapsed<.28f;elapsed+=Time.unscaledDeltaTime){preview.anchoredPosition=Vector2.Lerp(from,to,elapsed/.28f);yield return null;}preview.anchoredPosition=to;}
            if(plan?.Path.Count==1)preview.gameObject.SetActive(false);
        }
        private void RefreshPanel()
        {
            Clear(panel);
            var state=owner.State;var world=state.Campaign.WorldJourney;var node=world.Nodes.Single(item=>item.Id==selected);bool marching=BaseWorldJourney.Marching(state);var route=marching?world.Route:plan?.Path;float y=-24;
            var title=Text(panel,node.Title,new Vector2(18,y),new Vector2(196,64),22);
            float titleHeight=Math.Max(32,title.preferredHeight+8);
            title.rectTransform.sizeDelta=new Vector2(196,titleHeight);
            Button(panel,"⋯",()=>owner.Hud.OpenMenu(),new Vector2(224,-18),new Vector2(48,32));y-=titleHeight+8;
            Text(panel,"第"+state.Day+"天 · "+world.Nodes.Single(item=>item.Id==world.NodeId).Title,new Vector2(18,y),new Vector2(254,42),16);y-=44;
            var raidEvent=state.Campaign.Events.FirstOrDefault(e=>e.Id==BaseDemoDefense.SecondEventId&&e.Status=="pending");int raidArrival=raidEvent!=null&&BaseRaidIntel.Known(state,raidEvent)?raidEvent.AvailableDay:0;string raid=raidArrival>0?"距第二波来袭 "+Math.Max(0,raidArrival-state.Day)+" 天":state.Demo?.Defense?.SecondOutcome=="intercepted"?"途中已截住第二波":"";
            if(raid!=""){Text(panel,raid,new Vector2(18,y),new Vector2(254,36),16);y-=40;}
            int days=marching?world.Route.Skip(world.RoadIndex+1).Select((id,index)=>BaseWorldJourney.Road(world,world.Route[world.RoadIndex+index],id).Days).Sum()-world.RoadDay:plan?.Days??0;
            bool reachable=selected==(route?.LastOrDefault()??world.NodeId);
            Text(panel,!reachable?"暂不可达":marching?"剩余"+days+"天":route?.Count>1?"预计"+days+"天 · 第"+state.Day+" → "+(state.Day+days)+"天":"已在此地",new Vector2(18,y),new Vector2(254,36));y-=40;
            if(route?.Count>1){Text(panel,string.Join(" → ",route.Select(id=>world.Nodes.Single(item=>item.Id==id).Title)),new Vector2(18,y),new Vector2(254,88),16);y-=90;}
            if(!reachable)
            {
                string warning=BaseDemoFinale.Opened(state)&&node.Id=="home"&&state.Demo.Stage==BaseDemoStage.DefenseWon
                    ?"通往白桦村的道路被黑鸦寨封锁。"
                    :"道路尚未通行，请先探索或处理阻路敌营。";
                Text(panel,warning,new Vector2(18,y),new Vector2(254,56),16);y-=60;
            }
            var entries=world.Entries.Where(entry=>entry.NodeId==selected&&world.Discovered.Contains(entry.Id)).ToArray();if(!entries.Any(entry=>entry.Id==entryId))entryId=entries.FirstOrDefault()?.Id;
            var viewport=Box("EntriesViewport",panel,theme.Wood);
            viewport.offsetMin=new Vector2(18,84);viewport.offsetMax=new Vector2(-18,y);
            viewport.gameObject.AddComponent<RectMask2D>();
            var items=Layer("Entries",viewport);items.anchorMin=new Vector2(0,1);items.anchorMax=Vector2.one;items.pivot=new Vector2(0,1);
            var scenes=Text(items,"区域场景："+string.Join("、",node.MapIds.Select(id=>BaseSceneInstances.Layout(state,id).Title)),Vector2.zero,new Vector2(254,58),16);float sceneHeight=Math.Max(42,scenes.preferredHeight+8);scenes.rectTransform.sizeDelta=new Vector2(254,sceneHeight);Text(items,"已发现入口",new Vector2(0,-sceneHeight),new Vector2(254,30),16);
            items.sizeDelta=new Vector2(0,sceneHeight+34+entries.Length*52);var scroll=viewport.gameObject.AddComponent<ScrollRect>();scroll.viewport=viewport;scroll.content=items;scroll.horizontal=false;scroll.movementType=ScrollRect.MovementType.Clamped;
            for(int index=0;index<entries.Length;index++){var entry=entries[index];var layout=BaseSceneInstances.Layout(state,entry.MapId);var marker=layout.Objects.Single(obj=>obj.SourceObjectId==entry.ObjectId);var button=Button(items,(entry.Id==entryId?"● ":"")+layout.Title+" · "+marker.Name,()=>{entryId=entry.Id;RefreshPanel();},new Vector2(0,-sceneHeight-34-index*52),new Vector2(254,46));button.interactable=!owner.WorldMarchRunning;}
            var actions=Layer("JourneyActions",panel);
            actions.anchorMin=Vector2.zero;actions.anchorMax=new Vector2(1,0);
            actions.offsetMin=new Vector2(18,20);actions.offsetMax=new Vector2(-18,68);
            var enter=Button(actions,"进入",()=>owner.EnterWorldScene(entryId),new Vector2(136,0),new Vector2(118,48));enter.interactable=entryId!=null&&world.NodeId==selected&&!marching&&(route==null||route.Count<=1)&&!owner.Busy;
            var confirm=Button(actions,marching?"继续行军":"确认行军",()=>{if(animation!=null)StopCoroutine(animation);preview.gameObject.SetActive(false);owner.StartWorldMarch(plan?.Path);},Vector2.zero,new Vector2(128,48));confirm.interactable=!owner.WorldMarchRunning&&!owner.Busy&&(marching||route?.Count>1&&route.Last()==selected);
            enterAction=enter;marchAction=confirm;
            StyleAction(enter,theme.Button);StyleAction(confirm,theme.Gold);
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

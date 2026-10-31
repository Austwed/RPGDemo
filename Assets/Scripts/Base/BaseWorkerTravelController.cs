using System.Collections.Generic;
using System.Linq;
using BaseConquest.Battle;
using UnityEngine;
namespace BaseConquest.Base
{
    public sealed partial class BaseSceneController
    {
        private readonly BaseWorkerTravel workerTravel=new BaseWorkerTravel();
        private readonly Dictionary<BaseWorkerJourney,Transform> workerTravelViews=new Dictionary<BaseWorkerJourney,Transform>();
        private readonly Dictionary<string,Transform> militiaViews=new Dictionary<string,Transform>();
        private string workerTravelMap;
        public void EnqueueWorkerTransfer(BaseTroopKind kind,string from,string to,int count)
        {if(State!=null&&!BaseSceneCombat.Active(State))workerTravel.Enqueue(State,kind,from,to,count);}
        private void ClearWorkerTravel()
        {
            workerTravel.Clear();foreach(var view in workerTravelViews.Values)if(view!=null)BattleBoardView.DestroyObject(view.gameObject);workerTravelViews.Clear();
            foreach(var view in militiaViews.Values)if(view!=null)BattleBoardView.DestroyObject(view.gameObject);militiaViews.Clear();workerTravelMap=null;
        }
        private Transform TravelActor(string id,string art,BasePoint at)
        {
            var view=Person(player.parent,id,new Vector2((float)at.X,(float)at.Y),Color.white);view.GetComponent<BattlePawn2D>().EnableCompanion(art);return view;
        }
        private void TickWorkerTravel(double seconds)
        {
            string key=State.Campaign.Location+":"+State.Campaign.CurrentMapId+":"+BaseDemoPrologue.Village(State);
            if(workerTravelMap!=null&&workerTravelMap!=key)ClearWorkerTravel();workerTravelMap=key;
            workerTravel.Tick(State,seconds,EnvironmentPaused);
            foreach(var a in workerTravelViews.Keys.Where(a=>!workerTravel.Active.Contains(a)).ToArray()){if(workerTravelViews[a]!=null)BattleBoardView.DestroyObject(workerTravelViews[a].gameObject);workerTravelViews.Remove(a);}
            foreach(var a in workerTravel.Active){if(!workerTravelViews.TryGetValue(a,out var v)||v==null){v=TravelActor("WorkerTransfer",a.Kind==BaseTroopKind.Melee?"infantry":a.Kind==BaseTroopKind.Ranged?"archer":"cavalry",a.Position);workerTravelViews[a]=v;}v.GetComponent<BattlePawn2D>().SyncExploration(WorldPoint(a.Position.X,a.Position.Y),Color.white);}
            var intro=State.Demo?.Intro;
            foreach(var v in militiaViews.Values)if(v!=null)v.gameObject.SetActive(BaseDemoPrologue.Village(State)&&intro?.Step==7);
            if(!BaseDemoPrologue.Village(State)||intro?.Step!=7)return;
            foreach(var p in intro.Militia){if(!militiaViews.TryGetValue(p.Id,out var v)||v==null){v=TravelActor(p.Id,"infantry",new BasePoint(p.MapX,p.MapY));militiaViews[p.Id]=v;}v.gameObject.SetActive(true);v.GetComponent<BattlePawn2D>().SyncExploration(WorldPoint(p.MapX,p.MapY),Color.white);}
        }
    }
}

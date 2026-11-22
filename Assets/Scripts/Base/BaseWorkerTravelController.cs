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
            workerTravel.Tick(State,seconds,EnvironmentPaused || Hud.ProductionFacilityOpen);
            foreach(var a in workerTravelViews.Keys.Where(a=>!workerTravel.Active.Contains(a)).ToArray()){if(workerTravelViews[a]!=null)BattleBoardView.DestroyObject(workerTravelViews[a].gameObject);workerTravelViews.Remove(a);}
            foreach (var journey in workerTravel.Active)
            {
                if (!workerTravelViews.TryGetValue(journey, out var view) || view == null)
                {
                    view = TravelActor("WorkerTransfer", journey.AppearanceId, journey.Position);
                    workerTravelViews[journey] = view;
                }
                view.GetComponent<BattlePawn2D>().SyncExploration(WorldPoint(journey.Position.X, journey.Position.Y), Color.white);
            }
            var intro=State.Demo?.Intro;
            foreach(var v in militiaViews.Values)if(v!=null)v.gameObject.SetActive(BaseDemoPrologue.Village(State)&&intro?.Step==7);
            if(!BaseDemoPrologue.Village(State)||intro?.Step!=7)return;
            foreach(var p in intro.Militia){if(!militiaViews.TryGetValue(p.Id,out var v)||v==null){v=TravelActor(p.Id,"saber",new BasePoint(p.MapX,p.MapY));militiaViews[p.Id]=v;}v.gameObject.SetActive(true);v.GetComponent<BattlePawn2D>().SyncExploration(WorldPoint(p.MapX,p.MapY),Color.white);}
        }
    }
}

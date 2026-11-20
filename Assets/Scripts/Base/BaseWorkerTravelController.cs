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
        private BaseState civilianTravelState;
        private List<BasePoint> civilianWorkplaces = new List<BasePoint>();
        public void EnqueueWorkerTransfer(BaseTroopKind kind,string from,string to,int count)
        {if(State!=null&&!BaseSceneCombat.Active(State))workerTravel.Enqueue(State,kind,from,to,count);}
        private void ClearWorkerTravel()
        {
            workerTravel.Clear();foreach(var view in workerTravelViews.Values)if(view!=null)BattleBoardView.DestroyObject(view.gameObject);workerTravelViews.Clear();
            foreach(var view in militiaViews.Values)if(view!=null)BattleBoardView.DestroyObject(view.gameObject);militiaViews.Clear();workerTravelMap=null;
            civilianTravelState = null;
            civilianWorkplaces.Clear();
        }
        private Transform TravelActor(string id,string art,BasePoint at)
        {
            var view=Person(player.parent,id,new Vector2((float)at.X,(float)at.Y),Color.white);view.GetComponent<BattlePawn2D>().EnableCompanion(art);return view;
        }
        private void SyncCivilianWorkerTravel()
        {
            if ((State.Campaign.Location != "base" && !BaseSettlements.AtVillage(State)) || BaseBattleGateway.MainMenu || ReferenceEquals(civilianTravelState, State) ||
                workerTravel.CivilianInTransit)
                return;

            // Preserve the doors before building changes and play one journey per civilian.
            bool enteringCamp = civilianTravelState == null;
            civilianTravelState = State;
            var nextWorkplaces = new List<BasePoint>(2);
            foreach (string facility in new[] { "farm", "workshop" })
            {
                int count = BaseEconomy.CivilianWorkersAt(State, facility);
                if (count > 0) nextWorkplaces.AddRange(Enumerable.Repeat(BaseStageWalk.Door(State, facility), count));
            }
            if (enteringCamp)
            {
                // Restoring a map restores its existing posts. Only a new assignment starts a walk.
                civilianWorkplaces = nextWorkplaces;
                return;
            }
            var departures = new List<BasePoint>(civilianWorkplaces);
            var arrivals = new List<BasePoint>(2);
            foreach (var workplace in nextWorkplaces)
            {
                int retained = departures.FindIndex(old => old.Distance(workplace) < .01);
                if (retained >= 0) departures.RemoveAt(retained);
                else arrivals.Add(workplace);
            }
            civilianWorkplaces = nextWorkplaces;
            int journeys = System.Math.Max(departures.Count, arrivals.Count);
            if (journeys == 0) return;

            var camp = BaseStageWalk.Door(State, "home");
            for (int i = 0; i < journeys; i++)
            {
                bool leavingWork = i < departures.Count;
                bool startingWork = i < arrivals.Count;
                workerTravel.EnqueueCivilian(leavingWork ? departures[i] : camp, startingWork ? arrivals[i] : camp,
                    leavingWork && startingWork ? camp : (BasePoint?)null);
            }
        }
        private void TickWorkerTravel(double seconds)
        {
            string key=State.Campaign.Location+":"+State.Campaign.CurrentMapId+":"+BaseDemoPrologue.Village(State);
            if(workerTravelMap!=null&&workerTravelMap!=key)ClearWorkerTravel();workerTravelMap=key;
            SyncCivilianWorkerTravel();
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

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using BaseConquest.Battle;
using UnityEngine;
using UnityEngine.InputSystem;

namespace BaseConquest.Base
{
    public sealed partial class BaseSceneController
    {
        private string pendingWorldEntry;
        private List<string> pendingWorldRoute;
        private BaseWorldJourneyHud journeyHud;
        private bool attemptedSavedMarch;
        public bool WorldMarchRunning { get; private set; }
        internal bool JourneyMapOpen => journeyHud != null;
        internal int WorldRoadFood(int soldiers)=>BaseWorldJourney.TripFood(State,pendingWorldRoute,soldiers);
        internal string WorldMarchTitle => "行军整备 · " + WorldNodeTitle(pendingWorldRoute?.LastOrDefault());
        internal string WorldNodeTitle(string id)
        {
            if (BaseDemoFinale.Opened(State) && id == "bandit") return BaseScriptNames.Bandit;
            if (BaseDemoFinale.Opened(State) && id == "home") return BaseScriptNames.Village;
            return State.Campaign.WorldJourney.Nodes.FirstOrDefault(node => node.Id == id)?.Title ?? "世界道路";
        }
        private void OpenJourneyMap(string entry)
        {
            if (journeyHud != null) return;
            pendingWorldEntry = entry;
            if (entry != null) Hud.ClosePanel();
            var surface = Hud.JourneySurface();
            journeyHud = surface.gameObject.AddComponent<BaseWorldJourneyHud>();
            journeyHud.Initialize(this, font, entry);
            Hud.UpdateJourneyGuidance();
        }
        internal void LeaveJourneyMap()
        {
            if (WorldMarchRunning) return;
            if (BaseWorldJourney.OnWorld(State))
            {
                var world = State.Campaign.WorldJourney;
                var entry = world.Entries.FirstOrDefault(item => item.NodeId == world.NodeId && world.Discovered.Contains(item.Id));
                if (entry != null && Change(state =>
                {
                    if (BaseWorldJourney.Marching(state)) BaseWorldJourney.StopAtNode(state);
                    BaseWorldJourney.EnterScene(state, entry.Id);
                }, "返回" + WorldNodeTitle(world.NodeId))) ReloadTravelRegion();
                return;
            }
            var entryObject = State.Campaign.WorldJourney.Entries.FirstOrDefault(item => item.Id == pendingWorldEntry);
            if (entryObject != null && BaseDemo.Active(State))
            {
                var marker = BaseSpatial.Layout(State).Objects.FirstOrDefault(item => item.SourceObjectId == entryObject.ObjectId);
                if (marker != null && BaseDemoNpcs.RoadSign(marker.Id) != null)
                {
                    BaseDemoRelease.ArriveAtRoad(State, marker);
                    var facing = State.RoadArrivalFacing;
                    BaseInteraction.Walk(facing);
                    PlacePlayer(BaseConquest.Presentation.PlayerHeroFrames.FaceWalking(facing.X,facing.Y,BaseConquest.Presentation.HeroFacing.South));
                    lastTouchedRoad = null;
                }
            }
            ClearJourneyMap();
        }
        private void ClearJourneyMap()
        {
            pendingWorldEntry = null;
            pendingWorldRoute = null;
            attemptedSavedMarch = false;
            if (journeyHud == null) return;
            journeyHud.gameObject.SetActive(false);
            BattleBoardView.DestroyObject(journeyHud.gameObject);
            journeyHud = null;
            Hud?.UpdateJourneyGuidance();
        }
        private bool HandleJourneyMap()
        {
            bool onRoad = BaseWorldJourney.OnWorld(State);
            if (journeyHud == null && !onRoad) return false;
            if (journeyHud == null && !BaseBattleGateway.MainMenu)
            {
                OpenJourneyMap(null);
                if (BaseWorldJourney.Marching(State) && !attemptedSavedMarch && !Hud.PanelOpen) ResumeWorldMarch();
            }
            if (journeyHud == null) return true;
            if (Keyboard.current?.escapeKey.wasPressedThisFrame == true)
            {
                if (Hud.PanelOpen || Hud.ResourcePopoverOpen) Hud.ClosePanel(); else LeaveJourneyMap();
            }
            if (BaseBattleLoot.ShouldPresent(State) && !Hud.PanelOpen) Hud.OpenLoot();
            else if (State.Aftermath.GameOver && !Hud.PanelOpen) Hud.OpenMenu();
            return true;
        }
        internal void PrepareWorldMarch(IReadOnlyList<string> path)
        {
            if (Busy || WorldMarchRunning || path == null || path.Count < 2 || BaseWorldJourney.Marching(State)) return;
            pendingWorldRoute = path.ToList();
            Hud.OpenBattlePreparation(BasePrepRoute.Travel);
        }
        internal bool DepartWorldMarch(BaseBattlePreparation draft, bool production)
        {
            if (Busy || WorldMarchRunning || pendingWorldRoute == null) return false;
            string problem = BaseWorldJourney.DayAdvanceProblem(State, pendingWorldRoute);
            if (problem != "") { ReportMessage(problem, showFeedback:true); return false; }
            var entry = pendingWorldEntry;
            if (!Change(state => BaseWorldJourney.Depart(state, entry, pendingWorldRoute, draft, production), "队伍已出发")) return false;
            pendingWorldRoute = null;
            pendingWorldEntry = null;
            Hud.ClosePanel();
            if (entry != null) ReloadTravelRegion();
            else ResumeWorldMarch();
            return true;
        }
        public void EnterWorldScene(string entry)
        {
            if (Change(state => BaseWorldJourney.EnterScene(state, entry), "已进入地区场景")) ReloadTravelRegion();
        }
        public void ResumeWorldMarch()
        {
            if (Busy || WorldMarchRunning || !BaseWorldJourney.OnWorld(State) || !BaseWorldJourney.Marching(State)) return;
            attemptedSavedMarch = true;
            StartCoroutine(WorldMarchRoutine());
        }
        private IEnumerator WorldMarchRoutine()
        {
            WorldMarchRunning = true;
            bool arrival = false;
            var documents = new Dictionary<string,BattleScenarioDocument>();
            try
            {
                while (BaseWorldJourney.OnWorld(State) && BaseWorldJourney.Marching(State) && !State.Aftermath.GameOver && !BaseBattleLoot.Pending(State) && !Hud.PanelOpen)
                {
                    var current = State.Campaign.WorldJourney;
                    if (BaseWorldJourney.DayAdvanceProblem(State, current.Route, current.RoadIndex) != "") break;
                    Busy = true;
                    foreach (var file in BaseWorldJourney.RequiredBattles(State).Where(name => !documents.ContainsKey(name)).ToArray())
                    {
                        BattleScenarioDocument document = null;
                        yield return ReadBattle(file, loaded => document = loaded);
                        if (document == null) yield break;
                        documents[file] = document;
                    }
                    Busy = false;
                    bool advanced = Change(state =>
                    {
                        string from=state.Campaign.WorldJourney.NodeId;
                        if (!BaseWorldJourney.Step(state, file => documents[file])) throw new InvalidOperationException("道路暂时无法通行");
                        if (BaseWorldJourney.Marching(state) || BaseBattleLoot.Pending(state) || state.Aftermath.GameOver) return;
                        var world = state.Campaign.WorldJourney;
                        var entries = world.Entries.Where(entry => entry.NodeId == world.NodeId && world.Discovered.Contains(entry.Id)).ToArray();
                        var entry = entries.Length==1 ? entries[0] : BaseDemoFinale.Opened(state)&&world.NodeId=="bandit" ?
                            entries.FirstOrDefault(e=>e.Id==(from=="home"?"bandit_east_entry":"bandit_entry")) : null;
                        if (entry!=null) { BaseWorldJourney.EnterScene(state, entry.Id); arrival = true; }
                    }, "行军已结算");
                    if (!advanced) break;
                    if (!BaseWorldJourney.Marching(State)) break;
                    yield return new WaitForSecondsRealtime(.35f);
                }
            }
            finally { Busy = false; WorldMarchRunning = false; }
            if (arrival) ReloadTravelRegion();
        }
    }
}

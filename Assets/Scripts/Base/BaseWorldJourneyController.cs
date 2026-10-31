using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using BaseConquest.Battle;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BaseConquest.Base
{
    public sealed partial class BaseSceneController
    {
        private string pendingWorldEntry;
        private BaseWorldJourneyHud journeyHud;
        public bool WorldMarchRunning {get;private set;}
        public void EnterWorldScene(string entry)
        {
            if(Change(state=>BaseWorldJourney.EnterScene(state,entry),"已进入地区场景"))ReloadTravelRegion();
        }
        public void StartWorldMarch(IEnumerable<string> path)
        {
            if(Busy||WorldMarchRunning||!BaseWorldJourney.OnWorld(State))return;
            if(!BaseWorldJourney.Marching(State)&&!Change(state=>BaseWorldJourney.Start(state,path),"已确认行军"))return;
            StartCoroutine(WorldMarchRoutine());
        }
        private IEnumerator WorldMarchRoutine()
        {
            WorldMarchRunning=true;Busy=true;
            var documents=new Dictionary<string,BattleScenarioDocument>();
            foreach(string file in State.Campaign.Events.Select(item=>item.ScenarioFile).Concat(new[]{"23_据点迎战.json"}).Distinct())
            {
                BattleScenarioDocument document=null;yield return ReadBattle(file,value=>document=value);
                if(document==null){Busy=false;WorldMarchRunning=false;yield break;}documents[file]=document;
            }
            Busy=false;
            while(BaseWorldJourney.OnWorld(State)&&BaseWorldJourney.Marching(State)&&!BaseBattleLoot.Pending(State)&&!State.Aftermath.GameOver&&!Hud.PanelOpen)
            {
                bool advanced=false;
                if(!Change(state=>advanced=BaseWorldJourney.Step(state,file=>documents.TryGetValue(file,out var document)?document:throw new InvalidOperationException("缺少行军敌袭战场："+file)),"已按实际行程结算"))break;
                if(!advanced)break;
                yield return new WaitForSecondsRealtime(.4f);
            }
            WorldMarchRunning=false;
        }
    }
}

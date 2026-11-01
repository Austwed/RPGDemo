using System;
using BaseConquest.Battle;
using UnityEngine;

namespace BaseConquest.Base
{
    public sealed partial class BaseSceneController
    {
        public void StartStandaloneBattle(BaseStandaloneBattlePreset preset)
        {
            if(!RuntimeDeveloperOptions.Enabled||State==null||Busy||combatController!=null||State.Campaign.HasActiveBattle||State.Aftermath.GameOver)return;
            GameObject host=null;
            try
            {
                var session=BaseStandaloneBattle.Create(State,preset);
                Hud.ClosePanel();ClearSelection();Hud.SetVisible(false);
                host=new GameObject("StandaloneDeveloperBattle");host.SetActive(false);host.transform.SetParent(transform,false);
                regionView.Root.SetActive(false);
                combatController=host.AddComponent<BattleSceneController>();
                combatController.RestartStandaloneTest=()=>{if(FinishStandaloneBattle(host,false))StartStandaloneBattle(preset);};
                combatController.ConfigureSceneEncounter(session,null,null,(run,abandon,mainMenu)=>FinishStandaloneBattle(host,mainMenu),"独立测试 · "+BaseStandaloneBattle.Name(preset)+" · 退出不保存剧情");
                host.SetActive(true);
            }
            catch(Exception error)
            {
                if(host!=null)Destroy(host);combatController=null;regionView.Root.SetActive(true);Hud.SetVisible(true);
                Message="独立测试未启动："+error.Message;Hud.RefreshSummary();
            }
        }
        private bool FinishStandaloneBattle(GameObject host,bool mainMenu)
        {
            if(combatController==null)return false;
            combatController=null;regionView.Root.SetActive(true);
            if(host!=null){host.SetActive(false);Destroy(host);}
            Hud.SetVisible(true);ClearSelection();PlaceCompanions();PlacePlayer();
            Message="已退出独立战斗测试；剧情进度、物资与存档均未修改";Hud.RefreshSummary();
            if(mainMenu)ToMainMenu();
            return true;
        }
    }
}

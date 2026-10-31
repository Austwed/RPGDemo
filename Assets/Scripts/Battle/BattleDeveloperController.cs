using System;
namespace BaseConquest.Battle
{
    public sealed partial class BattleSceneController
    {
        public bool DeveloperAvailable=>battle!=null&&!CampaignGameOver&&!campaignSaved;
        public bool DeveloperUiBlocked=>campaignMenu!=null||drawerOpen||DetailsVisible||hud?.ReportVisible==true||hud?.ContextMenuVisible==true;
        public bool DeveloperReady=>DeveloperAvailable&&!animating&&!skipping&&battle.Phase!=BattlePhase.Finished;
        public void PauseForDeveloper(){ManualPause();realtime?.Model.SetPaused(true);CancelGesture();CloseContext();drawerOpen=false;detailsSlot=detailsUnitId=-1;hud?.CloseMenu();if(campaignMenu!=null){campaignMenu.SetActive(false);UnityEngine.Object.Destroy(campaignMenu);}}
        public bool DeveloperApplyUnit(BaseConquest.Base.DeveloperTargetSelection target,int id,string message)=>DeveloperEdit(b=>realtime!=null?target.ApplyRealtimeUnit(true,realtime,id):target.ApplyUnit(true,b,id),message);
        public bool DeveloperEdit(Func<BattleSimulation,bool> edit,string message)
        {
            if(!RuntimeDeveloperOptions.Enabled||!DeveloperReady)return false;
            PauseForDeveloper();bool ok=edit(battle);feedback=ok ? message : "所选对象或状态不适用此测试操作";
            boardView.Refresh();RefreshDeveloperViews();hud?.Refresh();PersistCampaignCheckpoint(true);return ok;
        }
        public void ClearDeveloperEffects()
        {
            if(battle==null)return;
            battle.ClearDeveloperEffects();boardView.Refresh();RefreshDeveloperViews();PersistCampaignCheckpoint(true,true);
        }
        private void RefreshDeveloperViews()
        {if(realtime!=null){realtime.RefreshDeveloperState();SyncRealtimeViews();}else{RefreshUnits();SyncViews();}}
    }
}

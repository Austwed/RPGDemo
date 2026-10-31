using System;
using BaseConquest.Battle;
using UnityEngine;
namespace BaseConquest.Base
{
    public sealed partial class BaseSceneController
    {
        public bool DeveloperAvailable=>State!=null&&!Busy&&combatController==null&&!State.Campaign.HasActiveBattle&&!State.Aftermath.GameOver&&!BaseBattleGateway.MainMenu;
        public bool DeveloperUiBlocked=>Hud==null||Hud.PanelOpen||ConstructionMode;
        public bool DeveloperPick(Vector2 screen,out GridPosition cell)=>regionView.Pick(screen,out cell);
        public bool DeveloperEdit(Func<BaseState,bool> edit,string message)
        {
            if(!RuntimeDeveloperOptions.Enabled||!DeveloperAvailable)return false;
            bool ok=Change(s=>{if(!edit(s))throw new InvalidOperationException("所选对象或状态不适用此测试操作");},message);
            regionView.Refresh();Hud.RefreshInspection();return ok;
        }
        public void ClearDeveloperEffects()
        {
            if(!DeveloperAvailable)return;
            Change(BaseDeveloperTools.ClearEffects,"已恢复临时地形、设施正常产出");regionView.Refresh();
        }
    }
}

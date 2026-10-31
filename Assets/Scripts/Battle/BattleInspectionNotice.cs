using UnityEngine;
namespace BaseConquest.Battle
{
    public sealed partial class BattleSceneController
    {
        private readonly TransientNotice informationNotice=new TransientNotice();
        public bool InformationNoticeVisible=>informationNotice.Visible(Time.unscaledTime);
        public void ShowInformationNotice()=>informationNotice.Show(Time.unscaledTime);
        public void DismissInformationNotice(){informationNotice.Dismiss();hud?.Refresh();}
    }
}

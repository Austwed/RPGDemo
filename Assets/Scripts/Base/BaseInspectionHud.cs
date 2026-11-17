using System.Collections.Generic;
using System.Linq;
using BaseConquest.Battle;
using UnityEngine;
using UnityEngine.UI;
namespace BaseConquest.Base
{
    public sealed partial class BaseHud
    {
        private readonly TransientNotice inspectionNotice=new TransientNotice();
        private readonly Dictionary<string,Text> extraCompanionMarkers=new Dictionary<string,Text>();
        private readonly Dictionary<string,Text> extraCompanionLabels=new Dictionary<string,Text>();
        public void NotifyInspection(){inspectionNotice.Show(Time.unscaledTime);RefreshInspection();}
        public void DismissInspection(){inspectionNotice.Dismiss();inspection.gameObject.SetActive(false);}
        private void RefreshCompanionLabels()
        {
            foreach(var a in owner.Companions.Agents.Where(a=>!labelObjects.Any(o=>o.Id==a.Id)))
            {
                if(!extraCompanionLabels.TryGetValue(a.Id,out var label))
                {label=Label(mapLabels,a.Name,Vector2.zero,new Vector2(260,40),18,TextAnchor.MiddleCenter);StyleMapName(label);extraCompanionLabels[a.Id]=label;extraCompanionMarkers[a.Id]=CreateNpcStatusMarker(label.rectTransform);}
                label.gameObject.SetActive(a.Visible);label.text=a.Name;var marker=extraCompanionMarkers[a.Id];marker.text=BaseDemoNpcs.StatusMarker(owner.State,a.Id);marker.gameObject.SetActive(marker.text!="");
                if(a.Visible)PlaceCompanionLabel(label.rectTransform,a.Id,a.Position.X,a.Position.Y);
            }
        }
    }
}

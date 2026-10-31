using BaseConquest.Battle;
using UnityEngine;
using UnityEngine.UI;
namespace BaseConquest.Base
{
    public sealed partial class BaseHud
    {
        private RectTransform constructionPalette,constructionCaption;
        private Button barricadeProject,demolishProject;
        private Text constructionResources,constructionStatus;
        private void BuildConstructionPalette()
        {
            constructionPalette=Box("ConstructionProjects",controls,new Vector2(0,1),new Vector2(95,-ExplorationHudLayout.HeaderHeight-192),new Vector2(166,360),new Color(.055f,.08f,.12f,.97f));
            Label(constructionPalette,"防御工程",new Vector2(0,151),new Vector2(150,40),22,TextAnchor.MiddleCenter);
            barricadeProject=MakeButton(constructionPalette,"拒马\n原料1 · 生命100",new Vector2(0,78),new Vector2(146,88),()=>owner.SelectConstructionProject(ConstructionProject.Barricade));
            demolishProject=MakeButton(constructionPalette,"拆除\n仅普通路障",new Vector2(0,-12),new Vector2(146,74),()=>owner.SelectConstructionProject(ConstructionProject.Demolish));
            constructionResources=Label(constructionPalette,"",new Vector2(0,-83),new Vector2(148,56),18,TextAnchor.MiddleCenter);
            MakeButton(constructionPalette,"退出 · Esc",new Vector2(0,-146),new Vector2(146,44),owner.StopConstruction);
            constructionCaption=Box("ConstructionStatus",controls,new Vector2(.5f,0),new Vector2(70,34),new Vector2(860,62),new Color(.055f,.08f,.12f,.96f));
            constructionStatus=Label(constructionCaption,"",Vector2.zero,new Vector2(838,56),19,TextAnchor.MiddleCenter);
            constructionPalette.gameObject.SetActive(false);constructionCaption.gameObject.SetActive(false);
        }
        public void RefreshConstructionPalette()
        {
            if(constructionPalette==null)return;
            bool show=owner.ConstructionMode&&!PanelOpen&&!owner.Busy&&owner.State!=null&&!owner.State.Campaign.HasActiveBattle&&!owner.State.Aftermath.GameOver;
            constructionPalette.gameObject.SetActive(show);constructionCaption.gameObject.SetActive(show);
            mapLabels.offsetMin=new Vector2(show ? 188 : 12,ExplorationHudLayout.FooterHeight);
            if(!show)return;
            barricadeProject.GetComponent<Image>().color=owner.SelectedConstructionProject==ConstructionProject.Barricade ? new Color(.15f,.39f,.52f) : buttonColor;
            demolishProject.GetComponent<Image>().color=owner.SelectedConstructionProject==ConstructionProject.Demolish ? new Color(.15f,.39f,.52f) : buttonColor;
            int material=owner.State.Inventory.Materials;constructionResources.text=BaseDemoFlow.Active(owner.State)?"成品 "+owner.State.Demo.Flow.ReadyStakes+" · 原料 "+material:"原料 "+material+"\n材料可建 "+material/3+"处";
            constructionStatus.text=owner.ConstructionHoverText;
        }
    }
}

using BaseConquest.Battle;
using UnityEngine;
using UnityEngine.UI;
namespace BaseConquest.Base
{
    public sealed partial class BaseHud
    {
        private RectTransform constructionPalette,constructionCaption;
        private UnityEngine.UI.Button demolishProject,trapProject,sandbagProject;
        private Text constructionResources,constructionStatus;
        private void BuildConstructionPalette()
        {
            constructionPalette=Box("ConstructionProjects",controls,new Vector2(0,1),new Vector2(95,-ExplorationHudLayout.HeaderHeight-212),new Vector2(166,400),new Color(.055f,.08f,.12f,.97f));
            Label(constructionPalette,"防御工程",new Vector2(0,171),new Vector2(150,40),22,TextAnchor.MiddleCenter);
            trapProject=MakeButton(constructionPalette,"陷阱\n原料4 · 最多10",new Vector2(0,78),new Vector2(146,70),()=>owner.SelectConstructionProject(ConstructionProject.Trap));
            sandbagProject=MakeButton(constructionPalette,"沙袋×3\n原料2",new Vector2(0,3),new Vector2(146,70),()=>owner.SelectConstructionProject(ConstructionProject.Sandbag));
            demolishProject=MakeButton(constructionPalette,"拆除\n整组工事不返还",new Vector2(0,-12),new Vector2(146,74),()=>owner.SelectConstructionProject(ConstructionProject.Demolish));
            demolishProject.GetComponent<RectTransform>().anchoredPosition=new Vector2(0,-71);
            constructionResources=Label(constructionPalette,"",new Vector2(0,-120),new Vector2(148,36),18,TextAnchor.MiddleCenter);
            MakeButton(constructionPalette,"退出 · Esc",new Vector2(0,-171),new Vector2(146,44),owner.StopConstruction);
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
            trapProject.gameObject.SetActive(true);sandbagProject.gameObject.SetActive(true);
            trapProject.GetComponent<Image>().color=owner.SelectedConstructionProject==ConstructionProject.Trap ? new Color(.15f,.39f,.52f) : buttonColor;
            sandbagProject.GetComponent<Image>().color=owner.SelectedConstructionProject==ConstructionProject.Sandbag ? new Color(.15f,.39f,.52f) : buttonColor;
            demolishProject.GetComponent<Image>().color=owner.SelectedConstructionProject==ConstructionProject.Demolish ? new Color(.15f,.39f,.52f) : buttonColor;
            int material=owner.State.Inventory.Materials;constructionResources.text="原料 "+material;
            constructionStatus.text=owner.ConstructionHoverText;
        }
    }
}

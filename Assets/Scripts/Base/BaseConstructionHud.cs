using System.Linq;
using BaseConquest.Battle;
using BaseConquest.Presentation;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
namespace BaseConquest.Base
{
    public sealed partial class BaseHud
    {
        private RectTransform constructionPalette,constructionCaption;
        private UnityEngine.UI.Button demolishProject,trapProject,sandbagProject;
        private Text constructionResources,constructionStatus;
        private RectTransform campConstructionPalette;
        private Button campStore, campConfirm, campExit;
        private Button[] campProjectButtons;
        private Text campBuildStatus;
        private (SettlementLayoutDraft Draft, int Revision, string Project, string Status) campPaletteContent;
        private void BuildConstructionPalette()
        {
            var theme=DemoUiTheme.Current;
            constructionPalette=Box("FieldworkToolbar",controls,new Vector2(.5f,0),new Vector2(0,44),new Vector2(666,60),theme.Panel);
            constructionResources=Label(constructionPalette,"",new Vector2(-265,0),new Vector2(116,44),19,TextAnchor.MiddleCenter);
            trapProject=MakeButton(constructionPalette,"陷阱 · 原料4",new Vector2(-126,0),new Vector2(150,44),()=>owner.SelectConstructionProject(ConstructionProject.Trap));
            sandbagProject=MakeButton(constructionPalette,"沙袋 · 原料2",new Vector2(28,0),new Vector2(150,44),()=>owner.SelectConstructionProject(ConstructionProject.Sandbag));
            demolishProject=MakeButton(constructionPalette,"拆除",new Vector2(161,0),new Vector2(105,44),()=>owner.SelectConstructionProject(ConstructionProject.Demolish));
            MakeButton(constructionPalette,"退出",new Vector2(268,0),new Vector2(98,44),owner.StopConstruction);
            constructionCaption=Box("FieldworkHint",controls,new Vector2(.5f,0),new Vector2(0,99),new Vector2(630,42),theme.Panel);
            constructionCaption.GetComponent<Image>().raycastTarget=false;
            constructionStatus=Label(constructionCaption,"",Vector2.zero,new Vector2(612,40),16,TextAnchor.MiddleCenter);
            constructionStatus.GetComponent<Shadow>().enabled=false;
            constructionResources.GetComponent<Shadow>().enabled=false;
            constructionPalette.gameObject.SetActive(false);constructionCaption.gameObject.SetActive(false);
            BuildCampConstructionPalette();
        }
        private void BuildCampConstructionPalette()
        {
            var theme = DemoUiTheme.Current;
            var tint=theme.Panel;
            tint.a=.82f;
            campConstructionPalette = Solid("CampConstructionPalette",controls,new Vector2(.5f,0),new Vector2(0,43),
                new Vector2(780,58),tint);
            campBuildStatus=Label(campConstructionPalette,"",Vector2.zero,new Vector2(205,44),16,TextAnchor.MiddleCenter);
            campBuildStatus.GetComponent<Shadow>().enabled=false;
            campBuildStatus.color=theme.Ink;
            campConfirm=MakeButton(campConstructionPalette,"确认布局",Vector2.zero,new Vector2(103,42),()=>owner.ConfirmCampLayout());
            campConfirm.name="ConfirmLayout";
            campExit=MakeButton(campConstructionPalette,"退出",Vector2.zero,new Vector2(75,42),owner.RequestExitCampPlacement);
            campExit.name="ExitLayout";
            var projects=BaseFacilityLayout.Catalog;
            campProjectButtons=new Button[projects.Length];
            for(int i=0;i<projects.Length;i++)
            {
                string id=projects[i].Id;
                var button=MakeButton(campConstructionPalette,projects[i].Name,Vector2.zero,new Vector2(82,42),()=>owner.SelectCampProject(id));
                button.name="Project-"+id;
                var trigger=button.gameObject.AddComponent<EventTrigger>();
                foreach (var type in new[]{EventTriggerType.BeginDrag,EventTriggerType.Drag,EventTriggerType.EndDrag})
                {
                    var entry=new EventTrigger.Entry{eventID=type};
                    entry.callback.AddListener(data=>
                    {
                        if (!button.interactable || !(data is PointerEventData pointer)) return;
                        owner.DragPaletteProject(id,pointer.position,type==EventTriggerType.EndDrag);
                    });
                    trigger.triggers.Add(entry);
                }
                campProjectButtons[i]=button;
            }
            campStore=MakeButton(campConstructionPalette,"收存",Vector2.zero,new Vector2(72,42),owner.StoreCampProject);
            campStore.name="StoreProject";
            campConstructionPalette.gameObject.SetActive(false);
        }
        private void RefreshCampConstructionPalette(bool show)
        {
            campConstructionPalette.gameObject.SetActive(show);
            if (!show) return;
            var draft = owner.CampLayoutDraft;
            var content=(draft,draft.Revision,owner.CampProject,owner.CampPlacementMessage);
            if(content==campPaletteContent)return;
            campPaletteContent=content;
            bool store=draft.CanStore(owner.CampProject);
            float width=12+draft.Projects.Length*86+(store?76:0)+205+107+79;
            campConstructionPalette.sizeDelta=new Vector2(width,58);
            float x=-width*.5f+6;
            void Place(RectTransform rect,float w)
            {
                rect.anchoredPosition=new Vector2(x+w*.5f,0);
                x+=w+4;
            }
            foreach(var button in campProjectButtons)
            {
                var id=button.name.Substring("Project-".Length);
                var project=draft.Projects.FirstOrDefault(d=>d.Id==id);
                button.gameObject.SetActive(project!=null);
                if(project==null)continue;
                button.targetGraphic.color=owner.CampProject==id?DemoUiTheme.Current.Gold:DemoUiTheme.Current.Button;
                Place((RectTransform)button.transform,82);
            }
            campStore.gameObject.SetActive(store);
            if(store)Place((RectTransform)campStore.transform,72);
            string problem=owner.CampPlacementMessage;
            int materials=draft.Cost(owner.State,false),money=draft.Cost(owner.State,true);
            string cost=money>0 ? $"原料 {materials} · 钱币 {money}" : $"原料 {materials}/{owner.State.Inventory.Materials}";
            if (!owner.CampLayoutChanged && draft.Village && draft.Placed("barracks")==null && owner.CampProject=="barracks")
                cost=$"原料 {draft.Price(owner.State,"barracks",false)} · 钱币 {draft.Price(owner.State,"barracks",true)}";
            campBuildStatus.text=string.IsNullOrEmpty(problem)?cost:problem;
            Place(campBuildStatus.rectTransform,201);
            campConfirm.interactable=owner.CampLayoutChanged&&string.IsNullOrEmpty(problem);
            Place((RectTransform)campConfirm.transform,103);
            Place((RectTransform)campExit.transform,75);
        }
        public void RefreshConstructionPalette()
        {
            if(constructionPalette==null)return;
            bool active=owner.ConstructionMode&&!PanelOpen&&!owner.Busy&&owner.State!=null&&!owner.State.Campaign.HasActiveBattle&&!owner.State.Aftermath.GameOver;
            bool show=active&&!owner.CampPlacementMode;
            bool showCamp=active&&owner.CampPlacementMode;
            RefreshCampConstructionPalette(showCamp);
            constructionPalette.gameObject.SetActive(show);constructionCaption.gameObject.SetActive(show);
            mapLabels.offsetMin=new Vector2(12,showCamp ? 154 : ExplorationHudLayout.FooterHeight);
            if(!show)return;
            trapProject.gameObject.SetActive(true);sandbagProject.gameObject.SetActive(true);
            trapProject.GetComponent<Image>().color=owner.SelectedConstructionProject==ConstructionProject.Trap ? DemoUiTheme.Current.Gold : DemoUiTheme.Current.Button;
            sandbagProject.GetComponent<Image>().color=owner.SelectedConstructionProject==ConstructionProject.Sandbag ? DemoUiTheme.Current.Gold : DemoUiTheme.Current.Button;
            demolishProject.GetComponent<Image>().color=owner.SelectedConstructionProject==ConstructionProject.Demolish ? DemoUiTheme.Current.Gold : DemoUiTheme.Current.Button;
            int material=owner.State.Inventory.Materials;constructionResources.text="原料 "+material;
            constructionStatus.text=owner.ConstructionHoverText;
        }
    }
}

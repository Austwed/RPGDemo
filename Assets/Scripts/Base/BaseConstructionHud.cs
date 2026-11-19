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
        private Button campConstructionResume, campStore, campConfirm, campExit;
        private Button[] campProjectButtons;
        private Text campBuildStatus, campReturnReminder;
        private (BaseState Draft, int Revision, string Project, string Status) campPaletteContent;
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
            BuildHomeRebuildPalette();
            BuildCampConstructionPalette();
        }
        private void BuildCampConstructionPalette()
        {
            var theme = DemoUiTheme.Current;
            campConstructionResume = MakeButton(controls, "营地建设", new Vector2(-156, 115), new Vector2(214, 48),
                owner.ArmCampPlacement, new Vector2(1, 0));
            campConstructionResume.name = "CampConstructionResume";
            campReturnReminder = Label(controls, "从西口返回灰岩营地", new Vector2(-156, 115), new Vector2(245, 38),
                20, TextAnchor.MiddleCenter, new Vector2(1, 0));
            campConstructionPalette = Box("CampConstructionPalette", controls, new Vector2(.5f, 0), new Vector2(0, 88),
                new Vector2(1140, 120), theme.Panel);
            campBuildStatus = Label(campConstructionPalette, "", new Vector2(-195, 34), new Vector2(710, 40), 18);
            campBuildStatus.GetComponent<Shadow>().enabled = false;
            campBuildStatus.color = theme.Ink;
            campConfirm = MakeButton(campConstructionPalette, "确认布局", new Vector2(349, 34), new Vector2(146, 40),
                () => owner.ConfirmCampLayout());
            campConfirm.name = "ConfirmLayout";
            campExit = MakeButton(campConstructionPalette, "退出", new Vector2(490, 34), new Vector2(116, 40), owner.RequestExitCampPlacement);
            campExit.name = "ExitLayout";
            var projects = BaseFacilityLayout.Catalog.Where(def => !def.Required).ToArray();
            campProjectButtons = new Button[projects.Length];
            for (int i = 0; i < projects.Length; i++)
            {
                string id = projects[i].Id;
                var button = MakeButton(campConstructionPalette, projects[i].Name, new Vector2(-438 + i * 203, -23),
                    new Vector2(193, 52), () => owner.SelectCampProject(id));
                button.name = "Project-" + id;
                var drag = button.gameObject.AddComponent<EventTrigger>();
                var start = new EventTrigger.Entry { eventID = EventTriggerType.BeginDrag };
                start.callback.AddListener(_ => { if (button.interactable) owner.SelectCampProject(id); });
                var drop = new EventTrigger.Entry { eventID = EventTriggerType.EndDrag };
                drop.callback.AddListener(data =>
                {
                    if (button.interactable && data is PointerEventData pointer) owner.PlaceCampProjectFromPointer(pointer.position);
                });
                drag.triggers.Add(start);
                drag.triggers.Add(drop);
                var text = button.GetComponentInChildren<Text>();
                text.fontSize = 18;
                text.GetComponent<Shadow>().enabled = false;
                campProjectButtons[i] = button;
            }
            campStore = MakeButton(campConstructionPalette, "收存设施", new Vector2(473, -23), new Vector2(150, 52),
                owner.StoreCampProject);
            campStore.name = "StoreProject";
            campConstructionPalette.gameObject.SetActive(false);
            campConstructionResume.gameObject.SetActive(false);
            campReturnReminder.gameObject.SetActive(false);
        }
        private void RefreshCampConstructionPalette(bool show)
        {
            campConstructionResume.gameObject.SetActive(!PanelOpen && owner.CampConstructionAvailable);
            campReturnReminder.gameObject.SetActive(!PanelOpen && owner.CampConstructionReturnReminder);
            campConstructionPalette.gameObject.SetActive(show);
            if (!show) return;
            var draft = owner.CampLayoutDraft;
            var content = (draft, draft.Layout.Revision, owner.CampProject, owner.CampPlacementMessage);
            if (content == campPaletteContent) return;
            campPaletteContent = content;
            var projects = BaseFacilityLayout.CampProjects(draft).ToArray();
            foreach (var button in campProjectButtons)
            {
                string id = button.name.Substring("Project-".Length);
                var project = projects.FirstOrDefault(def => def.Id == id);
                button.gameObject.SetActive(project != null);
                if (project == null) continue;
                button.interactable = id != "office" || !BaseDemo.ExperimentalManagementHidden(draft) || BaseFacilityLayout.Built(draft, id);
                button.targetGraphic.color = owner.CampProject == id ? DemoUiTheme.Current.Gold : DemoUiTheme.Current.Button;
                int cost = BaseFacilityLayout.Owned(draft, id) ? 0 : project.Materials;
                button.GetComponentInChildren<Text>().text = project.Name +
                    $" {BaseFacilityLayout.Count(draft, id)}/{project.MaxCount}\n收存 {(BaseFacilityLayout.Stored(draft, id) ? 1 : 0)} · 原料 {cost}";
            }
            campConfirm.interactable = owner.CampLayoutChanged;
            campExit.GetComponentInChildren<Text>().text = owner.CampLayoutChanged ? "放弃布局" : "退出";
            campStore.interactable = BaseFacilityLayout.Built(draft, owner.CampProject);
            campBuildStatus.text = $"营地建设 · 原料 {owner.State.Inventory.Materials - draft.Inventory.Materials}/{owner.State.Inventory.Materials}    {owner.CampPlacementMessage}";
        }
        public void RefreshConstructionPalette()
        {
            if(constructionPalette==null)return;
            bool active=owner.ConstructionMode&&!PanelOpen&&!owner.Busy&&owner.State!=null&&!owner.State.Campaign.HasActiveBattle&&!owner.State.Aftermath.GameOver;
            bool show=active&&!owner.RebuildPlacementMode&&!owner.CampPlacementMode;
            bool showRebuild=active&&owner.RebuildPlacementMode;
            bool showCamp=active&&owner.CampPlacementMode;
            RefreshHomeRebuildPalette(showRebuild);
            RefreshCampConstructionPalette(showCamp);
            constructionPalette.gameObject.SetActive(show);constructionCaption.gameObject.SetActive(show);
            mapLabels.offsetMin=new Vector2(showRebuild&&!owner.HomeRebuildOverview ? HomeRebuildGutter : 12,showCamp ? 154 : ExplorationHudLayout.FooterHeight);
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

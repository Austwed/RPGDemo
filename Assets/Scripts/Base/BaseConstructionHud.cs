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
        private Button campConstructionResume, campStore, campConfirm;
        private Button[] campProjectButtons;
        private Text campBuildStatus, campReturnReminder;
        private (BaseState Draft, int Revision, string Project, string Status) campPaletteContent;
        private void BuildConstructionPalette()
        {
            constructionPalette=Box("ConstructionProjects",controls,new Vector2(0,1),new Vector2(95,-ExplorationHudLayout.HeaderHeight-212),new Vector2(166,400),new Color(.055f,.08f,.12f,.97f));
            Label(constructionPalette,"防御工程",new Vector2(0,171),new Vector2(150,40),22,TextAnchor.MiddleCenter);
            trapProject=MakeButton(constructionPalette,"埋设陷阱\n原料4 · 最多10",new Vector2(0,78),new Vector2(146,70),()=>owner.SelectConstructionProject(ConstructionProject.Trap));
            sandbagProject=MakeButton(constructionPalette,"堆筑沙袋\n原料2",new Vector2(0,3),new Vector2(146,70),()=>owner.SelectConstructionProject(ConstructionProject.Sandbag));
            demolishProject=MakeButton(constructionPalette,"拆除\n整组工事不返还",new Vector2(0,-12),new Vector2(146,74),()=>owner.SelectConstructionProject(ConstructionProject.Demolish));
            demolishProject.GetComponent<RectTransform>().anchoredPosition=new Vector2(0,-71);
            constructionResources=Label(constructionPalette,"",new Vector2(0,-120),new Vector2(148,36),18,TextAnchor.MiddleCenter);
            MakeButton(constructionPalette,"退出 · Esc",new Vector2(0,-171),new Vector2(146,44),owner.StopConstruction);
            constructionCaption=Box("ConstructionStatus",controls,new Vector2(.5f,0),new Vector2(70,34),new Vector2(860,62),new Color(.055f,.08f,.12f,.96f));
            constructionStatus=Label(constructionCaption,"",Vector2.zero,new Vector2(838,56),19,TextAnchor.MiddleCenter);
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
            MakeButton(campConstructionPalette, "退出", new Vector2(490, 34), new Vector2(116, 40), owner.RequestExitCampPlacement).name = "ExitLayout";
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
            campStore.interactable = BaseFacilityLayout.Built(draft, owner.CampProject);
            campBuildStatus.text = $"营地建设 · 原料 {owner.State.Inventory.Materials - draft.Inventory.Materials}/{owner.State.Inventory.Materials}    {owner.CampPlacementMessage}";
        }
        internal void OpenDiscardCampLayout()
        {
            BeginPanel("退出营地建设", "camp-construction-discard");
            RowButton("放弃布局", -210, 400, () => { ClosePanel(); owner.StopConstruction(); RefreshSummary(); });
            RowButton("继续建设", 210, 400, ClosePanel);
            row += 60;
            EndPanel();
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
            mapLabels.offsetMin=new Vector2(showRebuild&&!owner.HomeRebuildOverview ? HomeRebuildGutter : show ? 188 : 12,showCamp ? 154 : ExplorationHudLayout.FooterHeight);
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

using BaseConquest.Base;
using BaseConquest.Presentation;
using UnityEngine;
using UnityEngine.UI;
namespace BaseConquest.Battle
{
    // 战斗结束后的只读页面，关闭后可回看棋盘，再次打开不重新结算。
    public sealed class BattleResultPanel
    {
        private readonly RectTransform backdrop, panel, content;
        private readonly Text title, body;
        private readonly ScrollRect scroll;
        private BattleReport previousReport, renderedReport;
        private BattleSide previousObserver;
        private bool dismissed;
        private readonly BattleSceneController campaignOwner;
        private readonly Button returnBase;
        private readonly Button loadGame,mainMenu,prisoners;
        private readonly RectTransform viewBoard;
        private string renderedCampaignFeedback;
        public bool Visible => backdrop.gameObject.activeSelf;
        public RectTransform Blocker => backdrop;
        public BattleResultPanel(Transform parent, Font font, BattleSceneController campaignOwner = null)
        {
            this.campaignOwner = campaignOwner;
            backdrop = Rect("BattleReportBackdrop", parent); backdrop.anchorMin = Vector2.zero; backdrop.anchorMax = Vector2.one; backdrop.offsetMin=Vector2.zero;backdrop.offsetMax=new Vector2(0,-64);
            backdrop.gameObject.GetOrAdd<Image>().color = new Color(0,0,0,0.65f);
            panel = Rect("BattleReportPage", backdrop); panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(0.5f,0.5f); panel.sizeDelta = new Vector2(680,520);
            panel.gameObject.GetOrAdd<Image>().color = new Color(0.055f,0.08f,0.12f,1);
            title = Label("ReportTitle", panel, font, 22); title.fontStyle = FontStyle.Bold; title.rectTransform.anchorMin = new Vector2(0,1); title.rectTransform.anchorMax=Vector2.one; title.rectTransform.offsetMin=new Vector2(18,-48);title.rectTransform.offsetMax=new Vector2(-18,-10);
            var viewport = Rect("ReportViewport", panel); viewport.anchorMin=Vector2.zero;viewport.anchorMax=Vector2.one;viewport.offsetMin=new Vector2(18,18);viewport.offsetMax=new Vector2(-18,-116);viewport.gameObject.GetOrAdd<Image>().color=new Color(0,0,0,0.1f);viewport.gameObject.GetOrAdd<RectMask2D>();
            content = Rect("ReportContent", viewport); content.anchorMin=new Vector2(0,1);content.anchorMax=Vector2.one;content.pivot=new Vector2(0.5f,1);content.sizeDelta=new Vector2(0,100);
            body = Label("ReportText", content, font, 14);body.alignment=TextAnchor.UpperLeft;body.supportRichText=true;body.horizontalOverflow=HorizontalWrapMode.Wrap;body.verticalOverflow=VerticalWrapMode.Overflow;body.lineSpacing=1.2f;body.rectTransform.offsetMin=new Vector2(8,0);body.rectTransform.offsetMax=new Vector2(-8,0);
            scroll=viewport.gameObject.GetOrAdd<ScrollRect>();scroll.viewport=viewport;scroll.content=content;scroll.horizontal=false;scroll.movementType=ScrollRect.MovementType.Clamped;
            var close = Rect("查看棋盘", panel);viewBoard=close;close.anchorMin=close.anchorMax=new Vector2(0.5f,1);close.pivot=new Vector2(0.5f,1);close.anchoredPosition=new Vector2(0,-60);close.sizeDelta=new Vector2(150,38);close.gameObject.GetOrAdd<Image>().color=new Color(0.16f,0.28f,0.4f);var button=close.gameObject.GetOrAdd<Button>();button.targetGraphic=close.GetComponent<Image>();button.onClick.AddListener(Close);Label("Label",close,font,14).text="查看棋盘";
            backdrop.gameObject.SetActive(false);
            var back = Rect("返回基地", panel); back.anchorMin = back.anchorMax = new Vector2(0.5f, 1); back.pivot = new Vector2(0.5f, 1); back.anchoredPosition = new Vector2(110, -60); back.sizeDelta = new Vector2(190, 38);
            back.gameObject.GetOrAdd<Image>().color = new Color(0.2f, 0.38f, 0.3f); returnBase = back.gameObject.GetOrAdd<Button>(); returnBase.targetGraphic=back.GetComponent<Image>(); returnBase.onClick.AddListener(() => this.campaignOwner?.ReturnCampaign()); Label("Label", back, font, 14).text = "处理战利品并返营";
            back.gameObject.SetActive(false);
            var load=Rect("读取存档",panel);load.anchorMin=load.anchorMax=new Vector2(.5f,1);load.pivot=new Vector2(.5f,1);load.anchoredPosition=new Vector2(-110,-60);load.sizeDelta=new Vector2(190,38);load.gameObject.GetOrAdd<Image>().color=new Color(.2f,.3f,.4f);loadGame=load.gameObject.GetOrAdd<Button>();loadGame.targetGraphic=load.GetComponent<Image>(); loadGame.onClick.AddListener(()=>this.campaignOwner?.OpenCampaignSaves(true));Label("Label",load,font,14).text="读取存档";
            var menu=Rect("回主菜单",panel);menu.anchorMin=menu.anchorMax=new Vector2(.5f,1);menu.pivot=new Vector2(.5f,1);menu.anchoredPosition=new Vector2(110,-60);menu.sizeDelta=new Vector2(190,38);menu.gameObject.GetOrAdd<Image>().color=new Color(.3f,.25f,.2f);mainMenu=menu.gameObject.GetOrAdd<Button>();mainMenu.targetGraphic=menu.GetComponent<Image>(); mainMenu.onClick.AddListener(()=>this.campaignOwner?.CampaignMainMenu());Label("Label",menu,font,14).text="回主菜单";
            load.gameObject.SetActive(false);menu.gameObject.SetActive(false);
            var held=Rect("俘虏处理",panel);held.anchorMin=held.anchorMax=new Vector2(.5f,1);held.pivot=new Vector2(.5f,1);held.anchoredPosition=new Vector2(0,-60);held.sizeDelta=new Vector2(150,38);held.gameObject.GetOrAdd<Image>().color=new Color(.3f,.4f,.2f);prisoners=held.gameObject.GetOrAdd<Button>();prisoners.targetGraphic=held.GetComponent<Image>(); prisoners.onClick.AddListener(()=>this.campaignOwner?.ReturnCampaignPrisoners());Label("Label",held,font,14).text="战利品与俘虏";held.gameObject.SetActive(false);
        }
        public void Close() { if(campaignOwner?.CampaignGameOver==true)return;dismissed=true;backdrop.gameObject.SetActive(false); }
        public void Open() { dismissed=false; }
        public void Refresh(BattleSimulation battle, BattleSide observer, bool animating, bool skipping, Vector2 available)
        {
            var report=battle.Report;
            if (report!=previousReport) {previousReport=report;dismissed=false;scroll.verticalNormalizedPosition=1;}
            bool visible=battle.Phase==BattlePhase.Finished && report!=null && !animating && !skipping && !dismissed
                && campaignOwner?.ContinuesThroughDialogue!=true;
            backdrop.gameObject.SetActive(visible);
            if (!visible) return;
            bool campaign = campaignOwner != null && campaignOwner.IsCampaignBattle;
            bool ended=campaign&&campaignOwner.CampaignGameOver;
            returnBase.gameObject.SetActive(campaign&&!ended); returnBase.interactable = campaign && campaignOwner.CanReturnCampaign;
            loadGame.gameObject.SetActive(ended);mainMenu.gameObject.SetActive(false);viewBoard.gameObject.SetActive(!ended);
            bool hasCaptives=campaign&&!ended&&!string.IsNullOrEmpty(campaignOwner.CampaignCaptives);prisoners.gameObject.SetActive(hasCaptives);prisoners.interactable=hasCaptives&&campaignOwner.CanReturnCampaign;
            viewBoard.anchoredPosition = new Vector2(campaign ? -130 : 0,-60);returnBase.GetComponent<RectTransform>().anchoredPosition=new Vector2(85,-60);prisoners.gameObject.SetActive(false);
            panel.sizeDelta=new Vector2(Mathf.Min(680,Mathf.Max(280,available.x-32)),Mathf.Min(520,Mathf.Max(220,available.y-32)));
            title.text=ended ? "游戏结束" : "战后报告 · "+BattleSceneController.OutcomeName(report.Outcome);
            bool sceneEncounter=campaignOwner?.IsSceneEncounter==true;
            if(sceneEncounter)
            {
                title.text=campaignOwner.SceneEncounterResult;
                returnBase.GetComponentInChildren<Text>().text=campaignOwner.IsBanditHideout?BaseBanditHideout.ReturnText:
                    report.Outcome==BattleOutcome.SideAWin
                        ?campaignOwner.IsDemoAttack?"结算并留在当地":campaignOwner.IsDemoDefense?"结算并返回营地":campaignOwner.IsDemoRuins?"返回入口":"继续剧情"
                        :campaignOwner.IsDemoDefense||campaignOwner.IsDemoAttack||campaignOwner.IsDemoRuins?"结算并撤退":"无损重试";
            }
            bool refreshText=renderedReport!=report || previousObserver!=observer || campaign && renderedCampaignFeedback != campaignOwner.Feedback;
            if(refreshText)
            {
                string summary=BattleReportFormatting.Format(report,observer);
                if(campaignOwner?.IsRealtimeCampaign==true)summary=summary.Replace("回合 "+report.Rounds,"作战 "+(campaignOwner.Realtime?.Model.Elapsed??0).ToString("0.0")+"秒");
                if(sceneEncounter)
                {
                    string feedback=campaignOwner.Feedback;
                    body.text=campaignOwner.SceneEncounterReport+"\n\n"+summary;
                    if(!string.IsNullOrEmpty(feedback)&&feedback!=title.text)body.text+="\n\n"+feedback;
                }
                else body.text=summary+(campaign&&!string.IsNullOrEmpty(campaignOwner.Feedback)?"\n\n"+campaignOwner.Feedback:"");
                previousObserver=observer;renderedReport=report;renderedCampaignFeedback=campaign ? campaignOwner.Feedback : null;
            }
            content.sizeDelta=new Vector2(0,Mathf.Max(scroll.viewport.rect.height,body.preferredHeight+12));
            if(refreshText) { scroll.StopMovement();scroll.verticalNormalizedPosition=1; }
            backdrop.SetAsLastSibling();
        }
        private static RectTransform Rect(string name, Transform parent)
        {return UiPrefabBinding.Rect(name,parent);}
        private static Text Label(string name,Transform parent,Font font,int size)
        {var r=Rect(name,parent);r.anchorMin=Vector2.zero;r.anchorMax=Vector2.one;r.offsetMin=r.offsetMax=Vector2.zero;var t=r.gameObject.GetOrAdd<Text>();t.font=font;t.fontSize=size;t.color=Color.white;t.alignment=TextAnchor.MiddleCenter;t.raycastTarget=false;return t;}
    }
}

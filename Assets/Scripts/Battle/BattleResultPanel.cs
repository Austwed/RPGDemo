using System;
using System.Linq;
using BaseConquest.Base;
using BaseConquest.Presentation;
using UnityEngine;
using UnityEngine.UI;

namespace BaseConquest.Battle
{
    public sealed class BattleResultPanel
    {
        private readonly RectTransform backdrop, panel;
        private readonly Text title, metrics, note;
        private readonly ArmyColumn own, opposing;
        private readonly Button viewBoard, returnBase, loadGame, mainMenu;
        private readonly BattleSceneController owner;
        private BattleReport previous;
        private BattleSide previousObserver;
        private string lastCaptives, defenseSettlement;
        private bool dismissed;
        public bool Visible => backdrop.gameObject.activeSelf;
        public RectTransform Blocker => backdrop;

        public BattleResultPanel(Transform parent, Font font, BattleSceneController campaignOwner = null)
        {
            owner=campaignOwner;
            backdrop=Region("AfterActionBackdrop",parent,0,0,1,1,0,0,0,0);
            backdrop.gameObject.GetOrAdd<Image>().color=DemoUiTheme.Current.Shade;
            panel=Region("AfterActionPanel",backdrop,.5f,.5f,.5f,.5f,0,0,0,0);
            panel.gameObject.GetOrAdd<Image>();
            DemoUiTheme.Current.Frame(panel);
            title=TextAt("Outcome",panel,font,27,0,.85f,1,1,20,2,-20,-6);
            title.fontStyle=FontStyle.Bold;
            metrics=TextAt("BattleMetrics",panel,font,18,0,.75f,1,.85f,20,0,-20,0);
            own=new ArmyColumn(panel,font,true);
            opposing=new ArmyColumn(panel,font,false);
            note=TextAt("SettlementNote",panel,font,16,0,.14f,1,.22f,16,0,-16,0);
            viewBoard=ActionButton("ReviewBoard","查看战场",false,Close);
            returnBase=ActionButton("SettleBattle","结算并返回营地",true,()=>owner?.ReturnCampaign());
            loadGame=ActionButton("LoadGame","读取存档",false,()=>owner?.OpenCampaignSaves(true));
            mainMenu=ActionButton("MainMenu","回主菜单",true,()=>owner?.CampaignMainMenu());
            backdrop.gameObject.SetActive(false);

            Button ActionButton(string id,string label,bool right,Action action)
            {
                var rect=Region(id,panel,right?.51f:.21f,.025f,right?.79f:.49f,.135f,0,0,0,0);
                var button=rect.gameObject.GetOrAdd<Button>();
                button.targetGraphic=rect.gameObject.GetOrAdd<Image>();
                DemoUiTheme.Current.StyleButton(button);
                button.onClick.AddListener(()=>action());
                TextAt("Label",rect,font,18,0,0,1,1,5,2,-5,-2).text=label;
                return button;
            }
        }

        private sealed class ArmyColumn
        {
            private readonly Text totals, rows;
            private readonly Text[] commanders=new Text[3];
            public ArmyColumn(RectTransform panel,Font font,bool friendly)
            {
                var card=Region(friendly?"AllySummary":"EnemySummary",panel,friendly?0:.5f,.22f,friendly?.5f:1,.735f,
                    friendly?18:5,0,friendly?-5:-18,0);
                card.gameObject.GetOrAdd<Image>().color=DemoUiTheme.Current.Inset;
                var faction=TextAt("Faction",card,font,22,0,.81f,1,.99f,12,0,-12,0);
                faction.text=friendly?"我方":"敌方";
                faction.fontStyle=FontStyle.Bold;
                totals=TextAt("Strength",card,font,18,0,.64f,1,.82f,8,0,-8,0);
                rows=TextAt("Troops",card,font,16,0,.37f,1,.65f,8,0,-8,0);
                var leaders=Region("Leaders",card,0,.025f,1,.37f,8,0,-8,0);
                for(int i=0;i<commanders.Length;i++)
                    commanders[i]=TextAt("Captain"+i,leaders,font,16,0,0,1,1,0,0,0,0);
            }

            public void Show(ArmyBattleReport army,string batuOutcome=null)
            {
                if(army==null){totals.text=rows.text="";foreach(var label in commanders)label.text="";return;}
                totals.text=$"队长 {army.Commanders.Count}    士兵 {army.Deployed}";
                int deaths=army.Troops.Sum(t=>t.InstantKilled);
                rows.text=$"留场 {army.Standing}    撤离 {army.Escaped}\n倒地 {army.Killed-deaths}    阵亡 {deaths}";
                for(int i=0;i<commanders.Length;i++)
                {
                    var label=commanders[i];
                    if(i>=army.Commanders.Count){label.text="";continue;}
                    var hero=army.Commanders[i];
                    float top=.5f+(army.Commanders.Count*.5f-i)/commanders.Length;
                    label.rectTransform.anchorMin=new Vector2(0,top-1f/commanders.Length);
                    label.rectTransform.anchorMax=new Vector2(1,top);
                    label.color=hero.Recruited?new Color32(139,81,47,255):DemoUiTheme.Current.Ink;
                    label.text=$"{hero.Name} · {(hero.Slot==0&&batuOutcome!=null?batuOutcome:hero.Status)}";
                }
            }
        }

        public void Close(){if(owner?.CampaignGameOver==true)return;dismissed=true;backdrop.gameObject.SetActive(false);}
        public void Open()=>dismissed=false;
        public void Refresh(BattleSimulation battle,BattleSide observer,bool animating,bool skipping,Vector2 available)
        {
            var report=battle.Report;
            bool changed=report!=previous;
            if(changed){previous=report;dismissed=false;lastCaptives=null;}
            bool visible=battle.Phase==BattlePhase.Finished&&report!=null&&!animating&&!skipping&&!dismissed&&owner?.ContinuesThroughDialogue!=true;
            bool opening=visible&&!Visible;
            if(Visible!=visible)backdrop.gameObject.SetActive(visible);
            if(!visible)return;
            panel.sizeDelta=new Vector2(Mathf.Min(830,Mathf.Max(240,available.x-32)),Mathf.Min(470,Mathf.Max(220,available.y-24)));
            bool campaign=owner!=null&&owner.IsCampaignBattle, ended=campaign&&owner.CampaignGameOver;
            viewBoard.gameObject.SetActive(!ended);returnBase.gameObject.SetActive(campaign&&!ended);
            returnBase.interactable=campaign&&owner.CanReturnCampaign;
            loadGame.gameObject.SetActive(ended);mainMenu.gameObject.SetActive(ended);
            var captives=owner?.CampaignCaptives??"";
            if(changed||opening||observer!=previousObserver||lastCaptives!=captives)
            {
                previousObserver=observer;lastCaptives=captives;
                title.text=ended?"游戏结束":owner?.IsSceneEncounter==true?owner.SceneEncounterResult.Split('·')[0].Trim():BattleSceneController.OutcomeName(report.Outcome);
                var run=owner?.Realtime;
                string duration=run!=null?"用时 "+CombatNumbers.Duration(run.Model.Elapsed):$"{report.Rounds} 回合";
                int supply=run?.Session.Opening.SupplySnapshot?.Equipment?.Where(e=>e.Product=="supply").Sum(e=>e.Count)??0;
                metrics.text=$"{duration}     下令 {report.CommandsIssued} 次     技能 {report.AbilitiesUsed} 次"+(supply>0?$"     军需 {supply} 件":"");
                defenseSettlement=owner?.IsDemoDefense==true&&!owner.IsSecondDefense
                    ?owner.FirstDefenseSettlement(report.Outcome!=BattleOutcome.SideAWin):"";
                bool firstDefenseVictory=owner?.IsDemoDefense==true&&!owner.IsSecondDefense&&report.Outcome==BattleOutcome.SideAWin;
                string batuOutcome=firstDefenseVictory?(BaseDemoDefense.BatuFell(battle)?"战死":"逃脱"):null;
                own.Show(report.Armies.FirstOrDefault(a=>a.Side==observer),observer==BattleSide.B?batuOutcome:null);
                opposing.Show(report.Armies.FirstOrDefault(a=>a.Side!=observer),observer==BattleSide.A?batuOutcome:null);
                returnBase.GetComponentInChildren<Text>(true).text=owner?.IsBanditHideout==true?BaseBanditHideout.ReturnText
                    :owner?.IsDemoAttack==true&&report.Outcome==BattleOutcome.SideAWin?"结算并留在当地"
                    :report.Outcome==BattleOutcome.SideAWin?"结算并返回营地":"结算并撤退";
            }
            string feedback=owner?.Feedback??"";
            note.text=feedback.StartsWith("战果尚未保存")||feedback.StartsWith("尚未返回")?feedback:captives.Length>0?"俘虏　"+captives:"";
            if(!string.IsNullOrEmpty(defenseSettlement) && !feedback.StartsWith("战果尚未保存")&&!feedback.StartsWith("尚未返回"))
                note.text=defenseSettlement+(captives.Length>0?"　俘虏 "+captives:"");
            backdrop.SetAsLastSibling();
        }

        private static RectTransform Region(string id,Transform parent,float x0,float y0,float x1,float y1,float left,float bottom,float right,float top)
        {
            var rect=UiPrefabBinding.Rect(id,parent);
            rect.anchorMin=new Vector2(x0,y0);rect.anchorMax=new Vector2(x1,y1);
            rect.offsetMin=new Vector2(left,bottom);rect.offsetMax=new Vector2(right,top);
            return rect;
        }
        private static Text TextAt(string id,Transform parent,Font font,int size,float x0,float y0,float x1,float y1,float left,float bottom,float right,float top)
        {
            var label=Region(id,parent,x0,y0,x1,y1,left,bottom,right,top).gameObject.GetOrAdd<Text>();
            label.font=font;label.fontSize=size;label.alignment=TextAnchor.MiddleCenter;
            label.color=DemoUiTheme.Current.Ink;label.raycastTarget=false;label.supportRichText=false;
            label.resizeTextForBestFit=true;label.resizeTextMinSize=12;label.resizeTextMaxSize=size;
            return label;
        }
    }
}

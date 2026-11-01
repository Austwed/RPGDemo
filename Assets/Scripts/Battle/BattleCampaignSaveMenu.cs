using BaseConquest.Presentation;
using System;
using System.Linq;
using BaseConquest.Base;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
namespace BaseConquest.Battle
{
    public sealed partial class BattleSceneController
    {
        private GameObject campaignMenu;
        private Font saveMenuFont;
        public void OpenCampaignSaves(bool loading=false)
        {
            if(IsRealtimeCampaign)loading=true;
            battleBagOpen=false;autoRun=false;if(Application.isPlaying)PersistCampaignCheckpoint(true);
            if(campaignMenu!=null)UiPrefabBinding.DestroyView(campaignMenu);
            if(saveMenuFont==null)saveMenuFont=Font.CreateDynamicFontFromOSFont(new[] {"Microsoft YaHei","Arial"},18);
            campaignMenu=UiPrefabBinding.Create("BattleSaves","战役存读档",transform,true);campaignMenu.GetComponent<Canvas>().renderMode=RenderMode.ScreenSpaceOverlay;campaignMenu.GetComponent<Canvas>().sortingOrder=3000;
            var scaler=campaignMenu.GetComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;scaler.referenceResolution=new Vector2(1280,720);
            RectTransform Box(string name,float y,float height){var obj=UiPrefabBinding.Rect(name,campaignMenu.transform).gameObject;var r=obj.GetComponent<RectTransform>();obj.GetOrAdd<Image>();r.anchorMin=r.anchorMax=new Vector2(.5f,.5f);r.sizeDelta=new Vector2(900,height);r.anchoredPosition=new Vector2(0,y);obj.GetComponent<Image>().color=new Color(.08f,.14f,.18f,.98f);return r;}
            void Button(string text,float x,float y,float width,Action click,bool enabled=true){var r=Box(text,y,43);r.sizeDelta=new Vector2(width,43);r.anchoredPosition=new Vector2(x,y);var b=r.gameObject.GetOrAdd<Button>();b.interactable=enabled;b.onClick.AddListener(()=>click());var t=UiPrefabBinding.Rect("Label",r).gameObject;t.GetOrAdd<Text>();var tr=t.GetComponent<RectTransform>();tr.anchorMin=Vector2.zero;tr.anchorMax=Vector2.one;tr.offsetMin=tr.offsetMax=Vector2.zero;var label=t.GetComponent<Text>();label.font=saveMenuFont;label.fontSize=18;label.color=Color.white;label.alignment=TextAnchor.MiddleCenter;label.text=text;label.raycastTarget=false;}
            Box("遮罩",0,700);
            Button(IsRealtimeCampaign?"战中不存档":loading ? "切换到保存" : "切换到读档",-280,300,250,()=>OpenCampaignSaves(!loading),!IsRealtimeCampaign&&!CampaignGameOver);
            Button("返回战斗",0,300,250,()=>UiPrefabBinding.DestroyView(campaignMenu),!CampaignGameOver);
            Button("回主菜单",280,300,250,CampaignMainMenu);
            var catalog=Application.isPlaying ? BaseBattleGateway.Catalog() : null;var entries=catalog?.Entries()??new System.Collections.Generic.List<BaseSaveEntry>();int row=0;
            foreach(string key in loading ? BaseSaveCatalog.Slots.Concat(BaseSaveCatalog.AutoSlots) : BaseSaveCatalog.Slots)
            {
                string id=key;var entry=entries.FirstOrDefault(e=>e.Id==id);string name=(BaseSaveCatalog.AutoSlots.Contains(id)?"自动 ":"手动 ")+id+" · "+(entry==null ? "空" : entry.Error!=null ? "损坏" : "第"+entry.Day+"天 · "+entry.Name);float y=230-row++*52;
                Button(name,-165,y,550,()=>{},false);
                Button(loading ? "读取" : "保存",215,y,160,()=>{
                    try {
                        if(loading){var next=catalog.LoadSlot(id);if(next.Aftermath.GameOver)throw new InvalidOperationException("此档已结束");BaseBattleGateway.Session().Restore(next);BaseBattleGateway.MainMenu=false;BaseBattleGateway.RequestedId=null;campaignBattleId="";SceneManager.LoadScene(BaseBattleGateway.ExplorationScene);}
                        else { if(entry!=null){Button("确认覆盖此槽",0,-300,460,()=>{catalog.SaveSlot(id,BaseBattleGateway.Store().Load(),"战斗手动存档"+id);OpenCampaignSaves();});return;}catalog.SaveSlot(id,BaseBattleGateway.Store().Load(),"战斗手动存档"+id);OpenCampaignSaves();}
                    }catch(Exception e){feedback="存读档失败，原状态保留："+e.Message;Button(e.Message,0,-300,780,()=>OpenCampaignSaves(loading));}
                },loading ? entry!=null&&entry.Error==null&&!entry.GameOver : !CampaignGameOver);
            }
            Button("选项",-280,-260,240,OpenCampaignOptions,!CampaignGameOver);
            Button("背包 · B",150,-260,560,()=>OpenBattleBackpack(),!CampaignGameOver);
            if(!loading)Button("取消战场准备并返营",0,-190,500,ReturnCampaign,battle.Phase==BattlePhase.Deployment&&!CampaignGameOver);
            UiPrefabBinding.Complete(campaignMenu);
        }
        public void OpenCampaignMenu()=>BuildCampaignMenu(true);
        public void OpenCampaignOptions()=>BuildCampaignMenu(false);
        private void BuildCampaignMenu(bool menu)
        {
            autoRun=false;
            if(saveMenuFont==null)saveMenuFont=Font.CreateDynamicFontFromOSFont(new[]{"Microsoft YaHei","Arial"},18);
            if(campaignMenu!=null){campaignMenu.SetActive(false);UiPrefabBinding.DestroyView(campaignMenu);}
            campaignMenu=UiPrefabBinding.Create("BattleOptions","战斗选项",transform,true);campaignMenu.GetComponent<Canvas>().renderMode=RenderMode.ScreenSpaceOverlay;campaignMenu.GetComponent<Canvas>().sortingOrder=3000;
            var scaler=campaignMenu.GetComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;scaler.referenceResolution=new Vector2(1280,720);
            var shade=UiPrefabBinding.Rect("选项面板",campaignMenu.transform).gameObject;shade.GetOrAdd<Image>();var panel=shade.GetComponent<RectTransform>();panel.anchorMin=panel.anchorMax=new Vector2(.5f,.5f);panel.sizeDelta=new Vector2(900,480);shade.GetComponent<Image>().color=new Color(.08f,.14f,.18f,.98f);
            void Option(string title,float y,Action action,bool enabled=true)
            {
                var obj=UiPrefabBinding.Rect(title,panel).gameObject;obj.GetOrAdd<Image>();obj.GetOrAdd<Button>();var r=obj.GetComponent<RectTransform>();r.anchorMin=r.anchorMax=new Vector2(.5f,.5f);r.anchoredPosition=new Vector2(0,y);r.sizeDelta=new Vector2(800,64);obj.GetComponent<Image>().color=new Color(.2f,.32f,.37f);var button=obj.GetComponent<Button>();button.interactable=enabled;button.targetGraphic=obj.GetComponent<Image>();button.onClick.AddListener(()=>action());
                var label=UiPrefabBinding.Rect("Label",r).gameObject;label.GetOrAdd<Text>();var tr=label.GetComponent<RectTransform>();tr.anchorMin=Vector2.zero;tr.anchorMax=Vector2.one;tr.offsetMin=tr.offsetMax=Vector2.zero;var text=label.GetComponent<Text>();text.font=saveMenuFont;text.fontSize=21;text.alignment=TextAnchor.MiddleCenter;text.color=Color.white;text.text=title;text.raycastTarget=false;
            }
            if(menu)
            {
                if(IsStandaloneTest)Option("重置本预设（不保存剧情）",200,()=>RestartStandaloneTest?.Invoke());
                Option("选项",110,OpenCampaignOptions,!CampaignGameOver);
                Option("回主菜单",20,CampaignMainMenu);
                if(IsStandaloneTest)Option("退出独立测试（不保存剧情）",-70,()=>AbandonRealtimeBattle());
                else if(IsRealtimeCampaign&&!CampaignGameOver&&!campaignSaved&&!IsSecondDefense)Option(realtime?.Session.Opening.DemoCombat==true?"立即撤出，结算战损":"放弃本场，返回战前",-70,()=>AbandonRealtimeBattle());
                else if(battle?.Phase==BattlePhase.Deployment)Option("取消战场准备并返营",-70,ReturnCampaign,CanReturnCampaign);
                Option("返回战斗",-160,()=>UiPrefabBinding.DestroyView(campaignMenu),!CampaignGameOver);
            }
            else
            {
                Option("返回菜单",110,OpenCampaignMenu);
                Option("战斗格子描边："+(MapDisplayOptions.BattleGridOutlines ? "开启" : "关闭"),15,()=>{MapDisplayOptions.BattleGridOutlines=!MapDisplayOptions.BattleGridOutlines;boardView.SetGridOutlines(MapDisplayOptions.BattleGridOutlines);OpenCampaignOptions();});
                Option("开发者模式："+(RuntimeDeveloperOptions.Enabled ? "开启" : "关闭"),-75,()=>{RuntimeDeveloperOptions.Enabled=!RuntimeDeveloperOptions.Enabled;OpenCampaignOptions();});
                Option("返回战斗",-160,()=>UiPrefabBinding.DestroyView(campaignMenu));
            }
            UiPrefabBinding.Complete(campaignMenu);
        }
    }
}

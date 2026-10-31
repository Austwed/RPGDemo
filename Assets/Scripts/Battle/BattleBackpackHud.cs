using BaseConquest.Presentation;
using System;
using System.Linq;
using BaseConquest.Base;
using UnityEngine;
using UnityEngine.UI;
namespace BaseConquest.Battle
{
    public sealed partial class BattleSceneController
    {
        private bool battleBagOpen;
        private bool battleJournalOpen;
        private bool preparationInventoryOpen;
        public void OpenBattleQuests()
        {
            if(IsSceneEncounter){ManualPause();feedback=SceneEncounterObjective+(IsDemoAttack?"：击败驻军，处理完战果后开放"+BaseScriptNames.Village+"道路；队长倒地不立即判负，撤退保留双方战损。":IsDemoDefense?(sceneEncounter.Opening.EventId==BaseDemoDefense.SecondEventId?"：守住时限或击退敌军；失败扣一次经济损失，不再有第三波。":"：守住时限后敌军撤退，可继续追击；失败扣经济损失后残军次日再袭。"):IsDemoRuins?"：击败控制来源，受控村民可制服，不会误杀。":"：先掩护村民离开，击倒一名追兵并重创另一名。");return;}
            if(!IsCampaignBattle||CampaignGameOver)return;autoRun=false;skipping=false;CancelGesture();PersistCampaignCheckpoint(true);
            try{BuildBattleBackpack(BaseBattleGateway.Store().Load(),"quests",null);}catch(Exception e){feedback="任务读取失败："+e.Message;}
        }
        private void CloseBattleBackpack(){battleBagOpen=false;if(campaignMenu!=null){campaignMenu.SetActive(false);Destroy(campaignMenu);}}
        public void OpenBattleBackpack(string category="all",string itemId=null)
        {
            if(IsSceneEncounter){ManualPause();feedback=IsDemoRuins||IsDemoDefense||IsDemoAttack?"交战中不使用和平背包，军需与装备已按战前状态锁定":"教学遭遇使用临时作战数据，不访问营地背包";return;}
            if(!IsCampaignBattle||CampaignGameOver)return;
            if(battle.Phase!=BattlePhase.Deployment){feedback="战斗中不能打开背包";return;}
            var owner=GetComponentInParent<BaseSceneController>();if(owner==null){feedback="此独立战场无冒险小队背包";return;}
            autoRun=false;skipping=false;CancelGesture();PersistCampaignCheckpoint(true);
            try{preparationInventoryOpen=true;owner.OpenPreparationInventory(()=>{preparationInventoryOpen=false;var state=BaseBattleGateway.Store().Load();campaignSession=new BaseCampaignSession(state.Campaign.ActiveBattle,sharedMapSession);ReplaceSimulation(campaignSession.Battle.Scenario,campaignSession.Battle);feedback="已更新准备装备，保留部署位置与当前生命";});}
            catch(Exception e){preparationInventoryOpen=false;feedback="背包读取失败："+e.Message;}
        }
        public void PreviewBattleBackpack(BaseState sample)
        {if(Application.isPlaying)throw new InvalidOperationException("仅供编辑预览");BuildBattleBackpack(sample,"all",null);}
        private void BuildBattleBackpack(BaseState s,string category,string itemId)
        {
            if(campaignMenu!=null){campaignMenu.SetActive(false);UiPrefabBinding.DestroyView(campaignMenu);}
            battleBagOpen=true;battleJournalOpen=category=="quests";
            if(saveMenuFont==null)saveMenuFont=Font.CreateDynamicFontFromOSFont(new[]{"Microsoft YaHei","Arial"},18);
            campaignMenu=UiPrefabBinding.Create("BattleBackpack","战斗背包",transform,true);var canvas=campaignMenu.GetComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceOverlay;canvas.sortingOrder=3000;
            var scale=campaignMenu.GetComponent<CanvasScaler>();scale.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;scale.referenceResolution=new Vector2(1280,720);scale.screenMatchMode=CanvasScaler.ScreenMatchMode.Expand;
            RectTransform Box(Transform parent,string name,Vector2 position,Vector2 size,Color color,Vector2? anchor=null){var o=UiPrefabBinding.Rect(name,parent).gameObject;var r=o.GetComponent<RectTransform>();o.GetOrAdd<Image>();r.anchorMin=r.anchorMax=anchor??Vector2.one*.5f;r.anchoredPosition=position;r.sizeDelta=size;o.GetComponent<Image>().color=color;return r;}
            void Label(Transform parent,string text,Vector2 pos,Vector2 size,int fontSize=20,Vector2? anchor=null){var o=UiPrefabBinding.Rect("Label",parent).gameObject;var r=o.GetComponent<RectTransform>();o.GetOrAdd<Text>();r.anchorMin=r.anchorMax=anchor??Vector2.one*.5f;r.anchoredPosition=pos;r.sizeDelta=size;var t=o.GetComponent<Text>();t.font=saveMenuFont;t.fontSize=fontSize;t.color=Color.white;t.alignment=TextAnchor.MiddleLeft;t.text=text;t.raycastTarget=false;}
            void Button(Transform parent,string title,Vector2 pos,Vector2 size,Action click,Vector2? anchor=null){var r=Box(parent,title,pos,size,new Color(.22f,.34f,.36f),anchor);var b=r.gameObject.GetOrAdd<Button>();b.onClick.AddListener(()=>click());Label(r,title,Vector2.zero,size-Vector2.one*8,18);}
            var shield=Box(campaignMenu.transform,"全屏遮罩",Vector2.zero,Vector2.zero,new Color(0,0,0,.65f));shield.anchorMin=Vector2.zero;shield.anchorMax=Vector2.one;shield.offsetMin=shield.offsetMax=Vector2.zero;
            var panel=Box(campaignMenu.transform,"背包",Vector2.zero,new Vector2(960,600),new Color(.10f,.17f,.18f));
            Label(panel,battleJournalOpen ? "任务 · 战斗中只查看" : "背包 · 战斗中只查看",new Vector2(-90,260),new Vector2(660,48),26);Button(panel,battleJournalOpen ? "关闭 · N / Esc" : "关闭 · B / Esc",new Vector2(350,260),new Vector2(200,48),CloseBattleBackpack);
            var viewport=Box(panel,"列表",new Vector2(0,-25),new Vector2(900,490),new Color(.10f,.17f,.18f));viewport.gameObject.GetOrAdd<RectMask2D>();
            var content=UiPrefabBinding.Rect("内容",viewport);content.gameObject.GetOrAdd<UiDynamicContent>();content.GetComponent<UiVisualNode>().UseAuthoredLayout=false;content.anchorMin=content.anchorMax=new Vector2(.5f,1);content.pivot=new Vector2(.5f,1);content.sizeDelta=new Vector2(880,490);
            var scroll=viewport.gameObject.GetOrAdd<ScrollRect>();scroll.viewport=viewport;scroll.content=content;scroll.horizontal=false;scroll.movementType=ScrollRect.MovementType.Clamped;
            float row=0;var top=new Vector2(.5f,1);var bstate=s.Backpack;
            var selected=bstate.Items.FirstOrDefault(i=>i.Id==itemId);
            if(battleJournalOpen)
            {
                Label(content,BaseQuests.Title+"\n状态："+BaseQuests.Status(s)+"\n\n目标：亲自赢得迎战或进攻胜利，或占领荒野据点。\n奖励：营造图册×1与营造入门剧情条件。\n\n接取和交付需要回城镇，接近镇务官交互；战斗中只能查看。",new Vector2(0,-165),new Vector2(830,330),22,top);row=345;
            }
            else if(selected!=null)
            {
                var d=bstate.Catalog.Single(x=>x.Id==selected.ItemId);Button(content,"返回物品列表",new Vector2(0,-28),new Vector2(830,48),()=>OpenBattleBackpack(category),top);row=72;
                Label(content,d.Name+" ×"+selected.Count+" · "+BaseBackpack.CategoryName(d.Category),new Vector2(0,-row-28),new Vector2(830,56),23,top);row+=70;
                Label(content,d.Description,new Vector2(0,-row-110),new Vector2(830,220),21,top);row+=235;
                if(!string.IsNullOrEmpty(d.ExclusiveLeaderId)){Label(content,"专属角色："+s.Leaders.Single(l=>l.Id==d.ExclusiveLeaderId).Name,new Vector2(0,-row-25),new Vector2(830,50),20,top);row+=60;}
            }
            else
            {
                var categories=new[]{"all"}.Concat(BaseBackpack.Categories).ToArray();
                for(int i=0;i<categories.Length;i++){string id=categories[i];Button(content,(id==category ? "✓ " : "")+BaseBackpack.CategoryName(id),new Vector2(i<4 ? -315+i*210 : -290+(i-4)*290,-row-26),new Vector2(i<4 ? 200 : 270,48),()=>OpenBattleBackpack(id),top);if(i==3)row+=60;}row+=64;
                var items=bstate.Items.Where(i=>category=="all"||bstate.Catalog.Single(d=>d.Id==i.ItemId).Category==category).ToArray();
                if(items.Length==0){Label(content,"该分类暂无物品。",new Vector2(0,-row-30),new Vector2(830,60),22,top);row+=70;}
                foreach(var item in items){string id=item.Id;var d=bstate.Catalog.Single(x=>x.Id==item.ItemId);Button(content,d.Name+" ×"+item.Count+" · "+BaseBackpack.CategoryName(d.Category),new Vector2(0,-row-26),new Vector2(830,48),()=>OpenBattleBackpack(category,id),top);row+=59;}
            }
            Label(content,"粮草、战略原料、钱币与量产军需另存库存。战斗中不能升级、建造、穿戴或使用背包物品。",new Vector2(0,-row-55),new Vector2(830,110),19,top);row+=120;content.sizeDelta=new Vector2(880,Mathf.Max(490,row));UiPrefabBinding.Complete(campaignMenu);
        }
    }
}

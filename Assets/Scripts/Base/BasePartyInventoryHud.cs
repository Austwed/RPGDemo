using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using BaseConquest.Battle;
using BaseConquest.Presentation;
namespace BaseConquest.Base
{
    public sealed partial class BaseHud
    {
        private bool inventoryWarehouse;
        private readonly InventoryClickTracker inventoryClicks=new InventoryClickTracker();
        private string inventoryPerson="",inventorySelected="";
        private int inventoryQuantity=1,warehousePage;
        private readonly Dictionary<string,string> inventoryFilters=new Dictionary<string,string>();
        private static readonly string[] InventoryCategories={"all","weapon","armor","accessory","consumable","quest","material","exclusive"};
        private string InventoryFilter(string holder)=>inventoryFilters.TryGetValue(holder,out var value)?value:"all";
        public void OpenWarehouse(){if(BasePartyInventory.AtWarehouse(owner.State))OpenInventory(true);else{owner.ReportMessage("请接近基地仓库交互");}}
        private void OpenInventory(bool warehouse)
        {
            var s=owner.State;if(BaseDemoIntro.Daily(s)&&s.Demo.Intro.Step==0){OpenHeroChoice();return;}if(s==null||!BasePartyInventory.Peaceful(s)){owner.ReportMessage("战斗中不能打开背包");return;}
            if(warehouse&&BaseBattleLoot.Pending(s)){OpenLoot();return;}
            if(warehouse&&!BasePartyInventory.AtWarehouse(s)){owner.ReportMessage("请接近基地仓库");return;}
            if(inventoryWarehouse!=warehouse||!PanelOpen)inventoryClicks.Reset();
            inventoryWarehouse=warehouse;activeObject=warehouse?"warehouse":"backpack";interactionObject=warehouse?"warehouse":"";
            var people=BasePartyInventory.People(s,warehouse).Where(l=>l.PersonalEquipment?.Version==1).ToArray();if(people.Length==0)return;
            if(!people.Any(l=>l.Id==inventoryPerson))inventoryPerson=people[0].Id;
            if(panel!=null){ClearPanelSelection();panel.gameObject.SetActive(false);UiPrefabBinding.DestroyView(panel.gameObject);}panelScroll=null;panelViewKey=null;
            modal.gameObject.SetActive(true);Joystick.ResetInput();Joystick.gameObject.SetActive(false);
            root.GetComponent<Canvas>().sortingOrder=100;
            string key=warehouse?"Page_party_warehouse":"Page_party_inventory";
            float panelWidth=warehouse?1220:Mathf.Min(1220,Mathf.Max(440,people.Length*400+40));
            panel=UiPrefabBinding.Create(key,warehouse?"基地仓库与人物":"当前小队背包",modal,false,owner.PageTemplate(key)).GetComponent<RectTransform>();panel.anchorMin=panel.anchorMax=panel.pivot=Vector2.one*.5f;panel.anchoredPosition=Vector2.zero;panel.sizeDelta=new Vector2(panelWidth,680);DemoUiTheme.Current.Surface(panel.gameObject.GetOrAdd<Image>());
            MakeButton(panel,"关闭",new Vector2(Mathf.Min(panelWidth/2+62,572),228),new Vector2(100,36),ClosePanel);
            if(warehouse)
            {
                float width=Math.Min(140,520f/people.Length);
                for(int i=0;i<people.Length;i++){var person=people[i];var b=MakeButton(panel,(person.Id==inventoryPerson?"● ":"")+person.Name,new Vector2(-586+width/2+i*width,228),new Vector2(width-5,40),()=>{inventoryClicks.Reset();inventoryPerson=person.Id;inventorySelected="";OpenInventory(true);});var tab=b.GetComponentInChildren<Text>();tab.color=person.Recruited?DemoUiTheme.Current.Copper:ink;tab.fontStyle=person.Recruited?FontStyle.Bold:FontStyle.Normal;}
                InventoryCharacter(people.Single(l=>l.Id==inventoryPerson),new Vector2(-342,-65),new Vector2(510,540));
                var store=Box("仓库物品与分类",panel,Vector2.one*.5f,new Vector2(260,94),new Vector2(660,294),new Color(DemoUiTheme.Current.Inset.r,DemoUiTheme.Current.Inset.g,DemoUiTheme.Current.Inset.b,.74f));
                store.GetComponent<UiVisualNode>().UseAuthoredLayout=false;store.gameObject.GetOrAdd<UiDynamicContent>();
                Label(store,"基地仓库 · 不限格数",new Vector2(-150,119),new Vector2(340,32),21);InventoryTools(store,"",new Vector2(178,119));
                var entries=BasePartyInventory.Entries(s).Where(e=>!e.Worn&&e.Holder=="").ToArray();int pages=Math.Max(1,(entries.Length+23)/24);warehousePage=Mathf.Clamp(warehousePage,0,pages-1);
                for(int n=0;n<24;n++){var e=entries.Skip(warehousePage*24+n).FirstOrDefault();InventoryCell(store,e,"",warehousePage*24+n,"",new Vector2(-265+(n%6)*106,72-(n/6)*52),new Vector2(100,46));}
                MakeButton(store,"上一页",new Vector2(-220,-126),new Vector2(140,32),()=>{inventoryClicks.Reset();warehousePage--;OpenInventory(true);}).interactable=warehousePage>0;
                Label(store,(warehousePage+1)+" / "+pages,new Vector2(0,-126),new Vector2(120,30),17,TextAnchor.MiddleCenter);
                MakeButton(store,"下一页",new Vector2(220,-126),new Vector2(140,32),()=>{inventoryClicks.Reset();warehousePage++;OpenInventory(true);}).interactable=warehousePage+1<pages;
                var details=Box("右侧物品描述与操作",panel,Vector2.one*.5f,new Vector2(260,-175),new Vector2(660,274),new Color(DemoUiTheme.Current.Inset.r,DemoUiTheme.Current.Inset.g,DemoUiTheme.Current.Inset.b,.74f));InventoryDetails(details,true);
            }
            else
            {
                // One continuous party row, including on touch devices. No character-switch tabs.
                var viewport=Rect("PartyViewport",panel,Vector2.one*.5f,new Vector2(0,-65),new Vector2(panelWidth-30,540));
                viewport.gameObject.GetOrAdd<RectMask2D>();viewport.gameObject.GetOrAdd<Image>().color=new Color(0,0,0,.01f);
                float total=people.Length*390f;
                var party=Rect("PartyRow",viewport,new Vector2(.5f,.5f),Vector2.zero,new Vector2(Math.Max(panelWidth-30,total),540));

                var scroll=viewport.gameObject.GetOrAdd<ScrollRect>();scroll.viewport=viewport;scroll.content=party;scroll.horizontal=true;scroll.vertical=false;scroll.movementType=ScrollRect.MovementType.Clamped;
                for(int n=0;n<people.Length;n++)InventoryCharacter(people[n],new Vector2(-total/2+195+n*390,0),new Vector2(380,540),party);
            }
            if(BaseDemoIntro.Daily(s))s.Demo.Intro.BackpackOpened=true;
            panelFeedback=null;
            if(!warehouse&&!string.IsNullOrEmpty(inventorySelected))InventoryItemMenu(panel,panelWidth);
            CompletePreservingLayout(panel);RefreshSummary();
        }
        // 版式照原型 `renderInventoryPanel`：顶部姓名＋生命、中部「左三槽／全身像／右三槽」方形格、
        // 下方容量行＋整理·详情、底部 6×4 方形背包格。第六槽只在 ExtraAccessory 解锁后可用，否则显示「锁定」。
        // 版式照原型 `renderInventoryPanel`：顶部姓名＋生命、中部「左三槽／竖矩形全身像／右三槽」、
        // 下方容量行＋整理·详情、底部 6×4 方形背包格。第六槽只在 ExtraAccessory 解锁后可用。
        private void InventoryCharacter(BaseLeader l,Vector2 position,Vector2 size,RectTransform parent=null)
        {
            var card=UiPrefabBinding.Create("PartyMemberCard","人物与背包_"+l.Id,parent??panel).GetComponent<RectTransform>();card.GetComponent<UiVisualNode>().UseAuthoredLayout=false;card.anchorMin=card.anchorMax=card.pivot=Vector2.one*.5f;card.anchoredPosition=position;card.sizeDelta=size;var cardImage=card.gameObject.GetOrAdd<Image>();cardImage.color=DemoUiTheme.Current.Inset;var cardOutline=cardImage.GetComponent<Outline>()??cardImage.gameObject.AddComponent<Outline>();cardOutline.effectColor=DemoUiTheme.Current.Line;cardOutline.effectDistance=new Vector2(1,-1);float top=size.y/2;
            // 名字是纯文字，不套按钮底：挂一个无 Image 的 Button 保留点击切人，主题 Apply 只会给有 Image 的按钮换肤。
            var nameRect=Rect("人物名",card,Vector2.one*.5f,new Vector2(-size.x/2+12+120,top-24),new Vector2(240,30));
            var backdrop=nameRect.GetComponent<Image>();if(backdrop!=null)backdrop.enabled=false;
            var backdropOutline=nameRect.GetComponent<Outline>();if(backdropOutline!=null)backdropOutline.enabled=false;
            Label(nameRect,BasePersonnel.LeaderName(l)+" · "+BasePersonalEquipment.ClassName(l),Vector2.zero,new Vector2(240,30),DemoUiMetrics.FontSubhead,TextAnchor.MiddleLeft);
            var nameButton=nameRect.gameObject.GetOrAdd<Button>();nameButton.targetGraphic=null;nameButton.transition=Selectable.Transition.None;
            nameButton.onClick.AddListener(()=>{inventoryPerson=l.Id;inventorySelected="";OpenInventory(inventoryWarehouse);});
            BindInventoryDrop(nameRect.gameObject,null,l.Id,-2,"");
            var stat=Label(card,"生命 "+l.Health.ToString("0.#")+" / "+l.MaxHealth.ToString("0.#"),new Vector2(size.x/2-12-70,top-24),new Vector2(140,30),DemoUiMetrics.FontCaption,TextAnchor.MiddleRight);
            stat.color=DemoUiTheme.Current.Muted;

            const float gear=48f,gearGap=4f,gearTop=52f;
            float equipmentHeight=3*gear+2*gearGap;
            bool extra=BasePersonalEquipment.SlotsFor(l).Count()>=6;
            for(int n=0;n<6;n++)
            {
                string slot=BasePersonalEquipment.Slots[n];bool left=n<3;int line=n%3;
                float x=left?-size.x/2+12+gear/2:size.x/2-12-gear/2;
                float y=top-gearTop-gear/2-line*(gear+gearGap);
                if(n==5&&!extra)
                {
                    var locked=MakeButton(card,"EquipmentSlot_locked",new Vector2(x,y),new Vector2(gear,gear),()=>{});
                    locked.interactable=false;var lockedText=locked.GetComponentInChildren<Text>();lockedText.text=BasePersonalEquipment.SlotName(slot)+"\n锁定";lockedText.fontSize=DemoUiMetrics.FontCaption-1;
                    continue;
                }
                InventoryCell(card,BasePartyInventory.Entries(owner.State).SingleOrDefault(e=>e.Holder==l.Id&&e.Slot==slot),l.Id,-1,slot,new Vector2(x,y),new Vector2(gear,gear));
            }
            // 竖矩形相框里放四向图集的正面全身帧（不是 Portraits 的半身头像）。
            var frame=Box("人物形象",card,Vector2.one*.5f,new Vector2(0,top-gearTop-equipmentHeight/2),new Vector2(112,equipmentHeight),new Color(DemoUiTheme.Current.Inset.r,DemoUiTheme.Current.Inset.g,DemoUiTheme.Current.Inset.b,.55f));
            // 模板的「角色立绘」节点自带 BattleAvatarGraphic，同一 GameObject 不能再挂 RawImage，故用新节点名。
            var art=Rect("角色全身像",frame,Vector2.one*.5f,Vector2.zero,new Vector2(112,112));
            string artId=l.Id==owner.State.PlayerLeaderId?"player":BaseCommanderIdentity.Appearance(l);
            var atlas=PixelDemoArt.Load("Characters",artId);
            if(atlas!=null){var old=art.GetComponent<BattleAvatarGraphic>();if(old!=null)UiPrefabBinding.DestroyView(old);var raw=art.gameObject.GetOrAdd<RawImage>();raw.texture=atlas;raw.uvRect=PixelDemoArt.Frame(HeroFacing.South,HeroPose.Idle,0);raw.raycastTarget=false;}
            else{var fallback=art.gameObject.GetOrAdd<BattleAvatarGraphic>();fallback.PlayerHero=l.Id==owner.State.PlayerLeaderId;fallback.AppearanceId=artId;fallback.raycastTarget=false;}

            float captionY=top-gearTop-equipmentHeight-24;
            int held=BasePartyInventory.Entries(owner.State).Count(e=>e.Holder==l.Id&&!e.Worn);
            Label(card,"背包 "+held+"/"+BasePartyInventory.Capacity,new Vector2(-size.x/2+12+55,captionY),new Vector2(110,26),DemoUiMetrics.FontCaption,TextAnchor.MiddleLeft);
            MakeButton(card,"分类 ▾",new Vector2(15,captionY),new Vector2(62,26),()=>InventoryPopup(card,l.Id,Vector2.zero)).GetComponentInChildren<Text>().fontSize=DemoUiMetrics.FontCaption;
            MakeButton(card,"整理",new Vector2(81,captionY),new Vector2(62,26),()=>InventoryChange(s=>BasePartyInventory.Sort(s,l.Id),"已整理物品")).GetComponentInChildren<Text>().fontSize=DemoUiMetrics.FontCaption;
            MakeButton(card,"详情",new Vector2(147,captionY),new Vector2(62,26),()=>{inventoryPerson=l.Id;inventorySelected="";OpenPersonnelDetail("leader:"+l.Id,BaseMuster.Snapshot(owner.State),()=>OpenInventory(inventoryWarehouse));}).GetComponentInChildren<Text>().fontSize=DemoUiMetrics.FontCaption;

            // 格子按卡片底边对齐，往下填满整块下半区；卡片高度变化时无需重排。
            const float cell=62f,cellGap=4f;float gridBottom=-size.y/2+28;float gridTop=gridBottom+4*cell+3*cellGap;
            for(int n=0;n<BasePartyInventory.Capacity;n++){int index=n;var e=BasePartyInventory.Entries(owner.State).SingleOrDefault(i=>i.Holder==l.Id&&!i.Worn&&i.Cell==index);
                float x=-2f*(cell+cellGap)+(n%5)*(cell+cellGap),y=gridTop-cell/2-(n/5)*(cell+cellGap);
                InventoryCell(card,e,l.Id,index,"",new Vector2(x,y),new Vector2(cell,cell));}
            CompletePreservingLayout(card);
        }
        // 自建面板不走 EndPanel；Complete 会按模板重排动态子节点的坐标与字色，收尾前后各留一份快照。
        private static void CompletePreservingLayout(RectTransform root)
        {
            var rects=root.GetComponentsInChildren<RectTransform>(true);
            Vector2[] mins=new Vector2[rects.Length],maxs=new Vector2[rects.Length],pivots=new Vector2[rects.Length],positions=new Vector2[rects.Length],sizes=new Vector2[rects.Length];
            for(int i=0;i<rects.Length;i++){mins[i]=rects[i].anchorMin;maxs[i]=rects[i].anchorMax;pivots[i]=rects[i].pivot;positions[i]=rects[i].anchoredPosition;sizes[i]=rects[i].sizeDelta;}
            var texts=root.GetComponentsInChildren<Text>(true);
            var textColors=new Color[texts.Length];var textAnchors=new TextAnchor[texts.Length];var textSizes=new int[texts.Length];
            for(int i=0;i<texts.Length;i++){textColors[i]=texts[i].color;textAnchors[i]=texts[i].alignment;textSizes[i]=texts[i].fontSize;}
            var images=root.GetComponentsInChildren<Image>(true);
            var imageColors=new Color[images.Length];
            for(int i=0;i<images.Length;i++)imageColors[i]=images[i].color;
            var outlines=root.GetComponentsInChildren<Outline>(true);
            var outlineColors=new Color[outlines.Length];var outlineDistances=new Vector2[outlines.Length];
            for(int i=0;i<outlines.Length;i++){outlineColors[i]=outlines[i].effectColor;outlineDistances[i]=outlines[i].effectDistance;}
            var known=new HashSet<Outline>(outlines);
            UiPrefabBinding.Complete(root.gameObject);
            foreach(var added in root.GetComponentsInChildren<Outline>(true))if(!known.Contains(added))UiPrefabBinding.DestroyView(added);
            for(int i=0;i<rects.Length;i++)if(rects[i]!=null){rects[i].anchorMin=mins[i];rects[i].anchorMax=maxs[i];rects[i].pivot=pivots[i];rects[i].anchoredPosition=positions[i];rects[i].sizeDelta=sizes[i];}
            for(int i=0;i<texts.Length;i++)if(texts[i]!=null){texts[i].color=textColors[i];texts[i].alignment=textAnchors[i];texts[i].fontSize=textSizes[i];}
            for(int i=0;i<images.Length;i++)if(images[i]!=null)images[i].color=imageColors[i];
            for(int i=0;i<outlines.Length;i++)if(outlines[i]!=null){outlines[i].effectColor=outlineColors[i];outlines[i].effectDistance=outlineDistances[i];}
        }
        private void InventoryTools(RectTransform parent,string holder,Vector2 position)
        {
            MakeButton(parent,BaseBackpack.CategoryName(InventoryFilter(holder))+" ▾",position,new Vector2(132,30),()=>InventoryPopup(parent,holder,position));
            MakeButton(parent,"整理",position+new Vector2(101,0),new Vector2(62,30),()=>InventoryChange(s=>BasePartyInventory.Sort(s,holder),"已整理物品"));
        }
        private void InventoryPopup(RectTransform parent,string holder,Vector2 pos)
        {
            inventoryClicks.Reset();
            var pop=Box("分类选择",panel,Vector2.one*.5f,Vector2.zero,new Vector2(260,382),DemoUiTheme.Current.Panel);pop.SetAsLastSibling();
            for(int i=0;i<InventoryCategories.Length;i++){string id=InventoryCategories[i];MakeButton(pop,BaseBackpack.CategoryName(id),new Vector2(0,161-i*41),new Vector2(244,36),()=>{inventoryFilters[holder]=id;OpenInventory(inventoryWarehouse);});}
            MakeButton(pop,"取消",new Vector2(0,-167),new Vector2(244,36),()=>OpenInventory(inventoryWarehouse));
        }
        private void InventoryCell(RectTransform parent,BaseInventoryEntry e,string holder,int cell,string slot,Vector2 pos,Vector2 size)
        {
            bool shown=e!=null&&(e.Worn||InventoryFilter(holder)=="all"||InventoryFilter(holder)==e.Category);
            string Short(string value,int take)=>string.IsNullOrEmpty(value)?value:value.Length>take?value.Substring(0,take):value;
            // 装备名带 "[品质] " 前缀，方形槽里先剥掉品质再截，避免只剩 "[一" 这类残字。
            string GearShort(string value){if(string.IsNullOrEmpty(value))return value;int close=value.IndexOf(']');if(close>=0&&close+1<value.Length)value=value.Substring(close+1).TrimStart();return Short(value,3);}
            string title=slot!=""?BasePersonalEquipment.SlotName(slot)+"\n"+(shown?GearShort(e.Name):"空"):shown?Short(e.Name,3)+(e.Count>1?" ×"+e.Count:""):e==null?"":"·";
            string key=slot!=""?"EquipmentSlot_"+slot:"BagSlot_"+cell.ToString();
            var button=MakeButton(parent,key,pos,size,()=>{
                bool twice=inventoryClicks.Click(e?.Id,Time.unscaledTime);
                if(twice&&inventoryWarehouse&&slot=="")
                {if(!shown){owner.ReportMessage("此格物品被分类隐藏，请先切换全部");return;}InventoryChange(s=>BasePartyInventory.QuickTransfer(s,e.Id,inventoryPerson,true),"已完成仓库快速存取");return;}
                inventoryPerson=holder==""?inventoryPerson:holder;inventorySelected=shown&&(!inventoryWarehouse&&e.Id==inventorySelected)?"":shown?e.Id:"";inventoryQuantity=e?.Count??1;OpenInventory(inventoryWarehouse);
            });var label=button.GetComponentInChildren<Text>();label.text=title;label.fontSize=slot!=""?DemoUiMetrics.FontCaption-1:DemoUiMetrics.FontCaption;
            if(shown&&e.Id==inventorySelected){var ring=button.GetComponent<Outline>()??button.gameObject.AddComponent<Outline>();ring.effectColor=DemoUiTheme.Current.Copper;ring.effectDistance=new Vector2(2,-2);}
            if(e?.Gear!=null)label.color=e.Quality>=4?DemoUiTheme.Current.Copper:ink;if(!shown&&e!=null)button.GetComponent<Image>().color=new Color(DemoUiTheme.Current.Muted.r,DemoUiTheme.Current.Muted.g,DemoUiTheme.Current.Muted.b,.35f);
            button.gameObject.name=slot!=""?"装备槽_"+holder+"_"+slot:"物品格_"+(holder==""?"仓库":holder)+"_"+(e?.Id??cell.ToString());
            BindInventoryDrop(button.gameObject,shown?e:null,holder,cell,slot,!shown&&e!=null);
        }
        private void BindInventoryDrop(GameObject obj,BaseInventoryEntry e,string holder,int cell,string slot,bool hidden=false)
        {
            var drag=obj.GetOrAdd<PartyInventoryCell>();drag.ItemId=e?.Id;drag.Holder=holder;drag.Cell=cell;drag.Slot=slot;drag.Scope=panel;drag.BeginDrag=inventoryClicks.Reset;
            drag.Move=(id,target,index,gear)=>{if(hidden){owner.ReportMessage("此格物品被分类隐藏，请先切换全部");return;}if(index==-2)index=BasePartyInventory.FreeCell(owner.State,target);InventoryChange(s=>BasePartyInventory.Move(s,id,target,index,gear),"已移动物品");};
        }
        private void InventoryChange(Func<BaseState,bool> action,string message)
        {inventoryClicks.Reset();owner.Change(s=>{if(!action(s))throw new InvalidOperationException("位置、空格、职业或专属条件不允许此操作");},message);OpenInventory(inventoryWarehouse);}
        private void InventoryDetails(RectTransform box,bool vertical,bool compact=false)
        {
            var inner=Rect("物品详情动态内容",box,Vector2.one*.5f,Vector2.zero,box.sizeDelta);inner.gameObject.GetOrAdd<UiDynamicContent>();box=inner;
            var s=owner.State;var e=BasePartyInventory.Find(s,inventorySelected);var l=s.Leaders.Single(p=>p.Id==inventoryPerson);float w=box.sizeDelta.x,h=box.sizeDelta.y;
            if(e==null)return;
            inventoryQuantity=Mathf.Clamp(inventoryQuantity,1,e.Count);
            string description=e.Gear!=null?InventoryGearDescription(e,l):e.Description;
            if(compact)
            {
                Label(box,e.Name+(e.Count>1?" ×"+e.Count:""),new Vector2(-10,h/2-22),new Vector2(w-56,26),DemoUiMetrics.FontSubhead,TextAnchor.MiddleCenter);
                Label(box,"数量 "+e.Count+(e.Gear!=null?" · "+BasePersonalEquipment.SlotName(BasePersonalEquipment.Definition(e.Gear.Template).Slot):""),new Vector2(0,h/2-46),new Vector2(w-20,20),DemoUiMetrics.FontCaption,TextAnchor.MiddleCenter).color=DemoUiTheme.Current.Muted;
                Label(box,description,new Vector2(0,h/2-66-22),new Vector2(w-20,42),DemoUiMetrics.FontCaption,TextAnchor.UpperLeft);
            }
            else
            {
                Label(box,e.Name+(e.Count>1?" ×"+e.Count:""),new Vector2(vertical?0:-420,vertical?h/2-24:25),new Vector2(vertical?w-24:310,36),20);
                float descriptionHeight=Math.Min(180,h-179);
                Label(box,description,new Vector2(vertical?0:-40,vertical?h/2-52-descriptionHeight/2:0),new Vector2(vertical?w-24:480,vertical?descriptionHeight:92),17);
            }
            float x=vertical?-w/2+78:330,y=vertical?-h/2+104:23;
            void Btn(string title,int col,int line,Action click,bool enabled=true)
            {
                if(compact){int index=col+line*2;var b=MakeButton(box,title,new Vector2(index%2==0?-50:50,-16-index/2*30),new Vector2(94,28),click);b.GetComponentInChildren<Text>().fontSize=DemoUiMetrics.FontCaption;b.interactable=enabled;}
                else MakeButton(box,title,new Vector2(x+col*139,y-line*40),new Vector2(132,34),click).interactable=enabled;
            }
            if(e.Gear!=null&&e.Holder!="")Btn(e.Worn?"卸下":"穿戴",0,0,()=>{string slot=e.Worn?e.Slot:BasePersonalEquipment.Definition(e.Gear.Template).Slot;if(slot=="accessory"){var p=s.Leaders.Single(v=>v.Id==e.Holder);slot=BasePersonalEquipment.SlotsFor(p).FirstOrDefault(v=>v.StartsWith("accessory")&&!p.PersonalEquipment.Items.Any(i=>i.Slot==v));}if(slot==null){owner.ReportMessage("请拖动到要替换的饰品槽");return;}InventoryChange(next=>e.Worn?BasePersonalEquipment.Unequip(next,e.Holder,slot):BasePersonalEquipment.Equip(next,e.Holder,e.Id,slot),"已更新装备");},e.Worn||BasePersonalEquipment.Definition(e.Gear.Template).Slot!="weapon"||BasePersonalEquipment.Definition(e.Gear.Template).Weapon==s.Leaders.Single(v=>v.Id==e.Holder).PersonalEquipment.Weapon);
            Btn("销毁",1,0,()=>InventoryDestroyConfirm(e),!e.Worn&&!e.Protected);
            if(inventoryWarehouse)
            {
                Btn(e.Holder==""?"取出给"+l.Name:"存入仓库",0,1,()=>InventoryChange(next=>BasePartyInventory.Move(next,e.Id,e.Holder==""?inventoryPerson:"",e.Holder==""?BasePartyInventory.FreeCell(next,inventoryPerson):-1,"",inventoryQuantity),"已完成仓库存取"),!e.Worn);
                Btn("转交",1,1,()=>InventoryTransferMenu(e),!e.Worn&&string.IsNullOrEmpty(e.Bound));
            }
            bool wine=e.Stack!=null&&new[]{BaseWine.Strong,BaseWine.Sake}.Contains(e.Stack.ItemId);if(wine&&e.Count==1)Btn("使用",0,0,()=>OpenWine(e.Id));
            if(e.Count>1){Btn(wine?"使用／拆分":"拆分",0,0,()=>{if(wine)OpenWine(e.Id);else InventoryChange(next=>BasePartyInventory.Move(next,e.Id,e.Holder,e.Holder==""?-1:BasePartyInventory.FreeCell(next,e.Holder),"",inventoryQuantity),"已拆分物品");},wine||inventoryQuantity<e.Count);int line=inventoryWarehouse?2:1;Btn("数量 −",0,line,()=>{inventoryQuantity=Math.Max(1,inventoryQuantity-1);OpenInventory(inventoryWarehouse);});Btn(inventoryQuantity+" / "+e.Count+" ＋",1,line,()=>{inventoryQuantity=Math.Min(e.Count,inventoryQuantity+1);OpenInventory(inventoryWarehouse);});}
        }
        private string InventoryGearDescription(BaseInventoryEntry e,BaseLeader selected)
        {
            if(BaseRpg.Active(owner.State))return BaseRpg.GearDescription(owner.State,e.Gear)+BaseRpg.GearComparison(owner.State,e.Gear,string.IsNullOrEmpty(e.Holder)?null:e.Holder);
            var d=BasePersonalEquipment.Definition(e.Gear.Template);var l=owner.State.Leaders.FirstOrDefault(p=>p.Id==e.Holder)??selected;int q=e.Quality;
            return BaseBackpack.CategoryName(e.Category)+(d.Slot=="weapon"?" · "+BasePersonalEquipment.Profession(d.Weapon)+"专用":"")+(e.Protected?" · 剧情保护／主角专属":"")+"\n攻击 "+(d.Slot=="weapon"?BasePersonalEquipment.WeaponAttack(l,e.Gear):BasePersonalEquipment.Scale(d.Damage,q)).ToString("+0.##;-0.##;0")+"／防御 "+BasePersonalEquipment.Scale(d.Defense,q).ToString("+0.##;-0.##;0")+"／生命 "+BasePersonalEquipment.Scale(d.Health,q).ToString("+0.##;-0.##;0")+(d.Slot=="weapon"?"／射程 "+BasePersonalEquipment.RealtimeRange(e.Gear)+"细格"+(d.Weapon!=l.PersonalEquipment.Weapon?"\n非本职业武器：无法装备此武器":""):"");
        }
        // 原型 `itemMenu()`：说明与操作只在选中物品后以小浮框出现，不常驻占一块。
        private void InventoryItemMenu(RectTransform parent,float panelWidth)
        {
            var e=BasePartyInventory.Find(owner.State,inventorySelected);if(e==null)return;
            var pop=Box("物品菜单",parent,Vector2.one*.5f,new Vector2(panelWidth/2-125,-175),new Vector2(210,228),DemoUiTheme.Current.Panel);
            pop.SetAsLastSibling();
            var outline=pop.gameObject.GetOrAdd<Outline>();outline.effectColor=DemoUiTheme.Current.Copper;outline.effectDistance=new Vector2(2,-2);
            MakeButton(pop,"关闭",new Vector2(72,96),new Vector2(52,24),()=>{inventorySelected="";OpenInventory(false);}).GetComponentInChildren<Text>().fontSize=DemoUiMetrics.FontCaption;
            InventoryDetails(pop,true,true);
        }
        private void InventoryTransferMenu(BaseInventoryEntry e)
        {
            var people=BasePartyInventory.People(owner.State,true).Where(l=>l.Id!=e.Holder).ToArray();var pop=Box("仓库转交选择",panel,Vector2.one*.5f,Vector2.zero,new Vector2(410,Math.Max(140,people.Length*48+60)),DemoUiTheme.Current.Panel);float y=people.Length*24;
            foreach(var l in people){string id=l.Id;MakeButton(pop,"转交给 "+l.Name,new Vector2(0,y),new Vector2(386,42),()=>InventoryChange(s=>BasePartyInventory.Move(s,e.Id,id,BasePartyInventory.FreeCell(s,id),"",inventoryQuantity),"已转交物品"));y-=48;}
            MakeButton(pop,"取消",new Vector2(0,y),new Vector2(386,42),()=>OpenInventory(true));
        }
        private void InventoryDestroyConfirm(BaseInventoryEntry e)
        {
            var pop=Box("销毁确认",panel,Vector2.one*.5f,Vector2.zero,new Vector2(660,200),DemoUiTheme.Current.Panel);Label(pop,"销毁 "+e.Name+" ×"+inventoryQuantity+"？\n此操作不可恢复。",new Vector2(0,40),new Vector2(610,100),23,TextAnchor.MiddleCenter);
            MakeButton(pop,"确认销毁",new Vector2(-150,-57),new Vector2(270,44),()=>InventoryChange(s=>BasePartyInventory.Destroy(s,e.Id,inventoryQuantity),"已销毁物品"));MakeButton(pop,"取消",new Vector2(150,-57),new Vector2(270,44),()=>OpenInventory(inventoryWarehouse));
        }
    }
}

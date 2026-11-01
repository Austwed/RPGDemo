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
            panel=UiPrefabBinding.Create(key,warehouse?"基地仓库与人物":"当前小队背包",modal,false,owner.PageTemplate(key)).GetComponent<RectTransform>();panel.anchorMin=panel.anchorMax=panel.pivot=Vector2.one*.5f;panel.anchoredPosition=Vector2.zero;panel.sizeDelta=new Vector2(1220,680);DemoUiTheme.Current.Surface(panel.gameObject.GetOrAdd<Image>());
            Label(panel,warehouse?"基地仓库 · 人物在左／仓库在右":"小队背包 · B键关闭"+(s.Adventure.Pauses.Count>0?" · 暂离占位："+string.Join("、",s.Adventure.Pauses.Select(p=>s.Leaders.Single(l=>l.Id==p.LeaderId).Name)):""),new Vector2(-180,309),new Vector2(820,42),25);
            MakeButton(panel,"关闭 · B",new Vector2(533,309),new Vector2(126,42),ClosePanel);
            if(warehouse)
            {
                float width=Math.Min(140,520f/people.Length);
                for(int i=0;i<people.Length;i++){var person=people[i];var b=MakeButton(panel,(person.Id==inventoryPerson?"● ":"")+person.Name,new Vector2(-586+width/2+i*width,258),new Vector2(width-5,40),()=>{inventoryClicks.Reset();inventoryPerson=person.Id;inventorySelected="";OpenInventory(true);});b.GetComponentInChildren<Text>().color=person.Recruited?Color.yellow:ink;}
                InventoryCharacter(people.Single(l=>l.Id==inventoryPerson),new Vector2(-342,-32),new Vector2(510,534));
                var store=Box("仓库物品与分类",panel,Vector2.one*.5f,new Vector2(260,119),new Vector2(660,294),new Color(.10f,.18f,.19f,.92f));
                store.GetComponent<UiVisualNode>().UseAuthoredLayout=false;store.gameObject.GetOrAdd<UiDynamicContent>();
                Label(store,"基地仓库 · 不限格数",new Vector2(-150,119),new Vector2(340,32),21);InventoryTools(store,"",new Vector2(178,119));
                var entries=BasePartyInventory.Entries(s).Where(e=>!e.Worn&&e.Holder=="").ToArray();int pages=Math.Max(1,(entries.Length+23)/24);warehousePage=Mathf.Clamp(warehousePage,0,pages-1);
                for(int n=0;n<24;n++){var e=entries.Skip(warehousePage*24+n).FirstOrDefault();InventoryCell(store,e,"",warehousePage*24+n,"",new Vector2(-265+(n%6)*106,72-(n/6)*52),new Vector2(100,46));}
                MakeButton(store,"上一页",new Vector2(-220,-126),new Vector2(140,32),()=>{inventoryClicks.Reset();warehousePage--;OpenInventory(true);}).interactable=warehousePage>0;
                Label(store,(warehousePage+1)+" / "+pages,new Vector2(0,-126),new Vector2(120,30),17,TextAnchor.MiddleCenter);
                MakeButton(store,"下一页",new Vector2(220,-126),new Vector2(140,32),()=>{inventoryClicks.Reset();warehousePage++;OpenInventory(true);}).interactable=warehousePage+1<pages;
                var details=Box("右侧物品描述与操作",panel,Vector2.one*.5f,new Vector2(260,-175),new Vector2(660,274),new Color(.10f,.18f,.19f,.92f));InventoryDetails(details,true);
            }
            else
            {
                // One continuous party row, including on touch devices. No character-switch tabs.
                var viewport=Rect("PartyViewport",panel,Vector2.one*.5f,new Vector2(0,40),new Vector2(1190,450));
                viewport.gameObject.GetOrAdd<RectMask2D>();viewport.gameObject.GetOrAdd<Image>().color=new Color(0,0,0,.01f);
                float total=people.Length*390f;
                var party=Rect("PartyRow",viewport,new Vector2(.5f,.5f),Vector2.zero,new Vector2(Math.Max(1190,total),450));

                var scroll=viewport.gameObject.GetOrAdd<ScrollRect>();scroll.viewport=viewport;scroll.content=party;scroll.horizontal=true;scroll.vertical=false;scroll.movementType=ScrollRect.MovementType.Clamped;
                for(int n=0;n<people.Length;n++)InventoryCharacter(people[n],new Vector2(-total/2+195+n*390,0),new Vector2(380,450),party);
                var info=Box("下方物品说明",panel,Vector2.one*.5f,new Vector2(0,-258),new Vector2(1190,132),DemoUiTheme.Current.Inset);InventoryDetails(info,false);
            }
            if(BaseDemoIntro.Daily(s))s.Demo.Intro.BackpackOpened=true;
            panelFeedback=Label(panel,owner.Message,new Vector2(0,-328),new Vector2(1170,24),16);UiPrefabBinding.Complete(panel.gameObject);RefreshSummary();
        }
        private void InventoryCharacter(BaseLeader l,Vector2 position,Vector2 size,RectTransform parent=null)
        {
            var card=UiPrefabBinding.Create("PartyMemberCard","人物与背包_"+l.Id,parent??panel).GetComponent<RectTransform>();card.GetComponent<UiVisualNode>().UseAuthoredLayout=false;card.anchorMin=card.anchorMax=card.pivot=Vector2.one*.5f;card.anchoredPosition=position;card.sizeDelta=size;DemoUiTheme.Current.Surface(card.gameObject.GetOrAdd<Image>());float top=size.y/2;
            var name=MakeButton(card,"MemberName",new Vector2(-size.x*.20f,top-24),new Vector2(size.x*.56f,34),()=>{inventoryPerson=l.Id;inventorySelected="";OpenInventory(inventoryWarehouse);});name.GetComponentInChildren<Text>().text=BasePersonnel.LeaderName(l)+" · "+BasePersonalEquipment.ClassName(l);BindInventoryDrop(name.gameObject,null,l.Id,-2,"");
            Label(card,l.Health.ToString("0.#")+" / "+l.MaxHealth.ToString("0.#"),new Vector2(size.x*.30f,top-24),new Vector2(size.x*.32f,30),17,TextAnchor.MiddleRight);
            float x=size.x*.31f;var slotRows=new[]{"weapon","helmet","armor"};
            for(int n=0;n<3;n++){string slot=slotRows[n];InventoryCell(card,BasePartyInventory.Entries(owner.State).SingleOrDefault(e=>e.Holder==l.Id&&e.Slot==slot),l.Id,-1,slot,new Vector2(-x,top-72-n*44),new Vector2(size.x*.32f,38));}
            var portrait=Box("人物形象",card,Vector2.one*.5f,new Vector2(0,top-116),new Vector2(size.x*.26f,124),new Color(.12f,.24f,.27f,.8f));
            var art=Rect("角色立绘",portrait,Vector2.one*.5f,new Vector2(0,8),new Vector2(size.x*.24f,100));var avatar=art.gameObject.GetOrAdd<BattleAvatarGraphic>();avatar.PlayerHero=l.Id==owner.State.PlayerLeaderId;avatar.AppearanceId=BaseCommanderIdentity.Appearance(l);avatar.raycastTarget=false;
            Label(portrait,l.Name,new Vector2(0,-45),new Vector2(size.x*.25f,30),18,TextAnchor.MiddleCenter).color=l.Recruited?Color.yellow:ink;
            var accessories=BasePersonalEquipment.SlotsFor(l).Where(v=>v.StartsWith("accessory")).ToArray();for(int n=0;n<accessories.Length;n++){string slot=accessories[n];float y=top-(accessories.Length==3?72:94)-n*44;InventoryCell(card,BasePartyInventory.Entries(owner.State).SingleOrDefault(e=>e.Holder==l.Id&&e.Slot==slot),l.Id,-1,slot,new Vector2(x,y),new Vector2(size.x*.32f,38));}
            MakeButton(card,"角色详情",new Vector2(0,top-199),new Vector2(size.x-24,30),()=>{inventoryPerson=l.Id;inventorySelected="";OpenPersonnelDetail("leader:"+l.Id,BaseMuster.Snapshot(owner.State),()=>OpenInventory(inventoryWarehouse));});
            Label(card,"物品 "+BasePartyInventory.Entries(owner.State).Count(e=>e.Holder==l.Id&&!e.Worn)+"/24",new Vector2(-size.x/2+60,top-235),new Vector2(105,30),17);InventoryTools(card,l.Id,new Vector2(size.x/2-145,top-235));
            float cw=(size.x-24-25)/6,ch=Math.Min(60,(size.y-265)/4-4);for(int n=0;n<24;n++){int cell=n;var e=BasePartyInventory.Entries(owner.State).SingleOrDefault(i=>i.Holder==l.Id&&!i.Worn&&i.Cell==cell);InventoryCell(card,e,l.Id,n,"",new Vector2(-size.x/2+12+cw/2+(n%6)*(cw+5),top-257-ch/2-(n/6)*(ch+5)),new Vector2(cw,ch));}
            UiPrefabBinding.Complete(card.gameObject);
        }
        private void InventoryTools(RectTransform parent,string holder,Vector2 position)
        {
            MakeButton(parent,BaseBackpack.CategoryName(InventoryFilter(holder))+" ▾",position,new Vector2(132,30),()=>InventoryPopup(parent,holder,position));
            MakeButton(parent,"整理",position+new Vector2(101,0),new Vector2(62,30),()=>InventoryChange(s=>BasePartyInventory.Sort(s,holder),"已整理物品"));
        }
        private void InventoryPopup(RectTransform parent,string holder,Vector2 pos)
        {
            inventoryClicks.Reset();
            var pop=Box("分类选择",panel,Vector2.one*.5f,Vector2.zero,new Vector2(260,382),new Color(.10f,.18f,.19f,1));pop.SetAsLastSibling();
            for(int i=0;i<InventoryCategories.Length;i++){string id=InventoryCategories[i];MakeButton(pop,BaseBackpack.CategoryName(id),new Vector2(0,161-i*41),new Vector2(244,36),()=>{inventoryFilters[holder]=id;OpenInventory(inventoryWarehouse);});}
            MakeButton(pop,"取消",new Vector2(0,-167),new Vector2(244,36),()=>OpenInventory(inventoryWarehouse));
        }
        private void InventoryCell(RectTransform parent,BaseInventoryEntry e,string holder,int cell,string slot,Vector2 pos,Vector2 size)
        {
            bool shown=e!=null&&(e.Worn||InventoryFilter(holder)=="all"||InventoryFilter(holder)==e.Category);string title=slot!=""?BasePersonalEquipment.SlotName(slot)+"\n"+(shown?e.Name:"空"):shown?e.Name+(e.Count>1?" ×"+e.Count:""):e==null?"":"·";
            string key=slot!=""?"EquipmentSlot_"+slot:"BagSlot_"+cell.ToString();
            var button=MakeButton(parent,key,pos,size,()=>{
                bool twice=inventoryClicks.Click(e?.Id,Time.unscaledTime);
                if(twice&&inventoryWarehouse&&slot=="")
                {if(!shown){owner.ReportMessage("此格物品被分类隐藏，请先切换全部");return;}InventoryChange(s=>BasePartyInventory.QuickTransfer(s,e.Id,inventoryPerson,true),"已完成仓库快速存取");return;}
                inventoryPerson=holder==""?inventoryPerson:holder;inventorySelected=shown?e.Id:"";inventoryQuantity=e?.Count??1;OpenInventory(inventoryWarehouse);
            });var label=button.GetComponentInChildren<Text>();label.text=title;label.fontSize=slot!=""?14:13;
            if(e?.Gear!=null)label.color=e.Quality>=4?new Color(.86f,.73f,.42f):ink;if(!shown&&e!=null)button.GetComponent<Image>().color=new Color(.13f,.18f,.19f,.7f);
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
        private void InventoryDetails(RectTransform box,bool vertical)
        {
            var inner=Rect("物品详情动态内容",box,Vector2.one*.5f,Vector2.zero,box.sizeDelta);inner.gameObject.GetOrAdd<UiDynamicContent>();box=inner;
            var s=owner.State;var e=BasePartyInventory.Find(s,inventorySelected);var l=s.Leaders.Single(p=>p.Id==inventoryPerson);float w=box.sizeDelta.x,h=box.sizeDelta.y;
            if(e==null){Label(box,"装备与物品",new Vector2(vertical?0:-420,vertical?h/2-24:28),new Vector2(vertical?w-24:310,36),21);Label(box,"点击物品查看说明 · 拖动整理或转交\n人物属性、技能与特性请打开角色详情。",new Vector2(0,-10),new Vector2(w-48,70),18,TextAnchor.MiddleCenter);return;}
            inventoryQuantity=Mathf.Clamp(inventoryQuantity,1,e.Count);
            string description=e.Gear!=null?InventoryGearDescription(e,l):e.Description;
            Label(box,e.Name+(e.Count>1?" ×"+e.Count:""),new Vector2(vertical?0:-420,vertical?h/2-24:25),new Vector2(vertical?w-24:310,36),20);
            float descriptionHeight=Math.Min(180,h-179);
            Label(box,description,new Vector2(vertical?0:-40,vertical?h/2-52-descriptionHeight/2:0),new Vector2(vertical?w-24:480,vertical?descriptionHeight:92),17);
            float x=vertical?-w/2+78:330,y=vertical?-h/2+104:23;
            void Btn(string title,int col,int line,Action click,bool enabled=true){MakeButton(box,title,new Vector2(x+col*139,y-line*40),new Vector2(132,34),click).interactable=enabled;}
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
        private void InventoryTransferMenu(BaseInventoryEntry e)
        {
            var people=BasePartyInventory.People(owner.State,true).Where(l=>l.Id!=e.Holder).ToArray();var pop=Box("仓库转交选择",panel,Vector2.one*.5f,Vector2.zero,new Vector2(410,Math.Max(140,people.Length*48+60)),new Color(.1f,.18f,.19f,1));float y=people.Length*24;
            foreach(var l in people){string id=l.Id;MakeButton(pop,"转交给 "+l.Name,new Vector2(0,y),new Vector2(386,42),()=>InventoryChange(s=>BasePartyInventory.Move(s,e.Id,id,BasePartyInventory.FreeCell(s,id),"",inventoryQuantity),"已转交物品"));y-=48;}
            MakeButton(pop,"取消",new Vector2(0,y),new Vector2(386,42),()=>OpenInventory(true));
        }
        private void InventoryDestroyConfirm(BaseInventoryEntry e)
        {
            var pop=Box("销毁确认",panel,Vector2.one*.5f,Vector2.zero,new Vector2(660,200),new Color(.12f,.19f,.20f,1));Label(pop,"销毁 "+e.Name+" ×"+inventoryQuantity+"？\n此操作不可恢复。",new Vector2(0,40),new Vector2(610,100),23,TextAnchor.MiddleCenter);
            MakeButton(pop,"确认销毁",new Vector2(-150,-57),new Vector2(270,44),()=>InventoryChange(s=>BasePartyInventory.Destroy(s,e.Id,inventoryQuantity),"已销毁物品"));MakeButton(pop,"取消",new Vector2(150,-57),new Vector2(270,44),()=>OpenInventory(inventoryWarehouse));
        }
    }
}

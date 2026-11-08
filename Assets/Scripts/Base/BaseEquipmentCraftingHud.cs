using System;
using System.Linq;
using UnityEngine;
using BaseConquest.Presentation;

namespace BaseConquest.Base
{
    public sealed partial class BaseHud
    {
        private string forgeItem="",forgeResult="";
        private int forgeStones=1,forgePage,stoneQuantity=1;
        private void OpenForgeShop()
        {
            var s=owner.State;BeginPanel("锻造石 · 每日限量","forge-shop");
            RowButton("返回交易",0,820,OpenTownMarket);row+=60;
            Line("钱币 "+s.Inventory.Coins+" · 锻造石每颗 "+BaseEquipmentCrafting.BuyPrice(s)+" 币 · 今日剩余 "+s.PersonalEquipment.StonesRemaining+"/"+BaseEquipmentCrafting.StockLimit(s),60,21);
            Line("仅过夜补满；进出页面或读当前进度不会补货。购买后进入随行队员背包，可在基地仓库存取。",66,19);
            int max=BaseEquipmentCrafting.MaxBuy(s);stoneQuantity=Math.Max(1,Math.Min(stoneQuantity,Math.Max(1,max)));
            RowButton("−1",-210,400,()=>{stoneQuantity=Math.Max(1,stoneQuantity-1);OpenForgeShop();},stoneQuantity>1);
            RowButton("＋1",210,400,()=>{stoneQuantity=Math.Min(max,stoneQuantity+1);OpenForgeShop();},stoneQuantity<max);row+=60;
            int quantity=stoneQuantity,revision=s.Backpack.Revision;
            RowButton("购买 "+quantity+"颗 · "+(quantity*BaseEquipmentCrafting.BuyPrice(s))+"币",0,820,()=>GearChange(next=>BaseEquipmentCrafting.Buy(next,quantity,revision),"已购买锻造石",OpenForgeShop),max>=quantity);row+=60;
            if(max==0)Line("当前无法购买：请检查钱币、今日余量、背包空间与摊位距离。",65,19);
            EndPanel();
        }
        private string forgeHolder="";
        private void OpenReforge()
        {
            var s=owner.State;BeginPanel("工坊 · 装备重铸","reforge");
            var people=BaseAdventure.Members(s).Where(l=>BasePartyInventory.AccessibleLeader(s,l)).ToArray();
            if(!people.Any(l=>l.Id==forgeHolder)&&forgeHolder!="warehouse")forgeHolder=people.FirstOrDefault()?.Id??"warehouse";
            if(forgeHolder=="warehouse"&&s.Campaign.Location!="base")forgeHolder=people.FirstOrDefault()?.Id??"";
            var holders=people.Select(l=>l.Id).Concat(s.Campaign.Location=="base"?new[]{"warehouse"}:Array.Empty<string>()).ToArray();
            float tabStep=880f/(holders.Length+1);RowButton("返回工坊",-440+tabStep/2,tabStep-8,()=>{activeObject="workshop";activePage="workers";OpenFacility();});
            for(int n=0;n<holders.Length;n++){string id=holders[n];RowButton((forgeHolder==id?"● ":"")+(id=="warehouse"?"基地仓库":people.Single(l=>l.Id==id).Name),-440+tabStep*(n+1.5f),tabStep-8,()=>{forgeHolder=id;OpenReforge();});}row+=58;
            var entries=BasePartyInventory.Entries(s).Where(e=>e.Gear!=null&&!e.Protected&&e.Category!="accessory"&&(e.Holder==""||people.Any(l=>l.Id==e.Holder))).ToArray();
            if(!entries.Any(e=>e.Id==forgeItem))forgeItem="";var selected=entries.FirstOrDefault(e=>e.Id==forgeItem);
            var bag=entries.Where(e=>e.Holder==(forgeHolder=="warehouse"?"":forgeHolder)).ToArray();int pages=Math.Max(1,(bag.Length+23)/24);forgePage=Math.Min(forgePage,pages-1);
            Line("从左侧背包拖入右侧重铸框；确认前不会消耗或移动物品。",48,20);
            for(int n=0;n<24;n++)
            {
                var e=bag.Skip(forgePage*24).ElementAtOrDefault(n);var cell=MakeButton(content,e==null?"":e.Name+(e.Worn?"\n已穿戴":""),new Vector2(-390+(n%6)*68,-row-28-(n/6)*60),new Vector2(64,56),()=>{},new Vector2(.5f,1));
                var cellRect=(RectTransform)cell.transform;
                // 格子只作拖拽源，不是可点按钮：去掉 Button 组件，避免出现「点了没反应」的控件。
                UiPrefabBinding.DestroyView(cell);
                cellRect.GetComponentInChildren<UnityEngine.UI.Text>().fontSize=13;
                if(e!=null){var drag=cellRect.gameObject.GetOrAdd<PartyInventoryCell>();drag.ItemId=e.Id;drag.Scope=panel;}
            }
            var slot=Box("ReforgeDropSlot",content,new Vector2(.5f,1),new Vector2(240,-row-45),new Vector2(330,84),new Color(.16f,.26f,.26f));
            Label(slot,selected==null?"将武器或护具拖到这里":selected.Name,Vector2.zero,new Vector2(310,74),20);
            var drop=slot.gameObject.GetOrAdd<PartyInventoryCell>();drop.Scope=panel;drop.Move=(id,holder,cell,gear)=>{if(entries.Any(e=>e.Id==id)){forgeItem=id;forgeResult="";OpenReforge();}};
            float gridTop=row;
            var previous=MakeButton(content,"上一页",new Vector2(-305,-gridTop-271),new Vector2(150,44),()=>{forgePage--;OpenReforge();},new Vector2(.5f,1));previous.interactable=forgePage>0;
            var following=MakeButton(content,"下一页",new Vector2(-115,-gridTop-271),new Vector2(150,44),()=>{forgePage++;OpenReforge();},new Vector2(.5f,1));following.interactable=forgePage<pages-1;
            if(selected!=null)
            {
                var quote=BaseEquipmentCrafting.Quote(s,forgeItem,forgeStones);
                string details="可用锻造石 "+BasePartyInventory.AvailableMaterial(s,BaseEquipmentCrafting.Stone)+" · 本次 "+forgeStones+"颗\n"+(quote==null?BaseEquipmentCrafting.Blocked(s,forgeItem,forgeStones):"升档 "+(quote.Up/100f).ToString("0.##")+"% ／ 留级 "+(quote.Stay/100f).ToString("0.##")+"%\n降档 "+(quote.Down/100f).ToString("0.##")+"%")+"\n"+(BaseRpg.Active(s)?BaseRpg.GearDescription(s,selected.Gear):selected.Name);
                Label(content,details,new Vector2(240,-gridTop-154),new Vector2(330,122),16,TextAnchor.UpperLeft,new Vector2(.5f,1));
                MakeButton(content,"−1颗",new Vector2(135,-gridTop-242),new Vector2(100,40),()=>{forgeStones=Math.Max(1,forgeStones-1);OpenReforge();},new Vector2(.5f,1)).interactable=forgeStones>1;
                MakeButton(content,"＋1颗",new Vector2(245,-gridTop-242),new Vector2(100,40),()=>{forgeStones=Math.Min(10,forgeStones+1);OpenReforge();},new Vector2(.5f,1)).interactable=forgeStones<10;
                MakeButton(content,"取出",new Vector2(355,-gridTop-242),new Vector2(100,40),()=>{forgeItem="";forgeResult="";OpenReforge();},new Vector2(.5f,1));
                var confirm=MakeButton(content,"确认消耗并重铸",new Vector2(240,-gridTop-295),new Vector2(330,44),()=>{
                    bool ok=owner.Change(next=>{if(!BaseEquipmentCrafting.Confirm(next,quote))throw new InvalidOperationException("物品、材料或状态已变化，请重新查看重铸概率。");},"已完成重铸");
                    if(ok){var item=BasePartyInventory.Find(owner.State,quote.ItemId);forgeResult="重铸结果：["+BasePersonalEquipment.Qualities[quote.Quality]+"] → ["+BasePersonalEquipment.Qualities[item.Quality]+"] · 消耗"+quote.Stones+"颗";}OpenReforge();},new Vector2(.5f,1));confirm.interactable=quote!=null;
            }
            row=gridTop+326;
            if(forgeResult!="")Line(forgeResult,60,21);EndPanel();
        }
    }
}

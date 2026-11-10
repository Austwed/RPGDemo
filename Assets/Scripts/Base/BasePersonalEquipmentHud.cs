using System;
using System.Linq;
using BaseConquest.Battle;
using BaseConquest.Story;
using UnityEngine;

namespace BaseConquest.Base
{
    public sealed partial class BaseHud
    {
        private string gearLeader="";
        private void GearChange(Func<BaseState,bool> change,string message,Action refresh)
        {owner.Change(next=>{if(!change(next))throw new InvalidOperationException("装备操作不可用，请检查位置、资格与当前物品。");},message);refresh();}
        public void OpenPersonalEquipment(){inventoryPerson=gearLeader;OpenInventory(false);}
        private void OpenPersonalSlot()=>OpenPersonalEquipment();
        private void GearItemLines(BasePersonalItem item)
        {
            if(BaseRpg.Active(owner.State)){Line(BaseRpg.GearDescription(owner.State,item)+BaseRpg.GearComparison(owner.State,item),145,18);return;}
            var d=BasePersonalEquipment.Definition(item.Template);int q=item.Quality;
            Line("攻击 "+BasePersonalEquipment.Scale(d.Damage,q).ToString("+0.##;-0.##;0")+" · 防御 "+BasePersonalEquipment.Scale(d.Defense,q).ToString("+0.##;-0.##;0")+" · 生命 "+BasePersonalEquipment.Scale(d.Health,q).ToString("+0.##;-0.##;0"),48,18);
            Line("暴击+"+BasePersonalEquipment.Scale(d.CritChance,q).ToString("P1")+" · 暴伤+"+BasePersonalEquipment.Scale(d.CritMultiplier,q).ToString("P1")+(d.Slot=="weapon" ? " · 武器射程 "+BasePersonalEquipment.RealtimeRange(item)+"细格" : "")+" · 移动 "+BasePersonalEquipment.Scale(d.Movement,q).ToString("+0.##;-0.##;0")+"（未启用）",48,18);
        }
        private void RenderEquipmentTrade(RectTransform offers,RectTransform details,BaseLeader recipient)
        {
            var s=owner.State;
            var goods=BasePersonalEquipment.ShopFor(s)
                .Where(d=>!BaseDemoFlow.Active(s)||!d.Id.StartsWith("novice_",StringComparison.Ordinal)||s.Demo.Flow.NoviceShop).ToArray();
            var selected=goods.FirstOrDefault(d=>d.Id==tradeSelection)??goods.FirstOrDefault();
            if(selected==null)return;
            tradeSelection=selected.Id;
            foreach(var gear in goods)
            {
                var offer=s.PersonalEquipment.Offers.Single(o=>o.Template==gear.Id);
                TradeOffer(offers,gear.Id,gear.Name+" · "+BasePersonalEquipment.BuyPrice(s,gear.Id)+"币 · 余 "+offer.Remaining);
            }
            if(details==null)return;

            string id=selected.Id,holder=recipient?.Id??"";
            int price=BasePersonalEquipment.BuyPrice(s,id);
            bool full=recipient!=null&&BasePartyInventory.FreeCell(s,holder)<0;
            TradeLine(details,selected.Name+" · "+price+"币");
            if(recipient!=null)TradeLine(details,StoryText.Get("story.ui.trade.recipient","收货人：{name}").Replace("{name}",recipient.Name));
            if(full)TradeLine(details,StoryText.Get("story.ui.trade.recipient_full","所选角色背包已满，请切换角色"));
            if(recipient==null)TradeLine(details,StoryText.Get("story.ui.trade.recipient_missing","当前没有可收货的随行角色"));
            TradeButton(details,"确认买入",()=>TradeCommit(next=>BasePersonalEquipment.Buy(next,id,holder,price),"已购入 "+selected.Name),
                BasePersonalEquipment.CanBuy(s,id,holder));
            var sample=new BasePersonalItem {Template=id,Quality=2};
            if(BaseRpg.Active(s))TradeLine(details,BaseRpg.GearDescription(s,sample)+BaseRpg.GearComparison(s,sample));
            else TradeLine(details,"攻击 "+selected.Damage.ToString("0.##")+" · 防御 "+selected.Defense.ToString("0.##")+" · 生命 "+selected.Health.ToString("0.##"));
        }

    }
}

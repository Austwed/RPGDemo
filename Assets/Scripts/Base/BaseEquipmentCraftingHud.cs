using System;
using System.Linq;
using BaseConquest.Story;
using UnityEngine;

namespace BaseConquest.Base
{
    public sealed partial class BaseHud
    {
        private void RenderStoneOffer(RectTransform offers)
        {
            var s=owner.State;
            TradeOffer(offers,BaseEquipmentCrafting.Stone,
                "锻造石 · "+BaseEquipmentCrafting.BuyPrice(s)+"币 · 余 "+s.PersonalEquipment.StonesRemaining);
        }

        private void RenderStoneDetails(RectTransform details)
        {
            var s=owner.State;
            int price=BaseEquipmentCrafting.BuyPrice(s);
            int max=BaseEquipmentCrafting.MaxBuy(s),revision=s.Backpack.Revision;
            TradeLine(details,"锻造石 · 单价 "+price+"币");
            TradeLine(details,"今日剩余 "+s.PersonalEquipment.StonesRemaining+"/"+BaseEquipmentCrafting.StockLimit(s));
            TradeQuantity(details,max);
            int count=tradeQuantity;
            TradeButton(details,"确认买入 · "+count*price+"币",
                ()=>TradeCommit(next=>BaseEquipmentCrafting.BuyPrice(next)==price&&BaseEquipmentCrafting.Buy(next,count,revision),"已购买锻造石"),count>0);
            if(max==0)TradeLine(details,"当前无法购买，请检查钱币、今日余量或背包空间");
        }

        private void RenderReforgeTrade(RectTransform offers,RectTransform details,BaseLeader person)
        {
            var s=owner.State;
            var entries=BasePartyInventory.Entries(s)
                .Where(e=>e.Holder==person?.Id&&BaseEquipmentCrafting.CanSelectReforge(s,e)).ToArray();
            foreach(var entry in entries)TradeOffer(offers,entry.Id,entry.Name+(entry.Worn?" · 已穿戴":""),InventoryIconKey(entry));

            var selected=entries.FirstOrDefault(e=>e.Id==tradeSelection);
            if(selected==null)
            {
                TradeLine(details,StoryText.Get("story.ui.trade.reforge_target","请选择随行角色的武器或护具"));
                return;
            }

            int available=BasePartyInventory.AvailableMaterial(s,BaseEquipmentCrafting.Stone);
            TradeLine(details,selected.Name);
            TradeLine(details,"可用锻造石 "+available+"颗");
            string blocked=BaseEquipmentCrafting.Blocked(s,selected.Id,1);
            if(blocked.Length==0)TradeQuantity(details,Math.Min(10,available));
            int stones=tradeQuantity;
            var quote=blocked.Length==0?BaseEquipmentCrafting.Quote(s,selected.Id,stones):null;
            if(quote==null)TradeLine(details,blocked);
            else
            {
                TradeLine(details,"升档 "+(quote.Up/100f).ToString("0.##")+"% ／ 留级 "+(quote.Stay/100f).ToString("0.##")+"% ／ 降档 "+(quote.Down/100f).ToString("0.##")+"%");
                string basis=selected.Worn?"角色属性":"装备属性";
                string preview=StoryText.Get("story.ui.trade.reforge_preview","{0} · {1} {2}");
                if(quote.Up>0)
                    TradeLine(details,string.Format(preview,basis,"升档后",BaseEquipmentCrafting.StatChanges(s,selected,quote.Quality,quote.Quality+1)));
                if(quote.Down>0)
                    TradeLine(details,string.Format(preview,basis,"降档后",BaseEquipmentCrafting.StatChanges(s,selected,quote.Quality,quote.Quality-1)));
            }
            TradeButton(details,"确认重铸",()=>{
                bool ok=owner.Change(next=>{
                    if(!BaseEquipmentCrafting.Confirm(next,quote))
                        throw new InvalidOperationException(StoryText.Get("story.ui.trade.changed","交易条件已变化，请重新确认"));
                },"已完成重铸");
                var result=ok?BasePartyInventory.Find(owner.State,selected.Id):null;
                tradeFeedback=result==null?owner.Message:string.Format(
                    StoryText.Get("story.ui.trade.reforge_result","重铸结果：{0} → {1} · 消耗 {2} 颗"),
                    BasePersonalEquipment.Qualities[quote.Quality],BasePersonalEquipment.Qualities[result.Quality],quote.Stones)
                    +"\n"+BaseEquipmentCrafting.StatChanges(owner.State,result,quote.Quality,result.Quality);
                RenderTownTrade();
            },quote!=null);
            if(tradeFeedback.Length>0)TradeLine(details,tradeFeedback);
            TradeLine(details,BaseRpg.Active(s)?BaseRpg.GearDescription(s,selected.Gear):selected.Description);
        }
    }
}

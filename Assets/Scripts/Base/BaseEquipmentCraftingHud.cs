using System;
using System.Linq;
using BaseConquest.Story;
using UnityEngine;

namespace BaseConquest.Base
{
    public sealed partial class BaseHud
    {
        private void RenderStonesTrade(RectTransform offers,RectTransform details)
        {
            var s=owner.State;
            int price=BaseEquipmentCrafting.BuyPrice(s);
            tradeSelection=BaseEquipmentCrafting.Stone;
            TradeOffer(offers,tradeSelection,"锻造石 · "+price+"币 · 余 "+s.PersonalEquipment.StonesRemaining);
            if(details==null)return;

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
            foreach(var entry in entries)TradeOffer(offers,entry.Id,entry.Name+(entry.Worn?" · 已穿戴":""));

            var selected=entries.FirstOrDefault(e=>e.Id==tradeSelection);
            if(selected==null)
            {
                TradeLine(details,StoryText.Get("story.ui.trade.reforge_target","请选择随行角色的武器或护具"));
                return;
            }

            int available=BasePartyInventory.AvailableMaterial(s,BaseEquipmentCrafting.Stone);
            TradeLine(details,selected.Name);
            TradeLine(details,"可用锻造石 "+available+"颗");
            TradeQuantity(details,Math.Min(10,available));
            int stones=tradeQuantity;
            var quote=BaseEquipmentCrafting.Quote(s,selected.Id,stones);
            if(available==0)TradeLine(details,"可用锻造石不足");
            else if(quote==null)TradeLine(details,BaseEquipmentCrafting.Blocked(s,selected.Id,stones));
            else TradeLine(details,"升档 "+(quote.Up/100f).ToString("0.##")+"% ／ 留级 "+(quote.Stay/100f).ToString("0.##")+"% ／ 降档 "+(quote.Down/100f).ToString("0.##")+"%");
            TradeButton(details,"确认重铸",()=>{
                int previousQuality=selected.Quality;
                bool ok=owner.Change(next=>{
                    if(!BaseEquipmentCrafting.Confirm(next,quote))
                        throw new InvalidOperationException(StoryText.Get("story.ui.trade.changed","交易条件已变化，请重新确认"));
                },"已完成重铸");
                var result=ok?BasePartyInventory.Find(owner.State,selected.Id):null;
                tradeFeedback=result==null?owner.Message:"重铸结果："+BasePersonalEquipment.Qualities[previousQuality]+" → "+BasePersonalEquipment.Qualities[result.Quality]+" · 消耗 "+stones+" 颗";
                RenderTownTrade();
            },quote!=null);
            TradeLine(details,BaseRpg.Active(s)?BaseRpg.GearDescription(s,selected.Gear):selected.Description);
        }
    }
}

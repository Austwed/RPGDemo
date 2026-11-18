using System;
using System.Linq;
using BaseConquest.Base;
using UnityEditor;
using UnityEngine;

namespace BaseConquest.Editor
{
    public static class VerifyDemoTrade
    {
        private static void Need(bool yes,string reason)
        {
            if(!yes)throw new InvalidOperationException(reason);
        }

        public static void RunAndExit()
        {
            try{Run();EditorApplication.Exit(0);}
            catch(Exception error){Debug.LogException(error);EditorApplication.Exit(1);}
        }

        public static void Run()
        {
            var originalFacing=BaseInteraction.Facing;
            try
            {
                var source=Resources.Load<TextAsset>("DemoStages/camp-male");
                Need(source!=null,"缺少营地阶段档");
                var state=JsonUtility.FromJson<BaseState>(source.text);
                BaseDemo.InstallCampMap(state);
                BaseEconomy.Validate(state);

                At(state,"home");
                Need(BaseDemo.AcknowledgeWaking(state),"营地开局失败");
                At(state,"demo_ahe");
                Need(BaseDemoRuins.Meet(state,"partner_farm")&&BaseDemoRuins.Join(state,"partner_farm"),"阿禾未加入");
                At(state,"exit_east");
                Need(BaseCampaign.Travel(state,"town"),"无法前往灰岩镇");
                VerifyMerchantBeforeCommission(state.Copy());
                var smith=BaseSpatial.Objects(state).First(o=>o.Id=="town_smith"||o.Id=="town_blacksmith");
                At(state,smith.Id);
                Need(BasePersonalEquipment.AtSmith(state),"未在铁匠交互范围");
                state.Inventory.Coins=Math.Max(10000,state.Inventory.Coins);

                string recipient=BaseAdventure.Members(state).First(l=>l.Id!=state.PlayerLeaderId).Id;
                const string template="novice_sword";
                Need(!BasePersonalEquipment.CanBuy(state,template,recipient),"初次进城不应提前开放新手装备");
                state.Demo.Flow.NoviceShop=true;
                int price=BasePersonalEquipment.BuyPrice(state,template);
                Need(BasePersonalEquipment.CanBuy(state,template,recipient),"随行角色不能收货");

                var full=state.Copy();
                for(int cell=BasePartyInventory.FreeCell(full,recipient);cell>=0;cell=BasePartyInventory.FreeCell(full,recipient))
                    full.Backpack.Items.Add(new BaseBagItem{Id="bag:"+full.Backpack.NextId++,ItemId=BaseBackpack.Blueprint,Count=1,Holder=recipient,Cell=cell});
                Need(BasePartyInventory.FreeCell(full,full.PlayerLeaderId)>=0,"测试主角需要保留空位");
                Unchanged(full,()=>BasePersonalEquipment.Buy(full,template,recipient,price),"满包购买不得改发主角");
                Unchanged(state,()=>BasePersonalEquipment.Buy(state,template,"",price),"缺收货人不得购买");
                Unchanged(state,()=>BasePersonalEquipment.Buy(state,template,recipient,price+1),"过期报价不得购买");
                var poor=state.Copy();poor.Inventory.Coins=0;
                Unchanged(poor,()=>BasePersonalEquipment.Buy(poor,template,recipient,price),"金币不足不得购买");

                string itemId="personal:"+state.PersonalEquipment.NextId;
                int coins=state.Inventory.Coins;
                Need(BasePersonalEquipment.Buy(state,template,recipient,price),"购买失败");
                var item=BasePartyInventory.Find(state,itemId);
                Need(item!=null&&item.Holder==recipient&&item.Cell>=0&&state.Inventory.Coins==coins-price,
                    "装备、收货人或扣款错误");
                Unchanged(state,()=>BasePersonalEquipment.Buy(state,template,recipient,price),"售罄后不得重复扣款");

                int cellForStone=BasePartyInventory.FreeCell(state,state.PlayerLeaderId);
                Need(cellForStone>=0,"缺少随身材料测试格");
                state.Backpack.Items.Add(new BaseBagItem{Id="bag:"+state.Backpack.NextId++,ItemId=BaseEquipmentCrafting.Stone,Count=2,Holder=state.PlayerLeaderId,Cell=cellForStone});
                var player=state.Leaders.Single(l=>l.Id==state.PlayerLeaderId);
                var basic=player.PersonalEquipment.Items.Single(gear=>gear.Slot=="weapon");
                Need(BaseEquipmentCrafting.Blocked(state,basic.Id,1).Contains("新手短剑"),"无属性变化的开局短剑应显示重铸引导");
                Unchanged(state,()=>BaseEquipmentCrafting.Confirm(state,BaseEquipmentCrafting.Quote(state,basic.Id,1)),"开局短剑不得消耗锻造石");
                var legacy=state.Copy();
                BasePartyInventory.Find(legacy,basic.Id).Gear.Quality=5;
                BaseEconomy.Validate(legacy);
                Need(BasePartyInventory.Find(legacy,basic.Id).Quality==5&&BaseEquipmentCrafting.Quote(legacy,basic.Id,1)==null,"旧档传奇短剑应保留原品质");
                var quote=BaseEquipmentCrafting.Quote(state,itemId,1);
                Need(quote!=null,"城镇铁匠未接受随行装备");
                var warehouse=state.Copy();
                var stored=BasePartyInventory.Find(warehouse,itemId).Gear;
                stored.Holder="";stored.Cell=-1;
                Need(BaseEquipmentCrafting.Quote(warehouse,itemId,1)==null,"铁匠不能重铸仓库装备");
                var leftTown=state.Copy();leftTown.Campaign.Location="base";
                Unchanged(leftTown,()=>BaseEquipmentCrafting.Confirm(leftTown,quote),"离开铁匠不得消耗材料");

                Need(BasePartyInventory.Move(state,itemId,state.PlayerLeaderId,BasePartyInventory.FreeCell(state,state.PlayerLeaderId))&&
                    BasePersonalEquipment.Equip(state,state.PlayerLeaderId,itemId,"weapon"),"新手短剑无法转移并装备给主角");
                var equipped=BaseEquipmentCrafting.Quote(state,itemId,1);
                Need(equipped!=null,"城镇铁匠未接受穿戴装备");
                Unchanged(state,()=>BaseEquipmentCrafting.Confirm(state,quote),"换装后旧报价不得重铸");
                float beforeAttack=BasePersonalEquipment.Stats(player).Damage;
                var upgrade=state.Copy();
                BasePartyInventory.Find(upgrade,itemId).Gear.Quality++;
                Need(BasePersonalEquipment.Stats(upgrade.Leaders.Single(l=>l.Id==state.PlayerLeaderId)).Damage>beforeAttack,"新手短剑升档应改变实际攻击");
                int available=BasePartyInventory.AvailableMaterial(state,BaseEquipmentCrafting.Stone);
                Need(BaseEquipmentCrafting.Confirm(state,equipped)
                    &&BasePartyInventory.AvailableMaterial(state,BaseEquipmentCrafting.Stone)==available-1,
                    "重铸未结算随身锻造石");
                int resultQuality=BasePartyInventory.Find(state,itemId).Quality;
                Need(Math.Sign(BasePersonalEquipment.Stats(player).Damage-beforeAttack)==Math.Sign(resultQuality-equipped.Quality),"重铸后的攻击应与实际品质一同变化");
                Unchanged(state,()=>BaseEquipmentCrafting.Confirm(state,equipped),"同一报价不得重复重铸");
                BaseEconomy.Validate(state);
                Debug.Log("[VerifyDemoTrade] 指定收货、满包／报价拒绝、开局短剑拦截、旧档保留、城镇重铸与属性变化 PASS");
            }
            finally{BaseInteraction.Facing=originalFacing;}
        }

        private static void VerifyMerchantBeforeCommission(BaseState state)
        {
            Need(state.Demo.AcceptedDay==0,"老莫交易检查需在接取领主委托之前");
            var oldFacing=BaseInteraction.Facing;
            try
            {
                var merchant=BaseSpatial.Objects(state).Single(o=>o.Id=="town_peddler");
                state.PlayerX=merchant.X;
                state.PlayerY=merchant.Y-1.2;
                BaseInteraction.Walk(new BasePoint(0,1));
                Need(BaseSpatial.Walkable(state,state.PlayerX,state.PlayerY)
                    && BaseInteraction.At(state,"town","town_peddler") && !BaseInteraction.At(state,"town","town_market"),
                    "复现点需要够得着老莫而够不到摊位");
                foreach(var good in state.Town.Goods.ToArray())
                {
                    int owned=BaseTown.Owned(state,good.Id), remaining=good.Remaining, coins=state.Inventory.Coins;
                    var quote=BaseTown.QuoteTrade(state,good.Id,true,1);
                    Need(BaseTown.Confirm(state,quote)&&BaseTown.Owned(state,good.Id)==owned+1&&good.Remaining==remaining-1&&
                        state.Inventory.Coins==coins-quote.Total,"从老莫本人买入资源失败："+good.Id);
                    Unchanged(state,()=>BaseTown.Confirm(state,quote),"同一资源报价不能重复扣款");
                }
                int stones=BasePartyInventory.AvailableMaterial(state,BaseEquipmentCrafting.Stone);
                int stonePrice=BaseEquipmentCrafting.BuyPrice(state), purse=state.Inventory.Coins;
                Need(BaseEquipmentCrafting.Buy(state,1,state.Backpack.Revision)&&state.Inventory.Coins==purse-stonePrice&&
                    BasePartyInventory.AvailableMaterial(state,BaseEquipmentCrafting.Stone)==stones+1,"从老莫本人购买锻造石失败");
                foreach(string id in new[]{BaseWine.Strong,BaseWine.Sake})
                {
                    purse=state.Inventory.Coins;
                    int owned=BasePartyInventory.AvailableMaterial(state,id), price=BaseProgressSupplies.Price(state,id);
                    Need(BaseProgressSupplies.Buy(state,id,1,state.Backpack.Revision)&&state.Inventory.Coins==purse-price&&
                        BasePartyInventory.AvailableMaterial(state,id)==owned+1,"从老莫本人购买酒失败："+id);
                }
                var pending=BaseTown.QuoteTrade(state,"food",true,1);
                At(state,"travel_base");
                Need(BaseCampaign.Travel(state,"base"),"离开商铺检查未能回营");
                BaseEconomy.Validate(state);
                Unchanged(state,()=>BaseTown.Confirm(state,pending),"离开灰岩镇不得使用旧资源报价");
                Unchanged(state,()=>BaseEquipmentCrafting.Buy(state,1,state.Backpack.Revision),"离开灰岩镇不得继续购买锻造石");
                Unchanged(state,()=>BaseProgressSupplies.Buy(state,BaseWine.Sake,1,state.Backpack.Revision),"离开灰岩镇不得继续购买酒");
                Debug.Log("[VerifyDemoTrade] 老莫本人且够不到摊位、领主委托前资源和随身补给交易、离场拒绝 PASS");
            }
            finally { BaseInteraction.Facing=oldFacing; }
        }

        private static void Unchanged(BaseState state,Func<bool> operation,string reason)
        {
            string before=JsonUtility.ToJson(state);
            Need(!operation()&&JsonUtility.ToJson(state)==before,reason);
        }

        private static void At(BaseState state,string id)
        {
            var target=BaseSpatial.Objects(state).Single(o=>o.Id==id);
            var point=target.Blocks?BaseStageWalk.Door(state,id):new BasePoint(target.X,target.Y);
            state.PlayerX=point.X;
            state.PlayerY=point.Y;
            BaseInteraction.Walk(new BasePoint(target.X-point.X,target.Y-point.Y));
            BaseAdventure.Arrive(state);
        }
    }
}

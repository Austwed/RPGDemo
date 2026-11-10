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
                var smith=BaseSpatial.Objects(state).First(o=>o.Id=="town_smith"||o.Id=="town_blacksmith");
                At(state,smith.Id);
                Need(BasePersonalEquipment.AtSmith(state),"未在铁匠交互范围");
                state.Inventory.Coins=Math.Max(10000,state.Inventory.Coins);
                state.Demo.Flow.NoviceShop=true;

                string recipient=BaseAdventure.Members(state).First(l=>l.Id!=state.PlayerLeaderId).Id;
                string template=BasePersonalEquipment.ShopFor(state).First(o=>o.Slot=="weapon"||o.Slot=="armor").Id;
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
                var quote=BaseEquipmentCrafting.Quote(state,itemId,1);
                Need(quote!=null,"城镇铁匠未接受随行装备");
                var warehouse=state.Copy();
                var stored=BasePartyInventory.Find(warehouse,itemId).Gear;
                stored.Holder="";stored.Cell=-1;
                Need(BaseEquipmentCrafting.Quote(warehouse,itemId,1)==null,"铁匠不能重铸仓库装备");
                var leftTown=state.Copy();leftTown.Campaign.Location="base";
                Unchanged(leftTown,()=>BaseEquipmentCrafting.Confirm(leftTown,quote),"离开铁匠不得消耗材料");

                int available=BasePartyInventory.AvailableMaterial(state,BaseEquipmentCrafting.Stone);
                Need(BaseEquipmentCrafting.Confirm(state,quote)
                    &&BasePartyInventory.AvailableMaterial(state,BaseEquipmentCrafting.Stone)==available-1,
                    "重铸未结算随身锻造石");
                Unchanged(state,()=>BaseEquipmentCrafting.Confirm(state,quote),"同一报价不得重复重铸");
                BaseEconomy.Validate(state);
                Debug.Log("[VerifyDemoTrade] 指定收货、满包／报价拒绝、城镇重铸与消耗 PASS");
            }
            finally{BaseInteraction.Facing=originalFacing;}
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

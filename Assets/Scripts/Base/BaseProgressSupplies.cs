using System;
using System.Collections.Generic;
using System.Linq;
using BaseConquest.Battle;
namespace BaseConquest.Base
{
    [Serializable] public sealed class BaseProgressOffer
    {
        public string Item;
        public int Remaining;
        public BaseProgressOffer Copy()=>(BaseProgressOffer)MemberwiseClone();
    }
    public static class BaseProgressSupplies
    {
        private static readonly string[] Wine={BaseWine.Strong,BaseWine.Sake};
        public static readonly string[] Items=Wine.Concat(new[]{SoldierKind.Melee,SoldierKind.Ranged,SoldierKind.Cavalry}
            .Select(kind=>BaseTraining.Book(kind,2))).ToArray();
        private static string[] StockItems(BaseState s)=>BaseDemoDefense.Won(s)?Items:Wine;
        public static int Price(BaseState s,string id)=>BaseRpg.Active(s)?BaseRpg.Price(s,BaseRpg.Value(s,id),true):Price(id);
        public static int Price(string id)=>id.EndsWith("_advanced")?80:id.StartsWith("training_")?20:id==BaseWine.Strong?20:15;
        public static int Stock(string id)=>id.EndsWith("_advanced")?5:id.StartsWith("training_")?10:3;
        public static void Normalize(BaseState s)
        {
            var g=s.PersonalEquipment;if(g==null)return;
            var items=StockItems(s);
            bool shaped=g.Supplies!=null&&g.Supplies.Count==items.Length&&g.Supplies.All(o=>o!=null&&items.Contains(o.Item))&&g.Supplies.Select(o=>o.Item).Distinct().Count()==items.Length;
            if(g.SuppliesVersion==1&&shaped)return;
            var previous=g.SuppliesVersion==1?g.Supplies:null;
            g.SuppliesVersion=1;
            g.Supplies=items.Select(id=>previous?.FirstOrDefault(o=>o?.Item==id)??new BaseProgressOffer {Item=id,Remaining=Stock(id)}).ToList();
        }
        public static void Validate(BaseState s)
        {var g=s.PersonalEquipment;var items=StockItems(s);if(g.SuppliesVersion!=1||g.Supplies==null||g.Supplies.Count!=items.Length||g.Supplies.Any(o=>o==null||!items.Contains(o.Item)||o.Remaining<0||o.Remaining>Stock(o.Item))||g.Supplies.Select(o=>o.Item).Distinct().Count()!=items.Length)throw new ArgumentException("补给库存非法");}
        public static void Refill(BaseState s){if(s.PersonalEquipment.Supplies!=null)foreach(var o in s.PersonalEquipment.Supplies)o.Remaining=Stock(o.Item);}
        public static int MaxBuy(BaseState s,string id)
            => BaseTown.CanOpenMarket(s) ? PreviewMaxBuy(s,id) : 0;
        internal static int PreviewMaxBuy(BaseState s,string id)
        {
            var offer=s.PersonalEquipment.Supplies.FirstOrDefault(o=>o.Item==id);
            if(offer==null||s.Backpack.Revision>=int.MaxValue-1||s.Backpack.NextId>=int.MaxValue-1)return 0;
            int room=BaseBackpack.Room(s,id);
            return Math.Min(room,Math.Min(s.Inventory.Coins/Price(s,id),offer.Remaining));
        }
        public static bool Buy(BaseState s,string id,int count,int revision)
        {
            if(revision!=s.Backpack.Revision||count<1||count>MaxBuy(s,id))return false;
            BaseBackpack.Receive(s,id,count);
            s.Inventory.Coins-=count*Price(s,id);s.PersonalEquipment.Supplies.Single(o=>o.Item==id).Remaining-=count;return true;
        }
    }
}

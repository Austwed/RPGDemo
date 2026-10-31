using System;
using System.Linq;
namespace BaseConquest.Base
{
    public sealed class BaseResourceReserve
    {
        public long Food,Materials,Coins;
        public bool FoodLow,MaterialsLow,CoinsLow;
        // 库存预警：按当前人员／配方／生产安排保留三天消耗，不预支未来产出或战利品。
        public static BaseResourceReserve Read(BaseState s)
        {
            var preview=BaseEconomy.Preview(s);
            double progress=BaseConstruction.Progress(s,s.WorkshopProduct).Fraction;
            double daily=Math.Max(0,preview.WorkCapacity+preview.WorkshopFraction-progress);
            long work=BaseEconomy.ProductionPaused(s,"workshop") ? 0 : (long)Math.Floor(progress+daily*3+1e-9);
            var need=new BaseResourceReserve {Food=(long)BaseEconomy.FoodRequired(s)*3,Materials=work*BaseConstruction.MaterialCost(s,s.WorkshopProduct),Coins=0};
            need.FoodLow=s.Inventory.Food<need.Food;need.MaterialsLow=s.Inventory.Materials<need.Materials;need.CoinsLow=s.Inventory.Coins<need.Coins;return need;
        }
        public static string Label(string name,int stock,bool low)=>low ? "<color=#FF6B6B>"+name+" "+stock+"</color>" : name+" "+stock;
    }
}

using System;
using System.Linq;
using BaseConquest.Battle;

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
        private void OpenPersonalShop()
        {
            var s=owner.State;BeginPanel(BaseDemoFlow.Active(s)&&!s.Demo.Flow.NoviceShop?"基础装备 · 每日限量":"装备 · 每日限量","personal-shop");RowButton("返回交易",-210,400,OpenEquipmentTradeHome);RowButton("查看个人装备",210,400,OpenPersonalEquipment);row+=62;
            Line("钱币 "+s.Inventory.Coins+" · 一般品质 · 购买后放入随行成员有空位的背包；非战斗时均可换装",58,20);
            foreach(var d in BasePersonalEquipment.ShopFor(s).Where(d=>!BaseDemoFlow.Active(s)||!d.Id.StartsWith("novice_")||s.Demo.Flow.NoviceShop))
            {string id=d.Id;var offer=s.PersonalEquipment.Offers.Single(o=>o.Template==id);RowButton("购买 "+d.Name+" · "+BasePersonalEquipment.BuyPrice(s,id)+"币 · 今日剩余 "+offer.Remaining+"/1",0,820,()=>GearChange(next=>BasePersonalEquipment.Buy(next,id),"已购入"+d.Name,OpenPersonalShop),BasePersonalEquipment.CanBuy(s,id));row+=56;GearItemLines(new BasePersonalItem {Template=id,Quality=2});}
            EndPanel();
        }
    }
}

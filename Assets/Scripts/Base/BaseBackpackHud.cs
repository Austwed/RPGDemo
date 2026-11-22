using System;
using System.Linq;
using BaseConquest.Battle;

namespace BaseConquest.Base
{
    public sealed partial class BaseHud
    {
        public void ToggleBackpack()
        {
            if(owner.State==null||owner.Busy||owner.State.Aftermath.GameOver||BaseBattleGateway.MainMenu)return;
            if(!BasePartyInventory.Peaceful(owner.State)){owner.ReportMessage("战斗中不能打开背包");return;}
            if(PanelOpen&&activeObject=="backpack"){ClosePanel();return;}
            OpenBackpack();
        }
        public void OpenBackpack()
        {
            if (!GuardPendingRosterChanges(OpenBackpack)) OpenInventory(false);
        }
        private void OpenBagItem(string id){inventorySelected=id;OpenInventory(false);}
        private void FacilityUpgrade(string id,Action refresh)
        {
            var s=owner.State;string problem=BaseConstruction.UpgradeProblem(s,id);
            BaseUpgradeQuote q=null;if(problem=="")try{q=BaseConstruction.Preview(s,id);}catch(Exception e){problem=e.Message;}
            RowButton(id=="fieldworks" ? "解锁固定工事" : "升级至2级",0,800,()=>{owner.Change(next=>{if(!BaseConstruction.Confirm(next,q))throw new InvalidOperationException("条件已变化，请重新查看升级");},"升级／解锁完成，营造图册消耗1本");owner.RefreshConstructionMap();refresh();},q!=null);row+=62;
            if(id=="fieldworks")
            {
                RowButton("开启建造模式",0,820,owner.ArmConstruction,s.Construction.FieldworksUnlocked);row+=62;
                Line("固定掩体：两处指定工程位，可通行、远程伤害×0.75；解锁后不能拆除。",85,21);
                Line(s.Construction.FieldworksUnlocked ? "固定工事已解锁；普通路障可自由选格建造。" : "解锁固定工事，同时开放普通路障营造。",65,20);
            }
            else
            {
                var f=s.Muster.Facilities.Single(x=>x.FacilityId==id);Line("当前 "+f.Level+"级 · 岗位 "+f.Capacity+"人；升级后增加2个岗位。",65,22);
                if(id=="workshop")Line("解锁二级剑／弓／盔甲生产，各需两倍原料与工作量。",65,20);
            }
            Line("需要：营造图册 ×1 ＋ 原料10 ＋ 钱币20",60,21);
            Line("持有图册 "+BaseBackpack.Count(s,BaseBackpack.Blueprint)+" · 原料 "+s.Inventory.Materials+" · 钱币 "+s.Inventory.Coins,55,20);
            Line(problem=="" ? "剧情条件已满足；确认后一次扣除，不会回收或重发。" : problem,80,20);
        }
    }
}

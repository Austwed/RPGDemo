using System;
using System.Linq;
using BaseConquest.Battle;

namespace BaseConquest.Base
{
    [Serializable] public sealed class BaseWineStatus
    {
        public string Kind="", BattleId="";
        public int Day;
        public BaseWineStatus Copy()=>(BaseWineStatus)MemberwiseClone();
    }
    public static class BaseWine
    {
        public const string Strong="strong_wine",Sake="sake";
        public static void Normalize(BaseState s)
        {
            foreach(string id in new[]{Strong,Sake})if(!s.Backpack.Catalog.Any(d=>d.Id==id))s.Backpack.Catalog.Add(new BaseBagDefinition {Id=id,Name=id==Strong?"一箱烈酒":"一箱清酒",Category="consumable",MaxStack=99,Description=id==Strong?"辣嗓子的烧刀子。喝下后全队今日血气暴涨，睡一觉酒劲便散。":"封坛慢酿的米酒，后劲绵长。提前温上一壶，留待下一场硬仗发力。"});
        }
        public static void Validate(BaseState s)
        {
            foreach(var l in s.Leaders){var w=l.Wine;if(w==null||!new[]{"",Strong,Sake}.Contains(w.Kind)||w.Day<0||w.Day>s.Day||w.Kind==Strong&&w.Day!=s.Day||w.Kind==""&&(w.Day!=0||w.BattleId!="")||w.BattleId!=""&&(!s.Campaign.HasActiveBattle||s.Campaign.ActiveBattle.Id!=w.BattleId))throw new ArgumentException("酒类效果期限或绑定非法");}
        }
        public static bool CanUse(BaseState s,string stack,string leader,bool replace=false)
        {
            var i=s.Backpack.Items.SingleOrDefault(x=>x.Id==stack);var l=s.Leaders.SingleOrDefault(x=>x.Id==leader);
            return i!=null&&new[]{Strong,Sake}.Contains(i.ItemId)&&BasePartyInventory.CanUseMaterial(s,i)&&BasePartyInventory.Peaceful(s)&&!BaseBattleLoot.Pending(s)&&BaseAdventure.Contains(s,leader)&&l!=null&&BaseAdventure.Alive(l)&&l.Health>0&&s.Backpack.Revision<int.MaxValue-1&&
                (l.Wine.Kind==""||i.ItemId==Strong&&l.Wine.Kind==Sake&&replace);
        }
        public static bool Use(BaseState s,string stack,string leader,int revision,bool replace=false)
        {
            if(revision!=s.Backpack.Revision||!CanUse(s,stack,leader,replace))return false;
            var i=s.Backpack.Items.Single(x=>x.Id==stack);var l=s.Leaders.Single(x=>x.Id==leader);l.Wine=new BaseWineStatus {Kind=i.ItemId,Day=s.Day};
            if(--i.Count==0)s.Backpack.Items.Remove(i);s.Backpack.Revision++;BasePartyInventory.RefreshPreparation(s);return true;
        }
        public static void Apply(BattleSoldier unit,BaseLeader l,int day)
        {
            var w=l.Wine;unit.DrinkMultiplier=w?.Kind==Strong&&w.Day==day?1.10f:w?.Kind==Sake?1.05f:1;
            if((l.Rpg?.Trait("drunkard")??0)>0)unit.DrinkMultiplier=1+(unit.DrinkMultiplier-1)*(float)l.Rpg.Trait("drunkard");
            unit.DrinkLabel=unit.DrinkMultiplier>1?(w.Kind==Strong?"烈酒（当日攻击＋":"清酒（本场攻击＋")+((unit.DrinkMultiplier-1)*100).ToString("0.##")+"%）":"";
        }
        public static void Bind(BaseState s,BaseCampaignSession session)
        {
            if(session.Battle.Phase==BattlePhase.Deployment)return;
            foreach(var snapshot in session.Opening.Leaders){var l=s.Leaders.Single(x=>x.Id==snapshot.Id);if(l.Wine.Kind==Sake&&l.Wine.BattleId=="")l.Wine.BattleId=session.Opening.Id;}
        }
        public static void Finish(BaseState s,BaseCampaignSession session)
        {foreach(var snapshot in session.Opening.Leaders){var l=s.Leaders.Single(x=>x.Id==snapshot.Id);if(l.Wine.Kind==Sake)l.Wine=new BaseWineStatus();}}
        public static void AdvanceDay(BaseState s){foreach(var l in s.Leaders)if(l.Wine.Kind==Strong)l.Wine=new BaseWineStatus();}
    }
}

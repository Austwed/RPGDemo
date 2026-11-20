using System;
using System.Collections.Generic;
using System.Linq;
using BaseConquest.Battle;
namespace BaseConquest.Base
{
    [Serializable] public sealed class BaseLostItems
    {
        public string Id, EventId, SourceSiteId="",Title="";
        public bool Temporary, Returned;
        public double WorldX=8,WorldY=7;
        public List<BasePersonalItem> Gear=new List<BasePersonalItem>();
        public List<BaseBagItem> Stacks=new List<BaseBagItem>();
        public BaseLostItems Copy()=>new BaseLostItems {Id=Id,EventId=EventId,SourceSiteId=SourceSiteId,Title=Title,Temporary=Temporary,Returned=Returned,WorldX=WorldX,WorldY=WorldY,Gear=Gear.Select(i=>i.Copy()).ToList(),Stacks=Stacks.Select(i=>i.Copy()).ToList()};
    }
    public static class BaseItemRecovery
    {
        private static IEnumerable<BaseEnemyMember> LiveEnemies(BaseState s)=>s.Campaign.EnemySites.SelectMany(site=>site.Garrison).Concat(s.Campaign.Events.Where(e=>e.Status=="pending").SelectMany(e=>e.EnemyArmy)).GroupBy(e=>e.Id).Select(g=>g.First());
        public static void Normalize(BaseState s)
        {
            s.Aftermath.LostItems??=new List<BaseLostItems>();
            foreach(var e in LiveEnemies(s))
            {
                bool empty=e.Equipment==null||e.Equipment.Version==0&&e.Equipment.Items?.Count>0==false&&string.IsNullOrEmpty(e.Equipment.Weapon);
                if(e.Kind!=SoldierKind.Hero){if(empty)e.Equipment=null;else throw new ArgumentException("普通士兵不使用个人装备槽");continue;}
                if(empty)
                {
                    string weapon=BaseRpg.Weapon(BaseRpg.Profession(e.Stats.HasProfessionKind?e.Stats.ProfessionKind:e.Stats.HasCombatKind?e.Stats.CombatKind:SoldierKind.Melee));
                    e.Equipment=new BasePersonalLoadout {Version=1,Weapon=weapon,InitialWeapon=weapon,BaseStats=e.Stats.Clone()};
                    foreach(string slot in new[]{"weapon","helmet","armor"})
                    {if(s.PersonalEquipment.NextId>=int.MaxValue-401)throw new ArgumentException("装备身份已达上限");e.Equipment.Items.Add(new BasePersonalItem {Id="personal:"+s.PersonalEquipment.NextId++,Template=slot=="weapon"?"basic_"+weapon:"basic_"+slot,Slot=slot});}
                }
                if(e.Equipment.Version!=1||e.Equipment.BaseStats==null||e.Equipment.Items==null||!new[]{"sword","bow","spear","staff"}.Contains(e.Equipment.Weapon)||e.Equipment.Items.Any(i=>i==null||BasePersonalEquipment.Definition(i.Template)==null||i.Quality<0||i.Quality>5)||e.Equipment.Items.Select(i=>i.Slot).Distinct().Count()!=e.Equipment.Items.Count)throw new ArgumentException("敌方装备配置非法");
                e.Stats=BasePersonalEquipment.Stats(new BaseLeader {Rpg=e.Rpg,PersonalEquipment=e.Equipment,Training=null});e.Health=Math.Min(e.Health,e.Stats.Health);
            }
        }
        public static void Validate(BaseState s)
        {
            var all=s.Aftermath.LostItems;
            if(all.Count>1000||all.Any(x=>x==null||string.IsNullOrWhiteSpace(x.Id)||string.IsNullOrWhiteSpace(x.EventId)||x.Gear==null||x.Stacks==null||!s.Campaign.Events.Any(e=>e.Id==x.EventId)||x.Returned&&(x.Gear.Count>0||x.Stacks.Count>0)||!BaseMap.Walkable(x.WorldX,x.WorldY,null,false))||all.Select(x=>x.Id).Distinct().Count()!=all.Count)throw new ArgumentException("遗失清单或反击目标非法");
            var gear=BasePartyInventory.Entries(s).Where(x=>x.Gear!=null).Select(x=>x.Gear).Concat(all.SelectMany(x=>x.Gear)).Concat(LiveEnemies(s).Where(e=>e.Equipment!=null).SelectMany(e=>e.Equipment.Items)).ToArray();
            if(gear.Any(i=>i==null||i.Id==null||!i.Id.StartsWith("personal:")||!int.TryParse(i.Id.Substring(9),out int n)||n<1||n>=s.PersonalEquipment.NextId||BasePersonalEquipment.Definition(i.Template)==null||i.Quality<0||i.Quality>5)||gear.Select(i=>i.Id).Distinct().Count()!=gear.Length)throw new ArgumentException("个人装备在仓库、敌人或遗失清单中重复／非法");
            var pending = s.Aftermath.Loot.SelectMany(m => m.Items).Where(i => i.Status == "pending" && !string.IsNullOrEmpty(i.CapturedId)).ToArray();
            var ids = gear.Select(i => i.Id).ToHashSet();
            if (pending.Any(i => !i.CapturedId.StartsWith("personal:",StringComparison.Ordinal) ||
                !int.TryParse(i.CapturedId.Substring(9),out int n) || n < 1 || n >= s.PersonalEquipment.NextId || !ids.Add(i.CapturedId)))
                throw new ArgumentException("待领取装备占用的实例编号非法或重复");
            var stacks=s.Backpack.Items.Concat(all.SelectMany(x=>x.Stacks)).ToArray();if(stacks.Any(i=>i==null||i.Id==null||!i.Id.StartsWith("bag:")||!int.TryParse(i.Id.Substring(4),out int n)||n<1||n>=s.Backpack.NextId||!s.Backpack.Catalog.Any(d=>d.Id==i.ItemId&&i.Count>0&&i.Count<=d.MaxStack))||stacks.Select(i=>i.Id).Distinct().Count()!=stacks.Length)throw new ArgumentException("遗失背包物品重复／非法");
            if(all.Any(x=>x.Gear.Any(i=>i.Slot!=""||i.Holder!=""||i.Cell!=-1)||x.Stacks.Any(i=>i.Holder!=""||i.Cell!=-1)))throw new ArgumentException("遗失物不得同时占人物格子或穿戴槽");
            foreach(var e in s.Campaign.Events.Where(e=>e.Id.StartsWith("recovery:")&&e.Status=="pending"))if(e.EnemyArmy==null||e.EnemyArmy.Count>93||!e.EnemyArmy.Any(u=>u.Kind==SoldierKind.Hero)||e.EnemyArmy.Any(u=>u==null||string.IsNullOrEmpty(u.Id)||!BaseAftermath.ValidStats(u.Stats)||float.IsNaN(u.Health)||float.IsInfinity(u.Health)||u.Health<=0||u.Health>u.Stats.Health)||e.EnemyArmy.Select(u=>u.Id).Distinct().Count()!=e.EnemyArmy.Count)throw new ArgumentException("临时残部实际编制非法");
            foreach(var e in LiveEnemies(s).Where(e=>e.Equipment!=null))if(e.Kind!=SoldierKind.Hero||e.Equipment.Version!=1||!BaseAftermath.ValidStats(e.Equipment.BaseStats)||e.Equipment.Items.Any(i=>!BasePersonalEquipment.Slots.Contains(i.Slot)||i.Template=="captains_ring"))throw new ArgumentException("敌人个人装备或裸属性非法");
        }
        internal static SoldierStats Disarm(BaseState s,BaseCampaignSession session,BattleSoldier unit,List<BasePersonalItem> captured)
        {
            var origin=session.Opening.Enemies.FirstOrDefault(e=>session.UnitForEnemy(session.Opening.Enemies.IndexOf(e))?.Id==unit.Id);
            if(unit.Kind!=SoldierKind.Hero)return (origin?.Stats??unit.Stats).Clone();
            if(origin?.Equipment==null)return unit.Stats.Clone();
            foreach (var source in origin.Equipment.Items)
            {
                var item = source.Copy();
                item.Slot = item.Holder = "";
                item.Cell = -1;
                captured.Add(item);
            }
            var live=LiveEnemies(s).FirstOrDefault(e=>e.Id==origin.Id);if(live?.Equipment!=null){live.Equipment.Items.Clear();live.Stats=origin.Equipment.BaseStats.Clone();live.Health=Math.Min(live.Health,live.Stats.Health);}
            return origin.Equipment.BaseStats.Clone();
        }
        public static void Record(BaseState s,BaseCampaignSession session,BaseCampaignEvent battleEvent,BaseCampaignReport report,bool win)
        {
            if(s.Aftermath.GameOver||s.Aftermath.LostItems.Any(x=>x.Id==session.Opening.Id))return;
            var loss=new BaseLostItems {Id=session.Opening.Id,Title=report.Title};int recovered=0;
            foreach(var l in s.Leaders.Where(l=>session.Opening.Leaders.Any(x=>x.Id==l.Id)&&(win?l.Fate=="dead":l.Fate=="dead"||l.Fate=="missing")))
            {
                var items=l.PersonalEquipment.Items.Concat(s.PersonalEquipment.Warehouse.Where(i=>i.Holder==l.Id)).ToArray();l.PersonalEquipment.Items.Clear();
                foreach(var item in items){s.PersonalEquipment.Warehouse.Remove(item);item.Slot="";item.Holder="";item.Cell=-1;if(win)s.PersonalEquipment.Warehouse.Add(item);else loss.Gear.Add(item);recovered++;}
                foreach(var stack in s.Backpack.Items.Where(i=>i.Holder==l.Id).ToArray()){stack.Holder="";stack.Cell=-1;if(!win){s.Backpack.Items.Remove(stack);loss.Stacks.Add(stack);}recovered++;}
                l.MaxHealth=BasePersonalEquipment.Stats(l).Health;l.Health=Math.Min(l.Health,l.MaxHealth);
            }
            if(win){if(recovered>0)report.RecoveryNote+=(report.RecoveryNote!=""?"；":"")+"阵亡者遗物已运回仓库："+recovered+"项";ReturnForEvent(s,battleEvent.Id,report);return;}
            if(loss.Gear.Count+loss.Stacks.Count==0)return;
            var site=BaseThreats.Site(s,battleEvent.SourceSiteId);if(site?.Owner!="enemy")site=s.Campaign.EnemySites.FirstOrDefault(x=>x.Owner=="enemy");
            if(site!=null){loss.SourceSiteId=site.Id;loss.EventId=site.AttackEventId;}
            else if(battleEvent.Id.StartsWith("recovery:"))
            {var previous=s.Aftermath.LostItems.First(x=>x.EventId==battleEvent.Id);loss.Temporary=true;loss.EventId=battleEvent.Id;loss.WorldX=previous.WorldX;loss.WorldY=previous.WorldY;}
            else
            {
                loss.Temporary=true;loss.EventId="recovery:"+loss.Id;loss.WorldX=4+(s.Aftermath.LostItems.Count%5)*3;loss.WorldY=3+(s.Aftermath.LostItems.Count/5%4)*2;
                var force=session.Opening.Enemies.Where((e,n)=>session.UnitForEnemy(n)?.InstantDead!=true).Select(e=>e.Copy()).ToList();
                if(!force.Any(e=>e.Kind==SoldierKind.Hero))force.Add(new BaseEnemyMember {Id=loss.EventId+":chief",Name="残部队长",Kind=SoldierKind.Hero,Stats=new SoldierStats {Health=200,Damage=20},Health=200});
                s.Campaign.Events.Add(new BaseCampaignEvent {Id=loss.EventId,Title="临时敌军残部 · 夺回遗失物",Kind="attack",ScenarioFile="24_据点进攻.json",AvailableDay=s.Day,DeadlineDay=s.Day,FoodReward=0,MaterialReward=0,CoinReward=0,FoodRaid=0,MaterialRaid=0,CoinRaid=0,ItemRewards=new List<BaseLootReward>(),EnemyArmy=force});
            }
            s.Aftermath.LostItems.Add(loss);report.RecoveryNote="遗失物"+recovered+"项，反击目标："+(loss.Temporary?"临时敌军残部":site.Name);s.Backpack.Revision++;
        }
        public static void ReturnForEvent(BaseState s,string eventId,BaseCampaignReport report)
        {
            int count=0;foreach(var loss in s.Aftermath.LostItems.Where(x=>!x.Returned&&x.EventId==eventId))
            {s.PersonalEquipment.Warehouse.AddRange(loss.Gear);s.Backpack.Items.AddRange(loss.Stacks);count+=loss.Gear.Count+loss.Stacks.Count;loss.Gear.Clear();loss.Stacks.Clear();loss.Returned=true;}
            if(count>0){report.RecoveryNote+=(report.RecoveryNote!=""?"；":"")+"反击夺回"+count+"项，已运回基地仓库";s.Backpack.Revision++;}
        }
        public static IEnumerable<BaseMapObject> Markers(BaseState s,string region)
        {if(region!="world"||s.Aftermath?.LostItems==null)return Enumerable.Empty<BaseMapObject>();return s.Aftermath.LostItems.Where(x=>x.Temporary&&!x.Returned).Select(x=>new BaseMapObject("recovery_marker:"+x.Id,"反击残部 · 夺回遗失物",x.WorldX,x.WorldY,1,1,false));}
    }
}

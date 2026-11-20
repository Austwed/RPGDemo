using System;
using System.Collections.Generic;
using System.Linq;
using BaseConquest.Battle;

namespace BaseConquest.Base
{
    public sealed class BaseInventoryEntry
    {
        public string Id, Name, Category, Holder, Slot, Bound, Description;
        public int Cell, Count, Quality;
        public BasePersonalItem Gear;
        public BaseBagItem Stack;
        public bool Worn=>!string.IsNullOrEmpty(Slot);
        public bool Protected;
    }
    public sealed class InventoryClickTracker
    {
        private string previous;
        private double time;
        public void Reset(){previous=null;}
        public bool Click(string id,double now)
        {
            bool twice=!string.IsNullOrEmpty(id)&&id==previous&&now>=time&&now-time<=.35;
            previous=twice ? null : id;time=now;return twice;
        }
    }
    public static class BasePartyInventory
    {
        public const int Capacity=20;
        public static bool QuickTransfer(BaseState s,string id,string person,bool visible)
        {
            var e=Find(s,id);
            if(!visible||e==null||e.Worn||!AtWarehouse(s)||!People(s,true).Any(l=>l.Id==person))return false;
            string target=e.Holder=="" ? person : "";
            return Move(s,id,target,target=="" ? -1 : FreeCell(s,target));
        }

        public static bool Peaceful(BaseState s)=>!s.Aftermath.GameOver&&(!BaseDemoPrologue.Village(s)||BaseDemoIntro.Daily(s))&&string.IsNullOrEmpty(s.SceneBattleId)&&(!s.Campaign.HasActiveBattle||!s.Campaign.ActiveBattle.HasCheckpoint||s.Campaign.ActiveBattle.Checkpoint.Phase==BattlePhase.Deployment);
        public static bool AccessibleLeader(BaseState s,BaseLeader l) => BaseAdventure.Alive(l) && !l.Relocating &&
            BaseAdventure.PauseFor(s,l.Id) == null && (BaseAdventure.Following(s,l.Id) ||
            !l.Away && (s.Campaign.Location == "base" || BaseSettlements.AtVillage(s)) && l.VillageResident == BaseSettlements.AtVillage(s));
        public static IEnumerable<BaseLeader> People(BaseState s,bool warehouse)=>warehouse?s.Leaders.Where(l=>AccessibleLeader(s,l)):BaseAdventure.Members(s).Where(l=>BaseAdventure.Alive(l));
        public static BaseMapObject WarehouseObject(BaseState s)
        {
            if(BaseDemo.Active(s))return BaseSpatial.Objects(s,BaseSettlements.AtVillage(s) ? BaseDemoFinale.Home : "base").Single(obj=>obj.Id=="home");
            var authored=BaseSpatial.Objects(s,"base").FirstOrDefault(o=>o.Id=="warehouse");if(authored!=null)return authored;
            var p=BaseSpatial.Nearest(s,new BasePoint(7.5,7.5),"base",new BaseMapObject[0]);return new BaseMapObject("warehouse","基地仓库",p.X,p.Y,.8,.8,false);
        }
        public static bool AtWarehouse(BaseState s)=>Peaceful(s)&&(s.Campaign.Location=="base" || BaseSettlements.AtVillage(s) && BaseDemoFinale.HomeRepaired(s,"home"))&&BaseInteraction.CanReach(s,WarehouseObject(s));
        public static IEnumerable<BaseInventoryEntry> Entries(BaseState s)
        {
            string practiceId=s.Demo?.Intro?.PracticeId;
            foreach(var l in s.Leaders.Where(l=>l.PersonalEquipment?.Version==1))foreach(var i in l.PersonalEquipment.Items)yield return GearEntry(i,l.Id,i.Slot,practiceId);
            foreach(var i in s.PersonalEquipment.Warehouse)yield return GearEntry(i,i.Holder??"","",practiceId);
            foreach(var i in s.Backpack.Items)
            {
                var d=s.Backpack.Catalog.Single(x=>x.Id==i.ItemId);
                bool royalSword=i.ItemId==BaseBackpack.RoyalSword;
                yield return new BaseInventoryEntry {Id=i.Id,Name=royalSword?BaseBackpack.RoyalSwordName:d.Name,Category=d.Category,
                    Holder=i.Holder??"",Cell=i.Cell,Slot="",Count=i.Count,Stack=i,Bound=d.ExclusiveLeaderId,Protected=d.Category=="quest",
                    Description=royalSword?BaseBackpack.RoyalSwordDescription:i.ItemId==BaseEquipmentCrafting.Stone?BaseEquipmentCrafting.DescribeStone(d.Description):d.Description};
            }
        }
        private static BaseInventoryEntry GearEntry(BasePersonalItem i,string holder,string slot,string practiceId)
        {
            var d=BasePersonalEquipment.Definition(i.Template);
            bool practice=i.Id==practiceId;
            return new BaseInventoryEntry {Id=i.Id,Name=practice?"旧制短剑":BasePersonalEquipment.Name(i),
                Category=d.Slot=="weapon"?"weapon":d.Slot=="accessory"?"accessory":"armor",Holder=holder,Cell=i.Cell,Slot=slot,
                Count=1,Quality=i.Quality,Gear=i,Bound=i.BoundLeader,Protected=i.Template=="captains_ring",
                Description=practice?BaseConquest.Story.StoryText.Get("story.item.practice_sword.description",
                    "搭档重新缠过青绳。剑刃虽旧，握着仍顺手。"):""};
        }
        public static BaseInventoryEntry Find(BaseState s,string id)=>Entries(s).SingleOrDefault(e=>e.Id==id);
        public static int FreeCell(BaseState s,string holder)=>Enumerable.Range(0,Capacity).Where(n=>!Entries(s).Any(e=>!e.Worn&&e.Holder==holder&&e.Cell==n)).DefaultIfEmpty(-1).First();
        public static bool PartyFreeCell(BaseState s,out string holder,out int cell){foreach(var l in BaseAdventure.Members(s).Where(l=>AccessibleLeader(s,l))){cell=FreeCell(s,l.Id);if(cell>=0){holder=l.Id;return true;}}holder="";cell=-1;return false;}
        public static void Normalize(BaseState s)
        {
            var b=s.Backpack;
            if(b.GridVersion==0){if(b.Items.Any(i=>!string.IsNullOrEmpty(i.Holder)||i.Cell!=-1)||s.PersonalEquipment.Warehouse.Any(i=>!string.IsNullOrEmpty(i.Holder)||i.Cell!=-1))throw new ArgumentException("背包位置数据缺少版本，拒绝覆盖");foreach(var i in b.Items){i.Holder="";i.Cell=-1;}foreach(var i in s.PersonalEquipment.Warehouse){i.Holder="";i.Cell=-1;}b.GridVersion=1;}
            if(b.GridVersion!=1)return;
            // 容量由 24 收到 20（5×4 版式）：旧档第 21～24 格的物品迁回前 20 格，
            // 前 20 格已满则退回基地仓库，物品不丢。Validate 每次都会先跑这里，读档入口也会显式调用。
            var occupied = Entries(s).Where(e => !e.Worn && e.Holder != "")
                .GroupBy(e => e.Holder).ToDictionary(g => g.Key, g => new HashSet<int>(g.Where(e => e.Cell >= 0 && e.Cell < Capacity).Select(e => e.Cell)));
            foreach (var e in Entries(s).Where(e => !e.Worn && e.Holder != "" && e.Cell >= Capacity).OrderBy(e => e.Cell).ToArray())
            {
                HashSet<int> used;
                if (!occupied.TryGetValue(e.Holder, out used)) { used = new HashSet<int>(); occupied[e.Holder] = used; }
                int free = Enumerable.Range(0, Capacity).Where(n => !used.Contains(n)).DefaultIfEmpty(-1).First();
                if (free < 0)
                {
                    if (e.Gear != null) { e.Gear.Holder = ""; e.Gear.Cell = -1; }
                    else { e.Stack.Holder = ""; e.Stack.Cell = -1; }
                }
                else
                {
                    used.Add(free);
                    if (e.Gear != null) e.Gear.Cell = free;
                    else e.Stack.Cell = free;
                }
            }
            var player=s.Leaders.Single(l=>l.Id==s.PlayerLeaderId);
            if(!b.RingPlaced&&!s.Campaign.HasActiveBattle&&!player.Away&&player.PersonalEquipment?.Version==1)
            {
                var worn=player.PersonalEquipment.Items.SingleOrDefault(i=>i.Template=="captains_ring");var ring=worn??s.PersonalEquipment.Warehouse.SingleOrDefault(i=>i.Template=="captains_ring");
                if(ring!=null&&!player.PersonalEquipment.Items.Any(i=>i.Slot=="accessory3"&&i!=ring))
                {if(worn==null){s.PersonalEquipment.Warehouse.Remove(ring);player.PersonalEquipment.Items.Add(ring);}ring.Holder="";ring.Cell=-1;ring.Slot="accessory3";player.MaxHealth=BasePersonalEquipment.Stats(player).Health;player.Health=Math.Min(player.Health,player.MaxHealth);b.RingPlaced=true;}
            }
        }
        public static void Validate(BaseState s)
        {
            Normalize(s);if(s.Backpack.GridVersion!=1)throw new ArgumentException("个人背包版本非法");
            var all=Entries(s).ToArray();
            if(all.Any(e=>!string.IsNullOrEmpty(e.Holder)&&!s.Leaders.Any(l=>l.Id==e.Holder)||e.Category=="quest"&&e.Holder!=""&&e.Holder!=s.PlayerLeaderId||e.Worn&&(e.Gear==null||e.Cell!=-1||!string.IsNullOrEmpty(e.Gear.Holder))||!e.Worn&&(e.Holder==""?e.Cell!=-1:e.Cell<0||e.Cell>=Capacity)||!string.IsNullOrEmpty(e.Bound)&&e.Holder!=""&&e.Holder!=e.Bound))throw new ArgumentException("物品持有位置／格子／专属归属非法");
            if(all.Where(e=>!e.Worn&&e.Holder!="").GroupBy(e=>e.Holder+":"+e.Cell).Any(g=>g.Count()>1)||all.Select(e=>e.Id).Distinct().Count()!=all.Length)throw new ArgumentException("个人物品身份或格子重复");
            foreach(var l in s.Leaders.Where(l=>l.PersonalEquipment?.Version==1))if(l.PersonalEquipment.ExtraAccessory!=(l.Id==s.PlayerLeaderId||BaseDemoPrologue.Village(s)&&l.Id=="demo_captain")||!new[]{"sword","bow","spear","staff"}.Contains(l.PersonalEquipment.InitialWeapon))throw new ArgumentException("人物初始武器／额外饰品槽非法");
        }
        private static bool Access(BaseState s,string holder)=>holder==""?AtWarehouse(s):s.Leaders.Any(l=>l.Id==holder&&AccessibleLeader(s,l));
        private static bool Fits(BaseState s,BaseInventoryEntry e,string holder,string slot)=>e==null||(!(e.Category=="quest"&&holder!=""&&holder!=s.PlayerLeaderId)&&(!string.IsNullOrEmpty(e.Bound)&&holder!=""&&e.Bound!=holder?false:slot==""||e.Gear!=null&&s.Leaders.Any(l=>l.Id==holder&&BasePersonalEquipment.Compatible(l,e.Gear,slot)&&BasePersonalEquipment.WeaponRangeAllowed(l,e.Gear,e.Quality))));
        public static bool MoveToBag(BaseState state, string id, string holder, int count = 0)
        {
            if (string.IsNullOrEmpty(holder)) return Move(state,id,"",-1,"",count);
            var entry = Find(state,id);
            if (entry == null || !Peaceful(state) || !Access(state,entry.Holder) || !Access(state,holder) ||
                !Fits(state,entry,holder,"")) return false;
            int amount = count == 0 ? entry.Count : count;
            if (amount < 1 || amount > entry.Count) return false;
            if (entry.Stack == null || entry.Holder == holder)
                return Move(state,id,holder,BaseBackpack.RequireGearCell(state,holder),"",amount);
            string item = entry.Stack.ItemId;
            if (BaseBackpack.Room(state,item,holder) < amount) throw new BagFullException();
            if (!state.Backpack.Items.Any(stack => stack.Holder == holder && stack.ItemId == item &&
                stack.Count < state.Backpack.Catalog.Single(def => def.Id == item).MaxStack))
                return Move(state,id,holder,BaseBackpack.RequireGearCell(state,holder),"",amount);
            BaseBackpack.Receive(state,item,amount,holder);
            entry.Stack.Count -= amount;
            if (entry.Stack.Count == 0) state.Backpack.Items.Remove(entry.Stack);
            return true;
        }

        public static bool Move(BaseState s,string id,string holder,int cell,string slot="",int count=0)
        {
            var source=Find(s,id);holder=holder??"";slot=slot??"";
            if(s.Backpack.Revision>=int.MaxValue-1||source==null||!Peaceful(s)||!Access(s,source.Holder)||!Access(s,holder)||slot!=""&&holder==""||slot==""&&holder!=""&&(cell<0||cell>=Capacity)||!Fits(s,source,holder,slot))return false;
            var target=Entries(s).SingleOrDefault(e=>e.Holder==holder&&(slot!=""?e.Slot==slot:!e.Worn&&holder!=""&&e.Cell==cell));if(target?.Id==id)return true;
            if(target!=null&&!Fits(s,target,source.Holder,source.Slot))return false;
            int amount=count==0?source.Count:count;if(amount<1||amount>source.Count||source.Worn&&amount!=source.Count)return false;
            if(source.Stack!=null&&target?.Stack!=null&&source.Stack.ItemId==target.Stack.ItemId)
            {int room=s.Backpack.Catalog.Single(d=>d.Id==source.Stack.ItemId).MaxStack-target.Count;amount=Math.Min(amount,room);if(amount<=0)return false;source.Stack.Count-=amount;target.Stack.Count+=amount;if(source.Stack.Count==0)s.Backpack.Items.Remove(source.Stack);s.Backpack.Revision++;return true;}
            if(amount<source.Count){if(target!=null||s.Backpack.NextId>=int.MaxValue-1)return false;var split=source.Stack.Copy();split.Id="bag:"+s.Backpack.NextId++;split.Count=amount;split.Holder=holder;split.Cell=holder==""?-1:cell;source.Stack.Count-=amount;s.Backpack.Items.Add(split);s.Backpack.Revision++;return true;}
            var trial=s.Copy();ApplyMove(trial,id,holder,cell,slot,target?.Id);try{BasePersonalEquipment.Validate(trial);Validate(trial);}catch(ArgumentException){return false;}
            ApplyMove(s,id,holder,cell,slot,target?.Id);return true;
        }
        private static void ApplyMove(BaseState s,string id,string holder,int cell,string slot,string targetId)
        {
            var a=Find(s,id);var b=targetId==null?null:Find(s,targetId);string oldHolder=a.Holder,oldSlot=a.Slot;int oldCell=a.Cell;
            Put(s,a,holder,cell,slot);if(b!=null)Put(s,b,oldHolder,oldCell,oldSlot);
            foreach(var l in s.Leaders.Where(l=>l.PersonalEquipment?.Version==1)){l.MaxHealth=BasePersonalEquipment.Stats(l).Health;l.Health=Math.Min(l.Health,l.MaxHealth);}RefreshPreparation(s);s.Backpack.Revision++;
        }
        internal static void RefreshPreparation(BaseState s)
        {
            if(!s.Campaign.HasActiveBattle||!Peaceful(s))return;var a=s.Campaign.ActiveBattle;
            a.Leaders=a.Leaders.Select(l=>s.Leaders.Single(p=>p.Id==l.Id).Copy()).ToList();a.Scenario.A.CommanderStats=a.Leaders.Select(l=>{var p=BaseCommanderIdentity.Stats(l,a.Scenario.A.Hero);p.Damage*=(float)(a.SupplySnapshot?.AttackFactor??1);return p;}).ToList();
            if(!a.HasCheckpoint)return;a.Checkpoint.Scenario.A.CommanderStats=a.Scenario.A.CommanderStats.Select(p=>p.Clone()).ToList();
            foreach(var u in a.Checkpoint.Units.Where(u=>u.Side==BattleSide.A&&u.Kind==SoldierKind.Hero&&!u.DeveloperSpawned)){u.Stats=a.Scenario.A.CommanderStats[u.OriginCommanderSlot].Clone();u.Health=Math.Min(u.Health,u.Stats.Health);}
            foreach(var u in a.Checkpoint.Units.Where(u=>u.Side==BattleSide.A&&!u.DeveloperSpawned&&u.OriginCommanderSlot>=0&&u.OriginCommanderSlot<a.Leaders.Count))BaseWine.Apply(u,a.Leaders[u.OriginCommanderSlot],a.StartedDay);
        }
        private static void Put(BaseState s,BaseInventoryEntry e,string holder,int cell,string slot)
        {
            if(e.Gear!=null){if(e.Worn)s.Leaders.Single(l=>l.Id==e.Holder).PersonalEquipment.Items.Remove(e.Gear);else s.PersonalEquipment.Warehouse.Remove(e.Gear);e.Gear.Slot=slot;e.Gear.Holder=slot!=""?"":holder;e.Gear.Cell=slot!=""||holder==""?-1:cell;if(slot!="")s.Leaders.Single(l=>l.Id==holder).PersonalEquipment.Items.Add(e.Gear);else s.PersonalEquipment.Warehouse.Add(e.Gear);}
            else{e.Stack.Holder=holder;e.Stack.Cell=holder==""?-1:cell;}
        }
        public static bool Destroy(BaseState s,string id,int count)
        {
            var e=Find(s,id);if(s.Backpack.Revision>=int.MaxValue-1||e==null||e.Worn||e.Protected||!Peaceful(s)||!Access(s,e.Holder)||count<1||count>e.Count)return false;
            if(e.Gear!=null)s.PersonalEquipment.Warehouse.Remove(e.Gear);else{e.Stack.Count-=count;if(e.Stack.Count==0)s.Backpack.Items.Remove(e.Stack);}s.Backpack.Revision++;return true;
        }
        public static bool Sort(BaseState s,string holder)
        {
            if(s.Backpack.Revision>=int.MaxValue-1||!Peaceful(s)||!Access(s,holder))return false;
            foreach(var group in s.Backpack.Items.Where(i=>(i.Holder??"")==holder).GroupBy(i=>i.ItemId).ToArray())
            {int left=group.Sum(i=>i.Count),max=s.Backpack.Catalog.Single(d=>d.Id==group.Key).MaxStack;foreach(var i in group.OrderBy(i=>i.Id,StringComparer.Ordinal).ToArray()){int n=Math.Min(left,max);left-=n;if(n==0)s.Backpack.Items.Remove(i);else i.Count=n;}}
            var rows=Entries(s).Where(e=>e.Holder==holder&&!e.Worn).OrderBy(e=>e.Category,StringComparer.Ordinal).ThenBy(e=>e.Name,StringComparer.Ordinal).ThenByDescending(e=>e.Quality).ThenBy(e=>e.Id,StringComparer.Ordinal).ToArray();
            if(holder==""){s.PersonalEquipment.Warehouse=s.PersonalEquipment.Warehouse.OrderBy(i=>i.Holder==""?0:1).ThenBy(i=>i.Template,StringComparer.Ordinal).ToList();s.Backpack.Items=s.Backpack.Items.OrderBy(i=>i.Holder==""?0:1).ThenBy(i=>i.ItemId,StringComparer.Ordinal).ToList();}
            else for(int i=0;i<rows.Length;i++){if(rows[i].Gear!=null)rows[i].Gear.Cell=i;else rows[i].Stack.Cell=i;}s.Backpack.Revision++;return true;
        }
        public static bool CanUseMaterial(BaseState s,BaseBagItem i)=>string.IsNullOrEmpty(i.Holder)?s.Campaign.Location=="base":s.Leaders.Any(l=>l.Id==i.Holder&&AccessibleLeader(s,l));
        public static int AvailableMaterial(BaseState s,string item)=>s.Backpack.Items.Where(i=>i.ItemId==item&&CanUseMaterial(s,i)).Sum(i=>i.Count);
        public static bool ConsumeMaterial(BaseState s,string item,int count)
        {if(count<=0||AvailableMaterial(s,item)<count)return false;foreach(var i in s.Backpack.Items.Where(i=>i.ItemId==item&&CanUseMaterial(s,i)).ToArray()){int n=Math.Min(count,i.Count);count-=n;i.Count-=n;if(i.Count==0)s.Backpack.Items.Remove(i);if(count==0)break;}s.Backpack.Revision++;return true;}
    }
}

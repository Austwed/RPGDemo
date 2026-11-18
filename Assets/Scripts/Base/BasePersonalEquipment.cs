using System;
using System.Collections.Generic;
using System.Linq;
using BaseConquest.Battle;

namespace BaseConquest.Base
{
    [Serializable] public sealed class BasePersonalItem
    {
        public string Id, Template, Slot="", BoundLeader="";
        public int Quality=2;
        public string Holder="";
        public int Cell=-1;
        public BasePersonalItem Copy() => (BasePersonalItem)MemberwiseClone();
    }
    [Serializable] public sealed class BasePersonalLoadout
    {
        public int Version;
        public string Weapon;
        public string InitialWeapon;
        public bool ExtraAccessory;
        public SoldierStats BaseStats;
        public List<BasePersonalItem> Items=new List<BasePersonalItem>();
        public BasePersonalLoadout Copy()=>new BasePersonalLoadout {Version=Version,Weapon=Weapon,InitialWeapon=InitialWeapon,ExtraAccessory=ExtraAccessory,BaseStats=BaseStats?.Clone(),Items=Items.Select(i=>i.Copy()).ToList()};
    }
    [Serializable] public sealed class BasePersonalOffer
    {
        public string Template;
        public int Remaining=1;
        public BasePersonalOffer Copy()=>(BasePersonalOffer)MemberwiseClone();
    }
    [Serializable] public sealed class BasePersonalEquipmentState
    {
        public int Version, NextId=1, StockDay, ForgeVersion, StonesRemaining, ReforgeRolls, SuppliesVersion;
        public List<BaseProgressOffer> Supplies=new List<BaseProgressOffer>();
        public List<BasePersonalItem> Warehouse=new List<BasePersonalItem>();
        public List<BasePersonalOffer> Offers=new List<BasePersonalOffer>();
        public BasePersonalEquipmentState Copy()=>new BasePersonalEquipmentState {Version=Version,NextId=NextId,StockDay=StockDay,ForgeVersion=ForgeVersion,StonesRemaining=StonesRemaining,ReforgeRolls=ReforgeRolls,SuppliesVersion=SuppliesVersion,Supplies=Supplies?.Select(o=>o.Copy()).ToList(),Warehouse=Warehouse.Select(i=>i.Copy()).ToList(),Offers=Offers.Select(i=>i.Copy()).ToList()};
    }
    public sealed class BasePersonalDefinition
    {
        public string Id,Name,Slot,Weapon="",Set="";
        public int Range=1,Price,Stage;
        public float Damage,Defense,Health,CritChance,CritMultiplier,Penetration,Resistance,Movement;
        public BasePersonalDefinition Copy()=>(BasePersonalDefinition)MemberwiseClone();
    }
    public static class BasePersonalEquipment
    {
        public static readonly string[] Slots={"weapon","helmet","armor","accessory1","accessory2","accessory3"};
        public static IEnumerable<string> SlotsFor(BaseLeader l)=>Slots.Take(l.PersonalEquipment.ExtraAccessory?6:5);
        public static string Profession(string weapon)=>weapon=="bow"?"弓兵":weapon=="spear"?"骑兵":"剑士";
        public static string ClassName(BaseLeader l)=>l.PersonalEquipment?.Version==1?Profession(l.PersonalEquipment.Weapon):BaseCommanderIdentity.Appearance(l)=="ahe"?"弓兵":BaseCommanderIdentity.Appearance(l)=="azhu"?"骑兵":"剑士";
        public static readonly string[] Qualities={"破损","较差","一般","良好","极佳","传奇"};
        private static readonly float[] Multipliers={.5f,.75f,1,1.25f,1.5f,2};
        private static readonly BasePersonalDefinition[] Definitions={
            new BasePersonalDefinition {Id="basic_sword",Name="短剑",Slot="weapon",Weapon="sword",Price=10},
            new BasePersonalDefinition {Id="basic_bow",Name="木弓",Slot="weapon",Weapon="bow",Range=5,Price=10},
            new BasePersonalDefinition {Id="basic_spear",Name="旧长枪",Slot="weapon",Weapon="spear",Price=10},
            new BasePersonalDefinition {Id="basic_helmet",Name="布帽",Slot="helmet",Price=6},
            new BasePersonalDefinition {Id="basic_armor",Name="布甲",Slot="armor",Price=8},
            new BasePersonalDefinition {Id="novice_sword",Name="新手短剑",Slot="weapon",Weapon="sword",Damage=6,CritChance=.02f,CritMultiplier=.1f,Price=30},
            new BasePersonalDefinition {Id="novice_bow",Name="新手短弓",Slot="weapon",Weapon="bow",Range=5,Damage=5,CritChance=.03f,CritMultiplier=.1f,Price=30},
            new BasePersonalDefinition {Id="novice_spear",Name="新手长枪",Slot="weapon",Weapon="spear",Range=2,Damage=7,CritChance=.01f,CritMultiplier=.1f,Price=30},
            new BasePersonalDefinition {Id="novice_light_helmet",Name="新手轻盔",Slot="helmet",Set="light",Damage=1,Health=5,Defense=1,Movement=.1f,Price=20},
            new BasePersonalDefinition {Id="novice_light_armor",Name="新手轻甲",Slot="armor",Set="light",Damage=2,Health=10,Defense=2,Movement=.2f,Price=30},
            new BasePersonalDefinition {Id="novice_heavy_helmet",Name="新手重盔",Slot="helmet",Set="heavy",Damage=-1,Health=10,Defense=2,Movement=-.1f,Price=20},
            new BasePersonalDefinition {Id="novice_heavy_armor",Name="新手重甲",Slot="armor",Set="heavy",Damage=-3,Health=20,Defense=4,Movement=-.3f,Price=30},
            new BasePersonalDefinition {Id="captains_ring",Name="队长赠予的指环",Slot="accessory",Health=5,Defense=1}
        };
        public static IEnumerable<BasePersonalDefinition> Shop=>Definitions.Where(d=>d.Price>0&&!d.Id.StartsWith("basic_"));
        public static System.Collections.Generic.IEnumerable<BasePersonalDefinition> ShopFor(BaseState s)=>BaseDemoFlow.Active(s)?Definitions.Where(d=>d.Price>0):Shop;
        public static BasePersonalDefinition Definition(string id)=>Definitions.SingleOrDefault(d=>d.Id==id);
        public static BasePersonalDefinition Definition(RpgRules rules,string id)=>rules?.EquipmentDefinition(id)??Definition(id);
        public static IEnumerable<BasePersonalDefinition> DefaultDefinitions()=>Definitions.Select(d=>d.Copy());
        public static string SlotName(string slot)=>slot=="weapon" ? "武器" : slot=="helmet" ? "头盔" : slot=="armor" ? "护甲" : slot=="accessory1" ? "饰品一" : slot=="accessory2"?"饰品二":"饰品三";
        public static string Name(BasePersonalItem item)=>(Definition(item.Template).Slot=="accessory" ? "" : "["+Qualities[item.Quality]+"] ")+Definition(item.Template).Name;
        public static float Scale(float value,int quality)=>value>=0 ? value*Multipliers[quality] : value/Multipliers[quality];
        public static int ExtraRange(int quality)=>quality==4 ? 1 : quality==5 ? 2 : 0;
        public static bool WeaponRangeAllowed(BaseLeader leader,BasePersonalItem item,int quality)
        {
            var d=Definition(item.Template);if(d.Slot!="weapon")return true;
            var r=leader.Rpg?.Rules;
            if(r!=null)return r.Range[leader.Rpg.Profession]+(d.Weapon=="bow"?r.BowQualityRange[quality]:0)<=24;
            return WeaponRange(leader,item)-ExtraRange(item.Quality)+ExtraRange(quality)<=12;
        }
        public static int RealtimeRange(BasePersonalItem item)=>RealtimeCombatRules.Range(Definition(item.Template).Weapon=="bow"?SoldierKind.Ranged:Definition(item.Template).Weapon=="spear"?SoldierKind.Cavalry:SoldierKind.Melee,item.Quality);
        public static double RealtimeRange(BaseLeader leader)
        {
            if(leader.Rpg?.Version==1)return leader.Rpg.Rules.Range[leader.Rpg.Profession]+(leader.Rpg.Profession==1?leader.Rpg.Rules.BowQualityRange[leader.PersonalEquipment?.Items.FirstOrDefault(i=>i.Slot=="weapon")?.Quality??2]:0);
            var weapon=leader.PersonalEquipment?.Items.FirstOrDefault(item=>item.Slot=="weapon");
            return weapon==null ? RealtimeCombatRules.Range(SoldierKind.Melee) : RealtimeRange(weapon);
        }
        private static BasePersonalItem Create(BaseState s,string template,string slot="",string bound="")=>new BasePersonalItem {Id="personal:"+s.PersonalEquipment.NextId++,Template=template,Slot=slot,BoundLeader=bound};
        public static void Normalize(BaseState s)
        {
            var g=s.PersonalEquipment;
            if(g==null||g.Version==0)
            {
                if(g!=null&&(g.Warehouse?.Count>0||g.Offers?.Count>0||g.StockDay!=0))throw new ArgumentException("个人装备缺少版本，拒绝覆盖");
                s.PersonalEquipment=g=new BasePersonalEquipmentState {Version=1,StockDay=s.Day,Offers=Shop.Select(d=>new BasePersonalOffer {Template=d.Id}).ToList()};
            }
            if(g.Version!=1)return;
            if(g.Warehouse==null||g.Offers==null)throw new ArgumentException("个人装备集合缺失，拒绝初始化覆盖");
            foreach(var l in s.Leaders)
            {
                if(l.PersonalEquipment?.Version==1&&s.Backpack.GridVersion==0){l.PersonalEquipment.ExtraAccessory=l.Id==s.PlayerLeaderId;l.PersonalEquipment.InitialWeapon=l.PersonalEquipment.Weapon;}
                if(l.PersonalEquipment?.Version>0||s.Campaign.HasActiveBattle||l.Away||!BaseAdventure.Alive(l))continue;
                if(l.PersonalEquipment?.Items?.Count>0||!string.IsNullOrEmpty(l.PersonalEquipment?.Weapon))throw new ArgumentException("人物装备缺少版本，拒绝重置");
                var stats=BaseCommanderIdentity.Stats(l,new ArmyDefinition().Hero);
                string appearance=BaseCommanderIdentity.Appearance(l);
                string weapon=UnitNaming.CastCombatKind(appearance)==SoldierKind.Ranged||stats.HasCombatKind&&stats.CombatKind==SoldierKind.Ranged ? "bow" : UnitNaming.CastCombatKind(appearance)==SoldierKind.Cavalry||stats.HasCombatKind&&stats.CombatKind==SoldierKind.Cavalry ? "spear" : "sword";
                stats.HasCombatKind=true;stats.CombatKind=weapon=="bow" ? SoldierKind.Ranged : weapon=="spear" ? SoldierKind.Cavalry : SoldierKind.Melee;
                l.PersonalEquipment=new BasePersonalLoadout {Version=1,Weapon=weapon,InitialWeapon=weapon,ExtraAccessory=l.Id==s.PlayerLeaderId,BaseStats=stats};
                // Basic gear represents the existing profile; migration grants no combat stats or healing.
                if(!l.Recruited)
                {
                    l.PersonalEquipment.Items.Add(Create(s,"basic_"+weapon,"weapon"));
                    l.PersonalEquipment.Items.Add(Create(s,"basic_helmet","helmet"));
                    l.PersonalEquipment.Items.Add(Create(s,"basic_armor","armor"));
                }
                if(l.Id==s.PlayerLeaderId)g.Warehouse.Add(Create(s,"captains_ring",bound:l.Id));
            }
            BaseEquipmentCrafting.Normalize(s);
        }
        public static bool Compatible(BaseLeader l,BasePersonalItem item,string slot)
        {
            var d=Definition(item.Template);
            return SlotsFor(l).Contains(slot)&&d!=null&&(d.Slot==slot||d.Slot=="accessory"&&slot.StartsWith("accessory"))&&(d.Slot!="weapon"||d.Weapon==l.PersonalEquipment.Weapon)&&(string.IsNullOrEmpty(item.BoundLeader)||item.BoundLeader==l.Id);
        }
        public static int WeaponRange(BaseLeader l,BasePersonalItem item){var d=Definition(item.Template);return (item.Template.StartsWith("basic_")&&d.Weapon==l.PersonalEquipment.InitialWeapon?l.PersonalEquipment.BaseStats.Range:d.Range)+ExtraRange(item.Quality);}
        public static float WeaponAttack(BaseLeader l,BasePersonalItem item){var d=Definition(item.Template);return Scale(d.Damage,item.Quality);}
        public static SoldierStats Stats(BaseLeader leader)
        {
            if(leader.Rpg?.Version==1)return BaseRpg.Stats(leader);
            var gear=leader.PersonalEquipment;var stats=gear.BaseStats.Clone();stats.Level=leader.Training?.Level??stats.Level;stats.Range=1;stats.HasCombatKind=true;stats.CombatKind=SoldierKind.Melee;
            stats.HasProfessionKind=true;stats.ProfessionKind=gear.Weapon=="bow"?SoldierKind.Ranged:gear.Weapon=="spear"?SoldierKind.Cavalry:SoldierKind.Melee;
            foreach(var item in gear.Items)
            {
                var d=Definition(item.Template);int q=item.Quality;
                stats.Damage+=d.Slot=="weapon"?WeaponAttack(leader,item):Scale(d.Damage,q);stats.Defense+=Scale(d.Defense,q);stats.Health+=Scale(d.Health,q);
                stats.CritChance+=Scale(d.CritChance,q);stats.CritMultiplier+=Scale(d.CritMultiplier,q);
                if(d.Slot=="weapon"){stats.Range=WeaponRange(leader,item);stats.CombatKind=d.Weapon=="bow"?SoldierKind.Ranged:d.Weapon=="spear"?SoldierKind.Cavalry:SoldierKind.Melee;}
            }
            string set=ArmorSet(leader);
            if(set=="light")stats.Damage+=2;
            if(set=="heavy"){stats.Health+=10;stats.Defense+=1;}
            if(set!=""&&gear.Items.Any(i=>i.Slot=="weapon"&&i.Template.StartsWith("novice_"))){stats.Damage+=2;stats.Health+=10;}
            return stats.Clone();
        }
        public static string ArmorSet(BaseLeader l)
        {
            var helmet=l.PersonalEquipment.Items.FirstOrDefault(i=>i.Slot=="helmet");var armor=l.PersonalEquipment.Items.FirstOrDefault(i=>i.Slot=="armor");
            return helmet!=null&&armor!=null&&Definition(helmet.Template).Set==Definition(armor.Template).Set ? Definition(helmet.Template).Set : "";
        }
        public static float Movement(BaseLeader l)=>l.PersonalEquipment.Items.Sum(i=>l.Rpg?.Rules!=null?l.Rpg.Rules.EquipmentScale(Definition(l.Rpg.Rules,i.Template).Movement,i.Quality):Scale(Definition(i.Template).Movement,i.Quality));
        public static bool ValidEffectiveStats(BaseLeader leader)
        {
            var stats=Stats(leader);if(!BaseAftermath.ValidStats(stats))return false;
            var rules=leader.Rpg?.Rules;if(rules==null)return true;
            double speed=rules.Speed[leader.Rpg.Profession]+Movement(leader);
            return stats.Damage<=1000000&&!double.IsNaN(speed)&&!double.IsInfinity(speed)&&speed>0;
        }
        public static void Validate(BaseState s)
        {
            Normalize(s);var g=s.PersonalEquipment;
            if(g.Version!=1||g.NextId<1||g.NextId>=int.MaxValue-400||g.StockDay!=s.Day||g.Warehouse==null||g.Offers==null||s.Backpack.GridVersion==0&&g.Warehouse.Count>10000||g.Offers.Count!=ShopFor(s).Count()||g.Offers.Any(o=>o==null||!ShopFor(s).Any(d=>d.Id==o.Template)||o.Remaining<0||o.Remaining>1)||g.Offers.Select(o=>o.Template).Distinct().Count()!=g.Offers.Count)throw new ArgumentException("个人装备仓库／商人状态非法");
            var all=new List<BasePersonalItem>(g.Warehouse);
            if(g.Warehouse.Any(i=>i!=null&&!string.IsNullOrEmpty(i.Slot)))throw new ArgumentException("仓库装备不得占穿戴槽");
            foreach(var l in s.Leaders.Where(l=>l.PersonalEquipment?.Version>0))
            {
                var p=l.PersonalEquipment;if(l.Rpg?.Version==1&&p.Weapon!=new[]{"sword","bow","spear"}[l.Rpg.Profession])throw new ArgumentException("个人武器配置与固定职业不符");
                if(p.Version!=1||!new[]{"sword","bow","spear"}.Contains(p.Weapon)||!BaseAftermath.ValidStats(p.BaseStats)||p.Items==null||p.Items.Count>SlotsFor(l).Count()||p.Items.Any(i=>i==null||!Compatible(l,i,i.Slot))||p.Items.Select(i=>i.Slot).Distinct().Count()!=p.Items.Count)throw new ArgumentException("个人装备槽／基础属性非法");
                all.AddRange(p.Items);
                if(p.Items.Any(i=>i.Quality<0||i.Quality>5))throw new ArgumentException("个人装备品质非法");
                var weapon=p.Items.SingleOrDefault(i=>i.Slot=="weapon");
                if(weapon!=null&&!WeaponRangeAllowed(l,weapon,weapon.Quality))throw new ArgumentException("个人武器射程超过当前边界");
                if(!ValidEffectiveStats(l))throw new ArgumentException("个人装备合成属性或移动速度非法");
                if(Math.Abs(Stats(l).Health-l.MaxHealth)>.001)throw new ArgumentException("人物生命上限与个人装备不一致");
            }
            if(all.Any(i=>i==null||string.IsNullOrWhiteSpace(i.Id)||!i.Id.StartsWith("personal:")||!int.TryParse(i.Id.Substring(9),out int id)||id<1||id>=g.NextId||Definition(i.Template)==null||i.Quality<0||i.Quality>5||!string.IsNullOrEmpty(i.BoundLeader)&&!s.Leaders.Any(l=>l.Id==i.BoundLeader)||i.Template=="captains_ring"&&i.BoundLeader!=s.PlayerLeaderId&&!(BaseDemoPrologue.Village(s)&&i.BoundLeader=="demo_captain"))||all.Select(i=>i.Id).Distinct().Count()!=all.Count)throw new ArgumentException("个人装备身份／品质／专属条件非法");
            if(all.Count(i=>i.Template=="captains_ring"&&i.BoundLeader!="demo_captain")>1||all.Count(i=>i.Template=="captains_ring"&&i.BoundLeader=="demo_captain")>(BaseDemoPrologue.Village(s)&&s.Leaders.Any(l=>l.Id=="demo_captain")?1:0)||all.Any(i=>Definition(i.Template).Slot=="accessory"&&i.Quality!=2))throw new ArgumentException("唯一指环／饰品品质非法");
            BaseEquipmentCrafting.Validate(s);
        }
        public static bool CanEdit(BaseState s,BaseLeader l)=>l!=null&&l.PersonalEquipment?.Version==1&&BaseAdventure.Alive(l)&&!s.Aftermath.GameOver&&BasePartyInventory.Peaceful(s)&&BasePartyInventory.AccessibleLeader(s,l);
        private static void UpdateHealth(BaseLeader l){l.MaxHealth=Stats(l).Health;l.Health=Math.Min(l.Health,l.MaxHealth);}
        public static bool Equip(BaseState s,string leaderId,string itemId,string slot)
        {
            if(s.Backpack.GridVersion==1)return BasePartyInventory.Move(s,itemId,leaderId,-1,slot);
            var l=s.Leaders.SingleOrDefault(x=>x.Id==leaderId);var item=s.PersonalEquipment.Warehouse.SingleOrDefault(i=>i.Id==itemId);
            if(!CanEdit(s,l)||item==null||!Compatible(l,item,slot))return false;
            var definition=Definition(item.Template);
            if(slot=="weapon"&&!WeaponRangeAllowed(l,item,item.Quality))return false;
            var old=l.PersonalEquipment.Items.SingleOrDefault(i=>i.Slot==slot);
            var preview=l.Copy();preview.PersonalEquipment.Items.RemoveAll(i=>i.Slot==slot);var candidate=item.Copy();candidate.Slot=slot;preview.PersonalEquipment.Items.Add(candidate);
            if(preview.Rpg!=null)preview.Rpg.Rules=l.Rpg.Rules;
            if(!ValidEffectiveStats(preview))return false;
            if(old!=null){l.PersonalEquipment.Items.Remove(old);old.Slot="";s.PersonalEquipment.Warehouse.Add(old);}
            s.PersonalEquipment.Warehouse.Remove(item);item.Slot=slot;l.PersonalEquipment.Items.Add(item);UpdateHealth(l);return true;
        }
        public static bool Unequip(BaseState s,string leaderId,string slot)
        {
            if(s.Backpack.GridVersion==1){var person=s.Leaders.SingleOrDefault(x=>x.Id==leaderId);var worn=person?.PersonalEquipment?.Items.SingleOrDefault(i=>i.Slot==slot);return worn!=null&&BasePartyInventory.Move(s,worn.Id,leaderId,BasePartyInventory.FreeCell(s,leaderId));}
            var l=s.Leaders.SingleOrDefault(x=>x.Id==leaderId);
            if(!CanEdit(s,l)||s.PersonalEquipment.Warehouse.Count>=10000)return false;var item=l.PersonalEquipment.Items.SingleOrDefault(i=>i.Slot==slot);if(item==null)return false;
            l.PersonalEquipment.Items.Remove(item);item.Slot="";s.PersonalEquipment.Warehouse.Add(item);UpdateHealth(l);return true;
        }
        public static int BuyPrice(BaseState s,string template)=>BaseRpg.Active(s)?BaseRpg.Price(s,BaseRpg.Value(s,template),true):Definition(template).Price;
        public static bool AtSmith(BaseState s)=>BaseInteraction.At(s,"town","town_smith")||BaseInteraction.At(s,"town","town_blacksmith");
        public static bool AtShop(BaseState s)=>BaseInteraction.At(s,"town","town_market")||AtSmith(s);
        public static bool CanBuy(BaseState s,string template,string recipient)
        {
            var d=Definition(template);var offer=s.PersonalEquipment.Offers.SingleOrDefault(o=>o.Template==template);
            var leader=BaseAdventure.Members(s).FirstOrDefault(l=>l.Id==recipient);
            return d!=null&&(!BaseDemoFlow.Active(s)||!template.StartsWith("novice_")||s.Demo.Flow.NoviceShop)
                &&offer?.Remaining>0&&s.Inventory.Coins>=BuyPrice(s,template)&&s.PersonalEquipment.NextId<int.MaxValue-401&&s.Backpack.Revision<int.MaxValue-1
                &&leader!=null&&BasePartyInventory.AccessibleLeader(s,leader)&&BasePartyInventory.FreeCell(s,recipient)>=0
                &&!s.Campaign.HasActiveBattle&&BasePartyInventory.Peaceful(s)&&!BaseBattleLoot.Pending(s)&&AtSmith(s);
        }
        public static bool Buy(BaseState s,string template,string recipient,int quotedPrice)
        {
            if(!CanBuy(s,template,recipient)||BuyPrice(s,template)!=quotedPrice)return false;
            var bought=Create(s,template);
            bought.Holder=recipient;
            bought.Cell=BasePartyInventory.FreeCell(s,recipient);
            s.Inventory.Coins-=quotedPrice;
            s.PersonalEquipment.Offers.Single(o=>o.Template==template).Remaining--;
            s.PersonalEquipment.Warehouse.Add(bought);
            s.Backpack.Revision++;
            if(template=="novice_sword")BaseAchievements.Unlock(s,BaseAchievements.Sword);
            return true;
        }
        public static void AdvanceDay(BaseState s)
        {
            if(s.PersonalEquipment==null)return;var g=s.PersonalEquipment;if(g.StockDay==s.Day)return;
            if(g.StockDay!=s.Day-1)throw new InvalidOperationException("个人装备供货必须逐日补充");
            g.StockDay=s.Day;foreach(var o in g.Offers)o.Remaining=1;g.StonesRemaining=BaseEquipmentCrafting.StockLimit(s);
            BaseProgressSupplies.Refill(s);
        }
    }
}

using System;
using System.Linq;
using BaseConquest.Battle;
namespace BaseConquest.Base
{
    public static partial class BaseRpg
    {
        public static int Capacity(BaseState s)=>Active(s)?s.Demo.Rpg.Capacity[s.Demo.Rpg.CampLevel-1]:100;
        public static int Population(BaseState s)=>s.Leaders.Count(l=>l.Fate!="dead"&&l.Fate!="missing"&&BaseInn.Guest(s,l.Id)==null);
        public static bool HasRoom(BaseState s)=>Population(s)<Capacity(s);
        public static string CampUpgradeProblem(BaseState s)
        {
            if(!Active(s)||s.Campaign.Location!="base"||!BasePartyInventory.Peaceful(s)||BaseSceneCombat.Active(s))return "请在安全营地升级";
            var r=s.Demo.Rpg;if(r.CampLevel>=r.Capacity.Length)return "营帐已满级";
            return s.Inventory.Materials<r.CampMaterials[r.CampLevel-1]||s.Inventory.Coins<r.CampCoins[r.CampLevel-1]?"原料或钱币不足":"";
        }
        public static bool UpgradeCamp(BaseState s)
        {
            if(CampUpgradeProblem(s)!="")return false;var r=s.Demo.Rpg;
            s.Inventory.Materials-=r.CampMaterials[r.CampLevel-1];s.Inventory.Coins-=r.CampCoins[r.CampLevel-1];r.CampLevel++;BaseAchievements.Unlock(s,BaseAchievements.Startup);return true;
        }
        public static string DismissProblem(BaseState s,string id)
        {
            var l=s.Leaders.FirstOrDefault(p=>p.Id==id);
            if(!Active(s)||s.Campaign.Location!="base"||!BasePartyInventory.Peaceful(s)||BaseSceneCombat.Active(s))return "请在安全营地处理";
            if(l==null||id==s.PlayerLeaderId||l.Rpg==null||l.Rpg.Story)return "主角与剧情伙伴不能请离基地";
            if(l.Away||BaseTraining.Busy(l.Training)||!string.IsNullOrEmpty(l.PartyId)||BaseAdventure.Region(l)!="base")return "队长正在外出或训练";
            if(s.PersonalEquipment.Warehouse.Count+(l.PersonalEquipment?.Items.Count??0)>10000||s.Backpack.Revision>=int.MaxValue-1)return "仓库空间不足，保留全部物品及队长";
            if(BasePartyInventory.Entries(s).Any(e=>e.Holder==id&&!string.IsNullOrEmpty(e.Bound)))return "请先处理此人的绑定物品";
            return "";
        }
        public static bool Dismiss(BaseState s,string id)
        {
            if(DismissProblem(s,id)!="")return false;var leader=s.Leaders.Single(l=>l.Id==id);
            foreach(var item in leader.PersonalEquipment.Items){item.Slot=item.Holder="";item.Cell=-1;s.PersonalEquipment.Warehouse.Add(item);}leader.PersonalEquipment.Items.Clear();
            foreach(var item in s.PersonalEquipment.Warehouse.Where(i=>i.Holder==id)){item.Holder="";item.Cell=-1;}
            foreach(var item in s.Backpack.Items.Where(i=>i.Holder==id)){item.Holder="";item.Cell=-1;}
            s.Adventure.Members.Remove(id);
            foreach(var p in s.Muster.Presets){p.Candidates.Remove(id);p.LeaderCount=Math.Min(p.LeaderCount,Math.Max(1,p.Candidates.Count));}
            s.Leaders.Remove(leader);s.Backpack.Revision++;BaseDevelopment.Allocate(s);BaseEconomy.Validate(s);return true;
        }
        public static double ProductionBonus(BaseLeader l,string facility)
        {
            if(l.Rpg?.Version!=1)return l.HasAffinity?(facility=="farm"?l.FarmBonus:l.WorkshopBonus):l.ProductionBonus;
            var c=l.Rpg;
            return (c.Trait("diligent")+c.Trait("workaholic")+c.Trait(facility=="farm"?"farming":"forging")+c.Trait(facility=="farm"?"farming_master":"forging_master"))/100;
        }
        public static double TradeModifier(BaseState s)
        {
            var people=BaseAdventure.Members(s).Where(l=>BaseAdventure.Alive(l)).Select(l=>l.Rpg).Where(c=>c!=null).ToArray();
            return (people.Select(c=>c.Trait("merchant")).DefaultIfEmpty(0).Max()-people.Select(c=>c.Trait("bad_temper")).DefaultIfEmpty(0).Max())/100;
        }
        public static int Price(BaseState s,double value,bool buy)
        {
            if(!Active(s)||double.IsNaN(value)||double.IsInfinity(value)||value<0)throw new ArgumentException("缺少RPG商品价值");
            var r=s.Demo.Rpg;double price=value*(buy?r.BuyFactor:r.SellFactor)*(1+(buy?-1:1)*TradeModifier(s));
            return Math.Max(1,checked((int)(buy?Math.Ceiling(price):Math.Floor(price))));
        }
        public static double Value(BaseState s,string id,int quality=2)
        {
            var r=s.Demo.Rpg;var d=BasePersonalEquipment.Definition(r,id);
            if(d!=null)
            {
                int stage=d.Stage;
                return (d.Slot=="weapon"?r.WeaponValues[stage]:d.Slot=="helmet"?r.HelmetValues[stage]:d.Slot=="armor"?r.ArmorValues[stage]:r.AccessoryValues[stage])*r.QualityValue[quality];
            }
            if(id.StartsWith("supply")){int level=id.EndsWith("2")?2:1;return (2d*r.SupplyMaterial*r.MaterialValue+r.SupplyWorkValue)*level;}
            return id=="food"?r.FoodValue:id=="materials"?r.MaterialValue:id=="forging_stone"?r.StoneValue:id.EndsWith("_advanced")?r.AdvancedBookValue:id.StartsWith("training_")?r.BookValue:id==BaseWine.Strong||id==BaseWine.Sake?r.WineValue:r.OtherValue;
        }
        public static double LethalModifier(BaseCampaignSession session,BattleSoldier unit)
        {
            var source=session.Battle.Soldier(unit.LastHitSourceId);
            var owner=source==null?null:session.Battle.Commander(source.Side,source.OriginCommanderSlot)?.Rpg;
            return owner==null?0:(owner.Trait("cruel")-owner.Trait("merciful"))/100;
        }
        public static int RewardQuality(BaseState s,double roll)
        {
            if(!Active(s))return BaseEquipmentCrafting.RewardQuality(roll);
            double move=BaseAdventure.Members(s).Where(BaseAdventure.Alive).Select(l=>(l.Rpg?.Trait("appraiser")??0)/100).DefaultIfEmpty(0).Sum();
            var odds=s.Demo.Rpg.DropQuality.ToArray();move=Math.Min(move,odds[2]);odds[2]-=move;odds[4]+=move*13/20;odds[5]+=move*7/20;
            for(int i=0;i<odds.Length;i++){roll-=odds[i];if(roll<0)return i;}return odds.Length-1;
        }
        public static double MovementSpeed(BaseLeader leader)
        {
            var c=leader.Rpg;var r=c.Rules;
            return (r.Speed[c.Profession]+BasePersonalEquipment.Movement(leader))*(1+(c.Trait("hasty")-c.Trait("slow"))/100);
        }
        private static string TraitUnit(string id)=>id=="advantage"||id=="disadvantage"?"层":id=="taunt"||id=="inconspicuous"||id=="drunkard"?"倍":"%";
        public static string Describe(RpgCharacter c)
        {
            if(c==null)return "";var r=c.Rules;
            return "共享模板："+(c.TemplateId.StartsWith("captain_awakened_")?"异能者队长":c.TemplateId.StartsWith("captain_normal_")?"普通队长":"普通士兵")+" · "+new[]{"剑士","弓兵","骑兵"}[c.Profession]+" · 等级"+c.Level+"\n主动："+r.Skills.Single(s=>s.Id==c.SkillId).Name+"\n特性："+(c.Traits.Count==0?"无":string.Join("；",c.Traits.Select(t=>r.Traits.Single(d=>d.Id==t.Id).Name+" "+t.Value.ToString("0.##")+TraitUnit(t.Id))))+"\n命中／闪避点数："+r.Hit[c.Profession]+" / "+r.Evasion[c.Profession];
        }
        public static string GearDescription(BaseState s,BasePersonalItem item)
        {
            var r=s.Demo.Rpg;var d=BasePersonalEquipment.Definition(r,item.Template);int q=item.Quality;
            return d.Name+(d.Slot=="weapon"?" · "+BasePersonalEquipment.Profession(d.Weapon)+"专用":"")+
                "\n攻击 "+r.EquipmentAttack(d,q).ToString("+0.##;-0.##;0")+
                "／防御 "+(r.EquipmentScale(d.Defense,q)*r.DefenseEquipmentScale).ToString("+0.##;-0.##;0")+
                "／生命 "+(r.EquipmentScale(d.Health,q)*r.HealthEquipmentScale).ToString("+0.##;-0.##;0")+
                (d.Slot=="weapon"?"\n射程 "+(r.Range[d.Weapon=="bow"?1:d.Weapon=="spear"?2:0]+(d.Weapon=="bow"?r.BowQualityRange[q]:0)).ToString("0.##")+"细格":"")+
                "\n暴伤 +"+r.EquipmentScale(d.CritMultiplier,q).ToString("0.##%")+"；移动 "+r.EquipmentScale(d.Movement,q).ToString("+0.##;-0.##;0")+
                "；基础价值 "+Value(s,item.Template,q).ToString("0.##");
        }
        public static string GearComparison(BaseState s,BasePersonalItem item,string holder=null)
        {
            var d=BasePersonalEquipment.Definition(item.Template);
            var l=s.Leaders.FirstOrDefault(p=>p.Id==(holder??s.PlayerLeaderId));
            if(l?.Rpg==null||d==null||d.Slot=="weapon"&&d.Weapon!=l.PersonalEquipment.Weapon)return "";
            string slot=d.Slot=="accessory"?"accessory1":d.Slot;var preview=l.Copy();preview.PersonalEquipment.Items.RemoveAll(g=>g.Slot==slot);
            var gear=item.Copy();gear.Slot=slot;preview.PersonalEquipment.Items.Add(gear);
            var a=Stats(l);var b=Stats(preview);
            return "\n"+l.Name+"换装：攻击 "+a.Damage.ToString("0.##")+" → "+b.Damage.ToString("0.##")+" · 防御 "+a.Defense.ToString("0.##")+" → "+b.Defense.ToString("0.##")+" · 生命 "+a.Health.ToString("0.##")+" → "+b.Health.ToString("0.##")+(d.Slot=="weapon"?" · 射程 "+BasePersonalEquipment.RealtimeRange(l).ToString("0.##")+" → "+BasePersonalEquipment.RealtimeRange(preview).ToString("0.##")+"细格":"");
        }
    }
}

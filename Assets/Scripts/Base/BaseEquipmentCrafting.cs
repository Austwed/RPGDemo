using System;
using System.Linq;
using BaseConquest.Battle;
using BaseConquest.Story;

namespace BaseConquest.Base
{
    public sealed class BaseForgeQuote
    {
        public readonly string ItemId, Stamp;
        public readonly int Stones, Quality, Up, Stay, Down;
        internal BaseForgeQuote(string id,string stamp,int stones,int quality,int up,int down)
        {ItemId=id;Stamp=stamp;Stones=stones;Quality=quality;Up=up;Down=down;Stay=10000-up-down;}
    }
    public static class BaseEquipmentCrafting
    {
        public const string Stone="forging_stone";
        public const int StonePrice=10, DailyStones=10;
        private const string StoneDescription="在灰岩镇铁匠处重铸武器或护具。每次使用 1～10 颗。";
        private const string OldStoneDescription="在基地工坊重铸武器或护具。每次1～10颗，可能升级、留级或降级；可存入基地仓库。";
        public static string DescribeStone(string description)=>description==StoneDescription||description==OldStoneDescription
            ? StoryText.Get("story.ui.trade.stone_description",StoneDescription) : description;
        public static int StockLimit(BaseState s)=>BaseRpg.Active(s)&&s.Demo.Rpg.ForgeDailyStock>0?s.Demo.Rpg.ForgeDailyStock:DailyStones;
        public static void Normalize(BaseState s)
        {
            var g=s.PersonalEquipment;
            var stone=s.Backpack.Catalog.FirstOrDefault(d=>d.Id==Stone);
            if(stone==null)s.Backpack.Catalog.Add(new BaseBagDefinition {Id=Stone,Name="锻造石",Category="material",MaxStack=99,Description=StoneDescription});
            else if(stone.Description==OldStoneDescription)stone.Description=StoneDescription;
            if(g.ForgeVersion!=0)return;
            if(g.StonesRemaining!=0||g.ReforgeRolls!=0)throw new ArgumentException("锻造状态缺少版本");
            g.ForgeVersion=1;g.StonesRemaining=StockLimit(s);
        }
        public static void Validate(BaseState s)
        {
            var g=s.PersonalEquipment;var d=s.Backpack.Catalog.SingleOrDefault(x=>x.Id==Stone);
            if(g.ForgeVersion!=1||g.StonesRemaining<0||g.StonesRemaining>StockLimit(s)||g.ReforgeRolls<0||g.ReforgeRolls>=int.MaxValue||d==null||d.Category!="material"||d.MaxStack!=99||!string.IsNullOrEmpty(d.ExclusiveLeaderId))throw new ArgumentException("锻造材料或状态非法");
        }
        public static int BuyPrice(BaseState s)=>BaseRpg.Active(s)?BaseRpg.Price(s,BaseRpg.Value(s,Stone),true):StonePrice;
        public static int RewardQuality(double roll)
        {
            if(double.IsNaN(roll)||roll<0||roll>=1)throw new ArgumentException("品质抽取范围错误");
            int[] thresholds={5,15,65,85,95,100};
            for(int i=0;i<thresholds.Length;i++)if(roll<thresholds[i]/100.0)return i;
            throw new InvalidOperationException("品质概率不完整");
        }
        public static int MaxBuy(BaseState s)
            => (BaseTown.CanOpenMarket(s) || BasePersonalEquipment.AtSmith(s) && BasePartyInventory.Peaceful(s) && !BaseBattleLoot.Pending(s))
                ? PreviewMaxBuy(s) : 0;
        internal static int PreviewMaxBuy(BaseState s)
        {
            if(s.Backpack.Revision>=int.MaxValue-1||s.Backpack.NextId>=int.MaxValue-1)return 0;
            int room=BaseBackpack.Room(s,Stone);
            return Math.Min(room,Math.Min(s.PersonalEquipment.StonesRemaining,s.Inventory.Coins/BuyPrice(s)));
        }
        public static bool Buy(BaseState s,int count,int revision)
        {
            if(revision!=s.Backpack.Revision||count<1||count>MaxBuy(s))return false;
            BaseBackpack.Receive(s,Stone,count);
            s.Inventory.Coins-=count*BuyPrice(s);s.PersonalEquipment.StonesRemaining-=count;return true;
        }
        public static bool CanSelectReforge(BaseState s,BaseInventoryEntry entry)=>entry?.Gear!=null&&!entry.Protected&&entry.Category!="accessory"
            &&BaseAdventure.Members(s).Any(l=>l.Id==entry.Holder&&BasePartyInventory.AccessibleLeader(s,l));

        private static (double Attack,double Defense,double Health,double Range,double CritChance,double CritDamage,double Speed)
            ItemStats(BaseState state,BasePersonalItem item,int quality)
        {
            var rules=BaseRpg.Active(state)?state.Demo.Rpg:null;
            var gear=BasePersonalEquipment.Definition(rules,item.Template);
            if(rules==null)
            {
                float Scale(float value)=>BasePersonalEquipment.Scale(value,quality);
                return (Scale(gear.Damage),Scale(gear.Defense),Scale(gear.Health),
                    gear.Slot=="weapon"?gear.Range+BasePersonalEquipment.ExtraRange(quality):0,
                    Scale(gear.CritChance),Scale(gear.CritMultiplier),Scale(gear.Movement));
            }

            double range=gear.Slot!="weapon"?0:gear.Weapon=="bow"?rules.Range[1]+rules.BowQualityRange[quality]
                :rules.Range[BaseRpg.Profession(BasePersonalEquipment.WeaponKind(gear.Weapon))];
            return (rules.EquipmentAttack(gear,quality),rules.EquipmentScale(gear.Defense,quality)*rules.DefenseEquipmentScale,
                rules.EquipmentScale(gear.Health,quality)*rules.HealthEquipmentScale,range,0,
                rules.EquipmentScale(gear.CritMultiplier,quality),rules.EquipmentScale(gear.Movement,quality));
        }

        private static (double Attack,double Defense,double Health,double Range,double CritChance,double CritDamage,double Speed)
            WornStats(BaseState state,BaseInventoryEntry entry,int quality)
        {
            var leader=state.Leaders.Single(person=>person.Id==entry.Holder);
            var preview=leader.Copy();
            if(preview.Rpg!=null)preview.Rpg.Rules=leader.Rpg.Rules;
            preview.PersonalEquipment.Items.Single(item=>item.Id==entry.Id).Quality=quality;
            var stats=BasePersonalEquipment.Stats(preview);
            bool rpg=preview.Rpg?.Version==1;
            return (stats.Damage,stats.Defense,stats.Health,rpg?BasePersonalEquipment.RealtimeRange(preview):stats.Range,
                stats.CritChance,stats.CritMultiplier,rpg?BaseRpg.MovementSpeed(preview):BasePersonalEquipment.Movement(preview));
        }

        internal static string StatChanges(BaseState state,BaseInventoryEntry entry,int fromQuality,int toQuality)
        {
            var before=entry.Worn?WornStats(state,entry,fromQuality):ItemStats(state,entry.Gear,fromQuality);
            var after=entry.Worn?WornStats(state,entry,toQuality):ItemStats(state,entry.Gear,toQuality);
            string Change(string name,double from,double to,string format="0")=>from==to?null:
                name+" "+from.ToString(format)+" → "+to.ToString(format);
            var changes=new[]{Change("攻击",before.Attack,after.Attack),Change("防御",before.Defense,after.Defense),
                Change("生命上限",before.Health,after.Health),Change("攻击距离",before.Range,after.Range),
                Change("暴击率",before.CritChance,after.CritChance,"0%"),
                Change("暴伤",before.CritDamage,after.CritDamage,"0%"),Change("移速",before.Speed,after.Speed)};
            string delta=string.Join(" · ",changes.Where(change=>change!=null));
            return delta.Length==0?StoryText.Get("story.ui.trade.reforge_stats_unchanged","属性未变"):delta;
        }

        public static string Blocked(BaseState s,string id,int stones)
        {
            if(!BasePersonalEquipment.AtSmith(s)||s.Campaign.HasActiveBattle||!BasePartyInventory.Peaceful(s)||BaseBattleLoot.Pending(s))
                return StoryText.Get("story.ui.trade.reforge_location","请靠近灰岩镇铁匠");
            var e=BasePartyInventory.Find(s,id);
            if(!CanSelectReforge(s,e))return StoryText.Get("story.ui.trade.reforge_target","请选择随行角色的武器或护具");
            if(e.Quality==5)return "传奇已是最高品质，不能继续重铸";
            var current=ItemStats(s,e.Gear,e.Quality);
            if(Enumerable.Range(0,BasePersonalEquipment.Qualities.Length).All(quality=>ItemStats(s,e.Gear,quality)==current))
                return StoryText.Get("story.ui.trade.reforge_no_stat_change","这件装备的属性不随品质变化。新手短剑可从巴隆赠礼或古窟战利品获得。");
            if(stones<1||stones>10)return "每次使用1～10颗锻造石";
            if(BasePartyInventory.AvailableMaterial(s,Stone)<stones)return "可用锻造石不足";
            if(s.Backpack.Revision>=int.MaxValue-1||s.PersonalEquipment.ReforgeRolls>=int.MaxValue-1)return "操作序号已达上限";
            if(e.Worn)
            {
                var l=s.Leaders.Single(x=>x.Id==e.Holder);var item=l.PersonalEquipment.Items.Single(i=>i.Id==id);
                foreach(int quality in new[]{e.Quality-1,e.Quality+1}.Where(q=>q>=0&&q<=5))
                {
                    if(!BasePersonalEquipment.WeaponRangeAllowed(l,item,quality))return "重铸后的攻击距离可能超过当前上限";
                    var preview=l.Copy();if(preview.Rpg!=null)preview.Rpg.Rules=l.Rpg.Rules;
                    preview.PersonalEquipment.Items.Single(i=>i.Id==id).Quality=quality;
                    if(!BasePersonalEquipment.ValidEffectiveStats(preview))return "重铸结果可能使穿戴者属性或移动速度非法，请先卸下装备";
                }
            }
            return "";
        }
        private static string Stamp(BaseState s)=>string.Join("|",s.Day,s.Backpack.Revision,s.PersonalEquipment.ReforgeRolls,s.Inventory.Coins,
            string.Join(";",BasePartyInventory.Entries(s).Select(e=>e.Id+":"+e.Holder+":"+e.Slot+":"+e.Cell+":"+e.Count+":"+e.Quality+":"+e.Gear?.Template+":"+e.Bound)),
            string.Join(";",s.Leaders.Select(l=>l.Id+":"+l.Health+":"+l.MaxHealth+":"+l.Away)));
        public static BaseForgeQuote Quote(BaseState s,string id,int stones)
        {
            if(Blocked(s,id,stones)!="")return null;
            int[] points={1,3,5,10},ups=BaseRpg.Active(s)?s.Demo.Rpg.ReforgeUp:null,downs=BaseRpg.Active(s)?s.Demo.Rpg.ReforgeDown:null;
            ups=ups??new[]{2000,3500,5000,7000};downs=downs??new[]{2500,1500,1000,500};int index=0;
            while(index<2&&stones>points[index+1])index++;
            int width=points[index+1]-points[index],offset=stones-points[index];
            int up=ups[index]+(ups[index+1]-ups[index])*offset/width,down=downs[index]+(downs[index+1]-downs[index])*offset/width;
            var e=BasePartyInventory.Find(s,id);return new BaseForgeQuote(id,Stamp(s),stones,e.Quality,up,e.Quality==0?0:down);
        }
        public static bool Confirm(BaseState s,BaseForgeQuote quote)
        {
            if(quote==null||quote.Stamp!=Stamp(s))return false;
            var actual=Quote(s,quote.ItemId,quote.Stones);if(actual==null||actual.Quality!=quote.Quality||actual.Up!=quote.Up||actual.Down!=quote.Down)return false;
            var e=BasePartyInventory.Find(s,quote.ItemId);
            double roll=BattleSimulation.StableRoll(BaseAftermath.StableIdentity(s.PlayerLeaderId+":"+s.Day+":"+e.Id),s.PersonalEquipment.ReforgeRolls,53)*10000;
            int quality=e.Quality+(roll<quote.Up?1:roll<quote.Up+quote.Stay?0:-1);
            if(!BasePartyInventory.ConsumeMaterial(s,Stone,quote.Stones))return false;
            e.Gear.Quality=quality;s.PersonalEquipment.ReforgeRolls++;
            if(e.Worn){var l=s.Leaders.Single(x=>x.Id==e.Holder);l.MaxHealth=BasePersonalEquipment.Stats(l).Health;l.Health=Math.Min(l.Health,l.MaxHealth);}
            BaseDemoSideQuests.Reforged(s);
            return true;
        }
    }
}

using System;
using System.Linq;
using BaseConquest.Battle;

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
        public static int StockLimit(BaseState s)=>BaseRpg.Active(s)&&s.Demo.Rpg.ForgeDailyStock>0?s.Demo.Rpg.ForgeDailyStock:DailyStones;
        public static void Normalize(BaseState s)
        {
            var g=s.PersonalEquipment;
            if(!s.Backpack.Catalog.Any(d=>d.Id==Stone))s.Backpack.Catalog.Add(new BaseBagDefinition {Id=Stone,Name="锻造石",Category="material",MaxStack=99,Description="在基地工坊重铸武器或护具。每次1～10颗，可能升级、留级或降级；可存入基地仓库。"});
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
        private static bool At(BaseState s,string region,string id)=>!s.Campaign.HasActiveBattle&&!s.Aftermath.GameOver&&!BaseBattleLoot.Pending(s)&&BaseInteraction.At(s,region,id);
        public static int MaxBuy(BaseState s)
        {
            if(!(At(s,"town","town_market")||BasePersonalEquipment.AtSmith(s))||s.Backpack.Revision>=int.MaxValue-1||s.Backpack.NextId>=int.MaxValue-1)return 0;
            int room=0;foreach(var l in BaseAdventure.Members(s).Where(l=>BasePartyInventory.AccessibleLeader(s,l)))
            {room+=s.Backpack.Items.Where(i=>i.Holder==l.Id&&i.ItemId==Stone).Sum(i=>99-i.Count);if(BasePartyInventory.FreeCell(s,l.Id)>=0)room+=99;}
            return Math.Min(room,Math.Min(s.PersonalEquipment.StonesRemaining,s.Inventory.Coins/BuyPrice(s)));
        }
        public static bool Buy(BaseState s,int count,int revision)
        {
            if(revision!=s.Backpack.Revision||count<1||count>MaxBuy(s))return false;
            int left=count;foreach(var l in BaseAdventure.Members(s).Where(l=>BasePartyInventory.AccessibleLeader(s,l)))
            {
                foreach(var i in s.Backpack.Items.Where(i=>i.Holder==l.Id&&i.ItemId==Stone)){int n=Math.Min(left,99-i.Count);i.Count+=n;left-=n;}
                if(left>0){int cell=BasePartyInventory.FreeCell(s,l.Id);if(cell>=0){s.Backpack.Items.Add(new BaseBagItem {Id="bag:"+s.Backpack.NextId++,ItemId=Stone,Count=left,Holder=l.Id,Cell=cell});left=0;}}
                if(left==0)break;
            }
            s.Inventory.Coins-=count*BuyPrice(s);s.PersonalEquipment.StonesRemaining-=count;s.Backpack.Revision++;return true;
        }
        private static bool Accessible(BaseState s,BaseInventoryEntry e)=>e!=null&&e.Gear!=null&&!e.Protected&&e.Category!="accessory"&&(e.Holder==""||s.Leaders.Any(l=>l.Id==e.Holder&&BasePartyInventory.AccessibleLeader(s,l)));
        public static string Blocked(BaseState s,string id,int stones)
        {
            if(!At(s,"base","workshop"))return "请在非战斗时接近基地工坊";
            var e=BasePartyInventory.Find(s,id);
            if(!Accessible(s,e))return "请选择基地可用人物或仓库中的武器、护具";
            if(e.Quality==5)return "传奇已是最高品质，不能继续重铸";
            if(stones<1||stones>10)return "每次使用1～10颗锻造石";
            if(BasePartyInventory.AvailableMaterial(s,Stone)<stones)return "可用锻造石不足";
            if(s.Backpack.Revision>=int.MaxValue-1||s.PersonalEquipment.ReforgeRolls>=int.MaxValue-1)return "操作序号已达上限";
            if(e.Worn)
            {
                var l=s.Leaders.Single(x=>x.Id==e.Holder);var item=l.PersonalEquipment.Items.Single(i=>i.Id==id);
                foreach(int quality in new[]{e.Quality-1,e.Quality+1}.Where(q=>q>=0&&q<=5))
                {
                    if(!BasePersonalEquipment.WeaponRangeAllowed(l,item,quality))return "重铸后的射程可能超过当前上限";
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
            return true;
        }
    }
}

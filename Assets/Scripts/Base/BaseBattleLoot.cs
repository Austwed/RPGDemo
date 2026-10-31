using System;
using System.Collections.Generic;
using System.Linq;

namespace BaseConquest.Base
{
    [Serializable] public sealed class BaseLootReward
    {
        public string Template="", ItemId="";
        public int Count=1, Quality=2;
        public bool RandomQuality;
        public BaseLootReward Copy()=>(BaseLootReward)MemberwiseClone();
    }
    [Serializable] public sealed class BaseLootLine
    {
        public string Id, Status="pending";
        public BaseLootReward Reward=new BaseLootReward();
        public BaseLootLine Copy()=>new BaseLootLine {Id=Id,Status=Status,Reward=Reward.Copy()};
    }
    [Serializable] public sealed class BaseLootManifest
    {
        public string BattleId, Title;
        public bool Closed;
        public int Food, Materials, Coins;
        public List<BaseLootLine> Items=new List<BaseLootLine>();
        public BaseLootManifest Copy()=>new BaseLootManifest {BattleId=BattleId,Title=Title,Closed=Closed,Food=Food,Materials=Materials,Coins=Coins,Items=Items.Select(i=>i.Copy()).ToList()};
    }
    public static class BaseBattleLoot
    {
        public static bool Pending(BaseState s)=>s?.Aftermath!=null&&!s.Aftermath.GameOver&&s.Aftermath.Loot.Any(m=>!m.Closed);
        public static bool ValidReward(BaseState s,BaseLootReward r)=>r!=null&&r.Template!=null&&r.ItemId!=null&&r.Quality>=0&&r.Quality<6&&
            (!string.IsNullOrEmpty(r.Template)?string.IsNullOrEmpty(r.ItemId)&&r.Count==1&&BasePersonalEquipment.Definition(r.Template)!=null&&r.Template!="captains_ring":
            !r.RandomQuality&&r.Quality==2&&s.Backpack.Catalog.Any(d=>d.Id==r.ItemId&&d.Category!="quest"&&string.IsNullOrEmpty(d.ExclusiveLeaderId)&&r.Count>=1&&r.Count<=d.MaxStack));
        public static void Validate(BaseState s)
        {
            foreach(var e in s.Campaign.Events)if(e.ItemRewards==null||e.ItemRewards.Count>100||e.ItemRewards.Any(r=>!ValidReward(s,r)))throw new ArgumentException("战役道具奖励非法");
            var list=s.Aftermath.Loot;
            if(list==null||list.Count>1000||list.Any(m=>m==null||string.IsNullOrWhiteSpace(m.BattleId)||string.IsNullOrWhiteSpace(m.Title)||!m.Closed&&!s.Campaign.SettledIds.Contains(m.BattleId)||m.Items==null||m.Items.Count>100||m.Items.Any(i=>i==null||string.IsNullOrWhiteSpace(i.Id)||!new[]{"pending","taken","discarded"}.Contains(i.Status)||!ValidReward(s,i.Reward)||i.Reward.RandomQuality)||m.Items.Select(i=>i.Id).Distinct().Count()!=m.Items.Count||m.Closed&&!CanFinish(s,m))||list.Select(m=>m.BattleId).Distinct().Count()!=list.Count)
                throw new ArgumentException("战利品清单身份、状态或奖励非法");
        }
        public static void Create(BaseState s,BaseCampaignReport report,IEnumerable<BaseLootReward> rewards)
        {
            if(s.Aftermath.GameOver||s.Aftermath.Loot.Any(m=>m.BattleId==report.BattleId))return;
            var entries=rewards.ToArray();if(entries.Length>100||entries.Any(r=>!ValidReward(s,r)))throw new ArgumentException("战利品奖励配置非法");
            if(s.Aftermath.Loot.Count>=256)s.Aftermath.Loot.RemoveAll(m=>m.Closed);
            s.Aftermath.Loot.Add(new BaseLootManifest {BattleId=report.BattleId,Title=report.Title,Food=report.FoodChange,Materials=report.MaterialChange+report.DropMaterials,Coins=report.CoinChange,
                Items=entries.Select((r,n)=>new BaseLootLine {Id=report.BattleId+":loot:"+n,Reward=ResolveQuality(s,r,report.BattleId,n)}).ToList()});
        }
        private static BaseLootReward ResolveQuality(BaseState s,BaseLootReward source,string battle,int index)
        {
            var reward=source.Copy();
            if(reward.RandomQuality)reward.Quality=BaseRpg.RewardQuality(s,BaseConquest.Battle.BattleSimulation.StableRoll(BaseAftermath.StableIdentity(battle),index,47));
            reward.RandomQuality=false;return reward;
        }
        public static void AppendRewards(BaseState s,string battle,string source,string title,int food,int materials,int coins,IEnumerable<BaseLootReward> rewards)
        {
            var entries=rewards.ToArray();var m=s.Aftermath.Loot.SingleOrDefault(v=>v.BattleId==battle);
            if(m==null){Create(s,new BaseCampaignReport{BattleId=battle,Title=title},Array.Empty<BaseLootReward>());m=s.Aftermath.Loot.Single(v=>v.BattleId==battle);}
            if(m.Items.Count+entries.Length>100||entries.Any(r=>!ValidReward(s,r))||m.Items.Any(i=>i.Id.StartsWith(source+":loot:",StringComparison.Ordinal)))throw new InvalidOperationException("奖励清单已存在或容量不足");
            m.Title=title;m.Closed=false;m.Food+=food;m.Materials+=materials;m.Coins+=coins;
            m.Items.AddRange(entries.Select((r,n)=>new BaseLootLine{Id=source+":loot:"+n,Reward=ResolveQuality(s,r,source,n)}));
        }
        public static string Name(BaseState s,BaseLootLine line)=>line.Reward.Template!=""?BasePersonalEquipment.Name(new BasePersonalItem {Template=line.Reward.Template,Quality=line.Reward.Quality}):s.Backpack.Catalog.Single(d=>d.Id==line.Reward.ItemId).Name+" ×"+line.Reward.Count;
        public static bool CanFinish(BaseState s,BaseLootManifest m)=>m.Items.All(i=>i.Status!="pending")&&(BaseRpg.Active(s)||!s.Aftermath.Prisoners.Any(p=>p.BattleId==m.BattleId&&p.Status=="held"));
        public static bool Finish(BaseState s,string battle)
        {var m=s.Aftermath.Loot.SingleOrDefault(x=>x.BattleId==battle);if(m==null||m.Closed||!BasePartyInventory.Peaceful(s)||!CanFinish(s,m))return false;m.Closed=true;return true;}
        public static int CollectAll(BaseState s,string battle)
        {
            var m=s.Aftermath.Loot.SingleOrDefault(x=>x.BattleId==battle&&!x.Closed);if(m==null)return 0;
            var holders=BaseAdventure.Members(s).Where(l=>BasePartyInventory.AccessibleLeader(s,l)).Select(l=>l.Id).ToArray();int count=0;
            foreach(var line in m.Items.Where(i=>i.Status=="pending"))
            {
                if(!holders.Any(holder=>Resolve(s,battle,line.Id,holder)))break;
                count++;
            }
            return count;
        }
        public static int RecruitAll(BaseState s,string battle)
        {
            if(!s.Aftermath.Loot.Any(m=>m.BattleId==battle&&!m.Closed)||!BasePartyInventory.Peaceful(s))return 0;
            int count=0;
            foreach(var p in s.Aftermath.Prisoners.Where(p=>p.BattleId==battle&&p.Status=="held").ToArray())
                if(BaseAftermath.Decide(s,p.Id,"recruit"))count++;
            return count;
        }
        public static bool Resolve(BaseState s,string battle,string lineId,string holder,bool discard=false)
        {
            var m=s.Aftermath.Loot.SingleOrDefault(x=>x.BattleId==battle&&!x.Closed);var line=m?.Items.SingleOrDefault(i=>i.Id==lineId&&i.Status=="pending");
            if(line==null||!BasePartyInventory.Peaceful(s)||s.Backpack.Revision>=int.MaxValue-1)return false;
            if(discard){line.Status="discarded";return true;}
            if(!BaseAdventure.Members(s).Any(l=>l.Id==holder&&BasePartyInventory.AccessibleLeader(s,l)))return false;
            var r=line.Reward;int cell=BasePartyInventory.FreeCell(s,holder);
            if(r.Template!="")
            {
                if(cell<0||s.PersonalEquipment.NextId>=int.MaxValue-401)return false;
                s.PersonalEquipment.Warehouse.Add(new BasePersonalItem {Id="personal:"+s.PersonalEquipment.NextId++,Template=r.Template,Quality=r.Quality,Holder=holder,Cell=cell});
            }
            else
            {
                var d=s.Backpack.Catalog.Single(i=>i.Id==r.ItemId);var stacks=s.Backpack.Items.Where(i=>i.Holder==holder&&i.ItemId==d.Id).ToArray();int room=stacks.Sum(i=>d.MaxStack-i.Count);
                if(room<r.Count&&(cell<0||s.Backpack.NextId>=int.MaxValue-1))return false;
                int left=r.Count;foreach(var stack in stacks){int n=Math.Min(left,d.MaxStack-stack.Count);stack.Count+=n;left-=n;}
                if(left>0)s.Backpack.Items.Add(new BaseBagItem {Id="bag:"+s.Backpack.NextId++,ItemId=d.Id,Count=left,Holder=holder,Cell=cell});
            }
            line.Status="taken";s.Backpack.Revision++;return true;
        }
        public static int SellPrice(BaseInventoryEntry e)=>e?.Gear==null||e.Protected||!string.IsNullOrEmpty(e.Bound)?0:Math.Max(1,(int)(Math.Max(2,BasePersonalEquipment.Definition(e.Gear.Template).Price)*BasePersonalEquipment.Scale(1,e.Quality)/2));
        public static int SellPrice(BaseState s,BaseInventoryEntry e)=>!BaseRpg.Active(s)?SellPrice(e):e==null||e.Protected||!string.IsNullOrEmpty(e.Bound)?0:BaseRpg.Price(s,BaseRpg.Value(s,e.Gear?.Template??e.Stack.ItemId,e.Quality),false);
        public static bool CanSell(BaseState s,string id)
        {
            var e=BasePartyInventory.Find(s,id);return e!=null&&!e.Worn&&e.Holder!=""&&BasePartyInventory.People(s,false).Any(l=>l.Id==e.Holder)&&BasePartyInventory.AccessibleLeader(s,s.Leaders.Single(l=>l.Id==e.Holder))&&BasePartyInventory.Peaceful(s)&&!Pending(s)&&BasePersonalEquipment.AtShop(s)&&SellPrice(s,e)>0&&s.Inventory.Coins<=100000000-SellPrice(s,e)&&s.Backpack.Revision<int.MaxValue-1;
        }
        public static bool Sell(BaseState s,string id)
        {if(!CanSell(s,id))return false;var e=BasePartyInventory.Find(s,id);s.Inventory.Coins+=SellPrice(s,e);if(e.Gear!=null)s.PersonalEquipment.Warehouse.Remove(e.Gear);else if(--e.Stack.Count==0)s.Backpack.Items.Remove(e.Stack);s.Backpack.Revision++;return true;}
    }
}

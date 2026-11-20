using System;
using System.Collections.Generic;
using System.Linq;

namespace BaseConquest.Base
{
    [Serializable] public sealed class BaseBagDefinition
    {
        public string Id, Name, Category, Description, ExclusiveLeaderId="";
        public int MaxStack=99;
        public BaseBagDefinition Copy() => (BaseBagDefinition)MemberwiseClone();
    }
    [Serializable] public sealed class BaseBagItem
    {
        public string Id, ItemId;
        public int Count;
        public string Holder="";
        public int Cell=-1;
        public BaseBagItem Copy() => (BaseBagItem)MemberwiseClone();
    }
    [Serializable] public sealed class BaseBackpackState
    {
        public int Version, Revision, GridVersion, NextId=1;
        public bool RingPlaced;
        public List<BaseBagDefinition> Catalog=new List<BaseBagDefinition>();
        public List<BaseBagItem> Items=new List<BaseBagItem>();
        public List<string> StoryFlags=new List<string>();
        public BaseBackpackState Copy() => new BaseBackpackState {Version=Version,Revision=Revision,GridVersion=GridVersion,RingPlaced=RingPlaced,NextId=NextId,Catalog=Catalog.Select(x=>x.Copy()).ToList(),Items=Items.Select(x=>x.Copy()).ToList(),StoryFlags=StoryFlags.ToList()};
    }
    public sealed class BagFullException : InvalidOperationException
    {
        public const string Notice = "背包已满";
        public BagFullException() : base(Notice) { }
    }

    public static class BaseBackpack
    {
        public const string Blueprint="construction_manual", Story="construction_introduction", RoyalSword="royal_mithril_sword";
        public const string RoyalSwordName="王室秘银长剑";
        public static string RoyalSwordDescription=>BaseConquest.Story.StoryText.Get("story.item.royal_sword.description",
            "刺穿你胸口的秘银剑，剑脊刻着禁卫军的暗金云纹。任务物品，不可装备。");
        public static readonly string[] Categories={"exclusive","weapon","armor","accessory","consumable","quest","material"};
        public static string CategoryName(string id) => id=="exclusive" ? "角色专属装备" : id=="weapon" ? "武器" : id=="armor" ? "护具" : id=="accessory" ? "饰品" : id=="consumable" ? "消耗品" : id=="quest" ? "任务道具" : id=="material" ? "特殊材料" : "全部";
        public static bool Single(string category) => category=="exclusive"||category=="weapon"||category=="accessory";
        public static void Normalize(BaseState s)
        {
            if(s.Backpack!=null&&s.Backpack.Version!=0)
            {
                if(s.Backpack.Version==1 && s.Backpack.Catalog!=null && !s.Backpack.Catalog.Any(d=>d?.Id==BaseDemoFinale.BrokenSwordItem))
                    s.Backpack.Catalog.Add(BrokenSwordDefinition());
                return;
            }
            var old=s.Backpack;
            if(old!=null&&(old.Revision!=0||old.Catalog?.Count>0||old.Items?.Count>0||old.StoryFlags?.Count>0))throw new ArgumentException("背包缺版本，拒绝覆盖已有物品或剧情");
            s.Backpack=new BaseBackpackState {Version=1,NextId=2,
                Catalog=new List<BaseBagDefinition>{new BaseBagDefinition {Id=Blueprint,Name="营造图册",Category="material",Description="剧情特殊道具。满足营造入门条件后，每次设施升级或解锁固定工事消耗1本；不能代替战略原料。测试版首次接入默认获得1本。",MaxStack=99}},
                Items=new List<BaseBagItem>{new BaseBagItem {Id="bag:1",ItemId=Blueprint,Count=1}},StoryFlags=new List<string>{Story}};
            s.Backpack.Catalog.Add(BrokenSwordDefinition());
        }
        private static BaseBagDefinition BrokenSwordDefinition() => new BaseBagDefinition
        {
            Id=BaseDemoFinale.BrokenSwordItem, Name="搭档的断剑", Category="quest", MaxStack=1,
            Description="剑柄上还缠着沾泥的青绳结。"
        };
        private static bool Id(string id) => !string.IsNullOrWhiteSpace(id)&&id.Length<=100;
        private static bool StackId(string id,int next) => id!=null&&id.StartsWith("bag:")&&int.TryParse(id.Substring(4),out int number)&&number>=1&&number<next;
        public static void Validate(BaseState s)
        {
            Normalize(s);var b=s.Backpack;
            if(b.Version!=1||b.Revision<0||b.Revision>=int.MaxValue||b.NextId<1||b.NextId>=int.MaxValue||b.Catalog==null||b.Items==null||b.StoryFlags==null||b.Catalog.Count>1000||b.GridVersion==0&&b.Items.Count>1000||b.StoryFlags.Count>1000)throw new ArgumentException("背包版本、序号或集合非法");
            if(b.Catalog.Any(d=>d==null||!Id(d.Id)||string.IsNullOrWhiteSpace(d.Name)||d.Name.Length>80||d.Description==null||d.Description.Length>2000||!Categories.Contains(d.Category)||d.MaxStack<1||d.MaxStack>999||Single(d.Category)&&d.MaxStack!=1||!string.IsNullOrEmpty(d.ExclusiveLeaderId)&&!s.Leaders.Any(l=>l.Id==d.ExclusiveLeaderId))||b.Catalog.Select(d=>d.Id).Distinct().Count()!=b.Catalog.Count)throw new ArgumentException("背包物品定义、类别或专属角色非法");
            if(b.Items.Any(x=>x==null||!StackId(x.Id,b.NextId)||!b.Catalog.Any(d=>d.Id==x.ItemId&&x.Count>=1&&x.Count<=d.MaxStack))||b.Items.Select(x=>x.Id).Distinct().Count()!=b.Items.Count||b.StoryFlags.Any(x=>!Id(x))||b.StoryFlags.Distinct().Count()!=b.StoryFlags.Count)throw new ArgumentException("背包物品数量、身份或剧情条件非法");
        }
        public static int Count(BaseState s,string item) => s.Backpack.Items.Where(x=>x.ItemId==item).Sum(x=>x.Count);
        // 先预检全部空位和数量，失败不留下半份奖励。
        public static bool AddToWarehouse(BaseState s,string item,int count)
        {
            var b=s.Backpack;var d=b.Catalog.SingleOrDefault(x=>x.Id==item);
            if(d==null||count<=0||count>999000||b.Revision>=int.MaxValue-1)return false;
            int room=b.Items.Where(x=>x.ItemId==item&&string.IsNullOrEmpty(x.Holder)).Sum(x=>d.MaxStack-x.Count),slots=(int)Math.Ceiling(Math.Max(0,count-room)/(double)d.MaxStack);
            if(b.GridVersion==0&&b.Items.Count+slots>1000||b.NextId>int.MaxValue-1-slots)return false;
            foreach(var x in b.Items.Where(x=>x.ItemId==item&&string.IsNullOrEmpty(x.Holder))){int n=Math.Min(count,d.MaxStack-x.Count);x.Count+=n;count-=n;if(count==0)break;}
            while(count>0){string id;do{id="bag:"+b.NextId++;}while(b.Items.Any(x=>x.Id==id));int n=Math.Min(count,d.MaxStack);b.Items.Add(new BaseBagItem {Id=id,ItemId=item,Count=n});count-=n;}
            b.Revision++;return true;
        }
        private static IEnumerable<string> Recipients(BaseState state, BaseBagDefinition item, string holder)
        {
            if (holder != null)
            {
                if (item.Category == "quest" && holder != state.PlayerLeaderId ||
                    !string.IsNullOrEmpty(item.ExclusiveLeaderId) && item.ExclusiveLeaderId != holder)
                    yield break;
                if (state.Leaders.Any(person => person.Id == holder)) yield return holder;
                yield break;
            }
            foreach (var person in BaseAdventure.Members(state))
                if (BasePartyInventory.AccessibleLeader(state, person) &&
                    (item.Category != "quest" || person.Id == state.PlayerLeaderId) &&
                    (string.IsNullOrEmpty(item.ExclusiveLeaderId) || person.Id == item.ExclusiveLeaderId))
                    yield return person.Id;
        }

        public static int Room(BaseState state, string itemId, string holder = null)
        {
            var item = state.Backpack.Catalog.Single(d => d.Id == itemId);
            int room = 0;
            foreach (var id in Recipients(state, item, holder))
            {
                int occupied = BasePartyInventory.Entries(state).Count(e => e.Holder == id && !e.Worn);
                room += (BasePartyInventory.Capacity - occupied) * item.MaxStack;
                room += state.Backpack.Items.Where(i => i.Holder == id && i.ItemId == itemId).Sum(i => item.MaxStack - i.Count);
            }
            return room;
        }

        public static void Receive(BaseState state, string itemId, int count, string holder = null)
        {
            if (count <= 0) throw new ArgumentOutOfRangeException(nameof(count));
            var bag = state.Backpack;
            var item = bag.Catalog.Single(d => d.Id == itemId);
            if (Room(state, itemId, holder) < count) throw new BagFullException();
            var recipients = Recipients(state, item, holder).ToArray();
            var stacks = bag.Items.Where(i => i.ItemId == itemId && recipients.Contains(i.Holder)).ToArray();
            int additional = Math.Max(0, count - stacks.Sum(i => item.MaxStack - i.Count));
            if (bag.Revision >= int.MaxValue - 1 || bag.NextId > int.MaxValue - 1 - (additional + item.MaxStack - 1) / item.MaxStack)
                throw new InvalidOperationException("物品记录已满");
            foreach (var stack in stacks)
            {
                int add = Math.Min(count, item.MaxStack - stack.Count);
                stack.Count += add;
                count -= add;
                if (count == 0) break;
            }
            foreach (string id in recipients)
            {
                while (count > 0)
                {
                    int cell = BasePartyInventory.FreeCell(state, id);
                    if (cell < 0) break;
                    int add = Math.Min(count, item.MaxStack);
                    bag.Items.Add(new BaseBagItem { Id = "bag:" + bag.NextId++, ItemId = itemId, Holder = id, Cell = cell, Count = add });
                    count -= add;
                }
                if (count == 0) break;
            }
            bag.Revision++;
        }

        public static int RequireGearCell(BaseState state, string holder)
        {
            int cell = BasePartyInventory.FreeCell(state, holder);
            if (cell < 0) throw new BagFullException();
            return cell;
        }

        public static bool Consume(BaseState s,string item,int count)
        {
            if(count<=0||Count(s,item)<count||s.Backpack.Revision>=int.MaxValue-1)return false;
            foreach(var x in s.Backpack.Items.Where(x=>x.ItemId==item)){int n=Math.Min(count,x.Count);x.Count-=n;count-=n;if(count==0)break;}
            s.Backpack.Items.RemoveAll(x=>x.Count==0);s.Backpack.Revision++;return true;
        }
    }
}

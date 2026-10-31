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
    public static class BaseBackpack
    {
        public const string Blueprint="construction_manual", Story="construction_introduction";
        public static readonly string[] Categories={"exclusive","weapon","armor","accessory","consumable","quest","material"};
        public static string CategoryName(string id) => id=="exclusive" ? "角色专属装备" : id=="weapon" ? "武器" : id=="armor" ? "护具" : id=="accessory" ? "饰品" : id=="consumable" ? "消耗品" : id=="quest" ? "任务道具" : id=="material" ? "特殊材料" : "全部";
        public static bool Single(string category) => category=="exclusive"||category=="weapon"||category=="accessory";
        public static void Normalize(BaseState s)
        {
            if(s.Backpack!=null&&s.Backpack.Version!=0)return;
            var old=s.Backpack;
            if(old!=null&&(old.Revision!=0||old.Catalog?.Count>0||old.Items?.Count>0||old.StoryFlags?.Count>0))throw new ArgumentException("背包缺版本，拒绝覆盖已有物品或剧情");
            s.Backpack=new BaseBackpackState {Version=1,NextId=2,
                Catalog=new List<BaseBagDefinition>{new BaseBagDefinition {Id=Blueprint,Name="营造图册",Category="material",Description="剧情特殊道具。满足营造入门条件后，每次设施升级或解锁固定工事消耗1本；不能代替战略原料。测试版首次接入默认获得1本。",MaxStack=99}},
                Items=new List<BaseBagItem>{new BaseBagItem {Id="bag:1",ItemId=Blueprint,Count=1}},StoryFlags=new List<string>{Story}};
        }
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
        public static bool Add(BaseState s,string item,int count)
        {
            var b=s.Backpack;var d=b.Catalog.SingleOrDefault(x=>x.Id==item);
            if(d==null||count<=0||count>999000||b.Revision>=int.MaxValue-1)return false;
            int room=b.Items.Where(x=>x.ItemId==item&&string.IsNullOrEmpty(x.Holder)).Sum(x=>d.MaxStack-x.Count),slots=(int)Math.Ceiling(Math.Max(0,count-room)/(double)d.MaxStack);
            if(b.GridVersion==0&&b.Items.Count+slots>1000||b.NextId>int.MaxValue-1-slots)return false;
            foreach(var x in b.Items.Where(x=>x.ItemId==item&&string.IsNullOrEmpty(x.Holder))){int n=Math.Min(count,d.MaxStack-x.Count);x.Count+=n;count-=n;if(count==0)break;}
            while(count>0){string id;do{id="bag:"+b.NextId++;}while(b.Items.Any(x=>x.Id==id));int n=Math.Min(count,d.MaxStack);b.Items.Add(new BaseBagItem {Id=id,ItemId=item,Count=n});count-=n;}
            b.Revision++;return true;
        }
        public static bool Consume(BaseState s,string item,int count)
        {
            if(count<=0||Count(s,item)<count||s.Backpack.Revision>=int.MaxValue-1)return false;
            foreach(var x in s.Backpack.Items.Where(x=>x.ItemId==item)){int n=Math.Min(count,x.Count);x.Count-=n;count-=n;if(count==0)break;}
            s.Backpack.Items.RemoveAll(x=>x.Count==0);s.Backpack.Revision++;return true;
        }
    }
}

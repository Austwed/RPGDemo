using System;
using System.Collections.Generic;
using System.Linq;
namespace BaseConquest.Base
{
    [Serializable] public sealed class BaseInnGuest
    {
        public string LeaderId,AreaId;public int PaidDay,Debt;public bool Free,WasCompanion;
        public BaseInnGuest Copy()=>(BaseInnGuest)MemberwiseClone();
    }
    [Serializable] public sealed class BaseInnState
    {
        public List<BaseInnGuest> Guests=new List<BaseInnGuest>();
        public BaseInnState Copy()=>new BaseInnState{Guests=Guests.Select(g=>g.Copy()).ToList()};
    }
    public static class BaseInn
    {
        public const int Capacity=3;
        private sealed class ServicePoint{public BaseConquest.Battle.BattleBoard Board;public int Count,Revision;public BasePoint Point;}
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<BaseMapLayout,ServicePoint> servicePoints=new System.Runtime.CompilerServices.ConditionalWeakTable<BaseMapLayout,ServicePoint>();
        public static bool Enabled(BaseState s)=>BaseRpg.Active(s);
        public static BaseInnGuest Guest(BaseState s,string id)=>s.Inn?.Guests?.FirstOrDefault(g=>g.LeaderId==id);
        public static string Area(BaseState s)=>BaseAdventure.Area(s);
        public static bool Free(BaseState s)=>Area(s)=="home";
        public static bool LocalBase(BaseState s)=>Area(s)==BaseAdventure.Area(s,"base");
        public static int Fee(BaseState s)=>Free(s)?0:s.Demo.Rpg.InnDailyFee;
        public static int Occupied(BaseState s)=>s.Inn.Guests.Count(g=>g.AreaId==Area(s));
        public static string StorageProblem(BaseState s)
        {
            if(!Enabled(s))return "此规则不提供旅店";
            if(Occupied(s)>=Capacity)return "当地寄存名额已满（3名），俘虏继续保留";
            return s.Inventory.Coins<Fee(s)?"钱币不足以支付首日住宿":"";
        }
        public static string RecruitDestination(BaseState s)=>!BaseRpg.HasRoom(s)?"inn":s.Adventure.Members.Count<BaseAdventure.Capacity?"party":LocalBase(s)?"base":"inn";
        public static bool CanRecruit(BaseState s)=>s.Leaders.Count<100&&(RecruitDestination(s)!="inn"||StorageProblem(s)=="");
        private static void Detach(BaseState s,BaseLeader leader)
        {
            var party=s.Muster.Parties.FirstOrDefault(p=>p.Id==leader.PartyId);
            if(party!=null)
            {
                party.Leaders.RemoveAll(r=>r.LeaderId==leader.Id);
                party.CommanderTroops=BaseMuster.Distribute(party.Leaders.Select(r=>new BaseCommanderAllocation{LeaderId=r.LeaderId,Requested=s.Leaders.Single(l=>l.Id==r.LeaderId).DefaultTroops.Copy()}),k=>s.Workers.Where(w=>w.PartyId==party.Id&&w.Kind==k).Sum(w=>w.Count),true);
                party.Preset.SelectLeaders(party.Leaders.Select(r=>r.LeaderId));
            }
            s.Adventure.Members.Remove(leader.Id);leader.PartyId=leader.FacilityId=leader.DesiredFacility="";leader.PostLocked=false;
        }
        private static void Register(BaseState s,BaseLeader leader,bool companion=false)
        {
            s.Inventory.Coins-=Fee(s);s.Inn.Guests.Add(new BaseInnGuest{LeaderId=leader.Id,AreaId=Area(s),PaidDay=s.Day,Free=Free(s),WasCompanion=companion});
            leader.Away=true;leader.HasMapPosition=false;leader.FacilityId=leader.DesiredFacility="";leader.PartyId="";leader.UnavailableDay=s.Day;
        }
        public static string CheckInProblem(BaseState s,string id)
        {
            var l=s.Leaders.FirstOrDefault(p=>p.Id==id);
            if(l==null||id==s.PlayerLeaderId||!BaseAdventure.Following(s,id))return "只能让实际随行伙伴入住，主角继续带队";
            if(!BasePartyInventory.Peaceful(s)||s.Muster.Parties.Any(p=>p.InCombat)||BaseBattleLoot.Pending(s)||BaseWorldJourney.OnWorld(s))return "请在场景中结束交战与战果后办理";
            if(!BaseAdventure.Alive(l)||BaseTraining.Busy(l.Training))return "角色当前无法入住";
            if(s.Muster.Parties.Any(p=>p.Id==l.PartyId&&p.Leaders.Count==1)||s.Supply.Quotas.Any(q=>q.PartyId==l.PartyId&&!string.IsNullOrEmpty(l.PartyId)))return "请先取消军需准备并保留带队队长";
            return StorageProblem(s);
        }
        public static bool CheckIn(BaseState s,string id)
        {
            if(CheckInProblem(s,id)!="")return false;var l=s.Leaders.Single(p=>p.Id==id);
            Detach(s,l);Register(s,l,true);BaseDevelopment.Allocate(s);return true;
        }
        private static void Join(BaseState s,BaseLeader l)
        {
            s.Adventure.Members.Add(l.Id);l.Away=false;l.FacilityId="";l.HasMapPosition=true;
            var point=BaseSpatial.Nearest(s,new BasePoint(s.PlayerX,s.PlayerY-.9));l.MapX=point.X;l.MapY=point.Y;l.MapRegion=s.Campaign.Location;l.MapId=s.Campaign.CurrentMapId;
            var party=s.Muster.Parties.FirstOrDefault(p=>p.Leaders.Any(r=>r.LeaderId==s.PlayerLeaderId));
            if(party!=null){l.Away=true;l.PartyId=party.Id;party.Leaders.Add(new BaseMusterLeader{LeaderId=l.Id,OriginalFacility=""});party.CommanderTroops=BaseMuster.Distribute(party.Leaders.Select(r=>new BaseCommanderAllocation{LeaderId=r.LeaderId,Requested=s.Leaders.Single(p=>p.Id==r.LeaderId).DefaultTroops.Copy()}),k=>s.Workers.Where(w=>w.PartyId==party.Id&&w.Kind==k).Sum(w=>w.Count),true);party.Preset.SelectLeaders(party.Leaders.Select(r=>r.LeaderId));}
            BaseAchievements.PartyChanged(s);
        }
        public static void PlaceRecruit(BaseState s,BaseLeader l,string destination)
        {if(destination=="inn")Register(s,l);else if(destination=="party")Join(s,l);}
        public static IEnumerable<BaseMapObject> Markers(BaseState s,string region=null)
        {
            if(!Enabled(s)||BaseDemoPrologue.Village(s))yield break;
            var layout=BaseSpatial.Layout(s,region);bool town=layout?.Region=="town",camp=layout?.SourceMapId==BaseDemoFinale.Home;
            if(!town&&!camp)yield break;
            var board=BaseBattlefield.Board(s,region);var origin=new BasePoint(town?7:4,town?11:8);
            var cached=servicePoints.GetValue(layout,_=>new ServicePoint());int revision=s.Construction?.Revision??0;
            if(cached.Board!=board||cached.Count!=layout.Objects.Count||cached.Revision!=revision)
            {var legal=board.Positions().Where(p=>board.Walkable(p)&&!layout.Objects.Any(o=>o.Blocks&&o.Object.Contains(p.X+.5,p.Z+.5,.6))).OrderBy(p=>new BasePoint(p.X+.5,p.Z+.5).Distance(origin)).First();cached.Point=new BasePoint(legal.X+.5,legal.Z+.5);cached.Board=board;cached.Count=layout.Objects.Count;cached.Revision=revision;}
            var point=cached.Point;
            yield return new BaseMapObject("town_inn",town?"旅店 · 休息与寄存":"扎营点 · 免费休息与留队",point.X,point.Y,1,1,town,"demo-inn:"+(layout.SourceMapId??layout.Region),town?"town_inn":"campfire",town?"facility":"pixel-prop");
        }
        public static bool AtService(BaseState s)=>BaseSpatial.Objects(s).Any(o=>o.Id=="town_inn"&&BaseInteraction.CanReach(s,o));
        public static string CollectProblem(BaseState s,string id,bool join)
        {
            var g=Guest(s,id);if(g==null||g.AreaId!=Area(s)||!AtService(s))return "请到该队长所在领地的旅店办理";
            if(!BasePartyInventory.Peaceful(s)||BaseBattleLoot.Pending(s))return "请先结束交战和战果";
            if(s.Inventory.Coins<g.Debt)return "请先补清住宿欠费";
            if(!BaseRpg.HasRoom(s))return "营帐名额不足，请先升级或腾出名额";
            if(join&&s.Adventure.Members.Count>=BaseAdventure.Capacity)return "随行队伍已满";
            return !join&&!LocalBase(s)?"本领地没有我方基地":"";
        }
        public static bool Collect(BaseState s,string id,bool join)
        {
            if(CollectProblem(s,id,join)!="")return false;var g=Guest(s,id);var l=s.Leaders.Single(p=>p.Id==id);s.Inventory.Coins-=g.Debt;s.Inn.Guests.Remove(g);l.Away=false;l.UnavailableDay=s.Day;
            if(join)Join(s,l);else {var home=BaseSpatial.Layout(s,"base");var point=BaseSpatial.Nearest(s,new BasePoint(home.SpawnX,home.SpawnY),"base");l.MapX=point.X;l.MapY=point.Y;l.MapRegion="base";l.MapId=home.SourceMapId;l.HasMapPosition=true;}
            if(join&&g.WasCompanion)BaseAchievements.Unlock(s,BaseAchievements.Reunion);BaseDevelopment.Allocate(s);return true;
        }
        public static void AdvanceDay(BaseState s)
        {
            if(!Enabled(s))return;
            foreach(var g in s.Inn.Guests.Where(g=>g.PaidDay<s.Day))
            {int due=g.Free?0:checked((s.Day-g.PaidDay)*s.Demo.Rpg.InnDailyFee),pay=Math.Min(s.Inventory.Coins,due);s.Inventory.Coins-=pay;g.Debt=checked(g.Debt+due-pay);g.PaidDay=s.Day;}
        }
        public static string RestProblem(BaseState s)
        {if(!Enabled(s)||!AtService(s)||!BasePartyInventory.Peaceful(s)||BaseBattleLoot.Pending(s))return "请到旅店结束交战后休息";if(BaseCampaign.Due(s).Any())return "需先处理本日入侵";string lesson=BaseDemoFlow.RestProblem(s);if(lesson!="")return lesson;return s.Inventory.Coins<Fee(s)*BaseAdventure.Members(s).Count()?"当前随行者住宿钱币不足":"";}
        public static bool Rest(BaseState s,int day)
        {if(s.Day!=day||RestProblem(s)!="")return false;int cost=Fee(s)*BaseAdventure.Members(s).Count();s.Inventory.Coins-=cost;if(!BaseEconomy.Rest(s,day)){s.Inventory.Coins+=cost;return false;}return true;}
        public static void Validate(BaseState s)
        {
            s.Inn??=new BaseInnState();
            if(s.Inn.Guests==null||s.Inn.Guests.Count>99||s.Inn.Guests.Any(g=>g==null||string.IsNullOrEmpty(g.AreaId)||g.PaidDay<1||g.PaidDay>s.Day||g.Debt<0||g.Debt>1000000000||!s.Leaders.Any(l=>l.Id==g.LeaderId&&l.Id!=s.PlayerLeaderId&&l.Away&&l.PartyId==""&&l.FacilityId==""&&!BaseAdventure.Contains(s,l.Id)))||s.Inn.Guests.Select(g=>g.LeaderId).Distinct().Count()!=s.Inn.Guests.Count||s.Inn.Guests.GroupBy(g=>g.AreaId).Any(g=>g.Count()>Capacity))throw new ArgumentException("旅店名额、欠费或队长归属非法");
        }
    }
}

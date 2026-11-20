using System;
using System.Collections.Generic;
using System.Linq;

namespace BaseConquest.Base
{
    [Serializable] public sealed class BaseAdventureState
    {
        public int Version;
        public List<string> Members = new List<string>();
        public List<BaseAdventurePause> Pauses = new List<BaseAdventurePause>();
        public BaseAdventureState Copy() => new BaseAdventureState { Version = Version, Members = Members?.ToList(),Pauses=Pauses?.Select(p=>p.Copy()).ToList() };
    }

    // 常驻伙伴身份与按场抽调的小兵、军需配额分开保存。
    public static partial class BaseAdventure
    {
        public const int Capacity = 3;
        public const string FullPartyReply = "队伍人满了，先腾个位置吧。";
        public static bool Full(BaseState state) => state.Adventure.Members.Count >= Capacity;
        public static bool CampResident(BaseState state, string id) => id == "partner_farm" || id == "partner_craft" ||
            state.Demo?.Flow?.ControllerPledged == true && id == "partner_recruit:" + state.Demo.Flow.FirstControllerId;

        public static bool WaitingHere(BaseState state, BaseLeader person) => person.WaitingAtRecruitment &&
            person.MapRegion == state.Campaign.Location && (!BaseSceneInstances.Active(state) || person.MapId == state.Campaign.CurrentMapId);

        public static void WaitAtRecruitment(BaseState state, BaseLeader person, BasePoint position)
        {
            person.WaitingAtRecruitment = person.HasMapPosition = true;
            person.MapRegion = state.Campaign.Location;
            person.MapId = state.Campaign.CurrentMapId;
            person.MapX = position.X; person.MapY = position.Y;
        }

        public static BasePoint CampPost(BaseState state, BaseLeader leader)
        {
            if (state.Demo?.Flow?.ControllerPledged == true && leader.Id == "partner_recruit:" + state.Demo.Flow.FirstControllerId)
            {
                var barracks = BaseSpatial.Layout(state, "base").Objects.First(o => o.Id == "barracks");
                return BaseSpatial.Nearest(state, new BasePoint(barracks.X + barracks.Width * .5 + .45, barracks.Y + .55), "base");
            }
            if (CampResident(state, leader.Id))
                return BaseSpatial.Nearest(state, leader.HasHomePosition ? new BasePoint(leader.HomeX,leader.HomeY) :
                    new BasePoint(leader.Id == "partner_farm" ? 8 : 10, 7), "base");
            return BaseSpatial.WorkPost(state,"home");
        }

        public static void StationAtCamp(BaseState state, BaseLeader leader)
        {
            var post = CampPost(state, leader);
            leader.WaitingAtRecruitment = false;
            leader.CampPostSet = true;
            leader.HasHomePosition = leader.HasMapPosition = true;
            leader.HomeX = leader.MapX = post.X; leader.HomeY = leader.MapY = post.Y;
            leader.MapRegion = "base"; leader.MapId = BaseSpatial.Layout(state,"base").SourceMapId;
        }

        public static bool InviteWaiting(BaseState state, BaseLeader person)
        {
            if (!WaitingHere(state, person) || !BasePartyInventory.Peaceful(state) || state.Campaign.HasActiveBattle ||
                Full(state) || Contains(state,person.Id) || !BaseDevelopment.CanFight(person)) return false;
            person.WaitingAtRecruitment = false;
            BaseAftermath.JoinRecruit(state, person);
            return true;
        }
        public static bool Alive(BaseLeader leader) => leader != null && (string.IsNullOrEmpty(leader.Fate) || leader.Fate == "active");
        public static bool Contains(BaseState state, string id) => state.Adventure?.Members?.Contains(id) == true;
        public static bool Following(BaseState state,string id)=>Contains(state,id)&&PauseFor(state,id)==null;
        public static IEnumerable<BaseLeader> Members(BaseState state) => (state.Adventure?.Members ?? new List<string>())
            .Where(id=>Following(state,id)).Select(id => state.Leaders.FirstOrDefault(l => l.Id == id)).Where(l => l != null);
        public static void Normalize(BaseState state)
        {
            var old = state.Adventure;
            if (old != null && old.Version != 0) return;
            if (old?.Members?.Count > 0) throw new ArgumentException("常驻小队缺版本，拒绝覆盖已有名单");
            // 迁移活动队伍时保留实际出征者；没有出征记录的新开局须通过对话邀请伙伴。
            var party = state.Muster.Parties.FirstOrDefault(p => p.Leaders.Any(l => l.LeaderId == state.PlayerLeaderId));
            var ids = new[] { state.PlayerLeaderId }.Concat(party?.Leaders.Select(l => l.LeaderId) ?? Enumerable.Empty<string>()).Distinct().Take(Capacity).ToList();
            state.Adventure = new BaseAdventureState { Version = 1, Members = ids };
            foreach (var leader in state.Leaders)
            {
                leader.Resting = false;
                if (Alive(leader) && !leader.Away && state.Campaign.HasActiveBattle == false && state.Aftermath.GameOver == false && leader.Health >= 0 && leader.Health < 1)
                    leader.Health = Math.Min(1, leader.MaxHealth);
                if (Contains(state, leader.Id)) leader.FacilityId = "";
            }
        }
        public static void Validate(BaseState state)
        {
            var a = state.Adventure;
            if (a == null || a.Version != 1 || a.Members == null || a.Members.Count < 1 || a.Members.Count > Capacity || a.Members[0] != state.PlayerLeaderId || a.Members.Distinct().Count() != a.Members.Count || a.Members.Any(id => !state.Leaders.Any(l => l.Id == id)))
                throw new ArgumentException("常驻小队版本、人数或人物身份非法");
            if (Members(state).Any(l => l.FacilityId != "")) throw new ArgumentException("随行队长不能同时管理设施");
            ValidatePauses(state);
            if(!state.Campaign.HasActiveBattle&&!state.Aftermath.GameOver&&state.Leaders.Any(l=>Alive(l)&&!l.Away&&l.Health<1)) throw new ArgumentException("战外存活队长至少保留1生命");
        }
        public static string ChangeProblem(BaseState state, string id, bool join) => ChangeProblem(state, id, join, false);

        private static string ChangeProblem(BaseState state, string id, bool join, bool invited)
        {
            var leader = state.Leaders.FirstOrDefault(l => l.Id == id);
            if (leader == null) return "找不到这名伙伴";
            if (id == state.PlayerLeaderId) return "主角固定带队";
            if (join && Full(state) && !Contains(state,id)) return FullPartyReply;
            if(PauseFor(state,id)!=null)return "请与暂离伙伴交谈，选择恢复随行";
            if(join&&state.Adventure.Pauses.Count>0&&!Contains(state,id))return "暂离伙伴占位期间不能添加新成员，请先让暂离者归队";
            if (leader.Relocating || join && !leader.WaitingAtRecruitment && !invited && leader.VillageResident != BaseSettlements.AtVillage(state)) return "伙伴在异地";
            bool local = BaseSettlements.AtVillage(state) || state.Campaign.Location == "base" || invited && state.Campaign.Location == "town";
            if (state.Aftermath.GameOver || state.Campaign.HasActiveBattle || !local || state.Muster.Parties.Count != 0) return "请先返回基地并结束外出队伍，再对话调整伙伴";
            if (join && (!Alive(leader) || leader.Health <= 0)) return "阵亡或失联者不能加入";
            if (leader.Away) return "该伙伴尚未返营";
            if (join&&!BaseDevelopment.CanFight(leader))return "该伙伴当前无法行动或正在训练";
            if (join == Contains(state, id)) return join ? "已经在冒险小队中" : "该伙伴未随行";
            return "";
        }
        public static bool SetMember(BaseState state, string id, bool join) => SetMember(state, id, join, false);

        internal static bool FollowInvitation(BaseState state, string id) => SetMember(state, id, true, true);

        private static bool SetMember(BaseState state, string id, bool join, bool invited)
        {
            if (ChangeProblem(state, id, join, invited) != "") return false;
            var leader = state.Leaders.Single(l => l.Id == id);
            if (join) state.Adventure.Members.Add(id); else state.Adventure.Members.Remove(id);
            leader.WaitingAtRecruitment = false;
            leader.VillageResident = BaseSettlements.AtVillage(state);
            leader.DesiredFacility = leader.FacilityId = "";
            BaseDevelopment.Allocate(state);BaseAchievements.PartyChanged(state);
            return true;
        }
        public static BaseArmyPreset Selection(BaseState state, string purpose)
        {
            var selection = state.Muster.Presets.Single(p => p.Purpose == purpose).Copy();
            selection.SelectLeaders(Members(state).Where(BaseDevelopment.CanFight).Select(l => l.Id));
            return selection;
        }
        public static BaseMusterPlan Preview(BaseState state, string purpose, bool bypass = false) => BaseMuster.Preview(state, purpose, bypass, Selection(state, purpose));

        public static string Region(BaseLeader leader) => string.IsNullOrEmpty(leader.MapRegion) ? "base" : leader.MapRegion;
        public static string MapKey(BaseState state,BaseLeader leader)=>BaseSceneInstances.Active(state)&&!string.IsNullOrEmpty(leader.MapId)?leader.MapId:Region(leader);
        // 切区是入口落点；同一区域内跟随只能实际寻路，不能用此方法追赶瞬移。
        public static void Arrive(BaseState state)
        {
            foreach (var recruit in state.Leaders.Where(l => l.WaitingAtRecruitment && !WaitingHere(state,l)))
                StationAtCamp(state, recruit);
            if(state.Campaign.Location=="world"||state.Adventure.Pauses.Any(p=>p.AreaId!=Area(state)))RecallPaused(state);
            var region = state.Campaign.Location;
            var occupants = new List<BaseMapObject> { new BaseMapObject("player", "主角", state.PlayerX, state.PlayerY, .45, .45) };
            foreach (var leader in Members(state).Where(l => l.Id != state.PlayerLeaderId && Alive(l) && l.Health > 0))
            {
                var p = BaseSpatial.Nearest(state, new BasePoint(state.PlayerX, state.PlayerY - .9), region, occupants);
                leader.MapX = p.X; leader.MapY = p.Y; leader.MapRegion = region; leader.HasMapPosition = true;
                leader.MapId=BaseSceneInstances.Active(state)?state.Campaign.CurrentMapId:null;
                occupants.Add(new BaseMapObject(leader.Id, leader.Name, p.X, p.Y, .45, .45));
            }
        }
    }
}

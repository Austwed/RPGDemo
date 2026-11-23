using System;
using System.Collections.Generic;
using System.Linq;

namespace BaseConquest.Base
{
    [Serializable] public sealed class BaseAdventurePause
    {
        public string LeaderId,Region,MapId,AreaId,PartyId="",Purpose="",OriginalFacility="";
        public double X,Y;
        public BaseCommanderAllocation Allocation;
        public List<BaseMusterSource> Sources=new List<BaseMusterSource>();
        public BaseAdventurePause Copy(){var p=(BaseAdventurePause)MemberwiseClone();p.Allocation=Allocation?.Copy();p.Sources=Sources.Select(s=>s.Copy()).ToList();return p;}
    }
    public static partial class BaseAdventure
    {
        public static BaseAdventurePause PauseFor(BaseState s,string id)=>s.Adventure?.Pauses?.FirstOrDefault(p=>p.LeaderId==id);
        public static string Area(BaseState s,string key=null)
        {
            string region=key??s.Campaign.Location;
            if(BaseSceneInstances.Active(s))
            {
                var map=BaseSceneInstances.Layout(s,key);
                if(map.Region=="world")return BaseWorldJourney.AreaForNode(s,s.Campaign.WorldJourney.NodeId);
                var node=s.Campaign.WorldJourney.Nodes.FirstOrDefault(n=>n.MapIds.Contains(map.SourceMapId));if(node!=null)return BaseWorldJourney.AreaForMap(s,map.SourceMapId);
                while(!string.IsNullOrEmpty(map.ParentMapId))map=BaseSceneInstances.Layout(s,map.ParentMapId);
                region=map.Region;
                if(!BaseDemo.Active(s)&&region!="base"&&region!="town"&&region!="perimeter")return map.SourceMapId;
            }
            if(BaseDemo.Active(s))return "demo-local";
            return region=="base"||region=="town"||region=="perimeter"?"base-local":region;
        }
        public static string PauseProblem(BaseState s,string id,bool resume=false)
        {
            var l=s.Leaders.FirstOrDefault(p=>p.Id==id);
            if(l==null||id==s.PlayerLeaderId||!Contains(s,id))return "只能调整随行伙伴，主角固定带队";
            if(!BasePartyInventory.Peaceful(s)||BaseBattleLoot.Pending(s)||BaseWorldJourney.OnWorld(s)||s.Muster.Parties.Any(p=>p.InCombat))return "请在非战斗地区场景中调整伙伴";
            if(!Alive(l)||l.Health<1)return "伙伴当前无法行动";
            var paused=PauseFor(s,id);
            if(resume)
            {
                if(paused==null)return "该伙伴未暂离";
                if(paused.AreaId!=Area(s)||paused.Region!=s.Campaign.Location||BaseSceneInstances.Active(s)&&paused.MapId!=s.Campaign.CurrentMapId)return "请返回暂离地点与伙伴交谈";
            }
            else if(paused!=null)return "该伙伴已经暂离";
            var party=s.Muster.Parties.FirstOrDefault(p=>p.Id==l.PartyId);
            if(party!=null&&s.Supply.Quotas.Any(q=>q.PartyId==party.Id))return "请先取消本队军需准备";
            if(!resume&&party?.Leaders.Count==1)return "请先让这支独立队伍整体返营";
            return "";
        }
        public static bool Pause(BaseState s,string id)=>DetachForPause(s,id);
        private static bool DetachForPause(BaseState s,string id)
        {
            if(PauseProblem(s,id)!="")return false;var l=s.Leaders.Single(p=>p.Id==id);
            var p=new BaseAdventurePause {LeaderId=id,Region=s.Campaign.Location,MapId=BaseSceneInstances.Active(s)?s.Campaign.CurrentMapId:null,AreaId=Area(s),X=l.HasMapPosition?l.MapX:s.PlayerX,Y=l.HasMapPosition?l.MapY:s.PlayerY};
            var party=s.Muster.Parties.FirstOrDefault(t=>t.Id==l.PartyId);
            if(party!=null)
            {
                if(!s.Adventure.Pauses.Any(held=>held.PartyId==party.Id)){int order=0;foreach(var w in s.Workers.Where(w=>w.PartyId==party.Id))w.PartyOrder=++order;}
                var formation=BaseMuster.CurrentFormation(s,party)??BaseMuster.Distribute(party.Leaders.Select(r=>new BaseCommanderAllocation {LeaderId=r.LeaderId,Requested=s.Leaders.Single(person=>person.Id==r.LeaderId).DefaultTroops.Copy()}),k=>s.Workers.Where(w=>w.PartyId==party.Id&&w.Kind==k).Sum(w=>w.Count),true);
                p.PartyId=party.Id;p.Purpose=party.Purpose;p.OriginalFacility=party.Leaders.Single(r=>r.LeaderId==id).OriginalFacility;p.Allocation=formation.Single(r=>r.LeaderId==id).Copy();
                foreach(BaseTroopKind kind in Enum.GetValues(typeof(BaseTroopKind)))
                {
                    var numbers=p.Allocation.SoldierNumbers?.ToHashSet();
                    int left=p.Allocation.Actual.Count(kind),skip=formation.TakeWhile(row=>row.LeaderId!=id).Sum(row=>row.Actual.Count(kind));
                    foreach(var worker in s.Workers.Where(w=>w.Away&&w.PartyId==party.Id&&w.Kind==kind).ToArray())
                    {
                        BaseWorkerBucket held;
                        if(numbers!=null)
                        {
                            var chosen=worker.SoldierNumbers.Where(numbers.Contains).ToList();
                            if(chosen.Count==0)continue;
                            held=worker.TakeNumbers(chosen);
                        }
                        else
                        {
                            if(skip>=worker.Count){skip-=worker.Count;continue;}
                            int count=Math.Min(left,worker.Count-skip);
                            if(count==0)continue;
                            held=worker.Take(skip,count);skip=0;
                        }
                        if(!p.Sources.Any(source=>source.Id==worker.SourceId))p.Sources.Add(party.Sources.Single(source=>source.Id==worker.SourceId).Copy());
                        held.HeldBy=id;held.HeldSourceId=worker.SourceId;held.PartyId=held.SourceId="";s.Workers.Add(held);left-=held.Count;
                    }
                    if(left!=0)throw new InvalidOperationException("分队士兵与实际出征来源不一致");
                }
                s.Workers.RemoveAll(w=>w.Count==0);party.Leaders.RemoveAll(r=>r.LeaderId==id);party.CommanderTroops=formation.Where(r=>r.LeaderId!=id).ToList();party.Preset.SelectLeaders(party.Leaders.Select(r=>r.LeaderId));l.PartyId="";
            }
            l.HasMapPosition=true;l.MapX=p.X;l.MapY=p.Y;l.MapRegion=p.Region;l.MapId=p.MapId;l.FacilityId="";s.Adventure.Pauses.Add(p);BaseDevelopment.Allocate(s);return true;
        }
        public static bool Resume(BaseState s,string id)
        {
            if(PauseProblem(s,id,true)!="")return false;var paused=PauseFor(s,id);bool outside=paused.Region!="base";RestorePaused(s,paused);BaseDevelopment.Allocate(s);BaseAchievements.PartyChanged(s);if(outside)BaseAchievements.Unlock(s,BaseAchievements.Reunion);return true;
        }
        internal static string LeaveHereProblem(BaseState s, string id) =>
            PauseProblem(s, id, PauseFor(s, id) != null);

        internal static bool LeaveHere(BaseState s, string id)
        {
            if (LeaveHereProblem(s, id) != "") return false;
            var leader = s.Leaders.Single(person => person.Id == id);
            var position = leader.HasMapPosition ? new BasePoint(leader.MapX, leader.MapY) :
                new BasePoint(s.PlayerX, s.PlayerY);
            var paused = PauseFor(s, id);
            // An explicit expedition may have brought soldiers. Return its detached contingent by the existing path.
            if (paused == null && s.Muster.Parties.Any(party => party.Id == leader.PartyId))
            {
                if (!Pause(s, id)) return false;
                paused = PauseFor(s, id);
            }
            if (paused != null) RestorePaused(s, paused, true);
            s.Adventure.Members.Remove(id);
            leader.FacilityId = leader.DesiredFacility = "";
            WaitAtRecruitment(s, leader, position);
            BaseDevelopment.Allocate(s);
            BaseAchievements.PartyChanged(s);
            return true;
        }

        public static bool SendToCamp(BaseState s, string id)
        {
            var leader = s.Leaders.FirstOrDefault(person => person.Id == id);
            if (leader == null || id == s.PlayerLeaderId || !BasePartyInventory.Peaceful(s)) return false;
            if (!WaitingHere(s,leader))
            {
                var pause = PauseFor(s,id);
                if (pause != null && PauseProblem(s,id,true) != "") return false;
                if (pause == null && !Pause(s,id)) return false;
                RestorePaused(s,PauseFor(s,id),true);
                s.Adventure.Members.Remove(id);
            }
            StationAtCamp(s,leader);
            BaseDevelopment.Allocate(s);
            BaseAchievements.PartyChanged(s);
            return true;
        }
        private static void RestorePaused(BaseState s,BaseAdventurePause p,bool toCamp = false)
        {
            var l=s.Leaders.Single(person=>person.Id==p.LeaderId);
            var party=toCamp?null:s.Muster.Parties.FirstOrDefault(t=>t.Id==p.PartyId)??s.Muster.Parties.FirstOrDefault(t=>(t.Purpose==p.Purpose||string.IsNullOrEmpty(p.Purpose)&&t.Purpose=="expedition")&&t.Leaders.Any(r=>r.LeaderId==s.PlayerLeaderId));
            var rows=party==null?null:BaseMuster.CurrentFormation(s,party)??BaseMuster.Distribute(party.Leaders.Select(r=>new BaseCommanderAllocation {LeaderId=r.LeaderId,Requested=s.Leaders.Single(person=>person.Id==r.LeaderId).DefaultTroops.Copy()}),k=>s.Workers.Where(w=>w.PartyId==party.Id&&w.Kind==k).Sum(w=>w.Count),true);
            var held=s.Workers.Where(w=>w.HeldBy==p.LeaderId).ToArray();
            if(p.Allocation!=null)p.Allocation.SoldierNumbers=held.SelectMany(w=>w.SoldierNumbers).ToList();
            foreach(var worker in held)
            {
                var source=p.Sources.Single(item=>item.Id==worker.HeldSourceId);
                worker.Away=party!=null;worker.PartyId=party?.Id??"";worker.SourceId=party!=null?source.Id:"";worker.DesiredFacility=source.FacilityId;worker.FacilityId="";worker.UnavailableDay=s.Day;if(party!=null)worker.Resting=false;
                if(party!=null&&!party.Sources.Any(item=>item.Id==source.Id))party.Sources.Add(source.Copy());
                if(party==null)worker.Resting=worker.Health<worker.MaxHealth*BaseDevelopment.DutyThreshold;
                if(party==null)worker.PartyOrder=0;
                worker.HeldBy=worker.HeldSourceId="";
            }
            l.Away=party!=null;l.PartyId=party?.Id??"";l.FacilityId="";l.UnavailableDay=s.Day;
            if(party!=null)
            {
                // Restore the saved composition before distributing any newly returned soldiers.
                rows.Add(p.Allocation?.LeaderId==l.Id?p.Allocation.Copy():new BaseCommanderAllocation {LeaderId=l.Id,Requested=l.DefaultTroops.Copy()});party.Leaders.Add(new BaseMusterLeader {LeaderId=l.Id,OriginalFacility=p.OriginalFacility});
                party.Leaders=party.Leaders.OrderBy(r=>s.Adventure.Members.IndexOf(r.LeaderId)).ToList();party.CommanderTroops=party.Leaders.Select(r=>rows.Single(a=>a.LeaderId==r.LeaderId)).ToList();party.Preset.SelectLeaders(party.Leaders.Select(r=>r.LeaderId));
                var ordered=s.Workers.Where(w=>w.PartyId==party.Id).OrderBy(w=>w.PartyOrder).ToArray();s.Workers.RemoveAll(w=>w.PartyId==party.Id);s.Workers.AddRange(ordered);
            }
            s.Adventure.Pauses.Remove(p);
        }
        public static void RecallPaused(BaseState s)
        {foreach(var p in s.Adventure.Pauses.ToArray())RestorePaused(s,p);}
        private static void ValidatePauses(BaseState s)
        {
            var pauses=s.Adventure.Pauses;
            if(pauses==null||pauses.Count>2||pauses.Any(p=>p==null||p.LeaderId==s.PlayerLeaderId||!Contains(s,p.LeaderId)||string.IsNullOrEmpty(p.AreaId)||!BaseSpatial.Walkable(s,p.X,p.Y,BaseSceneInstances.Active(s)?p.MapId:p.Region)||p.Sources==null)||pauses.Select(p=>p.LeaderId).Distinct().Count()!=pauses.Count)throw new ArgumentException("暂离成员、占位或站位非法");
            foreach(var p in pauses)
            {
                var l=s.Leaders.Single(person=>person.Id==p.LeaderId);if(l.PartyId!=""||l.FacilityId!="")throw new ArgumentException("暂离队长不能同时加入活动编组或任职");
                if(!string.IsNullOrEmpty(p.PartyId)&&(p.Allocation?.LeaderId!=p.LeaderId||!BaseMuster.CompositionValid(p.Allocation?.Actual)||Enum.GetValues(typeof(BaseTroopKind)).Cast<BaseTroopKind>().Any(k=>p.Allocation.Actual.Count(k)!=s.Workers.Where(w=>w.HeldBy==p.LeaderId&&w.Kind==k).Sum(w=>w.Count))))throw new ArgumentException("暂离分队兵力不一致");
            }
            if(s.Workers.Any(w=>!string.IsNullOrEmpty(w.HeldBy)&&(!pauses.Any(p=>p.LeaderId==w.HeldBy&&p.Sources.Any(source=>source.Id==w.HeldSourceId&&source.Kind==w.Kind))||!w.Away||w.PartyId!=""||w.SourceId!=""||w.FacilityId!="")))throw new ArgumentException("暂离普通兵缺少真实成员或来源");
        }
    }
}

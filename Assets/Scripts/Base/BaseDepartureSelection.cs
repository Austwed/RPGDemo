using System;
using System.Collections.Generic;
using System.Linq;
using BaseConquest.Battle;

namespace BaseConquest.Base
{
    // 同属性兵桶中的单兵序号只在本次选择快照内有效，不冒充跨日持久身份。
    public sealed class BaseSoldierPick
    {
        public int BucketIndex, Ordinal;
        public string Key => BucketIndex+":"+Ordinal;
        public BaseSoldierPick Copy() => (BaseSoldierPick)MemberwiseClone();
    }
    public sealed class BaseSoldierRow
    {
        public BaseSoldierPick Pick;
        public BaseTroopKind Kind;
        public SoldierStats Stats;
        public double Health, MaxHealth;
        public string Facility, UnavailableReason;
        public bool EffectiveWorker,Recruited;
        public bool Available => UnavailableReason=="";
    }
    public static partial class BaseMuster
    {
        public static IEnumerable<BaseSoldierRow> SoldierRows(BaseState s, BaseTroopKind kind)
        {
            foreach(var entry in s.Workers.Select((w,i)=>new {Worker=w,Index=i}).Where(x=>x.Worker.Kind==kind&&x.Worker.Count>0)
                .OrderBy(x=>x.Worker.FacilityId=="" ? -1 : s.Muster.Facilities.FirstOrDefault(f=>f.FacilityId==x.Worker.FacilityId)?.Priority??0).ThenBy(x=>x.Index))
            {
                var w=entry.Worker;
                var stats=w.HasCombatProfile ? w.CombatStats.Clone() : new ArmyDefinition().Stats(BaseCampaign.ToBattle(kind)).Clone();
                stats.Health=(float)w.MaxHealth;
                string reason=w.Away ? "已外出" : w.Health<=0 ? "无法行动" : w.Health<w.MaxHealth*BaseDevelopment.FightThreshold ? "伤重不宜出战" : "";
                for(int n=0;n<w.Count;n++) yield return new BaseSoldierRow {Pick=new BaseSoldierPick {BucketIndex=entry.Index,Ordinal=n},Kind=kind,Stats=stats,
                    Health=w.Health,MaxHealth=w.MaxHealth,Facility=w.Away ? "外出队伍" : BaseEconomy.FacilityName(w.FacilityId),EffectiveWorker=BaseDevelopment.Effective(s,w),Recruited=w.Recruited,UnavailableReason=reason};
            }
        }
        private static void SelectExactWorkers(BaseState s, BaseMusterPlan plan, BaseArmyPreset preset)
        {
            var picks=plan.ManualPicks;
            if(picks.Count>90||picks.Any(p=>p==null||p.BucketIndex<0||p.BucketIndex>=s.Workers.Count||p.Ordinal<0||p.Ordinal>=s.Workers[p.BucketIndex].Count||!BaseDevelopment.CanFight(s.Workers[p.BucketIndex]))||picks.Select(p=>p.Key).Distinct().Count()!=picks.Count)
                throw new ArgumentException("逐兵选择已变化或包含不可出战／重复单位，请重新选择");
            foreach(var kind in Enum.GetValues(typeof(BaseTroopKind)).Cast<BaseTroopKind>())
            {
                int selected=picks.Count(p=>s.Workers[p.BucketIndex].Kind==kind),available=s.Workers.Where(w=>w.Kind==kind&&BaseDevelopment.CanFight(w)).Sum(w=>w.Count);
                if(selected!=preset.Count(kind)||selected>Math.Min(30,available))throw new ArgumentException("所选人数不能超过可用兵力或本队兵种上限");
                if(!plan.IgnoreProtection&&selected>Math.Max(0,available-s.Muster.Keep(kind)))throw new InvalidOperationException("已达到留守限制：本兵种须留在基地 "+s.Muster.Keep(kind)+"人；可切换为允许抽调留守和生产人员");
            }
            plan.Takes=picks.GroupBy(p=>p.BucketIndex).OrderBy(g=>g.Key).Select(g=>new BaseMusterTake {BucketIndex=g.Key,Count=g.Count()}).ToList();
            if(!plan.IgnoreProtection&&plan.Leaders.Count>0)
            {
                var trial=s.Copy();Apply(trial,plan);
                if(s.Muster.Facilities.Any(f=>BaseEconomy.EligibleWorkers(trial,f.FacilityId)<(plan.PriorityMode=="production" ? Math.Max(1,f.MinimumWorkers) : Math.Min(f.MinimumWorkers,BaseEconomy.EligibleWorkers(s,f.FacilityId)))))throw new InvalidOperationException("已达到生产保留人数：继续带兵会影响设施生产；可选择作战优先");
            }
        }
    }
    // 数量按钮与逐兵表共用同一名单。加人必须找到可调的真实单位，绝不只增加请求数字。
    public sealed class BaseDepartureSelection
    {
        public BaseArmyPreset Preset { get; private set; }
        public List<BaseSoldierPick> Picks { get; private set; } = new List<BaseSoldierPick>();
        public string Stamp { get; private set; }
        public string PriorityMode {get;private set;}="";
        public BaseDepartureSelection(BaseState state,string purpose,bool bypass=false,string priorityMode="")
        {
            var preset=priorityMode=="" ? BaseAdventure.Selection(state,purpose) : BaseMuster.FormationSelection(state,purpose);
            var plan=BaseMuster.Preview(state,purpose,bypass,preset,null,priorityMode);Preset=preset;Stamp=BaseMuster.Snapshot(state);PriorityMode=priorityMode;
            foreach(var take in plan.Takes)for(int n=0;n<take.Count;n++)Picks.Add(new BaseSoldierPick {BucketIndex=take.BucketIndex,Ordinal=n});
            SynchronizeCounts(state);
        }
        private BaseDepartureSelection() {}
        public BaseDepartureSelection Copy() => new BaseDepartureSelection {Preset=Preset.Copy(),Picks=Picks.Select(p=>p.Copy()).ToList(),Stamp=Stamp,PriorityMode=PriorityMode};
        public int Count(BaseState s,BaseTroopKind kind) => Picks.Count(p=>p.BucketIndex>=0&&p.BucketIndex<s.Workers.Count&&s.Workers[p.BucketIndex].Kind==kind);
        public bool Selected(BaseSoldierPick pick) => Picks.Any(p=>p.Key==pick.Key);
        private void SynchronizeCounts(BaseState s) {foreach(var kind in Enum.GetValues(typeof(BaseTroopKind)).Cast<BaseTroopKind>())Preset.SetCount(kind,Count(s,kind));}
        public BaseMusterPlan Preview(BaseState s,bool bypass)
        {
            if(Stamp!=BaseMuster.Snapshot(s))throw new InvalidOperationException("人员或经营安排已变化，请重新读取预设后选兵");
            return BaseMuster.Preview(s,Preset.Purpose,bypass,Preset,Picks,PriorityMode);
        }
        public bool Toggle(BaseState s,BaseSoldierPick pick,bool bypass,out string error)
        {
            error="";var trial=Copy();
            if(trial.Selected(pick))
            {
                if(Stamp!=BaseMuster.Snapshot(s)){error="人员已变化，请重新读取预设";return false;}
                trial.Picks.RemoveAll(p=>p.Key==pick.Key);trial.SynchronizeCounts(s);Picks=trial.Picks;Preset=trial.Preset;return true;
            }
            trial.Picks.Add(pick.Copy());
            trial.SynchronizeCounts(s);
            try {var plan=trial.Preview(s,bypass);if(PriorityMode=="production"&&plan.Production?.Allowed!=true)throw new InvalidOperationException(plan.Production?.Problem??"无法保障三天经营");Picks=trial.Picks;Preset=trial.Preset;return true;}
            catch(ArgumentException e){error=e.Message;return false;}catch(InvalidOperationException e){error=e.Message;return false;}
        }
        public bool Adjust(BaseState s,BaseTroopKind kind,int delta,bool bypass,out string error)
        {
            error="没有更多可调单位";
            if(delta<0){var pick=Picks.LastOrDefault(p=>s.Workers[p.BucketIndex].Kind==kind);return pick!=null&&Toggle(s,pick,bypass,out error);}
            if(Count(s,kind)>=30){error="已达本队兵种上限";return false;}
            foreach(var entry in s.Workers.Select((w,i)=>new {Worker=w,Index=i}).Where(x=>x.Worker.Kind==kind&&BaseDevelopment.CanFight(x.Worker))
                .OrderBy(x=>x.Worker.FacilityId=="" ? -1 : s.Muster.Facilities.FirstOrDefault(f=>f.FacilityId==x.Worker.FacilityId)?.Priority??0).ThenBy(x=>x.Index))
            {
                int ordinal=0;while(ordinal<entry.Worker.Count&&Selected(new BaseSoldierPick {BucketIndex=entry.Index,Ordinal=ordinal}))ordinal++;
                if(ordinal<entry.Worker.Count&&Toggle(s,new BaseSoldierPick {BucketIndex=entry.Index,Ordinal=ordinal},bypass,out error))return true;
            }
            return false;
        }
        public bool CanAdd(BaseState s,BaseTroopKind kind,bool bypass) => Copy().Adjust(s,kind,1,bypass,out _);
        public BaseDepartureCapacity Capacity(BaseState s,BaseTroopKind kind,bool bypass)
        {
            var trial=Copy();int selected=Count(s,kind),ready=s.Workers.Where(w=>w.Kind==kind&&BaseDevelopment.CanFight(w)).Sum(w=>w.Count);
            string reason="";while(trial.Count(s,kind)<30&&trial.Adjust(s,kind,1,bypass,out reason)) {}
            int limit=trial.Count(s,kind);
            if(limit==30)reason="每种兵最多携带30人";
            else if(limit>=ready)reason="没有更多可出战人员（生命低于50%、外出者不能随队）";
            return new BaseDepartureCapacity {Selected=selected,Ready=ready,Limit=limit,Reserved=Math.Max(0,ready-limit),Reason=reason};
        }
        public bool CanToggle(BaseState s,BaseSoldierPick pick,bool bypass,out string reason) => Copy().Toggle(s,pick,bypass,out reason);
    }
    public sealed class BaseDepartureCapacity
    {
        public int Selected,Ready,Limit,Reserved;
        public string Reason;
        public string Summary => "已选 "+Selected+"人 · 最多可带 "+Limit+"人";
        public string Detail => "可出战 "+Ready+"人"+(Reserved>0 ? " · 需留在基地 "+Reserved+"人" : "")+(Selected>=Limit ? "；"+Reason : "");
    }
}

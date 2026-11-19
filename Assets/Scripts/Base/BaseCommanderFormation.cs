using System;
using System.Collections.Generic;
using System.Linq;
namespace BaseConquest.Base
{
    [Serializable] public sealed class BaseCommanderTroops
    {
        public int Version=1,Melee,Ranged,Cavalry;
        public int Count(BaseTroopKind kind)=>kind==BaseTroopKind.Melee ? Melee : kind==BaseTroopKind.Ranged ? Ranged : Cavalry;
        public void Set(BaseTroopKind kind,int n){if(kind==BaseTroopKind.Melee)Melee=n;else if(kind==BaseTroopKind.Ranged)Ranged=n;else Cavalry=n;}
        public BaseCommanderTroops Copy()=>(BaseCommanderTroops)MemberwiseClone();
    }
    [Serializable] public sealed class BaseCommanderAllocation
    {
        public string LeaderId;
        public BaseCommanderTroops Requested=new BaseCommanderTroops(),Actual=new BaseCommanderTroops();
        public List<int> SoldierNumbers;
        public BaseCommanderAllocation Copy()=>new BaseCommanderAllocation {LeaderId=LeaderId,Requested=Requested.Copy(),Actual=Actual.Copy(),SoldierNumbers=SoldierNumbers?.ToList()};
    }
    public sealed class BaseProductionDay
    {
        public int Day,FoodProduced,MaterialsSpent,FoodAfter,MaterialsAfter,FarmWorkers,WorkshopWorkers;
    }
    public sealed class BaseProductionForecast
    {
        public bool Allowed;
        public string Problem="";
        public int FailedDay,Deficit;
        public readonly List<BaseProductionDay> Days=new List<BaseProductionDay>();
    }
    public static partial class BaseMuster
    {
        public static bool CompositionValid(BaseCommanderTroops c)=>c!=null&&c.Version==1&&Enum.GetValues(typeof(BaseTroopKind)).Cast<BaseTroopKind>().All(k=>c.Count(k)>=0&&c.Count(k)<=30);
        public static void NormalizeCompositions(BaseState s)
        {
            var ids=s.Adventure.Members;var old=s.Muster.Presets.Single(p=>p.Purpose=="expedition");
            foreach(var l in s.Leaders)
            {
                if(l.DefaultTroops==null||l.DefaultTroops.Version==0&&l.DefaultTroops.Melee==0&&l.DefaultTroops.Ranged==0&&l.DefaultTroops.Cavalry==0)
                {
                    l.DefaultTroops=new BaseCommanderTroops();int slot=ids.IndexOf(l.Id);
                    if(slot>=0)foreach(BaseTroopKind k in Enum.GetValues(typeof(BaseTroopKind)))l.DefaultTroops.Set(k,old.Count(k)/ids.Count+(slot<old.Count(k)%ids.Count ? 1 : 0));
                }
                if(!CompositionValid(l.DefaultTroops))throw new ArgumentException("队长默认兵力版本或人数非法");
            }
        }
        public static BaseArmyPreset FormationSelection(BaseState s,string purpose)
        {
            var selected=BaseAdventure.Selection(s,purpose);
            foreach(BaseTroopKind k in Enum.GetValues(typeof(BaseTroopKind)))selected.SetCount(k,Math.Min(30,selected.Candidates.Sum(id=>s.Leaders.Single(l=>l.Id==id).DefaultTroops.Count(k))));
            return selected;
        }
        public static BaseMusterPlan PreviewFormation(BaseState s,string purpose,string mode="production")
        {
            BaseEconomy.Validate(s);return Preview(s,purpose,mode=="combat",FormationSelection(s,purpose),null,mode);
        }
        public static List<BaseCommanderAllocation> Distribute(IEnumerable<BaseCommanderAllocation> requested,Func<BaseTroopKind,int> available,bool allowExtra=false)
        {
            var rows=requested.Select(r=>r.Copy()).ToList();
            foreach(BaseTroopKind k in Enum.GetValues(typeof(BaseTroopKind)))
            {
                int left=available(k);
                foreach(var row in rows){int take=Math.Min(left,row.Requested.Count(k));row.Actual.Set(k,take);left-=take;}
                if(left>0&&allowExtra&&rows.Count>0)rows[0].Actual.Set(k,rows[0].Actual.Count(k)+left);
                else if(left>0)throw new ArgumentException("所选兵力超过队长预设需求；请在兵营修改编制");
            }
            return rows;
        }
        public static List<BaseCommanderAllocation> CurrentFormation(BaseState s,BaseOutsideParty party)
        {
            if(party.CommanderTroops==null)return null;
            var soldiers = s.Workers.Where(worker => worker.Away && worker.PartyId == party.Id)
                .SelectMany(worker => worker.SoldierNumbers.Select(number => (number, worker.Kind))).ToArray();
            var kinds = soldiers.ToDictionary(soldier => soldier.number, soldier => soldier.Kind);
            var assigned = new HashSet<int>();
            var rows = party.CommanderTroops.Select(group => group.Copy()).ToList();
            foreach (var row in rows)
            {
                var previous = row.SoldierNumbers;
                row.Actual = new BaseCommanderTroops();
                row.SoldierNumbers = new List<int>();
                if (previous == null) continue;
                foreach (int number in previous)
                    if (kinds.TryGetValue(number, out var kind) && assigned.Add(number)) Add(row, number, kind);
            }
            foreach (var (number, kind) in soldiers)
            {
                if (assigned.Contains(number)) continue;
                var team = rows.FirstOrDefault(row => row.Actual.Count(kind) < row.Requested.Count(kind)) ?? rows[0];
                Add(team, number, kind);
            }
            return rows;

            static void Add(BaseCommanderAllocation team, int number, BaseTroopKind kind)
            {
                team.SoldierNumbers.Add(number);
                team.Actual.Set(kind, team.Actual.Count(kind) + 1);
            }
        }
        private static void SetFormation(BaseState s,BaseMusterPlan plan)
        {
            var requested = plan.ManualTeams ?? plan.Leaders.Select(id=>new BaseCommanderAllocation
                {LeaderId=id,Requested=s.Leaders.Single(l=>l.Id==id).DefaultTroops.Copy()}).ToList();
            int Taken(BaseTroopKind kind) => plan.Takes.Where(t => s.Workers[t.BucketIndex].Kind == kind).Sum(t => t.Count);
            if (plan.ManualTeams != null && (!requested.Select(team => team.LeaderId).SequenceEqual(plan.Leaders) ||
                requested.Any(team => !CompositionValid(team.Requested)) ||
                Enum.GetValues(typeof(BaseTroopKind)).Cast<BaseTroopKind>().Any(kind => requested.Sum(team => team.Requested.Count(kind)) != Taken(kind))))
                throw new ArgumentException("出征分队与所选士兵不一致");
            plan.CommanderTroops=Distribute(requested,Taken);
            plan.MissingMelee=plan.CommanderTroops.Sum(r=>r.Requested.Melee-r.Actual.Melee);
            plan.MissingRanged=plan.CommanderTroops.Sum(r=>r.Requested.Ranged-r.Actual.Ranged);
            plan.MissingCavalry=plan.CommanderTroops.Sum(r=>r.Requested.Cavalry-r.Actual.Cavalry);
        }
        private static BaseProductionForecast ForecastPlan(BaseState s,BaseMusterPlan plan)
        {
            var trial=s.Copy();Apply(trial,plan);return ForecastProduction(trial);
        }
        private static void FinishFormation(BaseState s,BaseMusterPlan plan)
        {
            if(plan.PriorityMode=="")return;
            plan.Takes=plan.Takes.GroupBy(t=>t.BucketIndex).Select(g=>new BaseMusterTake {BucketIndex=g.Key,Count=g.Sum(t=>t.Count)}).Where(t=>t.Count>0).ToList();
            SetFormation(s,plan);
            plan.Production=ForecastPlan(s,plan);
        }
        private static void PrepareProduction(BaseState s,BaseMusterPlan plan)
        {
            if(plan.PriorityMode=="production"&&ForecastPlan(s,plan).Problem.Contains("原料"))plan.ConserveMaterials=true;
        }
        private static void SelectOrderedWorkers(BaseState s,BaseMusterPlan plan,BaseArmyPreset selection)
        {
            foreach(var id in plan.Leaders)
            {
                var desired=s.Leaders.Single(l=>l.Id==id).DefaultTroops;
                var request=selection.Copy();
                foreach(BaseTroopKind k in Enum.GetValues(typeof(BaseTroopKind)))
                {
                    int selected=plan.Takes.Where(t=>s.Workers[t.BucketIndex].Kind==k).Sum(t=>t.Count);
                    request.SetCount(k,Math.Max(0,Math.Min(desired.Count(k),selection.Count(k)-selected)));
                }
                SelectWorkers(s,plan,request,Apply,true);
            }
        }
        public static BaseProductionForecast ForecastProduction(BaseState projected)
        {
            var s=projected.Copy();var forecast=new BaseProductionForecast();
            BaseProductionForecast Fail(string message,int deficit){forecast.Problem=message;forecast.FailedDay=s.Day;forecast.Deficit=deficit;return forecast;}
            for(int day=0;day<3;day++)
            {
                if(s.Day>=1000000)return Fail("已到日期上限，无法完成三天经营预测",1);
                BaseDevelopment.Allocate(s);
                foreach(var f in s.Muster.Facilities.OrderByDescending(f=>f.Priority))
                {
                    int missing=Math.Max(1,f.MinimumWorkers)-BaseEconomy.EligibleWorkers(s,f.FacilityId);
                    if(missing>0)return Fail("第"+s.Day+"天"+BaseEconomy.FacilityName(f.FacilityId)+"缺有效工人 "+missing+"人（最低开工 "+Math.Max(1,f.MinimumWorkers)+"人）",100000+missing);
                    if(f.DeveloperProductionPaused)return Fail(BaseEconomy.FacilityName(f.FacilityId)+"已停用产出，不能保障正常经营",100000);
                }
                if(s.TodayFoodMissing>0)return Fail("第"+s.Day+"天已缺口粮 "+s.TodayFoodMissing, s.TodayFoodMissing);
                var p=BaseEconomy.Preview(s);
                int materialMissing=p.WorkCapacity*BaseConstruction.MaterialCost(s,s.WorkshopProduct)-s.Inventory.Materials;
                if(materialMissing>0)return Fail("第"+s.Day+"天工坊原料缺 "+materialMissing,materialMissing);
                var row=new BaseProductionDay {Day=s.Day,FoodProduced=p.FoodProduced,MaterialsSpent=p.MaterialsSpent,FarmWorkers=BaseEconomy.EligibleWorkers(s,"farm"),WorkshopWorkers=BaseEconomy.EligibleWorkers(s,"workshop")};
                BaseEconomy.CompleteProductionDay(s,false);
                row.FoodAfter=s.Inventory.Food;row.MaterialsAfter=s.Inventory.Materials;forecast.Days.Add(row);
                if(s.TodayFoodMissing>0)return Fail("第"+s.Day+"天口粮缺 "+s.TodayFoodMissing,s.TodayFoodMissing);
            }
            forecast.Allowed=true;return forecast;
        }
        public static string ModeName(string mode)=>mode=="production" ? "生产优先" : "作战优先";
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using BaseConquest.Battle;
namespace BaseConquest.Base
{
    public sealed class BasePersonnelUnit
    {
        public string Key,Name,Facility,State,Effects,LeaderId;
        public int BucketIndex=-1,Ordinal,SoldierNumber;
        public bool Remote,Relocating,FoughtToday,Wounded,CanBattle;
        public string Post;
        public bool IsLeader,Recruited,Effective,Resting,Away,Locked;
        public SoldierKind Kind;
        public double Health,MaxHealth,FarmEfficiency,WorkshopEfficiency;
        public SoldierStats Basic,Current;
    }
    // Sorting never changes a soldier's identity or the order used for number entry.
    public sealed class SoldierRosterFilter
    {
        public SoldierKind? Kind { get; private set; }
        public int HealthOrder { get; private set; }
        public int StatusOrder { get; private set; }
        public void Activate(int control)
        {
            switch (control)
            {
                case 0: Kind = null; break;
                case 1:
                    var kinds = BattleSimulation.TroopKinds;
                    int next = Kind.HasValue ? Array.IndexOf(kinds, Kind.Value) + 1 : 0;
                    Kind = kinds[next % kinds.Length];
                    break;
                case 2: HealthOrder = (HealthOrder + 1) % 3; StatusOrder = 0; break;
                case 3: StatusOrder = (StatusOrder + 1) % 3; HealthOrder = 0; break;
            }
        }
        public string Label(int control) => control switch
        {
            0 => "全部",
            1 => Kind.HasValue ? BattleSimulation.KindName(Kind.Value) : "兵种",
            2 => HealthOrder == 0 ? "生命" : HealthOrder == 1 ? "生命 ↑" : "生命 ↓",
            _ => StatusOrder == 0 ? "状态" : StatusOrder == 1 ? "不可工作" : "可战斗"
        };
        public bool Active(int control) => control switch
        {
            0 => !Kind.HasValue, 1 => Kind.HasValue, 2 => HealthOrder != 0, _ => StatusOrder != 0
        };
        public IEnumerable<BasePersonnelUnit> Apply(IEnumerable<BasePersonnelUnit> units, Func<BasePersonnelUnit,bool> canBattle = null)
        {
            var matching = units.Where(u => !Kind.HasValue || u.Kind == Kind.Value);
            return matching.OrderBy(u => HealthOrder == 0 || u.MaxHealth <= 0 ? 0 :
                    (HealthOrder == 1 ? 1 : -1) * u.Health / u.MaxHealth)
                .ThenBy(u => StatusOrder == 1 ? (u.Effective && !u.Relocating && !u.Away ? 1 : 0) :
                    StatusOrder == 2 && !(canBattle?.Invoke(u) ?? u.CanBattle) ? 1 : 0)
                .ThenBy(u => u.SoldierNumber);
        }
    }
    public static class BasePersonnel
    {
        public static string CompactPost(BasePersonnelUnit u)
        {
            if (u.Relocating || u.Remote || u.Away) return u.State;
            string duty = u.Wounded || u.Health <= 0 ? "疗养" : u.FoughtToday ? "已作战" : "可生产";
            return $"{u.Facility} · {duty}";
        }
        public static string LeaderName(BaseLeader l)=>UnitDisplayNames.Mark(l.Name,l.Recruited);
        public static string State(BaseState s,BaseLeader l)=>!BaseAdventure.Alive(l)||l.Health<=0 ? l.Fate=="dead" ? "已阵亡" : l.Fate=="missing" ? "失联／未救出" : "无法行动" : l.Relocating ? "调动中 · "+BaseSettlements.Name(!l.VillageResident) : !BaseAdventure.Contains(s,l.Id) && l.VillageResident != BaseSettlements.AtVillage(s) ? "异地 · "+BaseSettlements.Name(l.VillageResident) : l.WaitingAtRecruitment?"暂留原地":BaseAdventure.PauseFor(s,l.Id)!=null?"暂离 · 保留队伍名额":l.Away ? "外出" : BaseAdventure.Contains(s,l.Id) ? "冒险小队 · 随行" : l.FacilityId!="" ? BaseEconomy.FacilityName(l.FacilityId)+"管理员" : BaseTraining.Busy(l.Training) ? "训练中" : "闲置";
        private static void Effects(BaseState s,BasePersonnelUnit unit,string party)
        {
            var q=s.Supply.Quotas.FirstOrDefault(q=>q.PartyId==party&&!string.IsNullOrEmpty(party));
            double factor=q?.AttackFactor??BaseEconomy.AttackFactor(s),weapon=unit.IsLeader || q==null ? 0 : q.ByTeam ? q.SwordBonus : q.WeaponBonus(0,unit.Kind),armor=unit.IsLeader ? 0 : q?.ArmorBonus??0;
            unit.Current=unit.Basic.Clone();unit.Current.Damage=(float)((unit.Basic.Damage+weapon)*factor);unit.Current.Defense+=(float)armor;
            unit.Effects=ProvisionEffects.Summary(unit.Kind,factor,weapon,armor,q!=null)+(unit.IsLeader?"":"／今日生产效率 "+BaseEconomy.ProductionFactor(s).ToString("P0")+"／过夜恢复最大生命的"+(BaseDevelopment.RecoveryRatio(s)*100).ToString("0")+"%（满血停止）");
            if(unit.IsLeader&&unit.Health>0)unit.Effects+="／每日自然恢复最大生命的"+(BaseDevelopment.RecoveryRatio(s)*100).ToString("0")+"%（满血停止）";
        }
        public static IEnumerable<BasePersonnelUnit> Units(BaseState s) => Leaders(s).Concat(Soldiers(s));
        public static IEnumerable<BasePersonnelUnit> Leaders(BaseState s)
        {
            foreach(var l in s.Leaders)
            {
                var stats=BaseCommanderIdentity.Stats(l,new ArmyDefinition().Hero);
                var u=new BasePersonnelUnit {Key="leader:"+l.Id,LeaderId=l.Id,Name=LeaderName(l),IsLeader=true,Recruited=l.Recruited,Kind=SoldierKind.Hero,Health=l.Health,MaxHealth=l.MaxHealth,Basic=stats,Facility=l.Away ? "外出队伍" : BaseAdventure.Contains(s,l.Id) ? "冒险小队" : BaseEconomy.FacilityName(l.FacilityId),State=State(s,l),Effective=BaseDevelopment.Effective(s,l),Resting=false,Away=l.Away,Locked=l.PostLocked,FarmEfficiency=BaseDevelopment.Bonus(l,"farm"),WorkshopEfficiency=BaseDevelopment.Bonus(l,"workshop")};
                Effects(s,u,l.PartyId);yield return u;
            }
        }
        public static IEnumerable<BasePersonnelUnit> Soldiers(BaseState s, SoldierKind? kind = null)
            => SoldierRows(s, kind).OrderBy(unit => unit.SoldierNumber);
        private static IEnumerable<BasePersonnelUnit> SoldierRows(BaseState s, SoldierKind? kind)
        {
            s.EnsureSoldierNumbers();
            for(int i=0;i<s.Workers.Count;i++)
            {
                var w=s.Workers[i];
                if(kind.HasValue && BaseCampaign.ToBattle(w.Kind)!=kind.Value)continue;
                var stats=w.HasCombatProfile ? w.CombatStats.Clone() : new ArmyDefinition().Stats(BaseCampaign.ToBattle(w.Kind)).Clone();stats.Health=(float)w.MaxHealth;
                for(int n=0;n<w.Count;n++)
                {
                    int number=w.SoldierNumbers[n];
                    var u=new BasePersonnelUnit {Post=w.FacilityId, CanBattle=BaseSettlements.Local(s,w)&&BaseDevelopment.CanFight(w), FoughtToday=w.LastBattleDay==s.Day, Wounded=BaseDevelopment.HeavilyWounded(w),Key="soldier:"+number,SoldierNumber=number,BucketIndex=i,Ordinal=n,Name=UnitDisplayNames.Mark(BaseEconomy.TroopName(w.Kind)+" #"+number,w.Recruited),Kind=BaseCampaign.ToBattle(w.Kind),Recruited=w.Recruited,Health=w.Health,MaxHealth=w.MaxHealth,Basic=stats.Clone(),Facility=w.Away ? "外出队伍" : BaseEconomy.FacilityName(w.FacilityId),Remote=!BaseSettlements.Local(s,w),Relocating=w.Relocating,State=w.Relocating ? "调动中 · "+BaseSettlements.Name(!w.VillageResident) : !BaseSettlements.Local(s,w) ? "异地 · "+BaseSettlements.Name(w.VillageResident) : w.Away ? "外出" : w.Health<=0 ? "无法行动" : BaseDevelopment.HeavilyWounded(w) ? "疗养" : w.LastBattleDay==s.Day ? "今日已作战" : "今日可生产",Effective=BaseDevelopment.Effective(s,w),Resting=w.Resting,Away=w.Away,Locked=w.PostLocked,FarmEfficiency=w.FarmEfficiency,WorkshopEfficiency=w.WorkshopEfficiency};
                    Effects(s,u,w.PartyId);yield return u;
                }
            }
        }
        public static BasePoint BarracksDoor(BaseState s)
        {
            var door=BaseSpatial.WorkPost(s,"barracks");var occupied=s.Leaders.Where(l=>l.HasMapPosition&&!l.Away&&l.Health>0).Select(l=>new BasePoint(l.MapX,l.MapY)).Concat(new[]{new BasePoint(s.PlayerX,s.PlayerY)}).ToArray();
            var candidates=Enumerable.Range(0,12).SelectMany(row=>Enumerable.Range(0,19).Select(col=>new BasePoint(door.X+(col==0 ? 0 : (col%2==0 ? 1 : -1)*((col+1)/2)*.65),door.Y-row*.65)));
            return candidates.First(p=>BaseSpatial.Walkable(s,p.X,p.Y,"base")&&occupied.All(o=>o.Distance(p)>.55));
        }
    }
}

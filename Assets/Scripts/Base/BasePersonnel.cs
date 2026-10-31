using System;
using System.Collections.Generic;
using System.Linq;
using BaseConquest.Battle;
namespace BaseConquest.Base
{
    public sealed class BasePersonnelUnit
    {
        public string Key,Name,Facility,State,Effects,LeaderId;
        public int BucketIndex=-1,Ordinal;
        public bool IsLeader,Recruited,Effective,Resting,Away,Locked;
        public SoldierKind Kind;
        public double Health,MaxHealth,FarmEfficiency,WorkshopEfficiency;
        public SoldierStats Basic,Current;
    }
    public static class BasePersonnel
    {
        public static string LeaderName(BaseLeader l)=>UnitDisplayNames.Mark(l.Name,l.Recruited);
        public static string State(BaseState s,BaseLeader l)=>!BaseAdventure.Alive(l)||l.Health<=0 ? l.Fate=="dead" ? "已阵亡" : l.Fate=="missing" ? "失联／未救出" : "无法行动" : BaseInn.Guest(s,l.Id)!=null ? (BaseInn.Guest(s,l.Id).Free?"故乡扎营 · 留队":"旅店寄存")+(BaseInn.Guest(s,l.Id).Debt>0?" · 欠费"+BaseInn.Guest(s,l.Id).Debt+"金币":"") : BaseAdventure.PauseFor(s,l.Id)!=null?"暂离 · 保留队伍名额":l.Away ? "外出" : BaseAdventure.Contains(s,l.Id) ? "冒险小队 · 随行" : BaseDevelopment.Effective(s,l) ? l.FacilityId=="" ? "闲置 · 今日可生产" : BaseEconomy.FacilityName(l.FacilityId)+"管理员" : l.FacilityId!=""?BaseEconomy.FacilityName(l.FacilityId)+"管理员 · 今日不生产，次日生效":"今日不生产";
        private static void Effects(BaseState s,BasePersonnelUnit unit,string party)
        {
            var q=s.Supply.Quotas.FirstOrDefault(q=>q.PartyId==party&&!string.IsNullOrEmpty(party));
            double factor=q?.AttackFactor??BaseEconomy.AttackFactor(s),weapon=unit.IsLeader ? 0 : unit.Kind==SoldierKind.Ranged ? q?.BowBonus??0 : q?.SwordBonus??0,armor=unit.IsLeader ? 0 : q?.ArmorBonus??0;
            unit.Current=unit.Basic.Clone();unit.Current.Damage=(float)((unit.Basic.Damage+weapon)*factor);unit.Current.Defense+=(float)armor;
            unit.Effects=ProvisionEffects.Summary(unit.Kind,factor,weapon,armor,q!=null)+"／今日生产效率×"+BaseEconomy.ProductionFactor(s).ToString("0.##")+(unit.Resting ? "／休养：过夜恢复"+s.Development.RecoveryPerDay.ToString("0.#")+"生命" : "");
            if(unit.IsLeader&&unit.Health>0)unit.Effects+="／每日自然恢复"+s.Development.RecoveryPerDay.ToString("0.#")+"生命（满血停止）";
        }
        public static IEnumerable<BasePersonnelUnit> Units(BaseState s)
        {
            foreach(var l in s.Leaders)
            {
                var stats=BaseCommanderIdentity.Stats(l,new ArmyDefinition().Hero);
                var u=new BasePersonnelUnit {Key="leader:"+l.Id,LeaderId=l.Id,Name=LeaderName(l),IsLeader=true,Recruited=l.Recruited,Kind=SoldierKind.Hero,Health=l.Health,MaxHealth=l.MaxHealth,Basic=stats,Facility=l.Away ? "外出队伍" : BaseAdventure.Contains(s,l.Id) ? "冒险小队" : BaseEconomy.FacilityName(l.FacilityId),State=State(s,l),Effective=BaseDevelopment.Effective(s,l),Resting=false,Away=l.Away,Locked=l.PostLocked,FarmEfficiency=BaseDevelopment.Bonus(l,"farm"),WorkshopEfficiency=BaseDevelopment.Bonus(l,"workshop")};
                Effects(s,u,l.PartyId);yield return u;
            }
            int number=0;
            for(int i=0;i<s.Workers.Count;i++)
            {
                var w=s.Workers[i];var stats=w.HasCombatProfile ? w.CombatStats.Clone() : new ArmyDefinition().Stats(BaseCampaign.ToBattle(w.Kind)).Clone();stats.Health=(float)w.MaxHealth;
                for(int n=0;n<w.Count;n++)
                {
                    var u=new BasePersonnelUnit {Key="soldier:"+i+":"+n,BucketIndex=i,Ordinal=n,Name=UnitDisplayNames.Mark(BaseEconomy.TroopName(w.Kind)+" #"+(++number),w.Recruited),Kind=BaseCampaign.ToBattle(w.Kind),Recruited=w.Recruited,Health=w.Health,MaxHealth=w.MaxHealth,Basic=stats.Clone(),Facility=w.Away ? "外出队伍" : BaseEconomy.FacilityName(w.FacilityId),State=w.Away ? "外出" : w.Health<=0 ? "无法行动" : w.Resting ? "休养" : BaseDevelopment.Effective(s,w) ? "今日可生产" : "今日不生产",Effective=BaseDevelopment.Effective(s,w),Resting=w.Resting,Away=w.Away,Locked=w.PostLocked,FarmEfficiency=w.FarmEfficiency,WorkshopEfficiency=w.WorkshopEfficiency};
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

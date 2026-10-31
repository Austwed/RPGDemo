using System;
using System.Linq;
using BaseConquest.Battle;

namespace BaseConquest.Base
{
    [Serializable] public sealed class BaseTrainingProgress
    {
        public int Level=1, Target, EndDay;
        public SoldierStats Origin;
        public BaseTrainingProgress Copy()=>new BaseTrainingProgress {Level=Level,Target=Target,EndDay=EndDay,Origin=Origin?.Clone()};
    }
    public static class BaseTraining
    {
        public static bool Busy(BaseTrainingProgress p)=>p?.Target>0;
        public static string Key(BaseTrainingProgress p)=>p==null?"1:0:0":p.Level+":"+p.Target+":"+p.EndDay;
        public static string Book(SoldierKind kind,int target)=>"training_"+(kind==SoldierKind.Ranged?"bow":kind==SoldierKind.Cavalry?"spear":"sword")+(target%3==0?"_advanced":"_basic");
        public static int Days(bool leader,int target)=>(leader?2:1)+(target-2)/3;
        public static int Books(bool leader,int target)=>(leader?2:1)*Math.Max(1,(target+1)/2);
        public static int Days(BaseLeader leader,int target)=>leader?.Rpg?.Version==1?leader.Rpg.Rules.TrainingCost(true,leader.Rpg.Profession,target):Days(true,target);
        public static int Books(BaseLeader leader,int target)=>leader?.Rpg?.Version==1?leader.Rpg.Rules.TrainingCost(false,leader.Rpg.Profession,target):Books(true,target);
        public static BaseTrainingProgress FromProfile(SoldierStats stats)
        {
            var progress=new BaseTrainingProgress {Level=stats.Level};
            if(stats.Level>1){var origin=stats.Clone();float factor=GrowthFactor(stats.Level);origin.Level=1;origin.Health=Math.Max(1,origin.Health/factor);origin.Damage/=factor;origin.Defense/=factor;progress.Origin=origin;}
            return progress;
        }
        public static void Normalize(BaseState s)
        {
            foreach(var l in s.Leaders){l.Training??=new BaseTrainingProgress();l.Wine??=new BaseWineStatus();}
            foreach(var kind in new[]{SoldierKind.Melee,SoldierKind.Ranged,SoldierKind.Cavalry})foreach(int target in new[]{2,3})
            {string id=Book(kind,target);if(!s.Backpack.Catalog.Any(d=>d.Id==id))s.Backpack.Catalog.Add(new BaseBagDefinition {Id=id,Name=(target==3?"高阶":"基础")+BasePersonalEquipment.Profession(kind==SoldierKind.Ranged?"bow":kind==SoldierKind.Cavalry?"spear":"sword")+"兵书",Category="material",Description="角色等级训练使用；3／6／9级突破用高阶兵书，开始扣除，取消不退。",MaxStack=99});}
            BaseWine.Normalize(s);
            BaseProgressSupplies.Normalize(s);
            BaseItemRecovery.Normalize(s);
        }
        private static void ValidateProgress(BaseTrainingProgress p,int day,bool away)
        {
            if(p==null||p.Level<1||p.Level>9||p.Level>1&&p.Origin==null||p.Target!=0&&(p.Target!=p.Level+1||p.Target>9||p.EndDay<=day||p.EndDay>1000000||away||p.Origin==null)||p.Target==0&&p.EndDay!=0||p.Origin!=null&&!BaseAftermath.ValidStats(p.Origin))throw new ArgumentException("等级／训练进度非法");
        }
        public static void Validate(BaseState s)
        {
            foreach(var l in s.Leaders)ValidateProgress(l.Training,s.Day,l.Away);
            BaseWine.Validate(s);
            BaseProgressSupplies.Validate(s);
            BaseItemRecovery.Validate(s);
        }
        public static float GrowthFactor(int level)
        {
            if(level<1||level>9)throw new ArgumentOutOfRangeException(nameof(level));
            return new[]{1f,1.18f,1.5f,1.85f,2.25f,3f,3.7f,4.5f,6f}[level-1];
        }
        public static SoldierStats Growth(SoldierStats origin,int level)
        {
            var profile=origin.Clone();float factor=GrowthFactor(level);
            profile.Level=level;profile.Health=origin.Health*factor;profile.Damage=origin.Damage*factor;profile.Defense=origin.Defense*factor;return profile;
        }
        private static bool AtBarracks(BaseState s)=>BasePartyInventory.Peaceful(s)&&!s.Campaign.HasActiveBattle&&!BaseBattleLoot.Pending(s)&&BaseInteraction.At(s,"base","barracks");
        public static string LeaderProblem(BaseState s,BaseLeader l)
        {
            if(!AtBarracks(s))return "请在非战斗时接近基地兵营";
            if(l==null||l.Away||!BaseAdventure.Alive(l)||l.Health<=0||Busy(l.Training))return "人物不可训练或正在训练";
            if(l.Training.Level>=9)return "已达9级";
            if(!BaseAftermath.ValidStats(l.Rpg?.Version==1?l.Rpg.Rules.Bare(l.Rpg,l.Training.Level+1):Growth(l.Training.Level==1?l.PersonalEquipment.BaseStats:l.Training.Origin,l.Training.Level+1)))return "成长属性超过当前支持上限";
            return "";
        }
        public static bool StartLeader(BaseState s,string id,int revision)
        {
            var l=s.Leaders.SingleOrDefault(x=>x.Id==id);if(revision!=s.Backpack.Revision||LeaderProblem(s,l)!=""||s.Backpack.Revision>=int.MaxValue-1)return false;
            int target=l.Training.Level+1;string book=Book(BasePersonalEquipment.Stats(l).ProfessionKind,target);
            if(s.Day+Days(l,target)>1000000||!BasePartyInventory.ConsumeMaterial(s,book,Books(l,target)))return false;
            if(l.Training.Level==1)l.Training.Origin=l.PersonalEquipment.BaseStats.Clone();l.Training.Target=target;l.Training.EndDay=s.Day+Days(l,target);l.FacilityId="";l.UnavailableDay=s.Day;BaseDevelopment.Allocate(s);return true;
        }
        public static bool CancelLeader(BaseState s,string id)
        {var l=s.Leaders.SingleOrDefault(x=>x.Id==id);if(!AtBarracks(s)||l==null||!Busy(l.Training))return false;l.Training.Target=l.Training.EndDay=0;l.UnavailableDay=s.Day;BaseDevelopment.Allocate(s);return true;}
        public static void AdvanceDay(BaseState s)
        {
            foreach(var l in s.Leaders.Where(l=>Busy(l.Training)&&l.Training.EndDay<=s.Day))
            {
                var p=l.Training;l.PersonalEquipment.BaseStats=l.Rpg?.Version==1?l.Rpg.Rules.Bare(l.Rpg,p.Target):Growth(p.Origin,p.Target);l.CombatStats=l.PersonalEquipment.BaseStats.Clone();l.HasCombatProfile=true;p.Level=p.Target;p.Target=p.EndDay=0;
                l.MaxHealth=BasePersonalEquipment.Stats(l).Health;l.Health=Math.Min(l.Health,l.MaxHealth);
            }
            BaseWine.AdvanceDay(s);
        }
    }
}

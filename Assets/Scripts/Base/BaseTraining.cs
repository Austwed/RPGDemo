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
        public static string Book(SoldierKind kind,int target)=>"training_"+(kind==SoldierKind.Mage?"mage":BaseRpg.Weapon(kind==SoldierKind.Hero?0:BaseRpg.Profession(kind)))+(target%3==0?"_advanced":"_basic");
        internal static SoldierKind[] SoldierKinds(BaseState s) => Enumerable.Range(0,s.Demo?.Rpg?.ProfessionCount??3).Select(BaseRpg.Kind).ToArray();
        public static int Days(bool leader,int target)=>(leader?2:1)+(target-2)/3;
        public static int Books(bool leader,int target)=>(leader?2:1)*Math.Max(1,(target+1)/2);
        public static int Books(BaseLeader leader,int target)=>leader?.Rpg?.Version==1?leader.Rpg.Rules.TrainingCost(false,leader.Rpg.Profession,target):Books(true,target);
        public static BaseTrainingProgress FromProfile(SoldierStats stats)
        {
            var progress=new BaseTrainingProgress {Level=stats.Level};
            if(stats.Level>1){var origin=stats.Clone();float factor=GrowthFactor(stats.Level);origin.Level=1;origin.Health=Math.Max(1,origin.Health/factor);origin.Damage/=factor;origin.Defense/=factor;progress.Origin=origin;}
            return progress;
        }
        public static void Normalize(BaseState s)
        {
            foreach (var leader in s.Leaders)
            {
                leader.Training ??= new BaseTrainingProgress();
                leader.Wine ??= new BaseWineStatus();
                var progress = leader.Training;
                if (progress.Target > 0 && progress.Target == progress.Level + 1 && progress.Target <= 9 &&
                    progress.Level >= 1 && progress.EndDay is > 0 and <= 1000000 && !leader.Away && BaseAftermath.ValidStats(progress.Origin))
                    Upgrade(leader, progress.Target);
            }
            foreach(var kind in SoldierKinds(s))foreach(int target in new[]{2,3})
            {
                string id = Book(kind, target);
                var book = s.Backpack.Catalog.FirstOrDefault(d => d.Id == id);
                if (book == null)
                {
                    book = new BaseBagDefinition {Id=id,Name=(target==3?"高阶":"基础")+BasePersonalEquipment.Profession(BaseRpg.Weapon(BaseRpg.Profession(kind)))+"兵书",Category="material",MaxStack=99};
                    s.Backpack.Catalog.Add(book);
                }
                book.Description = "在营帐使用兵书立即升级。3／6／9级使用高阶兵书。";
            }
            BaseWine.Normalize(s);
            BaseProgressSupplies.Normalize(s);
            BaseItemRecovery.Normalize(s);
        }
        private static void ValidateProgress(BaseTrainingProgress p)
        {
            if(p==null||p.Level<1||p.Level>9||p.Level>1&&p.Origin==null||p.Target!=0||p.EndDay!=0||p.Origin!=null&&!BaseAftermath.ValidStats(p.Origin))throw new ArgumentException("等级／训练进度非法");
        }
        public static void Validate(BaseState s)
        {
            foreach(var l in s.Leaders)ValidateProgress(l.Training);
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
        private static bool AtTrainingSite(BaseState s)=>BasePartyInventory.Peaceful(s)&&!s.Campaign.HasActiveBattle&&!BaseBattleLoot.Pending(s)&&BaseInteraction.At(s,"base",BaseDemo.Active(s)?"home":"barracks");
        public static string LeaderProblem(BaseState s,BaseLeader l)
        {
            if(!AtTrainingSite(s))return BaseDemo.Active(s)?"请在非战斗时接近营帐":"请在非战斗时接近基地兵营";
            if(l==null||l.Away||!BaseAdventure.Alive(l)||l.Health<=0)return "人物当前不可升级";
            if(l.Training.Level>=9)return "已达9级";
            if(!BaseAftermath.ValidStats(l.Rpg?.Version==1?l.Rpg.Rules.Bare(l.Rpg,l.Training.Level+1):Growth(l.Training.Level==1?l.PersonalEquipment.BaseStats:l.Training.Origin,l.Training.Level+1)))return "成长属性超过当前支持上限";
            return "";
        }
        public static bool UpgradeLeader(BaseState s,string id,int revision)
        {
            var l=s.Leaders.SingleOrDefault(x=>x.Id==id);if(revision!=s.Backpack.Revision||LeaderProblem(s,l)!=""||s.Backpack.Revision>=int.MaxValue-1)return false;
            int target=l.Training.Level+1;string book=Book(BasePersonalEquipment.Stats(l).ProfessionKind,target);
            if(!BasePartyInventory.ConsumeMaterial(s,book,Books(l,target)))return false;
            if(l.Training.Level==1)l.Training.Origin=l.PersonalEquipment.BaseStats.Clone();
            Upgrade(l, target);
            return true;
        }
        private static void Upgrade(BaseLeader leader, int target)
        {
            var progress = leader.Training;
            leader.PersonalEquipment.BaseStats = leader.Rpg?.Version == 1 ? leader.Rpg.Rules.Bare(leader.Rpg, target) : Growth(progress.Origin, target);
            leader.CombatStats = leader.PersonalEquipment.BaseStats.Clone();
            leader.HasCombatProfile = true;
            progress.Level = target;
            progress.Target = progress.EndDay = 0;
            leader.MaxHealth = BasePersonalEquipment.Stats(leader).Health;
            leader.Health = Math.Min(leader.Health, leader.MaxHealth);
        }
    }
}

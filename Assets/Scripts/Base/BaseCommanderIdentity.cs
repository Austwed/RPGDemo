using BaseConquest.Battle;
namespace BaseConquest.Base
{
    // Stable identities survive formation slot changes and recruitment.
    public static class BaseCommanderIdentity
    {
        public static string Appearance(BaseLeader leader)
        {
            if(leader==null)return "";
            if(!string.IsNullOrEmpty(leader.AppearanceId))return leader.AppearanceId;
            if(leader.Id=="partner_farm")return "ahe";
            if(leader.Id=="partner_craft")return "azhu";
            if(leader.Id=="partner_test"||leader.Recruited&&UnitDisplayNames.RecruitedName(leader.Name)=="陈安")return "chenan";
            return "";
        }
        public static SoldierStats Stats(BaseLeader leader,SoldierStats fallback)
        {
            if(leader.Rpg?.Version==1)return BaseRpg.Stats(leader);
            if(leader.PersonalEquipment?.Version==1)return BasePersonalEquipment.Stats(leader);
            var stats=(leader.HasCombatProfile ? leader.CombatStats : fallback).Clone();
            if(!leader.HasCombatProfile)
            {
                if(Appearance(leader)=="ahe")stats.Range=new ArmyDefinition().Ranged.Range;
                if(Appearance(leader)=="azhu")stats.MoveDistance=new ArmyDefinition().Cavalry.MoveDistance;
            }
            stats.Level=leader.Training?.Level??stats.Level;stats.Health=(float)leader.MaxHealth;return stats;
        }
        public static string RoleName(BaseLeader leader)=>BasePersonalEquipment.ClassName(leader)+"队长";
    }
}

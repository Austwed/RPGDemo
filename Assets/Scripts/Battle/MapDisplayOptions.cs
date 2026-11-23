using UnityEngine;
namespace BaseConquest.Battle
{
    public static class MapDisplayOptions
    {
        private const string GridKey="BaseConquest.ShowMapGridOutlines";
        private const string BattleAssistKey="BaseConquest.ShowBattleAssist";
        private const string BattleGridKey="BaseConquest.ShowBattleGridOutlines";
        private const string BattleRangesKey="BaseConquest.ShowBattleRanges";
        private const string AttackRangeKey="BaseConquest.ShowAttackRange";
        private const string BattleLinesKey="BaseConquest.ShowBattleLines";
        public static bool BattleAssist
        {
            get
            {
                if (!PlayerPrefs.HasKey(BattleAssistKey))
                    BattleAssist = PlayerPrefs.GetInt(BattleGridKey, 1) != 0 &&
                        PlayerPrefs.GetInt(BattleRangesKey, 1) != 0 && PlayerPrefs.GetInt(AttackRangeKey, 1) != 0 &&
                        PlayerPrefs.GetInt(BattleLinesKey, 1) != 0;
                return PlayerPrefs.GetInt(BattleAssistKey, 1) != 0;
            }
            set {PlayerPrefs.SetInt(BattleAssistKey,value ? 1 : 0);PlayerPrefs.Save();}
        }
        public static bool ShowGridOutlines
        {
            get=>PlayerPrefs.GetInt(GridKey,0)!=0;
            set {PlayerPrefs.SetInt(GridKey,value ? 1 : 0);PlayerPrefs.Save();}
        }
    }
}

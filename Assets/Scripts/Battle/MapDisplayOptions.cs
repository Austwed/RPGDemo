using UnityEngine;
namespace BaseConquest.Battle
{
    public static class MapDisplayOptions
    {
        private const string GridKey="BaseConquest.ShowMapGridOutlines";
        private const string BattleGridKey="BaseConquest.ShowBattleGridOutlines";
        private const string BattleRangesKey="BaseConquest.ShowBattleRanges";
        private const string AttackRangeKey="BaseConquest.ShowAttackRange";
        private const string BattleLinesKey="BaseConquest.ShowBattleLines";
        public static bool BattleGridOutlines
        {
            get=>PlayerPrefs.GetInt(BattleGridKey,1)!=0;
            set {PlayerPrefs.SetInt(BattleGridKey,value ? 1 : 0);PlayerPrefs.Save();}
        }
        public static bool ShowGridOutlines
        {
            get=>PlayerPrefs.GetInt(GridKey,0)!=0;
            set {PlayerPrefs.SetInt(GridKey,value ? 1 : 0);PlayerPrefs.Save();}
        }
        public static bool BattleRanges
        {
            get=>PlayerPrefs.GetInt(BattleRangesKey,1)!=0;
            set {PlayerPrefs.SetInt(BattleRangesKey,value ? 1 : 0);PlayerPrefs.Save();}
        }
        public static bool BattleLines
        {
            get=>PlayerPrefs.GetInt(BattleLinesKey,1)!=0;
            set {PlayerPrefs.SetInt(BattleLinesKey,value ? 1 : 0);PlayerPrefs.Save();}
        }
        public static bool AttackRange
        {
            get=>PlayerPrefs.GetInt(AttackRangeKey,1)!=0;
            set {PlayerPrefs.SetInt(AttackRangeKey,value ? 1 : 0);PlayerPrefs.Save();}
        }
    }
}

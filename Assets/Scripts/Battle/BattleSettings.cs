using UnityEngine;

namespace BaseConquest.Battle
{
    [CreateAssetMenu(menuName = "SLG/格子战斗测试配置", fileName = "Stage1BattleSettings")]
    public sealed class BattleSettings : ScriptableObject
    {
        public BattleScenario Scenario = new BattleScenario();
        public Shader UnitShader;
        public Shader LineShader;
        [Range(0.1f, 1.5f)] public float RoundPresentationSeconds = 0.55f;
        private void OnValidate()
        {
            Scenario ??= new BattleScenario();
            Scenario.Board ??= BattleBoard.CreateDefault();
            Scenario.Board.EnsureValid();
            Scenario.A = (Scenario.A ?? new ArmyDefinition()).Clone();
            Scenario.B = (Scenario.B ?? new ArmyDefinition()).Clone();
            Scenario.MaxRounds = Mathf.Max(1, Scenario.MaxRounds);
            Scenario.StalemateRounds = Mathf.Max(1, Scenario.StalemateRounds);
        }
    }
}

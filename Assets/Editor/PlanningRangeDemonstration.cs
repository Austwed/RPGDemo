using System;
using System.Linq;
using BaseConquest.Base;
using BaseConquest.Battle;
using BaseConquest.RealtimePrototype;
using UnityEditor;
using UnityEngine;

namespace BaseConquest.Editor
{
    public sealed class PlanningRangeDemonstration : EditorWindow
    {
        private BaseState stage;
        private RealtimeCampaignBattle run;
        private bool selected;
        private string result;

        internal static void Open(BaseState state)
        {
            var window = GetWindow<PlanningRangeDemonstration>("调参备战");
            window.minSize = new Vector2(520, 220);
            window.stage = state;
            window.run = new RealtimeCampaignBattle(BaseStandaloneBattle.CreateTuningBattle(state));
            window.selected = false;
            window.result = null;
            window.Show();
        }

        internal static PrototypeUnit Caster(RealtimeCampaignBattle battle) =>
            battle.Model.Units.Single(u => battle.Battle.Soldier(u.Id - 1).Kind == SoldierKind.Mage);
        internal static PrototypeUnit Target(RealtimeCampaignBattle battle) => battle.Model.Units.Single(u => u.Side == PrototypeSide.Enemy);

        internal static RpgAttackPacket Fire(RealtimeCampaignBattle battle)
        {
            var caster = Caster(battle);
            var target = Target(battle);
            if (!target.Alive) throw new InvalidOperationException("目标已倒下，请重新载入调参备战档");
            if (!battle.Model.ClearAttack(caster.Position, target.BodyCenter)) throw new InvalidOperationException("调参备战射线被遮挡");
            bool inRange = RealtimeBattlePrototype.BodyGap(caster, target) <= battle.Model.ReachFor(caster, target);
            int start = battle.CaptureState().RpgAttackSerial;
            battle.Model.SetPaused(false);
            try
            {
                int limit = inRange ? 1000 : 100;
                for (int tick = 0; tick < limit && target.Alive; tick++)
                {
                    battle.Tick(.02);
                    if (tick % 5 != 0) continue;
                    var attack = battle.CaptureState().RpgAttacks.LastOrDefault(p => p.Id > start && p.SourceId == caster.Id && p.Settled && p.Hit);
                    if (attack != null) return attack;
                }
                if (!inRange && !battle.CaptureState().RpgAttacks.Any(p => p.Id > start && p.SourceId == caster.Id))
                    return null;
                throw new InvalidOperationException("法师已处于攻击范围内，但测试时段内未命中");
            }
            finally { battle.Model.SetPaused(true); }
        }

        private void OnGUI()
        {
            if (run == null) { EditorGUILayout.LabelField("请从导入窗口载入本次生成的调参备战档。"); return; }
            var caster = Caster(run);
            var target = Target(run);
            int distance = (int)Math.Round(RealtimeBattlePrototype.BodyGap(caster, target) * PrototypeArena.Subdivision);
            double reach = RealtimeCampaignBattle.RangeCells(run.Session, run.Battle.Soldier(caster.Id - 1));
            using (new EditorGUILayout.HorizontalScope())
            {
                for (int cell = 0; cell <= distance + 1; cell++)
                {
                    var color = GUI.backgroundColor;
                    if (selected && cell <= reach) GUI.backgroundColor = new Color(.75f, .86f, 1f);
                    if (cell == 0) { if (GUILayout.Button("法师", GUILayout.Height(40))) selected = true; }
                    else GUILayout.Box(cell == distance ? "目标" : cell.ToString(), GUILayout.Height(40), GUILayout.ExpandWidth(true));
                    GUI.backgroundColor = color;
                }
            }
            EditorGUILayout.LabelField("目标相距 " + distance + " 细格 · 生命 " + target.Health.ToString("0.##"));
            if (selected)
            {
                EditorGUILayout.LabelField("职业普攻距离 " + reach + " 细格");
                if (GUILayout.Button("普攻目标"))
                {
                    try
                    {
                        var shot = Fire(run);
                        result = shot == null ? "目标超出攻击距离" : "法术普攻命中 · 造成 " + shot.DamageApplied.ToString("0.##") + " 点伤害";
                    }
                    catch (Exception error) { result = error.Message; }
                }
                if (result != null) EditorGUILayout.LabelField(result);
            }
            EditorGUILayout.LabelField("粮田每人日产粮 " + stage.Rules.FarmPerWorker.ToString("0.##"));
            EditorGUILayout.LabelField("当前编制预计产粮 " + BaseEconomy.Preview(stage).FoodProduced + " 粮");
        }
    }
}

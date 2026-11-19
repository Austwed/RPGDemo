using System;
using System.Linq;
using System.Reflection;
using BaseConquest.Base;
using BaseConquest.Battle;
using UnityEditor;
using UnityEngine;

namespace BaseConquest.Editor
{
    public static class VerifyDemoAudio
    {
        private const string Root = "Audio/VillageOpening/";
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

        [MenuItem("SLG/其他工具/验证/场景配乐与动作音效")]
        public static void Run()
        {
            foreach (string id in new[] { "Exploration", "RuinsScore", "BattleScore", "Ambience", "Victory", "Defeat" })
            {
                var clip = Resources.Load<AudioClip>(Root + id);
                Need(clip != null && clip.channels == 2, id + " 缺少立体声素材");
                if (id.EndsWith("Score") || id == "Exploration")
                    Need(clip.loadType == AudioClipLoadType.Streaming && clip.length > 60, id + " 的长曲导入设置有误");
                if (id == "Victory" || id == "Defeat")
                    Need(clip.length > 1 && clip.length < 5 && clip.loadType != AudioClipLoadType.Streaming, id + " 的战果片段有误");
            }
            var cueType = typeof(VillageOpeningAudio).GetNestedType("Cue", BindingFlags.NonPublic);
            foreach (string id in Enum.GetNames(cueType))
            {
                var clips = Resources.LoadAll<AudioClip>(Root + id);
                Need(clips.Length > 0 && clips.All(c => c.channels == 1 && c.samples > 0), id + " 缺少动作音效");
            }

            var host = new GameObject("AudioVerification") { hideFlags = HideFlags.HideAndDontSave };
            try
            {
                var audio = host.AddComponent<VillageOpeningAudio>();
                var type = audio.GetType();
                if (host.GetComponent<AudioSource>() == null) type.GetMethod("Awake", Private).Invoke(audio, null);
                var loops = host.GetComponents<AudioSource>().Where(s => s.loop).ToArray();
                Need(loops.Length == 4 && loops.All(s => s.clip != null && !s.playOnAwake), "循环声源设置有误");
                var state = JsonUtility.FromJson<BaseState>(Resources.Load<TextAsset>("DemoStages/defense-male").text);
                var bind = type.GetMethod("BindBattle", Private);
                var release = type.GetMethod("ReleaseBattle", Private);
                foreach (var (id, place) in new[] { (BaseDemoIntro.BattleId, "Practice"), (BaseDemoRuins.EventId, "Dungeon"), (BaseDemoDefense.SecondEventId, "Battle") })
                {
                    var session = BaseStandaloneBattle.Create(state, BaseStandaloneBattlePreset.Mixed);
                    var run = new RealtimeCampaignBattle(session);
                    session.Opening.EventId = session.Opening.Id = id;
                    var attack = run.Model.AttackTriggered;
                    var damage = run.Model.DamageApplied;
                    var step = run.Model.CellEntered;
                    var prop = run.Tactics.ObjectHit;
                    for (int pass = 0; pass < 2; pass++)
                    {
                        bind.Invoke(audio, new object[] { run });
                        bind.Invoke(audio, new object[] { run });
                        Need(type.GetField("place", Private).GetValue(audio).ToString() == place, id + " 选错场景声音");
                        Need(run.Model.DamageApplied.GetInvocationList().Length == damage.GetInvocationList().Length + 1, id + " 受击重复订阅");
                        if (attack != null)
                            Need(run.Model.AttackTriggered.GetInvocationList().Skip(1).SequenceEqual(attack.GetInvocationList()), id + " 改写了原出手回调");
                        release.Invoke(audio, new object[] { run });
                        Need(type.GetField("encounter", Private).GetValue(audio) == null && run.Model.AttackTriggered == attack &&
                            run.Model.DamageApplied == damage && run.Model.CellEntered == step && run.Tactics.ObjectHit == prop,
                            id + " 离场后留有声音回调");
                    }
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(host); }
            Debug.Log("[VerifyDemoAudio] PASS 素材、循环导入、战场音乐切换和受击单次订阅");
        }

        private static void Need(bool ok, string message)
        {
            if (!ok) throw new InvalidOperationException(message);
        }
    }
}

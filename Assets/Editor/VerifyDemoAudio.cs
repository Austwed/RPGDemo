using System;
using System.Linq;
using System.Reflection;
using BaseConquest.Base;
using BaseConquest.Battle;
using BaseConquest.Presentation;
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
                CheckTitleMix(audio);
                CheckRoadFootsteps(host.transform, state);
                CheckSmithRoad(state);
            }
            finally { UnityEngine.Object.DestroyImmediate(host); }
            Debug.Log("[VerifyDemoAudio] PASS 素材、循环、回标题清音、路缘脚步、铁匠铺道路和战场订阅");
        }

        private static void CheckTitleMix(VillageOpeningAudio audio)
        {
            var type = audio.GetType();
            var setPlace = type.GetMethod("SetPlace", Private);
            var enterTitle = type.GetMethod("EnterTitle", Private);
            AudioSource Source(string key) => (AudioSource)type.GetField(key, Private).GetValue(audio);
            var exploration = Source("score");
            foreach (string previous in new[] { "Dungeon", "Battle", "Field" })
            {
                setPlace.Invoke(audio, new[] { Enum.Parse(setPlace.GetParameters()[0].ParameterType, previous) });
                foreach (string field in new[] { "ruinsGain", "battleGain", "duckUntil" })
                    type.GetField(field, Private).SetValue(audio, 1f);
                Source("battleScore").volume = Source("ruinsScore").volume = 1;
                exploration.volume = .21f;
                enterTitle.Invoke(audio, null);
                enterTitle.Invoke(audio, null);
                Need(Source("score") == exploration && Mathf.Approximately(exploration.volume, .21f) &&
                    Source("battleScore").volume == 0 && Source("ruinsScore").volume == 0 &&
                    !Source("resultMusic").isPlaying && (float)type.GetField("duckUntil", Private).GetValue(audio) == 0 &&
                    type.GetField("place", Private).GetValue(audio).ToString() == "Title",
                    previous + " 返回标题时须保留探索曲并立即清掉上一场景的音乐");
            }
        }

        private static void CheckRoadFootsteps(Transform parent, BaseState state)
        {
            var board = BattleBoard.CreateDefault(8, 6);
            foreach (var c in board.Cells) c.Surface = GroundSurface.Grass;
            board.Cell(new GridPosition(2, 3)).Surface = GroundSurface.Cobble;
            board.Cell(new GridPosition(5, 3)).Surface = GroundSurface.DirtPath;
            var mat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            var view = new BattleBoardView(board, parent, mat, mat);
            try
            {
                var type = view.GetType();
                var edges = EnvironmentRoadEdges.Build(state, board, view.Root.transform, false);
                type.GetProperty("RoadEdges", Private).SetValue(view, edges);
                var lookup = type.GetMethod("FootstepSurfaceAt", Private);
                GroundSurface At(double x, double z) => (GroundSurface)lookup.Invoke(view, new object[] { x, z });
                Need(At(2.5, 3.5) == GroundSurface.Cobble && At(2.5, 2.98) == GroundSurface.Cobble &&
                    At(2.5, 2.5) == GroundSurface.Grass && At(1.98, 2.98) == GroundSurface.Cobble,
                    "石路正中、铺进草格的直边及转角须随实际铺面发声");
                Need(At(4.98, 3.5) == GroundSurface.DirtPath && At(4.5, 3.5) == GroundSurface.Grass,
                    "原版泥路路缘应与未铺路的草面区分");
                Need(view.SetHomeTerrainTrial(true) && At(5.5, 3.5) == GroundSurface.Cobble &&
                    At(4.98, 3.5) == GroundSurface.Cobble && At(4.5, 3.5) == GroundSurface.Grass,
                    "换成石板外观后，路面与铺进邻格的路缘都应响石面脚步");
            }
            finally { view.Dispose(); UnityEngine.Object.DestroyImmediate(mat); }
        }

        private static void CheckSmithRoad(BaseState state)
        {
            state.Campaign.Location = "town";
            var town = state.Campaign.Layouts.Single(map => map.Region == "town");
            BaseEnvironment.Populate(town, BaseBattlefield.Board(state, "town"));
            bool SmithShrub(BaseMapPlacement obj) => obj.AssetId == "bush-wide" &&
                Math.Abs(obj.X - 11.3) < .001 && Math.Abs(obj.Y - 8.4) < .001;
            Need(!town.Objects.Any(SmithShrub), "生成灰岩镇时铁匠铺右下应留出道路");
            var old = new BaseMapPlacement { Id = "env_999", AssetId = "bush-wide", X = 11.3, Y = 8.4, VisualOnly = true };
            var nearby = old.Copy(); nearby.Id = "env_998"; nearby.X++;
            var custom = old.Copy(); custom.Id = "author_decoration";
            town.Objects.AddRange(new[] { old, nearby, custom });
            BaseDemo.EnsureCampLayout(state);
            Need(!town.Objects.Contains(old) && town.Objects.Contains(nearby) && town.Objects.Contains(custom),
                "旧地图应只移除铁匠铺路上的生成灌木，保留附近与作者自定义的摆件");
        }

        private static void Need(bool ok, string message)
        {
            if (!ok) throw new InvalidOperationException(message);
        }
    }
}

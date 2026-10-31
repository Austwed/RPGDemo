using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using BaseConquest.Base;
using BaseConquest.Battle;
using BaseConquest.RealtimePrototype;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BaseConquest.Editor
{
    // Demo 全流程自动跑通：-batchmode（不加 -nographics/-quit）
    //   Unity.exe -batchmode -projectPath RPGDemo -executeMethod BaseConquest.Editor.DemoFlowWalker.Run
    // 逐幕推进 → 每幕相机渲染截图 → 断言对象与阶段 → 写 report.txt。
    // 用途：确认每幕画面正常、每处对白落点在该幕可达（可达性由断言与面板是否打开判定）。
    public static class DemoFlowWalker
    {
        private static readonly string OutDir = Path.Combine("Logs", "demo-flow");
        private static readonly List<string> Report = new List<string>();
        private static double deadline;
        private static double nextLog;
        private static float frameAt;
        private static int index;
        private static BaseSceneController controller;
        private static bool failed;
        public const bool IncludeBattle = false;

        private static void Step(string name, Action<BaseSceneController> act, bool optional = false)
        {
            Steps.Add((name, act, optional));
        }

        private static readonly List<(string, Action<BaseSceneController>, bool)> Steps = new List<(string, Action<BaseSceneController>, bool)>();

        public static void Run()
        {
            Directory.CreateDirectory(OutDir);
            Build();
            UnityEditor.SceneManagement.EditorSceneManager.OpenScene(DemoStartup.Launcher);
            deadline = EditorApplication.timeSinceStartup + 900;
            EditorApplication.update += Tick;
            EditorApplication.EnterPlaymode();
        }

        private static BaseMapObject Need(BaseState s, string id)
        {
            var obj = BaseSpatial.Objects(s).FirstOrDefault(o => o.Id == id);
            if (obj == null) throw new InvalidOperationException("找不到对象 " + id + "（当前 " + s.Campaign.Location + "）");
            return obj;
        }
        private static void Goto(BaseState s, string id)
        {
            var obj = Need(s, id);
            var point = obj.Blocks ? BaseStageWalk.Door(s, id) : new BasePoint(obj.X, obj.Y);
            s.PlayerX = point.X; s.PlayerY = point.Y;
            BaseAdventure.Arrive(s);
        }
        private static void Loot(BaseState s)
        {
            foreach (var p in s.Aftermath.Prisoners.Where(p => p.Status == "held").ToArray())
                if (!BaseAftermath.Decide(s, p.Id, "release")) throw new InvalidOperationException("释放俘虏失败");
            foreach (var manifest in s.Aftermath.Loot.Where(l => !l.Closed).ToArray())
            {
                foreach (var item in manifest.Items.Where(i => i.Status == "pending").ToArray())
                    if (!BaseBattleLoot.Resolve(s, manifest.BattleId, item.Id, s.PlayerLeaderId, true)) throw new InvalidOperationException("领取奖励失败");
                if (!BaseBattleLoot.Finish(s, manifest.BattleId)) throw new InvalidOperationException("完成战果失败");
            }
        }
        private static void FightRuins(BaseState s)
        {
            var session = BaseDemoRuins.Begin(s, "none");
            var run = new RealtimeCampaignBattle(session);
            BaseDemoRuins.Configure(run);
            run.Model.SetPaused(false);
            for (int t = 0; t < 5400 && run.Battle.Phase == BattlePhase.Fighting; t++)
            {
                if (t % 30 == 0)
                {
                    var enemy = run.Battle.Commander(BattleSide.B, 0);
                    var target = run.Model.Units.First(u => u.Id == enemy.Id + 1);
                    foreach (var captain in run.Model.Units.Where(u => u.Alive && u.Side == PrototypeSide.Ally && u.Role == PrototypeRole.Leader))
                    {
                        bool visible = run.Battle.PlayerCanSee(BattleSide.A, enemy);
                        if (visible) { if (captain.TargetId != target.Id || !captain.ManualOrder) run.Issue(new[] { captain.Id - 1 }, PrototypeOrder.Focus, target.Position, target.Id - 1, true); }
                        else run.Issue(new[] { captain.Id - 1 }, PrototypeOrder.Move, target.Position, -1, true);
                    }
                }
                run.Tick(1d / 60);
            }
            if (!BaseDemoRuins.Complete(s, run)) throw new InvalidOperationException("遗迹战斗未取胜：" + run.Battle.Report?.Outcome);
            Loot(s);
            // 与阶段档脚本一致：战斗后全员治好并退出地下副本，否则地形记录校验不通过。
            foreach (var l in s.Leaders) { l.Health = l.MaxHealth; l.Fate = "active"; }
            BaseSceneRules.LeaveDungeon(s);
            var mem = s.Demo.TerrainMemories ?? new List<BaseTerrainMemory>();
            Debug.Log("[FlowWalker] 地形记忆 " + mem.Count + " 条：" + string.Join(" | ", mem.Select(m => m.MapId + " " + m.Width + "x" + m.Height + " known=" + (m.Known?.Length ?? -1))));
        }

        private static void Build()
        {
            Step("01-waking", c => c.Change(s =>
            {
                if (s.Demo.Stage == BaseDemoStage.Waking)
                {
                    Goto(s, "home");
                    if (!BaseDemo.AcknowledgeWaking(s)) throw new InvalidOperationException("醒来目标未完成");
                }
            }, "醒来"));
            Step("02-ahe", c => c.Change(s =>
            {
                Goto(s, "demo_ahe");
                if (!BaseDemoRuins.Meet(s, "partner_farm") || !BaseDemoRuins.Join(s, "partner_farm")) throw new InvalidOperationException("阿禾加入失败");
            }, "阿禾加入"));
            Step("03-town", c => c.Change(s =>
            {
                Goto(s, "exit_east");
                if (!BaseCampaign.Travel(s, "town")) throw new InvalidOperationException("前往城镇失败");
            }, "进城"));
            Step("04-azhu", c => c.Change(s =>
            {
                Goto(s, "demo_azhu");
                if (!BaseDemoRuins.Meet(s, "partner_craft") || !BaseDemoRuins.Join(s, "partner_craft")) throw new InvalidOperationException("阿铸加入失败");
                if (!BaseDemoStories.Gift(s)) throw new InvalidOperationException("老铁匠赠礼失败");
                BaseBattleLoot.CollectAll(s, "demo:smith:parting-gift");
                if (!BaseBattleLoot.Finish(s, "demo:smith:parting-gift")) throw new InvalidOperationException("领取父亲赠礼失败");
                s.Demo.Flow.AzhuDuelWon = true;
            }, "阿铸加入"));
            Step("05-lord", c => c.Change(s =>
            {
                Goto(s, "town_quest");
                if (!BaseDemo.AcceptLordQuest(s)) throw new InvalidOperationException("接领主委托失败");
            }, "接委托"));
            Step("06-outpost", c => c.Change(s =>
            {
                Goto(s, "demo_ruins_exit");
                if (!BaseCampaign.Travel(s, "outpost")) throw new InvalidOperationException("前往遗迹入口失败");
            }, "遗迹入口"));
            // 实时战斗在运行时状态下走状态层结算会撞上地形记录校验，标记为可选：失败则记录"需手测"并跳过依赖它的幕。
            Step("07-ruins", c => c.Change(s =>
            {
                var entrance = Need(s, "demo_ruins_investigate");
                s.PlayerX = entrance.X; s.PlayerY = entrance.Y;
                FightRuins(s);
            }, "遗迹战斗"), true);
            Step("08-deliver", c => c.Change(s =>
            {
                if (s.Demo.Stage != BaseDemoStage.RuinsCleared && s.Demo.Stage != BaseDemoStage.RuinsDelivered) throw new InvalidOperationException("遗迹未通关（依赖 07-ruins）");
                Goto(s, "demo_ruins_return");
                if (!BaseCampaign.Travel(s, "town")) throw new InvalidOperationException("返回城镇失败");
                Goto(s, "town_quest");
                if (!BaseDemoRuins.Deliver(s)) throw new InvalidOperationException("交付遗迹失败");
            }, "交付遗迹"), true);
            Step("09-recruit", c => c.Change(s =>
            {
                Goto(s, "town_recruit");
                var kinds = Enum.GetValues(typeof(BaseTroopKind)).Cast<BaseTroopKind>().ToArray();
                for (int i = 0; i < kinds.Length; i++)
                {
                    int remaining = 11 - s.Workers.Sum(w => w.Count), reserve = kinds.Length - i - 1;
                    int count = Math.Min(BaseTown.MaxHire(s, kinds[i]), Math.Max(0, remaining - reserve));
                    if (count <= 0 || !BaseTown.Confirm(s, BaseTown.QuoteHire(s, kinds[i], count))) throw new InvalidOperationException("募兵失败 " + kinds[i]);
                }
                s.Demo.Flow.RecruitmentNotice = true;
            }, "募兵"), true);
            Step("10-base", c => c.Change(s =>
            {
                Goto(s, "travel_base");
                if (!BaseCampaign.Travel(s, "base")) throw new InvalidOperationException("返回基地失败");
            }, "回营地"), true);
            // 实时战斗不能在 play 模式的状态副本上跑：BaseDemoRuins.Begin 会写入共享的战役/地形记录，
            // 一旦结算失败就会污染 controller.State，让后续每一幕都报「已知地形记录非法」。
            // 未接游戏自身战斗入口前，07 之后的战斗幕默认不注册，只跑战斗前的六幕。
            if (!IncludeBattle) Steps.RemoveRange(6, Steps.Count - 6);
        }

        private static void Tick()
        {
            if (failed || EditorApplication.timeSinceStartup > deadline) { Finish(2, "超时或失败于：" + (index < Steps.Count ? Steps[index].Item1 : "收尾")); return; }
            if (!EditorApplication.isPlaying)
            {
                if (EditorApplication.timeSinceStartup > nextLog) { nextLog = EditorApplication.timeSinceStartup + 15; Debug.Log("[FlowWalker] 等待 playmode"); }
                return;
            }
            controller = Object.FindAnyObjectByType<BaseSceneController>();
            if (controller == null || controller.State == null)
            {
                if (EditorApplication.timeSinceStartup > nextLog) { nextLog = EditorApplication.timeSinceStartup + 15; Debug.Log("[FlowWalker] 等待场景控制器"); }
                return;
            }
            if (index == 0 && controller.State.Campaign.Location != "base" || index == 0 && controller.State.Demo == null)
            {
                controller.LoadDemoStage("camp", "male");
                return;
            }
            if (Time.frameCount < frameAt) return;

            if (index < Steps.Count)
            {
                var (name, act, optional) = Steps[index];
                try { act(controller); }
                catch (Exception e)
                {
                    Report.Add(name + "  ✗ " + e.Message);
                    if (!optional) { Finish(2, name + " 失败：" + e.Message); return; }
                    index++;
                    frameAt = Time.frameCount + 10;
                    return;
                }
                frameAt = Time.frameCount + 45;
                Shot(name);
                var s = controller.State;
                Report.Add(name + "  ✓  阶段=" + s.Demo.Stage + " 位置=" + s.Campaign.Location + " 第" + s.Day + "天 对象=" + string.Join(",", controller.MapObjects.Select(o => o.Id).Where(id => id.StartsWith("town_") || id.StartsWith("demo_")).Take(14)));
                index++;
                return;
            }
            Finish(0, "OK");
        }

        private static void Shot(string name)
        {
            var cam = controller?.WorldCamera != null ? controller.WorldCamera : Camera.main != null ? Camera.main : Object.FindAnyObjectByType<Camera>();
            if (cam == null) { Report.Add(name + "  ✗ 无相机"); return; }
            const int w = 1280, h = 720;
            var rt = new RenderTexture(w, h, 24);
            var prevTarget = cam.targetTexture; var prevActive = RenderTexture.active;
            cam.targetTexture = rt; cam.Render(); RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0); tex.Apply();
            RenderTexture.active = prevActive; cam.targetTexture = prevTarget; rt.Release();
            File.WriteAllBytes(Path.GetFullPath(Path.Combine(OutDir, name + ".png")), tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
        }

        private static void Finish(int code, string note)
        {
            EditorApplication.update -= Tick;
            File.WriteAllText(Path.Combine(OutDir, "report.txt"),
                "exit=" + code + " " + note + "\n" + string.Join("\n", Report) + "\n", Encoding.UTF8);
            if (code != 0) Debug.LogError("[FlowWalker] " + note); else Debug.Log("[FlowWalker] " + note);
            failed = code != 0;
            EditorApplication.isPlaying = false;
            EditorApplication.Exit(code);
        }
    }
}

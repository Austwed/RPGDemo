#if UNITY_EDITOR || (DEVELOPMENT_BUILD && UNITY_STANDALONE_WIN)
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using BaseConquest.Base;
using BaseConquest.Battle;
using BaseConquest.Presentation;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace BaseConquest.Perf
{
    public sealed class CurrentScenePerfProbe : MonoBehaviour
    {
        // HUD actions go through their real buttons. Reflection here only locates the active panel
        // and reads the quantity for an independent assertion.
        private static readonly FieldInfo PanelField = typeof(BaseHud).GetField("panel", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo QuantityField = typeof(BaseHud).GetField("inventoryQuantity", BindingFlags.Instance | BindingFlags.NonPublic);
        private readonly List<string> output = new List<string>();
        private readonly List<string> raw = new List<string>
        {
            "scenario,sample,wall_frame_ms,gc_frame_bytes,main_thread_ms,click_ms,click_thread_bytes"
        };
        private string result;
        private ProfilerRecorder gc, main;
        private bool gcFrameSupported, threadAllocationSupported;
        private Keyboard keyboard;
        private int errors;
        private static BaseSceneController Scene => Object.FindAnyObjectByType<BaseSceneController>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        public static void Boot()
        {
            if (!Environment.GetCommandLineArgs().Contains("-slgScenePerf") || Object.FindAnyObjectByType<CurrentScenePerfProbe>() != null)
                return;
            string saveRoot = Environment.GetEnvironmentVariable("BASE_CONQUEST_UI_CHECK_SAVES");
            string report = Environment.GetEnvironmentVariable("SLG_SCENE_PERF_RESULT");
            if (string.IsNullOrWhiteSpace(report) || !Path.IsPathRooted(report))
            {
                UnityEngine.Debug.LogError("A full path for SLG_SCENE_PERF_RESULT is required.");
                Application.Quit(2);
                return;
            }

            try
            {
                // The gateway checks emptiness on first use, before startup callbacks can install story text.
                Need(!string.IsNullOrWhiteSpace(saveRoot) && Path.IsPathRooted(saveRoot) &&
                    string.Equals(Path.GetFullPath(saveRoot).TrimEnd(Path.DirectorySeparatorChar),
                        Path.GetFullPath(BaseBattleGateway.SaveDirectory).TrimEnd(Path.DirectorySeparatorChar),
                        StringComparison.OrdinalIgnoreCase), "Game is not using the isolated save directory");
            }
            catch (Exception error)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(report));
                File.WriteAllText(report, "FAIL " + error);
                Application.Quit(2);
                return;
            }

            Application.runInBackground = true;
            var host = new GameObject("CurrentScenePerfProbe");
            DontDestroyOnLoad(host);
            host.AddComponent<CurrentScenePerfProbe>();
        }

        private IEnumerator Start()
        {
            result = Environment.GetEnvironmentVariable("SLG_SCENE_PERF_RESULT");
            if (string.IsNullOrWhiteSpace(result))
            {
                Application.Quit(2);
                yield break;
            }

            result = Path.GetFullPath(result);
            Directory.CreateDirectory(Path.GetDirectoryName(result));
            Log("platform=" + Application.platform + " unity=" + Application.unityVersion + " resolution=" + Screen.width + "x" + Screen.height + " cpu=" + SystemInfo.processorType + " gpu=" + SystemInfo.graphicsDeviceName + " debug=" + UnityEngine.Debug.isDebugBuild);
            gc = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame", 1);
            main = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "Main Thread", 1);
            // Mono may implement the thread counter as a constant zero. Test it with a known allocation.
            const int knownAllocation = 256 * 1024;
            long before = GC.GetAllocatedBytesForCurrentThread();
            var control = new byte[knownAllocation];
            threadAllocationSupported = GC.GetAllocatedBytesForCurrentThread() - before >= knownAllocation;
            GC.KeepAlive(control);
            for (int i = 0; i < 6; i++)
            {
                var pulse = new byte[knownAllocation];
                GC.KeepAlive(pulse);
                yield return null;
                if (gc.Valid && gc.Count > 0 && gc.LastValue >= knownAllocation)
                    gcFrameSupported = true;
            }

            Log("GC frame counter calibrated=" + gcFrameSupported + ", allocation thread counter calibrated=" + threadAllocationSupported + ", Main Thread recorder=" + main.Valid + ". Counter values can lag one frame. Frame times include pacing.");
            yield return Guard("exploration", Exploration());
            if (keyboard != null && keyboard.added)
                InputSystem.RemoveDevice(keyboard);
            keyboard = null;
            yield return Guard("warehouse", Warehouse());
            yield return Guard("mixed18-1x", MixedBattle(1));
            yield return Guard("mixed18-3x", MixedBattle(3));
            gc.Dispose();
            main.Dispose();
            Log(errors == 0 ? "PASS" : "FAILED sections=" + errors);
            Application.Quit(errors == 0 ? 0 : 2);
        }

        private IEnumerator Guard(string name, IEnumerator routine)
        {
            var stack = new Stack<IEnumerator>();
            stack.Push(routine);
            while (stack.Count > 0)
            {
                object current = null;
                bool done;
                try
                {
                    done = !stack.Peek().MoveNext();
                    if (!done)
                        current = stack.Peek().Current;
                }
                catch (Exception e)
                {
                    errors++;
                    Log("FAIL " + name + " " + e);
                    yield break;
                }

                if (done)
                {
                    stack.Pop();
                    continue;
                }

                if (current is IEnumerator nested)
                    stack.Push(nested);
                else
                    yield return current;
            }
        }

        // Load the same official fixture before each group, outside its measurement window.
        private IEnumerator Load(string stage)
        {
            double end = Time.realtimeSinceStartupAsDouble + 45;
            BaseSceneController prior;
            while ((prior = Scene) == null || prior.Hud == null || prior.State == null || prior.Busy)
            {
                if (Time.realtimeSinceStartupAsDouble > end)
                    throw new InvalidOperationException("initial scene not ready");
                yield return null;
            }

            if (stage == "village")
            {
                string path = Environment.GetEnvironmentVariable("SLG_PERF_VILLAGE_FIXTURE");
                Need(!string.IsNullOrWhiteSpace(path) && Path.IsPathRooted(path) && File.Exists(path),
                    "Set SLG_PERF_VILLAGE_FIXTURE to the drill.json checkpoint from VerifyDemoPlayerRoute.");
                var village = JsonUtility.FromJson<BaseState>(File.ReadAllText(path));
                BaseEconomy.Validate(village);
                Need(BaseDemoPrologue.Village(village) && !village.Campaign.HasActiveBattle &&
                    string.IsNullOrEmpty(village.SceneBattleId) && village.Demo.ConfigHash == DemoBalanceRuntime.Load().WorkbookSha256,
                    "Expected the peaceful village checkpoint for the current balance configuration");
                BaseBattleGateway.Catalog(false).SaveSlot("slot1", village, "village frame benchmark");
                prior.LoadSlot("slot1");
            }
            else
                prior.LoadDemoStage(stage, "male");
            end = Time.realtimeSinceStartupAsDouble + 45;
            while (Scene == null || Scene == prior || Scene.State == null || Scene.Hud == null || Scene.Busy)
            {
                if (Time.realtimeSinceStartupAsDouble > end)
                    throw new InvalidOperationException("stage load timed out: " + stage);
                yield return null;
            }

            var scene = Scene;
            for (int i = 0; i < 35; i++)
                yield return null;
            if (stage == "camp")
                Need(scene.Change(s => Need(BaseDemo.AcknowledgeWaking(s), "wake stage"), "wake"), scene.Message);
            scene.Hud.ClosePanel();
            for (int i = 0; i < 35; i++)
                yield return null;
        }

        private IEnumerator Exploration()
        {
            yield return Load("village");
            var scene = Scene;
            Need(BaseDemoPrologue.Village(scene.State) && !scene.Hud.PanelOpen, "village exploration is blocked");
            double idleX = scene.State.PlayerX, idleY = scene.State.PlayerY;
            const double duration = 4;
            var idle = new Series(8192);
            int used = 0;
            double start = Time.unscaledTimeAsDouble;
            while (Time.unscaledTimeAsDouble - start < duration)
            {
                yield return null;
                Capture(idle, used++);
            }

            Need(Math.Abs(scene.State.PlayerX - idleX) + Math.Abs(scene.State.PlayerY - idleY) < .001, "village idle position changed");
            Emit("village-idle", idle, used);
            keyboard = InputSystem.AddDevice<Keyboard>("PerfKeyboard");
            keyboard.MakeCurrent();
            var move = new Series(8192);
            for (int i = 0; i < 5; i++)
                yield return null;
            used = 0;
            int lastLeg = -1;
            double distance = 0, left = 0, right = 0;
            start = Time.unscaledTimeAsDouble;
            while (Time.unscaledTimeAsDouble - start < duration)
            {
                // Give movement the same real travel time at both capped and uncapped frame rates.
                int leg = (int)((Time.unscaledTimeAsDouble - start) / .20);
                if (leg != lastLeg)
                {
                    InputSystem.QueueStateEvent(keyboard, new KeyboardState(leg % 2 == 0 ? Key.A : Key.D));
                    lastLeg = leg;
                }
                double x = scene.State.PlayerX, y = scene.State.PlayerY;
                yield return null;
                Capture(move, used++);
                double dx = scene.State.PlayerX - x, dy = scene.State.PlayerY - y;
                distance += Math.Sqrt(dx * dx + dy * dy);
                left += Math.Max(0, -dx);
                right += Math.Max(0, dx);
            }

            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            yield return null;
            Log("walk seconds=" + F(Time.unscaledTimeAsDouble - start) + " frames=" + used + " distance=" + F(distance) +
                " left=" + F(left) + " right=" + F(right) + " panelOpen=" + scene.Hud.PanelOpen);
            Need(distance > 2 && left > .25 && right > .25, "walk fixture did not move in both directions");
            Need(BaseDemoPrologue.Village(scene.State) && !scene.Hud.PanelOpen, "village walk left exploration");
            Emit("village-keyboard-walk", move, used);
        }

        private IEnumerator Warehouse()
        {
            yield return Load("camp");
            var scene = Scene;
            Need(scene.Change(s =>
            {
                var target = BasePartyInventory.WarehouseObject(s);
                var point = target.Blocks ? BaseStageWalk.Door(s, target.Id) : new BasePoint(target.X, target.Y);
                s.PlayerX = point.X;
                s.PlayerY = point.Y;
                BaseInteraction.Walk(new BasePoint(target.X - point.X, target.Y - point.Y));
                BaseAdventure.Arrive(s);
                if (!BaseBackpack.Add(s, BaseEquipmentCrafting.Stone, 3))
                    throw new InvalidOperationException("fixture stock");
            }, "warehouse fixture"), "prepare warehouse");
            scene.Hud.ClosePanel();
            scene.Hud.OpenWarehouse();
            var stored = BasePartyInventory.Entries(scene.State).First(e => e.Holder == "" && e.Stack?.ItemId == BaseEquipmentCrafting.Stone && e.Count >= 3);
            int initialStock = stored.Count;
            var view = (RectTransform)PanelField.GetValue(scene.Hud);
            var cell = view.GetComponentsInChildren<PartyInventoryCell>().Single(c => c.ItemId == stored.Id);
            cell.GetComponent<Button>().onClick.Invoke();
            for (int i = 0; i < 8; i++)
                yield return null;
            var samples = new Series(22);
            for (int i = 0; i < samples.Length; i++)
            {
                view = (RectTransform)PanelField.GetValue(scene.Hud);
                var btn = view.Find("InventoryItemMenu/" + (i % 2 == 0 ? "QuantityDown" : "QuantityUp")).GetComponent<Button>();
                Need(btn.isActiveAndEnabled && btn.IsInteractable(), "quantity button disabled");
                int old = (int)QuantityField.GetValue(scene.Hud);
                long before = threadAllocationSupported ? GC.GetAllocatedBytesForCurrentThread() : 0;
                long ticks = Stopwatch.GetTimestamp();
                btn.onClick.Invoke();
                samples.click[i] = (Stopwatch.GetTimestamp() - ticks) * 1000.0 / Stopwatch.Frequency;
                samples.bytes[i] = threadAllocationSupported ? GC.GetAllocatedBytesForCurrentThread() - before : -1;
                Need((int)QuantityField.GetValue(scene.Hud) == old + (i % 2 == 0 ? -1 : 1), "quantity remained unchanged");
                yield return null;
                Capture(samples, i);
                for (int n = 0; n < 3; n++)
                    yield return null;
            }

            Emit("warehouse-quantity-alternate", samples, samples.Length, 2);
            Need(BasePartyInventory.Find(scene.State, stored.Id).Count == initialStock, "stock changed on quantity preview");
            scene.Hud.ClosePanel();
        }

        // Rendering, controller updates and real model ticks all run on the normal frame loop.
        private IEnumerator MixedBattle(int speed)
        {
            yield return Load("defense");
            var scene = Scene;
            scene.StartStandaloneBattleFixture(BaseStandaloneBattlePreset.Mixed);
            BattleSceneController battle = null;
            for (int i = 0; i < 75 && (battle == null || battle.Realtime == null); i++)
            {
                yield return null;
                battle = scene.GetComponentInChildren<BattleSceneController>();
            }

            Need(battle != null && battle.Realtime != null, "mixed battle failed: " + scene.Message);
            if (!battle.Automatic)
                battle.ToggleRealtimePause();
            if (battle.FastMode != (speed == 3))
                battle.ToggleFast();
            Need(battle.FastMode == (speed == 3) && battle.Automatic, "speed or auto-run mismatch");
            var model = battle.Realtime.Model;
            int units = model.Units.Count;
            Need(units == 18 && model.Units.All(unit => unit.Alive), "expected two full nine-person armies");
            var samples = new Series(16384);
            double start = model.Elapsed;
            double startedAt = Time.realtimeSinceStartupAsDouble, lastProgressAt = startedAt, previous = start;
            int used = 0;
            while (model.Elapsed - start < 12 && battle.Realtime.Battle.Phase == BattlePhase.Fighting)
            {
                yield return null;
                Capture(samples, used++);
                double now = Time.realtimeSinceStartupAsDouble;
                if (model.Elapsed > previous)
                {
                    previous = model.Elapsed;
                    lastProgressAt = now;
                }
                if (now - lastProgressAt > 5 || now - startedAt > 60)
                    throw new InvalidOperationException("battle stopped advancing: simulated=" + F(model.Elapsed - start) +
                        " frames=" + used + " auto=" + battle.Automatic + " dialogue=" + battle.ExternalDialogue +
                        " menu=" + battle.CampaignMenuOpen + " phase=" + battle.Realtime.Battle.Phase);
            }

            double elapsed = model.Elapsed - start;
            Log("mixed18-" + speed + "x units=" + units + " simulationSeconds=" + F(elapsed) +
                " wallSeconds=" + F(Time.realtimeSinceStartupAsDouble - startedAt) + " frames=" + used + " automatic=" + battle.Automatic);
            Need(used > 8 && elapsed >= 12, "combat fixture did not complete 12 simulated seconds");
            Emit("mixed18-" + speed + "x", samples, used, Math.Min(6, used / 8));
        }

        private sealed class Series
        {
            public double[] wall, allocation, thread;
            public readonly double[] click, bytes;
            public int Length => wall.Length;

            public Series(int n)
            {
                wall = new double[n];
                allocation = new double[n];
                thread = new double[n];
                click = new double[n];
                bytes = new double[n];
            }
        }

        private void Capture(Series s, int i)
        {
            // The frame rate controls sample count. Grow only on hosts exceeding the initial reserve.
            if (i == s.Length)
            {
                int capacity = checked(i * 2);
                Array.Resize(ref s.wall, capacity);
                Array.Resize(ref s.allocation, capacity);
                Array.Resize(ref s.thread, capacity);
            }
            s.wall[i] = Time.unscaledDeltaTime * 1000.0;
            s.allocation[i] = gcFrameSupported ? gc.LastValue : -1;
            s.thread[i] = main.Valid && main.Count > 0 && main.LastValue > 0 ? main.LastValue / 1000000.0 : -1;
        }

        private void Emit(string name, Series s, int used, int skip = 1)
        {
            int n = used - skip;
            Need(n > 0, "empty sample");
            Log(name + " n=" + n + " wall_ms=" + Summary(s.wall, skip, n) + " gc_bytes=" + Summary(s.allocation, skip, n) + " main_ms=" + Summary(s.thread, skip, n) + (name.StartsWith("warehouse", StringComparison.Ordinal) ? " click_ms=" + Summary(s.click, skip, n) + " click_alloc_bytes=" + Summary(s.bytes, skip, n) : ""));
            for (int i = skip; i < used; i++)
                raw.Add(string.Join(",", name, i.ToString(), F(s.wall[i]), F(s.allocation[i]), F(s.thread[i]), name.StartsWith("warehouse", StringComparison.Ordinal) ? F(s.click[i]) : "", name.StartsWith("warehouse", StringComparison.Ordinal) ? F(s.bytes[i]) : ""));
            File.WriteAllLines(Path.ChangeExtension(result, ".frames.csv"), raw);
        }

        private static string Summary(double[] source, int start, int n)
        {
            var v = source.Skip(start).Take(n).Where(value => value >= 0).ToArray();
            n = v.Length;
            if (n == 0)
                return "unavailable";
            Array.Sort(v);
            double Pick(double p) => v[Math.Min(n - 1, (int)Math.Ceiling(p * n) - 1)];
            return "(p50:" + F(Pick(.5)) + " p95:" + F(Pick(.95)) + " p99:" + F(Pick(.99)) + " max:" + F(v[n - 1]) + ")";
        }

        private static string F(double n) => n < 0 ? "" : n.ToString("0.###", CultureInfo.InvariantCulture);
        private static void Need(bool ok, string reason)
        {
            if (!ok)
                throw new InvalidOperationException(reason);
        }

        private void Log(string value)
        {
            output.Add(value);
            File.WriteAllLines(result, output);
        }
    }
}
#endif

#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.Rendering;
using Debug = UnityEngine.Debug;

namespace BaseConquest.Base
{
    // Development Player: -slgTransitionPerf, a new BASE_CONQUEST_UI_CHECK_SAVES folder
    // and an absolute SLG_TRANSITION_PERF_RESULT filename. No normal game session starts this probe.
    public sealed class TransitionBenchmark : MonoBehaviour
    {
        private const string Switch = "-slgTransitionPerf";
        private static bool Requested => Environment.GetCommandLineArgs().Contains(Switch);
        private static BaseSceneController Scene => FindAnyObjectByType<BaseSceneController>();
        private static readonly string[] SlotIds = BaseSaveCatalog.AutoSlots.Concat(BaseSaveCatalog.Slots).ToArray();
        private static long worldTicks, boardTicks;
        private static int worlds, boards;
        private static bool measuring;

        internal readonly struct Scope : IDisposable
        {
            private readonly bool board;
            private readonly long start;
            internal Scope(bool board) { this.board = board; start = measuring ? Stopwatch.GetTimestamp() : 0; }
            public void Dispose()
            {
                if (start == 0) return;
                long duration = Stopwatch.GetTimestamp() - start;
                if (board) { boardTicks += duration; boards++; }
                else { worldTicks += duration; worlds++; }
            }
        }
        internal static Scope MeasureWorld() => new Scope(false);
        internal static Scope MeasureBoard() => new Scope(true);

        private string output;
        private Keyboard keyboard;
        private CanvasGroup cover;
        private Func<BaseSceneController, bool> target;
        private readonly Stopwatch clock = new Stopwatch();
        private double visibleMs;
        private double landingX, landingY;
        private BaseSceneController observedDestination;
        private int srpBegins, srpCameras, legacyCameras, srpContexts, destinationPasses, coveredPasses;
        private int destinationBeganFrame;
        private Camera destinationBeganCamera;
        private string visibleVia;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Boot()
        {
            if (!Requested) return;
            var host = new GameObject(nameof(TransitionBenchmark));
            DontDestroyOnLoad(host);
            var probe = host.AddComponent<TransitionBenchmark>();
            try
            {
                string path = Environment.GetEnvironmentVariable("SLG_TRANSITION_PERF_RESULT");
                string saves = Environment.GetEnvironmentVariable("BASE_CONQUEST_UI_CHECK_SAVES");
                Require(!string.IsNullOrWhiteSpace(path) && Path.IsPathRooted(path), "Missing absolute result filename");
                probe.output = Path.GetFullPath(path);
                Directory.CreateDirectory(Path.GetDirectoryName(probe.output));
                Require(!string.IsNullOrWhiteSpace(saves) && Path.IsPathRooted(saves) &&
                    string.Equals(Path.GetFullPath(saves).TrimEnd(Path.DirectorySeparatorChar),
                        Path.GetFullPath(BaseBattleGateway.SaveDirectory).TrimEnd(Path.DirectorySeparatorChar),
                        StringComparison.OrdinalIgnoreCase), "Game is not using the isolated save directory");
                // BaseBattleGateway validates emptiness on first access. Story text can already have
                // installed the packaged text below player-data during another pre-scene callback.
                File.WriteAllText(probe.output, "");
                Application.runInBackground = true;
                probe.StartCoroutine(probe.Guard(probe.Run()));
            }
            catch (Exception error) { probe.Finish(error); }
        }

#if UNITY_EDITOR
        public static void RunEditor()
        {
            Require(Requested, "Pass -slgTransitionPerf to the batch Editor");
            UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Scenes/Demo_Launcher.unity");
            UnityEditor.EditorApplication.EnterPlaymode();
        }
#endif
        private IEnumerator Guard(IEnumerator root)
        {
            // Drive nested iterators here so a failing assertion also terminates unattended runs.
            var stack = new Stack<IEnumerator>();
            stack.Push(root);
            while (stack.Count > 0)
            {
                object next = null;
                try
                {
                    if (!stack.Peek().MoveNext()) { stack.Pop(); continue; }
                    next = stack.Peek().Current;
                    if (next is IEnumerator child) { stack.Push(child); continue; }
                }
                catch (Exception error) { Finish(error); yield break; }
                yield return next;
            }
            Finish(null);
        }

        private IEnumerator Run()
        {
            yield return null;
            Log($"Unity={Application.unityVersion} platform={Application.platform} gpu={SystemInfo.graphicsDeviceName} " +
                $"resolution={Screen.width}x{Screen.height} vSync={QualitySettings.vSyncCount} targetFps={Application.targetFrameRate}");
            yield return Until(() => Ready(Scene), "launcher");
            var start = Scene;
            start.LoadDemoStage("camp", "male");
            yield return Until(() => Ready(Scene) && Scene != start && Scene.State.Demo.Stage == BaseDemoStage.Waking, "camp stage");
            Require(Scene.Change(state => Require(BaseDemo.AcknowledgeWaking(state), "Wake was rejected"), "benchmark wake"),
                "Could not finish waking: " + Scene.Message);
            Scene.Hud.ClosePanel();
            keyboard = InputSystem.AddDevice<Keyboard>();
            keyboard.MakeCurrent();
            var overlay = FindAnyObjectByType<SceneLoadingCover>(FindObjectsInactive.Include);
            cover = overlay == null ? null : overlay.GetComponent<CanvasGroup>();
            RenderPipelineManager.beginCameraRendering += CameraStarted;
            RenderPipelineManager.endCameraRendering += CameraRendered;
            RenderPipelineManager.endContextRendering += ContextRendered;
            Camera.onPostRender += LegacyCameraRendered;
            var catalog = BaseBattleGateway.Catalog(false);
            foreach (string id in BaseSaveCatalog.AutoSlots)
                if (!catalog.Store(id).Exists) catalog.SaveAuto(Scene.State, "benchmark auto seed");
            foreach (string id in BaseSaveCatalog.Slots) catalog.SaveSlot(id, Scene.State, "benchmark camp seed " + id);
            var backupFixture = Scene.State.Copy();
            BaseState primaryFixture = null;
            Require(SlotIds.All(id => catalog.Store(id).Exists), "Expected 6 auto and 3 manual slots");
            for (int n = 0; n < 10; n++) yield return null;

            int repeats = 3;
            string count = Environment.GetEnvironmentVariable("SLG_TRANSITION_PERF_REPEATS");
            if (!string.IsNullOrEmpty(count))
                Require(int.TryParse(count, out repeats) && repeats >= 1 && repeats <= 20, "Expected 1..20 repetitions");
            for (int i = 1; i <= repeats; i++)
            {
                yield return Travel(i, "base-town", "exit_east", "town", false, catalog);
                if (i == 1)
                {
                    catalog.SaveSlot("slot1", Scene.State, "benchmark town; backup at camp");
                    primaryFixture = Scene.State.Copy();
                }
                yield return Travel(i, "town-ruins", "demo_ruins_exit", "outpost", true, catalog);
                yield return Travel(i, "ruins-town", "demo_ruins_return", "town", true, catalog);
                yield return Travel(i, "town-base", "travel_base", "base", false, catalog);
                yield return Slots(i, catalog);
                yield return Load(i, primaryFixture, false);
                yield return Load(i, backupFixture, true);
            }
        }

        private IEnumerator Travel(int pass, string route, string exit, string location, bool inPlace, BaseSaveCatalog catalog)
        {
            var source = Scene;
            var marker = BaseSpatial.Objects(source.State).Single(obj => obj.Id == exit);
            source.State.PlayerX = marker.X; source.State.PlayerY = marker.Y;
            BaseInteraction.Facing = default;
            var before = SlotIds.ToDictionary(id => id, id => Digest(catalog.Store(id).Path));
            yield return Measure(pass, route, () => { if (inPlace) source.TravelDemo(location); else source.Travel(location); },
                scene => scene.State.Campaign.Location == location && (inPlace || scene != source));
            var changed = SlotIds.Where(id => before[id] != Digest(catalog.Store(id).Path)).ToArray();
            Require(changed.Length == 1 && BaseSaveCatalog.AutoSlots.Contains(changed[0]), "Travel must rotate exactly one auto slot");
            string auto = changed[0];
            Require(Digest(catalog.Store(auto).Path + ".bak") == before[auto], "Auto backup differs from previous primary");
            CheckLanding(catalog.LoadSlot(auto), Scene.State);
            Log($"SAVE pass={pass} route={route} changed={auto} landing={Scene.State.Campaign.CurrentMapId} backup=PASS manual=unchanged");
        }

        private IEnumerator Slots(int pass, BaseSaveCatalog catalog)
        {
            var scene = Scene;
            clock.Restart();
            scene.Hud.OpenSlots(false);
            double click = clock.Elapsed.TotalMilliseconds;
            yield return null;
            double nextFrame = clock.Elapsed.TotalMilliseconds;
            Require(scene.Hud.PanelOpen, "Load slots panel did not open");
            clock.Restart();
            var entries = catalog.Entries();
            double entriesMs = clock.Elapsed.TotalMilliseconds;
            Require(entries.Count == 9 && entries.All(entry => string.IsNullOrEmpty(entry.Error)), "Expected 9 readable entries");
            Log(FormattableString.Invariant($"SLOTS pass={pass} openMs={click:F2} nextFrameMs={nextFrame:F2} entriesOnlyWarmMs={entriesMs:F2} readable={entries.Count}"));
            scene.Hud.ClosePanel();
            yield return null;
        }

        private IEnumerator Load(int pass, BaseState expected, bool backup)
        {
            Require(expected.Campaign.Location == (backup ? "base" : "town"), "Primary and backup fixtures must be distinct");
            var original = Scene;
            yield return Measure(pass, backup ? "load-backup" : "load-primary", () => original.LoadSlot("slot1", backup),
                scene => scene != original && scene.State.Campaign.Location == expected.Campaign.Location);
            CheckLanding(expected, Scene.State);
        }

        // click = handler return; ready = first update with destination HUD/camera; visible = first destination
        // camera render with the loading cover below full opacity; input = first actual arrow-key displacement.
        private IEnumerator Measure(int pass, string route, Action enter, Func<BaseSceneController, bool> arrived)
        {
            for (int i = 0; i < 5; i++) yield return null;
            target = arrived; visibleMs = -1; worlds = boards = 0; worldTicks = boardTicks = 0;
            observedDestination = null; visibleVia = "unobserved";
            srpBegins = srpCameras = legacyCameras = srpContexts = destinationPasses = coveredPasses = 0;
            destinationBeganCamera = null; destinationBeganFrame = -1;
            int renderedAtStart = Time.renderedFrameCount;
            measuring = true;
            clock.Restart();
            enter();
            double click = clock.Elapsed.TotalMilliseconds;
            yield return Until(() => Ready(Scene) && arrived(Scene), route);
            double ready = clock.Elapsed.TotalMilliseconds;
            var scene = Scene;
            observedDestination = scene;
            Require(!scene.Hud.DialogueOpen, route + " has unexpected blocking dialogue");
            landingX = scene.State.PlayerX;
            landingY = scene.State.PlayerY;
            Key direction = WalkableDirection(scene);
            double queued = clock.Elapsed.TotalMilliseconds;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(direction));
            yield return Until(() => Math.Abs(scene.State.PlayerX - landingX) + Math.Abs(scene.State.PlayerY - landingY) > .0001,
                route + " keyboard movement");
            double input = clock.Elapsed.TotalMilliseconds;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            // Some desktop players continue processing input while the window is occluded and never submit
            // a camera pass. Keep the independently observed input time and report missing render evidence.
            double deadline = input + 2000;
            while (SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null && visibleMs < 0 && clock.Elapsed.TotalMilliseconds < deadline)
                yield return null;
            measuring = false; target = null;
            double worldMs = worldTicks * 1000d / Stopwatch.Frequency, boardMs = boardTicks * 1000d / Stopwatch.Frequency;
            double visibleAndControllable = visibleMs < 0 ? -1 : Math.Max(visibleMs, input);
            Log(FormattableString.Invariant($"TRANSITION pass={pass} route={route} slots=9 clickMs={click:F2} readyMs={ready:F2} visibleMs={visibleMs:F2} inputAckMs={input:F2} visibleAndControllableMs={visibleAndControllable:F2} inputQueuedMs={queued:F2} inputSetupMs={queued-ready:F2} movementKey={direction} buildWorldMs={worldMs:F2} buildWorldCount={worlds} boardRefreshMs={boardMs:F2} boardRefreshCount={boards}"));
            var camera = scene.WorldCamera;
            Log(FormattableString.Invariant($"RENDER pass={pass} route={route} via={visibleVia} srpCameraBegins={srpBegins} srpCameraEnds={srpCameras} builtinCameraEnds={legacyCameras} srpContextEnds={srpContexts} destinationPasses={destinationPasses} coveredPasses={coveredPasses} renderedFrames={Time.renderedFrameCount-renderedAtStart} focused={Application.isFocused} cameraEnabled={camera.isActiveAndEnabled} display={camera.targetDisplay} offscreen={camera.targetTexture!=null} coverActive={cover!=null && cover.isActiveAndEnabled} coverAlpha={(cover==null?-1:cover.alpha):F2} pipeline={GraphicsSettings.currentRenderPipeline?.GetType().Name ?? "BuiltIn"} waitAfterInputLimitMs=2000"));
            Require(worlds == 1, route + " expected one BuildWorld, observed " + worlds);
            yield return null;
        }

        private static Key WalkableDirection(BaseSceneController scene)
        {
            var state = scene.State;
            var geometry = BaseSpatial.Objects(state);
            var obstacles = scene.Companions.PlayerObstacles(state).ToArray();
            foreach (var (key, dx, dy) in new[] { (Key.RightArrow, .1, 0d), (Key.LeftArrow, -.1, 0d),
                (Key.UpArrow, 0d, .1), (Key.DownArrow, 0d, -.1) })
            {
                double x = state.PlayerX, y = state.PlayerY;
                BaseSpatial.Move(state, ref x, ref y, dx, dy, obstacles, geometry: geometry);
                if (Math.Abs(x - state.PlayerX) + Math.Abs(y - state.PlayerY) > .01) return key;
            }
            throw new InvalidOperationException("No walkable direction at the destination landing");
        }
        private void CameraStarted(ScriptableRenderContext context, Camera camera)
        {
            if (target == null || visibleMs >= 0) return;
            srpBegins++;
            var scene = observedDestination != null ? observedDestination : Scene;
            if (scene == null || scene.WorldCamera != camera || !Ready(scene) || !target(scene)) return;
            destinationBeganCamera = camera;
            destinationBeganFrame = Time.frameCount;
        }
        private void CameraRendered(ScriptableRenderContext context, Camera camera)
        {
            if (target == null || visibleMs >= 0) return;
            srpCameras++;
            ObserveCamera(camera, "srp-camera");
        }
        private void LegacyCameraRendered(Camera camera)
        {
            if (target == null || visibleMs >= 0) return;
            legacyCameras++;
            ObserveCamera(camera, "builtin-camera");
        }
        private void ContextRendered(ScriptableRenderContext context, List<Camera> cameras)
        {
            if (target == null || visibleMs >= 0) return;
            srpContexts++;
            if (destinationBeganFrame != Time.frameCount || destinationBeganCamera == null) return;
            foreach (var camera in cameras)
                if (camera == destinationBeganCamera && ObserveCamera(camera, "srp-context-after-camera-begin")) break;
        }
        private bool ObserveCamera(Camera camera, string via)
        {
            var scene = observedDestination != null ? observedDestination : Scene;
            if (!Ready(scene) || scene.WorldCamera != camera || !target(scene) ||
                !camera.isActiveAndEnabled || camera.targetTexture != null || camera.targetDisplay != 0) return false;
            observedDestination = scene;
            destinationPasses++;
            if (cover != null && cover.isActiveAndEnabled && cover.alpha >= 1) { coveredPasses++; return false; }
            visibleMs = clock.Elapsed.TotalMilliseconds;
            visibleVia = via;
            return true;
        }
        private static bool Ready(BaseSceneController scene) => scene != null && !scene.Busy && scene.State != null &&
            scene.MapSession != null && scene.WorldCamera != null && scene.Hud != null && scene.Hud.VisualRoot.activeInHierarchy;
        private static IEnumerator Until(Func<bool> predicate, string stage)
        {
            var wait = Stopwatch.StartNew();
            while (!predicate())
            {
                if (wait.Elapsed.TotalSeconds >= 45) throw new TimeoutException("Timed out at " + stage + " / " + Scene?.Message);
                yield return null;
            }
        }
        private static string Digest(string path)
        {
            using var sha = SHA256.Create();
            using var file = File.OpenRead(path);
            return Convert.ToBase64String(sha.ComputeHash(file));
        }
        private void CheckLanding(BaseState expected, BaseState actual)
        {
            Require(expected.Campaign.Location == actual.Campaign.Location && expected.Campaign.CurrentMapId == actual.Campaign.CurrentMapId &&
                expected.Day == actual.Day && expected.Inventory.Coins == actual.Inventory.Coins && expected.Workers.Count == actual.Workers.Count &&
                Math.Abs(expected.PlayerX - landingX) < .001 && Math.Abs(expected.PlayerY - landingY) < .001, "Save state or landing mismatch");
        }
        private static void Require(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }
        private void Log(string line)
        {
            if (output != null) File.AppendAllText(output, line + Environment.NewLine);
            Debug.Log("[TransitionBenchmark] " + line);
        }
        private void Finish(Exception error)
        {
            measuring = false; target = null;
            RenderPipelineManager.beginCameraRendering -= CameraStarted;
            RenderPipelineManager.endCameraRendering -= CameraRendered;
            RenderPipelineManager.endContextRendering -= ContextRendered;
            Camera.onPostRender -= LegacyCameraRendered;
            if (keyboard != null) InputSystem.RemoveDevice(keyboard);
            try { Log(error == null ? "PASS" : "FAIL " + error); }
            finally
            {
#if UNITY_EDITOR
                UnityEditor.EditorApplication.Exit(error == null ? 0 : 2);
#else
                Application.Quit(error == null ? 0 : 2);
#endif
            }
        }
    }

}
#endif

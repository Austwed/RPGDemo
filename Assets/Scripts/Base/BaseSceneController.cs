using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using BaseConquest.Battle;
using BaseConquest.Presentation;
using UnityEngine.InputSystem;
using UnityEngine.EventSystems;
using UnityEngine.Networking;

namespace BaseConquest.Base
{
    public sealed partial class BaseSceneController : MonoBehaviour
    {
        public GameObject AuthoredHud;
        public GameObject[] AuthoredPages=Array.Empty<GameObject>();
        public GameObject PageTemplate(string key)=>AuthoredPages.Concat(BaseBattleGateway.SessionPages).FirstOrDefault(p=>p!=null&&p.GetComponent<BaseConquest.Presentation.UiPrefabBinding>()?.ResourceKey==key);
        public string RecommendedTemplate = "33_即时格战经营闭环.json";
        public string InitialTemplate = "08_入侵经营闭环.json";
        public bool LoadDesignPackage;
        [SerializeField,HideInInspector] private string realtimeProfile;
        public BaseState State { get; private set; }
        public BaseSaveStore SaveStore { get; private set; }
        public BaseHud Hud { get; private set; }
        internal LanDuelScreen DuelScreen { get; set; }
        public Camera WorldCamera { get; private set; }
        internal VillageOpeningAudio OpeningAudio { get; private set; }
        public bool Busy { get; private set; }
        public BasePoint EnvironmentPlayer=>combatController?.Realtime?.Model.Player!=null?new BasePoint(combatController.Realtime.Model.Player.Position.X,combatController.Realtime.Model.Player.Position.Z):new BasePoint(State.PlayerX,State.PlayerY);
        public bool EnvironmentPaused=>Busy||DeploymentWorldActive||combatController!=null&&(combatController.ExternalDialogue||combatController.Realtime?.Model.Paused!=false);
        private string environmentMapKey;
        private readonly Dictionary<string,EnvironmentAnimal[]> environmentAnimals=new Dictionary<string,EnvironmentAnimal[]>();
        public EnvironmentAnimal[] EnvironmentAnimals(BaseMapObject placement,EnvironmentDefinition d)
        {
            if(!environmentAnimals.TryGetValue(placement.Id,out var animals))
            {int count=d.Animal=="bird"?3:1;animals=new EnvironmentAnimal[count];for(int n=0;n<count;n++)animals[n]=new EnvironmentAnimal(d.Animal,d.Animal=="rabbit"?BaseEnvironment.FineCenter(new BasePoint(placement.X,placement.Y)):new BasePoint(placement.X+n*.23,placement.Y+n*.13),n*.13);environmentAnimals[placement.Id]=animals;}return animals;
        }
        private void BeginEnvironmentVisit()
        {
            if(State==null){environmentMapKey=null;environmentAnimals.Clear();return;}
            var mapId=BaseSpatial.Layout(State)?.SourceMapId;string key=(string.IsNullOrEmpty(mapId)?State.Campaign.Location:mapId)+(BaseDemoPrologue.Village(State)?":prologue":"");
            if(key!=environmentMapKey){environmentAnimals.Clear();environmentMapKey=key;}
        }
        public BaseCellSelection SelectedCell { get; private set; }
        public BaseCompanionMovement Companions { get; private set; }
        private Transform player;
        private BaseMotionAssist playerMotion=new BaseMotionAssist();
        private Transform selectionFrame;
        private readonly Dictionary<string, Transform> companionViews = new Dictionary<string, Transform>();
        private readonly Dictionary<string,AuthoredMapView> npcViews = new Dictionary<string,AuthoredMapView>();
        private readonly Dictionary<string, BaseAmbientWalk> ambientPeople = new Dictionary<string, BaseAmbientWalk>();
        private readonly Dictionary<string, BaseMapObject> ambientPositions = new Dictionary<string, BaseMapObject>();
        private string ambientMap;
        private static float? explorationViewSize;
        public Vector3 PlayerCameraTarget=>player.GetComponent<BattlePawn2D>().ExplorationHeadPoint;
        private sealed class Tap { public Vector2 Start; public float Travel; public bool Pinched; }
        private readonly Dictionary<int, Tap> touches = new Dictionary<int, Tap>();
        private Tap mouseTap;private float explorationPinchSpan;
        private readonly List<RaycastResult> uiHits = new List<RaycastResult>();
        private Font font;
        [NonSerialized] private int initializedSession = -1;
        private static int session;
        private bool loadingTemplate;
        public void RefreshConstructionMap(){regionView?.Refresh();}
        public void ToggleHomeTerrainTrial()
        {
            if(!RuntimeDeveloperOptions.Enabled||!BaseConquest.Presentation.HomeTerrainTrial.Eligible(State)||combatController!=null)return;
            bool next=!BaseConquest.Presentation.HomeTerrainTrial.Enabled;
            if(regionView.SetHomeTerrainTrial(next)){BaseConquest.Presentation.HomeTerrainTrial.Enabled=next;BaseConquest.Presentation.EnvironmentRoadEdges.ApplyTrialToScene(State);ReportMessage(next?"故乡地面候选已启用，待视觉验收":"已恢复原版故乡地面");}
            else ReportMessage("故乡候选贴图尚未准备好");
        }
        private void RefreshFarmVisuals()
        {
            if(regionView==null)return;
            foreach(var view in regionView.Root.GetComponentsInChildren<BaseConquest.Presentation.AuthoredMapView>(true))view.RefreshFarmAppearance(State);
        }
        public string Message { get; private set; } = "";
        internal void ReportMessage(string message, bool showFeedback = false)
        {
            Message = message;
            Hud?.RefreshSummary(showFeedback);
        }
        private Action preparationInventoryClosed;
        public void OpenPreparationInventory(Action closed)
        {var state=SaveStore.Load();if(!state.Campaign.HasActiveBattle||!BasePartyInventory.Peaceful(state))throw new InvalidOperationException("只能在布兵准备时打开背包");State=state;preparationInventoryClosed=closed;Hud.SetVisible(true);Hud.OpenBackpack();}
        internal void NotifyPanelClosed(){if(preparationInventoryClosed==null)return;var closed=preparationInventoryClosed;preparationInventoryClosed=null;Hud.SetVisible(false);closed();}
        public bool AtBase => (State?.Campaign?.Location??"base")=="base";
        public bool PlayerAway => AtBase && State?.Leaders.FirstOrDefault(l => l.Id == State.PlayerLeaderId)?.Away == true;
        private BaseState objectState;private int objectFrame=-1;private string objectRegion,objectMap;private BaseMapObject[] objectCache;
        public void InvalidateObjects(){objectCache=null;}
        public BaseMapObject[] MapObjects
        {
            get
            {
                string region=State?.Campaign.Location,map=State?.Campaign.CurrentMapId;
                if(objectCache==null||objectState!=State||objectFrame!=Time.frameCount||objectRegion!=region||objectMap!=map)
                {
                    objectState=State;objectFrame=Time.frameCount;objectRegion=region;objectMap=map;objectCache=State==null?BaseCampaign.MapObjects("base"):State.Campaign.Location=="base"&&!BaseDemo.Active(State)?BaseConstruction.MapObjects(State).Where(o=>o.Id!="warehouse").Concat(new[]{BasePartyInventory.WarehouseObject(State)}).ToArray():BaseConstruction.MapObjects(State);
                    if (ambientPositions.Count > 0)
                        for (int i = 0; i < objectCache.Length; i++)
                            if (ambientPositions.TryGetValue(objectCache[i].Id, out var person)) objectCache[i] = person;
                }
                return objectCache;
            }
        }
        private readonly Dictionary<string,BaseConquest.Presentation.AuthoredMapView> motionViews=new Dictionary<string,BaseConquest.Presentation.AuthoredMapView>();
        private BaseMapObject[] motionObjects=Array.Empty<BaseMapObject>();
        private BaseMapObject[] authoredMotionObjects=Array.Empty<BaseMapObject>();
        private Transform motionChecksRoot;
        private bool motionChecksEnabled;
        public BaseMapObject[] DisplayObjects
        {
            get
            {
                var objects=MapObjects;
                if(authoredMotionObjects.Length>0)objects=objects.Where(o=>!BaseDemoNpcs.IsStoryShell(o)).ToArray();
                return motionObjects.Length==0?objects:objects.Concat(motionObjects).ToArray();
            }
        }
        public void SetMotionCheck(string id,HeroPose pose){if(motionViews.TryGetValue(id,out var view))view.Preview(pose);}
        public BaseMapObject Nearby => State == null || PlayerAway ? null : NearbyBenchSeat(
            DisplayObjects.Where(o=>!State.Leaders.Any(l=>l.Id==o.Id)).Concat(Companions.Footprints)
                .Where(o=>SceneObjectVisible(o)&&BaseInteraction.CanReach(State,o))
                .OrderByDescending(o => o.Id == "farm" && o.Contains(State.PlayerX, State.PlayerY, .45))
                .ThenBy(o => o.Distance(State.PlayerX, State.PlayerY)).FirstOrDefault());
        public BaseMapObject PresentedObject(string id) => (State?.Leaders.Any(l=>l.Id==id)==true||BaseDemoPrologue.Followers(State).Any(l=>l.Id==id)) ? Companions.Agents.FirstOrDefault(a => a.Id == id && a.Visible)?.Footprint : DisplayObjects.FirstOrDefault(o => o.Id == id&&SceneObjectVisible(o));
        internal bool TryCharacterTop(string id, out Vector3 top)
        {
            var visual = companionViews.TryGetValue(id, out var companion) ? companion :
                npcViews.TryGetValue(id, out var npc) && npc != null ? npc.transform : null;
            var pawn = visual != null && visual.gameObject.activeInHierarchy ? visual.GetComponent<BattlePawn2D>() : null;
            top = pawn != null ? pawn.ExplorationVisualTop : default;
            return pawn != null;
        }
        public static BaseTemplateEntry[] TemplateEntries => BaseDevelopmentFiles.Templates(Path.Combine(Application.streamingAssetsPath,"BaseScenarios"),json=>JsonUtility.FromJson<BaseScenarioDocument>(json).Title,JsonUtility.FromJson<BaseTemplateList>(Resources.Load<TextAsset>("Presentation/BaseTemplates").text).Entries);
        public static string[] Templates => TemplateEntries.Select(e=>e.File).ToArray();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)] private static void NextSession() {session++;explorationViewSize=null;}
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)] private static void RebuildEntries()
        {
            foreach (var entry in FindObjectsByType<BaseSceneController>(FindObjectsInactive.Exclude))
                if (entry.initializedSession != session) entry.BeginSession();
        }
        private void Awake() { if (Application.isPlaying) BeginSession(); }
        private void OnEnable()
        {
            if(!Application.isPlaying)return;
            // Play中脚本重载不再调用Awake；从已保存进度重建新HUD和实际伙伴位置。
            if(RuntimeMissing()||initializedSession!=session)BeginSession();
            RuntimeDeveloperOptions.Changed+=OnMotionChecksModeChanged;
            OnMotionChecksModeChanged(RuntimeDeveloperOptions.Enabled);
        }
        private void OnDisable()
        {
            RuntimeDeveloperOptions.Changed-=OnMotionChecksModeChanged;
            HideDeveloperDiagnostics();
        }
        private bool RuntimeMissing() => Hud == null || Companions == null || SaveStore == null || WorldCamera == null || player == null || selectionFrame == null;
        private void BeginSession()
        {
            if (SceneUiSource != null) SceneUiSource.gameObject.SetActive(false);
            if(initializedSession>=0&&initializedSession!=session)realtimeProfile=null;
            RemoveDeveloperDiagnostics();
            regionView?.Dispose(); if(mapMaterial!=null)BattleBoardView.DestroyObject(mapMaterial); combatController=null;
            initializedSession = session; constructionOpen=false;constructionBrush.Cancel();constructionTouch=-1; StopAllCoroutines(); State = null; Busy = loadingTemplate = false;
            fallenGalan = null;
            ClearWorkerTravel();
            ambientMap = null;
            environmentMapKey=null;environmentAnimals.Clear();playerMotion=new BaseMotionAssist(); SelectedCell = null; Companions = new BaseCompanionMovement(); companionViews.Clear(); touches.Clear(); mouseTap = null;
            if (Application.isMobilePlatform) Screen.orientation = ScreenOrientation.LandscapeLeft;
            foreach (Transform child in transform.Cast<Transform>().ToArray())
            {
                if(child.name!="RuntimeGenerated"&&child.name!="EditableChessboard"&&child.name!="BaseHud"&&child.name!="DeveloperTools"&&child.name!="BattleOnCurrentMap")continue;
                child.gameObject.SetActive(false);Destroy(child.gameObject);
            }
            runtimeRoot=new GameObject("RuntimeGenerated").transform;runtimeRoot.SetParent(transform,false);SetRegionReady(false);
            OpeningAudio = null;
            if (font != null) Destroy(font);
            SaveStore = new BaseSaveStore(Path.Combine(BaseBattleGateway.PlayerDataDirectory, "BaseConquest", "base-save-v1.json"),
                d => JsonUtility.ToJson(d, true), json => JsonUtility.FromJson<BaseSaveDocument>(json));
            string packageProblem=null;
            if(LoadDesignPackage&&!BaseBattleGateway.DesignMode)
            {
                try
                {
                    const string file="Library/SlgDesignPackage.json";
                    if(new FileInfo(file).Length>3*1024*1024)throw new ArgumentException("运行组合超过3MB");
                    BaseBattleGateway.UseDesignPackage(JsonUtility.FromJson<BaseDesignPackage>(File.ReadAllText(file)));
                }
                catch(Exception error){packageProblem="设计组合未加载；未覆盖进度："+error.Message;}
            }
            bool freshEditorTest=false;
            BaseBattleGateway.UseOpeningProfile(string.IsNullOrEmpty(realtimeProfile)?RecommendedTemplate:realtimeProfile);realtimeProfile=BaseBattleGateway.OpeningProfile;
#if UNITY_EDITOR
            freshEditorTest=BaseBattleGateway.BeginEditorTest()&&!BaseBattleGateway.RealtimeCampaign;
            if(freshEditorTest&&!LoadDesignPackage&&!BaseBattleGateway.DesignMode)BaseDevelopmentFiles.ClearEditorDevelopment(Path.Combine(BaseBattleGateway.PlayerDataDirectory,"BaseConquest"));
#endif
            SaveStore=BaseBattleGateway.Store();packageProblem=packageProblem??BaseBattleGateway.Session().LoadError;
            bool hasSavedProgress = !freshEditorTest && SaveStore.Exists;
            bool showMainMenu = BaseBattleGateway.DemoMode && BaseBattleGateway.MainMenu;
            bool restoreOnStart = hasSavedProgress && !showMainMenu && packageProblem == null;
            bool restored = restoreOnStart && TryReadSavedState(false);
            if (hasSavedProgress && !restored)
            {
                // 主菜单和恢复页读取现有进度，归整失败时也保留能读取的状态。
                try { State = SaveStore.Load(); BaseCampaign.SeedContent(State); }
                catch (Exception e) { if (!restoreOnStart) Message = e.Message; }
            }
            font = Font.CreateDynamicFontFromOSFont(new[] { "Microsoft YaHei", "Noto Sans CJK SC", "Arial" }, 20);

            BuildWorld(); Hud = new BaseHud(this, runtimeRoot, font);
            developerPanel=GetComponent<DeveloperPanel>()??gameObject.AddComponent<DeveloperPanel>();developerPanel.Initialize(this,font);
            if(showMainMenu){Hud.OpenMainMenu();SetRegionReady(true);return;}
            if(packageProblem!=null){Message=packageProblem;Hud.OpenRecovery();SetRegionReady(true);return;}
            if (restoreOnStart)
            {
                Hud.ClearBattlePreparation();
                StopConstruction();
                CompleteSavedLoad(restored);
            }
            else StartCoroutine(ReadTemplate(RecommendedTemplate));
        }
        public BaseMapSession MapSession { get; private set; }
        private BattleBoardView regionView;
        private Transform explorationActors;
        private Material mapMaterial;
        public BaseConquest.Presentation.DemoSunlight Sunlight{get;private set;}
        private BattleSceneController combatController;
        private DeveloperPanel developerPanel;
        public Vector3 WorldPoint(double x, double y) => new Vector3(((float)x-regionView.Board.Width*.5f)*BattleBoardView.CellSize,regionView.ExplorationHeight(x,y),((float)y-regionView.Board.Height*.5f)*BattleBoardView.CellSize);
        private void BuildWorld()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            using var measuredBuild = TransitionBenchmark.MeasureWorld();
#endif
            // 布局随存档序列化：旧档读进来先做一次性迁移，否则改过的营地坐标在旧档里看不到。
            if(State!=null)BaseDemo.EnsureCampLayout(State);
            var ambientKey = State == null ? null : (State.Campaign.CurrentMapId ?? "") + "/" + State.Campaign.Location + "/" + BaseDemoPrologue.Village(State);
            if (ambientMap != ambientKey || State == null) ambientPeople.Clear();
            ambientMap = ambientKey;
            ambientPositions.Clear();
            InvalidateObjects();
            explorationSight=null;nextSightAt=0;BeginEnvironmentVisit();
            var board=State==null ? BattleBoard.CreateDefault(24,14) : BaseBattlefield.Board(State);
            MapSession=new BaseMapSession(board);
            var shader=Shader.Find("Universal Render Pipeline/Unlit");mapMaterial=new Material(shader);mapMaterial.SetColor("_BaseColor",Color.white);
            regionView = new BattleBoardView(board, runtimeRoot ?? transform, mapMaterial, mapMaterial, deferInitialRefresh: true)
                { FlatView = true, ObliqueBaseView = true };
            regionView.SetSurfaceRegion((State==null?null:BaseSpatial.Layout(State))?.SurfaceStyle=="dungeon"?"dungeon":State?.Campaign.Location??"base");
            regionView.SetCombatMode(false);
            bool exploring = State != null && State.Campaign.Location != "world";
            if (exploring)
            {
                bool camp = BaseDemo.Active(State) && State.Campaign.Location == "base" && !BaseDemoPrologue.Village(State);
                regionView.EnableExplorationFraming(camp);
            }
            regionView.UpdateCamera(0,0);
            if (exploring)
            {
                regionView.BuildExplorationScenery();
                regionView.SetViewSize(explorationViewSize ?? regionView.FullViewSize * BattleBoardView.DefaultExplorationZoom);
            }
            regionView.SetAuthoredBuildings(MapObjects.Where(o=>(!string.IsNullOrEmpty(o.AssetId)||!string.IsNullOrEmpty(o.SourceObjectId))&&o.Blocks).Select(o=>o.Id));
            WorldCamera=regionView.Camera; WorldCamera.gameObject.AddComponent<AudioListener>();
            if(BaseConquest.Presentation.HomeTerrainTrial.Enabled&&BaseConquest.Presentation.HomeTerrainTrial.Eligible(State))regionView.SetHomeTerrainTrial(true);
            regionView.CompleteInitialRefresh();
            // 地标与地形归同一世界根节点，进入战斗时保留，只有自由活动人物隐藏。
            var landmarks=new GameObject("RegionLandmarks").transform;landmarks.SetParent(regionView.Root.transform,false);
            explorationActors=new GameObject("FreeMovementActors").transform;explorationActors.SetParent(regionView.Root.transform,false);
            npcViews.Clear();motionChecksRoot=null;
            BuildBenchTargets();
            regionView.RoadEdges = BaseConquest.Presentation.EnvironmentRoadEdges.Build(State,regionView.Board,regionView.Root.transform,regionView.ObliqueExploration);
            var objects=MapObjects;
            foreach (var obj in objects)
            {
                if (!BaseAmbientWalk.IsAmbient(obj) || State == null || State.Leaders.Any(person => person.Id == obj.Id) ||
                    BaseDesignRoutes.ForObject(State, obj.Id) != null || BaseWorldJourney.EntryForObject(State, obj.Id) != null) continue;
                var origin = new BasePoint(obj.X, obj.Y);
                if (!ambientPeople.TryGetValue(obj.Id, out var walk) || walk.Home.Distance(origin) > .01)
                    ambientPeople[obj.Id] = walk = new BaseAmbientWalk(obj.Id, origin, origin);
                ambientPositions[obj.Id] = AtAmbientPosition(obj, walk.Position);
            }
            if (ambientPositions.Count > 0) { InvalidateObjects(); objects = MapObjects; }
            authoredMotionObjects=objects.Where(BaseDemoNpcs.IsStoryShell).ToArray();
            foreach(var obj in objects.Where(o=>!BaseDemoNpcs.IsStoryShell(o)&&!o.Id.StartsWith("partner")&&(!o.Blocks||!string.IsNullOrEmpty(o.SourceObjectId)||!string.IsNullOrEmpty(o.AssetId))))
            {
                if(!string.IsNullOrEmpty(obj.SourceObjectId)||!string.IsNullOrEmpty(obj.AssetId))
                {
                    var feet=WorldPoint(obj.X,obj.Y);if(!string.IsNullOrEmpty(obj.Shape))feet.y=WorldPoint(obj.Parts[0].X,obj.Parts[0].Y).y;
                    var authored=BaseConquest.Presentation.AuthoredMapView.Create(obj.Kind=="npc"?explorationActors:landmarks,obj,feet,mapMaterial,regionView.ObliqueExploration);
                    if(obj.Kind=="npc")npcViews[obj.Id]=authored;
                }
                else
                {
                    var notice=BaseConquest.Presentation.MapSurfaceView.Notice(landmarks,obj.Id,WorldPoint(obj.X,obj.Y),mapMaterial);
                    if(regionView.ObliqueExploration)notice.transform.localRotation=BattleBoardView.BaseArtTilt;
                }
            }
            RestoreFallenGalanView();
            BuildMotionChecks(RuntimeDeveloperOptions.Enabled);
            player=Person(explorationActors,"Player",new Vector2((float)(State?.PlayerX??12),(float)(State?.PlayerY??6.5)),new Color(.32f,.73f,.92f));
            if (State != null && !BaseDemoPrologue.Village(State) && BaseDemo.Active(State))
            {
                var facing = BaseInteraction.Facing;
                player.GetComponent<BattlePawn2D>().SyncExploration(WorldPoint(State.PlayerX,State.PlayerY),
                    new Color(.32f,.73f,.92f), facing:BaseConquest.Presentation.PlayerHeroFrames.FaceWalking(facing.X,facing.Y,BaseConquest.Presentation.HeroFacing.South));
            }
            selectionFrame=new GameObject("FreeCellSelection").transform;selectionFrame.SetParent(explorationActors,false);selectionFrame.gameObject.SetActive(false);
            Sunlight=BaseConquest.Presentation.DemoSunlight.Create(this,regionView.Root.transform);
        }
        private void OnMotionChecksModeChanged(bool enabled)
        {
            Hud?.RefreshDeveloperModeButton(enabled);
            if(explorationActors==null||motionChecksEnabled==enabled)return;
            int previousCount=motionObjects.Length;
            BuildMotionChecks(enabled);
            if(previousCount!=motionObjects.Length)Hud?.RefreshMapLabels();
        }
        private void BuildMotionChecks(bool enabled)
        {
            motionChecksEnabled=enabled;
            motionViews.Clear();
            if(motionChecksRoot!=null)
            {
                motionChecksRoot.gameObject.SetActive(false);
                BattleBoardView.DestroyObject(motionChecksRoot.gameObject);
                motionChecksRoot=null;
            }
            motionObjects=BaseDemoNpcs.MotionChecks(State,enabled);
            if(enabled&&authoredMotionObjects.Length>0)motionObjects=motionObjects.Concat(authoredMotionObjects).ToArray();
            if(motionObjects.Length==0)return;
            motionChecksRoot=new GameObject("DeveloperMotionChecks").transform;
            motionChecksRoot.SetParent(explorationActors,false);
            foreach(var obj in motionObjects)
            {
                var view=AuthoredMapView.Create(motionChecksRoot,obj,WorldPoint(obj.X,obj.Y),mapMaterial,regionView.ObliqueExploration);
                view.Preview(HeroPose.Idle);
                motionViews.Add(obj.Id,view);
            }
        }
        private Transform Person(Transform parent,string name,Vector2 feet,Color color)
        {
            var obj=new GameObject(name);obj.transform.SetParent(parent,false);var pawn=obj.AddComponent<BattlePawn2D>();
            pawn.Initialize(new BattleSoldier {Kind=SoldierKind.Hero,Stats=new SoldierStats {Health=100},Health=100},mapMaterial);
            if(name=="Player")pawn.EnablePlayerHero(BaseDemoIntro.PlayerArt(State));
            else pawn.EnableCompanion(BaseCommanderIdentity.Appearance(State?.Leaders.FirstOrDefault(l=>l.Id==name)??BaseDemoPrologue.Followers(State).FirstOrDefault(person=>person.Id==name)));
            if(regionView.ObliqueExploration)
            {
                pawn.UprightExploration=true;
                obj.transform.localRotation=BattleBoardView.BasePawnTilt;
                // Standing art keeps its screen height at the 60-degree camera pitch without leaning into neighboring terrain.
                obj.transform.localScale=new Vector3(1,1,2);
            }
            pawn.SyncExploration(WorldPoint(feet.x,feet.y),color);return obj.transform;
        }
        private void RebuildRegion()
        {
            ResetVolunteerRoutes();
            ClearBenchSeat();
            InvalidateObjects();
            RemoveDeveloperDiagnostics();
            if(regionView!=null) {regionView.Root.SetActive(false);regionView.Dispose();BattleBoardView.DestroyObject(regionView.Root);}
            if(mapMaterial!=null)BattleBoardView.DestroyObject(mapMaterial);companionViews.Clear();BuildWorld();
        }
        internal bool RegionPresented => runtimeRoot != null && runtimeRoot.gameObject.activeInHierarchy;
        private void SetRegionReady(bool ready)
        {
            // Async opening data and restored camera/actors must be ready before the first map frame.
            if(ready&&State!=null)RefreshSceneSight(true);if(runtimeRoot!=null)runtimeRoot.gameObject.SetActive(ready);
        }
        private void RebuildHud()
        {
            if(Hud!=null){Hud.DisposeAchievementNotice();var old=Hud.VisualRoot;old.SetActive(false);BattleBoardView.DestroyObject(old);}
            Hud=new BaseHud(this,runtimeRoot,font);Hud.RefreshSummary();
        }
        private void Update()
        {
            if (!Application.isPlaying) return;
            if (DuelScreen != null) return;
            if (RuntimeMissing()) { BeginSession(); return; }
            RefreshOpeningAudio();
            if (DeploymentWorldActive) { UpdateDeploymentWorld(); return; }
            if(combatController==null) regionView?.UpdateCamera(0,0);
            if(State!=null&&!Busy&&runtimeRoot!=null&&runtimeRoot.gameObject.activeInHierarchy)Hud.RefreshAchievementNotice(Time.unscaledDeltaTime);
            if(State!=null&&!Busy)
            {
                Hud.TickDialogue(Time.unscaledDeltaTime);
                if(combatController==null&&HandleJourneyMap())return;
                RefreshSceneSight();
                if (combatController?.CampaignMenuOpen != true)
                {
                    if (combatController?.ContinueFinishedSceneDialogue() == true || TickRuinsRetreatGuide()) return;
                    TickPracticeGuide();
                    TickDefenseIntel();
                }
            }
            if (State == null || combatController != null) {if(workerTravelMap!=null)ClearWorkerTravel();return;}
            if (Busy) return;
            TickWorkerTravel(Math.Min(Time.deltaTime,.05));
            TickVillageReturn();
            TickDemoIntro(Math.Min(Time.deltaTime,.05));if(!Hud.PanelOpen)TickRescueJourney(Math.Min(Time.deltaTime,.05));
            if(BaseDemoIntro.Daily(State)&&State.Demo.Intro.Step==0&&!BaseBattleGateway.MainMenu&&!Hud.PanelOpen){TickHeroChoice(Time.deltaTime);return;}
            AdvanceExplorationCompanions(Time.deltaTime);PlaceCompanions();
            UpdateExplorationInput();
            if (combatController == null && !BaseWorldJourney.OnWorld(State))
            {
                Hud.RefreshMovement();
                Hud.RefreshInspection(false);
            }
        }
        private void UpdateExplorationInput()
        {
            if(BaseBattleLoot.ShouldPresent(State)&&!Hud.PanelOpen){Hud.OpenLoot();return;}
            if(Hud.TryOpenHomeArrival())return;
            TickDemoFlow();TickAzhuInvitation();
            if(developerPanel?.BlocksGameInput==true)return;
            var keyboard = Keyboard.current;
            bool escape = keyboard?.escapeKey.wasPressedThisFrame == true;
            if(Hud.DialogueOpen&&keyboard!=null&&(keyboard.eKey.wasPressedThisFrame||keyboard.spaceKey.wasPressedThisFrame)){Hud.ContinueDialogue();return;}
            if(Hud.DialogueExitLocked&&escape)return;
            if (Hud.ResourcePopoverOpen && escape) { Hud.CloseResourcePopover(); escape = false; }
            if (escape && Hud.CloseInventoryItemMenu()) return;
            if (CampPlacementMode && escape)
            {
                if (Hud.PanelOpen) Hud.ClosePanel(); else RequestExitCampPlacement();
                return;
            }
            if(keyboard!=null&&HandlePanelShortcuts(keyboard.bKey.wasPressedThisFrame,keyboard.nKey.wasPressedThisFrame,keyboard.mKey.wasPressedThisFrame))return;
            if(ConstructionMode&&escape){StopConstruction();if(Hud.PanelOpen)Hud.ClosePanel();Message="已退出营造";Hud.RefreshSummary();return;}
            if (escape) { if (Hud.PanelOpen) Hud.ClosePanel(); else if (SelectedCell != null) ClearSelection(); else Hud.OpenMenu(); return; }
            if (Hud.PanelOpen) explorationPinchSpan = 0;
            if (Hud.PanelOpen || PlayerAway || State.Campaign.HasActiveBattle || State.Aftermath.GameOver || BaseBattleGateway.MainMenu) { touches.Clear(); mouseTap = null; return; }
            if (Hud.ResourcePopoverOpen) ReadResourceDismissPointer();
            // A UI click may close its modal before this Update sees the same input sample.
            if (Hud.PanelClosedFrame == Time.frameCount) { touches.Clear(); mouseTap = null; explorationPinchSpan = 0; }
            else if(ConstructionMode)UpdateConstructionPointer();else ReadPointer();
            if (CampPlacementMode) return;
            Vector2 input = Hud.Joystick.Direction;
            if (keyboard != null)
            {
                input += new Vector2((keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed ? 1 : 0) - (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed ? 1 : 0),
                    (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed ? 1 : 0) - (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed ? 1 : 0));
                if (keyboard.eKey.wasPressedThisFrame) { Interact(); return; }
            }
            MoveExploration(input,Math.Min(Time.deltaTime,.05));
        }
        internal void MoveExploration(Vector2 input,double seconds)
        {
            if(UpdateBenchSeat(input,seconds))return;
            input = Vector2.ClampMagnitude(input, 1);if(input.sqrMagnitude>.0001f){float magnitude=input.magnitude;float angle=Mathf.Round(Mathf.Atan2(input.y,input.x)/(Mathf.PI/4))*(Mathf.PI/4);input=new Vector2(Mathf.Cos(angle),Mathf.Sin(angle))*magnitude;}
            bool cardinal=input.sqrMagnitude>.0001f&&(Mathf.Abs(input.x)<.000001f||Mathf.Abs(input.y)<.000001f);
            double x = State.PlayerX, y = State.PlayerY;
            BaseSpatial.Move(State,ref x,ref y,input.x * 4.3 * Math.Min(seconds,0.05),input.y * 4.3 * Math.Min(seconds,0.05),Companions.PlayerObstacles(State),playerMotion);
            double movedX = x - State.PlayerX, movedY = y - State.PlayerY;
            BaseInteraction.RecordWalk(cardinal?input.x:movedX,cardinal?input.y:movedY);
            if(input.sqrMagnitude>.0001f)
            {
                double travelled = Math.Sqrt(movedX * movedX + movedY * movedY);
                BaseDemoIntro.Walk(State, travelled);
                OpeningAudio?.Footstep(travelled, regionView, x, y);
            }
            double previousX = State.PlayerX, previousY = State.PlayerY;
            State.PlayerX = x; State.PlayerY = y; PlacePlayer(cardinal?BaseConquest.Presentation.PlayerHeroFrames.FaceWalking(input.x,input.y,BaseConquest.Presentation.HeroFacing.South):null);
            CheckPrologueTrigger();
            if ((movedX != 0 || movedY != 0) && !Busy && !Hud.PanelOpen && !ConstructionMode &&
                BaseDemo.Active(State) && !BaseDemoPrologue.Village(State))
            {
                if (lastTouchedRoad != null && !lastTouchedRoad.Contains(x, y)) lastTouchedRoad = null;
                foreach (var gate in MapObjects)
                    if (gate.Id != lastTouchedRoad?.Id && CrossedRoadEdge(gate, previousX, previousY, x, y) && BaseInteraction.CanReach(State, gate))
                    {
                        lastTouchedRoad = gate;
                        Hud.OpenObject(gate.Id);
                        break;
                    }
            }
        }
        private BaseMapObject lastTouchedRoad;
        internal static bool CrossedRoadEdge(BaseMapObject gate, double fromX, double fromY, double toX, double toY)
        {
            if (gate == null || gate.VisualOnly || !gate.Contains(toX, toY)) return false;
            var inward = BaseDemoNpcs.RoadInward(gate.Id);
            double along = (toX - fromX) * inward.X + (toY - fromY) * inward.Y;
            if (along >= -1e-6) return false;
            double depth = inward.X != 0 ? gate.BoundsWidth : gate.BoundsHeight;
            double edge = Math.Min(.5, depth) - depth / 2;
            double after = (toX - gate.X) * inward.X + (toY - gate.Y) * inward.Y;
            return after <= edge;
        }

        private void RefreshOpeningAudio()
        {
            if (BaseBattleGateway.MainMenu || State == null || combatController != null) return;
            if (!BaseDemo.Active(State) || State.Aftermath.GameOver)
            {
                OpeningAudio?.Release(this);
                return;
            }
            OpeningAudio ??= VillageOpeningAudio.Shared();
            if (pursuitArrivalOpen && Hud?.DialogueOpen != true) pursuitArrivalOpen = false;
            var place = pursuitArrivalOpen ? VillageOpeningAudio.Place.Battle :
                State.Demo.Stage == BaseDemoStage.PrologueWounded ? VillageOpeningAudio.Place.Silent :
                BaseSceneRules.Dungeon(State) ? VillageOpeningAudio.Place.Dungeon :
                BaseDemoPrologue.Village(State) ? VillageOpeningAudio.Place.Village : VillageOpeningAudio.Place.Field;
            OpeningAudio.SetExploration(this, place);
        }
        private void AdvanceExplorationCompanions(double seconds)
        {
            if(BaseBattleGateway.MainMenu||State.Aftermath.GameOver)return;
            bool sceneOnly=Hud.PanelOpen;if(sceneOnly&&!(BaseDemoPrologue.Village(State)&&Hud.DialogueOpen))return;
            Companions.Update(State,seconds,developerPanel?.BlocksGameInput!=true&&!State.Campaign.HasActiveBattle&&!BaseBattleLoot.Pending(State),sceneOnly,ReadNpcGeometry);Companions.Capture(State);
            if (!sceneOnly && developerPanel?.BlocksGameInput != true && !State.Campaign.HasActiveBattle &&
                !BaseVillageReport.Gathering(State) && !BaseDemoPrologue.Evacuating(State) && !BaseDemoPrologue.Pursuit(State))
                WalkAmbientPeople(seconds);
        }
        private BaseMapObject[] ReadNpcGeometry() => MapObjects;
        private static BaseMapObject AtAmbientPosition(BaseMapObject obj, BasePoint position) =>
            new BaseMapObject(obj.Id, obj.Name, position.X, position.Y, obj.Width, obj.Height, obj.Blocks,
                obj.SourceObjectId, obj.AssetId, obj.Kind, obj.VisualOnly, obj.Shape, obj.Rotation);

        private void WalkAmbientPeople(double seconds)
        {
            if (ambientPositions.Count == 0) return;
            var board = regionView.Board;
            var playerFeet = new BasePoint(State.PlayerX, State.PlayerY);
            BaseMapObject[] geometry = null;
            foreach (var entry in ambientPeople)
            {
                if (!ambientPositions.TryGetValue(entry.Key, out var person) || !npcViews.TryGetValue(entry.Key, out var view)) continue;
                bool Walkable(double x, double y)
                {
                    geometry ??= MapObjects;
                    if (!BaseSpatial.Geometry(geometry, x, y, board.Width, board.Height) || !BaseSpatial.TerrainWalkable(board, x, y, geometry)) return false;
                    foreach (var other in geometry)
                        if (other.Kind == "npc" && other.Id != entry.Key && !BaseDemoNpcs.IsStoryShell(other) &&
                            other.Contains(x, y, BaseMap.Radius)) return false;
                    foreach (var agent in Companions.Agents)
                        if (agent.Visible && agent.Position.Distance(new BasePoint(x, y)) < .55) return false;
                    return true;
                }
                if (!entry.Value.Tick(seconds, playerFeet, Walkable, board.Width, board.Height)) continue;
                var position = entry.Value.Position;
                var moved = ambientPositions[entry.Key] = AtAmbientPosition(person, position);
                // MapObjects is the shared array for this frame. Update only this body so the next walker also sees it.
                for (int i = 0; i < geometry.Length; i++)
                    if (geometry[i].Id == entry.Key) { geometry[i] = moved; break; }
                view.MoveTo(WorldPoint(position.X, position.Y));
            }
        }
        private bool HandlePanelShortcuts(bool backpackPressed,bool questsPressed,bool mapPressed=false)
        {
            if((!backpackPressed&&!questsPressed&&!mapPressed)||Hud.DialogueOpen||Hud.DemoCompletionOpen)return false;
            if (CampPlacementMode) return true;
            if(BaseDemoIntro.Daily(State)&&State.Demo.Intro.Step==0)return true;
            var selected=EventSystem.current!=null?EventSystem.current.currentSelectedGameObject:null;
            // Unity destroyed objects pass ?. checks; use Unity's null comparison before GetComponent.
            if(selected!=null&&selected.GetComponent<UnityEngine.UI.InputField>()?.isFocused==true)return false;
            if(Hud.OfferSmithLessonOnTradeExit()||Hud.OfferRecruiterHideoutClueOnExit())return true;
            if(Hud.HandleBattlePreparationShortcut(backpackPressed))return true;
            if(!BaseBattleGateway.MainMenu&&!State.Aftermath.GameOver&&!State.Campaign.HasActiveBattle&&!BaseBattleLoot.Pending(State)&&mapPressed){StopConstruction();Hud.ToggleLandMap();return true;}
            if(!BaseBattleGateway.MainMenu&&!State.Aftermath.GameOver&&!State.Campaign.HasActiveBattle&&!BaseBattleLoot.Pending(State)&&questsPressed){StopConstruction();Hud.ToggleQuests();return true;}
            if(backpackPressed){StopConstruction();Hud.ToggleBackpack();return true;}
            return false;
        }
        private void PlacePlayer(BaseConquest.Presentation.HeroFacing? facing=null)
        {
            if (State == null) return;
            player.gameObject.SetActive(!PlayerAway);
            var feet=WorldPoint(State.PlayerX,State.PlayerY);
            var pawn=player.GetComponent<BattlePawn2D>();
            if(benchSeat!=null)
                pawn.SyncExploration(Vector3.Lerp(feet,benchSeatAnchor,Mathf.SmoothStep(0,1,benchSitProgress)),new Color(.32f,.73f,.92f),benchProgress:benchSitProgress,standingUp:leavingBench);
            else pawn.SyncExploration(feet,new Color(.32f,.73f,.92f),facing:facing);
            if(combatController==null&&!BaseWorldJourney.OnWorld(State)){regionView.Follow(PlayerCameraTarget);explorationViewSize=WorldCamera.orthographicSize;}
        }
        private void PlaceCompanions()
        {
            var eyePoint = WorldPoint(State.PlayerX, State.PlayerY);
            double attentionNow = Time.unscaledTimeAsDouble;
            bool reportGathering = BaseVillageReport.Gathering(State);
            var reportCenter = reportGathering ? BaseVillageReport.Center(State) : default;
            var reporter = reportGathering ? WorldPoint(reportCenter.X, reportCenter.Y) : eyePoint;
            foreach (var agent in Companions.Agents)
            {
                if (!companionViews.ContainsKey(agent.Id)) {if(!agent.Visible)continue;var created=Person(player.parent,agent.Id,new Vector2((float)agent.Position.X,(float)agent.Position.Y),new Color(.62f,.55f,.32f));companionViews[agent.Id]=created;Sunlight?.BindArt();}
                var view = companionViews[agent.Id]; view.gameObject.SetActive(agent.Visible);
                if(agent.SceneOnly)view.SetParent(regionView.Root.transform,false);
                view.GetComponent<BattlePawn2D>().SyncExploration(WorldPoint(agent.Position.X,agent.Position.Y),new Color(.62f,.55f,.32f),
                    reportGathering && agent.SceneOnly ? reporter : eyePoint, attentionNow, autonomous: reportGathering && agent.SceneOnly && !agent.Arrived);
            }
            bool choosingHero = BaseDemoIntro.Active(State) && State.Demo.Intro.Step == 0;
            foreach(var npc in npcViews)
                if (!choosingHero || npc.Key != "demo_captain")
                {
                    var focus = eyePoint;
                    if (reportGathering)
                    {
                        if (npc.Key == "prologue_report") focus = WorldPoint(reportCenter.X, reportCenter.Y - .9);
                        else if (BaseVillageReport.Find(State, npc.Key) != null) focus = reporter;
                    }
                    npc.Value.RefreshAttention(focus, attentionNow);
                }
            // 美术检查台也随主角转身，否则只能看到正面一列。
            foreach(var view in motionViews.Values)view.RefreshAttention(eyePoint,attentionNow);
        }
        private bool IsUiPoint(Vector2 position)
        {
            if (EventSystem.current == null) return false;
            uiHits.Clear(); EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = position }, uiHits);
            return uiHits.Count > 0;
        }
        private void ReadResourceDismissPointer()
        {
            bool pressedTouch = false;
            if (Touchscreen.current != null)
                foreach (var touch in Touchscreen.current.touches)
                {
                    if (!touch.press.wasPressedThisFrame) continue;
                    pressedTouch = true;
                    DismissResourcePopoverOutside(touch.position.ReadValue());
                }
            var mouse = Mouse.current;
            if (!pressedTouch && mouse?.leftButton.wasPressedThisFrame == true) DismissResourcePopoverOutside(mouse.position.ReadValue());
        }
        private void DismissResourcePopoverOutside(Vector2 position) =>
            Hud.DismissResourcePopoverOutside(IsUiPoint(position) ? uiHits[0].gameObject.transform : null);
        private void ReadPointer()
        {
            float threshold = Mathf.Max(10, Screen.height / 720f * 16); bool handledTouch = false;
            var screen = Touchscreen.current;
            if (screen != null)
            {
                UnityEngine.InputSystem.Controls.TouchControl first = null, second = null;
                foreach (var touch in screen.touches)
                {
                    bool pressed = touch.press.isPressed;
                    handledTouch |= pressed || touch.press.wasReleasedThisFrame;
                    int id = touch.touchId.ReadValue();
                    if (touch.press.wasPressedThisFrame)
                    {
                        Vector2 point = touch.position.ReadValue();
                        if (WorldCamera.pixelRect.Contains(point) && !IsUiPoint(point))
                            touches[id] = new Tap { Start = point };
                    }
                    if (!pressed || !touches.ContainsKey(id)) continue;
                    if (first == null) first = touch;
                    else if (second == null) second = touch;
                }
                if (second != null)
                {
                    Vector2 a = first.position.ReadValue(), b = second.position.ReadValue();
                    float span = Vector2.Distance(a, b);
                    touches[first.touchId.ReadValue()].Pinched = true;
                    touches[second.touchId.ReadValue()].Pinched = true;
                    bool onMap = WorldCamera.pixelRect.Contains(a) && WorldCamera.pixelRect.Contains(b) &&
                        !IsUiPoint(a) && !IsUiPoint(b);
                    if (onMap)
                    {
                        var center = (a + b) * .5f;
                        if (explorationPinchSpan > 0)
                        {
                            ZoomExplorationFactor(center, explorationPinchSpan / Mathf.Max(1, span));
                            if (CampPlacementMode) regionView.Pan(center - campPinchCenter);
                        }
                        campPinchCenter = center;
                    }
                    explorationPinchSpan = onMap ? span : 0;
                }
                else explorationPinchSpan = 0;
                foreach (var touch in screen.touches)
                {
                    int id = touch.touchId.ReadValue(); Vector2 point = touch.position.ReadValue();
                    if (touches.TryGetValue(id, out var tap))
                    {
                        tap.Travel = Mathf.Max(tap.Travel, Vector2.Distance(tap.Start, point));
                        if (touch.press.wasReleasedThisFrame)
                        {
                            touches.Remove(id);
                            if (!tap.Pinched && touch.phase.ReadValue() != UnityEngine.InputSystem.TouchPhase.Canceled &&
                                tap.Travel <= threshold && !IsUiPoint(point)) SelectScreenPoint(point);
                        }
                    }
                }
            }
            else explorationPinchSpan = 0;
            if (handledTouch) { mouseTap = null; return; }
            if (Mouse.current == null) return;
            Vector2 mouse = Mouse.current.position.ReadValue();
            float scroll=Mouse.current.scroll.ReadValue().y;
            if(Mathf.Abs(scroll)>.01f)ZoomExploration(mouse,scroll);
            if (Mouse.current.leftButton.wasPressedThisFrame) mouseTap = IsUiPoint(mouse) ? null : new Tap { Start = mouse };
            if (mouseTap != null) mouseTap.Travel = Mathf.Max(mouseTap.Travel, Vector2.Distance(mouseTap.Start, mouse));
            if (Mouse.current.leftButton.wasReleasedThisFrame)
            {
                if (mouseTap != null && mouseTap.Travel <= threshold && !IsUiPoint(mouse)) SelectScreenPoint(mouse);
                mouseTap = null;
            }
        }
        public bool ZoomExploration(Vector2 screen,float wheel)
        {
            if(State==null||combatController!=null||Hud.PanelOpen||BaseWorldJourney.OnWorld(State)||!WorldCamera.pixelRect.Contains(screen)||IsUiPoint(screen))return false;
            // Input System defaults to one unit per wheel tick, not Windows' native 120 units.
            float ticks=wheel;
            if(InputSystem.settings.scrollDeltaBehavior==InputSettings.ScrollDeltaBehavior.KeepPlatformSpecificInputRange&&(Application.platform==RuntimePlatform.WindowsEditor||Application.platform==RuntimePlatform.WindowsPlayer))ticks/=120f;
            return ZoomExplorationFactor(screen,Mathf.Pow(BattleBoardView.ExplorationWheelFactor,Mathf.Clamp(ticks,-4,4)));
        }
        public bool ZoomExplorationFactor(Vector2 screen,float factor)
        {
            if(State==null||combatController!=null||Hud.PanelOpen||BaseWorldJourney.OnWorld(State)||!WorldCamera.pixelRect.Contains(screen)||IsUiPoint(screen)||float.IsNaN(factor)||float.IsInfinity(factor)||factor<=0)return false;
            float before=WorldCamera.orthographicSize;
            if (CampPlacementMode)
            {
                regionView.ZoomAt(factor, screen);
                return Mathf.Abs(before - WorldCamera.orthographicSize) >= .0001f;
            }
            regionView.SetViewSize(before*factor);
            explorationViewSize=WorldCamera.orthographicSize;regionView.Follow(PlayerCameraTarget);
            if(Mathf.Abs(before-WorldCamera.orthographicSize)<.0001f)return false;
            if(BaseDemoIntro.Daily(State)&&State.Demo.Intro.Step==2)State.Demo.Intro.Zoomed=true;Hud.RefreshIntroObjective();return true;
        }
        private void SelectScreenPoint(Vector2 screen)
        {
            if (!WorldCamera.pixelRect.Contains(screen)) return;
            if(!regionView.Pick(screen,out var cell)) { ClearSelection(); return; }
            if (!ConstructionMode && PresentedObject("farm") is BaseMapObject field &&
                field.Parts.Any(part => part.Touches(cell.X, cell.Z)) && BaseInteraction.CanReach(State, field))
            {
                Hud.OpenObject(field.Id);
                return;
            }
            SelectCell(cell.X,cell.Z);
        }
        public void SelectCell(int x, int y)
        {
            if (State == null || Busy || PlayerAway || Hud.PanelOpen || !BaseMap.TryCell(x + 0.5, y + 0.5, out _, out _,BaseBattlefield.Board(State).Width,BaseBattlefield.Board(State).Height)) return;
            if(!SceneCellKnown(new GridPosition(x,y)))return;
            if(ConstructionMode){PaintConstructionCell(new GridPosition(x,y));return;}
            SelectedCell = new BaseCellSelection(x, y); regionView.SelectCell(new GridPosition(x,y)); Hud.NotifyInspection();
        }
        public void ClearSelection() { SelectedCell = null; regionView?.SelectCell(null); if (selectionFrame != null) selectionFrame.gameObject.SetActive(false); Hud.DismissInspection(); }
        public void Interact()
        {
            if (State?.Campaign.HasActiveBattle == true) { Hud.OpenMenu(); return; }
            if (Busy || Hud.PanelOpen || BaseBattleGateway.MainMenu) return;
            var nearby = Nearby;
            if (nearby?.Kind == BenchSeatKind) UseBenchSeat(nearby);
            else if (nearby != null) Hud.OpenObject(nearby.Id);
        }
        public bool Change(Action<BaseState> edit, string message) => CommitChange(edit,message,true);
        private bool CommitChange(Action<BaseState> edit, string message, bool fromInteraction)
        {
            if (State == null || Busy) return false;
            if(!string.IsNullOrEmpty(State.SceneBattleId)){ReportMessage("战斗中不能使用设施或修改经营");return false;}
            if(fromInteraction && Hud?.InteractionProblem is string problem && problem!=""){ReportMessage(problem);return false;}
            if (State.Aftermath.GameOver || BaseBattleGateway.MainMenu) { Message="游戏已结束，只能读档或回主菜单"; return false; }
            if (State.Campaign.HasActiveBattle&&(preparationInventoryClosed==null||!BasePartyInventory.Peaceful(State))) { Message = "先恢复并结束当前战斗"; Hud.RefreshSummary(); return false; }
            try
            {
                Companions.Capture(State); var next = State.Copy(); edit(next); BaseDemoIntro.CheckEquipment(next); BaseDevelopment.Allocate(next); SaveStore.Save(next,beforeChange:State);
                bool changedMap = State.Campaign.Location != next.Campaign.Location ||
                    State.Campaign.CurrentMapId != next.Campaign.CurrentMapId || BaseDemoPrologue.Village(State) != BaseDemoPrologue.Village(next);
                if(!deploymentChangedMap&&!changedMap)BaseBattlefield.KeepLiveBoard(next,MapSession.Board);
                State = next; if(fromInteraction)Message = message;
                // Landing coordinates and followers belong to the destination board.
                if (changedMap)
                {
                    BaseInteraction.Walk(next.RoadArrivalFacing);
                    lastTouchedRoad = null;
                    fallenGalan = null;
                    ClearWorkerTravel();
                    return true;
                }
                Companions.Synchronize(State);
                if (!DeploymentWorldActive) { PlaceCompanions(); PlacePlayer(); }
                RefreshFarmVisuals(); if (PlayerAway) ClearSelection(); Hud.RefreshSummary(); return true;
            }
            catch (BagFullException) { Hud.ShowBagFull(); return false; }
            catch (Exception e)
            {
                Debug.LogException(e, this);
                ReportMessage("操作未保存，原状态保留：" + e.Message);
                return false;
            }
        }
        public void Rest(int expectedDay)
        {
            if (State == null || State.Day != expectedDay || Busy) return;
            if(!BaseSettlements.AtHomeDoor(State)){ReportMessage("请在家边缘一格以内、与地基同层休息");return;}
            StartCoroutine(RestCampaign(expectedDay));
        }
        public void Save()
        {
            if (!Application.isPlaying || State == null || Busy || SaveStore == null || Hud == null) return;
            if (DuelScreen != null) return;
            if(combatController!=null) return;
            try { Companions.Capture(State); SaveStore.Save(State); }
            catch (Exception e) { Message = "保存失败：" + e.Message; }
            Hud.RefreshSummary();
        }
        public void LoadSaved(bool backup = false)
        {
            if (Busy) return;
            ClearJourneyMap();
            Hud?.ClearBattlePreparation();
            SetRegionReady(false);
            StopConstruction();
            string location = State?.Campaign.Location ?? "base";
            bool restored = TryReadSavedState(backup);
            CompleteSavedLoad(restored, location);
        }
        private bool TryReadSavedState(bool backup)
        {
            try
            {
                var next = backup ? SaveStore.LoadBackup() : SaveStore.Load();
                bool seed = !next.Campaign.ContentInitialized || next.Campaign.ThreatVersion == 0 && !next.Campaign.HasActiveBattle && !next.Aftermath.GameOver;
                BaseCampaign.SeedContent(next);
                BaseDevelopment.Allocate(next);
                BasePartyInventory.Normalize(next);
                if (seed || backup) SaveStore.Save(next);
                if (!RuntimeDeveloperOptions.Enabled) BaseDeveloperTools.ClearEffects(next);
                State = next;
                fallenGalan = null;
                return true;
            }
            catch (Exception e)
            {
                Message = "载入失败，当前状态保留：" + e.Message;
                return false;
            }
        }
        private void CompleteSavedLoad(bool restored, string previousLocation = null)
        {
            try
            {
                if (restored)
                {
                    ClearWorkerTravel();
                    if (previousLocation != null && previousLocation != State.Campaign.Location)
                    {
                        SceneLoadingCover.LoadExploration();
                        return;
                    }
                    Message = "已载入第" + State.Day + "天，未重复扣粮";
                    if (previousLocation != null) RebuildRegion();
                    Companions.Synchronize(State, true);
                    PlaceCompanions();
                    PlacePlayer();
                    ClearSelection();
                    Hud.ClosePanel();
                    if (State.Campaign.HasActiveBattle || State.Aftermath.GameOver || BaseBattleGateway.MainMenu) Hud.OpenMenu();
                    else if (BaseBattleGateway.ShowPrisoners)
                    {
                        BaseBattleGateway.ShowPrisoners = false;
                        Hud.OpenLoot();
                    }
                    else if ((State.Campaign.Location == "world" || State.Campaign.Location == "outpost") && !State.Muster.Parties.Any(p => p.Purpose == "expedition")) Hud.OpenDeparture("world");
                }
            }
            catch (Exception e) { Message = "载入失败，当前状态保留：" + e.Message; }
            if (State == null) Hud.OpenRecovery();
            Hud.RefreshSummary();
            if(BaseDemo.Active(State)&&State.Demo.Stage==BaseDemoStage.Waking&&!BaseBattleGateway.MainMenu&&BaseInteraction.At(State,"base","home"))Hud.OpenDemoStory();
            else if(BaseDemoPrologue.Village(State)&&!BaseBattleGateway.MainMenu)Hud.OpenDemoStory();
            else if(BaseDemoDefense.Won(State)&&State.Demo.Defense.BatuStoryDay==0&&!BaseBattleGateway.MainMenu&&!State.Campaign.HasActiveBattle)Hud.OpenDemoDefenseAftermath();
            else if(BaseDemoFinale.SecondRaidReportPending(State)&&!BaseBattleGateway.MainMenu&&!State.Campaign.HasActiveBattle)Hud.OpenSecondRaidVictoryReport();
            else if(BaseDemoRuins.Enabled(State)&&BaseDemoRuins.InRuins(State)&&!BaseBattleGateway.MainMenu&&!string.IsNullOrEmpty(State.Demo.RuinsRewards?.PendingBattleId))Hud.OpenRuinsReward();
            if(State?.Campaign.HasActiveBattle==true && BaseBattleGateway.RequestedId==State.Campaign.ActiveBattle.Id && !BaseBattleGateway.MainMenu) ResumeCampaign();
            SetRegionReady(true);
        }
        public void NewTemplate(string filename)
        {
            if(BaseBattleGateway.DemoMode&&filename!=BaseDemo.Profile&&filename!=BaseDemo.ReleaseProfile)return;
            if ((!Templates.Contains(filename)&&!(BaseBattleGateway.DemoMode&&(filename==BaseDemo.Profile||filename==BaseDemo.ReleaseProfile))) || loadingTemplate) return;
            StopConstruction();
            StartCoroutine(ReadTemplate(filename));
        }
        private IEnumerator ReadTemplate(string filename)
        {
            SetRegionReady(false);
            if(BaseBattleGateway.DesignMode||LoadDesignPackage)
            {
                try {if(!BaseBattleGateway.DesignMode)throw new InvalidOperationException("请先在工作台重新加载有效的浏览器运行组合");InstallOpening(BaseBattleGateway.DesignOpening);}
                catch(Exception error){Message="设计开局失败，原进度保留："+error.Message;if(State==null)Hud.OpenRecovery();}
                Hud.RefreshSummary();SetRegionReady(true);yield break;
            }
            Busy = loadingTemplate = true; Hud.RefreshSummary();
            if(BaseBattleGateway.DemoMode)filename=BaseDemo.BalanceTemplate;
            string path = Application.streamingAssetsPath.TrimEnd('/', '\\') + "/BaseScenarios/" + filename;
            string uri = path.Contains("://") ? path : new Uri(path).AbsoluteUri;
            using (var request = UnityWebRequest.Get(uri))
            {
                yield return request.SendWebRequest();
                bool demo = BaseBattleGateway.DemoMode;
                bool release = BaseBattleGateway.DemoReleaseMode;
                BaseStoryScenePackage story = null;
                // The legacy profile may optionally load an older package. A custom test requires a validated package below.
                if (request.result == UnityWebRequest.Result.Success && demo && !release)
                {
                    string storyPath = Application.streamingAssetsPath.TrimEnd('/', '\\') + "/StoryScene/story-scene-package.json";
                    bool embedded = storyPath.Contains("://");
                    if (embedded || File.Exists(storyPath))
                    {
                        using var storyRequest = UnityWebRequest.Get(embedded ? storyPath : new Uri(storyPath).AbsoluteUri);
                        yield return storyRequest.SendWebRequest();
                        if (storyRequest.result == UnityWebRequest.Result.Success)
                            story = ParseStoryScene(storyRequest.downloadHandler.text, storyPath);
                        else Debug.LogWarning("[StoryScene] 剧情场景包不可用，本次按内置布局开局：" + storyRequest.error);
                    }
                }
                try
                {
                    if (request.result != UnityWebRequest.Result.Success) throw new IOException(request.error);
#if UNITY_EDITOR
                    if (BaseBattleGateway.CustomMapTestMode)
                    {
                        story = ReadCustomMapTestPackage(UnityEditor.SessionState.GetString(DemoLauncher.CustomMapTestFileKey, ""));
                        if (story.Id != BaseBattleGateway.CustomMapTestPackageId)
                            throw new InvalidOperationException("地图包在启动后被替换，请从 Unity 菜单重新开始测试。");
                        Debug.Log("[StoryScene] 自定义地图测试使用正式剧情规则（" + story.Id.Substring(0, 8) + "）");
                    }
#endif
                    var opening = JsonUtility.FromJson<BaseScenarioDocument>(request.downloadHandler.text);
                    if (demo)
                    {
                        var config = DemoBalanceRuntime.Load();
                        opening = BaseDemo.CreateOpening(opening, request.downloadHandler.text, true, release, true, config.Rules, config.WorkbookSha256, story);
                    }
                    if (opening == null) throw new ArgumentException("模板为空");
                    InstallOpening(opening);
                }
                catch (Exception e)
                {
                    Message = (BaseBattleGateway.CustomMapTestMode ? "自定义地图开局失败：" : "开局失败，原进度保留：") + e.Message;
                    if (Application.isBatchMode) Debug.LogException(e);
                    if (State == null) Hud.OpenRecovery();
                }
            }
            Busy = loadingTemplate = false; Hud.RefreshSummary();SetRegionReady(true);
        }
#if UNITY_EDITOR
        public static BaseStoryScenePackage ReadCustomMapTestPackage(string path = null)
        {
            if (string.IsNullOrEmpty(path))
                path = Path.Combine(Application.streamingAssetsPath, "StoryScene", "story-scene-package.json");
            if (!File.Exists(path))
                throw new FileNotFoundException("找不到地图 JSON，请重新选择导出的文件。", path);
            var package = JsonUtility.FromJson<BaseStoryScenePackage>(File.ReadAllText(path));
            if (package == null || package.Version != 2)
                throw new ArgumentException("地图包不是当前版本，请从浏览器地图编辑器重新导出。");
            package.ValidateForPlay();
            return package;
        }
#endif
        // 编辑器导出的剧情场景包：存在即用于新开局，不存在则用内置布局。
        // 包体损坏时退回内置布局并留下警告，不阻断开局。
        private static BaseStoryScenePackage ParseStoryScene(string json, string path)
        {
            try
            {
                var package=JsonUtility.FromJson<BaseStoryScenePackage>(json);
                if(package==null)throw new ArgumentException("剧情场景包为空");
                package.Validate();
                Debug.Log("[StoryScene] 新开局使用编辑器设计："+path+"（"+package.Id.Substring(0,8)+"）");
                return package;
            }
            catch(Exception error)
            {
                Debug.LogWarning("[StoryScene] 剧情场景包不可用，本次按内置布局开局："+error.Message);
                return null;
            }
        }
        private void InstallOpening(BaseScenarioDocument opening)
        {
            pendingDesignExit=null;
            var next=opening.CreateState();BaseThreats.Enable(next);bool changedLocation=State!=null&&State.Campaign.Location!=next.Campaign.Location;BaseBattleGateway.Session().Start(next);State=next;
            BaseBattleGateway.MainMenu=false;Message="新开局 · "+opening.Title+"；首日口粮已扣一次";RebuildRegion();RebuildHud();Companions.Synchronize(State,true);PlaceCompanions();PlacePlayer();ClearSelection();Hud.ClosePanel();
            if(BaseDemo.Active(State)&&(State.Demo.Stage==BaseDemoStage.Waking||BaseDemoPrologue.Village(State)))Hud.OpenDemoStory();
            if(changedLocation)SceneLoadingCover.LoadExploration();
            else if((State.Campaign.Location=="world"||State.Campaign.Location=="outpost")&&!State.Muster.Parties.Any(p=>p.Purpose=="expedition"))Hud.OpenDeparture("world");
        }
        private void OnApplicationPause(bool paused) { if (paused) SaveAfterInputReset(); }
        private void OnApplicationFocus(bool focused) { if (!focused) SaveAfterInputReset(); }
        private void SaveAfterInputReset()
        {
            touches.Clear(); mouseTap = null; explorationPinchSpan = 0;
            constructionBrush.EndStroke(); constructionTouch = -1;
            Hud?.Joystick.ResetInput();
            Save();
        }
        private void OnApplicationQuit() => Save();
        private void OnDestroy() { if (OpeningAudio != null) OpeningAudio.Release(this); EndDeploymentWorld(false); RemoveDeveloperDiagnostics(); regionView?.Dispose(); BattleBoardView.DestroyObject(mapMaterial); BattleBoardView.DestroyObject(font); }
    }
}

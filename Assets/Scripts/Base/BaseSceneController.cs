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
        public Camera WorldCamera { get; private set; }
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
        private readonly List<BaseConquest.Presentation.AuthoredMapView> npcViews=new List<BaseConquest.Presentation.AuthoredMapView>();
        private static float? explorationViewSize;
        public Vector3 PlayerCameraTarget=>player.GetComponent<BattlePawn2D>().ExplorationHeadPoint;
        private sealed class Tap { public Vector2 Start; public float Travel; }
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
        internal void ReportMessage(string message){Message=message;Hud?.RefreshSummary();}
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
                {objectState=State;objectFrame=Time.frameCount;objectRegion=region;objectMap=map;objectCache=State==null?BaseCampaign.MapObjects("base"):State.Campaign.Location=="base"&&!BaseDemo.Active(State)?BaseConstruction.MapObjects(State).Where(o=>o.Id!="warehouse").Concat(new[]{BasePartyInventory.WarehouseObject(State)}).ToArray():BaseConstruction.MapObjects(State);}
                return objectCache;
            }
        }
        private readonly Dictionary<string,BaseConquest.Presentation.AuthoredMapView> motionViews=new Dictionary<string,BaseConquest.Presentation.AuthoredMapView>();
        private BaseMapObject[] motionObjects=Array.Empty<BaseMapObject>();
        public BaseMapObject[] DisplayObjects=>motionObjects.Length==0?MapObjects:MapObjects.Concat(motionObjects).ToArray();
        public void SetMotionCheck(string id,HeroPose pose){if(motionViews.TryGetValue(id,out var view))view.Preview(pose);}
        public BaseMapObject Nearby => State == null || PlayerAway ? null : DisplayObjects.Where(o=>!State.Leaders.Any(l=>l.Id==o.Id)).Concat(Companions.Footprints).Where(o=>SceneObjectVisible(o)&&BaseInteraction.CanReach(State,o)).OrderBy(o => o.Distance(State.PlayerX, State.PlayerY)).FirstOrDefault();
        public BaseMapObject PresentedObject(string id) => (State?.Leaders.Any(l=>l.Id==id)==true||BaseDemoPrologue.Followers(State).Any(l=>l.Id==id)) ? Companions.Agents.FirstOrDefault(a => a.Id == id && a.Visible)?.Footprint : DisplayObjects.FirstOrDefault(o => o.Id == id&&SceneObjectVisible(o));
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
            // Play中脚本重载不再调用Awake；从已保存进度重建新HUD和实际伙伴位置。
            if (Application.isPlaying && (RuntimeMissing() || initializedSession != session)) BeginSession();
        }
        private bool RuntimeMissing() => Hud == null || Companions == null || SaveStore == null || WorldCamera == null || player == null || selectionFrame == null;
        private void BeginSession()
        {
            if (SceneUiSource != null) SceneUiSource.gameObject.SetActive(false);
            if(initializedSession>=0&&initializedSession!=session)realtimeProfile=null;
            regionView?.Dispose(); if(mapMaterial!=null)BattleBoardView.DestroyObject(mapMaterial); combatController=null;
            initializedSession = session; constructionOpen=false;constructionBrush.Cancel();constructionTouch=-1; StopAllCoroutines(); State = null; Busy = loadingTemplate = false;
            environmentMapKey=null;environmentAnimals.Clear();playerMotion=new BaseMotionAssist(); SelectedCell = null; Companions = new BaseCompanionMovement(); companionViews.Clear(); touches.Clear(); mouseTap = null;
            if (Application.isMobilePlatform) Screen.orientation = ScreenOrientation.LandscapeLeft;
            foreach (Transform child in transform.Cast<Transform>().ToArray())
            {
                if(child.name!="RuntimeGenerated"&&child.name!="EditableChessboard"&&child.name!="BaseHud"&&child.name!="DeveloperTools"&&child.name!="BattleOnCurrentMap")continue;
                child.gameObject.SetActive(false);Destroy(child.gameObject);
            }
            runtimeRoot=new GameObject("RuntimeGenerated").transform;runtimeRoot.SetParent(transform,false);SetRegionReady(false);
            if (font != null) Destroy(font);
            SaveStore = new BaseSaveStore(Path.Combine(Application.persistentDataPath, "BaseConquest", "base-save-v1.json"),
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
            if(freshEditorTest&&!LoadDesignPackage&&!BaseBattleGateway.DesignMode)BaseDevelopmentFiles.ClearEditorDevelopment(Path.Combine(Application.persistentDataPath,"BaseConquest"));
#endif
            SaveStore=BaseBattleGateway.Store();packageProblem=packageProblem??BaseBattleGateway.Session().LoadError;
            if(freshEditorTest)State=null;
            if (!freshEditorTest&&SaveStore.Exists) try { State = SaveStore.Load(); BaseCampaign.SeedContent(State); } catch (Exception e) { Message = e.Message; }
            font = Font.CreateDynamicFontFromOSFont(new[] { "Microsoft YaHei", "Noto Sans CJK SC", "Arial" }, 20);

            BuildWorld(); Hud = new BaseHud(this, runtimeRoot, font);
            developerPanel=GetComponent<DeveloperPanel>()??gameObject.AddComponent<DeveloperPanel>();developerPanel.Initialize(this,font);
            if(packageProblem!=null){Message=packageProblem;Hud.OpenRecovery();SetRegionReady(true);return;}
            if (!freshEditorTest&&SaveStore.Exists) LoadSaved(); else StartCoroutine(ReadTemplate(RecommendedTemplate));
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
            // 布局随存档序列化：旧档读进来先做一次性迁移，否则改过的营地坐标在旧档里看不到。
            if(State!=null)BaseDemo.EnsureCampLayout(State);
            motionObjects=BaseDemoNpcs.MotionChecks(State);
            explorationSight=null;nextSightAt=0;BeginEnvironmentVisit();
            var board=State==null ? BattleBoard.CreateDefault(24,14) : BaseBattlefield.Board(State);
            MapSession=new BaseMapSession(board);
            var shader=Shader.Find("Universal Render Pipeline/Unlit");mapMaterial=new Material(shader);mapMaterial.SetColor("_BaseColor",Color.white);
            regionView=new BattleBoardView(board,runtimeRoot??transform,mapMaterial,mapMaterial) {FlatView=true,ObliqueBaseView=true};regionView.SetSurfaceRegion((State==null?null:BaseSpatial.Layout(State))?.SurfaceStyle=="dungeon"?"dungeon":State?.Campaign.Location??"base");regionView.SetCombatMode(false);
            if(State!=null&&!BaseWorldJourney.OnWorld(State))regionView.Zoom(.65f);
            regionView.UpdateCamera(0,0);
            if(State!=null&&!BaseWorldJourney.OnWorld(State))regionView.SetViewSize(regionView.Camera.orthographicSize);
            if(State!=null&&!BaseWorldJourney.OnWorld(State)&&explorationViewSize.HasValue)regionView.SetViewSize(Mathf.Clamp(explorationViewSize.Value,regionView.MinViewSize,regionView.FullViewSize));
            regionView.SetAuthoredBuildings(MapObjects.Where(o=>(!string.IsNullOrEmpty(o.AssetId)||!string.IsNullOrEmpty(o.SourceObjectId))&&o.Blocks).Select(o=>o.Id));
            WorldCamera=regionView.Camera; WorldCamera.gameObject.AddComponent<AudioListener>();
            if(BaseConquest.Presentation.HomeTerrainTrial.Enabled&&BaseConquest.Presentation.HomeTerrainTrial.Eligible(State))regionView.SetHomeTerrainTrial(true);
            // 地标与地形归同一世界根节点，进入战斗时保留，只有自由活动人物隐藏。
            var landmarks=new GameObject("RegionLandmarks").transform;landmarks.SetParent(regionView.Root.transform,false);
            explorationActors=new GameObject("FreeMovementActors").transform;explorationActors.SetParent(regionView.Root.transform,false);
            npcViews.Clear();motionViews.Clear();
            BaseConquest.Presentation.EnvironmentRoadEdges.Build(State,regionView.Board,regionView.Root.transform,regionView.ObliqueExploration);
            bool hasSwordProp=MapObjects.Any(o=>o.Id=="demo_sword_prop");
            foreach(var obj in MapObjects.Where(o=>!o.Id.StartsWith("partner")&&(!o.Blocks||!string.IsNullOrEmpty(o.SourceObjectId)||!string.IsNullOrEmpty(o.AssetId))))
            {
                if(hasSwordProp&&obj.Id=="demo_broken_sword")continue;
                if(!string.IsNullOrEmpty(obj.SourceObjectId)||!string.IsNullOrEmpty(obj.AssetId))
                {
                    var feet=WorldPoint(obj.X,obj.Y);if(!string.IsNullOrEmpty(obj.Shape))feet.y=WorldPoint(obj.Parts[0].X,obj.Parts[0].Y).y;
                    var authored=BaseConquest.Presentation.AuthoredMapView.Create(obj.Kind=="npc"?explorationActors:landmarks,obj,feet,mapMaterial,regionView.ObliqueExploration);
                    if(obj.Kind=="npc")npcViews.Add(authored);
                    // 剧情人偶与美术检查台同规格：原地待机、可被交互切动作，顺带把视图登记进 motionViews。
                    if(BaseDemoNpcs.IsStoryShell(obj)){authored.Preview(HeroPose.Idle);motionViews[obj.Id]=authored;}
                }
                else
                {
                    var notice=BaseConquest.Presentation.MapSurfaceView.Notice(landmarks,obj.Id,WorldPoint(obj.X,obj.Y),mapMaterial);
                    if(regionView.ObliqueExploration)notice.transform.localRotation=BattleBoardView.BaseArtTilt;
                }
            }
            foreach(var obj in motionObjects)
            {var view=BaseConquest.Presentation.AuthoredMapView.Create(explorationActors,obj,WorldPoint(obj.X,obj.Y),mapMaterial,regionView.ObliqueExploration);view.Preview(HeroPose.Idle);motionViews.Add(obj.Id,view);}
            player=Person(explorationActors,"Player",new Vector2(12,6.5f),new Color(.32f,.73f,.92f));
            selectionFrame=new GameObject("FreeCellSelection").transform;selectionFrame.SetParent(explorationActors,false);selectionFrame.gameObject.SetActive(false);
            Sunlight=BaseConquest.Presentation.DemoSunlight.Create(this,regionView.Root.transform);
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
            InvalidateObjects();
            if(regionView!=null) {regionView.Root.SetActive(false);regionView.Dispose();BattleBoardView.DestroyObject(regionView.Root);}
            if(mapMaterial!=null)BattleBoardView.DestroyObject(mapMaterial);companionViews.Clear();BuildWorld();
        }
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
            if (RuntimeMissing()) { BeginSession(); return; }
            if (DeploymentWorldActive) { UpdateDeploymentWorld(); return; }
            if(combatController==null) regionView?.UpdateCamera(HomeRebuildOverview ? 0 : RebuildPlacementMode ? Hud.HomeRebuildMapInset : ConstructionMode ? 190f/1280 : 0,0);
            if(State!=null&&!Busy&&runtimeRoot!=null&&runtimeRoot.gameObject.activeInHierarchy)Hud.RefreshAchievementNotice(Time.unscaledDeltaTime);
            if(State!=null&&!Busy)
            {
                Hud.TickDialogue(Time.unscaledDeltaTime);
                RefreshSceneSight();
                if (combatController?.CampaignMenuOpen != true)
                {
                    if (combatController?.ContinueFinishedSceneDialogue() == true || TickRuinsRetreatGuide()) return;
                    TickPracticeGuide();
                    TickDefenseIntel();
                }
            }
            if (State == null || Busy || combatController != null) {ClearWorkerTravel();return;}
            TickWorkerTravel(Math.Min(Time.deltaTime,.05));
            TickDemoIntro(Math.Min(Time.deltaTime,.05));if(!Hud.PanelOpen)TickRescueJourney(Math.Min(Time.deltaTime,.05));
            if(BaseWorldJourney.OnWorld(State))
            {
                if(journeyHud==null){var host=new GameObject("WorldJourney");host.transform.SetParent(runtimeRoot,false);journeyHud=host.AddComponent<BaseWorldJourneyHud>();journeyHud.Initialize(this,font);}
                return;
            }
            if(BaseDemoIntro.Daily(State)&&State.Demo.Intro.Step==0&&!BaseBattleGateway.MainMenu&&!Hud.PanelOpen){TickHeroChoice(Time.deltaTime);return;}
            AdvanceExplorationCompanions(Time.deltaTime);PlaceCompanions();Hud.RefreshMovement();Hud.RefreshInspection(false);
            if(BaseBattleLoot.Pending(State)&&!Hud.PanelOpen){Hud.OpenLoot();return;}
            TickDemoFlow();TickAzhuInvitation();
            if(developerPanel?.BlocksGameInput==true)return;
            var keyboard = Keyboard.current;
            bool escape = keyboard?.escapeKey.wasPressedThisFrame == true;
            if(Hud.DialogueOpen&&keyboard!=null&&(keyboard.eKey.wasPressedThisFrame||keyboard.spaceKey.wasPressedThisFrame)){Hud.ContinueDialogue();return;}
            if(Hud.DialogueExitLocked&&escape)return;
            if (Hud.ResourcePopoverOpen && escape) { Hud.CloseResourcePopover(); escape = false; }
            if (escape && Hud.CloseInventoryItemMenu()) return;
            if(keyboard!=null&&HandlePanelShortcuts(keyboard.bKey.wasPressedThisFrame,keyboard.nKey.wasPressedThisFrame,keyboard.mKey.wasPressedThisFrame))return;
            if(HomeRebuildOverview&&escape&&!Hud.PanelOpen){ReturnToHomeRebuild();return;}
            if(ConstructionMode&&escape){StopConstruction();if(Hud.PanelOpen)Hud.ClosePanel();Message="已退出营造";Hud.RefreshSummary();return;}
            if (escape) { if (Hud.PanelOpen) Hud.ClosePanel(); else if (SelectedCell != null) ClearSelection(); else Hud.OpenMenu(); return; }
            if (Hud.PanelOpen) explorationPinchSpan = 0;
            if (Hud.PanelOpen || PlayerAway || State.Campaign.HasActiveBattle || State.Aftermath.GameOver || BaseBattleGateway.MainMenu) { touches.Clear(); mouseTap = null; return; }
            if (Hud.ResourcePopoverOpen) ReadResourceDismissPointer();
            if(HomeRebuildOverview)return;
            if(ConstructionMode)UpdateConstructionPointer();else ReadPointer();
            Vector2 input = Hud.Joystick.Direction;
            if (keyboard != null)
            {
                input += new Vector2((keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed ? 1 : 0) - (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed ? 1 : 0),
                    (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed ? 1 : 0) - (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed ? 1 : 0));
                if (keyboard.eKey.wasPressedThisFrame&&!RebuildPlacementMode) { Interact(); return; }
            }
            MoveExploration(input,Math.Min(Time.deltaTime,.05));
        }
        internal void MoveExploration(Vector2 input,double seconds)
        {
            input = Vector2.ClampMagnitude(input, 1);if(input.sqrMagnitude>.0001f){float magnitude=input.magnitude;float angle=Mathf.Round(Mathf.Atan2(input.y,input.x)/(Mathf.PI/4))*(Mathf.PI/4);input=new Vector2(Mathf.Cos(angle),Mathf.Sin(angle))*magnitude;}
            double x = State.PlayerX, y = State.PlayerY;
            BaseSpatial.Move(State,ref x,ref y,input.x * 4.3 * Math.Min(seconds,0.05),input.y * 4.3 * Math.Min(seconds,0.05),Companions.PlayerObstacles(State),playerMotion);
            double movedX = x - State.PlayerX, movedY = y - State.PlayerY;
            BaseInteraction.RecordWalk(movedX, movedY);
            if(input.sqrMagnitude>.0001f)
            {
                BaseDemoIntro.Walk(State,Math.Sqrt(movedX*movedX+movedY*movedY));
            }
            State.PlayerX = x; State.PlayerY = y; PlacePlayer(); Hud.RefreshMovement();CheckPrologueTrigger();
        }
        private void AdvanceExplorationCompanions(double seconds)
        {
            if(BaseBattleGateway.MainMenu||State.Aftermath.GameOver)return;
            bool sceneOnly=Hud.PanelOpen;if(sceneOnly&&!(BaseDemoPrologue.Village(State)&&Hud.DialogueOpen))return;
            Companions.Update(State,seconds,developerPanel?.BlocksGameInput!=true&&!State.Campaign.HasActiveBattle&&!BaseBattleLoot.Pending(State),sceneOnly);Companions.Capture(State);
        }
        private bool HandlePanelShortcuts(bool backpackPressed,bool questsPressed,bool mapPressed=false)
        {
            if((!backpackPressed&&!questsPressed&&!mapPressed)||Hud.DialogueOpen)return false;
            if(BaseDemoIntro.Daily(State)&&State.Demo.Intro.Step==0)return true;
            var selected=EventSystem.current!=null?EventSystem.current.currentSelectedGameObject:null;
            // Unity destroyed objects pass ?. checks; use Unity's null comparison before GetComponent.
            if(selected!=null&&selected.GetComponent<UnityEngine.UI.InputField>()?.isFocused==true)return false;
            if(Hud.HandleBattlePreparationShortcut(backpackPressed))return true;
            if(!BaseBattleGateway.MainMenu&&!State.Aftermath.GameOver&&!State.Campaign.HasActiveBattle&&!BaseBattleLoot.Pending(State)&&mapPressed){StopConstruction();Hud.ToggleLandMap();return true;}
            if(!BaseBattleGateway.MainMenu&&!State.Aftermath.GameOver&&!State.Campaign.HasActiveBattle&&!BaseBattleLoot.Pending(State)&&questsPressed){StopConstruction();Hud.ToggleQuests();return true;}
            if(backpackPressed){StopConstruction();Hud.ToggleBackpack();return true;}
            return false;
        }
        private void PlacePlayer()
        {
            if (State == null) return;
            player.gameObject.SetActive(!PlayerAway);
            player.GetComponent<BattlePawn2D>().SyncExploration(WorldPoint(State.PlayerX,State.PlayerY),new Color(.32f,.73f,.92f));
            if(combatController==null&&!BaseWorldJourney.OnWorld(State)){regionView.Follow(PlayerCameraTarget);explorationViewSize=WorldCamera.orthographicSize;}
        }
        private void PlaceCompanions()
        {
            foreach (var agent in Companions.Agents)
            {
                if (!companionViews.ContainsKey(agent.Id)) {if(!agent.Visible)continue;var created=Person(player.parent,agent.Id,new Vector2((float)agent.Position.X,(float)agent.Position.Y),new Color(.62f,.55f,.32f));companionViews[agent.Id]=created;Sunlight?.BindArt();}
                var view = companionViews[agent.Id]; view.gameObject.SetActive(agent.Visible);
                if(agent.SceneOnly)view.SetParent(regionView.Root.transform,false);
                view.GetComponent<BattlePawn2D>().SyncExploration(WorldPoint(agent.Position.X,agent.Position.Y),new Color(.62f,.55f,.32f),WorldPoint(State.PlayerX,State.PlayerY),Time.unscaledTimeAsDouble);
            }
            var eyePoint=WorldPoint(State.PlayerX,State.PlayerY);var attentionNow=Time.unscaledTimeAsDouble;
            foreach(var npc in npcViews)npc.RefreshAttention(eyePoint,attentionNow);
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
            if(Touchscreen.current!=null)
            {
                var active=Touchscreen.current.touches.Where(t=>t.press.isPressed).Take(2).ToArray();
                if(active.Length==2)
                {
                    Vector2 a=active[0].position.ReadValue(),b=active[1].position.ReadValue();float span=Vector2.Distance(a,b);
                    if(explorationPinchSpan>0&&!IsUiPoint(a)&&!IsUiPoint(b))ZoomExplorationFactor((a+b)*.5f,explorationPinchSpan/Mathf.Max(1,span));
                    explorationPinchSpan=span;touches.Clear();mouseTap=null;return;
                }
            }
            explorationPinchSpan=0;
            if (Touchscreen.current != null)
                foreach (var touch in Touchscreen.current.touches)
                {
                    int id = touch.touchId.ReadValue(); Vector2 point = touch.position.ReadValue();
                    if (touch.press.wasPressedThisFrame) { handledTouch = true; if (!IsUiPoint(point)) touches[id] = new Tap { Start = point }; }
                    if (touches.TryGetValue(id, out var tap))
                    {
                        tap.Travel = Mathf.Max(tap.Travel, Vector2.Distance(tap.Start, point));
                        if (touch.press.wasReleasedThisFrame)
                        {
                            handledTouch = true; touches.Remove(id);
                            if (touch.phase.ReadValue() != UnityEngine.InputSystem.TouchPhase.Canceled && tap.Travel <= threshold && !IsUiPoint(point)) SelectScreenPoint(point);
                        }
                    }
                    handledTouch |= touch.press.isPressed;
                }
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
            return ZoomExplorationFactor(screen,Mathf.Pow(.8f,Mathf.Clamp(ticks,-4,4)));
        }
        public bool ZoomExplorationFactor(Vector2 screen,float factor)
        {
            if(State==null||combatController!=null||Hud.PanelOpen||BaseWorldJourney.OnWorld(State)||!WorldCamera.pixelRect.Contains(screen)||IsUiPoint(screen)||float.IsNaN(factor)||float.IsInfinity(factor)||factor<=0)return false;
            float before=WorldCamera.orthographicSize;regionView.SetViewSize(Mathf.Clamp(before*factor,regionView.MinViewSize,regionView.FullViewSize));
            explorationViewSize=WorldCamera.orthographicSize;regionView.Follow(PlayerCameraTarget);
            if(Mathf.Abs(before-WorldCamera.orthographicSize)<.0001f)return false;
            if(BaseDemoIntro.Daily(State)&&State.Demo.Intro.Step==2)State.Demo.Intro.Zoomed=true;Hud.RefreshIntroObjective();return true;
        }
        private void SelectScreenPoint(Vector2 screen)
        {
            if (!WorldCamera.pixelRect.Contains(screen)) return;
            if(regionView.Pick(screen,out var cell)) SelectCell(cell.X,cell.Z); else ClearSelection();
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
            if (Busy || Hud.PanelOpen || Nearby == null) return;
            Hud.OpenObject(Nearby.Id);
        }
        public bool Change(Action<BaseState> edit, string message)
        {
            if (State == null || Busy) return false;
            if(!string.IsNullOrEmpty(State.SceneBattleId)){ReportMessage("战斗中不能使用设施或修改经营");return false;}
            if(Hud?.InteractionProblem is string problem&&problem!=""){ReportMessage(problem);return false;}
            if (State.Aftermath.GameOver || BaseBattleGateway.MainMenu) { Message="游戏已结束，只能读档或回主菜单"; return false; }
            if (State.Campaign.HasActiveBattle&&(preparationInventoryClosed==null||!BasePartyInventory.Peaceful(State))) { Message = "先恢复并结束当前战斗"; Hud.RefreshSummary(); return false; }
            try
            {
                Companions.Capture(State); var next = State.Copy(); edit(next); BaseDemoIntro.CheckEquipment(next); BaseDevelopment.Allocate(next); BaseEconomy.Validate(next); SaveStore.Save(next);
                if(!deploymentChangedMap&&State.Campaign.Location==next.Campaign.Location&&State.Campaign.CurrentMapId==next.Campaign.CurrentMapId)BaseBattlefield.KeepLiveBoard(next,MapSession.Board);
                State = next; Message = message;
                Companions.Synchronize(State);
                if (!DeploymentWorldActive) { PlaceCompanions(); PlacePlayer(); }
                RefreshFarmVisuals(); if (PlayerAway) ClearSelection(); Hud.RefreshSummary(); return true;
            }
            catch (Exception e) { Message = "操作未保存，原状态保留：" + e.Message; Hud.RefreshSummary(); return false; }
        }
        public void Rest(int expectedDay)
        {
            if (State == null || State.Day != expectedDay || Busy) return;
            if(!BaseInteraction.At(State,"base","home")){ReportMessage("请在家边缘一格以内、与地基同层休息");return;}
            StartCoroutine(RestCampaign(expectedDay));
        }
        public void Save()
        {
            if (!Application.isPlaying || State == null || Busy || SaveStore == null || Hud == null) return;
            if(combatController!=null) return;
            try { Companions.Capture(State); SaveStore.Save(State); }
            catch (Exception e) { Message = "保存失败：" + e.Message; }
            Hud.RefreshSummary();
        }
        public void LoadSaved(bool backup = false)
        {
            if (Busy) return;
            Hud?.ClearBattlePreparation();
            SetRegionReady(false);
            StopConstruction();
            try { var next = backup ? SaveStore.LoadBackup() : SaveStore.Load(); bool seed = !next.Campaign.ContentInitialized || next.Campaign.ThreatVersion==0&&!next.Campaign.HasActiveBattle&&!next.Aftermath.GameOver; string location = State?.Campaign.Location ?? "base"; BaseCampaign.SeedContent(next); BaseDevelopment.Allocate(next); BasePartyInventory.Normalize(next); if (seed || backup) SaveStore.Save(next); if(!RuntimeDeveloperOptions.Enabled)BaseDeveloperTools.ClearEffects(next);State = next; if (location != State.Campaign.Location) { UnityEngine.SceneManagement.SceneManager.LoadScene(BaseBattleGateway.ExplorationScene); return; } Message = "已载入第" + State.Day + "天，未重复扣粮"; RebuildRegion(); Companions.Synchronize(State, true); PlaceCompanions(); PlacePlayer(); ClearSelection(); Hud.ClosePanel(); if (State.Campaign.HasActiveBattle || State.Aftermath.GameOver || BaseBattleGateway.MainMenu) Hud.OpenMenu(); else if(BaseBattleGateway.ShowPrisoners){BaseBattleGateway.ShowPrisoners=false;Hud.OpenLoot();} else if((State.Campaign.Location=="world"||State.Campaign.Location=="outpost")&&!State.Muster.Parties.Any(p=>p.Purpose=="expedition"))Hud.OpenDeparture("world"); }
            catch (Exception e) { Message = "载入失败，当前状态保留：" + e.Message; if (State == null) Hud.OpenRecovery(); }
            Hud.RefreshSummary();
            if(BaseDemo.Active(State)&&State.Demo.Stage==BaseDemoStage.Waking&&!BaseBattleGateway.MainMenu&&BaseInteraction.At(State,"base","home"))Hud.OpenDemoStory();
            else if(BaseDemoPrologue.Village(State)&&!BaseBattleGateway.MainMenu)Hud.OpenDemoStory();
            else if(BaseDemoDefense.Won(State)&&State.Demo.Defense.BatuStoryDay==0&&!BaseBattleGateway.MainMenu&&!State.Campaign.HasActiveBattle)Hud.OpenDemoDefenseAftermath();
            else if(BaseDemoRuins.Enabled(State)&&BaseDemoRuins.InRuins(State)&&!BaseBattleGateway.MainMenu&&(!string.IsNullOrEmpty(State.Demo.RuinsRewards?.PendingBattleId)||BaseRuinsRewards.FirstRewardPending(State)))Hud.OpenRuinsVictory();
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
                try
                {
                    if (request.result != UnityWebRequest.Result.Success) throw new IOException(request.error);
                    var opening = JsonUtility.FromJson<BaseScenarioDocument>(request.downloadHandler.text);
                    if (BaseBattleGateway.DemoMode)
                    {
                        var config = DemoBalanceRuntime.Load();
                        bool release = BaseBattleGateway.DemoReleaseMode;
                        // The release profile has fixed tactical snapshots; the authored story package belongs to the ordinary profile.
                        var story = release ? null : StorySceneFile();
                        opening = BaseDemo.CreateOpening(opening, request.downloadHandler.text, true, release, true, config.Rules, config.WorkbookSha256, story);
                    }
                    if (opening == null) throw new ArgumentException("模板为空");
                    InstallOpening(opening);
                }
                catch (Exception e)
                {
                    Message = "开局失败，原进度保留：" + e.Message;
                    if (Application.isBatchMode) Debug.LogException(e);
                    if (State == null) Hud.OpenRecovery();
                }
            }
            Busy = loadingTemplate = false; Hud.RefreshSummary();SetRegionReady(true);
        }
        // 编辑器导出的剧情场景包：存在即用于新开局，不存在则用内置布局。
        // 包体损坏时退回内置布局并留下警告，不阻断开局。
        public static BaseStoryScenePackage StorySceneFile()
        {
            if(!Application.isPlaying)return null;
            string path=Path.Combine(Application.streamingAssetsPath,"StoryScene","story-scene-package.json");
            if(!File.Exists(path))return null;
            try
            {
                var package=JsonUtility.FromJson<BaseStoryScenePackage>(File.ReadAllText(path));
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
            if(changedLocation)UnityEngine.SceneManagement.SceneManager.LoadScene(BaseBattleGateway.ExplorationScene);
            else if((State.Campaign.Location=="world"||State.Campaign.Location=="outpost")&&!State.Muster.Parties.Any(p=>p.Purpose=="expedition"))Hud.OpenDeparture("world");
        }
        private void OnApplicationPause(bool paused) { if (paused) Save(); }
        private void OnApplicationFocus(bool focused) { if (!focused) { touches.Clear(); mouseTap = null; constructionBrush.EndStroke();constructionTouch=-1;Hud?.Joystick.ResetInput(); Save(); } }
        private void OnApplicationQuit() => Save();
        private void OnDestroy() { EndDeploymentWorld(false); regionView?.Dispose(); BattleBoardView.DestroyObject(mapMaterial); BattleBoardView.DestroyObject(font); }
    }
}

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BaseConquest.Battle;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;

namespace BaseConquest.Base
{
    public sealed partial class BaseSceneController
    {
        public void BeginSceneEncounter(BaseCampaignSession encounter,Action<BaseConquest.Battle.RealtimeCampaignBattle> configure,Action<double> advance,Func<BaseConquest.Battle.RealtimeCampaignBattle,bool,bool,bool> finish,string objective)
        {
            if(State==null||combatController!=null||State.SceneBattleId!=encounter.Opening.Id&&!(State.Campaign.HasActiveBattle&&State.Campaign.ActiveBattle.Id==encounter.Opening.Id)||encounter.Opening.MapRegion!=State.Campaign.Location)throw new InvalidOperationException("同图遭遇身份、地图或交战状态不一致");
            BattleScenePlacement.Start(State,encounter);
            if(BaseSceneRules.Active(State))BattleScenePlacement.Snap(State.Copy(),new RealtimeCampaignBattle(encounter));
            MapSession.PrepareCombat(State,encounter.Battle.Board);MapSession.EnterBattle(encounter.Battle);
            Hud.ClosePanel();Hud.SetVisible(false);HideSceneParticipants(encounter);
            if(encounter.Opening.Id==BaseDemoIntro.BattleId)foreach(var prop in runtimeRoot.GetComponentsInChildren<BaseConquest.Presentation.AuthoredMapView>(true))if(prop.name.StartsWith(BaseDemoIntro.DummyId+" ·"))prop.gameObject.SetActive(false);
            var host=new GameObject("BattleOnCurrentMap");host.SetActive(false);host.transform.SetParent(transform,false);
            combatController=host.AddComponent<BattleSceneController>();combatController.UseCurrentMap(regionView,MapSession);
            combatController.ConfigureSceneEncounter(encounter,run=>{BattleScenePlacement.Snap(State,run);if(BaseSceneRules.Active(State))run.Battle.Vision.RememberTerrain(BattleSide.A,BaseSceneRules.Memory(State).Known);configure?.Invoke(run);run.RefreshDeveloperState();},advance,finish,objective);host.SetActive(true);
        }
        private string pendingDesignExit;
        public BaseState DepartureState(string location)
        {
            if(location!="world"||pendingWorldEntry==null||State.Adventure.Pauses.Count==0)return State;
            var preview=State.Copy();BaseAdventure.RecallPaused(preview);return preview;
        }
        public void TravelConnection(string objectId)
        {
            var worldEntry=BaseWorldJourney.EntryForObject(State,objectId);
            if(worldEntry!=null)
            {
                if(!BaseInteraction.CanReach(State,BaseSpatial.Layout(State).Objects.Single(obj=>obj.SourceObjectId==worldEntry.ObjectId).Object)){ReportMessage("请在世界出入口同层一格内交互");return;}
                pendingWorldEntry=worldEntry.Id;
                if(!State.Muster.Parties.Any(party=>party.Purpose=="expedition"&&party.Leaders.Any(leader=>leader.LeaderId==State.PlayerLeaderId)))
                {
                    if(objectId=="demo_march_exit"&&BaseDemoFinale.Enabled(State))Hud.OpenBattlePreparation(BasePrepRoute.Travel);
                    else Hud.OpenDeparture("world");
                    return;
                }
                CompleteTravel("world",null);return;
            }
            pendingWorldEntry=null;
            var link=BaseDesignRoutes.ForObject(State,objectId);
            if(link==null||BaseDesignRoutes.Reachable(State,BaseDesignRoutes.Target(State,link).Region,objectId)==null){ReportMessage("出口未绑定，或不在同层一格以内");return;}
            pendingDesignExit=objectId;string target=BaseDesignRoutes.Target(State,link).Region;
            if((target=="world"||target=="outpost"||target=="perimeter")&&!State.Muster.Parties.Any(p=>p.Purpose==(target=="perimeter"?"defense":"expedition"))) {Hud.OpenDeparture(target);return;}
            CompleteTravel(target,null);
        }
        public void Travel(string location)
        {
            if (State == null || Busy || State.Aftermath.GameOver) return;
            string demoProblem=BaseDemo.TravelProblem(State,location);if(demoProblem!=""){ReportMessage(demoProblem);return;}
            if(BaseDesignRoutes.Active(State))
            {
                var link=BaseDesignRoutes.Reachable(State,location);
                if(link==null){ReportMessage("请靠近已绑定该目标的出入口，不能从菜单远程旅行");return;}
                var source=BaseSpatial.Layout(State).Objects.Single(obj=>obj.SourceObjectId==link.SourceObjectId);TravelConnection(source.Id);return;
            }
            if(location=="outpost"){if(State.Campaign.Location=="world")StartCoroutine(EnterAttackRegionRoutine());return;}
            if (location == "world" && State.Campaign.Location!="outpost" || location == "perimeter" && State.Campaign.Events.Any(e => e.Kind == "defense" && e.Status == "pending" && e.AvailableDay <= State.Day)) { Hud.OpenDeparture(location); return; }
            CompleteTravel(location, null);
        }
        public void CompleteTravel(string location, BaseMusterPlan plan)
        {
            if(location=="base"&&State.Campaign.Location!="base"&&OfferFirstRaidIntel())return;
            if(BaseDemoDefense.Enabled(State)&&State.Campaign.Location=="perimeter"&&location=="perimeter"){PrepareDemoDefense(plan);return;}
            if(pendingWorldEntry!=null)
            {
                string entry=pendingWorldEntry;
                if(Change(state=>{BaseAdventure.RecallPaused(state);if(plan!=null&&!BaseMuster.Confirm(state,plan))throw new InvalidOperationException("出征队伍已变化");BaseWorldJourney.EnterWorld(state,entry);},"已进入世界地图，暂离成员自动随队")){pendingWorldEntry=null;ReloadTravelRegion();}return;
            }
            if (Change(s => {
                if(location=="base"&&(s.Campaign.Location=="world"||s.Campaign.Location=="outpost"||s.Campaign.Location=="perimeter")){string purpose=s.Campaign.Location=="perimeter"?"defense":"expedition";var returning=s.Muster.Parties.FirstOrDefault(p=>p.Purpose==purpose);if(returning!=null){foreach(var q in s.Supply.Quotas.Where(q=>q.PartyId==returning.Id).ToArray())if(!BaseSupply.Cancel(s,q.BattleId))throw new InvalidOperationException("战斗中不能返营");if(!BaseMuster.Return(s,returning.Id))throw new InvalidOperationException("未能返营");}}
                if (plan != null && !BaseMuster.Confirm(s, plan)) throw new InvalidOperationException("编制已变化，重新选择"); if ((BaseSceneInstances.Active(s)||s.Campaign.Location!=location)&&!BaseCampaign.Travel(s, location,pendingDesignExit)) throw new InvalidOperationException("目前不能前往该区域，请重新靠近原出口并确认连接"); }, "前往" + BaseCampaign.LocationName(location)))
            {pendingDesignExit=null;ReloadTravelRegion();}
        }
        // P32：领地地图的快捷传送。只切场景、落到出入口旁边，不消耗日程、不代替编队。
        public void MapTeleport(string key, string objectId)
        {
            if (State == null || Busy) return;
            if(State.Campaign.Location!="base"&&BaseLandMap.Nodes(State).Any(node=>node.Key==key&&node.Region=="base")&&OfferFirstRaidIntel())return;
            string title = BaseLandMap.Nodes(State).FirstOrDefault(node => node.Key == key)?.Title ?? "目标场景";
            if (!Change(next => { if (!BaseLandMap.Teleport(next, key, objectId)) throw new InvalidOperationException("这个场景现在过不去"); }, "已传送到" + title)) return;
            Hud.ClosePanel(); ReloadTravelRegion();
        }
        public void ResumeCampaign()
        {
            if (State == null || !State.Campaign.HasActiveBattle || Busy) return;
            if(combatController!=null) return;
            if(BaseSceneRules.Active(State))
            {
                var resumed=new BaseCampaignSession(State.Campaign.ActiveBattle);string eventId=resumed.Opening.EventId;BaseBattleGateway.RequestedId=null;
                if(eventId==BaseDemoRuins.EventId){beforeDemoRuins=State.Copy();BeginSceneEncounter(resumed,BaseDemoRuins.Configure,null,FinishDemoRuinsEncounter,"遗迹救助 · 继续交战");return;}
                if(eventId==BaseDemoDefense.EventId||eventId==BaseDemoDefense.SecondEventId){beforeDemoDefense=State.Copy();BeginSceneEncounter(resumed,BaseDemoDefense.Configure,null,FinishDemoDefenseEncounter,"营地防守 · 继续交战");return;}
                if(State.Campaign.EnemySites.Any(site=>site.Id==BaseDemoFinale.Camp&&site.AttackEventId==eventId)){beforeDemoAttack=State.Copy();BeginSceneEncounter(resumed,BaseDemoFinale.Configure,null,FinishDemoAttack,"攻占阻路营地 · 继续交战");return;}
            }
            Hud.ClosePanel();ClearSelection();Companions.Capture(State);SaveStore.Save(State);
            Hud.SetVisible(false);HideSceneParticipants(new BaseCampaignSession(State.Campaign.ActiveBattle));
            var host=new GameObject("BattleOnCurrentMap");host.SetActive(false);host.transform.SetParent(transform,false);
            combatController=host.AddComponent<BattleSceneController>();
            combatController.UseCurrentMap(regionView,MapSession);
            BaseBattleGateway.RequestedId = State.Campaign.ActiveBattle.Id;host.SetActive(true);
        }
        public void LaunchCampaign(string id, string mode, BaseMusterPlan plan)
        { if (State != null && !Busy) StartCoroutine(LaunchCampaignRoutine(id, mode, plan)); }
        public void ClaimOutpost(string id)
        {
            if(Change(s=>{if(!BaseCampaign.ClaimUnopposed(s,id))throw new InvalidOperationException("只有无指挥者的敌方据点可直接接管");},"已接管据点；已出发的入侵仍会抵达"))
            {RebuildRegion();PlaceCompanions();PlacePlayer();ClearSelection();Hud.OpenCampaignEvents("attack");}
        }
        private IEnumerator ReadBattle(string filename, Action<BattleScenarioDocument> receive)
        {
            string path = Application.streamingAssetsPath.TrimEnd('/', '\\') + "/BattleScenarios/" + filename;
            string uri = path.Contains("://") ? path : new Uri(path).AbsoluteUri;
            using (var request = UnityWebRequest.Get(uri))
            {
                yield return request.SendWebRequest();
                BattleScenarioDocument document = null;
                try
                {
                    if (request.result != UnityWebRequest.Result.Success) throw new IOException(request.error);
                    document = JsonUtility.FromJson<BattleScenarioDocument>(request.downloadHandler.text); BattleScenarioDocument.Validate(document);
                }
                catch (Exception e) { Message = "战场读取失败，进度未变：" + e.Message; }
                receive(document);
            }
        }
        private IEnumerator LaunchCampaignRoutine(string id, string mode, BaseMusterPlan plan)
        {
            var e = State.Campaign.Events.SingleOrDefault(x => x.Id == id); if (e == null) yield break;
            if(!BaseDesignRoutes.CanBegin(State,e.Kind)){ReportMessage("设计组合须在对应战场开战，不能从其他区域遥控");yield break;}
            Busy = true; Hud.RefreshSummary(); BattleScenarioDocument document = null;
            yield return ReadBattle(e.ScenarioFile, d => document = d); Busy = false;
            if (document != null && !SaveBeforeBattle()) yield break;
            string previousLocation=State.Campaign.Location;
            if (document != null && Change(s => { if(!BaseDesignRoutes.CanBegin(s,e.Kind))throw new InvalidOperationException("请在对应战场开战");if(plan!=null&&!BaseMuster.Confirm(s,plan))throw new InvalidOperationException("所选人员已变化，请重新选兵");if(s.Campaign.Location=="base") BaseCampaign.Travel(s,e.Kind=="defense" ? "perimeter" : "world"); BaseCampaign.Begin(s, id, document, mode,realtime:BaseBattleGateway.RealtimeCampaign); }, "实际人员与军需已锁定，在当前区域进入战斗"))
            { if(previousLocation!=State.Campaign.Location) {BaseBattleGateway.RequestedId=State.Campaign.ActiveBattle.Id;SceneManager.LoadScene(BaseBattleGateway.ExplorationScene);}else ResumeCampaign(); }
            else Hud.RefreshSummary();
        }
        private IEnumerator EnterAttackRegionRoutine()
        {
            var target=State.Campaign.Events.FirstOrDefault(e=>e.Kind=="attack");
            if(target==null){Message="此处尚无可进入的区域";Hud.RefreshSummary();yield break;}
            Busy=true;Hud.RefreshSummary();BattleScenarioDocument document=null;
            yield return ReadBattle(target.ScenarioFile,d=>document=d);Busy=false;
            if(document!=null&&Change(s=>{if(!BaseWorldMap.EnterAttackRegion(s,document))throw new InvalidOperationException("先选择主角出征队伍，再进入区域");},"已进入据点区域；接近据点状态点查看归属与敌情"))SceneManager.LoadScene(BaseBattleGateway.ExplorationScene);
            else Hud.RefreshSummary();
        }
        private IEnumerator RestCampaign(int expectedDay)
        {
            if (State.Campaign.HasActiveBattle) { Message = "先恢复并结束当前战斗"; Hud.RefreshSummary(); yield break; }
            // 2026-11-03 用户定：敌军抵营门时不许休息，改为陈安当面拦下的对白（不要求陈安站在旁边）。
            if (BaseDemoDefense.AnyDue(State)) { Hud.OpenDemoScoutWarning(); yield break; }
            Busy = true; Hud.RefreshSummary(); var documents = new Dictionary<string, BattleScenarioDocument>();
            foreach (string file in BaseCampaign.Due(State).Select(e => e.ScenarioFile).Distinct().ToArray())
            {
                BattleScenarioDocument document = null; yield return ReadBattle(file, d => document = d);
                if (document == null) { Busy = false; Hud.RefreshSummary(); yield break; } documents[file] = document;
            }
            Busy = false;
            if(documents.Count>0&&!SaveBeforeBattle())yield break;
            if (Change(s => { if (!BaseCampaign.Rest(s, expectedDay, file => documents[file],BaseBattleGateway.RealtimeCampaign)) throw new InvalidOperationException("日期已结算"); }, "本日已结算；到据点管理所查看日结报告")) Hud.ClosePanel();
        }
        // 副手迎战（P43）：后台模拟或进入战场观战，都先按副手编队锁人；两条路用同一套真实战斗与结算。
        public void LaunchDeputyDefense(string id, bool watch)
        {
            if (State != null && !Busy) StartCoroutine(LaunchDeputyDefenseRoutine(id, watch));
        }
        private IEnumerator LaunchDeputyDefenseRoutine(string id, bool watch)
        {
            string problem = BaseDeputyBattle.Problem(State, id);
            if (problem != "") { ReportMessage(problem); yield break; }
            var mission = State.Campaign.Events.FirstOrDefault(item => item.Id == id);
            if (mission == null) { ReportMessage("这次袭击已不存在"); yield break; }
            Busy = true; Hud.RefreshSummary(); BattleScenarioDocument document = null;
            yield return ReadBattle(mission.ScenarioFile, d => document = d); Busy = false;
            if (document == null) { Hud.RefreshSummary(); yield break; }
            if (!SaveBeforeBattle()) yield break;
            if (watch)
            {
                bool entered = Change(s =>
                {
                    if (BaseDeputyBattle.Problem(s, id) != "") throw new InvalidOperationException("副手或袭击状态已变化");
                    var plan = BaseDeputyBattle.Preview(s);
                    var opening = BaseCampaign.Begin(s, id, document, "full", plan, true);
                    opening.Deputy = true;
                }, "副手已接管防守：只能查看、暂停、调速和调整策略");
                if (entered) { BaseBattleGateway.RequestedId = State.Campaign.ActiveBattle.Id; ResumeCampaign(); }
                else Hud.RefreshSummary();
                yield break;
            }
            if (Change(s => BaseDeputyBattle.Resolve(s, id, document), "副手迎战结束，战果已入战报")) Hud.OpenCampaignEvents("defense");
            else Hud.RefreshSummary();
        }
        private bool SaveBeforeBattle() {try {BaseBattleGateway.Session().RememberBeforeBattle(State);return true;}catch(Exception e){Message="战前回退点未建立，尚未开战："+e.Message;Hud.RefreshSummary();return false;}}
    }
    public static class BaseBattleGateway
    {
        public static bool RealtimeCampaign=>true;
        public static string OpeningProfile {get;private set;}
        public static void UseOpeningProfile(string file)
        {if(Path.GetFileName(file)!=file)throw new ArgumentException("实验开局标识不能包含目录");if(string.IsNullOrEmpty(OpeningProfile))OpeningProfile=file;}
        public static string RequestedId;
        public static bool MainMenu;
        public static bool ShowPrisoners;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)] private static void Reset() { sessions.Clear();OpeningProfile=null;designPackage=null;workshopSession=false;editorTestSession=false;editorTestPending=true; SessionHud=null;SessionPages=Array.Empty<GameObject>(); RequestedId = null; MainMenu = ShowPrisoners = false; }
        public static GameObject SessionHud;
        public static GameObject[] SessionPages=Array.Empty<GameObject>();
        private static bool workshopSession;
        private static BaseDesignPackage designPackage;
        public static bool DesignMode=>designPackage!=null;
        public static bool DemoMode=>!DesignMode&&(OpeningProfile==BaseDemo.Profile||OpeningProfile==BaseDemo.ReleaseProfile);
        public static bool DemoReleaseMode=>!DesignMode&&OpeningProfile==BaseDemo.ReleaseProfile;
        public static void UseDemo(bool release=true){designPackage=null;workshopSession=false;OpeningProfile=release?BaseDemo.ReleaseProfile:BaseDemo.Profile;RequestedId=null;ShowPrisoners=false;}
        public static string ExplorationScene=>DesignMode ? "DesignPackage_Stage3" : "Base_Stage2";
        public static void UseDesignPackage(BaseDesignPackage package){package.CreateState();designPackage=package;}
        public static BaseScenarioDocument DesignOpening=>designPackage?.Scenario;
        private static bool editorTestSession,editorTestPending=true;
        public static bool BeginEditorTest()
        {bool first=editorTestPending;editorTestPending=false;if(!workshopSession)editorTestSession=true;return first;}
        public static void UseWorkshop()=>workshopSession=true;
        public static string SaveDirectory
        {
            get
            {
#if UNITY_EDITOR
                var checkPath = Environment.GetEnvironmentVariable("BASE_CONQUEST_UI_CHECK_SAVES");
                if (Application.isBatchMode && !string.IsNullOrEmpty(checkPath)) return checkPath;
#endif
                return Path.Combine(Application.persistentDataPath,"BaseConquest","RealtimeCampaignExperiment",DemoMode ? "Demo/"+OpeningProfile : DesignMode ? "DesignPackageExperiment/"+designPackage.Id : workshopSession ? "EditorExperiment" : editorTestSession ? "EditorDevelopment/"+Path.GetFileNameWithoutExtension(OpeningProfile??"33_即时格战经营闭环.json") : "");
            }
        }
        public static BaseSaveCatalog Catalog() => new BaseSaveCatalog(SaveDirectory, d=>JsonUtility.ToJson(d,true), json=>JsonUtility.FromJson<BaseSaveDocument>(json),DemoMode);
        private static readonly Dictionary<string,BasePlaySession> sessions=new Dictionary<string,BasePlaySession>();
        public static BasePlaySession Session()
        {
            string path=SaveDirectory;if(!sessions.TryGetValue(path,out var session))sessions.Add(path,session=new BasePlaySession(Catalog(),Path.Combine(path,"session-memory")));return session;
        }
        public static BaseSaveStore Store()=>Session().Store;
    }
}

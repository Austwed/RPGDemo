using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BaseConquest.Base;
using BaseConquest.Battle;
using BaseConquest.Presentation;
using BaseConquest.RealtimePrototype;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UI;
using Object = UnityEngine.Object;
using TouchPhase = UnityEngine.InputSystem.TouchPhase;

namespace BaseConquest.Editor
{
    // P8（W8）C4～C6 离屏截图验收：
    //   Unity.exe -batchmode -projectPath RPGDemo -executeMethod BaseConquest.Editor.UiSkinScreenshot.Run
    // 不加 -nographics、不加 -quit。使用本次检查专用进度验证正式 Demo 流程。
    // 产物：Logs/wood-ui/。
    // RunPlayerRouteViews 通过连续路线的存档补拍首防后对白、世界地图与返乡重建。
    // 截图沿用 UiInventoryCapture 的双遍合成：世界相机渲一张，屏幕空间 Overlay 的 Canvas 再单独渲到透明底按 alpha 叠回。
    public static partial class UiSkinScreenshot
    {
        private static readonly string OutDir = Path.Combine("Logs", "wood-ui");
        private static double deadline, stageAt, nextLog;
        private static int frameDeadline;
        private static string stage = "boot";
        private static BaseSceneController controller;
        private static BattleSceneController defenseBattle;
        private static GameObject defenseHud;
        private static Camera defenseCamera;
        private static Rect defenseViewport;
        private static int defenseSupplyStock;
        private static Camera ruinsEntranceCamera;
        private static string ruinsEntranceMap;
        private static bool failed, androidTouch, homeCopyOnly;
        private static bool campSceneryOnly;
        private static BaseDayPreview civilianDayPreview;
        private static AudioSource civilianMusic;
        private static readonly List<string> Log = new List<string>();

        private static BaseSceneController C => Object.FindAnyObjectByType<BaseSceneController>();
        private static BaseHud H => C?.Hud;

        public static void Run()
        {
            Directory.CreateDirectory(OutDir);
            if (!Application.isBatchMode) throw new InvalidOperationException("界面检查须在独立后台编辑器运行");
            Environment.SetEnvironmentVariable("BASE_CONQUEST_UI_CHECK_SAVES",
                Path.GetFullPath(Path.Combine(OutDir, "check-saves", Guid.NewGuid().ToString("N"))));
            UnityEditor.SceneManagement.EditorSceneManager.OpenScene(DemoStartup.Launcher);
            deadline = EditorApplication.timeSinceStartup + 480;
            EditorApplication.update += Tick;
            EditorApplication.EnterPlaymode();
        }

        private static bool worldJourneyOnly;
        private static int worldDepartureDay;
        public static void RunWorldJourney()
        {
            worldJourneyOnly = true;
            stage = "route-boot";
            Run();
        }

        public static void RunPlayerRouteViews()
        {
            stage = "route-boot";
            Run();
        }

        public static void RunTradeFlow()
        {
            stage = "trade-flow-boot";
            Run();
        }

        private static void CheckTradeFlow()
        {
            controller = C;
            if (controller?.State == null || controller.Busy || Object.FindAnyObjectByType<SceneLoadingCover>() != null) return;
            if (stage == "trade-flow-boot")
            {
                LoadRouteView("first-intel");
                stage = "trade-flow-intel";
                return;
            }
            if (!RouteViewReady(state => state.Campaign.Location == "town" && BaseRaidIntel.CanClaimFirst(state))) return;
            VerifyFirstIntelReopened();
            H.ClosePanel();
            Approach("town_peddler");
            H.OpenObject("town_peddler");
            FinishDialogue();
            Click(BaseConquest.Story.StoryText.Get("story.ui.trade.market_entry", "买卖东西"));
            var beforeSale = controller.State;
            Click("我想卖点东西");
            var saleShop = RequirePanel("TownTrade");
            int salePrice = BaseTown.TradePrice(beforeSale, "food", false);
            Click(BaseTown.Name("food") + " · " + salePrice + "币");
            if (!saleShop.Find("Details").GetComponentsInChildren<Text>().Any(t => t.text.Contains("行军路粮")))
                throw new InvalidOperationException("结算名称下没有商品说明");
            Click("确认卖出 · " + salePrice + "币");
            if (controller.State.Inventory.Food != beforeSale.Inventory.Food - 1 ||
                controller.State.Inventory.Coins != beforeSale.Inventory.Coins + salePrice)
                throw new InvalidOperationException("出售货架未按报价成交");
            CaptureViewports("ui-skin-merchant-sell");
            Click("我想买点东西");
            if (!RequirePanel("TownTrade").Find("OfferList").GetComponentsInChildren<Text>()
                .Any(t => t.text.StartsWith("锻造石 · ")))
                throw new InvalidOperationException("切回购买后没有恢复商品列表");
            CaptureViewports("ui-skin-merchant-buy");
            Debug.Log("[TradeFlow] PASS 免费情报、日历、商品详情和买卖切换");
            Finish(0, "OK");
        }

        private static IEnumerable<string> MenuLabels(Transform root) =>
            root.GetComponentsInChildren<Button>().Select(button => button.GetComponentInChildren<Text>()?.text ?? "");

        public static void RunMenuHelp()
        {
            stage = "menu-help-boot";
            Run();
        }

        public static void RunAndroidTouch()
        {
            androidTouch = true;
            Run();
        }

        public static void RunPursuitPresentation()
        {
            stage = "pursuit-boot";
            Run();
        }

        public static void RunOpeningCopy()
        {
            stage = "opening-copy";
            Run();
        }

        public static void RunDialogueSkip()
        {
            stage = "skip-boot";
            Run();
        }

        public static void RunFullCampInvitation()
        {
            stage = "full-camp-boot";
            Run();
        }

        public static void RunHomeCopy()
        {
            homeCopyOnly = true;
            stage = "route-boot";
            Run();
        }

        public static void RunMageRoster()
        {
            stage = "mage-roster-boot";
            Run();
        }

        private static void CheckMageRoster()
        {
            controller = C;
            if (controller?.State == null || controller.Busy || Object.FindAnyObjectByType<SceneLoadingCover>() != null) return;
            if (stage == "mage-roster-boot")
            {
                controller.LoadDemoStage("defense", "male");
                stage = "mage-roster-open";
                return;
            }
            if (stage == "mage-roster-camp")
            {
                if (controller.State.Campaign.Location != "base") return;
                foreach (string id in new[]{"farm","workshop"})
                {
                    var facility = OpenCivilianFacility(id);
                    var actions = facility.Find("Left/List").GetComponent<ScrollRect>().content;
                    foreach (var kind in BattleSimulation.TroopKinds)
                    {
                        var counter=actions.Find(BaseCampaign.ToBase(kind).ToString());
                        if (counter.Find("Minus").gameObject.activeSelf || counter.Find("Plus").gameObject.activeSelf ||
                            counter.Find("Input").GetComponent<InputField>()==null) throw new InvalidOperationException("设施缺少四兵种数字框");
                    }
                    var rows=facility.Find("Right/List").GetComponent<ScrollRect>().content;
                    if (RosterUnits(rows).Length!=BasePersonnel.Soldiers(controller.State).Count() ||
                        rows.GetComponentsInChildren<Text>().Any(t=>t.text.Contains("村民 ×"))) throw new InvalidOperationException("设施名册不同步");
                    foreach (var row in RosterUnits(rows)) VerifyRosterRow(row,false);
                    var workers=RosterUnits(rows);
                    var assigned=workers.FirstOrDefault(r=>r.GetComponent<Button>().interactable && r.Find("Status").GetComponent<Text>().text.StartsWith(BaseEconomy.FacilityName(id)));
                    if (assigned!=null && assigned.GetComponent<Image>().color.g<=assigned.GetComponent<Image>().color.r)
                        throw new InvalidOperationException("本设施士兵未显示淡绿");
                    var blocked=workers.First(r=>!r.GetComponent<Button>().interactable);
                    if (blocked.GetComponent<Image>().color.r<=blocked.GetComponent<Image>().color.g)
                        throw new InvalidOperationException("不合资格士兵未显示淡红");
                    Press(facility,"Right/Filters/Status");
                    Capture("p53-"+id+"-1920x1080",1920,1080);
                    Capture("p53-"+id+"-960x540",960,540);
                    Capture("p53-"+id+"-960x432",960,432);
                }
                H.ClosePanel();
                Approach("barracks"); H.OpenObject("barracks");
                var barracks = RequirePanel("FacilityPanel");
                var controls = barracks.Find("Left/List").GetComponent<ScrollRect>().content;
                if (controls.Find("EditFormation") != null)
                    throw new InvalidOperationException("兵营保留了旧点名入口");
                Press(barracks, "PortraitColumn/CaptainSeat0");
                int initial = controller.State.Muster.SlotTroops[0].Melee;
                int changed = initial < 30 ? initial + 1 : initial - 1;
                string step = initial < 30 ? "Plus" : "Minus";
                string undo = initial < 30 ? "Minus" : "Plus";
                var rosterNumbers = BasePersonnel.Soldiers(controller.State).Select(u => u.SoldierNumber).ToArray();
                Press(controls, "Melee/Input/" + step);
                if (controller.State.Muster.SlotTroops[0].Melee != initial ||
                    controls.Find("Melee/Input").GetComponent<InputField>().text != changed.ToString())
                    throw new InvalidOperationException("兵营数量调整应先留在草稿");
                Capture("p53-barracks-960x540",960,540);

                H.ClosePanel();
                ChooseRosterDraft("继续编辑");
                if (RequirePanel("FacilityPanel") != barracks || controller.State.Muster.SlotTroops[0].Melee != initial)
                    throw new InvalidOperationException("继续编辑提前提交了预设");
                H.ClosePanel();
                ChooseRosterDraft("放弃修改");
                if (H.PanelOpen || controller.State.Muster.SlotTroops[0].Melee != initial)
                    throw new InvalidOperationException("放弃后没有保留原编队");
                H.OpenObject("barracks");
                barracks = RequirePanel("FacilityPanel");
                controls = barracks.Find("Left/List").GetComponent<ScrollRect>().content;
                Press(controls, "Melee/Input/" + step);
                H.ClosePanel();
                ChooseRosterDraft("保存方案");
                if (H.PanelOpen || controller.State.Muster.SlotTroops[0].Melee != changed ||
                    !BasePersonnel.Soldiers(controller.State).Select(u => u.SoldierNumber).SequenceEqual(rosterNumbers))
                    throw new InvalidOperationException("保存兵营预设未更新位次人数，或改动了实际名册");
                H.OpenObject("barracks");
                controls = RequirePanel("FacilityPanel").Find("Left/List").GetComponent<ScrollRect>().content;
                Press(controls, "Melee/Input/" + undo);
                H.ClosePanel();
                ChooseRosterDraft("保存方案");
                Approach("home"); H.OpenHome();
                var tent = RequirePanel("FacilityPanel");
                var captains = RosterUnits(tent.Find("Right/List").GetComponent<ScrollRect>().content);
                var dashboard = tent.Find("Left/List").GetComponent<ScrollRect>().content;
                if (!captains.Any() || tent.Find("PortraitColumn/FacilityActionRow/farm")==null || !dashboard.GetComponentsInChildren<Text>().Any(t=>t.text.Contains("日结预览")) ||
                    SceneRect(tent.Find("Right"),tent).center.x >= SceneRect(tent.Find("PortraitColumn"),tent).center.x ||
                    SceneRect(tent.Find("PortraitColumn"),tent).center.x >= SceneRect(tent.Find("Left"),tent).center.x)
                    throw new InvalidOperationException("营帐三栏或固定日结预览缺失");
                if (captains.Length>1) ClickHudButton(captains[1]);
                if (RequirePanel("FacilityPanel")!=tent || !Object.ReferenceEquals(dashboard,tent.Find("Left/List").GetComponent<ScrollRect>().content))
                    throw new InvalidOperationException("切队长时重建了营帐");
                Capture("p53-tent-1920x1080",1920,1080);
                Capture("p53-tent-960x540",960,540);
                Capture("p53-tent-960x432",960,432);
                H.ClosePanel();
                var duel = H.OpenLanDuel();
                if (duel == null) throw new InvalidOperationException("营地模拟对抗入口未开放");
                typeof(LanDuelScreen).GetMethod("ShowPreparation",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(duel,null);
                var duelPage = duel.transform.Cast<Transform>().Last(t=>t.gameObject.activeSelf && t.name=="DuelPage");
                var duelList = duelPage.Find("Soldiers").GetComponent<ScrollRect>().content;
                if (RosterUnits(duelList).Length != BasePersonnel.Soldiers(controller.State).Count() ||
                    !Enumerable.Range(0,3).All(i=>duelPage.Find("Captain"+i)!=null) ||
                    !Enumerable.Range(0,4).All(i=>duelPage.Find("Filter"+i)!=null) ||
                    duelPage.Find("Captain0").GetComponentsInChildren<InputField>().Length!=4)
                    throw new InvalidOperationException("模拟对抗名册、筛选或数字编队布局缺失");
                var duelPick=RosterUnits(duelList).First(r=>r.GetComponent<Button>().interactable);
                var duelUnit=duel.Draft.Units.Single(u=>u.Id==duelPick.name);
                bool previous=duelUnit.Selected;
                ClickHudButton(duelPick);
                if (previous==duelUnit.Selected && duelUnit.TeamId==duel.Draft.PlayerId)
                    throw new InvalidOperationException("模拟对抗名册点选未调整当前队长编队");
                Capture("p53-lan-960x540",960,540);
                duel.Leave();
                Finish(0,"P53 名册、备战、设施和营帐三分辨率及联机备战面板通过"); return;
            }
            if (controller.State.Campaign.Location != "perimeter") return;
            if (stage == "mage-roster-open")
            {
                if (!controller.Change(state =>
                {
                    if (!state.Workers.Any(w=>w.Kind==BaseTroopKind.Mage && !w.VillageResident))
                        state.Workers.Add(new BaseWorkerBucket { Kind=BaseTroopKind.Mage,Count=3,Health=80,MaxHealth=80 });
                    state.Workers.Add(new BaseWorkerBucket { Kind=BaseTroopKind.Mage,Count=1,Health=80,MaxHealth=80,VillageResident=true });
                    state.Workers.Add(new BaseWorkerBucket { Kind=BaseTroopKind.Melee,Count=30,Health=100,MaxHealth=100 });
                    state.Workers.Add(new BaseWorkerBucket { Kind=BaseTroopKind.Ranged,Count=1,Health=10,MaxHealth=100 });
                    BaseDevelopment.SetPolicy(state,"food");
                },"")) throw new InvalidOperationException("无法载入实名整备检查人员 "+controller.Message);
                Approach("demo_defense_start");
                H.OpenObject("demo_defense_start");
                stage = "mage-roster-layout";
                frameDeadline = Time.frameCount + 3;
                return;
            }
            if (Time.frameCount < frameDeadline) return;
            var prep = (RectTransform)RequirePanel("BattlePreparation");
            var teams = (RectTransform)prep.Find("Teams");
            var cards = teams.Cast<RectTransform>().Where(c => c.gameObject.activeSelf && c.Find("Mage") != null).ToArray();
            if (cards.Length != 3) throw new InvalidOperationException("实际可见队长行数 " + cards.Length);
            Canvas.ForceUpdateCanvases();
            var kinds = new[] { "Melee", "Ranged", "Cavalry", "Mage" };
            foreach (var card in cards)
            {
                var groupBounds = SceneRect(card, teams);
                if (groupBounds.yMin < teams.rect.yMin - 1 || groupBounds.yMax > teams.rect.yMax + 1)
                    throw new InvalidOperationException("队长行超出分队区");
                float edge = SceneRect((RectTransform)card.Find("Select"), card).xMax;
                foreach (var kind in kinds)
                {
                    var bounds = SceneRect((RectTransform)card.Find(kind), card);
                    var header = SceneRect((RectTransform)prep.Find(kind + "Heading"), prep);
                    var column = SceneRect((RectTransform)card.Find(kind), prep);
                    if (bounds.xMin < edge - 1 || bounds.xMax > card.rect.xMax + 1 || Mathf.Abs(header.center.x - column.center.x) > 1)
                        throw new InvalidOperationException($"备战兵种列 {kind} 左 {bounds.xMin} 上一列右 {edge} 右 {bounds.xMax} 卡片右 {card.rect.xMax} 表头中心 {header.center.x} 内容中心 {column.center.x}");
                    edge = bounds.xMax;
                }
            }
            var candidates = (RectTransform)prep.Find("Candidates");
            float last = candidates.rect.xMin;
            foreach (var kind in new[] {"All","Melee","SortHealth","SortStatus"})
            {
                var rect = SceneRect((RectTransform)candidates.Find(kind), candidates);
                if (rect.xMin < last - 1 || rect.xMax > candidates.rect.xMax + 1)
                    throw new InvalidOperationException("名册筛选超出内容区：" + kind);
                last = rect.xMax;
            }
            for(int i=0;i<4;i++) Press(candidates, "Melee");
            var list = candidates.Find("List").GetComponent<ScrollRect>().content;
            var mages = RosterUnits(list);
            if (mages.Length != BasePersonnel.Soldiers(controller.State, SoldierKind.Mage).Count() || mages.Length == 0)
                throw new InvalidOperationException($"法师筛选人数与实际状态不同 当前 {mages.Length} 应有 {BasePersonnel.Soldiers(controller.State, SoldierKind.Mage).Count()} 筛选 {candidates.Find("Melee/Label").GetComponent<Text>().text}");
            foreach (var row in mages)
            {
                VerifyRosterRow(row, true);
                if (!row.Find("Label").GetComponent<Text>().text.Contains("法师"))
                    throw new InvalidOperationException("法师筛选混入其他兵种");
            }
            Capture("p52-preparation-mage-960x540", 960, 540);
            Press(candidates, "All");
            if (RosterUnits(list).Length != BasePersonnel.Soldiers(controller.State).Count())
                throw new InvalidOperationException("全部筛选未恢复完整名册");
            Capture("p52-preparation-all-960x540", 960, 540);
            if (failed) throw new InvalidOperationException("备战截图失败");
            var master=cards.First();
            var input=master.Find("Melee/Input").GetComponent<InputField>();
            input.onEndEdit.Invoke("0");
            if (input.text!="0") throw new InvalidOperationException("实名输入不能减至零");
            input.onEndEdit.Invoke("999");
            if (!int.TryParse(input.text,out int actual) || actual<1 || actual>BaseMuster.BattleSoldierLimit)
                throw new InvalidOperationException("出战人数上限错误");
            var remote=RosterUnits(list).First(row=>row.Find("Status").GetComponent<Text>().text.Contains("异地"));
            if(remote.GetComponent<Button>().interactable || remote.GetComponent<Image>().color.g>=remote.GetComponent<Image>().color.r)
                throw new InvalidOperationException("异地士兵不可参战状态或底色错误");
            Capture("p53-preparation-1920x1080",1920,1080);
            Capture("p53-preparation-960x540",960,540);
            Capture("p53-preparation-960x432",960,432);
            foreach(var card in cards)
                foreach(var kind in kinds)
                    if(card.Find(kind+"/Plus").gameObject.activeInHierarchy || card.Find(kind+"/Minus").gameObject.activeInHierarchy)
                        throw new InvalidOperationException("备战仍显示加减控件");
            H.ClosePanel();
            Approach("demo_defense_return");
            controller.TravelDemo("base");
            stage="mage-roster-camp";
        }

        public static void RunSoldierRoster()
        {
            stage = "soldiers-boot";
            Run();
        }

        public static void RunCivilianWork()
        {
            CivilianStage("civilian-boot");
            Run();
        }

        public static void RunRuinsExit()
        {
            stage = "ruins-exit-boot";
            Run();
        }

        public static void RunRoadContacts()
        {
            stage = "road-boot";
            Run();
        }

        private static void TickRoadContacts()
        {
            controller = C;
            if (controller?.State == null || controller.Busy || Object.FindAnyObjectByType<SceneLoadingCover>() != null) return;
            void LoadStage(string name, bool town)
            {
                var s = JsonUtility.FromJson<BaseState>(Resources.Load<TextAsset>("DemoStages/" + name + "-male").text);
                if (town)
                {
                    var home = BaseStageWalk.Door(s,"home");
                    s.PlayerX = home.X; s.PlayerY = home.Y;
                    BaseInteraction.Facing = default;
                    if (!BaseDemo.AcknowledgeWaking(s)) throw new InvalidOperationException("营地醒来失败");
                    var gate = BaseSpatial.Objects(s).Single(o => o.Id == "exit_east");
                    s.PlayerX = gate.X; s.PlayerY = gate.Y;
                    if (!BaseCampaign.Travel(s,"town")) throw new InvalidOperationException("前往灰岩镇失败");
                }
                BaseBattleGateway.MainMenu = false;
                controller.SaveStore.RestoreSession(s);
                controller.LoadSaved();
            }
            void WalkGate(string id, Vector2 direction)
            {
                H.ClosePanel();
                var s = controller.State;
                var gate = BaseSpatial.Objects(s).Single(o => o.Id == id);
                double half = direction.x != 0 ? gate.BoundsWidth / 2 : gate.BoundsHeight / 2;
                if (!controller.Change(n => {
                    n.PlayerX = gate.X - direction.x * (half + .6);
                    n.PlayerY = gate.Y - direction.y * (half + .6);
                    BaseAdventure.Arrive(n);
                }, "")) throw new InvalidOperationException(controller.Message);
                for (int i = 0; i < 35; i++)
                {
                    double distance = (controller.State.PlayerX - gate.X) * direction.x + (controller.State.PlayerY - gate.Y) * direction.y;
                    MoveSceneExplorer(direction);
                    if (controller.State.Campaign.Location != s.Campaign.Location)
                    {
                        if (distance + .216 < half - .5) throw new InvalidOperationException(id + " 提前触发");
                        return;
                    }
                }
                throw new InvalidOperationException(id + " 未能步行切图：" + controller.Message);
            }
            void Arrival(string id, int inward)
            {
                var s = controller.State;
                var gate = BaseSpatial.Objects(s).Single(o => o.Id == id);
                if ((s.PlayerX - gate.X) * inward <= gate.BoundsWidth / 2 || BaseInteraction.Facing.X != inward ||
                    !BaseSpatial.Walkable(s,s.PlayerX,s.PlayerY)) throw new InvalidOperationException(id + " 返回落点或朝向错误");
            }
            switch (stage)
            {
                case "road-boot": LoadStage("defense", false); stage = "road-field"; break;
                case "road-field":
                    if (controller.State.Campaign.Location != "perimeter") return;
                    H.ClosePanel();
                    var count = BaseSpatial.Layout(controller.State).Objects.Count(o => o.Kind == "decoration");
                    var board = BaseBattlefield.Board(controller.State);
                    if (count < 10 || !board.Cells.Any(c => c.Surface == GroundSurface.DirtPath) || !board.Cells.Any(c => c.Surface == GroundSurface.Grass))
                        throw new InvalidOperationException("迎敌阶段档布景恢复失败");
                    Capture("road-stage-field", 1366, 768);
                    if (!controller.Change(s => {
                        var edge = BaseSpatial.Nearest(s,new BasePoint(board.Width-.5,2));
                        s.PlayerX=edge.X; s.PlayerY=edge.Y;
                    }, "")) throw new InvalidOperationException(controller.Message);
                    controller.ZoomExplorationFactor(controller.WorldCamera.pixelRect.center,100);
                    Capture("road-stage-field-edge",1366,768);
                    Log.Add("迎敌测试档恢复 " + count + " 件装饰");
                    WalkGate("demo_defense_return",Vector2.right); Arrival("demo_defense_exit",1);
                    WalkGate("demo_defense_exit",Vector2.left); Arrival("demo_defense_return",-1);
                    LoadStage("camp", true); stage = "road-town"; break;
                case "road-town":
                    if (controller.State.Campaign.Location != "town") return;
                    WalkGate("demo_ruins_exit",Vector2.right); Arrival("demo_ruins_return",1);
                    WalkGate("demo_ruins_return",Vector2.left); Arrival("demo_ruins_exit",-1);
                    Finish(0,"迎敌战场与营地、灰岩镇与野外往返均由实际移动触发，四处落点与朝向通过"); break;
            }
        }

        public static void RunHomeFieldViews()
        {
            stage="home-field-boot";
            Run();
        }

        public static void RunCampScenery()
        {
            campSceneryOnly = true;
            Run();
        }

        private static void Tick()
        {
            try { TickStage(); }
            catch (Exception error) { Finish(2, stage + "：" + error); }
        }

        private static void TickStage()
        {
            if (failed || EditorApplication.timeSinceStartup > deadline) { Finish(2, "超时或前置失败：" + stage); return; }
            if (!EditorApplication.isPlaying) { Note("等待进入 playmode"); return; }
            if (stage.StartsWith("footstep-",StringComparison.Ordinal)){TickFootstepRecording();return;}
            if (stage.StartsWith("civilian-", StringComparison.Ordinal)) { TickCivilianWork(); return; }
            if (stage.StartsWith("trade-flow-", StringComparison.Ordinal)) { CheckTradeFlow(); return; }
            if (stage.StartsWith("ruins-exit-", StringComparison.Ordinal)) { TickRuinsExit(); return; }
            if(stage.StartsWith("road-",StringComparison.Ordinal)){TickRoadContacts();return;}
            if(stage.StartsWith("mage-roster-",StringComparison.Ordinal)){CheckMageRoster();return;}
            if(stage.StartsWith("home-field-",StringComparison.Ordinal)){TickHomeField();return;}
            switch (stage)
            {
                case "menu-help-boot":
                    controller = C;
                    if (controller?.State == null || controller.Busy || Object.FindAnyObjectByType<SceneLoadingCover>() != null) return;
                    controller.LoadDemoStage("defense", "female");
                    stage = "menu-help-perimeter";
                    break;
                case "menu-help-perimeter":
                    controller = C;
                    if (controller?.State?.Campaign.Location != "perimeter" || controller.Busy ||
                        Object.FindAnyObjectByType<SceneLoadingCover>() != null) return;
                    H.ClosePanel();
                    H.OpenMenu();
                    var menu = RequirePanel("CampMenu");
                    var actions = menu.Find("Actions/Viewport/Content/Columns");
                    var left = actions.Find("Gameplay");
                    var right = actions.Find("System");
                    if (!MenuLabels(left).Contains("日结报告") || !MenuLabels(left).Contains("任务 · N") ||
                        !MenuLabels(right).Take(4).SequenceEqual(new[] { "手动保存", "读取存档", "选项", "帮助" }) ||
                        MenuLabels(actions).Any(label => label.Contains("返回基地") || label == "战前布置工事"))
                        throw new InvalidOperationException("据点菜单分栏或移除旧入口失败");
                    Canvas.ForceUpdateCanvases();
                    if (((RectTransform)left).anchoredPosition.x >= ((RectTransform)right).anchoredPosition.x)
                        throw new InvalidOperationException("据点菜单两栏重叠或左右颠倒");
                    CaptureViewports("camp-menu-two-columns");
                    VerifyExplorationHelp();
                    VerifyResourceHud();
                    if (!controller.Change(s => s.Inventory.Equipment.Single(e => e.Product == "supply" && e.Level == 1).Count += 10,
                        "菜单帮助走查：备足军需"))
                        throw new InvalidOperationException(controller.Message);
                    Approach("demo_defense_start");
                    H.OpenObject("demo_defense_start");
                    var helpPrep = RequirePanel("BattlePreparation");
                    if (helpPrep.Find("Help") != null || helpPrep.Find("HelpOverlay") != null)
                        throw new InvalidOperationException("备战页仍包含旧的规则入口");
                    SetPreparationSupply(helpPrep, 1);
                    Press(helpPrep, "Continue");
                    var helpBattle = RequireBattleDraft();
                    VerifyBattleHelp(helpBattle);
                    var helpDraft = (BaseBattlePreparation)Field(H, "battleDraft");
                    PlaceByPointer(helpBattle, helpDraft, helpDraft.PlayerId);
                    VerifyDeploymentSpace(helpBattle);
                    FinishDialogue();
                    Click("拔剑迎战");
                    stage = "menu-help-battle";
                    break;
                case "menu-help-battle":
                    var liveHelpBattle = Field(controller, "combatController") as BattleSceneController;
                    if (liveHelpBattle == null || liveHelpBattle.PreviewSimulation.Phase != BattlePhase.Fighting) return;
                    VerifyBattleHelp(liveHelpBattle);
                    Finish(0, "菜单帮助走查通过");
                    break;
                case "soldiers-boot":
                    controller = C;
                    if (controller?.State == null || controller.Busy || Object.FindAnyObjectByType<SceneLoadingCover>() != null) return;
                    controller.LoadDemoStage("defense", "male");
                    stage = "soldiers-perimeter";
                    break;
                case "soldiers-perimeter":
                    controller = C;
                    if (controller?.State?.Campaign.Location != "perimeter" || controller.Busy ||
                        Object.FindAnyObjectByType<SceneLoadingCover>() != null) return;
                    Approach("demo_defense_return");
                    controller.TravelDemo("base");
                    stage = "soldiers-camp";
                    break;
                case "soldiers-camp":
                    controller = C;
                    if (controller?.State?.Campaign.Location != "base" || controller.Busy ||
                        Object.FindAnyObjectByType<SceneLoadingCover>() != null) return;
                    VerifySharedSoldiers();
                    Approach("exit_east");
                    controller.TravelDemo("town");
                    stage = "soldiers-recruit";
                    break;
                case "soldiers-recruit":
                    controller = C;
                    if (controller?.State?.Campaign.Location != "town" || controller.Busy ||
                        Object.FindAnyObjectByType<SceneLoadingCover>() != null) return;
                    Approach("town_recruit");
                    H.OpenObject("town_recruit");
                    if (H.DialogueOpen) { FinishDialogue(); Click("开始招募"); }
                    var recruit = RequirePanel("TownTrade");
                    if (recruit.GetComponentsInChildren<Button>().Any(button => button.GetComponentInChildren<Text>()?.text == "规则 i"))
                        throw new InvalidOperationException("招募面板仍包含旧的规则按钮");
                    var readOnly = recruit.Find("RightBody").GetComponent<ScrollRect>().content;
                    var soldiers = RosterUnits(readOnly);
                    if (soldiers.Length != 8 || soldiers.Any(r => r.GetComponent<Button>().interactable))
                        throw new InvalidOperationException("募兵处未显示全部八名现役士兵");
                    foreach (var item in soldiers) VerifyRosterRow(item, false);
                    Capture("roster-recruit");
                    if (failed) throw new InvalidOperationException("名册截图失败");
                    Log.Add("[PASS] 募兵处全营八名士兵只读，包括重伤者");
                    controller.LoadDemoStage("defense", "male");
                    stage = "soldiers-preparation";
                    break;
                case "soldiers-preparation":
                    controller = C;
                    if (controller?.State?.Campaign.Location != "perimeter" || controller.Busy ||
                        Object.FindAnyObjectByType<SceneLoadingCover>() != null) return;
                    Approach("demo_defense_start");
                    H.OpenObject("demo_defense_start");
                    var prep = RequirePanel("BattlePreparation");
                    var prepList = prep.Find("Candidates/List").GetComponent<ScrollRect>().content;
                    var picked = RosterUnits(prepList).First();
                    VerifyRosterRow(picked, true);
                    string before = picked.Find("Status").GetComponent<Text>().text;
                    ClickHudButton(picked);
                    if (picked == null || !picked.gameObject.activeInHierarchy || picked.Find("Status").GetComponent<Text>().text == before)
                        throw new InvalidOperationException("战前点选未就地更新该士兵的分队状态");
                    Press(picked,"Info");
                    if (!prep.Find("Candidates/Detail").gameObject.activeInHierarchy)
                        throw new InvalidOperationException("备战士兵详情没有打开");
                    Press(prep, "Candidates/Detail/Back");
                    Capture("roster-preparation", 854, 480);
                    if (failed) throw new InvalidOperationException("备战名册截图失败");
                    Log.Add("[PASS] 备战使用单行名册，点行分配士兵，详情按钮单独生效");
                    Finish(0, "通用士兵名册走查通过");
                    break;
                case "skip-boot":
                    controller = C;
                    if (controller?.State == null || controller.Busy || Object.FindAnyObjectByType<SceneLoadingCover>() != null) return;
                    controller.LoadDemoStage("camp", "male");
                    stage = "skip-waking";
                    break;
                case "skip-waking":
                    controller = C;
                    if (controller?.State?.Demo?.Stage != BaseDemoStage.Waking || controller.Busy || Object.FindAnyObjectByType<SceneLoadingCover>() != null) return;
                    Approach("home");
                    H.OpenDemoStory();
                    Click("跳过对话");
                    if (H.ContinueDialogue() || controller.State.Demo.Stage != BaseDemoStage.Waking)
                        throw new InvalidOperationException("跳过越过了苏醒时的第一句主角回应");
                    Click("嘶……这是哪儿？我睡了多久？");
                    FinishDialogue();
                    Click("起身，拜访领主");
                    Approach("demo_ahe");
                    H.OpenObject("demo_ahe");
                    Click("跳过对话");
                    FinishDialogue();
                    if (controller.State.Leaders.Any(person => person.Id == "partner_farm"))
                        throw new InvalidOperationException("快进对白擅自招募了阿禾");
                    Click("好，一起走");
                    FinishDialogue();
                    if (controller.State.Leaders.Count(person => person.Id == "partner_farm") != 1)
                        throw new InvalidOperationException("点选邀请后未加入营地");
                    Approach("exit_east");
                    controller.TravelDemo("town");
                    stage = "skip-town";
                    break;
                case "skip-town":
                    controller = C;
                    if (controller?.State?.Campaign.Location != "town" || controller.Busy || Object.FindAnyObjectByType<SceneLoadingCover>() != null) return;
                    ApproachMerchantAwayFromStall();
                    H.OpenObject("town_peddler");
                    Click("跳过对话");
                    if (!H.DialogueOpen) throw new InvalidOperationException("快进擅自打开了商店");
                    Click(BaseConquest.Story.StoryText.Get("story.ui.trade.market_entry", "买卖东西"));
                    VerifyPersonTabWidths(RequirePanel("TownTrade"));
                    H.ClosePanel();
                    Approach("town_quest");
                    H.OpenObject("town_quest");
                    Click("跳过对话");
                    if (H.ContinueDialogue() || controller.State.Demo.AcceptedDay != 0)
                        throw new InvalidOperationException("快进替玩家向领主提出了请求");
                    Click("多谢爵士接济。您可知道袭村的古裔从哪儿来？");
                    Click("跳过对话");
                    Click("我想在营地修工坊、招些人手。请您给道许可。");
                    var heldSkip = RequirePanel("NpcDialogue").Find("Replies").GetComponent<ScrollRect>().content.Find("SkipDialogue").GetComponent<Button>();
                    heldSkip.OnPointerDown(new PointerEventData(EventSystem.current) {button = PointerEventData.InputButton.Left});
                    FinishDialogue();
                    heldSkip.OnPointerUp(new PointerEventData(EventSystem.current) {button = PointerEventData.InputButton.Left});
                    heldSkip.OnPointerClick(new PointerEventData(EventSystem.current) {button = PointerEventData.InputButton.Left});
                    if (heldSkip.gameObject.activeInHierarchy) throw new InvalidOperationException("跳过按钮在答复出现后仍可接收松手事件");
                    if (H.ContinueDialogue() || controller.State.Demo.AcceptedDay != 0)
                        throw new InvalidOperationException("快进擅自接受领主委托");
                    Click("暂且告辞");
                    H.OpenObject("town_quest");
                    FinishDialogue();
                    Click("接受" + BaseScriptNames.Ruins + "调查");
                    if (controller.State.Demo.AcceptedDay == 0) throw new InvalidOperationException("选择接取后领主委托未生效");
                    VerifyRoyalSwordChoice();
                    H.OpenAzhuInvitation();
                    FinishDialogue();
                    Click("眼下有要事，改日再比");
                    Click("跳过对话");
                    if (H.DialogueOpen) throw new InvalidOperationException("无答复的独白跳过后未收起");
                    Finish(0, "苏醒与王室剑须主动发言，招募、交易及领主接取等待选择，纯叙述可跳过 PASS");
                    break;
                case "pursuit-boot":
                    controller = C;
                    if (controller?.State == null || controller.Busy) return;
                    LoadRouteView("pursuit-arrival");
                    break;
                case "route-pursuit-arrival":
                    if (!RouteViewReady(BaseDemoPrologue.Evacuating)) return;
                    if (!H.DialogueOpen)
                    {
                        H.ClosePanel();
                        MoveSceneExplorer(Vector2.zero);
                    }
                    if (!H.DialogueOpen || Field(Object.FindAnyObjectByType<VillageOpeningAudio>(), "place").ToString() != "Battle")
                        throw new InvalidOperationException("追兵出现时未播放战斗曲");
                    Capture("p53-pursuit-arrival", 960, 540);
                    LoadRouteView("pursuit-wounded");
                    break;
                case "route-pursuit-wounded":
                    if (!RouteViewReady(s => s.Demo.Stage == BaseDemoStage.PrologueWounded)) return;
                    if (!H.DialogueOpen) H.OpenDemoStory();
                    var background = ((RectTransform)Field(H, "modal")).GetComponent<Image>();
                    var choice = (RectTransform)Field(H, "heroChoiceLayer");
                    if (background.color != Color.black || (choice != null && choice.gameObject.activeInHierarchy))
                        throw new InvalidOperationException("剑伤旁白底色未完全遮住场景和选角控件");
                    Capture("p53-pursuit-blackout", 960, 540);
                    Finish(0, "追兵出现切曲、剑伤段落黑底 PASS");
                    break;
                case "opening-copy":
                    controller = C;
                    if (controller?.State == null || controller.Busy || Object.FindAnyObjectByType<SceneLoadingCover>() != null) return;
                    VerifyOpeningCopy();
                    VerifyRuinsConfrontationCopy();
                    Finish(0, "OK");
                    break;
                case "full-camp-boot":
                    controller = C;
                    if (controller?.State == null || controller.Busy || Object.FindAnyObjectByType<SceneLoadingCover>() != null) return;
                    LoadRouteView("full-camp-before-ahe");
                    break;
                case "route-full-camp-before-ahe":
                    if (!RouteViewReady(state => state.Campaign.Location == "base" && BaseRpg.Capacity(state) == 6 &&
                        state.Adventure.Members.Count == BaseAdventure.Capacity)) return;
                    VerifyFullCampInvitation();
                    Finish(0, "OK");
                    break;
                case "route-boot":
                    controller = C;
                    if (controller?.State == null || controller.Busy || Object.FindAnyObjectByType<SceneLoadingCover>() != null) return;
                    LoadRouteView(worldJourneyOnly ? "world" : homeCopyOnly ? "home-arrival" : "drill");
                    break;
                case "route-drill":
                    if (!RouteViewReady(state => BaseDemoPrologue.Village(state) && state.Demo.Intro.Step == 4)) return;
                    VerifyOpeningDrillControls();
                    LoadRouteView("first-intel");
                    break;
                case "route-first-intel":
                    if (!RouteViewReady(state => state.Campaign.Location == "town" && BaseRaidIntel.CanClaimFirst(state))) return;
                    VerifyFirstIntelReopened();
                    LoadRouteView("merchant");
                    break;
                case "route-merchant":
                    if (!RouteViewReady(state => state.Campaign.Location == "base" && BaseDemoDefense.Won(state))) return;
                    if(!H.DialogueOpen)return;
                    var merchantView=RequirePanel("NpcDialogue");
                    var merchantLine=merchantView.Find("DialogueText").GetComponent<Text>();
                    if(!merchantLine.text.Contains("来找我"))H.ContinueDialogue();
                    if(merchantView.Find("SpeakerName").GetComponent<Text>().text!=BaseScriptNames.Merchant ||
                        !merchantLine.text.Contains("黑鸦寨") || !merchantLine.text.Contains("钱") || !controller.State.Demo.Defense.MerchantDone)
                        throw new InvalidOperationException("第七天的行商未主动提示付费消息");
                    CaptureViewports("ui-player-route-merchant");
                    Log.Add("[PASS] 第七日老莫主动报信，交易留在镇上");
                    H.ClosePanel();
                    LoadRouteView("second-occupation");
                    break;
                case "route-second-occupation":
                    if (!RouteViewReady(state => BaseDemoFinale.SecondRaidReportPending(state) && H.DialogueOpen)) return;
                    var report = RequirePanel("NpcDialogue");
                    var reportLine = report.Find("DialogueText").GetComponent<Text>();
                    if (!reportLine.text.EndsWith("路通了。")) H.ContinueDialogue();
                    if (report.Find("SpeakerName").GetComponent<Text>().text != "陈安" ||
                        !reportLine.text.Contains("匪兵都跑散了") || !reportLine.text.Contains("白桦村"))
                        throw new InvalidOperationException("陈安的第一句缺少寨中残兵或返乡通路");
                    VerifyUnreadReplies(report.Find("Replies").GetComponentsInChildren<Button>(), 1);
                    if (!H.ContinueDialogue() || !H.ContinueDialogue() || !reportLine.text.Contains("物资留下"))
                        throw new InvalidOperationException("陈安的第二句未交代可搜查遗留物资");
                    CaptureViewports("ui-player-route-second-occupation");
                    Click("知道了");
                    if (BaseDemoFinale.SecondRaidReportPending(controller.State))
                        throw new InvalidOperationException("陈安报信确认后没有记录进度");
                    Log.Add("[PASS] 第二波胜利后载入可看陈安报信，确认后保存");
                    LoadRouteView("camp-remnants");
                    break;
                case "route-camp-remnants":
                    if (!RouteViewReady(state => state.Campaign.CurrentMapId == BaseDemoFinale.Camp)) return;
                    Approach(BaseDemoFinale.CampSuppliesMarker);
                    H.OpenObject(BaseDemoFinale.CampSuppliesMarker);
                    H.ContinueDialogue();
                    if (!controller.State.Demo.Finale.CampSuppliesTaken ||
                        !RequirePanel("NpcDialogue").Find("DialogueText").GetComponent<Text>().text.Contains("这些东西都没带走"))
                        throw new InvalidOperationException("已占领黑鸦寨的搜查入口未领取或显示反馈");
                    CaptureViewports("ui-player-route-camp-remnants");
                    Log.Add("[PASS] 已占领黑鸦寨的地标可搜查一次并显示领取反馈");
                    LoadRouteView("world");
                    break;
                case "route-world":
                    if (!RouteViewReady(BaseWorldJourney.OnWorld)) return;
                    // The continuous rules run grants rewards but does not dismiss their receipts.
                    if (H.PanelOpen && (string)Field(H, "panelViewKey") == "reward-receipt")
                    {
                        H.ClosePanel();
                        frameDeadline = Time.frameCount + 2;
                        return;
                    }
                    var oldMap = Object.FindAnyObjectByType<BaseWorldJourneyHud>();
                    if (oldMap == null) return;
                    worldDepartureDay = controller.State.Day;
                    PressJourney("返回");
                    JourneyStage("journey-browse");
                    break;
                case "journey-browse":
                    if (!RouteViewReady(state => state.Campaign.Location == "base")) return;
                    if (H.PanelOpen) H.ClosePanel();
                    Approach(BaseDemoNpcs.CampScout);
                    H.OpenObject(BaseDemoNpcs.CampScout);
                    H.ContinueDialogue();
                    if (!RequirePanel("NpcDialogue").Find("DialogueText").GetComponent<Text>().text.Contains("南口"))
                        throw new InvalidOperationException("首防后陈安未告知南口路线");
                    H.ClosePanel();
                    var south = controller.MapObjects.Single(obj => obj.Id == "demo_march_exit");
                    controller.State.PlayerX = south.X;
                    controller.State.PlayerY = south.Y + south.Height / 2 + BaseMap.Radius + .16;
                    MoveSceneExplorer(Vector2.down);
                    if (Object.FindAnyObjectByType<BaseWorldJourneyHud>() != null) throw new InvalidOperationException("南口在路牌外误触发");
                    for (int i = 0; i < 18 && Object.FindAnyObjectByType<BaseWorldJourneyHud>() == null; i++)
                    {
                        double previous = controller.State.PlayerY;
                        MoveSceneExplorer(Vector2.down);
                        if (Object.FindAnyObjectByType<BaseWorldJourneyHud>() != null && previous - .216 > south.Y - south.Height / 2 + .5)
                            throw new InvalidOperationException("南口在最下细格前触发");
                    }
                    var overview = Object.FindAnyObjectByType<BaseWorldJourneyHud>();
                    if (overview == null || H.PanelOpen) throw new InvalidOperationException("南口未直接打开地图");
                    ClickHudButton(overview.transform.Find("WorldMap/Node_home"));
                    if (!overview.GetComponentsInChildren<Text>().Any(t=>t.text.Contains("被黑鸦寨封锁")))
                        throw new InvalidOperationException("阻路原因未显示");
                    PressJourney("返回");
                    if(controller.State.Day!=worldDepartureDay || controller.State.Muster.Parties.Any(p=>p.Purpose=="expedition"))
                        throw new InvalidOperationException("取消查看路线误扣日期或调兵");
                    if (controller.State.PlayerY <= south.Y + south.Height / 2 || BaseInteraction.Facing.Y != 1)
                        throw new InvalidOperationException("南口返回未向北落在路牌外");
                    BaseInteraction.Walk(new BasePoint(0,-1));
                    H.OpenObject("demo_march_exit");
                    var journey = Object.FindAnyObjectByType<BaseWorldJourneyHud>();
                    var map = (RectTransform)journey.transform.Find("WorldMap");
                    var art = (RectTransform)map.Find("MapArt");
                    Canvas.ForceUpdateCanvases();
                    if (Math.Abs(map.rect.width-((RectTransform)journey.transform).rect.width)>.1f ||
                        art.rect.width<map.rect.width-.1f || art.rect.height<map.rect.height-.1f)
                        throw new InvalidOperationException("世界地图未铺满地图栏");
                    var header = journey.transform.parent.Find("Header");
                    if(header==null || !header.gameObject.activeInHierarchy)throw new InvalidOperationException("世界地图缺少资源顶栏");
                    ClickHudButton(header.Find("菜单"));
                    if (!H.PanelOpen || !map.gameObject.activeInHierarchy) throw new InvalidOperationException("菜单切掉了世界地图");
                    H.ClosePanel();
                    ClickHudButton(map.Find("Node_bandit"));
                    Canvas.ForceUpdateCanvases();
                    if (((RectTransform)Field(H,"questBar")).gameObject.activeInHierarchy ||
                        ((Component)Field(H,"questArrow")).gameObject.activeInHierarchy ||
                        journey.transform.Find("JourneyDestinationArrow").gameObject.activeInHierarchy ||
                        !journey.GetComponentsInChildren<Text>().Any(label=>label.text=="目的地提示 · 黑鸦寨"))
                        throw new InvalidOperationException("查看已选目的地时仍显示探索路口指引，或没有当前目的地");
                    JourneyStage("journey-selection");
                    break;
                case "journey-selection":
                    if (!RouteViewReady(state=>state.Campaign.Location=="base") || EditorApplication.timeSinceStartup-stageAt<.45) return;
                    var selectedMap=Object.FindAnyObjectByType<BaseWorldJourneyHud>().transform.Find("WorldMap");
                    var preview=(RectTransform)selectedMap.Find("PlanParty");
                    var destination=(RectTransform)selectedMap.Find("Node_bandit");
                    if (Math.Abs(preview.anchoredPosition.x-destination.anchoredPosition.x)>1)
                        throw new InvalidOperationException("路线预览没有走到所选目的地");
                    CaptureViewports("ui-player-route-world");
                    PressJourney("整备出征");
                    var departureScreen = RequirePanel("BattlePreparation");
                    if (controller.State.Day != worldDepartureDay || controller.State.Muster.Parties.Any(p=>p.Purpose=="expedition"))
                        throw new InvalidOperationException("备战预览已提前提交行军");
                    ClickHudButton(departureScreen.Find("Back"));
                    if (H.PanelOpen || !selectedMap.gameObject.activeInHierarchy) throw new InvalidOperationException("取消编队没有回到路线图");
                    PressJourney("整备出征");
                    ClickHudButton(RequirePanel("BattlePreparation").Find("Continue"));
                    JourneyStage("journey-arrived");
                    break;
                case "journey-arrived":
                    if (!RouteViewReady(state => state.Campaign.CurrentMapId == BaseDemoFinale.Camp)) return;
                    if (controller.State.Day != worldDepartureDay + 1 || BaseWorldJourney.Marching(controller.State) ||
                        controller.State.Muster.Parties.Count(p=>p.Purpose=="expedition") != 1)
                        throw new InvalidOperationException("到达黑鸦寨后日期或编队不符");
                    Log.Add("[PASS] 南口查看、退出、资源顶栏菜单、返回备战及出征，到达黑鸦寨结算一天");
                    // Reproduce a save written by the old confirm button before its battle-file preload failed.
                    var stranded = JsonUtility.FromJson<BaseState>(File.ReadAllText(Path.Combine("Logs","demo-route-checkpoints","world.json")));
                    BaseWorldJourney.Start(stranded,BaseWorldJourney.Plan(stranded,null,"bandit").Path);
                    controller.SaveStore.RestoreSession(stranded);
                    controller.LoadSaved();
                    JourneyStage("journey-resumed");
                    break;
                case "journey-resumed":
                    if (!RouteViewReady(state => state.Campaign.CurrentMapId == BaseDemoFinale.Camp)) return;
                    if (controller.State.Day != worldDepartureDay + 1 || BaseWorldJourney.Marching(controller.State) ||
                        controller.State.Muster.Parties.Count(p=>p.Purpose=="expedition")!=1)
                        throw new InvalidOperationException("旧档续走一天后位置、日期或队伍异常");
                    Log.Add("[PASS] 读取旧版已确认、尚余一天的路线后，原队伍抵达黑鸦寨且只结算一次");
                    if(worldJourneyOnly) Finish(0,"OK"); else LoadRouteView("home-arrival");
                    break;
                case "route-home-arrival":
                    if (!RouteViewReady(state => state.Campaign.CurrentMapId == BaseDemoFinale.Home && !state.Demo.Flow.HomeEnterSeen)) return;
                    if (H.PanelOpen && (string)Field(H, "panelViewKey") == "reward-receipt")
                    {
                        H.ClosePanel();
                        frameDeadline = Time.frameCount + 2;
                        return;
                    }
                    CheckHomeArrival();
                    LoadRouteView("rebuild");
                    break;
                case "route-rebuild":
                    if (!RouteViewReady(state => state.Campaign.CurrentMapId == BaseDemoFinale.Home && BaseDemoFinale.Rebuilding(state))) return;
                    if(H.DialogueOpen)FinishDialogue();
                    CheckHomeRebuild();
                    stage="route-home-walkers";stageAt=EditorApplication.timeSinceStartup;nextLog=0;
                    break;
                case "route-home-walkers":
                    if(!BaseDemoFinale.HomeVolunteers(controller.State))
                    {
                        var live=controller.State;
                        bool timedOut=EditorApplication.timeSinceStartup-stageAt>45;
                        if(!timedOut && EditorApplication.timeSinceStartup<nextLog)return;
                        string positions=string.Join(" / ",BaseSceneInstances.Layout(live,BaseDemoFinale.Home).Objects
                            .Where(o=>o.Id==BaseDemoFinale.Builder||o.Id==BaseDemoFinale.Helper).Select(o=>o.Id+" "+o.X.ToString("F2")+","+o.Y.ToString("F2")));
                        string detail=positions+" panel="+Field(H,"panelViewKey")+" open="+H.PanelOpen+" arrival="+live.Demo.Flow.HomeEnterSeen+
                            " day="+live.Day+" busy="+controller.Busy+" delta="+Time.deltaTime+" frame="+Time.frameCount+" message="+controller.Message;
                        Note(detail);
                        if(timedOut)throw new InvalidOperationException("村民未到麦田："+detail);
                        return;
                    }
                    if(H.PanelOpen && (string)Field(H,"panelViewKey")=="reward-receipt")
                    {
                        H.ClosePanel();
                        return;
                    }
                    if(H.DialogueOpen && RequirePanel("NpcDialogue").Find("SpeakerName").GetComponent<Text>().text==BaseScriptNames.Recruiter)
                    {
                        FinishDialogue();
                        Click("回营地安排生产");
                        return;
                    }
                    Approach(BaseDemoFinale.Builder,besideNpc:true);H.OpenObject(BaseDemoFinale.Builder);FinishDialogue();
                    var welcome=RequirePanel("NpcDialogue");
                    if(!welcome.Find("DialogueText").GetComponent<Text>().text.Contains("您吩咐吧。")||
                        !welcome.GetComponentsInChildren<Button>().Any(b=>b.interactable&&b.GetComponentInChildren<Text>()?.text=="村庄还需要建一些设施……"))
                        throw new InvalidOperationException("田边村民的对白或建设选项尚未显示");
                    CaptureViewports("ui-player-route-home-volunteers");
                    Click("没什么事了。");
                    if(!controller.State.Demo.Finale.VillageRebuilt || controller.State.Demo.Finale.EndingSeen || H.PanelOpen)
                        throw new InvalidOperationException("村民对话后应等玩家前往长椅");
                    var finaleBench=controller.MapObjects.Single(o=>o.Id==BaseDemoFinale.EndingBench(controller.State));
                    if(!controller.Change(s=>{s.PlayerX=finaleBench.X+finaleBench.Width*.29;s.PlayerY=finaleBench.Y-finaleBench.Height*.5-BaseMap.Radius-.06;},"") ||
                        controller.Nearby?.Id!=finaleBench.Id+":seat:right")throw new InvalidOperationException("返乡长椅座位不可达");
                    controller.Interact();
                    var settleOnBench=typeof(BaseSceneController).GetMethod("MoveExploration",BindingFlags.Instance|BindingFlags.NonPublic);
                    for(int frame=0;frame<9;frame++)settleOnBench.Invoke(controller,new object[]{Vector2.zero,.05});
                    FinishDialogue();
                    if((string)Field(H,"panelViewKey")!="demo-completion" || !controller.SaveStore.Load().Demo.Finale.EndingSeen)
                        throw new InvalidOperationException("结尾旁白读完后未显示或保存试玩完成卡");
                    stage="route-home-completion";stageAt=EditorApplication.timeSinceStartup;
                    break;
                case "route-home-completion":
                    var ending=((RectTransform)Field(H,"panel")).GetComponent<CanvasGroup>();
                    if(!ending.interactable||ending.alpha<.999f)
                    {
                        if(EditorApplication.timeSinceStartup-stageAt>5)throw new InvalidOperationException("试玩完成卡未显现");
                        return;
                    }
                    CaptureViewports("ui-player-route-home-ending");
                    Click("继续探索");
                    if(H.PanelOpen||controller.State.Campaign.CurrentMapId!=BaseDemoFinale.Home)throw new InvalidOperationException("完成卡无法返回白桦村探索");
                    Finish(0, "OK");
                    break;
                case "boot":
                    controller = C;
                    if (controller?.Message?.StartsWith("开局失败", StringComparison.Ordinal) == true)
                        throw new InvalidOperationException(controller.Message);
                    if (controller?.State == null || Object.FindAnyObjectByType<SceneLoadingCover>() != null)
                    { Note("等待序章进度"); return; }
                    if (!BaseDemoPrologue.Village(controller.State)) throw new InvalidOperationException("新档未进入白桦村");
                    if (campSceneryOnly)
                    {
                        if (Object.FindObjectsByType<Transform>().Any(t => t.name == "CampScenery"))
                            throw new InvalidOperationException("灰岩营地布景出现在白桦村");
                    }
                    else
                    {
                        CaptureViewports("ui-skin-prologue");
                        VerifyOpeningCopy();
                    }
                    controller.LoadDemoStage("camp", "female");
                    stage = "loading"; stageAt = EditorApplication.timeSinceStartup;
                    break;
                case "loading":
                    controller = C;
                    if (controller?.State == null) { if (EditorApplication.timeSinceStartup - stageAt > 150) Finish(2, "阶段档载入超时"); return; }
                    if (controller.State.Campaign.Location != "base") return;
                    H?.ClosePanel();
                    stage = campSceneryOnly ? "camp-scenery-shot" : "wake";
                    frameDeadline = Time.frameCount + (campSceneryOnly ? 45 : 5);
                    break;
                case "camp-scenery-shot":
                    if (Time.frameCount < frameDeadline) return;
                    CheckCampScenerySpawnAndEdge();
                    stage = "camp-scenery-edge"; frameDeadline = Time.frameCount + 4;
                    break;
                case "camp-scenery-edge":
                    if (Time.frameCount < frameDeadline) return;
                    Capture("exploration-camp-edge");
                    CheckCampSceneryFullView();
                    stage = "camp-scenery-wide"; frameDeadline = Time.frameCount + 4;
                    break;
                case "camp-scenery-wide":
                    if (Time.frameCount < frameDeadline) return;
                    Capture("exploration-camp-wide");
                    CheckCampNames();
                    CheckSceneryOcclusion();
                    H.ClosePanel();
                    if (!controller.Change(s => BaseDemo.AcknowledgeWaking(s), "镜头验收"))
                        throw new InvalidOperationException(controller.Message);
                    Approach("exit_east");
                    controller.TravelDemo("town");
                    stage = "exploration-town-edge"; frameDeadline = Time.frameCount + 12;
                    break;
                case "exploration-town-edge":
                    if (Time.frameCount < frameDeadline) return;
                    CheckTownFraming();
                    Finish(failed ? 2 : 0, failed ? "截图失败" : "OK");
                    break;
                case "wake":
                    // camp 阶段档停在 Waking：Demo 里「先了解处境」之前不许出营地，所以先完成醒来目标。
                    if (Time.frameCount < frameDeadline) return;
                    if (controller.State.Demo.Stage == BaseDemoStage.Waking)
                    {
                        if (!controller.Change(s =>
                        {
                            if (s.Demo.Stage != BaseDemoStage.Waking) return;
                            var home = BaseSpatial.Objects(s).Single(o => o.Id == "home");
                            // 营帐是带地基的设施，直接写中心点会落进不可通行格；走门前格。
                            var spot = home.Blocks ? BaseStageWalk.Door(s, home.Id) : new BasePoint(home.X, home.Y);
                            s.PlayerX = spot.X; s.PlayerY = spot.Y; BaseAdventure.Arrive(s);
                            if (!BaseDemo.AcknowledgeWaking(s)) throw new InvalidOperationException("醒来目标未完成");
                        }, "截图：完成醒来")) { Finish(2, "醒来失败：" + controller.Message); return; }
                    }
                    stage = "camp-shot"; frameDeadline = Time.frameCount + 45;
                    break;
                case "camp-shot":
                    if (Time.frameCount < frameDeadline) return;
                    CaptureViewports("ui-skin-camp-hud");
                    H.OpenMenu();
                    stage = "camp-menu-shot"; frameDeadline = Time.frameCount + 20;
                    break;
                case "camp-menu-shot":
                    if (Time.frameCount < frameDeadline) return;
                    RequirePanel("CampMenu");
                    Capture("ui-skin-camp-menu");
                    VerifyResourceHud();
                    H.OpenLandMap();
                    stage = "landmap-shot"; frameDeadline = Time.frameCount + 20;
                    break;
                case "landmap-shot":
                    if (Time.frameCount < frameDeadline) return;
                    Capture("ui-skin-landmap");
                    H.OpenQuests();
                    stage = "quests-shot"; frameDeadline = Time.frameCount + 20;
                    break;
                case "quests-shot":
                    if (Time.frameCount < frameDeadline) return;
                    Capture("ui-skin-quests");
                    H.OpenQuests("achievement");
                    stage = "achievements-shot"; frameDeadline = Time.frameCount + 20;
                    break;
                case "achievements-shot":
                    if (Time.frameCount < frameDeadline) return;
                    Capture("ui-skin-achievements");
                    H.ClosePanel();
                    H.OpenBackpack();
                    stage = "backpack-shot"; frameDeadline = Time.frameCount + 20;
                    break;
                case "backpack-shot":
                    if (Time.frameCount < frameDeadline) return;
                    VerifyPartyCardLayout(1);
                    CaptureViewports("ui-skin-backpack");
                    VerifyAuthoredLayout();
                    // 选中一件物品，验证说明区已改为点选后才出现的小弹框。
                    var firstItem = BasePartyInventory.Entries(controller.State).FirstOrDefault(e => e != null && !string.IsNullOrEmpty(e.Holder) && !e.Worn);
                    if (firstItem != null)
                    {
                        var hudType = H.GetType();
                        hudType.GetField("inventorySelected", BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(H, firstItem.Id);
                        hudType.GetMethod("OpenInventory", BindingFlags.Instance | BindingFlags.NonPublic)?.Invoke(H, new object[] { false });
                        stage = "backpack-menu-shot"; frameDeadline = Time.frameCount + 20;
                        break;
                    }
                    OpenWarehouseShot();
                    break;
                case "backpack-menu-shot":
                    if (Time.frameCount < frameDeadline) return;
                    Capture("ui-skin-backpack-menu");
                    VerifyBackpackEquip();
                    OpenWarehouseShot();
                    break;
                case "warehouse-shot":
                    if (Time.frameCount < frameDeadline) return;
                    VerifyWarehouseUi();
                    H.ClosePanel();
                    BaseBattleGateway.MainMenu = true;
                    try { H.OpenMenu(); } finally { BaseBattleGateway.MainMenu = false; }
                    stage = "menu-shot"; frameDeadline = Time.frameCount + 40;
                    break;
                case "menu-shot":
                    if (Time.frameCount < frameDeadline) return;
                    Capture("ui-skin-mainmenu");
                    H.ClosePanel();
                    Approach("exit_east");
                    controller.TravelDemo("town");
                    if (controller.State.Campaign.Location != "town")
                        throw new InvalidOperationException("前往城镇被拒：" + controller.Message);
                    stage = "to-town"; frameDeadline = Time.frameCount + 90; stageAt = EditorApplication.timeSinceStartup;
                    break;
                case "to-town":
                    controller = C;
                    if (controller?.State == null) return;
                    if (controller.State.Campaign.Location != "town")
                    {
                        if (EditorApplication.timeSinceStartup - stageAt > 60) Finish(2, "未到达城镇");
                        return;
                    }
                    if (Time.frameCount < frameDeadline) return;
                    Approach("town_quest");
                    stage = "town-arrive"; frameDeadline = Time.frameCount + 30;
                    break;
                case "town-arrive":
                    if (Time.frameCount < frameDeadline) return;
                    var mapLabels = (RectTransform[])Field(H, "labels");
                    var authoredLabels = (BaseMapObject[])Field(H, "labelObjects");
                    int lordLabel = Array.FindIndex(authoredLabels, label => label.Id == "town_quest");
                    if (lordLabel < 0) throw new InvalidOperationException("缺少领主名牌：" + string.Join(",", authoredLabels.Select(label => label.Id)));
                    var lordLabelText = mapLabels[lordLabel].GetComponent<Text>();
                    if (lordLabelText.color.grayscale < .72f)
                        throw new InvalidOperationException("场景人物名牌被面板主题改成了深色：" + lordLabelText.color);
                    H.OpenObject("town_quest");
                    VerifySavedDialogueColor(RequirePanel("NpcDialogue"));
                    if (!H.DialogueExitLocked) throw new InvalidOperationException("首次领主对白没有锁定退出");
                    H.ClosePanel();
                    if (!H.DialogueOpen) throw new InvalidOperationException("中途关闭了首次领主对白");
                    FinishDialogue();
                    if (H.DialogueExitLocked) throw new InvalidOperationException("领主叙述完成后仍锁定退出");
                    stage = "dialogue-shot"; frameDeadline = Time.frameCount + 45;
                    break;
                case "dialogue-shot":
                    if (Time.frameCount < frameDeadline) return;
                    RequirePanel("NpcDialogue");
                    CaptureViewports("ui-skin-dialogue", mobile: true);
                    H.ClosePanel();
                    ApproachMerchantAwayFromStall();
                    H.OpenObject("town_peddler");
                    var merchantDialogue = RequirePanel("NpcDialogue");
                    VerifySavedDialogueColor(merchantDialogue);
                    var pendingReplies = merchantDialogue.Find("Replies").GetComponentsInChildren<Button>();
                    VerifyUnreadReplies(pendingReplies,3);
                    if (merchantDialogue.Find("AdvanceButton").GetComponentInChildren<Text>().text != "→")
                        throw new InvalidOperationException("对白继续按钮未使用右箭头");
                    Capture("ui-skin-dialogue-placeholders");
                    FinishDialogue();
                    if (merchantDialogue.Find("Replies").GetComponentsInChildren<Button>()
                        .Any(button => !button.interactable || button.GetComponentInChildren<Text>().text == "..."))
                        throw new InvalidOperationException("行商对白播完后未原位开放实际选项");
                    Click(BaseConquest.Story.StoryText.Get("story.ui.trade.market_entry", "买卖东西"));
                    var merchantMarket = RequirePanel("TownTrade");
                    if (merchantMarket.Find("Tabs").gameObject.activeInHierarchy)
                        throw new InvalidOperationException("交易面板仍显示业务页签");
                    VerifyMerchantCommonGoods();
                    BuySelectedForgeStone();
                    stage = "trade-shot"; frameDeadline = Time.frameCount + 30;
                    break;
                case "trade-shot":
                    if (Time.frameCount < frameDeadline) return;
                    VerifyTradeCardLayout(RequirePanel("TownTrade"));
                    CaptureViewports("ui-skin-trade");
                    H.ClosePanel();
                    VerifyTownReforge();
                    VerifyAzhuDuelDialogue();
                    Approach("town_quest");
                    if (controller.State.Demo.AcceptedDay == 0)
                    {
                        H.OpenObject("town_quest");
                        H.ContinueDialogue();
                        if (!H.DialogueExitLocked || !RequirePanel("NpcDialogue").Find("DialogueText").GetComponent<Text>().text.Contains("拖住了追兵"))
                            throw new InvalidOperationException("领主委托第一页或退出锁定错误");
                        FinishDialogue();
                        if (!RequirePanel("NpcDialogue").Find("DialogueText").GetComponent<Text>().text.Contains("募兵处也会放行"))
                            throw new InvalidOperationException("领主委托漏播建设和募兵条件");
                        Click("接受" + BaseScriptNames.Ruins + "调查");
                        if (controller.State.Demo.AcceptedDay == 0)
                            throw new InvalidOperationException("对白未能接取古窟调查");
                    }
                    Approach("demo_ruins_exit");
                    controller.TravelDemo("outpost");
                    stage = "ruins-entrance"; frameDeadline = Time.frameCount + 15;
                    break;
                case "ruins-entrance":
                    if (Time.frameCount < frameDeadline) return;
                    VerifyRuinsEntrance();
                    ruinsEntranceCamera = controller.WorldCamera;
                    ruinsEntranceMap = controller.State.Campaign.CurrentMapId;
                    controller.Interact();
                    Press(RequirePanel("BattlePreparation"), "Continue");
                    stage = "ruins-deployment"; frameDeadline = Time.frameCount + 5;
                    break;
                case "ruins-deployment":
                    if (Time.frameCount < frameDeadline) return;
                    var ruinsPlacement = RequireBattleDraft();
                    if (controller.WorldCamera == ruinsEntranceCamera || controller.State.Campaign.CurrentMapId != ruinsEntranceMap || controller.State.Campaign.HasActiveBattle)
                        throw new InvalidOperationException("从遗迹入口预览地牢时写入了进度或未显示实际地牢");
                    VerifyDeploymentPointer(ruinsPlacement);
                    Capture("ui-skin-ruins-deployment");
                    Press(BattleSafeArea(ruinsPlacement), "DraftPreparationControls/Back");
                    if (controller.State.Campaign.CurrentMapId != ruinsEntranceMap || !BaseSceneRules.Entrance(controller.State) ||
                        Object.FindObjectsByType<BattleSceneController>().Any(b => b.DeploymentDraftActive) ||
                        !Object.FindObjectsByType<Renderer>().Any(r => r.name == "PixelDemo_dungeon-entrance"))
                        throw new InvalidOperationException("返回备战没有恢复遗迹入口");
                    Press(RequirePanel("BattlePreparation"), "Continue");
                    var cancelRuins = RequireBattleDraft();
                    Press(BattleSafeArea(cancelRuins), "DraftPreparationControls/Start");
                    FinishDialogue();
                    Click("先退出去准备一下");
                    if (!BaseSceneRules.Entrance(controller.State) || H.PanelOpen || controller.State.Campaign.HasActiveBattle ||
                        Object.FindObjectsByType<BattleSceneController>().Any(b=>b.DeploymentDraftActive))
                        throw new InvalidOperationException("暂缓初遇没有退回遗迹入口");
                    if (BaseAchievements.Record(controller.State, BaseAchievements.Caution) != null)
                        throw new InvalidOperationException("免疫队伍误触发了古窟危险警告");
                    if (!controller.Change(state => VerifyBanditHideout.AddSoldiers(state, 1, 0, 0), "界面检查"))
                        throw new InvalidOperationException(controller.Message);
                    VerifyRuinsWarning();
                    if (H.PanelOpen || controller.State.Campaign.CurrentMapId != ruinsEntranceMap ||
                        controller.State.Campaign.HasActiveBattle || Object.FindObjectsByType<BattleSceneController>().Any(b => b.DeploymentDraftActive))
                        throw new InvalidOperationException("退出部署后没有恢复遗迹入口");
                    Log.Add("[PASS] 古窟免疫队伍跳过警告、普通士兵弹出确认，退出初遇后返回入口");
                    VerifyRuinsSoloRetreat();
                    VerifyHideoutUi();
                    controller.LoadDemoStage("defense", "female");
                    stage = "defense-ready"; stageAt = EditorApplication.timeSinceStartup;
                    break;
                case "defense-ready":
                    controller = C;
                    if (controller?.State == null || controller.State.Demo.Stage != BaseDemoStage.DefenseWarned
                        || Object.FindAnyObjectByType<SceneLoadingCover>() != null) return;
                    H.OpenBackpack();
                    if (RequirePanel("PartyInventory").Find("Cards").childCount != 3)
                        throw new InvalidOperationException("整备阶段背包应同屏显示三名队员");
                    stage = "defense-inventory-shot"; frameDeadline = Time.frameCount + 5;
                    break;
                case "defense-inventory-shot":
                    if (Time.frameCount < frameDeadline) return;
                    VerifyPartyCardLayout(3);
                    CaptureViewports("ui-skin-backpack-three");
                    VerifyTwoMemberBackpack();
                    VerifyBackpackTransfer();
                    // This check exercises the ordinary player view, regardless of editor preferences.
                    bool developerView = RuntimeDeveloperOptions.Enabled;
                    try
                    {
                        if (developerView) RuntimeDeveloperOptions.Enabled = false;
                        VerifyFacilityRosters();
                    }
                    finally
                    {
                        if (developerView) RuntimeDeveloperOptions.Enabled = true;
                    }
                    VerifyRecruitRoster();
                    controller.LoadDemoStage("defense", "female");
                    stage = "defense-rosters-done"; frameDeadline = Time.frameCount + 15;
                    break;
                case "defense-rosters-done":
                    controller = C;
                    if (Time.frameCount < frameDeadline || controller?.State?.Campaign.Location != "perimeter" ||
                        Object.FindAnyObjectByType<SceneLoadingCover>() != null) return;
                    H.ClosePanel();
                    if (!controller.Change(s => s.Muster.Strategies = new List<int>
                        { (int)SquadStrategy.Obey, (int)SquadStrategy.Attack, (int)SquadStrategy.Obey }, "界面检查"))
                        throw new InvalidOperationException(controller.Message);
                    Approach("demo_defense_start");
                    H.OpenObject("demo_defense_start");
                    RequirePanel("BattlePreparation");
                    stage = "defense-prep-shot"; frameDeadline = Time.frameCount + 15;
                    break;
                case "defense-prep-shot":
                    if (Time.frameCount < frameDeadline) return;
                    var preparation = RequirePanel("BattlePreparation");
                    VerifyBelowTopBar(preparation);
                    if (preparation.Find("Backpack") != null || ((RectTransform)preparation.Find("Continue")).anchoredPosition.x <
                        ((RectTransform)preparation.Find("Reset")).anchoredPosition.x)
                        throw new InvalidOperationException("备战页多出背包按钮或按钮顺序不是恢复后进入");
                    if (Environment.GetEnvironmentVariable("BASE_CONQUEST_UI_CHECK_SAVED_LAYOUT") == "1" &&
                        ((RectTransform)preparation.Find("Close")).anchoredPosition != new Vector2(1025, -9))
                        throw new InvalidOperationException("启动后备战关闭按钮未读取保存的位置");
                    var defenseDraft = (BaseBattlePreparation)Field(H, "battleDraft");
                    var preparedPortraits = preparation.GetComponentsInChildren<BattleAvatarGraphic>();
                    if (preparedPortraits.Length == 0 || preparedPortraits.Any(avatar => avatar.mainTexture.width != 256) ||
                        preparation.GetComponentsInChildren<RawImage>().Any(image => image.transform.parent.name == "全身像" || image.name == "全身像"))
                        throw new InvalidOperationException("战前编队未统一使用半身像");
                    if (defenseDraft.RequiredSupply(false) <= 0 || defenseDraft.SupplyGrade!=1)
                        throw new InvalidOperationException("首防测试编队缺少普通兵或未以一级军需起步");
                    if (preparation.Find("Help") != null || preparation.Find("HelpOverlay") != null)
                        throw new InvalidOperationException("备战页仍包含旧的规则入口");
                    Press(preparation, "Candidates/SortHealth");
                    preparation = RequirePanel("BattlePreparation");
                    var roster = preparation.Find("Candidates/List").GetComponent<ScrollRect>();
                    VerifyRosterRow(roster.content.GetChild(0), true);
                    Press(roster.content.GetChild(0), "Info");
                    preparation = RequirePanel("BattlePreparation");
                    if (!preparation.Find("Candidates/Detail").gameObject.activeInHierarchy ||
                        !ReferenceEquals(defenseDraft, Field(H, "battleDraft")))
                        throw new InvalidOperationException("备战名册详情未打开或改变了草案");
                    Press(preparation, "Candidates/Detail/Back");
                    preparation = RequirePanel("BattlePreparation");
                    if (defenseDraft.AvailableSupply(controller.State, 2) >= defenseDraft.RequiredSupply(false))
                        throw new InvalidOperationException("检查用首防阵容没有二级军需缺口");
                    SetPreparationSupply(preparation,2);
                    if (preparation.Find("Continue").GetComponent<Button>().interactable)
                        throw new InvalidOperationException("二级军需不足仍可进场");
                    if (defenseDraft.AvailableSupply(controller.State, 1) < defenseDraft.RequiredSupply(false))
                    {
                        // The stage fixture can start with one more soldier than the level I stock.
                        H.ClosePanel();
                        if (!controller.Change(s => s.Inventory.Equipment.Single(e => e.Product == "supply" && e.Level == 1).Count += 2,
                            "检查一级军需选择"))
                            throw new InvalidOperationException(controller.Message);
                        H.OpenObject("demo_defense_start");
                        preparation = RequirePanel("BattlePreparation");
                    }
                    SetPreparationSupply(preparation,1);
                    if (((BaseBattlePreparation)Field(H, "battleDraft")).SupplyGrade != 1)
                        throw new InvalidOperationException("一级军需未作为整场档位保存");
                    var teamRows = preparation.Find("Teams");
                    var secondTeam = teamRows.GetChild(1);
                    ClickHudButton(secondTeam);
                    if (RequirePanel("BattlePreparation") != preparation || teamRows.GetChild(1) != secondTeam)
                        throw new InvalidOperationException("点击队长时整页或队长行被重建");
                    if (teamRows.GetChild(1).Find("Portrait").GetComponent<Image>().color != DemoUiTheme.Current.Gold)
                        throw new InvalidOperationException("点击队长整行后，分配目标没有变为金色");
                    ClickHudButton(teamRows.GetChild(0));
                    var firstTeam = teamRows.GetChild(0);
                    var role = new[] { "Melee", "Ranged", "Cavalry", "Mage" }.FirstOrDefault(kind =>
                        int.Parse(firstTeam.Find(kind + "/Input").GetComponent<InputField>().text)>0);
                    if (role != null)
                    {
                        var field = firstTeam.Find(role + "/Input").GetComponent<InputField>();
                        string initial = field.text;
                        field.onEndEdit.Invoke((int.Parse(initial)-1).ToString());
                        if (RequirePanel("BattlePreparation") != preparation || field.text == initial)
                            throw new InvalidOperationException("备战减兵未原位更新");
                        field.onEndEdit.Invoke(initial);
                        if (RequirePanel("BattlePreparation") != preparation || field.text != initial)
                            throw new InvalidOperationException("备战连续加减没有保留原数量控件");
                    }
                    var guide = (Graphic)Field(H, "questArrow");
                    var continueButton = (RectTransform)preparation.Find("Continue");
                    if (((RectTransform)Field(H, "questBar")).gameObject.activeSelf || !guide.gameObject.activeInHierarchy ||
                        Mathf.Abs(guide.transform.position.x - continueButton.TransformPoint(continueButton.rect.center).x) > 1)
                        throw new InvalidOperationException("备战面板上的任务箭头未对准进入战场或任务条遮住了面板");
                    Log.Add("[PASS] 备战点击整行切换金色头像，任务箭头指向进入战场");
                    defenseSupplyStock = controller.State.Inventory.Equipment.Single(e => e.Product == "supply" && e.Level == 1).Count;
                    CaptureViewports("ui-skin-battle-preparation");
                    var fieldCamera = controller.WorldCamera;
                    Press(preparation, "Continue");
                    var deploy = RequireBattleDraft();
                    if (deploy.PreviewBoard.Camera != fieldCamera)
                        throw new InvalidOperationException("迎战部署替换了当前战场相机");
                    CaptureViewports("ui-skin-battle-deployment-entry");
                    var draftArea = BattleSafeArea(deploy);
                    var banner = (RectTransform)draftArea.Find("FormalBanner");
                    if (banner == null || !banner.gameObject.activeInHierarchy || banner.anchorMin.x != 0 || banner.anchorMax.x != 1)
                        throw new InvalidOperationException("战况没有汇入横贯屏幕的顶栏");
                    var summary = banner.Find("FormalSummary");
                    if (!summary.gameObject.activeInHierarchy) throw new InvalidOperationException("战况未默认展开");
                    Press(banner, "DraftSummaryToggle");
                    if (summary.gameObject.activeInHierarchy) throw new InvalidOperationException("战况不能收起");
                    Press(banner, "DraftSummaryToggle");
                    if (!summary.gameObject.activeInHierarchy) throw new InvalidOperationException("战况不能重新展开");
                    var firstTroopRow = draftArea.Find("CommanderSlot0/DraftTroops");
                    var secondTroopRow = draftArea.Find("CommanderSlot1/DraftTroops");
                    Canvas.ForceUpdateCanvases();
                    var cards = firstTroopRow.GetComponentsInChildren<Button>().Select(b => (RectTransform)b.transform).ToArray();
                    float portraitWidth = ((RectTransform)draftArea.Find("CommanderSlot0/CommanderPortrait_0")).rect.width;
                    if (draftArea.Find("ControlledArmy").gameObject.activeInHierarchy ||
                        cards.Length != 3 || cards.Any(card => card.rect.width < portraitWidth - .01f || card.rect.height < 44) ||
                        cards[0].anchoredPosition.y <= cards[1].anchoredPosition.y + cards[1].rect.height ||
                        cards[1].anchoredPosition.y <= cards[2].anchoredPosition.y + cards[2].rect.height)
                        throw new InvalidOperationException("兵种卡未按头像全宽竖向排列");
                    if (cards.Where((card, index) => card.GetComponentsInChildren<BattleAvatarGraphic>().Length > 0 ||
                        !card.GetComponentInChildren<Text>().text.StartsWith(BattleSimulation.KindName(BattleSimulation.TroopKinds[index]) + "  ")).Any())
                        throw new InvalidOperationException("兵种卡未用文字显示兵种和余量");
                    Press(draftArea, "CommanderSlot0/TroopToggle");
                    if (firstTroopRow.gameObject.activeInHierarchy || !secondTroopRow.gameObject.activeInHierarchy)
                        throw new InvalidOperationException("兵种卡未按队独立收起");
                    Press(draftArea, "CommanderSlot0/TroopToggle");
                    if (!firstTroopRow.gameObject.activeInHierarchy)
                        throw new InvalidOperationException("兵种卡未重新展开");
                    Log.Add("[PASS] 战况统一进横幅，三张全宽兵种卡竖排且可按队独立折叠");
                    VerifyContinuousDeployment(deploy);
                    Press(draftArea, "DraftPreparationControls/Clear");
                    if (deploy.PreviewSimulation.Soldiers.Any(s => s.Side == BattleSide.A))
                        throw new InvalidOperationException("一键撤销未清空全部落点");
                    VerifyDeploymentPointer(deploy);
                    var resetDraft = (BaseBattlePreparation)Field(H, "battleDraft");
                    var firstSoldier = resetDraft.Units.First(u => u.TeamId == resetDraft.PlayerId &&
                        u.Selected && u.Kind != SoldierKind.Hero);
                    PlaceByPointer(deploy, resetDraft, firstSoldier.Id);
                    foreach (var leader in resetDraft.Units.Where(u => u.Selected && u.Kind == SoldierKind.Hero && u.Id != resetDraft.PlayerId))
                        if (!resetDraft.Place(leader.Id, resetDraft.Board.Positions().First(p => resetDraft.CanPlace(leader.Id, p))))
                            throw new InvalidOperationException("未能摆放默认选中检查用的队长");
                    Press(BattleSafeArea(deploy), "DraftPreparationControls/Back");
                    Press(RequirePanel("BattlePreparation"), "Continue");
                    defenseBattle = RequireBattleDraft();
                    defenseHud = defenseBattle.PreviewHud.Root;
                    defenseCamera = defenseBattle.PreviewBoard.Camera;
                    defenseViewport = defenseCamera.rect;
                    if (defenseBattle.PreviewSimulation.Soldiers.Count(s => s.Side == BattleSide.A) != 4)
                        throw new InvalidOperationException("从备战返回丢失了原落点");
                    stage = "defense-deployment-shot"; frameDeadline = Time.frameCount + 15;
                    break;
                case "defense-deployment-shot":
                    if (Time.frameCount < frameDeadline) return;
                    CaptureViewports("ui-skin-battle-deployment", mobile: true);
                    VerifyDeploymentSpace(defenseBattle);
                    FinishDialogue();
                    Click("拔剑迎战");
                    stage = "battle"; frameDeadline = Time.frameCount + 60; stageAt = EditorApplication.timeSinceStartup;
                    break;
                case "battle":
                    if (Field(controller, "combatController") == null)
                    {
                        if (EditorApplication.timeSinceStartup - stageAt > 30) Finish(2, "首防未开战：" + controller.Message);
                        frameDeadline = Time.frameCount + 20;
                        return;
                    }
                    if (Time.frameCount < frameDeadline) return;
                    if (!defenseBattle || defenseBattle.DeploymentDraftActive || defenseBattle.PreviewHud.Root != defenseHud ||
                        defenseBattle.PreviewBoard.Camera != defenseCamera || defenseCamera.rect != defenseViewport ||
                        defenseBattle.PreviewSimulation.Phase != BattlePhase.Fighting)
                        throw new InvalidOperationException("开战时没有沿用同一战场、相机和战斗 HUD");
                    Log.Add("[PASS] 确认开战沿用原战场相机、同一战斗 HUD，未重新建场");
                    var opening = defenseBattle.Realtime.Session.Opening;
                    if (opening.SupplySnapshot.FullConsumptionGrade != 1 || opening.SupplySnapshot.Demand("supply") != 1 ||
                        controller.State.Inventory.Equipment.Single(e => e.Product == "supply" && e.Level == 1).Count != defenseSupplyStock)
                        throw new InvalidOperationException("开战未按实际落位人数锁定军需，或提前扣除了库存");
                    Log.Add("[PASS] 备战军需按计划校验，实际开战按一个落位士兵锁定且未扣库存");
                    VerifyOpeningBattleSelection(defenseBattle);
                    if (androidTouch) VerifyBattleTouch(defenseBattle);
                    if (Object.FindObjectsByType<RectTransform>().Any(t => t.name == "BattleJoystick"))
                        throw new InvalidOperationException("正式战斗仍显示角色操控摇杆");
                    CaptureViewports("ui-icons-battle-hud");
                    stage = "done"; frameDeadline = Time.frameCount + 60;
                    break;
                case "done":
                    if (Time.frameCount < frameDeadline) return;
                    exitSaveFiles=SnapshotSaves();
                    defenseBattle.OpenCampaignMenu();
                    var systemMenu=((GameObject)Field(defenseBattle,"campaignMenu")).GetComponentsInChildren<RectTransform>(true).Single(rect=>rect.name=="选项面板");
                    Press(systemMenu,"MainMenu");
                    stage="discarded";stageAt=EditorApplication.timeSinceStartup;
                    break;
                case "discarded":
                    if(C==null||C.Hud==null||!BaseBattleGateway.MainMenu||defenseBattle)
                    {
                        if(EditorApplication.timeSinceStartup-stageAt>20)throw new InvalidOperationException("回主菜单未丢弃当前战斗");
                        return;
                    }
                    if(C.State!=null||!exitSaveFiles.SequenceEqual(SnapshotSaves()))
                        throw new InvalidOperationException("回主菜单仍恢复了会话或改写了磁盘存档");
                    Log.Add("[PASS] 战中回主菜单直接丢弃会话，存档文件逐字节不变");
                    Finish(0,"OK");
                    break;
            }
        }

        private static string[] exitSaveFiles;
        private static string[] SnapshotSaves()=>Directory.GetFiles(BaseBattleGateway.SaveDirectory,"*.json*")
            .OrderBy(path=>path,StringComparer.Ordinal).Select(path=>Path.GetFileName(path)+":"+Convert.ToBase64String(File.ReadAllBytes(path))).ToArray();

        private static void TickHomeField()
        {
            controller=C;
            if(controller?.State==null||controller.Busy||Object.FindAnyObjectByType<SceneLoadingCover>()!=null)return;
            switch(stage)
            {
                case "home-field-boot":
                    LoadRouteView("home-field");
                    stage="home-field-ready";return;
                case "home-field-ready":
                    if(Time.frameCount<frameDeadline||controller.State.Campaign.Location!="perimeter")return;
                    H.ClosePanel();
                    var board=BaseBattlefield.Board(controller.State);
                    if(board.Cell(new GridPosition(20,5)).Terrain!=TerrainKind.Ground||
                       !Enumerable.Range(9,8).All(z=>board.Cell(new GridPosition(10,z)).Terrain==TerrainKind.Swamp)||
                       BaseSceneRules.FogEnabled(controller.State)||controller.State.Demo.EnvironmentVersion<1||
                       !BaseSpatial.Layout(controller.State,"perimeter").Objects.Any(o=>o.Id.StartsWith("env_")))
                        throw new InvalidOperationException("正式迎敌场景的布景、入口、纵向沼泽或主场探索视野错误");
                    if(!controller.Change(s=>{s.PlayerX=16.5;s.PlayerY=11.5;BaseAdventure.Arrive(s);},""))
                        throw new InvalidOperationException(controller.Message);
                    controller.ZoomExplorationFactor(controller.WorldCamera.pixelRect.center,100);
                    defenseViewport=controller.WorldCamera.rect;
                    controller.ArmConstruction();controller.SelectConstructionProject(ConstructionProject.Trap);
                    stage="home-field-tools";frameDeadline=Time.frameCount+12;return;
                case "home-field-tools":
                    if(Time.frameCount<frameDeadline)return;
                    var toolbar=Object.FindObjectsByType<RectTransform>().Single(t=>t.name=="FieldworkToolbar");
                    if(!controller.ConstructionMode||controller.WorldCamera.rect!=defenseViewport||toolbar.rect.height>70||
                       Object.FindObjectsByType<Text>().Any(t=>t.text=="巨石"||t.text=="枯树"))
                        throw new InvalidOperationException("开启工事改变了相机范围，或环境摆件仍显示地名");
                    H.RefreshAchievementNotice(3);
                    Capture("home-field-tools");controller.StopConstruction();
                    if(!controller.Change(s=>s.Inventory.Equipment.Single(e=>e.Product=="supply"&&e.Level==1).Count+=8,"检查首防"))
                        throw new InvalidOperationException(controller.Message);
                    Approach("demo_defense_start");H.OpenObject("demo_defense_start");
                    var prep=RequirePanel("BattlePreparation");SetPreparationSupply(prep,1);Press(prep,"Continue");
                    stage="home-field-deploy";frameDeadline=Time.frameCount+12;return;
                case "home-field-deploy":
                    if(Time.frameCount<frameDeadline)return;
                    defenseBattle=RequireBattleDraft();
                    VerifyHomeSight(defenseBattle.PreviewSimulation);
                    defenseBattle.PreviewBoard.SetViewSize(defenseBattle.PreviewBoard.FullViewSize);
                    defenseBattle.PreviewBoard.CenterOn(new GridPosition(16,12));
                    Capture("home-field-deployment");
                    Press(BattleSafeArea(defenseBattle),"DraftPreparationControls/Auto");
                    Press(BattleSafeArea(defenseBattle),"DraftPreparationControls/Start");
                    FinishDialogue();Click("拔剑迎战");stage="home-field-live";return;
                case "home-field-live":
                    var run=defenseBattle.Realtime;
                    if(run==null)return;
                    VerifyHomeSight(run.Battle);
                    foreach(var lead in run.Battle.Soldiers.Where(u=>u.Side==BattleSide.A&&u.Kind==SoldierKind.Hero))
                        run.Strategy(lead.OriginCommanderSlot,SquadStrategy.Protect);
                    run.Model.SetPaused(false);
                    for(int tick=0;tick<run.Battle.Scenario.Objective.HoldRounds*60+3&&run.Battle.Phase==BattlePhase.Fighting;tick++)
                        run.Tick(1d/60);
                    if (run.DefensePursuitReady) defenseBattle.RealtimeRetreat();
                    if (!run.VictoryExitOpen || run.Battle.Report != null || !run.OrderBattlefieldExit(null))
                        throw new InvalidOperationException("首防锁胜后应先前往出口");
                    AdvanceBattleToExit(defenseBattle);
                    if(run.Battle.Report?.Outcome!=BattleOutcome.SideAWin||run.Model.Elapsed<=1)
                        throw new InvalidOperationException("由备战名册部署的队伍未能在实战中守住营地");
                    defenseBattle.PreviewHud.Refresh();
                    stage="home-field-report";frameDeadline=Time.frameCount+12;return;
                case "home-field-report":
                    if(Time.frameCount<frameDeadline)return;
                    var report=Object.FindObjectsByType<RectTransform>().Single(t=>t.name=="AfterActionPanel");
                    if(report.GetComponentInChildren<ScrollRect>()!=null||!report.Find("AllySummary/Strength")||
                       !report.Find("EnemySummary/Troops")||!report.Find("SettleBattle").gameObject.activeInHierarchy)
                        throw new InvalidOperationException("单屏战报缺少双方战损或结算入口");
                    var leaders=report.Find("AllySummary/Leaders").GetComponentsInChildren<Text>();
                    if(leaders.Any(t=>t.supportRichText||t.text.Contains(UnitDisplayNames.RecruitmentColor))||
                       !leaders.Any(t=>t.text.Contains("迦兰")&&((Color32)t.color).Equals(new Color32(139,81,47,255))))
                        throw new InvalidOperationException("战报姓名未按纯文字显示，或俘虏队长的颜色丢失");
                    Capture("home-field-report");Capture("home-field-report-mobile",854,480);
                    Finish(failed?2:0,"首防工事、部署单向全图、交战视野和单屏战报检查完成");return;
            }
        }

        private static void VerifyHomeSight(BattleSimulation battle)
        {
            if(!battle.Board.Positions().All(p=>battle.Vision.Visible(BattleSide.A,p))||
               !battle.Board.Positions().Any(p=>!battle.Vision.Visible(BattleSide.B,p)))
                throw new InvalidOperationException("迎敌战场应仅向我方揭开全图");
        }

        private static void VerifyOpeningDrillControls()
        {
            H.ClosePanel();
            controller.BeginDemoPractice();
            var drill = controller.GetComponentsInChildren<BattleSceneController>().SingleOrDefault(battle => battle.IsDemoPractice);
            if (drill?.Realtime == null) throw new InvalidOperationException("无法从序章快照进入演练：" + controller.Message);

            var area = BattleSafeArea(drill);
            var portrait = area.Find("CommanderSlot0/CommanderPortrait_0");
            var pause = area.Find("RealtimeControls/Pause");
            var skill = area.Find("CommanderSlot0/战技");
            var attack = area.Find("RealtimeControls/Attack");
            var close = area.Find("CommanderDetails/关闭详情");
            var arrow = area.Find("PracticeArrow");
            var buttons = new[] { portrait, pause, skill, attack, close };
            if (buttons.Any(button => button?.GetComponent<Outline>() == null) || arrow == null ||
                area.Find("ControlledArmy").gameObject.activeInHierarchy || area.Find("自动").gameObject.activeInHierarchy ||
                area.Find("RealtimeControls").GetComponentsInChildren<Button>().Length != 5 ||
                ((RectTransform)skill).anchoredPosition.y <= ((RectTransform)portrait).anchoredPosition.y)
                throw new InvalidOperationException("演练缺少正式场景按钮或边框组件");
            var intro = controller.State.Demo.Intro;
            var tickGuide = typeof(BaseSceneController).GetMethod("TickPracticeGuide", BindingFlags.Instance | BindingFlags.NonPublic);
            drill.PreviewHud.Refresh();
            var resting = buttons.ToDictionary(button => button, ReadOutline);
            if (drill.Realtime.HasBattlefieldExits ||
                (intro.DrillFlags & BaseDemoIntro.Select) != 0 || drill.SelectedUnits.Count != 1)
                throw new InvalidOperationException("序章演练开场默认选中角色，选择教学仍需实际操作");

            (Color Color, Vector2 Distance, bool Enabled) ReadOutline(Transform target)
            {
                var outline = target.GetComponent<Outline>();
                return (outline.effectColor, outline.effectDistance, outline.enabled);
            }

            void ExpectCue(Transform target = null, bool mapTarget = false)
            {
                if (arrow.gameObject.activeInHierarchy != (target != null || mapTarget))
                    throw new InvalidOperationException("演练没有显示或清除箭头");
                if (arrow.gameObject.activeInHierarchy && arrow.GetComponent<CanvasRenderer>() == null)
                    throw new InvalidOperationException("演练箭头缺少 CanvasRenderer");
                if (target is RectTransform rect)
                {
                    var center = area.InverseTransformPoint(rect.TransformPoint(rect.rect.center));
                    if (Mathf.Abs(area.InverseTransformPoint(arrow.position).x - center.x) > 1)
                        throw new InvalidOperationException("演练箭头未对准按钮中心");
                }
                foreach (var button in buttons)
                {
                    var expected = button == target ? (DemoUiTheme.Current.Gold, new Vector2(4, -4), true) : resting[button];
                    if (!ReadOutline(button).Equals(expected) || button == target && !button.gameObject.activeInHierarchy)
                        throw new InvalidOperationException("演练提示边框状态不对：" + button.name);
                }
            }

            void ShowStep(string expected)
            {
                BaseDemoIntro.AdvanceTutorialDelay(intro, 1);
                tickGuide.Invoke(controller, null);
                if (!area.Find("PracticeInstruction").GetComponentInChildren<Text>().text.Contains(expected))
                    throw new InvalidOperationException("演练正文没有指向当前控件：" + expected);
                drill.PreviewHud.Refresh();
            }

            void ClearDelayCue()
            {
                tickGuide.Invoke(controller, null);
                ExpectCue();
            }

            void HoldPortrait()
            {
                var pointer = new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left };
                ExecuteEvents.Execute(portrait.gameObject, pointer, ExecuteEvents.pointerDownHandler);
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
                typeof(BattleSceneController).GetField("portraitPressTime", flags).SetValue(drill, Time.unscaledTime - .5f);
                typeof(BattleSceneController).GetMethod("UpdatePortraitHold", flags).Invoke(drill, null);
                ExecuteEvents.Execute(portrait.gameObject, pointer, ExecuteEvents.pointerUpHandler);
                EventSystem.current.SetSelectedGameObject(null);
                if (!drill.DetailsVisible || drill.DetailsSlot != 0)
                    throw new InvalidOperationException("长按左下角头像没有打开主角详情");
            }

            ShowStep("左下角");
            ExpectCue(portrait);
            CaptureViewports("ui-player-route-drill-select");
            ClickHudButton(portrait);
            ClearDelayCue();
            ShowStep("右下角");
            ExpectCue(pause);
            ClickHudButton(pause);
            ClearDelayCue();
            if (!drill.Automatic || (intro.DrillFlags & BaseDemoIntro.Pause) == 0)
                throw new InvalidOperationException("战斗时间按钮没有推进演练");

            intro.DrillFlags |= BaseDemoIntro.Move;
            ShowStep("挥");
            ExpectCue(mapTarget: true);
            HoldPortrait();
            ExpectCue();
            if (!drill.Realtime.Model.Paused || area.Find("CommanderDetails/Strategy_Attack").gameObject.activeInHierarchy)
                throw new InvalidOperationException("查看详情时未暂时暂停，或旧策略按钮仍可见");
            ClickHudButton(close);
            if (drill.Realtime.Model.Paused || !drill.Automatic)
                throw new InvalidOperationException("关闭详情后未恢复战斗");

            intro.DummyPhase = 2;
            ShowStep("进攻");
            ExpectCue(attack);
            Canvas.ForceUpdateCanvases();
            var attackBounds = RectTransformUtility.CalculateRelativeRectTransformBounds(attack.parent, attack);
            foreach (var strategy in new[] { "Manual", "Defense", "Pause", "Speed" })
            {
                var other = attack.parent.Find(strategy);
                if (other?.gameObject.activeInHierarchy != true ||
                    attackBounds.Intersects(RectTransformUtility.CalculateRelativeRectTransformBounds(attack.parent, other)))
                    throw new InvalidOperationException("进攻与其他策略按钮重叠");
            }
            if (!attack.GetComponent<Image>().raycastTarget)
                throw new InvalidOperationException("进攻按钮未接收界面输入");
            Capture("ui-player-route-drill-strategy");
            ClickHudButton(attack);
            ExpectCue(mapTarget: true);
            if (intro.DummyPhase != 3 || !drill.Automatic || !drill.WithdrawSceneEncounter())
                throw new InvalidOperationException("演练策略或返回村庄失败");
            Log.Add("[PASS] 序章演练实际控件有指引箭头，查看详情暂时暂停并恢复，进攻在统一操作栏");
        }

        private static void LoadRouteView(string fixture)
        {
            var path = Path.Combine("Logs", "demo-route-checkpoints", fixture + ".json");
            var state = JsonUtility.FromJson<BaseState>(File.ReadAllText(path));
            // The rules-only route did not advance presentation time between achievements.
            while (BaseAchievements.AdvanceNotice(state, 3) != null) { }
            BaseBattleGateway.MainMenu = false;
            controller.SaveStore.RestoreSession(state);
            if (fixture == "merchant")
                UnityEngine.SceneManagement.SceneManager.LoadScene(BaseBattleGateway.ExplorationScene);
            else controller.LoadSaved();
            stage = "route-" + fixture;
            frameDeadline = Time.frameCount + 20;
            stageAt = EditorApplication.timeSinceStartup;
        }

        private static void JourneyStage(string next)
        {
            stage=next;stageAt=EditorApplication.timeSinceStartup;frameDeadline=Time.frameCount+10;
        }
        private static void PressJourney(string label)
        {
            var surface=Object.FindAnyObjectByType<BaseWorldJourneyHud>();
            ClickHudButton(surface.transform.Find("JourneyPanel").GetComponentsInChildren<Button>().Single(b=>b.GetComponentInChildren<Text>().text==label).transform);
        }

        private static void VerifyFirstIntelReopened()
        {
            H.ClosePanel();
            H.OpenFirstRaidIntel();
            RequirePanel("TownTrade");
            H.ClosePanel();
            Approach("town_peddler");
            H.OpenObject("town_peddler");
            FinishDialogue();
            string reopen = BaseConquest.Story.StoryText.Get("story.ui.intel.first_reopen", "免费首防消息");
            string intel = BaseConquest.Story.StoryText.Get("story.ui.trade.intel_entry", "小道消息");
            Click(intel);
            Click("领取免费首防消息 · 0币");
            RequirePanel("TownTrade");
            int day = controller.State.Demo.Flow.FreeFirstRaidDay;
            var raid = controller.State.Campaign.Events.Single(item => item.Id == BaseDemoDefense.EventId);
            if (day != raid.AvailableDay || !BaseRaidIntel.Known(controller.State, raid) ||
                !BaseCalendar.Quarter(controller.State, BaseCalendar.QuarterNumber(day)).Single(item => item.Day == day).Raids.Contains(raid.Title))
                throw new InvalidOperationException("补领后首防消息和日历日期不一致");
            Capture("ui-player-route-first-intel");
            H.ClosePanel();
            H.OpenObject("town_peddler");
            FinishDialogue();
            Click(intel);
            if (RequirePanel("TownTrade").GetComponentsInChildren<Text>().Any(label => label.text == reopen))
                throw new InvalidOperationException("补领后小道消息仍显示免费首防消息");
            Log.Add("[PASS] 免费首防消息离开后可从老莫补领，日历日期一致，领取后入口消失");
        }

        private static bool RectWithin(RectTransform child, RectTransform ancestor)
        {
            Canvas.ForceUpdateCanvases();
            var bounds = new Vector3[4];
            child.GetWorldCorners(bounds);
            var area = ancestor.rect;
            return bounds.Select(ancestor.InverseTransformPoint).All(point =>
                point.x >= area.xMin - .1f && point.x <= area.xMax + .1f &&
                point.y >= area.yMin - .1f && point.y <= area.yMax + .1f);
        }

        private static bool RouteViewReady(Func<BaseState, bool> matches)
        {
            controller = C;
            bool ready = Time.frameCount >= frameDeadline && controller?.State != null && !controller.Busy &&
                Object.FindAnyObjectByType<SceneLoadingCover>() == null && matches(controller.State);
            if (!ready && EditorApplication.timeSinceStartup - stageAt > 30)
                throw new InvalidOperationException("连续路线存档未进入预期界面：" + controller?.Message);
            return ready;
        }

        private static void CheckHomeArrival()
        {
            var arrival = RequirePanel("NpcDialogue");
            if (controller.State.Demo.Flow.HomeEnterSeen)
                throw new InvalidOperationException("首次返乡开场读完前就写入了已读标记");
            H.ContinueDialogue();
            if (!arrival.Find("DialogueText").GetComponent<Text>().text.Contains("粮仓也被搬空"))
                throw new InvalidOperationException("首次入村未独立播放返乡开场");
            CaptureViewports("ui-player-route-home-arrival");
            string galanId = "partner_recruit:" + controller.State.Demo.Flow.FirstControllerId;
            bool shouldHearGalan = BaseAdventure.Following(controller.State, galanId);
            bool heardGalan = false;
            for (int i = 0; i < 24 && H.DialogueOpen; i++)
            {
                H.ContinueDialogue();
                if (H.DialogueOpen && arrival.Find("SpeakerName").GetComponent<Text>().text == "迦兰" &&
                    arrival.Find("DialogueText").GetComponent<Text>().text.Contains("脚印"))
                    heardGalan = true;
            }
            if (heardGalan != shouldHearGalan || H.PanelOpen || !controller.State.Demo.Flow.HomeEnterSeen ||
                !controller.SaveStore.Load().Demo.Flow.HomeEnterSeen)
                throw new InvalidOperationException("返乡开场没有按迦兰实际随行情况播放，或读完没有回到探索并保存");

            if(BaseSpatial.Objects(controller.State).Any(o=>o.Id=="demo_survivor"))
                throw new InvalidOperationException("村中仍有旧的指路人");
            Log.Add("[PASS] 返乡开场按实际阵容显示，村中恢复自由探索");
        }

        private static void CheckHomeRebuild()
        {
            var s=controller.State;
            string cost="修理 · 原料 "+BaseDemoDefense.RepairMaterialCost(s)+"　钱币 "+BaseDemoDefense.RepairCoinCost(s);
            foreach(var id in new[]{"farm","workshop","home"})
            {
                Approach(id);H.OpenObject(id);FinishDialogue();
                Click(cost);
                if(id=="home")
                {
                    FinishDialogue();Click("你们在里面做什么？");FinishDialogue();
                    var choices=((RectTransform)Field(H,"panel")).GetComponentsInChildren<Button>()
                        .Where(b=>b.interactable).Select(b=>b.GetComponentInChildren<Text>()?.text).ToArray();
                    var ask=choices.Single(label=>label!=null&&label.StartsWith("你们认"));
                    for(int i=0;i<2;i++){Click(ask);FinishDialogue();}
                    CaptureViewports("ui-player-route-home-rebuild");
                    Click("滚吧，别让我再见到你们。");FinishDialogue();
                }
                if(!BaseDemoFinale.HomeRepaired(controller.State,id))throw new InvalidOperationException(id+" 尚未修复");
            }
            CaptureViewports("ui-player-route-home-rebuilt-village");
        }

        private static void VerifyExplorationHelp()
        {
            Click("帮助");
            foreach (var topic in BaseExperienceRules.Help)
            {
                Click(topic.Title);
                VerifyHelpText(RequirePanel("CommonPanel"), topic.Text);
                Click("返回帮助");
            }
            Click("返回菜单");
            RequirePanel("CampMenu");
            Log.Add("[PASS] 探索菜单帮助六个主题可阅读，并可返回菜单");
        }

        private static void VerifyBattleHelp(BattleSceneController battle)
        {
            Transform Menu() => ((GameObject)Field(battle, "campaignMenu")).GetComponentsInChildren<RectTransform>()
                .Single(rect => rect.name == "选项面板");
            battle.OpenCampaignMenu();
            Press(Menu(), "Help");
            for (int i = 0; i < BaseExperienceRules.Help.Count; i++)
            {
                Press(Menu(), "HelpTopic" + i);
                VerifyHelpText(Menu().Find("HelpContent"), BaseExperienceRules.Help[i].Text);
                Press(Menu(), "Back");
            }
            Press(Menu(), "Back");
            Press(Menu(), "Continue");
            if (Field(battle, "campaignMenu") != null)
                throw new InvalidOperationException("未从帮助返回战场");
            Log.Add("[PASS] " + (battle.DeploymentDraftActive ? "部署" : "战斗") + "菜单可查看并退出共享帮助");
        }

        private static void VerifyHelpText(Transform view, string expected)
        {
            Canvas.ForceUpdateCanvases();
            var body = view.GetComponentsInChildren<Text>().SingleOrDefault(text => text.text == expected);
            if (body == null || body.preferredHeight > body.rectTransform.rect.height + 1)
                throw new InvalidOperationException("帮助正文未显示完整");
        }

        private static void VerifyResourceHud()
        {
            var strip = Field(H, "resourceStrip") as RectTransform;
            var stats = strip?.Find("Stats");
            var order = new[] { "Calendar", "Food", "Supplies", "Materials", "Coins", "Energy", "Troops" };
            if (stats == null || !stats.Cast<Transform>().Select(item => item.name).SequenceEqual(order))
                throw new InvalidOperationException("资源栏七项的顺序不符");
            if (strip.parent.Find("任务") != null || strip.parent.Find("i") != null || string.IsNullOrWhiteSpace(strip.Find("Location").GetComponent<Text>().text))
                throw new InvalidOperationException("资源栏仍有旧入口或缺少地点名");
            var positions = stats.Cast<RectTransform>().Select(item => item.anchoredPosition).ToArray();
            if (positions.Any(position => Mathf.Abs(position.y - positions[0].y) > .01f) || !positions.Select(position => position.x).SequenceEqual(positions.Select(position => position.x).OrderBy(x => x)))
                throw new InvalidOperationException("七项资源没有按顺序同排显示");

            var menu = RequirePanel("CampMenu");
            var removedMenuItems = new[] { "当前剧情目标", "资源与人员信息", "日历", "体力" };
            if (menu.GetComponentsInChildren<Button>().Select(b => b.GetComponentInChildren<Text>()?.text)
                .Any(title => title != null && removedMenuItems.Any(removed => title.StartsWith(removed, StringComparison.Ordinal))))
                throw new InvalidOperationException("系统菜单仍含剧情目标、资源说明、日历或体力入口");
            Click("任务 · N");
            if ((string)Field(H, "panelViewKey") != "quests") throw new InvalidOperationException("菜单任务入口没有打开任务列表");
            H.ClosePanel();

            if (Object.FindObjectsByType<RectTransform>().Any(t => t.name == "TrackedQuest"))
                throw new InvalidOperationException("仍显示常驻任务追踪");

            var expectedTitles = new[] { "日历", "粮食", "军需", "原料", "钱币", "体力", "兵力" };
            for (int i = 0; i < order.Length; i++)
            {
                ClickHudButton(stats.Find(order[i]));
                var popup = Field(H, "resourcePopover") as RectTransform;
                if (!H.ResourcePopoverOpen || H.PanelOpen || !popup.Find("Window/Title").GetComponent<Text>().text.StartsWith(expectedTitles[i], StringComparison.Ordinal))
                    throw new InvalidOperationException("资源入口没有打开对应的下拉：" + order[i]);
                var scroll = popup.Find("Window/Body").GetComponent<ScrollRect>();
                var body = scroll.content;
                if (body.childCount == 0 || popup.GetComponentsInChildren<Text>().Any(t => t.resizeTextForBestFit))
                    throw new InvalidOperationException("资源下拉缺少明细或开启了字号自适应：" + order[i]);
                if (body.rect.width > scroll.viewport.rect.width + 1)
                    throw new InvalidOperationException("资源明细超出可见宽度：" + order[i]);
                if (i == 0)
                {
                    var calendar = body.Find("ResourceCalendar");
                    int start = BaseCalendar.QuarterStart(controller.State.Day);
                    if (calendar.Find("Days").childCount != 15 || !calendar.Find("Days").GetChild(0).GetComponentInChildren<Text>().text.StartsWith(start.ToString(), StringComparison.Ordinal))
                        throw new InvalidOperationException("日历未显示本季度 15 天");
                    CaptureViewports("ui-skin-calendar");
                    ClickHudButton(calendar.Find("Next"));
                    var next = ((Transform)Field(H, "resourcePopover")).Find("Window/Body/Viewport/Content/ResourceCalendar");
                    if (next.Find("Next").GetComponent<Button>().interactable || !next.Find("Days").GetChild(0).GetComponentInChildren<Text>().text.StartsWith((start + 15).ToString(), StringComparison.Ordinal))
                        throw new InvalidOperationException("日历没有切到下季度或允许越界");
                    ClickHudButton(next.Find("Previous"));
                    if (BaseCalendar.QuarterNumber(controller.State.Day) == 1 && ((Transform)Field(H, "resourcePopover")).Find("Window/Body/Viewport/Content/ResourceCalendar/Previous").GetComponent<Button>().interactable)
                        throw new InvalidOperationException("日历允许翻到首季度以前");
                }
                else
                {
                    string name = i == 1 ? "ui-skin-resources" : "ui-skin-resource-" + order[i].ToLowerInvariant();
                    if (i == 1 || i == 6) CaptureViewports(name);
                    else Capture(name);
                }
            }

            Canvas.ForceUpdateCanvases();
            var lastPopup = (RectTransform)Field(H, "resourcePopover");
            var outside = lastPopup.TransformPoint(new Vector3(lastPopup.rect.xMax - 18, lastPopup.rect.yMin + 100, 0));
            var pointer = new PointerEventData(EventSystem.current) { position = RectTransformUtility.WorldToScreenPoint(null, outside) };
            var hits = new List<RaycastResult>();
            EventSystem.current.RaycastAll(pointer, hits);
            if (hits.Any(hit => hit.gameObject.transform.IsChildOf(lastPopup)))
                throw new InvalidOperationException("下拉挡住了弹窗外的地图区域");
            var state = controller.State;
            int x = Mathf.FloorToInt((float)state.PlayerX), y = Mathf.FloorToInt((float)state.PlayerY);
            controller.ClearSelection();
            controller.SelectCell(x, y);
            if (controller.SelectedCell == null || !H.ResourcePopoverOpen)
                throw new InvalidOperationException("资源下拉打开时不能照常选中地图格");
            controller.ClearSelection();
            var dismiss = controller.GetType().GetMethod("DismissResourcePopoverOutside", BindingFlags.Instance | BindingFlags.NonPublic);
            dismiss.Invoke(controller, new object[] { pointer.position });
            controller.SelectCell(x, y);
            if (H.ResourcePopoverOpen || H.PanelOpen || controller.SelectedCell == null)
                throw new InvalidOperationException("点击外部没有关闭下拉并保留同帧地图交互");
            controller.ClearSelection();
            Log.Add("[PASS] 七项资源下拉、日历翻页、点击外部关闭且地图可操作、菜单任务册、移除常驻追踪");
        }

        private static void ClickHudButton(Transform node)
        {
            var button = node == null ? null : node.GetComponent<Button>();
            if (button == null || !button.isActiveAndEnabled || !button.interactable)
                throw new InvalidOperationException("HUD 按钮不可操作：" + node?.name);
            button.onClick.Invoke();
        }

        private static void VerifyOpeningCopy()
        {
            const string playerName = "青岚";
            var state = controller.State;
            if (!BaseDemoPrologue.Village(state) || state.Demo.Intro.Step != 0)
                throw new InvalidOperationException("新档未停在白桦村选角");

            var arrow = Field(H, "questArrow") as MaskableGraphic;
            if (arrow == null || !arrow.isActiveAndEnabled || arrow.GetComponent<CanvasRenderer>() == null)
                throw new InvalidOperationException("选角任务箭头未显示或缺少 CanvasRenderer");
            Canvas.ForceUpdateCanvases();
            Log.Add("[PASS] 开局选角任务箭头进入画布渲染");

            var choice = Field(H, "heroChoiceLayer") as RectTransform;
            if (choice == null) throw new InvalidOperationException("新档未打开选角界面");
            ClickHudButton(choice.Find("HeroChoice_female"));
            ClickHudButton(choice.Find("确认选择"));
            var nameDialog = choice.Find("HeroName/NameConfirmation");
            var input = nameDialog.Find("PlayerNameInput").GetComponent<InputField>();
            var confirm = nameDialog.Find("ConfirmPlayerName").GetComponent<Button>();
            if (confirm.interactable) throw new InvalidOperationException("空姓名可以确认");
            input.text = "错/名";
            if (confirm.interactable) throw new InvalidOperationException("包含路径符号的姓名可以确认");
            input.text = playerName;
            if (!confirm.interactable) throw new InvalidOperationException("合法中文姓名不能确认");
            CaptureViewports("ui-skin-player-name");
            ClickHudButton(nameDialog.Find("返回选角"));
            string directory = BaseBattleGateway.SaveDirectory;
            if (controller.State.Demo.Intro.Step != 0 || !string.IsNullOrEmpty(controller.State.Demo.Intro.PlayerName)
                || Directory.Exists(directory) && Directory.EnumerateFiles(directory, "*.json").Any())
                throw new InvalidOperationException("取消填写姓名写入了进度");

            ClickHudButton(choice.Find("确认选择"));
            var currentNameDialog = ((RectTransform)Field(H, "heroNameLayer")).Find("NameConfirmation");
            currentNameDialog.Find("PlayerNameInput").GetComponent<InputField>().text = playerName;
            ClickHudButton(currentNameDialog.Find("ConfirmPlayerName"));
            state = controller.State;
            var catalog = BaseBattleGateway.Catalog();
            var namedEntry = catalog.Entries().FirstOrDefault(e => e.Error == null && e.PlayerName == playerName);
            if (state.Demo.Intro.Step != 1 || state.Demo.Intro.Hero != "female" ||
                state.Leaders.Single(l => l.Id == state.PlayerLeaderId).Name != playerName || namedEntry == null ||
                !Directory.EnumerateFiles(directory, playerName + "-auto*.json").Any() ||
                catalog.LoadSlot(namedEntry.Id).Demo.Intro.PlayerName != playerName)
                throw new InvalidOperationException("自定义姓名没有保存到主角和新版自动档");
            Log.Add("[PASS] 选角后填写姓名，拒绝空白与非法符号，取消零写入，中文姓名保存并可重读");

            H.ClosePanel();
            H.OpenQuests("achievement");
            H.ClosePanel();
            Canvas.ForceUpdateCanvases();
            var taskBar = (RectTransform)Field(H, "questBar");
            var pointer = new PointerEventData(EventSystem.current)
            {
                position = RectTransformUtility.WorldToScreenPoint(null, taskBar.TransformPoint(taskBar.rect.center)),
                button = PointerEventData.InputButton.Left
            };
            var taskBackground = taskBar.GetComponent<Image>();
            if (!taskBackground.isActiveAndEnabled || !taskBackground.raycastTarget ||
                !RectTransformUtility.RectangleContainsScreenPoint(taskBar, pointer.position) ||
                !taskBackground.Raycast(pointer.position, null) ||
                !ExecuteEvents.Execute(taskBar.gameObject, pointer, ExecuteEvents.pointerClickHandler) ||
                (string)Field(H, "questTab") != "tasks" || RequirePanel("CommonPanel").Find("Title").GetComponent<Text>().text != "任务与成就")
                throw new InvalidOperationException("点探索任务栏没有直接进入任务页");
            H.ClosePanel();
            Log.Add("[PASS] 点击探索任务栏直达任务，即使此前查看的是成就");
            VerifyBenchInteraction();
            Approach("demo_captain");
            H.OpenObject("demo_captain");
            FinishDialogue();
            Click("聊聊从前在王都值夜");
            H.ContinueDialogue();
            if (!RequirePanel("NpcDialogue").Find("DialogueText").GetComponent<Text>().text.Contains("宫门外"))
                throw new InvalidOperationException("长椅旧事分支未播放");
            FinishDialogue();
            if (controller.State.Demo.Intro.Step != 1)
                throw new InvalidOperationException("追问旧事提前推进了序章");
            Click("一起去，先向阿禾打个招呼");
            Approach("intro_villager");
            H.OpenObject("intro_villager");
            FinishDialogue();
            Click("我们去粮田安排");
            Approach("intro_farm");
            H.OpenObject("intro_farm");
            var villageFarm = RequirePanel("FacilityPanel");
            var villageActions = villageFarm.Find("Left/List").GetComponent<ScrollRect>().content;
            foreach (string row in new[] { "VillageFarmWorkers", "Ranged", "Cavalry", "Mage" })
            {
                var target = villageActions.Find(row);
                if (target == null || !target.gameObject.activeInHierarchy || !target.Find("Input").gameObject.activeInHierarchy ||
                    target.Find("Unlock").gameObject.activeSelf)
                    throw new InvalidOperationException("序章派工缺少预留兵种数量框，或显示了未开放的解锁操作：" + row);
            }
            Canvas.ForceUpdateCanvases();
            typeof(BaseHud).GetMethod("RefreshQuestCue", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(H, null);
            Capture("ui-village-farm-compact");
            var villageList = villageFarm.Find("Right/List").GetComponent<ScrollRect>().content;
            var villageRows = RosterUnits(villageList);
            if (villageRows.Length != 4) throw new InvalidOperationException("序章没有显示四名护村剑士");
            foreach (var card in villageRows) VerifyRosterRow(card, false);
            var quantity = villageActions.Find("VillageFarmWorkers/Input").GetComponent<InputField>();
            var submit = villageActions.Find("Apply").GetComponent<Button>();
            var cancelStaff = villageActions.Find("Cancel").GetComponent<Button>();
            Transform Guided() => (RectTransform)Field(H, "questFarmTarget");
            if (!quantity.isActiveAndEnabled || !quantity.interactable || quantity.text != "2" || submit.interactable)
                throw new InvalidOperationException("序章初始派工人数或控件状态错误");
            void EnterCount(string value)
            {
                quantity.text = value;
                quantity.onEndEdit.Invoke(value);
            }
            EnterCount("4");
            if (quantity.text != "2" || submit.interactable)
                throw new InvalidOperationException("数字输入抢走了铁匠铺的在岗工人");
            EnterCount("1");
            if (quantity.text != "1" || !submit.interactable) throw new InvalidOperationException("输入人数没有暂时调出在岗士兵");
            EnterCount("2");
            if (quantity.text != "2" || submit.interactable) throw new InvalidOperationException("数字未重新匹配同一名闲置士兵");
            var guidedFirst = Guided();
            if (!villageRows.Contains(guidedFirst) || !guidedFirst.Find("Status").GetComponent<Text>().text.Contains("铁匠铺"))
                throw new InvalidOperationException("序章箭头没有指向第一名可调士兵");
            ClickHudButton(guidedFirst);
            if (quantity.text != "3" || Guided() == guidedFirst || Guided() == submit.transform || !cancelStaff.interactable)
                throw new InvalidOperationException("点名后人数或下一人指引未更新");
            ClickHudButton(Guided());
            if (quantity.text != "4" || Guided() != submit.transform || !submit.interactable || controller.State.Demo.Intro.FarmWorkers != 2)
                throw new InvalidOperationException("第四名士兵后未指向确认方案，或草案提前提交");
            ClickHudButton(submit.transform);
            H.ContinueDialogue();
            if (controller.State.Demo.Intro.Step != 10 || controller.State.Demo.Intro.FarmWorkers != 4 ||
                !RequirePanel("NpcDialogue").Find("DialogueText").GetComponent<Text>().text.Contains("晚麦"))
                throw new InvalidOperationException("调工到四人后未接入阿禾回应");
            H.ContinueDialogue();
            H.ContinueDialogue();
            if (!RequirePanel("NpcDialogue").Find("DialogueText").GetComponent<Text>().text.Contains("来找我拿剑"))
                throw new InvalidOperationException("调工后未接入搭档领剑指引");
            FinishDialogue();
            if (H.DialogueOpen || BaseQuestLog.Cue(controller.State).ObjectId != "demo_captain")
                throw new InvalidOperationException("调工回应结束后未继续领剑主线");
            Log.Add("[PASS] 长椅旧事汇回主线；粮田数字调出与匹配闲置士兵、按引导逐名调入铁匠铺两人、确认四人后接阿禾与搭档对白");
        }

        private static void VerifyBenchInteraction()
        {
            foreach (string hero in new[] { "player", "female" })
            {
                var atlas = PixelDemoArt.Motion(hero, "sit");
                if (atlas == null || atlas.width != 320 || atlas.height != 80)
                    throw new InvalidOperationException("缺少完整坐姿图集：" + hero);
            }
            var bench = controller.MapObjects.Single(o => o.Id == "village_bench");
            var view = ((Transform)Field(controller, "player")).GetComponent<PlayerHeroView>();
            var move = typeof(BaseSceneController).GetMethod("MoveExploration", BindingFlags.Instance | BindingFlags.NonPublic);
            foreach (int side in new[] { -1, 1 })
            {
                double x = bench.X + side * bench.Width * .29;
                double y = bench.Y - bench.Height / 2 - BaseMap.Radius - .06;
                if (!controller.Change(s => { s.PlayerX = x; s.PlayerY = y; }, "") ||
                    controller.Nearby?.Id != bench.Id + (side < 0 ? ":seat:left" : ":seat:right"))
                    throw new InvalidOperationException("长椅没有对应的可达座位");
                var catalog = BaseBattleGateway.Catalog();
                var before = SnapshotSaves();
                controller.Interact();
                move.Invoke(controller, new object[] { Vector2.zero, .05 });
                move.Invoke(controller, new object[] { Vector2.down, .05 });
                if (view.Pose != HeroPose.Idle || H.PanelOpen || !before.SequenceEqual(SnapshotSaves()))
                    throw new InvalidOperationException("坐到一半起身应回到探索且不写档");
                controller.Interact();
                for (int n = 0; n < 9; n++) move.Invoke(controller, new object[] { Vector2.zero, .05 });
                if (view.Pose != HeroPose.Seated || view.Facing != HeroFacing.South || view.AtlasFrame != 3 ||
                    controller.State.PlayerX != x || controller.State.PlayerY != y || controller.State.Demo.Intro.Step != 1)
                    throw new InvalidOperationException("长椅坐姿变更了实际位置或剧情");
                var after = SnapshotSaves();
                var writes = after.Except(before).Select(line => line.Substring(0, line.IndexOf(':'))).ToArray();
                var changedSlots = writes.Where(name => name.EndsWith(".json", StringComparison.Ordinal)).ToArray();
                if (H.PanelOpen || changedSlots.Length != 1 || !BaseSaveCatalog.AutoSlots.Any(id =>
                    changedSlots[0].EndsWith("-" + id + ".json", StringComparison.Ordinal)))
                    throw new InvalidOperationException("坐稳应只写一个自动槽，继续显示地图");
                var entry = catalog.Entries().Single(item =>
                    Path.GetFileName(catalog.Store(item.Id).Path) == changedSlots[0]);
                var saved = catalog.LoadSlot(entry.Id);
                if (entry.Name != "长椅休息" || saved.PlayerX != x || saved.PlayerY != y ||
                    !BaseSpatial.Walkable(saved, x, y) || saved.Demo.Intro.Step != 1 ||
                    saved.Demo.Intro.PlayerName != controller.State.Demo.Intro.PlayerName)
                    throw new InvalidOperationException("长椅自动档应读回椅前落点与剧情");
                for (int n = 0; n < 9; n++) move.Invoke(controller, new object[] { Vector2.zero, .05 });
                if (!after.SequenceEqual(SnapshotSaves())) throw new InvalidOperationException("持续坐着不应重复写档");
                if (side < 0)
                {
                    CaptureViewports("ui-skin-bench-sit");
                    controller.Interact();
                }
                for (int n = 0; n < 8; n++) move.Invoke(controller, new object[] { side < 0 ? Vector2.zero : Vector2.down, .05 });
                if (view.Pose != HeroPose.Idle || controller.State.PlayerX != x || controller.State.PlayerY != y)
                    throw new InvalidOperationException("完成起身后没有回到原站位");
            }
            Log.Add("[PASS] 两侧长椅坐稳各写一个自动槽，中断与持续坐着零写入，读回椅前落点与剧情，两种起身均可用");
        }

        private static void VerifyAuthoredLayout()
        {
            int index = BasePartyInventory.FreeCell(controller.State, controller.State.PlayerLeaderId);
            if (index < 0) throw new InvalidOperationException("场景布局检查需要一个空背包格");
            string path = "BagSlot_" + index;
            var source = (RectTransform)controller.transform.Find("UI 布局源/PartyMemberCard/" + path);
            var icon = (RectTransform)source.Find("ItemIcon");
            var originalPosition = source.anchoredPosition;
            var originalIconPosition = icon.anchoredPosition;
            try
            {
                source.anchoredPosition += new Vector2(7, -3);
                icon.anchoredPosition += Vector2.up * 5;
                H.ClosePanel();
                H.OpenBackpack();
                var card = RequirePanel("PartyInventory").Find("Cards").GetChild(0);
                var live = (RectTransform)card.Find(path);
                if (Vector2.Distance(source.anchoredPosition, live.anchoredPosition) > .01f
                    || Vector2.Distance(icon.anchoredPosition, ((RectTransform)live.Find("ItemIcon")).anchoredPosition) > .01f)
                    throw new InvalidOperationException("背包格运行时覆盖了场景位置或预制体内的图标位置");
                Log.Add("[PASS] 场景格子位置与预制体图标位置在重新打开时生效");
            }
            finally { source.anchoredPosition = originalPosition; icon.anchoredPosition = originalIconPosition; }
        }

        private static Rect SceneRect(Transform node, Transform relativeTo)
        {
            var corners = new Vector3[4];
            ((RectTransform)node).GetWorldCorners(corners);
            var min = relativeTo.InverseTransformPoint(corners[0]);
            var max = relativeTo.InverseTransformPoint(corners[2]);
            return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }

        private static void VerifyPartyCardLayout(int count)
        {
            var view = (RectTransform)RequirePanel("PartyInventory");
            var source = controller.transform.Find("UI 布局源");
            var authoredView = (RectTransform)source.Find("PartyInventory/FrameWidths/" + count);
            var authoredCard = (RectTransform)source.Find("PartyMemberCard");
            var group = (RectTransform)view.Find("Cards");
            Canvas.ForceUpdateCanvases();
            if (group.GetComponentsInChildren<BattleAvatarGraphic>().Length != 0 || group.GetComponentsInChildren<RawImage>().Count(image =>
                    image.name == "全身像" || image.name == "骑乘取景") != count)
                throw new InvalidOperationException("背包人物卡未统一使用角色全身像");
            var cards = group.Cast<RectTransform>().Select(card => SceneRect(card, group)).ToArray();
            if (view.rect.size != authoredView.rect.size || cards.Length != count ||
                cards.Any(card => Vector2.Distance(card.size, authoredCard.rect.size) > .1f) ||
                Mathf.Abs(cards.Average(card => card.center.x) - group.rect.center.x) > .1f ||
                cards.Any(card => Mathf.Abs(card.center.y - cards[0].center.y) > .1f) ||
                cards.Zip(cards.Skip(1), (left, right) => left.xMax > right.xMin).Any(overlaps => overlaps))
                throw new InvalidOperationException("独立背包没有沿用固定外窗和等宽居中角色卡：" + count);
            foreach (var cell in group.GetComponentsInChildren<PartyInventoryCell>())
            {
                if (cell.Cell < 0 && string.IsNullOrEmpty(cell.Slot)) continue;
                var rect = ((RectTransform)cell.transform).rect;
                if (Mathf.Abs(rect.width - rect.height) > .1f)
                    throw new InvalidOperationException("物品格或装备槽应为正方形：" + cell.name);
                var slotLabel = cell.transform.Find("SlotLabel").GetComponent<Text>();
                if (!string.IsNullOrEmpty(cell.Slot) && string.IsNullOrEmpty(slotLabel.text))
                    throw new InvalidOperationException("装备格缺少槽位标识：" + cell.name);
                if (!string.IsNullOrEmpty(cell.ItemId) && !cell.transform.Find("ItemIcon").GetComponent<Image>().enabled)
                    throw new InvalidOperationException("有物品的格子缺少图标：" + cell.name);
            }
            foreach (var person in controller.State.Leaders.Where(leader => BaseCommanderIdentity.Appearance(leader) == "azhu"))
            {
                var card = group.Find("人物与背包_" + person.Id);
                if (card == null) continue;
                var image = card.Find("Portrait/全身像/骑乘取景")?.GetComponent<RawImage>();
                if (image == null || Mathf.Abs((1 - image.uvRect.yMax) * image.texture.height - 14) > .01f ||
                    Mathf.Abs((1 - image.uvRect.yMin) * image.texture.height - 60) > .01f ||
                    Mathf.Abs(image.rectTransform.rect.height / ((RectTransform)image.transform.parent).rect.height - .875f) > .01f)
                    throw new InvalidOperationException("阿铸背包全身像应从头巾裁到马蹄，并与步行人物使用相同的可见高度");
                Log.Add("[PASS] 阿铸背包取景从头巾到马蹄，枪尖不占人物显示高度");
            }
            Log.Add("[PASS] 背包 " + count + " 人，外窗与角色卡尺寸从场景读取，卡组居中");
        }

        private static void VerifyTwoMemberBackpack()
        {
            H.ClosePanel();
            string member = controller.State.Adventure.Members.Last();
            if (!controller.Change(state => state.Adventure.Members.Remove(member), "界面检查"))
                throw new InvalidOperationException(controller.Message);
            H.OpenBackpack();
            VerifyPartyCardLayout(2);
            CaptureViewports("ui-skin-backpack-two");
            H.ClosePanel();
            if (!controller.Change(state => state.Adventure.Members.Add(member), "界面检查"))
                throw new InvalidOperationException(controller.Message);
            H.OpenBackpack();
        }

        private static void VerifyHideoutUi()
        {
            void StandWest()
            {
                if (!controller.Change(s =>
                {
                    var west = BaseSpatial.Nearest(s, new BasePoint(14.5, 12.5));
                    s.PlayerX = west.X; s.PlayerY = west.Y;
                    BaseAdventure.Arrive(s);
                }, "界面检查")) throw new InvalidOperationException(controller.Message);
            }
            StandWest();
            var refreshSight = controller.GetType().GetMethod("RefreshSceneSight", BindingFlags.Instance | BindingFlags.NonPublic);
            void RefreshSight() => refreshSight.Invoke(controller, new object[] { true });
            RefreshSight();
            var entrance = BaseSpatial.Objects(controller.State).Single(o => o.Id == BaseBanditHideout.EntryId);
            var cell = new GridPosition((int)entrance.X, (int)entrance.Y);
            var landmarks = ((BattleBoardView)Field(controller, "regionView")).Root.transform.Find("RegionLandmarks");
            var landmark = landmarks.Cast<Transform>().Single(view => view.name.StartsWith(entrance.Id + " · "));
            bool Presented() => controller.PresentedObject(entrance.Id) != null && landmark.gameObject.activeInHierarchy;
            if (controller.SceneCellKnown(cell) || controller.PresentedObject(entrance.Id) != null || landmark.gameObject.activeInHierarchy)
                throw new InvalidOperationException("浓雾未遮住窝点贴图及名称");
            Capture("ui-bandit-hideout-before-wall");
            Approach(BaseBanditHideout.EntryId);
            RefreshSight();
            if (!Presented()) throw new InvalidOperationException("绕到墙后仍不能发现窝点");
            CaptureViewports("ui-bandit-hideout-entrance");
            StandWest();
            RefreshSight();
            if (((BattleVision)Field(controller, "explorationSight")).Visible(BattleSide.A, cell) || !Presented())
                throw new InvalidOperationException("回到墙前后窝点未在薄雾下保留贴图及名称");
            Capture("ui-bandit-hideout-thin-fog");
            Approach(BaseBanditHideout.EntryId);
            RefreshSight();
            H.OpenObject(BaseBanditHideout.EntryId);
            var preparation = RequirePanel("BattlePreparation");
            var draft = (BaseBattlePreparation)Field(H, "battleDraft");
            if (draft.Route != BasePrepRoute.Hideout || draft.KnownEnemyCount() <= 0 ||
                !preparation.Find("Enemy/Value").GetComponent<Text>().text.Contains("敌方"))
                throw new InvalidOperationException("窝点没有进入备战并公开敌方阵容");
            if (preparation.Find("Sweep").gameObject.activeInHierarchy || preparation.Find("Reward").gameObject.activeInHierarchy ||
                !preparation.GetComponentsInChildren<Text>().Any(label => label.text.Contains("体力") && label.text.Contains("首通")))
                throw new InvalidOperationException("窝点首战没有显示体力规则，或提前显示扫荡及待领奖");
            if (draft.RequiredSupply(false) == 0)
            {
                preparation.Find("Teams").GetChild(0).Find("Melee/Input").GetComponent<InputField>().onEndEdit.Invoke("1");
                preparation = RequirePanel("BattlePreparation");
            }
            var supply = preparation.Find("Supply");
            if (supply.Find("Grade2").gameObject.activeSelf || !supply.Find("Grade1").gameObject.activeInHierarchy)
                throw new InvalidOperationException("军需没有合成单一切换按钮");
            SetPreparationSupply(preparation,1);
            preparation = RequirePanel("BattlePreparation");
            CaptureViewports("ui-bandit-hideout-preparation");
            Press(preparation, "Continue");
            var battle = RequireBattleDraft();
            Press(BattleSafeArea(battle), "DraftPreparationControls/Auto");
            CaptureViewports("ui-bandit-hideout-deployment");
            Capture("ui-bandit-hideout-deployment-wide", 1680, 720);
            VerifyBattleMenu(battle, draft);
            if (controller.State.Campaign.HasActiveBattle || !BaseSceneRules.Entrance(controller.State))
                throw new InvalidOperationException("窝点部署提前切图或写入战斗");
            Press(BattleSafeArea(battle), "DraftPreparationControls/Back");
            if (!ReferenceEquals(draft, Field(H, "battleDraft"))) throw new InvalidOperationException("返回备战重抽了窝点");
            Press(RequirePanel("BattlePreparation"), "Continue");
            battle = RequireBattleDraft();
            Press(BattleSafeArea(battle), "DraftPreparationControls/Start");
            if (H.DialogueOpen || battle.Realtime == null || !BaseBanditHideout.InHideout(controller.State) ||
                battle.Realtime.Session.Opening.Enemies.Count != draft.KnownEnemyCount() ||
                battle.Realtime.Battle.Soldiers.Any(unit => unit.Side == BattleSide.B && unit.Kind == SoldierKind.Hero) ||
                battle.Realtime.RuinsExposureFraction != 0)
                throw new InvalidOperationException("窝点开战存在剧情、侵蚀或阵容偏差");
            CaptureViewports("ui-bandit-hideout-battle");
            VerifyBattleMenu(battle, null);
            if (!battle.WithdrawSceneEncounter() || battle.Realtime.Battle.Report != null)
                throw new InvalidOperationException("窝点撤退应先下令，实际离场后再结算");
            AdvanceBattleToExit(battle);
            battle.ReturnCampaign();
            if (!BaseSceneRules.Entrance(controller.State))
                throw new InvalidOperationException("窝点撤退没有回到入口");
            Log.Add("[PASS] 浓雾隐藏窝点，绕墙后发现，薄雾保留贴图与名称；预览阵容与实战一致，撤退回入口");
        }

        private static void VerifyRuinsSoloRetreat()
        {
            Approach("demo_ruins_investigate");
            H.OpenObject("demo_ruins_investigate");
            if (H.DialogueOpen) FinishDialogue();
            var prep = RequirePanel("BattlePreparation");
            if (((BaseBattlePreparation)Field(H, "battleDraft")).RequiredSupply(false) > 0)
                SetPreparationSupply(prep,1);
            Press(RequirePanel("BattlePreparation"), "Continue");
            var battle = RequireBattleDraft();
            VerifyDeploymentPointer(battle);
            Press(BattleSafeArea(battle), "DraftPreparationControls/Start");
            FinishDialogue();
            Click("继续推进");
            if (H.DialogueOpen) FinishDialogue();
            CaptureViewports("ui-skin-ruins-combat", mobile: true);
            var player = battle.Realtime.Model.Player;
            player.Health = player.MaxHealth * .5;
            battle.ResumeAfterDialogue(true);
            battle.AdvanceRealtime(.02);
            if (!battle.Realtime.RuinsHalfHealthRetreat || battle.Realtime.Battle.Report?.Outcome != BattleOutcome.Retreat ||
                battle.Realtime.Model.Units.Any(unit => unit.Escaped))
                throw new InvalidOperationException("主角半血应直接离开，不用走向出口");
            battle.ReturnCampaign();
            if (controller.State.Campaign.HasActiveBattle || !BaseSceneRules.Entrance(controller.State))
                throw new InvalidOperationException("主角半血没有真实撤退并回到遗迹入口");
            var dialogue = RequirePanel("NpcDialogue");
            H.ContinueDialogue();
            Click("想想还能找谁帮忙");
            H.ContinueDialogue();
            if (dialogue.Find("DialogueText").GetComponent<Text>().text != "对方人多势众，优势不在我，或许镇上那位年轻铁匠可以帮忙。" ||
                new[] { "DialogueHint", "AdvanceButton", "Replies" }.Any(path => dialogue.Find(path).gameObject.activeInHierarchy))
                throw new InvalidOperationException("撤退独白仍有额外提示或选项");
            Capture("ui-skin-ruins-solo-retreat");
            H.ContinueDialogue();
            if (H.PanelOpen) throw new InvalidOperationException("独白收起后又显示了其他面板");
            Log.Add("[PASS] 古窟单人半血真实撤退，只有主角独白，收起后回探索");
        }

        private static void TickRuinsExit()
        {
            controller = C;
            if (controller?.State == null || controller.Busy || Object.FindAnyObjectByType<SceneLoadingCover>() != null) return;
            var state = controller.State;
            switch (stage)
            {
                case "ruins-exit-boot":
                    controller.LoadDemoStage("camp", "male");
                    stage = "ruins-exit-camp";
                    break;
                case "ruins-exit-camp":
                    if (state.Demo.Stage != BaseDemoStage.Waking) return;
                    H.ClosePanel();
                    Approach("home");
                    if (!controller.Change(s =>
                    {
                        if (!BaseDemo.AcknowledgeWaking(s)) throw new InvalidOperationException("苏醒未完成");
                    }, "")) throw new InvalidOperationException(controller.Message);
                    Approach(BaseDemoNpcs.CampScout);
                    controller.Interact();
                    FinishDialogue();
                    Click("为什么？");
                    FinishDialogue();
                    if (H.PanelOpen || controller.State.Demo.Defense.BatuStoryDay != 0)
                        throw new InvalidOperationException("陈安闲聊没有正常结束，或触发了首防剧情");
                    // Walk through the east doorway with the same movement handler as keyboard / touch input.
                    var east = controller.MapObjects.Single(obj => obj.Id == "exit_east");
                    controller.State.PlayerX = east.X - east.Width / 2 - BaseMap.Radius - .16;
                    controller.State.PlayerY = east.Y;
                    for (int step = 0; step < 32 && controller.State.Campaign.Location == "base"; step++) MoveSceneExplorer(Vector2.right);
                    stage = "ruins-exit-town";
                    break;
                case "ruins-exit-town":
                    if (state.Campaign.Location != "town") throw new InvalidOperationException("步行到东口应直接进入灰岩镇：" + controller.Message);
                    var west = controller.MapObjects.Single(obj => obj.Id == "travel_base");
                    if (GameObject.Find(west.Id + " · " + west.Name)?.GetComponentsInChildren<Renderer>()
                            .Any(r => r.name == "PixelDemo_road-sign-left") != true)
                        throw new InvalidOperationException("西口没有木质左路牌");
                    Approach("demo_ruins_exit");
                    controller.TravelDemo("outpost");
                    stage = "ruins-exit-entrance";
                    break;
                case "ruins-exit-entrance":
                    if (!BaseSceneRules.Entrance(state)) throw new InvalidOperationException(controller.Message);
                    Approach("demo_ruins_investigate");
                    controller.Interact();
                    if (H.ContinueDialogue() || controller.State.Demo.RuinsPlayerHeard)
                        throw new InvalidOperationException("洞口的主角独白在玩家选择前已播放");
                    Click("这遗迹不对劲，我感觉身体很热");
                    if (!controller.State.Demo.RuinsPlayerHeard || controller.State.Demo.RuinsAzhuHeard || controller.State.Demo.RuinsAheHeard)
                        throw new InvalidOperationException("独自到达应只记录主角标记");
                    Press(RequirePanel("BattlePreparation"), "Continue");
                    stage = "ruins-exit-first-greeting";
                    break;
                case "ruins-exit-first-greeting":
                    var firstDraft = RequireBattleDraft();
                    VerifyDeploymentPointer(firstDraft);
                    Press(BattleSafeArea(firstDraft), "DraftPreparationControls/Start");
                    FinishDialogue();
                    var firstGreeting = RequirePanel("NpcDialogue");
                    if (firstGreeting.Find("SpeakerName").GetComponent<Text>().text != BaseScriptNames.Galan ||
                        firstGreeting.Find("PortraitFrame/SpeakerPortrait").GetComponent<BattleAvatarGraphic>().enabled)
                        throw new InvalidOperationException("首次隔空喊话必须自报姓名并保留未知肖像");
                    Click("先退出去准备一下");
                    if (controller.State.Campaign.HasActiveBattle || !controller.State.Demo.RuinsControllerHeard)
                        throw new InvalidOperationException("暂缓开战后未保留初遇标记");
                    BaseBattleGateway.Catalog().SaveSlot("slot2", controller.State, "迦兰自报姓名后暂缓开战");
                    stage = "ruins-exit-reenter";
                    break;
                case "ruins-exit-reenter":
                    Approach("demo_ruins_investigate");
                    H.OpenObject("demo_ruins_investigate");
                    Press(RequirePanel("BattlePreparation"), "Continue");
                    stage = "ruins-exit-deploy";
                    break;
                case "ruins-exit-deploy":
                    var draft = RequireBattleDraft();
                    VerifyDeploymentPointer(draft);
                    Press(BattleSafeArea(draft), "DraftPreparationControls/Start");
                    FinishDialogue();
                    var known = RequirePanel("NpcDialogue");
                    if (known.Find("DialogueText").GetComponent<Text>().text != "怎么又是你，没完了是吧" ||
                        !known.Find("PortraitFrame/SpeakerPortrait").GetComponent<BattleAvatarGraphic>().enabled)
                        throw new InvalidOperationException("首通前重进缺少迦兰肖像或重遇台词");
                    Click("继续推进");
                    if (H.DialogueOpen) FinishDialogue();
                    // This regression begins with the controller's defeat; all extraction movement uses the battlefield input.
                    var encounter = Object.FindObjectsByType<BattleSceneController>().Single(b => b.IsDemoRuins);
                    var model = encounter.Realtime;
                    var source = model.Battle.Commander(BattleSide.B, 0);
                    var actor = model.Model.Units.Single(u => u.Id == source.Id + 1);
                    actor.Health = source.Health = 1;
                    actor.KnockedDown = source.KnockedDown = source.DeathResolved = true;
                    if (model.Model.Paused) encounter.ToggleRealtimePause();
                    stage = "ruins-exit-rescue";
                    stageAt = EditorApplication.timeSinceStartup;
                    break;
                case "ruins-exit-rescue":
                    if (!H.PanelOpen)
                    {
                        if (EditorApplication.timeSinceStartup - stageAt > 12) throw new InvalidOperationException("击败迦兰后未进入洞内对白");
                        return;
                    }
                    FinishDialogue();
                    Click("去出口");
                    stage = "ruins-exit-order";
                    frameDeadline = Time.frameCount + 4;
                    break;
                case "ruins-exit-order":
                    if (Time.frameCount < frameDeadline) return;
                    var battle = Object.FindObjectsByType<BattleSceneController>().Single(b => b.IsDemoRuins);
                    var run = battle.Realtime;
                    if (!run.RuinsExitOpen || run.AwaitingRescueDialogue || H.PanelOpen)
                        throw new InvalidOperationException("洞内对白读完后未恢复现场操控");
                    var exit = run.Battle.ExitCell(BattleSide.A);
                    var view = battle.PreviewBoard;
                    view.Follow(BattleBoardView.World(exit, run.Battle.Board));
                    Canvas.ForceUpdateCanvases();
                    var screen = BattleScreen(battle, run.Battle.Board, exit);
                    Capture("ui-ruins-first-extraction");
                    WithBattleTouch(battle, "ReadTouchPointer", touch =>
                    {
                        touch(1, TouchPhase.Began, screen);
                        touch(1, TouchPhase.Ended, screen);
                    });
                    if (run.Model.Player.Order != PrototypeOrder.Retreat)
                        throw new InvalidOperationException($"点撤离格 {exit} 未下达撤离指令：{battle.Feedback}，屏幕 {screen}");
                    if (run.Model.Paused) battle.ToggleRealtimePause();
                    Log.Add("[PASS] 正式部署开战，控制者倒地触发洞内对白；点去出口、再从触摸输入点击出口格，下达队长撤离");
                    stage = "ruins-exit-outside";
                    stageAt = EditorApplication.timeSinceStartup;
                    break;
                case "ruins-exit-outside":
                    if (state.Demo.RuinsDay == 0 || !H.PanelOpen)
                    {
                        if (EditorApplication.timeSinceStartup - stageAt > 35) throw new InvalidOperationException("撤离命令后未在洞外接到获救村民对白：" + controller.Message);
                        return;
                    }
                    if (!BaseSceneRules.Entrance(state) || !string.IsNullOrEmpty(BaseRuinsRewards.PendingBattle(state)))
                        throw new InvalidOperationException("队长抵达后没有返回遗迹入口或意外生成首通奖励");
                    FinishDialogue();
                    Click("路上小心，我们城里见");
                    if (H.PanelOpen) throw new InvalidOperationException("洞外对白未收起");
                    if (!controller.Change(s =>
                    {
                        var target = BaseSpatial.Objects(s).Single(o => o.Id == BaseDemoStories.GalanMarker);
                        var point = BaseSpatial.Nearest(s, new BasePoint(target.X + 1.25, target.Y - .25));
                        s.PlayerX = point.X; s.PlayerY = point.Y;
                        BaseInteraction.Walk(new BasePoint(target.X - point.X, target.Y - point.Y));
                        BaseAdventure.Arrive(s);
                    }, "")) throw new InvalidOperationException(controller.Message);
                    stage = "ruins-exit-galan";
                    stageAt = EditorApplication.timeSinceStartup;
                    break;
                case "ruins-exit-galan":
                    if (state.Demo.Rescue.Stage != 3)
                    {
                        if (EditorApplication.timeSinceStartup - stageAt > 25) throw new InvalidOperationException("获救村民未能动身回镇");
                        return;
                    }
                    var objects = (BaseMapObject[])Field(H, "labelObjects");
                    var labels = (RectTransform[])Field(H, "labels");
                    var portalLabel = labels[Array.FindIndex(objects, o => o.Id == "demo_ruins_investigate")];
                    var galanLabel = labels[Array.FindIndex(objects, o => o.Id == BaseDemoStories.GalanMarker)];
                    if (!portalLabel.gameObject.activeInHierarchy || !galanLabel.gameObject.activeInHierarchy ||
                        SceneRect(portalLabel, portalLabel.parent).Overlaps(SceneRect(galanLabel, portalLabel.parent)))
                        throw new InvalidOperationException("迦兰姓名与遗迹入口标签应同时可见且各占独立位置");
                    Capture("ui-ruins-galan-waiting");
                    BaseBattleGateway.Catalog().SaveSlot("slot1", controller.State, "洞外处置分支走查");
                    controller.Interact();
                    FinishDialogue();
                    CaptureViewports("ui-ruins-galan-questions", mobile: true);
                    Click("遗迹里有什么？");
                    FinishDialogue();
                    Click("遗迹里有什么？");
                    FinishDialogue();
                    Click("关于村子，你长得有点像……");
                    FinishDialogue();
                    Click("你确实控制了他们");
                    FinishDialogue();
                    Click("我还想了解一些事");
                    FinishDialogue();
                    Click("你先待在这");
                    if (H.PanelOpen || !BaseDemoStories.ControllerDialogue(controller.State, state.Demo.Flow.FirstControllerId))
                        throw new InvalidOperationException("暂留后没有回到探索，或迦兰已被处置");
                    controller.Interact();
                    FinishDialogue();
                    Click("你确实控制了他们");
                    FinishDialogue();
                    Click("我看你刚才倒挺想杀我的");
                    FinishDialogue();
                    Click("我要再想想");
                    FinishDialogue();
                    controller.Interact();
                    FinishDialogue();
                    Click("你确实控制了他们");
                    FinishDialogue();
                    Click("我怎么相信你？");
                    FinishDialogue();
                    Click("不论如何，操控百姓当斩");
                    if (!H.DialogueOpen || !BaseDemoStories.ControllerDialogue(controller.State, state.Demo.Flow.FirstControllerId))
                        throw new InvalidOperationException("选择处斩后，惊呼还未播完便提交了处置");
                    var cry = RequirePanel("NpcDialogue").Find("DialogueText").GetComponent<Text>();
                    if (cry.text != "啊呀！") H.ContinueDialogue();
                    if (cry.text != "啊呀！") throw new InvalidOperationException("处斩未播放迦兰的惊呼");
                    H.ContinueDialogue();
                    stage = "ruins-exit-galan-fallen";
                    stageAt = EditorApplication.timeSinceStartup;
                    break;
                case "ruins-exit-galan-fallen":
                    if (EditorApplication.timeSinceStartup - stageAt < .8) return;
                    var body = GameObject.Find("GalanFallen");
                    var actorInk = new MaterialPropertyBlock();
                    bool fallenArt = body != null && body.GetComponentsInChildren<Renderer>().Any(renderer => {
                        renderer.GetPropertyBlock(actorInk);
                        return actorInk.GetTexture("_BaseMap")?.name.IndexOf("down", StringComparison.OrdinalIgnoreCase) >= 0;
                    });
                    if (!fallenArt || H.DialogueOpen || controller.PresentedObject(BaseDemoStories.GalanMarker) != null)
                        throw new InvalidOperationException("迦兰处斩后应呈现倒地贴图，并立即失去交互与任务标记");
                    Capture("ui-ruins-galan-fallen");
                    Approach("demo_ruins_return");
                    controller.TravelDemo("town");
                    Approach("demo_ruins_exit");
                    controller.TravelDemo("outpost");
                    if (!BaseSceneRules.Entrance(controller.State) || GameObject.Find("GalanFallen") != null ||
                        controller.PresentedObject(BaseDemoStories.GalanMarker) != null)
                        throw new InvalidOperationException("离开再进遗迹，迦兰或遗体仍然出现");
                    Log.Add("[PASS] 陈安封路问答、遗迹首胜与实走撤离、迦兰循环问答和暂缓重谈、处斩惊呼、倒地贴图及切图移除");
                    controller.LoadSlot("slot1");
                    stage = "ruins-exit-root-verdict";
                    break;
                case "ruins-exit-root-verdict":
                case "ruins-exit-recruit":
                    if (!BaseSceneRules.Entrance(state) || !BaseDemoStories.ControllerDialogue(state, state.Demo.Flow.FirstControllerId)) return;
                    if (stage == "ruins-exit-recruit")
                    {
                        PrepareFullPartyAtEntrance();
                        VerifyCompanionRuinsArrival();
                    }
                    Approach(BaseDemoStories.GalanMarker);
                    H.OpenObject(BaseDemoStories.GalanMarker);
                    FinishDialogue();
                    if (stage == "ruins-exit-root-verdict")
                    {
                        Click("不论如何，操控百姓当斩");
                        H.ContinueDialogue();
                        if (RequirePanel("NpcDialogue").Find("DialogueText").GetComponent<Text>().text != "啊呀！")
                            throw new InvalidOperationException("首层判斩没有接入迦兰惊呼");
                        H.ContinueDialogue();
                        if (GameObject.Find("GalanFallen") == null || BaseDemoStories.ControllerDialogue(controller.State, state.Demo.Flow.FirstControllerId))
                            throw new InvalidOperationException("首层判斩没有完成处置");
                        controller.LoadSlot("slot1");
                        stage = "ruins-exit-recruit";
                        break;
                    }
                    Click("你确实控制了他们");
                    FinishDialogue();
                    Click("我怎么相信你？");
                    FinishDialogue();
                    Click("我会盯紧你的");
                    H.ContinueDialogue();
                    if (!RequirePanel("NpcDialogue").Find("DialogueText").GetComponent<Text>().text.Contains("万死不辞") ||
                        !controller.State.Demo.Flow.ControllerPledged ||
                        controller.State.Leaders.Count(leader => leader.Id == "partner_recruit:" + state.Demo.Flow.FirstControllerId) != 1)
                        throw new InvalidOperationException("同意招募后没有听见回应或未登记唯一的迦兰");
                    FinishDialogue();
                    Click("先在这里等");
                    var newId = "partner_recruit:" + state.Demo.Flow.FirstControllerId;
                    if (H.PanelOpen || !BaseAdventure.WaitingHere(controller.State,controller.State.Leaders.Single(l=>l.Id==newId)))
                        throw new InvalidOperationException("选择原地等候未保留迦兰");
                    TalkToPresentRecruit(newId);
                    Click("邀请随行"); FinishDialogue();
                    if (RequirePanel("NpcDialogue").Find("DialogueText").GetComponent<Text>().text != BaseAdventure.FullPartyReply)
                        throw new InvalidOperationException("满队当面邀请迦兰没有给出统一回复");
                    Click("知道了");
                    TalkToPresentRecruit("partner_craft");
                    Click("回到营地");
                    if (BaseAdventure.Contains(controller.State,"partner_craft") || controller.PresentedObject("partner_craft") != null)
                        throw new InvalidOperationException("选择回营后阿铸仍在队中或还留在洞口");
                    TalkToPresentRecruit(newId);
                    Click("邀请随行");
                    if (!BaseAdventure.Following(controller.State,newId) || controller.State.Leaders.Single(l=>l.Id==newId).WaitingAtRecruitment ||
                        !BaseAdventure.Full(controller.State))
                        throw new InvalidOperationException("空出位置后未当场接入迦兰");
                    Log.Add("[PASS] 满队接纳迦兰后实际点选原地等候，满队邀入得到回复，遣阿铸回营后当场邀请迦兰随行");
                    Log.Add("[PASS] 读同一洞外存档，首层处斩与起誓后的招募分别生效");
                    Finish(0, "古窟首通撤离与入口迦兰 PASS");
                    break;
            }
        }

        private static void VerifyCompanionRuinsArrival()
        {
            Approach("demo_ruins_investigate");
            H.OpenObject("demo_ruins_investigate");
            var view = RequirePanel("NpcDialogue");
            if (view.Find("SpeakerName").GetComponent<Text>().text != "阿铸") throw new InvalidOperationException("阿铸首次抵达未发言");
            FinishDialogue();
            var ahe = RequirePanel("NpcDialogue");
            var player = controller.State.Leaders.Single(l => l.Id == controller.State.PlayerLeaderId);
            if (!ahe.Find("DialogueText").GetComponent<Text>().text.StartsWith(player.Name)) throw new InvalidOperationException("阿禾没有称呼实际主角名");
            Click("在洞口等我。");
            BaseBattleGateway.Catalog().SaveSlot("slot3", controller.State, "阿禾在古窟洞口等候");
            var saved = BaseBattleGateway.Catalog().LoadSlot("slot3");
            if (!saved.Demo.RuinsPlayerHeard || !saved.Demo.RuinsAzhuHeard || !saved.Demo.RuinsAheHeard ||
                BaseAdventure.PauseFor(saved, "partner_farm") == null || BaseAdventure.Following(saved, "partner_farm"))
                throw new InvalidOperationException("三人到达标记或洞外等候未保存");
            RequirePanel("BattlePreparation");
            H.ClosePanel();
            TalkToPresentRecruit("partner_farm");
            Click("恢复随行");
            if (!BaseAdventure.Following(controller.State, "partner_farm")) throw new InvalidOperationException("洞口未能重新邀回阿禾");
            H.ClosePanel();
            Approach("demo_ruins_investigate");
            H.OpenObject("demo_ruins_investigate");
            RequirePanel("BattlePreparation");
            H.ClosePanel();
            Log.Add("[PASS] 洞口三人独立对白、阿禾洞外等候、当面重新随行与再次直接备战");
        }

        private static void PrepareFullPartyAtEntrance()
        {
            if (!controller.Change(s =>
            {
                void ApproachMarker(string id)
                {
                    var obj = BaseSpatial.Objects(s).Single(o=>o.Id==id);
                    s.PlayerX=obj.X; s.PlayerY=obj.Y; BaseAdventure.Arrive(s);
                }
                void Go(string gate,string dest)
                {
                    ApproachMarker(gate);
                    if (!BaseCampaign.Travel(s,dest)) throw new InvalidOperationException("准备满队时无法通行："+gate);
                }
                Go("demo_ruins_return","town"); ApproachMarker("demo_azhu");
                if (!BaseDemoRuins.Meet(s,"partner_craft") || !BaseDemoRuins.Join(s,"partner_craft"))
                    throw new InvalidOperationException("无法准备满队阿铸");
                Go("travel_base","base"); ApproachMarker("demo_ahe");
                if (!BaseDemoRuins.Join(s,"partner_farm") || !BaseAdventure.Full(s)) throw new InvalidOperationException("无法准备满队阿禾");
                Go("exit_east","town"); Go("demo_ruins_exit","outpost");
            },"")) throw new InvalidOperationException(controller.Message);
        }

        private static void TalkToPresentRecruit(string id)
        {
            var target = controller.PresentedObject(id) ?? throw new InvalidOperationException("地图上找不到伙伴："+id);
            if (!controller.Change(s=>
            {
                var point = BaseSpatial.Nearest(s,new BasePoint(target.X,target.Y-.65));
                s.PlayerX=point.X; s.PlayerY=point.Y;
                BaseInteraction.Walk(new BasePoint(target.X-point.X,target.Y-point.Y));
            },"")) throw new InvalidOperationException(controller.Message);
            H.OpenObject(id);
            FinishDialogue();
        }

        private static void VerifyRuinsConfrontationCopy()
        {
            int battleStarts = 0;
            void Open() => H.OpenRuinsConfrontation(() => battleStarts++);
            Open();
            FinishDialogue();
            var view = RequirePanel("NpcDialogue");
            if (view.Find("SpeakerName").GetComponent<Text>().text != BaseScriptNames.Galan ||
                !view.Find("DialogueText").GetComponent<Text>().text.Contains("何人聒噪"))
                throw new InvalidOperationException("未见面的伽兰应当在暗处喊话");
            Click("先退出去准备一下");
            if (H.DialogueOpen || battleStarts != 0) throw new InvalidOperationException("退出古窟误触发战斗");
            Open();
            FinishDialogue();
            if (RequirePanel("NpcDialogue").Find("DialogueText").GetComponent<Text>().text != "怎么又是你，没完了是吧")
                throw new InvalidOperationException("古窟重进未识别迦兰");
            Click("继续推进");
            if (battleStarts != 1) throw new InvalidOperationException("喊话结束后未开战或开战多次");
            H.ClosePanel();
            Log.Add("[PASS] 古窟初遇自报姓名，暂缓返回和继续开战");
        }

        private static void ApproachMerchantAwayFromStall()
        {
            if (!controller.Change(state =>
            {
                var objects = BaseSpatial.Objects(state);
                var merchant = objects.Single(item => item.Id == "town_peddler");
                var stall = objects.Single(item => item.Id == "town_market");
                var board = BaseBattlefield.Board(state);
                var approach = Enumerable.Range(0, 16).Select(i =>
                {
                    double angle = Math.PI * i / 8;
                    return new BasePoint(merchant.X + 1.1 * Math.Cos(angle), merchant.Y + 1.1 * Math.Sin(angle));
                }).First(point => BaseSpatial.Walkable(state, point.X, point.Y) &&
                    BaseInteraction.WithinReach(board, merchant, point.X, point.Y) &&
                    !BaseInteraction.WithinReach(board, stall, point.X, point.Y));
                state.PlayerX = approach.X;
                state.PlayerY = approach.Y;
                BaseInteraction.Walk(new BasePoint(merchant.X - approach.X, merchant.Y - approach.Y));
                BaseAdventure.Arrive(state);
            }, "")) throw new InvalidOperationException(controller.Message);
        }

        private static void VerifyMerchantCommonGoods()
        {
            var opening = controller.State;
            if (opening.Demo.AcceptedDay != 0) throw new InvalidOperationException("商店回归必须在接取领主委托前完成");
            foreach (string id in new[] { "food", "materials", "supply1", "supply2", BaseWine.Strong, BaseWine.Sake, BaseEquipmentCrafting.Stone })
            {
                var state = controller.State;
                bool stone = id == BaseEquipmentCrafting.Stone, wine = id == BaseWine.Strong || id == BaseWine.Sake;
                string name = wine || stone ? state.Backpack.Catalog.Single(item => item.Id == id).Name : BaseTown.Name(id);
                int price = stone ? BaseEquipmentCrafting.BuyPrice(state) : wine ? BaseProgressSupplies.Price(state, id) : BaseTown.TradePrice(state, id, true);
                var shop = RequirePanel("TownTrade");
                var offer = shop.Find("OfferList").GetComponentsInChildren<Button>()
                    .Single(button => button.GetComponentInChildren<Text>().text.StartsWith(name + " · ", StringComparison.Ordinal));
                if (offer.GetComponentInChildren<Text>().text != name + " · " + price + "币")
                    throw new InvalidOperationException(name + "的货架行应只显示名称及单价");
                offer.onClick.Invoke();
                if (RequirePanel("TownTrade") != shop || !offer.gameObject.activeInHierarchy)
                    throw new InvalidOperationException("切换货架商品时重建了商店或货架行");
                var details = shop.Find("Details");
                var label = details.GetComponentsInChildren<Text>().Single(text => text.text.StartsWith(name + " · 单价 ", StringComparison.Ordinal));
                Button Control(string text) => details.GetComponentsInChildren<Button>()
                    .Single(button => button.GetComponentInChildren<Text>().text == text);
                if (!label.text.Contains(price + "币") || !Control($"确认买入 · {price}币").interactable)
                    throw new InvalidOperationException(name + "的单价或买入状态与报价不同");
                var plus = Control("＋");
                var minus = Control("−");
                Control("最多");
                if (id == "food")
                {
                    plus.onClick.Invoke();
                    plus.onClick.Invoke();
                    if (RequirePanel("TownTrade") != shop || !Control($"确认买入 · {3 * price}币").interactable)
                        throw new InvalidOperationException("连续加量没有在同一面板更新报价");
                    minus.onClick.Invoke();
                    minus.onClick.Invoke();
                    Click($"确认买入 · {price}币");
                    if (controller.State.Inventory.Food != state.Inventory.Food + 1 || controller.State.Inventory.Coins != state.Inventory.Coins - price)
                        throw new InvalidOperationException("接委托前未能在老莫处购买粮草");
                }
                if (wine)
                {
                    Click($"确认买入 · {price}币");
                    if (BaseBackpack.Count(controller.State, id) != BaseBackpack.Count(state, id) + 1 ||
                        controller.State.Inventory.Coins != state.Inventory.Coins - price)
                        throw new InvalidOperationException("接委托前未能在老莫处购买" + name);
                }
                if (id == "supply2" && state.Town.Goods.Single(good => good.Id == id).Remaining == 1)
                {
                    var originalFill = offer.targetGraphic.color;
                    Click($"确认买入 · {price}币");
                    var soldOut = RequirePanel("TownTrade").Find("OfferList").GetComponentsInChildren<Button>()
                        .Single(button => button.GetComponentInChildren<Text>().text == name + " · " + price + "币");
                    soldOut.onClick.Invoke();
                    details = RequirePanel("TownTrade").Find("Details");
                    if (!soldOut.interactable || soldOut.targetGraphic.color.grayscale >= originalFill.grayscale ||
                        !details.GetComponentsInChildren<Text>().Any(text => text.text.StartsWith("今日剩余 0/", StringComparison.Ordinal)) ||
                        Control("确认买入 · 0币").interactable)
                        throw new InvalidOperationException("售罄项未变暗，或无法查看已售罄详情");
                    Capture("ui-skin-merchant-sold-out");
                }
            }
            Log.Add("[PASS] 接领主委托前在老莫本人旁打开商店，物资、锻造石与两种酒使用共用单价和买入控件，连续加量原位更新");
        }

        private static void BuySelectedForgeStone()
        {
            var before = controller.State;
            int price = BaseEquipmentCrafting.BuyPrice(before);
            int available = BasePartyInventory.AvailableMaterial(before, BaseEquipmentCrafting.Stone);
            Click("确认买入 · " + price + "币");
            var after = controller.State;
            if (after.Inventory.Coins != before.Inventory.Coins - price ||
                after.PersonalEquipment.StonesRemaining != before.PersonalEquipment.StonesRemaining - 1 ||
                BasePartyInventory.AvailableMaterial(after, BaseEquipmentCrafting.Stone) != available + 1)
                throw new InvalidOperationException("老莫购买一颗锻造石未正确扣款、减库存并发货");
            Log.Add("[PASS] 老莫货架购买一颗锻造石，扣款 " + price + " 币，随身锻造石 " + available + " → " + (available + 1));
        }

        private static void VerifyTownReforge()
        {
            Approach("town_blacksmith");
            H.OpenObject("town_blacksmith");
            FinishDialogue();
            Click(BaseConquest.Story.StoryText.Get("story.ui.trade.smith_goods_entry", "看看商品"));
            var equipment = RequirePanel("TownTrade");
            string weaponName = BasePersonalEquipment.Definition("basic_sword").Name;
            equipment.Find("OfferList").GetComponentsInChildren<Button>().Single(button =>
                button.GetComponentInChildren<Text>().text.StartsWith(weaponName + " · ", StringComparison.Ordinal)).onClick.Invoke();
            var weaponDetails = equipment.Find("Details").GetComponentsInChildren<Text>();
            if (!weaponDetails.Any(label => label.text.Contains("攻击距离")) || weaponDetails.Any(label => label.text.Contains("换装")))
                throw new InvalidOperationException("装备商品详情应显示攻击距离和自身属性");
            Capture("ui-skin-smith-weapon-details");
            H.ClosePanel();
            H.OpenObject("town_blacksmith");
            FinishDialogue();
            Click(BaseConquest.Story.StoryText.Get("story.ui.trade.reforge_entry", "重铸"));
            var opening = controller.State;
            var basic = BasePartyInventory.Entries(opening).Single(item => item.Holder == opening.PlayerLeaderId && item.Slot == "weapon");
            int openingStones = BasePartyInventory.AvailableMaterial(opening, BaseEquipmentCrafting.Stone);
            Click(basic.Name + " · 已穿戴");
            var openingDetails = RequirePanel("TownTrade").Find("Details");
            if (openingDetails.GetComponentsInChildren<Button>().Single(button => button.GetComponentInChildren<Text>().text == "确认重铸").interactable ||
                !openingDetails.GetComponentsInChildren<Text>().Any(label => label.text.Contains("新手短剑")) ||
                BasePartyInventory.AvailableMaterial(controller.State, BaseEquipmentCrafting.Stone) != openingStones)
                throw new InvalidOperationException("开局短剑未显示重铸引导或仍允许消耗锻造石");
            Capture("ui-skin-town-reforge-basic");
            H.ClosePanel();
            if (!controller.Change(next =>
                {
                    const string template = "novice_sword";
                    string id = "personal:" + next.PersonalEquipment.NextId;
                    bool stockUnlocked = next.Demo.Flow.NoviceShop;
                    next.Demo.Flow.NoviceShop = true;
                    if (!BasePersonalEquipment.Buy(next, template, next.PlayerLeaderId, BasePersonalEquipment.BuyPrice(next, template)) ||
                        !BasePersonalEquipment.Equip(next, next.PlayerLeaderId, id, "weapon"))
                        throw new InvalidOperationException("离屏重铸样本无法购买并穿戴新手短剑");
                    next.Demo.Flow.NoviceShop = stockUnlocked;
                }, "离屏重铸样本：穿戴新手短剑"))
                throw new InvalidOperationException(controller.Message);
            H.OpenObject("town_blacksmith");
            FinishDialogue();
            Click(BaseConquest.Story.StoryText.Get("story.ui.trade.reforge_entry", "重铸"));
            var before = controller.State;
            var weapon = BasePartyInventory.Entries(before).Single(item =>
                item.Holder == before.PlayerLeaderId && item.Slot == "weapon" && BaseEquipmentCrafting.CanSelectReforge(before, item));
            var quote = BaseEquipmentCrafting.Quote(before, weapon.Id, 1);
            if (quote == null) throw new InvalidOperationException("巴隆无法为穿戴的新手短剑报价");
            Click(weapon.Name + " · 已穿戴");
            var details = RequirePanel("TownTrade").Find("Details");
            string probabilities = "升档 " + (quote.Up / 100f).ToString("0.##") + "% ／ 留级 " +
                (quote.Stay / 100f).ToString("0.##") + "% ／ 降档 " + (quote.Down / 100f).ToString("0.##") + "%";
            if (!details.GetComponentsInChildren<Text>().Any(label => label.text == probabilities))
                throw new InvalidOperationException("巴隆重铸页显示的升档／留级／降档概率与报价不符");
            var upgraded = before.Copy();
            BasePartyInventory.Find(upgraded, weapon.Id).Gear.Quality++;
            float previousAttack = BasePersonalEquipment.Stats(before.Leaders.Single(person => person.Id == before.PlayerLeaderId)).Damage;
            float upgradedAttack = BasePersonalEquipment.Stats(upgraded.Leaders.Single(person => person.Id == before.PlayerLeaderId)).Damage;
            string preview = "攻击 " + previousAttack.ToString("0") + " → " + upgradedAttack.ToString("0");
            if (upgradedAttack <= previousAttack || !details.GetComponentsInChildren<Text>().Any(label => label.text.Contains("升档后") && label.text.Contains(preview)))
                throw new InvalidOperationException("重铸确认前未显示新手短剑的实际升档收益");
            Capture("ui-skin-town-reforge-odds");
            int available = BasePartyInventory.AvailableMaterial(before, BaseEquipmentCrafting.Stone);
            Click("确认重铸");
            var after = controller.State;
            var result = BasePartyInventory.Find(after, weapon.Id);
            if (result == null || after.PersonalEquipment.ReforgeRolls != before.PersonalEquipment.ReforgeRolls + 1 ||
                BasePartyInventory.AvailableMaterial(after, BaseEquipmentCrafting.Stone) != available - quote.Stones ||
                Math.Abs(result.Quality - quote.Quality) > 1)
                throw new InvalidOperationException("确认重铸后未正确扣除一颗锻造石并结算原武器品质");
            string outcome = BasePersonalEquipment.Qualities[quote.Quality] + " → " + BasePersonalEquipment.Qualities[result.Quality];
            float actualAttack = BasePersonalEquipment.Stats(after.Leaders.Single(person => person.Id == after.PlayerLeaderId)).Damage;
            string changes = previousAttack == actualAttack ? "属性未变" : "攻击 " + previousAttack.ToString("0") + " → " + actualAttack.ToString("0");
            var trade = RequirePanel("TownTrade");
            var confirm = trade.Find("Details").GetComponentsInChildren<Button>().Single(button => button.GetComponentInChildren<Text>().text == "确认重铸");
            var feedback = confirm.transform.parent.GetChild(confirm.transform.GetSiblingIndex() + 1).GetComponent<Text>();
            if (feedback == null || !feedback.text.Contains(outcome) || !feedback.text.Contains(changes) || trade.Find("SellerSpeech").GetComponent<Text>().text.Contains(outcome))
                throw new InvalidOperationException("重铸结果与实际属性变化应在确认按钮下方显示");
            Capture("ui-skin-town-reforge-result");
            Log.Add("[PASS] 巴隆重铸 " + weapon.Name + "，" + probabilities + "，结果 " + outcome + "，消耗 " + quote.Stones + " 颗");
            H.ClosePanel();
        }

        private static void VerifyAzhuDuelDialogue()
        {
            Approach("demo_azhu");
            var tickInvitation = controller.GetType().GetMethod("TickAzhuInvitation", BindingFlags.Instance | BindingFlags.NonPublic);
            tickInvitation.Invoke(controller, null);
            FinishDialogue();
            Capture("ui-skin-azhu-invitation");
            Click("眼下有要事，改日再比");
            FinishDialogue();
            tickInvitation.Invoke(controller, null);
            if (H.DialogueOpen || Field(controller, "combatController") != null || !controller.State.Demo.AzhuInvitationSeen)
                throw new InvalidOperationException("拒绝阿铸叫停邀约后重复弹出对白或开启比武");
            Log.Add("[PASS] 靠近阿铸触发一次性叫停对白，拒绝后可继续探索");
            H.OpenObject("demo_azhu");
            var replies = RequirePanel("NpcDialogue").Find("Replies");
            var buttons = replies.GetComponentsInChildren<Button>();
            VerifyUnreadReplies(buttons,1);
            Click("跳过对话");
            if (H.ContinueDialogue() || Field(controller, "combatController") != null)
                throw new InvalidOperationException("快进替玩家追问阿铸或直接开始切磋");
            Click("门边那杆长枪是你的？");
            FinishDialogue();
            var player = controller.State.Leaders.Single(l => l.Id == controller.State.PlayerLeaderId);
            double health = player.Health;
            Click("原地切磋");
            var duel = (BattleSceneController)Field(controller, "combatController");
            if (duel?.Realtime == null || !duel.IsAzhuDuel)
                throw new InvalidOperationException("原地比武未开战");
            duel.ResumeAfterDialogue(true);
            duel.AdvanceRealtime(.001);
            var footprints = duel.GetComponentsInChildren<Renderer>().Where(renderer => renderer.name == "GroundOccupation" && renderer.enabled).ToArray();
            var ink = new MaterialPropertyBlock();
            Color FootprintColor(Renderer renderer) { renderer.GetPropertyBlock(ink); return ink.GetColor("_BaseColor"); }
            if (!footprints.Any(renderer => FootprintColor(renderer).b > .8f) ||
                !footprints.Any(renderer => FootprintColor(renderer).r > .8f && Mathf.Abs(renderer.transform.localScale.x - renderer.transform.localScale.z) > .1f))
                throw new InvalidOperationException("战斗占用未显示我方蓝色与敌方骑兵红色占用");
            var debuffRing = duel.GetComponentsInChildren<LineRenderer>(true).First(ring => ring.name == "DebuffPurpleAura");
            if (Mathf.Abs((debuffRing.GetPosition(0).z + debuffRing.GetPosition(16).z) * .5f) > .01f)
                throw new InvalidOperationException("战斗状态圈没有对齐单位落脚点");
            Capture("ui-battle-occupation");
            var fighter = duel.Realtime.Model.Player;
            fighter.Health = fighter.MaxHealth * .5;
            duel.AdvanceRealtime(.04);
            var finish = duel.GetType().GetMethod("ContinueFinishedSceneDialogue", BindingFlags.Instance | BindingFlags.NonPublic);
            if (!(bool)finish.Invoke(duel, null) || Field(controller, "combatController") != null ||
                controller.State.Leaders.Single(l => l.Id == controller.State.PlayerLeaderId).Health != health ||
                controller.State.Demo.Flow.AzhuDuelWon || !controller.State.Demo.Flow.AzhuDuelCompleted)
                throw new InvalidOperationException("比武落败没有恢复状态或正确区分胜负和流程完成");
            H.ContinueDialogue();
            if (RequirePanel("NpcDialogue").Find("DialogueText").GetComponent<Text>().text !=
                "前些天伤得那么重，还没养好就能跟我打成这样。你的剑法，我服。")
                throw new InvalidOperationException("阿铸未在落败后夸奖主角");
            FinishDialogue();
            Capture("ui-skin-azhu-duel-dialogue");
            Click("暂缓邀请");
            Log.Add("[PASS] 原地比武落败恢复状态，按落败对白夸奖主角并开放邀请");
            Approach("demo_azhu");
            H.OpenObject("demo_azhu");
            FinishDialogue();
            Click("欢迎加入");
            FinishDialogue();
            var receipt = RequirePanel("CommonPanel");
            var receiptLabels = receipt.GetComponentsInChildren<Text>();
            var shortSword = receiptLabels.FirstOrDefault(label => label.text.Contains("新手短剑"));
            var destination = receiptLabels.FirstOrDefault(label => label.text.EndsWith("的背包", StringComparison.Ordinal));
            if ((string)Field(H, "panelViewKey") != "reward-receipt" ||
                shortSword == null || destination == null || destination.alignment != TextAnchor.MiddleRight ||
                destination.rectTransform.anchoredPosition.x <= shortSword.rectTransform.anchoredPosition.x ||
                !receiptLabels.Any(label => label.text.Contains("锻造石")) ||
                receipt.GetComponentsInChildren<Button>().Any(button => button.interactable && button.GetComponentInChildren<Text>()?.text.Contains("领取") == true))
                throw new InvalidOperationException("老铁匠的赠礼没有自动入包并显示简明奖励小窗");
            CaptureViewports("ui-skin-reward-receipt", mobile: true);
            H.ClosePanel();
            Log.Add("[PASS] 阿铸入队后完成赠礼对白，奖励自动入包并在小窗显示");
            var partner = controller.PresentedObject("partner_craft");
            if (partner == null) throw new InvalidOperationException("入队后找不到当前随行的阿铸");
            var fromPartner = new BasePoint(controller.State.PlayerX - partner.X, controller.State.PlayerY - partner.Y);
            BaseInteraction.Walk(fromPartner);
            if (BaseInteraction.CanReach(controller.State, partner))
                throw new InvalidOperationException("背后的阿铸仍在可交互范围");
            BaseInteraction.Walk(new BasePoint(-fromPartner.X, -fromPartner.Y));
            H.OpenObject("partner_craft");
            FinishDialogue();
            var partnerChoices = RequirePanel("NpcDialogue").Find("Replies").GetComponentsInChildren<Button>()
                .Select(button => button.GetComponentInChildren<Text>().text).ToArray();
            if (!partnerChoices.Contains("聊天对话") || !partnerChoices.Contains("留在此地") ||
                partnerChoices.Any(text => text is "原地比武" or "任职安排" or "查看角色详情" or "留在营地" or "在这里等候" ||
                    text.Contains("带本队士兵")))
                throw new InvalidOperationException("灰岩镇伙伴对话仍有旧功能或营地专用选项");
            H.ClosePanel();
            Log.Add("[PASS] 阿铸初次切磋可招募，入队后对话精简，镇内不显示留营操作");
        }

        private static void VerifyRuinsWarning()
        {
            Approach("demo_ruins_investigate");
            H.OpenObject("demo_ruins_investigate");
            var preparation = RequirePanel("BattlePreparation");
            var draft = (BaseBattlePreparation)Field(H, "battleDraft");
            if (draft.RequiredSupply(false) == 0)
            {
                preparation.Find("Teams").GetChild(0).Find("Melee/Input").GetComponent<InputField>().onEndEdit.Invoke("1");
                preparation = RequirePanel("BattlePreparation");
            }
            SetPreparationSupply(preparation,1);
            Press(RequirePanel("BattlePreparation"), "Continue");
            var deployed = RequireBattleDraft();
            var field = BattleSafeArea(deployed);
            Press(field, "DraftPreparationControls/Auto");
            if (!field.Find("BottomInformation").GetComponentsInChildren<Text>().Any(label =>
                label.text.Contains("普通参战成员") && label.text.Contains("每秒")))
                throw new InvalidOperationException("普通士兵实际落位后，部署内未显示侵蚀影响");
            Capture("ui-skin-ruins-vulnerable-warning");
            Press(field, "DraftPreparationControls/Start");
            FinishDialogue();
            Click("暂不交战");
            if (H.PanelOpen || RequireBattleDraft() != deployed || controller.State.Campaign.HasActiveBattle)
                throw new InvalidOperationException("拒绝交战后未保留当前部署");
            Press(field, "DraftPreparationControls/Back");
            RequirePanel("BattlePreparation");
            if (BaseAchievements.Record(controller.State, BaseAchievements.Caution) == null)
                throw new InvalidOperationException("从受侵蚀阵容返回备战未记入谨慎为上");
            H.ClosePanel();
        }

        private static PartyInventoryCell InventoryCell(Transform view, string id) =>
            view.GetComponentsInChildren<PartyInventoryCell>().Single(cell => cell.ItemId == id);

        private static void VerifyBackpackEquip()
        {
            var view = RequirePanel("PartyInventory");
            var gear = BasePartyInventory.Entries(controller.State).First(entry =>
                entry.Holder == controller.State.PlayerLeaderId && entry.Slot == "weapon");
            ClickHudButton(InventoryCell(view, gear.Id).transform);
            view = RequirePanel("PartyInventory");
            Capture("ui-skin-backpack-equipment-popup");
            var popup = view.Find("InventoryItemMenu");
            if (popup.Find("Primary").gameObject.activeSelf)
                throw new InvalidOperationException("穿戴中的装备仍显示穿戴操作");
            Press(popup, "Close");

            var source = InventoryCell(view, gear.Id);
            var destination = view.GetComponentsInChildren<PartyInventoryCell>()
                .First(cell => cell.Holder == gear.Holder && cell.Cell >= 0 && string.IsNullOrEmpty(cell.ItemId));
            var pointer = new PointerEventData(EventSystem.current)
            {
                position = RectTransformUtility.WorldToScreenPoint(null, source.transform.position)
            };
            source.OnBeginDrag(pointer);
            destination.OnDrop(pointer);
            source.OnEndDrag(pointer);
            if (BasePartyInventory.Find(controller.State, gear.Id).Worn)
                throw new InvalidOperationException("装备无法从装备槽拖回背包");

            view = RequirePanel("PartyInventory");
            if (view.Find("InventoryItemMenu") == null)
                ClickHudButton(InventoryCell(view, gear.Id).transform);
            Press(RequirePanel("PartyInventory").Find("InventoryItemMenu"), "Primary");
            if (!BasePartyInventory.Find(controller.State, gear.Id).Worn) throw new InvalidOperationException("背包贴格小窗无法穿戴装备");
            view = RequirePanel("PartyInventory");
            var card = view.Find("Cards").GetChild(0);
            Press(card, "Sort");
            if (!BasePartyInventory.Find(controller.State, gear.Id).Worn) throw new InvalidOperationException("整理背包改变了已装备物品");
            Log.Add("[PASS] 独立背包的贴格装备详情、拖回背包、穿戴及整理");
        }

        private static void VerifyBackpackTransfer()
        {
            string player = controller.State.PlayerLeaderId;
            var person = BasePartyInventory.People(controller.State, false).First(member =>
                member.Id != player && BasePartyInventory.FreeCell(controller.State, member.Id) >= 0);
            string item = "";
            if (!controller.Change(state =>
            {
                item = "bag:" + state.Backpack.NextId++;
                state.Backpack.Items.Add(new BaseBagItem { Id = item, ItemId = BaseEquipmentCrafting.Stone,
                    Count = 1, Holder = player, Cell = BasePartyInventory.FreeCell(state, player) });
                state.Backpack.Revision++;
            }, "界面检查")) throw new InvalidOperationException(controller.Message);
            H.OpenBackpack();
            var view = RequirePanel("PartyInventory");
            var card = view.Find("Cards/人物与背包_" + person.Id);
            var locked = card.Find("EquipmentSlot_5").GetComponent<Button>();
            if (BasePersonalEquipment.SlotsFor(person).Count() < 6 && locked.interactable)
                throw new InvalidOperationException("未解锁的装备槽仍可点击");
            ClickHudButton(InventoryCell(view, item).transform);
            Press(RequirePanel("PartyInventory").Find("InventoryItemMenu"), "Transfer");
            Click("转交给 " + person.Name);
            if (BasePartyInventory.Find(controller.State, item).Holder != person.Id)
                throw new InvalidOperationException("独立背包转交未进入指定队友的背包");
            Log.Add("[PASS] 三人背包的锁定装备槽及贴格跨人转交");
        }

        private static Transform RequireInventoryPopup(Transform view, Transform cell, bool onLeft)
        {
            var popup = view.Find("InventoryItemMenu");
            if (popup == null) throw new InvalidOperationException("点选物品后未打开共用贴格小窗");
            var bounds = ((RectTransform)view).rect;
            var anchor = SceneRect(cell, view);
            var box = SceneRect(popup, view);
            if ((onLeft ? Mathf.Abs(anchor.xMin - box.xMax - 8) : Mathf.Abs(box.xMin - anchor.xMax - 8)) > .1f ||
                box.xMin < bounds.xMin - .1f || box.xMax > bounds.xMax + .1f ||
                box.yMin < bounds.yMin - .1f || box.yMax > bounds.yMax + .1f)
                throw new InvalidOperationException("物品详情越过面板边缘或与选中的物品格分离");
            return popup;
        }

        private static void VerifyWarehouseUi()
        {
            if (!controller.Change(state =>
            {
                if (!BaseBackpack.AddToWarehouse(state, BaseEquipmentCrafting.Stone, 3)) throw new InvalidOperationException("检查用锻造石发放失败");
            }, "界面检查")) throw new InvalidOperationException(controller.Message);
            H.OpenWarehouse();
            var view = RequirePanel("PartyWarehouse");
            Canvas.ForceUpdateCanvases();
            var grid = (RectTransform)view.Find("Grid");
            var card = view.Find("Card").GetChild(0);
            var gridBounds = SceneRect(grid, view);
            var cardBounds = SceneRect(card, view);
            if (view.Find("Details") != null || grid.childCount != 24 ||
                gridBounds.xMax >= cardBounds.xMin || gridBounds.height < cardBounds.height * .9f ||
                gridBounds.width < ((RectTransform)view).rect.width * .65f ||
                card.GetComponentsInChildren<PartyInventoryCell>().Count(cell => cell.Cell >= 0) != BasePartyInventory.Capacity)
                throw new InvalidOperationException("仓库左格右包布局或格子容量错误");
            CaptureViewports("ui-skin-warehouse");

            var stored = BasePartyInventory.Entries(controller.State).First(entry =>
                entry.Holder == "" && entry.Stack?.ItemId == BaseEquipmentCrafting.Stone && entry.Count >= 3);
            string player = controller.State.PlayerLeaderId;
            var before = BasePartyInventory.Entries(controller.State).Select(entry => entry.Id).ToHashSet();
            ClickHudButton(InventoryCell(view, stored.Id).transform);
            if (RequirePanel("PartyWarehouse") != view) throw new InvalidOperationException("点物品时重建了仓库");
            var popup = RequireInventoryPopup(view, InventoryCell(view, stored.Id).transform, false);
            CaptureViewports("ui-skin-warehouse-take-popup");
            Press(popup, "QuantityDown");
            if (RequirePanel("PartyWarehouse") != view || RequireInventoryPopup(view, InventoryCell(view, stored.Id).transform, false) != popup)
                throw new InvalidOperationException("减少取出数量时重建了仓库或详情小窗");
            Press(popup, "Store");
            var withdrawn = BasePartyInventory.Entries(controller.State).Single(entry =>
                !before.Contains(entry.Id) && entry.Holder == player && entry.Stack?.ItemId == BaseEquipmentCrafting.Stone);
            if (withdrawn.Count != stored.Count - 1 || BasePartyInventory.Find(controller.State, stored.Id).Count != 1)
                throw new InvalidOperationException("仓库贴格取出没有应用选择的数量");

            view = RequirePanel("PartyWarehouse");
            ClickHudButton(InventoryCell(view, withdrawn.Id).transform);
            view = RequirePanel("PartyWarehouse");
            popup = RequireInventoryPopup(view, InventoryCell(view, withdrawn.Id).transform, true);
            Capture("ui-skin-warehouse-store-popup");
            Press(popup, "Close");
            if (RequirePanel("PartyWarehouse") != view || view.Find("InventoryItemMenu") != null)
                throw new InvalidOperationException("关闭物品小窗时未保留现有仓库");
            ClickHudButton(InventoryCell(view, withdrawn.Id).transform);
            ClickHudButton(InventoryCell(RequirePanel("PartyWarehouse"), withdrawn.Id).transform);
            if (BasePartyInventory.Find(controller.State, withdrawn.Id).Holder != "")
                throw new InvalidOperationException("仓库双击快速存入失败");

            view = RequirePanel("PartyWarehouse");
            var source = InventoryCell(view, withdrawn.Id);
            var destination = view.Find("Card").GetComponentsInChildren<PartyInventoryCell>()
                .First(cell => cell.Cell >= 0 && string.IsNullOrEmpty(cell.ItemId));
            var pointer = new PointerEventData(EventSystem.current) { position = RectTransformUtility.WorldToScreenPoint(null, source.transform.position) };
            source.OnBeginDrag(pointer);
            destination.OnDrop(pointer);
            source.OnEndDrag(pointer);
            if (BasePartyInventory.Find(controller.State, withdrawn.Id).Holder != player)
                throw new InvalidOperationException("从仓库拖到角色背包失败");
            view = RequirePanel("PartyWarehouse");
            if (view.Find("InventoryItemMenu") == null) ClickHudButton(InventoryCell(view, withdrawn.Id).transform);
            view = RequirePanel("PartyWarehouse");
            Press(RequireInventoryPopup(view, InventoryCell(view, withdrawn.Id).transform, true), "Store");
            if (BasePartyInventory.Find(controller.State, withdrawn.Id).Holder != "")
                throw new InvalidOperationException("仓库贴格存入失败");
            H.ClosePanel();
            H.OpenWarehouse();
            if (RequirePanel("PartyWarehouse").Find("InventoryItemMenu") != null)
                throw new InvalidOperationException("重新打开仓库时残留物品详情");
            Log.Add("[PASS] 仓库 24 格铺满左侧、右侧共用背包，贴格双向避让、数量、存取、快取、拖放和重开");
        }

        private static RectTransform[] RosterUnits(Transform list) =>
            list.Cast<RectTransform>().Where(row => row.gameObject.activeSelf && row.Find("Status") != null &&
                row.Find("Info") != null).ToArray();

        private static void VerifyRosterRow(Transform row, bool infoVisible)
        {
            Canvas.ForceUpdateCanvases();
            var source = (RectTransform)controller.transform.Find("UI 布局源/PersonnelRosterRow");
            var left = row.Find("Label")?.GetComponent<Text>();
            var right = row.Find("Status")?.GetComponent<Text>();
            if (left == null || right == null || !left.text.Contains("生命") || string.IsNullOrWhiteSpace(right.text) ||
                right.text.Contains("攻击") || left.text.Contains("攻 ") || right.text.Contains("防御") ||
                Mathf.Abs(((RectTransform)row).rect.height - source.rect.height) > .1f ||
                row.Find("Info").gameObject.activeSelf != infoVisible)
                throw new InvalidOperationException("名册单行内容不符：" + row.name);
            foreach (var text in new[] { left, right })
            {
                var settings = text.GetGenerationSettings(text.rectTransform.rect.size);
                var generator = text.cachedTextGenerator;
                generator.Populate(text.text, settings);
                if (text.text.Contains("\n") || generator.lineCount != 1 || generator.characterCountVisible == 0)
                    throw new InvalidOperationException($"名册文字未排在一行：{text.text}，宽度 {text.rectTransform.rect.width}，行数 {generator.lineCount}");
            }
            var container = (RectTransform)row;
            var a = SceneRect(left.rectTransform, container);
            var b = SceneRect(right.rectTransform, container);
            if (a.xMax > b.xMin + .1f || Mathf.Abs(a.center.y - b.center.y) > 1f ||
                (infoVisible && b.xMax > SceneRect((RectTransform)row.Find("Info"), container).xMin + .1f))
                throw new InvalidOperationException("名册左右文字或详情按钮重叠");
        }

        private static void VerifySharedSoldiers()
        {
            H.ClosePanel();
            if (!controller.Change(s =>
            {
                s.Workers.Clear();
                foreach (BaseTroopKind kind in Enum.GetValues(typeof(BaseTroopKind)))
                    s.Workers.Add(new BaseWorkerBucket { Kind = kind, Count = 2, Health = 75, MaxHealth = 100, FacilityId = "workshop", DesiredFacility = "workshop" });
            }, "六名士兵全在工坊")) throw new InvalidOperationException(controller.Message);
            var original = BasePersonnel.Units(controller.State).Where(unit => !unit.IsLeader)
                .OrderBy(unit => unit.SoldierNumber).ToArray();
            var pick = original[1];
            var farm = OpenCivilianFacility("farm");
            var list = farm.Find("Right/List").GetComponent<ScrollRect>().content;
            var actions = farm.Find("Left/List").GetComponent<ScrollRect>().content;
            var rows = RosterUnits(list);
            var target=actions.Find("Melee/Input").GetComponent<InputField>();
            target.onEndEdit.Invoke("1");
            if (rows.Length!=6 || target.text!="0")
                throw new InvalidOperationException("工坊全员时粮田数字输入抽走了已在岗人员或名册缺人");
            foreach (var row in rows) VerifyRosterRow(row, false);
            var chosen = rows.Single(row => row.Find("Label").GetComponent<Text>().text.StartsWith(pick.Name, StringComparison.Ordinal));
            string originalStatus = chosen.Find("Status").GetComponent<Text>().text;
            ClickHudButton(chosen);
            if (chosen == null || RequirePanel("FacilityPanel") != farm || chosen.Find("Status").GetComponent<Text>().text == originalStatus ||
                BaseEconomy.WorkersAt(controller.State, "farm") != 0)
                throw new InvalidOperationException("跨设施点名必须原位更新草案");
            ClickHudButton(chosen);
            if (chosen.Find("Status").GetComponent<Text>().text != originalStatus || actions.Find("FacilityActionRow/Apply").GetComponent<Button>().interactable)
                throw new InvalidOperationException("再点同一人没有恢复原岗位");
            ClickHudButton(chosen);
            Press(actions, "FacilityActionRow/Cancel");
            if (chosen.Find("Status").GetComponent<Text>().text != originalStatus)
                throw new InvalidOperationException("取消草案后未恢复士兵状态");
            ClickHudButton(chosen);
            Capture("roster-farm-transfer");
            Capture("roster-farm-transfer-854x480", 854, 480);
            Press(actions, "FacilityActionRow/Apply");
            var catalog = BaseBattleGateway.Catalog();
            string slot = BaseSaveCatalog.Slots[0];
            catalog.SaveSlot(slot, controller.State, "点名调工");
            var saved = BasePersonnel.Units(catalog.LoadSlot(slot)).Where(unit => !unit.IsLeader).OrderBy(unit => unit.SoldierNumber).ToArray();
            if (!saved.Select(u => u.SoldierNumber).SequenceEqual(original.Select(u => u.SoldierNumber)) ||
                saved.Single(u => u.SoldierNumber == pick.SoldierNumber).Facility != BaseEconomy.FacilityName("farm") ||
                BaseEconomy.WorkersAt(controller.State, "workshop") != 5)
                throw new InvalidOperationException("调动后读档没有保留同一名士兵和正确岗位");
            var works = OpenCivilianFacility("workshop");
            if (RosterUnits(works.Find("Right/List").GetComponent<ScrollRect>().content).Length != 6)
                throw new InvalidOperationException("工坊应仍能看到已调到粮田的士兵");
            var barracks = OpenCivilianFacility("barracks");
            var soldier = RosterUnits(barracks.Find("Right/List").GetComponent<ScrollRect>().content)
                .Single(row => row.Find("Label").GetComponent<Text>().text.StartsWith(pick.Name, StringComparison.Ordinal));
            ClickHudButton(soldier);
            if (barracks.Find("Right/Title").GetComponent<Text>().text != "单位详情" ||
                !barracks.Find("Right").GetComponentsInChildren<Text>().Any(label => label.text.Contains(pick.Name)))
                throw new InvalidOperationException("兵营点名未显示所点士兵详情");
            H.ClosePanel();
            Log.Add("[PASS] 六人全在工坊，粮田可逐人调入、再点撤销、取消及确认；读档保号；兵营点同一人查看详情");
            if (!controller.Change(s =>
            {
                s.Workers.Add(new BaseWorkerBucket { Kind = BaseTroopKind.Melee, Count = 1, Health = 80, MaxHealth = 100, DesiredFacility = "workshop" });
                s.Workers.Add(new BaseWorkerBucket { Kind = BaseTroopKind.Ranged, Count = 1, Health = 10, MaxHealth = 100 });
            }, "满岗换人与重伤名册")) throw new InvalidOperationException(controller.Message);
            farm = OpenCivilianFacility("farm");
            list = farm.Find("Right/List").GetComponent<ScrollRect>().content;
            actions = farm.Find("Left/List").GetComponent<ScrollRect>().content;
            RectTransform RowFor(int number) => (RectTransform)list.Find("Soldier" + number);
            foreach (var unit in original.Where(unit => unit != pick)) ClickHudButton(RowFor(unit.SoldierNumber));
            Press(actions, "FacilityActionRow/Apply");
            if (BaseEconomy.WorkersAt(controller.State, "farm") != 6 || BaseEconomy.CivilianWorkersAt(controller.State, "farm") != 0)
                throw new InvalidOperationException("六名士兵占满粮田后村民仍在补岗");
            var replace = BasePersonnel.Units(controller.State).Single(u => !u.IsLeader && u.Facility == BaseEconomy.FacilityName("workshop"));
            var injured = BasePersonnel.Units(controller.State).Single(u => !u.IsLeader && u.Health == 10);
            if (RowFor(injured.SoldierNumber).GetComponent<Button>().interactable ||
                !RowFor(injured.SoldierNumber).Find("Status").GetComponent<Text>().text.Contains("重伤"))
                throw new InvalidOperationException("重伤士兵未在原名册中显示无法生产的原因");
            ClickHudButton(RowFor(replace.SoldierNumber));
            if (!farm.GetComponentsInChildren<Text>().Any(text => text.text.Contains("岗位已满")) ||
                actions.Find("FacilityActionRow/Apply").GetComponent<Button>().interactable)
                throw new InvalidOperationException("满岗点击外岗士兵应提示玩家先调出一人");
            ClickHudButton(RowFor(pick.SoldierNumber));
            ClickHudButton(RowFor(replace.SoldierNumber));
            Press(actions, "FacilityActionRow/Apply");
            var committed = BasePersonnel.Units(controller.State).Where(u => !u.IsLeader).ToDictionary(u => u.SoldierNumber);
            if (committed[pick.SoldierNumber].Facility != BaseEconomy.FacilityName("") ||
                committed[replace.SoldierNumber].Facility != BaseEconomy.FacilityName("farm") ||
                BaseEconomy.WorkersAt(controller.State, "farm") != 6)
                throw new InvalidOperationException("满岗换人后兵号或岗位不符");
            H.ClosePanel();
            Log.Add("[PASS] 六个正式岗位占满时先调出再调入一次确认；村民让位，重伤者保留可读状态");
        }

        private static void VerifyPersonTabWidths(Transform trade)
        {
            var build = typeof(BaseHud).GetMethod("BuildInventoryPersonTabs", BindingFlags.NonPublic | BindingFlags.Instance);
            foreach (string path in new[] { "TownTrade/RightTabs", "PartyWarehouse/Tabs" })
            foreach (int count in new[] { 1, 2, 3, 6 })
            {
                var template = controller.transform.Find("UI 布局源/" + path);
                var strip = Object.Instantiate(template.gameObject, trade, false).GetComponent<ScrollRect>();
                try
                {
                    var people = Enumerable.Range(1, count).Select(i => new BaseLeader { Id = "test-" + i, Name = "队长" + i }).ToArray();
                    build.Invoke(H, new object[] { strip.viewport, people, people[count - 1].Id, (Action<BaseLeader>)(_ => {}) });
                    LayoutRebuilder.ForceRebuildLayoutImmediate(strip.content);
                    var gap = strip.content.GetComponent<HorizontalLayoutGroup>().spacing;
                    float width = (strip.viewport.rect.width - 2 * gap) / 3;
                    var tabs = strip.content.Cast<RectTransform>().ToArray();
                    var first = SceneRect(tabs[0], strip.viewport);
                    var last = SceneRect(tabs[count - 1], strip.viewport);
                    if (tabs.Length != count || tabs.Any(tab => Mathf.Abs(tab.rect.width - width) > .1f) ||
                        (count <= 3 && Mathf.Abs(first.xMin - strip.viewport.rect.xMin) > .1f) ||
                        (count >= 3 && Mathf.Abs(last.xMax - strip.viewport.rect.xMax) > .1f) || strip.horizontal != (count > 3))
                        throw new InvalidOperationException(path + " 切换栏未保留三个等宽角色位：" + count);
                }
                finally { Object.DestroyImmediate(strip.gameObject); }
            }
            Log.Add("[PASS] 商店和仓库的1、2、3、6人切换栏，空位、宽度和末项滚动");
        }

        private static void VerifyTradeCardLayout(Transform view)
        {
            Canvas.ForceUpdateCanvases();
            var source = (RectTransform)controller.transform.Find("UI 布局源/PartyMemberCard");
            var right = view.Find("RightBody").GetComponent<ScrollRect>();
            var card = (RectTransform)right.content.GetChild(0);
            if (Vector2.Distance(card.rect.size, source.rect.size) > .1f ||
                card.GetComponentsInChildren<PartyInventoryCell>().Count(cell => cell.Cell >= 0) != BasePartyInventory.Capacity)
                throw new InvalidOperationException("交易右栏未保留共用背包卡的尺寸和格数");
            Log.Add("[PASS] 交易右栏使用与独立背包一致的角色卡尺寸与格数");
        }

        private static void CivilianStage(string key)
        {
            stage = key;
            stageAt = EditorApplication.timeSinceStartup;
        }

        private static void TickCivilianWork()
        {
            if (EditorApplication.timeSinceStartup - stageAt > 60)
                throw new InvalidOperationException("村民走查等待超时：" + (controller != null ? controller.Message : stage));
            controller = C;
            if (controller?.State == null || controller.Busy || Object.FindAnyObjectByType<SceneLoadingCover>() != null) return;

            var state = controller.State;
            var travel = (BaseWorkerTravel)Field(controller, "workerTravel");
            if (travel.Active.Any(j => j.AppearanceId == "villager"))
                throw new InvalidOperationException("粮田补益生成了村民派工演员");
            switch (stage)
            {
                case "civilian-boot":
                    controller.LoadDemoStage("camp", "male");
                    CivilianStage("civilian-farm-out");
                    break;
                case "civilian-farm-out":
                    if (state.Demo.Stage != BaseDemoStage.Waking) return;
                    if (!CivilianTravelIdle()) throw new InvalidOperationException("读档后村民重复从营帐出工");
                    var audio = Object.FindAnyObjectByType<VillageOpeningAudio>();
                    civilianMusic = audio?.GetComponents<AudioSource>().SingleOrDefault(s => s.clip?.name == "Exploration");
                    if (civilianMusic == null || !civilianMusic.loop)
                        throw new InvalidOperationException("灰岩营地未启用循环探索曲");
                    H.ClosePanel();
                    var tent = BaseStageWalk.Door(state, "home");
                    Approach("home");
                    if (!controller.Change(s =>
                    {
                        if (!BaseDemo.AcknowledgeWaking(s)) throw new InvalidOperationException("没有完成灰岩营地苏醒");
                        s.PlayerX = tent.X;
                        s.PlayerY = tent.Y;
                        s.Inventory.Food = s.Inventory.Coins = s.Inventory.Materials = 0;
                        s.TodayFoodConsumed = 0;
                        s.TodayFoodMissing = s.TodayFoodRequired;
                    }, "村民生产走查准备")) throw new InvalidOperationException(controller.Message);
                    VerifyCivilianRoster("farm", 2, "ui-civilian-farm-roster");
                    Approach("home");
                    civilianDayPreview = BaseEconomy.Preview(controller.State);
                    H.OpenObject("home");
                    var home = RequirePanel("FacilityPanel");
                    var forecast = home.Find("Left/List").GetComponent<ScrollRect>().content;
                    if (!forecast.GetComponentsInChildren<Text>()
                        .Any(t => t.text.Contains($"预计产粮 +{civilianDayPreview.FoodProduced} · 明日口粮 {civilianDayPreview.FoodRequired} · 扣粮后库存 {civilianDayPreview.NextFood}")) ||
                        civilianDayPreview.FoodProduced != (int)controller.State.Rules.FarmPerWorker ||
                        civilianDayPreview.NextFood <= 0 || controller.State.Workers.Count != 0)
                        throw new InvalidOperationException($"零兵、零资源时的营帐预估与隐含产粮补益不符：产粮 {civilianDayPreview.FoodProduced}，应为 {(int)controller.State.Rules.FarmPerWorker}，结余 {civilianDayPreview.NextFood}、工人 {controller.State.Workers.Count}");
                    CaptureViewports("ui-civilian-before-rest");
                    Press(home.Find("Left/List").GetComponent<ScrollRect>().content, "Rest");
                    CivilianStage("civilian-rest");
                    break;
                case "civilian-rest":
                    if (state.Day != civilianDayPreview.Day + 1) return;
                    var report = state.LastReport;
                    if (!state.HasReport || report.FoodProduced != civilianDayPreview.FoodProduced ||
                        report.FoodRequired != civilianDayPreview.FoodRequired || state.Inventory.Food != civilianDayPreview.NextFood ||
                        state.Inventory.Coins != 0 || state.Inventory.Materials != 0 || state.Workers.Count != 0)
                        throw new InvalidOperationException("实际日结与营帐预估不符，或村民改变了军队和库存口径");
                    H.ClosePanel();
                    H.OpenReport();
                    if (!RequirePanel("CommonPanel").GetComponentsInChildren<Text>()
                        .Any(t => t.text.Contains($"产粮{report.FoodProduced} - 口粮{report.FoodConsumed} = {report.FoodAfter}")))
                        throw new InvalidOperationException("日结报告没有显示对应产粮与口粮");
                    CaptureViewports("ui-civilian-after-rest");
                    Log.Add("[PASS] 零兵零库存时村民补益隐含在产粮，名册、营帐预估与实际日结一致");
                    H.ClosePanel();
                    Approach("exit_east");
                    var departingCamera = controller.WorldCamera;
                    var departingPosition = departingCamera.transform.position;
                    controller.Travel("town");
                    var cover = Object.FindAnyObjectByType<SceneLoadingCover>();
                    if (departingCamera != null && (departingCamera.transform.position != departingPosition ||
                        cover == null || cover.GetComponent<CanvasGroup>().alpha < 1))
                        throw new InvalidOperationException("切图出现旧地图镜头移动或未被完全盖住的过渡帧");
                    CivilianStage("civilian-town");
                    break;
                case "civilian-town":
                    if (state.Campaign.Location != "town") return;
                    var townAudio = Object.FindAnyObjectByType<VillageOpeningAudio>();
                    if (townAudio == null || civilianMusic == null || civilianMusic.gameObject != townAudio.gameObject ||
                        !civilianMusic.loop || !civilianMusic.gameObject.activeInHierarchy)
                        throw new InvalidOperationException("切至灰岩镇时探索音乐源被替换或停用");
                    Log.Add("[PASS] 营地到灰岩镇，出发帧保持原镜头并全遮蔽；目的地出现时沿用同一个循环音乐源");
                    controller.LoadDemoStage("defense", "male");
                    CivilianStage("civilian-perimeter");
                    break;
                case "civilian-perimeter":
                    if (state.Demo.Stage != BaseDemoStage.DefenseWarned || state.Campaign.Location != "perimeter") return;
                    H.ClosePanel();
                    Approach("demo_defense_return");
                    controller.TravelDemo("base");
                    CivilianStage("civilian-workshop-out");
                    break;
                case "civilian-workshop-out":
                    if (state.Campaign.Location != "base") return;
                    if (!CivilianTravelIdle()) throw new InvalidOperationException("切图返营后村民重复从营帐出工");
                    H.ClosePanel();
                    if (!controller.Change(s =>
                    {
                        int capacity = s.Muster.Facilities.Single(f => f.FacilityId == "farm").Capacity;
                        foreach (var source in s.Workers.Where(w => w.FacilityId != "farm" && BaseDevelopment.Effective(s, w))
                            .Select(w => (w.Kind, w.FacilityId, w.Count)).ToArray())
                        {
                            int needed = capacity - BaseEconomy.WorkersAt(s, "farm");
                            if (needed <= 0) break;
                            BaseEconomy.MoveWorkers(s, source.Kind, source.FacilityId, "farm", Math.Min(needed, source.Count));
                        }
                        if (BaseEconomy.WorkersAt(s, "farm") != capacity)
                            throw new InvalidOperationException("首防阶段的士兵不足以准备满员粮田");
                    }, "准备满员粮田")) throw new InvalidOperationException(controller.Message);
                    VerifyCivilianRoster("farm", 0, "ui-civilian-farm-full");
                    VerifyCivilianRoster("workshop", 0, "ui-civilian-workshop-roster");
                    ChangeCivilianFarmPost("Minus", 1);
                    VerifyCivilianRoster("farm", 1, "ui-civilian-split-farm");
                    ChangeCivilianFarmPost("Plus", 0);
                    VerifyCivilianRoster("farm", 0, "ui-civilian-farm-replaced");
                    CivilianStage("civilian-settled");
                    break;
                case "civilian-settled":
                    if (!CivilianTravelIdle()) return;
                    Log.Add("[PASS] 调岗退出确认、粮田补益、工坊无村民岗位和派工形象");
                    Finish(0, "OK");
                    break;
            }
        }

        private static bool CivilianTravelIdle()
        {
            var travel = (BaseWorkerTravel)Field(controller, "workerTravel");
            return travel.Active.Count == 0 && travel.Pending == 0;
        }

        private static Transform OpenCivilianFacility(string facility)
        {
            H.ClosePanel();
            Approach(facility);
            H.OpenObject(facility);
            return RequirePanel("FacilityPanel");
        }

        private static void VerifyCivilianRoster(string facility, int civilians, string screenshot)
        {
            var view = OpenCivilianFacility(facility);
            RequireCivilianRoster(view);
            var actions = view.Find("Left/List").GetComponent<ScrollRect>().content;
            int soldiers = BaseEconomy.ProductionWorkersAt(controller.State, facility);
            int supplement = BaseEconomy.CivilianWorkersAt(controller.State, facility);
            string posts = actions.GetComponentsInChildren<Text>().Select(t => t.text)
                .FirstOrDefault(t => t.StartsWith("岗位 ", StringComparison.Ordinal));
            if (supplement != civilians || posts?.StartsWith($"岗位 {soldiers}/", StringComparison.Ordinal) != true)
                throw new InvalidOperationException($"{facility} 的隐含补益 {supplement}（应为 {civilians}），在岗士兵 {soldiers}，显示 {posts}");
            if (screenshot != null) CaptureViewports(screenshot);
            H.ClosePanel();
        }

        private static void ChooseRosterDraft(string label)
        {
            var dialog = H.VisualRoot.GetComponentsInChildren<RectTransform>().Single(node =>
                node.name == "RosterDraftConfirmation");
            var button = dialog.GetComponentsInChildren<Button>().Single(candidate =>
                candidate.GetComponentInChildren<Text>().text == label);
            button.onClick.Invoke();
        }

        private static void RequireCivilianRoster(Transform view)
        {
            var list = view.Find("Right/List").GetComponent<ScrollRect>().content;
            if (list.GetComponentsInChildren<Text>().Any(t=>t.text.StartsWith("村民 ×",StringComparison.Ordinal)))
                throw new InvalidOperationException("自动补位村民不应进入实名士兵名册");
        }

        private static void ChangeCivilianFarmPost(string adjustment, int expected)
        {
            var farm = OpenCivilianFacility("farm");
            var actions = farm.Find("Left/List").GetComponent<ScrollRect>().content;
            int old = BaseEconomy.CivilianWorkersAt(controller.State, "farm");
            var field=actions.Find("Melee/Input").GetComponent<InputField>();
            field.onEndEdit.Invoke((int.Parse(field.text)+(adjustment=="Plus"?1:-1)).ToString());
            farm = RequirePanel("FacilityPanel");
            RequireCivilianRoster(farm);
            if (BaseEconomy.CivilianWorkersAt(controller.State, "farm") != old)
                throw new InvalidOperationException("调工草案未确认就变更实际村民岗位");
            H.ClosePanel();
            ChooseRosterDraft("继续编辑");
            RequirePanel("FacilityPanel");
            H.ClosePanel();
            ChooseRosterDraft("保存方案");
            if (H.PanelOpen || BaseEconomy.CivilianWorkersAt(controller.State, "farm") != expected ||
                BaseEconomy.CivilianWorkersAt(controller.State, "workshop") != 0)
                throw new InvalidOperationException("退出确认没有保存调岗或错误地为工坊计入村民");
            H.ClosePanel();
        }

        private static void VerifyFacilityRosters()
        {
            H.ClosePanel();
            Approach("demo_defense_return");
            controller.TravelDemo("base");
            if (controller.State.Campaign.Location != "base") throw new InvalidOperationException(controller.Message);
            Approach("farm");
            controller.Interact();
            var view = RequirePanel("FacilityPanel");
            VerifyFacilityPortrait(view);
            var actions = view.Find("Left/List").GetComponent<ScrollRect>().content;
            var list = view.Find("Right/List").GetComponent<ScrollRect>();
            if (actions.GetComponentsInChildren<Button>().Any(button => button.name == "Unlock" || button.name == "ManagerLock"))
                throw new InvalidOperationException("正式粮田界面仍显示自动调工专用的岗位锁");
            var rows = RosterUnits(list.content);
            if (rows.Length == 0) throw new InvalidOperationException("粮田点名名册没有显示可调士兵");
            foreach (var row in rows) VerifyRosterRow(row, false);
            CaptureViewports("ui-skin-facility-farm");
            Canvas.ForceUpdateCanvases();
            float scrollTop = list.content.anchoredPosition.y;
            float actionTop = actions.anchoredPosition.y;
            list.verticalNormalizedPosition = 0;
            if (list.content.anchoredPosition.y <= scrollTop || actions.anchoredPosition.y != actionTop)
                throw new InvalidOperationException("粮田名册没有独立滚动");
            list.verticalNormalizedPosition = 1;
            int assigned = BaseEconomy.WorkersAt(controller.State, "farm");
            var farmAdjuster = actions.Find("Melee/Input");
            ClickHudButton(rows.First(row => row.GetComponent<Button>().interactable));
            if (BaseEconomy.WorkersAt(controller.State, "farm") != assigned)
                throw new InvalidOperationException("点名调配未确认就写入了岗位");
            var draft = (BaseState)Field(H, "workerDraft");
            if (BaseEconomy.WorkersAt(draft, "farm") == assigned)
                throw new InvalidOperationException("点名没有更新设施草案");
            if (RequirePanel("FacilityPanel") != view || farmAdjuster == null ||
                actions.Find("Melee/Input") != farmAdjuster || view.Find("Right/List").GetComponent<ScrollRect>() != list)
                throw new InvalidOperationException("粮田点名时重建了窗口或固定控件");
            Press(actions, "FacilityActionRow/Cancel");
            if (BaseEconomy.WorkersAt(controller.State, "farm") != assigned ||
                BaseEconomy.WorkersAt((BaseState)Field(H, "workerDraft"), "farm") != assigned)
                throw new InvalidOperationException("取消点名后岗位未恢复");
            if (!view.Find("Right/Filters").gameObject.activeSelf)
                throw new InvalidOperationException("生产设施没有显示士兵名册的兵种筛选");
            var manage = view.Find("PortraitColumn/Manage").GetComponent<Button>();
            var managerOptions = view.Find("PortraitColumn/ManagerOptions");
            if (managerOptions != null && manage.gameObject.activeInHierarchy && manage.interactable)
            {
                ClickHudButton(manage.transform);
                if (!managerOptions.gameObject.activeInHierarchy ||
                    view.Find("Right/List").GetComponent<ScrollRect>() != list)
                    throw new InvalidOperationException("任免负责人应在左侧肖像栏操作，并保留工人名册");
            }
            H.ClosePanel();
            Approach("workshop");
            H.OpenObject("workshop");
            view = RequirePanel("FacilityPanel");
            VerifyFacilityPortrait(view);
            actions = view.Find("Left/List").GetComponent<ScrollRect>().content;
            var workshop = controller.State.Muster.Facilities.Single(f => f.FacilityId == "workshop");
            if (!actions.GetComponentsInChildren<Button>().Any(b => b.name == "Grade1") ||
                actions.GetComponentsInChildren<Button>().Any(b => b.name == "Grade2") != (workshop.MaxProductLevel >= 2))
                throw new InvalidOperationException("工坊生产档位没有按解锁显示");
            CaptureViewports("ui-skin-facility-workshop");
            H.ClosePanel();
            Approach("barracks");
            H.OpenObject("barracks");
            view = RequirePanel("FacilityPanel");
            VerifyFacilityPortrait(view);
            var rangeDraft = view.Find("Left").GetComponentsInChildren<InputField>().First();
            rangeDraft.text = "17";
            Press(view, "Right/Filters/Kind");
            if (RequirePanel("FacilityPanel") != view || rangeDraft == null || rangeDraft.text != "17")
                throw new InvalidOperationException("兵营切筛选时丢失左侧范围草案");
            list = view.Find("Right/List").GetComponent<ScrollRect>();
            var soldier = RosterUnits(list.content).First();
            VerifyRosterRow(soldier, false);
            CaptureViewports("ui-skin-facility-barracks");
            list.verticalNormalizedPosition = 0;
            ClickHudButton(soldier);
            if (RequirePanel("FacilityPanel") != view || rangeDraft == null || rangeDraft.text != "17" ||
                view.Find("Right/Title").GetComponent<Text>().text != "单位详情")
                throw new InvalidOperationException("兵营名册无法在右侧打开对应士兵详情");
            Press(view, "Right/Filters/Back");
            H.ClosePanel();
            Approach("home");
            H.OpenObject("home");
            view = RequirePanel("FacilityPanel");
            VerifyFacilityPortrait(view);
            actions = view.Find("Left/List").GetComponent<ScrollRect>().content;
            list = view.Find("Right/List").GetComponent<ScrollRect>();
            if (actions.Find("Rest") == null || !actions.GetComponentsInChildren<Text>().Any(t =>
                t.text.Contains("扣粮后库存 " + BaseEconomy.Preview(controller.State).NextFood)))
                throw new InvalidOperationException("营帐休息操作或日结预估缺失");
            if (actions.Find("Warehouse") == null || actions.Find("Deputy") != null || actions.Find("Government") != null)
                throw new InvalidOperationException("营帐应保留仓库入口，营地建设由老周开启");
            CaptureViewports("ui-skin-facility-home");
            int day = controller.State.Day;
            Press(actions, "Rest");
            if (!H.DialogueOpen || controller.State.Day != day)
                throw new InvalidOperationException("来袭时营帐休息没有接入对白拦截");
            H.ClosePanel();
            Log.Add("[PASS] 四个设施三种横屏，独立肖像栏，粮田局部滚动和取消草案，工坊解锁，兵营详情，营帐预估与来袭拦截");
            VerifyConstructionLockedBeforePermit();
        }

        private static void VerifyFullCampInvitation()
        {
            const string ahe = "partner_farm";
            H.ClosePanel();
            var priorParty = controller.State.Adventure.Members.ToArray();
            Approach("demo_ahe");
            H.OpenObject("demo_ahe");
            FinishDialogue();
            Click("好，一起走");
            if (!H.DialogueOpen || !controller.State.Adventure.Members.SequenceEqual(priorParty) ||
                !controller.State.Leaders.Any(leader => leader.Id == ahe && BaseAdventure.WaitingHere(controller.State,leader)))
                throw new InvalidOperationException("满队邀请阿禾后未在原地等候");
            var response = RequirePanel("NpcDialogue").Find("DialogueText").GetComponent<Text>();
            FinishDialogue();
            if (!response.text.Contains("队伍人满了")) throw new InvalidOperationException("满队邀请未用对白交代去向");
            Capture("ui-skin-ahe-full-following");
            Click("先回营地");
            if (H.DialogueOpen || controller.State.Leaders.Single(l=>l.Id==ahe).WaitingAtRecruitment)
                throw new InvalidOperationException("选择回营后阿禾还在等待去向");

            Approach("farm");
            H.OpenObject("farm");
            var farm = RequirePanel("FacilityPanel");
            Press(farm, "PortraitColumn/Manage");
            Press(farm.Find("PortraitColumn/ManagerOptions").GetComponent<ScrollRect>().content, ahe);
            if (H.DialogueOpen) FinishDialogue();
            RequirePanel("FacilityPanel");
            if (controller.State.Leaders.Single(leader => leader.Id == ahe).FacilityId != "farm" ||
                !controller.State.Adventure.Members.SequenceEqual(priorParty))
                throw new InvalidOperationException("营地满队时阿禾没有直接任职粮田");
            H.ClosePanel();
            Log.Add("[PASS] 实际对白满队邀请阿禾后入营，保留原随行队伍，点名即可任命粮田负责人");

            Approach("home");
            H.OpenObject("home");
            var tent = RequirePanel("FacilityPanel");
            var roster = tent.Find("Right/List").GetComponent<ScrollRect>();
            IEnumerable<Transform> RosterRows() => roster.content.Cast<Transform>().Where(row => row.name == "PersonnelRosterRow");
            Transform Captain(string name) => RosterRows().Single(row => row.GetComponentInChildren<Text>().text.Contains(name));
            Button FollowButton() => tent.Find("Left/List").GetComponent<ScrollRect>().content
                .GetComponentsInChildren<Button>().Single(button => button.name == "CaptainFollow");
            if (tent.Find("Right/Title").GetComponent<Text>().text != "队长名册" || RosterRows().Count() != controller.State.Leaders.Count)
                throw new InvalidOperationException("营帐没有显示完整的队长名册");
            ClickHudButton(Captain("阿禾"));
            if (RequirePanel("FacilityPanel") != tent || !FollowButton().interactable)
                throw new InvalidOperationException("满队时邀请按钮不可点击");
            CaptureViewports("ui-skin-tent-captains-full");
            ClickHudButton(FollowButton().transform);
            FinishDialogue();
            if (RequirePanel("NpcDialogue").Find("DialogueText").GetComponent<Text>().text != BaseAdventure.FullPartyReply ||
                !controller.State.Adventure.Members.SequenceEqual(priorParty))
                throw new InvalidOperationException("满队点击邀请未保留队伍或没有统一回复");
            Click("知道了");
            tent = RequirePanel("FacilityPanel");
            roster = tent.Find("Right/List").GetComponent<ScrollRect>();
            ClickHudButton(Captain("迦兰"));
            ClickHudButton(FollowButton().transform);
            if (RequirePanel("FacilityPanel") != tent || controller.State.Adventure.Members.Count != priorParty.Length - 1)
                throw new InvalidOperationException("点留在营地后没有直接腾出随行位或打断了当前名册");
            ClickHudButton(Captain("阿禾"));
            ClickHudButton(FollowButton().transform);
            if (!BaseAdventure.Contains(controller.State, ahe) || controller.State.Adventure.Members.Count != priorParty.Length ||
                controller.State.Leaders.Single(leader => leader.Id == ahe).FacilityId != "")
                throw new InvalidOperationException("腾出位置后未从营帐正确邀请阿禾随行并释放岗位");
            Log.Add("[PASS] 营帐管理满队名册，在同一页更换随行队长");
            Press(tent.Find("Left/List").GetComponent<ScrollRect>().content, "Training");
            Click("返回营帐");
            tent = RequirePanel("FacilityPanel");
            var tentActions = tent.Find("Left/List").GetComponent<ScrollRect>().content;
            if (!tentActions.GetComponentsInChildren<Text>().Any(label => label.text.StartsWith("营帐 1级", StringComparison.Ordinal)) ||
                !tentActions.GetComponentsInChildren<Text>().Any(label => label.text.Contains("容量 10") && label.text.Contains("每日恢复")))
                throw new InvalidOperationException("营帐未显示当前等级、下一级容量和恢复变化");
            Press(tentActions, "Upgrade");
            tent = RequirePanel("FacilityPanel");
            if (controller.State.Demo.Rpg.CampLevel != 2 || !tent.GetComponentsInChildren<Text>().Any(label =>
                label.text.StartsWith("营帐 2级", StringComparison.Ordinal) && label.text.Contains("招降空位 6")))
                throw new InvalidOperationException("营帐升至二级后，招降空位未正确更新");
            H.ClosePanel();

            H.OpenBackpack();
            string player = controller.State.PlayerLeaderId;
            var stone = BasePartyInventory.Entries(controller.State).First(item =>
                item.Holder == player && item.Stack?.ItemId == BaseEquipmentCrafting.Stone);
            ClickHudButton(InventoryCell(RequirePanel("PartyInventory"), stone.Id).transform);
            var itemMenu = RequirePanel("PartyInventory").Find("InventoryItemMenu");
            for (int count = stone.Count; count > 1; count--) Press(itemMenu, "QuantityDown");
            if (stone.Count > 1 && itemMenu.Find("Destroy").GetComponentInChildren<Text>().text != "销毁 ×1")
                throw new InvalidOperationException("销毁按钮没有标明将销毁的数量");
            Press(itemMenu, "Destroy");
            if ((BasePartyInventory.Find(controller.State, stone.Id)?.Count ?? 0) != stone.Count - 1)
                throw new InvalidOperationException("销毁选定数量仍需额外确认或数量错误");
            H.ClosePanel();

            H.OpenSlots(true);
            Click("保存到此槽");
            var catalog = BaseBattleGateway.Catalog();
            string slot = BaseSaveCatalog.Slots[0];
            int coins = catalog.LoadSlot(slot).Inventory.Coins;
            H.ClosePanel();
            if (!controller.Change(state => state.Inventory.Coins++, "")) throw new InvalidOperationException(controller.Message);
            H.OpenSlots(true);
            Click("覆盖此槽（保留备份）");
            if (catalog.LoadSlot(slot).Inventory.Coins != coins + 1 || catalog.LoadSlot(slot, true).Inventory.Coins != coins)
                throw new InvalidOperationException("手动槽直接覆盖未写入新档或未保留原档备份");
            Log.Add("[PASS] 从营帐打开训练，销毁直接提交，手动槽覆盖保留旧内容备份");
        }

        private static void VerifyFacilityPortrait(Transform view)
        {
            Canvas.ForceUpdateCanvases();
            var root = (RectTransform)view;
            var portrait = view.Find("PortraitColumn");
            var image = portrait?.Find("Portrait");
            if (((RectTransform)Field(H, "questBar")).gameObject.activeSelf || image == null ||
                (image.gameObject.activeInHierarchy && image.GetComponent<BattleAvatarGraphic>() == null) ||
                SceneRect(portrait, root).xMax > SceneRect(view.Find("Left"), root).xMin + .1f ||
                view.GetComponentsInChildren<Button>().Any(button => button.name == "Rules"))
                throw new InvalidOperationException("设施应有独立的左侧肖像栏，并移除重复的规则按钮");
        }

        private static void VerifyConstructionLockedBeforePermit()
        {
            Approach("exit_east");
            controller.TravelDemo("town");
            if (controller.State.Campaign.Location != "town") throw new InvalidOperationException(controller.Message);
            Approach("town_quest");
            H.OpenObject("town_quest");
            FinishDialogue();
            var reply = RequirePanel("NpcDialogue");
            if (reply.Find("DialogueText").GetComponent<Text>().text.Contains("调查奖赏"))
                throw new InvalidOperationException("领主对白末页不应重复播报资源数字");
            if (reply.GetComponentsInChildren<Button>().Any(button => button.interactable &&
                button.GetComponentInChildren<Text>()?.text == "营地建设"))
                throw new InvalidOperationException("霍恩仍能开启建设界面");
            H.ClosePanel();
            Approach("travel_base");
            controller.TravelDemo("base");
            if (controller.State.Campaign.Location != "base") throw new InvalidOperationException(controller.Message);
            Approach("demo_rescuer");
            H.OpenObject("demo_rescuer");
            FinishDialogue();
            var zhou = RequirePanel("NpcDialogue");
            if (zhou.GetComponentsInChildren<Button>().Any(button => button.interactable &&
                button.GetComponentInChildren<Text>()?.text == "营地建设"))
                throw new InvalidOperationException("首防后的独立许可尚未取得，老周不应开启建设");
            H.ClosePanel();
            Log.Add("[PASS] 古窟交差许可作用保持，首防后独立建设许可前霍恩和老周均不能开启建设");
        }

        private static void VerifyRecruitRoster()
        {
            H.ClosePanel();
            Approach("exit_east");
            controller.TravelDemo("town");
            if (controller.State.Campaign.Location != "town") throw new InvalidOperationException(controller.Message);
            Approach("town_recruit");
            H.OpenObject("town_recruit");
            if (H.DialogueOpen) { FinishDialogue(); Click("开始招募"); }
            var trade = RequirePanel("TownTrade");
            var recruitmentPortrait = trade.Find("SellerPortrait").GetComponent<BattleAvatarGraphic>();
            if (recruitmentPortrait.mainTexture != DemoPortraits.HalfBody("recruiter"))
                throw new InvalidOperationException("募兵处未使用雷恩的共用半身像");
            var roster = trade.Find("RightBody").GetComponent<ScrollRect>();
            var rows = RosterUnits(roster.content);
            if (rows.Length == 0 || rows.Length != BasePersonnel.Units(controller.State).Count(unit => !unit.IsLeader) ||
                !roster.vertical || trade.Find("RightTabs").gameObject.activeSelf)
                throw new InvalidOperationException("军官处没有在单一滚动列表中显示现役士兵");
            foreach (var row in rows)
            {
                VerifyRosterRow(row, false);
                if (row.GetComponent<Button>().interactable) throw new InvalidOperationException("军官右侧现役名册不是只读");
            }
            Capture("ui-skin-recruit-roster");
            var state = controller.State;
            var kind = state.Town.Recruits.First(offer => BaseTown.MaxHire(state, offer.Kind) > 0).Kind;
            var offerButton = trade.Find("OfferList").GetComponentsInChildren<Button>().Single(button =>
                button.GetComponentInChildren<Text>().text.TrimStart('●', ' ').StartsWith(BaseEconomy.TroopName(kind) + " · 今日余 "));
            ClickHudButton(offerButton.transform);
            var quote = BaseTown.QuoteHire(state, kind, 1);
            Click("招募 1 人 · " + quote.Total + "币");
            if (H.DialogueOpen || !controller.State.Demo.Flow.HideoutRecruitCluePending || controller.State.Demo.Flow.HideoutRecruitClueSeen ||
                BaseTown.TroopTotal(controller.State) != BaseTown.TroopTotal(state) + 1 ||
                controller.State.Inventory.Coins != state.Inventory.Coins - quote.Total ||
                !RequirePanel("TownTrade").Find("Details").GetComponentsInChildren<Text>().Any(text =>
                    text.text == "招募 1 人 · " + BaseTown.HirePrice(controller.State, kind) + "币"))
                throw new InvalidOperationException("首次购兵未等待退出，或逐人招募未结算重报价");
            Press(trade, "Close");
            if (!H.DialogueOpen || controller.State.Demo.Flow.HideoutRecruitCluePending || !controller.State.Demo.Flow.HideoutRecruitClueSeen)
                throw new InvalidOperationException("关闭招募时未显示一次窝点线索");
            H.ContinueDialogue();
            var clue = RequirePanel("NpcDialogue");
            const string hideoutClue = "破墙背后有个匪窝";
            if (!clue.Find("DialogueText").GetComponent<Text>().text.Contains(hideoutClue))
                throw new InvalidOperationException("雷恩没有介绍断壁后的窝点");
            if (clue.Find("PortraitFrame/SpeakerPortrait").GetComponent<BattleAvatarGraphic>().mainTexture !=
                DemoPortraits.HalfBody("recruiter"))
                throw new InvalidOperationException("雷恩对白与募兵处没有使用同一张半身像");
            Capture("ui-skin-recruit-clue");
            FinishDialogue();
            if (H.PanelOpen) throw new InvalidOperationException("关闭招募后，读完线索应返回探索");
            H.OpenObject("town_recruit");
            var secondKind = controller.State.Town.Recruits.First(offer => BaseTown.MaxHire(controller.State, offer.Kind) > 0).Kind;
            var secondOffer = RequirePanel("TownTrade").Find("OfferList").GetComponentsInChildren<Button>().Single(button =>
                button.GetComponentInChildren<Text>().text.TrimStart('●', ' ').StartsWith(BaseEconomy.TroopName(secondKind) + " · 今日余 "));
            ClickHudButton(secondOffer.transform);
            Click("招募 1 人 · " + BaseTown.QuoteHire(controller.State, secondKind, 1).Total + "币");
            if (H.DialogueOpen || BaseTown.TroopTotal(controller.State) != BaseTown.TroopTotal(state) + 2 ||
                controller.State.Demo.Flow.HideoutRecruitCluePending)
                throw new InvalidOperationException("同一存档第二次购兵重复登记了窝点线索");
            Press(RequirePanel("TownTrade"), "Return");
            H.ContinueDialogue();
            if (!H.DialogueOpen || RequirePanel("NpcDialogue").Find("DialogueText").GetComponent<Text>().text.Contains(hideoutClue))
                throw new InvalidOperationException("再次退出招募时重播了窝点线索");
            H.ClosePanel();

            // Restore the same saved pre-close flag pair to exercise the alternate exit button.
            if (!controller.Change(next =>
                {
                    next.Demo.Flow.HideoutRecruitClueSeen = false;
                    next.Demo.Flow.HideoutRecruitCluePending = true;
                }, "")) throw new InvalidOperationException(controller.Message);
            H.OpenObject("town_recruit");
            Press(RequirePanel("TownTrade"), "Return");
            H.ContinueDialogue();
            if (!controller.State.Demo.Flow.HideoutRecruitClueSeen ||
                !RequirePanel("NpcDialogue").Find("DialogueText").GetComponent<Text>().text.Contains(hideoutClue))
                throw new InvalidOperationException("返回对话时未先播放待播的窝点线索");
            FinishDialogue();
            if (!H.DialogueOpen) throw new InvalidOperationException("返回对话读完线索后应保留雷恩交谈入口");
            H.ClosePanel();
            Log.Add("[PASS] 军官名册单列表滚动，逐人招募后重报价，关闭和返回对话时各自交付待播线索");
        }

        private static void OpenWarehouseShot()
        {
            H.ClosePanel();
            Approach("home");
            H.OpenObject("home");
            Click("仓库");
            stage = "warehouse-shot"; frameDeadline = Time.frameCount + 20;
        }

        private static Transform RequirePanel(string name)
        {
            var value = Field(H, "panel") as RectTransform;
            if (!H.PanelOpen || value == null || value.name != name)
                throw new InvalidOperationException("未进入 " + name + "：" + controller.Message);
            return value;
        }

        private static void MoveSceneExplorer(Vector2 direction)
        {
            typeof(BaseSceneController).GetMethod("MoveExploration", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(controller, new object[] { direction, .05 });
        }

        private static void Approach(string id,bool besideNpc=false)
        {
            if (!controller.Change(s =>
            {
                var target = BaseSpatial.Objects(s).Single(o => o.Id == id);
                var point = target.Blocks ? BaseStageWalk.Door(s, id) : besideNpc ?
                    BaseSpatial.Nearest(s,new BasePoint(target.X-.9,target.Y)) : new BasePoint(target.X, target.Y);
                s.PlayerX = point.X; s.PlayerY = point.Y;
                BaseInteraction.Walk(new BasePoint(target.X - point.X, target.Y - point.Y));
                BaseAdventure.Arrive(s);
            }, "界面检查")) throw new InvalidOperationException(controller.Message);
        }

        private static void FinishDialogue()
        {
            RequirePanel("NpcDialogue");
            for (int i = 0; i < 100 && H.DialogueOpen; i++)
            {
                if (H.ContinueDialogue()) continue;
                var line = RequirePanel("NpcDialogue").Find("Replies").GetComponent<ScrollRect>().content.Find("PlayerSpeechChoice");
                if (line == null || !line.gameObject.activeInHierarchy) return;
                line.GetComponent<Button>().onClick.Invoke();
            }
        }

        private static void Click(string label)
        {
            var button = ((RectTransform)Field(H, "panel")).GetComponentsInChildren<Button>()
                .FirstOrDefault(b => b.interactable && b.GetComponentInChildren<Text>()?.text == label);
            if (button == null) throw new InvalidOperationException("找不到可操作的「" + label + "」");
            button.onClick.Invoke();
        }

        private static void VerifyUnreadReplies(Button[] buttons, int expected)
        {
            if (buttons.Length != expected || !buttons[0].interactable || buttons[0].GetComponentInChildren<Text>().text != "跳过对话" ||
                buttons.Skip(1).Any(button => button.interactable || button.GetComponentInChildren<Text>().text != "..."))
                throw new InvalidOperationException("对白首行跳过和后续占位项不正确");
        }

        private static void VerifyRoyalSwordChoice()
        {
            int starts = 0;
            typeof(BaseHud).GetMethod("OpenPursuitArrival", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(H, new object[] { (Action)(() => starts++) });
            Click("跳过对话");
            var view = RequirePanel("NpcDialogue");
            var speaker = view.Find("SpeakerName").GetComponent<Text>();
            if (speaker.text == "你" || H.ContinueDialogue())
                throw new InvalidOperationException("王室剑原句在选择前已播放");
            Click("追问王室的剑");
            H.ContinueDialogue();
            if (speaker.text != "你" || view.Find("DialogueText").GetComponent<Text>().text != "王室的剑怎么会在你们手里？")
                throw new InvalidOperationException("点明意图后未播放王室剑原句");
            Click("跳过对话");
            if (starts != 0 || speaker.text == "你") throw new InvalidOperationException("跳过重复主角发言或提前开战");
            Click("阿禾，带乡亲们往东走！这两个交给我！");
            if (starts != 1) throw new InvalidOperationException("东口迎敌未交还战斗入口");
            H.ClosePanel();
        }
        private static void SetPreparationSupply(Transform preparation,int grade)
        {
            var draft=(BaseBattlePreparation)Field(H,"battleDraft");
            if (draft.SupplyGrade!=grade) Press(preparation,"Supply/Grade1");
            if (draft.SupplyGrade!=grade) throw new InvalidOperationException("军需切换失败");
        }

        private static void Press(Transform root, string path)
        {
            var button = root.Find(path)?.GetComponent<Button>();
            if (button?.interactable != true) throw new InvalidOperationException(root.name + "/" + path + " 不可操作");
            button.onClick.Invoke();
        }
        private static void VerifyBelowTopBar(Transform panelRoot)
        {
            var header = panelRoot.GetComponentInParent<Canvas>().GetComponentsInChildren<RectTransform>(true)
                .Single(t => t.name == "ResourceStrip").parent as RectTransform;
            if (!header.gameObject.activeInHierarchy) return;
            Canvas.ForceUpdateCanvases();
            var corners = new Vector3[4];
            ((RectTransform)panelRoot).GetWorldCorners(corners);
            float panelTop = corners[1].y;
            header.GetWorldCorners(corners);
            if (panelTop > corners[0].y + .01f) throw new InvalidOperationException("探索顶栏压住备战标题");
        }
        private static void VerifySavedDialogueColor(Transform view)
        {
            if (Environment.GetEnvironmentVariable("BASE_CONQUEST_UI_CHECK_SAVED_LAYOUT") == "1" &&
                !((Color32)view.GetComponent<Image>().color).Equals(new Color32(241, 222, 182, 255)))
                throw new InvalidOperationException("对白框未读取已保存的场景颜色");
        }

        private static void VerifyRuinsEntrance()
        {
            if (!BaseSceneRules.Entrance(controller.State)) throw new InvalidOperationException("没有进入遗迹入口地图");
            var marker = BaseSpatial.Objects(controller.State).Single(o => o.Id == "demo_ruins_investigate");
            if (!controller.Change(s =>
            {
                var p = BaseSpatial.Nearest(s, new BasePoint(marker.X, marker.Y - .35));
                s.PlayerX = p.X;
                s.PlayerY = p.Y;
                BaseInteraction.Walk(new BasePoint(0, 1));
                BaseAdventure.Arrive(s);
            }, "检查遗迹遮挡")) throw new InvalidOperationException(controller.Message);
            var avatar = (Transform)Field(controller, "player");
            var portal = Object.FindObjectsByType<Renderer>().Where(r => r.name == "PixelDemo_dungeon-entrance")
                .OrderBy(r => (r.transform.position - avatar.position).sqrMagnitude).First();
            var sprite = avatar.GetComponentsInChildren<MeshRenderer>().Single(r => r.name == "PixelDemoCharacter");
            var walk = typeof(BaseSceneController).GetMethod("MoveExploration", BindingFlags.Instance | BindingFlags.NonPublic);
            foreach (double offset in new[] { .10, .18 })
            {
                double target = marker.Y + offset;
                for (int step = 0; step < 40 && target - controller.State.PlayerY > .001; step++)
                    walk.Invoke(controller, new object[] { Vector2.up, Math.Min(.02, (target - controller.State.PlayerY) / 4.3) });
                if (Math.Abs(controller.State.PlayerY - target) > .02)
                    throw new InvalidOperationException("主角向北未走到门洞取样位置：" + offset);
                if (sprite.sortingOrder <= portal.sortingOrder)
                    throw new InvalidOperationException("向北穿过门槛时贴图排序错误：" + offset);
            }
            for (int step = 0; step < 30; step++) walk.Invoke(controller, new object[] { Vector2.up, .01 });
            if (Math.Abs(controller.State.PlayerX - marker.X) > .02 || controller.State.PlayerY < marker.Y + .18 ||
                controller.State.PlayerY > marker.Y + .241 || sprite.sortingOrder <= portal.sortingOrder ||
                !BaseInteraction.CanReach(controller.State, marker))
                throw new InvalidOperationException("遗迹门前没有正确阻挡移动、显示角色或保留交互");
            var facing=avatar.GetComponent<PlayerHeroView>();
            walk.Invoke(controller,new object[]{Vector2.right,0d});
            double y=controller.State.PlayerY;
            walk.Invoke(controller,new object[]{Vector2.up,.01d});
            if(facing==null||facing.Facing!=HeroFacing.North||BaseInteraction.Facing.Y!=1||Math.Abs(controller.State.PlayerY-y)>.001)
                throw new InvalidOperationException("被门槛挡住时单独朝北输入未更新人物与交互朝向");
            Capture("ui-skin-ruins-entrance");
            Log.Add("[PASS] 北向移动停在门槛外，角色完整且仍可交互");
        }

        private static BattleSceneController RequireBattleDraft()
        {
            var battle = Object.FindObjectsByType<BattleSceneController>().SingleOrDefault(b => b.DeploymentDraftActive);
            if (battle == null) throw new InvalidOperationException("正式战斗部署未打开：" + controller.Message);
            if (battle.PreviewBoard.Camera != controller.WorldCamera || !battle.PreviewHud.Root.activeInHierarchy ||
                battle.PreviewSimulation.Phase != BattlePhase.Deployment || controller.State.Campaign.HasActiveBattle ||
                !battle.DraftPlacementActive ||
                BattleSafeArea(battle).Find("DraftPreparationControls/Start")?.GetComponentInChildren<Text>()?.text != "开战" ||
                Object.FindObjectsByType<RectTransform>().Any(t => t.name == "ResourceStrip" || t.name == "BattleDeployment"))
                throw new InvalidOperationException("部署应由正常战场和战斗 HUD 承载，且尚未提交开战");
            return battle;
        }

        private static RectTransform BattleSafeArea(BattleSceneController battle) =>
            battle.PreviewHud.Root.GetComponentsInChildren<RectTransform>(true).Single(t => t.name == "SafeArea");

        private static void VerifyBattleMenu(BattleSceneController battle, BaseBattlePreparation draft)
        {
            var menu = (RectTransform)BattleSafeArea(battle).Find("测试");
            var source = (RectTransform)controller.transform.Find("UI 布局源/BaseShell/Header/菜单");
            if (menu.anchorMax != source.anchorMax || menu.anchoredPosition != source.anchoredPosition || menu.sizeDelta != source.sizeDelta)
                throw new InvalidOperationException("战场菜单没有沿用探索菜单的位置和尺寸");

            Transform Overlay() => ((GameObject)Field(battle, "campaignMenu")).GetComponentsInChildren<RectTransform>(true)
                .Single(rect => rect.name == "选项面板");
            Press(menu.parent, menu.name);
            var positions = draft?.Positions.ToDictionary(p => p.Key, p => p.Value);
            if (draft != null)
            {
                var cell = positions[draft.PlayerId];
                var screen = BattleScreen(battle, draft.Board, cell);
                battle.PointerDown(-1, screen);
                battle.PointerUp(-1, screen);
                if (battle.TryDraftDeploymentCell(cell, true) || !draft.Positions.ContainsKey(draft.PlayerId))
                    throw new InvalidOperationException("菜单打开时还能撤回地图上的单位");
                battle.SpaceAction();
                if (controller.State.Campaign.HasActiveBattle || !battle.DeploymentDraftActive)
                    throw new InvalidOperationException("菜单打开时空格还能提交开战");
            }
            else
            {
                if (!battle.Realtime.Model.Paused) throw new InvalidOperationException("打开菜单没有立即暂停战斗");
                battle.SpaceAction();
                if (!battle.Realtime.Model.Paused) throw new InvalidOperationException("菜单打开时空格恢复了战斗");
            }
            CaptureViewports(draft != null ? "ui-bandit-hideout-deployment-menu" : "ui-bandit-hideout-battle-menu");
            var overlay = Overlay();
            if (overlay.Find("MainMenu")?.GetComponent<Button>()?.interactable != true ||
                overlay.Find("Quit")?.GetComponent<Button>()?.interactable != true)
                throw new InvalidOperationException("战场菜单缺少回主菜单或退出游戏入口");
            Press(overlay, "Continue");
            if (Field(battle, "campaignMenu") != null || (draft != null && !positions.OrderBy(p => p.Key)
                    .SequenceEqual(draft.Positions.OrderBy(p => p.Key))) || (draft == null && !battle.Realtime.Model.Paused))
                throw new InvalidOperationException("返回战场没有保留部署或战斗暂停状态");
            Log.Add(draft != null ? "[PASS] 部署菜单位置、两个退出入口、拦截地图和空格、返回后保留落点" : "[PASS] 战斗菜单位置、两个退出入口、返回后保持暂停直至手动继续");
        }

        private static void VerifyDeploymentPointer(BattleSceneController battle)
        {
            var root = BattleSafeArea(battle);
            var draft = (BaseBattlePreparation)Field(H, "battleDraft");
            Canvas.ForceUpdateCanvases();
            if (root.Find("DraftPreparationControls/Start").GetComponent<Button>().interactable)
                throw new InvalidOperationException("主角未布置便可开战");
            PlaceByPointer(battle, draft, draft.PlayerId);
            Log.Add(androidTouch ? "[PASS] Touchscreen 点按战场部署主角，开战前未写进度" : "[PASS] 进场直接点选落位，开战前未写进度");
        }

        private static void PlaceByPointer(BattleSceneController battle, BaseBattlePreparation draft, string id)
        {
            int visible = 0, picked = 0, unblocked = 0;
            foreach (var point in draft.Board.Positions().Where(p => draft.CanPlace(id, p) && !draft.Positions.ContainsValue(p)))
            {
                var screen = (Vector2)battle.PreviewBoard.Camera.WorldToScreenPoint(BattleBoardView.World(point, draft.Board));
                if (!battle.PreviewBoard.Camera.pixelRect.Contains(screen)) continue;
                visible++;
                if (!battle.PreviewBoard.Pick(screen, out var hit) || hit != point) continue;
                picked++;
                if (battle.PreviewHud.BlocksPointer(screen)) continue;
                unblocked++;
                if (androidTouch)
                {
                    WithBattleTouch(battle, "ReadPointer", sample =>
                    {
                        sample(1, TouchPhase.Began, screen);
                        sample(1, TouchPhase.Ended, screen);
                    });
                }
                else
                {
                    battle.PointerDown(-1, screen);
                    battle.PointerUp(-1, screen);
                }
                if (!draft.Positions.TryGetValue(id, out var chosen)) continue;
                if (chosen != point) throw new InvalidOperationException("战场点击没有落在所选地格");
                return;
            }
            throw new InvalidOperationException($"从战场坐标点击未放置 {id}：可见 {visible} 格，拾取 {picked} 格，无遮挡 {unblocked} 格，反馈 {battle.Feedback}");
        }

        private static void VerifyContinuousDeployment(BattleSceneController battle)
        {
            var draft = (BaseBattlePreparation)Field(H, "battleDraft");
            var next = draft.Units.Where(u => u.Selected && u.Kind == SoldierKind.Hero && u.Id != draft.PlayerId).First();
            int length = draft.Units.Count(u => u.Selected && u.Kind != SoldierKind.Hero && u.TeamId == draft.PlayerId) + 2;
            Canvas.ForceUpdateCanvases();
            bool CanPoint(GridPosition cell)
            {
                if (!draft.CanPlace(draft.PlayerId, cell)) return false;
                var screen = BattleScreen(battle, draft.Board, cell);
                return !battle.PreviewHud.BlocksPointer(screen) && battle.PreviewBoard.Pick(screen, out var hit) && hit == cell;
            }
            var stroke = draft.Board.Positions().Select(p => Enumerable.Range(0, length)
                .Select(i => new GridPosition(p.X, p.Z + i)).ToArray()).FirstOrDefault(cells => cells.All(CanPoint));
            if (stroke == null) throw new InvalidOperationException("找不到连续可点的部署格");
            var from = BattleScreen(battle, draft.Board, stroke[0]);
            var to = BattleScreen(battle, draft.Board, stroke[stroke.Length - 1]);
            battle.PointerDown(-1, from);
            battle.PointerMove(-1, to);
            battle.PointerUp(-1, to);
            if (!draft.Positions.TryGetValue(draft.PlayerId, out var first) || first != stroke[0] ||
                !draft.Positions.TryGetValue(next.Id, out var second) || second != stroke[stroke.Length - 1])
                throw new InvalidOperationException("快划未补齐跨过的格或未自动衔接下一位队长");
            Log.Add("[PASS] 一次快划连续摆放首位队长、所属士兵和下一位队长");
        }

        private static Vector2 BattleScreen(BattleSceneController battle, BattleBoard board, GridPosition cell) =>
            battle.PreviewBoard.Camera.WorldToScreenPoint(BattleBoardView.World(cell, board));

        private static void WithBattleTouch(BattleSceneController battle, string reader,
            Action<Action<int, TouchPhase, Vector2>> verify)
        {
            var read = typeof(BattleSceneController).GetMethod(reader, BindingFlags.Instance | BindingFlags.NonPublic);
            // Batch mode has no focused Game view. Drive the player's Input System buffer explicitly.
            var update = typeof(InputSystem).GetMethod("Update", BindingFlags.Static | BindingFlags.NonPublic,
                null, new[] { typeof(InputUpdateType) }, null);
            var settings = InputSystem.settings;
            var originalEditorRouting = settings.editorInputBehaviorInPlayMode;
            var originalBackgroundInput = settings.backgroundBehavior;
            bool originalRunInBackground = Application.runInBackground;
            Touchscreen device = null;
            try
            {
                Application.runInBackground = true;
                settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
                settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
                device = InputSystem.AddDevice<Touchscreen>();
                verify((id, phase, screen) =>
                {
                    InputSystem.QueueStateEvent(device, new TouchState { touchId = id, phase = phase, position = screen });
                    update.Invoke(null, new object[] { InputUpdateType.Dynamic });
                    if (phase == TouchPhase.Began && !device.touches.Any(t => t.touchId.ReadValue() == id && t.press.wasPressedThisFrame))
                        throw new InvalidOperationException("合成触点没有送入 Play Mode 输入缓冲区");
                    read.Invoke(battle, null);
                });
            }
            finally
            {
                if (device != null) InputSystem.RemoveDevice(device);
                settings.editorInputBehaviorInPlayMode = originalEditorRouting;
                settings.backgroundBehavior = originalBackgroundInput;
                Application.runInBackground = originalRunInBackground;
            }
        }

        private static void VerifyBattleTouch(BattleSceneController battle)
        {
            var view = battle.PreviewBoard;
            var board = battle.PreviewSimulation.Board;
            var camera = view.Camera;
            float originalSize = camera.orthographicSize;
            var originalOffset = (Vector3)Field(view, "panOffset");
            var originalInspection = battle.RealtimeFineInformation;
            var originalFeedback = battle.Feedback;
            Canvas.ForceUpdateCanvases();
            bool MapPoint(Vector2 point) => camera.pixelRect.Contains(point) && !battle.PreviewHud.BlocksPointer(point) && view.Pick(point, out _);
            void NoCommand()
            {
                if (battle.RealtimeFineInformation != originalInspection || battle.Feedback != originalFeedback)
                    throw new InvalidOperationException("战场触屏手势误触发检视或指令");
            }
            try
            {
                view.SetViewSize(view.FullViewSize);
                Vector2 a = camera.pixelRect.center - Vector2.right * 48, b = camera.pixelRect.center + Vector2.right * 48;
                Vector2 outward = Vector2.right * 42;
                if (!MapPoint(a) || !MapPoint(b) || !MapPoint(a - outward) || !MapPoint(b + outward))
                    throw new InvalidOperationException("战场中央没有足够空间验证触屏手势");
                WithBattleTouch(battle, "ReadTouchPointer", sample =>
                {
                    var beforePan = camera.transform.position;
                    sample(1, TouchPhase.Began, a);
                    NoCommand();
                    sample(1, TouchPhase.Moved, a - outward);
                    sample(1, TouchPhase.Ended, a - outward);
                    NoCommand();
                    if (camera.transform.position == beforePan) throw new InvalidOperationException("单指拖动未平移战场");

                    float beforeZoom = camera.orthographicSize;
                    sample(2, TouchPhase.Began, a);
                    sample(3, TouchPhase.Began, b);
                    sample(2, TouchPhase.Moved, a - outward);
                    sample(3, TouchPhase.Moved, b + outward);
                    sample(2, TouchPhase.Ended, a - outward);
                    sample(3, TouchPhase.Ended, b + outward);
                    NoCommand();
                    if (camera.orthographicSize >= beforeZoom) throw new InvalidOperationException("双指捏合未缩放战场");

                    var target = board.Positions().Select(cell => (cell, point: BattleScreen(battle, board, cell)))
                        .First(hit => hit.cell != battle.InformationCell && MapPoint(hit.point));
                    sample(4, TouchPhase.Began, target.point);
                    NoCommand();
                    sample(4, TouchPhase.Canceled, target.point);
                    NoCommand();
                    sample(5, TouchPhase.Began, target.point);
                    NoCommand();
                    sample(5, TouchPhase.Ended, target.point);
                    if (battle.InformationCell != target.cell)
                        throw new InvalidOperationException("触屏抬手未检视目标地格");
                });
            }
            finally
            {
                view.SetViewSize(originalSize);
                view.Follow(originalOffset);
            }
            Log.Add("[PASS] Touchscreen 战斗单指平移、双指缩放与取消均不误下令，点按抬手检视目标地格");
        }

        private static void VerifyDeploymentSpace(BattleSceneController battle)
        {
            // Batch mode has no focused Game view. Exercise the command shared by both Space input paths.
            battle.SpaceAction();
            if (!H.DialogueOpen)
                throw new InvalidOperationException("空格对应的开战操作未提交：" + battle.Feedback);
        }

        private static void VerifyOpeningBattleSelection(BattleSceneController battle)
        {
            var leaders = battle.Realtime.Model.Units.Where(unit => unit.Alive &&
                unit.Role == PrototypeRole.Leader && unit.Side == PrototypeSide.Ally).OrderBy(unit => unit.Squad).ToArray();
            var strategies = leaders.Select(unit => unit.Strategy).ToArray();
            if (leaders.Length != 3 || !battle.SelectedUnits.SequenceEqual(leaders.Select(unit => unit.Id - 1)) ||
                !battle.RealtimeStatus.Contains("进行中"))
                throw new InvalidOperationException("开战未按编队顺位选中全部队长");
            var select = battle.GetType().GetMethod("SelectOpeningCommanders", BindingFlags.Instance | BindingFlags.NonPublic);
            for (int count = 3; count >= 0; count--)
            {
                for (int i = 0; i < leaders.Length; i++) leaders[i].Strategy = i < count ? PrototypeStrategy.Obey : PrototypeStrategy.Attack;
                select.Invoke(battle, null);
                if (!battle.SelectedUnits.SequenceEqual(leaders.Select(unit => unit.Id - 1)))
                    throw new InvalidOperationException($"开战默认选择受 {count} 名手动队长影响");
            }
            for (int i = 0; i < leaders.Length; i++)
            {
                leaders[i].Strategy = strategies[i];
                battle.ClickCommanderPortrait(i);
            }
            var player = battle.Realtime.Model.Player;
            var board = battle.PreviewSimulation.Board;
            var center = player.BodyCenter;
            var position = new Vector3(((float)center.X - board.Width * .5f) * BattleBoardView.CellSize, 0,
                ((float)center.Z - board.Height * .5f) * BattleBoardView.CellSize);
            var camera = battle.PreviewBoard.Camera;
            if (!battle.RealtimePointer(camera.WorldToScreenPoint(position + camera.transform.up * .55f)) ||
                battle.SelectedUnits.Count > 0)
                throw new InvalidOperationException("空选点击友军格重新选中了队长");
            battle.ClickCommanderPortrait(0);
            if (battle.SelectedUnits.Count == 0)
                throw new InvalidOperationException("主动点击头像未恢复指挥");
            battle.ClickCommanderPortrait(0);
            if (battle.SelectedUnits.Count > 0)
                throw new InvalidOperationException("再次点击当前头像未取消选中");
            Canvas.ForceUpdateCanvases();
            var area = BattleSafeArea(battle);
            Vector2 PortraitCenter(int slot)
            {
                var rect = (RectTransform)area.Find($"CommanderSlot{slot}/CommanderPortrait_{slot}");
                return RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(rect.rect.center));
            }
            battle.BeginPortraitSelection(0, PortraitCenter(0));
            battle.MovePortraitSelection(PortraitCenter(2));
            battle.EndPortraitSelection();
            if (battle.SelectedUnits.Count != 3 || leaders.Any(unit => !battle.SelectedUnits.Contains(unit.Id - 1)))
                throw new InvalidOperationException("滑过三张头像后没有选择三名队长");
            battle.BeginPortraitSelection(0, PortraitCenter(0));
            battle.MovePortraitSelection(PortraitCenter(2));
            battle.EndPortraitSelection();
            if (battle.SelectedUnits.Count != 0) throw new InvalidOperationException("滑过已选头像后未取消选择");
            battle.ResumeAfterDialogue(true);
            select.Invoke(battle, null);
            Log.Add("[PASS] 混合策略默认选中全队、空选点格查看、头像点击和滑选");
        }
        private static void AdvanceBattleToExit(BattleSceneController battle)
        {
            battle.ResumeAfterDialogue(true);
            for (int t = 0; t < 800 && battle.Realtime.Battle.Phase == BattlePhase.Fighting; t++)
                battle.AdvanceRealtime(.1);
            if (battle.Realtime.Battle.Phase != BattlePhase.Finished)
                throw new InvalidOperationException("等待预计离场的单位抵达出口超时");
        }

        private static object Field(object target, string name)
            => target?.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(target);

        private static void Note(string message)
        {
            if (EditorApplication.timeSinceStartup < nextLog) return;
            nextLog = EditorApplication.timeSinceStartup + 15;
            Debug.Log("[UiSkin] " + message + "（阶段 " + stage + "）");
        }

        private static void CheckCampScenerySpawnAndEdge()
        {
            var view = (BattleBoardView)Field(controller, "regionView");
            var scenery = view.Root.transform.Find("CampScenery");
            var state = controller.State;
            var layout = BaseSpatial.Layout(state);
            if (scenery == null || scenery.GetComponentsInChildren<Collider>().Length > 0 ||
                view.Board.Width != layout.Columns || view.Board.Height != layout.Rows)
                throw new InvalidOperationException("布景未建立、含有碰撞体积或修改了正式棋盘");
            Log.Add($"出生点 ({state.PlayerX:F2}, {state.PlayerY:F2})；探索视野 {view.Camera.orthographicSize:F2}");
            if (scenery.GetComponent<MeshRenderer>().sharedMaterials.Any(material =>
                material.GetTexture("_BaseMap")?.name.StartsWith("grass", StringComparison.Ordinal) != true))
                throw new InvalidOperationException("灰岩营地外围仍有非草地材质");
            Capture("exploration-camp-spawn");

            var move = typeof(BaseSceneController).GetMethod("MoveExploration", BindingFlags.Instance | BindingFlags.NonPublic);
            view.SetViewSize(view.FullViewSize * BattleBoardView.DefaultExplorationZoom);
            view.Follow(controller.PlayerCameraTarget);
            var startCamera = view.Camera.transform.position;
            for (int i = 0; i < 20; i++)
            {
                move.Invoke(controller, new object[] { Vector2.left, .05 });
                RequireCentered(view, controller.PlayerCameraTarget);
            }
            double edgeX = state.PlayerX, edgeY = state.PlayerY;
            if (edgeX < 0 || edgeX >= .65 || BaseSpatial.Walkable(controller.State, -.5, edgeY) ||
                view.Board.Cell(new GridPosition(-1, (int)edgeY)) != null)
                throw new InvalidOperationException($"西边缘的移动或布置边界异常：x={edgeX}");
            if (Vector3.Distance(startCamera, view.Camera.transform.position) < 1f)
                throw new InvalidOperationException("镜头没有跟随主角走到西边缘");
            var sceneryPoint = view.Camera.WorldToScreenPoint(controller.WorldPoint(-1.3, edgeY));
            if (!view.Camera.pixelRect.Contains(sceneryPoint) || view.Pick(sceneryPoint, out _))
                throw new InvalidOperationException("地图外草地未入画或被误判为部署格");
            Log.Add($"[PASS] 向西走到 x={edgeX:F2}，逐步核对主角始终对准画面中心，外景无可拾取棋盘格");
        }

        private static void CheckCampSceneryFullView()
        {
            var view = (BattleBoardView)Field(controller, "regionView");
            var point = view.Camera.pixelRect.center;
            controller.ZoomExplorationFactor(point, 100);
            if (Mathf.Abs(view.Camera.orthographicSize / view.FullViewSize - 1f) > .0001f)
                throw new InvalidOperationException("未恢复原来的最远探索范围");
            RequireCentered(view, controller.PlayerCameraTarget);
            controller.ZoomExplorationFactor(point, .001f);
            if (Mathf.Abs(view.Camera.orthographicSize / view.FullViewSize - .3f) > .0001f)
                throw new InvalidOperationException("最近档改变了");
            RequireCentered(view, controller.PlayerCameraTarget);
            view.SetViewSize(view.FullViewSize * BattleBoardView.DefaultExplorationZoom);
            CheckOuterCorners(view, view.Root.transform.Find("CampScenery"));
            Log.Add("[PASS] 复原最远档 1、最近档 .3，两个极限保持居中；默认视野下四个边角有外围地表覆盖");
        }

        private static void CheckTownFraming()
        {
            H.ClosePanel();
            if (controller.State.Campaign.Location != "town") throw new InvalidOperationException(controller.Message);
            var view = (BattleBoardView)Field(controller, "regionView");
            Approach("travel_base");
            view.SetViewSize(view.FullViewSize * BattleBoardView.DefaultExplorationZoom);
            view.Follow(controller.PlayerCameraTarget);
            var scenery = view.Root.transform.Find("ExplorationScenery");
            if (scenery == null)
                throw new InvalidOperationException("灰岩镇缺少外围布景");
            RequireCentered(view, controller.PlayerCameraTarget);
            CheckOuterCorners(view, scenery);
            Capture("exploration-town-centered-west");
            Capture("exploration-town-centered-west-narrow", 1024, 640);
        }

        private static void RequireCentered(BattleBoardView view, Vector3 target)
        {
            var point = view.Camera.WorldToViewportPoint(target);
            if (Mathf.Abs(point.x - .5f) > .0001f || Mathf.Abs(point.y - .5f) > .0001f)
                throw new InvalidOperationException($"角色没有居中：{point}");
        }

        private static void CheckOuterCorners(BattleBoardView view, Transform scenery)
        {
            var mesh = scenery.GetComponent<MeshFilter>();
            float raisedFocus = view.Board.Cells.Max(cell => cell.Elevation) * BaseTerrainView.LevelHeight + 1.2f;
            for (int side = 0; side < 4; side++)
            {
                bool east = (side & 1) != 0, north = (side & 2) != 0;
                var target = new Vector3((east ? 1 : -1) * view.Board.Width * BattleBoardView.CellSize * .5f,
                    raisedFocus, (north ? 1 : -1) * view.Board.Height * BattleBoardView.CellSize * .5f + 1.2f);
                view.Follow(target);
                RequireCentered(view, target);
                var corner = new Vector3(east ? .999f : .001f, north ? .999f : .001f);
                if (!BaseTerrainView.Raycast(mesh, view.Camera.ViewportPointToRay(corner), out _))
                    throw new InvalidOperationException($"布景未覆盖视口边角 {corner}");
            }
            view.Follow(controller.PlayerCameraTarget);
        }

        private static void CheckSceneryOcclusion()
        {
            var type = typeof(PixelCharacterSheet).Assembly.GetType("BaseConquest.Presentation.SceneryOcclusion");
            var root = new GameObject("Occlusion check");
            var camera = root.AddComponent<Camera>();
            camera.transform.SetPositionAndRotation(new Vector3(100, 10, 100), Quaternion.Euler(90, 0, 0));
            camera.orthographic = true;
            camera.orthographicSize = 2;
            camera.aspect = 1;
            camera.cullingMask = 1 << 30;
            const int size = 16;
            Texture2D Half(bool right, bool readable)
            {
                var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
                var pixels = new Color32[size * size];
                for (int y = 0; y < size; y++)
                    for (int x = 0; x < size; x++)
                        if (right ? x >= 10 : x < 6) pixels[y * size + x] = Color.white;
                texture.SetPixels32(pixels);
                texture.Apply(false, !readable);
                return texture;
            }

            var actorImage = Half(true, false);
            var decorImage = Half(false, true);
            var actorMesh = new Mesh { vertices = new[] { new Vector3(-1, 0, -1), new Vector3(-1, 0, 1),
                new Vector3(1, 0, 1), new Vector3(1, 0, -1) }, triangles = new[] { 0, 1, 2, 0, 2, 3 } };
            var actorObject = new GameObject("Actor", typeof(MeshFilter), typeof(MeshRenderer)) { layer = 30 };
            actorObject.transform.position = new Vector3(100, 0, 100);
            actorObject.GetComponent<MeshFilter>().sharedMesh = actorMesh;
            var actor = actorObject.GetComponent<Renderer>();
            var decorObject = GameObject.CreatePrimitive(PrimitiveType.Quad);
            decorObject.transform.SetPositionAndRotation(actorObject.transform.position, camera.transform.rotation);
            decorObject.transform.localScale = new Vector3(2, 2, 1);
            var material = new Material(Shader.Find("BaseConquest/SunSprite"));
            material.SetTexture("_BaseMap", decorImage);
            var decor = decorObject.GetComponent<Renderer>();
            decor.sharedMaterial = material;
            decor.sortingOrder = 1;
            type.GetMethod("Attach").Invoke(null, new object[] { decor, camera });
            type.GetMethod("TrackFrame").Invoke(null, new object[] { actor, actorMesh, actorImage, new Rect(0, 0, 1, 1) });
            try
            {
                var fade = decorObject.GetComponent(type);
                var properties = new MaterialPropertyBlock();
                int Sample()
                {
                    type.GetField("cachedFrame", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, -1);
                    type.GetMethod("LateUpdate", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(fade, null);
                    decor.GetPropertyBlock(properties);
                    return properties.GetInt("_ActorFadeCount");
                }
                if (Sample() != 0) throw new InvalidOperationException("只有透明画布交叠也触发了虚化");
                actorObject.transform.position += Vector3.left * .8f;
                if (Sample() != 1) throw new InvalidOperationException("可见角色实际被盖住时没有虚化");
                actor.sortingOrder = 2;
                if (Sample() != 0) throw new InvalidOperationException("角色处于前景仍触发虚化");
                actor.sortingOrder = 0;
                actor.forceRenderingOff = true;
                if (Sample() != 0) throw new InvalidOperationException("隐藏角色触发了虚化");
            }
            finally
            {
                type.GetMethod("Untrack").Invoke(null, new object[] { actor });
                foreach (var item in new Object[] { root, actorObject, decorObject, material, actorMesh, actorImage, decorImage }) Object.DestroyImmediate(item);
            }
            Log.Add("[PASS] 画布交叠、角色在前及隐藏均无虚化，真实像素遮挡产生虚化");
        }

        private static void CheckCampNames()
        {
            var strip = (RectTransform)Field(H, "resourceStrip");
            if (strip.Find("Location").GetComponent<Text>().text != BaseScriptNames.Camp)
                throw new InvalidOperationException("界面地名未使用灰岩营地");

            var oldState = controller.State.Copy();
            BaseSpatial.Layout(oldState).Title = "断流滩新营地";
            BaseSpatial.Layout(oldState, "town").Objects.Single(obj => obj.Id == "travel_base").Name = "← 返回营地";
            BaseEconomy.Validate(oldState);
            if (BaseCampaign.LocationName(oldState) != BaseScriptNames.Camp ||
                BaseSpatial.Layout(oldState, "town").Objects.Single(obj => obj.Id == "travel_base").Name != "← 返回" + BaseScriptNames.Camp)
                throw new InvalidOperationException("旧地图的营地标签没有随读取更新");

            H.OpenDemoStory();
            var dialogue = RequirePanel("NpcDialogue");
            H.ContinueDialogue();
            Click("嘶……这是哪儿？我睡了多久？");
            H.ContinueDialogue();
            if (dialogue.Find("SpeakerName").GetComponent<Text>().text != "老周" ||
                !dialogue.Find("DialogueText").GetComponent<Text>().text.Contains("灰岩镇西边的营地。"))
                throw new InvalidOperationException("苏醒对白未使用老周的口语称呼");
            Log.Add("[PASS] 实际顶栏为灰岩营地，旧地图标签可读，老周对白用口语称呼");
        }

        private static void CaptureViewports(string name, bool mobile = false)
        {
            Capture(name);
            Capture(name + "-1080p", 1920, 1080);
            Capture(name + "-narrow", 1024, 640);
            if (mobile)
            {
                Capture(name + "-854x480", 854, 480);
                Capture(name + "-960x432", 960, 432);
            }
        }

        internal static void Capture(string name, int w = 1280, int h = 720)
        {
            var cams = Camera.allCameras.Where(c => c.enabled && c.targetTexture == null).OrderBy(c => c.depth).ToList();
            if (cams.Count == 0 && controller?.WorldCamera != null) cams.Add(controller.WorldCamera);
            if (cams.Count == 0) { Log.Add("[失败] " + name + "：找不到相机"); failed = true; return; }
            var fit = GameFrameLayout.Fit(w, h, 0, 0, w, h);
            var area = new Rect((float)fit.X, (float)fit.Y, (float)fit.Width, (float)fit.Height);
            foreach (var frame in Object.FindObjectsByType<GameFrameCanvas>()) frame.ApplyLayout(area, w, h);
            var prev = RenderTexture.active;
            var tex = CaptureWorld(cams, area, w, h);
            var top = cams.LastOrDefault();
            var canvases = Object.FindObjectsByType<Canvas>().Where(c => c.isRootCanvas && c.isActiveAndEnabled && c.renderMode == RenderMode.ScreenSpaceOverlay).OrderBy(c => c.sortingOrder).ToArray();
            var layers = new Dictionary<GameObject, int>();
            if (top != null && canvases.Length > 0)
            {
                var uiRt = RenderTexture.GetTemporary(w, h, 24, RenderTextureFormat.ARGB32);
                var rect = top.rect; float aspect = top.aspect; var target = top.targetTexture;
                int mask = top.cullingMask; var clear = top.clearFlags; var bg = top.backgroundColor;
                foreach (var canvas in canvases)
                {
                    foreach (var t in canvas.GetComponentsInChildren<Transform>(true)) { layers[t.gameObject] = t.gameObject.layer; t.gameObject.layer = 31; }
                    canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = top; canvas.planeDistance = Mathf.Min(top.farClipPlane * .5f, top.nearClipPlane + 1f);
                }
                Canvas.ForceUpdateCanvases();
                top.targetTexture = uiRt; top.rect = new Rect(0, 0, 1, 1); top.aspect = w / (float)h; top.cullingMask = 1 << 31; top.clearFlags = CameraClearFlags.SolidColor; top.backgroundColor = new Color(0, 0, 0, 0); top.Render();
                top.cullingMask = mask; top.clearFlags = clear; top.backgroundColor = bg;
                foreach (var canvas in canvases) { canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.worldCamera = null; }
                foreach (var pair in layers) if (pair.Key != null) pair.Key.layer = pair.Value;
                var ui = Read(uiRt, w, h);
                var a = tex.GetPixels32(); var b = ui.GetPixels32();
                for (int i = 0; i < a.Length; i++)
                {
                    int al = b[i].a; if (al == 0) continue;
                    a[i] = new Color32((byte)Mathf.Min(255, b[i].r + a[i].r * (255 - al) / 255), (byte)Mathf.Min(255, b[i].g + a[i].g * (255 - al) / 255), (byte)Mathf.Min(255, b[i].b + a[i].b * (255 - al) / 255), 255);
                }
                tex.SetPixels32(a); tex.Apply(); Object.DestroyImmediate(ui);
                top.targetTexture = target; top.rect = rect; top.aspect = aspect;
                RenderTexture.active = prev;
                RenderTexture.ReleaseTemporary(uiRt);
            }
            foreach (var frame in Object.FindObjectsByType<GameFrameCanvas>()) frame.Apply();
            RenderTexture.active = prev;
            File.WriteAllBytes(Path.Combine(OutDir, name + ".png"), tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            Log.Add("## " + name + " ｜ 地点：" + controller?.State?.Campaign.Location + " ｜ 阶段：" + controller?.State?.Demo?.Stage
                + " ｜ 页面：" + (H != null && H.PanelOpen ? "有模态页" : "无模态页") + " ｜ Canvas=" + canvases.Length);
            Debug.Log("[UiSkin] 截图 " + name);
        }

        private static Texture2D CaptureWorld(IEnumerable<Camera> cameras, Rect gameArea, int width, int height)
        {
            var liveArea = GameFrameCanvas.Area;
            var liveScreen = new Rect(0, 0, Screen.width, Screen.height);
            var screen = new Rect(0, 0, width, height);
            var pixels = new Color32[width * height];
            foreach (var camera in cameras)
            {
                bool background = camera.cullingMask == 0;
                var source = background ? liveScreen : liveArea;
                var destination = background ? screen : gameArea;
                var viewport = camera.pixelRect;
                var scale = new Vector2(destination.width / source.width, destination.height / source.height);
                var min = Vector2.Max(destination.min, destination.min + Vector2.Scale(viewport.min - source.min, scale));
                var max = Vector2.Min(destination.max, destination.min + Vector2.Scale(viewport.max - source.min, scale));
                int left = Mathf.RoundToInt(min.x), right = Mathf.RoundToInt(max.x);
                int bottom = Mathf.RoundToInt(min.y), top = Mathf.RoundToInt(max.y);
                if (left >= right || bottom >= top) continue;

                int cameraWidth = right - left, cameraHeight = top - bottom;
                var target = RenderTexture.GetTemporary(cameraWidth, cameraHeight, 24);
                var active = RenderTexture.active;
                var savedTarget = camera.targetTexture;
                var savedRect = camera.rect;
                float savedAspect = camera.aspect;
                try
                {
                    // A camera renders across its entire target texture. Compose that image at the live viewport's position.
                    camera.targetTexture = target;
                    camera.rect = new Rect(0, 0, 1, 1);
                    camera.aspect = cameraWidth / (float)cameraHeight;
                    camera.Render();
                    var image = Read(target, cameraWidth, cameraHeight);
                    var cameraPixels = image.GetPixels32();
                    Object.DestroyImmediate(image);
                    for (int row = 0; row < cameraHeight; row++)
                        Array.Copy(cameraPixels, row * cameraWidth, pixels, (bottom + row) * width + left, cameraWidth);
                }
                finally
                {
                    camera.targetTexture = savedTarget;
                    camera.rect = savedRect;
                    camera.aspect = savedAspect;
                    RenderTexture.active = active;
                    RenderTexture.ReleaseTemporary(target);
                }
            }
            var result = new Texture2D(width, height, TextureFormat.RGBA32, false);
            result.SetPixels32(pixels);
            result.Apply();
            return result;
        }

        private static Texture2D Read(RenderTexture rt, int w, int h)
        {
            RenderTexture.active = rt; var tex = new Texture2D(w, h, TextureFormat.RGBA32, false); tex.ReadPixels(new Rect(0, 0, w, h), 0, 0); tex.Apply(); return tex;
        }

        private static void Finish(int code, string message)
        {
            if(stage.StartsWith("footstep-",StringComparison.Ordinal))StopFootstepCapture();
            EditorApplication.update -= Tick;
            Log.Insert(0, "exit=" + code + " " + message + " ｜ " + DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
            string report = campSceneryOnly ? "ui-exploration-framing-report.txt" : androidTouch ? "ui-android-touch-report.txt" :
                stage.StartsWith("footstep-",StringComparison.Ordinal) ? "footstep-recording-report.txt" :
                stage.StartsWith("route-pursuit-",StringComparison.Ordinal) ? "pursuit-presentation-report.txt" :
                stage == "opening-copy" ? "ui-opening-copy-report.txt" :
                stage.StartsWith("ruins-exit-", StringComparison.Ordinal) ? "ui-ruins-exit-report.txt" :
                stage.StartsWith("mage-roster-", StringComparison.Ordinal) ? "p52-roster-report.txt" :
                stage.StartsWith("soldiers-", StringComparison.Ordinal) ? "ui-soldier-roster-report.txt" :
                stage.StartsWith("civilian-", StringComparison.Ordinal) ? "ui-civilian-work-report.txt" :
                stage.StartsWith("route-", StringComparison.Ordinal) ? "ui-player-route-views-report.txt" : "ui-skin-report.txt";
            File.WriteAllText(Path.Combine(OutDir, report), string.Join("\n", Log));
            if (code == 0) Debug.Log("[UiSkin] " + message); else Debug.LogError("[UiSkin] " + message);
            failed = code != 0;
            EditorApplication.isPlaying = false;
            EditorApplication.Exit(code);
        }
    }
}

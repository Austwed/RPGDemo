using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BaseConquest.Base;
using BaseConquest.Battle;
using BaseConquest.Presentation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace BaseConquest.Editor
{
    public static class VerifyCampaignPlanningHud
    {
        private const string Output = "Logs/wood-ui", DeveloperKey = "BaseConquest.DeveloperMode";
        private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
        private const BindingFlags PrivateStatic = BindingFlags.Static | BindingFlags.NonPublic;
        private static readonly Queue<(BasePrepRoute Route, BaseState State)> Fixtures = new();
        private static (BasePrepRoute Route, BaseState State) currentFixture;
        private static int step, developerPreference;
        private static bool enteredSecondField;
        private static bool hadDeveloperPreference;
        private static double deadline;

        public static void Run()
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("请使用隔离检查进程。");
            Directory.CreateDirectory(Output);
            Environment.SetEnvironmentVariable("BASE_CONQUEST_UI_CHECK_SAVES",
                Path.GetFullPath(Path.Combine(Output, "check-saves", Guid.NewGuid().ToString("N"))));
            hadDeveloperPreference = PlayerPrefs.HasKey(DeveloperKey);
            developerPreference = PlayerPrefs.GetInt(DeveloperKey);
            EditorSceneManager.OpenScene(DemoStartup.Launcher);
            deadline = EditorApplication.timeSinceStartup + 240;
            Fixtures.Clear();
            step = 0;
            EditorApplication.update += Tick;
            EditorApplication.EnterPlaymode();
        }

        private static void Tick()
        {
            try
            {
                if (EditorApplication.timeSinceStartup > deadline) throw new TimeoutException($"界面检查超时 · 步骤 {step}");
                if (!EditorApplication.isPlaying || Object.FindAnyObjectByType<SceneLoadingCover>() != null) return;
                var owner = Object.FindAnyObjectByType<BaseSceneController>();
                if (owner?.State == null || owner.Hud == null || owner.Busy) return;
                switch (step)
                {
                    case 0:
                        owner.LoadDemoStage("defense", "male");
                        step++;
                        return;
                    case 1:
                        if (owner.State.Campaign.Location != "perimeter") return;
                        VerifyPreparation(owner);
                        BuildFixtures();
                        RestoreNextFixture(owner);
                        step++;
                        return;
                    case 2:
                        if (currentFixture.Route == BasePrepRoute.Second)
                        {
                            if (!VerifySecondWaveEntry(owner)) return;
                        }
                        else VerifyFixture(owner);
                        if (Fixtures.Count != 0) RestoreNextFixture(owner);
                        else
                        {
                            RestoreCaravanFixture(owner);
                            step++;
                        }
                        return;
                    case 3:
                        if (!BaseFacilityLayout.Built(owner.State, "caravan")) return;
                        VerifyCaravan(owner);
                        Finish(0);
                        return;
                }
            }
            catch (Exception error)
            {
                Debug.LogException(error);
                Finish(1);
            }
        }

        private static void VerifyPreparation(BaseSceneController owner)
        {
            owner.Hud.ClosePanel();
            var state = owner.State;
            var unchanged = JsonUtility.ToJson(state);
            typeof(BaseHud).GetMethod("OpenBattlePreparation", PrivateInstance).Invoke(owner.Hud, new object[] { BasePrepRoute.Defense });
            var panel = FindPanel(owner.Hud, "BattlePreparation");
            var forecast = panel.Find("Forecast");
            var after = state.Copy();
            var draft = (BaseBattlePreparation)typeof(BaseHud).GetField("battleDraft", PrivateInstance).GetValue(owner.Hud);
            BaseDevelopment.MarkFoughtToday(after, draft.Units.Where(unit => unit.Selected && unit.SoldierNumber > 0)
                .Select(unit => unit.SoldierNumber));
            var current = BaseEconomy.Preview(state.Copy());
            var projected = BaseEconomy.Preview(after);
            Need(forecast.Find("Food").GetComponent<Text>().text.EndsWith($"{current.FoodProduced} → {projected.FoodProduced}") &&
                forecast.Find("Supply").GetComponent<Text>().text.EndsWith($"{current.ProductsMade} → {projected.ProductsMade}") &&
                forecast.Find("EndFood").GetComponent<Text>().text.Contains($"{current.NextFood} → {projected.NextFood}"),
                "首防预估与实际生产计算不一致");
            Need(JsonUtility.ToJson(owner.State) == unchanged, "备战预估写入了实际进度");
            Canvas.ForceUpdateCanvases();
            var forecastBounds = RectTransformUtility.CalculateRelativeRectTransformBounds(panel, forecast);
            var teamsBounds = RectTransformUtility.CalculateRelativeRectTransformBounds(panel, panel.Find("Teams"));
            Need(teamsBounds.max.y < forecastBounds.min.y && panel.Find("Context").GetComponent<Text>().fontSize == 20,
                "预估区与队长卡重叠，或目标文字未放大");
            CaptureBoth("campaign-prep-defense");
        }

        private static void BuildFixtures()
        {
            // Read the same shipped opening and drive the same first-defence completion as the race-free campaign checks.
            var defense = LoadStage("defense");
            At(defense, "demo_defense_start");
            defense.Inventory.Equipment.Single(item => item.Product == "supply" && item.Level == 1).Count = 24;
            var draft = new BaseBattlePreparation(defense, BasePrepRoute.Defense);
            Need(draft.AutoPlace() > 0, "首防夹具无法部署");
            var battle = (RealtimeCampaignBattle)PrepHarness("Start", defense, draft);
            BaseDemoDefense.Configure(battle);
            PrepHarness("Win", defense, battle, (Func<bool>)(() => BaseDemoDefense.Complete(defense, battle)));
            var advance = typeof(BaseEconomy).GetMethod("CompleteProductionDay", PrivateStatic);
            foreach (string fate in new[] { "escaped", "dead" })
            {
                var second = defense.Copy();
                second.Demo.Defense.BatuFate = fate;
                second.Campaign.Location = "base";
                At(second, "demo_march_exit");
                BaseDemoDefense.AcknowledgeBatu(second);
                BaseDemoFinale.OpenWorld(second);
                int arrival = BaseDemoDefense.SecondArrival(second);
                while (second.Day < arrival) advance.Invoke(null, new object[] { second, false });
                At(second, "demo_defense_exit");
                BaseEconomy.Validate(second);
                second.Muster.SlotTroops[0].Melee = Math.Max(second.Muster.SlotTroops[0].Melee, 1);
                Fixtures.Enqueue((BasePrepRoute.Second, second));
            }

            BaseDemoDefense.AcknowledgeBatu(defense);
            var march = defense.Copy();
            march.Campaign.Location = "base";
            At(march, "demo_march_exit");
            BaseDemoFinale.OpenWorld(march);
            Fixtures.Enqueue((BasePrepRoute.Travel, march));

            var attack = march.Copy();
            var departure = new BaseBattlePreparation(attack, BasePrepRoute.Travel);
            Need(BaseMuster.Confirm(attack, departure.TravelPlan(attack)), "攻营夹具无法编队");
            At(attack, "demo_march_exit");
            BaseWorldJourney.EnterWorld(attack, "camp_entry");
            BaseWorldJourney.Start(attack, new[] { "camp", "bandit" });
            Need(BaseWorldJourney.Step(attack, _ => null), "攻营夹具行军失败");
            BaseWorldJourney.EnterScene(attack, "bandit_entry");
            At(attack, "demo_camp_attack");
            Fixtures.Enqueue((BasePrepRoute.BlackCrow, attack));

            var ruins = LoadStage("camp");
            At(ruins, "home");
            Need(BaseDemo.AcknowledgeWaking(ruins), "古窟夹具无法进入营地");
            At(ruins, "exit_east");
            Need(BaseCampaign.Travel(ruins, "town"), "古窟夹具无法进镇");
            At(ruins, "town_quest");
            Need(BaseDemo.AcceptLordQuest(ruins), "古窟夹具无法接领主任务");
            At(ruins, "demo_ruins_exit");
            Need(BaseCampaign.Travel(ruins, "outpost"), "古窟夹具无法抵达入口");
            At(ruins, "demo_ruins_investigate");
            Fixtures.Enqueue((BasePrepRoute.Ruins, ruins));
            var hideout = ruins.Copy();
            BaseBanditHideout.Install(hideout);
            At(hideout, BaseBanditHideout.EntryId);
            VerifyBanditHideout.AddSoldiers(hideout, 4, 3, 3);
            Fixtures.Enqueue((BasePrepRoute.Hideout, hideout));
        }

        private static BaseState LoadStage(string id) => (BaseState)PrepHarness("Load", id);
        private static void At(BaseState state, string id) => PrepHarness("At", state, id);
        private static object PrepHarness(string method, params object[] args) =>
            typeof(VerifyBattlePreparation).GetMethod(method, PrivateStatic).Invoke(null, args);

        private static void RestoreNextFixture(BaseSceneController owner)
        {
            currentFixture = Fixtures.Dequeue();
            enteredSecondField = false;
            owner.SaveStore.RestoreSession(currentFixture.State);
            owner.LoadSaved();
        }

        private static bool VerifySecondWaveEntry(BaseSceneController owner)
        {
            const string gate = "demo_defense_exit", start = "demo_defense_start";
            var state = owner.State;
            var (route, expected) = currentFixture;
            Need(route == BasePrepRoute.Second && state.Day == expected.Day &&
                state.Demo.Defense.BatuFate == expected.Demo.Defense.BatuFate &&
                BaseDemoFinale.Opened(state) && BaseDemoDefense.SecondDue(state), "第二波路线夹具未到达");
            BaseInteraction.Facing = default;
            if (!enteredSecondField)
            {
                Need(state.Campaign.Location == "base" && state.Campaign.CurrentMapId == "demo_camp" &&
                    BaseInteraction.At(state, "base", gate), "第二波应从营地真实出口出发");
                owner.Hud.ClosePanel();
                string workers = RosterBindings(state);
                owner.Hud.OpenObject(gate);
                Need(owner.State.Campaign.Location == "perimeter" && owner.State.Campaign.CurrentMapId == "demo_field",
                    "营地出口未进入外围：" + owner.Message);
                Need(!owner.State.Muster.Parties.Any(party => party.Purpose == "defense") &&
                    workers == RosterBindings(owner.State) && owner.State.Day == state.Day,
                    "营地转场提前抽兵或推进了日期");
                enteredSecondField = true;
                return false;
            }

            Need(state.Campaign.Location == "perimeter" && state.Campaign.CurrentMapId == "demo_field",
                "第二波外围场景未载入");
            owner.Hud.ClosePanel();
            At(state, start);
            if (state.Demo.Defense.BatuFate == "dead")
            {
                var old = BaseMuster.PreviewFormation(state, "defense", "combat");
                Need(BaseMuster.Confirm(state, old), "无法恢复旧防守选兵样本");
                int before = state.Workers.Sum(worker => worker.Count);
                owner.Hud.OpenObject(start);
                FindPanel(owner.Hud, "BattlePreparation");
                state = owner.State;
                Need(state.Workers.Sum(worker => worker.Count) == before &&
                    !state.Muster.Parties.Any(party => party.Purpose == "defense"),
                    "旧预备守军进入统一备战时未归队");
                owner.Hud.ClosePanel();
            }
            var stock = state.Inventory.Equipment.Single(item => item.Product == "supply" && item.Level == 1);
            stock.Count = 0;
            string untouched = JsonUtility.ToJson(state);
            owner.Hud.OpenObject(start);
            var panel = FindPanel(owner.Hud, "BattlePreparation");
            var go = panel.Find("Continue").GetComponent<Button>();
            Need(!go.interactable && panel.Find("Feedback").GetComponent<Text>().text.Contains("军需预警") &&
                !state.Campaign.HasActiveBattle && JsonUtility.ToJson(owner.State) == untouched,
                "不足一级军需时应明确提示并阻止开战");
            panel.Find("Close").GetComponent<Button>().onClick.Invoke();
            Need(JsonUtility.ToJson(owner.State) == untouched, "关闭备战写入实际进度");
            stock.Count = 24;
            untouched = JsonUtility.ToJson(state);
            owner.Hud.OpenObject(start);
            panel = FindPanel(owner.Hud, "BattlePreparation");
            var draft = (BaseBattlePreparation)typeof(BaseHud).GetField("battleDraft", PrivateInstance).GetValue(owner.Hud);
            go = panel.Find("Continue").GetComponent<Button>();
            Need(draft.Route == BasePrepRoute.Second && panel.Find("Title").GetComponent<Text>().text.Contains("第二波") &&
                panel.Find("Priority")?.gameObject.activeSelf != true && go.interactable &&
                JsonUtility.ToJson(owner.State) == untouched, "外围入口未打开正确的统一备战");
            CaptureBoth("campaign-prep-second-" + state.Demo.Defense.BatuFate);
            go.onClick.Invoke();
            var combat = Object.FindObjectsByType<BattleSceneController>().Single(view => view.DeploymentDraftActive);
            Button DeployButton(string id) =>
                combat.GetComponentsInChildren<RectTransform>().Single(control => control.name == "DraftPreparationControls")
                    .Find(id).GetComponent<Button>();
            Button Reply(string phrase)
            {
                var dialogue = FindPanel(owner.Hud, "NpcDialogue");
                dialogue.GetComponentsInChildren<Button>().Single(button => button.name == "SkipDialogue").onClick.Invoke();
                return dialogue.GetComponentsInChildren<Button>().Single(button =>
                    button.name == "Reply" && button.GetComponentInChildren<Text>().text == phrase);
            }
            DeployButton("Auto").onClick.Invoke();
            Need(draft.Positions.Count == draft.Units.Count(unit => unit.Selected) && DeployButton("Start").interactable,
                "第二波一键布阵或确认按钮未生效");
            DeployButton("Start").onClick.Invoke();
            Reply("暂缓迎战").onClick.Invoke();
            Need(combat.DeploymentDraftActive && !combat.ExternalDialogue && !owner.State.Campaign.HasActiveBattle &&
                JsonUtility.ToJson(owner.State) == untouched, "暂缓迎战应回到未提交的布阵");
            DeployButton("Start").onClick.Invoke();
            Reply("准备迎战").onClick.Invoke();
            Need(owner.State.Campaign.HasActiveBattle && owner.State.Campaign.ActiveBattle.EventId == BaseDemoDefense.SecondEventId &&
                owner.State.Demo.Defense.SecondBriefingSeen && !combat.DeploymentDraftActive && combat.IsRealtimeCampaign,
                "正式确认后未启动正确的即时战斗：" + owner.Message);
            Debug.Log("[SecondWaveEntry] PASS " + state.Demo.Defense.BatuFate);
            return true;
        }

        private static string RosterBindings(BaseState state) => string.Join("|", state.Workers
            .SelectMany(worker => worker.SoldierNumbers.Select(number =>
                number + ":" + worker.Kind + ":" + worker.Away + ":" + worker.PartyId))
            .OrderBy(key => key, StringComparer.Ordinal));

        private static void VerifyFixture(BaseSceneController owner)
        {
            var (route, expected) = currentFixture;
            var state = owner.State;
            Need(expected.Day == state.Day && expected.Campaign.Location == state.Campaign.Location &&
                expected.Campaign.CurrentMapId == state.Campaign.CurrentMapId &&
                Math.Abs(expected.PlayerX - state.PlayerX) < .01 && Math.Abs(expected.PlayerY - state.PlayerY) < .01,
                "未载入备战夹具 " + route + " · " + owner.Message);
            owner.Hud.ClosePanel();
            BaseInteraction.Facing = default;
            if (route == BasePrepRoute.Travel)
            {
                typeof(BaseSceneController).GetField("pendingWorldEntry", PrivateInstance).SetValue(owner, "camp_entry");
                typeof(BaseSceneController).GetMethod("PrepareWorldMarch", PrivateInstance)
                    .Invoke(owner, new object[] { new[] { "camp", "bandit" } });
            }
            else typeof(BaseHud).GetMethod("OpenBattlePreparation", PrivateInstance).Invoke(owner.Hud, new object[] { route });

            var panel = FindPanel(owner.Hud, "BattlePreparation");
            var draft = (BaseBattlePreparation)typeof(BaseHud).GetField("battleDraft", PrivateInstance).GetValue(owner.Hud);
            var forecast = panel.Find("Forecast");
            Need(draft.Route == route && panel.Find("Context").GetComponent<Text>().text.Length > 0 &&
                forecast.Find("Food").GetComponent<Text>().text.Contains("→"), "备战页未复用目标与变化区域 " + route);
            if (route == BasePrepRoute.Travel)
            {
                var projection = state.Copy();
                BaseWorldJourney.Depart(projection, "camp_entry", new[] { "camp", "bandit" }, draft);
                Need(forecast.Find("RoadFood").GetComponent<Text>().text.Contains($"{state.Inventory.Food} → {projection.Inventory.Food}") &&
                    !forecast.Find("EndFood").GetComponent<Text>().text.EndsWith("—"), "行军库存预估与真实预扣不符");
            }
            if (route == BasePrepRoute.Ruins)
                Need(panel.Find("Enemy/Value").GetComponent<Text>().text.Contains("暂无已知情报"), "古窟首次备战泄露敌情");
            CaptureBoth("campaign-prep-" + route.ToString().ToLowerInvariant());
        }

        private static void CaptureBoth(string name)
        {
            UiSkinScreenshot.Capture(name);
            UiSkinScreenshot.Capture(name + "-wide", 960, 432);
        }

        private static void RestoreCaravanFixture(BaseSceneController owner)
        {
            var state = JsonUtility.FromJson<BaseState>(Resources.Load<TextAsset>("DemoStages/camp-male").text);
            BaseEconomy.Validate(state);
            var tent = BaseStageWalk.Door(state, "home");
            state.PlayerX = tent.X;
            state.PlayerY = tent.Y;
            Need(BaseDemo.AcknowledgeWaking(state), "无法进入营地");
            state.Layout.OfficeGift = true;
            state.Inventory.Food = 300;
            state.Inventory.Materials = Math.Max(50, state.Inventory.Materials);
            Need(BaseFacilityLayout.AutoPlace(state, "caravan", out var problem, out _, out _), problem);
            VerifyBanditHideout.AddSoldiers(state, 2, 0, 0);
            BaseDevelopment.Allocate(state);
            var door = BaseStageWalk.Door(state, "caravan");
            state.PlayerX = door.X;
            state.PlayerY = door.Y;
            RuntimeDeveloperOptions.Enabled = false;
            owner.SaveStore.RestoreSession(state);
            owner.LoadSaved();
        }

        private static void VerifyCaravan(BaseSceneController owner)
        {
            owner.Hud.ClosePanel();
            BaseInteraction.Facing = default;
            var mapObject = owner.GetComponentsInChildren<AuthoredMapView>()
                .Single(view => view.name.StartsWith("caravan ·", StringComparison.Ordinal));
            var mapArt = mapObject.GetComponentInChildren<SpriteFacade>()?.Surface?.sharedMaterial?.GetTexture("_BaseMap");
            Need(mapArt != null && mapArt.name == "caravan-v1", "营地商队设施仍为占位外观");
            UiSkinScreenshot.Capture("campaign-caravan-map");
            owner.Hud.OpenObject("caravan");
            var panel = FindPanel(owner.Hud, "FacilityPanel");
            Button Pressable(string key) => panel.GetComponentsInChildren<Button>(true).Single(b => b.name == key);
            Button FirstWorker() => panel.Find("Right/List").GetComponent<ScrollRect>().content
                .GetComponentsInChildren<Button>().First(b => b.interactable && b.name.StartsWith("Soldier"));
            Need(panel.Find("Left/TripActions") != null && !Pressable("Dispatch").interactable &&
                panel.Find("Left/TripActions/Crow") == null, "无人员时不应能出发，正式路线只显示领地内跑商");
            Need(panel.GetComponentsInChildren<RawImage>().Any(image => image.name == "CaravanArt" && image.texture != null),
                "商队窗口缺少设施贴图");
            int before = owner.State.Inventory.Food;
            FirstWorker().onClick.Invoke();
            Need(BaseCaravan.Crew(owner.State) == 0 && !Pressable("Dispatch").interactable, "人员草稿提前应用");
            Pressable("CancelCrew").onClick.Invoke();
            Need(!Pressable("ConfirmCrew").interactable, "取消后应清空草稿");
            FirstWorker().onClick.Invoke();
            Pressable("ConfirmCrew").onClick.Invoke();
            Need(BaseCaravan.Crew(owner.State) == 1 && Pressable("Dispatch").interactable, "无法从名册确认商队人员");
            UiSkinScreenshot.Capture("campaign-caravan-ready");
            Pressable("Dispatch").onClick.Invoke();
            Need(owner.State.Caravan.Route == "local" && owner.State.Caravan.DaysProvisioned == BaseCaravan.TripDays &&
                owner.State.Inventory.Food == before - BaseCaravan.DailyFood(owner.State) * BaseCaravan.TripDays,
                "商队未按实际人员预扣全程口粮");
            UiSkinScreenshot.Capture("campaign-caravan-travel-wide", 960, 432);
            Pressable("Recall").onClick.Invoke();
            Need(owner.State.Caravan.Route == "" && owner.State.Inventory.Food == before && BaseCaravan.Crew(owner.State) == 0,
                "召回未退还余粮和释放人员");
        }

        private static Transform FindPanel(BaseHud hud, string key) =>
            hud.VisualRoot.GetComponentsInChildren<RectTransform>().Single(node => node.name == key);

        private static void Need(bool pass, string message)
        {
            if (!pass) throw new InvalidOperationException(message);
        }

        private static void Finish(int exit)
        {
            EditorApplication.update -= Tick;
            if (hadDeveloperPreference) PlayerPrefs.SetInt(DeveloperKey, developerPreference);
            else PlayerPrefs.DeleteKey(DeveloperKey);
            PlayerPrefs.Save();
            if (exit == 0) Debug.Log("[VerifyCampaignPlanningHud] PASS 六类备战、生产与路粮预估、商队名册和召回");
            EditorApplication.Exit(exit);
        }
    }
}

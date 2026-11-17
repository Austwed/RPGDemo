using System.Linq;
using BaseConquest.Base;
using BaseConquest.Presentation;
using UnityEngine;
using UnityEngine.UI;

namespace BaseConquest.Battle
{
    public sealed partial class BattleMobileHud
    {
        private RectTransform formalBanner, formalHeading, formalSummary;
        private Text formalMap, formalGoal, formalStatus, formalAlly, formalEnemy, formalSupply;
        private float formalExpandedHeight;
        private Button formalSummaryToggle;
        private bool formalSummaryFolded;
        private readonly RectTransform[] formalTroopRows = new RectTransform[3];
        private readonly Button[] formalTroopToggles = new Button[3];
        private readonly bool[] formalTroopsFolded = new bool[3];
        private readonly Button[,] formalTroops = new Button[3, 3];
        private readonly Text[,] formalTroopCounts = new Text[3, 3];

        private void CreateFormalBanner()
        {
            formalBanner = Box("FormalBanner", safe, new Vector2(.5f, 1), Vector2.zero, new Vector2(0, 76));
            formalBanner.anchorMin = new Vector2(0, 1);
            formalBanner.anchorMax = Vector2.one;
            formalBanner.GetComponent<Image>().color = DemoUiTheme.Current.Wood;
            formalHeading = Rect("Heading", formalBanner, new Vector2(.5f, 1), Vector2.zero, new Vector2(0, 42));
            formalHeading.anchorMin = new Vector2(0, 1);
            formalHeading.anchorMax = Vector2.one;
            formalMap = BannerLabel("MapName", formalHeading, 18, 0, 0, 16, 210);
            formalMap.fontStyle = FontStyle.Bold;
            formalGoal = BannerLabel("BattleGoal", formalHeading, 14, 0, 1, 222, -368);
            formalStatus = BannerLabel("BattleStatus", formalHeading, 14, 1, 1, -375, -290);
            formalStatus.alignment = TextAnchor.MiddleRight;
            formalSummaryToggle = Button("DraftSummaryToggle", formalBanner, Vector2.one, new Vector2(-126, -9), new Vector2(28, 33), () =>
            {
                formalSummaryFolded = !formalSummaryFolded;
                Refresh();
            });
            formalSummary = Rect("FormalSummary", formalBanner, new Vector2(.5f, 1), new Vector2(0, -42), new Vector2(0, 34));
            formalSummary.anchorMin = new Vector2(0, 1);
            formalSummary.anchorMax = Vector2.one;
            formalAlly = BannerLabel("AllyCounts", formalSummary, 14, 0, .34f, 16, -8);
            formalEnemy = BannerLabel("EnemyCounts", formalSummary, 14, .34f, .68f, 8, -8);
            formalSupply = BannerLabel("SupplyCounts", formalSummary, 14, .68f, 1, 8, -16);
            formalBanner.gameObject.SetActive(false);
        }

        private void CreateRealtimeControls()
        {
            realtimeControls = Rect("RealtimeControls", safe, new Vector2(1, 0), new Vector2(-14, 44), new Vector2(390, 44));
            string[] names = { "Manual", "Attack", "Defense" };
            string[] labels = { "手动", "进攻", "防守" };
            for (int i = 0; i < realtimeStrategies.Length; i++)
            {
                var strategy = TierStrategies[i];
                realtimeStrategies[i] = RealtimeButton(names[i], labels[i], i, () => owner.ChooseRealtimeStrategy(strategy));
            }
            realtimePause = RealtimeButton("Pause", "继续", 3, owner.ToggleRealtimePause);
            realtimeSpeed = RealtimeButton("Speed", "1倍", 4, owner.ToggleFast);
            realtimeControls.gameObject.SetActive(false);
        }

        private Button RealtimeButton(string name, string label, int index, System.Action action)
        {
            var button = Button(name, realtimeControls, Vector2.zero, new Vector2(index * 79, 0), new Vector2(74, 44), action);
            button.GetComponentInChildren<Text>().text = label;
            return button;
        }

        private void CreatePracticeGuide()
        {
            practiceInstruction = Rect("PracticeInstruction", safe, new Vector2(0, 1), new Vector2(12, -115), new Vector2(420, 54));
            var panel = practiceInstruction.gameObject.GetOrAdd<Image>();
            panel.color = DemoUiTheme.Current.Wood;
            panel.raycastTarget = false;
            practiceInstructionText = Label("", practiceInstruction, 16);
            practiceInstructionText.color = DemoUiTheme.Current.Panel;
            practiceInstructionText.alignment = TextAnchor.MiddleLeft;
            practiceInstructionText.rectTransform.offsetMin = new Vector2(12, 4);
            practiceInstructionText.rectTransform.offsetMax = new Vector2(-12, -4);
            practiceArrow = GuidanceArrow.Create(safe, "PracticeArrow", 42);
        }

        private void RefreshPracticeControl(BattleSimulation battle)
        {
            if (practiceScene == null) return;
            var intro = practiceScene.State?.Demo?.Intro;
            int step = owner.PracticeStep;
            bool visible = battle.Phase == BattlePhase.Fighting && step != 0 && !owner.DetailsVisible && !owner.CampaignMenuOpen;
            practiceInstruction.gameObject.SetActive(visible && !string.IsNullOrWhiteSpace(owner.PracticeText));
            practiceInstructionText.text = owner.PracticeText;
            Button target = !visible ? null : step switch
            {
                BaseDemoIntro.Select => portraits[0],
                BaseDemoIntro.Pause => realtimePause,
                BaseDemoIntro.Skill => skills[0],
                BaseDemoIntro.AutoAttack when intro?.DummyPhase < 3 => realtimeStrategies[1].interactable ? realtimeStrategies[1] : portraits[0],
                BaseDemoIntro.Move or BaseDemoIntro.AutoAttack when !owner.Automatic => realtimePause,
                _ => null
            };
            var cue = target != null && target.gameObject.activeInHierarchy ? target.GetComponent<DemoButtonTint>() : null;
            if (cue != practiceCue)
            {
                practiceCue?.SetGuide(false);
                practiceCue = cue;
                practiceCue?.SetGuide(true);
            }
            if (target != null && target.gameObject.activeInHierarchy && target.IsInteractable())
                practiceArrow.PointTo((RectTransform)target.transform);
            else if (visible && owner.TryPracticeMapTarget(step, out var point))
                practiceArrow.PointToMap(point, point);
            else practiceArrow.Hide();
        }

        private Text BannerLabel(string name, RectTransform parent, int size, float min, float max, float left, float right)
        {
            var text = Label("", parent, size, name);
            text.color = DemoUiTheme.Current.Panel;
            text.alignment = TextAnchor.MiddleLeft;
            var rect = text.rectTransform;
            rect.anchorMin = new Vector2(min, 0);
            rect.anchorMax = new Vector2(max, 1);
            rect.offsetMin = new Vector2(left, 0);
            rect.offsetMax = new Vector2(right, 0);
            return text;
        }

        private void ApplyFormalLayout(bool authored)
        {
            formalBanner.SetAsFirstSibling();
            formalExpandedHeight = formalBanner.rect.height;
            if (!authored)
            {
                formalBanner.GetComponent<Image>().color = DemoUiTheme.Current.Wood;
                foreach (var label in new[] { formalMap, formalGoal, formalStatus, formalAlly, formalEnemy, formalSupply })
                    label.color = DemoUiTheme.Current.Panel;
            }
        }

        private void ApplySystemMenuLayout(RectTransform source)
        {
            var menu = (RectTransform)drawer.transform;
            if (source != null) MoveTopControl(drawer, source);
            else
            {
                menu.anchorMin = menu.anchorMax = menu.pivot = Vector2.one;
                menu.anchoredPosition = new Vector2(-14, -9);
                menu.sizeDelta = new Vector2(106, 33);
            }
            if (owner.FormalFieldHud) return;
            float x = menu.anchoredPosition.x - menu.rect.width - 6;
            foreach (var control in new[] { skip, fast, automatic })
            {
                var rect = (RectTransform)control.transform;
                rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.one;
                rect.anchoredPosition = new Vector2(x, menu.anchoredPosition.y);
                x -= rect.rect.width + 6;
            }
        }

        private void ApplyAndroidControls()
        {
            if (owner.UsesCampaignSystemMenu)
            {
                PlaceTouchButton(drawer, -14, -9, 106, 52, 22);
                if (!owner.FormalFieldHud)
                {
                    PlaceTouchButton(skip, -128, -9, 70, 52, 20);
                    PlaceTouchButton(fast, -206, -9, 70, 52, 20);
                    PlaceTouchButton(automatic, -284, -9, 92, 52, 20);
                }
            }

            for (int i = 0; i < portraits.Length; i++)
            {
                var portrait = (RectTransform)portraits[i].transform;
                var module = (RectTransform)portrait.parent;
                module.anchoredPosition = new Vector2(10 + i * 126, module.anchoredPosition.y);
                module.sizeDelta = new Vector2(118, 142);
                portrait.anchoredPosition = new Vector2(0, owner.FormalFieldHud ? 0 : 56);
                portrait.sizeDelta = new Vector2(118, 86);
                portraitLabels[i].fontSize = 16;
                PlaceTouchButton(skills[i], 0, owner.FormalFieldHud ? 91 : 0, 72, 52, 17);
                if (!owner.FormalFieldHud) continue;
                var row = formalTroopRows[i];
                row.anchoredPosition = new Vector2(0, 147);
                row.sizeDelta = new Vector2(118, 176);
                PlaceTouchButton(formalTroopToggles[i], 76, 91, 42, 52, 21);
                for (int troop = 0; troop < BattleSimulation.TroopKinds.Length; troop++)
                    PlaceTouchButton(formalTroops[i, troop], 0, (2 - troop) * 60, 118, 56, 21);
            }

            if (!owner.FormalFieldHud) return;

            formalHeading.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, 68);
            formalSummary.anchoredPosition = new Vector2(0, -68);
            formalSummary.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, 40);
            formalExpandedHeight = 108;
            formalMap.fontSize = 23;
            formalGoal.fontSize = formalStatus.fontSize = 19;
            formalAlly.fontSize = formalEnemy.fontSize = formalSupply.fontSize = 18;
            formalGoal.rectTransform.offsetMax = new Vector2(-288, 0);
            formalStatus.rectTransform.offsetMin = new Vector2(-282, 0);
            formalStatus.rectTransform.offsetMax = new Vector2(-188, 0);
            PlaceTouchButton(formalSummaryToggle, -128, -9, 56, 52, 23);
            status.fontSize = 17;
            infoPanel.anchoredPosition = new Vector2(infoPanel.anchoredPosition.x, -114);
            logPanel.anchoredPosition = new Vector2(logPanel.anchoredPosition.x, -327);
            information.fontSize = log.fontSize = 16;

            draftControls.sizeDelta = new Vector2(564, 66);
            string[] actions = { "Back", "Auto", "Clear", "Start" };
            for (int i = 0; i < actions.Length; i++)
                PlaceTouchButton(draftControls.Find(actions[i]).GetComponent<Button>(), i * 143, 2, 134, 62, 22);

            realtimeControls.sizeDelta = new Vector2(464, 64);
            var actionsLive = realtimeStrategies.Concat(new[] { realtimePause, realtimeSpeed }).ToArray();
            for (int i = 0; i < actionsLive.Length; i++)
                PlaceTouchButton(actionsLive[i], 94 * i, 2, 86, 60, 22);
        }

        private static void PlaceTouchButton(Button button, float x, float y, float width, float height, int fontSize)
        {
            var rect = (RectTransform)button.transform;
            rect.anchoredPosition = new Vector2(x, y);
            rect.sizeDelta = new Vector2(width, height);
            button.GetComponentInChildren<Text>(true).fontSize = fontSize;
        }

        private static void MoveTopControl(Button control, RectTransform anchor)
        {
            var rect = (RectTransform)control.transform;
            rect.anchorMin = anchor.anchorMin;
            rect.anchorMax = anchor.anchorMax;
            rect.pivot = anchor.pivot;
            rect.anchoredPosition = anchor.anchoredPosition;
            rect.sizeDelta = anchor.sizeDelta;
        }

        private void CreateFormalTroops(RectTransform module, int slot)
        {
            var row = Rect("DraftTroops", module, Vector2.zero, new Vector2(0, 134), new Vector2(92, 146));
            formalTroopRows[slot] = row;
            formalTroopToggles[slot] = Button("TroopToggle", module, Vector2.zero, new Vector2(64, 91), new Vector2(28, 38), () =>
            {
                formalTroopsFolded[slot] = !formalTroopsFolded[slot];
                Refresh();
            });
            for (int i = 0; i < BattleSimulation.TroopKinds.Length; i++)
            {
                var kind = BattleSimulation.TroopKinds[i];
                var button = Button("Deploy_" + kind, row, Vector2.zero, new Vector2(0, (2 - i) * 50), new Vector2(92, 46),
                    () => owner.SelectDraftTroop(slot, kind));
                var count = button.GetComponentInChildren<Text>();
                count.alignment = TextAnchor.MiddleCenter;
                count.rectTransform.offsetMin = new Vector2(4, 0);
                count.rectTransform.offsetMax = new Vector2(-4, 0);
                formalTroops[slot, i] = button;
                formalTroopCounts[slot, i] = count;
            }
        }

        private void RefreshFormalField(BattleSimulation battle)
        {
            bool shown = owner.FormalFieldHud && !owner.DrawerVisible;
            bool draft = owner.DeploymentDraftActive;
            formalBanner.gameObject.SetActive(shown);
            objectivePanelRect.gameObject.SetActive(!owner.FormalFieldHud && !owner.ContinuesThroughDialogue);
            formalSummaryToggle.gameObject.SetActive(shown);
            formalSummaryToggle.interactable = shown;
            formalSummary.gameObject.SetActive(shown && !formalSummaryFolded);
            formalGoal.gameObject.SetActive(shown && !formalSummaryFolded);
            formalStatus.gameObject.SetActive(shown && !draft);
            formalSupply.gameObject.SetActive(shown && !draft);
            for (int slot = 0; slot < formalTroopRows.Length; slot++)
            {
                bool exists = shown && slot < battle.Scenario.Army(BattleSide.A).HeroCount;
                var toggle = formalTroopToggles[slot];
                toggle.gameObject.SetActive(exists);
                toggle.interactable = exists;
                toggle.GetComponentInChildren<Text>(true).text = formalTroopsFolded[slot] ? "＋" : "−";
                formalTroopRows[slot].gameObject.SetActive(exists && !formalTroopsFolded[slot]);
                if (!exists || formalTroopsFolded[slot]) continue;
                for (int index = 0; index < BattleSimulation.TroopKinds.Length; index++)
                {
                    var kind = BattleSimulation.TroopKinds[index];
                    int count = draft ? owner.DraftRemaining(slot, kind) : battle.Soldiers.Count(u =>
                        u.Side == BattleSide.A && u.OriginCommanderSlot == slot && u.Kind == kind && u.IsActive);
                    var button = formalTroops[slot, index];
                    button.gameObject.SetActive(true);
                    formalTroopCounts[slot, index].text = $"{BattleSimulation.KindName(kind)}  {count}";
                    button.interactable = draft && owner.DraftCanChooseTroop(slot, kind);
                    SetHighlighted(button, draft && owner.DraftTeamSlot == slot && owner.DraftArmedKind == kind);
                }
            }
            if (!shown) return;

            formalBanner.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, formalSummaryFolded ? formalHeading.rect.height : formalExpandedHeight);
            formalMap.text = owner.FieldMapName;
            formalGoal.text = "目标  " + (draft ? owner.FieldDraftGoal : objectiveProgress.text);
            if (!draft) formalStatus.text = battle.Phase == BattlePhase.Finished ? "战斗结束" : owner.Automatic ? "战斗中" : "已暂停";
            formalSummaryToggle.GetComponentInChildren<Text>().text = formalSummaryFolded ? "＋" : "−";
            if (!formalSummaryFolded)
            {
                if (draft) (formalAlly.text, formalEnemy.text) = owner.FieldDraftSummary();
                else (formalAlly.text, formalEnemy.text, formalSupply.text) = FieldLiveSummary(battle);
            }
        }

        private (string Ally, string Enemy, string Supply) FieldLiveSummary(BattleSimulation battle)
        {
            var kinds = new[] { SoldierKind.Hero, SoldierKind.Melee, SoldierKind.Ranged, SoldierKind.Cavalry };
            var ally = kinds.Select(kind => battle.Vision.Status(BattleSide.A, BattleSide.A, kind).InField).ToArray();
            var enemy = kinds.Select(kind => battle.Vision.Status(BattleSide.A, BattleSide.B, kind)).ToArray();
            string hostile = enemy.Any(s => s.Exact || s.SeenTotal > 0)
                ? $"可见队长 {enemy[0].InField} · 剑 {enemy[1].InField} · 弓 {enemy[2].InField} · 骑 {enemy[3].InField}" : "暂无已知情报";
            var supply = owner.Realtime?.Session.Opening.SupplySnapshot?.Equipment.FirstOrDefault(e => e.Product == "supply" && e.Count > 0);
            string supplyText = supply == null ? "配给 0 件" : $"{supply.Level} 级 · 配给 {supply.Count} 件";
            return ($"我方在场  队长 {ally[0]} · 剑 {ally[1]} · 弓 {ally[2]} · 骑 {ally[3]}", "敌方  " + hostile, "军需  " + supplyText);
        }
    }
}

using System.Linq;
using BaseConquest.Presentation;
using UnityEngine;
using UnityEngine.UI;

namespace BaseConquest.Battle
{
    public sealed partial class BattleMobileHud
    {
        private RectTransform formalBanner, formalHeading, formalSummary;
        private Text formalMap, formalGoal, formalStatus, formalAlly, formalEnemy, formalSupply;
        private RectTransform formalPauseAnchor, formalSpeedAnchor;
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
            formalPauseAnchor = Rect("PauseAnchor", formalBanner, Vector2.one, new Vector2(-212, -9), new Vector2(70, 33));
            formalSpeedAnchor = Rect("SpeedAnchor", formalBanner, Vector2.one, new Vector2(-162, -9), new Vector2(42, 33));
            formalBanner.gameObject.SetActive(false);
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
            MoveTopControl(automatic, formalPauseAnchor);
            MoveTopControl(fast, formalSpeedAnchor);
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
            formalTroopToggles[slot] = Button("TroopToggle", module, Vector2.one, new Vector2(-2, -2), new Vector2(25, 24), () =>
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
            objectivePanelRect.gameObject.SetActive(!owner.FormalFieldHud);
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
            if (!draft)
            {
                realtimeGuard.interactable = owner.SelectedUnits.Count > 0;
                cancel.interactable &= owner.SelectedUnits.Count > 0;
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

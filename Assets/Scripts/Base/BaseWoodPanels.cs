using System;
using System.Runtime.CompilerServices;
using BaseConquest.Presentation;
using UnityEngine;
using UnityEngine.UI;

namespace BaseConquest.Base
{
    public sealed partial class BaseHud
    {
        private static readonly ConditionalWeakTable<RectTransform, object> InsetWoodPanels = new();
        private static readonly ConditionalWeakTable<RectTransform, object> InsetWoodViewports = new();
        private static readonly ConditionalWeakTable<RectTransform, object> InsetWoodPreparation = new();
        private static readonly ConditionalWeakTable<Text, object> RaisedWoodButtonLabels = new();
        private sealed class PaperHeight
        {
            internal float Size;
            internal float Y;
        }
        private static readonly ConditionalWeakTable<RectTransform, PaperHeight> TradePaperHeights = new();

        private static void AddWoodPanelInsets(RectTransform view)
        {
            if (InsetWoodPanels.TryGetValue(view, out _)) return;
            InsetWoodPanels.Add(view, new object());
            view.ForceUpdateRectTransforms();
            var scale = new Vector2(
                Mathf.Max(.84f, 1 - 46 / view.rect.width),
                Mathf.Max(.84f, 1 - 34 / view.rect.height));
            foreach (Transform child in view)
            {
                if (child is not RectTransform rect || rect.name == "FrameWidths") continue;
                var center = view.rect.center;
                var position = rect.localPosition;
                rect.localPosition = (Vector3)(center + Vector2.Scale((Vector2)position - center, scale))
                    + Vector3.forward * position.z;
                rect.localScale = Vector3.Scale(rect.localScale, new Vector3(scale.x, scale.y, 1));
            }
        }

        private static void ApplyWoodClose(RectTransform view, bool small = false)
        {
            var rect = view.Find("Close") as RectTransform;
            if (rect == null || !rect.TryGetComponent<Button>(out var button) ||
                button.targetGraphic is not Image image) return;

            rect.anchorMin = rect.anchorMax = Vector2.one;
            rect.pivot = Vector2.one;
            rect.localScale = Vector3.one;
            rect.anchoredPosition = small ? new Vector2(-12, -7) : new Vector2(-25, -18);
            rect.sizeDelta = small ? new Vector2(27, 26) : new Vector2(38, 37);
            foreach (var label in rect.GetComponentsInChildren<Text>(true))
                label.enabled = false;
            image.sprite = WoodenBarracksSprite("close");
            image.type = Image.Type.Simple;
            image.preserveAspect = true;
            image.color = Color.white;
            if (image.TryGetComponent<Outline>(out var outline)) outline.enabled = false;
            button.transition = Selectable.Transition.ColorTint;
            button.colors = new ColorBlock
            {
                normalColor = Color.white,
                highlightedColor = new Color32(255, 230, 193, 255),
                pressedColor = new Color32(186, 157, 125, 255),
                selectedColor = new Color32(255, 230, 193, 255),
                disabledColor = new Color32(150, 143, 134, 255),
                colorMultiplier = 1,
                fadeDuration = .08f
            };
        }

        internal static void PaintOtherWoodScreen(RectTransform view, bool recruitLayout = false)
        {
            AddWoodPanelInsets(view);
            PaintBarracksBoard(view.GetComponent<Image>(), WoodenBarracksSprite("board"), Image.Type.Simple);
            foreach (var path in new[] { "Header", "TitleRail" })
                if (view.Find(path)?.GetComponent<Image>() is { } rail) ClearBarracksPanel(rail);
            if (view.Find("Wallet")?.GetComponent<Text>() is { } wallet) wallet.color = BarracksIvory;

            switch (view.name)
            {
                case "CommonPanel":
                    ApplyWoodPaper(view.Find("Viewport"));
                    break;
                case "FacilityPanel":
                    if (view.Find("PortraitColumn").GetComponent<Image>().sprite == WoodenBarracksSprite("section")) break;
                    foreach (var path in new[] { "PortraitColumn", "Left", "Right" }) ApplyWoodPaper(view.Find(path));
                    foreach (var path in new[] { "Left/List/Viewport", "Right/List/Viewport" })
                        if (view.Find(path)?.GetComponent<Image>() is { } viewport) ClearBarracksPanel(viewport);
                    break;
                case "CampMenu":
                    ApplyWoodPaper(view.Find("Actions"));
                    break;
                case "PartyInventory":
                    foreach (Transform card in view.Find("Cards")) ShowBackpackPaper(card);
                    break;
                case "PartyWarehouse":
                    ApplyWoodPaper(view.Find("Grid"));
                    var cardHost = view.Find("Card");
                    if (cardHost.childCount > 0) ShowBackpackPaper(cardHost.GetChild(0));
                    break;
                case "TownTrade":
                    var offerBounds = LocalWoodRect(view, (RectTransform)view.Find("OfferList"));
                    MatchRecruitPaperHeight(view, (RectTransform)view.Find("Details"), offerBounds, recruitLayout);
                    MatchRecruitPaperHeight(view, (RectTransform)view.Find("RightBody"), offerBounds, recruitLayout);
                    foreach (var path in new[] { "OfferList", "Details", "RightBody" })
                    {
                        var region = view.Find(path);
                        ApplyWoodPaper(region);
                        if (region?.Find("Viewport")?.GetComponent<Image>() is { } oldPaper) ClearBarracksPanel(oldPaper);
                        InsetPaperViewport(region);
                    }
                    var sellerFrame = (RectTransform)view.Find("SellerFrame");
                    var seller = LocalWoodRect(view, sellerFrame);
                    var sellerName = LocalWoodRect(view, (RectTransform)view.Find("SellerName"));
                    var speech = LocalWoodRect(view, (RectTransform)view.Find("SellerSpeech"));
                    seller.yMin = recruitLayout ? offerBounds.yMin : speech.yMin - 8;
                    seller.yMax = recruitLayout ? offerBounds.yMax : sellerName.yMax + 7;
                    MatchRecruitPaperHeight(view, sellerFrame, seller, aligned: true);
                    ApplyWoodPaper(sellerFrame);
                    foreach (var card in view.Find("RightBody").GetComponentsInChildren<Image>())
                        if (card.name == "PartyMemberCard") ClearBarracksPanel(card);
                    break;
                case "BattlePreparation":
                    foreach (var path in new[] { "Ally", "Enemy", "Candidates" }) ApplyWoodPaper(view.Find(path));
                    var left = LocalWoodRect(view, (RectTransform)view.Find("Teams"));
                    var ally = LocalWoodRect(view, (RectTransform)view.Find("Ally"));
                    left.yMax = ally.yMin - 4;
                    left.yMin = view.rect.yMin + 18;
                    left.xMin -= 4;
                    left.xMax += 4;
                    PlacePaperBehind(view, "WoodFormationPaper", left);
                    if (!InsetWoodPreparation.TryGetValue(view, out _))
                    {
                        InsetWoodPreparation.Add(view, new object());
                        ((RectTransform)view.Find("Context")).anchoredPosition += Vector2.down * 10;
                        foreach (var path in new[] { "Supply", "Reset", "Continue" })
                            ((RectTransform)view.Find(path)).anchoredPosition += Vector2.up * 10;
                    }
                    if (view.Find("Teams").GetComponent<Image>() is { } teams) ClearBarracksPanel(teams);
                    ClearBarracksPanel(view.Find("Supply").GetComponent<Image>());
                    if (view.Find("Versus").GetComponent<Text>() is { } versus) versus.color = BarracksIvory;
                    break;
                case "AfterActionPanel":
                    ApplyWoodPaper(view.Find("AllySummary"));
                    ApplyWoodPaper(view.Find("EnemySummary"));
                    foreach (var path in new[] { "Outcome", "BattleMetrics", "SettlementNote" })
                        view.Find(path).GetComponent<Text>().color = BarracksIvory;
                    break;
                case "选项面板":
                    ApplyWoodPaper(view.Find("HelpContent"));
                    foreach (var path in new[] { "HelpHeading/Label", "ExitMessage/Label" })
                        if (view.Find(path)?.GetComponent<Text>() is { } caption) caption.color = BarracksIvory;
                    break;
                case "NpcDialogue":
                    var paper = view.Find("WoodDialoguePaper") as RectTransform;
                    if (paper == null)
                    {
                        paper = (RectTransform)new GameObject("WoodDialoguePaper", typeof(RectTransform)).transform;
                        paper.SetParent(view, false);
                        paper.SetAsFirstSibling();
                        paper.anchorMin = Vector2.zero;
                        paper.anchorMax = Vector2.one;
                        paper.offsetMin = new Vector2(26, 17);
                        paper.offsetMax = new Vector2(-26, -17);
                    }
                    ApplyWoodPaper(paper);
                    if (view.Find("PortraitFrame")?.GetComponent<Image>() is { } portrait) ClearBarracksPanel(portrait);
                    break;
            }

            var offers = view.name == "TownTrade" ? view.Find("OfferList") : null;
            var pressed = WoodenBarracksSprite("pressed");
            foreach (var button in view.GetComponentsInChildren<Button>(true))
            {
                if (button.transform == view || button.name == "Close" || button.targetGraphic is not Image image ||
                    button.GetComponent<PartyInventoryCell>() != null ||
                    (offers != null && button.transform.IsChildOf(offers))) continue;
                if (button.transform.Find("Status") != null && view.name == "TownTrade" ||
                    view.name == "BattlePreparation" && button.transform.parent.name == "Teams")
                {
                    ClearBarracksPanel(image);
                    continue;
                }
                if (button.transform.Find("Status") != null) continue;
                var text = button.GetComponentInChildren<Text>(true);
                if (text == null || string.IsNullOrWhiteSpace(text.text)) continue;
                bool selected = image.color == DemoUiTheme.Current.Gold ||
                    image.color == Color.white && image.sprite == pressed;
                PaintWoodButton(button, image, text, selected);
            }
            ApplyWoodClose(view);
        }

        private static void PaintWoodButton(Button button, Image image, Text label, bool selected)
        {
            var pressed = WoodenBarracksSprite("pressed");
            image.sprite = selected ? pressed : WoodenBarracksSprite("button");
            image.type = Image.Type.Sliced;
            image.pixelsPerUnitMultiplier = 3;
            image.color = Color.white;
            button.transition = Selectable.Transition.SpriteSwap;
            button.spriteState = new SpriteState { pressedSprite = pressed, disabledSprite = pressed };
            StyleWoodButtonLabel(label, selected);
            if (image.TryGetComponent<Outline>(out var outline)) outline.enabled = false;
        }

        private static void ShowBackpackPaper(Transform card)
        {
            ClearBarracksPanel(card.GetComponent<Image>());
            card.Find("BackpackPaper").gameObject.SetActive(true);
        }

        private static void StyleWoodButtonLabel(Text label, bool selected)
        {
            label.color = selected ? new Color32(255, 204, 117, 255) : BarracksIvory;
            if (RaisedWoodButtonLabels.TryGetValue(label, out _)) return;
            RaisedWoodButtonLabels.Add(label, new object());
            label.rectTransform.anchoredPosition += Vector2.up * 3f;
        }

        private static void InsetPaperViewport(Transform region)
        {
            var viewport = region?.Find("Viewport") as RectTransform;
            if (viewport == null || InsetWoodViewports.TryGetValue(viewport, out _)) return;
            InsetWoodViewports.Add(viewport, new object());
            viewport.offsetMin += new Vector2(9, 9);
            viewport.offsetMax -= new Vector2(9, 9);
            if (region.GetComponent<ScrollRect>()?.content.TryGetComponent<VerticalLayoutGroup>(out var rows) == true)
            {
                var padding = rows.padding;
                rows.padding = new RectOffset(padding.left + 7, padding.right + 7, padding.top + 4, padding.bottom + 4);
            }
        }

        private static void MatchRecruitPaperHeight(RectTransform view, RectTransform target, Rect bounds, bool aligned)
        {
            var original = TradePaperHeights.GetValue(target, rect =>
                new PaperHeight { Size = rect.rect.height, Y = rect.localPosition.y });
            float height = aligned ? bounds.height / target.localScale.y : original.Size;
            target.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);
            var position = target.localPosition;
            position.y = original.Y;
            target.localPosition = position;
            if (!aligned) return;
            var measured = LocalWoodRect(view, target);
            target.localPosition += Vector3.up * (bounds.center.y - measured.center.y);
        }

        private static Rect LocalWoodRect(RectTransform root, RectTransform node)
        {
            var corners = new Vector3[4];
            node.GetWorldCorners(corners);
            var min = root.InverseTransformPoint(corners[0]);
            var max = root.InverseTransformPoint(corners[2]);
            return UnityEngine.Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }

        private static void PlacePaperBehind(RectTransform view, string name, Rect bounds)
        {
            var paper = view.Find(name) as RectTransform;
            if (paper == null)
            {
                paper = (RectTransform)new GameObject(name, typeof(RectTransform)).transform;
                paper.SetParent(view, false);
                paper.SetAsFirstSibling();
            }
            paper.anchorMin = paper.anchorMax = view.pivot;
            paper.sizeDelta = bounds.size;
            paper.anchoredPosition = bounds.center;
            ApplyWoodPaper(paper);
        }

        private static void ApplyWoodPaper(Transform node)
        {
            if (node == null) return;
            if (!node.TryGetComponent<Image>(out var image))
            {
                image = node.gameObject.AddComponent<Image>();
                image.raycastTarget = false;
            }
            PaintBarracksBoard(image, WoodenBarracksSprite("section"), Image.Type.Sliced);
        }
    }
}

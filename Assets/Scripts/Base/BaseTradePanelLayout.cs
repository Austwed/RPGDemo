using System.Runtime.CompilerServices;
using UnityEngine;

namespace BaseConquest.Base
{
    public sealed partial class BaseHud
    {
        private sealed class TradePanelHeight
        {
            internal float Size;
            internal float Y;
        }

        private static readonly ConditionalWeakTable<RectTransform, TradePanelHeight> TradePanelHeights = new();

        private static void LayoutRecruitmentPanels(RectTransform view, bool recruiting)
        {
            var offers = LocalTradeRect(view, Ui<RectTransform>(view, "OfferList"));
            MatchRecruitPanelHeight(view, Ui<RectTransform>(view, "Details"), offers, recruiting);
            MatchRecruitPanelHeight(view, Ui<RectTransform>(view, "RightBody"), offers, recruiting);
            MatchRecruitPanelHeight(view, Ui<RectTransform>(view, "SellerFrame"), offers, recruiting);
        }

        private static void MatchRecruitPanelHeight(RectTransform view, RectTransform target, Rect bounds, bool aligned)
        {
            var original = TradePanelHeights.GetValue(target, rect =>
                new TradePanelHeight { Size = rect.rect.height, Y = rect.localPosition.y });
            float height = aligned ? bounds.height / target.localScale.y : original.Size;
            target.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);
            var position = target.localPosition;
            position.y = original.Y;
            target.localPosition = position;
            if (!aligned) return;
            var measured = LocalTradeRect(view, target);
            target.localPosition += Vector3.up * (bounds.center.y - measured.center.y);
        }

        private static Rect LocalTradeRect(RectTransform root, RectTransform node)
        {
            var corners = new Vector3[4];
            node.GetWorldCorners(corners);
            var min = root.InverseTransformPoint(corners[0]);
            var max = root.InverseTransformPoint(corners[2]);
            return UnityEngine.Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }
    }
}

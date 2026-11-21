using System;
using BaseConquest.Battle;
using BaseConquest.Presentation;
using UnityEngine;
using UnityEngine.UI;

namespace BaseConquest.Base
{
    public sealed partial class BaseHud
    {
        private Button[] BuildBarracksSeats(Transform column, BaseState state, BaseLeader[] captains, Action<string> select)
        {
            Ui<Text>(column, "Name").gameObject.SetActive(false);
            Ui<Text>(column, "Effect").gameObject.SetActive(false);
            column.Find("Portrait").gameObject.SetActive(false);
            var seats = new Button[BaseAdventure.Capacity];
            for (int index = 0; index < seats.Length; index++)
            {
                var captain = index < captains.Length ? captains[index] : null;
                var card = Rect("CaptainSeat" + index, column, Vector2.zero, Vector2.zero, Vector2.zero);
                card.anchorMin = new Vector2(0, 1 - (index + 1f) / seats.Length);
                card.anchorMax = new Vector2(1, 1 - index / (float)seats.Length);
                card.offsetMin = new Vector2(6, 4);
                card.offsetMax = new Vector2(-6, -4);
                var image = card.gameObject.AddComponent<Image>();
                image.color = Color.clear;
                var button = card.gameObject.AddComponent<Button>();
                button.targetGraphic = image;
                button.transition = Selectable.Transition.None;
                button.interactable = captain != null;
                if (captain != null) button.onClick.AddListener(() => { if (!owner.Busy) select(captain.Id); });
                seats[index] = button;

                var name = Label(card, captain == null ? "空位" : BasePersonnel.LeaderName(captain),
                    new Vector2(0, captain == null ? 0 : 22), new Vector2(138, 25), 18,
                    TextAnchor.MiddleCenter, new Vector2(.5f, captain == null ? .5f : 0));
                name.color = DemoUiTheme.Current.Ink;
                name.GetComponent<Shadow>().enabled = false;
                if (index < seats.Length - 1) AddBarracksDivider(card, 0, 9);
                if (captain == null) continue;

                bool player = captain.Id == state.PlayerLeaderId;
                var face = Rect("Avatar", card, new Vector2(.5f, .56f), Vector2.zero, new Vector2(90, 96));
                var avatar = face.gameObject.AddComponent<BattleAvatarGraphic>();
                avatar.PlayerHero = player;
                avatar.AppearanceId = player ? BaseDemoIntro.PlayerArt(state) : BaseCommanderIdentity.Appearance(captain);
                avatar.raycastTarget = false;
            }
            return seats;
        }

        private static void PaintBarracksSeats(Button[] seats, BaseLeader[] captains, string selected)
        {
            for (int i = 0; i < seats.Length; i++)
            {
                var label = seats[i].GetComponentInChildren<Text>();
                bool occupied = i < captains.Length;
                bool active = occupied && captains[i].Id == selected;
                label.text = occupied ? (active ? "◆ " : "") + BasePersonnel.LeaderName(captains[i]) : "空位";
                label.color = occupied ? active ? DemoUiTheme.Current.Copper : DemoUiTheme.Current.Ink : DemoUiTheme.Current.Muted;
                label.fontStyle = active ? FontStyle.Bold : FontStyle.Normal;
            }
        }

        private Action AddBarracksStepper(InputField field, int min, int max, bool compact, Action<int> setValue)
        {
            float width = compact ? 23 : 32;
            var text = field.textComponent;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = DemoUiTheme.Current.Ink;
            text.rectTransform.anchorMin = Vector2.zero;
            text.rectTransform.anchorMax = Vector2.one;
            text.rectTransform.offsetMin = new Vector2(width, 0);
            text.rectTransform.offsetMax = new Vector2(-width, 0);
            if (compact) text.fontSize = Mathf.Min(text.fontSize, 17);

            var less = Step("Minus", "−", 0);
            var more = Step("Plus", "+", 1);
            less.onClick.AddListener(() => Adjust(-1));
            more.onClick.AddListener(() => Adjust(1));
            AddBarracksDivider(field.transform, 3, width + 5);
            field.onValueChanged.AddListener(_ => Refresh());
            return Refresh;

            Button Step(string key, string title, int side)
            {
                var rect = CloneSceneUi("FacilityAction", field.transform);
                rect.name = key;
                rect.anchorMin = rect.anchorMax = new Vector2(side, .5f);
                rect.pivot = new Vector2(.5f, .5f);
                rect.sizeDelta = new Vector2(width, compact ? 29 : 34);
                rect.anchoredPosition = new Vector2((side == 0 ? 1 : -1) * width * .5f, 0);
                var button = rect.GetComponent<Button>();
                var label = button.GetComponentInChildren<Text>();
                label.text = title;
                label.fontSize = compact ? 16 : 19;
                return button;
            }

            void Adjust(int delta)
            {
                if (owner.Busy || !field.interactable || !int.TryParse(field.text, out int number)) return;
                setValue(Mathf.Clamp(number + delta, min, max));
                Refresh();
            }

            void Refresh()
            {
                bool valid = int.TryParse(field.text, out int value) && field.interactable;
                less.interactable = valid && value > min;
                more.interactable = valid && value < max;
            }
        }

        private static void AddBarracksDivider(Transform parent, float bottom, float inset)
        {
            if (parent.Find("BarracksDivider") != null) return;
            var line = new GameObject("BarracksDivider", typeof(RectTransform), typeof(Image));
            var rect = (RectTransform)line.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.right;
            rect.offsetMin = new Vector2(inset, bottom);
            rect.offsetMax = new Vector2(-inset, bottom + 2.5f);
            var image = line.GetComponent<Image>();
            image.color = DemoUiTheme.Current.Line;
            image.raycastTarget = false;
        }
    }
}

using System;
using System.Globalization;
using System.Linq;
using BaseConquest.Battle;
using BaseConquest.Presentation;
using BaseConquest.Story;
using UnityEngine;
using UnityEngine.UI;

namespace BaseConquest.Base
{
    public sealed partial class BaseHud
    {
        private sealed class NpcLine
        {
            public string Name,Appearance,Text;
            public bool Player;
            public NpcLine(string name,string appearance,string text,bool player=false){Name=name;Appearance=player?appearance:BaseDemoNpcs.DialogueArt(name,appearance);Text=text;Player=player;}
        }
        private sealed class NpcReply
        {
            public readonly string Text;
            public readonly Action Action;
            public readonly bool Enabled, Unlocked;
            public NpcReply(string text, Action action, bool enabled = true, bool unlocked = true)
            { Text = text; Action = action; Enabled = enabled; Unlocked = unlocked; }
        }
        private const float DialogueCharacterSeconds = .035f;
        private Action dialogueContinue;
        private Action<float> dialogueTick;
        private bool dialogueLocked;
        private Button dialogueQuestTarget;

        public bool DialogueOpen => PanelOpen && panelViewKey == "npc-dialogue";
        public bool DialogueExitLocked => DialogueOpen && dialogueLocked;

        public bool ContinueDialogue()
        {
            if (!DialogueOpen || owner.Busy || dialogueContinue == null) return false;
            dialogueContinue();
            return true;
        }

        internal void TickDialogue(float seconds)
        {
            if (DialogueOpen) dialogueTick?.Invoke(seconds);
            else dialogueTick = null;
        }

        private void NpcDialogue(NpcLine[] lines, NpcReply[] replies, bool lockExitUntilFinished = false, Action onCompleted = null,
            bool quietAdvance = false, bool guide = false)
        {
            var view = OpenScenePanel("NpcDialogue", "npc-dialogue");
            root.GetComponent<Canvas>().sortingOrder = 300;
            modal.GetComponent<Image>().color = new Color(.14f, .08f, .04f, .26f);
            interactionObject = "";
            var portrait = Ui<RectTransform>(view, "PortraitFrame");
            var avatar = Ui<RectTransform>(portrait, "SpeakerPortrait").gameObject.GetOrAdd<BattleAvatarGraphic>();
            avatar.raycastTarget = false;
            var name = Ui<Text>(view, "SpeakerName");
            var speech = Ui<Text>(view, "DialogueText");
            speech.supportRichText = false;
            var hint = Ui<Text>(view, "DialogueHint");
            hint.text = StoryText.Get("story.ui.dialogue.advance", "点击正文或按 E／空格继续");
            var advance = Ui<Button>(view, "AdvanceButton");
            dialogueQuestTarget = guide ? advance : null;
            var choices = Ui<ScrollRect>(view, "Replies");
            var template = Ui<Button>(choices.content, "ReplyTemplate");
            bool touch = Application.platform == RuntimePlatform.Android;
            if (touch)
            {
                SetDialogueRect(view, 0, 174, 1248, 324);
                SetDialogueRect(portrait, -540, 0, 134, 194);
                avatar.rectTransform.sizeDelta = new Vector2(116, 178);
                SetDialogueRect(name.rectTransform, -125, 128, 660, 44);
                name.fontSize = 29;
                SetDialogueRect(speech.rectTransform, -125, 14, 660, 184);
                speech.fontSize = 24;
                SetDialogueRect(hint.rectTransform, -171, -123, 550, 58);
                hint.fontSize = 19;
                hint.text = "点按正文或箭头继续";
                SetDialogueRect((RectTransform)advance.transform, 164, -121, 78, 60);
                advance.GetComponentInChildren<Text>(true).fontSize = 25;
                SetDialogueRect((RectTransform)choices.transform, 413, 0, 392, 300);
                var row = template.GetComponent<LayoutElement>();
                row.minHeight = row.preferredHeight = 68;
                template.GetComponentInChildren<Text>(true).fontSize = 23;
            }
            var surface = Ui<Button>(view, "");
            surface.onClick.AddListener(() => ContinueDialogue());
            advance.onClick.AddListener(() => ContinueDialogue());
            var visibleReplies = replies.Where(reply => reply.Unlocked).ToArray();
            var replyRows = new (Button Button, Text Label, NpcReply Reply)[visibleReplies.Length];
            for (int i = 0; i < visibleReplies.Length; i++)
            {
                var reply = visibleReplies[i];
                var button = UnityEngine.Object.Instantiate(template, choices.content, false);
                var label = button.GetComponentInChildren<Text>(true);
                label.text = "...";
                button.interactable = false;
                replyRows[i] = (button, label, reply);
                button.gameObject.SetActive(true);
                button.onClick.AddListener(() =>
                {
                    if (owner.Busy || panel != view || !choices.isActiveAndEnabled || !button.IsInteractable()) return;
                    var previousMessage = owner.Message;
                    reply.Action();
                    if (!DialogueOpen || panel != view || previousMessage == owner.Message || string.IsNullOrEmpty(owner.Message)) return;
                    hint.text = owner.Message;
                    hint.gameObject.SetActive(true);
                });
            }

            dialogueLocked = lockExitUntilFinished || quietAdvance;
            choices.gameObject.SetActive(replyRows.Length > 0);
            PresentLine(0);
            RefreshSummary();
            Canvas.ForceUpdateCanvases();
            if (replyRows.Length > 0) choices.verticalNormalizedPosition = 1;

            void PresentLine(int index)
            {
                var line = lines[index];
                avatar.PlayerHero = line.Player;
                avatar.AppearanceId = line.Player ? BaseDemoIntro.PlayerArt(owner.State) : line.Appearance;
                portrait.gameObject.SetActive(line.Player || !string.IsNullOrEmpty(line.Appearance));
                name.text = line.Name;
                var address = BaseDemoIntro.Address(owner.State);
                var fullText = line.Text.Replace("{称呼}", address)
                    .Replace("{搭档代词}", BaseDemoIntro.CaptainArt(owner.State) == "female" ? "她" : "他")
                    .Replace("{Player}", address).Replace("{Partner}", BaseDemoIntro.PartnerName(owner.State));
                var characters = StringInfo.ParseCombiningCharacters(fullText);
                var last = index == lines.Length - 1;
                var shown = 0;
                var elapsed = 0f;
                speech.text = "";
                advance.gameObject.SetActive(!quietAdvance);
                hint.gameObject.SetActive(!quietAdvance);
                dialogueContinue = Advance;
                dialogueTick = Reveal;
                if (characters.Length == 0) FinishLine();

                void Advance()
                {
                    if (shown < characters.Length) FinishLine();
                    else if (!last) PresentLine(index + 1);
                    else if (onCompleted != null)
                    {
                        dialogueContinue = null;
                        dialogueLocked = false;
                        onCompleted();
                    }
                }

                void Reveal(float seconds)
                {
                    elapsed += seconds;
                    var count = Mathf.Min(characters.Length, Mathf.FloorToInt(elapsed / DialogueCharacterSeconds));
                    if (count == shown) return;
                    if (count == characters.Length) FinishLine();
                    else
                    {
                        shown = count;
                        speech.text = fullText.Substring(0, characters[shown]);
                    }
                }

                void FinishLine()
                {
                    shown = characters.Length;
                    speech.text = fullText;
                    dialogueTick = null;
                    if (!last) return;
                    dialogueLocked = quietAdvance || guide && lockExitUntilFinished && onCompleted != null;
                    foreach (var row in replyRows)
                    {
                        row.Label.text = row.Reply.Text;
                        row.Button.interactable = row.Reply.Enabled;
                    }
                    if (onCompleted == null)
                    {
                        dialogueContinue = null;
                        advance.gameObject.SetActive(false);
                        hint.gameObject.SetActive(false);
                        if (guide) dialogueQuestTarget = replyRows.FirstOrDefault(row => row.Reply.Enabled).Button;
                    }
                }
            }
        }

        private static void SetDialogueRect(RectTransform rect, float x, float y, float width, float height)
        {
            rect.anchoredPosition = new Vector2(x, y);
            rect.sizeDelta = new Vector2(width, height);
        }
    }
}

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
            public readonly string Intent;
            public NpcLine(string name,string appearance,string text,bool player=false,string intent=null){Intent=intent;Name=name;Appearance=player?appearance:BaseDemoNpcs.DialogueArt(name,appearance);Text=text;Player=player;}
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
        private Action skipNarration;
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

        private void SkipToChoice()
        {
            var hud = this;
            while (hud != null && hud.DialogueOpen && !hud.owner.Busy && hud.skipNarration != null)
            {
                var finish = hud.skipNarration;
                hud.skipNarration = null;
                finish();
                hud = owner != null ? owner.Hud : null;
            }
        }

        private void InvokeDialogueAction(Action action)
        {
            var current = panel;
            string previousMessage = owner.Message;
            action();
            if (owner == null || owner.Hud != this || !DialogueOpen || panel != current ||
                previousMessage == owner.Message || string.IsNullOrEmpty(owner.Message)) return;
            var hint = Ui<Text>(current, "DialogueHint");
            hint.text = owner.Message;
            hint.gameObject.SetActive(true);
        }

        private void NpcDialogue(NpcLine[] lines, NpcReply[] replies, bool lockExitUntilFinished = false, Action onCompleted = null,
            bool quietAdvance = false, bool guide = false, bool blackout = false)
        {
            var view = OpenScenePanel("NpcDialogue", "npc-dialogue");
            root.GetComponent<Canvas>().sortingOrder = 300;
            modal.GetComponent<Image>().color = blackout ? Color.black : new Color(.14f, .08f, .04f, .26f);
            interactionObject = "";
            var portrait = Ui<RectTransform>(view, "PortraitFrame");
            var avatar = Ui<RectTransform>(portrait, "SpeakerPortrait").gameObject.GetOrAdd<BattleAvatarGraphic>();
            var unknownPortrait = Label(portrait,"？",Vector2.zero,new Vector2(100,100),52,TextAnchor.MiddleCenter);
            unknownPortrait.raycastTarget=false;
            unknownPortrait.gameObject.SetActive(false);
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
            // A press started on Skip cannot land on a reply that appears before release.
            var skip = UnityEngine.Object.Instantiate(template, choices.content, false);
            skip.name = "SkipDialogue";
            skip.GetComponentInChildren<Text>(true).text = "跳过对话";
            skip.interactable = true;
            skip.onClick.AddListener(() =>
            {
                if (DialogueOpen && panel == view && !owner.Busy && skip.isActiveAndEnabled) SkipToChoice();
            });
            var rows = new Button[Math.Max(1, visibleReplies.Length)];
            var labels = new Text[rows.Length];
            float minRowHeight = template.GetComponent<LayoutElement>().minHeight;
            for (int i = 0; i < rows.Length; i++)
            {
                rows[i] = UnityEngine.Object.Instantiate(template, choices.content, false);
                labels[i] = rows[i].GetComponentInChildren<Text>(true);
            }
            ShowSegment(0);
            PaintOtherWoodScreen(view);
            RefreshSummary();
            Canvas.ForceUpdateCanvases();

            string Resolve(string source)
            {
                string address = BaseDemoIntro.Address(owner.State);
                return source.Replace("{称呼}", address).Replace("{Player}", address)
                    .Replace("{搭档代词}", BaseDemoIntro.CaptainArt(owner.State) == "female" ? "她" : "他")
                    .Replace("{Partner}", BaseDemoIntro.PartnerName(owner.State));
            }

            void Bind(int index, string text, Action click, bool enabled = true)
            {
                var button = rows[index];
                var label = labels[index];
                label.text = text;
                label.horizontalOverflow = HorizontalWrapMode.Wrap;
                label.supportRichText = false;
                button.GetComponent<LayoutElement>().preferredHeight = Mathf.Max(minRowHeight, label.preferredHeight + 8);
                button.name = "Reply";
                button.onClick.RemoveAllListeners();
                button.interactable = enabled;
                if (click != null) button.onClick.AddListener(() =>
                {
                    if (!owner.Busy && DialogueOpen && panel == view && button.isActiveAndEnabled && button.IsInteractable())
                        InvokeDialogueAction(click);
                });
            }

            void ShowRows(int count, bool reading = false)
            {
                choices.gameObject.SetActive(count > 0);
                skip.gameObject.SetActive(reading);
                for (int i = 0; i < rows.Length; i++)
                    rows[i].gameObject.SetActive(i < count && (!reading || i > 0));
                if (count > 0) choices.verticalNormalizedPosition = 1;
            }

            void AwaitChoice(int next)
            {
                dialogueContinue = null;
                skipNarration = null;
                dialogueTick = null;
                advance.gameObject.SetActive(false);
                hint.gameObject.SetActive(false);
                if (next < lines.Length)
                {
                    var line = lines[next];
                    Bind(0, Resolve(line.Intent ?? line.Text), () => ShowSegment(line.Intent == null ? next + 1 : next, line.Intent != null));
                    rows[0].name = "PlayerSpeechChoice";
                    ShowRows(1);
                    if (guide) dialogueQuestTarget = rows[0];
                    return;
                }
                dialogueLocked = false;
                ShowRows(visibleReplies.Length);
                for (int i = 0; i < visibleReplies.Length; i++)
                {
                    var reply = visibleReplies[i];
                    Bind(i, Resolve(reply.Text), reply.Action, reply.Enabled);
                }
                if (guide)
                {
                    int first = Array.FindIndex(visibleReplies, reply => reply.Enabled);
                    dialogueQuestTarget = first >= 0 ? rows[first] : null;
                }
                if (visibleReplies.Length == 0) onCompleted?.Invoke();
            }

            // Each run of narrated pages ends at the next player line or authored reply.
            void ShowSegment(int first, bool playerChoseVoice = false)
            {
                dialogueLocked = lockExitUntilFinished || quietAdvance;
                if (first == lines.Length || (lines[first].Player && !playerChoseVoice))
                {
                    if (first == 0)
                    {
                        portrait.gameObject.SetActive(false);
                        name.text = speech.text = "";
                    }
                    AwaitChoice(first);
                    return;
                }
                int end = first + 1;
                while (end < lines.Length && !lines[end].Player) end++;
                bool endsInChoice = end < lines.Length || visibleReplies.Length > 0;
                ShowRows(end < lines.Length ? 1 : rows.Length, reading: true);
                skipNarration = () =>
                {
                    if (endsInChoice) PresentLine(end - 1, end, true);
                    else AwaitChoice(end);
                };
                for (int i = 1; i < rows.Length; i++) Bind(i, "...", null, false);
                dialogueQuestTarget = guide ? advance : null;
                PresentLine(first, end);
            }

            void PresentLine(int index, int end, bool immediate = false)
            {
                var line = lines[index];
                bool unknown = line.Appearance=="unknown-speaker";
                unknownPortrait.gameObject.SetActive(unknown);
                avatar.enabled = !unknown;
                avatar.PlayerHero = line.Player;
                avatar.AppearanceId = line.Player ? BaseDemoIntro.PlayerArt(owner.State) : unknown ? "" : line.Appearance;
                portrait.gameObject.SetActive(line.Player || !string.IsNullOrEmpty(line.Appearance));
                name.text = line.Name;
                var fullText = Resolve(line.Text);
                var characters = StringInfo.ParseCombiningCharacters(fullText);
                var last = index == end - 1;
                var shown = 0;
                var elapsed = 0f;
                speech.text = "";
                advance.gameObject.SetActive(!quietAdvance);
                hint.gameObject.SetActive(!quietAdvance);
                dialogueContinue = Advance;
                dialogueTick = Reveal;
                if (immediate || characters.Length == 0) FinishLine();

                void Advance()
                {
                    if (shown < characters.Length) FinishLine();
                    else if (!last)
                    {
                        owner.OpeningAudio?.Play(VillageOpeningAudio.Cue.Page);
                        PresentLine(index + 1, end);
                    }
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
                    if (end < lines.Length || visibleReplies.Length > 0)
                        AwaitChoice(end);
                    else
                        ShowRows(0);
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

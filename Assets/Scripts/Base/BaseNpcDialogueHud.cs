using System;
using System.Globalization;
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
            public string Text;public Action Action;public bool Enabled;
            public NpcReply(string text,Action action,bool enabled=true){Text=text;Action=action;Enabled=enabled;}
        }
        private const float DialogueCharacterSeconds = .035f;
        private Action dialogueContinue;
        private Action<float> dialogueTick;
        private bool dialogueLocked;

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

        private void NpcDialogue(NpcLine[] lines, NpcReply[] replies, bool lockExitUntilFinished = false)
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
            var choices = Ui<ScrollRect>(view, "Replies");
            var template = Ui<Button>(choices.content, "ReplyTemplate");
            var surface = Ui<Button>(view, "");
            surface.onClick.AddListener(() => ContinueDialogue());
            advance.onClick.AddListener(() => ContinueDialogue());
            foreach (var reply in replies)
            {
                var button = UnityEngine.Object.Instantiate(template, choices.content, false);
                button.GetComponentInChildren<Text>(true).text = reply.Text;
                button.interactable = reply.Enabled;
                button.gameObject.SetActive(true);
                button.onClick.AddListener(() =>
                {
                    if (owner.Busy || panel != view || !choices.isActiveAndEnabled) return;
                    var previousMessage = owner.Message;
                    reply.Action();
                    if (!DialogueOpen || panel != view || previousMessage == owner.Message || string.IsNullOrEmpty(owner.Message)) return;
                    hint.text = owner.Message;
                    hint.gameObject.SetActive(true);
                });
            }

            dialogueLocked = lockExitUntilFinished;
            PresentLine(0);
            RefreshSummary();
            Canvas.ForceUpdateCanvases();

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
                choices.gameObject.SetActive(false);
                advance.gameObject.SetActive(true);
                hint.gameObject.SetActive(true);
                dialogueContinue = Advance;
                dialogueTick = Reveal;
                if (characters.Length == 0) FinishLine();

                void Advance()
                {
                    if (shown < characters.Length) FinishLine();
                    else if (!last) PresentLine(index + 1);
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
                    dialogueContinue = null;
                    dialogueLocked = false;
                    advance.gameObject.SetActive(false);
                    hint.gameObject.SetActive(false);
                    choices.gameObject.SetActive(true);
                    choices.verticalNormalizedPosition = 1;
                }
            }
        }
    }
}

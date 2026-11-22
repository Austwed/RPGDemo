using System;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BaseConquest.Base
{
    public sealed partial class BaseHud
    {
        private RectTransform villageTitle;
        private GameObject titleActions;
        private string recentTitleSlot;

        private void OpenVillageTitle()
        {
            ResetScenePanel("village-title");
            if (villageTitle == null)
            {
                villageTitle = CloneSceneUi("VillageTitle", modal);
                titleActions = villageTitle.Find("Actions").gameObject;
                if (owner.VillageTitlePreview)
                    villageTitle.Find("Village").gameObject.SetActive(false);
                villageTitle.GetComponent<VillageTitleLoop>().BindBack(() =>
                {
                    if (BaseBattleGateway.MainMenu && !titleActions.activeSelf) OpenVillageTitle();
                });
                // Read only the slot summaries. A new campaign and an abandoned session both reach this page.
                recentTitleSlot = Application.isPlaying ? BaseBattleGateway.Catalog(false).Entries()
                    .Where(e => e.Error == null && !e.GameOver && !e.InBattle)
                    .OrderByDescending(e => e.SavedUtc, StringComparer.Ordinal).FirstOrDefault()?.Id : null;
                Bind("Continue", () => owner.LoadSlot(recentTitleSlot), recentTitleSlot != null);
                Bind("New", owner.StartVillageTitleGame);
                Bind("Load", () => OpenSlots(false));
                Bind("Options", OpenOptions);
                Bind("Quit", () => BaseBattleGateway.ExitDemoSession(true));
                if (Application.isPlaying) VillageOpeningAudio.PlayTitle();
            }
            if (owner.VillageTitlePreview) modal.GetComponent<Image>().color = Color.clear;
            titleActions.SetActive(true);
            var focus = titleActions.transform.Find(recentTitleSlot == null ? "New" : "Continue").gameObject;
            EventSystem.current?.SetSelectedGameObject(focus);

            void Bind(string key, Action action, bool enabled = true)
            {
                var button = Ui<Button>(titleActions.transform, key);
                button.interactable = enabled;
                var colors = button.colors;
                colors.highlightedColor = colors.selectedColor = Color.white;
                colors.normalColor = new Color(.95f, .94f, .92f, .88f);
                colors.disabledColor = new Color(.67f, .64f, .57f, .69f);
                button.colors = colors;
                if (!enabled) button.GetComponentInChildren<Text>(true).color = new Color(.93f, .87f, .74f);
                button.gameObject.AddComponent<VillageTitleFocus>().Bind(button);
                button.onClick.AddListener(() => { if (!owner.Busy) action(); });
            }
        }
    }
}

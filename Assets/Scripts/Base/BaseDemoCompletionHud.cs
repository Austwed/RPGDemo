using System.Collections;
using BaseConquest.Presentation;
using UnityEngine;

namespace BaseConquest.Base
{
    public sealed partial class BaseHud
    {
        private const string DemoCompletionView = "demo-completion";
        internal bool DemoCompletionOpen => PanelOpen && panelViewKey == DemoCompletionView;

        private void OpenDemoCompletion()
        {
            if(!owner.State.Demo.Finale.EndingSeen && !owner.Change(next=>next.Demo.Finale.EndingSeen=true,""))return;
            ResetScenePanel(DemoCompletionView);
            var shade = modal.GetComponent<UnityEngine.UI.Image>();
            shade.color = new Color(.13f, .08f, .04f, .26f);
            var view = panel = StretchLayer("DemoCompletion", modal);
            var reveal = view.gameObject.AddComponent<CanvasGroup>();
            var title = Label(view, "试玩篇完", new Vector2(0, 52), new Vector2(560, 88), 54, TextAnchor.MiddleCenter);
            title.color = DemoUiTheme.Current.Panel;
            title.gameObject.AddComponent<UnityEngine.UI.Shadow>().effectColor = new Color(.15f, .08f, .03f, .9f);

            MakeButton(view, "继续探索", new Vector2(-108, -50), new Vector2(192, 54), ClosePanel);
            MakeButton(view, "返回主菜单", new Vector2(108, -50), new Vector2(192, 54), owner.ToMainMenu);
            RefreshSummary();
            if (Application.isPlaying) owner.StartCoroutine(RevealDemoCompletion(reveal));
        }

        private IEnumerator RevealDemoCompletion(CanvasGroup group)
        {
            group.alpha = 0;
            group.interactable = false;
            float elapsed = 0;
            const float hold = .45f;
            const float fade = .65f;
            while (group != null && PanelOpen && panelViewKey == DemoCompletionView && elapsed < hold + fade)
            {
                elapsed += Time.unscaledDeltaTime;
                group.alpha = Mathf.Clamp01((elapsed - hold) / fade);
                yield return null;
            }
            if (group != null)
            {
                group.alpha = 1;
                group.interactable = true;
            }
        }
    }
}

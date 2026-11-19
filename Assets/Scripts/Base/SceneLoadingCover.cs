using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace BaseConquest.Base
{
    public sealed class SceneLoadingCover : MonoBehaviour
    {
        private static SceneLoadingCover instance;
        private CanvasGroup group;
        private bool covered;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Boot()
        {
            if (instance != null) return;
            var go = new GameObject("SceneLoadingCover");
            DontDestroyOnLoad(go);
            instance = go.AddComponent<SceneLoadingCover>();
            SceneManager.sceneUnloaded += instance.OnUnloaded;
            SceneManager.sceneLoaded += instance.OnLoaded;
        }

        internal static void LoadExploration()
        {
            // LoadScene finishes on a later frame. Cover the outgoing renderer before that frame.
            if (instance != null) instance.Immediate();
            SceneManager.LoadScene(BaseBattleGateway.ExplorationScene);
        }

        private void OnUnloaded(Scene scene) => Immediate();
        private void OnLoaded(Scene scene, LoadSceneMode mode)
        {
            if (!covered || mode != LoadSceneMode.Single) return;
            var destination = FindAnyObjectByType<BaseSceneController>();
            StartCoroutine(FadeRoutine(destination));
        }

        private void OnDestroy()
        {
            SceneManager.sceneUnloaded -= OnUnloaded;
            SceneManager.sceneLoaded -= OnLoaded;
            if (instance == this) instance = null;
        }

        private void Awake()
        {
            var canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 32767; // 盖过所有 HUD（BaseHud 面板 sortingOrder 300）。
            gameObject.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            var image = new GameObject("Cover", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
            image.rectTransform.SetParent(transform, false);
            image.rectTransform.anchorMin = Vector2.zero; image.rectTransform.anchorMax = Vector2.one;
            image.rectTransform.offsetMin = Vector2.zero; image.rectTransform.offsetMax = Vector2.zero;
            image.color = new Color(0.02f, 0.03f, 0.04f, 1f); // 与场景背景 (0.045,0.065,0.095) 同调的近黑色。
            image.raycastTarget = false;
            group = gameObject.AddComponent<CanvasGroup>();
            group.alpha = 0; group.blocksRaycasts = false; group.interactable = false;
            gameObject.SetActive(false);
        }

        private void Immediate()
        {
            gameObject.SetActive(true);
            StopAllCoroutines();
            group.alpha = 1;
            covered = true;
        }

        private IEnumerator FadeRoutine(BaseSceneController destination)
        {
            do { yield return null; }
            while (destination != null && !destination.RegionPresented);
            covered = false;
            float t = 0;
            while (t < 0.22f)
            {
                t += Time.unscaledDeltaTime;
                group.alpha = 1f - Mathf.Clamp01(t / 0.22f);
                yield return null;
            }
            group.alpha = 0;
            gameObject.SetActive(false);
        }
    }
}

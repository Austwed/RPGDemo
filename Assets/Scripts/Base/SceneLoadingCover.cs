using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace BaseConquest.Base
{
    // 统一场景加载黑屏：任何 SceneManager.LoadScene 自动被不透明黑屏覆盖，加载完成后淡出。
    // 解决「切换场景瞬间闪现原场景同坐标区域」的渲染时序问题（旧 regionView 帧末才销毁、新场景第一帧未就绪）。
    // 做法：DontDestroyOnLoad 挂一张全屏黑图，sceneUnloaded 时置不透明、sceneLoaded 后延迟一帧淡出；
    // 不依赖任何调用点改动，也不依赖 Canvas 构造时序（Image 用纯色即可，无需字体/贴图）。
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
            SceneManager.sceneUnloaded += _ => instance.Immediate();
            SceneManager.sceneLoaded += (_, __) => instance.FadeOut();
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
            group.alpha = 1;
            covered = true;
        }

        private void FadeOut()
        {
            // 首次启动没有 sceneUnloaded，不需要从黑淡出；只有真正发生过场景切换才淡出，避免启动闪黑。
            if (!covered) return;
            covered = false;
            gameObject.SetActive(true);
            StopAllCoroutines();
            StartCoroutine(FadeRoutine());
        }

        private IEnumerator FadeRoutine()
        {
            group.alpha = 1;
            // 等新场景第一帧就绪（BeginSession → BuildWorld 同步完成后）再淡出，避免黑屏过早消失露出中间帧。
            yield return null;
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

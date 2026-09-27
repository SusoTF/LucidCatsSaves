using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace LucidCatsSaves
{
    /// <summary>
    /// A small "Saving... / Game saved" message in the bottom-right corner, shown on the host's screen
    /// every time the game is saved. It uses the game's own font so it fits in.
    /// </summary>
    internal class SaveIndicator : MonoBehaviour
    {
        private static SaveIndicator instance;

        private CanvasGroup group;
        private TextMeshProUGUI label;
        private bool fontReady;
        private Coroutine running;

        public static void Create()
        {
            if (instance != null)
                return;

            var go = new GameObject("Save Indicator (mod)");
            DontDestroyOnLoad(go);
            instance = go.AddComponent<SaveIndicator>();
            instance.Build();
        }

        public static void Show()
        {
            if (instance == null || !SavesPlugin.ShowSaveIndicator.Value)
                return;
            instance.Play();
        }

        private void Build()
        {
            Canvas canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 32000;

            CanvasScaler scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            group = gameObject.AddComponent<CanvasGroup>();
            group.alpha = 0f;
            group.interactable = false;
            group.blocksRaycasts = false;

            var textObject = new GameObject("Label", typeof(RectTransform));
            textObject.transform.SetParent(transform, false);
            var rect = (RectTransform)textObject.transform;
            rect.anchorMin = new Vector2(1f, 0f);
            rect.anchorMax = new Vector2(1f, 0f);
            rect.pivot = new Vector2(1f, 0f);
            rect.anchoredPosition = new Vector2(-40f, 32f);
            rect.sizeDelta = new Vector2(600f, 60f);

            label = textObject.AddComponent<TextMeshProUGUI>();
            label.alignment = TextAlignmentOptions.Right;
            label.fontSize = 30f;
            label.raycastTarget = false;
            label.text = string.Empty;
        }

        /// <summary>Borrows the font from any text of the game's interface.</summary>
        private void EnsureFont()
        {
            if (fontReady)
                return;

            foreach (TMP_Text text in Object.FindObjectsByType<TMP_Text>(FindObjectsSortMode.None))
            {
                if (text == null || text == label || text.font == null)
                    continue;
                label.font = text.font;
                label.fontSharedMaterial = text.fontSharedMaterial;
                fontReady = true;
                return;
            }
        }

        private void Play()
        {
            EnsureFont();
            if (running != null)
                StopCoroutine(running);
            running = StartCoroutine(Animate());
        }

        private IEnumerator Animate()
        {
            label.text = "Saving...";
            label.color = new Color(1f, 1f, 1f, 0.85f);
            yield return Fade(group.alpha, 1f, 0.15f);

            yield return new WaitForSecondsRealtime(0.5f);

            label.text = "Game saved";
            label.color = new Color(1f, 0.85f, 0.25f, 0.95f);
            yield return new WaitForSecondsRealtime(1.3f);

            yield return Fade(1f, 0f, 0.6f);
            running = null;
        }

        private IEnumerator Fade(float from, float to, float seconds)
        {
            float t = 0f;
            while (t < seconds)
            {
                t += Time.unscaledDeltaTime;
                group.alpha = Mathf.Lerp(from, to, t / seconds);
                yield return null;
            }
            group.alpha = to;
        }
    }
}

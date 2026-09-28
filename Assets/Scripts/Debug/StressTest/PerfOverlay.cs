using TMPro;
using UltimateXR.Avatar;
using UnityEngine;

namespace VrBattlegrounds.DevTools.StressTest
{
    /// <summary>
    /// Табличка с цифрами перед глазами игрока — в шлеме вне студии другого экрана нет.
    /// Висит на камере локального аватара, создаётся по первому обращению.
    /// </summary>
    public sealed class PerfOverlay : MonoBehaviour
    {
        private static PerfOverlay _instance;

        private TextMeshPro _text;
        private float _hideAt = float.PositiveInfinity;

        /// <summary>Показывает текст; <paramref name="seconds"/> ≤ 0 — пока не заменят.</summary>
        public static void Show(string text, float seconds = 0f)
        {
            PerfOverlay overlay = GetOrCreate();
            if (overlay == null) return;

            overlay.gameObject.SetActive(true);
            overlay._text.text = text;
            overlay._hideAt = seconds > 0f ? Time.unscaledTime + seconds : float.PositiveInfinity;
        }

        public static void Hide()
        {
            if (_instance != null) _instance.gameObject.SetActive(false);
        }

        private static PerfOverlay GetOrCreate()
        {
            if (_instance != null) return _instance;

            // Пока шлем ждёт сервер, аватара нет — тогда на главную камеру сцены.
            Transform camera = UxrAvatar.LocalAvatar != null ? UxrAvatar.LocalAvatar.CameraTransform : null;
            if (camera == null && Camera.main != null) camera = Camera.main.transform;
            if (camera == null) return null;

            var go = new GameObject("PerfOverlay");
            go.transform.SetParent(camera, false);
            go.transform.localPosition = new Vector3(0f, -0.18f, 0.7f);
            go.transform.localRotation = Quaternion.identity;

            var text = go.AddComponent<TextMeshPro>();
            text.fontSize = 0.22f;
            text.alignment = TextAlignmentOptions.TopLeft;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.color = new Color(1f, 1f, 0.85f);
            text.rectTransform.sizeDelta = new Vector2(0.6f, 0.35f);

            _instance = go.AddComponent<PerfOverlay>();
            _instance._text = text;
            return _instance;
        }

        private void Update()
        {
            if (Time.unscaledTime >= _hideAt) gameObject.SetActive(false);
        }

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }
    }
}

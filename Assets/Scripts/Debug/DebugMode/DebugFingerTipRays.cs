using System.Collections.Generic;
using UltimateXR.Avatar;
using UltimateXR.UI;
using UltimateXR.UI.UnityInputModule;
using UnityEngine;
using UnityEngine.EventSystems;
using VrBattlegrounds.Core;

namespace VrBattlegrounds.DevTools
{
    /// <summary>
    /// Переключатель штатной визуализации UxrFingerTip текущего локального аватара.
    /// Управляет SessionState, диагностическими подложками и игровыми логами касаний.
    /// Отрисовка и ограничение луча поверхностью принадлежат патчу UltimateXR.
    /// </summary>
    [DefaultExecutionOrder(10000)]
    public sealed class DebugFingerTipRays : MonoBehaviour
    {
        public const string EditorSessionKey = "VrBattlegrounds.FingerTipRays.Visible";

        private static DebugFingerTipRays _instance;
        private readonly Dictionary<UxrFingerTip, bool> _tips = new Dictionary<UxrFingerTip, bool>();
        private readonly HashSet<UxrFingerTip> _seen = new HashSet<UxrFingerTip>();
        private readonly List<UxrFingerTip> _removed = new List<UxrFingerTip>();
        private readonly Dictionary<UxrCanvas, DebugFingerTipSurface> _surfaces = new Dictionary<UxrCanvas, DebugFingerTipSurface>();
        private readonly HashSet<UxrCanvas> _seenCanvases = new HashSet<UxrCanvas>();
        private readonly List<UxrCanvas> _removedCanvases = new List<UxrCanvas>();

        public static bool Visible { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetState()
        {
            _instance = null;
            Visible = false;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void RestoreEditorSetting()
        {
#if UNITY_EDITOR
            if (UnityEditor.SessionState.GetBool(EditorSessionKey, false)) SetVisible(true);
#endif
        }

        public static void SetVisible(bool visible)
        {
#if UNITY_EDITOR
            UnityEditor.SessionState.SetBool(EditorSessionKey, visible);
#else
            if (visible && !DebugMode.Enabled) return;
#endif
            if (!Application.isPlaying || Application.isBatchMode) return;
            if (Visible == visible) return;

            Visible = visible;
            if (visible)
            {
                if (_instance == null)
                {
                    var go = new GameObject("DebugFingerTipRays");
                    DontDestroyOnLoad(go);
                    _instance = go.AddComponent<DebugFingerTipRays>();
                }
            }
            else if (_instance != null) _instance.ClearViews();

            GameLog.Debug.Info($"[FingerTipRays] Лучи пальцев UI {(visible ? "включены" : "выключены")}.");
        }

        private void OnEnable() => DebugMode.Changed += HandleDebugModeChanged;

        private void OnDisable()
        {
            DebugMode.Changed -= HandleDebugModeChanged;
            ClearViews();
        }

        private void OnDestroy()
        {
            ClearViews();
            if (_instance != this) return;
            _instance = null;
            Visible = false;
        }

        private static void HandleDebugModeChanged(bool enabled)
        {
            if (!enabled) SetVisible(false);
        }

        private void LateUpdate()
        {
            if (!Visible) return;
            SyncSurfaces();
            _seen.Clear();

            UxrAvatar localAvatar = UxrAvatar.LocalAvatar;
            if (localAvatar != null)
            {
                foreach (UxrFingerTip tip in UxrFingerTip.EnabledComponentsInLocalAvatar)
                {
                    // Старый риг может ещё жить один кадр после смены скина.
                    if (tip.Avatar != localAvatar) continue;
                    _seen.Add(tip);
                    if (!_tips.ContainsKey(tip))
                    {
                        // Восстанавливаем исходную настройку, если другой потребитель
                        // SDK уже показывал визуализацию до включения нашего меню.
                        _tips.Add(tip, tip.RayVisualizationEnabled);
                        tip.RayVisualizationEnabled = true;
                    }
                    LogPointerAction(tip);
                }
            }

            _removed.Clear();
            foreach (KeyValuePair<UxrFingerTip, bool> pair in _tips)
                if (!_seen.Contains(pair.Key)) _removed.Add(pair.Key);
            foreach (UxrFingerTip tip in _removed)
            {
                if (tip != null) tip.RayVisualizationEnabled = _tips[tip];
                _tips.Remove(tip);
            }
        }

        private static void LogPointerAction(UxrFingerTip tip)
        {
            UxrPointerInputModule module = UxrPointerInputModule.Instance;
            EventSystem eventSystem = module != null ? module.GetComponent<EventSystem>() : null;
            if (module == null || !module.isActiveAndEnabled || eventSystem == null
                || !eventSystem.isActiveAndEnabled || eventSystem.currentInputModule != module
                || tip.Avatar == null || !tip.Avatar.RenderMode.HasFlag(UxrAvatarRenderModes.Avatar)) return;
            UxrPointerEventData data = module.GetPointerEventData(tip);
            if (data == null || (!data.PressedThisFrame && !data.ReleasedThisFrame)) return;

            bool allowed = tip.Avatar != null && tip.Avatar.AvatarController != null
                && tip.Avatar.ControllerInput != null && tip.Avatar.AvatarController.CanHandInteractWithUI(tip.Side);
            string action = data.PressedThisFrame ? "нажатие" : "отпускание";
            string target = data.pointerCurrentRaycast.gameObject != null
                ? data.pointerCurrentRaycast.gameObject.name : "нет";
            GameLog.UI.Info($"[FingerTipRays] {tip.Side}: {action}; UI={target}; допуск={allowed}; " +
                            $"за плоскостью={data.FingerTipPosIsInsideControl}; " +
                            $"клик разрешён={data.eligibleForClick}; drag={data.dragging}.", tip);
        }

        private void SyncSurfaces()
        {
            _seenCanvases.Clear();
            foreach (UxrCanvas canvas in UxrCanvas.EnabledComponents)
            {
                if (!IsCanvasAvailable(canvas)) continue;
                _seenCanvases.Add(canvas);
                if (!_surfaces.TryGetValue(canvas, out DebugFingerTipSurface surface) || surface == null)
                {
                    surface = DebugFingerTipSurface.Create(canvas);
                    _surfaces[canvas] = surface;
                }
                else surface.Refresh();
            }
            _removedCanvases.Clear();
            foreach (KeyValuePair<UxrCanvas, DebugFingerTipSurface> pair in _surfaces)
                if (!_seenCanvases.Contains(pair.Key)) _removedCanvases.Add(pair.Key);
            foreach (UxrCanvas canvas in _removedCanvases)
            {
                if (_surfaces[canvas] != null) _surfaces[canvas].Dispose();
                _surfaces.Remove(canvas);
            }
        }

        private void ClearViews()
        {
            foreach (KeyValuePair<UxrFingerTip, bool> pair in _tips)
                if (pair.Key != null) pair.Key.RayVisualizationEnabled = pair.Value;
            _tips.Clear();
            _seen.Clear();
            _removed.Clear();
            foreach (DebugFingerTipSurface surface in _surfaces.Values)
                if (surface != null) surface.Dispose();
            _surfaces.Clear();
            _seenCanvases.Clear();
            _removedCanvases.Clear();
        }

        private static bool IsCanvasAvailable(UxrCanvas canvas)
        {
            if (canvas == null || !canvas.isActiveAndEnabled || canvas.UnityCanvas == null
                || !canvas.UnityCanvas.isActiveAndEnabled
                || canvas.CanvasInteractionType != UxrInteractionType.FingerTips) return false;

            for (Transform current = canvas.transform; current != null; current = current.parent)
            {
                CanvasGroup group = current.GetComponent<CanvasGroup>();
                if (group == null) continue;
                if (!group.interactable || !group.blocksRaycasts || group.alpha <= 0f) return false;
                if (group.ignoreParentGroups) break;
            }
            return true;
        }
    }
}

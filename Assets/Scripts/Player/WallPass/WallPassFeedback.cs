using System.Collections.Generic;
using Mirror;
using TMPro;
using UltimateXR.Avatar;
using UltimateXR.Core;
using UltimateXR.Haptics;
using UltimateXR.Mechanics.Weapons;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using VrBattlegrounds.UI.Menu.Kit;

namespace VrBattlegrounds.Player.WallPass
{
    /// <summary>
    /// Локальная обратная связь единого серверного состояния стены. Не назначает штрафы.
    /// Рендереры скрываются только на время рендера своей камеры; чужие камеры и сетевое
    /// состояние не меняются. Отдельные эффекты SDK не содержат публичного владельца,
    /// поэтому во время нарушения скрываются трассеры/вспышки всех участников.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WallPassFeedback : MonoBehaviour
    {
        private readonly Dictionary<Renderer, bool> _renderFlags = new Dictionary<Renderer, bool>();
        private readonly Dictionary<Light, bool> _lightFlags = new Dictionary<Light, bool>();
        private readonly HashSet<string> _effectNames = new HashSet<string>();
        private readonly Dictionary<UxrAvatar, Renderer[]> _avatarRenderers = new Dictionary<UxrAvatar, Renderer[]>();
        private readonly Dictionary<UxrProjectileSource, Renderer[]> _weaponRenderers = new Dictionary<UxrProjectileSource, Renderer[]>();
        private bool _scanMeshEffects;
        private bool _scanEffectLights;
        private bool _overlayFailed;
        private bool _overlayDiagnosticReported;
        private PlayerSession _session;
        private PlayerController _player;
        private UxrAvatar _avatar;
        private Camera _camera;
        private GameObject _overlay;
        private TextMeshProUGUI _warning;
        private TextMeshProUGUI _countdown;
        private Canvas _canvas;
        private WallPassParticleFlow _flow;
        private WallPassCameraEffect _cameraEffect;
        private Renderer[] _ownRenderers = System.Array.Empty<Renderer>();
        private float _nextOwnScan;
        private float _severity;
        private bool _preview;
        private Material _fontMaterial;
        private WallPassStage _shownStage;
        private float _nextHaptic;
        private Scene _scene;

        private void Awake() => _session = GetComponent<PlayerSession>();

        private void OnEnable()
        {
            RenderPipelineManager.beginCameraRendering += BeginCamera;
            RenderPipelineManager.endCameraRendering += EndCamera;
            SceneManager.activeSceneChanged += SceneChanged;
            UxrAvatar.GlobalDisabled += AvatarDisabled;
            UxrProjectileSource.GlobalEnabled += SourceEnabled;
            UxrProjectileSource.GlobalDisabled += SourceDisabled;
        }

        private void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering -= BeginCamera;
            RenderPipelineManager.endCameraRendering -= EndCamera;
            SceneManager.activeSceneChanged -= SceneChanged;
            UxrAvatar.GlobalDisabled -= AvatarDisabled;
            UxrProjectileSource.GlobalEnabled -= SourceEnabled;
            UxrProjectileSource.GlobalDisabled -= SourceDisabled;
            ResetFeedback();
        }

        private void OnDestroy() => ResetFeedback();

        private void SceneChanged(Scene previous, Scene next) => ResetFeedback();

        private void LateUpdate()
        {
            if (!NetworkClient.active || PlayerSession.LocalSession != _session || _session == null)
            {
                ResetFeedback();
                return;
            }

            PlayerController player = _session.ActiveAvatar;
            UxrAvatar avatar = player != null ? player.GetComponent<UxrAvatar>() : null;
            Camera camera = avatar != null ? avatar.CameraComponent : null;
            if (_player != player || _camera != camera || (_camera != null && _scene != _camera.gameObject.scene))
            {
                ResetFeedback();
                _player = player;
                _avatar = avatar;
                _camera = camera;
                _scene = camera != null ? camera.gameObject.scene : default;
                // Сохраняем имена ещё до нарушения: летящий tracer переживает деспавн оружия.
                foreach (UxrProjectileSource source in UxrProjectileSource.AllComponents)
                    if (source != null && source.isActiveAndEnabled) RegisterSource(source);
            }

            if (_player == null || !_player.IsAlive || _session.IsEliminated ||
                _session.Role != VrBattlegrounds.Core.GameRole.Player || _camera == null || !_camera.isActiveAndEnabled ||
                _avatar.AvatarMode != UxrAvatarMode.Local)
            {
                ResetFeedback();
                return;
            }

            WallPassStatus status = _session.WallPassStatus;
            // Реальное нарушение/предупреждение всегда имеет приоритет над косметическим стендом.
            _preview = WallPassVisualRuntime.PreviewEnabled && status.Stage == WallPassStage.Clear;
            if (status.Stage == WallPassStage.Clear && !_preview)
            {
                ClearVisuals();
                return;
            }

            WallPassVisualSettings settings = WallPassVisualRuntime.Settings;
            bool violating = status.Stage == WallPassStage.Violating || _preview;
            float timeRisk = status.DeathAt > status.EnteredAt
                ? Mathf.Clamp01((float)((NetworkTime.time - status.EnteredAt) / (status.DeathAt - status.EnteredAt)))
                : violating ? Mathf.Clamp01((float)(NetworkTime.time - status.EnteredAt) / 3f) : 0f;
            float risk = _preview ? WallPassVisualRuntime.PreviewRisk : Mathf.Max(timeRisk, status.BarrierProgress);
            float target = violating ? settings.Evaluate(risk) : settings.Intensity * 0.12f;
            // Нарушение заметно сразу; последующие сетевые шаги сглаживаются локально.
            if (_shownStage == WallPassStage.Clear && violating) _severity = settings.Evaluate(0f);
            _severity = Mathf.MoveTowards(_severity, target, Time.unscaledDeltaTime * 4f);
            Vector3 direction = status.ReturnPoint - _camera.transform.position;
            if (_preview)
            {
                Vector3 forward = Vector3.ProjectOnPlane(_camera.transform.forward, Vector3.up);
                if (forward.sqrMagnitude < 0.001f) forward = Vector3.ProjectOnPlane(_camera.transform.up, Vector3.up);
                direction = Quaternion.AngleAxis(WallPassVisualRuntime.PreviewYaw, Vector3.up) * forward;
            }
            direction.y = 0f;
            _flow ??= new WallPassParticleFlow();
            _cameraEffect ??= new WallPassCameraEffect();
            _flow.UpdateVisual(_camera, status.HasReturnPoint || _preview ? direction : Vector3.zero,
                              _severity, settings, Time.unscaledTime);
            if (Time.unscaledTime >= _nextOwnScan)
            {
                var own = new HashSet<Renderer>(_avatar.GetComponentsInChildren<Renderer>(true));
                foreach (UxrProjectileSource source in UxrProjectileSource.AllComponents)
                {
                    if (source == null || !source.isActiveAndEnabled) continue;
                    UxrActor actor = source.TryGetWeaponOwner();
                    if (actor == null || actor.GetComponent<PlayerController>() != _player) continue;
                    RegisterSource(source);
                    foreach (Renderer renderer in _weaponRenderers[source]) own.Add(renderer);
                }
                _ownRenderers = new Renderer[own.Count];
                own.CopyTo(_ownRenderers);
                _nextOwnScan = Time.unscaledTime + 0.5f;
            }
            _cameraEffect.SetVisual(_camera, _severity, settings, _ownRenderers, _flow, violating);
            if (EnsureOverlay())
            {
                _overlay.SetActive(true);
                _warning.gameObject.SetActive(violating);
                _countdown.gameObject.SetActive(violating && !_preview && status.Punitive && status.DeathAt > 0);
                if (_countdown.gameObject.activeSelf)
                {
                    int seconds = Mathf.CeilToInt((float)System.Math.Max(0, status.DeathAt - NetworkTime.time));
                    _countdown.text = $"Вернитесь за {seconds} с";
                }
            }

            if (!_preview && (status.Stage != _shownStage || Time.unscaledTime >= _nextHaptic))
            {
                Pulse(violating ? 0.35f : 0.08f, violating ? 0.15f : 0.06f);
                _nextHaptic = Time.unscaledTime + (violating ? 0.75f : 1.5f);
            }
            _shownStage = _preview ? WallPassStage.Violating : status.Stage;
        }

        private void Pulse(float amplitude, float seconds)
        {
            if (_avatar == null || _avatar.ControllerInput == null) return;
            _avatar.ControllerInput.SendHapticFeedback(UxrHandSide.Left, UxrHapticClipType.RumbleFreqNormal, amplitude, seconds, UxrHapticMode.Mix);
            _avatar.ControllerInput.SendHapticFeedback(UxrHandSide.Right, UxrHapticClipType.RumbleFreqNormal, amplitude, seconds, UxrHapticMode.Mix);
        }

        private bool EnsureOverlay()
        {
            if (_overlay != null) return true;
            if (_overlayFailed) return false;
            Material textMaterial = Resources.Load<Material>("WallPassTextOverlay");
            TMP_FontAsset font = MenuTheme.Instance.BoldFont != null ? MenuTheme.Instance.BoldFont : TMP_Settings.defaultFontAsset;
            int layer = VisibleLayer(_camera.cullingMask);
            if (textMaterial == null || textMaterial.shader == null || !textMaterial.shader.isSupported ||
                font == null || font.material == null || layer < 0)
            {
                _overlayFailed = true;
                if (!_overlayDiagnosticReported)
                {
                    _overlayDiagnosticReported = true;
                    VrBattlegrounds.Core.GameLog.Player.Error("[WallPassFeedback] Overlay недоступен: проверьте Resources-материалы, шейдеры, шрифт MenuTheme и cullingMask камеры. Скрытие противников и вибрация продолжаются.", this);
                }
                return false;
            }
            _overlay = new GameObject("WallPassLocalOverlay", typeof(RectTransform), typeof(Canvas));
            _overlay.transform.SetParent(_camera.transform, false);
            _overlay.transform.localPosition = new Vector3(0f, 0f, 0.75f);
            _overlay.transform.localScale = Vector3.one * 0.001f;
            Canvas canvas = _overlay.GetComponent<Canvas>();
            _canvas = canvas;
            canvas.enabled = false;
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = _camera;
            canvas.sortingOrder = 32000;
            ((RectTransform)_overlay.transform).sizeDelta = new Vector2(1800f, 1800f);
            _overlay.layer = layer;
            // Resources-ссылки сохраняют overlay-шейдеры при stripping Android-сборки.
            // В обоих шейдерах ZTest Always и поддержка single-pass stereo.
            _fontMaterial = new Material(font.material);
            _fontMaterial.shader = textMaterial.shader;
            _warning = AddText("Warning", new Vector2(0f, -170f), new Vector2(820f, 110f), 36f);
            _warning.text = "Вы задели стену — вернитесь в игровое поле";
            _countdown = AddText("Countdown", new Vector2(0f, -250f), new Vector2(700f, 60f), 32f);
            return true;
        }

        private static int VisibleLayer(int mask)
        {
            int ui = LayerMask.NameToLayer("UI");
            if (ui >= 0 && (mask & (1 << ui)) != 0) return ui;
            for (int i = 0; i < 32; i++) if ((mask & (1 << i)) != 0) return i;
            return -1;
        }

        private TextMeshProUGUI AddText(string objectName, Vector2 position, Vector2 size, float fontSize)
        {
            var go = new GameObject(objectName, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            go.transform.SetParent(_overlay.transform, false);
            go.layer = _overlay.layer;
            TextMeshProUGUI text = go.GetComponent<TextMeshProUGUI>();
            text.rectTransform.anchoredPosition = position;
            text.rectTransform.sizeDelta = size;
            text.font = MenuTheme.Instance.BoldFont != null ? MenuTheme.Instance.BoldFont : TMP_Settings.defaultFontAsset;
            text.fontSize = fontSize;
            text.alignment = TextAlignmentOptions.Center;
            text.color = MenuTheme.Instance.TextPrimary;
            text.raycastTarget = false;
            if (_fontMaterial != null) text.fontSharedMaterial = _fontMaterial;
            return text;
        }

        private void BeginCamera(ScriptableRenderContext context, Camera camera)
        {
            // Canvas.worldCamera не ограничивает другие камеры: включаем только на свой рендер.
            if (_canvas != null) _canvas.enabled = camera == _camera && _overlay.activeSelf;
            if (camera != _camera) return;
            RestoreRenderers();
            if (_session == null || PlayerSession.LocalSession != _session || _player == null || !_player.IsAlive ||
                _session.WallPassStatus.Stage != WallPassStage.Violating || _preview) return;

            // До culling, каждый кадр: эффект, созданный после LateUpdate, не успевает засветиться.
            // Имена берутся из реальных дескрипторов, а не из соглашений об именах оружия.
            foreach (UxrProjectileSource source in UxrProjectileSource.AllComponents)
            {
                if (source == null || !source.isActiveAndEnabled) continue;
                RegisterSource(source);
                UxrActor actor = source.TryGetWeaponOwner();
                PlayerController owner = actor != null ? actor.GetComponent<PlayerController>() : null;
                if (IsEnemy(owner)) Hide(_weaponRenderers[source]);
            }
            foreach (UxrAvatar avatar in UxrAvatar.AllComponents)
            {
                if (avatar == null || !avatar.isActiveAndEnabled || !IsEnemy(avatar.GetComponent<PlayerController>())) continue;
                if (!_avatarRenderers.TryGetValue(avatar, out Renderer[] renderers))
                {
                    renderers = avatar.GetComponentsInChildren<Renderer>(true);
                    _avatarRenderers[avatar] = renderers;
                }
                Hide(renderers);
            }
            // Текущие эффекты — TrailRenderer и ParticleSystemRenderer. Не сканируем
            // тысячи MeshRenderer карты; поддержка mesh-эффекта включается его дескриптором.
            HideEffects(FindObjectsByType<TrailRenderer>());
            HideEffects(FindObjectsByType<ParticleSystemRenderer>());
            HideEffects(FindObjectsByType<LineRenderer>());
            if (_scanMeshEffects) HideEffects(FindObjectsByType<MeshRenderer>());
            if (_scanEffectLights)
            {
                foreach (Light light in FindObjectsByType<Light>())
                {
                    if (!IsWeaponEffect(light.transform)) continue;
                    _lightFlags[light] = light.enabled;
                    light.enabled = false;
                }
            }
        }

        private bool IsEnemy(PlayerController owner) => owner != null && owner != _player &&
            (_session.TeamIndex == 0 || owner.TeamIndex != _session.TeamIndex);

        private void SourceEnabled(UxrProjectileSource source)
        {
            if (PlayerSession.LocalSession == _session) RegisterSource(source);
        }

        private void SourceDisabled(UxrProjectileSource source) => _weaponRenderers.Remove(source);

        private void AvatarDisabled(UxrAvatar avatar) => _avatarRenderers.Remove(avatar);

        private void RegisterSource(UxrProjectileSource source)
        {
            if (_weaponRenderers.ContainsKey(source)) return;
            UxrWeapon weapon = source.GetComponentInParent<UxrWeapon>();
            _weaponRenderers[source] = (weapon != null ? weapon.gameObject : source.gameObject).GetComponentsInChildren<Renderer>(true);
            foreach (UxrShotDescriptor shot in source.ShotTypes)
            {
                RememberEffect(shot.ProjectilePrefab);
                RememberEffect(shot.PrefabInstantiateOnTipWhenShot);
                RememberEffect(shot.PrefabInstantiateOnImpact);
            }
        }

        private void Hide(IEnumerable<Renderer> renderers)
        {
            foreach (Renderer renderer in renderers)
            {
                if (renderer == null || _renderFlags.ContainsKey(renderer)) continue;
                _renderFlags[renderer] = renderer.forceRenderingOff;
                renderer.forceRenderingOff = true;
            }
        }

        private void HideEffects<T>(T[] renderers) where T : Renderer
        {
            foreach (T renderer in renderers)
            {
                if (!IsWeaponEffect(renderer.transform) || _renderFlags.ContainsKey(renderer)) continue;
                _renderFlags[renderer] = renderer.forceRenderingOff;
                renderer.forceRenderingOff = true;
            }
        }

        private void RememberEffect(GameObject prefab)
        {
            if (prefab == null || !_effectNames.Add(prefab.name + "(Clone)")) return;
            if (prefab.GetComponentInChildren<MeshRenderer>(true) != null) _scanMeshEffects = true;
            if (prefab.GetComponentInChildren<Light>(true) != null) _scanEffectLights = true;
        }

        private bool IsWeaponEffect(Transform transform)
        {
            for (Transform current = transform; current != null; current = current.parent)
                if (_effectNames.Contains(current.name)) return true;
            return false;
        }

        private void EndCamera(ScriptableRenderContext context, Camera camera)
        {
            if (camera == _camera) RestoreRenderers();
            if (_canvas != null) _canvas.enabled = false;
        }

        private void RestoreRenderers()
        {
            foreach (KeyValuePair<Renderer, bool> pair in _renderFlags)
                if (pair.Key != null) pair.Key.forceRenderingOff = pair.Value;
            _renderFlags.Clear();
            foreach (KeyValuePair<Light, bool> pair in _lightFlags)
                if (pair.Key != null) pair.Key.enabled = pair.Value;
            _lightFlags.Clear();
        }

        private void ClearVisuals()
        {
            RestoreRenderers();
            if (_overlay != null) _overlay.SetActive(false);
            if (_canvas != null) _canvas.enabled = false;
            _flow?.Clear();
            _cameraEffect?.Clear();
            _severity = 0f;
            _preview = false;
            _shownStage = WallPassStage.Clear;
            _nextHaptic = 0f;
        }

        private void ResetFeedback()
        {
            ClearVisuals();
            if (_overlay != null) Destroy(_overlay);
            if (_fontMaterial != null) Destroy(_fontMaterial);
            _cameraEffect?.Dispose();
            _flow?.Dispose();
            _cameraEffect = null;
            _flow = null;
            _ownRenderers = System.Array.Empty<Renderer>();
            _nextOwnScan = 0f;
            _canvas = null;
            _overlay = null;
            _fontMaterial = null;
            _camera = null;
            _avatar = null;
            _player = null;
            _effectNames.Clear();
            _avatarRenderers.Clear();
            _weaponRenderers.Clear();
            _scanMeshEffects = false;
            _scanEffectLights = false;
            _overlayFailed = false;
        }
    }

}

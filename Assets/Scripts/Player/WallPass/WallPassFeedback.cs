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
        private WallPassEdgeGraphic _edge;
        private TextMeshProUGUI _warning;
        private TextMeshProUGUI _countdown;
        private RectTransform _arrow;
        private Material _graphicMaterial;
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
            if (status.Stage == WallPassStage.Clear)
            {
                ClearVisuals();
                return;
            }

            bool violating = status.Stage == WallPassStage.Violating;
            if (EnsureOverlay())
            {
                _overlay.SetActive(true);
                _edge.color = violating ? new Color(1f, 0.08f, 0.03f, 0.62f) : new Color(1f, 0.68f, 0.15f, 0.18f);
                _warning.gameObject.SetActive(violating);
                _countdown.gameObject.SetActive(violating && status.Punitive && status.DeathAt > 0);
                if (_countdown.gameObject.activeSelf)
                {
                    int seconds = Mathf.CeilToInt((float)System.Math.Max(0, status.DeathAt - NetworkTime.time));
                    _countdown.text = $"Вернитесь за {seconds} с";
                }
                _arrow.gameObject.SetActive(violating);
                if (violating)
                {
                    Vector3 direction = status.ReturnPoint - _camera.transform.position;
                    direction.y = 0f;
                    Vector3 local = _camera.transform.InverseTransformDirection(direction);
                    _arrow.localRotation = Quaternion.Euler(0f, 0f, -Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg);
                }
            }

            if (status.Stage != _shownStage || Time.unscaledTime >= _nextHaptic)
            {
                Pulse(violating ? 0.35f : 0.08f, violating ? 0.15f : 0.06f);
                _nextHaptic = Time.unscaledTime + (violating ? 0.75f : 1.5f);
            }
            _shownStage = status.Stage;
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
            Material edgeMaterial = Resources.Load<Material>("WallPassEdgeOverlay");
            Material textMaterial = Resources.Load<Material>("WallPassTextOverlay");
            TMP_FontAsset font = MenuTheme.Instance.BoldFont != null ? MenuTheme.Instance.BoldFont : TMP_Settings.defaultFontAsset;
            int layer = VisibleLayer(_camera.cullingMask);
            if (edgeMaterial == null || edgeMaterial.shader == null || !edgeMaterial.shader.isSupported ||
                textMaterial == null || textMaterial.shader == null || !textMaterial.shader.isSupported ||
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
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = _camera;
            canvas.sortingOrder = 32000;
            ((RectTransform)_overlay.transform).sizeDelta = new Vector2(1800f, 1800f);
            _overlay.layer = layer;
            // Resources-ссылки сохраняют overlay-шейдеры при stripping Android-сборки.
            // В обоих шейдерах ZTest Always и поддержка single-pass stereo.
            _graphicMaterial = new Material(edgeMaterial);
            _fontMaterial = new Material(font.material);
            _fontMaterial.shader = textMaterial.shader;
            var edgeObject = new GameObject("WallPassEdge", typeof(RectTransform), typeof(CanvasRenderer), typeof(WallPassEdgeGraphic));
            edgeObject.transform.SetParent(_overlay.transform, false);
            edgeObject.layer = _overlay.layer;
            _edge = edgeObject.GetComponent<WallPassEdgeGraphic>();
            _edge.rectTransform.sizeDelta = new Vector2(1800f, 1800f);
            _edge.material = _graphicMaterial;
            _edge.raycastTarget = false;

            _warning = AddText("Warning", new Vector2(0f, -170f), new Vector2(820f, 110f), 36f);
            _warning.text = "Вы задели стену — вернитесь в игровое поле";
            _countdown = AddText("Countdown", new Vector2(0f, -250f), new Vector2(700f, 60f), 32f);
            var arrowObject = new GameObject("ReturnDirection", typeof(RectTransform), typeof(CanvasRenderer), typeof(WallPassArrowGraphic));
            arrowObject.transform.SetParent(_overlay.transform, false);
            arrowObject.layer = _overlay.layer;
            _arrow = arrowObject.GetComponent<RectTransform>();
            _arrow.anchoredPosition = new Vector2(0f, -340f);
            _arrow.sizeDelta = new Vector2(60f, 70f);
            Graphic arrowGraphic = arrowObject.GetComponent<Graphic>();
            arrowGraphic.material = _graphicMaterial;
            arrowGraphic.color = MenuTheme.Instance.TextPrimary;
            arrowGraphic.raycastTarget = false;
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
            if (camera != _camera) return;
            RestoreRenderers();
            if (_session == null || PlayerSession.LocalSession != _session || _player == null || !_player.IsAlive ||
                _session.WallPassStatus.Stage != WallPassStage.Violating) return;

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
            _shownStage = WallPassStage.Clear;
            _nextHaptic = 0f;
        }

        private void ResetFeedback()
        {
            ClearVisuals();
            if (_overlay != null) Destroy(_overlay);
            if (_graphicMaterial != null) Destroy(_graphicMaterial);
            if (_fontMaterial != null) Destroy(_fontMaterial);
            _overlay = null;
            _graphicMaterial = null;
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

    /// <summary>Градиентное свечение края в общем для двух глаз world-space Canvas.</summary>
    internal sealed class WallPassEdgeGraphic : MaskableGraphic
    {
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Vector2 half = rectTransform.rect.size * 0.5f;
            const int segments = 64;
            Color innerColor = color;
            innerColor.a = 0f;
            for (int i = 0; i <= segments; i++)
            {
                float angle = i * Mathf.PI * 2f / segments;
                Vector2 direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                // Внешний контур — квадрат: свечение закрывает также углы поля зрения.
                Vector2 outer = direction / Mathf.Max(Mathf.Abs(direction.x), Mathf.Abs(direction.y));
                vh.AddVert(Vector2.Scale(direction * 0.42f, half), innerColor, Vector2.zero);
                vh.AddVert(Vector2.Scale(outer, half), color, Vector2.zero);
                if (i == segments) continue;
                int index = i * 2;
                vh.AddTriangle(index, index + 1, index + 2);
                vh.AddTriangle(index + 1, index + 3, index + 2);
            }
        }
    }

    /// <summary>Стрелка на плоскости HUD: вверх — вперёд, вниз — назад.</summary>
    internal sealed class WallPassArrowGraphic : MaskableGraphic
    {
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Vector2 half = rectTransform.rect.size * 0.5f;
            vh.AddVert(new Vector3(0f, half.y, 0f), color, Vector2.zero);
            vh.AddVert(new Vector3(-half.x, -half.y, 0f), color, Vector2.zero);
            vh.AddVert(new Vector3(half.x, -half.y, 0f), color, Vector2.zero);
            vh.AddTriangle(0, 1, 2);
        }
    }
}

using UltimateXR.Avatar;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using VrBattlegrounds.Core;

namespace VrBattlegrounds.Weapons.Sights
{
    /// <summary>
    /// Кратность увеличивающего прицела (решение пользователя 2026-10-23): своя моно-камера с узким полем рендерит сцену
    /// в малую текстуру, только пока окуляр у глаза локального игрока. Владеет камерой, текстурой и условием «у глаза»;
    /// параметры линзы пишет только <see cref="WeaponOpticView"/> (берёт отсюда <see cref="Texture"/> и <see cref="IsViewing"/>).
    /// Точка сведения — тоже у <see cref="WeaponOpticView"/>: камера смотрит из центра линзы на неё, поэтому центр
    /// картинки совпадает с сеткой. Выстрел и Transform оружия не меняются.
    /// </summary>
    public sealed class WeaponScopeCamera : MonoBehaviour
    {
        [SerializeField] private WeaponOpticView _view;
        [Tooltip("Кратность: во сколько раз угловой размер цели в окуляре больше, чем без прицела.")]
        [SerializeField, Range(1f, 4f)] private float _magnification = 1.75f;
        [Tooltip("Видимое поле окуляра: тангенс половины угла, под которым глаз видит картинку прицела. " +
                 "Дальше от оси — край трубы (затемнение в shader).")]
        [SerializeField, Range(0.02f, 0.3f)] private float _apparentHalfField = 0.1f;
        [Tooltip("Сторона квадратной текстуры камеры, пикс. Цена GPU растёт с площадью; бюджет — +1 мс ovr_gpu (решение 2026-10-23).")]
        [SerializeField, Range(128, 512)] private int _resolution = 320;
        [Tooltip("Дальняя плоскость камеры прицела, м.")]
        [SerializeField, Min(5f)] private float _farClip = 80f;

        [Header("У глаза (гистерезис)")]
        [Tooltip("Расстояние от глаза (камеры головы) до окуляра, м.")]
        [SerializeField, Min(0.01f)] private float _enterDistance = 0.10f;
        [SerializeField, Min(0.01f)] private float _exitDistance = 0.14f;
        [SerializeField, Range(1f, 45f)] private float _enterAngle = 10f;
        [SerializeField, Range(1f, 60f)] private float _exitAngle = 15f;
        [Tooltip("Минимальное время, на которое включается камера: без дребезга на границе и без повторных выделений.")]
        [SerializeField, Min(0f)] private float _minHoldSeconds = 0.5f;

        private Camera _camera;
        private RenderTexture _texture;
        private bool _viewing;
        private float _viewingSince;

        public float Magnification => _magnification;
        public float ApparentHalfField => _apparentHalfField;
        /// <summary>Камера прицела активна и текстура содержит текущий кадр.</summary>
        public bool IsViewing => _viewing && _texture != null;
        public Texture Texture => _texture;

        private static readonly System.Collections.Generic.HashSet<Camera> ScopeCameras = new System.Collections.Generic.HashSet<Camera>();

        /// <summary>Камера принадлежит прицелу (не глазу игрока): линзы для неё параметры не пишут.</summary>
        public static bool IsScopeCamera(Camera camera) => ScopeCameras.Count > 0 && ScopeCameras.Contains(camera);

        private void OnEnable() => Application.onBeforeRender += HandleBeforeRender;

        private void OnDisable()
        {
            Application.onBeforeRender -= HandleBeforeRender;
            SetViewing(false);
        }

        private void OnDestroy()
        {
            if (_camera != null)
            {
                ScopeCameras.Remove(_camera);
                Destroy(_camera.gameObject);
            }
            if (_texture != null)
            {
                _texture.Release();
                Destroy(_texture);
            }
        }

        // После позы трекинга и SDK, до рендера кадра: та же поза, что увидит WeaponOpticView.
        private void HandleBeforeRender()
        {
            Transform head = UxrAvatar.LocalAvatar != null ? UxrAvatar.LocalAvatar.CameraTransform : null;
            if (head == null || _view == null || !_view.TryGetZero(out Vector3 zero, out Transform muzzle, out _)
                || !_view.TryGetLensBounds(out Bounds bounds))
            {
                SetViewing(false);
                return;
            }

            Vector3 lens = bounds.center;
            Vector3 axis = zero - lens;
            Vector3 dir = axis.normalized;
            // Окуляр — задний край корпуса вдоль оси прицела (опорная точка AABB по направлению −dir).
            Vector3 e = bounds.extents;
            Vector3 eyepiece = lens - dir * (Mathf.Abs(e.x * dir.x) + Mathf.Abs(e.y * dir.y) + Mathf.Abs(e.z * dir.z));
            Vector3 toEyepiece = eyepiece - head.position;
            float distance = toEyepiece.magnitude;
            float angle = Vector3.Angle(axis, toEyepiece);
            bool near = _viewing
                ? distance <= _exitDistance && angle <= _exitAngle
                : distance <= _enterDistance && angle <= _enterAngle;
            if (!near && _viewing && Time.unscaledTime - _viewingSince < _minHoldSeconds) near = true;
            SetViewing(near);
            if (!_viewing) return;

            Transform t = _camera.transform;
            t.SetPositionAndRotation(lens, Quaternion.LookRotation(axis, muzzle.up));
            // Ствол и детали самого оружия ближе среза дула в кадр не попадают.
            _camera.nearClipPlane = Mathf.Max(0.05f, Vector3.Dot(muzzle.position - lens, dir) + 0.05f);
            _camera.farClipPlane = Mathf.Max(_camera.nearClipPlane + 1f, _farClip);
            _camera.fieldOfView = 2f * Mathf.Atan(_apparentHalfField / _magnification) * Mathf.Rad2Deg;
        }

        private void SetViewing(bool viewing)
        {
            if (viewing == _viewing) return;
            if (viewing) EnsureCamera();
            _viewing = viewing;
            if (viewing) _viewingSince = Time.unscaledTime;
            if (_camera != null) _camera.enabled = viewing;
            GameLog.WeaponSystem.Info($"{name}: камера прицела {(viewing ? "включена" : "выключена")} (×{_magnification:0.##}).", this);
        }

        private void EnsureCamera()
        {
            if (_texture == null)
            {
                _texture = new RenderTexture(_resolution, _resolution, 16, RenderTextureFormat.Default)
                {
                    name = name + "_Scope", antiAliasing = 1, useMipMap = false, autoGenerateMips = false,
                };
                _texture.Create();
            }
            if (_camera != null) return;

            var go = new GameObject(name + "_ScopeCamera") { hideFlags = HideFlags.DontSave };
            go.transform.SetParent(transform, false);
            _camera = go.AddComponent<Camera>();
            ScopeCameras.Add(_camera);
            _camera.enabled = false;
            _camera.stereoTargetEye = StereoTargetEyeMask.None;
            _camera.targetTexture = _texture;
            _camera.aspect = 1f;
            _camera.depth = -50f;
            _camera.allowMSAA = false;
            _camera.allowHDR = false;
            _camera.useOcclusionCulling = true;
            var data = go.AddComponent<UniversalAdditionalCameraData>();
            data.renderShadows = false;
            data.renderPostProcessing = false;
            data.requiresColorOption = CameraOverrideOption.Off;
            data.requiresDepthOption = CameraOverrideOption.Off;
            data.antialiasing = AntialiasingMode.None;
            Camera head = UxrAvatar.LocalAvatar != null ? UxrAvatar.LocalAvatar.CameraComponent : null;
            if (head != null)
            {
                _camera.cullingMask = head.cullingMask;
                _camera.clearFlags = head.clearFlags;
                _camera.backgroundColor = head.backgroundColor;
            }
        }
    }
}

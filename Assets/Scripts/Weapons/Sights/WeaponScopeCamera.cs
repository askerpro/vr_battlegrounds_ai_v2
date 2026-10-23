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
        [Tooltip("Сторона квадратной текстуры камеры, пикс. Цена GPU растёт с площадью; бюджет — +1 мс ovr_gpu (решение 2026-10-23).")]
        [SerializeField, Range(128, 512)] private int _resolution = 384;
        [Tooltip("MSAA текстуры прицела: без него мишени в увеличении рябили (шлем 2026-10-23).")]
        [SerializeField, Range(1, 8)] private int _antiAliasing = 4;
        [Tooltip("Дальняя плоскость камеры прицела, м.")]
        [SerializeField, Min(5f)] private float _farClip = 80f;

        [Header("У глаза (гистерезис)")]
        // Шлем 2026-10-23: корпус гарнитуры не даёт поднести окуляр ближе ~5 см к глазу; угол к оси у самого окуляра
        // скачет на 20–30° от сдвига на пару сантиметров — условие по углу заменено смещением глаза от оси прицела.
        [Tooltip("Глаз позади окуляра вдоль оси прицела не дальше, м.")]
        [SerializeField, Min(0.01f)] private float _enterDistance = 0.15f;
        [SerializeField, Min(0.01f)] private float _exitDistance = 0.20f;
        [Tooltip("Смещение ближнего глаза от оси прицела, м: в этих пределах камера работает, а картинку открывает зрачок выхода.")]
        [SerializeField, Min(0.001f)] private float _enterOffset = 0.02f;
        [SerializeField, Min(0.001f)] private float _exitOffset = 0.03f;
        [Tooltip("Минимальное время, на которое включается камера: без дребезга на границе и без повторных выделений.")]
        [SerializeField, Min(0f)] private float _minHoldSeconds = 0.5f;

        private Camera _camera;
        private RenderTexture _texture;
        private bool _viewing;
        private float _viewingSince;
        private float _nextNearLog;
        private const float NearLogDistance = 0.3f, NearLogInterval = 2f;
        // Поле окуляра: глаз ближе 3 см к стеклу не подходит (гарнитура), пределы — от узкого до широкого окуляра.
        private const float MinEyeRelief = 0.03f, MinField = 0.1f, MaxField = 0.8f;

        public float Magnification => _magnification;
        /// <summary>Зрачок выхода — допуск ракурса из общих настроек прицелов (ползунок в <see cref="WeaponSightCalibrationSettings"/>).</summary>
        public float ExitPupil => _view != null && _view.Settings != null
            ? _view.Settings.ScopeEyeTolerance : WeaponSightCalibrationSettings.DefaultScopeEyeTolerance;
        /// <summary>Окуляр (задний край корпуса на оси) и ось прицела к точке сведения — для тени зрачка в shader.</summary>
        public Vector3 Eyepiece { get; private set; }
        public Vector3 Axis { get; private set; }
        /// <summary>
        /// Видимое поле окуляра (тангенс половины угла): радиус стекла / расстояние глаза до окуляра. Картинка и сетка
        /// привязаны к стеклу (шлем 2026-10-23: привязка к направлению взгляда уводила картинку вбок), поэтому кратность
        /// честная при любом удалении глаза: поле камеры — это же поле, делённое на кратность.
        /// </summary>
        public float ApparentHalfField { get; private set; } = 0.3f;
        public float LensRadius { get; private set; }
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
                || !_view.TryGetLens(out Vector3 eyepiece, out float lensRadius))
            {
                SetViewing(false);
                return;
            }

            // Ось прицела — от центра стекла окуляра к точке сведения: на ней центр картинки и сетка.
            Vector3 axis = zero - eyepiece;
            Vector3 dir = axis.normalized;
            Eyepiece = eyepiece;
            Axis = dir;
            LensRadius = lensRadius;
            // Ближний к оси глаз: в стерео смотрит один глаз, центр головы смещён от оси на полглаза.
            Camera headCamera = UxrAvatar.LocalAvatar.CameraComponent;
            float behind = 0f, offset = float.MaxValue;
            foreach (Vector3 eye in Eyes(headCamera, head))
            {
                Vector3 w = eye - eyepiece;
                float along = -Vector3.Dot(w, dir);
                float lateral = (w + dir * along).magnitude;
                if (lateral < offset) { offset = lateral; behind = along; }
            }
            bool near = _viewing
                ? behind > -0.02f && behind <= _exitDistance && offset <= _exitOffset
                : behind > -0.02f && behind <= _enterDistance && offset <= _enterOffset;
            if (!near && _viewing && Time.unscaledTime - _viewingSince < _minHoldSeconds) near = true;
            SetViewing(near);
            // Диагностика порога: окуляр рядом с глазом, а увеличение не включилось — чего не хватило (не чаще раза в 2 с).
            if (!_viewing && Mathf.Abs(behind) < NearLogDistance && offset < NearLogDistance && Time.unscaledTime >= _nextNearLog)
            {
                _nextNearLog = Time.unscaledTime + NearLogInterval;
                GameLog.WeaponSystem.Info($"{name}: глаз в {behind * 100f:0.0} см за окуляром, в {offset * 100f:0.0} см от оси — увеличение " +
                                          $"не включено (вход ≤ {_enterDistance * 100f:0} см и ≤ {_enterOffset * 100f:0.0} см от оси).", this);
            }
            if (!_viewing) return;

            // Поле окуляра — угол, под которым глаз видит стекло; камера — то же поле, делённое на кратность.
            ApparentHalfField = Mathf.Clamp(lensRadius / Mathf.Max(behind, MinEyeRelief), MinField, MaxField);
            Transform t = _camera.transform;
            t.SetPositionAndRotation(eyepiece, Quaternion.LookRotation(axis, muzzle.up));
            // Ствол и детали самого оружия ближе среза дула в кадр не попадают.
            _camera.nearClipPlane = Mathf.Max(0.05f, Vector3.Dot(muzzle.position - eyepiece, dir) + 0.05f);
            _camera.farClipPlane = Mathf.Max(_camera.nearClipPlane + 1f, _farClip);
            _camera.fieldOfView = 2f * Mathf.Atan(ApparentHalfField / _magnification) * Mathf.Rad2Deg;
        }

        /// <summary>Позиции глаз в мире: стерео-матрицы камеры головы, иначе центр головы.</summary>
        private static System.Collections.Generic.IEnumerable<Vector3> Eyes(Camera camera, Transform head)
        {
            if (camera != null && camera.stereoEnabled)
            {
                yield return camera.GetStereoViewMatrix(Camera.StereoscopicEye.Left).inverse.GetColumn(3);
                yield return camera.GetStereoViewMatrix(Camera.StereoscopicEye.Right).inverse.GetColumn(3);
            }
            else yield return head.position;
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
                // Глубина 24 бита: с 16 мишени и декали на 15 м дрожали (z-fighting), шлем 2026-10-23.
                _texture = new RenderTexture(_resolution, _resolution, 24, RenderTextureFormat.Default)
                {
                    name = name + "_Scope", antiAliasing = _antiAliasing, useMipMap = false, autoGenerateMips = false,
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
            _camera.allowMSAA = _antiAliasing > 1;
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

using UltimateXR.Avatar;
using UltimateXR.Core;
using UltimateXR.Manipulation;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using VrBattlegrounds.Core;

namespace VrBattlegrounds.Weapons.Sights
{
    /// <summary>
    /// Кратность увеличивающего прицела (решение пользователя 2026-10-23): своя моно-камера с узким полем рендерит сцену
    /// в малую текстуру, пока окуляр у головы локального игрока. Владеет камерой, текстурой, условием «у глаза» и чёткостью
    /// картинки; параметры линзы пишет только <see cref="WeaponOpticView"/> (берёт отсюда <see cref="Texture"/>,
    /// <see cref="IsViewing"/> и <see cref="Clarity"/>). Точка сведения — тоже у <see cref="WeaponOpticView"/>: камера
    /// смотрит из центра линзы на неё, поэтому центр картинки совпадает с сеткой. Выстрел и Transform оружия не меняются.
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

        [Header("У глаза")]
        // Шлем 2026-10-23: корпус гарнитуры не даёт поднести окуляр ближе ~5 см к глазу; угол к оси у самого окуляра
        // скачет на 20–30° от сдвига на пару сантиметров — условие по углу заменено смещением глаза от оси прицела.
        // Дистанция чёткой картинки (глаз позади окуляра вдоль оси) — ползунок общих настроек прицелов, EnterDistance.
        [Tooltip("Смещение ближнего глаза от оси прицела, м, до которого картинка чёткая (дальше — плавно размывается за " +
                 "LateralFade); внутри неё яркое поле ещё сужает зрачок выхода.")]
        [SerializeField, Min(0.001f)] private float _enterOffset = 0.02f;

        private Camera _camera;
        private RenderTexture _texture;
        // UxrAvatar.LocalAvatar — LINQ по компонентам с выделением на каждый вызов: берётся заново, только когда
        // закешированный аватар пропал, выключен или перестал быть локальным.
        private UxrAvatar _localAvatar;
        private bool _active;
        // Текстура содержит кадр, отрисованный после включения (шлем 2026-10-24: при повторном поднесении первым
        // показывался застывший кадр прошлого раза).
        private bool _fresh;
        // Оружие этого прицела (корневой граббабл): камера работает только у оружия в руке локального игрока — прицелы на
        // стене арсенала, стенде и у чужих игроков камеру и RT не получают (ревью performance 2026-10-24).
        private UxrGrabbableObject _weapon;
        private bool _blurLogged;
        // Гистерезис границ включения: без мигания пятна и лога при дрожании руки на границе (ревью 2026-10-24).
        private const float CullHysteresis = 0.05f, FrontEnter = -0.02f, FrontExit = -0.04f;
        // Решение пользователя 2026-10-24: картинка появляется и уходит плавно. Полосы размытия за порогами чёткости, м.
        private const float LateralFade = 0.04f;
        // Чёткость догоняет цель за это время, с: без скачка при входе и выходе.
        private const float ClarityResponse = 0.15f;
        // Размытая картинка вне зоны чёткости (решение 2026-10-24: «всегда размытая картинка по центру») — камера рисует
        // раз в столько кадров: размытие скрывает низкую частоту, цена камеры — четверть.
        private const int PreviewInterval = 4;
        // Прицелы дальше зоны размытия + этого запаса от головы (стена арсенала, пол, чужие руки) отсекаются до расчёта
        // глаз — onBeforeRender идёт каждый кадр у каждого экземпляра.
        private const float HeadCullMargin = 0.15f;
        // Поле окуляра: глаз ближе 3 см к стеклу не подходит (гарнитура), пределы — от узкого до широкого окуляра.
        private const float MinEyeRelief = 0.03f, MinField = 0.1f, MaxField = 0.8f;

        public float Magnification => _magnification;
        /// <summary>Зрачок выхода — допуск ракурса из общих настроек прицелов (ползунок в <see cref="WeaponSightCalibrationSettings"/>).</summary>
        public float ExitPupil => _view != null && _view.Settings != null
            ? _view.Settings.ScopeEyeTolerance : WeaponSightCalibrationSettings.DefaultScopeEyeTolerance;
        /// <summary>
        /// Дистанция чёткой картинки — глаз не дальше этого позади окуляра, м; ползунок в <see cref="WeaponSightCalibrationSettings"/>
        /// (решение пользователя 2026-10-24). Дальше на <see cref="WeaponSightCalibrationSettings.ScopeFadeBand"/> картинка
        /// плавно размывается.
        /// </summary>
        public float EnterDistance => _view != null && _view.Settings != null
            ? _view.Settings.ScopeEnterDistance : WeaponSightCalibrationSettings.DefaultScopeEnterDistance;
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
        /// <summary>Чёткость картинки 0..1: 1 — глаз у окуляра на оси, 0 — размытая тусклая картинка (сетки нет).</summary>
        public float Clarity { get; private set; }
        /// <summary>Камера прицела работает и текстура содержит кадр, отрисованный после её включения.</summary>
        public bool IsViewing => _active && _fresh && _texture != null;
        public Texture Texture => _texture;

        private static readonly System.Collections.Generic.HashSet<Camera> ScopeCameras = new System.Collections.Generic.HashSet<Camera>();

        /// <summary>Камера принадлежит прицелу (не глазу игрока): линзы для неё параметры не пишут.</summary>
        public static bool IsScopeCamera(Camera camera) => ScopeCameras.Count > 0 && ScopeCameras.Contains(camera);

        private void OnEnable()
        {
            Application.onBeforeRender += HandleBeforeRender;
            RenderPipelineManager.endCameraRendering += HandleEndCameraRendering;
        }

        private void OnDisable()
        {
            Application.onBeforeRender -= HandleBeforeRender;
            RenderPipelineManager.endCameraRendering -= HandleEndCameraRendering;
            SetActive(false);
            ReleaseCamera();
        }

        private void OnDestroy() => ReleaseCamera();

        /// <summary>Камера и RT (384², MSAA) не живут у выключенного прицела; при следующем «у глаза» создаются заново.</summary>
        private void ReleaseCamera()
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
            _camera = null;
            _texture = null;
        }

        private void HandleEndCameraRendering(ScriptableRenderContext context, Camera camera)
        {
            if (camera == _camera && _camera != null) _fresh = true;
        }

        // После позы трекинга и SDK, до рендера кадра: та же поза, что увидит WeaponOpticView.
        private void HandleBeforeRender()
        {
            UxrAvatar avatar = LocalAvatar();
            Transform head = avatar != null ? avatar.CameraTransform : null;
            if (head == null || !HeldBy(avatar) || _view == null || !_view.TryGetZero(out Vector3 zero, out Transform muzzle, out _)
                || !_view.TryGetLens(out Vector3 eyepiece, out float lensRadius))
            {
                SetActive(false);
                return;
            }

            // Ось прицела — от центра стекла окуляра к точке сведения: на ней центр картинки и сетка.
            Vector3 axis = zero - eyepiece;
            Vector3 dir = axis.normalized;
            Eyepiece = eyepiece;
            Axis = dir;
            LensRadius = lensRadius;
            float enterDistance = EnterDistance;
            float fadeDistance = enterDistance + WeaponSightCalibrationSettings.ScopeFadeBand;
            float cull = fadeDistance + HeadCullMargin + (_active ? CullHysteresis : 0f);
            if ((eyepiece - head.position).sqrMagnitude > cull * cull)
            {
                SetActive(false);
                return;
            }
            // Ближний к оси глаз: в стерео смотрит один глаз, центр головы смещён от оси на полглаза.
            Eyes(avatar.CameraComponent, head, out Vector3 left, out Vector3 right);
            Offset(left, eyepiece, dir, out float behind, out float offset);
            Offset(right, eyepiece, dir, out float behindRight, out float offsetRight);
            if (offsetRight < offset) { offset = offsetRight; behind = behindRight; }
            // Глаз перед окуляром (смотрит в объектив или сбоку на корпус) — картинки нет.
            if (behind <= (_active ? FrontExit : FrontEnter))
            {
                SetActive(false);
                return;
            }

            // Чёткость: 1 у окуляра на оси, плавно к 0 за полосой по расстоянию и по смещению вбок.
            float target = (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(enterDistance, fadeDistance, behind)))
                           * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(_enterOffset, _enterOffset + LateralFade, offset)));
            SetActive(true);
            Clarity = _fresh ? Mathf.MoveTowards(Clarity, target, Time.unscaledDeltaTime / ClarityResponse) : 0f;
            // Чёткая картинка — каждый кадр; размытая — раз в PreviewInterval кадров со своим сдвигом у экземпляра
            // (и сразу после включения).
            _camera.enabled = target > 0f || Clarity > 0f || !_fresh
                              || (Time.frameCount + (GetInstanceID() & 3)) % PreviewInterval == 0;
            // Диагностика: картинка ушла в размытие у окуляра — чего не хватило; один раз на переход, не покадрово.
            bool blurred = target <= 0f;
            if (blurred && !_blurLogged && behind < fadeDistance)
            {
                GameLog.WeaponSystem.Info($"{name}: глаз в {behind * 100f:0.0} см за окуляром, в {offset * 100f:0.0} см от оси — картинка " +
                                          $"размыта (чётко ≤ {enterDistance * 100f:0} см и ≤ {_enterOffset * 100f:0.0} см от оси).", this);
            }
            _blurLogged = blurred && (_blurLogged || behind < fadeDistance);

            // Поле окуляра — угол, под которым глаз видит стекло; камера — то же поле, делённое на кратность.
            ApparentHalfField = Mathf.Clamp(lensRadius / Mathf.Max(behind, MinEyeRelief), MinField, MaxField);
            Transform t = _camera.transform;
            t.SetPositionAndRotation(eyepiece, Quaternion.LookRotation(axis, muzzle.up));
            // Ствол и детали самого оружия ближе среза дула в кадр не попадают.
            _camera.nearClipPlane = Mathf.Max(0.05f, Vector3.Dot(muzzle.position - eyepiece, dir) + 0.05f);
            _camera.farClipPlane = Mathf.Max(_camera.nearClipPlane + 1f, _farClip);
            _camera.fieldOfView = 2f * Mathf.Atan(ApparentHalfField / _magnification) * Mathf.Rad2Deg;
        }

        /// <summary>Локальный аватар без покадровых выделений: кеш, пока он включён и остаётся локальным.</summary>
        private UxrAvatar LocalAvatar()
        {
            if (_localAvatar == null || !_localAvatar.isActiveAndEnabled || _localAvatar.AvatarMode != UxrAvatarMode.Local)
                _localAvatar = UxrAvatar.LocalAvatar;
            return _localAvatar;
        }

        /// <summary>Оружие прицела в руке (или его деталь — затвор, помпа) у аватара; без выделений: граббер каждой руки.</summary>
        private bool HeldBy(UxrAvatar avatar)
        {
            if (_weapon == null) _weapon = GetComponentInParent<UxrGrabbableObject>();
            if (_weapon == null) return false;
            return Holds(avatar.GetGrabber(UxrHandSide.Left)) || Holds(avatar.GetGrabber(UxrHandSide.Right));
        }

        private bool Holds(UxrGrabber grabber)
        {
            UxrGrabbableObject grabbed = grabber != null ? grabber.GrabbedObject : null;
            return grabbed != null && (grabbed == _weapon || grabbed.transform.IsChildOf(_weapon.transform));
        }

        /// <summary>Позиции глаз в мире без выделений: стерео-матрицы камеры головы, иначе оба — центр головы.</summary>
        private static void Eyes(Camera camera, Transform head, out Vector3 left, out Vector3 right)
        {
            if (camera != null && camera.stereoEnabled)
            {
                left = camera.GetStereoViewMatrix(Camera.StereoscopicEye.Left).inverse.GetColumn(3);
                right = camera.GetStereoViewMatrix(Camera.StereoscopicEye.Right).inverse.GetColumn(3);
            }
            else left = right = head.position;
        }

        /// <summary>Глаз относительно окуляра: насколько позади вдоль оси и насколько в стороне от неё, м.</summary>
        private static void Offset(Vector3 eye, Vector3 eyepiece, Vector3 axis, out float behind, out float lateral)
        {
            Vector3 w = eye - eyepiece;
            behind = -Vector3.Dot(w, axis);
            lateral = (w + axis * behind).magnitude;
        }

        /// <summary>Камера работает (чётко или размыто) или выключена; после выключения кадр в текстуре считается устаревшим.</summary>
        private void SetActive(bool active)
        {
            if (active == _active) return;
            if (active) EnsureCamera();
            _active = active;
            _fresh = false;
            _blurLogged = false;
            Clarity = 0f;
            if (_camera != null) _camera.enabled = active;
            GameLog.WeaponSystem.Info($"{name}: камера прицела {(active ? "включена" : "выключена")} (×{_magnification:0.##}).", this);
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
            UxrAvatar avatar = LocalAvatar();
            Camera head = avatar != null ? avatar.CameraComponent : null;
            if (head != null)
            {
                _camera.cullingMask = head.cullingMask;
                _camera.clearFlags = head.clearFlags;
                _camera.backgroundColor = head.backgroundColor;
            }
        }
    }
}

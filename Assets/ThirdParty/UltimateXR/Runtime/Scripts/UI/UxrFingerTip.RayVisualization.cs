// --------------------------------------------------------------------------------------------------------------------
// <copyright file="UxrFingerTip.RayVisualization.cs" company="VRMADA">
//   Copyright (c) VRMADA, All rights reserved.
// </copyright>
// --------------------------------------------------------------------------------------------------------------------
// VR Battlegrounds patch: визуализация существующего указателя без второго потока ввода.
using UltimateXR.Avatar;
using UltimateXR.Core;
using UltimateXR.Extensions.Unity.Render;
using UltimateXR.UI.UnityInputModule;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;

namespace UltimateXR.UI
{
    public partial class UxrFingerTip
    {
        #region Public Types & Data

        /// <summary>
        ///     Включает диагностический луч и метки. По умолчанию выключено; значение не сериализуется.
        ///     Не меняет обработку касаний, пороги наведения и режим канваса.
        /// </summary>
        public bool RayVisualizationEnabled
        {
            get => _rayVisualizationEnabled;
            set
            {
                if (_rayVisualizationEnabled == value)
                {
                    return;
                }

                _rayVisualizationEnabled = value;
                DisableRayVisualization();
                EnableRayVisualization();
            }
        }

        /// <summary>
        ///     Показывается ли сейчас луч или метка при наведении на доступный канвас.
        /// </summary>
        public bool IsRayVisible => _rayVisualization != null && _rayVisualization.IsVisible;

        /// <summary>
        ///     Текущая длина луча. За плоскостью касания и при скрытой визуализации равна нулю.
        /// </summary>
        public float CurrentRayLength { get; private set; }

        /// <summary>Ширина луча в метрах.</summary>
        public float RayWidth { get; set; } = 0.0015f;

        /// <summary>Диаметр метки кончика пальца в метрах.</summary>
        public float RayOriginSize { get; set; } = 0.004f;

        /// <summary>Диаметр метки штатного попадания в метрах.</summary>
        public float RayHitSize { get; set; } = 0.003f;

        /// <summary>Цвет при штатном попадании.</summary>
        public Color RayColorInteractive { get; set; } = new Color(0.15f, 1.0f, 0.3f, 0.9f);

        /// <summary>Цвет предварительного наведения на экран без штатного попадания.</summary>
        public Color RayColorNonInteractive { get; set; } = new Color(1.0f, 0.65f, 0.1f, 0.9f);

        /// <summary>Цвет при нажатии.</summary>
        public Color RayColorPressed { get; set; } = new Color(0.2f, 0.7f, 1.0f, 0.9f);

        /// <summary>Цвет при запрете взаимодействия руки с UI.</summary>
        public Color RayColorBlocked { get; set; } = new Color(1.0f, 0.15f, 0.15f, 0.9f);

        #endregion

        #region Unity

        /// <summary>Отписывает визуализацию и освобождает созданные ресурсы.</summary>
        protected override void OnDisable()
        {
            DisableRayVisualization();
            base.OnDisable();
        }

        /// <summary>Освобождает ресурсы также при уничтожении неактивного компонента.</summary>
        protected override void OnDestroy()
        {
            DisableRayVisualization();
            base.OnDestroy();
        }

        #endregion

        #region Private Methods

        /// <summary>Подключает показ к завершению штатного обновления позы аватаров.</summary>
        private void EnableRayVisualization()
        {
            if (RayVisualizationEnabled && isActiveAndEnabled)
            {
                UxrManager.AvatarsUpdated -= HandleRayAvatarsUpdated;
                UxrManager.AvatarsUpdated += HandleRayAvatarsUpdated;
            }
        }

        /// <summary>Снимает подписку и удаляет только принадлежащие визуализатору объекты.</summary>
        private void DisableRayVisualization()
        {
            UxrManager.AvatarsUpdated -= HandleRayAvatarsUpdated;
            _rayVisualization?.Dispose();
            _rayVisualization = null;
            CurrentRayLength = 0.0f;
        }

        /// <summary>Читает готовое событие ввода после трекинга и IK.</summary>
        private void HandleRayAvatarsUpdated()
        {
            // RaycastAll изменяет состояние raycaster: визуализатор его не вызывает.
            UxrPointerInputModule inputModule = UxrPointerInputModule.Instance;
            UxrPointerEventData data = IsRayInputActive(inputModule, Avatar) && Avatar == UxrAvatar.LocalAvatar
                                          ? inputModule.GetPointerEventData(this)
                                          : null;
            UpdateRayVisualization(data);
        }

        /// <summary>Исключает сохранённое событие, когда штатный модуль уже не обрабатывает пальцы.</summary>
        private static bool IsRayInputActive(UxrPointerInputModule inputModule, UxrAvatar avatar)
        {
            EventSystem eventSystem = inputModule != null ? inputModule.GetComponent<EventSystem>() : null;
            return inputModule != null && inputModule.isActiveAndEnabled && eventSystem != null
                   && eventSystem.isActiveAndEnabled && eventSystem.currentInputModule == inputModule
                   && avatar != null && avatar.RenderMode.HasFlag(UxrAvatarRenderModes.Avatar);
        }

        /// <summary>Обновляет отображение, не генерируя события ввода.</summary>
        private void UpdateRayVisualization(UxrPointerEventData data)
        {
            if (!RayVisualizationEnabled || !isActiveAndEnabled)
            {
                return;
            }

            if (_rayVisualization == null)
            {
                Shader shader = ShaderExt.UnlitTransparentColorNoDepthTest;
                if (shader == null)
                {
                    return;
                }

                _rayVisualization = new FingerTipRayVisualization(this, shader);
            }

            CurrentRayLength = _rayVisualization.Update(this, data);
        }

        /// <summary>Проверяет доступность экрана без выполнения дополнительного raycast.</summary>
        private bool IsRayCanvasAvailable(UxrCanvas canvas)
        {
            if (canvas == null || !canvas.isActiveAndEnabled || canvas.UnityCanvas == null
                || !canvas.UnityCanvas.isActiveAndEnabled || !canvas.IsCompatible(Side)
                || canvas.CanvasInteractionType != UxrInteractionType.FingerTips)
            {
                return false;
            }

            for (Transform current = canvas.transform; current != null; current = current.parent)
            {
                CanvasGroup group = current.GetComponent<CanvasGroup>();
                if (group == null)
                {
                    continue;
                }

                if (!group.interactable || !group.blocksRaycasts || group.alpha <= 0.0f)
                {
                    return false;
                }

                if (group.ignoreParentGroups)
                {
                    break;
                }
            }

            return true;
        }

        /// <summary>Возвращает штатное попадание либо ближайшую плоскость предварительного наведения.</summary>
        private bool TryGetRayTarget(UxrPointerEventData data, out bool hasHit, out Vector3 hitPosition, out float distance)
        {
            hasHit      = false;
            hitPosition = Vector3.zero;
            distance    = float.PositiveInfinity;

            if (data != null && data.pointerCurrentRaycast.gameObject != null
                && data.pointerCurrentRaycast.module is UxrFingerTipRaycaster raycaster
                && raycaster.isActiveAndEnabled && data.pointerCurrentRaycast.gameObject.activeInHierarchy
                && IsRayCanvasAvailable(data.pointerCurrentRaycast.gameObject.GetComponentInParent<UxrCanvas>()))
            {
                float hoverDistance = Mathf.Max(0.0f, raycaster.FingerTipMinHoverDistance);
                Vector3 movement = WorldPos - data.WorldPos;
                Vector3 sampledHitOffset = data.pointerCurrentRaycast.worldPosition - data.WorldPos;
                float nativeDistance = data.pointerCurrentRaycast.distance;
                // Штатная дистанция не теряет точность при округлении мировой точки контакта.
                // Учитываем движение кончика после ввода, сохраняя тот же порог канваса.
                float currentDistanceSquared = Mathf.Max(0.0f, nativeDistance * nativeDistance + movement.sqrMagnitude
                                                                - 2.0f * Vector3.Dot(sampledHitOffset, movement));
                float hoverDistanceSquared = hoverDistance * hoverDistance;
                if (currentDistanceSquared <= hoverDistanceSquared || Mathf.Approximately(currentDistanceSquared, hoverDistanceSquared))
                {
                    hasHit      = true;
                    hitPosition = data.pointerCurrentRaycast.worldPosition;
                    distance    = Vector3.Dot(hitPosition - WorldPos, WorldDir);
                    return true;
                }
            }

            // Дальность общая с fingertip-вводом канваса. Лазерные настройки не участвуют.
            // Проверка геометрии сохраняет диагностику угла и запрета взаимодействия рукой.
            foreach (UxrCanvas canvas in UxrCanvas.EnabledComponents)
            {
                if (!IsRayCanvasAvailable(canvas) || !(canvas.transform is RectTransform rect))
                {
                    continue;
                }

                float alignment = Vector3.Dot(rect.forward, WorldDir);
                if (Mathf.Abs(alignment) < 0.00001f)
                {
                    continue;
                }

                float candidateDistance = Vector3.Dot(rect.forward, rect.position - WorldPos) / alignment;
                if (candidateDistance < 0.0f || candidateDistance > Mathf.Max(0.0f, canvas.FingerTipMinHoverDistance)
                    || candidateDistance >= distance)
                {
                    continue;
                }

                Vector3 localHit = rect.InverseTransformPoint(WorldPos + WorldDir * candidateDistance);
                if (rect.rect.Contains(new Vector2(localHit.x, localHit.y)))
                {
                    distance = candidateDistance;
                }
            }

            return !float.IsPositiveInfinity(distance);
        }

        #endregion

        #region Private Types & Data

        private bool                      _rayVisualizationEnabled;
        private FingerTipRayVisualization _rayVisualization;

        /// <summary>Владелец LineRenderer, меток и материала одного кончика.</summary>
        private sealed class FingerTipRayVisualization
        {
            public bool IsVisible => _root != null && _root.activeSelf;

            public FingerTipRayVisualization(UxrFingerTip tip, Shader shader)
            {
                _root = new GameObject("FingerTip Ray");
                _root.transform.SetParent(tip.transform, false);
                _root.SetActive(false);
                _material = new Material(shader) { enableInstancing = true };
                _material.renderQueue = (int)RenderQueue.Overlay + 1;

                var rayObject = new GameObject("Ray");
                rayObject.transform.SetParent(_root.transform, false);
                _ray = rayObject.AddComponent<LineRenderer>();
                _ray.useWorldSpace = true;
                _ray.positionCount = 2;
                ConfigureRenderer(_ray);
                _origin = CreateDot("Origin");
                _hit = CreateDot("UI Hit");
            }

            public float Update(UxrFingerTip tip, UxrPointerEventData data)
            {
                bool visible = tip.TryGetRayTarget(data, out bool hasHit, out Vector3 hitPosition, out float distance);
                _root.SetActive(visible);
                if (!visible)
                {
                    return 0.0f;
                }

                bool allowed = tip.Avatar != null && tip.Avatar.AvatarController != null
                               && tip.Avatar.ControllerInput != null
                               && tip.Avatar.AvatarController.CanHandInteractWithUI(tip.Side);
                bool pressed = data != null && (data.PressedThisFrame || data.pointerPress != null);
                _material.color = !allowed ? tip.RayColorBlocked
                                          : pressed ? tip.RayColorPressed
                                                    : hasHit ? tip.RayColorInteractive : tip.RayColorNonInteractive;

                // При штатном попадании конец и метка используют одну мировую точку,
                // даже если поза пальца уже немного изменилась после обработки UI.
                Vector3 end = distance > 0.0f
                                  ? hasHit ? hitPosition : tip.WorldPos + tip.WorldDir * distance
                                  : tip.WorldPos;
                _ray.SetPosition(0, tip.WorldPos);
                _ray.SetPosition(1, end);
                float length = Vector3.Distance(tip.WorldPos, end);
                _ray.enabled = length > 0.00001f;
                _ray.startWidth = _ray.endWidth = Mathf.Max(0.0f, tip.RayWidth);
                _origin.position = tip.WorldPos;
                _hit.gameObject.SetActive(hasHit);
                if (hasHit)
                {
                    _hit.position = hitPosition;
                }

                // Размер меток задаётся в метрах и не зависит от масштаба костей.
                Vector3 scale = _root.transform.lossyScale;
                Vector3 inverseScale = new Vector3(SafeInverse(scale.x), SafeInverse(scale.y), SafeInverse(scale.z));
                _origin.localScale = inverseScale * Mathf.Max(0.0f, tip.RayOriginSize);
                _hit.localScale = inverseScale * Mathf.Max(0.0f, tip.RayHitSize);
                return length;
            }

            public void Dispose()
            {
                if (_root != null)
                {
                    _root.SetActive(false);
                    DestroyOwned(_root);
                }

                if (_material != null)
                {
                    DestroyOwned(_material);
                }
            }

            private Transform CreateDot(string name)
            {
                var dot = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                dot.name = name;
                dot.transform.SetParent(_root.transform, false);
                Collider collider = dot.GetComponent<Collider>();
                collider.enabled = false;
                DestroyOwned(collider);
                ConfigureRenderer(dot.GetComponent<Renderer>());
                return dot.transform;
            }

            private void ConfigureRenderer(Renderer renderer)
            {
                renderer.sharedMaterial = _material;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }

            private static float SafeInverse(float value) => Mathf.Abs(value) > 0.00001f ? 1.0f / Mathf.Abs(value) : 0.0f;

            private static void DestroyOwned(Object owned)
            {
                if (Application.isPlaying)
                {
                    Destroy(owned);
                }
                else
                {
                    DestroyImmediate(owned);
                }
            }

            private readonly GameObject   _root;
            private readonly LineRenderer _ray;
            private readonly Transform    _origin;
            private readonly Transform    _hit;
            private readonly Material     _material;
        }

        #endregion
    }
}

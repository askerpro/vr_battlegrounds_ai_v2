using UltimateXR.Avatar;
using UltimateXR.Core;
using UltimateXR.Haptics;
using UltimateXR.Manipulation;
using UnityEngine;
using VrBattlegrounds.Core;

namespace VrBattlegrounds.Interaction
{
    /// <summary>
    /// Позитивный отклик карманов: пока карман готов к действию руки — принять предмет из неё
    /// или отдать своё содержимое, — контроллер этой руки непрерывно слегка вибрирует. В игре
    /// карманов не видно (тело виртуальное, на своё бедро не смотрят), и игрок находит карман
    /// на ощупь. Когда готовность считается, решает <see cref="PocketReadiness" />.
    ///
    /// <para>
    /// Работает только у своего аватара (<see cref="UxrAvatarMode.Local" />): у чужих аватаров
    /// нет контроллеров. Режим проверяется каждый кадр, а не в <c>OnEnable</c>: сетевой аватар
    /// становится локальным уже после спавна. Сигнал для «принять» и «отдать» один: вопрос у
    /// игрока всегда один — «если сейчас разожму/сожму руку, карман сработает?».
    /// </para>
    /// </summary>
    [RequireComponent(typeof(UxrAvatar))]
    public sealed class PocketHaptics : MonoBehaviour
    {
        [Tooltip("Сила вибрации, 0..1. Лёгкое дрожание — чтобы не путать с отдачей и щелчками затвора.")]
        [SerializeField, Range(0f, 1f)] private float _amplitude = 0.15f;

        [Tooltip("Как часто посылать импульс, с. Каждый импульс чуть длиннее интервала — вибрация без пауз.")]
        [SerializeField, Range(0.02f, 0.5f)] private float _pulseInterval = 0.1f;

        // Импульс длиннее интервала, чтобы между импульсами не было провала. После ухода из
        // зоны вибрация сама гаснет за один импульс: Stop не зовём — он глушит и чужие
        // хаптики руки (выстрел, затвор).
        private const float PulseOverlap = 1.5f;

        private UxrAvatar       _avatar;
        private PocketReadiness _readiness;

        private readonly UxrGrabbableObjectAnchor[] _readyPocket = new UxrGrabbableObjectAnchor[2];
        private readonly float[]                    _nextPulse   = new float[2];

        /// <summary>Карман, от которого сейчас вибрирует рука <paramref name="side" />, или null.</summary>
        public UxrGrabbableObjectAnchor GetVibratingPocket(UxrHandSide side) => _readyPocket[(int)side];

        private void Awake()
        {
            _avatar = GetComponent<UxrAvatar>();
        }

        private void OnDisable()
        {
            ReleaseReadiness();
        }

        private void Update()
        {
            if (_avatar.AvatarMode != UxrAvatarMode.Local)
            {
                // Чужой аватар (их до 9 на сцене): освобождать нечего — выходим, не трогая состояние.
                if (_readiness != null) ReleaseReadiness();
                return;
            }

            _readiness ??= new PocketReadiness(_avatar);

            UpdateHand(UxrHandSide.Left);
            UpdateHand(UxrHandSide.Right);
        }

        private void UpdateHand(UxrHandSide side)
        {
            int i = (int)side;
            UxrGrabbableObjectAnchor pocket = _readiness.GetReadyPocket(side);

            if (pocket != _readyPocket[i])
            {
                GameLog.Player.Verbose(pocket != null
                                           ? $"[PocketHaptics] {side}: карман '{pocket.name}' готов — вибрация"
                                           : $"[PocketHaptics] {side}: карман не готов — вибрация гаснет", this);

                _readyPocket[i] = pocket;
                _nextPulse[i]   = 0f;
            }

            if (pocket == null || Time.unscaledTime < _nextPulse[i]) return;

            _nextPulse[i] = Time.unscaledTime + _pulseInterval;

            // Mix, а не Replace: Replace глушит остальные хаптики руки.
            _avatar.ControllerInput.SendHapticFeedback(side, 0f, _amplitude, _pulseInterval * PulseOverlap, UxrHapticMode.Mix);
        }

        private void ReleaseReadiness()
        {
            _readiness?.Dispose();
            _readiness      = null;
            _readyPocket[0] = null;
            _readyPocket[1] = null;
        }
    }
}

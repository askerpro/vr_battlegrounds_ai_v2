using UltimateXR.Avatar;
using UltimateXR.Core;
using UltimateXR.Haptics;
using UltimateXR.Manipulation;
using UnityEngine;
using VrBattlegrounds.Core;

namespace VrBattlegrounds.Interaction
{
    /// <summary>
    /// Позитивный отклик карманов: когда карман становится готов к действию руки — принять предмет
    /// из неё или отдать своё содержимое, — контроллер этой руки коротко вздрагивает один раз.
    /// В игре карманов не видно (тело виртуальное, на своё бедро не смотрят), и игрок находит
    /// карман на ощупь. Непрерывная вибрация, пока рука в кармане, раздражала и сливалась с
    /// отдачей. Когда готовность считается, решает <see cref="PocketReadiness" />, когда вздрогнуть —
    /// <see cref="PocketTap" />.
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
        [Tooltip("Сила импульса, 0..1. Средняя — заметна сквозь движение, но слабее отдачи.")]
        [SerializeField, Range(0f, 1f)] private float _tapAmplitude = 0.5f;

        [Tooltip("Длительность импульса, с.")]
        [SerializeField, Range(0.02f, 0.5f)] private float _tapSeconds = 0.3f;

        private UxrAvatar       _avatar;
        private PocketReadiness _readiness;

        private readonly UxrGrabbableObjectAnchor[] _readyPocket = new UxrGrabbableObjectAnchor[2];
        private readonly PocketTap[]                _tap         = { new PocketTap(), new PocketTap() };

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
            }

            if (!_tap[i].Update(pocket)) return;

            // Mix, а не Replace: Replace глушит остальные хаптики руки.
            _avatar.ControllerInput.SendHapticFeedback(side, 0f, _tapAmplitude, _tapSeconds, UxrHapticMode.Mix);
        }

        private void ReleaseReadiness()
        {
            _readiness?.Dispose();
            _readiness      = null;
            _readyPocket[0] = null;
            _readyPocket[1] = null;
            _tap[0].Update(null);
            _tap[1].Update(null);
        }
    }

    /// <summary>
    /// Когда вздрогнуть: один раз, когда рука нашла готовый карман (или сразу перешла к другому
    /// готовому). Пока рука остаётся у того же кармана — тишина. Ушла и вернулась — снова импульс.
    /// </summary>
    public sealed class PocketTap
    {
        private object _last;

        /// <summary>Передать готовый карман этого кадра (или null). true — пора дать импульс.</summary>
        public bool Update(object readyPocket)
        {
            bool tap = readyPocket != null && !ReferenceEquals(readyPocket, _last);
            _last = readyPocket;
            return tap;
        }
    }
}

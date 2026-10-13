using UltimateXR.Manipulation;
using UnityEngine;

namespace VrBattlegrounds.Weapons
{
    /// <summary>
    /// Поддерживающая рука не поворачивает оружие: при хвате двумя руками поза остаётся такой,
    /// как при хвате одной основной рукой.
    ///
    /// <para>
    /// UltimateXR при хвате двумя руками всегда доворачивает предмет к фактическому положению
    /// второй руки (<c>UxrGrabManager.SolveUsingLookAtAveraging</c>), настройки против этого нет.
    /// Для винтовки это и нужно — цевьё наводит ствол. У пистолета точки хвата в 2 см друг от
    /// друга, и любое дрожание второй руки крутит его на десятки градусов.
    /// </para>
    ///
    /// <para>
    /// Пока оружие держит только основная точка, поза относительно основной руки запоминается.
    /// Когда держат и другие точки, она возвращается в <c>ConstraintsApplied</c> — после решения
    /// SDK и до <c>KeepGripsInPlace</c>, так что вторая рука прилипает к рукояти, а не к контроллеру.
    /// Решение локальное на каждой машине: руки чужих аватаров приходят по сети, а поза считается
    /// из них так же, как у SDK.
    /// </para>
    ///
    /// <para>
    /// Запоминается только поза, в которой оружие догнало руку (aimlock-held-lag, 2026-10-13). У своего
    /// аватара SDK сглаживает предмет в руке (сопротивление, SDK-патч 73), плавно ведёт его при хвате и
    /// подбрасывает отдачей — в эти кадры оружие отстаёт от трекинга. Раньше такая поза запоминалась
    /// как есть, и если поддержку брали на ходу, отставание оставалось навсегда (у PPK 33,9 мм / 2,2°,
    /// bots-fix 4442). Теперь кандидат из <c>ConstraintsApplied</c> принимается в <c>ConstraintsFinished</c>
    /// (после <c>KeepGripsInPlace</c>), только если граббер основной руки совпал со своим положением по
    /// трекингу: <c>KeepGripsInPlace</c> двигает кость кисти, а граббер — её дочерний объект
    /// (<c>UxrHandIntegration.Awake</c>), поэтому он встаёт на снап оружия, и расхождение и есть
    /// отставание предмета. В покое оно равно нулю, у ботов и чужих аватаров сглаживания нет.
    /// </para>
    /// </summary>
    [RequireComponent(typeof(UxrGrabbableObject))]
    public sealed class MainGripAimLock : MonoBehaviour
    {
        /// <summary>
        /// Допуск «оружие догнало руку», метры. Отставание только по повороту почти не смещает снап
        /// (2,2° на плече ~5 см ≈ 2 мм), поэтому допуск меньше этого.
        /// </summary>
        private const float SettledGap = 0.0015f;

        [Tooltip("Индекс точки хвата, которая одна задаёт направление оружия.")]
        [SerializeField] private int _mainGrabPoint;

        private UxrGrabbableObject _grabbable;
        private bool _hasPose;
        private Vector3 _localPosition;
        private Quaternion _localRotation;

        // Поза одной руки этого кадра — ждёт проверки в ConstraintsFinished.
        private bool _hasCandidate;
        private Vector3 _candidatePosition;
        private Quaternion _candidateRotation;

        private void Awake()
        {
            _grabbable = GetComponent<UxrGrabbableObject>();
        }

        private void OnEnable()
        {
            _grabbable.ConstraintsApplied += GrabbableObject_ConstraintsApplied;
            _grabbable.ConstraintsFinished += GrabbableObject_ConstraintsFinished;
        }

        private void OnDisable()
        {
            _grabbable.ConstraintsApplied -= GrabbableObject_ConstraintsApplied;
            _grabbable.ConstraintsFinished -= GrabbableObject_ConstraintsFinished;
            _hasPose = false;
            _hasCandidate = false;
        }

        private void GrabbableObject_ConstraintsApplied(object sender, UxrApplyConstraintsEventArgs e)
        {
            UxrGrabManager manager = UxrGrabManager.Instance;
            _hasCandidate = false;

            if (!manager.GetGrabbingHand(_grabbable, _mainGrabPoint, out UxrGrabber mainHand))
            {
                _hasPose = false;
                return;
            }

            Transform hand = mainHand.transform;

            // Зависимые захваты (затвор) не считаются: они двигают свою деталь, а не всё оружие.
            if (manager.GetHandsGrabbingCount(_grabbable, false) <= 1)
            {
                // Граббер здесь ещё в позе трекинга: KeepGripsInPlace будет позже.
                _candidatePosition = hand.InverseTransformPoint(transform.position);
                _candidateRotation = Quaternion.Inverse(hand.rotation) * transform.rotation;
                _hasCandidate = true;
                return;
            }

            if (!_hasPose)
            {
                // Вторую руку взяли раньше, чем оружие хоть раз догнало основную, — берём текущую позу:
                // лучше зафиксировать её, чем крутить оружие.
                _localPosition = hand.InverseTransformPoint(transform.position);
                _localRotation = Quaternion.Inverse(hand.rotation) * transform.rotation;
                _hasPose = true;
                return;
            }

            transform.SetPositionAndRotation(hand.TransformPoint(_localPosition), hand.rotation * _localRotation);
        }

        private void GrabbableObject_ConstraintsFinished(object sender, UxrApplyConstraintsEventArgs e)
        {
            if (!_hasCandidate)
            {
                return;
            }

            _hasCandidate = false;

            if (!UxrGrabManager.Instance.GetGrabbingHand(_grabbable, _mainGrabPoint, out UxrGrabber mainHand))
            {
                return;
            }

            // После KeepGripsInPlace граббер стоит на снапе оружия: расхождение с трекингом — отставание.
            if ((mainHand.transform.position - mainHand.UnprocessedGrabberPosition).sqrMagnitude > SettledGap * SettledGap)
            {
                return;
            }

            _localPosition = _candidatePosition;
            _localRotation = _candidateRotation;
            _hasPose = true;
        }
    }
}

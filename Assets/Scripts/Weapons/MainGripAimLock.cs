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
    /// </summary>
    [RequireComponent(typeof(UxrGrabbableObject))]
    public sealed class MainGripAimLock : MonoBehaviour
    {
        [Tooltip("Индекс точки хвата, которая одна задаёт направление оружия.")]
        [SerializeField] private int _mainGrabPoint;

        private UxrGrabbableObject _grabbable;
        private bool _hasPose;
        private Vector3 _localPosition;
        private Quaternion _localRotation;

        private void Awake()
        {
            _grabbable = GetComponent<UxrGrabbableObject>();
        }

        private void OnEnable()
        {
            _grabbable.ConstraintsApplied += GrabbableObject_ConstraintsApplied;
        }

        private void OnDisable()
        {
            _grabbable.ConstraintsApplied -= GrabbableObject_ConstraintsApplied;
            _hasPose = false;
        }

        private void GrabbableObject_ConstraintsApplied(object sender, UxrApplyConstraintsEventArgs e)
        {
            UxrGrabManager manager = UxrGrabManager.Instance;

            if (!manager.GetGrabbingHand(_grabbable, _mainGrabPoint, out UxrGrabber mainHand))
            {
                _hasPose = false;
                return;
            }

            Transform hand = mainHand.transform;

            // Зависимые захваты (затвор) не считаются: они двигают свою деталь, а не всё оружие.
            if (manager.GetHandsGrabbingCount(_grabbable, false) <= 1 || !_hasPose)
            {
                // Вторую руку добавили раньше, чем успели запомнить позу, — берём текущую: лучше
                // зафиксировать её, чем крутить оружие.
                _localPosition = hand.InverseTransformPoint(transform.position);
                _localRotation = Quaternion.Inverse(hand.rotation) * transform.rotation;
                _hasPose = true;
                return;
            }

            transform.SetPositionAndRotation(hand.TransformPoint(_localPosition), hand.rotation * _localRotation);
        }
    }
}

using UltimateXR.Manipulation;
using UltimateXR.Mechanics.Weapons;
using UnityEngine;

namespace VrBattlegrounds.Weapons
{
    /// <summary>
    /// Накопленная отдача оружия (T-38): очередь уводит ствол вверх и вбок по картине <see cref="RecoilPattern"/>,
    /// пауза возвращает. Поверх отдачи UltimateXR (короткий одинаковый подброс на каждый выстрел), не вместо неё.
    ///
    /// <para>
    /// Поворот ставится в <c>ConstraintsApplied</c> главного граббабла — там же, где отдача SDK: поза из рук
    /// пересчитывается каждый кадр, поворот не копится в трансформе, а <c>KeepGripsInPlace</c> после события
    /// держит руки на оружии. Ствол повёрнут до следующего выстрела — снаряд летит по нему.
    /// </para>
    ///
    /// <para>
    /// Порядок: <see cref="MainGripAimLock"/> в том же событии возвращает позу основной руки и затёр бы подброс,
    /// поэтому при каждом хвате обработчик переподписывается последним.
    /// </para>
    ///
    /// <para>
    /// Сеть: <c>ProjectileShot</c> поднимается только у стрелка (патч SDK 23), поэтому копится отдача только у него;
    /// снаряд вылетает по уже повёрнутому стволу, и его поворот уходит всем значением в синхронизированном
    /// <c>Shoot</c> — пули у наблюдателя летят туда же. Подброс ствола в чужих руках наблюдатель не видит (поза
    /// оружия у него — из рук); это только визуальное расхождение. Отдельной синхронизации нет.
    /// </para>
    ///
    /// Параметры — из <c>WeaponInfo</c> командой <c>Tools/VR Battlegrounds/Gameplay/Apply Weapon Balance</c>.
    /// </summary>
    [RequireComponent(typeof(UxrFirearmWeapon))]
    [RequireComponent(typeof(UxrGrabbableObject))]
    public sealed class RecoilAccumulator : MonoBehaviour
    {
        [SerializeField] private RecoilPattern _pattern = new RecoilPattern();

        [Tooltip("Оси и точка поворота отдачи (RecoilSourceAxes у рукояти). Пусто — корень оружия.")]
        [SerializeField] private Transform _axes;

        private UxrFirearmWeapon _weapon;
        private UxrGrabbableObject _grabbable;

        public RecoilPattern Pattern => _pattern;

        private void Awake()
        {
            _weapon = GetComponent<UxrFirearmWeapon>();
            _grabbable = GetComponent<UxrGrabbableObject>();
        }

        private void OnEnable()
        {
            _weapon.ProjectileShot += OnProjectileShot;
            _grabbable.Grabbed += OnGrabbed;
            _grabbable.ConstraintsApplied += OnConstraintsApplied;
        }

        private void OnDisable()
        {
            _weapon.ProjectileShot -= OnProjectileShot;
            _grabbable.Grabbed -= OnGrabbed;
            _grabbable.ConstraintsApplied -= OnConstraintsApplied;
            _pattern.Reset();
        }

        private void Update() => _pattern.Tick(Time.deltaTime);

        private void OnProjectileShot(int triggerIndex) => _pattern.Shot();

        private void OnGrabbed(object sender, UxrManipulationEventArgs e)
        {
            // В конец списка обработчиков — после MainGripAimLock и отдачи SDK.
            _grabbable.ConstraintsApplied -= OnConstraintsApplied;
            _grabbable.ConstraintsApplied += OnConstraintsApplied;
        }

        private void OnConstraintsApplied(object sender, UxrApplyConstraintsEventArgs e)
        {
            int hands = UxrGrabManager.Instance.GetGrabbingHandCount(_grabbable);
            if (hands == 0) return;

            bool oneHand = hands == 1;
            float pitch = _pattern.Pitch(oneHand);
            float yaw = _pattern.Yaw(oneHand);
            if (pitch == 0f && yaw == 0f) return;

            Transform root = _grabbable.transform;
            Transform axes = _axes != null ? _axes : root;
            root.RotateAround(axes.position, -axes.right, pitch);
            root.RotateAround(axes.position, axes.up, yaw);
        }
    }
}

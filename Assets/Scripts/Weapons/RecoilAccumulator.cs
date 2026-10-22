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
    /// <para>
    /// Шарнир отдачи — запястье руки, держащей основную рукоять (решение пользователя 2026-10-22): кисть
    /// поворачивается в своём суставе, предплечье остаётся вдоль контроллера. Перед ограничениями хвата точка
    /// <see cref="_axes" /> встаёт в кость запястья (<see cref="UxrGrabber.HandBone" />). Её же как <c>_recoilAxes</c>
    /// берёт подброс SDK. Ориентация осей — от оружия. Без руки с костью запястья — точка сборки (ладонь пака).
    /// </para>
    ///
    /// Параметры — из <c>WeaponInfo</c> командой <c>Tools/VR Battlegrounds/Gameplay/Apply Weapon Balance</c>.
    /// </summary>
    [RequireComponent(typeof(UxrFirearmWeapon))]
    [RequireComponent(typeof(UxrGrabbableObject))]
    public sealed class RecoilAccumulator : MonoBehaviour
    {
        [SerializeField] private RecoilPattern _pattern = new RecoilPattern();

        [Tooltip("Оси отдачи (RecoilSourceAxes). Точка поворота в руках — запястье руки на основной рукояти; " +
                 "позиция этого объекта — запасная, без руки. Пусто — корень оружия.")]
        [SerializeField] private Transform _axes;

        private UxrFirearmWeapon _weapon;
        private UxrGrabbableObject _grabbable;
        private Vector3 _authoredPivot;

        public RecoilPattern Pattern => _pattern;

        private void Awake()
        {
            _weapon = GetComponent<UxrFirearmWeapon>();
            _grabbable = GetComponent<UxrGrabbableObject>();
            if (_axes != null) _authoredPivot = _axes.localPosition;
        }

        private void OnEnable()
        {
            _weapon.ProjectileShot += OnProjectileShot;
            _grabbable.Grabbed += OnGrabbed;
            _grabbable.Released += OnReleased;
            _grabbable.ConstraintsApplying += OnConstraintsApplying;
            _grabbable.ConstraintsApplied += OnConstraintsApplied;
        }

        private void OnDisable()
        {
            _weapon.ProjectileShot -= OnProjectileShot;
            _grabbable.Grabbed -= OnGrabbed;
            _grabbable.Released -= OnReleased;
            _grabbable.ConstraintsApplying -= OnConstraintsApplying;
            _grabbable.ConstraintsApplied -= OnConstraintsApplied;
            _pattern.Reset();
            if (_axes != null) _axes.localPosition = _authoredPivot;
        }

        /// <summary>Шарнир — в запястье руки на основной рукояти, до подброса SDK и накопленной отдачи.</summary>
        private void OnConstraintsApplying(object sender, UxrApplyConstraintsEventArgs e)
        {
            if (_axes == null) return;
            UxrGrabManager grabs = UxrGrabManager.Instance;
            UxrGrabber grabber = null;
            if (!grabs.GetGrabbingHand(_grabbable, 0, out grabber))
                for (int point = 1; point < _grabbable.GrabPointCount && grabber == null; point++)
                    grabs.GetGrabbingHand(_grabbable, point, out grabber);
            PlacePivot(grabber != null && grabber.Avatar != null ? grabber.HandBone : null);
        }

        /// <summary>Шарнир в запястье <paramref name="wrist" />; без запястья — точка сборки. Оси не поворачиваются.</summary>
        private void PlacePivot(Transform wrist)
        {
            if (wrist != null) _axes.position = wrist.position;
            else _axes.localPosition = _authoredPivot;
        }

        private void OnReleased(object sender, UxrManipulationEventArgs e)
        {
            if (_axes != null && UxrGrabManager.Instance.GetGrabbingHandCount(_grabbable) == 0) _axes.localPosition = _authoredPivot;
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

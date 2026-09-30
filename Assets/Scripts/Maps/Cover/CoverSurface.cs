using UnityEngine;

namespace VrBattlegrounds.Maps
{
    /// <summary>
    /// Класс укрытия объекта и всех коллайдеров под ним (ищется от коллайдера вверх, ближайший побеждает).
    ///
    /// <para>
    /// Класс руками не ставится: его пишет <c>Tools/VR Battlegrounds/Gameplay/Apply Cover Classes</c> по суффиксу
    /// имени (<see cref="CoverClassRules"/>), расхождение ловит <c>CoverClassTests</c>. Руками настраивается только
    /// <see cref="PenetrationModifier"/> у Soft.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CoverSurface : MonoBehaviour
    {
        [Tooltip("Класс укрытия. Пишет Apply Cover Classes по суффиксу имени _Hard/_Soft/_Visual — руками не менять.")]
        [SerializeField] private CoverClass _class = CoverClass.Soft;

        [Tooltip("Только Soft: penetrationmodifier материала по Counter-Strike (больше — легче пробить). " +
                 "Дерево и картон насквозь — 3 (правило CS «вход и выход один материал»), пластик — 2, " +
                 "остальное — из surfaceproperties CS2. Меньше 0.1 — не пробивается.")]
        [SerializeField] [Min(0f)] private float _penetrationModifier = 3f;

        public CoverClass Class
        {
            get => _class;
            set => _class = value;
        }

        public float PenetrationModifier
        {
            get => _penetrationModifier;
            set => _penetrationModifier = Mathf.Max(0f, value);
        }

        /// <summary>Разметка коллайдера: ближайший <see cref="CoverSurface"/> вверх по иерархии или null (Hard).</summary>
        public static CoverSurface Of(Collider collider) => collider != null ? collider.GetComponentInParent<CoverSurface>() : null;
    }
}

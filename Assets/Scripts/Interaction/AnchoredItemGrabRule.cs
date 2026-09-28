using UltimateXR.Manipulation;

namespace VrBattlegrounds.Interaction
{
    /// <summary>
    /// Предмет, вставленный в якорь другого предмета (магазин в оружии), рукой не берётся вовсе:
    /// ни из оружия в кобуре, на стене арсенала или на полу, ни из оружия в руке. Магазин выходит
    /// из оружия только кнопкой выброса (A/X, <c>VrBattlegrounds.Weapons.MagazineEject</c>).
    ///
    /// <para>
    /// Без правила магазин перехватывал хват: в рукояти пистолета он ближе к ладони, чем прокси
    /// кобуры и сама рукоять, — игрок тянулся за пистолетом и доставал магазин; вторая рука,
    /// положенная на пистолет для поддержки, брала магазин вместо точки поддержки. Приоритет
    /// (<c>UxrGrabbableObject.Priority</c>) эту задачу не решает: у лежащего оружия прокси нет, а
    /// приоритет поддержки против магазина пришлось бы выставлять на каждом оружии.
    /// </para>
    ///
    /// <para>
    /// Хозяин ищется по иерархии, а не по <c>CurrentAnchor</c>: вставленный предмет висит под
    /// якорем, и так же лежит в префабе — до того, как SDK зарегистрирует размещение. Якоря
    /// карманов и стены арсенала не внутри хватаемого предмета — их содержимое правило не
    /// трогает. Ср. <see cref="GrabOnlyWhenParentHeld" /> — детали (затвор, помпа), которые не
    /// вставляются, а являются частью предмета, и берутся, когда предмет в руке.
    /// </para>
    /// </summary>
    public static class AnchoredItemGrabRule
    {
        public static bool AllowsGrab(UxrGrabber grabber, UxrGrabbableObject grabbable)
        {
            // Зовётся на каждую точку каждого предмета каждый кадр — хозяин берётся из покадрового кэша.
            return GrabbableHierarchyCache.GetAnchorHost(grabbable) == null;
        }

        /// <summary>Предмет, в якорь которого вставлен <paramref name="grabbable" />; null — не вставлен ни в какой.</summary>
        public static UxrGrabbableObject GetHost(UxrGrabbableObject grabbable)
        {
            if (grabbable == null || grabbable.transform.parent == null)
            {
                return null;
            }

            UxrGrabbableObjectAnchor anchor = grabbable.transform.parent.GetComponentInParent<UxrGrabbableObjectAnchor>(true);
            return anchor != null ? anchor.GetComponentInParent<UxrGrabbableObject>(true) : null;
        }
    }
}

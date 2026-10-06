using UltimateXR.Manipulation;
using UltimateXR.Mechanics.Weapons;
using UnityEngine;
using VrBattlegrounds.Network;

namespace VrBattlegrounds.Weapons
{
    /// <summary>
    /// Выброс магазина из оружия: магазин выходит из гнезда и падает. Вызывается кнопкой руки,
    /// держащей оружие (<see cref="MagazineEjectInput" />).
    ///
    /// <para>
    /// Магазин ищется по гнёздам оружия — якорь, в котором лежит <see cref="UxrFirearmMag" />, — а не
    /// по списку триггеров <c>UxrFirearmWeapon</c>: список в SDK приватный. Выемка — штатный
    /// <c>UxrGrabManager.RemoveObjectFromAnchor</c>: он синхронизируется каналом состояния UltimateXR
    /// (<c>NetworkStateRelay</c>), и магазин выпадает у всех игроков. Оружие само отмечает выемку
    /// (<c>MagTarget_Removed</c>: коллайдер магазина включается, перезарядка сбрасывается).
    /// </para>
    ///
    /// <para>
    /// Магазин отцепляется от оружия (<c>unparent</c>), иначе остался бы дочерним объектом ствола в
    /// руке. Столкновения с корпусом <c>AnchoredItemCollisionIgnore</c> возвращает сразу после выемки —
    /// проверено: у <c>Gun_real</c> и M16 в руке магазин всё равно падает вниз, а не вылетает вбок
    /// (оружие в руке kinematic).
    /// </para>
    /// </summary>
    public static class MagazineEject
    {
        /// <summary>
        /// Выбрасывает магазин оружия, частью которого является <paramref name="held" /> (само оружие
        /// или его деталь). False — это не оружие или магазина в нём нет.
        /// </summary>
        public static bool TryEject(UxrGrabbableObject held, out UxrGrabbableObject magazine)
        {
            magazine = null;
            if (!StateEventAuthority.IsAuthorOfItem(held)) return false;
            magazine = FindMagazine(held);
            if (magazine == null) return false;

            UxrGrabManager.Instance.RemoveObjectFromAnchor(magazine, true, true);
            return true;
        }

        /// <summary>Магазин, вставленный в оружие, которому принадлежит <paramref name="held" />; null — нет.</summary>
        public static UxrGrabbableObject FindMagazine(UxrGrabbableObject held)
        {
            UxrFirearmWeapon weapon = held != null ? held.GetComponentInParent<UxrFirearmWeapon>(true) : null;
            if (weapon == null) return null;

            foreach (UxrGrabbableObjectAnchor anchor in weapon.GetComponentsInChildren<UxrGrabbableObjectAnchor>(true))
            {
                UxrGrabbableObject placed = anchor.CurrentPlacedObject;

                // Гнездо вложенного оружия (если когда-то появится) принадлежит не этому оружию.
                if (placed != null && placed.GetComponent<UxrFirearmMag>() != null &&
                    !placed.GetComponent<UxrFirearmMag>().IsFixedAmmoStore && anchor.GetComponentInParent<UxrFirearmWeapon>(true) == weapon)
                {
                    return placed;
                }
            }

            return null;
        }
    }
}

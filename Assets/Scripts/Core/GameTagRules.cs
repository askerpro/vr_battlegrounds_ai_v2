using UltimateXR.Avatar;
using UltimateXR.Manipulation;
using UltimateXR.Mechanics.Weapons;
using UnityEngine;
using VrBattlegrounds.Arsenal;
using VrBattlegrounds.Maps;
using VrBattlegrounds.PhysicalSpaceUtils;
using VrBattlegrounds.Weapons;

namespace VrBattlegrounds.Core
{
    /// <summary>
    /// Единственное место, где решается, какой тег из <see cref="GameTags" /> положен объекту.
    ///
    /// <para>
    /// Правда о категории — компоненты, тег лишь её дешёвая копия для <c>CompareTag</c>
    /// и <c>FindGameObjectsWithTag</c>. Поэтому правило читает только компоненты, и по нему
    /// работают и инструмент разметки, и тест согласованности. Новая категория добавляется
    /// сюда и в <see cref="GameTags" />, больше никуда.
    /// </para>
    /// </summary>
    public static class GameTagRules
    {
        /// <summary>Значение «игрового тега не положено».</summary>
        public const string Untagged = "Untagged";

        /// <summary>
        /// Тег, положенный объекту: сущность — по компонентам на самом объекте, геометрия —
        /// по коллайдеру вне сущностей, остальное — <see cref="Untagged" />.
        /// </summary>
        public static string ExpectedTag(GameObject go)
        {
            if (IsPlayerRoot(go))    return GameTags.Player;
            if (IsWeaponRoot(go))    return GameTags.Weapon;
            if (IsMagazineRoot(go))  return GameTags.Magazine;
            if (IsSpawnZoneRoot(go)) return GameTags.SpawnZone;
            if (IsArsenalRoot(go))   return GameTags.Arsenal;
            if (IsEnvironmentGeometry(go)) return GameTags.Environment;
            return Untagged;
        }

        public static bool IsPlayerRoot(GameObject go)    => go.GetComponent<UxrAvatar>() != null;
        public static bool IsWeaponRoot(GameObject go)    => go.GetComponent<UxrFirearmWeapon>() != null || go.GetComponent<UxrGrenadeWeapon>() != null;
        // Боеприпас: магазин (в том числе встроенный запас оружия) или одиночный патрон ручного заряжания.
        public static bool IsMagazineRoot(GameObject go)  => go.GetComponent<UxrFirearmMag>() != null || go.GetComponent<Cartridge>() != null;
        public static bool IsSpawnZoneRoot(GameObject go) => go.GetComponent<TeamSpawnZone>() != null;
        public static bool IsArsenalRoot(GameObject go)   => go.GetComponent<ArsenalWallController>() != null || go.GetComponent<ArsenalSlotController>() != null;

        /// <summary>
        /// Неподвижная геометрия: на объекте есть не-trigger коллайдер, и ни он, ни его
        /// предки не являются чем-то подвижным или интерактивным. Иначе коллайдер
        /// принадлежит сущности — оружию, телу игрока, планшету меню, — и тег ему не нужен:
        /// сущность опознаётся по корню.
        /// </summary>
        public static bool IsEnvironmentGeometry(GameObject go)
        {
            if (!HasSolidCollider(go)) return false;

            for (Transform t = go.transform; t != null; t = t.parent)
                if (IsNonEnvironmentOwner(t)) return false;

            return true;
        }

        private static bool HasSolidCollider(GameObject go)
        {
            foreach (Collider collider in go.GetComponents<Collider>())
                if (!collider.isTrigger) return true;
            return false;
        }

        private static bool IsNonEnvironmentOwner(Transform t) =>
            t.GetComponent<VrBattlegrounds.LevelDesign.PhysicalArenaLayout>() != null ||
            t.GetComponent<Rigidbody>()                != null ||
            t.GetComponent<UxrAvatar>()                != null ||
            t.GetComponent<UxrGrabbableObject>()       != null ||
            t.GetComponent<UxrGrabbableObjectAnchor>() != null ||
            t.GetComponent<TeamSpawnZone>()            != null ||
            t.GetComponent<PhysicalSpaceAnchor>()      != null ||
            t.GetComponent<Canvas>()                   != null;
    }
}

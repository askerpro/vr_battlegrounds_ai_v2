using UnityEngine;

namespace VrBattlegrounds.Player
{
    /// <summary>Зона попадания — часть тела, в которую попала пуля (T-36); под урон по зоне.</summary>
    public enum HitZone
    {
        Head,
        Torso,
        Arm,
        Leg,
    }

    /// <summary>
    /// Хитбокс части тела игрока (T-36): сплошной коллайдер на кости, слой <c>Hitbox</c>
    /// (<see cref="VrBattlegrounds.Core.HitLayers"/>). Генерирует сборщик
    /// (<c>Tools/VR Battlegrounds/Avatars/Build Hitboxes</c>) по скелету UltimateXR — руками не
    /// ставится. Попадание (<c>UxrDamageEventArgs.RaycastHit.collider</c>) → <see cref="TryGetPart"/>
    /// → часть тела; множители урона по зонам — отдельная задача.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class Hitbox : MonoBehaviour
    {
        [SerializeField] private HitZone _part;

        public HitZone Part => _part;

        /// <summary>Часть тела по коллайдеру попадания. false — не хитбокс игрока.</summary>
        public static bool TryGetPart(Collider collider, out HitZone part)
        {
            Hitbox hitbox = collider != null ? collider.GetComponent<Hitbox>() : null;
            part = hitbox != null ? hitbox.Part : default;
            return hitbox != null;
        }
    }
}

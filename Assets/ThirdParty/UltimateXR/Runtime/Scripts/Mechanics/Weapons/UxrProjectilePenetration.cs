// VR Battlegrounds patch (Патч 32, Docs/UltimateXR/sdk-patches.md): прострел стен.
using UnityEngine;

namespace UltimateXR.Mechanics.Weapons
{
    /// <summary>
    ///     Что пуля делает с не-актором, в который попала (патч 32).
    /// </summary>
    public enum UxrPenetrationKind
    {
        /// <summary>Останавливается — поведение SDK без патча.</summary>
        Stop,

        /// <summary>Пробивает: эффекты попадания на входе, декаль на выходе, летит дальше с меньшим уроном.</summary>
        Penetrate,

        /// <summary>Пролетает, как будто препятствия нет: без эффектов попадания и без потери урона.</summary>
        PassThrough
    }

    /// <summary>
    ///     Решение по пробитию одного препятствия.
    /// </summary>
    public struct UxrPenetrationResult
    {
        public UxrPenetrationKind Kind;

        /// <summary>Откуда пуля летит дальше (для <see cref="UxrPenetrationKind.Stop" /> не используется).</summary>
        public Vector3 ExitPoint;

        /// <summary>Попадание в выходную грань — для декали на выходе. Только у <see cref="UxrPenetrationKind.Penetrate" />.</summary>
        public RaycastHit ExitHit;

        /// <summary>Накопленный множитель урона пули после этого препятствия.</summary>
        public float DamageMultiplier;

        public static UxrPenetrationResult Stop => new UxrPenetrationResult { Kind = UxrPenetrationKind.Stop };
    }

    /// <summary>
    ///     Решает, пробивает ли пуля препятствие.
    /// </summary>
    /// <param name="hit">Попадание во входную грань</param>
    /// <param name="direction">Направление полёта пули</param>
    /// <param name="shot">Тип выстрела (сила пробития — <see cref="UxrShotDescriptor.PenetrationPower" />)</param>
    /// <param name="penetrations">Сколько препятствий пуля уже пробила</param>
    /// <param name="currentDamage">Урон пули у препятствия: спад по дистанции × накопленный множитель</param>
    /// <param name="damageMultiplier">Текущий множитель урона пули</param>
    public delegate UxrPenetrationResult UxrProjectilePenetrationHandler(RaycastHit hit, Vector3 direction, UxrShotDescriptor shot, int penetrations, float currentDamage, float damageMultiplier);
}

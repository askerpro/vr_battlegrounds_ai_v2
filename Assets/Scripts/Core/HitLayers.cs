using UnityEngine;

namespace VrBattlegrounds.Core
{
    /// <summary>
    /// Во что попадает пуля (T-36): статичный мир и мишени (<c>Default</c>), пол (<c>Ground</c>) и
    /// хитбоксы игроков (<see cref="HitboxLayerName"/>). Одна маска на всё оружие — её прописывает
    /// сборщик хитбоксов в каждый <c>UxrShotDescriptor</c> и сверяет <c>HitboxTests</c>.
    ///
    /// <para>
    /// <b>Почему у хитбоксов свой слой.</b> Раньше хитбоксы лежали на <c>Default</c>, как и геометрия
    /// карт, и их нельзя было отделить от мира слоями: трупу (T-35) приходилось выключать
    /// столкновения с телами попарно, а маски оружия подбирались по факту (у кого <c>Default</c>, у кого
    /// <c>Default|Ground</c>). Вне маски осознанно: <c>Corpse</c> (труп не укрытие), <c>Player</c>,
    /// <c>SpawnZone</c>, <c>LocalHead</c>, <c>UI</c>, <c>Ignore Raycast</c>.
    /// </para>
    /// </summary>
    public static class HitLayers
    {
        public const string HitboxLayerName = "Hitbox";

        /// <summary>Слои, которые видит пуля.</summary>
        public static readonly string[] ProjectileLayers = { "Default", "Ground", HitboxLayerName };

        public static int HitboxLayer => LayerMask.NameToLayer(HitboxLayerName);

        public static int ProjectileMask => LayerMask.GetMask(ProjectileLayers);

        /// <summary>
        /// Хитбоксы нужны только лучу пули (лучи матрицу столкновений не учитывают) — физически они
        /// ни с чем не сталкиваются: тело игрока не толкает оружие у руки, магазины, выпавшее и
        /// трупы. В VR тело ведёт трекинг, а не физика — толкание было бы только помехой хвату.
        /// Прописывается в настройки проекта сборщиком хитбоксов (только редактор).
        /// </summary>
        public static void ConfigureLayerCollisions()
        {
            int hitbox = HitboxLayer;
            if (hitbox < 0) return;
            for (int layer = 0; layer < 32; layer++) Physics.IgnoreLayerCollision(hitbox, layer, true);
        }
    }
}

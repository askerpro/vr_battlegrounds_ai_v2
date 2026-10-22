using UnityEngine;

namespace VrBattlegrounds.Editor.Gameplay
{
    /// <summary>Область реальной геометрии в осях корня оружия, выбранная для отдельного действия.</summary>
    public sealed class WeaponVisualRegion
    {
        public Transform Source;
        public Bounds Bounds;
        public string Name;

        /// <summary>
        /// Треугольники меша <see cref="Source" /> (номер тройки в <c>sharedMesh.triangles</c>) — область целиком, без
        /// обрезки по <see cref="Bounds" />; null — обрезка коробкой <see cref="Bounds" />.
        /// </summary>
        public int[] Triangles;

        /// <summary>Затравка: ближайшая к нейтральной точке хвата точка поверхности, оси корня.</summary>
        public Vector3 Seed;

        /// <summary>Остров меша с затравкой — для решения «остров/обрезка» и отчёта preflight.</summary>
        internal MeshIslands.Island SeedIsland;

        /// <summary>Почему остров не взят целиком; null — взят или не рассматривался.</summary>
        public string IslandRejection;
    }

    /// <summary>Контакт ручки отделён от источника хода. Области сохраняются выпеченными мешами.</summary>
    public sealed class WeaponInteractionRecipe
    {
        public string Name;
        public string AssetFolder;
        public Transform ActionGripPart;
        public Vector3? ActionContact;
        public WeaponVisualRegion Primary;
        public WeaponVisualRegion Support;
        public WeaponVisualRegion Action;
        public WeaponVisualRegion Insertion;
    }
}

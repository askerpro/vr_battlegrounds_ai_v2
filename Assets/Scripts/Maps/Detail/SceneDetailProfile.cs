using System;
using UnityEngine;

namespace VrBattlegrounds.Maps.Detail
{
    /// <summary>
    /// Профиль детализации сцены (задача scene-detail, решение пользователя 2026-10-14): единственный
    /// источник параметров. Планы — по горизонтальной дистанции от края пола арены
    /// (<see cref="LevelDesign.PhysicalArenaDefinition" />) до объекта: ближний (не трогается),
    /// средний с <see cref="midStart" />, дальний с <see cref="farStart" />. Правило плана —
    /// <see cref="PlaneRule" />. Применяет <see cref="SceneDetailApplier" />; ассет лежит рядом со сценой
    /// (<c>Assets/Scenes/&lt;Сцена&gt;/&lt;Сцена&gt;DetailProfile.asset</c>).
    /// </summary>
    [CreateAssetMenu(menuName = "VR Battlegrounds/Detail/Профиль детализации сцены", fileName = "DetailProfile")]
    public sealed class SceneDetailProfile : ScriptableObject
    {
        /// <summary>Порог отсечения мелочи не больше 0,6 м (решение пользователя 2026-10-13).</summary>
        public const float MaxCullSize = .6f;

        /// <summary>Пропуск уровней LOD, при котором остаётся только последний.</summary>
        public const int WorstLod = 9;

        /// <summary>Наибольшее смещение mip текстур.</summary>
        public const int MaxTextureBias = 4;

        [Serializable]
        public struct PlaneRule
        {
            [Tooltip("Сколько первых уровней LOD пропустить (0 — как задано, 9 — только последний). Порог отсечения сохраняется.")]
            [Range(0, WorstLod)] public int lodSkip;

            [Tooltip("Сколько верхних mip-уровней текстур плана отбросить (текстуры только этого плана).")]
            [Range(0, MaxTextureBias)] public int textureBias;

            [Tooltip("Объекты мельче этого размера (диагональ bounds, м) не рисуются.")]
            [Range(0f, MaxCullSize)] public float cullSize;

            public PlaneRule Clamped() => new PlaneRule
            {
                lodSkip = Mathf.Clamp(lodSkip, 0, WorstLod),
                textureBias = Mathf.Clamp(textureBias, 0, MaxTextureBias),
                cullSize = Mathf.Clamp(cullSize, 0f, MaxCullSize),
            };
        }

        [Tooltip("Средний план начинается с этой дистанции от края пола арены, м.")]
        public float midStart = 10f;

        [Tooltip("Дальний план начинается с этой дистанции от края пола арены, м (не ближе среднего).")]
        public float farStart = 25f;

        public PlaneRule mid;
        public PlaneRule far = new PlaneRule { lodSkip = WorstLod, textureBias = 2, cullSize = MaxCullSize };

        [Tooltip("Пути от корня сцены, которые профиль не трогает (главные объекты: стрельбище, арсенал).")]
        public string[] exemptPaths = Array.Empty<string>();

        [Tooltip("Папки текстур, которыми владеет сцена: только им назначаются группы mipmap limit.")]
        public string[] textureFolders = Array.Empty<string>();
    }
}

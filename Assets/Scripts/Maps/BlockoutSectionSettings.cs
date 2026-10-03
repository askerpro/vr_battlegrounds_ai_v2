using System;
using UnityEngine;
using VrBattlegrounds.Maps;

namespace VrBattlegrounds.LevelDesign
{
    public enum BlockoutCoverSummary { Hard, Soft, Visual, Mixed }

    /// <summary>Постоянный рецепт секции; отсутствие её геометрии не удаляет настройки.</summary>
    [Serializable]
    public sealed class BlockoutSectionSettings
    {
        public CoverClass material;
        public BlockoutOpeningSettings openings = BlockoutOpeningSettings.Default;
        public BlockoutSectionSettings Copy() => new BlockoutSectionSettings { material = material, openings = openings };

        public static float Bottom(int index) => index == 0 ? 0f : index == 1 ? 1.2f : 1.6f;
        public static float Top(int index) => index == 0 ? 1.2f : index == 1 ? 1.6f : 2.5f;
        public static string Label(int index) => index == 0 ? "Нижняя" : index == 1 ? "Средняя" : "Верхняя";
        public static string ChildName(int index) => index == 0 ? "Lower" : index == 1 ? "Middle" : "Upper";

        /// <summary>Старая общая пустота пересекается с каждой секцией без дополнительных перемычек.</summary>
        public static BlockoutSectionSettings[] FromLegacy(float height, CoverClass material, BlockoutOpeningSettings openings)
        {
            var result = new BlockoutSectionSettings[3];
            for (int i = 0; i < result.Length; i++)
            {
                float bottom = Bottom(i), top = Mathf.Min(Top(i), height);
                var slot = openings;
                float gapBottom = Mathf.Max(bottom, openings.sillHeight), gapTop = Mathf.Min(top, height - openings.lintelHeight);
                slot.enabled = openings.enabled && top > bottom && gapTop > gapBottom;
                slot.sillHeight = slot.enabled ? gapBottom - bottom : Mathf.Min(.3f, (Top(i) - bottom) / 4);
                slot.lintelHeight = slot.enabled ? top - gapTop : Mathf.Min(.3f, (Top(i) - bottom) / 4);
                result[i] = new BlockoutSectionSettings { material = material, openings = slot };
            }
            return result;
        }

        public static BlockoutSectionSettings[] Copy(BlockoutSectionSettings[] sections)
        {
            var copy = new BlockoutSectionSettings[3];
            for (int i = 0; i < copy.Length; i++) copy[i] = sections[i].Copy();
            return copy;
        }
    }
}

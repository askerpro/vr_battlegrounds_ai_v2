using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using VrBattlegrounds.Core;

namespace VrBattlegrounds.Editor.Avatars
{
    /// <summary>
    /// Присед и сидение контроллера ног (<see cref="AvatarMixamoLocomotionSetup"/>) — данные, а не код: поза приседа
    /// (<c>Legs_Crouch</c> 1, клип и кадр; повороты и шаг в приседе — набора винтовки) и позы ниже приседа сверху вниз — между
    /// 1 и 2 на порогах по высоте шеи (самая глубокая — 2). Нет ассета — присед винтовки (кадр 0) и сидение из папки
    /// <c>Sit/</c>, как раньше.
    /// <para>
    /// Пишется кнопкой «Применить в игру» стенда перемотки клипов (сохранённая комбинация → ассет → пересборка контроллера):
    /// так результаты экспериментов на стенде переносятся в игру. Ассет под git — применённое видно, откатывается и повторяется.
    /// Контроллер общий для всех аватаров с ногами UltimateXR — префабы не меняются.
    /// </para>
    /// </summary>
    public sealed class AvatarLegsCrouchConfig : ScriptableObject
    {
        public const string AssetPath = AvatarMixamoLocomotionSetup.LocomotionFolder + "/AvatarLegsCrouch.asset";

        /// <summary>Папка статичных поз, собранных по ассету (кадр клипа → <c>_F&lt;кадр&gt;_Static.anim</c>).</summary>
        public const string StaticFolder = AvatarMixamoLocomotionSetup.LocomotionFolder + "/CrouchLevels";

        [Serializable]
        public class PoseFrame
        {
            public AnimationClip clip;
            public float frame;
        }

        [Tooltip("Поза приседа — Legs_Crouch 1 (статичная, кадр клипа). Повороты и шаг в приседе — набора винтовки.")]
        public PoseFrame crouch = new PoseFrame();

        [Tooltip("Позы ниже приседа сверху вниз — между Legs_Crouch 1 и 2 по высоте шеи; самая глубокая — 2.")]
        public List<PoseFrame> below = new List<PoseFrame>();

        [Tooltip("Откуда применено (комбинация стенда) и когда.")]
        public string source;

        public static AvatarLegsCrouchConfig Load() => AssetDatabase.LoadAssetAtPath<AvatarLegsCrouchConfig>(AssetPath);

        /// <summary>
        /// Применить в игру: записать ассет (поза приседа, позы ниже сверху вниз) и пересобрать контроллер ног. Простые типы —
        /// вызывается стендом через отражение (сборка стенда эту не видит). Возвращает итог для журнала.
        /// </summary>
        public static string Apply(AnimationClip crouchClip, float crouchFrame, AnimationClip[] belowClips, float[] belowFrames, string sourceLabel)
        {
            if (crouchClip == null) return "нет клипа приседа — не применено";
            AvatarLegsCrouchConfig config = Load();
            if (config == null)
            {
                config = CreateInstance<AvatarLegsCrouchConfig>();
                AssetDatabase.CreateAsset(config, AssetPath);
            }

            Undo.RecordObject(config, "Apply Legs Crouch");
            config.crouch = new PoseFrame { clip = crouchClip, frame = crouchFrame };
            config.below = new List<PoseFrame>();
            for (int i = 0; i < belowClips.Length; i++)
            {
                if (belowClips[i] != null) config.below.Add(new PoseFrame { clip = belowClips[i], frame = belowFrames[i] });
            }

            config.source = $"{sourceLabel} ({DateTime.Now:yyyy-MM-dd HH:mm})";
            EditorUtility.SetDirty(config);
            AssetDatabase.SaveAssets();

            if (!AssetDatabase.IsValidFolder(StaticFolder))
            {
                AssetDatabase.CreateFolder(Path.GetDirectoryName(StaticFolder).Replace(Path.DirectorySeparatorChar, '/'), Path.GetFileName(StaticFolder));
            }

            var controller = AvatarMixamoLocomotionSetup.EnsureController();
            string result = controller != null
                ? $"применено: присед {crouchClip.name}@{crouchFrame:0}, ниже приседа {config.below.Count} поз; контроллер пересобран"
                : "ассет записан, но контроллер не собран (см. консоль)";
            GameLog.Player.Info($"[AvatarLegsCrouchConfig] {result}. Источник: {config.source}");
            return result;
        }
    }
}

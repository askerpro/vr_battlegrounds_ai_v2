using System;
using System.Collections.Generic;

namespace VrBattlegrounds.Editor.Avatars
{
    /// <summary>Режим прогона Grip Extraction.</summary>
    public enum GripExtractionMode
    {
        /// <summary>Только расчёт и JSON-отчёт; префабы и позы не меняются.</summary>
        Measure,

        /// <summary>Запись align и записей на копии префаба в памяти (LoadPrefabContents) без сохранения; позы — временные.</summary>
        Preview,

        /// <summary>Запись поз, align и записей хвата на настоящие префабы со сторожем идентификаторов.</summary>
        Apply
    }

    /// <summary>
    /// Запрос <see cref="GripExtraction.RunJson" /> (JSON, имена полей в camelCase, регистр не важен).
    /// Пример: <c>{"source":"pack","recipes":["all"],"target":"CyborgHands","mode":"measure","outDir":"F:/…/reports/…"}</c>.
    /// </summary>
    public sealed class GripExtractionRequest
    {
        /// <summary>Источник: pack (кадр клипа паков Hands/KINEMATION).</summary>
        public string Source = PackClipGripSource.SourceId;

        /// <summary>all | hands | kinemation | имя позы | имя префаба оружия.</summary>
        public string[] Recipes = { "all" };

        /// <summary>
        /// CyborgHands | MEF_cyborg_hands. По умолчанию — CyborgHands: общая база CyborgHandsBase, на неё рассчитаны
        /// регистрация хватов (<c>AvatarGripRegistration</c>) и правило владения записями Cyborg; профиль MEF выведен 2026-10-23.
        /// </summary>
        public string Target = GripTargetProfile.CyborgHandsName;

        /// <summary>wrist | palm; null — как в профиле.</summary>
        public string Anchor;

        /// <summary>source | rig; null — как в профиле.</summary>
        public string Encoding;

        /// <summary>measure | preview | apply.</summary>
        public string Mode = "measure";

        /// <summary>Папка отчёта вне Assets; null — Temp/GripExtraction/&lt;время&gt;.</summary>
        public string OutDir;

        /// <summary>Сдвиг по нормали ладони для всех рук, мм (+ — ладонь ближе к предмету); |s| ≤ <see cref="MaxNormalOffsetMm" />.</summary>
        public float NormalOffsetMm;

        /// <summary>Сдвиг по рукам: ключ <c>&lt;поза&gt;/Left|Right</c>, мм.</summary>
        public Dictionary<string, float> NormalOffsetMmByHand;

        /// <summary>Curl доводки по рукам: ключ <c>&lt;поза&gt;/Left|Right</c> → {thumb..little: градусы}; нужен encoding=rig.</summary>
        public Dictionary<string, Dictionary<string, float>> CurlDegByHand;

        public float MaxNormalOffsetMm = 15f;

        /// <summary>Переопределения профиля для Apply; null — как в профиле.</summary>
        public bool? RegisterPoses;
        public bool? RemoveReplacedAligns;

        /// <summary>После Apply прогнать Hand Pose Fit по каждой руке (лист ревью, подсказки сдвига и curl).</summary>
        public bool Analyze;

        /// <summary>true — RunJson возвращает полный отчёт, иначе краткую сводку (полный всегда в outDir).</summary>
        public bool ReturnFull;

        public GripExtractionMode ParseMode()
        {
            switch ((Mode ?? "measure").ToLowerInvariant())
            {
                case "measure": return GripExtractionMode.Measure;
                case "preview": return GripExtractionMode.Preview;
                case "apply":   return GripExtractionMode.Apply;
                default:        throw new ArgumentException($"mode: «{Mode}» — нужен measure, preview или apply.");
            }
        }

        public GripAnchor? ParseAnchor()
        {
            if (string.IsNullOrEmpty(Anchor)) return null;
            switch (Anchor.ToLowerInvariant())
            {
                case "wrist": return GripAnchor.Wrist;
                case "palm":
                case "palmcenter": return GripAnchor.PalmCenter;
                default: throw new ArgumentException($"anchor: «{Anchor}» — нужен wrist или palm.");
            }
        }

        public GripPoseEncoding? ParseEncoding()
        {
            if (string.IsNullOrEmpty(Encoding)) return null;
            switch (Encoding.ToLowerInvariant())
            {
                case "source":
                case "assource": return GripPoseEncoding.AsSource;
                case "rig":
                case "targetrigworld": return GripPoseEncoding.TargetRigWorld;
                default: throw new ArgumentException($"encoding: «{Encoding}» — нужен source или rig.");
            }
        }
    }
}

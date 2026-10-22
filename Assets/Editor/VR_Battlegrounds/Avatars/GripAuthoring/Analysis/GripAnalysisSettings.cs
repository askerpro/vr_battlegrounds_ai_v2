using System;
using UnityEditor;
using UnityEngine;

namespace VrBattlegrounds.Editor.GripAuthoring.Analysis
{
    internal enum GripCheckStatus { NotApplicable, Ok, Warn, Fail }

    /// <summary>Редактируемый порог проверки; значение хранится в EditorPrefs.</summary>
    internal sealed class GripThreshold
    {
        public readonly string Key;
        public readonly string Label;
        public readonly string Tooltip;
        public readonly float Default;
        public readonly float Min;
        public readonly float Max;
        public float Value;

        public GripThreshold(string key, string label, float defaultValue, float min, float max, string tooltip = null)
        {
            Key = key;
            Label = label;
            Default = defaultValue;
            Min = min;
            Max = max;
            Tooltip = tooltip ?? "";
            Value = defaultValue;
        }
    }

    /// <summary>Описание проверки: id, переключатель, состояние детализации и пороги.</summary>
    internal sealed class GripCheckDefinition
    {
        public readonly string Id;
        public readonly string Title;
        public readonly string Description;
        public readonly GripThreshold[] Thresholds;
        public bool Enabled = true;
        public bool DetailsExpanded;

        public GripCheckDefinition(string id, string title, string description, params GripThreshold[] thresholds)
        {
            Id = id;
            Title = title;
            Description = description;
            Thresholds = thresholds ?? Array.Empty<GripThreshold>();
        }
    }

    /// <summary>
    /// Состояние режима «Анализ»: включение, переключатели проверок, пороги и раскрытые блоки.
    /// Всё хранится в EditorPrefs; чтение защищено от повреждённых значений.
    /// </summary>
    internal static class GripAnalysisSettings
    {
        const string Prefix = "VrBattlegrounds.GripAnalysis.";

        // Общие пороги знака и окраски проб
        public static readonly GripThreshold NearGapMm = new GripThreshold("common.near_gap_mm", "Просвет «близко» (голубой), мм", 5f, 0.5f, 50f,
            "Точки с просветом до этого значения рисуются голубым.");
        public static readonly GripThreshold WindingNearMm = new GripThreshold("common.winding_near_mm", "Уточнять знак winding ближе, мм", 5f, 0f, 30f,
            "Для точек ближе к поверхности знак «внутри/снаружи» уточняется обобщённым winding number.");
        public static readonly GripThreshold WindingRadiusMm = new GripThreshold("common.winding_radius_mm", "Радиус треугольников winding, мм", 30f, 5f, 220f,
            "Winding number считается по треугольникам, чья ограничивающая сфера пересекает сферу этого радиуса вокруг точки.");

        public static readonly GripCheckDefinition K1 = new GripCheckDefinition(
            "K1", "Касание точки фаланги",
            "Ладонная точка на кольце фаланги (радиус фаланги, доля длины t из профиля кисти, по умолчанию 0,5); контакт, если знаковое расстояние в [−допуск внутрь; +допуск наружу].",
            new GripThreshold("K1.tol_in_mm", "Допуск внутрь, мм", 1.0f, 0f, 20f),
            new GripThreshold("K1.tol_out_mm", "Допуск наружу, мм", 0.5f, 0f, 20f),
            new GripThreshold("K1.ok_min", "ok при контактах ≥", 10f, 0f, 15f),
            new GripThreshold("K1.warn_min", "warn при контактах ≥", 6f, 0f, 15f));

        public static readonly GripCheckDefinition X1 = new GripCheckDefinition(
            "X1", "Проникновение кольца фаланги",
            "6 точек на кольце фаланги в доле длины t (90 точек на кисть); fail, если самая глубокая точка фаланги глубже допуска.",
            new GripThreshold("X1.tol_mm", "Допуск проникновения, мм", 1.0f, 0f, 20f));

        public static readonly GripCheckDefinition X2 = new GripCheckDefinition(
            "X2", "Кончик пальца",
            "Точка кончика (GetDistalTipWorld); fail, если глубже допуска.",
            new GripThreshold("X2.tol_mm", "Допуск проникновения, мм", 1.0f, 0f, 20f));

        public static readonly GripCheckDefinition D1 = new GripCheckDefinition(
            "D1", "Направление контактной точки",
            "Угол между внешней нормалью ладонной точки K1 (радиально от оси кости) и направлением к ближайшей точке поверхности.",
            new GripThreshold("D1.max_dist_mm", "Оценивать ближе, мм", 10f, 0.5f, 100f),
            new GripThreshold("D1.ok_deg", "ok при угле ≤, °", 45f, 0f, 180f),
            new GripThreshold("D1.fail_deg", "fail при угле >, °", 90f, 0f, 180f));

        public static readonly GripCheckDefinition P1 = new GripCheckDefinition(
            "P1", "Ладонь",
            "Центр ладони SDK (GetPalmCenter): расстояние до поверхности и угол нормали ладони (GetPalmOutDirection) к поверхности.",
            new GripThreshold("P1.warn_mm", "warn дальше, мм", 10f, 0f, 200f),
            new GripThreshold("P1.fail_mm", "fail дальше, мм", 25f, 0f, 200f),
            new GripThreshold("P1.ok_deg", "ok при угле ≤, °", 60f, 0f, 180f));

        public static readonly GripCheckDefinition F1 = new GripCheckDefinition(
            "F1", "Пределы суставов",
            "Углы Spread/Curl в параметризации IK и «Обхватить» (MeasureAnatomicalFingerAngles от bind-аватара) против GetFingerAngleBounds; " +
            "выход за предел — warn. Больше порога — warn: остаток вне шарнира средней и конечной фаланг (вбок и скручивание), " +
            "осевое скручивание проксимальной фаланги (кроме большого пальца) и отличие пястной кости от bind.",
            new GripThreshold("F1.tol_deg", "Допуск выхода, °", 0.5f, 0f, 30f),
            new GripThreshold("F1.hinge_residual_deg", "Остаток вне модели сустава, warn >, °", 2f, 0f, 45f));

        public static readonly GripCheckDefinition[] Checks = { K1, X1, X2, D1, P1, F1 };
        public static readonly GripThreshold[] CommonThresholds = { NearGapMm, WindingNearMm, WindingRadiusMm };

        static bool _analysisEnabled;
        static bool _showSceneProbes = true;
        static bool _showRingPoints = true;
        static bool _thresholdsExpanded;
        static bool _palmUseManual = true;
        static bool _ringsMoveInScene;
        static bool _ringsExpanded = true;
        static bool _handSettingsExpanded = true;

        /// <summary>Растёт при любом изменении, влияющем на результат; входит в сигнатуру пересчёта.</summary>
        public static int Version { get; private set; }

        public static bool AnalysisEnabled
        {
            get => _analysisEnabled;
            set => SetBool("enabled", ref _analysisEnabled, value);
        }

        public static bool ShowSceneProbes
        {
            get => _showSceneProbes;
            set => SetBool("scene_probes", ref _showSceneProbes, value);
        }

        public static bool ShowRingPoints
        {
            get => _showRingPoints;
            set => SetBool("scene_ring_points", ref _showRingPoints, value);
        }

        /// <summary>Источник точки ладони P1: true — ручная разметка профиля (если есть), false — всегда авто (кожа).</summary>
        public static bool PalmUseManual
        {
            get => _palmUseManual;
            set => SetBool("palm_use_manual", ref _palmUseManual, value);
        }

        /// <summary>Ручки Handles.Slider для сдвига колец фаланг вдоль оси кости в SceneView.</summary>
        public static bool RingsMoveInScene
        {
            get => _ringsMoveInScene;
            set => SetBool("rings_move_in_scene", ref _ringsMoveInScene, value);
        }

        /// <summary>Раздел «Настройки кисти» блока анализа раскрыт.</summary>
        public static bool HandSettingsExpanded
        {
            get => _handSettingsExpanded;
            set => SetBool("hand_settings_expanded", ref _handSettingsExpanded, value);
        }

        public static bool RingsExpanded
        {
            get => _ringsExpanded;
            set => SetBool("rings_expanded", ref _ringsExpanded, value);
        }

        public static bool ThresholdsExpanded
        {
            get => _thresholdsExpanded;
            set => SetBool("thresholds_expanded", ref _thresholdsExpanded, value);
        }

        static GripAnalysisSettings()
        {
            _analysisEnabled = ReadBool("enabled", false);
            _showSceneProbes = ReadBool("scene_probes", true);
            _showRingPoints = ReadBool("scene_ring_points", true);
            _thresholdsExpanded = ReadBool("thresholds_expanded", false);
            _palmUseManual = ReadBool("palm_use_manual", true);
            _ringsMoveInScene = ReadBool("rings_move_in_scene", false);
            _ringsExpanded = ReadBool("rings_expanded", true);
            _handSettingsExpanded = ReadBool("hand_settings_expanded", true);

            foreach (GripThreshold threshold in CommonThresholds)
            {
                threshold.Value = ReadFloat(threshold);
            }

            foreach (GripCheckDefinition check in Checks)
            {
                check.Enabled = ReadBool(check.Id + ".enabled", true);
                check.DetailsExpanded = ReadBool(check.Id + ".details", false);
                foreach (GripThreshold threshold in check.Thresholds)
                {
                    threshold.Value = ReadFloat(threshold);
                }
            }
        }

        public static float Get(GripCheckDefinition check, int thresholdIndex) => check.Thresholds[thresholdIndex].Value;

        public static void SetCheckEnabled(GripCheckDefinition check, bool enabled)
        {
            if (check.Enabled == enabled)
            {
                return;
            }

            check.Enabled = enabled;
            WriteBool(check.Id + ".enabled", enabled);
            Version++;
        }

        public static void SetDetailsExpanded(GripCheckDefinition check, bool expanded)
        {
            if (check.DetailsExpanded == expanded)
            {
                return;
            }

            check.DetailsExpanded = expanded;
            WriteBool(check.Id + ".details", expanded);
        }

        public static void SetThreshold(GripThreshold threshold, float value)
        {
            value = Mathf.Clamp(value, threshold.Min, threshold.Max);
            if (Mathf.Approximately(threshold.Value, value))
            {
                return;
            }

            threshold.Value = value;
            try
            {
                EditorPrefs.SetFloat(Prefix + threshold.Key, value);
            }
            catch
            {
                // Значение остаётся в памяти до конца сессии
            }

            Version++;
        }

        public static void ResetThresholds()
        {
            foreach (GripThreshold threshold in CommonThresholds)
            {
                SetThreshold(threshold, threshold.Default);
            }

            foreach (GripCheckDefinition check in Checks)
            {
                foreach (GripThreshold threshold in check.Thresholds)
                {
                    SetThreshold(threshold, threshold.Default);
                }
            }
        }

        static void SetBool(string key, ref bool field, bool value)
        {
            if (field == value)
            {
                return;
            }

            field = value;
            WriteBool(key, value);
            Version++;
        }

        static bool ReadBool(string key, bool defaultValue)
        {
            try
            {
                return EditorPrefs.GetBool(Prefix + key, defaultValue);
            }
            catch
            {
                return defaultValue;
            }
        }

        static void WriteBool(string key, bool value)
        {
            try
            {
                EditorPrefs.SetBool(Prefix + key, value);
            }
            catch
            {
                // Значение остаётся в памяти до конца сессии
            }
        }

        static float ReadFloat(GripThreshold threshold)
        {
            try
            {
                float value = EditorPrefs.GetFloat(Prefix + threshold.Key, threshold.Default);
                if (float.IsNaN(value) || float.IsInfinity(value))
                {
                    return threshold.Default;
                }

                return Mathf.Clamp(value, threshold.Min, threshold.Max);
            }
            catch
            {
                return threshold.Default;
            }
        }
    }
}

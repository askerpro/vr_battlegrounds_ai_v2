using UnityEditor;
using UnityEngine;
using VrBattlegrounds.Player.WallPass;

namespace VrBattlegrounds.Editor
{
    /// <summary>Личные настройки T-40: профиль не загрязняется экспериментами на машине.</summary>
    public sealed class WallPassVisualWindow : EditorWindow
    {
        private const string PrefKey = "VrBattlegrounds.T40.VisualSettings";
        private const string PreviewKey = "VrBattlegrounds.T40.VisualPreview";
        private Vector2 _scroll;
        private WallPassVisualPreview _preview;
        private bool _showPreview = true;

        [MenuItem("Tools/VR Battlegrounds/Debug/T-40 Visual Feedback")]
        public static void Open() => GetWindow<WallPassVisualWindow>("T-40: эффекты");

        [InitializeOnLoadMethod]
        private static void Initialize()
        {
            EditorApplication.delayCall += () =>
            {
                string json = EditorPrefs.GetString(PrefKey, "");
                if (!string.IsNullOrEmpty(json)) JsonUtility.FromJsonOverwrite(json, WallPassVisualRuntime.Settings);
                WallPassVisualRuntime.PreviewEnabled = SessionState.GetBool(PreviewKey, false);
                WallPassVisualRuntime.PreviewRisk = SessionState.GetFloat(PreviewKey + ".Risk", 0.5f);
                WallPassVisualRuntime.PreviewYaw = SessionState.GetFloat(PreviewKey + ".Yaw", 0f);
            };
        }

        private void OnDisable()
        {
            _preview?.Dispose();
            _preview = null;
            WallPassVisualRuntime.PreviewEnabled = false;
            SessionState.SetBool(PreviewKey, false);
        }

        private void OnInspectorUpdate() => Repaint();

        private void OnGUI()
        {
            WallPassVisualSettings settings = WallPassVisualRuntime.Settings;
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            EditorGUILayout.HelpBox("Ползунки меняют локальный визуал и сохраняются на этой машине. Для игры на Quest выбранные значения можно явно записать в профиль сборки.", MessageType.Info);
            EditorGUI.BeginChangeCheck();
            settings.Intensity = EditorGUILayout.Slider("Общая интенсивность", settings.Intensity, 0f, 2f);
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Поток частиц", EditorStyles.boldLabel);
            settings.ParticleDensity = EditorGUILayout.Slider("Количество", settings.ParticleDensity, 0f, 1f);
            settings.ParticleBrightness = EditorGUILayout.Slider("Яркость", settings.ParticleBrightness, 0f, 2f);
            settings.ParticleSpeed = EditorGUILayout.Slider("Скорость", settings.ParticleSpeed, 0.05f, 2f);
            settings.ParticleTailLength = EditorGUILayout.Slider("Длина хвостов", settings.ParticleTailLength, 0.02f, 0.4f);
            settings.PeripheralWidth = EditorGUILayout.Slider("Ширина периферии", settings.PeripheralWidth, 0.08f, 0.5f);
            settings.EffectRadius = EditorGUILayout.Slider("Радиус вокруг головы, м", settings.EffectRadius, 1f, 3f);
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Окружение и игрок", EditorStyles.boldLabel);
            settings.WorldStrength = EditorGUILayout.Slider("Окружение", settings.WorldStrength, 0f, 1f);
            settings.BodyStrength = EditorGUILayout.Slider("Руки, тело, оружие", settings.BodyStrength, 0f, 1f);
            settings.RimStrength = EditorGUILayout.Slider("Свечение края", settings.RimStrength, 0f, 1f);
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Нарастание опасности", EditorStyles.boldLabel);
            settings.InitialStrength = EditorGUILayout.Slider("Начальная сила", settings.InitialStrength, 0f, 1f);
            settings.MaximumStrength = EditorGUILayout.Slider("Максимальная сила", settings.MaximumStrength, settings.InitialStrength, 1f);
            settings.Escalation = EditorGUILayout.CurveField("Кривая усиления", settings.Escalation, Color.cyan, new Rect(0, 0, 1, 1));
            if (EditorGUI.EndChangeCheck()) SaveSettings();
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Мягкий")) ApplyPreset(0);
            if (GUILayout.Button("Средний")) ApplyPreset(1);
            if (GUILayout.Button("Сильный")) ApplyPreset(2);
            if (GUILayout.Button("По умолчанию")) ResetSettings();
            EditorGUILayout.EndHorizontal();
            if (GUILayout.Button("Сохранить настройки в профиль сборки"))
            {
                WallPassVisualSettings profile = Resources.Load<WallPassVisualSettings>("WallPassVisualProfile");
                if (profile != null)
                {
                    Undo.RecordObject(profile, "Настроить визуальный профиль T-40");
                    JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(settings), profile);
                    EditorUtility.SetDirty(profile);
                    AssetDatabase.SaveAssetIfDirty(profile);
                }
            }
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Предпросмотр", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            WallPassVisualRuntime.PreviewRisk = EditorGUILayout.Slider("Опасность", WallPassVisualRuntime.PreviewRisk, 0f, 1f);
            WallPassVisualRuntime.PreviewYaw = EditorGUILayout.Slider("Направление, °", WallPassVisualRuntime.PreviewYaw, -180f, 180f);
            EditorGUILayout.LabelField("0° — вперёд; ±180° — назад; ±90° — вбок.");
            WallPassVisualRuntime.PreviewEnabled = EditorGUILayout.Toggle("Показать в игре (Play)", WallPassVisualRuntime.PreviewEnabled);
            if (EditorGUI.EndChangeCheck())
            {
                SessionState.SetBool(PreviewKey, WallPassVisualRuntime.PreviewEnabled);
                SessionState.SetFloat(PreviewKey + ".Risk", WallPassVisualRuntime.PreviewRisk);
                SessionState.SetFloat(PreviewKey + ".Yaw", WallPassVisualRuntime.PreviewYaw);
            }
            _showPreview = EditorGUILayout.Toggle("Изолированный стенд", _showPreview);
            if (_showPreview && !EditorApplication.isPlayingOrWillChangePlaymode)
            {
                _preview ??= new WallPassVisualPreview();
                Rect rect = GUILayoutUtility.GetRect(240f, 220f, GUILayout.ExpandWidth(true));
                if (Event.current.type == EventType.Repaint)
                {
                    _preview.Render(settings, WallPassVisualRuntime.PreviewRisk, WallPassVisualRuntime.PreviewYaw,
                                    (float)EditorApplication.timeSinceStartup);
                    GUI.DrawTexture(rect, _preview.Texture, ScaleMode.ScaleToFit, false);
                }
                EditorGUILayout.LabelField("Схематичные руки; читаемость направления проверить в шлеме.", EditorStyles.wordWrappedMiniLabel);
            }
            else if (_preview != null) { _preview.Dispose(); _preview = null; }
            EditorGUILayout.EndScrollView();
        }

        private static void SaveSettings() => EditorPrefs.SetString(PrefKey, JsonUtility.ToJson(WallPassVisualRuntime.Settings));

        private static void ApplyPreset(int index)
        {
            ResetSettings();
            WallPassVisualSettings settings = WallPassVisualRuntime.Settings;
            settings.Intensity = index == 0 ? 0.65f : index == 2 ? 1.4f : 1f;
            settings.ParticleDensity = index == 0 ? 0.35f : index == 2 ? 0.9f : 0.6f;
            settings.ParticleBrightness = index == 0 ? 0.5f : index == 2 ? 1f : 0.7f;
            SaveSettings();
        }

        private static void ResetSettings()
        {
            WallPassVisualSettings source = Resources.Load<WallPassVisualSettings>("WallPassVisualProfile");
            bool temporary = source == null;
            if (temporary) source = CreateInstance<WallPassVisualSettings>();
            JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(source), WallPassVisualRuntime.Settings);
            if (temporary) DestroyImmediate(source);
            EditorPrefs.DeleteKey(PrefKey);
        }
    }
}

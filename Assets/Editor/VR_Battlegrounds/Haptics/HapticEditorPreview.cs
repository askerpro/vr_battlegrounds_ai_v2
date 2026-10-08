using System.Collections.Generic;
using UltimateXR.Core;
using UltimateXR.Haptics;
using UnityEditor;
using UnityEngine;
using VrBattlegrounds.Haptics;

namespace VrBattlegrounds.EditorTools.Haptics
{
    /// <summary>
    /// Общие куски редакторского UI вибрации: проба клипа на руках в Play Mode, живое состояние рук и картинка формы.
    /// Используются drawer'ом <see cref="UxrHapticClipDrawer" />, инспектором формы и окном «Вибрация».
    /// </summary>
    internal static class HapticEditorPreview
    {
        private static readonly Dictionary<UxrHandSide, HapticHandle> s_held = new Dictionary<UxrHandSide, HapticHandle>();

        public static bool CanPlay => Application.isPlaying && HapticService.IsInstalled;

        /// <summary>Кнопки пробы: разово на левой/правой/обеих, держать на левой/правой, стоп.</summary>
        public static void DrawButtons(Rect rect, UxrHapticClip clip, Object owner)
        {
            using (new EditorGUI.DisabledScope(!CanPlay || clip == null || !clip.HasWaveform))
            {
                float w = rect.width / 6f;
                Rect r = new Rect(rect.x, rect.y, w - 2f, rect.height);
                if (GUI.Button(r, "Левая")) HapticService.Play(clip, UxrHandSide.Left);
                r.x += w;
                if (GUI.Button(r, "Правая")) HapticService.Play(clip, UxrHandSide.Right);
                r.x += w;
                if (GUI.Button(r, "Обе")) HapticService.PlayBoth(clip);
                r.x += w;
                if (GUI.Button(r, "Держать Л")) Hold(clip, UxrHandSide.Left, owner);
                r.x += w;
                if (GUI.Button(r, "Держать П")) Hold(clip, UxrHandSide.Right, owner);
                r.x += w;
                if (GUI.Button(r, "Стоп")) StopHeld();
            }
        }

        public static void DrawButtonsLayout(UxrHapticClip clip, Object owner) =>
            DrawButtons(EditorGUILayout.GetControlRect(), clip, owner);

        private static void Hold(UxrHapticClip clip, UxrHandSide side, Object owner)
        {
            if (s_held.TryGetValue(side, out HapticHandle old)) old.End();
            s_held[side] = HapticService.Begin(clip, side, owner);
        }

        public static void StopHeld()
        {
            foreach (HapticHandle handle in s_held.Values) handle.End();
            s_held.Clear();
        }

        /// <summary>Живое состояние рук: виден ли контроллер, сколько голосов, какая сила уходит в мотор.</summary>
        public static void DrawLiveState()
        {
            if (!Application.isPlaying)
            {
                EditorGUILayout.HelpBox("Проба и живая правка — в Play Mode с подключённым шлемом (Quest Link). Править формы и " +
                                        "клипы можно и сейчас: это настройка проекта.", MessageType.Info);
                return;
            }
            if (!HapticService.IsInstalled)
            {
                EditorGUILayout.HelpBox("HapticService не установлен — вибрации нет.", MessageType.Warning);
                return;
            }

            foreach (UxrHandSide side in new[] { UxrHandSide.Left, UxrHandSide.Right })
            {
                HapticService.GetHandState(side, out int voices, out float amplitude);
                bool connected = HapticService.IsControllerConnected(side);
                string label = $"{(side == UxrHandSide.Left ? "Левая" : "Правая")}: " +
                               $"{(connected ? "контроллер есть" : "НЕТ контроллера")}, голосов {voices}, сила {amplitude:0.00}";
                EditorGUI.ProgressBar(EditorGUILayout.GetControlRect(), amplitude, label);
            }
        }

        /// <summary>Картинка формы: столбики отрезков, ширина — длительность, высота — сила.</summary>
        public static void DrawWaveform(Rect rect, UxrHapticWaveform waveform)
        {
            EditorGUI.DrawRect(rect, new Color(0.12f, 0.12f, 0.12f));
            if (waveform == null || waveform.LengthSeconds <= 0f) return;

            float total = waveform.LengthSeconds * 1000f;
            float x = rect.x;
            foreach (UxrHapticWaveform.Segment segment in waveform.Segments)
            {
                float width = rect.width * Mathf.Max(0, segment.DurationMs) / total;
                float height = (rect.height - 2f) * Mathf.Clamp01(segment.Amplitude);
                EditorGUI.DrawRect(new Rect(x, rect.yMax - 1f - height, Mathf.Max(1f, width - 1f), height), new Color(1f, 0.65f, 0.15f));
                x += width;
            }
            GUI.Label(new Rect(rect.x + 4f, rect.y + 2f, rect.width, 16f), $"{total:0} мс", EditorStyles.miniLabel);
        }
    }

    /// <summary>Записывает формы и роли вибрации при выходе из Play, чтобы подобранные в шлеме значения не потерялись.</summary>
    [InitializeOnLoad]
    internal static class HapticAutoSave
    {
        static HapticAutoSave()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }

        private static void OnPlayModeChanged(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.ExitingPlayMode) HapticEditorPreview.StopHeld();
            if (change != PlayModeStateChange.EnteredEditMode) return;
            SaveAll();
        }

        public static void SaveAll()
        {
            foreach (string guid in AssetDatabase.FindAssets("t:" + nameof(UxrHapticWaveform) + " t:" + nameof(HapticRoles)))
            {
                Object asset = AssetDatabase.LoadMainAssetAtPath(AssetDatabase.GUIDToAssetPath(guid));
                if (asset != null && EditorUtility.IsDirty(asset)) AssetDatabase.SaveAssetIfDirty(asset);
            }
        }
    }
}

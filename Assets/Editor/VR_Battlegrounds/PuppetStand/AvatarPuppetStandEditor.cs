using System.IO;
using UnityEditor;
using UnityEngine;
using VrBattlegrounds.DevTools.LegsCompare;

namespace VrBattlegrounds.Editor.PuppetStand
{
    /// <summary>
    /// Инспектор стенда-плейграунда <see cref="AvatarPuppetStand"/>: кнопки запуска программ, сценария из клипа, «Стоп» и
    /// замер стоп клипа по фазам (режим просмотра клипа). Агенту — то же одной строкой: <see cref="AvatarPuppetStand.RunAll"/>.
    /// </summary>
    [CustomEditor(typeof(AvatarPuppetStand))]
    public sealed class AvatarPuppetStandEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            var stand = (AvatarPuppetStand)target;
            DrawDefaultInspector();
            EditorGUILayout.Space();

            if (!Application.isPlaying)
            {
                EditorGUILayout.HelpBox("Play — аватары в ряд, ручной режим: двигай «голову» и кисти в Scene view. Программы и сценарий из клипа — кнопками в Play. Режим ClipPreview работает и без Play (клип — в дочернем ClipPreview).", MessageType.Info);
                ClipPreviewButtons(stand);
                return;
            }

            EditorGUILayout.LabelField("Состояние", stand.Status);
            if (GUILayout.Button($"Запустить программу {stand.program}")) stand.RunProgram(stand.program);
            if (GUILayout.Button("Сценарий из клипа")) stand.RunClipScenario();
            if (GUILayout.Button("Все программы и сценарий (как RunAll)")) AvatarPuppetStand.RunAll(null);
            if (GUILayout.Button("Стоп → ручной")) stand.StopToManual();
            if (!string.IsNullOrEmpty(AvatarPuppetStand.LastOutput)) EditorGUILayout.LabelField("Запись", AvatarPuppetStand.LastOutput);
            ClipPreviewButtons(stand);
            Repaint();
        }

        private static void ClipPreviewButtons(AvatarPuppetStand stand)
        {
            if (stand.mode != PuppetMode.ClipPreview || stand.clipPreview == null) return;
            var preview = stand.clipPreview.GetComponent<ClipFeetPreview>();
            if (preview == null) return;
            if (GUILayout.Button("Выбрать клип и фазу (ClipPreview)")) Selection.activeGameObject = stand.clipPreview;
            if (GUILayout.Button("Замерить стопы клипа по фазам"))
            {
                string folder = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "tmp", "PuppetStand", "clipfeet_" + (preview.clip != null ? preview.clip.name : "none")));
                string path = preview.MeasurePhases(folder);
                if (path != null) EditorUtility.RevealInFinder(path);
            }
        }
    }
}

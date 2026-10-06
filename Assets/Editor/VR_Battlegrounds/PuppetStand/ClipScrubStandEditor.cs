using UnityEditor;
using UnityEngine;
using VrBattlegrounds.DevTools.LegsCompare;

namespace VrBattlegrounds.Editor.PuppetStand
{
    /// <summary>
    /// Инспектор <see cref="ClipScrubStand"/>: цепочка поз аватара и одна инспектируемая строка (<see cref="ClipScrubGui"/>).
    /// Данные выделенного аватара по разделам и его управление — окно «Отладка аватара» (<see cref="AvatarDebugWindow"/>):
    /// оно не пропадает при выделении манипулятора.
    /// </summary>
    [CustomEditor(typeof(ClipScrubStand))]
    public sealed class ClipScrubStandEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            var stand = (ClipScrubStand)target;
            DrawDefaultInspector();
            ClipScrubList list = stand.list;
            if (list == null)
            {
                EditorGUILayout.HelpBox("Нет списка клипов (ClipScrubList).", MessageType.Warning);
                return;
            }

            if (GUILayout.Button("Окно «Отладка аватара» (данные выделенного аватара)")) AvatarDebugWindow.Open();

            EditorGUILayout.Space();
            ClipScrubGui.ViewToolbar(stand);
            int inspected = ClipScrubGui.InspectedRow(stand);
            EditorGUILayout.LabelField(stand.SavedView ? "Цепочка поз инспектируемой комбинации" : "Цепочка поз аватара (сверху вниз, общая для всех строк)", EditorStyles.boldLabel);
            if (inspected >= 0) ClipScrubGui.DrawChain(stand, inspected);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Инспектируемая строка — кандидат (одна, как в окне «Отладка аватара»)", EditorStyles.boldLabel);
            bool byHead = stand.followHead && stand.head != null;
            int row = ClipScrubGui.RowSelector(stand);
            if (row >= 0)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                bool removed = ClipScrubGui.DrawEntryControls(stand, row, true);
                if (!removed)
                {
                    string source = stand.PoseSource(row);
                    if (source.Length > 0) EditorGUILayout.LabelField("сейчас: " + source, EditorStyles.miniLabel);
                }

                EditorGUILayout.EndVertical();
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Папки кандидатов (выпадающие списки клипов)", EditorStyles.boldLabel);
            string sit = EditorGUILayout.TextField("sit candidates", list.sitCandidatesFolder);
            string crouch = EditorGUILayout.TextField("crouch candidates", list.crouchCandidatesFolder);
            if (sit != list.sitCandidatesFolder || crouch != list.crouchCandidatesFolder)
            {
                ClipScrubGui.Change(list, () =>
                {
                    list.sitCandidatesFolder = sit;
                    list.crouchCandidatesFolder = crouch;
                });
            }

            if (GUILayout.Button("+ строки для новых клипов sit candidates")) ClipScrubGui.AddMissingSitRows(list);
            if (!stand.SavedView && row >= 0 && GUILayout.Button("Сохранить комбинацию (инспектируемая строка)")) ClipScrubGui.SaveCombo(stand, row);
            if (!stand.SavedView && GUILayout.Button("+ Кандидат"))
            {
                ClipScrubGui.Change(list, () => list.entries.Add(new ClipScrubList.Entry()));
                ClipScrubGui.SetInspectedRow(stand, list.entries.Count - 1);
            }

            if (byHead) Repaint();
        }
    }
}

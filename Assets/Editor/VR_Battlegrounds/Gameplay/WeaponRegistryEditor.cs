#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using VrBattlegrounds.Arsenal;

namespace VrBattlegrounds.Editor.Gameplay
{
    /// <summary>Список каталога и переход к явной сетевой регистрации с областью изменения.</summary>
    [CustomEditor(typeof(WeaponRegistry))]
    public class WeaponRegistryEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();
            EditorGUILayout.Space();
            EditorGUILayout.HelpBox("Сетевая регистрация меняет канонический префаб менеджера. План и применение находятся в едином редакторе арсенала.", MessageType.Info);
            if (GUILayout.Button("Открыть сетевую регистрацию в редакторе арсенала"))
                VrBattlegrounds.Editor.Arsenal.ArsenalEditorWindow.OpenMaintenance(VrBattlegrounds.Editor.Arsenal.ArsenalEditorWindow.Tab.Catalog);
        }
    }
}
#endif

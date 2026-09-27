using UnityEditor;
using VrBattlegrounds.Arsenal;

namespace VrBattlegrounds.Editor.Arsenal
{
    /// <summary>
    /// Custom inspector for <see cref="FirearmSlotController"/>.
    /// Adds the decorative magazine anchor field; the magazine preview itself
    /// is spawned by <see cref="ArsenalSlotPreview"/>.
    /// </summary>
    [CustomEditor(typeof(FirearmSlotController))]
    public class FirearmSlotControllerEditor : ArsenalSlotEditorBase
    {
        private SerializedProperty _magAnchorProp;

        protected override void OnEnable()
        {
            base.OnEnable();
            _magAnchorProp = serializedObject.FindProperty("_magAnchor");
        }

        protected override void DrawExtraFields()
        {
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Decorative Magazine", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(_magAnchorProp);
        }
    }
}

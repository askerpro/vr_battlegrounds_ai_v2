// Copyright (c) 2026 KINEMATION.
// All rights reserved.

using System.Collections.Generic;
using KINEMATION.Shared.KAnimationCore.Editor.Rig;
using KINEMATION.Shared.KAnimationCore.Runtime.Rig;
using UnityEditor;
using UnityEngine;

namespace KINEMATION.Shared.KAnimationCore.Editor.Attributes
{
    [CustomPropertyDrawer(typeof(KRigElement))]
    public class RigElementDrawer : PropertyDrawer
    {
        private const float ClearButtonSpacing = 2f;

        private static void ClearSelection(SerializedProperty name, SerializedProperty index)
        {
            name.stringValue = string.Empty;
            index.intValue = -1;
            name.serializedObject.ApplyModifiedProperties();
        }

        private void DrawRigElement(Rect position, SerializedProperty property, GUIContent label)
        {
            IRigProvider rig = RigEditorUtility.TryGetRigProvider(fieldInfo, property);
             
            SerializedProperty name = property.FindPropertyRelative("name");
            SerializedProperty index = property.FindPropertyRelative("index");

            if (rig == null)
            {
                EditorGUI.PropertyField(position, name, label, true);
                return;
            }
            
            bool hasUserSelection = index.intValue > -1 || !string.IsNullOrEmpty(name.stringValue);
            Rect contentRect = EditorGUI.PrefixLabel(position, label);
            Rect buttonRect = new Rect(contentRect.x, position.y, contentRect.width,
                EditorGUIUtility.singleLineHeight);

            if (hasUserSelection)
            {
                float clearButtonWidth = EditorGUIUtility.singleLineHeight;
                buttonRect.width -= clearButtonWidth + ClearButtonSpacing;
            }

            string currentName = string.IsNullOrEmpty(name.stringValue) ? "None" : name.stringValue;

            if (GUI.Button(buttonRect, currentName))
            {
                var hierarchy = rig.GetHierarchy();
                if (hierarchy == null) return;

                List<int> selection = null;
                if (index.intValue > -1 || !string.IsNullOrEmpty(name.stringValue))
                {
                    int foundIndex = ArrayUtility.FindIndex(hierarchy,
                        element => element.name.Equals(name.stringValue));
                    if(foundIndex >= 0) selection = new List<int>() { foundIndex + 1 };
                }

                RigWindow.ShowWindow(hierarchy, (selectedElement) =>
                    {
                        name.stringValue = selectedElement.name;
                        index.intValue = selectedElement.index;
                        name.serializedObject.ApplyModifiedProperties();
                    },
                    items => { },
                    false, selection, "Rig Element Selection"
                );
            }

            if (!hasUserSelection)
            {
                return;
            }

            Rect clearButtonRect = new Rect(buttonRect.xMax + ClearButtonSpacing, position.y,
                EditorGUIUtility.singleLineHeight, EditorGUIUtility.singleLineHeight);
            GUIContent clearButtonContent = new GUIContent("-");
            clearButtonContent.tooltip = "Clear bone selection";

            if (GUI.Button(clearButtonRect, clearButtonContent, EditorStyles.miniButton))
            {
                ClearSelection(name, index);
            }
        }
        
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);

            DrawRigElement(position, property, label);

            EditorGUI.EndProperty();
        }
    }
}
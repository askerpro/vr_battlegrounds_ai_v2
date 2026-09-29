// Copyright (c) 2026 KINEMATION.
// All rights reserved.

using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using KINEMATION.Shared.KAnimationCore.Runtime.Attributes;
using UnityEditor;
using UnityEngine;

namespace KINEMATION.Shared.KAnimationCore.Editor.Widgets
{
    public struct EditorTab
    {
        public string name;
        public List<SerializedProperty> properties;
    }
    
    public class TabInspectorWidget
    {
        private SerializedObject _serializedObject;

        private List<SerializedProperty> _defaultProperties;
        private List<EditorTab> _editorTabs;

        private string[] _tabNames;

        private int _selectedIndex = 0;
        private string _sessionStateKey;
        
        private T[] GetPropertyAttributes<T>(SerializedProperty property) where T : System.Attribute
        {
            T[] output = null;
            
            FieldInfo fieldInfo = _serializedObject.targetObject.GetType().GetField(property.propertyPath,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (fieldInfo != null)
            {
                output = (T[]) fieldInfo.GetCustomAttributes(typeof(T), true);
            }

            if (output == null || output.Length == 0)
            {
                return null;
            }
            
            return output;
        }
        
        public TabInspectorWidget(SerializedObject targetObject)
        {
            _serializedObject = targetObject;
            _sessionStateKey = $"KINEMATION.TabInspectorWidget.{targetObject.targetObject.GetType().FullName}.SelectedIndex";
        }

        public void Init()
        {
            _defaultProperties = new List<SerializedProperty>();
            _editorTabs = new List<EditorTab>();
            
            SerializedProperty property = _serializedObject.GetIterator();
            property.NextVisible(true);
            int activeTabIndex = -1;

            while (property.NextVisible(false))
            {
                TabAttribute[] tabAttributes = GetPropertyAttributes<TabAttribute>(property);
                if (tabAttributes == null)
                {
                    if (activeTabIndex >= 0)
                    {
                        _editorTabs[activeTabIndex].properties.Add(property.Copy());
                        continue;
                    }
                    
                    _defaultProperties.Add(property.Copy());
                    continue;
                }
                
                string tabName = tabAttributes[0].tabName;
                activeTabIndex = _editorTabs.FindIndex(tab => tab.name == tabName);
                if (activeTabIndex < 0)
                {
                    _editorTabs.Add(new EditorTab()
                    {
                        name = tabName,
                        properties = new List<SerializedProperty>()
                    });
                    activeTabIndex = _editorTabs.Count - 1;
                }

                _editorTabs[activeTabIndex].properties.Add(property.Copy());
            }

            _tabNames = _editorTabs.Select(item => item.name).ToArray();
            _selectedIndex = Mathf.Clamp(SessionState.GetInt(_sessionStateKey, 0), 0, Mathf.Max(0, _tabNames.Length - 1));
        }
        
        public void OnGUI()
        {
            _serializedObject.Update();
            
            foreach (var defaultProperty in _defaultProperties)
            {
                EditorGUILayout.PropertyField(defaultProperty, true);
            }

            if (_tabNames.Length > 0)
            {
                int prevIndex = _selectedIndex;
                _selectedIndex = GUILayout.Toolbar(_selectedIndex, _tabNames);
                if(prevIndex != _selectedIndex) SessionState.SetInt(_sessionStateKey, _selectedIndex);
                
                foreach (var tabProperty in _editorTabs[_selectedIndex].properties)
                {
                    EditorGUILayout.PropertyField(tabProperty, true);
                }
            }
            
            _serializedObject.ApplyModifiedProperties();
        }
    }
}
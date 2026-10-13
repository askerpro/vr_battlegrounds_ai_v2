using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace VrBattlegrounds.Editor.Avatars
{
    /// <summary>
    /// Сторож сериализованных идентификаторов префаба вокруг сохранения: <c>SaveAsPrefabAsset</c> из
    /// <c>LoadPrefabContents</c> однажды сбросил у варианта аватара <c>__prefabGuid</c>/<c>__isInPrefab</c> (UxrComponent)
    /// и Mirror <c>_assetId</c> (Details.md §cyborg-grips). Снимок до и после сохранения сравнивается по всем компонентам
    /// префаба; изменённое значение — исключение. Компоненты, которых после сохранения нет (удалённые align), не ошибка.
    /// </summary>
    public static class GripPrefabIdentityGuard
    {
        /// <summary>UxrComponent: id и данные префаба; Mirror NetworkIdentity: id ассета и сцены.</summary>
        private static readonly string[] Fields = { "_uxrUniqueId", "__prefabGuid", "__isInPrefab", "_assetId", "sceneId" };

        public static Dictionary<string, string> Snapshot(string assetPath)
        {
            GameObject root = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath) ?? throw new ArgumentException($"Нет префаба {assetPath}");
            var        map  = new Dictionary<string, string>();
            var        seen = new Dictionary<string, int>();
            foreach (Component component in root.GetComponentsInChildren<Component>(true))
            {
                if (component == null) continue; // пропавший скрипт
                string owner = HierarchyPath(component.transform, root.transform) + "|" + component.GetType().FullName;
                seen.TryGetValue(owner, out int n);
                seen[owner] = n + 1;

                var so = new SerializedObject(component);
                foreach (string field in Fields)
                {
                    SerializedProperty property = so.FindProperty(field);
                    if (property != null) map[$"{owner}#{n}.{field}"] = Value(property);
                }
            }

            return map;
        }

        private static string Value(SerializedProperty property)
        {
            switch (property.propertyType)
            {
                case SerializedPropertyType.String:  return property.stringValue;
                case SerializedPropertyType.Boolean: return property.boolValue ? "1" : "0";
                case SerializedPropertyType.Integer: return property.type == "ulong" ? property.ulongValue.ToString(CultureInfo.InvariantCulture) : property.longValue.ToString(CultureInfo.InvariantCulture);
                default:                             return Convert.ToString(property.boxedValue, CultureInfo.InvariantCulture);
            }
        }

        /// <summary>Изменённые значения (ключ: было → стало). Пустой список — идентификаторы целы.</summary>
        public static List<string> Changed(Dictionary<string, string> before, Dictionary<string, string> after) =>
            before.Where(pair => after.TryGetValue(pair.Key, out string value) && value != pair.Value)
                  .Select(pair => $"{pair.Key}: «{pair.Value}» → «{after[pair.Key]}»").ToList();

        /// <summary>Проверка после сохранения: при изменении идентификаторов — исключение с перечнем.</summary>
        public static void Verify(string assetPath, Dictionary<string, string> before)
        {
            List<string> changed = Changed(before, Snapshot(assetPath));
            if (changed.Count > 0)
            {
                throw new InvalidOperationException($"{assetPath}: сохранение изменило идентификаторы ({changed.Count}) — " +
                                                    $"восстановите их до коммита: {string.Join("; ", changed.Take(10))}");
            }
        }

        /// <summary>Путь по именам; одноимённые соседи различаются номером <c>[i]</c>.</summary>
        private static string HierarchyPath(Transform t, Transform root)
        {
            var parts = new List<string>();
            for (; t != null && t != root; t = t.parent)
            {
                Transform parent = t.parent;
                int       index  = 0;
                for (int i = 0; i < parent.childCount && parent.GetChild(i) != t; i++)
                {
                    if (parent.GetChild(i).name == t.name) index++;
                }

                parts.Add(index == 0 ? t.name : $"{t.name}[{index}]");
            }

            parts.Reverse();
            return parts.Count == 0 ? "." : string.Join("/", parts);
        }
    }
}

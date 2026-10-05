using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace VrBattlegrounds.Editor.Avatars.Workbench
{
    /// <summary>Межкарманные ссылки хранятся как адреса и восстанавливаются после инстанцирования всех шаблонов.</summary>
    internal static class AvatarPocketTemplateBindings
    {
        internal const string Path = "Assets/Prefabs/Player/Pockets/Bindings.json";
        [Serializable] internal sealed class Manifest { public int version = 1; public Binding[] bindings; }
        [Serializable] internal sealed class Binding
        {
            public string from, fromPath, fromType, property, to, toPath, toType;
            public int fromIndex, toIndex;
        }
        internal static Manifest Capture(Transform[] pockets)
        {
            var result = new List<Binding>();
            foreach (var pocket in pockets)
                foreach (var component in pocket.GetComponentsInChildren<Component>(true).Where(c => c))
                {
                    var so = new SerializedObject(component); var property = so.GetIterator();
                    while (property.Next(true))
                    {
                        // Родитель корня кармана — кость аватара; экспорт намеренно создаёт независимый корень.
                        if (component == pocket && property.propertyPath == "m_Father") continue;
                        if (property.propertyType != SerializedPropertyType.ObjectReference || !property.objectReferenceValue || EditorUtility.IsPersistent(property.objectReferenceValue)) continue;
                        var target = property.objectReferenceValue as Component;
                        var targetObject = property.objectReferenceValue as GameObject;
                        var transform = target ? target.transform : targetObject ? targetObject.transform : null;
                        if (!transform) throw new InvalidOperationException("Неэкспортируемая ссылка: " + component.name + "." + property.propertyPath);
                        if (transform == pocket || transform.IsChildOf(pocket)) continue;
                        var other = pockets.SingleOrDefault(p => transform == p || transform.IsChildOf(p));
                        if (!other) throw new InvalidOperationException("Карман ссылается за пределы экспортной группы: " + component.name + "." + property.propertyPath + " → " + transform.name);
                        result.Add(new Binding { from = pocket.name, fromPath = AnimationUtility.CalculateTransformPath(component.transform, pocket), fromType = component.GetType().FullName,
                            fromIndex = Array.IndexOf(component.GetComponents(component.GetType()), component), property = property.propertyPath,
                            to = other.name, toPath = AnimationUtility.CalculateTransformPath(transform, other), toType = target ? target.GetType().FullName : "GameObject",
                            toIndex = target ? Array.IndexOf(target.GetComponents(target.GetType()), target) : 0 });
                    }
                }
            return new Manifest { bindings = result.ToArray() };
        }
        internal static void ClearExternal(Transform copy, string name, Manifest manifest)
        {
            foreach (var binding in manifest.bindings.Where(b => b.from == name))
            {
                var component = ResolveComponent(Node(copy, binding.fromPath), binding.fromType, binding.fromIndex);
                var so = new SerializedObject(component); var property = so.FindProperty(binding.property);
                if (property == null) throw new InvalidOperationException("Экспортное поле не найдено: " + binding.property);
                property.objectReferenceValue = null; so.ApplyModifiedPropertiesWithoutUndo();
            }
        }
        internal static void Save(Manifest manifest, string output = Path)
        {
            if (!output.StartsWith("Assets/Prefabs/", StringComparison.Ordinal) || output.Contains("..") || !output.EndsWith("/Bindings.json", StringComparison.Ordinal))
                throw new InvalidOperationException("Недопустимая область manifest карманов.");
            File.WriteAllText(output, JsonUtility.ToJson(manifest, true), new System.Text.UTF8Encoding(false));
            AssetDatabase.ImportAsset(output, ImportAssetOptions.ForceSynchronousImport);
        }
        internal static void Apply(GameObject avatar, HashSet<string> created)
        {
            if (created.Count == 0 || !File.Exists(Path)) return;
            var manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText(Path));
            Apply(avatar, created, manifest);
        }
        internal static void Apply(GameObject avatar, HashSet<string> created, Manifest manifest)
        {
            if (manifest == null || manifest.version != 1 || manifest.bindings == null) throw new InvalidOperationException("Неизвестный формат межкарманных связей.");
            var transforms = avatar.GetComponentsInChildren<Transform>(true);
            // Проверяем все адреса до переназначения первого поля.
            var assignments = manifest.bindings.Where(b => created.Contains(b.from)).Select(binding =>
            {
                var from = transforms.Single(t => t.name == binding.from); var to = transforms.Single(t => t.name == binding.to);
                var component = ResolveComponent(Node(from, binding.fromPath), binding.fromType, binding.fromIndex);
                var target = Node(to, binding.toPath);
                UnityEngine.Object value = binding.toType == "GameObject" ? target.gameObject : ResolveComponent(target, binding.toType, binding.toIndex);
                var so = new SerializedObject(component); var property = so.FindProperty(binding.property);
                if (property == null || property.propertyType != SerializedPropertyType.ObjectReference) throw new InvalidOperationException("Поле межкарманной связи изменилось.");
                return (so, property, value);
            }).ToArray();
            foreach (var group in assignments.GroupBy(a => a.so.targetObject))
            {
                var so = new SerializedObject(group.Key);
                foreach (var assignment in group) so.FindProperty(assignment.property.propertyPath).objectReferenceValue = assignment.value;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
        }
        static Transform Node(Transform root, string path) => string.IsNullOrEmpty(path) ? root : root.Find(path) ?? throw new InvalidOperationException("Узел кармана не найден: " + path);
        static Component ResolveComponent(Transform root, string type, int index)
        {
            var matches = root.GetComponents<Component>().Where(c => c && c.GetType().FullName == type).ToArray();
            if (index < 0 || index >= matches.Length) throw new InvalidOperationException("Компонент кармана не найден: " + type);
            return matches[index];
        }
    }
}

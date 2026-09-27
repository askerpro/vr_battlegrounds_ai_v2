using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UltimateXR.Core.Components;
using UltimateXR.Extensions.Unity;
using UnityEditor;
using UnityEngine;

namespace VrBattlegrounds.Tests.Prefabs
{
    /// <summary>
    /// У каждого UXR-компонента префаба флаги <c>__isInPrefab</c> / <c>__prefabGuid</c> верные,
    /// а <c>_uxrUniqueId</c> в памяти редактора совпадает с записанным на диске.
    ///
    /// <para>
    /// <b>Зачем.</b> Канал состояния UltimateXR находит компонент по id. При неверных флагах
    /// <c>NotifyOnValidate</c> основного редактора на реимпорте префаба выдаёт компоненту новый
    /// случайный id и не сохраняет его. Хост MPPM живёт со случайными id, клон (патч 7) — с id
    /// из файла, и события отвергаются в обе стороны (MPPM-02, troubleshooting). Неверные флаги
    /// приносит <b>Apply to Prefab</b> с экземпляра на сцене и сохранение через
    /// <c>LoadPrefabContents</c>.
    /// </para>
    ///
    /// <para>
    /// Проверок две, потому что состояние бывает двух видов. После перезагрузки домена память
    /// совпадает с диском, но флаги неверные. После реимпорта флаги в памяти уже «исправлены»,
    /// зато id случайные и на диске их нет. Постпроцессор <c>UxrUniqueIdPersister</c> чинит
    /// оба автоматически; тест ловит, если он отвалился.
    /// </para>
    /// </summary>
    public class UxrUniqueIdOnDiskTests
    {
        private static readonly string[] Roots = { "Assets/Prefabs" };

        private static readonly Regex GuidPattern =
            new Regex("[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}");

        [Test]
        public void Флаги_и_id_UXR_компонентов_совпадают_с_диском()
        {
            var wrong = new List<string>();
            int checks = 0;

            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", Roots))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null) continue;

                HashSet<string> onDisk = ReadIdsOnDisk(path);
                int badFlags = 0, notOnDisk = 0;
                string example = null;

                foreach (UxrComponent component in prefab.GetComponentsInChildren<UxrComponent>(true))
                {
                    var so = new SerializedObject(component);
                    SerializedProperty id = so.FindProperty("_uxrUniqueId");
                    if (id == null) continue;
                    checks++;

                    if (!onDisk.Contains(id.stringValue))
                    {
                        notOnDisk++;
                        example ??= $"{component.GetType().Name} на '{component.name}': id {id.stringValue} нет на диске";
                    }

                    if (!component.GetPrefabGuid(out string actualGuid, out _)) continue;

                    if (so.FindProperty("__isInPrefab").boolValue != component.IsInPrefab() ||
                        so.FindProperty("__prefabGuid").stringValue != actualGuid)
                    {
                        badFlags++;
                        example ??= $"{component.GetType().Name} на '{component.name}': флаги " +
                                    $"({so.FindProperty("__isInPrefab").boolValue}, {so.FindProperty("__prefabGuid").stringValue}), " +
                                    $"фактически ({component.IsInPrefab()}, {actualGuid})";
                    }
                }

                if (badFlags + notOnDisk > 0)
                    wrong.Add($"{path}: флаги {badFlags}, id не с диска {notOnDisk}; например {example}");
            }

            Assert.That(checks, Is.GreaterThan(0), "Не найдено ни одного UXR-компонента — тест ничего не проверил.");
            Assert.IsEmpty(wrong,
                "UXR-компоненты префабов разойдутся между хостом и клиентом MPPM.\n" +
                "Исправляет Tools/VR Battlegrounds/VersionControl/Persist UltimateXR Unique Ids.\n" +
                string.Join("\n", wrong));
        }

        /// <summary>Id из файла префаба и файлов его баз и вложенных префабов.</summary>
        private static HashSet<string> ReadIdsOnDisk(string path)
        {
            var ids = new HashSet<string>();

            foreach (string dependency in AssetDatabase.GetDependencies(path, true))
            {
                if (!dependency.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase) || !File.Exists(dependency)) continue;

                foreach (Match m in GuidPattern.Matches(File.ReadAllText(dependency)))
                    ids.Add(m.Value);
            }

            return ids;
        }
    }
}

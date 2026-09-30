using System.Collections.Generic;
using NUnit.Framework;
using UltimateXR.Manipulation;
using UnityEditor;
using UnityEngine;

namespace VrBattlegrounds.Tests.Prefabs
{
    /// <summary>
    /// У каждой точки хвата задано, от чего мерить близость руки.
    ///
    /// <para>
    /// <b>Дефект.</b> У дополнительной точки револьвера (вторая рука) флаг <c>Grab Proximity Transform Use Self</c>
    /// был выключен при пустом <c>Grab Proximity Transform</c>: близость мерилась не от места ладони, а от корня
    /// оружия, свободная точка «не дотягивалась», и вторая рука брала основную — оружие перескакивало в левую руку
    /// (<c>GunTwoHandGrabTests</c>). Причина — копирование полей донора сборщиком пропускало всё с
    /// <c>_grabProximityTransform</c> в пути, включая флаг, а у нового элемента массива он по умолчанию выключен.
    /// </para>
    ///
    /// <para><b>Правило.</b> Use Self включён или трансформ близости назначен — у каждой точки каждого хватаемого предмета <c>Assets/Prefabs</c>.</para>
    /// </summary>
    public class GrabProximityTests
    {
        [Test]
        public void У_точки_хвата_есть_источник_близости()
        {
            var failures = new List<string>();
            int points = 0;

            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Prefabs" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null) continue;

                foreach (UxrGrabbableObject grabbable in prefab.GetComponentsInChildren<UxrGrabbableObject>(true))
                {
                    var so = new SerializedObject(grabbable);
                    SerializedProperty additional = so.FindProperty("_additionalGrabPoints");
                    for (int i = 0; i <= additional.arraySize; i++)
                    {
                        SerializedProperty point = i == 0 ? so.FindProperty("_grabPoint") : additional.GetArrayElementAtIndex(i - 1);
                        points++;

                        bool useSelf = point.FindPropertyRelative("_grabProximityTransformUseSelf").boolValue;
                        bool hasTransform = point.FindPropertyRelative("_grabProximityTransform").objectReferenceValue != null;
                        if (!useSelf && !hasTransform)
                            failures.Add($"{path} :: {grabbable.name} / точка {i}: Use Self выключен, трансформ близости пуст");
                    }
                }
            }

            Assert.Greater(points, 0, "Контроль: точек хвата не найдено.");
            Assert.IsEmpty(failures, "Близость руки мерится не от точки хвата:\n  " + string.Join("\n  ", failures));
        }
    }
}

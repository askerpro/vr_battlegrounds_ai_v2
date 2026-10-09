using System.Collections.Generic;
using NUnit.Framework;
using UltimateXR.Manipulation;
using UnityEditor;
using UnityEngine;
using VrBattlegrounds.Haptics;

namespace VrBattlegrounds.Tests.Haptics
{
    /// <summary>
    /// Отклик готовности второй руки (<see cref="InteractionFeedback" />, <c>tasks/haptics-system/Details.md</c>, п. 0.4):
    /// пустая рука тянется к предмету, который уже держит другая рука того же игрока, или к его части (цевьё, затвор) — гул
    /// тише, чем у первой руки (решение пользователя 2026-10-09 после проверки в шлеме).
    /// </summary>
    public class InteractionFeedbackTests
    {
        private const string GrabReadyPath = "Assets/Prefabs/Feedback/Interaction/Feedback_GrabReady.prefab";

        private readonly List<GameObject> _created = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject go in _created)
                if (go != null) Object.DestroyImmediate(go);
            _created.Clear();
        }

        private UxrGrabbableObject Grabbable(string name, Transform parent = null)
        {
            var go = new GameObject(name);
            if (parent != null) go.transform.SetParent(parent, false);
            else _created.Add(go);
            return go.AddComponent<UxrGrabbableObject>();
        }

        [Test]
        public void Вторая_рука_к_тому_же_предмету()
        {
            UxrGrabbableObject weapon = Grabbable("Weapon");
            Assert.IsTrue(InteractionFeedback.IsPartOfHeld(weapon, weapon));
        }

        [Test]
        public void Вторая_рука_к_части_удерживаемого_предмета()
        {
            UxrGrabbableObject weapon = Grabbable("Weapon");
            var mesh = new GameObject("MeshContainer");
            mesh.transform.SetParent(weapon.transform, false);
            UxrGrabbableObject slide = Grabbable("Slide", mesh.transform);

            Assert.IsTrue(InteractionFeedback.IsPartOfHeld(slide, weapon));
        }

        [Test]
        public void Первая_рука_если_другая_пуста_или_держит_другое()
        {
            UxrGrabbableObject weapon = Grabbable("Weapon");
            UxrGrabbableObject magazine = Grabbable("Magazine");

            Assert.IsFalse(InteractionFeedback.IsPartOfHeld(weapon, null), "другая рука пуста");
            Assert.IsFalse(InteractionFeedback.IsPartOfHeld(weapon, magazine), "другая рука держит другой предмет");
            Assert.IsFalse(InteractionFeedback.IsPartOfHeld(magazine, weapon), "предмет не часть удерживаемого");
        }

        [Test]
        public void Гул_готовности_второй_руки_тише_первой()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(GrabReadyPath);
            Assert.IsNotNull(prefab, $"Нет {GrabReadyPath}.");
            HapticPlayer player = prefab.GetComponentInChildren<HapticPlayer>(true);
            Assert.IsNotNull(player, "В префабе готовности нет HapticPlayer.");

            float secondary = player.Clip.SecondaryHandGain;
            Assert.That(secondary, Is.GreaterThan(0f).And.LessThan(1f),
                        "Вторая рука должна чувствовать готовность, но тише первой (SecondaryHandGain в (0; 1)).");
        }
    }
}

using System.Collections.Generic;
using NUnit.Framework;
using UltimateXR.Manipulation;
using UltimateXR.Mechanics.Weapons;
using UnityEditor;
using UnityEngine;
using VrBattlegrounds.Maps;

namespace VrBattlegrounds.Tests.Maps
{
    /// <summary>
    /// Мишень лобби (<see cref="ShootingTarget"/>): падает от пули в щит, не от пули в
    /// стойку, лежит и поднимается сама. Плюс состав префаба: мишень не хватается и её
    /// щит вообще досягаем для пуль оружия проекта.
    /// </summary>
    public class ShootingTargetTests
    {
        public const string PrefabPath = "Assets/Prefabs/Environment/Lobby/ShootingTarget.prefab";

        private readonly List<GameObject> _created = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject go in _created)
                if (go != null) Object.DestroyImmediate(go);
            _created.Clear();
        }

        private ShootingTarget CreateTarget(out Collider plate, out Collider post)
        {
            var root = new GameObject("Target");
            _created.Add(root);

            var postGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
            postGo.transform.SetParent(root.transform, false);
            post = postGo.GetComponent<Collider>();

            var pivot = new GameObject("Pivot").transform;
            pivot.SetParent(root.transform, false);
            pivot.localPosition = new Vector3(0f, 0.5f, 0f);

            var plateGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
            plateGo.transform.SetParent(pivot, false);
            plate = plateGo.GetComponent<Collider>();

            var target = root.AddComponent<ShootingTarget>();
            target.SetPivot(pivot);
            return target;
        }

        private static void Run(ShootingTarget target, float seconds)
        {
            for (float t = 0f; t < seconds; t += 0.02f) target.Step(0.02f);
        }

        [Test]
        public void Пуля_в_щит_роняет_мишень()
        {
            ShootingTarget target = CreateTarget(out Collider plate, out _);

            Assert.IsTrue(target.TryRegisterHit(plate));
            Run(target, 0.3f);

            Assert.AreEqual(1, target.HitCount);
            Assert.IsTrue(target.IsDown);
            Assert.Greater(target.Tilt, 45f, "Щит не завалился.");
            // Верх щита ушёл назад (к -Z мишени), а не на стрелка.
            Assert.Less(target.Pivot.up.z, -0.5f, $"Щит упал не туда: up={target.Pivot.up}");
        }

        [Test]
        public void Пуля_в_стойку_и_в_чужое_не_считается()
        {
            ShootingTarget target = CreateTarget(out _, out Collider post);
            ShootingTarget other = CreateTarget(out Collider otherPlate, out _);

            Assert.IsFalse(target.TryRegisterHit(post), "Попадание в стойку уронило щит.");
            Assert.IsFalse(target.TryRegisterHit(otherPlate), "Попадание в чужую мишень уронило эту.");
            Assert.IsFalse(target.TryRegisterHit(null));
            Assert.AreEqual(0, target.HitCount);
            Assert.IsFalse(target.IsDown);
            Assert.AreEqual(0, other.HitCount);
        }

        [Test]
        public void Лежачий_щит_не_считает_попаданий_и_поднимается_сам()
        {
            ShootingTarget target = CreateTarget(out Collider plate, out _);
            Quaternion rest = target.Pivot.localRotation;

            target.TryRegisterHit(plate);
            Run(target, 0.3f);
            Assert.IsFalse(target.TryRegisterHit(plate), "Лежачий щит засчитал второе попадание.");

            Run(target, 5f);

            Assert.IsFalse(target.IsDown, "Щит не поднялся.");
            Assert.AreEqual(0f, target.Tilt, 0.01f);
            Assert.Less(Quaternion.Angle(rest, target.Pivot.localRotation), 0.5f, "Щит не вернулся в стойку.");

            Assert.IsTrue(target.TryRegisterHit(plate), "Поднявшийся щит не принимает попаданий.");
            Assert.AreEqual(2, target.HitCount);
        }

        [Test]
        public void Префаб_мишени_не_хватается_и_досягаем_для_пуль()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Assert.IsNotNull(prefab, $"Нет префаба мишени {PrefabPath}.");

            var target = prefab.GetComponent<ShootingTarget>();
            Assert.IsNotNull(target, "На корне префаба нет ShootingTarget.");
            Assert.IsNotNull(target.Pivot, "У мишени не назначен щит.");

            Assert.IsEmpty(prefab.GetComponentsInChildren<UxrGrabbableObject>(true),
                "Мишень хватается — тест поз хвата потребует для неё позу.");
            Assert.IsEmpty(prefab.GetComponentsInChildren<Rigidbody>(true),
                "У мишени Rigidbody — пуля будет толкать её (AddForceAtPosition), а тег Environment с неё снимется.");

            var solid = new List<Collider>();
            foreach (Collider c in target.Pivot.GetComponentsInChildren<Collider>(true))
                if (!c.isTrigger && c.enabled) solid.Add(c);
            Assert.IsNotEmpty(solid, "У щита нет твёрдого коллайдера — пуле не во что попасть.");

            int checkedWeapons = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Prefabs/Weapons" }))
            {
                var weapon = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
                foreach (UxrProjectileSource source in weapon.GetComponentsInChildren<UxrProjectileSource>(true))
                {
                    foreach (UxrShotDescriptor shot in source.ShotTypes)
                    {
                        checkedWeapons++;
                        foreach (Collider c in solid)
                            Assert.AreNotEqual(0, shot.CollisionLayerMask.value & (1 << c.gameObject.layer),
                                $"{weapon.name}: пуля не видит слой щита '{LayerMask.LayerToName(c.gameObject.layer)}'.");
                    }
                }
            }

            Assert.Greater(checkedWeapons, 0, "Не нашлось ни одного оружия — сверять слой не с чем.");
        }
    }
}

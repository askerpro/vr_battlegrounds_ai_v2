using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using VrBattlegrounds.Arsenal;
using VrBattlegrounds.Tests.Arsenal;

namespace VrBattlegrounds.Tests.ArsenalWall
{
    /// <summary>
    /// Общая игровая станция в новом флоу: корпус без слотов, ряды корпуса вешают слоты пресета, сборщик слота
    /// ставит карточку по раскладке. Слоты собираются исполнителями сборщика (<see cref="ArsenalTestStation.BuildSlots" />).
    /// </summary>
    public class ArsenalStationPresentationTests
    {
        private const string Station = "Assets/Prefabs/Arsenal/CommonOpenArsenalStation.prefab";
        private const string GameplayPreset = "Assets/Data/Weapons/CurrentGameplayArsenal.asset";

        [Test]
        public void Корпус_без_слотов_и_ряд_полки_едет_с_нижней_частью()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Station);
            Assert.That(prefab.GetComponentsInChildren<ArsenalSlotController>(true), Is.Empty, "Слоты в корпусе — только от генератора.");
            var rows = prefab.GetComponentsInChildren<ArsenalSlotRow>(true);
            CollectionAssert.AreEquivalent(new[] { "pegboard", "shelf" }, rows.Select(r => r.RowKey).ToArray());
            var lower = prefab.GetComponent<ArsenalEquipmentPoses>().Targets.Single(p => p.ClosedPose.name.Contains("Lower"));
            Assert.That(rows.Single(r => r.RowKey == "shelf").transform.IsChildOf(lower.Target), Is.True,
                "Полка должна выезжать вместе с нижней частью корпуса.");
        }

        [Test]
        public void Пять_слотов_в_каждом_ряду_и_MKR9_на_полке()
        {
            var instance = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Station));
            try
            {
                var built = ArsenalTestStation.BuildSlots(instance, GameplayPreset);
                foreach (var row in instance.GetComponentsInChildren<ArsenalSlotRow>(true))
                    Assert.That(row.GetComponentsInChildren<FirearmSlotController>(true).Length, Is.EqualTo(5), row.RowKey);
                var mkr9 = built.Single(b => b.Manifest.Entry.WeaponResource.name.Contains("MKR9"));
                Assert.That(mkr9.Row.RowKey, Is.EqualTo("shelf"));
                Assert.That(mkr9.Slot.transform.parent, Is.SameAs(mkr9.Row.transform));
            }
            finally { Object.DestroyImmediate(instance); }
        }

        [Test]
        public void Карточка_одна_на_слот_и_на_полке_лежит()
        {
            var instance = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Station));
            try
            {
                var built = ArsenalTestStation.BuildSlots(instance, GameplayPreset);
                Assert.That(built.Count, Is.EqualTo(10));
                foreach (var b in built)
                {
                    var card = b.Slot.GetComponentsInChildren<ArsenalPriceTag>(true).Single();
                    Assert.That(ArsenalPriceTag.Create(b.Slot, b.Presentation), Is.SameAs(card), b.Slot.name);
                    Assert.That(b.Slot.GetComponentsInChildren<ArsenalPriceTag>(true).Length, Is.EqualTo(1));
                    if (b.Row.RowKey == "shelf")
                        Assert.That(Mathf.Abs(Vector3.Dot(card.transform.forward, Vector3.up)), Is.GreaterThan(.99f),
                            "Карточка слота полки должна лежать на горизонтальной панели: " + b.Slot.name);
                }
            }
            finally { Object.DestroyImmediate(instance); }
        }

        [Test]
        public void RuntimeRetractionLeavesBodyAndItsCollidersUnchanged()
        {
            var instance = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Station));
            try
            {
                var deployment = instance.GetComponent<ArsenalDeploymentAnimator>();
                var body = deployment.PresentationRoot;
                var position = body.localPosition;
                var rotation = body.localRotation;
                var colliders = body.GetComponentsInChildren<Collider>(true);
                var enabled = colliders.Select(c => c.enabled).ToArray();
                typeof(ArsenalDeploymentAnimator).GetField("_motion", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(deployment, new ArsenalDeploymentAnimator.DeploymentMotion { From = 0, To = 0 });
                typeof(ArsenalDeploymentAnimator).GetMethod("ApplyPose", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(deployment, null);
                Assert.That(body.localPosition, Is.EqualTo(position));
                Assert.That(body.localRotation, Is.EqualTo(rotation));
                CollectionAssert.AreEqual(enabled, colliders.Select(c => c.enabled).ToArray());
                Assert.That(deployment.IsRetracted, Is.True);
                Assert.That(deployment.ReadyForAccess, Is.False);
            }
            finally { Object.DestroyImmediate(instance); }
        }
    }
}

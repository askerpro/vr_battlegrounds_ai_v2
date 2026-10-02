using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using VrBattlegrounds.Arsenal;

namespace VrBattlegrounds.Tests.ArsenalWall
{
    public class ArsenalStationPresentationTests
    {
        private const string Station = "Assets/Prefabs/Arsenal/CommonOpenArsenalStation.prefab";

        [Test]
        public void SavedStationHasFiveSlotsPerRowAndMkr9OnShelf()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Station);
            var poses = prefab.GetComponent<ArsenalEquipmentPoses>();
            Assert.That(poses.Targets.Length, Is.EqualTo(2));
            foreach (var row in poses.Targets)
                Assert.That(row.Target.GetComponentsInChildren<FirearmSlotController>(true).Length, Is.EqualTo(5), row.Target.name);
            var lower = poses.Targets.Single(p => p.ClosedPose.name.Contains("Lower"));
            Assert.That(lower.Target.GetComponentsInChildren<FirearmSlotController>(true)
                .Any(s => s.WeaponData != null && s.WeaponData.name.Contains("MKR9")), Is.True);
        }

        [Test]
        public void CardsAreSavedAndRuntimeCreationReusesThem()
        {
            var instance = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Station));
            try
            {
                var slots = instance.GetComponentsInChildren<FirearmSlotController>(true);
                Assert.That(slots.Length, Is.EqualTo(10));
                foreach (var slot in slots)
                {
                    var card = slot.GetComponentsInChildren<ArsenalPriceTag>(true).Single();
                    Assert.That(ArsenalPriceTag.Create(slot), Is.SameAs(card), slot.name);
                    Assert.That(slot.GetComponentsInChildren<ArsenalPriceTag>(true).Length, Is.EqualTo(1));
                }
                var lower = instance.GetComponent<ArsenalEquipmentPoses>().Targets.Single(p => p.ClosedPose.name.Contains("Lower"));
                foreach (var card in lower.Target.GetComponentsInChildren<ArsenalPriceTag>(true))
                    Assert.That(Mathf.Abs(Vector3.Dot(card.transform.forward, Vector3.up)), Is.GreaterThan(.99f),
                        "Карточка нижнего слота должна лежать на горизонтальной панели.");
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

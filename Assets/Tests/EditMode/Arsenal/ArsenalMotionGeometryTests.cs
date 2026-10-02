using System.Linq;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using VrBattlegrounds.Arsenal;

namespace VrBattlegrounds.Tests.Arsenal
{
    public sealed class ArsenalMotionGeometryTests
    {
        private Scene _scene;
        [SetUp] public void SetUp() => _scene = EditorSceneManager.NewPreviewScene();
        [TearDown] public void TearDown() => EditorSceneManager.ClosePreviewScene(_scene);

        [TestCase(false, true)]
        [TestCase(true, true)]
        [TestCase(true, false)]
        public void AuditDetectsIntermediateCollisionWithoutMovingOriginal(bool obstructed, bool wallCollider)
        {
            var equipment = Station(Vector3.left, Vector3.right, Vector3.one * .2f);
            var wall = Cube("Wall");
            wall.transform.localScale = Vector3.one * .1f;
            wall.transform.position = obstructed ? Vector3.zero : Vector3.forward * 3f;
            wall.GetComponent<BoxCollider>().enabled = wallCollider;
            var target = equipment.Targets[0].Target;
            Vector3 original = target.position;
            string[] errors = ArsenalMotionGeometryAudit.Audit(_scene).ToArray();
            Assert.That(errors.Length > 0, Is.EqualTo(obstructed), string.Join("\n", errors));
            Assert.That(target.position, Is.EqualTo(original), "Исходная сцена должна оставаться неизменной.");
        }

        [Test]
        public void RotationSweepDetectsObstacleClearAtBothEndpoints()
        {
            var equipment = Station(Vector3.zero, Vector3.zero, new Vector3(2f, .1f, .1f));
            equipment.Targets[0].OpenPose.rotation = Quaternion.Euler(0, 90, 0);
            var wall = Cube("RotationWall");
            wall.transform.position = new Vector3(.6f, 0, -.6f);
            wall.transform.localScale = Vector3.one * .08f;
            string[] errors = ArsenalMotionGeometryAudit.Audit(_scene).ToArray();
            Assert.That(errors.Length, Is.EqualTo(1), string.Join("\n", errors));
            StringAssert.Contains("RotationWall", errors[0]);
        }

        [Test]
        public void ThinObstacleBetweenOldFixedSamplesIsDetected()
        {
            Station(Vector3.left, Vector3.right, Vector3.one * .004f);
            var wall = Cube("ThinWall");
            wall.transform.position = Vector3.right * .015f;
            wall.transform.localScale = new Vector3(.002f, .1f, .1f);
            Assert.That(ArsenalMotionGeometryAudit.Audit(_scene), Is.Not.Empty);
        }

        [Test]
        public void TriggerAndHiddenVisualAreNotSolidWall()
        {
            Station(Vector3.left, Vector3.right, Vector3.one * .2f);
            var mask = Cube("BoundaryMask");
            mask.GetComponent<BoxCollider>().isTrigger = true;
            mask.GetComponent<Renderer>().enabled = false;
            Assert.That(ArsenalMotionGeometryAudit.Audit(_scene), Is.Empty);
        }

        [Test]
        public void MissingMarkersAreReportedInsteadOfSkipped()
        {
            var equipment = NewObject("BrokenStation").AddComponent<ArsenalEquipmentPoses>();
            equipment.Configure(new[] { new ArsenalEquipmentPoses.PoseTarget() });
            Assert.That(ArsenalMotionGeometryAudit.Audit(_scene), Is.Not.Empty);
        }

        private ArsenalEquipmentPoses Station(Vector3 closedPosition, Vector3 openPosition, Vector3 size)
        {
            var station = NewObject("Station");
            var equipment = station.AddComponent<ArsenalEquipmentPoses>();
            var shelf = Cube("Shelf");
            shelf.transform.SetParent(station.transform, false);
            shelf.transform.localScale = size;
            shelf.transform.position = closedPosition;
            // Подвижный меш нужно проверять, даже если его физический коллайдер временно отключён.
            shelf.GetComponent<BoxCollider>().enabled = false;
            var closed = NewObject("Closed");
            closed.transform.position = closedPosition;
            var open = NewObject("Open");
            open.transform.position = openPosition;
            equipment.Configure(new[] { new ArsenalEquipmentPoses.PoseTarget
                { Target = shelf.transform, ClosedPose = closed.transform, OpenPose = open.transform } });
            return equipment;
        }

        private GameObject Cube(string name)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            SceneManager.MoveGameObjectToScene(go, _scene);
            go.name = name;
            return go;
        }

        private GameObject NewObject(string name)
        {
            var go = new GameObject(name);
            SceneManager.MoveGameObjectToScene(go, _scene);
            return go;
        }
    }
}

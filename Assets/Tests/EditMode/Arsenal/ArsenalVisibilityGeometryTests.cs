using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace VrBattlegrounds.Tests.Arsenal
{
    public sealed class ArsenalVisibilityGeometryTests
    {
        private Scene _scene;
        [SetUp] public void SetUp() => _scene = EditorSceneManager.NewPreviewScene();
        [TearDown] public void TearDown() => EditorSceneManager.ClosePreviewScene(_scene);

        [TestCase(true, true)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        public void VisibleWallIsDetectedWithOrWithoutCollider(bool visible, bool collider)
        {
            var wall = Wall("BlockingWall", new Vector3(0, 1.7f, 0));
            wall.GetComponent<Renderer>().enabled = visible;
            wall.GetComponent<Collider>().enabled = collider;
            var original = wall.transform.position;
            var blocker = ArsenalVisibilityGeometryAudit.FirstBlocker(_scene,
                new Vector3(0, 1.7f, -1), new Vector3(0, 1.7f, 1));
            Assert.That(blocker != null, Is.EqualTo(visible));
            if (visible) Assert.That(blocker, Is.EqualTo("BlockingWall"));
            Assert.That(wall.transform.position, Is.EqualTo(original));
        }

        [TestCase(-2f)]
        [TestCase(2f)]
        public void WallOutsideSightSegmentDoesNotBlock(float wallZ)
        {
            Wall("Outside", new Vector3(0, 1.7f, wallZ));
            Assert.That(ArsenalVisibilityGeometryAudit.FirstBlocker(_scene,
                new Vector3(0, 1.7f, -1), new Vector3(0, 1.7f, 1)), Is.Null);
        }

        [Test]
        public void ReturnsNearestWallRegardlessOfHierarchyOrder()
        {
            Wall("Far", new Vector3(0, 1.7f, .6f));
            Wall("Near", new Vector3(0, 1.7f, -.4f));
            Assert.That(ArsenalVisibilityGeometryAudit.FirstBlocker(_scene,
                new Vector3(0, 1.7f, -1), new Vector3(0, 1.7f, 1)), Is.EqualTo("Near"));
        }

        [Test]
        public void RotatedAndScaledWallIsDetected()
        {
            var wall = Wall("Rotated", new Vector3(0, 1.7f, 0));
            wall.transform.rotation = Quaternion.Euler(0, 37, 0);
            wall.transform.localScale = new Vector3(2f, 1f, .02f);
            Assert.That(ArsenalVisibilityGeometryAudit.FirstBlocker(_scene,
                new Vector3(0, 1.7f, -1), new Vector3(0, 1.7f, 1)), Is.EqualTo("Rotated"));
        }

        [Test]
        public void EmptyPartOfMeshBoundsDoesNotProduceFalseOcclusion()
        {
            var wall = Wall("Triangle", new Vector3(0, 1.7f, 0));
            var mesh = new Mesh();
            try
            {
                // Луч пересекает bounds, но проходит справа от единственного треугольника.
                mesh.vertices = new[] { new Vector3(-1, -1, 0), new Vector3(-1, 1, 0), new Vector3(1, 1, 0) };
                mesh.triangles = new[] { 0, 1, 2 };
                mesh.RecalculateBounds();
                wall.GetComponent<MeshFilter>().sharedMesh = mesh;
                Assert.That(ArsenalVisibilityGeometryAudit.FirstBlocker(_scene,
                    new Vector3(.3f, 1.4f, -1), new Vector3(.3f, 1.4f, 1)), Is.Null);
            }
            finally { Object.DestroyImmediate(mesh); }
        }

        [Test]
        public void UnreadableBlockingMeshFailsExplicitlyInsteadOfPassing()
        {
            var wall = Wall("Unreadable", new Vector3(0, 1.7f, 0));
            Mesh mesh = Object.Instantiate(wall.GetComponent<MeshFilter>().sharedMesh);
            try
            {
                mesh.UploadMeshData(true);
                wall.GetComponent<MeshFilter>().sharedMesh = mesh;
                Assert.Throws<System.InvalidOperationException>(() => ArsenalVisibilityGeometryAudit.FirstBlocker(_scene,
                    new Vector3(0, 1.7f, -1), new Vector3(0, 1.7f, 1)));
            }
            finally { Object.DestroyImmediate(mesh); }
        }

        [Test]
        public void TargetGeometryDoesNotOccludeItself()
        {
            var target = Wall("Target", new Vector3(0, 1.7f, 1));
            Assert.That(ArsenalVisibilityGeometryAudit.FirstBlocker(_scene,
                new Vector3(0, 1.7f, -1), target.transform.position, target.transform), Is.Null);
        }

        [Test]
        public void PartialOcclusionIsDetectedAwayFromTargetCenter()
        {
            var wall = Wall("PartialCover", new Vector3(.35f, 1.7f, 0));
            wall.transform.localScale = new Vector3(.1f, 1f, .1f);
            Vector3 eye = new Vector3(0, 1.7f, -1);
            Assert.That(ArsenalVisibilityGeometryAudit.FirstBlocker(_scene, eye,
                new Vector3(0, 1.7f, 1)), Is.Null);
            Assert.That(ArsenalVisibilityGeometryAudit.FirstBlocker(_scene, eye,
                new Vector3(.7f, 1.7f, 1)), Is.EqualTo("PartialCover"));
        }

        [Test]
        public void OtherLoadedSceneCannotOccludeAuditedScene()
        {
            Scene other = EditorSceneManager.NewPreviewScene();
            try
            {
                var wall = Wall("OtherScene", new Vector3(0, 1.7f, 0));
                SceneManager.MoveGameObjectToScene(wall, other);
                Assert.That(ArsenalVisibilityGeometryAudit.FirstBlocker(_scene,
                    new Vector3(0, 1.7f, -1), new Vector3(0, 1.7f, 1)), Is.Null);
            }
            finally { EditorSceneManager.ClosePreviewScene(other); }
        }

        private GameObject Wall(string name, Vector3 position)
        {
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            SceneManager.MoveGameObjectToScene(wall, _scene);
            wall.name = name;
            wall.transform.position = position;
            wall.transform.localScale = new Vector3(1f, 1f, .1f);
            return wall;
        }
    }
}

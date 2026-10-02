using NUnit.Framework;
using UnityEngine;
using VrBattlegrounds.Arsenal;

namespace VrBattlegrounds.Tests
{
    public class ArsenalEquipmentMotionTests
    {
        private GameObject _root;
        private ArsenalEquipmentPoses _equipment;
        private Transform _target;

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("EquipmentFixture");
            _equipment = _root.AddComponent<ArsenalEquipmentPoses>();
            _target = Child("Shelf", Vector3.zero);
            var closed = Child("Closed", new Vector3(0, 1, .2f));
            var open = Child("Open", new Vector3(0, 1.2f, -.4f));
            open.localRotation = Quaternion.Euler(25, 0, 0);
            _equipment.Configure(new[] { new ArsenalEquipmentPoses.PoseTarget
                { Target = _target, ClosedPose = closed, OpenPose = open } });
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_root);

        [TestCase(0f, 0f)]
        [TestCase(.5f, .5f)]
        [TestCase(1f, 1f)]
        public void EquipmentMovesThroughoutWholeAnimation(float progress, float expected)
        {
            _equipment.Apply(progress);
            var pose = _equipment.Targets[0];
            Assert.That(Vector3.Distance(_target.position,
                Vector3.Lerp(pose.ClosedPose.position, pose.OpenPose.position, expected)), Is.LessThan(.00001f));
            Assert.That(Quaternion.Angle(_target.rotation,
                Quaternion.Slerp(pose.ClosedPose.rotation, pose.OpenPose.rotation, expected)), Is.LessThan(.001f));
        }

        [Test]
        public void EditedOpenMarkerIsAppliedImmediatelyWithoutMovingBody()
        {
            _root.transform.SetPositionAndRotation(new Vector3(4, 0, 3), Quaternion.Euler(0, 90, 0));
            var bodyPosition = _root.transform.position;
            var pose = _equipment.Targets[0];
            pose.OpenPose.localPosition += new Vector3(.1f, 0, -.1f);
            _equipment.Apply(1f);
            Assert.That(Vector3.Distance(_target.position, pose.OpenPose.position), Is.LessThan(.00001f));
            Assert.That(_root.transform.position, Is.EqualTo(bodyPosition));
        }

        [Test]
        public void RetractionReachesClosedPoseAndLeavesBodyColliderEnabled()
        {
            var collider = _root.AddComponent<BoxCollider>();
            _equipment.Apply(1f);
            _equipment.Apply(.5f);
            _equipment.Apply(0f);
            Assert.That(Vector3.Distance(_target.position, _equipment.Targets[0].ClosedPose.position), Is.LessThan(.00001f));
            Assert.That(collider.enabled, Is.True);
        }

        [Test]
        public void MotionSnapshotSupportsLateClientAndMidMotionReversal()
        {
            var motion = new ArsenalDeploymentAnimator.DeploymentMotion
                { StartedAt = 100, From = 0, To = 1, Duration = 2 };
            Assert.That(ArsenalDeploymentAnimator.Evaluate(motion, 99), Is.Zero);
            Assert.That(ArsenalDeploymentAnimator.Evaluate(motion, 101), Is.EqualTo(.5f).Within(.00001f));
            Assert.That(ArsenalDeploymentAnimator.Evaluate(motion, 105), Is.EqualTo(1));
            var reverse = new ArsenalDeploymentAnimator.DeploymentMotion
                { StartedAt = 101, From = .5f, To = 0, Duration = 1 };
            Assert.That(ArsenalDeploymentAnimator.Evaluate(reverse, 101), Is.EqualTo(.5f));
            Assert.That(ArsenalDeploymentAnimator.Evaluate(reverse, 102), Is.Zero);
        }

        private Transform Child(string name, Vector3 localPosition)
        {
            var child = new GameObject(name).transform;
            child.SetParent(_root.transform, false);
            child.localPosition = localPosition;
            return child;
        }
    }
}

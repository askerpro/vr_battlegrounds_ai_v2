using System;
using UnityEngine;

namespace VrBattlegrounds.Arsenal
{
    /// <summary>Редактируемые позы оборудования; используют общий прогресс станции без второго таймера.</summary>
    public sealed class ArsenalEquipmentPoses : MonoBehaviour
    {
        [Serializable]
        public struct PoseTarget
        {
            public Transform Target;
            public Transform ClosedPose;
            public Transform OpenPose;
        }

        [SerializeField] private PoseTarget[] _targets = Array.Empty<PoseTarget>();
        [SerializeField] private Bounds _foldedBoundsLocal;
        public PoseTarget[] Targets => _targets;
        public Bounds FoldedBoundsLocal => _foldedBoundsLocal;
        public void ConfigureFoldedBounds(Bounds bounds) => _foldedBoundsLocal = bounds;
        public void Configure(PoseTarget[] targets) => _targets = targets;

        // Содержимое движется весь интервал; корпус станции остаётся неподвижным.
        public static float EquipmentFraction(float progress)
        {
            float t = Mathf.Clamp01(progress);
            return t * t * (3f - 2f * t);
        }

        public void Apply(float progress)
        {
            float t = EquipmentFraction(progress);
            foreach (var pose in _targets)
            {
                if (pose.Target == null || pose.ClosedPose == null || pose.OpenPose == null) continue;
                pose.Target.SetPositionAndRotation(Vector3.Lerp(pose.ClosedPose.position, pose.OpenPose.position, t),
                    Quaternion.Slerp(pose.ClosedPose.rotation, pose.OpenPose.rotation, t));
            }
        }

        private void OnDrawGizmosSelected()
        {
            foreach (var pose in _targets)
            {
                if (pose.ClosedPose == null || pose.OpenPose == null) continue;
                Gizmos.color = Color.yellow; Gizmos.DrawWireSphere(pose.ClosedPose.position, .025f);
                Gizmos.color = Color.cyan; Gizmos.DrawWireSphere(pose.OpenPose.position, .025f);
                Gizmos.DrawLine(pose.ClosedPose.position, pose.OpenPose.position);
            }
        }
    }
}

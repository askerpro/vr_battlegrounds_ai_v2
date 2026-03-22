// --------------------------------------------------------------------------------------------------------------------
// <copyright file="SceneManagerExt.cs" company="VRMADA">
//   Copyright (c) VRMADA, All rights reserved.
// </copyright>
// --------------------------------------------------------------------------------------------------------------------
using UnityEngine;
using UnityEngine.SpatialTracking;

namespace UltimateXR.Extensions.Unity
{
    /// <summary>
    ///     <see cref="TrackedPoseDriver" /> extensions.
    /// </summary>
    public class TrackedPoseDriverExt : TrackedPoseDriver
    {
        #region Public Methods

        public Vector3 allowed_position_axies = Vector3.one;
        public Vector3 allowed_rotation_axies = Vector3.one;

        private Pose _lastDeviceOutput;

        public void MoveTo(Pose newPose)
        {
            m_OriginPose = GetTransformSource(newPose, _lastDeviceOutput);
        }

        public Pose GetTransformSource(Pose p1, Pose p2)
        {
            Pose p3 = new Pose();
            p3.rotation = p1.rotation * Quaternion.Inverse(p2.rotation);
            p3.position = p1.position - (p3.rotation * p2.position);
            return p3;
        }

        PoseDataFlags GetPoseData(DeviceType device, TrackedPose poseSource, out Pose resultPose)
        {
            return poseProviderComponent != null
                ? poseProviderComponent.GetPoseFromProvider(out resultPose)
                : PoseDataSource.GetDataFromSource(poseSource, out resultPose);
        }

        protected Pose ApplyAxisLimits(Pose p)
        {
            Vector3 limitedPosition = Vector3.Scale(p.position, allowed_position_axies);

            Quaternion limitedRotation = Quaternion.Euler(Vector3.Scale(p.rotation.eulerAngles, allowed_rotation_axies));

            return new Pose(limitedPosition, limitedRotation);
        }


        protected override void PerformUpdate()
        {
            if (!enabled)
                return;
            PoseDataFlags poseFlags = GetPoseData(deviceType, poseSource, out _lastDeviceOutput);

            _lastDeviceOutput = ApplyAxisLimits(_lastDeviceOutput);

            if (poseFlags != PoseDataFlags.NoData)
            {
                Pose localPose = TransformPoseByOriginIfNeeded(_lastDeviceOutput);
                SetLocalTransform(localPose.position, localPose.rotation, poseFlags);
            }
        }

        #endregion
    }
}

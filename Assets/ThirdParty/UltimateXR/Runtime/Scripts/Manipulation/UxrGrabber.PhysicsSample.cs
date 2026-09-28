// --------------------------------------------------------------------------------------------------------------------
// <copyright file="UxrGrabber.PhysicsSample.cs" company="VRMADA">
//   Copyright (c) VRMADA, All rights reserved.
// </copyright>
// --------------------------------------------------------------------------------------------------------------------
using UnityEngine;

namespace UltimateXR.Manipulation
{
    public partial class UxrGrabber
    {
        #region Private Types & Data

        /// <summary>
        ///     Stores physics data of a frame to perform smooth throw computations.
        ///     VR Battlegrounds patch 19: struct вместо class — сэмплы живут в кольцевом буфере
        ///     граббера без аллокаций в каждом кадре. Математика прежняя. См. Docs/UltimateXR/sdk-patches.md.
        /// </summary>
        private struct PhysicsSample
        {
            #region Public Types & Data

            /// <summary>
            ///     Gets the sampled center of mass world position.
            /// </summary>
            public Vector3 CenterOfMass { get; }

            /// <summary>
            ///     Gets the sampled finger tip world position.
            /// </summary>
            public Vector3 Tip { get; }

            /// <summary>
            ///     Gets the sampled rotation.
            /// </summary>
            public Quaternion Rotation { get; }

            /// <summary>
            ///     Gets the sampled angular speed in Euler angles.
            /// </summary>
            public Vector3 EulerSpeed { get; }

            /// <summary>
            ///     Gets the sampled linear velocity in world space.
            /// </summary>
            public Vector3 Velocity { get; }

            /// <summary>
            ///     Gets the sampled total velocity in world space. It is the result of combining the linear velocity plus the velocity
            ///     due to the throw axis angular speed. This should be used to compute throw release velocity.
            /// </summary>
            public Vector3 TotalVelocity { get; }

            /// <summary>
            ///     Gets the elapsed time in seconds with respect to the previous sample.
            /// </summary>
            public float DeltaTime { get; }

            /// <summary>
            ///     Sample age in seconds. VR Battlegrounds patch 19: поле, а не свойство — меняется прямо
            ///     в элементе массива кольцевого буфера.
            /// </summary>
            public float Age;

            #endregion

            #region Constructors & Finalizer

            /// <summary>
            ///     Constructor.
            /// </summary>
            /// <param name="lastSample">Last frame data, to compute velocities</param>
            /// <param name="hasLastSample">Whether <paramref name="lastSample" /> is valid (replaces the former null check)</param>
            /// <param name="sampledTransform">Transform, from the grabbed object if there is one currently being grabbed, otherwise from the grabber</param>
            /// <param name="centerOfMass">World position of the throwing center of mass</param>
            /// <param name="tip">World position of the finger tip approximation, to account for angular velocity in the throw</param>
            /// <param name="deltaTime">Time in seconds since last frame</param>
            public PhysicsSample(in PhysicsSample lastSample, bool hasLastSample, Transform sampledTransform, Vector3 centerOfMass, Vector3 tip, float deltaTime)
            {
                Age             = 0.0f;
                Rotation        = sampledTransform.rotation;
                DeltaTime       = deltaTime;
                CenterOfMass    = centerOfMass;
                Tip             = tip;

                if (hasLastSample)
                {
                    // Angular

                    Vector3 v1           = lastSample.Tip - lastSample.CenterOfMass;
                    Vector3 v2           = tip - centerOfMass;
                    Vector3 rotationAxis = Vector3.Cross(v2, v1);

                    Quaternion relative = Quaternion.Inverse(lastSample.Rotation) * sampledTransform.rotation;
                    relative.ToAngleAxis(out float angle, out Vector3 axis);
                    EulerSpeed = (angle * sampledTransform.TransformDirection(axis)) / deltaTime;

                    // Linear. TODO: Improve using a mix of linear and angular components?

                    // VR Battlegrounds patch 19: через локальную переменную — в конструкторе struct
                    // нельзя читать свойство this до присвоения всех полей (CS0188).
                    Vector3 velocity = ((tip - lastSample.Tip) / deltaTime);
                    Velocity      = velocity;
                    TotalVelocity = velocity;
                }
                else
                {
                    EulerSpeed    = Vector3.zero;
                    Velocity      = Vector3.zero;
                    TotalVelocity = Vector3.zero;
                }
            }

            #endregion
        }

        #endregion
    }
}
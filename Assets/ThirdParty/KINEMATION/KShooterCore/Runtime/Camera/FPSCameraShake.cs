// Copyright (c) 2026 KINEMATION.
// All rights reserved.

using KINEMATION.Shared.KAnimationCore.Runtime.Attributes;
using KINEMATION.Shared.KAnimationCore.Runtime.Core;
using UnityEngine;

namespace KINEMATION.KShooterCore.Runtime.Camera
{
    [CreateAssetMenu(fileName = "NewCameraShake", menuName = ShooterUtility.ShooterMenuPath + "Camera Shake")]
    public class FPSCameraShake : ScriptableObject
    {
        [Unfold] public VectorCurve shakeCurve = VectorCurve.Constant(0f, 1f, 0f);
        public Vector2 pitch;
        public Vector2 yaw;
        public Vector2 roll;
        [Min(0f)] public float smoothSpeed;
        [Min(0f)] public float playRate = 1f;

        public static float GetTarget(Vector2 value)
        {
            return Random.Range(value.x, value.y);
        }
    }
}
// Copyright (c) 2026 KINEMATION.
// All rights reserved.

using System;
using KINEMATION.Shared.KAnimationCore.Runtime.Core;
using UnityEngine;

namespace KINEMATION.TacticalShooterPack.Scripts.Animation
{
    [Serializable]
    public struct WeaponSway
    {
        public VectorSpring position;
        public VectorSpring rotation;
        public Vector3 clampPosition;
        public Vector3 clampRotation;
        public float dampingFactor;
        public float adsScale;

        public static WeaponSway identity = new WeaponSway()
        {
            position = VectorSpring.identity,
            rotation = VectorSpring.identity,
            dampingFactor = 0f
        };

        public static WeaponSway shooterAimPreset = new WeaponSway()
        {
            position = new VectorSpring()
            {
                damping = new Vector3(0.4f, 0.4f, 0.4f),
                stiffness = new Vector3(0.4f, 0.4f, 0.8f),
                speed = new Vector3(7f, 7f, 7f),
                scale = new Vector3(1f, 1f, 1f),
            },
            rotation = new VectorSpring()
            {
                damping = new Vector3(0.4f, 0.4f, 0.3f),
                stiffness = new Vector3(0.8f, 0.8f, 0.8f),
                speed = new Vector3(15f, 20f, 15f),
                scale = new Vector3(-2f, 2f, -2f),
            },
            dampingFactor = 8f
        };
        
        public static WeaponSway shooterMovePreset = new WeaponSway()
        {
            position = new VectorSpring()
            {
                damping = new Vector3(0.4f, 0.4f, 0.4f),
                stiffness = new Vector3(0.8f, 0.8f, 0.8f),
                speed = new Vector3(7f, 7f, 7f),
                scale = new Vector3(1f, 0f, 1f),
            },
            rotation = new VectorSpring()
            {
                damping = new Vector3(0.4f, 0.4f, 0.4f),
                stiffness = new Vector3(0.8f, 0.8f, 0.8f),
                speed = new Vector3(12f, 12f, 12f),
                scale = new Vector3(2f, 2f, -2f),
            },
            dampingFactor = 8f
        };
    }
}
// Copyright (c) 2026 KINEMATION.
// All rights reserved.

using System;
using System.Collections.Generic;
using KINEMATION.Shared.KAnimationCore.Runtime.Attributes;
using KINEMATION.Shared.KAnimationCore.Runtime.Core;
using KINEMATION.KShooterCore.Runtime.Camera;
using KINEMATION.ProceduralRecoilAnimationSystem.Runtime;
using KINEMATION.TacticalShooterPack.Scripts.Animation;
using UnityEngine;

namespace KINEMATION.TacticalShooterPack.Scripts.Weapon
{
    [Serializable]
    public struct TacCurveAnimIntensity
    {
        [Range(0f, 1f)] public float standing;
        [Range(0f, 1f)] public float aiming;
        [Range(0f, 1f)] public float firing;
    }
    
    [CreateAssetMenu(fileName = "NewTacWeaponSettings", menuName = TacShooterUtility.TacAssetMenuPath + "Weapon Settings")]
    public class TacticalWeaponSettings : ScriptableObject
    {
        public string weaponName = string.Empty;
        [Tab("Animation")]
        
        public bool isOneHanded = false;
        
        [Header("Controllers")]
        public RuntimeAnimatorController characterAnimatorController;
        public RuntimeAnimatorController weaponAnimatorController;
        [Min(0f)] public float drawSpeed = 1f;
        
        [Header("Offsets")]
        public Quaternion weaponRotationOffset = Quaternion.identity;
        public KTransform weaponOffset = KTransform.Identity;
        public KTransform rightHandOffset = KTransform.Identity;
        public KTransform leftHandOffset = KTransform.Identity;
        
        [Header("Pistol Quick Draw")]
        public KTransform quickDrawOffset = KTransform.Identity;
        public KTransform quickDrawRightHandOffset = KTransform.Identity;
        public Vector3 pistolRightHandOffset = Vector3.zero;
        
        [Header("Weapon Sway")]
        public WeaponSway movementSway = WeaponSway.shooterMovePreset;
        public WeaponSway aimingSway = WeaponSway.shooterAimPreset;

        [Header("Procedural Movement")]
        public TacCurveAnimIntensity idleIntensity = new TacCurveAnimIntensity()
        {
            standing = 1f,
            aiming = 1f,
            firing = 1f
        };
        
        public TacCurveAnimIntensity walkIntensity = new TacCurveAnimIntensity()
        {
            standing = 1f,
            aiming = 1f,
            firing = 1f
        };

        [Header("Sprinting")]
        public KTransform sprintPoseOffset = KTransform.Identity;
        
        [Header("Aiming")]
        [Min(0f)] public float aimingSpeed;
        [Min(0f)] public float aimFov = 70f;
        
        [Tab("Firing")]
        
        [Header("Recoil")]
        public RecoilAnimData recoilData;
        public FPSCameraShake recoilShake;
        [Min(0f)] public float fireRate;
        
        [Header("Fire mode")]
        public bool supportsFullAuto = false;
        public int burstRounds = 0;
        public int ammoCapacity;

        [Tab("Sound")]
        [Header("Fire")]
        public Vector2 firePitchRange = Vector2.one;
        public Vector2 fireVolumeRange = Vector2.one;
        public AudioClip fireModeSwitchSound;
        public List<AudioClip> fireSounds;
        public List<AudioClip> suppressedFireSounds;
        
        [Header("Aiming")]
        public AudioClip aimInSound;
        public AudioClip aimOutSound;

        [Header("Equip/Draw/Holster")]
        public AudioClip equipSound;
        public AudioClip drawSound;
        public AudioClip holsterSound;
        public AudioClip quickDrawSound;
        public AudioClip quickHolsterSound;

        [Header("Reload")]
        public AudioClip reloadEmptySound;
        public AudioClip reloadTacSound;
        public AudioClip reloadLoopSound;
        public AudioClip reloadEndSound;
        
        [Header("Actions")]
        public AudioClip magCheckSound;
        public AudioClip inspectSound;
        
        public AudioClip deployAttachmentSound;
        public AudioClip stowAttachmentSound;
    }
}

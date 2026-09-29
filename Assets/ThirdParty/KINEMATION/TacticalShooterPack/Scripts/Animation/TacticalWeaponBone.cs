// Copyright (c) 2026 KINEMATION.
// All rights reserved.

using KINEMATION.Shared.KAnimationCore.Runtime.Core;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Experimental.Animations;
using UnityEngine.Playables;

namespace KINEMATION.TacticalShooterPack.Scripts.Animation
{
    public struct TacticalWeaponBoneJob : IAnimationJob
    {
        public TransformStreamHandle weaponBoneHandle;
        public TransformStreamHandle rightHandIkBoneHandle;
        public TransformStreamHandle rightHandBoneHandle;

        [ReadOnly] public float weight;
        
        public void ProcessAnimation(AnimationStream stream)
        {
            if (!weaponBoneHandle.IsValid(stream) || !rightHandIkBoneHandle.IsValid(stream) 
                                                  || !rightHandBoneHandle.IsValid(stream))
            {
                return;
            }

            KTransform weaponTransform = KAnimationMath.GetTransform(stream, weaponBoneHandle);
            weaponTransform = KAnimationMath.GetTransform(stream, rightHandIkBoneHandle)
                .GetRelativeTransform(weaponTransform, false);

            weaponTransform = KAnimationMath.GetTransform(stream, rightHandBoneHandle)
                .GetWorldTransform(weaponTransform, false);
            weaponTransform = KTransform.Lerp(KAnimationMath.GetTransform(stream, weaponBoneHandle), 
                weaponTransform, weight);
            
            weaponBoneHandle.SetPosition(stream, weaponTransform.position);
            weaponBoneHandle.SetRotation(stream, weaponTransform.rotation);
        }

        public void ProcessRootMotion(AnimationStream stream)
        {
        }
    }
    
    [ExecuteInEditMode]
    [AddComponentMenu("KINEMATION/Tactical Shooter Pack/Tactical Weapon Bone")]
    public class TacticalWeaponBone : MonoBehaviour
    {
        [Tooltip("Use this to control the influence of this component.")]
        [SerializeField] [Range(0f, 1f)] protected float weight = 1f;
        
        [Tooltip("This bone contains baked weapon movement (e.g., ik_hand_gun).")]
        [SerializeField] protected Transform weaponBone;
        [Tooltip("This bone contains baked right hand movement (e.g., ik_hand_r).")]
        [SerializeField] protected Transform rightHandIkBone;
        [Tooltip("This is the right hand bone (e.g., hand_r).")]
        [SerializeField] protected Transform rightHandBone;
        
        protected AnimationScriptPlayable _playable;
        protected TacticalWeaponBoneJob _job;

        protected Animator _animator;
        protected bool _isInitialized;

        protected const string WeaponBoneName = "ik_hand_gun";
        protected const string RightHandIkBoneName = "ik_hand_r";
        protected const string RightHandBoneName = "hand_r";
        
        protected void FindBoneByName(Transform search, ref Transform bone, string boneName)
        {
            if (search.name.Equals(boneName))
            {
                bone = search;
                return;
            }

            for (int i = 0; i < search.childCount; i++)
            {
                FindBoneByName(search.GetChild(i), ref bone, boneName);
            }
        }

        protected void OnEnable()
        {
            if (Application.isPlaying) return;
            
            if (weaponBone == null)
            {
                FindBoneByName(transform.root, ref weaponBone, WeaponBoneName);
            }

            if (rightHandIkBone == null)
            {
                FindBoneByName(transform.root, ref rightHandIkBone, RightHandIkBoneName);
            }

            if (rightHandBone == null)
            {
                FindBoneByName(transform.root, ref rightHandBone, RightHandBoneName);
            }
        }

        private void Start()
        {
            if (!Application.isPlaying) return;
            
            _animator = transform.root.GetComponentInChildren<Animator>();
            if (_animator == null) return;

            _job = new TacticalWeaponBoneJob()
            {
                weaponBoneHandle = _animator.BindStreamTransform(weaponBone),
                rightHandIkBoneHandle = _animator.BindStreamTransform(rightHandIkBone),
                rightHandBoneHandle = _animator.BindStreamTransform(rightHandBone),
                weight = 1f
            };

            _playable = AnimationScriptPlayable.Create(_animator.playableGraph, _job);
            var output = AnimationPlayableOutput.Create(_animator.playableGraph, "Tactical Weapon Bone", 
                _animator);
            output.SetSourcePlayable(_playable);
            output.SetAnimationStreamSource(AnimationStreamSource.PreviousInputs);

            _isInitialized = true;
        }

        private void Update()
        {
            if (!_isInitialized) return;
            
            _job.weight = weight;
            _playable.SetJobData(_job);
        }
    }
}
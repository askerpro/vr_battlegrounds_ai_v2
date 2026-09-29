// Copyright (c) 2026 KINEMATION.
// All rights reserved.

using KINEMATION.Shared.KAnimationCore.Runtime.Core;
using KINEMATION.KShooterCore.Runtime;
using KINEMATION.ProceduralRecoilAnimationSystem.Runtime;
using KINEMATION.TacticalShooterPack.Scripts.Weapon;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Animations;
using Quaternion = UnityEngine.Quaternion;
using Vector3 = UnityEngine.Vector3;

namespace KINEMATION.TacticalShooterPack.Scripts.Animation
{
    public struct TwoBoneIkChain
    {
        public TransformStreamHandle rootHandle;
        public TransformStreamHandle midHandle;
        public TransformStreamHandle tipHandle;

        public KTwoBoneIkData twoBoneIkData;

        public void Setup(Animator animator, Transform tip)
        {
            Transform mid = tip.parent;

            tipHandle = animator.BindStreamTransform(tip);
            midHandle = animator.BindStreamTransform(mid);
            rootHandle = animator.BindStreamTransform(mid.parent);
        }

        public void SetWeights(float effector, float hint)
        {
            twoBoneIkData.posWeight = twoBoneIkData.rotWeight = effector;
            twoBoneIkData.hintWeight = hint;
        }

        public void ProcessTwoBoneIK(AnimationStream stream)
        {
            twoBoneIkData.root.position = rootHandle.GetPosition(stream);
            twoBoneIkData.root.rotation = rootHandle.GetRotation(stream);
            
            twoBoneIkData.mid.position = midHandle.GetPosition(stream);
            twoBoneIkData.mid.rotation = midHandle.GetRotation(stream);
            
            twoBoneIkData.tip.position = tipHandle.GetPosition(stream);
            twoBoneIkData.tip.rotation = tipHandle.GetRotation(stream);
            
            KTwoBoneIK.Solve(ref twoBoneIkData);
            
            rootHandle.SetRotation(stream, twoBoneIkData.root.rotation);
            midHandle.SetRotation(stream, twoBoneIkData.mid.rotation);
            tipHandle.SetRotation(stream, twoBoneIkData.tip.rotation);
        }
    }
    
    public struct TacticalShooterPlayerJob : IAnimationJob
    {
        public TacticalWeaponSettings _weaponSettings;
        // Cached pose of the ik_hand_gun relative to the right hand.
        public KTransform cachedIkHandGunRight;
        public Vector3 pistolRightHandOffset;
        public KTransform aimPointTransform;

        public Vector3 lookInput;
        public Vector2 moveInput;
        public Vector2 deltaLookInput;
        
        public float aimingWeight;
        public float spineStabilization;
        public Quaternion stablePelvisRotation;
        
        public KTransform _ikMotion;
        public NativeArray<SpineBoneAtom> lookUpBones;
        public NativeArray<SpineBoneAtom> lookRightBones;
        
        private TransformStreamHandle _ikHandGunRootHandle;
        private TransformStreamHandle _ikHandGunHandle;
        private TransformStreamHandle _ikHandGunAdditiveHandle;
        private TransformStreamHandle _ikHandRightHandle;
        private TransformStreamHandle _ikHandLeftHandle;
        private TransformStreamHandle _cameraHandle;
        private TransformStreamHandle _headHandle;
        private TransformStreamHandle _pelvisHandle;
        private TransformStreamHandle _firstSpineHandle;

        private TransformSceneHandle _rootHandle;

        private TwoBoneIkChain _rightHandIk;
        private TwoBoneIkChain _leftHandIk;

        private Animator _animator;
        private RecoilAnimation _recoilAnimation;
        private IkMotionPlayer _ikMotionPlayer;
        
        private KTransform _ikHandGunRight;
        private KTransform _ikHandGunLeft;
        
        private float _quickDrawAlpha;
        private KTransform _recoil;
        
        // Move sway.
        private Vector3 _moveSwayPositionTarget;
        private Vector3 _moveSwayRotationTarget;
        
        private Vector3 _moveSwayPositionResult;
        private Vector3 _moveSwayRotationResult;
        
        private VectorSpringState _moveSwayPositionSpring;
        private VectorSpringState _moveSwayRotationSpring;
        
        // Aim sway.
        private Vector2 _aimSwayTarget;
        private Vector3 _aimSwayPositionResult;
        private Vector3 _aimSwayRotationResult;

        private VectorSpringState _aimSwayPositionSpring;
        private VectorSpringState _aimSwayRotationSpring;

        private float _gait;

        public void UpdateJob(AnimationScriptPlayable playable)
        {
            _quickDrawAlpha = _animator.GetFloat(TacShooterUtility.Animator_PistolQuickDraw.hash);
            _animator.SetLayerWeight(_animator.layerCount - 1, 1f - _quickDrawAlpha);
            
            if(_recoilAnimation != null) _recoil = _recoilAnimation.RecoilTransform;

            _gait = _animator.GetFloat(TacShooterUtility.Animator_Gait.hash);

            // Copy the sway data from the animation thread.
            var job = playable.GetJobData<TacticalShooterPlayerJob>();
            _moveSwayPositionResult = job._moveSwayPositionResult;
            _moveSwayPositionSpring = job._moveSwayPositionSpring;
            _moveSwayPositionTarget = job._moveSwayPositionTarget;
            
            _moveSwayRotationResult = job._moveSwayRotationResult;
            _moveSwayRotationSpring = job._moveSwayRotationSpring;
            _moveSwayRotationTarget = job._moveSwayRotationTarget;
            
            _aimSwayPositionResult = job._aimSwayPositionResult;
            _aimSwayPositionSpring = job._moveSwayPositionSpring;
            
            _aimSwayRotationResult = job._aimSwayRotationResult;
            _aimSwayRotationSpring = job._aimSwayRotationSpring;

            _aimSwayTarget = job._aimSwayTarget;
        }

        public void SetupIkBones(Animator animator, TacSkeletonBones bones)
        {
            _animator = animator;

            Transform root = animator.transform.root;
            _recoilAnimation = root.GetComponentInChildren<RecoilAnimation>();
            _recoil = KTransform.Identity;
            
            _ikHandGunRootHandle = _animator.BindStreamTransform(bones.ikHandGun.parent);
            _ikHandGunHandle = _animator.BindStreamTransform(bones.ikHandGun);
            _ikHandGunAdditiveHandle = _animator.BindStreamTransform(bones.ikHandGunAdditive);
            _ikHandRightHandle = _animator.BindStreamTransform(bones.ikRightHand);
            _ikHandLeftHandle = _animator.BindStreamTransform(bones.ikLeftHand);
            _headHandle = _animator.BindStreamTransform(bones.headBone);
            _cameraHandle = _animator.BindStreamTransform(bones.camera);
            _firstSpineHandle = _animator.BindStreamTransform(bones.spineRoot);
            _pelvisHandle = _animator.BindStreamTransform(bones.spineRoot.parent);
            stablePelvisRotation = Quaternion.Inverse(_animator.transform.rotation)
                                   * bones.spineRoot.parent.rotation;
            
            _rightHandIk.Setup(animator, bones.rightHand);
            _leftHandIk.Setup(animator, bones.leftHand);
            
            _rightHandIk.SetWeights(1f, 0f);
            _leftHandIk.SetWeights(1f, 0f);

            _rootHandle = _animator.BindSceneTransform(animator.transform);
            cachedIkHandGunRight = KTransform.Identity;
        }

        private void ProcessSpineStabilization(AnimationStream stream)
        {
            if (spineStabilization <= 0f) return;

            Quaternion pelvisRotation = _pelvisHandle.GetRotation(stream);
            Quaternion spineRotation = _firstSpineHandle.GetRotation(stream);
            Quaternion relativeSpineRotation = Quaternion.Inverse(pelvisRotation) * spineRotation;

            Quaternion stablePelvisWorldRotation = _rootHandle.GetRotation(stream) * stablePelvisRotation;
            Quaternion stableSpineRotation = stablePelvisWorldRotation * relativeSpineRotation;

            _firstSpineHandle.SetRotation(stream,
                Quaternion.Slerp(spineRotation, stableSpineRotation, spineStabilization));
        }

        private void ProcessWeaponIk(AnimationStream stream)
        {
            KTransform rootTransform = KAnimationMath.GetTransform(stream, _rootHandle);
            
            KTransform ikHandGun = KAnimationMath.GetTransform(stream, _ikHandGunHandle);
            KTransform ikRightHand = KAnimationMath.GetTransform(stream, _ikHandRightHandle);
            KTransform ikLeftHand = KAnimationMath.GetTransform(stream, _ikHandLeftHandle);
            
            _ikHandGunRight = ikRightHand.GetRelativeTransform(ikHandGun, false);
            _ikHandGunLeft = ikLeftHand.GetRelativeTransform(ikHandGun, false);

            KTransform rightHandTransform = KAnimationMath.GetTransform(stream, _rightHandIk.tipHandle);
            KTransform leftHandTransform = KAnimationMath.GetTransform(stream, _leftHandIk.tipHandle);
            
            KTransform quickRightHandOffset = KTransform.Lerp(KTransform.Identity,
                _weaponSettings.quickDrawRightHandOffset, _quickDrawAlpha);

            rightHandTransform.position = KAnimationMath.MoveInSpace(rootTransform, rightHandTransform,
                quickRightHandOffset.position, 1f);
            rightHandTransform.rotation = KAnimationMath.RotateInSpace(rootTransform, rightHandTransform,
                quickRightHandOffset.rotation, 1f);

            // Position the gun bone.
            KTransform gunRightHandWorld = rightHandTransform.GetWorldTransform(_ikHandGunRight, false);
            KTransform gunLeftHandWorld = leftHandTransform.GetWorldTransform(_ikHandGunLeft, false);
            
            var ikHandGunRoot = KTransform.Lerp(gunRightHandWorld,
                rightHandTransform.GetWorldTransform(cachedIkHandGunRight, false), _quickDrawAlpha);

            ikHandGunRoot.rotation *= _weaponSettings.weaponRotationOffset;
            
            // Position the main gun root bone.
            _ikHandGunRootHandle.SetPosition(stream, ikHandGunRoot.position);
            _ikHandGunRootHandle.SetRotation(stream, ikHandGunRoot.rotation);
            
            gunRightHandWorld = KTransform.Lerp(gunRightHandWorld, gunLeftHandWorld, 1f);
            gunRightHandWorld.rotation *= _weaponSettings.weaponRotationOffset;
            
            // Position its child ik_hand_gun.
            _ikHandGunHandle.SetPosition(stream, gunRightHandWorld.position);
            _ikHandGunHandle.SetRotation(stream, gunRightHandWorld.rotation);
            
            // Copy data from hands to IK targets.
            _ikHandRightHandle.SetPosition(stream, rightHandTransform.position);
            _ikHandRightHandle.SetRotation(stream, rightHandTransform.rotation);
            
            _ikHandLeftHandle.SetPosition(stream, leftHandTransform.position);
            _ikHandLeftHandle.SetRotation(stream, leftHandTransform.rotation);
        }

        private void ProcessWeaponOffsets(AnimationStream stream)
        {
            KTransform weaponOffset = KTransform.Lerp(_weaponSettings.weaponOffset, 
                _weaponSettings.quickDrawOffset, _quickDrawAlpha);
            
            weaponOffset = KTransform.Lerp(weaponOffset, _weaponSettings.sprintPoseOffset, 
                Mathf.Clamp01(_gait - 1f));

            KAnimationMath.MoveInSpace(stream, _rootHandle, _ikHandGunRootHandle, weaponOffset.position, 1f);
            KAnimationMath.RotateInSpace(stream, _rootHandle, _ikHandGunRootHandle, weaponOffset.rotation, 1f);
            
            KAnimationMath.MoveInSpace(stream, _ikHandGunHandle, _ikHandRightHandle,
                _weaponSettings.rightHandOffset.position, 1f);
            KAnimationMath.RotateInSpace(stream, _ikHandGunHandle, _ikHandRightHandle,
                _weaponSettings.rightHandOffset.rotation, 1f);
            
            KAnimationMath.MoveInSpace(stream, _ikHandGunHandle, _ikHandLeftHandle,
                _weaponSettings.leftHandOffset.position, 1f);
            KAnimationMath.RotateInSpace(stream, _ikHandGunHandle, _ikHandLeftHandle,
                _weaponSettings.leftHandOffset.rotation, 1f);
        }

        private void ProcessAdditives(AnimationStream stream)
        {
            KTransform ikHandGun = KAnimationMath.GetTransform(stream, _ikHandGunHandle);
            
            KAnimationMath.MoveInSpace(stream, _rootHandle, _ikHandGunRootHandle, 
                _recoil.position + _ikMotion.position, 1f);
            KAnimationMath.RotateInSpace(stream, _rootHandle, _ikHandGunRootHandle, 
                _recoil.rotation * _ikMotion.rotation, 1f);

            KTransform ikRightHand = new KTransform()
            {
                position = _ikHandRightHandle.GetPosition(stream),
                rotation = _ikHandRightHandle.GetRotation(stream)
            };
            
            // Keep the pivot bone and the left hand in place.
            _ikHandGunHandle.SetPosition(stream, 
                Vector3.Lerp(_ikHandGunHandle.GetPosition(stream), ikHandGun.position, _quickDrawAlpha));
            _ikHandGunHandle.SetRotation(stream, 
                Quaternion.Slerp(_ikHandGunHandle.GetRotation(stream), ikHandGun.rotation, _quickDrawAlpha));
            
            // Keep the right hand animated by the root gun bone.
            _ikHandRightHandle.SetPosition(stream, ikRightHand.position);
            _ikHandRightHandle.SetRotation(stream, ikRightHand.rotation);

            KTransform rootTransform = KAnimationMath.GetTransform(stream, _rootHandle);

            KTransform gunAdditiveBone = new KTransform()
            {
                position = _ikHandGunAdditiveHandle.GetPosition(stream),
                rotation = _ikHandGunAdditiveHandle.GetRotation(stream)
            };

            gunAdditiveBone = rootTransform.GetRelativeTransform(gunAdditiveBone, false);
            gunAdditiveBone.rotation *= _weaponSettings.weaponRotationOffset;

            KAnimationMath.MoveInSpace(stream, _rootHandle, _ikHandGunRootHandle, gunAdditiveBone.position, 1f);
            KAnimationMath.RotateInSpace(stream, _rootHandle, _ikHandGunRootHandle, gunAdditiveBone.rotation, 1f);

            KTransform primaryWeaponBone = KAnimationMath.GetTransform(stream, _ikHandGunHandle);
            KAnimationMath.MoveInSpace(stream, _ikHandGunRootHandle, _ikHandGunRootHandle,
                pistolRightHandOffset, _quickDrawAlpha);
            _ikHandGunHandle.SetPosition(stream, primaryWeaponBone.position);
            _ikHandGunHandle.SetRotation(stream, primaryWeaponBone.rotation);
        }

        private void ProcessInverseKinematics(AnimationStream stream)
        {
            _rightHandIk.twoBoneIkData.target = KAnimationMath.GetTransform(stream, _ikHandRightHandle);
            _leftHandIk.twoBoneIkData.target = KAnimationMath.GetTransform(stream, _ikHandLeftHandle);
            
            _rightHandIk.ProcessTwoBoneIK(stream);
            _leftHandIk.ProcessTwoBoneIK(stream);
        }

        private void ProcessAimOffset(AnimationStream stream)
        {
            KTransform ikHandGunRoot = KAnimationMath.GetTransform(stream, _ikHandGunRootHandle);
            KTransform head = KAnimationMath.GetTransform(stream, _headHandle);
            ikHandGunRoot = head.GetRelativeTransform(ikHandGunRoot, false);
            
            float fraction = lookInput.z / 90f;
            bool sign = fraction > 0f;
            
            foreach (var bone in lookRightBones)
            {
                float angle = sign ? bone.clampedAngle.x : bone.clampedAngle.y;
                
                bone.handle.SetRotation(stream, KAnimationMath.RotateInSpace(_rootHandle.GetRotation(stream),
                    bone.handle.GetRotation(stream), Quaternion.Euler(0f, 0f, angle * fraction), 1f));
            }
            
            fraction = lookInput.x / 90f;
            sign = fraction > 0f;
            
            foreach (var bone in lookRightBones)
            {
                float angle = sign ? bone.clampedAngle.x : bone.clampedAngle.y;
                
                bone.handle.SetRotation(stream, KAnimationMath.RotateInSpace(_rootHandle.GetRotation(stream),
                    bone.handle.GetRotation(stream), Quaternion.Euler(0f, angle * fraction, 0f), 1f));
            }
            
            fraction = lookInput.y / 90f;
            sign = fraction > 0f;
            
            Quaternion space = Quaternion.Euler(0f, lookInput.x, 0f);
            
            foreach (var bone in lookUpBones)
            {
                float angle = sign ? bone.clampedAngle.x : bone.clampedAngle.y;
                
                Quaternion rootRotation = _rootHandle.GetRotation(stream) * space;
                bone.handle.SetRotation(stream, KAnimationMath.RotateInSpace(rootRotation,
                    bone.handle.GetRotation(stream), Quaternion.Euler(angle * fraction, 0f, 0f), 1f));
            }
            
            head = KAnimationMath.GetTransform(stream, _headHandle);
            ikHandGunRoot = head.GetWorldTransform(ikHandGunRoot, false);
            
            _ikHandGunRootHandle.SetPosition(stream, ikHandGunRoot.position);
            _ikHandGunRootHandle.SetRotation(stream, ikHandGunRoot.rotation);
        }

        private void ProcessAds(AnimationStream stream)
        {
            KTransform rootTransform = new KTransform()
            {
                position = _rootHandle.GetPosition(stream),
                rotation = _rootHandle.GetRotation(stream)
            };

            KTransform ikGunRoot = new KTransform()
            {
                position = _ikHandGunRootHandle.GetPosition(stream),
                rotation = _ikHandGunRootHandle.GetRotation(stream)
            };

            KTransform aimTransform = new KTransform()
            {
                position = _cameraHandle.GetPosition(stream),
                rotation = rootTransform.rotation
            };

            aimTransform.position = KAnimationMath.MoveInSpace(rootTransform, aimTransform,
                aimPointTransform.rotation * aimPointTransform.position, 1f);
            aimTransform.rotation =
                KAnimationMath.RotateInSpace(rootTransform, aimTransform, aimPointTransform.rotation, 1f);
            
            EaseMode easeMode = new EaseMode(EEaseFunc.Sine);
            aimingWeight = KCurves.Ease(0f, 1f, aimingWeight, easeMode);
            ikGunRoot = KTransform.Lerp(ikGunRoot, aimTransform, aimingWeight);
            
            KTransform ikHandGun = new KTransform()
            {
                position = _ikHandGunHandle.GetPosition(stream),
                rotation = _ikHandGunHandle.GetRotation(stream)
            };
            
            _ikHandGunRootHandle.SetPosition(stream, ikGunRoot.position);
            _ikHandGunRootHandle.SetRotation(stream, ikGunRoot.rotation);

            KTransform ikRightHand = new KTransform()
            {
                position = _ikHandRightHandle.GetPosition(stream),
                rotation = _ikHandRightHandle.GetRotation(stream)
            };
            
            // Keep the pivot bone and the left hand in place.
            _ikHandGunHandle.SetPosition(stream, 
                Vector3.Lerp(_ikHandGunHandle.GetPosition(stream), ikHandGun.position, _quickDrawAlpha));
            _ikHandGunHandle.SetRotation(stream, 
                Quaternion.Slerp(_ikHandGunHandle.GetRotation(stream), ikHandGun.rotation, _quickDrawAlpha));
            
            // Keep the right hand animated by the root gun bone.
            _ikHandRightHandle.SetPosition(stream, ikRightHand.position);
            _ikHandRightHandle.SetRotation(stream, ikRightHand.rotation);
        }
        
        private void ProcessSway(AnimationStream stream)
        {
            Vector3 clampPosition = _weaponSettings.movementSway.clampPosition;
            Vector3 clampRotation = _weaponSettings.movementSway.clampRotation;
            
            float adsScale = Mathf.Lerp(1f, _weaponSettings.movementSway.adsScale, aimingWeight);
            var rotationTarget = new Vector3()
            {
                x = Mathf.Clamp(moveInput.y, -clampRotation.x, clampRotation.x),
                y = Mathf.Clamp(moveInput.x, -clampRotation.y, clampRotation.y),
                z =  Mathf.Clamp(moveInput.x, -clampRotation.z, clampRotation.z)
            };

            var positionTarget = new Vector3()
            {
                x = Mathf.Clamp(moveInput.x, -clampPosition.x, clampPosition.x),
                y = Mathf.Clamp(moveInput.y, -clampPosition.y, clampPosition.y),
                z = Mathf.Clamp(moveInput.y, -clampPosition.z, clampPosition.z)
            };

            rotationTarget *= adsScale;
            positionTarget *= adsScale;
            
            // Movement sway.
            float alpha = KMath.ExpDecayAlpha(_weaponSettings.movementSway.dampingFactor, stream.deltaTime);

            _moveSwayPositionTarget = Vector3.Lerp(_moveSwayPositionTarget, positionTarget / 100f, alpha);
            _moveSwayRotationTarget = Vector3.Lerp(_moveSwayRotationTarget, rotationTarget, alpha);

            _moveSwayPositionResult = KSpringMath.VectorSpringInterp(_moveSwayPositionResult,
                _moveSwayPositionTarget, _weaponSettings.movementSway.position, ref _moveSwayPositionSpring, stream.deltaTime);
            _moveSwayRotationResult = KSpringMath.VectorSpringInterp(_moveSwayRotationResult,
                _moveSwayRotationTarget, _weaponSettings.movementSway.rotation, ref _moveSwayRotationSpring, stream.deltaTime);

            // Aiming sway.
            _aimSwayTarget += new Vector2(deltaLookInput.x, deltaLookInput.y) * 0.01f;

            alpha = KMath.ExpDecayAlpha(_weaponSettings.aimingSway.dampingFactor, stream.deltaTime);
            _aimSwayTarget = Vector2.Lerp(_aimSwayTarget, Vector2.zero, alpha);
            
            clampPosition = _weaponSettings.aimingSway.clampPosition;
            clampRotation = _weaponSettings.aimingSway.clampRotation;
            
            Vector3 targetLoc = new Vector3()
            {
                x = Mathf.Clamp(_aimSwayTarget.x, -clampPosition.x, clampPosition.x),
                y = Mathf.Clamp(_aimSwayTarget.y, -clampPosition.y, clampPosition.y),
                z = 0f
            };
            
            Vector3 targetRot = new Vector3()
            {
                x = Mathf.Clamp(_aimSwayTarget.y, -clampRotation.x, clampRotation.x),
                y = Mathf.Clamp(_aimSwayTarget.x, -clampRotation.y, clampRotation.y),
                z = Mathf.Clamp(_aimSwayTarget.x, -clampRotation.z, clampRotation.z)
            };
            
            adsScale = Mathf.Lerp(1f, _weaponSettings.aimingSway.adsScale, aimingWeight);
            targetLoc *= adsScale;
            targetRot *= adsScale;

            _aimSwayPositionResult = KSpringMath.VectorSpringInterp(_aimSwayPositionResult,
                targetLoc / 100f, _weaponSettings.aimingSway.position, ref _aimSwayPositionSpring, stream.deltaTime);

            _aimSwayRotationResult = KSpringMath.VectorSpringInterp(_aimSwayRotationResult,
                targetRot, _weaponSettings.aimingSway.rotation, ref _aimSwayRotationSpring, stream.deltaTime);
            
            KTransform root = KAnimationMath.GetTransform(stream, _rootHandle);
            
            KTransform sway = new KTransform()
            {
                position = _moveSwayPositionResult + _aimSwayPositionResult,
                rotation = Quaternion.Euler(_moveSwayRotationResult) * Quaternion.Euler(_aimSwayRotationResult),
                scale = Vector3.one
            };
            
            KTransform ikHandGunRoot = KAnimationMath.GetTransform(stream, _ikHandGunRootHandle);
            ikHandGunRoot.position = KAnimationMath.MoveInSpace(root, ikHandGunRoot, sway.position, 1f);
            ikHandGunRoot.rotation = KAnimationMath.RotateInSpace(root, ikHandGunRoot, sway.rotation, 1f);

            _ikHandGunRootHandle.SetPosition(stream, ikHandGunRoot.position);
            _ikHandGunRootHandle.SetRotation(stream, ikHandGunRoot.rotation);
        }
        
        public void ProcessAnimation(AnimationStream stream)
        {
            ProcessSpineStabilization(stream);
            ProcessWeaponIk(stream);
            ProcessWeaponOffsets(stream);
            ProcessAds(stream);
            ProcessAdditives(stream);
            ProcessSway(stream);
            ProcessAimOffset(stream);
            ProcessInverseKinematics(stream);
        }

        public void ProcessRootMotion(AnimationStream stream)
        {
        }
    }
}

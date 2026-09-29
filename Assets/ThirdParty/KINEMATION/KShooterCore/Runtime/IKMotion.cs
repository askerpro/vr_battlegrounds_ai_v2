// Copyright (c) 2026 KINEMATION.
// All rights reserved.

using KINEMATION.Shared.KAnimationCore.Runtime.Core;
using UnityEngine;

namespace KINEMATION.KShooterCore.Runtime
{
    [CreateAssetMenu(fileName = "NewIkMotionLayer", menuName = ShooterUtility.ShooterMenuPath + "IK Motion")]
    public class IKMotion : ScriptableObject
    {
        public VectorCurve rotationCurves = new VectorCurve(new Keyframe[]
        {
            new Keyframe(0f, 0f),
            new Keyframe(1f, 0f)
        });
        
        public VectorCurve translationCurves = new VectorCurve(new Keyframe[]
        {
            new Keyframe(0f, 0f),
            new Keyframe(1f, 0f)
        });
        
        public Vector3 rotationScale = Vector3.one;
        public Vector3 translationScale = Vector3.one;

        [Min(0f)] public float blendTime = 0f;
        [Min(0f)] public float playRate = 1f;

        public float GetLength()
        {
            return Mathf.Max(rotationCurves.GetCurveLength(), translationCurves.GetCurveLength());
        }
    }
    
    public class IkMotionPlayer
    {
        public KTransform IkMotion => _ikMotionTransform;
        
        private float _ikMotionPlayback = 0f;
        private IKMotion _activeMotion;

        private KTransform _ikMotionTransform = KTransform.Identity;
        private KTransform _cachedIkMotionTransform = KTransform.Identity;
        
        public void PlayIkMotion(IKMotion newMotion)
        {
            if (newMotion == null) return;
            
            _ikMotionPlayback = 0f;
            _cachedIkMotionTransform = _ikMotionTransform;
            _activeMotion = newMotion;
        }
        
        public void ProcessIkMotion()
        {
            if (_activeMotion == null) return;
            
            _ikMotionPlayback = Mathf.Clamp(_ikMotionPlayback + _activeMotion.playRate * Time.deltaTime, 0f, 
                _activeMotion.GetLength());

            Vector3 positionTarget = _activeMotion.translationCurves.GetValue(_ikMotionPlayback);
            positionTarget.x *= _activeMotion.translationScale.x;
            positionTarget.y *= _activeMotion.translationScale.y;
            positionTarget.z *= _activeMotion.translationScale.z;

            Vector3 rotationTarget = _activeMotion.rotationCurves.GetValue(_ikMotionPlayback);
            rotationTarget.x *= _activeMotion.rotationScale.x;
            rotationTarget.y *= _activeMotion.rotationScale.y;
            rotationTarget.z *= _activeMotion.rotationScale.z;

            _ikMotionTransform.position = positionTarget;
            _ikMotionTransform.rotation = Quaternion.Euler(rotationTarget);

            if (!Mathf.Approximately(_activeMotion.blendTime, 0f))
            {
                _ikMotionTransform = KTransform.Lerp(_cachedIkMotionTransform, _ikMotionTransform,
                    _ikMotionPlayback / _activeMotion.blendTime);
            }
        }
    }
}
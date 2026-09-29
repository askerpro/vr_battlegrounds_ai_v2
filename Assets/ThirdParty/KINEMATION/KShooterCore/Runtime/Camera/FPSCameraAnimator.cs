// Copyright (c) 2026 KINEMATION.
// All rights reserved.

using KINEMATION.Shared.KAnimationCore.Runtime.Core;
using UnityEngine;

namespace KINEMATION.KShooterCore.Runtime.Camera
{
    [AddComponentMenu(ShooterUtility.ShooterMenuPath + "FPS Camera Animator")]
    public class FPSCameraAnimator : MonoBehaviour
    {
        public float BaseFOV => _baseFOV;
        public bool UseFreeLook => _useFreeLook;
        [HideInInspector] public Vector2 lookInput = Vector2.zero;
        
        [Header("General")]
        [SerializeField] protected Transform cameraBone;
        [SerializeField, Min(0f)] protected float cameraAnimationScale = 1f;

        [Header("Free Look")]
        [SerializeField] protected Vector2 maxFreeLookAngle = new Vector2(40f, 40f);
        [SerializeField] protected float freeLookSmoothing = 7f;
         
        protected FPSCameraShake _activeShake;
        protected Vector3 _cameraShake;
        protected Vector3 _cameraShakeTarget;
        protected float _cameraShakePlayback;
        
        protected Vector2 _freeLookInput;
        protected Vector2 _smoothFreeLookInput;
        protected bool _useFreeLook;

        protected UnityEngine.Camera _camera;
        protected float _targetFOV;
        protected float _baseFOV;
        protected float _fovSmoothing;
        
        public void ToggleFreeLook()
        {
            _useFreeLook = !_useFreeLook;
            if (_useFreeLook)
            {
                return;
            }
            
            _freeLookInput = Vector2.zero;
        }

        public void AddFreeLookInput(Vector2 delta)
        {
            delta.y *= -1f;
            _freeLookInput += delta;
            _freeLookInput.x = Mathf.Clamp(_freeLookInput.x, -maxFreeLookAngle.x, maxFreeLookAngle.x);
            _freeLookInput.y = Mathf.Clamp(_freeLookInput.y, -maxFreeLookAngle.y, maxFreeLookAngle.y);
        }

        public virtual void PlayCameraShake(FPSCameraShake newShake)
        {
            if (newShake == null) return;

            _activeShake = newShake;
            _cameraShakePlayback = 0f;

            _cameraShakeTarget.x = FPSCameraShake.GetTarget(_activeShake.pitch);
            _cameraShakeTarget.y = FPSCameraShake.GetTarget(_activeShake.yaw);
            _cameraShakeTarget.z = FPSCameraShake.GetTarget(_activeShake.roll);
        }

        public virtual void SetTargetFOV(float newFov, float smoothing = 0f)
        {
            _targetFOV = newFov;
            _fovSmoothing = smoothing;
        }

        public void RestoreFOV(float smoothing = 0f)
        {
            _targetFOV = _baseFOV;
            _fovSmoothing = smoothing;
        }
        
        protected virtual void UpdateCameraShake()
        {
            if (_activeShake == null) return;

            float length = _activeShake.shakeCurve.GetCurveLength();
            _cameraShakePlayback += Time.deltaTime * _activeShake.playRate;
            _cameraShakePlayback = Mathf.Clamp(_cameraShakePlayback, 0f, length);

            float alpha = KMath.ExpDecayAlpha(_activeShake.smoothSpeed, Time.deltaTime);
            if (!KAnimationMath.IsWeightRelevant(_activeShake.smoothSpeed))
            {
                alpha = 1f;
            }

            Vector3 target = _activeShake.shakeCurve.GetValue(_cameraShakePlayback);
            target.x *= _cameraShakeTarget.x;
            target.y *= _cameraShakeTarget.y;
            target.z *= _cameraShakeTarget.z;
            
            _cameraShake = Vector3.Lerp(_cameraShake, target, alpha);
            transform.rotation *= Quaternion.Euler(_cameraShake);
        }

        protected virtual void UpdateFOV()
        {
            _camera.fieldOfView = KMath.FloatInterp(_camera.fieldOfView, _targetFOV, _fovSmoothing, 
                Time.deltaTime);
        }

        private void Start()
        {
            _camera = GetComponentInChildren<UnityEngine.Camera>();
            _targetFOV = _baseFOV = _camera.fieldOfView;
        }

        private void LateUpdate()
        {
            Transform root = transform.root;
            
            _smoothFreeLookInput = Vector2.Lerp(_smoothFreeLookInput, _freeLookInput,
                KMath.ExpDecayAlpha(freeLookSmoothing, Time.deltaTime));
            
            Vector2 freeLook = _smoothFreeLookInput;

            transform.rotation = root.rotation * Quaternion.Euler(lookInput.y, lookInput.x, 0f)
                                               * Quaternion.Euler(freeLook.y, freeLook.x, 0f);
            
            if (cameraBone != null)
            {
                Vector3 eulerAngles = cameraBone.localRotation.eulerAngles;
                eulerAngles *= cameraAnimationScale;
                transform.rotation *= Quaternion.Euler(eulerAngles);
            }
            
            UpdateCameraShake();
            UpdateFOV();
        }
    }
}
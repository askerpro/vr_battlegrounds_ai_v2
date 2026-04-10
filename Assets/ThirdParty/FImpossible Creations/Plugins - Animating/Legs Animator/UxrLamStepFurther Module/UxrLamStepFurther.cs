#if UNITY_EDITOR
using UnityEditor;
#endif
using FIMSpace.FProceduralAnimation;
using UltimateXR.Avatar;
using UnityEngine;

namespace ROVR.Character.IK
{
    //[CreateAssetMenu(fileName = "UxrLamStepFurther", menuName = "FImpossible Creations/Legs Animator/Control Module - UltimateXR Step Further", order = 1)]
    public class UxrLamStepFurther : LegsAnimatorControlModuleBase
    {
        #region Private Types & Data
        
        // Variable necessary for determining the foot space.
        private LegsAnimator.Variable _stepForwardOffset;
        
        private UxrAvatar             _uxrAvatar;
        
        // Positional data
        private Transform _cachedPosition;
        private Vector3 _previousPosition;
        private Vector3 _velocity           = Vector3.zero;
        private Vector3 _smoothDampVelocity = Vector3.zero;
        
        private const float PowerValue = 0.3f;

        #endregion
        
        #region Legs Animator methods

        public override void OnInit(LegsAnimator.LegsAnimatorCustomModuleHelper helper)
        {
            base.OnInit(helper);
            _stepForwardOffset = helper.RequestVariable("IK Step Forward Offset", 0.5f);
            
            if (_cachedPosition == null)
                CachePositionTransform(LA.BaseTransform);
        }

        public override void OnPreLateUpdate(LegsAnimator.LegsAnimatorCustomModuleHelper helper)
        {
            Vector3 velocity           = GetVelocity(_cachedPosition.position);
            Vector3 horizontalVelocity = LA.ToRootLocalSpaceVec(velocity);
            
            horizontalVelocity.y = 0f;
            horizontalVelocity   = LA.RootToWorldSpaceVec(horizontalVelocity);

            _velocity = Vector3.SmoothDamp(_velocity, horizontalVelocity, ref _smoothDampVelocity, 0.1f, 1000000f, LA.DeltaTime);
        }

        public override void Leg_LatePreRaycastingUpdate(LegsAnimator.LegsAnimatorCustomModuleHelper helper, LegsAnimator.Leg leg)
        {
            leg.OverrideFinalAndSourceIKPos(leg.GetFinalIKPos() + _velocity * (PowerValue * _stepForwardOffset.GetFloat() * EffectBlend));
            leg.OverrideControlPositionsWithCurrentIKState();
        }
        
        #endregion

        #region Private Methods
        
        /// <summary>
        /// Gets velocity based on position.
        /// </summary>
        /// <param name="currentPosition">Any object's transform position.</param>
        /// <returns></returns>
        private Vector3 GetVelocity(Vector3 currentPosition)
        {
            Vector3 velocity = (currentPosition - _previousPosition) / Time.deltaTime;
            _previousPosition = currentPosition;
            return velocity;
        }

        /// <summary>
        /// gameObject.transform calls GetComponent every time, which isn't very performant for PreLateUpdate.<br /><br />
        /// Caching the transform is slightly faster than calling gameObject.transform everytime.<br /><br />
        /// This might improve performance slightly of the script.
        /// </summary>
        /// <param name="transform">Transform to be cached.</param>
        private void CachePositionTransform(Transform transform)
        {
            _cachedPosition = transform;
        }
        
        #endregion
        
        #region Editor Code

#if UNITY_EDITOR

        public override void Editor_InspectorGUI(LegsAnimator legsAnimator, LegsAnimator.LegsAnimatorCustomModuleHelper helper)
        {
            EditorGUILayout.HelpBox("Velocity based further stepping module made for UltimateXR developers!", MessageType.Info);
            GUILayout.Space(5);
            
            LegsAnimator.Variable stepForward = helper.RequestVariable("IK Step Forward Offset", 0.5f);
            stepForward.SetMinMaxSlider(0f, 5f);
            stepForward.Editor_DisplayVariableGUI();
        }

#endif
        #endregion

    }
}
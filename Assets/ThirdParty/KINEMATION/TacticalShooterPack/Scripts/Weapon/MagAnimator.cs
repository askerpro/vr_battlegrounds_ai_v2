// Copyright (c) 2026 KINEMATION.
// All rights reserved.

using KINEMATION.Shared.KAnimationCore.Runtime.Core;
using KINEMATION.KShooterCore.Runtime.Weapon;
using UnityEngine;

namespace KINEMATION.TacticalShooterPack.Scripts.Weapon
{
    [AddComponentMenu("KINEMATION/Tactical Shooter Pack/Mag Animator")]
    public class MagAnimator : MonoBehaviour
    {
        [Header("Magazine")]
        [SerializeField, Min(0)] protected int magCapacity;
        [SerializeField, Min(0f)] protected float timeStep;
        [SerializeField, Min(0f)] protected float interpSpeed;
        [SerializeField, Range(0f, 1f)] protected float magProgress;
        
        protected Animator _animator;
        protected KShooterWeapon _shooterWeapon;

        protected float _bulletsAnimLength;

        private void Start()
        {
            _shooterWeapon = transform.GetComponentInParent<KShooterWeapon>();
            _animator = GetComponent<Animator>();
            _bulletsAnimLength = _animator.runtimeAnimatorController.animationClips[0].length;
        }

        private void Update()
        {
            if (_animator == null) return;
            
            if (_shooterWeapon != null)
            {
                int activeAmmo = _shooterWeapon.GetActiveAmmo();

                if (activeAmmo >= magCapacity)
                {
                    magProgress = 0f;
                }
                else
                {
                    magProgress = KMath.FloatInterp(magProgress, (magCapacity - activeAmmo) * (timeStep / _bulletsAnimLength), 
                        interpSpeed, Time.deltaTime);
                    magProgress = Mathf.Clamp01(magProgress);
                }
            }
            
            _animator.SetFloat(TacShooterUtility.Animator_MagProgress.hash, magProgress);
        }
    }
}
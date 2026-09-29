// Copyright (c) 2026 KINEMATION.
// All rights reserved.

using KINEMATION.ProceduralRecoilAnimationSystem.Runtime;
using UnityEngine;

namespace KINEMATION.TacticalShooterPack.Scripts.Weapon
{
    [AddComponentMenu("KINEMATION/Tactical Shooter Pack/Tactical MX68")]
    public class TacticalMX68 : TacticalShooterWeapon
    {
        [SerializeField] protected AudioClip tacticalReload02Sound;

        private static readonly int Reload01Hash = Animator.StringToHash("Reload_01");
        private static readonly int Reload02Hash = Animator.StringToHash("Reload_02");
        private static readonly int Reload03Hash = Animator.StringToHash("Reload_03");
        private static readonly int FireBeltHash = Animator.StringToHash("Fire_Belt");
        private static readonly int AmmoProgressHash = Animator.StringToHash("AmmoProgress");

        private int _ammoThreshold = 28;

        public override void Reload()
        {
            if (_activeAmmo == tacWeaponSettings.ammoCapacity) return;

            int reloadHash;
            AudioClip reloadSound;

            if (_activeAmmo == 0)
            {
                reloadHash = Reload03Hash;
                reloadSound = tacWeaponSettings.reloadEmptySound;
            }
            else if (_activeAmmo < 12)
            {
                reloadHash = Reload02Hash;
                reloadSound = tacticalReload02Sound;
            }
            else
            {
                reloadHash = Reload01Hash;
                reloadSound = tacWeaponSettings.reloadTacSound;
            }
            
            _characterAnimator.CrossFadeInFixedTime(reloadHash, 0.15f, -1);
            _weaponAnimator.CrossFadeInFixedTime(reloadHash, 0.15f, -1);
            PlaySound(reloadSound);
        }

        protected override void Fire()
        {
            if (!_isFiring || _activeAmmo == 0) return;

            if (muzzleFlash != null && !isSuppressed) muzzleFlash.Play();
            if (muzzleFlashSuppressed != null && isSuppressed) muzzleFlashSuppressed.Play();

            if (_recoilAnimation != null) _recoilAnimation.Play();
            if (_fpsCamera != null) _fpsCamera.PlayCameraShake(tacWeaponSettings.recoilShake);
            PlayFireSound();

            _activeAmmo--;
            PlayCharacterWeaponAnimation(_activeAmmo > 0
                ? TacShooterUtility.Animator_Fire.hash
                : TacShooterUtility.Animator_FireOut.hash);
            _weaponAnimator.Play(FireBeltHash, -1, 0f);

            if (_activeAmmo == 0 || fireMode == FireMode.Semi || fireMode == FireMode.Burst && _burstsLeft == 0)
            {
                StopFiring();
                return;
            }

            if (fireMode == FireMode.Burst) _burstsLeft--;
            Invoke(nameof(Fire), 60f / tacWeaponSettings.fireRate);
        }

        protected void Update()
        {
            float progress = (float) Mathf.Max(_ammoThreshold - GetActiveAmmo(), 0) / _ammoThreshold;
            _weaponAnimator.SetFloat(AmmoProgressHash, progress);
        }
    }
}
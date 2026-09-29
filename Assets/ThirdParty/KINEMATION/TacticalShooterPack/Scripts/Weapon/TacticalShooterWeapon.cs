// Copyright (c) 2026 KINEMATION.
// All rights reserved.

using System;
using System.Collections.Generic;
using KINEMATION.Shared.KAnimationCore.Runtime.Core;
using KINEMATION.KShooterCore.Runtime.Camera;
using KINEMATION.KShooterCore.Runtime.Weapon;
using KINEMATION.ProceduralRecoilAnimationSystem.Runtime;
using KINEMATION.TacticalShooterPack.Scripts.Animation;
using UnityEngine;
using Random = UnityEngine.Random;

namespace KINEMATION.TacticalShooterPack.Scripts.Weapon
{
    [Serializable]
    public struct BoneVisibility
    {
        public Transform bone;
        public bool isHidden;
    }
    
    [AddComponentMenu("KINEMATION/Tactical Shooter Pack/Tactical Shooter Weapon")]
    public class TacticalShooterWeapon : KShooterWeapon
    {
        public FireMode FireMode => fireMode;
        public bool IsFiring => _isFiring;
        public bool IsOneHanded => tacWeaponSettings.isOneHanded;
        public float AimingSpeed => tacWeaponSettings.aimingSpeed;
        public AnimationClip BaseIdlePose => _idlePose;
        
        public TacticalWeaponSettings tacWeaponSettings;
        [HideInInspector] public KTransform gunRightHandPose = KTransform.Identity;
        
        [SerializeField] protected Transform aimPoint;
        [SerializeField] protected FireMode fireMode;
        [SerializeField] protected bool isSuppressed = false;
        [SerializeField] protected bool isAttachmentDeployed;
        
        [SerializeField] protected List<BoneVisibility> bonesToHide = new List<BoneVisibility>();

        [SerializeField] protected ParticleSystem muzzleFlash;
        [SerializeField] protected ParticleSystem muzzleFlashSuppressed;
        [SerializeField] protected ParticleSystem emptyCasing;
        
        protected Animator _characterAnimator;
        protected RecoilAnimation _recoilAnimation;
        protected FPSCameraAnimator _fpsCamera;

        protected Animator _weaponAnimator;
        protected int _activeAmmo;
        protected int _burstsLeft;

        protected AnimationClip _idlePose;
        protected AnimationClip _draw;
        protected AnimationClip _holster;
        protected AnimationClip _quickHolster;
        
        protected bool _isFiring;
        protected bool _hasPlayedInitialEquipAnimation;
        protected AudioSource _audioSource;

        protected float _lastShotTime;

        public virtual Transform GetWeaponRoot()
        {
            return transform.parent;
        }

        public void PlayEmptyCasing()
        {
            if (emptyCasing == null) return;
            emptyCasing.Play();
        }

        protected void SetBoneVisibility(string boneName, bool isVisible)
        {
            int index = bonesToHide.FindIndex(t => t.bone.name.Equals(boneName));
            if (index < 0) return;

            var boneVisibility = bonesToHide[index];
            boneVisibility.isHidden = !isVisible;
            bonesToHide[index] = boneVisibility;
        }

        public void HideBoneByName(string boneName)
        {
            SetBoneVisibility(boneName, false);
        }
        
        public void UnhideBoneByName(string boneName)
        {
            SetBoneVisibility(boneName, true);
        }

        protected void PlayCharacterWeaponAnimation(int hash, float bledInTime = 0.15f)
        {
            _characterAnimator.CrossFadeInFixedTime(hash, bledInTime, -1);
            _weaponAnimator.Play(hash, -1, 0f);
        }

        protected void ProcessAnimationClips()
        {
            var clips = tacWeaponSettings.characterAnimatorController.animationClips;
            foreach (var clip in clips)
            {
                if (_idlePose == null && clip.name.Contains(TacShooterUtility.Animator_Idle.name))
                {
                    _idlePose = clip;
                    continue;
                }
                
                if (_draw == null && clip.name.Contains(TacShooterUtility.Animator_Draw.name))
                {
                    _draw = clip;
                    continue;
                }
                
                if (_holster == null && clip.name.Contains(TacShooterUtility.Animator_Holster.name))
                {
                    _holster = clip;
                    continue;
                }

                if (_quickHolster == null && clip.name.Contains(TacShooterUtility.Animator_QuickHolster.name))
                {
                    _quickHolster = clip;
                }
            }
        }

        public virtual void Initialize(GameObject owner, Transform rightHand)
        {
            _characterAnimator = owner.GetComponentInChildren<Animator>();
            _recoilAnimation = owner.GetComponent<RecoilAnimation>();
            _fpsCamera = owner.transform.root.GetComponentInChildren<FPSCameraAnimator>();

            _weaponAnimator = transform.GetComponentInChildren<Animator>();
            _activeAmmo = tacWeaponSettings.ammoCapacity;
            _burstsLeft = tacWeaponSettings.burstRounds - 1;

            _audioSource = owner.GetComponentInChildren<AudioSource>();
            
            ProcessAnimationClips();
            if (!tacWeaponSettings.isOneHanded) return;

            AnimationClip quickPistolPose = owner.GetComponent<TacticalProceduralAnimation>()?.quickPistolPose;
            if (quickPistolPose == null) return;
            
            quickPistolPose.SampleAnimation(_characterAnimator.gameObject, 0f);
            gunRightHandPose = new KTransform(rightHand).GetRelativeTransform(new KTransform(transform), false);
        }
        
        public void RestoreWeaponVisibility()
        {
            for (int i = 0; i < transform.childCount; i++)
            {
                transform.GetChild(i).gameObject.SetActive(true);
            }
        }

        public void HideWeapon()
        {
            for (int i = 0; i < transform.childCount; i++)
            {
                transform.GetChild(i).gameObject.SetActive(false);
            }
        }

        public virtual float Draw(bool playAnimation, bool updateController = true, float visibilityDelay = -1f)
        {
            if (visibilityDelay > 0f) Invoke(nameof(RestoreWeaponVisibility), visibilityDelay);
            if (_recoilAnimation != null) _recoilAnimation.Init(tacWeaponSettings.recoilData, 
                tacWeaponSettings.fireRate, fireMode);

            if (updateController)
            {
                _characterAnimator.runtimeAnimatorController = tacWeaponSettings.characterAnimatorController;
                _weaponAnimator.runtimeAnimatorController = tacWeaponSettings.weaponAnimatorController;
            }
            
            if (!playAnimation) return 0f;
            
            _characterAnimator.SetFloat(TacShooterUtility.Animator_DrawSpeed.hash, tacWeaponSettings.drawSpeed);
            
            int equipLayer = -1;
            if (!_hasPlayedInitialEquipAnimation)
            {
                for (int i = 0; i < _characterAnimator.layerCount; i++)
                {
                    if (_characterAnimator.HasState(i, TacShooterUtility.Animator_Equip.hash))
                    {
                        equipLayer = i;
                        break;
                    }
                }
            }

            if (equipLayer >= 0)
            {
                PlaySound(tacWeaponSettings.equipSound);
                _characterAnimator.CrossFadeInFixedTime(TacShooterUtility.Animator_Equip.hash, 0f, equipLayer);
                _hasPlayedInitialEquipAnimation = true;
            }
            else if (_activeAmmo == 0)
            {
                PlaySound(tacWeaponSettings.quickDrawSound);
                PlayCharacterWeaponAnimation(TacShooterUtility.Animator_QuickDraw.hash, 0f);
                _weaponAnimator.Play(TacShooterUtility.Animator_FireOut.hash, -1, 0f);
            }
            else
            {
                PlaySound(tacWeaponSettings.drawSound);
                PlayCharacterWeaponAnimation(TacShooterUtility.Animator_Draw.hash, 0f);
            }
            
            return _draw.length;
        }

        public virtual float Holster(bool playAnimation, float visibilityDelay = -1f)
        {
            float delay = -1f;
            
            if (playAnimation)
            {
                PlayCharacterWeaponAnimation(_activeAmmo == 0
                    ? TacShooterUtility.Animator_QuickHolster.hash
                    : TacShooterUtility.Animator_Holster.hash);
                PlaySound(_activeAmmo == 0 ? tacWeaponSettings.quickHolsterSound : tacWeaponSettings.holsterSound);
                
                delay = _activeAmmo == 0 && _quickHolster != null ? _quickHolster.length : _holster.length;
            }
            
            if (visibilityDelay > 0f) Invoke(nameof(HideWeapon), visibilityDelay);
            return delay;
        }

        public virtual void Inspect()
        {
            PlayCharacterWeaponAnimation(TacShooterUtility.Animator_Inspect.hash);
            PlaySound(tacWeaponSettings.inspectSound);
        }

        public virtual void DoMagCheck()
        {
            PlayCharacterWeaponAnimation(TacShooterUtility.Animator_MagCheck.hash);
            PlaySound(tacWeaponSettings.magCheckSound);
        }

        public virtual void ToggleAttachment()
        {
            isAttachmentDeployed = !isAttachmentDeployed;
            PlayCharacterWeaponAnimation(isAttachmentDeployed
                ? TacShooterUtility.Animator_DeployAttachment.hash
                : TacShooterUtility.Animator_StowAttachment.hash);
            
            PlaySound(isAttachmentDeployed
                ? tacWeaponSettings.deployAttachmentSound
                : tacWeaponSettings.stowAttachmentSound);
        }

        protected virtual void Fire()
        {
            if (!_isFiring || _activeAmmo == 0) return;

            if (muzzleFlash != null && !isSuppressed) muzzleFlash.Play();
            if (muzzleFlashSuppressed != null && isSuppressed) muzzleFlashSuppressed.Play();
            
            if(_recoilAnimation != null) _recoilAnimation.Play();
            if(_fpsCamera != null) _fpsCamera.PlayCameraShake(tacWeaponSettings.recoilShake);
            PlayFireSound();

            _activeAmmo--;
            PlayCharacterWeaponAnimation(_activeAmmo > 0
                ? TacShooterUtility.Animator_Fire.hash
                : TacShooterUtility.Animator_FireOut.hash);

            if (_activeAmmo == 0 || fireMode == FireMode.Semi || fireMode == FireMode.Burst && _burstsLeft == 0)
            {
                StopFiring();
                return;
            }
            
            if (fireMode == FireMode.Burst) _burstsLeft--;
            Invoke(nameof(Fire), 60f / tacWeaponSettings.fireRate);
        }

        public virtual void StartFiring()
        {
            if (_activeAmmo == 0) return;
            if (Time.time - _lastShotTime < 60f / tacWeaponSettings.fireRate) return;
            
            _isFiring = true;
            if (fireMode == FireMode.Burst) _burstsLeft = tacWeaponSettings.burstRounds - 1;
            
            Fire();
        }
        
        public virtual void StopFiring()
        {
            _isFiring = false;
            if(_recoilAnimation != null) _recoilAnimation.Stop();
            CancelInvoke(nameof(Fire));
        }

        public virtual void Reload()
        {
            if (_activeAmmo == tacWeaponSettings.ammoCapacity) return;
            
            PlayCharacterWeaponAnimation(_activeAmmo == 0
                ? TacShooterUtility.Animator_ReloadEmpty.hash
                : TacShooterUtility.Animator_ReloadTac.hash);
            
            PlaySound(_activeAmmo == 0 ? tacWeaponSettings.reloadEmptySound : tacWeaponSettings.reloadTacSound);
        }

        public virtual void ChangeFireMode()
        {
            var prevFireMode = fireMode;
            
            if (fireMode == FireMode.Semi)
            {
                fireMode = tacWeaponSettings.burstRounds > 0 ? FireMode.Burst :
                    tacWeaponSettings.supportsFullAuto ? FireMode.Auto : FireMode.Semi;
            }
            else if (fireMode == FireMode.Burst)
            {
                fireMode = tacWeaponSettings.supportsFullAuto ? FireMode.Auto : FireMode.Semi;
            }
            else
            {
                fireMode = FireMode.Semi;
            }

            if(prevFireMode != fireMode)
            {
                _recoilAnimation.fireMode = fireMode;
                PlaySound(tacWeaponSettings.fireModeSwitchSound);
            }
        }

        public virtual void ReloadWeapon()
        {
            _activeAmmo = tacWeaponSettings.ammoCapacity;
        }

        public virtual void OnAiming(bool isAiming)
        {
            if (_recoilAnimation != null) _recoilAnimation.isAiming = isAiming;
            PlaySound(isAiming ? tacWeaponSettings.aimInSound : tacWeaponSettings.aimOutSound);
        }
        
        public override string GetWeaponName()
        {
            return tacWeaponSettings.weaponName;
        }

        public override int GetActiveAmmo()
        {
            return _activeAmmo;
        }

        public override int GetMaxAmmo()
        {
            return tacWeaponSettings.ammoCapacity;
        }

        public override FireMode GetFireMode()
        {
            return fireMode;
        }
        
        public Transform GetAimPoint()
        {
            return aimPoint;
        }

        protected void LateUpdate()
        {
            foreach (var visibility in bonesToHide)
            {
                if (!visibility.isHidden) continue;
                visibility.bone.localScale = new Vector3(0.001f, 0.001f, 0.001f);
            }
        }

        protected void PlaySound(AudioClip clip, float pitch = 1f, float volume = 1f)
        {
            if (_audioSource == null)
            {
                Debug.LogWarning($"Failed to play weapon sound: invalid Audio Source!");
                return;
            }

            _audioSource.pitch = pitch;
            _audioSource.volume = volume;
            _audioSource.PlayOneShot(clip);
        }
        
        protected void PlayFireSound()
        {
            var sounds = isSuppressed
                ? tacWeaponSettings.suppressedFireSounds
                : tacWeaponSettings.fireSounds;
            
            if (tacWeaponSettings.fireSounds.Count == 0) return;
            
            int index = Random.Range(0, sounds.Count - 1);
            AudioClip audioClip = sounds[index];
            float pitch = Random.Range(tacWeaponSettings.firePitchRange.x, tacWeaponSettings.firePitchRange.y);
            float volume = Random.Range(tacWeaponSettings.fireVolumeRange.x, tacWeaponSettings.fireVolumeRange.y);
            PlaySound(audioClip, pitch, volume);
        }
        
        public void AttachSuppressor()
        {
            isSuppressed = true;
        }

        public void DetachSuppressor()
        {
            isSuppressed = false;
        }
    }
}

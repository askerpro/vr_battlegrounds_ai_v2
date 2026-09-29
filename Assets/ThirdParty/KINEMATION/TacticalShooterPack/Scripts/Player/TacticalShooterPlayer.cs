// Copyright (c) 2026 KINEMATION.
// All rights reserved.

using System.Collections.Generic;
using KINEMATION.Shared.KAnimationCore.Runtime.Attributes;
using KINEMATION.Shared.KAnimationCore.Runtime.Core;
using KINEMATION.KShooterCore.Runtime;
using KINEMATION.KShooterCore.Runtime.Camera;
using KINEMATION.KShooterCore.Runtime.Character;
using KINEMATION.KShooterCore.Runtime.Weapon;
using KINEMATION.TacticalShooterPack.Scripts.Animation;
using KINEMATION.TacticalShooterPack.Scripts.Weapon;

using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using Cursor = UnityEngine.Cursor;
using Random = UnityEngine.Random;

namespace KINEMATION.TacticalShooterPack.Scripts.Player
{
    [AddComponentMenu("KINEMATION/Tactical Shooter Pack/Tactical Shooter Player")]
    public class TacticalShooterPlayer : KShooterCharacter
    {
        [Tab("General")]
        [Header("Inputs")]
        [SerializeField] protected float lookSensitivity = 1f;
        [SerializeField, Range(0f, 1f)] protected float timeScale = 1f;

        [Header("Weapons & Camera")] [SerializeField]
        protected GameObject[] weaponPrefabs;

        [SerializeField] protected FPSCameraAnimator fpsCamera;
        
        [Tab("Animation")]
        [SerializeField] protected IKMotion aimIkMotion;
        [SerializeField] protected IKMotion fireModeIkMotion;
        
        [Tab("Sounds")]
        
        [Header("Actions")]
        [SerializeField] protected AudioClip quickDrawSound;
        [SerializeField] protected AudioClip quickHolsterSound;
        [SerializeField] protected AudioClip jumpSound;
        [SerializeField] protected AudioClip landSound;
        
        [Header("Movement")]
        [SerializeField] private List<AudioClip> walkSounds;
        [SerializeField] private List<AudioClip> sprintSounds;
        [SerializeField] private float walkDelay = 0.4f;
        [SerializeField] private float sprintDelay = 0.4f;
        
        protected TacticalWeaponSettings _weaponSettings;

        protected List<TacticalShooterWeapon> _weapons;
        protected int _activeWeaponIndex;
        
        // Pistol quick draw.
        protected int _quickDrawWeaponIndex;
        protected bool _quickDrawPistol;
        protected bool _isAiming;

        protected Animator _animator;
        protected bool _wantsToSprint;

        protected AudioSource _audioSource;
        protected TacticalProceduralAnimation _tacProceduralAnimation;
        
        protected float _playback = 0f;
        protected bool _hasActiveAction;

        protected float _leanInput;

        public void OnActionStarted()
        {
            _hasActiveAction = true;
            _wantsToSprint = false;
        }

        public void OnActionEnded()
        {
            _hasActiveAction = false;
        }

        private void Awake()
        {
            if (fpsCamera == null) fpsCamera = transform.root.GetComponentInChildren<FPSCameraAnimator>();
        }

        public override KShooterWeapon GetActiveShooterWeapon()
        {
            return GetActiveWeapon();
        }

        public TacticalShooterWeapon GetActiveWeapon()
        {
            return _weapons[_activeWeaponIndex];
        }

        public TacticalShooterWeapon GetPrimaryWeapon()
        {
            return _quickDrawPistol ? _weapons[_quickDrawWeaponIndex] : GetActiveWeapon();
        }

        private void Start()
        {
            Application.targetFrameRate = 120;
            Cursor.visible = false;
            Cursor.lockState = CursorLockMode.Locked;
            
            _tacProceduralAnimation = GetComponent<TacticalProceduralAnimation>();
            _audioSource = GetComponent<AudioSource>();
            _weapons = new List<TacticalShooterWeapon>();

            var bones = _tacProceduralAnimation.bones;

            foreach (var prefab in weaponPrefabs)
            {
                var weapon = Instantiate(prefab, bones.ikHandGun).GetComponentInChildren<TacticalShooterWeapon>();
                weapon.Initialize(gameObject, bones.rightHand);
                weapon.HideWeapon();
                
                _weapons.Add(weapon);
            }
            
            _animator = GetComponentInChildren<Animator>();
            EquipWeapon();
        }

        private void UpdateCurveAnimIntensity(TacCurveAnimIntensity intensity, AnimatorStateName stateName)
        {
            float value = _animator.GetFloat(stateName.hash);
            float target = GetActiveWeapon().IsFiring ? intensity.firing : intensity.standing;
            target = Mathf.Lerp(target, intensity.aiming, _tacProceduralAnimation.aimingWeight);
            _animator.SetFloat(stateName.hash, KMath.FloatInterp(value, target, 8f, Time.deltaTime));
        }

        private float GetDesiredGait()
        {
            float desiredGait = _tacProceduralAnimation.moveInput.magnitude > 0f ? 1f : 0f;

            if (_wantsToSprint)
            {
                if (_tacProceduralAnimation.moveInput.y > 0f)
                {
                    desiredGait = 2f;
                }
                else
                {
                    _wantsToSprint = false;
                }
            }

            return desiredGait;
        }
        
        protected void PlayWalkSound()
        {
            if (_audioSource == null) return;
            _audioSource.PlayOneShot(walkSounds[Random.Range(0, walkSounds.Count - 1)]);
        }
        
        protected void PlaySprintSound()
        {
            if (_audioSource == null) return;
            _audioSource.PlayOneShot(sprintSounds[Random.Range(0, sprintSounds.Count - 1)]);
        }
        
        protected void PlayMovementSounds(float gait, float error = 0.4f)
        {
            if (Mathf.Approximately(gait, 0f) || _animator.GetBool(TacShooterUtility.Animator_IsInAir.hash))
            {
                _playback = 0f;
                return;
            }

            _playback += Time.deltaTime;
            
            if (gait >= error && gait <= 1f + error)
            {
                if (_playback >= walkDelay)
                {
                    PlayWalkSound();
                    _playback = 0f;
                }
                return;
            }

            if (gait >= 1f + error && gait <= 2f + error)
            {
                if (_playback >= sprintDelay)
                {
                    PlaySprintSound();
                    _playback = 0f;
                }
            }
        }

        private void Update()
        {
            float aimingSpeed = GetPrimaryWeapon().AimingSpeed * (_isAiming ? 1f : -1f);
            _tacProceduralAnimation.aimingWeight += Time.deltaTime * aimingSpeed;
            _tacProceduralAnimation.aimingWeight = Mathf.Clamp01(_tacProceduralAnimation.aimingWeight);
            
            Transform aimPoint = GetPrimaryWeapon().GetAimPoint();
            KTransform aimTransform = KTransform.Identity;
            if (aimPoint != null)
            {
                aimTransform = new KTransform(GetPrimaryWeapon().transform);
                aimTransform =
                    aimTransform.GetRelativeTransform(new KTransform(GetPrimaryWeapon().GetAimPoint()), false);
                aimTransform.position *= -1f;
            }

            _tacProceduralAnimation.UpdateAimPoint(aimTransform);

            UpdateCurveAnimIntensity(_weaponSettings.idleIntensity, TacShooterUtility.Animator_IdleIntensity);
            UpdateCurveAnimIntensity(_weaponSettings.walkIntensity, TacShooterUtility.Animator_WalkIntensity);

            fpsCamera.lookInput.y = _tacProceduralAnimation.pitchInput;
            fpsCamera.lookInput.x = _tacProceduralAnimation.yawInput;

            _tacProceduralAnimation.leanInput = KMath.FloatInterp(_tacProceduralAnimation.leanInput, _leanInput,
                8f, Time.deltaTime);
            
            float gait = _animator.GetFloat(TacShooterUtility.Animator_Gait.hash);
            gait = KMath.FloatInterp(gait, GetDesiredGait(), 6f, Time.deltaTime);
            _animator.SetFloat(TacShooterUtility.Animator_Gait.hash, gait);
            
            PlayMovementSounds(gait);
            
#if !ENABLE_INPUT_SYSTEM
            UpdateLegacyInputs();
#endif
        }

        private void OnValidate()
        {
            Time.timeScale = timeScale;
        }

        protected void EquipWeapon(bool playDraw = true)
        {
            Transform weaponTransform = GetActiveWeapon().GetWeaponRoot();
            weaponTransform.parent = _tacProceduralAnimation.bones.ikHandGun;
            weaponTransform.localPosition = Vector3.zero;
            weaponTransform.localRotation = Quaternion.identity;
            
            _weaponSettings = GetActiveWeapon().tacWeaponSettings;
            _tacProceduralAnimation.UpdateAnimationSettings(GetActiveWeapon());
            
            GetActiveWeapon().Draw(playDraw, true, playDraw ? 0.03f : -1f);
        }

        protected void EquipNextWeapon()
        {
            GetActiveWeapon().HideWeapon();
            
            _activeWeaponIndex++;
            _activeWeaponIndex = _activeWeaponIndex > _weapons.Count - 1 ? 0 : _activeWeaponIndex;
            
            EquipWeapon(false);
        }

        protected void EquipPreviousWeapon()
        {
            GetActiveWeapon().HideWeapon();
            
            _activeWeaponIndex--;
            _activeWeaponIndex = _activeWeaponIndex < 0 ? _weapons.Count - 1 : _activeWeaponIndex;
            
            EquipWeapon(false);
        }

        protected void ChangeWeapon()
        {
            GetActiveWeapon().HideWeapon();
            
            _activeWeaponIndex++;
            _activeWeaponIndex = _activeWeaponIndex > _weapons.Count - 1 ? 0 : _activeWeaponIndex;

            EquipWeapon();
        }

        public void OnChangeWeapon()
        {
            if (_hasActiveAction || _quickDrawPistol) return;
            float delay = GetActiveWeapon().Holster(true);
            Invoke(nameof(ChangeWeapon), delay);
        }

        public void OnChangeFireMode()
        {
            var prevFireMode = GetActiveWeapon().FireMode;
            GetActiveWeapon().ChangeFireMode();
            
            if(GetActiveWeapon().FireMode != prevFireMode) _tacProceduralAnimation.PlayIkMotion(fireModeIkMotion);
        }

        public void OnReload()
        {
            if (_hasActiveAction || _quickDrawPistol) return;
            GetActiveWeapon().Reload();
        }

        public void OnMagCheck()
        {
            if (_hasActiveAction || _quickDrawPistol) return;
            GetActiveWeapon().DoMagCheck();
        }

        public void OnInspect()
        {
            if (_hasActiveAction || _quickDrawPistol) return;
            GetActiveWeapon().Inspect();
        }

        public void OnToggleAttachment()
        {
            if (_hasActiveAction || _quickDrawPistol) return;
            GetActiveWeapon().ToggleAttachment();
        }
        
        public void OnQuickPistolDraw()
        {
            if (_hasActiveAction && !_quickDrawPistol) return;
            if (GetActiveWeapon().tacWeaponSettings.isOneHanded) return;
            
            if (!_quickDrawPistol)
            {
                _quickDrawWeaponIndex = -1;
                for (int i = _activeWeaponIndex + 1; i < _weapons.Count; i++)
                {
                    if (!_weapons[i].IsOneHanded) continue;
                    _quickDrawWeaponIndex = i;
                    break;
                }

                if (_quickDrawWeaponIndex < 0)
                {
                    for (int i = 0; i < _activeWeaponIndex; i++)
                    {
                        if (!_weapons[i].IsOneHanded) continue;
                        _quickDrawWeaponIndex = i;
                        break;
                    }
                }

                if (_quickDrawWeaponIndex < 0)
                {
                    // No one-handed weapon in the inventory.
                    Debug.LogWarning("Couldn't find a one-handed weapon.");
                    return;
                }

                _quickDrawPistol = true;
                TacticalShooterWeapon pistol = _weapons[_quickDrawWeaponIndex];

                // Equip the gun without playing the animation.
                pistol.Draw(false, false, 0.2f);

                // Update the right-handed pose.
                _tacProceduralAnimation.UpdateRightHandPose(pistol.gunRightHandPose,
                    pistol.tacWeaponSettings.pistolRightHandOffset);
                
                // Parent the weapon to the main gun bone.
                Transform weaponTransform = pistol.GetWeaponRoot();
                weaponTransform.parent = _tacProceduralAnimation.bones.ikHandGun.parent;
                weaponTransform.localPosition = Vector3.zero;
                weaponTransform.localRotation = Quaternion.identity;
            }
            else
            {
                GetPrimaryWeapon().Holster(false, 0.35f);
                GetActiveWeapon().Draw(false);
                _quickDrawPistol = false;
                _quickDrawWeaponIndex = -1;
            }
            
            if(_audioSource != null) _audioSource.PlayOneShot(_quickDrawPistol ? quickDrawSound : quickHolsterSound);
            _animator.SetBool(TacShooterUtility.Animator_UseQuickDraw.hash, _quickDrawPistol);
        }

        public void OnAim()
        {
            _isAiming = !_isAiming;
            GetPrimaryWeapon().OnAiming(_isAiming);
            _tacProceduralAnimation.PlayIkMotion(aimIkMotion);

            float aimFov = GetPrimaryWeapon().tacWeaponSettings.aimFov;
            fpsCamera.SetTargetFOV(_isAiming ? aimFov : fpsCamera.BaseFOV, 6f);
        }
        
        public void OnFreeLook()
        {
            fpsCamera.ToggleFreeLook();
        }

        private void UpdateLookInput(Vector2 delta)
        {
            if (fpsCamera.UseFreeLook)
            {
                fpsCamera.AddFreeLookInput(delta);
                return;
            }
            
            _tacProceduralAnimation.deltaLookInput = delta;

            _tacProceduralAnimation.pitchInput -= delta.y;
            _tacProceduralAnimation.yawInput += delta.x;

            _tacProceduralAnimation.pitchInput = Mathf.Clamp(_tacProceduralAnimation.pitchInput, -90f, 90f);
            _tacProceduralAnimation.yawInput = Mathf.Clamp(_tacProceduralAnimation.yawInput, -90f, 90f);
        }

        private void UpdateLegacyInputs()
        {
            if(Input.GetKeyDown(KeyCode.Mouse1)) OnAim();
            if(Input.GetKeyDown(KeyCode.Mouse0)) GetPrimaryWeapon().StartFiring();
            if(Input.GetKeyUp(KeyCode.Mouse0)) GetPrimaryWeapon().StopFiring();
            if(Input.GetKeyDown(KeyCode.T)) OnFreeLook();

            _tacProceduralAnimation.moveInput.x = Input.GetAxis("Horizontal");
            _tacProceduralAnimation.moveInput.y = Input.GetAxis("Vertical");

            Vector2 deltaLook = new Vector2(Input.GetAxis("Mouse X"), Input.GetAxis("Mouse Y"));
            UpdateLookInput(deltaLook);
        }

#if ENABLE_INPUT_SYSTEM
        public void OnFire(InputValue value)
        {
            if (_hasActiveAction)
            {
                if (GetPrimaryWeapon().IsFiring) GetPrimaryWeapon().StopFiring();
                return;
            }
            
            if (value.isPressed)
            {
                GetPrimaryWeapon().StartFiring();
                return;
            }
            
            GetPrimaryWeapon().StopFiring();
        }
        
        public void OnMove(InputValue value)
        {
            _tacProceduralAnimation.moveInput = value.Get<Vector2>();
        }

        public void OnLook(InputValue value)
        {
            UpdateLookInput(value.Get<Vector2>() * lookSensitivity);
        }

        public void OnLean(InputValue value)
        {
            _leanInput = -value.Get<float>() * 30f;
        }

        public void OnSprint(InputValue value)
        {
            if (_hasActiveAction) return;
            
            _wantsToSprint = value.isPressed;
        }

        public void OnMouseScroll(InputValue value)
        {
            if (_hasActiveAction || _quickDrawPistol) return;
            
            float direction = value.Get<float>();
            
            if (direction > 0f)
            {
                EquipNextWeapon();
            }
            else if (direction < 0f)
            {
                EquipPreviousWeapon();
            }
            
            GetActiveWeapon().RestoreWeaponVisibility();
        }
#endif
    }
}

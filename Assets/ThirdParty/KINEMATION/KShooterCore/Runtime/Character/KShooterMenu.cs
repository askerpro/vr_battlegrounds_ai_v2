// Copyright (c) 2026 KINEMATION.
// All rights reserved.

using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace KINEMATION.KShooterCore.Runtime.Character
{
    public class KShooterMenu : MonoBehaviour
    {
        [Header("UI")]
        [SerializeField] protected GameObject controlsMenu;

        [SerializeField] protected TMP_Text weaponText;
        [SerializeField] protected TMP_Text ammoLeftText;
        [SerializeField] protected TMP_Text ammoTotalText;
        [SerializeField] protected TMP_Text fireModeText;
        [SerializeField] protected TMP_Text activeAnimationText;

        [Header("Animation")]
        [SerializeField] protected List<string> activeAnimationLayers;

        protected Animator _animator;
        protected bool _areControlsToggled;
        protected KShooterCharacter _shooterCharacter;
        
        private void Start()
        {
            _shooterCharacter = FindObjectsByType<KShooterCharacter>(FindObjectsSortMode.None)[0];
            if (_shooterCharacter == null) return;
            
            _animator = _shooterCharacter.GetComponentInChildren<Animator>();
            controlsMenu.SetActive(false);
        }

        private bool TryGetActiveAnimName(string layerName)
        {
            int layerIndex = _animator.GetLayerIndex(layerName);
            var stateInfo = _animator.GetCurrentAnimatorStateInfo(layerIndex);
            if (stateInfo.normalizedTime >= 1f) return false;
            
            var clipInfos = _animator.GetCurrentAnimatorClipInfo(layerIndex);
            if (clipInfos.Length == 0) return false;
            
            activeAnimationText.SetText(clipInfos[0].clip.name);
            return true;
        }
        
        private void UpdateActiveAnimationName()
        {
            foreach (var layerName in activeAnimationLayers)
            {
                if (TryGetActiveAnimName(layerName)) return;
            }
            
            activeAnimationText.SetText("None");
        }
        
        private void Update()
        {
            var activeWeapon = _shooterCharacter.GetActiveShooterWeapon();
            
            weaponText.SetText(activeWeapon.GetWeaponName());
            ammoLeftText.SetText(activeWeapon.GetActiveAmmo().ToString());
            ammoTotalText.SetText(activeWeapon.GetMaxAmmo().ToString());
            fireModeText.SetText(activeWeapon.GetFireMode().ToString());
            
            UpdateActiveAnimationName();

            if (Input.GetKeyDown(KeyCode.Tab))
            {
                _areControlsToggled = !_areControlsToggled;
                controlsMenu.SetActive(_areControlsToggled);
            }

            if (!Application.isEditor && Input.GetKeyDown(KeyCode.Escape)) Application.Quit(0);
        }
    }
}
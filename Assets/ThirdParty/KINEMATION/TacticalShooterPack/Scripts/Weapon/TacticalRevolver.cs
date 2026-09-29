// Copyright (c) 2026 KINEMATION.
// All rights reserved.

using System.Collections.Generic;
using UnityEngine;

namespace KINEMATION.TacticalShooterPack.Scripts.Weapon
{
    [AddComponentMenu("KINEMATION/Tactical Shooter Pack/Tactical Revolver")]
    public class TacticalRevolver : TacticalShooterWeapon
    {
        [SerializeField] protected AnimationClip[] reloadClips;
        [SerializeField] protected List<AudioClip> reloadSounds;
        
        protected int[] _reloadHashes;

        public void PlayRevolverSound(string soundName)
        {
            if (string.IsNullOrEmpty(soundName)) return;
            PlaySound(reloadSounds.Find(clip => clip.name.ToLower().Contains(soundName.ToLower())));
        }

        public override void Initialize(GameObject owner, Transform rightHand)
        {
            base.Initialize(owner, rightHand);
            
            _reloadHashes = new int[reloadClips.Length];

            for (int i = 0; i < _reloadHashes.Length; i++)
            {
                _reloadHashes[i] = Animator.StringToHash($"Reload_0{i + 1}");
            }
        }

        public override void Reload()
        {
            if (_activeAmmo == tacWeaponSettings.ammoCapacity) return;

            int index = GetMaxAmmo() - _activeAmmo - 1;
            if (index < 0 || index > GetMaxAmmo() - 1) return;
            PlayCharacterWeaponAnimation(_reloadHashes[index]);
        }
    }
}

// Copyright (c) 2026 KINEMATION.
// All rights reserved.

using KINEMATION.ProceduralRecoilAnimationSystem.Runtime;
using UnityEngine;

namespace KINEMATION.KShooterCore.Runtime.Weapon
{
    public abstract class KShooterWeapon : MonoBehaviour
    {
        public virtual string GetWeaponName()
        {
            return string.Empty;
        }

        public virtual int GetActiveAmmo()
        {
            return 0;
        }

        public virtual int GetMaxAmmo()
        {
            return 0;
        }

        public virtual FireMode GetFireMode()
        {
            return FireMode.Semi;
        }
    }
}
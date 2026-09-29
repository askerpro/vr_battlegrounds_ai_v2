// Copyright (c) 2026 KINEMATION.
// All rights reserved.

using KINEMATION.KShooterCore.Runtime.Weapon;
using UnityEngine;

namespace KINEMATION.KShooterCore.Runtime.Character
{
    public abstract class KShooterCharacter : MonoBehaviour
    {
        public virtual KShooterWeapon GetActiveShooterWeapon()
        {
            return null;
        }
    }
}
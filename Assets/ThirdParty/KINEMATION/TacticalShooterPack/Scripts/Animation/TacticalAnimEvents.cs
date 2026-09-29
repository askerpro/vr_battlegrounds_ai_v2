// Copyright (c) 2026 KINEMATION.
// All rights reserved.

using System;
using KINEMATION.TacticalShooterPack.Scripts.Player;
using UnityEngine;

namespace KINEMATION.TacticalShooterPack.Scripts.Animation
{
    public class TacticalAnimEvents : MonoBehaviour
    {
        private TacticalShooterPlayer _player;
        
        private void Start()
        {
            _player = GetComponentInParent<TacticalShooterPlayer>();
        }

        public void OnActionStarted()
        {
            _player.OnActionStarted();
        }

        public void OnActionEnded()
        {
            _player.OnActionEnded();
        }
    }
}
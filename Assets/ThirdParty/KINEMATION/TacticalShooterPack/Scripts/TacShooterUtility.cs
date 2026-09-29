// Copyright (c) 2026 KINEMATION.
// All rights reserved.

using UnityEngine;

namespace KINEMATION.TacticalShooterPack.Scripts
{
    public struct AnimatorStateName
    {
        public string name;
        public int hash;

        public AnimatorStateName(string name)
        {
            this.name = name;
            hash = Animator.StringToHash(name);
        }
    }
    
    public class TacShooterUtility
    {
        public const string TacAssetMenuPath = "KINEMATION/Tactical Shooter Pack/";
        
        public static AnimatorStateName Animator_Idle = new AnimatorStateName("Idle");
        
        public static AnimatorStateName Animator_Draw = new AnimatorStateName("Draw");
        public static AnimatorStateName Animator_Equip = new AnimatorStateName("Equip");
        public static AnimatorStateName Animator_Holster = new AnimatorStateName("Holster");
        public static AnimatorStateName Animator_QuickDraw = new AnimatorStateName("Quick_Draw");
        public static AnimatorStateName Animator_QuickHolster = new AnimatorStateName("Quick_Holster");
        public static AnimatorStateName Animator_IsInAir = new AnimatorStateName("IsInAir");
        
        public static AnimatorStateName Animator_Inspect = new AnimatorStateName("Inspect");
        public static AnimatorStateName Animator_MagCheck = new AnimatorStateName("MagCheck");
        public static AnimatorStateName Animator_DeployAttachment = new AnimatorStateName("DeployAttachment");
        public static AnimatorStateName Animator_StowAttachment = new AnimatorStateName("StowAttachment");
        public static AnimatorStateName Animator_ReloadEmpty = new AnimatorStateName("Reload_Empty");
        public static AnimatorStateName Animator_ReloadTac = new AnimatorStateName("Reload_Tac");
        public static AnimatorStateName Animator_ReloadStart = new AnimatorStateName("Reload_Start");
        public static AnimatorStateName Animator_ReloadStartEmpty = new AnimatorStateName("Reload_Start_Empty");
        public static AnimatorStateName Animator_ReloadLoop = new AnimatorStateName("Reload_Loop");
        public static AnimatorStateName Animator_ReloadEnd = new AnimatorStateName("Reload_End");
        public static AnimatorStateName Animator_Fire = new AnimatorStateName("Fire");
        public static AnimatorStateName Animator_FireOut = new AnimatorStateName("FireOut");
        public static AnimatorStateName Animator_MagProgress = new AnimatorStateName("MagProgress");
        public static AnimatorStateName Animator_PistolQuickDraw = new AnimatorStateName("PistolQuickDraw");
        public static AnimatorStateName Animator_UseQuickDraw = new AnimatorStateName("UseQuickDraw");
        public static AnimatorStateName Animator_Gait = new AnimatorStateName("Gait");
        public static AnimatorStateName Animator_DrawSpeed = new AnimatorStateName("DrawSpeed");
        
        public static AnimatorStateName Animator_IdleIntensity = new AnimatorStateName("IdleIntensity");
        public static AnimatorStateName Animator_WalkIntensity = new AnimatorStateName("WalkIntensity");
    }
}
using System;
using UnityEngine;

namespace VrBattlegrounds.Player.WallPass
{
    public enum WallPassStage { Clear, Hint, Violating }
    public enum WallPassCause { None, HeadEmbedded, NoSupport, CrossedBarrier, Timeout }

    [Serializable]
    public struct WallPassStatus
    {
        public WallPassStage Stage;
        public double EnteredAt;
        public double DeathAt;
        public Vector3 ReturnPoint;
        public bool Punitive;
        public WallPassCause Cause;
    }

    [Serializable]
    public sealed class WallPassSettings
    {
        [Min(0)] public float ContactDamage = 10;
        [Min(0)] public float DamageCooldown = 1;
        [Min(0)] public float DeathDelay = 3;

        // Атрибут Min защищает инспектор, но не NaN/Infinity из кода или сериализации.
        public float SafeContactDamage => SafeNonNegative(ContactDamage, 10);
        public float SafeDamageCooldown => SafeNonNegative(DamageCooldown, 1);
        public float SafeDeathDelay => SafeNonNegative(DeathDelay, 3);

        private static float SafeNonNegative(float value, float fallback) =>
            float.IsNaN(value) || float.IsInfinity(value) ? fallback : Math.Max(0, value);
    }

    public struct WallPassObservation
    {
        public bool HasOriginalSupport;
        public bool HasAnySupport;
        public Vector3 Support;
        public float HeadDepth;
        public float HeadClearance;
        public float LeanDistance;
        // Не просто другая опора: путь ног/таза к ней пересёк преграду, голова уже снаружи.
        public bool CrossedBarrier;
    }

    public struct WallPassDecision
    {
        public WallPassStatus Status;
        public bool DealContactDamage;
        public bool Kill;
    }
}

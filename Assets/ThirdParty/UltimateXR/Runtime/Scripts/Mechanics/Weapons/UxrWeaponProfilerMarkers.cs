// --------------------------------------------------------------------------------------------------------------------
// <copyright file="UxrWeaponProfilerMarkers.cs" company="VRMADA">
//   Copyright (c) VRMADA, All rights reserved.
// </copyright>
// --------------------------------------------------------------------------------------------------------------------
using Unity.Profiling;

namespace UltimateXR.Mechanics.Weapons
{
    /// <summary>
    ///     VR Battlegrounds patch 79 (shot-cost-probe): маркеры профайлера тракта выстрела SDK.
    ///     Только замер: поведение не меняется, маркеры не аллоцируют. Имена ищутся в Unity Profiler
    ///     и читаются <c>ProfilerRecorder</c> (категория Scripts). Игровые маркеры (<c>WS.*</c>) объявлены
    ///     в своих классах сборки игры.
    /// </summary>
    internal static class UxrWeaponProfilerMarkers
    {
        // Выстрел по учёту (UxrFirearmWeapon.Readiness).
        public static readonly ProfilerMarker ShotPrepare     = new ProfilerMarker("Uxr.Shot.Prepare");
        public static readonly ProfilerMarker ShotBuildCommit = new ProfilerMarker("Uxr.Shot.BuildCommit");
        public static readonly ProfilerMarker ShotValidate    = new ProfilerMarker("Uxr.Shot.Validate");
        public static readonly ProfilerMarker ShotWrite       = new ProfilerMarker("Uxr.Shot.Write");
        public static readonly ProfilerMarker ShotAudio       = new ProfilerMarker("Uxr.Shot.Audio");
        public static readonly ProfilerMarker ShotEvent       = new ProfilerMarker("Uxr.Shot.Event");
        public static readonly ProfilerMarker ShotEndSync     = new ProfilerMarker("Uxr.Shot.EndSync");

        // Источник снаряда (UxrProjectileSource).
        public static readonly ProfilerMarker SourceMuzzleFx   = new ProfilerMarker("Uxr.Source.MuzzleFx");
        public static readonly ProfilerMarker SourceProjectile = new ProfilerMarker("Uxr.Source.Projectile");

        // Менеджер снарядов (UxrWeaponManager).
        public static readonly ProfilerMarker ProjectileUpdate = new ProfilerMarker("Uxr.Wm.ProjectileUpdate");
        public static readonly ProfilerMarker ImpactFx         = new ProfilerMarker("Uxr.Wm.Impact.Fx");
        public static readonly ProfilerMarker ImpactDecal      = new ProfilerMarker("Uxr.Wm.Impact.Decal");
        public static readonly ProfilerMarker ImpactActor      = new ProfilerMarker("Uxr.Wm.Impact.Actor");
    }
}

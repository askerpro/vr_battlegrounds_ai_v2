using System;
using System.Collections.Generic;
using System.Linq;
using Mirror;
using UltimateXR.Core;
using UltimateXR.Mechanics.Weapons;
using UnityEngine;
using VrBattlegrounds.Bots;
using VrBattlegrounds.Player;

namespace VrBattlegrounds.DevTools.BotCombatStand
{
    /// <summary>Условное окно наблюдения по публичному SDK. Не доказывает empty manager или terminal конкретного shot.</summary>
    internal sealed class BotStandShotTailObservation : IDisposable
    {
        private sealed class Descriptor
        {
            public UxrShotDescriptor Value;
            public bool Auto;
            public float Speed, Distance, Length;
            public int Mask;
            public Transform Origin;
            public bool Matches(UxrShotDescriptor value) => value == Value && value != null &&
                value.UseAutomaticProjectileTrajectory == Auto && value.ProjectileSpeed == Speed &&
                value.ProjectileMaxDistance == Distance && value.ProjectileLength == Length &&
                (int)value.CollisionLayerMask == Mask && value.ShotSource == Origin;
        }

        private readonly BotStandSession _session;
        private readonly PlayerController _body, _target;
        private readonly BotGunner _gunner;
        private readonly BotRuntimeController _runtime;
        private readonly UxrActor _subjectActor, _targetActor;
        private readonly float _deadline;
        private readonly float _startRealtime = Time.realtimeSinceStartup;
        private readonly List<object> _events = new List<object>();
        private readonly List<object> _cycles = new List<object>();
        private readonly Dictionary<string, object> _metrics;
        private readonly List<Descriptor> _descriptors = new List<Descriptor>();
        private readonly Collider[] _colliders;
        private UxrWeaponManager _manager;
        private UxrFirearmWeapon _weapon;
        private UxrProjectileSource _source;
        private IDisposable _fireWindow;
        private string _failure;
        private int _emissions, _fired, _hits, _foreignDamage, _positiveCycles;
        private int _cutoffFrame, _skipFrame = -1, _budgetFrame = -1, _cycleStartedFrame = -1;
        private bool _cycleStartedAfterCutoff;
        private float _cycleStartedTime, _cutoffRealtime, _budget, _advanced;
        private bool _disposed;
        private bool _sourceBound;
        public bool ActiveEnded { get; private set; }
        public bool Finished { get; private set; }
        public bool ObservationIncomplete => !Finished || (bool)_metrics["observationIncomplete"];
        public string EndReason => _metrics.TryGetValue("endReason", out object reason) ? reason as string : "observationPending";

        public BotStandShotTailObservation(BotStandSession session, BotStandCaseResult result, float start, float duration)
        {
            _session = session;
            _body = session.Subject.ActiveAvatar;
            _target = session.Target != null ? session.Target.ActiveAvatar : null;
            _gunner = _body.GetComponent<BotGunner>();
            _runtime = _body.GetComponent<BotRuntimeController>();
            _subjectActor = _body._actor;
            _targetActor = _target != null ? _target._actor : null;
            _colliders = _target != null ? _target.GetComponentsInChildren<Collider>().Where(c => c.enabled &&
                c.gameObject.activeInHierarchy && c.gameObject.layer == LayerMask.NameToLayer("Player")).ToArray() : Array.Empty<Collider>();
            _deadline = start + duration;
            _metrics = new Dictionary<string, object>
            {
                ["activeStartGameTime"] = start, ["activeDurationSeconds"] = duration, ["deadlineGameTime"] = _deadline,
                ["started"] = Clock(), ["subjectBodyId"] = _body.GetInstanceID(), ["targetBodyId"] = _target != null ? _target.GetInstanceID() : 0,
                ["targetColliderIds"] = _colliders.Select(c => c.GetInstanceID()).ToArray(),
                ["cycles"] = _cycles, ["events"] = _events, ["observationIncomplete"] = true,
                ["tailRealtimeCapSeconds"] = 15f, ["activeRealtimeCapSeconds"] = 150f,
                ["publicConditions"] = "стабильные ShotTypes: auto, finite speed>0, finite maxDistance>0; WeaponSystemEnabled; положительные полные UXR циклы",
                ["endpoint"] = "Условная полнота окна измерения. Exact shot identity / projectile terminal / empty manager недоступны."
            };
            result.Metrics["shotTail"] = _metrics;
            _fireWindow = BotDirector.Instance.OpenShotWindow(session.Subject, _deadline);
            if (_runtime != null) _runtime.ShotSampled += OnShot;
            if (_targetActor != null) _targetActor.DamageReceived += OnDamage;
            UxrManager.AvatarsUpdating += BeforeCycle;
            UxrManager.AvatarsUpdated += AfterCycle;
            BindSource();
        }

        private static Dictionary<string, object> Clock() => new Dictionary<string, object>
        {
            ["frame"] = Time.frameCount, ["gameTime"] = Time.time,
            ["realtime"] = Time.realtimeSinceStartup, ["networkTime"] = NetworkTime.localTime
        };

        private object Counts() => new Dictionary<string, object>
        {
            ["clock"] = Clock(), ["fired"] = _fired, ["sourceEmissions"] = _emissions,
            ["gunnerShotsFired"] = _gunner != null ? _gunner.ShotsFired : 0,
            ["hitsOnTarget"] = _hits, ["foreignDamageEventsOnTarget"] = _foreignDamage
        };

        private void Fail(string reason)
        {
            if (_failure == null) _failure = reason;
            if (Finished)
            {
                _metrics["observationIncomplete"] = true;
                _metrics["endReason"] = _failure;
            }
        }

        private void BindSource()
        {
            if (_sourceBound || _gunner == null || _gunner.SelectedFirearm == null) return;
            _weapon = _gunner.SelectedFirearm;
            _source = _weapon.GetComponent<UxrProjectileSource>();
            if (_source == null) { Fail("sourceMissing"); return; }
            _sourceBound = true;
            _source.ShotFired += OnEmission;
            if (_source.ShotTypes == null || _source.ShotTypes.Count == 0) { Fail("descriptorsMissing"); return; }
            var rows = new List<object>();
            for (int i = 0; i < _source.ShotTypes.Count; i++)
            {
                UxrShotDescriptor value = _source.ShotTypes[i];
                if (value == null) { Fail("descriptorMissing"); continue; }
                var descriptor = new Descriptor { Value = value, Auto = value.UseAutomaticProjectileTrajectory,
                    Speed = value.ProjectileSpeed, Distance = value.ProjectileMaxDistance, Length = value.ProjectileLength,
                    Mask = value.CollisionLayerMask, Origin = value.ShotSource };
                _descriptors.Add(descriptor);
                bool valid = descriptor.Auto && PositiveFinite(descriptor.Speed) && PositiveFinite(descriptor.Distance) &&
                    PositiveFinite(descriptor.Distance / descriptor.Speed);
                if (!valid) Fail("descriptorOutsideAutomaticFiniteFlightContract");
                else _budget = Mathf.Max(_budget, descriptor.Distance / descriptor.Speed);
                rows.Add(new Dictionary<string, object> { ["index"] = i, ["auto"] = descriptor.Auto,
                    ["speedMps"] = descriptor.Speed, ["maxDistanceM"] = descriptor.Distance,
                    ["lengthM"] = descriptor.Length, ["collisionMask"] = descriptor.Mask,
                    ["originId"] = descriptor.Origin != null ? descriptor.Origin.GetInstanceID() : 0, ["valid"] = valid });
            }
            _metrics["descriptors"] = rows;
            _metrics["budgetGameSeconds"] = _budget;
            _metrics["sourceId"] = _source.GetInstanceID();
            _metrics["weaponId"] = _weapon.GetInstanceID();
            _metrics["weaponUniqueId"] = _weapon.UniqueId.ToString();
        }

        private static bool PositiveFinite(float value) => value > 0f && !float.IsNaN(value) && !float.IsInfinity(value);

        private void BeforeCycle()
        {
            if (Finished) return;
            if (!ActiveEnded && Time.time >= _deadline) CaptureActiveEnd("beforeUxrDeadline");
            _cycleStartedFrame = Time.frameCount;
            _cycleStartedTime = Time.time;
            _cycleStartedAfterCutoff = ActiveEnded && Time.frameCount > _cutoffFrame;
            // Проверка до manager: выключенный в начале цикла SDK не считается положительным update.
            if (!UxrWeaponManager.HasInstance || !UxrWeaponManager.Instance.WeaponSystemEnabled) Fail("weaponManagerDisabledOrMissing");
        }

        private void AfterCycle()
        {
            if (Finished) return;
            try
            {
                BindSource();
                CheckPremises();
                if (!ActiveEnded && Time.time >= _deadline) CaptureActiveEnd("afterUxrDeadline");
                if (!ActiveEnded) return;
                if (_failure != null) { End(_failure); return; }
                bool full = _cycleStartedAfterCutoff && _cycleStartedFrame == Time.frameCount &&
                    _cycleStartedTime >= _deadline && Time.frameCount > _cutoffFrame;
                if (!full) return;
                float delta = Time.deltaTime;
                if (!PositiveFinite(delta) || Time.timeScale <= 0f) { End("pausedOrNonPositiveManagerCycle"); return; }
                string phase;
                if (_skipFrame < 0) { _skipFrame = Time.frameCount; phase = "firstFullCycleSkipped"; }
                else if (Time.frameCount <= _skipFrame) return;
                else if (_budgetFrame >= 0 && Time.frameCount > _budgetFrame) phase = "extraFullCycle";
                else
                {
                    _advanced += delta;
                    _positiveCycles++;
                    phase = "flightBudget";
                    if (_advanced >= _budget) _budgetFrame = Time.frameCount;
                }
                var row = Clock(); row["phase"] = phase; row["deltaTime"] = delta; row["advancedGameSeconds"] = _advanced;
                row["weaponSystemEnabled"] = _manager != null && _manager.WeaponSystemEnabled;
                _cycles.Add(row);
                if (phase == "extraFullCycle") End("conditionalObservationBudgetCompleted");
            }
            catch (Exception e) { Fail("observationError: " + e.Message); if (ActiveEnded) End(_failure); }
        }

        private void CheckPremises()
        {
            if (_body == null || _session.Subject == null || _session.Subject.ActiveAvatar != _body || !_body.IsAlive ||
                !_body.gameObject.activeInHierarchy || _body._actor != _subjectActor) Fail("subjectBodyOrActorLost");
            if (_target == null || _session.Target == null || _session.Target.ActiveAvatar != _target || !_target.IsAlive ||
                !_target.gameObject.activeInHierarchy || _target._actor != _targetActor) Fail("targetBodyOrActorLost");
            if (_colliders.Length == 0 || _colliders.Any(c => c == null || !c.enabled || !c.gameObject.activeInHierarchy ||
                c.gameObject.layer != LayerMask.NameToLayer("Player"))) Fail("targetColliderLost");
            if (!UxrWeaponManager.HasInstance || !UxrWeaponManager.Instance.WeaponSystemEnabled) Fail("weaponManagerDisabledOrMissing");
            else if (_manager == null) _manager = UxrWeaponManager.Instance;
            else if (_manager != UxrWeaponManager.Instance) Fail("weaponManagerChanged");
            if (_sourceBound && (_source == null || _gunner == null || _gunner.SelectedFirearm != _weapon ||
                !_source.isActiveAndEnabled || _weapon == null || !_weapon.gameObject.activeInHierarchy ||
                _weapon.GetComponent<UxrProjectileSource>() != _source)) Fail("weaponOrSourceChanged");
            if (_source != null && (_source.ShotTypes == null || _source.ShotTypes.Count != _descriptors.Count ||
                _descriptors.Where((d, i) => !d.Matches(_source.ShotTypes[i])).Any())) Fail("descriptorChanged");
            if (Time.timeScale <= 0f) Fail("paused");
            if (Time.time >= _deadline && (BotDirector.Instance == null || BotDirector.Instance.IsShotAllowed(_body))) Fail("stopNewFireNotAcknowledged");
        }

        private void CaptureActiveEnd(string boundary)
        {
            if (ActiveEnded) return;
            ActiveEnded = true;
            _cutoffFrame = Time.frameCount;
            _cutoffRealtime = Time.realtimeSinceStartup;
            bool stopped = BotDirector.Instance != null && !BotDirector.Instance.IsShotAllowed(_body);
            _metrics["activeEnd"] = Counts();
            _metrics["activeEndBoundary"] = boundary;
            _metrics["stopAcknowledged"] = stopped;
            _metrics["tailStartBaseline"] = Counts();
            if (!stopped) Fail("stopNewFireNotAcknowledged");
            if (_source == null || _weapon == null || _descriptors.Count == 0 || !PositiveFinite(_budget)) Fail("activeEndSourceOrDescriptorsUnavailable");
        }

        /// <summary>Вызывается корутиной без game-time wait: cancel и realtime cap работают при остановившихся UXR cycles.</summary>
        public void Poll(bool cancelled, bool invalidFixture)
        {
            if (Finished) return;
            if (!ActiveEnded && Time.time >= _deadline) CaptureActiveEnd("coroutineAfterDeadline");
            if (!ActiveEnded && (Time.timeScale <= 0f || Time.realtimeSinceStartup - _startRealtime >= 150f))
            {
                CaptureActiveEnd("interruptedBeforeDeadline");
                End(Time.timeScale <= 0f ? "paused" : "activeRealtimeTimeout");
                return;
            }
            if (cancelled || invalidFixture)
            {
                if (!ActiveEnded) CaptureActiveEnd("interruptedBeforeDeadline");
                End(cancelled ? "cancelled" : "invalidFixture");
            }
            else if (ActiveEnded)
            {
                CheckPremises();
                if (_failure != null) End(_failure);
                else if (Time.realtimeSinceStartup - _cutoffRealtime >= 15f) End("tailRealtimeTimeout");
            }
        }

        private void OnEmission(int type)
        {
            if (!ActiveEnded && Time.time >= _deadline) CaptureActiveEnd("beforeLateEmission");
            _emissions++;
            if (Time.time >= _deadline) Fail("newSourceEmissionAfterDeadline");
            if (Time.time >= _deadline || type < 0 || type >= _descriptors.Count)
            {
                var row = Clock(); row["event"] = "sourceEmission"; row["shotType"] = type; _events.Add(row);
            }
            if (type < 0 || type >= _descriptors.Count) Fail("emittedDescriptorUnavailable");
        }

        private void OnShot(BotShotSample sample)
        {
            if (!ActiveEnded && Time.time >= _deadline) CaptureActiveEnd("beforeLateShotOutcome");
            if (sample.Outcome == BotShotOutcome.Fired) _fired++;
            if (Time.time < _deadline) return;
            var row = Clock(); row["event"] = "shotOutcomeAfterDeadline"; row["outcome"] = sample.Outcome.ToString(); _events.Add(row);
            if (sample.Outcome == BotShotOutcome.Fired) Fail("newFiredAfterDeadline");
        }

        private void OnDamage(object sender, UxrDamageEventArgs args)
        {
            if (args == null) return;
            if (!ActiveEnded && Time.time >= _deadline) CaptureActiveEnd("beforeLateDamage");
            if (args.ActorSource == _subjectActor) _hits++; else _foreignDamage++;
            if (Time.time < _deadline) return;
            var row = Clock(); row["event"] = "damageAfterDeadline";
            row["subjectAttribution"] = args.ActorSource == _subjectActor;
            row["damage"] = args.Damage;
            row["colliderId"] = args.RaycastHit.collider != null ? args.RaycastHit.collider.GetInstanceID() : 0;
            row["exactShotIdentity"] = "unavailable"; _events.Add(row);
        }

        private void End(string reason)
        {
            if (Finished) return;
            if (reason == "conditionalObservationBudgetCompleted" && (_emissions != _fired || _gunner == null || _gunner.ShotsFired != _fired))
            {
                Fail("shotObservationCountsDisagree");
                reason = _failure;
            }
            Finished = true;
            _metrics["observationEndCounts"] = Counts();
            _metrics["endReason"] = reason;
            _metrics["observationIncomplete"] = reason != "conditionalObservationBudgetCompleted";
            _metrics["advancedGameSeconds"] = _advanced;
            _metrics["positiveManagerBoundaryCycles"] = _positiveCycles;
            _metrics["firstSkippedCycleFrame"] = _skipFrame;
            _metrics["budgetReachedFrame"] = _budgetFrame;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (!Finished) End("disposedBeforeObservationComplete");
            _metrics["finalCounts"] = Counts();
            _metrics["observerDisposed"] = Clock();
            UxrManager.AvatarsUpdating -= BeforeCycle;
            UxrManager.AvatarsUpdated -= AfterCycle;
            if (_runtime != null) _runtime.ShotSampled -= OnShot;
            if (_targetActor != null) _targetActor.DamageReceived -= OnDamage;
            if (_source != null) _source.ShotFired -= OnEmission;
            _fireWindow?.Dispose(); _fireWindow = null;
        }
    }
}

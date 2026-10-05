using System;
using UltimateXR.Core;
using UltimateXR.Manipulation;
using UltimateXR.Mechanics.Weapons;
using UnityEngine;
using VrBattlegrounds.Core;
using VrBattlegrounds.Network;

namespace VrBattlegrounds.Weapons
{
    /// <summary>Связывает SDK fresh-press результат с policy command и локальными readonly получателями.</summary>
    [DisallowMultipleComponent, RequireComponent(typeof(UxrFirearmWeapon)), DefaultExecutionOrder(220)]
    public sealed class WeaponTriggerAttemptRouter : MonoBehaviour
    {
        [SerializeField] private int _triggerIndex;
        private UxrFirearmWeapon _firearm;
        private WeaponReadinessController _controller;
        private Func<int, UxrGrabber, UxrFirearmTriggerDecision> _previousClassifier;
        private bool _configured;
        private WeaponChamberPolicy _policy;
        public event Action<WeaponTriggerAttemptContext, UxrFirearmNotReadyReason> NotReadyAttempted;
        public bool IsConfigured => _configured;
        public int TriggerIndex => _triggerIndex;

        private void Awake() => _firearm = GetComponent<UxrFirearmWeapon>();
        private void OnEnable()
        {
            var owner = GetComponent<WeaponReadinessController>();
            if (!_configured && owner != null && owner.IsConfigured) Configure(owner, out _);
        }
        private void Start()
        {
            if (!_configured && GetComponent<WeaponReadinessController>() is WeaponReadinessController owner && owner.IsConfigured)
                Configure(owner, out _);
        }
        private void OnDisable() => Disconnect();

        public bool Configure(WeaponReadinessController owner, out string error)
        {
            error = null;
            if (_firearm == null) _firearm = GetComponent<UxrFirearmWeapon>();
            if (owner == null || owner.gameObject != gameObject || !owner.IsConfigured || owner.Profile == null ||
                !_firearm.UsesReadinessLedger(_triggerIndex))
            { error = "Router требует свой configured Controller/ledger trigger."; return false; }
            if ((_firearm.EvaluateLocalTriggerAttempt != null && _firearm.EvaluateLocalTriggerAttempt.Target != owner &&
                 _firearm.EvaluateLocalTriggerAttempt.Target != this) ||
                (_firearm.PrepareLocalTriggerAttempt != null && _firearm.PrepareLocalTriggerAttempt.Target != this) ||
                (_firearm.CaptureLocalTriggerPolicyId != null && _firearm.CaptureLocalTriggerPolicyId.Target != this))
            { error = "Trigger ports принадлежат другому владельцу."; return false; }
            Disconnect();
            _controller = owner; _policy = owner.Profile.ChamberPolicy;
            _previousClassifier = _firearm.EvaluateLocalTriggerAttempt;
            _firearm.EvaluateLocalTriggerAttempt = EvaluateAttempt;
            _firearm.PrepareLocalTriggerAttempt = PrepareAttempt;
            _firearm.CaptureLocalTriggerPolicyId = CapturePolicy;
            _firearm.LocalTriggerAttemptDecided += HandleAttempt;
            _configured = true;
            return true;
        }

        private void Disconnect()
        {
            if (_firearm != null)
            {
                if (_firearm.EvaluateLocalTriggerAttempt?.Target == this)
                    _firearm.EvaluateLocalTriggerAttempt = _previousClassifier;
                if (_firearm.PrepareLocalTriggerAttempt?.Target == this) _firearm.PrepareLocalTriggerAttempt = null;
                if (_firearm.CaptureLocalTriggerPolicyId?.Target == this) _firearm.CaptureLocalTriggerPolicyId = null;
                _firearm.LocalTriggerAttemptDecided -= HandleAttempt;
            }
            _configured = false; _controller = null; _previousClassifier = null;
        }

        private UxrFirearmTriggerDecision EvaluateAttempt(int trigger, UxrGrabber hand)
        {
            if (!_configured || !isActiveAndEnabled || trigger != _triggerIndex ||
                _controller == null || !_controller.IsConfigured || _controller.Profile.ChamberPolicy != _policy ||
                !_firearm.CanUse || !StateEventAuthority.IsAuthorOfItem(_firearm) ||
                (UxrManager.HasInstance && UxrManager.Instance.IsInsideStateSync))
                return new UxrFirearmTriggerDecision(UxrFirearmTriggerDecisionKind.OtherDenied);
            UxrFirearmTriggerDecision decision = _firearm.QueryReadinessDecision(trigger);
            if (decision.Kind == UxrFirearmTriggerDecisionKind.NotReady &&
                decision.Reason == UxrFirearmNotReadyReason.ChamberingRequired && _policy == WeaponChamberPolicy.TriggerAssistPrepareOnly)
                return new UxrFirearmTriggerDecision(UxrFirearmTriggerDecisionKind.PrepareOnlyConsumed, decision.Reason);
            return decision;
        }

        private int CapturePolicy(int trigger) => trigger == _triggerIndex ? (int)_policy : -1;

        private void PrepareAttempt(int trigger, uint pressSequence)
        {
            if (_configured && trigger == _triggerIndex && _policy == WeaponChamberPolicy.TriggerAssistPrepareOnly)
                _controller.RequestChamber(ChamberRequestOrigin.TriggerAssist, pressSequence);
        }

        private void HandleAttempt(UxrFirearmLocalTriggerAttempt attempt)
        {
            if (!_configured || attempt.TriggerIndex != _triggerIndex) return;
            var context = new WeaponTriggerAttemptContext(_firearm, attempt);
            // Ошибка косметического receiver не отменяет уже consumed/accepted SDK episode.
            if (NotReadyAttempted == null) return;
            foreach (Action<WeaponTriggerAttemptContext, UxrFirearmNotReadyReason> receiver in NotReadyAttempted.GetInvocationList())
                try { receiver(context, attempt.Decision.Reason); }
                catch (Exception exception) { GameLog.WeaponSystem.Error("Ошибка receiver попытки спуска: " + exception, this); }
        }
    }
}

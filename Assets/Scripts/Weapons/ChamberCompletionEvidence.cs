// Одноразовое подтверждение physical completion для единственного SDK ledger writer.
using UltimateXR.Manipulation;
using UltimateXR.Mechanics.Weapons;
using UnityEngine;

namespace VrBattlegrounds.Weapons
{
    public enum ChamberRequestOrigin { MagazineInsert, TriggerAssist }
    public enum ChamberCompletionKind { Chamber, CloseOnly, EmptyRest, Automation }

    public readonly struct ChamberCompletionEvidence
    {
        public readonly int TriggerIndex;
        public readonly uint Revision;
        public readonly uint CycleSequence;
        public readonly UxrGrabbableObject Magazine;
        public readonly UxrGrabbableObjectAnchor Anchor;
        public readonly ChamberCompletionKind Kind;

        public ChamberCompletionEvidence(int trigger, uint revision, uint cycle,
            UxrGrabbableObject magazine, UxrGrabbableObjectAnchor anchor, ChamberCompletionKind kind)
        {
            TriggerIndex = trigger; Revision = revision; CycleSequence = cycle;
            Magazine = magazine; Anchor = anchor; Kind = kind;
        }
    }

    // Public producer API:
    // bool Configure(WeaponReadinessProfile profile, ChamberActionBinding[] requiredBindings, out string error);
    // bool RequestChamber(ChamberRequestOrigin origin, uint pressSequence);
    // void RefreshPhysicalActionState(int triggerIndex);
    // bool TryGetPhysicalCompletion(out ChamberCompletionEvidence evidence);
    // void CancelPendingCycle();
    // bool IsConfigured { get; }
    // WeaponReadinessProfile Profile { get; }

    // Stage3 owns EvaluateLocalTriggerAttempt/episode. Configure installs a query-only default
    // for required SDK port; no SDK firing path calls it yet. Router replaces that one delegate
    // through explicit owner handoff; controller never keeps another MustRelease bool.

    // Две независимые readonly validation delegates: ValidateChamberCompletion и
    // ValidatePhysicalActionClosed. Reserved evidence существует только на stack команды;
    // Kind обязан совпасть с вызываемой SDK операцией. Initial migration имеет отдельное
    // локальное _initializing окно, actual rest и author/world контекст; не fake Begin.
    // SDK после delegate сам revalidates runtime/mag/revision/authority, поэтому adapter
    // не компенсирует внутренние commit и не держит вторую live копию ammo/ready.

    // Physical/policy APIs requested from existing owners:
    // Feedback: RestLocalPosition, PositionEpsilon, AutoReturnSpeed, IsActionHeld;
    //             ObserveAuthorManualContact, NotifyLedgerManualCompletion (audio once), opt-in suppress boolReload.
    // Visuals: public read-only Body/ContactPart/RequiredActionTargets/IsHeldEmpty;
    //          BeginOwnedChamberReturn(captured poses), AdvanceOwnedReturn(remaining), EndOwnedReturn;
    //          opt-in Empty total selection/instance hold vs return choice, no shared Motion edits.
    // Controller never writes Rounds/HasReloaded/Readiness clone; all transitions invoke SDK commands.
}

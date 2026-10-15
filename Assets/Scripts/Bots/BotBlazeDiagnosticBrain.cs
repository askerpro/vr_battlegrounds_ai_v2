using BlazeAISpace;
using UnityEngine;

namespace VrBattlegrounds.Bots
{
    /// <summary>Наблюдает публичные вызовы Blaze, каждый base вызывается ровно один раз, без нового решения.</summary>
    [AddComponentMenu("")]
    public sealed class BotBlazeDiagnosticBrain : BlazeAI
    {
        private const int EventLimit = 65536;
        private int _ownerBodyId, _generation, _proxyId, _sequence, _callId;
        private bool _coverageLost;

        internal void Configure(int ownerBodyId, int generation)
        {
            _ownerBodyId = ownerBodyId; _generation = generation; _proxyId = gameObject.GetInstanceID();
        }

        public override void Start()
        {
            // Awake базового Blaze уже создал manager. Не копируем его приватное состояние и не теряем ранние Play.
            if (BotBlazeDiagnostics.HasListeners && animManager != null && animManager.currentState == null)
                animManager = new RecordingAnimationManager(anim, this);
            else Emit(BotBlazeDiagnosticKind.CoverageLost);
            Emit(BotBlazeDiagnosticKind.ProxyStarted);
            base.Start();
        }

        internal void Removing() => Emit(BotBlazeDiagnosticKind.ProxyRemoving);

        private void Emit(BotBlazeDiagnosticKind kind, int callId = 0, BotBlazeDiagnosticCall call = default,
            bool? result = null, string caller = null)
        {
            if (!BotBlazeDiagnostics.HasListeners) return;
            if (_coverageLost && kind != BotBlazeDiagnosticKind.ProxyRemoving) return;
            if ((++_sequence > EventLimit && kind != BotBlazeDiagnosticKind.ProxyRemoving) ||
                kind == BotBlazeDiagnosticKind.CoverageLost)
            {
                _coverageLost = true; kind = BotBlazeDiagnosticKind.CoverageLost;
            }
            try
            {
                BotBlazeDiagnostics.Publish(new BotBlazeDiagnosticEvent(kind, _ownerBodyId, _proxyId, _generation,
                    _sequence, callId, new BotBlazeDiagnosticSample(this), call, result, caller));
            }
            catch (System.Exception)
            {
                // Диагностическое чтение не должно отменять следующий единственный base-вызов SDK.
                _coverageLost = true;
                BotBlazeDiagnostics.Publish(new BotBlazeDiagnosticEvent(BotBlazeDiagnosticKind.CoverageLost,
                    _ownerBodyId, _proxyId, _generation, _sequence, callId, default, call, null,
                    "Потеря диагностического snapshot; private gate не измерен."));
            }
        }

        public override bool MoveTo(Vector3 location, float moveSpeed, float turnSpeed, string animName = null,
            float animT = 0.25f, string dir = "front", float closestPointDistance = 0)
        {
            int id = ++_callId;
            var call = new BotBlazeDiagnosticCall(location, animName, animT, moveSpeed, turnSpeed, dir,
                closestPointDistance: closestPointDistance);
            Emit(BotBlazeDiagnosticKind.MoveToBefore, id, call);
            bool result = base.MoveTo(location, moveSpeed, turnSpeed, animName, animT, dir, closestPointDistance);
            Emit(BotBlazeDiagnosticKind.MoveToAfter, id, call, result);
            return result;
        }

        public override bool IsPathReachable(Vector3 position, bool addAsLastCalcPath = false)
        {
            int id = ++_callId;
            var call = new BotBlazeDiagnosticCall(position, addAsLastPath: addAsLastCalcPath);
            Emit(BotBlazeDiagnosticKind.PathBefore, id, call);
            bool result = base.IsPathReachable(position, addAsLastCalcPath);
            Emit(BotBlazeDiagnosticKind.PathAfter, id, call, result);
            return result;
        }

        public override void NonRootMotionMovement(string anim, float animT, float moveSpeed, float turnSpeed, string dir)
        {
            int id = ++_callId;
            var call = new BotBlazeDiagnosticCall(animation: anim, transitionSeconds: animT,
                moveSpeed: moveSpeed, turnSpeed: turnSpeed, direction: dir);
            Emit(BotBlazeDiagnosticKind.NonRootBefore, id, call);
            base.NonRootMotionMovement(anim, animT, moveSpeed, turnSpeed, dir);
            Emit(BotBlazeDiagnosticKind.NonRootAfter, id, call);
        }

        public override void SetState(State stateToTurnTo, bool forceSetInOffMesh = false)
        {
            int id = ++_callId;
            var call = new BotBlazeDiagnosticCall(requestedState: stateToTurnTo, forceState: forceSetInOffMesh);
            // Стек только у редкой смены решения; приватные ветви и случайное число из него не выводятся.
            string caller = null;
            try { if (BotBlazeDiagnostics.HasListeners) caller = new System.Diagnostics.StackTrace(1, false).ToString(); }
            catch (System.Exception) { Emit(BotBlazeDiagnosticKind.CoverageLost); }
            if (caller != null && caller.Length > 2048) caller = caller.Substring(0, 2048);
            Emit(BotBlazeDiagnosticKind.SetStateBefore, id, call, caller: caller);
            base.SetState(stateToTurnTo, forceSetInOffMesh);
            Emit(BotBlazeDiagnosticKind.SetStateAfter, id, call);
        }

        private sealed class RecordingAnimationManager : AnimationManager
        {
            private readonly BotBlazeDiagnosticBrain _brain;
            internal RecordingAnimationManager(Animator animator, BotBlazeDiagnosticBrain brain) : base(animator, brain)
            {
                _brain = brain;
            }

            public override void Play(string state, float time = 0.25f, bool overplay = false)
            {
                int id = ++_brain._callId;
                var call = new BotBlazeDiagnosticCall(animation: state, transitionSeconds: time, overplay: overplay);
                _brain.Emit(BotBlazeDiagnosticKind.PlayBefore, id, call);
                base.Play(state, time, overplay);
                _brain.Emit(BotBlazeDiagnosticKind.PlayAfter, id, call);
            }
        }
    }
}

using System;
using UnityEngine;
using UnityEngine.AI;
using VrBattlegrounds.Core;

namespace VrBattlegrounds.Bots
{
    public enum BotBlazeDiagnosticKind
    {
        ProxyStarted, ProxyRemoving, PlayBefore, PlayAfter, MoveToBefore, MoveToAfter,
        PathBefore, PathAfter, NonRootBefore, NonRootAfter, SetStateBefore, SetStateAfter, CoverageLost
    }

    /// <summary>Аргументы реального вызова SDK; Result задаётся только после возвращения bool.</summary>
    public readonly struct BotBlazeDiagnosticCall
    {
        public readonly Vector3 Destination;
        public readonly string Animation, Direction;
        public readonly float TransitionSeconds, MoveSpeed, TurnSpeed, ClosestPointDistance;
        public readonly bool Overplay, AddAsLastPath, ForceState;
        public readonly BlazeAI.State? RequestedState;

        public BotBlazeDiagnosticCall(Vector3 destination = default, string animation = null,
            float transitionSeconds = 0, float moveSpeed = 0, float turnSpeed = 0, string direction = null,
            bool overplay = false, bool addAsLastPath = false, BlazeAI.State? requestedState = null,
            bool forceState = false, float closestPointDistance = 0)
        {
            Destination = destination; Animation = animation; TransitionSeconds = transitionSeconds;
            MoveSpeed = moveSpeed; TurnSpeed = turnSpeed; Direction = direction; Overplay = overplay;
            AddAsLastPath = addAsLastPath; RequestedState = requestedState; ForceState = forceState;
            ClosestPointDistance = closestPointDistance;
        }
    }

    /// <summary>Снимок только публичных API. Путь агента не выдаётся за приватный путь Blaze.</summary>
    public readonly struct BotBlazeDiagnosticSample
    {
        public readonly int Frame, CurrentHash, NextHash, TransitionHash;
        public readonly double Time, Realtime;
        public readonly float DeltaTime, CurrentNt, NextNt, TransitionNt;
        public readonly bool AnimatorValid, InTransition, AnimatorEnabled, AgentEnabled, OnNavMesh,
            IsStopped, PathPending, HasPath, OffMeshLink, PathReachable, IsAttacking;
        public readonly Vector3 Position, PathCorner, AgentDestination, NextPosition, Velocity, DesiredVelocity;
        public readonly NavMeshPathStatus AgentPathStatus;
        public readonly BlazeAI.State State;
        public readonly string PreviousRequestedAnimation;

        internal BotBlazeDiagnosticSample(BlazeAI brain)
        {
            Frame = UnityEngine.Time.frameCount; Time = UnityEngine.Time.timeAsDouble;
            Realtime = UnityEngine.Time.realtimeSinceStartupAsDouble; DeltaTime = UnityEngine.Time.deltaTime;
            Position = brain.transform.position; PathCorner = brain.pathCorner; State = brain.state;
            PathReachable = brain.isPathReachable; IsAttacking = brain.isAttacking;
            PreviousRequestedAnimation = brain.animManager?.currentState;
            var animator = brain.anim;
            AnimatorEnabled = animator != null && animator.isActiveAndEnabled;
            AnimatorValid = AnimatorEnabled && animator.isInitialized && animator.layerCount > 0;
            InTransition = AnimatorValid && animator.IsInTransition(0);
            var current = AnimatorValid ? animator.GetCurrentAnimatorStateInfo(0) : default;
            var next = InTransition ? animator.GetNextAnimatorStateInfo(0) : default;
            var transition = InTransition ? animator.GetAnimatorTransitionInfo(0) : default;
            CurrentHash = current.fullPathHash; CurrentNt = current.normalizedTime;
            NextHash = next.fullPathHash; NextNt = next.normalizedTime;
            TransitionHash = transition.fullPathHash; TransitionNt = transition.normalizedTime;
            var agent = brain.navmeshAgent;
            AgentEnabled = agent != null && agent.isActiveAndEnabled;
            OnNavMesh = AgentEnabled && agent.isOnNavMesh;
            IsStopped = OnNavMesh && agent.isStopped; PathPending = OnNavMesh && agent.pathPending;
            HasPath = OnNavMesh && agent.hasPath; OffMeshLink = OnNavMesh && agent.isOnOffMeshLink;
            AgentDestination = OnNavMesh ? agent.destination : default;
            NextPosition = OnNavMesh ? agent.nextPosition : default;
            Velocity = OnNavMesh ? agent.velocity : default;
            DesiredVelocity = OnNavMesh ? agent.desiredVelocity : default;
            AgentPathStatus = OnNavMesh ? agent.pathStatus : NavMeshPathStatus.PathInvalid;
        }
    }

    /// <summary>Неизменяемая запись; нет ссылок на изменяемые Unity-объекты и приватных предположений о gate.</summary>
    public readonly struct BotBlazeDiagnosticEvent
    {
        public readonly BotBlazeDiagnosticKind Kind;
        public readonly int OwnerBodyId, ProxyId, ProxyGeneration, Sequence, CallId;
        public readonly BotBlazeDiagnosticSample Sample;
        public readonly BotBlazeDiagnosticCall Call;
        public readonly bool? Result;
        public readonly string Caller;

        internal BotBlazeDiagnosticEvent(BotBlazeDiagnosticKind kind, int ownerBodyId, int proxyId,
            int generation, int sequence, int callId, BotBlazeDiagnosticSample sample,
            BotBlazeDiagnosticCall call, bool? result, string caller)
        {
            Kind = kind; OwnerBodyId = ownerBodyId; ProxyId = proxyId; ProxyGeneration = generation;
            Sequence = sequence; CallId = callId; Sample = sample; Call = call; Result = result; Caller = caller;
        }
    }

    /// <summary>
    /// Опциональный синхронный канал стенда, только Unity main thread. Подписаться до создания прокси;
    /// callback только сохраняет запись, не управляет Blaze. Dispose обязателен при завершении прогона.
    /// Буфер принадлежит потребителю; здесь максимум четыре подписки, без истории и поиска объектов.
    /// </summary>
    public static class BotBlazeDiagnostics
    {
        private sealed class Subscription : IDisposable
        {
            internal UnityEngine.Object Lifetime;
            internal Action<BotBlazeDiagnosticEvent> Listener;
            public void Dispose() { Lifetime = null; Listener = null; }
        }

        private static readonly Subscription[] Subscriptions = new Subscription[4];

        public static IDisposable Subscribe(UnityEngine.Object lifetime, Action<BotBlazeDiagnosticEvent> listener)
        {
            if (lifetime == null) throw new ArgumentNullException(nameof(lifetime));
            if (listener == null) throw new ArgumentNullException(nameof(listener));
            for (int i = 0; i < Subscriptions.Length; i++)
            {
                var old = Subscriptions[i];
                if (old != null && old.Lifetime != null && old.Listener != null) continue;
                old?.Dispose();
                var subscription = new Subscription { Lifetime = lifetime, Listener = listener };
                Subscriptions[i] = subscription;
                return subscription;
            }
            throw new InvalidOperationException("Уже подключены четыре наблюдателя Blaze.");
        }

        /// <summary>Полнота своей подписки в момент сохранения результата.</summary>
        public static bool IsSubscribed(IDisposable handle) => handle is Subscription own &&
            own.Lifetime != null && own.Listener != null && Array.IndexOf(Subscriptions, own) >= 0;

        internal static bool HasListeners
        {
            get
            {
                bool found = false;
                for (int i = 0; i < Subscriptions.Length; i++)
                {
                    var subscription = Subscriptions[i];
                    if (subscription == null) continue;
                    if (subscription.Lifetime == null || subscription.Listener == null)
                    {
                        subscription.Dispose(); Subscriptions[i] = null;
                    }
                    else found = true;
                }
                return found;
            }
        }

        internal static void Publish(BotBlazeDiagnosticEvent value)
        {
            for (int i = 0; i < Subscriptions.Length; i++)
            {
                var subscription = Subscriptions[i];
                if (subscription == null || subscription.Lifetime == null || subscription.Listener == null) continue;
                try { subscription.Listener(value); }
                catch (Exception error)
                {
                    subscription.Dispose();
                    GameLog.Player.Warning($"[BotBlazeDiagnostics] Наблюдатель отключён: {error.GetType().Name}.");
                }
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            for (int i = 0; i < Subscriptions.Length; i++)
            {
                Subscriptions[i]?.Dispose(); Subscriptions[i] = null;
            }
        }
    }
}

using System;
using System.Collections.Generic;
using Mirror;
using UltimateXR.Avatar;
using UltimateXR.Core.StateSync;
using UltimateXR.Core.Unique;
using UltimateXR.Extensions.System;
using UltimateXR.Networking.Integrations.Net.Mirror;
using UnityEngine;

namespace VrBattlegrounds.Network
{
    internal enum AvatarRootAdmissionResult
    {
        Unscoped,
        Allowed,
        Unavailable,
        InvalidEnvelope,
        InvalidBinding,
        InvalidAlignment,
        InvalidRegistry
    }

    /// <summary>Уже существующие сетевой и SDK id цели; не новое поколение или владелец жизни.</summary>
    internal readonly struct AvatarRootEventScope
    {
        public readonly uint NetId;
        public readonly Guid UniqueId;

        public AvatarRootEventScope(uint netId, Guid uniqueId)
        {
            NetId = netId;
            UniqueId = uniqueId;
        }
    }

    /// <summary>Снимок только на синхронный Send: байты нельзя переназначать другому телу после сериализации.</summary>
    internal readonly struct AvatarRootEventCapture
    {
        public readonly UxrAvatar Avatar;
        public readonly NetworkIdentity Identity;
        public readonly AvatarRootEventScope Scope;
        public readonly Guid ExpectedCombineIdSource;

        public AvatarRootEventCapture(UxrAvatar avatar, NetworkIdentity identity, AvatarRootEventScope scope,
                                      Guid expectedCombineIdSource)
        {
            Avatar = avatar;
            Identity = identity;
            Scope = scope;
            ExpectedCombineIdSource = expectedCombineIdSource;
        }
    }

    /// <summary>Скалярная диагностика экземпляра relay. Не участвует в решении о доступности объектов.</summary>
    internal struct AvatarRootAdmissionCounters
    {
        public ulong ScopedReceived;
        public ulong ScopedDecoderEntered;
        public ulong Unavailable;
        public ulong InvalidEnvelope;
        public ulong InvalidBinding;
        public ulong InvalidAlignment;
        public ulong InvalidRegistry;
        public ulong ScopedApplied;

        /// <summary>v2: событие пришло с netId сетевого предмета (только сервер, только от клиента).</summary>
        public ulong ItemScopedReceived;

        /// <summary>v2: предмета с этим netId уже нет в spawned получателя; событие отброшено до SDK.</summary>
        public ulong ItemUnavailable;
    }

    /// <summary>
    /// Допуск только concrete UxrAvatar root до SDK decoder. Mirror владеет spawned, SDK — UniqueId и registry;
    /// helper их читает и не заводит историю, кеш жизни или ремонт идентичности. Child/item цели вне этого scope.
    /// </summary>
    internal static class AvatarRootEventAdmission
    {
        internal static AvatarRootAdmissionResult CaptureOutgoing(IUxrStateSync source,
            IReadOnlyDictionary<uint, NetworkIdentity> spawned, out AvatarRootEventCapture capture)
        {
            capture = default;
            if (!(source is UxrAvatar root)) return AvatarRootAdmissionResult.Unscoped;
            if (root == null) return AvatarRootAdmissionResult.Unavailable;
            if (root.GetComponent<UxrMirrorAvatar>() == null) return AvatarRootAdmissionResult.Unscoped;

            NetworkIdentity identity = root.GetComponent<NetworkIdentity>();
            if (identity == null) return AvatarRootAdmissionResult.InvalidBinding;
            if (identity.netId == 0 || !spawned.TryGetValue(identity.netId, out NetworkIdentity live) || live == null)
                return AvatarRootAdmissionResult.Unavailable;
            if (!ReferenceEquals(identity, live)) return AvatarRootAdmissionResult.InvalidBinding;

            Guid uniqueId = ((IUxrUniqueId)root).UniqueId;
            if (uniqueId == Guid.Empty) return AvatarRootAdmissionResult.InvalidBinding;

            // Точная формула SDK. Guid живёт только в capture: повторный SHA-256 после Serialize не нужен.
            Guid expected = identity.netId.ToString().GetGuid();
            if (((IUxrUniqueId)root).CombineIdSource != expected) return AvatarRootAdmissionResult.InvalidAlignment;

            capture = new AvatarRootEventCapture(root, identity,
                new AvatarRootEventScope(identity.netId, uniqueId), expected);
            return AvatarRootAdmissionResult.Allowed;
        }

        internal static AvatarRootAdmissionResult RevalidateOutgoing(in AvatarRootEventCapture capture,
            IReadOnlyDictionary<uint, NetworkIdentity> spawned)
        {
            if (capture.Scope.NetId == 0) return AvatarRootAdmissionResult.Unscoped;
            if (capture.Avatar == null || capture.Identity == null ||
                !spawned.TryGetValue(capture.Scope.NetId, out NetworkIdentity live) || live == null)
                return AvatarRootAdmissionResult.Unavailable;

            if (!ReferenceEquals(live, capture.Identity) || capture.Identity.netId != capture.Scope.NetId ||
                !ReferenceEquals(capture.Avatar.GetComponent<NetworkIdentity>(), capture.Identity) ||
                capture.Avatar.GetComponent<UxrMirrorAvatar>() == null ||
                ((IUxrUniqueId)capture.Avatar).UniqueId != capture.Scope.UniqueId)
                return AvatarRootAdmissionResult.InvalidBinding;

            return ((IUxrUniqueId)capture.Avatar).CombineIdSource == capture.ExpectedCombineIdSource
                ? AvatarRootAdmissionResult.Allowed
                : AvatarRootAdmissionResult.InvalidAlignment;
        }

        internal static AvatarRootAdmissionResult CheckIncoming(in AvatarRootEventScope scope,
            IReadOnlyDictionary<uint, NetworkIdentity> spawned, out UxrAvatar root)
        {
            root = null;
            // Проверка всей пары обязана предшествовать нулевой ветке: 0/nonempty не означает unscoped.
            if ((scope.NetId == 0) != (scope.UniqueId == Guid.Empty)) return AvatarRootAdmissionResult.InvalidEnvelope;
            if (scope.NetId == 0) return AvatarRootAdmissionResult.Unscoped;
            if (!spawned.TryGetValue(scope.NetId, out NetworkIdentity identity) || identity == null)
                return AvatarRootAdmissionResult.Unavailable;

            root = identity.GetComponent<UxrAvatar>();
            if (root == null || identity.netId != scope.NetId || root.GetComponent<UxrMirrorAvatar>() == null ||
                !ReferenceEquals(root.GetComponent<NetworkIdentity>(), identity) ||
                ((IUxrUniqueId)root).UniqueId != scope.UniqueId)
                return AvatarRootAdmissionResult.InvalidBinding;

            if (((IUxrUniqueId)root).CombineIdSource != scope.NetId.ToString().GetGuid())
                return AvatarRootAdmissionResult.InvalidAlignment;

            // Untyped lookup не пишет type-mismatch лог и обязан разрешить именно этот root, не любой живой объект.
            // Штатная очистка dead-owner внутри SDK остаётся ответственностью SDK.
            if (!UxrUniqueIdImplementer.TryGetComponentById(scope.UniqueId, out IUxrUniqueId found) ||
                !ReferenceEquals(found, root))
                return AvatarRootAdmissionResult.InvalidRegistry;

            return AvatarRootAdmissionResult.Allowed;
        }

        internal static AvatarRootAdmissionResult CheckDecodedTarget(in AvatarRootEventScope scope,
            UxrAvatar admittedRoot, IUxrStateSync decodedTarget)
        {
            if (scope.NetId == 0)
            {
                // Клиент не может обойти scoped admission, назвав сетевой root несетевой целью.
                return decodedTarget is UxrAvatar root && root != null && root.GetComponent<UxrMirrorAvatar>() != null
                    ? AvatarRootAdmissionResult.InvalidBinding
                    : AvatarRootAdmissionResult.Unscoped;
            }

            if (admittedRoot == null || !ReferenceEquals(decodedTarget, admittedRoot) ||
                admittedRoot.GetComponent<UxrMirrorAvatar>() == null ||
                ((IUxrUniqueId)admittedRoot).UniqueId != scope.UniqueId)
                return AvatarRootAdmissionResult.InvalidBinding;

            NetworkIdentity identity = admittedRoot.GetComponent<NetworkIdentity>();
            return identity != null && identity.netId == scope.NetId
                ? AvatarRootAdmissionResult.Allowed
                : AvatarRootAdmissionResult.InvalidBinding;
        }
    }

    /// <summary>
    /// Контракт допуска v2: ворота доступности сетевого предмета до SDK-декодера сервера.
    ///
    /// <para>
    /// Клиент-автор шлёт событие предмета (курок, затвор, отпускание) за RTT до того, как узнает, что
    /// сервер этот предмет уже снял (снаряжение в конце раунда, смерть, смена карты). На сервере
    /// SDK-декодер не находит цель и пишет Error. Клиент кладёт в конверт netId ближайшей
    /// <see cref="NetworkIdentity"/> источника; сервер, не найдя её в <c>NetworkServer.spawned</c>,
    /// отбрасывает событие тихо — без decode, Execute и рассылки.
    /// </para>
    ///
    /// <para>
    /// Это только ворота доступности, не binding: decoded target с netId не сверяется. Ближайшая
    /// NetworkIdentity компонента может смениться в полёте (магазин отделён, предмет перепарентен),
    /// а обман конверта даёт клиенту лишь потерю собственного события. Живой предмет идёт прежним
    /// строгим путём SDK. Аватары (корень и дети) и объекты без NetworkIdentity — вне scope.
    /// Mirror владеет spawned; истории снятых netId здесь нет.
    /// </para>
    /// </summary>
    internal static class ItemEventAdmission
    {
        /// <summary>
        /// netId ближайшей заспавненной NetworkIdentity источника, если это не аватар; иначе 0 (прежний путь).
        /// </summary>
        internal static uint CaptureOutgoing(IUxrStateSync source, IReadOnlyDictionary<uint, NetworkIdentity> spawned)
        {
            Component component = source?.Component;
            if (component == null) return 0;

            NetworkIdentity identity = component.GetComponentInParent<NetworkIdentity>(true);
            if (identity == null || identity.netId == 0) return 0;

            // Корень аватара ведёт AvatarRoot scope v1, дети аватара остаются на прежнем пути.
            if (identity.GetComponent<UxrAvatar>() != null) return 0;

            return spawned.TryGetValue(identity.netId, out NetworkIdentity live) && ReferenceEquals(live, identity)
                ? identity.netId
                : 0;
        }

        /// <summary>Жив ли у получателя сетевой объект с этим netId (по Mirror spawned).</summary>
        internal static bool IsAvailable(uint itemNetId, IReadOnlyDictionary<uint, NetworkIdentity> spawned)
        {
            return spawned.TryGetValue(itemNetId, out NetworkIdentity identity) && identity != null;
        }
    }
}

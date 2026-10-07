using System;
using UltimateXR.Core.Components;

namespace VrBattlegrounds.Network
{
    /// <summary>
    ///     Назначение UniqueId сгенерированному компоненту UltimateXR: какой компонент и под каким ID он
    ///     должен зарегистрироваться при пробуждении. ID вычисляет владелец манифеста (для арсенала —
    ///     <c>ArsenalGeneratedIdentityManifest</c>), выдаёт <see cref="NetworkUxrIdentity.PrepareGeneratedIdentities" />.
    /// </summary>
    public readonly struct NetworkUxrIdentityAssignment
    {
        public UxrComponent Target { get; }
        public Guid ExpectedUniqueId { get; }

        public NetworkUxrIdentityAssignment(UxrComponent target, Guid expectedUniqueId)
        {
            Target = target;
            ExpectedUniqueId = expectedUniqueId;
        }
    }
}

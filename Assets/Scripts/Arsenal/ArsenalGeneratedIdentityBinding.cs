using System;
using System.Collections.Generic;
using UltimateXR.Core.Components;
using UltimateXR.Core.Unique;
using UnityEngine;
using VrBattlegrounds.Maps.Runtime;
using VrBattlegrounds.Network;

namespace VrBattlegrounds.Arsenal
{
    /// <summary>
    ///     Связывает роли слота с компонентами его свежего неактивного клона и строит назначения UniqueId из
    ///     <see cref="ArsenalGeneratedIdentityManifest" />. Роль находится по исходному UniqueId префаба,
    ///     прочитанному с клона до пробуждения: по имени и пути объекта не ищем.
    ///     Каждый компонент с UniqueId в клоне обязан быть объявленной ролью, каждая роль — встретиться ровно
    ///     один раз; иначе отказ: необъявленный компонент получил бы на машинах разные ID.
    /// </summary>
    public static class ArsenalGeneratedIdentityBinding
    {
        /// <summary>
        ///     Роли оружейного слота: якорь оружия и якорь магазина. UniqueId читается со свежего неактивного клона —
        ///     это UniqueId компонента в префабе слота.
        /// </summary>
        public static IReadOnlyList<ArsenalTemplateRole> RolesOf(FirearmSlotController freshSlot)
        {
            if (freshSlot == null || freshSlot.ItemAnchor == null || freshSlot.MagAnchor == null)
            {
                throw new InvalidOperationException("ArsenalIdentity.Roles.MissingAnchor");
            }

            return new[]
            {
                Role(ArsenalGeneratedIdentityManifest.ItemAnchorRole, freshSlot.ItemAnchor),
                Role(ArsenalGeneratedIdentityManifest.MagazineAnchorRole, freshSlot.MagAnchor)
            };
        }

        private static ArsenalTemplateRole Role(string key, UxrComponent component)
        {
            if (!component.TryGetUninitializedRuntimeUniqueId(out Guid source))
            {
                throw new InvalidOperationException("ArsenalIdentity.Roles.NotFreshInactive:" + component.name);
            }

            return new ArsenalTemplateRole(key, source.ToString(), "", component.GetType().FullName);
        }

        public static IReadOnlyList<NetworkUxrIdentityAssignment> Bind(GameObject inactiveSlotRoot,
            IReadOnlyList<ArsenalTemplateRole> roles, MapRunKey run, string stationKey, string logicalSlotKey)
        {
            if (inactiveSlotRoot == null || roles == null)
            {
                throw new InvalidOperationException("ArsenalIdentity.Bind.NullInput");
            }

            if (inactiveSlotRoot.activeInHierarchy)
            {
                throw new InvalidOperationException("ArsenalIdentity.Bind.RootActive");
            }

            if (!ArsenalGeneratedIdentityManifest.TryValidateTemplateRoles(roles, out string rolesError))
            {
                throw new InvalidOperationException(rolesError);
            }

            var roleBySource = new Dictionary<Guid, string>();
            foreach (ArsenalTemplateRole role in roles)
            {
                roleBySource.Add(Guid.Parse(role.SourceUniqueId), role.RoleKey);
            }

            IUxrUniqueId[] identified = inactiveSlotRoot.GetComponentsInChildren<IUxrUniqueId>(true);
            var bound = new HashSet<string>(StringComparer.Ordinal);
            var assignments = new List<NetworkUxrIdentityAssignment>(identified.Length);

            foreach (IUxrUniqueId candidate in identified)
            {
                if (!(candidate is UxrComponent component))
                {
                    throw new InvalidOperationException("ArsenalIdentity.Bind.NonUxrComponentId:" + candidate.GetType().Name);
                }

                if (!component.TryGetUninitializedRuntimeUniqueId(out Guid source))
                {
                    throw new InvalidOperationException("ArsenalIdentity.Bind.NotFreshInactive:" + component.name);
                }

                if (!roleBySource.TryGetValue(source, out string roleKey))
                {
                    throw new InvalidOperationException("ArsenalIdentity.Bind.UndeclaredComponent:" + component.GetType().Name + "@" + component.name);
                }

                if (!bound.Add(roleKey))
                {
                    throw new InvalidOperationException("ArsenalIdentity.Bind.DuplicateRoleInstance:" + roleKey);
                }

                assignments.Add(new NetworkUxrIdentityAssignment(component,
                    ArsenalGeneratedIdentityManifest.ExpectedUniqueId(run, stationKey, logicalSlotKey, roleKey)));
            }

            foreach (ArsenalTemplateRole role in roles)
            {
                if (!bound.Contains(role.RoleKey))
                {
                    throw new InvalidOperationException("ArsenalIdentity.Bind.MissingRoleInstance:" + role.RoleKey);
                }
            }

            return assignments.AsReadOnly();
        }
    }
}

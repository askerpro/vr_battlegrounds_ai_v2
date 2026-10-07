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
    ///     Связывает роли функционального шаблона с компонентами его свежего неактивного клона и строит
    ///     назначения UniqueId из <see cref="ArsenalGeneratedIdentityManifest" />. Роль находится по исходному
    ///     UniqueId шаблона, прочитанному до пробуждения: по имени и пути объекта не ищем.
    ///     Каждый компонент с UniqueId в клоне обязан быть объявленной ролью, каждая роль — встретиться ровно
    ///     один раз; иначе отказ: необъявленный компонент получил бы на машинах разные ID.
    /// </summary>
    public static class ArsenalGeneratedIdentityBinding
    {
        public static IReadOnlyList<NetworkUxrIdentityAssignment> Bind(GameObject inactiveSlotRoot,
            ArsenalFunctionalSlotTemplate template, MapRunKey run, string stationKey, string logicalSlotKey)
        {
            if (inactiveSlotRoot == null || template == null)
            {
                throw new InvalidOperationException("ArsenalIdentity.Bind.NullInput");
            }

            if (inactiveSlotRoot.activeInHierarchy)
            {
                throw new InvalidOperationException("ArsenalIdentity.Bind.RootActive");
            }

            if (!ArsenalGeneratedIdentityManifest.TryValidateTemplateRoles(template.Roles, out string rolesError))
            {
                throw new InvalidOperationException(rolesError);
            }

            var roleBySource = new Dictionary<Guid, string>();
            foreach (ArsenalTemplateRole role in template.Roles)
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

            foreach (ArsenalTemplateRole role in template.Roles)
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

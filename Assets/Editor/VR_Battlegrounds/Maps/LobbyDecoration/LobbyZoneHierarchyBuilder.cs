using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using VrBattlegrounds.LevelDesign;
using VrBattlegrounds.Maps.Runtime;
using VrBattlegrounds.PhysicalSpaceUtils;

namespace VrBattlegrounds.Editor.Maps.LobbyDecoration
{
    /// <summary>Группирует только явно переданные части собственного декоративного префаба.</summary>
    public static class LobbyZoneHierarchyBuilder
    {
        public sealed class ZoneAssignment
        {
            public readonly string role;
            public readonly Transform[] members;
            /// <summary>Мировая точка вращения зоны; null — общий canonicalWorldPivot.</summary>
            public readonly Vector3? pivot;
            public ZoneAssignment(string role, params Transform[] members)
            { this.role = role; this.members = members; }
            public ZoneAssignment(string role, Vector3 pivot, params Transform[] members)
            { this.role = role; this.pivot = pivot; this.members = members; }
        }

        public struct ZonePose
        {
            public string role;
            public Vector3 position;
            public Quaternion rotation;
            public Vector3 scale;
        }

        /// <summary>Кадры существующих зон; явный список ссылок возвращает ведущий авторинг.</summary>
        public static ZonePose[] CaptureZonePoses(IReadOnlyDictionary<string, Transform> zones)
            => zones.Select(pair => new ZonePose
            {
                role = pair.Key, position = pair.Value.position, rotation = pair.Value.rotation,
                scale = pair.Value.lossyScale
            }).ToArray();

        /// <summary>Явная нормализация чисто декоративных зон. Связанные зоны сначала нормализуются вместе с Gameplay.</summary>
        public static void NormalizeZonePoses(IReadOnlyDictionary<string, Transform> zones, Vector3 canonicalWorldPivot)
        {
            RequireEditing();
            foreach (var pair in zones)
                if (pair.Value == null || pair.Value.parent == null || (pair.Value.parent.lossyScale - Vector3.one).sqrMagnitude > .00000001f
                    || (pair.Value.lossyScale - Vector3.one).sqrMagnitude > .00000001f)
                    throw new InvalidOperationException("Нельзя нормализовать потерянную или масштабированную зону «" + pair.Key + "».");
            using (LobbyZoneLayoutEditor.SuppressAutomaticSynchronization())
                foreach (var pair in zones)
                {
                    Undo.RecordObject(pair.Value, "Подготовить зону к пересборке");
                    pair.Value.SetPositionAndRotation(canonicalWorldPivot, Quaternion.identity);
                    pair.Value.localScale = Vector3.one;
                    PrefabUtility.RecordPrefabInstancePropertyModifications(pair.Value);
                    EditorUtility.SetDirty(pair.Value);
                }
        }

        /// <summary>После canonical генерации восстанавливает только зоны; затем сервис применяет их связь.</summary>
        public static void RestoreZonePoses(IReadOnlyDictionary<string, Transform> zones, ZonePose[] poses)
        {
            RequireEditing();
            foreach (var pose in poses)
            {
                if (!zones.TryGetValue(pose.role, out Transform zone) || zone == null)
                    throw new InvalidOperationException("После пересборки потеряна зона «" + pose.role + "».");
                if ((pose.scale - Vector3.one).sqrMagnitude > .00000001f || zone.parent == null
                    || (zone.parent.lossyScale - Vector3.one).sqrMagnitude > .00000001f)
                    throw new InvalidOperationException("Нельзя восстановить масштаб общей зоны, отличный от 1.");
            }
            using (LobbyZoneLayoutEditor.SuppressAutomaticSynchronization())
                foreach (var pose in poses)
                {
                    Transform zone = zones[pose.role];
                    Undo.RecordObject(zone, "Восстановить размещение зоны");
                    zone.SetPositionAndRotation(pose.position, pose.rotation);
                    zone.localScale = Vector3.one;
                    PrefabUtility.RecordPrefabInstancePropertyModifications(zone);
                    EditorUtility.SetDirty(zone);
                }
        }

        /// <summary>Без явно спроектированного восстановления запрещает сбрасывать передвинутые зоны генератором.</summary>
        public static void RequireCanonicalBeforeRegeneration(IReadOnlyDictionary<string, Transform> zones, Vector3 canonicalWorldPivot)
        {
            foreach (var pair in zones)
            {
                Transform zone = pair.Value;
                if (zone == null || (zone.position - canonicalWorldPivot).sqrMagnitude > .00000001f
                    || Quaternion.Angle(zone.rotation, Quaternion.identity) > .001f
                    || (zone.lossyScale - Vector3.one).sqrMagnitude > .00000001f)
                    throw new InvalidOperationException("Зона «" + pair.Key + "» размещена вручную. Сначала явно восстановите исходное размещение или используйте сохранение и восстановление поз зон при пересборке.");
            }
        }

        public static Dictionary<string, Transform> GroupByRoles(Transform decorationRoot,
            Vector3 canonicalWorldPivot, params ZoneAssignment[] assignments)
        {
            RequireEditing();
            if (decorationRoot == null || assignments == null || assignments.Length == 0)
                throw new ArgumentException("Назначьте собственный декор и состав функциональных зон.");
            if ((decorationRoot.lossyScale - Vector3.one).sqrMagnitude > .00000001f)
                throw new InvalidOperationException("Собственный корень декора должен иметь масштаб 1.");
            if (decorationRoot.Cast<Transform>().Any(child => child.name == "FunctionalZones"))
                throw new InvalidOperationException("Функциональные зоны уже собраны. Повторная группировка запрещена.");
            var roles = new HashSet<string>(StringComparer.Ordinal);
            var members = new HashSet<Transform>();
            foreach (var assignment in assignments)
            {
                if (assignment == null || string.IsNullOrWhiteSpace(assignment.role) || assignment.role.Contains("/")
                    || !roles.Add(assignment.role) || assignment.members == null || assignment.members.Length == 0)
                    throw new InvalidOperationException("Роль зоны должна быть уникальной и иметь явно назначенные части.");
                foreach (var member in assignment.members)
                {
                    if (member == null || member == decorationRoot || !member.IsChildOf(decorationRoot) || !members.Add(member))
                        throw new InvalidOperationException("Часть зоны потеряна, повторена или не принадлежит собственному декору.");
                    if (member.GetComponentsInChildren<MapRoot>(true).Length != 0
                        || member.GetComponentsInChildren<PhysicalArenaLayout>(true).Length != 0
                        || member.GetComponentsInChildren<PhysicalSpaceAnchor>(true).Length != 0
                        || member.GetComponentsInChildren<Mirror.NetworkIdentity>(true).Length != 0
                        || member.GetComponentsInChildren<Rigidbody>(true).Length != 0
                        || member.GetComponentsInChildren<VrBattlegrounds.Maps.ShootingTarget>(true).Length != 0
                        || member.GetComponentsInChildren<VrBattlegrounds.Maps.TeamSpawnZone>(true).Length != 0
                        || member.GetComponentsInChildren<VrBattlegrounds.Arsenal.ArsenalWallController>(true).Length != 0
                        || member.GetComponentsInChildren<VrBattlegrounds.Arsenal.ArsenalStationAnchor>(true).Length != 0
                        || member.GetComponentsInChildren<UltimateXR.Manipulation.UxrGrabbableObject>(true).Length != 0)
                        throw new InvalidOperationException("Нельзя группировать игровую сущность или физическую разметку как декор.");
                }
            }
            var items = members.ToArray();
            for (int i = 0; i < items.Length; i++)
                for (int j = i + 1; j < items.Length; j++)
                    if (items[i].IsChildOf(items[j]) || items[j].IsChildOf(items[i]))
                        throw new InvalidOperationException("Части разных зон не должны быть вложены друг в друга.");
            var snapshot = members.SelectMany(member => member.GetComponentsInChildren<Transform>(true))
                .Distinct().ToDictionary(item => item, item => item.localToWorldMatrix);
            var result = new Dictionary<string, Transform>(StringComparer.Ordinal);
            using (LobbyZoneLayoutEditor.SuppressAutomaticSynchronization())
            {
                var container = new GameObject("FunctionalZones").transform;
                Undo.RegisterCreatedObjectUndo(container.gameObject, "Сгруппировать функциональные зоны");
                container.SetParent(decorationRoot, false);
                foreach (var assignment in assignments)
                {
                    var zone = new GameObject(assignment.role).transform;
                    Undo.RegisterCreatedObjectUndo(zone.gameObject, "Создать функциональную зону");
                    zone.SetParent(container, false);
                    zone.SetPositionAndRotation(assignment.pivot ?? canonicalWorldPivot, Quaternion.identity);
                    zone.localScale = Vector3.one;
                    foreach (var member in assignment.members) Undo.SetTransformParent(member, zone, "Сгруппировать части зоны");
                    result.Add(assignment.role, zone);
                }
            }
            foreach (var pair in snapshot)
                for (int i = 0; i < 16; i++)
                    if (Mathf.Abs(pair.Key.localToWorldMatrix[i] - pair.Value[i]) > .0001f)
                        throw new InvalidOperationException("Группировка изменила мировую геометрию «" + pair.Key.name + "».");
            return result;
        }

        private static void RequireEditing()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Иерархия функциональных зон создаётся вне Play Mode.");
        }
    }
}

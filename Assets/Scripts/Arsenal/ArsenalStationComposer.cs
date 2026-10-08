using System;
using System.Collections.Generic;
using UltimateXR.Core.Unique;
using UltimateXR.Manipulation;
using UnityEngine;
using VrBattlegrounds.Maps.Runtime;
using VrBattlegrounds.Network;

namespace VrBattlegrounds.Arsenal
{
    /// <summary>Готовность сборки станции для барьера Relay: опрашивается повторно.</summary>
    public enum ArsenalCompositionReadiness { Pending, Passed, Failed }

    /// <summary>
    ///     Собранная станция одного запуска карты. Владеет только тем, что создала сама: неактивным контейнером
    ///     со слотами и метками поз. <see cref="Dispose" /> снимает свои цели поз и уничтожает контейнер, но всё,
    ///     что к нему прицепилось извне (предметы в слотах), сначала отцепляет — чужое через родителя не уничтожается.
    /// </summary>
    public sealed class ArsenalCompositionHandle : IDisposable
    {
        public ArsenalStationDescription Description { get; }
        public MapRunScope Scope { get; }
        public ArsenalStationCompositionBinding Station { get; }
        public GameObject Root { get; }
        public IReadOnlyList<ArsenalSlotController> Slots { get; }
        public IReadOnlyList<NetworkUxrIdentityAssignment> Assignments { get; }
        public bool IsActive { get; internal set; }
        public bool IsDisposed { get; private set; }

        private readonly HashSet<Transform> _ownedTransforms;
        private readonly List<Transform> _poseTargets;

        internal ArsenalCompositionHandle(ArsenalStationDescription description, MapRunScope scope, ArsenalStationCompositionBinding station,
            GameObject root, IReadOnlyList<ArsenalSlotController> slots, IReadOnlyList<NetworkUxrIdentityAssignment> assignments,
            List<Transform> poseTargets)
        {
            Description = description;
            Scope = scope;
            Station = station;
            Root = root;
            Slots = slots;
            Assignments = assignments;
            _poseTargets = poseTargets;
            _ownedTransforms = new HashSet<Transform>(root.GetComponentsInChildren<Transform>(true));
        }

        public void Dispose()
        {
            if (IsDisposed) return;
            IsDisposed = true;
            ArsenalStationComposer.Forget(this);

            if (Station != null && Station.EquipmentPoses != null) Station.EquipmentPoses.RemoveTargets(_poseTargets);
            if (Root == null) return;

            // Предмет, лежащий в слоте или прицепленный к поддереву извне, — не наш: отцепить до уничтожения.
            foreach (UxrGrabbableObject item in Root.GetComponentsInChildren<UxrGrabbableObject>(true))
                if (!_ownedTransforms.Contains(item.transform)) item.transform.SetParent(null, true);

            if (Application.isPlaying) UnityEngine.Object.Destroy(Root);
            else UnityEngine.Object.DestroyImmediate(Root);
        }
    }

    /// <summary>
    ///     Собирает функциональные слоты сгенерированной станции по описанию (задача 3 генератора арсенала).
    ///     Вызывает только MapBootstrap (через адаптер) — на сервере и на каждом клиенте с одним и тем же
    ///     ключом запуска, поэтому ID и порядок слотов совпадают на всех машинах.
    ///     <para>
    ///     Готовность сборки (<see cref="ValidateReady" />) — только «собрано и зарегистрировано». Право выдавать
    ///     предметы и взаимодействовать даёт допуск карты (<see cref="MapRunAdmission" />), не сборщик.
    ///     </para>
    /// </summary>
    public static class ArsenalStationComposer
    {
        private static readonly Dictionary<ArsenalStationCompositionBinding, ArsenalCompositionHandle> Live =
            new Dictionary<ArsenalStationCompositionBinding, ArsenalCompositionHandle>();

        /// <summary>
        ///     Собирает неактивную станцию: клоны шаблонов слотов в порядке манифеста, ID ролей, метки поз,
        ///     подготовка оружия и представления, установка слотов в стену. Повтор с тем же описанием и запуском
        ///     возвращает тот же handle; другое описание или запуск при живой сборке — отказ.
        /// </summary>
        public static ArsenalCompositionHandle PrepareComposition(ArsenalStationDescription description, MapRunScope scope,
            ArsenalStationCompositionBinding station)
        {
            if (description == null || scope == null || station == null)
                throw new InvalidOperationException("ArsenalComposer.NullInput");
            if (Live.TryGetValue(station, out ArsenalCompositionHandle existing) && !existing.IsDisposed)
            {
                if (ReferenceEquals(existing.Description, description) && ReferenceEquals(existing.Scope, scope)) return existing;
                throw new InvalidOperationException("ArsenalComposer.AlreadyComposed:" + station.StationKey);
            }
            if (!Application.isPlaying) throw new InvalidOperationException("ArsenalComposer.NotPlaying");
            if (scope.IsClosed) throw new InvalidOperationException("ArsenalComposer.ScopeClosed");
            if (!description.Success) throw new InvalidOperationException("ArsenalComposer.DescriptionFailed:" + description.Failures.Count);
            if (station.Mode != ArsenalCompositionMode.Generated) throw new InvalidOperationException("ArsenalComposer.NotGenerated:" + station.StationKey);
            if (description.StationKey != station.StationKey) throw new InvalidOperationException("ArsenalComposer.StationKeyMismatch:" + station.StationKey);
            ArsenalWallController wall = station.Controller;
            ArsenalStationPresetBinding presets = wall != null ? wall.GetComponent<ArsenalStationPresetBinding>() : null;
            ArsenalEquipmentPoses poses = station.EquipmentPoses;
            if (wall == null || presets == null || poses == null) throw new InvalidOperationException("ArsenalComposer.ShellIncomplete:" + station.StationKey);
            if (wall.SlotsInstalled) throw new InvalidOperationException("ArsenalComposer.SlotsAlreadyInstalled:" + station.StationKey);
            if (description.Slots.Count == 0) throw new InvalidOperationException("ArsenalComposer.NoSlots:" + station.StationKey);

            var root = new GameObject("GeneratedSlots");
            root.SetActive(false);
            root.transform.SetParent(poses.transform, false);
            try
            {
                var slots = new List<ArsenalSlotController>(description.Slots.Count);
                var assignments = new List<NetworkUxrIdentityAssignment>();
                var poseTargets = new List<ArsenalEquipmentPoses.PoseTarget>();
                for (int i = 0; i < description.Slots.Count; i++)
                {
                    ArsenalSlotManifest manifest = description.Slots[i];
                    if (manifest.Entry.NetworkIndex != i) throw new InvalidOperationException("ArsenalComposer.ManifestOrder:" + i);
                    GameObject instance = UnityEngine.Object.Instantiate(manifest.Template.Prefab, root.transform, false);
                    instance.name = "Slot_" + i + "_" + manifest.Entry.LogicalSlotKey;
                    var slot = instance.GetComponent<ArsenalSlotController>();
                    if (slot == null) throw new InvalidOperationException("ArsenalComposer.TemplateWithoutSlot:" + manifest.Template.TemplateId);
                    instance.transform.localPosition = manifest.ClosedPose.Position;
                    instance.transform.localRotation = manifest.ClosedPose.Rotation;
                    poseTargets.Add(new ArsenalEquipmentPoses.PoseTarget
                    {
                        Target = instance.transform,
                        ClosedPose = Marker(root.transform, "Closed_" + i, manifest.ClosedPose),
                        OpenPose = Marker(root.transform, "Open_" + i, manifest.OpenPose)
                    });
                    assignments.AddRange(ArsenalGeneratedIdentityBinding.Bind(instance, manifest.Template, scope.Key,
                        description.StationKey, manifest.Entry.LogicalSlotKey));
                    // Якоря оружия и магазина — в позы стиля: туда UltimateXR примагничивает возвращаемый предмет.
                    // У авторских станций это делает редакторский сборщик и сохраняет в префаб; здесь — до включения.
                    ArsenalPresentationApplicator.MaterializeFrames(slot, manifest.Entry.Presentation.Snapshot);
                    slots.Add(slot);
                }

                // Сначала всё, что можно откатить уничтожением поддерева; необратимое — после.
                NetworkUxrIdentity.PrepareGeneratedIdentities(assignments);
                presets.PrepareGenerated(description, slots);
                wall.InstallGeneratedSlots(slots);
                poses.AddTargets(poseTargets);

                var targets = new List<Transform>(poseTargets.Count);
                foreach (var target in poseTargets) targets.Add(target.Target);
                var handle = new ArsenalCompositionHandle(description, scope, station, root, slots.AsReadOnly(), assignments.AsReadOnly(), targets);
                Live[station] = handle;
                scope.Own(handle);
                return handle;
            }
            catch
            {
                UnityEngine.Object.DestroyImmediate(root); // Никогда не была активна: Awake/регистраций не было.
                throw;
            }
        }

        /// <summary>Включает собранную станцию: компоненты просыпаются и регистрируются под ID манифеста.</summary>
        public static void Activate(ArsenalCompositionHandle handle)
        {
            if (handle == null || handle.IsDisposed) throw new InvalidOperationException("ArsenalComposer.Activate.Disposed");
            if (handle.Scope.IsClosed) throw new InvalidOperationException("ArsenalComposer.Activate.ScopeClosed");
            if (handle.IsActive) return;
            handle.IsActive = true;
            handle.Root.SetActive(true);

            // Роль на выключенном внутри шаблона объекте не получит Awake и не зарегистрируется сама —
            // готовность зависла бы в Pending. Регистрируем её явно; проснувшись позже, она повторно не регистрируется.
            foreach (NetworkUxrIdentityAssignment assignment in handle.Assignments)
                if (assignment.Target != null && !assignment.Target.gameObject.activeInHierarchy)
                    assignment.Target.RegisterIfNecessary();
        }

        /// <summary>
        ///     Повторяемый опрос: Pending — ещё не включена; Passed — каждая роль зарегистрирована под своим ID
        ///     именно своим компонентом; Failed — разобрана, запуск закрыт или ID занял чужой.
        /// </summary>
        public static ArsenalCompositionReadiness ValidateReady(ArsenalCompositionHandle handle, out string reason)
        {
            reason = null;
            if (handle == null || handle.IsDisposed) { reason = "Disposed"; return ArsenalCompositionReadiness.Failed; }
            if (handle.Scope.IsClosed) { reason = "ScopeClosed"; return ArsenalCompositionReadiness.Failed; }
            if (!handle.IsActive) { reason = "NotActivated"; return ArsenalCompositionReadiness.Pending; }
            foreach (NetworkUxrIdentityAssignment assignment in handle.Assignments)
            {
                if (assignment.Target == null) { reason = "TargetDestroyed"; return ArsenalCompositionReadiness.Failed; }
                if (!UxrUniqueIdImplementer.TryGetComponentById(assignment.ExpectedUniqueId, out IUxrUniqueId owner))
                { reason = "NotRegistered:" + assignment.Target.name; return ArsenalCompositionReadiness.Pending; }
                if (!ReferenceEquals(owner, assignment.Target))
                { reason = "ForeignOwner:" + assignment.ExpectedUniqueId; return ArsenalCompositionReadiness.Failed; }
            }
            return ArsenalCompositionReadiness.Passed;
        }

        internal static void Forget(ArsenalCompositionHandle handle)
        {
            if (handle.Station != null && Live.TryGetValue(handle.Station, out ArsenalCompositionHandle current) && ReferenceEquals(current, handle))
                Live.Remove(handle.Station);
        }

        private static Transform Marker(Transform parent, string name, ArsenalPresentationPose pose)
        {
            var marker = new GameObject(name).transform;
            marker.SetParent(parent, false);
            marker.localPosition = pose.Position;
            marker.localRotation = pose.Rotation;
            return marker;
        }
    }
}

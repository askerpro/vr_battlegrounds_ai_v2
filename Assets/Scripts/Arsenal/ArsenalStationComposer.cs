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
    ///     Собранная станция одного запуска карты. Владеет только тем, что создала сама: слотами в рядах корпуса.
    ///     <see cref="Dispose" /> уничтожает свои слоты, но всё, что к ним прицепилось извне (предметы в слотах),
    ///     сначала отцепляет — чужое через родителя не уничтожается.
    /// </summary>
    public sealed class ArsenalCompositionHandle : IDisposable
    {
        public ArsenalStationDescription Description { get; }
        public MapRunScope Scope { get; }
        public ArsenalStationCompositionBinding Station { get; }
        public IReadOnlyList<ArsenalSlotController> Slots { get; }
        public IReadOnlyList<NetworkUxrIdentityAssignment> Assignments { get; }
        public bool IsActive { get; internal set; }
        public bool IsDisposed { get; private set; }

        private readonly HashSet<Transform> _ownedTransforms = new HashSet<Transform>();

        internal ArsenalCompositionHandle(ArsenalStationDescription description, MapRunScope scope, ArsenalStationCompositionBinding station,
            IReadOnlyList<ArsenalSlotController> slots, IReadOnlyList<NetworkUxrIdentityAssignment> assignments)
        {
            Description = description;
            Scope = scope;
            Station = station;
            Slots = slots;
            Assignments = assignments;
            foreach (ArsenalSlotController slot in slots)
                _ownedTransforms.UnionWith(slot.GetComponentsInChildren<Transform>(true));
        }

        public void Dispose()
        {
            if (IsDisposed) return;
            IsDisposed = true;
            ArsenalStationComposer.Forget(this);
            foreach (ArsenalSlotController slot in Slots)
            {
                if (slot == null) continue;
                // Предмет, лежащий в слоте или прицепленный к нему извне, — не наш: отцепить до уничтожения.
                foreach (UxrGrabbableObject item in slot.GetComponentsInChildren<UxrGrabbableObject>(true))
                    if (!_ownedTransforms.Contains(item.transform)) item.transform.SetParent(null, true);
                if (Application.isPlaying) UnityEngine.Object.Destroy(slot.gameObject);
                else UnityEngine.Object.DestroyImmediate(slot.gameObject);
            }
        }
    }

    /// <summary>
    ///     Собирает сгенерированную станцию по описанию: связывает исполнителей и сам геометрии не знает.
    ///     Слот по префабу ряда и содержимому собирает <see cref="ArsenalSlotBuilder" />, место слота задаёт
    ///     ряд корпуса <see cref="ArsenalSlotRow" />. Вызывает только MapBootstrap (через адаптер) — на сервере и
    ///     на каждом клиенте с одним ключом запуска, поэтому ID и порядок слотов совпадают на всех машинах.
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
        ///     Собирает выключенные слоты в порядке манифеста и вешает их в ряды корпуса; назначает ID ролей,
        ///     готовит оружие и представление, устанавливает слоты в стену. Повтор с тем же описанием и запуском
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

            if (description.StationKey != station.StationKey) throw new InvalidOperationException("ArsenalComposer.StationKeyMismatch:" + station.StationKey);
            ArsenalWallController wall = station.Controller;
            ArsenalStationPresetBinding presets = wall != null ? wall.GetComponent<ArsenalStationPresetBinding>() : null;
            if (wall == null || presets == null) throw new InvalidOperationException("ArsenalComposer.ShellIncomplete:" + station.StationKey);
            if (wall.SlotsInstalled) throw new InvalidOperationException("ArsenalComposer.SlotsAlreadyInstalled:" + station.StationKey);
            if (description.Slots.Count == 0) throw new InvalidOperationException("ArsenalComposer.NoSlots:" + station.StationKey);
            ArsenalSlotRow[] rows = station.GetComponentsInChildren<ArsenalSlotRow>(true);

            // Слоты рождаются под выключенным контейнером: ни один не проснётся до назначения ID.
            var staging = new GameObject("ArsenalSlotStaging");
            staging.SetActive(false);
            var slots = new List<ArsenalSlotController>(description.Slots.Count);
            try
            {
                var assignments = new List<NetworkUxrIdentityAssignment>();
                var presentations = new List<ArsenalPresentationSnapshot>(description.Slots.Count);
                var rowSlots = new Dictionary<ArsenalSlotRow, List<ArsenalSlotController>>();
                for (int i = 0; i < description.Slots.Count; i++)
                {
                    ArsenalSlotManifest manifest = description.Slots[i];
                    if (manifest.Entry.NetworkIndex != i) throw new InvalidOperationException("ArsenalComposer.ManifestOrder:" + i);
                    ArsenalSlotRow row = FindRow(rows, manifest.Entry.RowKey);
                    if (row == null)
                        throw new InvalidOperationException("ArsenalComposer.MissingRow:" + station.StationKey + ":" + manifest.Entry.RowKey);
                    if (row.SlotPrefab == null) throw new InvalidOperationException("ArsenalComposer.RowWithoutSlotPrefab:" + row.name);
                    // Раскладка слота — своя у оружия для вида слота этого ряда, иначе умолчание префаба слота;
                    // внешний вид — стиль арсенала.
                    ArsenalPresentationSnapshot presentation;
                    try { presentation = ArsenalPresentationResolver.Resolve(manifest.Entry.WeaponResource, row.Zone, description.Style, row.SlotPrefab.DefaultLayout); }
                    catch (InvalidOperationException ex) { throw new InvalidOperationException("ArsenalComposer.Presentation:" + manifest.Entry.LogicalSlotKey + ":" + ex.Message); }
                    ArsenalSlotController slot = ArsenalSlotBuilder.Build(row.SlotPrefab, row.Zone, presentation, staging.transform,
                        manifest.Entry, scope.Key, description.StationKey, assignments);
                    slots.Add(slot);
                    presentations.Add(presentation);
                    if (!rowSlots.TryGetValue(row, out List<ArsenalSlotController> list)) rowSlots.Add(row, list = new List<ArsenalSlotController>());
                    list.Add(slot);
                }
                // Слоты каждого ряда — в порядке манифеста; ряд сам знает, где и с каким шагом их повесить.
                foreach (KeyValuePair<ArsenalSlotRow, List<ArsenalSlotController>> pair in rowSlots) pair.Key.Arrange(pair.Value);

                // Сначала всё, что можно откатить уничтожением слотов; необратимое — после.
                NetworkUxrIdentity.PrepareGeneratedIdentities(assignments);
                presets.Prepare(description, slots, presentations);
                wall.InstallSlots(slots);

                var handle = new ArsenalCompositionHandle(description, scope, station, slots.AsReadOnly(), assignments.AsReadOnly());
                Live[station] = handle;
                scope.Own(handle);
                return handle;
            }
            catch
            {
                // Слоты никогда не были активны: Awake и регистраций не было.
                foreach (ArsenalSlotController slot in slots)
                    if (slot != null) UnityEngine.Object.DestroyImmediate(slot.gameObject);
                throw;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(staging);
            }
        }

        /// <summary>Ряд записи по ключу; два ряда с одним ключом на станции — ошибка корпуса.</summary>
        private static ArsenalSlotRow FindRow(ArsenalSlotRow[] rows, string rowKey)
        {
            ArsenalSlotRow found = null;
            foreach (ArsenalSlotRow row in rows)
            {
                if (!string.Equals(row.RowKey, rowKey, StringComparison.Ordinal)) continue;
                if (found != null) throw new InvalidOperationException("ArsenalComposer.DuplicateRowKey:" + rowKey);
                found = row;
            }
            return found;
        }

        /// <summary>Включает собранную станцию: слоты просыпаются и регистрируются под ID манифеста.</summary>
        public static void Activate(ArsenalCompositionHandle handle)
        {
            if (handle == null || handle.IsDisposed) throw new InvalidOperationException("ArsenalComposer.Activate.Disposed");
            if (handle.Scope.IsClosed) throw new InvalidOperationException("ArsenalComposer.Activate.ScopeClosed");
            if (handle.IsActive) return;
            handle.IsActive = true;
            foreach (ArsenalSlotController slot in handle.Slots) slot.gameObject.SetActive(true);

            // Роль на выключенном объекте (внутри шаблона или в выключенном ряду) не получит Awake и не
            // зарегистрируется сама — готовность зависла бы в Pending. Регистрируем её явно; проснувшись позже,
            // она повторно не регистрируется.
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
    }
}

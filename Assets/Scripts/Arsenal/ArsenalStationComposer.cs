using System;
using System.Collections.Generic;
using UltimateXR.Core.Unique;
using UltimateXR.Manipulation;
using UnityEngine;
using UnityEngine.SceneManagement;
using VrBattlegrounds.Maps.Runtime;
using VrBattlegrounds.Network;

namespace VrBattlegrounds.Arsenal
{
    /// <summary>Готовность сборки станции для барьера Relay: опрашивается повторно.</summary>
    public enum ArsenalCompositionReadiness { Pending, Passed, Failed }

    /// <summary>
    ///     Собранная станция одного запуска карты. Владеет только тем, что создала сама: слотами в рядах корпуса.
    ///     <see cref="Dispose" /> уничтожает свои слоты. Чужие предметы в слотах (оружие, магазины) зависят от того,
    ///     живёт ли сцена дальше:
    ///     <list type="bullet">
    ///     <item>сцена живёт (отказ запуска, остановка сервера, отказ клиента) — предметы верхнего уровня отцепляются
    ///     в корень своей сцены, вложенные в них (стартовый магазин, затвор) едут с родителем; через родителя
    ///     чужое не уничтожается;</item>
    ///     <item>сцена уходит (владелец сцены объявил <see cref="ArsenalStationComposer.ReleaseWithScene" />) —
    ///     ничего не переподвешивается: предметы уничтожаются вместе со сценой, сетевые снимаются Mirror
    ///     (<c>NetworkIdentity.OnDestroy</c> → <c>NetworkServer.Destroy</c>). Новый корень, рождённый во время
    ///     выгрузки, пережил бы её вне сцены.</item>
    ///     </list>
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

        /// <summary>
        ///     Разборка пойдёт вместе с уничтожением сцены станции: чужие предметы не отцеплять. Ставит только
        ///     <see cref="ArsenalStationComposer.ReleaseWithScene" /> по объявлению владельца сцены.
        /// </summary>
        public bool ReleasesWithScene { get; private set; }

        /// <summary>Сцена станции на момент сборки: по ней владелец сцены находит её станции, даже уже уничтоженные.</summary>
        internal Scene StationScene { get; }

        private readonly HashSet<Transform> _ownedTransforms = new HashSet<Transform>();

        internal ArsenalCompositionHandle(ArsenalStationDescription description, MapRunScope scope, ArsenalStationCompositionBinding station,
            IReadOnlyList<ArsenalSlotController> slots, IReadOnlyList<NetworkUxrIdentityAssignment> assignments)
        {
            Description = description;
            Scope = scope;
            Station = station;
            Slots = slots;
            Assignments = assignments;
            StationScene = station != null ? station.gameObject.scene : default;
            foreach (ArsenalSlotController slot in slots)
                _ownedTransforms.UnionWith(slot.GetComponentsInChildren<Transform>(true));
        }

        internal void MarkReleasesWithScene()
        {
            if (!IsDisposed) ReleasesWithScene = true;
        }

        public void Dispose()
        {
            if (IsDisposed) return;
            IsDisposed = true;
            ArsenalStationComposer.Forget(this);
            foreach (ArsenalSlotController slot in Slots)
            {
                if (slot == null) continue;
                if (!ReleasesWithScene) DetachForeignItems(slot.transform);
                if (Application.isPlaying) UnityEngine.Object.Destroy(slot.gameObject);
                else UnityEngine.Object.DestroyImmediate(slot.gameObject);
            }
        }

        /// <summary>
        ///     Сцена живёт: чужой предмет верхнего уровня (между ним и слотом нет чужого хватаемого предка) отцепить в
        ///     корень до уничтожения слота. Вложенный в чужой предмет (стартовый магазин в оружии, затвор) остаётся в
        ///     родителе: вырванный из оружия, он стал бы отдельным предметом без родителя.
        /// </summary>
        private void DetachForeignItems(Transform slot)
        {
            UxrGrabbableObject[] items = slot.GetComponentsInChildren<UxrGrabbableObject>(true);
            // Сначала решить по нетронутой иерархии, потом отцеплять: отцепление меняет предков остальных.
            var detach = new List<Transform>(items.Length);
            foreach (UxrGrabbableObject item in items)
                if (!_ownedTransforms.Contains(item.transform) && !HasForeignGrabbableAncestor(item.transform, slot))
                    detach.Add(item.transform);
            foreach (Transform item in detach) item.SetParent(null, true);
        }

        /// <summary>
        ///     Есть ли между предметом и слотом чужой хватаемый предмет — тот, что отцепится сам и увезёт вложенное.
        ///     Свой хватаемый предок уничтожается со слотом и вложенное не спасает.
        /// </summary>
        private bool HasForeignGrabbableAncestor(Transform item, Transform slot)
        {
            for (Transform parent = item.parent; parent != null && parent != slot; parent = parent.parent)
                if (!_ownedTransforms.Contains(parent) && parent.GetComponent<UxrGrabbableObject>() != null) return true;
            return false;
        }
    }

    /// <summary>Место одного слота в плане станции: запись манифеста, ряд корпуса, раскладка слота и поза в ряду.</summary>
    public sealed class ArsenalSlotPlacement
    {
        public ArsenalSlotManifest Manifest { get; }
        /// <summary>Ряд корпуса; его префаб слота (<see cref="ArsenalSlotRow.SlotPrefab" />) и вид слота монтируются здесь.</summary>
        public ArsenalSlotRow Row { get; }
        /// <summary>Раскладка и внешний вид слота: позы оружия, магазина, карточки, опоры, коробка приёма.</summary>
        public ArsenalPresentationSnapshot Presentation { get; }
        /// <summary>Поза слота в локальных координатах ряда.</summary>
        public Pose LocalPose { get; internal set; }

        internal ArsenalSlotPlacement(ArsenalSlotManifest manifest, ArsenalSlotRow row, ArsenalPresentationSnapshot presentation)
        {
            Manifest = manifest;
            Row = row;
            Presentation = presentation;
        }
    }

    /// <summary>
    ///     План станции (<see cref="ArsenalStationComposer.PlanComposition" />): слоты в порядке манифеста. Только данные:
    ///     объектов не создаёт и сцену не меняет.
    /// </summary>
    public sealed class ArsenalStationPlan
    {
        public ArsenalStationDescription Description { get; }
        public ArsenalStationCompositionBinding Station { get; }
        public IReadOnlyList<ArsenalSlotPlacement> Slots { get; }

        internal ArsenalStationPlan(ArsenalStationDescription description, ArsenalStationCompositionBinding station,
            IReadOnlyList<ArsenalSlotPlacement> slots)
        {
            Description = description;
            Station = station;
            Slots = slots;
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
        ///     Собирает выключенные слоты в порядке манифеста по плану <see cref="PlanComposition" /> и вешает их в ряды
        ///     корпуса; назначает ID ролей,
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
            ArsenalStationPlan plan = PlanComposition(description, station);

            // Слоты рождаются под выключенным контейнером: ни один не проснётся до назначения ID.
            var staging = new GameObject("ArsenalSlotStaging");
            staging.SetActive(false);
            var slots = new List<ArsenalSlotController>(plan.Slots.Count);
            try
            {
                var assignments = new List<NetworkUxrIdentityAssignment>();
                var presentations = new List<ArsenalPresentationSnapshot>(plan.Slots.Count);
                foreach (ArsenalSlotPlacement placement in plan.Slots)
                {
                    ArsenalSlotController slot = ArsenalSlotBuilder.Build(placement.Row.SlotPrefab, placement.Row.Zone, placement.Presentation,
                        staging.transform, placement.Manifest.Entry, scope.Key, description.StationKey, assignments);
                    slots.Add(slot);
                    presentations.Add(placement.Presentation);
                }
                // Слоты — в позы плана: ряд рассчитал их по ширинам слотов в порядке манифеста.
                for (int i = 0; i < slots.Count; i++) plan.Slots[i].Row.Mount(slots[i].transform, plan.Slots[i].LocalPose);

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

        /// <summary>
        ///     Расчёт размещения станции без создания объектов: для каждой записи манифеста — ряд корпуса, раскладка
        ///     слота (своя у оружия для вида слота ряда, иначе умолчание префаба слота) и поза слота в ряду. Сцену и
        ///     ассеты не меняет, Play Mode и сеть не нужны. Сборка (<see cref="PrepareComposition" />) создаёт слоты строго
        ///     по этому плану; редакторское превью станций рисует по нему визуальные копии.
        /// </summary>
        public static ArsenalStationPlan PlanComposition(ArsenalStationDescription description, ArsenalStationCompositionBinding station)
        {
            if (description == null || station == null) throw new InvalidOperationException("ArsenalComposer.NullInput");
            if (!description.Success) throw new InvalidOperationException("ArsenalComposer.DescriptionFailed:" + description.Failures.Count);
            if (description.StationKey != station.StationKey) throw new InvalidOperationException("ArsenalComposer.StationKeyMismatch:" + station.StationKey);
            if (description.Slots.Count == 0) throw new InvalidOperationException("ArsenalComposer.NoSlots:" + station.StationKey);
            ArsenalSlotRow[] rows = station.GetComponentsInChildren<ArsenalSlotRow>(true);

            var placements = new List<ArsenalSlotPlacement>(description.Slots.Count);
            var rowSlots = new Dictionary<ArsenalSlotRow, List<int>>();
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
                placements.Add(new ArsenalSlotPlacement(manifest, row, presentation));
                if (!rowSlots.TryGetValue(row, out List<int> list)) rowSlots.Add(row, list = new List<int>());
                list.Add(i);
            }
            // Слоты каждого ряда — в порядке манифеста; ряд сам знает, где и с каким шагом их повесить.
            foreach (KeyValuePair<ArsenalSlotRow, List<int>> pair in rowSlots)
            {
                var widths = new float[pair.Value.Count];
                for (int i = 0; i < widths.Length; i++) widths[i] = pair.Key.SlotPrefab.SlotWidth;
                Pose[] poses = pair.Key.SlotLocalPoses(widths);
                for (int i = 0; i < poses.Length; i++) placements[pair.Value[i]].LocalPose = poses[i];
            }
            return new ArsenalStationPlan(description, station, placements.AsReadOnly());
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

        /// <summary>
        ///     Владелец сцены карты (<c>MapBootstrap.OnDestroy</c>) объявляет: сцена уничтожается, её станции будут
        ///     разобраны вместе с ней. Вызывать до разборки scope запуска. Живые сборки этой сцены при
        ///     <see cref="ArsenalCompositionHandle.Dispose" /> не переподвешивают предметы в корень — те уходят со сценой.
        /// </summary>
        internal static void ReleaseWithScene(Scene scene)
        {
            foreach (ArsenalCompositionHandle handle in Live.Values)
                if (handle.StationScene == scene) handle.MarkReleasesWithScene();
        }

        internal static void Forget(ArsenalCompositionHandle handle)
        {
            // Ссылка, а не Unity-null: при выгрузке станция бывает уничтожена раньше разборки, запись всё равно снять.
            if (!ReferenceEquals(handle.Station, null) && Live.TryGetValue(handle.Station, out ArsenalCompositionHandle current) && ReferenceEquals(current, handle))
                Live.Remove(handle.Station);
        }
    }
}

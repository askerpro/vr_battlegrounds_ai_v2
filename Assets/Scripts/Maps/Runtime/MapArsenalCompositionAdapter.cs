using System;
using System.Collections.Generic;
using UnityEngine;
using VrBattlegrounds.Arsenal;

namespace VrBattlegrounds.Maps.Runtime
{
    /// <summary>
    /// Шов к <see cref="ArsenalStationComposer"/>. Настоящий сборщик требует Play Mode; EditMode-тесты MapBootstrap
    /// подставляют свой, чтобы проверить ожидание, отказ и готовность без сцены.
    /// </summary>
    internal interface IArsenalStationComposer
    {
        ArsenalCompositionHandle Prepare(ArsenalStationDescription description, MapRunScope scope, ArsenalStationCompositionBinding station);
        void Activate(ArsenalCompositionHandle handle);
        ArsenalCompositionReadiness ValidateReady(ArsenalCompositionHandle handle, out string reason);
    }

    /// <summary>Сборщик игры: прямые вызовы <see cref="ArsenalStationComposer"/>.</summary>
    internal sealed class RuntimeArsenalStationComposer : IArsenalStationComposer
    {
        public static readonly IArsenalStationComposer Instance = new RuntimeArsenalStationComposer();

        private RuntimeArsenalStationComposer() { }

        public ArsenalCompositionHandle Prepare(ArsenalStationDescription description, MapRunScope scope, ArsenalStationCompositionBinding station) =>
            ArsenalStationComposer.PrepareComposition(description, scope, station);

        public void Activate(ArsenalCompositionHandle handle) => ArsenalStationComposer.Activate(handle);

        public ArsenalCompositionReadiness ValidateReady(ArsenalCompositionHandle handle, out string reason) =>
            ArsenalStationComposer.ValidateReady(handle, out reason);
    }

    /// <summary>
    /// Адаптер генерируемых станций арсенала одного запуска карты (задача 7 плана map-runtime-bootstrap).
    /// Вызывает его только <see cref="MapBootstrap"/> — на сервере и на каждом клиенте одинаково, с одним
    /// <see cref="MapRunKey"/>; host вторую сборку не делает.
    ///
    /// <para>
    /// Порядок: <see cref="Describe"/> строит описание каждой станции в режиме Generated из тех же входов, что у всех
    /// машин (паспорт карты, каталог ресурсов, поза станции в сцене). Сервер записывает выбор оформления, fallback,
    /// layout hash и версию схемы ID в <see cref="MapStationConfig"/> до публикации config (<see cref="Apply"/>);
    /// клиент сверяет своё описание с опубликованным (<see cref="Verify"/>) и при расхождении не собирает станцию.
    /// <see cref="Compose"/> собирает и включает станции в scope запуска; <see cref="Poll"/> — сводная готовность
    /// для барьера Relay и server Ready: все Passed — готово, любой Failed — отказ, иначе ждать.
    /// </para>
    ///
    /// <para>
    /// Геометрию, ID и выбор оформления адаптер не вычисляет — это владельцы генератора. Разборку ведёт scope запуска
    /// (handle принадлежит ему с момента сборки), своего teardown у адаптера нет. Авторские станции сюда не попадают.
    /// </para>
    /// </summary>
    internal sealed class MapArsenalCompositionAdapter
    {
        /// <summary>Значение <see cref="MapStationConfig.DecorationFallback"/> авторской станции (пишет <see cref="MapRoot"/>).</summary>
        internal const string AuthoredFallback = "Authored";

        private sealed class Station
        {
            public ArsenalStationCompositionBinding Binding;
            public ArsenalStationDescription Description;
            public ArsenalCompositionHandle Handle;
        }

        private readonly List<Station> _stations;
        private readonly IArsenalStationComposer _composer;

        /// <summary>Сгенерированные станции есть (иначе адаптер ничего не собирает и готов сразу).</summary>
        public bool HasStations => _stations.Count > 0;

        public int StationCount => _stations.Count;

        /// <summary>Результат последнего <see cref="Poll"/>. Без станций — Passed.</summary>
        public ArsenalCompositionReadiness Readiness { get; private set; }

        /// <summary>Причина ожидания или отказа последнего <see cref="Poll"/> со StationKey; пусто при Passed.</summary>
        public string Reason { get; private set; } = string.Empty;

        private MapArsenalCompositionAdapter(List<Station> stations, IArsenalStationComposer composer)
        {
            _stations = stations;
            _composer = composer ?? RuntimeArsenalStationComposer.Instance;
            Readiness = stations.Count == 0 ? ArsenalCompositionReadiness.Passed : ArsenalCompositionReadiness.Pending;
        }

        /// <summary>
        /// Описания станций в режиме Generated. Отказ описания (нестилизованный пресет, нет каталога, переполнение
        /// места) — именованная ошибка в <paramref name="errors"/>: <c>Station.Generated.&lt;вид&gt;:&lt;StationKey&gt;</c>.
        /// </summary>
        public static MapArsenalCompositionAdapter Describe(IEnumerable<ArsenalStationCompositionBinding> stations, ArsenalPreset preset,
            ArsenalCompositionCatalog catalog, IArsenalStationComposer composer, List<string> errors)
        {
            if (errors == null) throw new ArgumentNullException(nameof(errors));
            var described = new List<Station>();
            foreach (ArsenalStationCompositionBinding station in stations ?? Array.Empty<ArsenalStationCompositionBinding>())
            {
                if (station == null || station.Mode != ArsenalCompositionMode.Generated) continue;
                string key = station.StationKey;
                if (catalog == null)
                {
                    errors.Add("Station.Generated.CatalogMissing:" + key);
                    continue;
                }

                ArsenalStationDescription description = ArsenalStationResolver.ResolveDescription(
                    ArsenalStationBuildInput.Capture(key, preset, catalog, VisualRequest(station), Placement(station)));
                if (!description.Success)
                {
                    foreach (ArsenalCompositionFailure failure in description.Failures)
                        errors.Add($"Station.Generated.{failure.Kind}:{key}" +
                                   (string.IsNullOrEmpty(failure.LogicalSlotKey) ? "" : "/" + failure.LogicalSlotKey) +
                                   (string.IsNullOrEmpty(failure.Detail) ? "" : " (" + failure.Detail + ")"));
                    continue;
                }

                described.Add(new Station { Binding = station, Description = description });
            }

            return new MapArsenalCompositionAdapter(described, composer);
        }

        /// <summary>
        /// Место станции — мировая поза корня поз оборудования (под ним сборщик ставит слоты) и постоянные границы
        /// поднятого корпуса как доступный конверт. Одна сцена — одни значения на всех машинах.
        /// </summary>
        internal static ArsenalPlacementInput Placement(ArsenalStationCompositionBinding station)
        {
            Transform frame = station.EquipmentPoses != null ? station.EquipmentPoses.transform : station.transform;
            ArsenalStationAnchor anchor = station.StationAnchor;
            Bounds envelope = anchor != null ? anchor.RaisedBoundsWorld : default;
            Transform zone = anchor != null && anchor.Zone != null ? anchor.Zone.transform : null;
            return new ArsenalPlacementInput(new ArsenalPresentationPose(frame.position, frame.rotation), frame.lossyScale, envelope,
                zone, anchor != null ? anchor.StandingPoint : null, anchor != null ? anchor.ArenaFacing : null);
        }

        /// <summary>
        /// Запрос оформления — авторский набор и декор станции (<see cref="ArsenalStationCompositionBinding.VisualRequest"/>);
        /// пустой набор даёт станцию без корпуса (Bare), одинаково на всех машинах.
        /// </summary>
        internal static ArsenalVisualRequest VisualRequest(ArsenalStationCompositionBinding station) => station.VisualRequest;

        /// <summary>Запись config сгенерированной станции: выбор оформления, fallback, layout hash, версия схемы ID.</summary>
        internal static MapStationConfig ConfigFor(ArsenalStationDescription description) =>
            new MapStationConfig(description.StationKey, description.Selection.DecorationId, description.Selection.Kind.ToString(),
                description.LayoutFingerprint, (uint)description.IdentitySchemaVersion);

        /// <summary>Сервер: заменить записи сгенерированных станций описаниями до разрешения config.</summary>
        public MapRunBindings Apply(MapRunBindings source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            var described = new Dictionary<string, MapStationConfig>(StringComparer.Ordinal);
            foreach (Station station in _stations) described[station.Description.StationKey] = ConfigFor(station.Description);
            var result = new List<MapStationConfig>(source.Stations.Count);
            foreach (MapStationConfig config in source.Stations)
                result.Add(described.TryGetValue(config.StationKey ?? "", out MapStationConfig generated) ? generated : config);
            return new MapRunBindings(source.MapScene, result);
        }

        /// <summary>
        /// Клиент: опубликованный сервером config описывает те же станции. Режим (Authored/Generated) каждой станции
        /// сцены совпадает; у сгенерированной совпадают оформление, fallback, layout hash и версия схемы ID.
        /// Расхождение — разные сборки или входы; станцию не собирать и не подменять своим выбором.
        /// </summary>
        public bool Verify(MapRunConfig config, IEnumerable<ArsenalStationCompositionBinding> sceneStations, List<string> errors)
        {
            if (errors == null) throw new ArgumentNullException(nameof(errors));
            int before = errors.Count;
            var published = new Dictionary<string, MapStationConfig>(StringComparer.Ordinal);
            if (config != null)
                foreach (MapStationConfig station in config.Stations)
                    if (station.StationKey != null) published[station.StationKey] = station;

            foreach (ArsenalStationCompositionBinding binding in sceneStations ?? Array.Empty<ArsenalStationCompositionBinding>())
            {
                if (binding == null || binding.Mode != ArsenalCompositionMode.Authored) continue;
                if (published.TryGetValue(binding.StationKey ?? "", out MapStationConfig server) && server.DecorationFallback != AuthoredFallback)
                    errors.Add("Station.Generated.ModeMismatch:" + binding.StationKey);
            }

            foreach (Station station in _stations)
            {
                string key = station.Description.StationKey;
                if (!published.TryGetValue(key, out MapStationConfig server))
                {
                    errors.Add("Station.Generated.NotPublished:" + key);
                    continue;
                }
                MapStationConfig local = ConfigFor(station.Description);
                if (server.DecorationFallback == AuthoredFallback) errors.Add("Station.Generated.ModeMismatch:" + key);
                else if (server.IdentitySchemaVersion != local.IdentitySchemaVersion) errors.Add("Station.Generated.IdentitySchemaMismatch:" + key);
                else if (server.DecorationId != local.DecorationId || server.DecorationFallback != local.DecorationFallback)
                    errors.Add($"Station.Generated.DecorationMismatch:{key} (сервер {server.DecorationFallback}/{server.DecorationId}, " +
                               $"клиент {local.DecorationFallback}/{local.DecorationId})");
                else if (server.LayoutFingerprint != local.LayoutFingerprint) errors.Add("Station.Generated.LayoutMismatch:" + key);
            }

            return errors.Count == before;
        }

        /// <summary>
        /// Собрать и включить все станции в scope запуска. Сбой сборщика — исключение с его именованной причиной
        /// («ArsenalComposer.*», «NetworkUxrIdentity.Generated.*»); уже собранные станции разберёт scope.
        /// </summary>
        public void Compose(MapRunScope scope)
        {
            if (scope == null) throw new ArgumentNullException(nameof(scope));
            foreach (Station station in _stations)
                station.Handle = _composer.Prepare(station.Description, scope, station.Binding);
            foreach (Station station in _stations)
                _composer.Activate(station.Handle);
        }

        /// <summary>
        /// Сводная готовность: любой Failed — Failed (первая причина), иначе любой Pending — Pending, иначе Passed.
        /// Несобранная станция — Pending. Опрос повторяемый.
        /// </summary>
        public ArsenalCompositionReadiness Poll()
        {
            ArsenalCompositionReadiness result = ArsenalCompositionReadiness.Passed;
            string reason = string.Empty;
            foreach (Station station in _stations)
            {
                ArsenalCompositionReadiness readiness;
                string detail;
                if (station.Handle == null)
                {
                    readiness = ArsenalCompositionReadiness.Pending;
                    detail = "NotComposed";
                }
                else readiness = _composer.ValidateReady(station.Handle, out detail);

                if (readiness == ArsenalCompositionReadiness.Failed)
                {
                    result = ArsenalCompositionReadiness.Failed;
                    reason = station.Description.StationKey + ": " + detail;
                    break;
                }
                if (readiness == ArsenalCompositionReadiness.Pending && result == ArsenalCompositionReadiness.Passed)
                {
                    result = ArsenalCompositionReadiness.Pending;
                    reason = station.Description.StationKey + ": " + detail;
                }
            }

            Readiness = result;
            Reason = reason;
            return result;
        }
    }
}

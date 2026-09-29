using System.Collections.Generic;
using Mirror;
using UnityEngine;
using VrBattlegrounds.Core;
using VrBattlegrounds.Managers;
using VrBattlegrounds.PhysicalSpaceUtils;

namespace VrBattlegrounds.Player.Avatars
{
    /// <summary>
    /// Серверная память о том, где стоял игрок, — чтобы смена карты его не двигала.
    ///
    /// <para>
    /// <b>Телепортов нет ни для кого.</b> Игроки ходят по физической арене ногами, и любой
    /// перенос аватара рвёт связь картинки с телом. Поэтому при смене карты место сохраняет
    /// каждый игрок с аватаром:
    /// </para>
    /// <list type="bullet">
    /// <item><b>Откалиброванный</b> — в координатах якорей арены (<b>CAL-01</b>, задача T-30):
    ///       место задано физически и переносится вместе с ареной.</item>
    /// <item><b>Неоткалиброванный</b> — в мировых координатах, как есть. Связи с ареной у него
    ///       ещё нет, и лучшее, что может игра, — не двигать его. Арены всех карт стоят
    ///       одинаково (<c>MapAlignmentTests</c>), так что мировая точка на новой карте — то же
    ///       место в комнате.</item>
    /// </list>
    ///
    /// <para>
    /// <b>Кто вычисляет точку и почему так.</b> Физическое положение игрока в комнате
    /// знает только его машина — но результат этого знания у сервера уже есть: после
    /// калибровки мировая поза аватара <i>и есть</i> физическое положение, перенесённое
    /// в арену, и она приезжает серверу каждым пакетом <c>NetworkTransform</c>. Поэтому
    /// клиент присылает ровно один бит — <c>PlayerSession.IsCalibrated</c>, — а точку
    /// считает сервер.
    /// </para>
    ///
    /// <para>
    /// Альтернативу — «сервер ставит куда угодно, клиент поправляет себя сам» —
    /// отвергли по двум причинам, и вторая тяжелее первой. Первая: между спавном
    /// и поправкой игрок видит рывок. Вторая: всё это время <b>сервер считает игрока
    /// не там, где он есть</b>, а именно сервер решает, кто в зоне спавна
    /// (<c>TeamSpawnZone</c> → <c>PlayerSession.IsInSpawnZone</c> → готовность к раунду)
    /// и кто куда попал. Трансформы аватаров клиент-авторитетные
    /// (<c>syncDirection = ClientToServer</c>), так что поправка клиента в итоге победит, —
    /// но «в итоге» здесь означает «после того, как сервер уже принял решения».
    /// </para>
    ///
    /// <para>
    /// <b>Почему поза откалиброванного — в системе координат якорей.</b> Якоря отмечают одни
    /// и те же физические метки в комнате, поэтому поза относительно них у карт общая, даже
    /// если арену на новой карте поставят иначе. Сейчас арены всех карт выровнены и мировая
    /// поза дала бы то же, но якоря — страховка на карту, собранную по-другому. Пересчёт —
    /// <see cref="PhysicalSpaceAnchorFrame" />.
    /// </para>
    ///
    /// <para>
    /// <b>Жизненный цикл.</b> Мест два источника, и оба кладут одно и то же — позу
    /// в координатах якорей:
    /// </para>
    /// <list type="number">
    /// <item><b>Смена карты.</b> <see cref="CaptureAll" /> по <c>MapLoader.MapLoadStarted</c>,
    ///       то есть пока старая сцена ещё жива и её якоря на месте. Расходуется при
    ///       пересоздании аватаров на новой карте (<c>GameNetworkManager.OnServerReady</c>
    ///       → <c>AvatarManager.ChangeAvatar</c>). Каждый новый снимок затирает предыдущий
    ///       целиком, поэтому отключившиеся сессии не копятся.</item>
    /// <item><b>Подключение.</b> <see cref="Remember" /> из
    ///       <c>PlayersManager.HandlePlayerConnect</c>: игрок приносит своё место с собой
    ///       в <c>GamePlayerConnectMessage</c>. Снять его серверу самому не по чему —
    ///       клиент был на другой карте, и её якорей здесь нет (находка <b>CAL-02</b>).
    ///       Расходуется при первом спавне (<c>AvatarManager.SpawnAvatar</c>).</item>
    /// </list>
    /// </summary>
    public static class SpawnPlaceRegistry
    {
        /// <summary>
        /// Поза игрока: откалиброванного — в системе координат якорей той карты, где он стоял,
        /// неоткалиброванного — в мировых координатах (<see cref="InWorld"/>).
        /// </summary>
        private readonly struct Placement
        {
            public readonly Vector3 Position;
            public readonly Quaternion Rotation;

            /// <summary>Поза в мировых координатах, а не относительно якорей.</summary>
            public readonly bool InWorld;

            /// <summary>Имя карты, с которой снят снимок. Нужно только для строки в логе.</summary>
            public readonly string CapturedOnMap;

            public Placement(Vector3 position, Quaternion rotation, bool inWorld, string capturedOnMap)
            {
                Position = position;
                Rotation = rotation;
                InWorld = inWorld;
                CapturedOnMap = capturedOnMap;
            }
        }

        private static readonly Dictionary<uint, Placement> Placements = new Dictionary<uint, Placement>();

        /// <summary>Сколько откалиброванных игроков сейчас в памяти. Для тестов и диагностики.</summary>
        public static int Count => Placements.Count;

        // ── Подписка на смену карты ───────────────────────────────────────────

        /// <summary>
        /// Реестр цепляется к <c>MapLoader.MapLoadStarted</c> сам, а не компонентом
        /// на сцене: сцены и префабы в проекте — общий ресурс, и заводить ради одного
        /// подписчика ещё один объект на <c>PersistentRoot</c> дороже, чем подписаться
        /// из кода. Тем же приёмом поднимается харнесс e2e.
        ///
        /// Память чистится здесь же: статика переживает выход из Play Mode в редакторе,
        /// и снимок прошлого запуска дожил бы до следующего.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Install()
        {
            Placements.Clear();

            MapLoader.MapLoadStarted -= OnMapLoadStarted;
            MapLoader.MapLoadStarted += OnMapLoadStarted;
        }

        private static void OnMapLoadStarted(string sceneName)
        {
            CaptureAll($"смена карты на '{sceneName}'");
        }

        // ── Снимок ────────────────────────────────────────────────────────────

        /// <summary>
        /// Запоминает позы всех игроков с аватаром на <b>текущей</b> сцене. Зовётся до
        /// <c>ServerChangeScene</c>: после него ни якорей, ни аватаров уже нет.
        /// </summary>
        /// <param name="reason">Что вызвало снимок — уходит в лог как есть.</param>
        public static void CaptureAll(string reason)
        {
            Placements.Clear();

            if (!NetworkServer.active) return;
            if (PlayersManager.Instance == null) return;

            foreach (PlayerSession session in PlayersManager.Instance.Sessions)
            {
                if (session == null) continue;

                if (session.ActiveAvatar == null)
                {
                    GameLog.PhysicalSpace.Verbose(
                        $"[SpawnPlaceRegistry] {session.PlayerName}: аватара сейчас нет — запоминать нечего ({reason}).");
                    continue;
                }

                Capture(session, session.ActiveAvatar.transform, reason);
            }
        }

        /// <summary>
        /// Запоминает позу одного игрока: откалиброванного — относительно якорей текущей
        /// сцены, неоткалиброванного — в мировых координатах. Отдельно от
        /// <see cref="CaptureAll"/>, чтобы выбор ветки проверялся тестом без менеджера сессий.
        /// </summary>
        /// <returns>true — поза запомнена.</returns>
        public static bool Capture(PlayerSession session, Transform avatar, string reason)
        {
            if (session == null || avatar == null) return false;

            string map = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;

            if (!session.IsCalibrated)
            {
                Placements[session.netId] = new Placement(avatar.position, avatar.rotation, inWorld: true, map);
                GameLog.PhysicalSpace.Info(
                    $"[SpawnPlaceRegistry] {session.PlayerName} не откалиброван: место запомнено в мировых координатах " +
                    $"{avatar.position} на карте '{map}' ({reason}).");
                return true;
            }

            PhysicalSpaceAnchorFrame frame;
            string diagnosis;
            if (!PhysicalSpaceAnchorFrame.TryBuildFromScene(out frame, out diagnosis))
            {
                GameLog.PhysicalSpace.Warning(
                    $"[SpawnPlaceRegistry] Место откалиброванного {session.PlayerName} не запомнено ({reason}): {diagnosis}. " +
                    "Без якорей позу не к чему привязать — после смены карты он окажется в зоне своей команды.");
                return false;
            }

            Placement placement = new Placement(frame.ToLocal(avatar.position), frame.ToLocal(avatar.rotation),
                                                inWorld: false, map);
            Placements[session.netId] = placement;

            GameLog.PhysicalSpace.Info(
                $"[SpawnPlaceRegistry] {session.PlayerName}: место запомнено ({reason}). " +
                $"Мир {avatar.position} на карте '{map}', относительно якорей " +
                $"{placement.Position}. Якоря: {frame}");
            return true;
        }

        // ── Выдача ────────────────────────────────────────────────────────────

        /// <summary>
        /// Отвечает, где обязан появиться игрок на <b>текущей</b> карте: там, где стоял.
        /// </summary>
        /// <param name="session">Сессия игрока. <c>null</c> и неоткалиброванные отсеиваются здесь же.</param>
        /// <param name="point">Готовая точка спавна. Осмысленна только при <c>true</c>.</param>
        /// <param name="diagnosis">Почему точки нет. Пусто при успехе.</param>
        public static bool TryResolve(PlayerSession session, out AvatarSpawnPoint point, out string diagnosis)
        {
            point = default(AvatarSpawnPoint);

            if (session == null)
            {
                diagnosis = "сессия не передана";
                return false;
            }

            Placement placement;
            if (!Placements.TryGetValue(session.netId, out placement))
            {
                diagnosis = $"снимка места игрока нет — первый спавн (запомнено мест: {Placements.Count})";
                return false;
            }

            // Неоткалиброванный остаётся в мировых координатах: связи с ареной у него нет,
            // и игра его не двигает.
            if (placement.InWorld)
            {
                point = new AvatarSpawnPoint(placement.Position, placement.Rotation,
                                             AvatarSpawnPointSource.PreviousWorldPlace, placement.CapturedOnMap);
                diagnosis = string.Empty;
                return true;
            }

            // Место относительно якорей имеет смысл только у откалиброванного: у остальных
            // (место, принесённое неоткалиброванным клиентом при подключении) связи с ареной нет.
            if (!session.IsCalibrated)
            {
                diagnosis = "игрок не калибровался — место относительно якорей к нему не применимо";
                return false;
            }

            PhysicalSpaceAnchorFrame frame;
            string frameDiagnosis;
            if (!PhysicalSpaceAnchorFrame.TryBuildFromScene(out frame, out frameDiagnosis))
            {
                diagnosis = $"на новой карте не к чему привязать место: {frameDiagnosis}";
                return false;
            }

            point = new AvatarSpawnPoint(
                frame.ToWorld(placement.Position),
                frame.ToWorld(placement.Rotation),
                AvatarSpawnPointSource.CalibratedPlace,
                placement.CapturedOnMap);

            diagnosis = string.Empty;
            return true;
        }

        /// <summary>Забыть всё. Нужно тестам и переходу в оффлайн.</summary>
        public static void Clear()
        {
            Placements.Clear();
        }

        /// <summary>
        /// Кладёт готовую позу напрямую, минуя сцену и <c>PlayersManager</c>.
        ///
        /// <para>
        /// Игровых вызывающих один: <c>PlayersManager.HandlePlayerConnect</c> — игрок
        /// приносит своё место с собой в <c>GamePlayerConnectMessage</c>, уже
        /// в координатах якорей, потому что снял его, пока был на своей карте
        /// (находка <b>CAL-02</b>). Снимать позу самим в этот момент не по чему:
        /// сцена подключения — уже карта сервера, а не та, где игрок стоял.
        /// </para>
        ///
        /// <para>
        /// Тесты пользуются тем же входом: поднимать в EditMode настоящие якоря, аватары
        /// и менеджер сессий ради проверки одной ветки резолвера дороже, чем польза.
        /// </para>
        ///
        /// <para>
        /// Признак калибровки здесь не спрашивается намеренно. Место кладут все, а решает,
        /// применять ли его, <see cref="TryResolve" /> — там и только там. Второй гейт
        /// разъехался бы с первым при первой же правке.
        /// </para>
        /// </summary>
        /// <param name="sessionNetId"><c>netId</c> сессии, которой принадлежит поза.</param>
        /// <param name="localPosition">Позиция в системе координат якорей.</param>
        /// <param name="localRotation">Поворот в системе координат якорей.</param>
        /// <param name="capturedOnMap">Имя карты, с которой снят снимок — только для лога.</param>
        public static void Remember(uint sessionNetId, Vector3 localPosition, Quaternion localRotation,
                                    string capturedOnMap)
        {
            Placements[sessionNetId] = new Placement(localPosition, localRotation, inWorld: false, capturedOnMap);
        }
    }
}

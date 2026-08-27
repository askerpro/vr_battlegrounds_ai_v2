using System.Collections.Generic;
using Mirror;
using UnityEngine;
using VrBattlegrounds.Core;
using VrBattlegrounds.Managers;
using VrBattlegrounds.PhysicalSpaceUtils;

namespace VrBattlegrounds.Player.Avatars
{
    /// <summary>
    /// Серверная память о том, где стоял <b>откалиброванный</b> игрок, — чтобы смена
    /// карты его не двигала (<b>CAL-01</b>, задача T-30).
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
    /// <b>Почему поза хранится в системе координат якорей.</b> Мировых координат мало:
    /// обе карты проекта собраны из одного префаба арены, но в <c>TestMap1</c> он повёрнут
    /// на 90° вокруг Y относительно <c>TestMap2</c>. Сохранённая мировая позиция перенесла бы
    /// игрока в арену, повёрнутую на 90°, — то есть в стену или в чужую базу. Якоря же
    /// отмечают одни и те же физические метки в комнате, поэтому поза относительно них
    /// у карт общая. Пересчёт — <see cref="PhysicalSpaceAnchorFrame" />.
    /// </para>
    ///
    /// <para>
    /// <b>Жизненный цикл.</b> Снимок снимается один раз на карту — по
    /// <c>MapManager.MapLoadStarted</c>, то есть пока старая сцена ещё жива и её якоря
    /// на месте. Расходуется он при пересоздании аватаров на новой карте
    /// (<c>GameNetworkManager.OnServerReady</c> → <c>AvatarManager.ChangeAvatar</c> →
    /// <see cref="AvatarSpawnPointResolver" />). Каждый новый снимок затирает предыдущий
    /// целиком, поэтому отключившиеся сессии не копятся.
    /// </para>
    /// </summary>
    public static class CalibratedSpawnRegistry
    {
        /// <summary>Поза откалиброванного игрока в системе координат якорей той карты, где он стоял.</summary>
        private readonly struct Placement
        {
            public readonly Vector3 LocalPosition;
            public readonly Quaternion LocalRotation;

            /// <summary>Имя карты, с которой снят снимок. Нужно только для строки в логе.</summary>
            public readonly string CapturedOnMap;

            public Placement(Vector3 localPosition, Quaternion localRotation, string capturedOnMap)
            {
                LocalPosition = localPosition;
                LocalRotation = localRotation;
                CapturedOnMap = capturedOnMap;
            }
        }

        private static readonly Dictionary<uint, Placement> Placements = new Dictionary<uint, Placement>();

        /// <summary>Сколько откалиброванных игроков сейчас в памяти. Для тестов и диагностики.</summary>
        public static int Count => Placements.Count;

        // ── Подписка на смену карты ───────────────────────────────────────────

        /// <summary>
        /// Реестр цепляется к <c>MapManager.MapLoadStarted</c> сам, а не компонентом
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

            MapManager.MapLoadStarted -= OnMapLoadStarted;
            MapManager.MapLoadStarted += OnMapLoadStarted;
        }

        private static void OnMapLoadStarted(string sceneName)
        {
            CaptureAll($"смена карты на '{sceneName}'");
        }

        // ── Снимок ────────────────────────────────────────────────────────────

        /// <summary>
        /// Запоминает позы всех откалиброванных игроков в системе координат якорей
        /// <b>текущей</b> сцены. Зовётся до <c>ServerChangeScene</c>: после него ни якорей,
        /// ни аватаров уже нет.
        /// </summary>
        /// <param name="reason">Что вызвало снимок — уходит в лог как есть.</param>
        public static void CaptureAll(string reason)
        {
            Placements.Clear();

            if (!NetworkServer.active) return;
            if (PlayersManager.Instance == null) return;

            int calibrated = 0;
            foreach (PlayerSession session in PlayersManager.Instance.Sessions)
            {
                if (session == null || !session.IsCalibrated) continue;
                calibrated++;

                if (session.ActiveAvatar == null)
                {
                    GameLog.PhysicalSpace.Warning(
                        $"[CalibratedSpawnRegistry] {session.PlayerName} откалиброван, но аватара у него сейчас нет — " +
                        $"запоминать нечего ({reason}). После смены карты он окажется в зоне своей команды.");
                    continue;
                }

                PhysicalSpaceAnchorFrame frame;
                string diagnosis;
                if (!PhysicalSpaceAnchorFrame.TryBuildFromScene(out frame, out diagnosis))
                {
                    GameLog.PhysicalSpace.Warning(
                        $"[CalibratedSpawnRegistry] Место откалиброванных игроков не запомнено ({reason}): {diagnosis}. " +
                        "Без якорей позу не к чему привязать — после смены карты все окажутся в зонах своих команд.");
                    return;
                }

                Transform avatar = session.ActiveAvatar.transform;
                string map = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;

                Placement placement = new Placement(
                    frame.ToLocal(avatar.position),
                    frame.ToLocal(avatar.rotation),
                    map);

                Placements[session.netId] = placement;

                GameLog.PhysicalSpace.Info(
                    $"[CalibratedSpawnRegistry] {session.PlayerName}: место запомнено ({reason}). " +
                    $"Мир {avatar.position} на карте '{map}', относительно якорей " +
                    $"{placement.LocalPosition}. Якоря: {frame}");
            }

            if (calibrated == 0)
                GameLog.PhysicalSpace.Verbose($"[CalibratedSpawnRegistry] Откалиброванных игроков нет ({reason}).");
        }

        // ── Выдача ────────────────────────────────────────────────────────────

        /// <summary>
        /// Отвечает, где обязан появиться откалиброванный игрок на <b>текущей</b> карте.
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

            if (!session.IsCalibrated)
            {
                diagnosis = "игрок не калибровался — его место назначает игра";
                return false;
            }

            Placement placement;
            if (!Placements.TryGetValue(session.netId, out placement))
            {
                diagnosis = $"игрок откалиброван, но снимка его места нет " +
                            $"(запомнено мест: {Placements.Count})";
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
                frame.ToWorld(placement.LocalPosition),
                frame.ToWorld(placement.LocalRotation),
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
        /// Существует ради тестов: поднимать в EditMode настоящие якоря, аватары
        /// и менеджер сессий ради проверки одной ветки резолвера дороже, чем польза.
        /// Игровой код зовёт <see cref="CaptureAll" />, а не этот метод.
        /// </para>
        /// </summary>
        /// <param name="sessionNetId"><c>netId</c> сессии, которой принадлежит поза.</param>
        /// <param name="localPosition">Позиция в системе координат якорей.</param>
        /// <param name="localRotation">Поворот в системе координат якорей.</param>
        /// <param name="capturedOnMap">Имя карты, с которой снят снимок — только для лога.</param>
        public static void Remember(uint sessionNetId, Vector3 localPosition, Quaternion localRotation,
                                    string capturedOnMap)
        {
            Placements[sessionNetId] = new Placement(localPosition, localRotation, capturedOnMap);
        }
    }
}

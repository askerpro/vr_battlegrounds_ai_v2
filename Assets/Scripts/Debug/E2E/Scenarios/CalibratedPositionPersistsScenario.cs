// Ярус C (два процесса) — сценарий calibrated-position-persists, находка CAL-01 (задача T-30).
#if !VRBG_NO_E2E
using System;
using System.Collections;
using System.Collections.Generic;
using Mirror;
using UnityEngine;
using UnityEngine.SceneManagement;
using VrBattlegrounds.Core;
using VrBattlegrounds.GameModes;
using VrBattlegrounds.Managers;
using VrBattlegrounds.Maps;
using VrBattlegrounds.PhysicalSpaceUtils;
using VrBattlegrounds.Player;
using VrBattlegrounds.Player.Avatars;

namespace VrBattlegrounds.DevTools.E2E.Scenarios
{
    /// <summary>
    /// Сценарий <c>calibrated-position-persists</c> — находка <b>CAL-01</b> (T-30).
    ///
    /// <para>
    /// <b>Что доказывает.</b> Требование пользователя звучит так: до калибровки игрок
    /// может появиться где угодно — где он внутри арены, игра не знает; после калибровки
    /// его место задано физически и обязано пережить смену карты. Значит правильных
    /// ответов на вопрос «куда ставить» два, и сценарий проверяет <b>оба разом</b>:
    /// в одном прогоне один клиент объявляет калибровку, второй нет, и после смены карты
    /// они обязаны оказаться в <b>разных</b> местах. Одна ветка без другой ничего
    /// не доказывает: «всех в зону» и «никого не двигать» по отдельности выглядят
    /// одинаково правдоподобно.
    /// </para>
    ///
    /// <para>
    /// <b>Чего сценарий не проверяет.</b> Саму калибровку: она читает положение шлема
    /// и контроллеров, то есть требует железа. Клиент не калибруется, а объявляет
    /// результат — тот же приём, что в <c>calibration-scale-replication</c>. Проверяется
    /// всё, что после неё.
    /// </para>
    ///
    /// <para>
    /// <b>Почему две разные карты, а не перезагрузка одной.</b> Обе карты проекта собраны
    /// из одного префаба арены, но в <c>TestMap1</c> он повёрнут на 90° вокруг Y
    /// относительно <c>TestMap2</c>. Мировые координаты якорей у карт поэтому не совпадают,
    /// и «сохранить позицию» в мировых координатах означало бы развернуть игрока
    /// на 90° относительно арены. Перезагрузка одной и той же карты этой разницы
    /// не показала бы: там наивное сохранение мировой позиции прошло бы проверку.
    /// </para>
    ///
    /// <para>
    /// <b>Независимый эталон.</b> Откалиброванного игрока уводят на точку, вычисленную
    /// <b>из зон спавна</b>: середина между базами, сдвинутая на четыре метра в сторону
    /// чужой. Ни одного обращения к коду калибровки — зоны ставит художник. От своей
    /// зоны точка отстоит на 12 м, а в мировых координатах двух карт расходится на 5,7 м,
    /// потому что арена повёрнута. Значит замер различает сразу три ответа: «своё место
    /// в арене», «зона команды» и «старая мировая позиция».
    /// </para>
    ///
    /// <para>
    /// Матч не запускается: зоны нужны здесь как точки на карте, а не как условие
    /// готовности. Нужны два клиента — <c>-Clients 2</c>, — и карта, у которой есть пара.
    /// </para>
    /// </summary>
    public class CalibratedPositionPersistsScenario : IE2EScenario
    {
        public string Name => "calibrated-position-persists";

        // ── Имена проверок сервера ────────────────────────────────────────

        private const string CheckDedicated  = "сервер поднят как выделенный (ServerOnly, не хост)";
        private const string CheckClients    = "оба клиента подключились и сервер создал сессии";
        private const string CheckFirstMap   = "первая карта загружена, у каждой сессии есть аватар";
        private const string CheckBackChannel = "обратный канал наблюдения работает у обоих клиентов";
        private const string CheckDeclared   = "ровно один клиент объявил калибровку, второй остался без неё";
        private const string CheckAnchors    = "якоря обеих карт задают одну и ту же систему координат арены";
        private const string CheckDisplaced  = "откалиброванного игрока увели из зоны его команды на известную точку арены";
        private const string CheckKeptPlace  = "после смены карты откалиброванный игрок остался на своём месте в арене (CAL-01)";
        private const string CheckPlainZone  = "после смены карты неоткалиброванный игрок оказался в зоне своей команды";
        private const string CheckBranches   = "ветки различаются: откалиброванного в зону команды не утащило";

        // ── Имена проверок клиента ────────────────────────────────────────

        private const string CheckClientConnected = "клиент подключился к серверу";
        private const string CheckClientSession   = "сервер создал сессию для клиента";
        private const string CheckClientFirstMap  = "клиент переехал на первую карту вместе с сервером";
        private const string CheckClientSecondMap = "клиент переехал на вторую карту вместе с сервером";
        private const string CheckClientPlace     = "клиент видит свой аватар там, где положено его ветке";

        // ── Фазы, раздаваемые клиенту через SyncVar Score ─────────────────

        /// <summary>Сервер ещё готовится, клиенту делать нечего.</summary>
        private const int PhaseWait = 0;

        /// <summary>Рукопожатие, шаг 1: поднять флаг наблюдения.</summary>
        private const int PhaseRaise = 1;

        /// <summary>Рукопожатие, шаг 2: опустить флаг наблюдения.</summary>
        private const int PhaseLower = 2;

        /// <summary>Клиент-1 объявляет калибровку — подмена результата процедуры по якорям.</summary>
        private const int PhaseDeclare = 3;

        /// <summary>Вторая карта загружена, аватары пересозданы: клиенту пора снимать замер.</summary>
        private const int PhaseMeasure = 4;

        /// <summary>Прогон завершён, клиент вправе записать вердикт.</summary>
        private const int PhaseDone = 9;

        // ── Сроки ─────────────────────────────────────────────────────────

        private const float ServerWait       = 60f;
        private const float ClientsWait      = 90f;
        private const float MapWait          = 90f;
        private const float AvatarWait       = 90f;
        private const float HandshakeWait    = 60f;
        private const float DeclareWait      = 60f;
        private const float DisplaceWait     = 20f;
        private const float ClientMeasureWait = 90f;
        private const float ClientVerdictWait = 45f;
        private const float ClientPhaseWait   = 180f;

        /// <summary>
        /// Пауза, после которой позиция считается установившейся. Аватар создаёт сервер,
        /// но <c>NetworkTransform</c> на аватарах стоит с <c>syncDirection = ClientToServer</c>:
        /// владелец вправе прислать свою позицию поверх серверной. Замер по установившемуся
        /// состоянию отвечает на вопрос игрока «где я оказался», а не «где меня создали».
        /// </summary>
        private const float SettleHold = 3f;

        // ── Пороги ────────────────────────────────────────────────────────

        /// <summary>
        /// Допуск «аватар в точке», метры. Сервер создаёт аватар ровно в точке, но
        /// владелец может прислать свою позицию поверх, а сам аватар — осесть на коллайдер
        /// пола. Полтора метра заведомо меньше любого расстояния, которое проверка обязана
        /// различать (см. <see cref="MinSeparation"/>).
        /// </summary>
        private const float PositionTolerance = 1.5f;

        /// <summary>
        /// Минимальное разведение двух точек, при котором проверка вообще что-то значит,
        /// метры. Втрое больше допуска: перепутать «там же» и «в другом месте» нельзя.
        /// </summary>
        private const float MinSeparation = 4.5f;

        /// <summary>
        /// Насколько откалиброванного игрока отводят от центра арены вдоль линии
        /// «своя база → чужая», метры.
        ///
        /// <para>
        /// Центр арены как точка замера <b>не годится</b>, и это выяснилось прогоном:
        /// обе карты — одна и та же арена, повёрнутая вокруг собственного начала координат,
        /// поэтому центр у них общий и в мировых координатах тоже. Наивное сохранение
        /// мировой позиции прошло бы такую проверку. Четыре метра вдоль линии баз
        /// разводят мировые координаты одной и той же точки арены на 5,7 м — втрое больше
        /// допуска, — и оставляют до своей зоны 12 м.
        /// </para>
        /// </summary>
        private const float ProbeOffset = 4f;

        /// <summary>
        /// Насколько геометрия арены обязана совпасть у двух карт в системе координат
        /// якорей, метры. Карты собраны из одного префаба, поэтому расхождение здесь —
        /// не погрешность, а разъехавшаяся расстановка: одна калибровка на сессию
        /// на таких картах работать не может, и прогон недействителен.
        /// </summary>
        private const float ArenaCongruence = 0.5f;

        public IEnumerator Run(E2EContext context, E2EResult result)
        {
            if (context.IsServerRole)
                return RunServer(context, result);

            return RunClient(context, result);
        }

        // ══════════════════════════════════════════════════════════════════
        //  Роль сервера
        // ══════════════════════════════════════════════════════════════════

        private IEnumerator RunServer(E2EContext context, E2EResult result)
        {
            result.Declare(CheckDedicated, CheckClients, CheckFirstMap, CheckBackChannel,
                           CheckDeclared, CheckAnchors, CheckDisplaced,
                           CheckKeptPlace, CheckPlainZone, CheckBranches);

            string firstMap  = context.Map;
            string secondMap = SecondMapFor(firstMap);

            try
            {
                // ── 1. Выделенный сервер ──────────────────────────────────
                float deadline = Now + ServerWait;
                while (!NetworkServer.active && Now < deadline)
                    yield return null;

                bool dedicated = NetworkServer.active && !NetworkClient.active;
                result.Set(CheckDedicated, dedicated,
                    dedicated
                        ? "NetworkServer.active=true, NetworkClient.active=false — это ServerOnly"
                        : $"NetworkServer.active={NetworkServer.active}, NetworkClient.active={NetworkClient.active}. " +
                          "Смена карты через ServerChangeScene и пересоздание аватаров в OnServerReady " +
                          "воспроизводятся только на выделенном сервере: процесс обязан идти " +
                          "с -batchmode -nographics.");

                if (!dedicated)
                {
                    result.Summary = "конфигурация не выделенный сервер, прогон недействителен";
                    yield break;
                }

                // Первым делом: иначе он сам расставит игроков по зонам спавна
                // и измерять будет нечего.
                DisableDebugOrchestrator();

                // ── 2. Клиенты ────────────────────────────────────────────
                deadline = Now + ClientsWait;
                while (SessionCount() < context.ExpectedClients && Now < deadline)
                    yield return null;

                int sessions = SessionCount();
                bool clientsOk = sessions >= context.ExpectedClients;
                result.Set(CheckClients, clientsOk,
                    clientsOk
                        ? $"сессий на сервере: {sessions} (ждали {context.ExpectedClients})"
                        : $"за {ClientsWait:F0} с подключилось сессий: {sessions} из {context.ExpectedClients}. " +
                          "Смотри client-*.log рядом с этим файлом.");

                if (!clientsOk)
                {
                    result.Summary = "клиенты не подключились, вердикт вынести нельзя";
                    yield break;
                }

                SessionManager sessionManager = SessionManager.Instance;
                if (sessionManager == null || MapManager.Instance == null || AvatarManager.Instance == null)
                {
                    result.Set(CheckFirstMap, false,
                        $"SessionManager={(sessionManager == null ? "null" : "есть")}, " +
                        $"MapManager={(MapManager.Instance == null ? "null" : "есть")}, " +
                        $"AvatarManager={(AvatarManager.Instance == null ? "null" : "есть")}");
                    result.Summary = "менеджеры не поднялись, прогон недействителен";
                    yield break;
                }

                // Режим выбирается не ради матча, а ради валидных индексов команд:
                // ChangeAvatar молча выходит, если TeamRegistry не знает индекс команды.
                sessionManager.SetSession(firstMap, "elimination");
                yield return null;

                GameModeData mode = sessionManager.SelectedGameModeData;
                if (mode == null || mode.teams == null || mode.teams.Length < 2)
                {
                    result.Set(CheckFirstMap, false,
                        "режим не отдал две команды — развести игроков по разным базам нечем " +
                        $"(команд: {(mode == null || mode.teams == null ? 0 : mode.teams.Length)})");
                    result.Summary = "режим без двух команд, вердикт вынести нельзя";
                    yield break;
                }

                GameLog.Debug.Info($"[E2E] Команды распределены: {AssignTeams(mode)}");

                // ── 3. Первая карта ───────────────────────────────────────
                MapManager.Instance.LoadMap(firstMap);

                E2EWaitOutcome onFirstMap = new E2EWaitOutcome();
                yield return E2EWait.Until(onFirstMap,
                    $"сервер переехал на первую карту '{firstMap}'",
                    MapWait,
                    () => SceneManager.GetActiveScene().name == firstMap,
                    () => $"активная сцена='{SceneManager.GetActiveScene().name}'");

                if (!onFirstMap.Succeeded)
                {
                    result.Set(CheckFirstMap, false, onFirstMap.Diagnosis);
                    result.Summary = "первая карта не загрузилась, вердикт вынести нельзя";
                    yield break;
                }

                E2EWaitOutcome avatarsUp = new E2EWaitOutcome();
                yield return E2EWait.Until(avatarsUp,
                    "у обеих сессий появился аватар на первой карте",
                    AvatarWait,
                    AllSessionsHaveAvatar,
                    DescribeSessions,
                    () => NetworkServer.connections.Count > 0
                        ? null
                        : "на сервере не осталось подключений — аватаров уже не будет");

                result.Set(CheckFirstMap, avatarsUp.Succeeded,
                    avatarsUp.Succeeded
                        ? $"активная сцена='{SceneManager.GetActiveScene().name}', {DescribeSessions()}"
                        : avatarsUp.Diagnosis +
                          " GameNetworkManager.OnServerReady спавнит аватар только для готового клиента.");

                if (!avatarsUp.Succeeded)
                {
                    result.Summary = "аватары не появились, вердикт вынести нельзя";
                    yield break;
                }

                // ── 4. Рукопожатие обратного канала ───────────────────────
                // Без него «клиент не отчитался» в конце нельзя отличить
                // от «отчитываться было нечем».
                SetPhase(PhaseRaise);

                E2EWaitOutcome raised = new E2EWaitOutcome();
                yield return E2EWait.Until(raised,
                    "оба клиента подняли флаг наблюдения",
                    HandshakeWait,
                    AllClientsRaised,
                    DescribeFlags,
                    () => NetworkServer.connections.Count > 0 ? null : "подключений не осталось");

                SetPhase(PhaseLower);

                E2EWaitOutcome lowered = new E2EWaitOutcome();
                yield return E2EWait.Until(lowered,
                    "оба клиента опустили флаг наблюдения",
                    HandshakeWait,
                    () => !AnyClientFlag(),
                    DescribeFlags,
                    () => NetworkServer.connections.Count > 0 ? null : "подключений не осталось");

                bool backChannelOk = raised.Succeeded && lowered.Succeeded;
                result.Set(CheckBackChannel, backChannelOk,
                    backChannelOk
                        ? $"оба клиента подняли и опустили флаг по команде сервера ({raised.Diagnosis})"
                        : $"рукопожатие не состоялось. Подъём: {raised.Diagnosis}. Спуск: {lowered.Diagnosis}");

                if (!backChannelOk)
                {
                    result.Summary = "обратный канал наблюдения не поднялся, вердикт вынести нельзя";
                    yield break;
                }

                // ── 5. Калибровку объявляет ровно один ────────────────────
                SetPhase(PhaseDeclare);

                E2EWaitOutcome declared = new E2EWaitOutcome();
                yield return E2EWait.Until(declared,
                    "ровно одна сессия отмечена как откалиброванная",
                    DeclareWait,
                    () => CalibratedCount() == 1,
                    DescribeCalibration,
                    () => NetworkServer.connections.Count > 0 ? null : "подключений не осталось");

                PlayerSession calibrated = SessionWithCalibration(true);
                PlayerSession plain      = SessionWithCalibration(false);

                bool declaredOk = declared.Succeeded && calibrated != null && plain != null;
                result.Set(CheckDeclared, declaredOk,
                    declaredOk
                        ? $"откалиброван: {calibrated.PlayerName} (команда {calibrated.TeamIndex}); " +
                          $"без калибровки: {plain.PlayerName} (команда {plain.TeamIndex})"
                        : declared.Diagnosis +
                          " CmdSetCalibrated не доехал до сервера или его прислали оба клиента — " +
                          "тогда сравнивать две ветки не с чем.");

                if (!declaredOk)
                {
                    result.Summary = "признак калибровки до сервера не доехал, вердикт вынести нельзя";
                    yield break;
                }

                // ── 6. Точка замера на первой карте ───────────────────────
                Vector3 firstProbe;
                string probeDiagnosis;
                if (!TryArenaProbePoint(calibrated.Team, out firstProbe, out probeDiagnosis))
                {
                    result.Set(CheckAnchors, false, $"на карте '{firstMap}': {probeDiagnosis}");
                    result.Summary = "точку замера на первой карте не вычислить, вердикт вынести нельзя";
                    yield break;
                }

                PhysicalSpaceAnchorFrame firstFrame;
                string firstFrameDiagnosis;
                bool firstFrameOk = PhysicalSpaceAnchorFrame.TryBuildFromScene(out firstFrame, out firstFrameDiagnosis);

                Vector3 firstProbeInAnchors = firstFrameOk ? firstFrame.ToLocal(firstProbe) : Vector3.zero;

                // ── 7. Увод откалиброванного в точку замера ───────────────
                TeamSpawnZone calibratedZoneFirst = AvatarSpawnPointResolver.FindZone(calibrated.Team);
                float probeToZoneFirst = calibratedZoneFirst != null
                    ? Vector3.Distance(firstProbe, calibratedZoneFirst.transform.position)
                    : 0f;

                yield return DisplaceAvatar(calibrated, firstProbe);

                Vector3 displaced = calibrated.ActiveAvatar != null
                    ? calibrated.ActiveAvatar.transform.position
                    : Vector3.zero;

                float displacedToProbe = Vector3.Distance(displaced, firstProbe);
                bool displacedOk = calibrated.ActiveAvatar != null
                                   && displacedToProbe <= PositionTolerance
                                   && probeToZoneFirst >= MinSeparation;

                result.Set(CheckDisplaced, displacedOk,
                    displacedOk
                        ? $"{calibrated.PlayerName} стоит в {Fmt(firstProbe)} на карте '{firstMap}', " +
                          $"до зоны своей команды {probeToZoneFirst:F2} м. Пока игрок в зоне, «остался на месте» " +
                          "и «переехал в зону» неразличимы — поэтому его уводят до замера."
                        : $"увести не удалось: цель {Fmt(firstProbe)}, аватар " +
                          $"{(calibrated.ActiveAvatar == null ? "исчез" : Fmt(displaced))}, до цели {displacedToProbe:F2} м " +
                          $"при допуске {PositionTolerance:F1} м; от точки замера до зоны команды {probeToZoneFirst:F2} м " +
                          $"при нужных {MinSeparation:F1} м.");

                if (!displacedOk)
                {
                    result.Summary = "откалиброванного игрока не удалось развести с его зоной, вердикт вынести нельзя";
                    yield break;
                }

                Vector3 displacedInAnchors = firstFrameOk ? firstFrame.ToLocal(displaced) : Vector3.zero;

                // ── 8. Вторая карта ───────────────────────────────────────
                GameLog.Debug.Info($"[E2E] Меняю карту '{firstMap}' -> '{secondMap}'");
                MapManager.Instance.LoadMap(secondMap);

                E2EWaitOutcome onSecondMap = new E2EWaitOutcome();
                yield return E2EWait.Until(onSecondMap,
                    $"сервер переехал на вторую карту '{secondMap}'",
                    MapWait,
                    () => SceneManager.GetActiveScene().name == secondMap,
                    () => $"активная сцена='{SceneManager.GetActiveScene().name}'",
                    () => NetworkServer.connections.Count > 0 ? null : "подключений не осталось");

                if (!onSecondMap.Succeeded)
                {
                    result.Set(CheckAnchors, false, onSecondMap.Diagnosis);
                    result.Summary = "вторая карта не загрузилась, вердикт вынести нельзя";
                    yield break;
                }

                E2EWaitOutcome avatarsBack = new E2EWaitOutcome();
                yield return E2EWait.Until(avatarsBack,
                    "после смены карты у обеих сессий снова есть аватар",
                    AvatarWait,
                    AllSessionsHaveAvatar,
                    DescribeSessions,
                    () => NetworkServer.connections.Count > 0
                        ? null
                        : "на сервере не осталось подключений — аватаров уже не будет");

                if (!avatarsBack.Succeeded)
                {
                    result.Set(CheckAnchors, false, avatarsBack.Diagnosis);
                    result.Summary = "аватары не пересоздались, вердикт по CAL-01 вынести нельзя";
                    yield break;
                }

                yield return E2EWait.Hold(SettleHold);

                // ── 9. Годность двух карт как пары ────────────────────────
                Vector3 secondProbe;
                if (!TryArenaProbePoint(calibrated.Team, out secondProbe, out probeDiagnosis))
                {
                    result.Set(CheckAnchors, false, $"на карте '{secondMap}': {probeDiagnosis}");
                    result.Summary = "точку замера на второй карте не вычислить, вердикт вынести нельзя";
                    yield break;
                }

                PhysicalSpaceAnchorFrame secondFrame;
                string secondFrameDiagnosis;
                bool secondFrameOk = PhysicalSpaceAnchorFrame.TryBuildFromScene(out secondFrame, out secondFrameDiagnosis);

                Vector3 secondProbeInAnchors = secondFrameOk ? secondFrame.ToLocal(secondProbe) : Vector3.zero;
                float congruence = Vector3.Distance(firstProbeInAnchors, secondProbeInAnchors);
                float worldShift = Vector3.Distance(firstProbe, secondProbe);

                bool anchorsOk = firstFrameOk && secondFrameOk
                                 && congruence <= ArenaCongruence
                                 && worldShift > PositionTolerance;

                result.Set(CheckAnchors, anchorsOk,
                    anchorsOk
                        ? $"'{firstMap}': {firstFrame}; '{secondMap}': {secondFrame}. " +
                          $"Одна и та же точка арены относительно якорей: {Fmt(firstProbeInAnchors)} и " +
                          $"{Fmt(secondProbeInAnchors)}, расхождение {congruence:F3} м. В мировых координатах " +
                          $"она же разъехалась на {worldShift:F2} м ({Fmt(firstProbe)} против {Fmt(secondProbe)}) — " +
                          "вот почему сохранять мировую позицию нельзя, а позицию относительно якорей можно. " +
                          "Замер ниже эти два ответа различает."
                        : !firstFrameOk
                            ? $"на карте '{firstMap}' система координат якорей не построена: {firstFrameDiagnosis}"
                            : !secondFrameOk
                                ? $"на карте '{secondMap}' система координат якорей не построена: {secondFrameDiagnosis}"
                                : worldShift <= PositionTolerance
                                    ? $"точка замера на обеих картах пришлась почти в одно и то же место мира " +
                                      $"({Fmt(firstProbe)} и {Fmt(secondProbe)}, {worldShift:F2} м при допуске " +
                                      $"{PositionTolerance:F1} м). На такой паре карт наивное сохранение мировой " +
                                      "позиции прошло бы проверку — прогон ничего не доказывает."
                                    : $"якоря карт разъехались с ареной: одна и та же точка арены относительно " +
                                      $"якорей на '{firstMap}' — {Fmt(firstProbeInAnchors)}, на '{secondMap}' — " +
                                      $"{Fmt(secondProbeInAnchors)}, расхождение {congruence:F2} м при допуске " +
                                      $"{ArenaCongruence:F2} м. Одна калибровка на сессию на такой паре карт " +
                                      "работать не может: якоря обязаны отмечать одни и те же физические метки.");

                if (!anchorsOk)
                {
                    result.Summary = "карты непригодны как пара для одной калибровки, вердикт вынести нельзя";
                    yield break;
                }

                // ── 10. Ветка «откалиброван»: место сохранилось ───────────
                TeamSpawnZone calibratedZone = AvatarSpawnPointResolver.FindZone(calibrated.Team);
                Vector3 calibratedZonePos = calibratedZone != null ? calibratedZone.transform.position : Vector3.zero;

                Vector3 keptWorld = calibrated.ActiveAvatar != null
                    ? calibrated.ActiveAvatar.transform.position
                    : Vector3.zero;

                Vector3 keptInAnchors = secondFrame.ToLocal(keptWorld);
                float keptToProbe = Vector3.Distance(keptWorld, secondProbe);
                float keptToZone  = Vector3.Distance(keptWorld, calibratedZonePos);
                float keptToStale = Vector3.Distance(keptWorld, displaced);
                float keptDrift   = Vector3.Distance(keptInAnchors, displacedInAnchors);

                bool keptPlace = calibrated.ActiveAvatar != null && keptToProbe <= PositionTolerance;

                result.Set(CheckKeptPlace, keptPlace,
                    (keptPlace
                        ? $"{calibrated.PlayerName} остался на своём месте в арене: до ожидаемой точки " +
                          $"{Fmt(secondProbe)} на '{secondMap}' {keptToProbe:F2} м, до зоны своей команды " +
                          $"{keptToZone:F2} м, до старой мировой позиции {keptToStale:F2} м."
                        : $"{calibrated.PlayerName} оказался в {Fmt(keptWorld)}: до своего места {keptToProbe:F2} м " +
                          $"при допуске {PositionTolerance:F1} м, до зоны своей команды {keptToZone:F2} м, " +
                          $"до старой мировой позиции {keptToStale:F2} м. " +
                          (keptToZone <= PositionTolerance
                              ? "Это CAL-01 в чистом виде: игру не интересует, что игрок откалиброван, " +
                                "и она отправляет его на базу вместе со всеми."
                              : keptToStale <= PositionTolerance
                                  ? "Позиция сохранена в мировых координатах, а не относительно якорей: " +
                                    "арена на этих картах повёрнута, и игрока развернуло вместе с ней."
                                  : "Ни своё место, ни зона, ни старая мировая позиция — ищи, кто ещё двигает аватар.")) +
                    $" До смены карты стоял в {Fmt(displaced)} на '{firstMap}', относительно якорей " +
                    $"{Fmt(displacedInAnchors)}; сейчас относительно якорей {Fmt(keptInAnchors)} " +
                    $"(сдвиг в системе координат арены {keptDrift:F2} м).");

                // ── 11. Ветка «не откалиброван»: зона своей команды ───────
                TeamSpawnZone plainZone = AvatarSpawnPointResolver.FindZone(plain.Team);
                Vector3 plainZonePos = plainZone != null ? plainZone.transform.position : Vector3.zero;

                Vector3 plainWorld = plain.ActiveAvatar != null
                    ? plain.ActiveAvatar.transform.position
                    : Vector3.zero;

                float plainToZone  = Vector3.Distance(plainWorld, plainZonePos);
                float plainToProbe = Vector3.Distance(plainWorld, secondProbe);
                bool plainOk = plain.ActiveAvatar != null && plainZone != null && plainToZone <= PositionTolerance;

                result.Set(CheckPlainZone, plainOk,
                    plainOk
                        ? $"{plain.PlayerName} в зоне своей команды: {Fmt(plainWorld)}, до зоны {plainToZone:F2} м, " +
                          $"до места откалиброванного {plainToProbe:F2} м. До калибровки игра не знает, где игрок " +
                          "внутри арены, и зона — разумное «где угодно»."
                        : $"{plain.PlayerName} оказался в {Fmt(plainWorld)}: до зоны своей команды " +
                          $"{plainToZone:F2} м при допуске {PositionTolerance:F1} м " +
                          $"(зона={(plainZone == null ? "не найдена" : Fmt(plainZonePos))}). " +
                          "Ветка «не откалиброван» обязана вести в зону — иначе исправление CAL-01 " +
                          "сломало общий случай.");

                // ── 12. Ветки действительно разные ────────────────────────
                float betweenPlayers = Vector3.Distance(keptWorld, plainWorld);
                bool branchesDiffer = calibrated.ActiveAvatar != null
                                      && plain.ActiveAvatar != null
                                      && keptToZone >= MinSeparation;

                result.Set(CheckBranches, branchesDiffer,
                    branchesDiffer
                        ? $"откалиброванный стоит в {keptToZone:F2} м от зоны своей команды, " +
                          $"неоткалиброванный — в {plainToZone:F2} м от своей. Между игроками {betweenPlayers:F2} м. " +
                          "Значит выбор точки действительно зависит от признака калибровки, а не совпал случайно."
                        : $"откалиброванный оказался в {keptToZone:F2} м от зоны своей команды при нужных " +
                          $"{MinSeparation:F1} м — «его место» и «зона команды» в этом прогоне неразличимы, " +
                          "и зелёная проверка выше ничего не значила бы.");

                // ── 13. Замер клиентов ────────────────────────────────────
                SetPhase(PhaseMeasure);

                E2EWaitOutcome clientsMeasured = new E2EWaitOutcome();
                yield return E2EWait.Until(clientsMeasured,
                    "оба клиента отчитались, что сняли свой замер",
                    ClientMeasureWait,
                    () => ReportedFlags() >= context.ExpectedClients,
                    () => $"отчитались {ReportedFlags()} из {context.ExpectedClients}; {DescribeSessions()}",
                    () => NetworkServer.connections.Count > 0
                        ? null
                        : "на сервере не осталось подключений — замера уже не будет");

                GameLog.Debug.Info($"[E2E] Клиентский замер: {clientsMeasured.Diagnosis}");

                result.Summary = result.AllChecksGreen
                    ? "откалиброванный игрок сохранил своё место в арене, неоткалиброванный появился в зоне — " +
                      "CAL-01 не воспроизводится"
                    : !keptPlace
                        ? "CAL-01 подтверждена: смена карты сдвинула откалиброванного игрока с его места"
                        : "есть красные проверки, см. detail";

                SetPhase(PhaseDone);
                yield return WaitForClientVerdicts(context);
            }
            finally
            {
                // Клиент ждёт PhaseDone, чтобы записать вердикт. Ставим его на любом
                // выходе, включая ранний yield break: иначе клиент досидит до своего
                // таймаута и прогон станет INCONCLUSIVE вместо честного красного.
                SetPhase(PhaseDone);
            }
        }

        /// <summary>
        /// Держит серверный процесс живым, пока клиенты не запишут вердикт. Обрыв связи
        /// посреди клиентских ожиданий выглядит как «сигнал не пришёл» и даёт мигающие
        /// ворота (TEST-01).
        /// </summary>
        private static IEnumerator WaitForClientVerdicts(E2EContext context)
        {
            E2EWaitOutcome barrier = new E2EWaitOutcome();

            yield return E2EWait.Until(barrier,
                $"все {context.ExpectedClients} клиента отчитались, что записали вердикт",
                ClientVerdictWait,
                () => ReportedFlags() == 0 && SessionCount() >= context.ExpectedClients,
                () => $"держат флаг: {ReportedFlags()} из {SessionCount()} сессий; " +
                      $"подключений: {NetworkServer.connections.Count}");

            GameLog.Debug.Info($"[E2E] Барьер клиентских вердиктов: {barrier.Diagnosis}");
        }

        // ══════════════════════════════════════════════════════════════════
        //  Роль клиента
        // ══════════════════════════════════════════════════════════════════

        private IEnumerator RunClient(E2EContext context, E2EResult result)
        {
            result.Declare(CheckClientConnected, CheckClientSession,
                           CheckClientFirstMap, CheckClientSecondMap, CheckClientPlace);

            DisableDebugOrchestrator();

            string firstMap  = context.Map;
            string secondMap = SecondMapFor(firstMap);

            // Калибровку объявляет только первый клиент: вторая ветка проверки
            // существует ровно потому, что кто-то обязан остаться без калибровки.
            bool declaresCalibration = context.Role != "client-2";
            GameLog.Debug.Info(
                $"[E2E] Роль {context.Role}: калибровку {(declaresCalibration ? "объявляю" : "не объявляю")}");

            // ── Подключение ───────────────────────────────────────────────
            float deadline = Now + 30f;
            while (!NetworkClient.isConnected && Now < deadline)
                yield return null;

            if (!NetworkClient.isConnected && !string.IsNullOrEmpty(context.ServerAddress))
            {
                GameLog.Debug.Info($"[E2E] Discovery молчит 30 с, подключаюсь напрямую к {context.ServerAddress}");

                Mirror.Discovery.NetworkDiscovery discovery = UnityEngine.Object.FindFirstObjectByType<Mirror.Discovery.NetworkDiscovery>();
                if (discovery != null)
                    discovery.StopDiscovery();

                if (NetworkManager.singleton != null && !NetworkClient.active)
                {
                    NetworkManager.singleton.networkAddress = context.ServerAddress;
                    NetworkManager.singleton.StartClient();
                }

                deadline = Now + 30f;
                while (!NetworkClient.isConnected && Now < deadline)
                    yield return null;
            }

            bool connected = NetworkClient.isConnected;
            result.Set(CheckClientConnected, connected,
                connected
                    ? $"подключён к {(NetworkManager.singleton != null ? NetworkManager.singleton.networkAddress : "?")}"
                    : $"за 60 с подключиться не удалось: ни Discovery, ни прямое подключение к '{context.ServerAddress}'");

            if (!connected)
            {
                result.Summary = "клиент не подключился";
                yield break;
            }

            deadline = Now + 60f;
            while (PlayerSession.LocalSession == null && Now < deadline)
                yield return null;

            PlayerSession local = PlayerSession.LocalSession;
            result.Set(CheckClientSession, local != null,
                local != null
                    ? $"PlayerSession.LocalSession netId={local.netId}"
                    : "за 60 с сервер не создал PlayerSession для этого клиента");

            if (local == null)
            {
                result.Summary = "сессии нет, отчитываться нечем";
                yield break;
            }

            // ── Первая карта ──────────────────────────────────────────────
            E2EWaitOutcome firstArrival = new E2EWaitOutcome();
            yield return E2EWait.Until(firstArrival,
                $"клиент переехал на первую карту '{firstMap}'",
                ClientPhaseWait,
                () => SceneManager.GetActiveScene().name == firstMap,
                () => $"активная сцена='{SceneManager.GetActiveScene().name}'",
                () => NetworkClient.isConnected ? null : "связь с сервером пропала");

            result.Set(CheckClientFirstMap, firstArrival.Succeeded, firstArrival.Diagnosis);

            if (!firstArrival.Succeeded)
            {
                result.Summary = "клиент не переехал на первую карту";
                yield break;
            }

            // ── Наблюдение по фазам сервера ───────────────────────────────
            bool raised = false;
            bool lowered = false;
            bool declaredCalibration = false;
            bool reachedSecondMap = false;
            bool measured = false;
            string placeDetail = "(замер не выполнялся)";
            bool placeOk = false;
            int lastPhase = -1;

            float hardDeadline = Now + Mathf.Max(30f, context.Timeout - 25f);

            while (Now < hardDeadline)
            {
                if (!NetworkClient.isConnected || PlayerSession.LocalSession == null)
                    break;

                local = PlayerSession.LocalSession;
                int phase = local.Score;

                if (phase != lastPhase)
                {
                    GameLog.Debug.Info($"[E2E] Клиент видит фазу прогона: {phase}");
                    lastPhase = phase;
                }

                if (phase >= PhaseRaise && !raised)
                {
                    local.CmdSetDogTagGrabbed(true);
                    raised = true;
                }

                if (phase >= PhaseLower && !lowered)
                {
                    local.CmdSetDogTagGrabbed(false);
                    lowered = true;
                }

                // Объявление калибровки — подмена результата процедуры по якорям.
                if (phase >= PhaseDeclare && !declaredCalibration && lowered)
                {
                    if (declaresCalibration)
                    {
                        local.CmdSetCalibrated(true);
                        GameLog.Debug.Info("[E2E] Клиент объявил калибровку физического пространства");
                    }

                    declaredCalibration = true;
                }

                if (!reachedSecondMap && SceneManager.GetActiveScene().name == secondMap)
                {
                    reachedSecondMap = true;
                    GameLog.Debug.Info($"[E2E] Клиент переехал на вторую карту '{secondMap}'");
                }

                // Замер снимается по отмашке сервера: раньше аватар может быть ещё
                // не пересоздан, позже сервер уже погасит прогон.
                if (phase >= PhaseMeasure && !measured && reachedSecondMap)
                {
                    placeOk = MeasureOwnPlace(local, declaresCalibration, out placeDetail);
                    measured = true;

                    local.CmdSetDogTagGrabbed(true);
                    GameLog.Debug.Info($"[E2E] Клиентский замер: {placeDetail}");
                }

                if (phase >= PhaseDone && measured)
                    break;

                if (phase >= PhaseDone && !measured)
                {
                    // Сервер закончил, а замерять было нечем — фиксируем как есть.
                    placeDetail = reachedSecondMap
                        ? placeDetail
                        : $"клиент так и не оказался на второй карте '{secondMap}', " +
                          $"активная сцена='{SceneManager.GetActiveScene().name}'";
                    break;
                }

                yield return null;
            }

            result.Set(CheckClientSecondMap, reachedSecondMap,
                reachedSecondMap
                    ? $"активная сцена='{SceneManager.GetActiveScene().name}'"
                    : $"клиент не переехал на '{secondMap}', остался в '{SceneManager.GetActiveScene().name}'");

            result.Set(CheckClientPlace, placeOk, placeDetail);

            result.Summary = result.AllChecksGreen
                ? "клиент видит свой аватар там, где положено его ветке"
                : "клиент увидел свой аватар не там, где ждали, см. detail";

            // Флаг опускается последним: по нему сервер понимает, что вердикт записан.
            if (PlayerSession.LocalSession != null)
                PlayerSession.LocalSession.CmdSetDogTagGrabbed(false);
        }

        /// <summary>
        /// Замер клиента: своё место против того, что положено его ветке.
        ///
        /// <para>
        /// Откалиброванный обязан стоять в точке замера — туда его увёл сервер
        /// на прошлой карте. Неоткалиброванный — в зоне своей команды. Обе точки
        /// клиент вычисляет сам, из зон спавна на своей сцене: сравнивать надо
        /// с независимым эталоном, а не с тем, что сообщил сервер.
        /// </para>
        /// </summary>
        private static bool MeasureOwnPlace(PlayerSession local, bool calibrated, out string detail)
        {
            PlayerController avatar = local.ActiveAvatar;

            Vector3 probe;
            string probeDiagnosis;
            bool haveProbe = TryArenaProbePoint(local.Team, out probe, out probeDiagnosis);

            TeamSpawnZone zone = AvatarSpawnPointResolver.FindZone(local.Team);

            if (avatar == null || !haveProbe || zone == null)
            {
                detail = $"замерять нечем: аватар={(avatar == null ? "нет" : "есть")}, " +
                         $"точка замера={(haveProbe ? Fmt(probe) : probeDiagnosis)}, " +
                         $"зона своей команды={(zone == null ? "не найдена" : zone.name)}.";
                return false;
            }

            Vector3 position = avatar.transform.position;
            Vector3 expected = calibrated ? probe : zone.transform.position;
            float toExpected = Vector3.Distance(position, expected);
            float toOther    = Vector3.Distance(position, calibrated ? zone.transform.position : probe);
            bool ok = toExpected <= PositionTolerance;

            detail = (ok
                    ? $"свой аватар там, где положено: {Fmt(position)}."
                    : $"свой аватар в {Fmt(position)}, а ждали {Fmt(expected)} — до неё {toExpected:F2} м " +
                      $"при допуске {PositionTolerance:F1} м.") +
                $" Ветка: {(calibrated ? "откалиброван — место задано физически, ждём своё место в арене" : "не калибровался — ждём зону своей команды")}. " +
                $"Место в арене: {Fmt(probe)}, зона '{(local.Team != null ? local.Team.displayName : "?")}': " +
                $"{Fmt(zone.transform.position)}. До второй точки {toOther:F2} м.";

            return ok;
        }

        // ══════════════════════════════════════════════════════════════════
        //  Вспомогательное — сервер
        // ══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Уводит игрока в заданную точку игровым способом — <c>PlayerController.Respawn</c>.
        ///
        /// <para>
        /// Прямая запись <c>transform.position</c> на сервере тут ненадёжна:
        /// <c>NetworkTransform</c> на аватарах стоит с <c>syncDirection = ClientToServer</c>,
        /// и владелец вернёт свою позицию поверх серверной ближайшим же пакетом. Поэтому
        /// сначала <c>ServerDevTeleport</c> — он рассылает <c>RpcDevTeleport</c>, и настоящий переезд
        /// делает сам владелец, — а прямая запись остаётся запасным вариантом.
        /// </para>
        /// </summary>
        private static IEnumerator DisplaceAvatar(PlayerSession session, Vector3 target)
        {
            PlayerController avatar = session.ActiveAvatar;
            if (avatar == null)
                yield break;

            GameObject marker = new GameObject("E2E_DisplaceTarget");
            marker.transform.SetPositionAndRotation(target, avatar.transform.rotation);

            avatar.ServerDevTeleport(marker.transform.position, marker.transform.rotation);

            E2EWaitOutcome moved = new E2EWaitOutcome();
            yield return E2EWait.Until(moved,
                $"серверная копия аватара доехала до {Fmt(target)}",
                DisplaceWait,
                () => session.ActiveAvatar != null &&
                      Vector3.Distance(session.ActiveAvatar.transform.position, target) <= PositionTolerance,
                () => session.ActiveAvatar == null
                    ? "аватар исчез"
                    : $"аватар в {Fmt(session.ActiveAvatar.transform.position)}, до цели " +
                      $"{Vector3.Distance(session.ActiveAvatar.transform.position, target):F2} м",
                () => NetworkServer.connections.Count > 0 ? null : "подключений не осталось");

            GameLog.Debug.Info($"[E2E] Увод игрока на точку замера: {moved.Diagnosis}");

            if (!moved.Succeeded && session.ActiveAvatar != null)
            {
                GameLog.Debug.Info("[E2E] Владелец не отчитался — ставлю аватар на место записью на сервере");
                session.ActiveAvatar.transform.position = target;
            }

            UnityEngine.Object.Destroy(marker);

            yield return E2EWait.Hold(SettleHold);
        }

        /// <summary>Раздаёт команды по кругу: игроки обязаны оказаться в разных базах.</summary>
        private static string AssignTeams(GameModeData mode)
        {
            IReadOnlyList<PlayerSession> sessions = PlayersManager.Instance.Sessions;
            List<string> report = new List<string>();

            for (int i = 0; i < sessions.Count; i++)
            {
                TeamData team = mode.teams[i % mode.teams.Length];
                if (team == null) continue;

                sessions[i].TeamIndex = team.teamIndex;
                report.Add($"{sessions[i].PlayerName}->{team.displayName}({team.teamIndex})");
            }

            return string.Join(", ", report.ToArray());
        }

        /// <summary>Раздаёт номер фазы клиентам через SyncVar <c>PlayerSession.Score</c>.</summary>
        private static void SetPhase(int phase)
        {
            if (PlayersManager.Instance == null) return;

            foreach (PlayerSession session in PlayersManager.Instance.Sessions)
            {
                if (session != null)
                    session.Score = phase;
            }
        }

        private static int CalibratedCount()
        {
            if (PlayersManager.Instance == null) return 0;

            int count = 0;
            foreach (PlayerSession session in PlayersManager.Instance.Sessions)
            {
                if (session != null && session.IsCalibrated)
                    count++;
            }

            return count;
        }

        private static PlayerSession SessionWithCalibration(bool calibrated)
        {
            if (PlayersManager.Instance == null) return null;

            foreach (PlayerSession session in PlayersManager.Instance.Sessions)
            {
                if (session != null && session.IsCalibrated == calibrated)
                    return session;
            }

            return null;
        }

        private static string DescribeCalibration()
        {
            if (PlayersManager.Instance == null) return "PlayersManager отсутствует";

            List<string> parts = new List<string>();
            foreach (PlayerSession session in PlayersManager.Instance.Sessions)
            {
                if (session != null)
                    parts.Add($"{session.PlayerName}: откалиброван={session.IsCalibrated}");
            }

            return parts.Count > 0 ? string.Join("; ", parts.ToArray()) : "сессий нет";
        }

        private static bool AllSessionsHaveAvatar()
        {
            if (PlayersManager.Instance == null || PlayersManager.Instance.Sessions.Count == 0)
                return false;

            foreach (PlayerSession session in PlayersManager.Instance.Sessions)
            {
                if (session == null || session.ActiveAvatar == null)
                    return false;
            }

            return true;
        }

        private static string DescribeSessions()
        {
            if (PlayersManager.Instance == null)
                return "PlayersManager отсутствует";

            List<string> parts = new List<string>();
            foreach (PlayerSession session in PlayersManager.Instance.Sessions)
            {
                if (session == null) continue;

                PlayerController avatar = session.ActiveAvatar;
                parts.Add($"{session.PlayerName}: команда={session.TeamIndex}, " +
                          $"откалиброван={session.IsCalibrated}, " +
                          (avatar == null
                              ? "аватара нет"
                              : $"аватар netId={avatar.netId} в {Fmt(avatar.transform.position)}"));
            }

            return parts.Count > 0 ? string.Join("; ", parts.ToArray()) : "сессий нет";
        }

        private static string DescribeFlags()
        {
            if (PlayersManager.Instance == null) return "(нет PlayersManager)";

            List<string> parts = new List<string>();
            foreach (PlayerSession session in PlayersManager.Instance.Sessions)
            {
                if (session != null)
                    parts.Add($"{session.PlayerName}={session.HasGrabbedDogTag}");
            }

            return parts.Count > 0 ? string.Join(", ", parts.ToArray()) : "(нет сессий)";
        }

        private static bool AllClientsRaised()
        {
            if (PlayersManager.Instance == null || PlayersManager.Instance.Sessions.Count == 0)
                return false;

            foreach (PlayerSession session in PlayersManager.Instance.Sessions)
            {
                if (session == null || !session.HasGrabbedDogTag)
                    return false;
            }

            return true;
        }

        private static bool AnyClientFlag()
        {
            if (PlayersManager.Instance == null) return false;

            foreach (PlayerSession session in PlayersManager.Instance.Sessions)
            {
                if (session != null && session.HasGrabbedDogTag)
                    return true;
            }

            return false;
        }

        private static int ReportedFlags()
        {
            if (PlayersManager.Instance == null) return 0;

            int reported = 0;
            foreach (PlayerSession session in PlayersManager.Instance.Sessions)
            {
                if (session != null && session.HasGrabbedDogTag)
                    reported++;
            }

            return reported;
        }

        // ══════════════════════════════════════════════════════════════════
        //  Общее
        // ══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Вторая карта прогона. Пара <c>TestMap1</c> / <c>TestMap2</c> взята намеренно:
        /// это одна и та же арена, повёрнутая на 90°, — то есть та самая ситуация,
        /// в которой мировая позиция врёт, а позиция относительно якорей нет.
        /// Любая другая карта означает перезагрузку самой себя: проверка останется
        /// осмысленной, но слабее.
        /// </summary>
        private static string SecondMapFor(string firstMap)
        {
            if (string.Equals(firstMap, "TestMap1", StringComparison.OrdinalIgnoreCase)) return "TestMap2";
            if (string.Equals(firstMap, "TestMap2", StringComparison.OrdinalIgnoreCase)) return "TestMap1";

            return firstMap;
        }

        /// <summary>
        /// Точка замера — середина между зонами спавна, сдвинутая на
        /// <see cref="ProbeOffset"/> метров в сторону чужой базы.
        ///
        /// <para>
        /// Эталон намеренно вычисляется <b>из зон</b>, а не из якорей: сравнивать место
        /// откалиброванного игрока с тем же кодом, который его и посчитал, значит
        /// проверять код им самим. Зоны же ставит художник, и к калибровке они
        /// отношения не имеют.
        /// </para>
        ///
        /// <para>
        /// Сдвиг обязателен. Первая версия сценария брала сам центр — и это была дыра:
        /// арена на второй карте повёрнута вокруг собственного начала координат, поэтому
        /// центр у карт общий и в мировых координатах тоже, и наивное сохранение мировой
        /// позиции прошло бы проверку. Сдвинутая точка при повороте арены уезжает,
        /// и два ответа расходятся.
        /// </para>
        ///
        /// <para>
        /// Направление сдвига задано командой игрока — «от своей базы к чужой», — а не
        /// порядком объектов на сцене: только так одна и та же точка арены получается
        /// на обеих картах и у сервера, и у клиента.
        /// </para>
        /// </summary>
        private static bool TryArenaProbePoint(TeamData ownTeam, out Vector3 probe, out string diagnosis)
        {
            probe = Vector3.zero;

            TeamSpawnZone[] zones = UnityEngine.Object.FindObjectsByType<TeamSpawnZone>(FindObjectsSortMode.InstanceID);

            if (zones.Length != 2)
            {
                diagnosis = $"на сцене '{SceneManager.GetActiveScene().name}' зон спавна {zones.Length}, " +
                            "а точка замера определена только для пары";
                return false;
            }

            if (ownTeam == null)
            {
                diagnosis = "команда не назначена — направление «от своей базы к чужой» не определено";
                return false;
            }

            TeamSpawnZone own = zones[0].Team == ownTeam
                ? zones[0]
                : zones[1].Team == ownTeam ? zones[1] : null;

            if (own == null)
            {
                diagnosis = $"зоны команды '{ownTeam.displayName}' на сцене нет: " +
                            $"есть '{ZoneTeamName(zones[0])}' и '{ZoneTeamName(zones[1])}'";
                return false;
            }

            TeamSpawnZone other = own == zones[0] ? zones[1] : zones[0];

            Vector3 ownPos   = own.transform.position;
            Vector3 otherPos = other.transform.position;

            float between = Vector3.Distance(ownPos, otherPos);
            if (between < MinSeparation * 2f)
            {
                diagnosis = $"зоны спавна разведены всего на {between:F2} м — точку замера не отличить от самих зон";
                return false;
            }

            Vector3 center  = (ownPos + otherPos) * 0.5f;
            Vector3 toOther = Vector3.Scale(otherPos - ownPos, new Vector3(1f, 0f, 1f)).normalized;

            probe = center + toOther * ProbeOffset;
            diagnosis = string.Empty;
            return true;
        }

        private static string ZoneTeamName(TeamSpawnZone zone)
        {
            return zone != null && zone.Team != null ? zone.Team.displayName : "(без команды)";
        }

        private static float Now => Time.realtimeSinceStartup;

        private static int SessionCount()
        {
            return PlayersManager.Instance != null ? PlayersManager.Instance.Sessions.Count : 0;
        }

        private static string Fmt(Vector3 v)
        {
            return $"({v.x:F2}, {v.y:F2}, {v.z:F2})";
        }

        private static void DisableDebugOrchestrator()
        {
            DebugOrchestrator orchestrator = UnityEngine.Object.FindFirstObjectByType<DebugOrchestrator>();
            if (orchestrator == null || !orchestrator.enabled)
                return;

            orchestrator.enabled = false;
            GameLog.Debug.Info("[E2E] DebugOrchestrator отключён: дирижёром прогона выступает сценарий");
        }
    }
}
#endif
